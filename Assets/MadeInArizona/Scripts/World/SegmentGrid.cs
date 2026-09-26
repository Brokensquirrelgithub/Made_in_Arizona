using System.Collections.Generic;
using UnityEngine;

namespace MadeInArizona
{
    /// <summary>
    /// Buckets 2D line segments into square cells so "nearest segment within a fixed reach" queries only visit the
    /// segments near the query point. Terrain generation asks this for every heightfield vertex, so a full scan of
    /// every road segment per vertex would grow with the number of towns.
    /// </summary>
    public sealed class SegmentGrid
    {
        readonly float cell, reach;
        readonly List<Vector2> starts = new List<Vector2>(), ends = new List<Vector2>();
        readonly List<float> startHeights = new List<float>(), endHeights = new List<float>();
        readonly Dictionary<long, List<int>> cells = new Dictionary<long, List<int>>();

        public SegmentGrid(float cellSize, float queryReach) { cell = Mathf.Max(1, cellSize); reach = Mathf.Max(0, queryReach); }
        public int Count => starts.Count;
        /// <summary>Distances beyond this are reported as <see cref="Far"/> rather than measured.</summary>
        public float Reach => reach;
        public float Far => reach + 1;

        public void Clear() { starts.Clear(); ends.Clear(); startHeights.Clear(); endHeights.Clear(); cells.Clear(); }

        public void Add(Vector2 a, Vector2 b, float heightA = 0, float heightB = 0)
        {
            int index = starts.Count;
            starts.Add(a); ends.Add(b); startHeights.Add(heightA); endHeights.Add(heightB);
            int x0 = Mathf.FloorToInt((Mathf.Min(a.x, b.x) - reach) / cell), x1 = Mathf.FloorToInt((Mathf.Max(a.x, b.x) + reach) / cell);
            int z0 = Mathf.FloorToInt((Mathf.Min(a.y, b.y) - reach) / cell), z1 = Mathf.FloorToInt((Mathf.Max(a.y, b.y) + reach) / cell);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    long key = Key(x, z);
                    if (!cells.TryGetValue(key, out var list)) cells.Add(key, list = new List<int>(8));
                    list.Add(index);
                }
        }

        /// <summary>
        /// Exact distance to the nearest segment and its interpolated height when one lies within <see cref="Reach"/>;
        /// otherwise returns <see cref="Far"/> (a lower bound) and a height of 0.
        /// </summary>
        public float Nearest(Vector2 p, out float height, out Vector2 closest)
        {
            height = 0; closest = p;
            if (!cells.TryGetValue(Key(Mathf.FloorToInt(p.x / cell), Mathf.FloorToInt(p.y / cell)), out var list)) return Far;
            float best = float.MaxValue;
            foreach (int i in list)
            {
                Vector2 a = starts[i], d = ends[i] - a;
                float u = Mathf.Clamp01(Vector2.Dot(p - a, d) / Mathf.Max(.0001f, d.sqrMagnitude));
                Vector2 q = a + d * u;
                float sq = (q - p).sqrMagnitude;
                if (sq < best) { best = sq; height = Mathf.Lerp(startHeights[i], endHeights[i], u); closest = q; }
            }
            if (best > reach * reach) { height = 0; closest = p; return Far; }
            return Mathf.Sqrt(best);
        }
        public float Nearest(Vector2 p, out float height) => Nearest(p, out height, out _);
        public float Distance(Vector2 p) => Nearest(p, out _, out _);

        static long Key(int x, int z) => (long)x << 32 | (uint)z;
    }
}
