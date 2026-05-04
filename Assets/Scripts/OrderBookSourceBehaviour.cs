using System.Collections.Generic;
using UnityEngine;

public abstract class OrderBookSourceBehaviour : MonoBehaviour
{
    public abstract int Count { get; }
    public abstract bool IsLive { get; }

    public abstract void RefreshSource();
    public abstract bool TryGetRange(int startInclusive, int count, List<OrderBookSnapshot> results);
    public abstract int FindNearestIndexByTime(long capturedAtMs);
}
