using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

public class JsonlOrderBookFileSource : OrderBookSourceBehaviour
{
    public enum PathMode
    {
        AbsolutePath,
        StreamingAssetsRelativePath
    }

    [Header("History File")]
    public PathMode pathMode = PathMode.StreamingAssetsRelativePath;

    [Tooltip("Absolute path, or a path relative to StreamingAssets depending on Path Mode.")]
    public string historyPath = "orderbooks.jsonl";

    [Tooltip("Build the line-offset index on enable.")]
    public bool buildIndexOnEnable = true;

    [Tooltip("Log the resolved path when indexing.")]
    public bool logResolvedPath = false;

    [Header("Refresh Properties")]
    
    public float sourceRefreshTime = 2.5f;
    
    private float timeDelta = 0.0f;

    private readonly List<long> _lineOffsets = new List<long>();
    private string _resolvedPath = string.Empty;

    public override int Count
    {
        get { return _lineOffsets.Count; }
    }

    public override bool IsLive
    {
        get { return false; }
    }

    public string ResolvedPath
    {
        get { return _resolvedPath; }
    }

    private void OnEnable()
    {
        if (buildIndexOnEnable)
            RefreshSource();
    }

    private void Update()
    {
        timeDelta += Time.deltaTime;
        if (timeDelta >= sourceRefreshTime)
        {
            timeDelta -= sourceRefreshTime;
            RefreshSource();
        }
    }

    [ContextMenu("Refresh Source")]
    public override void RefreshSource()
    {
        _resolvedPath = ResolvePath();
        _lineOffsets.Clear();

        if (string.IsNullOrWhiteSpace(_resolvedPath))
        {
            Debug.LogWarning("JsonlOrderBookFileSource: no file path configured.");
            return;
        }

        if (logResolvedPath)
            Debug.Log("JsonlOrderBookFileSource resolved path: " + _resolvedPath);

        if (!File.Exists(_resolvedPath))
        {
            Debug.LogWarning("JsonlOrderBookFileSource: file not found: " + _resolvedPath);
            return;
        }

        BuildLineOffsetIndex(_resolvedPath);
        Debug.Log("JsonlOrderBookFileSource indexed " + _lineOffsets.Count + " snapshots.");
    }

    public override bool TryGetRange(int startInclusive, int count, List<OrderBookSnapshot> results)
    {
        if (results == null)
            throw new ArgumentNullException(nameof(results));

        results.Clear();

        if (_lineOffsets.Count == 0 || count <= 0)
            return false;

        int start = Mathf.Clamp(startInclusive, 0, _lineOffsets.Count - 1);
        int remaining = Mathf.Min(count, _lineOffsets.Count - start);

        try
        {
            using (FileStream fs = OpenReadStream())
            {
                fs.Seek(_lineOffsets[start], SeekOrigin.Begin);

                using (StreamReader reader = new StreamReader(fs, Encoding.UTF8, true, 4096, false))
                {
                    while (results.Count < remaining)
                    {
                        string line = reader.ReadLine();
                        if (line == null)
                            break;

                        OrderBookSnapshot snapshot;
                        if (JsonlOrderBookParser.TryParseLine(line, out snapshot))
                            results.Add(snapshot);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("JsonlOrderBookFileSource.TryGetRange failed: " + ex.Message);
            results.Clear();
            return false;
        }

        return results.Count > 0;
    }

    public override int FindNearestIndexByTime(long capturedAtMs)
    {
        if (_lineOffsets.Count == 0)
            return -1;

        int lo = 0;
        int hi = _lineOffsets.Count - 1;

        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) / 2);

            long midTime;
            if (!TryReadTimestampAt(mid, out midTime))
                return Mathf.Clamp(mid, 0, _lineOffsets.Count - 1);

            if (midTime < capturedAtMs)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        if (lo <= 0)
            return 0;
        if (lo >= _lineOffsets.Count)
            return _lineOffsets.Count - 1;

        long beforeTime;
        long afterTime;

        if (!TryReadTimestampAt(lo - 1, out beforeTime))
            return lo - 1;
        if (!TryReadTimestampAt(lo, out afterTime))
            return lo;

        long beforeDelta = Math.Abs(beforeTime - capturedAtMs);
        long afterDelta = Math.Abs(afterTime - capturedAtMs);

        return beforeDelta <= afterDelta ? lo - 1 : lo;
    }

    private string ResolvePath()
    {
        if (pathMode == PathMode.AbsolutePath)
            return historyPath;

        return Path.Combine(Application.streamingAssetsPath, historyPath);
    }

    private FileStream OpenReadStream()
    {
        return new FileStream(_resolvedPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    }

    private void BuildLineOffsetIndex(string path)
    {
        using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            byte[] chunk = new byte[64 * 1024];
            MemoryStream lineBuffer = new MemoryStream(2048);

            long lineStartOffset = 0;
            long absoluteOffset = 0;

            int bytesRead;
            while ((bytesRead = fs.Read(chunk, 0, chunk.Length)) > 0)
            {
                for (int i = 0; i < bytesRead; i++)
                {
                    byte b = chunk[i];

                    if (b == (byte)'\n')
                    {
                        TryAddLineOffset(lineStartOffset, lineBuffer);
                        lineBuffer.SetLength(0);
                        lineStartOffset = absoluteOffset + 1;
                    }
                    else
                    {
                        lineBuffer.WriteByte(b);
                    }

                    absoluteOffset++;
                }
            }

            if (lineBuffer.Length > 0)
                TryAddLineOffset(lineStartOffset, lineBuffer);
        }
    }

    private void TryAddLineOffset(long lineStartOffset, MemoryStream lineBuffer)
    {
        if (lineBuffer.Length <= 0)
            return;

        int len = (int)lineBuffer.Length;
        byte[] bytes = lineBuffer.GetBuffer();

        while (len > 0 && bytes[len - 1] == (byte)'\r')
            len--;

        if (len <= 0)
            return;

        string line = Encoding.UTF8.GetString(bytes, 0, len);
        if (!string.IsNullOrWhiteSpace(line))
            _lineOffsets.Add(lineStartOffset);
    }

    private bool TryReadTimestampAt(int index, out long capturedAtMs)
    {
        capturedAtMs = 0;

        if (index < 0 || index >= _lineOffsets.Count)
            return false;

        try
        {
            using (FileStream fs = OpenReadStream())
            {
                fs.Seek(_lineOffsets[index], SeekOrigin.Begin);

                using (StreamReader reader = new StreamReader(fs, Encoding.UTF8, true, 4096, false))
                {
                    string line = reader.ReadLine();
                    if (line == null)
                        return false;

                    OrderBookSnapshot snapshot;
                    if (!JsonlOrderBookParser.TryParseLine(line, out snapshot))
                        return false;

                    capturedAtMs = snapshot.capturedAtMs;
                    return true;
                }
            }
        }
        catch
        {
            return false;
        }
    }
}
