using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class OrderBookWindowController : MonoBehaviour
{
    [Header("References")]
    public OrderBookSourceBehaviour dataSource;
    public OrderBookSourceBehaviour liveSource;
    public OrderBookHistoryRenderer renderer;

    [Header("Window")]
    [Min(1)] public int startIndex = 0;
    [Min(1)] public int windowSize = 100;

    [Header("Startup")]
    public bool refreshSourceOnStart = true;
    public bool loadWindowOnStart = true;
    
    [Header("Scrolling Properties")]
    public float secondsPerTick = 0.05f;
    public int stepSize = 1;
    private float timeDelta = 0f; 

    private readonly List<OrderBookSnapshot> _buffer = new List<OrderBookSnapshot>();

    private OrderBookSourceBehaviour source;

    private const string UseLiveDataPrefsKey = "OrderBookUseLiveData";
    private void Start()
    {
        if (PlayerPrefs.GetInt(UseLiveDataPrefsKey) == 1)
        {
            source = liveSource;
            Debug.Log("Using Live Data");
        }
        else
        {
            source = dataSource;
            Debug.Log("Using Sourced Data");
        }

        if (source == null || renderer == null)
            return;

        if (refreshSourceOnStart)
            source.RefreshSource();

        if (loadWindowOnStart)
            RefreshWindow();
    }

    private void Update()
    {
        if (!Application.isPlaying)
            return;

        if (source == null || renderer == null)
            return;
        if (IsMeshReady() == false)
        {
            return;
        }

        timeDelta += Time.deltaTime;

        if (timeDelta >= secondsPerTick)
        {
            timeDelta -= secondsPerTick;
            ScrollBy(stepSize);
        }
    }

    public bool IsMeshReady()
    {
        return (startIndex + windowSize < source.Count);
    }

    [ContextMenu("Refresh Window")]
    public void RefreshWindow()
    {
        if (source == null || renderer == null)
            return;

        if (source.Count <= 0)
        {
            renderer.ClearWindow();
            return;
        }

        startIndex = Mathf.Clamp(startIndex, 0, Mathf.Max(0, source.Count - 1));

        _buffer.Clear();
        bool gotAny = source.TryGetRange(startIndex, windowSize, _buffer);

        if (!gotAny || _buffer.Count == 0)
            renderer.ClearWindow();
        else
            renderer.SetWindow(_buffer);
    }

    public void ScrollBy(int delta)
    {
        if (source == null)
            return;

        int maxStart = Mathf.Max(0, source.Count - 1);
        startIndex = Mathf.Clamp(startIndex + delta, 0, maxStart);
        RefreshWindow();
    }

    public void JumpToIndex(int index)
    {
        if (source == null)
            return;

        int maxStart = Mathf.Max(0, source.Count - 1);
        startIndex = Mathf.Clamp(index, 0, maxStart);
        RefreshWindow();
    }

    public void JumpToTime(long capturedAtMs)
    {
        if (source == null)
            return;

        int index = source.FindNearestIndexByTime(capturedAtMs);
        if (index < 0)
            return;

        startIndex = index;
        RefreshWindow();
    }

    [ContextMenu("Jump To Start")]
    public void JumpToStart()
    {
        JumpToIndex(0);
    }

    [ContextMenu("Jump To End")]
    public void JumpToEnd()
    {
        if (source == null)
            return;

        JumpToIndex(Mathf.Max(0, source.Count - windowSize));
    }
}
