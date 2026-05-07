using System;
using System.Collections.Generic;
using UnityEngine;

public class LiveOrderBookSource : OrderBookSourceBehaviour
{    public override int Count
    {
        get
        {
            var sampler = LiveOrderBookSampler.instance;
            if (sampler == null)
            {
               //Debug.LogWarning("[LiveOrderBookSource] Sampler instance is null");
                return 0;
            }
            if (!sampler.IsSynced)
            {
               //Debug.Log("[LiveOrderBookSource] Sampler not yet synced");
                return 0;
            }
           //Debug.Log($"[LiveOrderBookSource] Snapshot count: {sampler.SnapshotCount}");
            return sampler.SnapshotCount;
        }
    }

    public override bool IsLive
    {
        get { return true; }
    }

    public override void RefreshSource()
    {
    }

    public override bool TryGetRange(int startInclusive, int count, List<OrderBookSnapshot> results)
    {
        var sampler = LiveOrderBookSampler.instance;
        if (sampler == null)
        {
            results.Clear();
            return false;
        }
        return sampler.TryGetRange(startInclusive, count, results);
    }

    public override int FindNearestIndexByTime(long capturedAtMs)
    {
        var sampler = LiveOrderBookSampler.instance;
        if (sampler == null)
            return -1;
        return sampler.FindNearestIndexByTime(capturedAtMs);
    }
}