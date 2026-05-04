#!/usr/bin/env python3
"""
Collect sampled Binance Spot order books to JSONL.

Dependencies:
  pip install requests websockets

Example:
  python binance_orderbook_sampler.py \
      --symbol BTCUSDT \
      --duration-seconds 600 \
      --sample-ms 500 \
      --levels 20 \
      --snapshot-limit 5000 \
      --ws-speed-ms 100 \
      --output btcusdt_orderbooks.jsonl
"""

import argparse
import asyncio
import json
import time
from dataclasses import dataclass, field
from typing import Dict, List

import requests
import websockets

REST_BASE = "https://data-api.binance.vision"
WS_BASE = "wss://data-stream.binance.vision/ws"


@dataclass
class LocalOrderBook:
    symbol: str
    snapshot_limit: int = 5000
    bids: Dict[str, str] = field(default_factory=dict)
    asks: Dict[str, str] = field(default_factory=dict)
    last_update_id: int = 0

    def load_snapshot(self, snapshot: dict) -> None:
        self.last_update_id = int(snapshot["lastUpdateId"])
        self.bids = {price: qty for price, qty in snapshot["bids"] if qty not in ("0", "0.00000000")}
        self.asks = {price: qty for price, qty in snapshot["asks"] if qty not in ("0", "0.00000000")}

    def apply_event(self, event: dict) -> None:
        event_u = int(event["u"])
        event_U = int(event["U"])

        if event_u < self.last_update_id:
            return

        if event_U > self.last_update_id + 1:
            raise RuntimeError(
                f"Gap detected: event U={event_U} is ahead of local book last_update_id={self.last_update_id}"
            )

        for price, qty in event.get("b", []):
            if qty in ("0", "0.00000000"):
                self.bids.pop(price, None)
            else:
                self.bids[price] = qty

        for price, qty in event.get("a", []):
            if qty in ("0", "0.00000000"):
                self.asks.pop(price, None)
            else:
                self.asks[price] = qty

        self.last_update_id = event_u

    def sampled_snapshot(self, levels_per_side: int) -> dict:
        bids = sorted(self.bids.items(), key=lambda x: float(x[0]), reverse=True)
        asks = sorted(self.asks.items(), key=lambda x: float(x[0]))

        if levels_per_side > 0:
            bids = bids[:levels_per_side]
            asks = asks[:levels_per_side]

        best_bid = list(bids[0]) if bids else None
        best_ask = list(asks[0]) if asks else None

        return {
            "symbol": self.symbol,
            "captured_at_ms": int(time.time() * 1000),
            "last_update_id": self.last_update_id,
            "best_bid": best_bid,
            "best_ask": best_ask,
            "bids": [list(x) for x in bids],
            "asks": [list(x) for x in asks],
        }


def fetch_depth_snapshot(symbol: str, limit: int) -> dict:
    response = requests.get(
        f"{REST_BASE}/api/v3/depth",
        params={"symbol": symbol, "limit": limit},
        timeout=10,
    )
    response.raise_for_status()
    return response.json()


def normalize_ws_event(raw_message: str) -> dict:
    obj = json.loads(raw_message)
    if "stream" in obj and "data" in obj:
        return obj["data"]
    return obj


async def fetch_depth_snapshot_async(symbol: str, limit: int) -> dict:
    return await asyncio.to_thread(fetch_depth_snapshot, symbol, limit)


async def sync_order_book(ws, book: LocalOrderBook) -> None:
    """
    Implements Binance's documented sync pattern:
      1) buffer depth events
      2) fetch REST snapshot
      3) discard stale buffered events
      4) apply aligned events
    """
    buffer: List[dict] = []
    first_U = None

    while first_U is None:
        raw = await ws.recv()
        event = normalize_ws_event(raw)
        if event.get("e") != "depthUpdate":
            continue
        buffer.append(event)
        first_U = int(event["U"])

    while True:
        snapshot = await fetch_depth_snapshot_async(book.symbol, book.snapshot_limit)
        if int(snapshot["lastUpdateId"]) >= first_U:
            break

    book.load_snapshot(snapshot)
    buffer = [e for e in buffer if int(e["u"]) > book.last_update_id]

    if not buffer:
        return

    first_event = buffer[0]
    if not (int(first_event["U"]) <= book.last_update_id + 1 <= int(first_event["u"])):
        raise RuntimeError("Could not align buffered events with REST snapshot. Retry the process.")

    for event in buffer:
        book.apply_event(event)


async def collect(args) -> None:
    symbol = args.symbol.upper()
    stream_symbol = symbol.lower()

    suffix = "@depth" if args.ws_speed_ms == 1000 else f"@depth@{args.ws_speed_ms}ms"
    ws_url = f"{WS_BASE}/{stream_symbol}{suffix}"

    book = LocalOrderBook(symbol=symbol, snapshot_limit=args.snapshot_limit)

    end_time = time.monotonic() + args.duration_seconds
    next_sample = time.monotonic()

    with open(args.output, "w", encoding="utf-8") as fout:
        async with websockets.connect(
            ws_url,
            max_queue=None,
            ping_interval=20,
            ping_timeout=60,
        ) as ws:
            await sync_order_book(ws, book)

            while time.monotonic() < end_time:
                now = time.monotonic()
                timeout = min(max(next_sample - now, 0.0), 1.0)

                try:
                    raw = await asyncio.wait_for(ws.recv(), timeout=timeout)
                    event = normalize_ws_event(raw)
                    if event.get("e") != "depthUpdate":
                        continue
                    try:
                        book.apply_event(event)
                    except RuntimeError:
                        print("Gap detected, resyncing local book...")
                        await sync_order_book(ws, book)
                except asyncio.TimeoutError:
                    pass

                now = time.monotonic()
                while now >= next_sample and now < end_time + args.sample_ms / 1000.0:
                    sample = book.sampled_snapshot(levels_per_side=args.levels)
                    fout.write(json.dumps(sample, separators=(",", ":")) + "\n")
                    fout.flush()
                    next_sample += args.sample_ms / 1000.0
                    now = time.monotonic()

    print(f"Done. Wrote samples to {args.output}")


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Collect sampled Binance Spot order books to JSONL."
    )
    parser.add_argument("--symbol", default="BTCUSDT", help="Spot symbol, e.g. BTCUSDT")
    parser.add_argument(
        "--duration-seconds",
        type=int,
        default=60,
        help="How long to collect data for",
    )
    parser.add_argument(
        "--sample-ms",
        type=int,
        default=1000,
        help="Sampling interval in milliseconds",
    )
    parser.add_argument(
        "--levels",
        type=int,
        default=20,
        help="How many levels per side to save in each sample; use 0 for the full reconstructed book",
    )
    parser.add_argument(
        "--snapshot-limit",
        type=int,
        default=5000,
        choices=[100, 500, 1000, 5000],
        help="REST depth snapshot size used for initial sync",
    )
    parser.add_argument(
        "--ws-speed-ms",
        type=int,
        default=100,
        choices=[100, 1000],
        help="Diff depth stream speed in milliseconds",
    )
    parser.add_argument(
        "--output",
        default="orderbooks.jsonl",
        help="Output JSONL file",
    )
    return parser


def main() -> None:
    parser = build_parser()
    args = parser.parse_args()

    if args.duration_seconds <= 0:
        raise SystemExit("--duration-seconds must be > 0")
    if args.sample_ms <= 0:
        raise SystemExit("--sample-ms must be > 0")

    asyncio.run(collect(args))


if __name__ == "__main__":
    main()