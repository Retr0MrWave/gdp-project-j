using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public class OrderBookHistoryRenderer : MonoBehaviour
{
    public enum ZMode
    {
        Exact,
        Cumulative
    }

    [Header("Axes")]
    public float timeXScale = 20f;
    public float priceYScale = 10f;
    public float amountZScale = 2f;
    public bool centerOnMidPrice = true;
    public bool invertPriceAxis = false;
    public bool useActualTimeSpacing = true;
    public bool fixedCenter = true;
    private double fixedCenterPoint = -1.0;

    [Header("Z Meaning")]
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

    private Mesh _bidMesh;
    private Mesh _askMesh;
    private MeshCollider _bidMeshCollider;
    private MeshCollider _askMeshCollider;
    private GameObject _bidsChild;
    private GameObject _asksChild;
    private Material _generatedBidMaterial;
    private Material _generatedAskMaterial;

#if UNITY_EDITOR
    private bool _materialRefreshQueued;
#endif

    private void Awake()
    {
        EnsureInitialized();
    }

    private void OnEnable()
    {
        EnsureInitialized();
        ApplyMaterials();
    }

    private void OnValidate()
    {
        EnsureInitialized();

#if UNITY_EDITOR
        if (!_materialRefreshQueued)
        {
            _materialRefreshQueued = true;
            EditorApplication.delayCall -= DelayedMaterialRefresh;
            EditorApplication.delayCall += DelayedMaterialRefresh;
        }
#endif
    }

#if UNITY_EDITOR
    private void DelayedMaterialRefresh()
    {
        EditorApplication.delayCall -= DelayedMaterialRefresh;
        _materialRefreshQueued = false;

        if (this == null)
            return;

        EnsureInitialized();
        ApplyMaterials();
    }
#endif

    public void ClearWindow()
    {
        EnsureInitialized();

        _bidMesh.Clear();
        _askMesh.Clear();
    }

    public void SetWindow(IReadOnlyList<OrderBookSnapshot> snapshots)
    {
        EnsureInitialized();

        if (snapshots == null || snapshots.Count < 2)
        {
            ClearWindow();
            return;
        }

        List<double> uniquePrices = BuildUniquePriceAxis(snapshots);
        if (uniquePrices.Count == 0)
        {
            ClearWindow();
            return;
        }

        double[] priceBoundaries = BuildPriceBoundaries(uniquePrices);
        double[,] bidGrid;
        double[,] askGrid;
        BuildExactPriceGrids(snapshots, uniquePrices, out bidGrid, out askGrid);

        float[] xPositions = BuildXPositions(snapshots);

        double minBoundary = priceBoundaries[0];
        double maxBoundary = priceBoundaries[priceBoundaries.Length - 1];
        double priceOrigin = centerOnMidPrice ? 0.5 * (minBoundary + maxBoundary) : 0.0;

        if (fixedCenter && fixedCenterPoint == -1.0) fixedCenterPoint = priceOrigin;
        if (fixedCenter) priceOrigin = fixedCenterPoint;

        BuildIntoMesh(
            _bidMesh,
            bidGrid,
            priceBoundaries,
            xPositions,
            priceOrigin,
            true
        );

        BuildIntoMesh(
            _askMesh,
            askGrid,
            priceBoundaries,
            xPositions,
            priceOrigin,
            false
        );
    }

    private void EnsureInitialized()
    {
        if (_bidsChild == null)
            _bidsChild = EnsureChild(bidsChildName);

        if (_asksChild == null)
            _asksChild = EnsureChild(asksChildName);

        MeshFilter bidFilter = _bidsChild.GetComponent<MeshFilter>();
        MeshFilter askFilter = _asksChild.GetComponent<MeshFilter>();

        if (_bidMesh == null)
        {
            _bidMesh = new Mesh();
            _bidMesh.name = "Dynamic Bids Mesh";
            _bidMesh.MarkDynamic();
            bidFilter.sharedMesh = _bidMesh;
        }

        _bidMeshCollider = _bidsChild.GetComponent<MeshCollider>();

        if (_askMesh == null)
        {
            _askMesh = new Mesh();
            _askMesh.name = "Dynamic Asks Mesh";
            _askMesh.MarkDynamic();
            askFilter.sharedMesh = _askMesh;
        }

        _askMeshCollider = _asksChild.GetComponent<MeshCollider>();

        ApplyMaterials();
    }

    private void ApplyMaterials()
    {
        if (_bidsChild == null || _asksChild == null)
            return;

        MeshRenderer bidRenderer = _bidsChild.GetComponent<MeshRenderer>();
        MeshRenderer askRenderer = _asksChild.GetComponent<MeshRenderer>();

        bidRenderer.sharedMaterial = bidMaterial != null ? bidMaterial : GetOrCreateGeneratedMaterial(true);
        askRenderer.sharedMaterial = askMaterial != null ? askMaterial : GetOrCreateGeneratedMaterial(false);
    }

    private void BuildIntoMesh(
        Mesh mesh,
        double[,] rawGrid,
        double[] priceBoundaries,
        float[] xPositions,
        double priceOrigin,
        bool isBidMesh)
    {
        int timeCount = rawGrid.GetLength(0);
        int levelCount = rawGrid.GetLength(1);

        mesh.Clear();
        if (timeCount < 2 || levelCount < 1)
            return;

        int rowLength = levelCount;
        int[,] indexMap = new int[timeCount, rowLength];

        double[,] smoothed = new double[timeCount, levelCount];
        
        for (int t = 0; t < timeCount; t++)
        {
            for (int i = 0; i < levelCount; i++)
            {

                smoothed[t, i] = (
                    rawGrid[t, i] +
                    rawGrid[Mathf.Max(t - 1, 0), Mathf.Max(i - 1, 0)] +
                    rawGrid[Mathf.Min(t + 1, timeCount - 1), Mathf.Min(i + 1, levelCount - 1)] +
                    rawGrid[t, Mathf.Max(i - 1, 0)] +
                    rawGrid[t, Mathf.Min(i + 1, levelCount - 1)] +
                    rawGrid[Mathf.Max(t - 1, 0), i] +
                    rawGrid[Mathf.Min(t + 1, timeCount - 1), i] +
                    rawGrid[Mathf.Max(t - 1, 0), Mathf.Min(i + 1, levelCount - 1)] +
                    rawGrid[Mathf.Min(t + 1, timeCount - 1), Mathf.Max(i - 1, 0)]
                ) / 9.0;

            }
        }

        List<Vector3> vertices = new List<Vector3>(timeCount * rowLength);
        List<Vector2> uvs = new List<Vector2>(timeCount * rowLength);
        List<int> triangles = new List<int>((timeCount - 1) * (rowLength - 1) * (doubleSided ? 12 : 6));

        for (int t = 0; t < timeCount; t++)
        {
            float x = xPositions[t];

            for (int i = 0; i < levelCount; i++)
            {
                float shaped = ShapeVolume(smoothed[t, i]);
                float z = shaped * amountZScale;

                float y0 = (float)((priceBoundaries[i] - priceOrigin) * priceYScale * (invertPriceAxis ? -1.0 : 1.0));
                float y1 = (float)((priceBoundaries[i + 1] - priceOrigin) * priceYScale * (invertPriceAxis ? -1.0 : 1.0));

                int a = vertices.Count;
                vertices.Add(new Vector3(x, y0, z));
                uvs.Add(new Vector2(timeCount == 1 ? 0f : t / (float)(timeCount - 1), i / (float)levelCount));

                /*int b = vertices.Count;
                vertices.Add(new Vector3(x, y1, z));
                uvs.Add(new Vector2(timeCount == 1 ? 0f : t / (float)(timeCount - 1), (i + 1) / (float)levelCount));
                */

                indexMap[t, i /* 2*/] = a;
                /*indexMap[t, i * 2 + 1] = b;*/
            }
        }

        float shapedThreshold = ShapeVolume(emptyCellThreshold) * amountZScale;

        for (int t = 0; t < timeCount - 1; t++)
        {
            for (int r = 0; r < rowLength - 1; r++)
            {
                int a = indexMap[t, r];
                int b = indexMap[t + 1, r];
                int c = indexMap[t, r + 1];
                int d = indexMap[t + 1, r + 1];

                float z00 = vertices[a].z;
                float z10 = vertices[b].z;
                float z01 = vertices[c].z;
                float z11 = vertices[d].z;

                if (z00 <= shapedThreshold &&
                    z10 <= shapedThreshold &&
                    z01 <= shapedThreshold &&
                    z11 <= shapedThreshold)
                {
                    continue;
                }

                triangles.Add(a);
                triangles.Add(c);
                triangles.Add(b);

                triangles.Add(b);
                triangles.Add(c);
                triangles.Add(d);

                if (doubleSided)
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

        mesh.indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        if (isBidMesh)
            _bidMeshCollider.sharedMesh = mesh;
        else
            _askMeshCollider.sharedMesh = mesh;
    }

    private static List<double> BuildUniquePriceAxis(IReadOnlyList<OrderBookSnapshot> snapshots)
    {
        SortedSet<double> unique = new SortedSet<double>();

        for (int i = 0; i < snapshots.Count; i++)
        {
            List<OrderBookLevel> bids = snapshots[i].bids;
            List<OrderBookLevel> asks = snapshots[i].asks;

            for (int j = 0; j < bids.Count; j++)
                unique.Add(bids[j].price);

            for (int j = 0; j < asks.Count; j++)
                unique.Add(asks[j].price);
        }

        return new List<double>(unique);
    }

    private static double[] BuildPriceBoundaries(List<double> prices)
    {
        int n = prices.Count;
        double[] boundaries = new double[n + 1];

        if (n == 1)
        {
            double p = prices[0];
            double halfSpan = Math.Max(0.0001, Math.Max(Math.Abs(p) * 0.001, 0.5));
            boundaries[0] = p - halfSpan;
            boundaries[1] = p + halfSpan;
            return boundaries;
        }

        boundaries[0] = prices[0] - 0.5 * (prices[1] - prices[0]);

        for (int i = 1; i < n; i++)
            boundaries[i] = 0.5 * (prices[i - 1] + prices[i]);

        boundaries[n] = prices[n - 1] + 0.5 * (prices[n - 1] - prices[n - 2]);

        return boundaries;
    }

    private void BuildExactPriceGrids(
        IReadOnlyList<OrderBookSnapshot> snapshots,
        List<double> uniquePrices,
        out double[,] bidGrid,
        out double[,] askGrid)
    {
        int timeCount = snapshots.Count;
        int levelCount = uniquePrices.Count;

        bidGrid = new double[timeCount, levelCount];
        askGrid = new double[timeCount, levelCount];

        Dictionary<double, int> priceToIndex = new Dictionary<double, int>(levelCount);
        for (int i = 0; i < levelCount; i++)
            priceToIndex[uniquePrices[i]] = i;

        for (int t = 0; t < timeCount; t++)
        {
            double[] bidExact = new double[levelCount];
            double[] askExact = new double[levelCount];

            List<OrderBookLevel> bids = snapshots[t].bids;
            List<OrderBookLevel> asks = snapshots[t].asks;

            for (int i = 0; i < bids.Count; i++)
            {
                int index;
                if (priceToIndex.TryGetValue(bids[i].price, out index))
                    bidExact[index] += bids[i].quantity;
            }

            for (int i = 0; i < asks.Count; i++)
            {
                int index;
                if (priceToIndex.TryGetValue(asks[i].price, out index))
                    askExact[index] += asks[i].quantity;
            }

            if (zMode == ZMode.Exact)
            {
                for (int i = 0; i < levelCount; i++)
                {
                    bidGrid[t, i] = bidExact[i];
                    askGrid[t, i] = askExact[i];
                }
            }
            else
            {
                double runningBid = 0.0;
                for (int i = levelCount - 1; i >= 0; i--)
                {
                    runningBid += bidExact[i];
                    bidGrid[t, i] = runningBid;
                }

                double runningAsk = 0.0;
                for (int i = 0; i < levelCount; i++)
                {
                    runningAsk += askExact[i];
                    askGrid[t, i] = runningAsk;
                }
            }
        }
    }

    private float[] BuildXPositions(IReadOnlyList<OrderBookSnapshot> snapshots)
    {
        int count = snapshots.Count;
        float[] x = new float[count];

        if (count == 1)
        {
            x[0] = 0f;
            return x;
        }

        if (useActualTimeSpacing)
        {
            long first = snapshots[0].capturedAtMs;
            long last = snapshots[count - 1].capturedAtMs;

            if (last > first)
            {
                double span = last - first;
                for (int i = 0; i < count; i++)
                {
                    double u = (snapshots[i].capturedAtMs - first) / span;
                    x[i] = (float)(u * timeXScale);
                }
                return x;
            }
        }

        for (int i = 0; i < count; i++)
            x[i] = (i / (float)(count - 1)) * timeXScale;

        return x;
    }

    private float ShapeVolume(double rawQty)
    {
        double v = Math.Max(0.0, rawQty);

        if (useLogVolume)
            v = Math.Log(1.0 + v);

        if (Math.Abs(volumeExponent - 1f) > 1e-6f)
            v = Math.Pow(v, volumeExponent);

        return (float)v;
    }

    private GameObject EnsureChild(string childName)
    {
        Transform existing = transform.Find(childName);
        if (existing != null)
            return existing.gameObject;

        GameObject go = new GameObject(childName);
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>();
        go.AddComponent<MeshRenderer>();
        return go;
    }

    private Material GetOrCreateGeneratedMaterial(bool bids)
    {
        Material cache = bids ? _generatedBidMaterial : _generatedAskMaterial;
        if (cache != null)
            return cache;

        Shader shader = FindUsableShader();
        if (shader == null)
            return null;

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
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader != null) return shader;

        shader = Shader.Find("Standard");
        if (shader != null) return shader;

        shader = Shader.Find("Sprites/Default");
        return shader;
    }

    private static void SafeDestroy(UnityEngine.Object obj)
    {
        if (obj == null)
            return;

        if (Application.isPlaying)
            Destroy(obj);
        else
            DestroyImmediate(obj);
    }

    private void OnDestroy()
    {
#if UNITY_EDITOR
        EditorApplication.delayCall -= DelayedMaterialRefresh;
#endif

        if (_bidMesh != null) SafeDestroy(_bidMesh);
        if (_askMesh != null) SafeDestroy(_askMesh);
        if (_generatedBidMaterial != null) SafeDestroy(_generatedBidMaterial);
        if (_generatedAskMaterial != null) SafeDestroy(_generatedAskMaterial);
    }
}
