using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

public class LiveOrderBookSampler : MonoBehaviour
{
    public string symbol = "ETHUSDT";
    public int sampleMs = 100;
    public int levels = 20;
    public int snapshotLimit = 5000;
    public int wsSpeedMs = 100;
    public string outputSubfolder = "StreamingAssets";
    public string outputFileName = "ethusdt_live.jsonl";
    public float flushIntervalSeconds = 2.5f;
    public string restBaseUrl = "https://api.binance.com";
    public string wsBaseUrl = "wss://stream.binance.com:9443/ws";

    private Dictionary<string, string> bookBids = new Dictionary<string, string>();
    private Dictionary<string, string> bookAsks = new Dictionary<string, string>();
    private long lastUpdateId;

    private readonly Queue<string> rawEventQueue = new Queue<string>();
    private readonly object queueLock = new object();

    private ClientWebSocket ws;
    private CancellationTokenSource cts;

    private bool synced;
    private float nextSampleTime;
    private float lastFlushTime;
    private int sampleCount;
    private string outputPath;

    private List<string> unflushedLines = new List<string>();
    private StreamWriter outputWriter;
    private StringBuilder jsonBuilder = new StringBuilder(4096);

    private List<KeyValuePair<float, string>> bidSortBuffer = new List<KeyValuePair<float, string>>();
    private List<KeyValuePair<float, string>> askSortBuffer = new List<KeyValuePair<float, string>>();

    private string cachedSymbolUpper;

    private string ResolveOutputDirectory()
    {
        return Path.Combine(Application.dataPath, outputSubfolder);
    }

    private void Start()
    {
        cachedSymbolUpper = symbol.ToUpper();
        StartCoroutine(RunSampler());
    }

    private void OnDestroy()
    {
        cts?.Cancel();
        cts?.Dispose();
        FlushToDisk();
        outputWriter?.Close();
        outputWriter = null;
        Debug.Log($"[Sampler] Stopped. Wrote {sampleCount} total samples to {outputPath}");
    }

    private void Awake()
    {
        if (FindObjectsByType<LiveOrderBookSampler>(FindObjectsSortMode.None).Length > 1)
        {
            Destroy(gameObject);
            return;
        }
        DontDestroyOnLoad(gameObject);
    }

    private IEnumerator RunSampler()
    {
        string outputDir = ResolveOutputDirectory();
        outputPath = Path.Combine(outputDir, outputFileName);

        Debug.Log($"[Sampler] Output path: {outputPath}");

        if (!Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        outputWriter = new StreamWriter(outputPath, false, Encoding.UTF8);
        outputWriter.AutoFlush = false;

        cts = new CancellationTokenSource();
        Thread wsThread = new Thread(() => RunWebSocket(cts.Token));
        wsThread.IsBackground = true;
        wsThread.Start();

        yield return new WaitForSeconds(1f);
        yield return SyncOrderBook();

        if (!synced)
        {
            Debug.LogError("[Sampler] Failed to sync order book.");
            yield break;
        }

        nextSampleTime = Time.realtimeSinceStartup;
        lastFlushTime = Time.realtimeSinceStartup;
        float sampleIntervalSec = sampleMs / 1000f;

        Debug.Log($"[Sampler] Collecting {cachedSymbolUpper} indefinitely, sampling every {sampleMs}ms, flushing every {flushIntervalSeconds}s");

        while (true)
        {
            DrainAndApplyEvents();

            float now = Time.realtimeSinceStartup;
            if (now >= nextSampleTime)
            {
                CollectSample();
                sampleCount++;
                nextSampleTime += sampleIntervalSec;

                if (nextSampleTime < now)
                    nextSampleTime = now + sampleIntervalSec;
            }

            if (now - lastFlushTime >= flushIntervalSeconds)
            {
                FlushToDisk();
                lastFlushTime = now;
            }

            yield return null;
        }
    }

    private void FlushToDisk()
    {
        if (unflushedLines.Count == 0 || outputWriter == null) return;

        try
        {
            for (int i = 0; i < unflushedLines.Count; i++)
                outputWriter.WriteLine(unflushedLines[i]);

            outputWriter.Flush();
            unflushedLines.Clear();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Sampler] Flush failed: {ex.Message}");
        }
    }

    private void CollectSample()
    {
        bidSortBuffer.Clear();
        foreach (var kv in bookBids)
        {
            float price;
            if (float.TryParse(kv.Key, NumberStyles.Float, CultureInfo.InvariantCulture, out price))
                bidSortBuffer.Add(new KeyValuePair<float, string>(price, kv.Key));
        }
        bidSortBuffer.Sort((a, b) => b.Key.CompareTo(a.Key));

        askSortBuffer.Clear();
        foreach (var kv in bookAsks)
        {
            float price;
            if (float.TryParse(kv.Key, NumberStyles.Float, CultureInfo.InvariantCulture, out price))
                askSortBuffer.Add(new KeyValuePair<float, string>(price, kv.Key));
        }
        askSortBuffer.Sort((a, b) => a.Key.CompareTo(b.Key));

        int bidCount = levels > 0 ? Math.Min(levels, bidSortBuffer.Count) : bidSortBuffer.Count;
        int askCount = levels > 0 ? Math.Min(levels, askSortBuffer.Count) : askSortBuffer.Count;

        long capturedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        jsonBuilder.Clear();
        jsonBuilder.Append("{\"symbol\":\"");
        jsonBuilder.Append(cachedSymbolUpper);
        jsonBuilder.Append("\",\"captured_at_ms\":");
        jsonBuilder.Append(capturedAtMs);
        jsonBuilder.Append(",\"last_update_id\":");
        jsonBuilder.Append(lastUpdateId);

        if (bidCount > 0)
        {
            string bestBidPrice = bidSortBuffer[0].Value;
            jsonBuilder.Append(",\"best_bid\":[\"");
            jsonBuilder.Append(bestBidPrice);
            jsonBuilder.Append("\",\"");
            jsonBuilder.Append(bookBids[bestBidPrice]);
            jsonBuilder.Append("\"]");
        }
        else
        {
            jsonBuilder.Append(",\"best_bid\":null");
        }

        if (askCount > 0)
        {
            string bestAskPrice = askSortBuffer[0].Value;
            jsonBuilder.Append(",\"best_ask\":[\"");
            jsonBuilder.Append(bestAskPrice);
            jsonBuilder.Append("\",\"");
            jsonBuilder.Append(bookAsks[bestAskPrice]);
            jsonBuilder.Append("\"]");
        }
        else
        {
            jsonBuilder.Append(",\"best_ask\":null");
        }

        jsonBuilder.Append(",\"bids\":[");
        for (int i = 0; i < bidCount; i++)
        {
            if (i > 0) jsonBuilder.Append(",");
            string priceKey = bidSortBuffer[i].Value;
            jsonBuilder.Append("[\"");
            jsonBuilder.Append(priceKey);
            jsonBuilder.Append("\",\"");
            jsonBuilder.Append(bookBids[priceKey]);
            jsonBuilder.Append("\"]");
        }
        jsonBuilder.Append("]");

        jsonBuilder.Append(",\"asks\":[");
        for (int i = 0; i < askCount; i++)
        {
            if (i > 0) jsonBuilder.Append(",");
            string priceKey = askSortBuffer[i].Value;
            jsonBuilder.Append("[\"");
            jsonBuilder.Append(priceKey);
            jsonBuilder.Append("\",\"");
            jsonBuilder.Append(bookAsks[priceKey]);
            jsonBuilder.Append("\"]");
        }
        jsonBuilder.Append("]}");

        unflushedLines.Add(jsonBuilder.ToString());
    }

    private IEnumerator SyncOrderBook()
    {
        List<string> buffer = new List<string>();
        long firstU = -1;

        float waitStart = Time.realtimeSinceStartup;
        while (firstU < 0 && Time.realtimeSinceStartup - waitStart < 10f)
        {
            lock (queueLock)
            {
                while (rawEventQueue.Count > 0)
                {
                    string raw = rawEventQueue.Dequeue();
                    var ev = ParseEvent(raw);
                    if (ev == null) continue;
                    buffer.Add(raw);
                    if (firstU < 0)
                        firstU = ev.firstUpdateId;
                }
            }
            yield return null;
        }

        if (firstU < 0)
        {
            Debug.LogError("[Sampler] No depth events received from WebSocket");
            yield break;
        }

        Dictionary<string, object> snapshot = null;
        bool snapshotValid = false;

        while (!snapshotValid)
        {
            yield return FetchSnapshot((result) => { snapshot = result; });

            if (snapshot == null)
            {
                Debug.LogError("[Sampler] Failed to fetch REST snapshot");
                yield break;
            }

            long snapUpdateId = Convert.ToInt64(snapshot["lastUpdateId"]);
            if (snapUpdateId >= firstU)
                snapshotValid = true;
            else
                yield return new WaitForSeconds(0.5f);
        }

        LoadSnapshot(snapshot);

        List<string> aligned = new List<string>();
        for (int i = 0; i < buffer.Count; i++)
        {
            var ev = ParseEvent(buffer[i]);
            if (ev != null && ev.finalUpdateId > lastUpdateId)
                aligned.Add(buffer[i]);
        }

        if (aligned.Count > 0)
        {
            var firstEvent = ParseEvent(aligned[0]);
            if (!(firstEvent.firstUpdateId <= lastUpdateId + 1 && lastUpdateId + 1 <= firstEvent.finalUpdateId))
            {
                Debug.LogError("[Sampler] Could not align buffered events with REST snapshot");
                yield break;
            }

            for (int i = 0; i < aligned.Count; i++)
            {
                var ev = ParseEvent(aligned[i]);
                if (ev != null) ApplyEvent(ev);
            }
        }

        synced = true;
        Debug.Log($"[Sampler] Order book synced. lastUpdateId={lastUpdateId}, bids={bookBids.Count}, asks={bookAsks.Count}");
    }

    private void LoadSnapshot(Dictionary<string, object> snapshot)
    {
        lastUpdateId = Convert.ToInt64(snapshot["lastUpdateId"]);
        bookBids.Clear();
        bookAsks.Clear();

        var bidsList = snapshot["bids"] as List<List<string>>;
        if (bidsList != null)
        {
            for (int i = 0; i < bidsList.Count; i++)
            {
                if (bidsList[i][1] != "0" && bidsList[i][1] != "0.00000000")
                    bookBids[bidsList[i][0]] = bidsList[i][1];
            }
        }

        var asksList = snapshot["asks"] as List<List<string>>;
        if (asksList != null)
        {
            for (int i = 0; i < asksList.Count; i++)
            {
                if (asksList[i][1] != "0" && asksList[i][1] != "0.00000000")
                    bookAsks[asksList[i][0]] = asksList[i][1];
            }
        }
    }

    private void ApplyEvent(DepthEvent ev)
    {
        if (ev.finalUpdateId < lastUpdateId) return;

        if (ev.firstUpdateId > lastUpdateId + 1)
        {
            Debug.LogWarning($"[Sampler] Gap detected: U={ev.firstUpdateId}, local={lastUpdateId}");
            return;
        }

        for (int i = 0; i < ev.bidCount; i++)
        {
            if (ev.bids[i].quantity == "0" || ev.bids[i].quantity == "0.00000000")
                bookBids.Remove(ev.bids[i].price);
            else
                bookBids[ev.bids[i].price] = ev.bids[i].quantity;
        }

        for (int i = 0; i < ev.askCount; i++)
        {
            if (ev.asks[i].quantity == "0" || ev.asks[i].quantity == "0.00000000")
                bookAsks.Remove(ev.asks[i].price);
            else
                bookAsks[ev.asks[i].price] = ev.asks[i].quantity;
        }

        lastUpdateId = ev.finalUpdateId;
    }

    private void DrainAndApplyEvents()
    {
        lock (queueLock)
        {
            while (rawEventQueue.Count > 0)
            {
                string raw = rawEventQueue.Dequeue();
                var ev = ParseEvent(raw);
                if (ev != null) ApplyEvent(ev);
            }
        }
    }

    private IEnumerator FetchSnapshot(Action<Dictionary<string, object>> callback)
    {
        string url = $"{restBaseUrl}/api/v3/depth?symbol={cachedSymbolUpper}&limit={snapshotLimit}";

        using (var req = UnityWebRequest.Get(url))
        {
            req.timeout = 10;
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[Sampler] REST snapshot failed: {req.error}");
                callback(null);
                yield break;
            }

            callback(ParseSnapshotJson(req.downloadHandler.text));
        }
    }

    private async void RunWebSocket(CancellationToken ct)
    {
        string streamSymbol = symbol.ToLower();
        string suffix = wsSpeedMs == 1000 ? "@depth" : $"@depth@{wsSpeedMs}ms";
        string wsUrl = $"{wsBaseUrl}/{streamSymbol}{suffix}";

        try
        {
            ws = new ClientWebSocket();
            await ws.ConnectAsync(new Uri(wsUrl), ct);

            byte[] buffer = new byte[16384];

            while (!ct.IsCancellationRequested &&
                   ws.State == System.Net.WebSockets.WebSocketState.Open)
            {
                var sb = new StringBuilder();
                System.Net.WebSockets.WebSocketReceiveResult result;

                do
                {
                    result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                }
                while (!result.EndOfMessage);

                lock (queueLock)
                    rawEventQueue.Enqueue(sb.ToString());
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.LogError($"[Sampler] WebSocket error: {ex.Message}");
        }
    }

    private class DepthEvent
    {
        public long firstUpdateId;
        public long finalUpdateId;
        public OrderEntry[] bids = new OrderEntry[64];
        public OrderEntry[] asks = new OrderEntry[64];
        public int bidCount;
        public int askCount;
    }

    private struct OrderEntry
    {
        public string price;
        public string quantity;
    }

    private DepthEvent ParseEvent(string raw)
    {
        string json = raw;

        int dataIdx = json.IndexOf("\"data\"", StringComparison.Ordinal);
        if (dataIdx >= 0)
        {
            int braceStart = json.IndexOf('{', dataIdx);
            if (braceStart >= 0)
            {
                int depth = 0;
                int braceEnd = -1;
                for (int i = braceStart; i < json.Length; i++)
                {
                    if (json[i] == '{') depth++;
                    else if (json[i] == '}') { depth--; if (depth == 0) { braceEnd = i; break; } }
                }
                if (braceEnd >= 0)
                    json = json.Substring(braceStart, braceEnd - braceStart + 1);
            }
        }

        string eventType = ExtractString(json, "e");
        if (eventType != "depthUpdate") return null;

        var ev = new DepthEvent();
        ev.firstUpdateId = ExtractLong(json, "U");
        ev.finalUpdateId = ExtractLong(json, "u");
        ExtractOrderArrayInto(json, "b", ev.bids, out ev.bidCount);
        ExtractOrderArrayInto(json, "a", ev.asks, out ev.askCount);
        return ev;
    }

    private Dictionary<string, object> ParseSnapshotJson(string json)
    {
        var result = new Dictionary<string, object>();
        result["lastUpdateId"] = ExtractLong(json, "lastUpdateId");
        result["bids"] = ExtractStringPairArray(json, "bids");
        result["asks"] = ExtractStringPairArray(json, "asks");
        return result;
    }

    private static string ExtractString(string json, string field)
    {
        string key = $"\"{field}\"";
        int idx = json.IndexOf(key, StringComparison.Ordinal);
        if (idx < 0) return null;
        int colon = json.IndexOf(':', idx + key.Length);
        if (colon < 0) return null;
        int quoteStart = json.IndexOf('"', colon + 1);
        if (quoteStart < 0) return null;
        int quoteEnd = json.IndexOf('"', quoteStart + 1);
        if (quoteEnd < 0) return null;
        return json.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
    }

    private static long ExtractLong(string json, string field)
    {
        string key = $"\"{field}\"";
        int idx = json.IndexOf(key, StringComparison.Ordinal);
        if (idx < 0) return 0;
        int colon = json.IndexOf(':', idx + key.Length);
        int end = json.IndexOfAny(new[] { ',', '}', ']' }, colon + 1);
        string val = json.Substring(colon + 1, end - colon - 1).Trim().Trim('"');
        long.TryParse(val, out long result);
        return result;
    }

    private static void ExtractOrderArrayInto(string json, string field, OrderEntry[] output, out int count)
    {
        count = 0;
        string key = $"\"{field}\"";
        int keyIdx = json.IndexOf(key, StringComparison.Ordinal);
        if (keyIdx < 0) return;

        int arrStart = json.IndexOf('[', keyIdx + key.Length);
        if (arrStart < 0) return;

        int depth = 0, arrEnd = -1;
        for (int i = arrStart; i < json.Length; i++)
        {
            if (json[i] == '[') depth++;
            else if (json[i] == ']') { depth--; if (depth == 0) { arrEnd = i; break; } }
        }
        if (arrEnd < 0) return;

        int cursor = arrStart + 1;
        while (cursor < arrEnd && count < output.Length)
        {
            int innerStart = json.IndexOf('[', cursor);
            if (innerStart < 0 || innerStart >= arrEnd) break;
            int innerEnd = json.IndexOf(']', innerStart);
            if (innerEnd < 0) break;

            int commaIdx = json.IndexOf(',', innerStart + 1);
            if (commaIdx > 0 && commaIdx < innerEnd)
            {
                output[count].price = json.Substring(innerStart + 2, commaIdx - innerStart - 3);
                output[count].quantity = json.Substring(commaIdx + 2, innerEnd - commaIdx - 3);
                count++;
            }
            cursor = innerEnd + 1;
        }
    }

    private static List<List<string>> ExtractStringPairArray(string json, string field)
    {
        var list = new List<List<string>>();
        string key = $"\"{field}\"";
        int keyIdx = json.IndexOf(key, StringComparison.Ordinal);
        if (keyIdx < 0) return list;

        int arrStart = json.IndexOf('[', keyIdx + key.Length);
        if (arrStart < 0) return list;

        int depth = 0, arrEnd = -1;
        for (int i = arrStart; i < json.Length; i++)
        {
            if (json[i] == '[') depth++;
            else if (json[i] == ']') { depth--; if (depth == 0) { arrEnd = i; break; } }
        }
        if (arrEnd < 0) return list;

        int cursor = arrStart + 1;
        while (cursor < arrEnd)
        {
            int innerStart = json.IndexOf('[', cursor);
            if (innerStart < 0 || innerStart >= arrEnd) break;
            int innerEnd = json.IndexOf(']', innerStart);
            if (innerEnd < 0) break;

            string inner = json.Substring(innerStart + 1, innerEnd - innerStart - 1);
            string[] parts = inner.Split(',');
            if (parts.Length >= 2)
            {
                list.Add(new List<string>
                {
                    parts[0].Trim().Trim('"'),
                    parts[1].Trim().Trim('"')
                });
            }
            cursor = innerEnd + 1;
        }
        return list;
    }
}

#if !NETSTANDARD2_1 && !NET_4_6
namespace System.Net.WebSockets
{
    public enum WebSocketState { Open, Closed }
    public class WebSocketReceiveResult
    {
        public int Count;
        public bool EndOfMessage;
    }
    public class ClientWebSocket : IDisposable
    {
        public WebSocketState State => WebSocketState.Open;
        public System.Threading.Tasks.Task ConnectAsync(Uri uri, CancellationToken ct)
            => throw new NotImplementedException("Install a Unity WebSocket package");
        public System.Threading.Tasks.Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buf, CancellationToken ct)
            => throw new NotImplementedException("Install a Unity WebSocket package");
        public void Dispose() { }
    }
}
#endif