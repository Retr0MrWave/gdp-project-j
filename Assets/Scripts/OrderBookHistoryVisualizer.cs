using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public class OrderBookHistoryVisualizer : MonoBehaviour
{
    public enum ZMode
    {
        Exact,
        Cumulative
    }

    [Header("Input")]
    public TextAsset orderBookJsonl;
    public bool autoRebuildInEditor = true;

    [Min(1)] public int maxSamples = 256;
    [Min(2)] public int priceBins = 128;

    [Header("Axis Scales")]
    public float timeXScale = 20f;
    public float priceYScale = 10f;
    public float amountZScale = 2f;
    public bool centerOnMidPrice = true;
    public bool invertPriceAxis = false;

    [Header("Z Axis Meaning")]
    public ZMode zMode = ZMode.Cumulative;

    [Header("Volume Shaping")]
    public bool useLogVolume = true;
    [Range(0.1f, 3f)] public float volumeExponent = 1f;
    [Min(0f)] public float emptyCellThreshold = 0.0001f;

    [Header("Rendering")]
    public bool doubleSided = true;
    public Material bidMaterial;
    public Material askMaterial;

    [Header("Generated Child Names")]
    public string bidsChildName = "Bids Surface";
    public string asksChildName = "Asks Surface";

    private Material _generatedBidMaterial;
    private Material _generatedAskMaterial;

#if UNITY_EDITOR
    private bool _rebuildQueued;
#endif

    private static readonly Regex CapturedAtRegex =
        new Regex(@"""captured_at_ms"":(?<v>\d+)", RegexOptions.Compiled);

    private static readonly Regex PairRegex =
        new Regex(@"\[""(?<p>[^""]+)"",""(?<q>[^""]+)""\]", RegexOptions.Compiled);

    [Serializable]
    private struct PriceLevel
    {
        public float price;
        public float qty;

        public PriceLevel(float price, float qty)
        {
            this.price = price;
            this.qty = qty;
        }
    }

    private sealed class Sample
    {
        public long capturedAtMs;
        public readonly List<PriceLevel> bids = new List<PriceLevel>();
        public readonly List<PriceLevel> asks = new List<PriceLevel>();
    }

    private void OnEnable()
    {
        RequestRebuild();
    }

    private void OnValidate()
    {
        if (!enabled) return;
        if (!Application.isPlaying && !autoRebuildInEditor) return;
        RequestRebuild();
    }

    [ContextMenu("Rebuild Visualization")]
    public void Rebuild()
    {
        RebuildImmediate();
    }

    private void RequestRebuild()
    {
        if (Application.isPlaying)
        {
            RebuildImmediate();
            return;
        }

#if UNITY_EDITOR
        if (!autoRebuildInEditor) return;
        if (_rebuildQueued) return;

        _rebuildQueued = true;
        EditorApplication.delayCall -= DelayedRebuild;
        EditorApplication.delayCall += DelayedRebuild;
#endif
    }

#if UNITY_EDITOR
    private void DelayedRebuild()
    {
        EditorApplication.delayCall -= DelayedRebuild;
        _rebuildQueued = false;

        if (this == null) return;
        if (!enabled) return;

        RebuildImmediate();
    }
#endif

    private void RebuildImmediate()
    {
        if (orderBookJsonl == null || string.IsNullOrWhiteSpace(orderBookJsonl.text))
        {
            ClearGeneratedMeshes();
            return;
        }

        List<Sample> samples = ParseJsonl(orderBookJsonl.text);
        if (samples.Count == 0)
        {
            Debug.LogWarning("OrderBookHistoryVisualizer: no valid samples found.");
            ClearGeneratedMeshes();
            return;
        }

        samples = Downsample(samples, maxSamples);

        float[,] bidGrid;
        float[,] askGrid;
        float minPrice;
        float maxPrice;

        BuildDepthProfiles(samples, out bidGrid, out askGrid, out minPrice, out maxPrice);

        if (float.IsInfinity(minPrice) || float.IsInfinity(maxPrice) || Mathf.Approximately(minPrice, maxPrice))
        {
            Debug.LogWarning("OrderBookHistoryVisualizer: invalid price range.");
            ClearGeneratedMeshes();
            return;
        }

        float priceOrigin = centerOnMidPrice ? 0.5f * (minPrice + maxPrice) : 0f;

        Mesh bidsMesh = CreateSteppedSurfaceMesh(
            bidGrid,
            minPrice,
            maxPrice,
            priceOrigin,
            timeXScale,
            priceYScale,
            amountZScale,
            invertPriceAxis,
            doubleSided,
            emptyCellThreshold
        );
        bidsMesh.name = "Generated Bids Mesh";

        Mesh asksMesh = CreateSteppedSurfaceMesh(
            askGrid,
            minPrice,
            maxPrice,
            priceOrigin,
            timeXScale,
            priceYScale,
            amountZScale,
            invertPriceAxis,
            doubleSided,
            emptyCellThreshold
        );
        asksMesh.name = "Generated Asks Mesh";

        ApplyMeshToChild(
            EnsureChild(bidsChildName),
            bidsMesh,
            bidMaterial != null ? bidMaterial : GetOrCreateGeneratedMaterial(true)
        );

        ApplyMeshToChild(
            EnsureChild(asksChildName),
            asksMesh,
            askMaterial != null ? askMaterial : GetOrCreateGeneratedMaterial(false)
        );
    }

    private List<Sample> ParseJsonl(string text)
    {
        List<Sample> samples = new List<Sample>();
        string[] lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrEmpty(line))
                continue;

            try
            {
                Sample sample = new Sample();

                Match capturedAt = CapturedAtRegex.Match(line);
                sample.capturedAtMs = capturedAt.Success
                    ? long.Parse(capturedAt.Groups["v"].Value, CultureInfo.InvariantCulture)
                    : i;

                string bidsSegment = ExtractArraySegment(line, "\"bids\":");
                string asksSegment = ExtractArraySegment(line, "\"asks\":");

                if (!string.IsNullOrEmpty(bidsSegment))
                    sample.bids.AddRange(ParsePairs(bidsSegment));

                if (!string.IsNullOrEmpty(asksSegment))
                    sample.asks.AddRange(ParsePairs(asksSegment));

                if (sample.bids.Count > 0 || sample.asks.Count > 0)
                    samples.Add(sample);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"OrderBookHistoryVisualizer: failed to parse line {i + 1}: {ex.Message}");
            }
        }

        return samples;
    }

    private static string ExtractArraySegment(string input, string key)
    {
        int keyIndex = input.IndexOf(key, StringComparison.Ordinal);
        if (keyIndex < 0)
            return null;

        int start = input.IndexOf('[', keyIndex);
        if (start < 0)
            return null;

        int depth = 0;
        for (int i = start; i < input.Length; i++)
        {
            char c = input[i];
            if (c == '[') depth++;
            else if (c == ']')
            {
                depth--;
                if (depth == 0)
                    return input.Substring(start, i - start + 1);
            }
        }

        return null;
    }

    private static IEnumerable<PriceLevel> ParsePairs(string arraySegment)
    {
        MatchCollection matches = PairRegex.Matches(arraySegment);
        foreach (Match match in matches)
        {
            float price = float.Parse(match.Groups["p"].Value, CultureInfo.InvariantCulture);
            float qty = float.Parse(match.Groups["q"].Value, CultureInfo.InvariantCulture);
            yield return new PriceLevel(price, qty);
        }
    }

    private static List<Sample> Downsample(List<Sample> samples, int maxCount)
    {
        if (maxCount <= 0 || samples.Count <= maxCount)
            return samples;

        if (maxCount == 1)
            return new List<Sample> { samples[0] };

        List<Sample> result = new List<Sample>(maxCount);
        for (int i = 0; i < maxCount; i++)
        {
            float t = i / (float)(maxCount - 1);
            int index = Mathf.RoundToInt(t * (samples.Count - 1));
            result.Add(samples[index]);
        }
        return result;
    }

    private void BuildDepthProfiles(
        List<Sample> samples,
        out float[,] bidGrid,
        out float[,] askGrid,
        out float minPrice,
        out float maxPrice)
    {
        minPrice = float.PositiveInfinity;
        maxPrice = float.NegativeInfinity;

        int timeCount = samples.Count;
        int bins = Mathf.Max(2, priceBins);

        for (int t = 0; t < timeCount; t++)
        {
            foreach (PriceLevel bid in samples[t].bids)
            {
                minPrice = Mathf.Min(minPrice, bid.price);
                maxPrice = Mathf.Max(maxPrice, bid.price);
            }

            foreach (PriceLevel ask in samples[t].asks)
            {
                minPrice = Mathf.Min(minPrice, ask.price);
                maxPrice = Mathf.Max(maxPrice, ask.price);
            }
        }

        if (float.IsInfinity(minPrice) || float.IsInfinity(maxPrice))
        {
            bidGrid = new float[0, 0];
            askGrid = new float[0, 0];
            return;
        }

        if (Mathf.Approximately(minPrice, maxPrice))
        {
            minPrice -= 0.5f;
            maxPrice += 0.5f;
        }

        bidGrid = new float[timeCount, bins];
        askGrid = new float[timeCount, bins];

        for (int t = 0; t < timeCount; t++)
        {
            float[] bidExact = new float[bins];
            float[] askExact = new float[bins];

            foreach (PriceLevel bid in samples[t].bids)
            {
                int p = PriceToBinIndex(bid.price, minPrice, maxPrice, bins);
                bidExact[p] += bid.qty;
            }

            foreach (PriceLevel ask in samples[t].asks)
            {
                int p = PriceToBinIndex(ask.price, minPrice, maxPrice, bins);
                askExact[p] += ask.qty;
            }

            if (zMode == ZMode.Exact)
            {
                for (int p = 0; p < bins; p++)
                {
                    bidGrid[t, p] = bidExact[p];
                    askGrid[t, p] = askExact[p];
                }
            }
            else
            {
                float runningBid = 0f;
                for (int p = bins - 1; p >= 0; p--)
                {
                    runningBid += bidExact[p];
                    bidGrid[t, p] = runningBid;
                }

                float runningAsk = 0f;
                for (int p = 0; p < bins; p++)
                {
                    runningAsk += askExact[p];
                    askGrid[t, p] = runningAsk;
                }
            }
        }
    }

    private static int PriceToBinIndex(float price, float minPrice, float maxPrice, int bins)
    {
        float normalized = Mathf.InverseLerp(minPrice, maxPrice, price);
        return Mathf.Clamp(Mathf.FloorToInt(normalized * bins), 0, bins - 1);
    }

    private Mesh CreateSteppedSurfaceMesh(
        float[,] rawGrid,
        float minPrice,
        float maxPrice,
        float priceOrigin,
        float xScale,
        float yScale,
        float zScale,
        bool invertY,
        bool makeDoubleSided,
        float emptyThreshold)
    {
        int timeCount = rawGrid.GetLength(0);
        int binCount = rawGrid.GetLength(1);

        Mesh mesh = new Mesh();
        if (timeCount < 2 || binCount < 1)
            return mesh;

        int rowLength = binCount * 2;
        int[,] indexMap = new int[timeCount, rowLength];
        float[,] shapedGrid = new float[timeCount, binCount];

        List<Vector3> vertices = new List<Vector3>(timeCount * rowLength);
        List<Vector2> uvs = new List<Vector2>(timeCount * rowLength);

        for (int t = 0; t < timeCount; t++)
        {
            float x = timeCount == 1 ? 0f : (t / (float)(timeCount - 1)) * xScale;

            for (int p = 0; p < binCount; p++)
            {
                float raw = rawGrid[t, p];
                float shaped = ShapeVolume(raw);
                shapedGrid[t, p] = shaped;

                float y0Price = Mathf.Lerp(minPrice, maxPrice, p / (float)binCount);
                float y1Price = Mathf.Lerp(minPrice, maxPrice, (p + 1) / (float)binCount);

                float y0 = (y0Price - priceOrigin) * yScale * (invertY ? -1f : 1f);
                float y1 = (y1Price - priceOrigin) * yScale * (invertY ? -1f : 1f);
                float z = shaped * zScale;

                int i0 = vertices.Count;
                vertices.Add(new Vector3(x, y0, z));
                uvs.Add(new Vector2(timeCount == 1 ? 0f : t / (float)(timeCount - 1), p / (float)binCount));

                int i1 = vertices.Count;
                vertices.Add(new Vector3(x, y1, z));
                uvs.Add(new Vector2(timeCount == 1 ? 0f : t / (float)(timeCount - 1), (p + 1) / (float)binCount));

                indexMap[t, p * 2] = i0;
                indexMap[t, p * 2 + 1] = i1;
            }
        }

        List<int> triangles = new List<int>();

        float shapedThreshold = ShapeVolume(emptyThreshold) * zScale;

        for (int t = 0; t < timeCount - 1; t++)
        {
            for (int r = 0; r < rowLength - 1; r++)
            {
                int pA = r / 2;
                int pB = (r + 1) / 2;

                float z00 = vertices[indexMap[t, r]].z;
                float z10 = vertices[indexMap[t + 1, r]].z;
                float z01 = vertices[indexMap[t, r + 1]].z;
                float z11 = vertices[indexMap[t + 1, r + 1]].z;

                if (z00 <= shapedThreshold &&
                    z10 <= shapedThreshold &&
                    z01 <= shapedThreshold &&
                    z11 <= shapedThreshold)
                {
                    continue;
                }

                int a = indexMap[t, r];
                int b = indexMap[t + 1, r];
                int c = indexMap[t, r + 1];
                int d = indexMap[t + 1, r + 1];

                triangles.Add(a);
                triangles.Add(c);
                triangles.Add(b);

                triangles.Add(b);
                triangles.Add(c);
                triangles.Add(d);

                if (makeDoubleSided)
                {
                    triangles.Add(a);
                    triangles.Add(b);
                    triangles.Add(c);

                    triangles.Add(b);
                    triangles.Add(d);
                    triangles.Add(c);
                }
            }
        }

        if (vertices.Count > 65535)
            mesh.indexFormat = IndexFormat.UInt32;

        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }

    private float ShapeVolume(float rawQty)
    {
        float v = Mathf.Max(0f, rawQty);

        if (useLogVolume)
            v = Mathf.Log(1f + v);

        if (!Mathf.Approximately(volumeExponent, 1f))
            v = Mathf.Pow(v, volumeExponent);

        return v;
    }

    private GameObject EnsureChild(string childName)
    {
        Transform child = transform.Find(childName);
        if (child != null)
            return child.gameObject;

        GameObject go = new GameObject(childName);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        go.AddComponent<MeshFilter>();
        go.AddComponent<MeshRenderer>();

        return go;
    }

    private void ApplyMeshToChild(GameObject child, Mesh newMesh, Material material)
    {
        MeshFilter filter = child.GetComponent<MeshFilter>();
        MeshRenderer renderer = child.GetComponent<MeshRenderer>();

        Mesh old = filter.sharedMesh;
        if (old != null && old != newMesh && old.name.StartsWith("Generated "))
            SafeDestroy(old);

        filter.sharedMesh = newMesh;

        if (material != null)
            renderer.sharedMaterial = material;
    }

    private void ClearGeneratedMeshes()
    {
        ClearChildMesh(bidsChildName);
        ClearChildMesh(asksChildName);
    }

    private void ClearChildMesh(string childName)
    {
        Transform child = transform.Find(childName);
        if (child == null)
            return;

        MeshFilter filter = child.GetComponent<MeshFilter>();
        if (filter != null && filter.sharedMesh != null && filter.sharedMesh.name.StartsWith("Generated "))
        {
            SafeDestroy(filter.sharedMesh);
            filter.sharedMesh = null;
        }
    }

    private Material GetOrCreateGeneratedMaterial(bool bids)
    {
        Material cache = bids ? _generatedBidMaterial : _generatedAskMaterial;
        if (cache != null) return cache;

        Shader shader = FindUsableShader();
        if (shader == null) return null;

        cache = new Material(shader);
        cache.name = bids ? "Generated Bid Material" : "Generated Ask Material";
        cache.hideFlags = HideFlags.HideAndDontSave;

        if (cache.HasProperty("_BaseColor"))
        {
            cache.SetColor("_BaseColor", bids
                ? new Color(0.20f, 0.85f, 0.35f, 1f)
                : new Color(0.90f, 0.25f, 0.25f, 1f));
        }
        else if (cache.HasProperty("_Color"))
        {
            cache.color = bids
                ? new Color(0.20f, 0.85f, 0.35f, 1f)
                : new Color(0.90f, 0.25f, 0.25f, 1f);
        }

        if (bids) _generatedBidMaterial = cache;
        else _generatedAskMaterial = cache;

        return cache;
    }

    private static Shader FindUsableShader()
    {
        Shader s = Shader.Find("Universal Render Pipeline/Unlit");
        if (s != null) return s;

        s = Shader.Find("Standard");
        if (s != null) return s;

        s = Shader.Find("Sprites/Default");
        return s;
    }

    private static void SafeDestroy(UnityEngine.Object obj)
    {
        if (obj == null) return;

        if (Application.isPlaying)
            Destroy(obj);
        else
            DestroyImmediate(obj);
    }

    private void OnDestroy()
    {
#if UNITY_EDITOR
        EditorApplication.delayCall -= DelayedRebuild;
#endif
        if (_generatedBidMaterial != null) SafeDestroy(_generatedBidMaterial);
        if (_generatedAskMaterial != null) SafeDestroy(_generatedAskMaterial);
    }
}
