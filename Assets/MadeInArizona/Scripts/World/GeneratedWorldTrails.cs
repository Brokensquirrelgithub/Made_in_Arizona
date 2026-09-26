using System.Collections.Generic;
using UnityEngine;
using static MadeInArizona.WorldArt;

namespace MadeInArizona
{
    /// <summary>
    /// A web of unpaved dirt roads, tracks and footpaths across the wilderness. Junctions are scattered over the
    /// map and joined to their nearest neighbours, points of interest and the main roads, so every area has routes.
    /// </summary>
    public sealed partial class GeneratedWorld
    {
        sealed class Trail { public Vector2[] points; public float width; public bool dirtRoad; }
        struct TrailSegment { public Vector2 a, b; public float half; }

        const float TrailCell = 32, TrailQueryReach = 8, TownTrailClearance = 60;
        readonly List<Trail> trails = new List<Trail>();
        readonly List<TrailSegment> trailSegments = new List<TrailSegment>();
        readonly Dictionary<long, List<int>> trailCells = new Dictionary<long, List<int>>();

        public int TrailCount => trails.Count;
        /// <summary>Midpoint and type of a trail, for review captures.</summary>
        public Vector3 TrailMidpoint(int index, out float width, out bool dirtRoad, out Vector2 direction)
        {
            var trail = trails[index]; int m = trail.points.Length / 2;
            width = trail.width; dirtRoad = trail.dirtRoad; direction = (trail.points[m + 1] - trail.points[m - 1]).normalized;
            Vector3 p = new Vector3(trail.points[m].x, 0, trail.points[m].y); p.y = HeightAt(p); return p;
        }

        void PlanTrails()
        {
            float density = Mathf.Clamp(cfg.trailDensity, 0, 3);
            if (density <= 0) return;
            // Separate stream so trails never shift the layout of towns, pins or scenery for an existing seed.
            var random = new System.Random(unchecked(seed * 486187739 + 1013));
            float scale = size / 1600f, spacing = size * .075f / Mathf.Sqrt(density);
            // Small maps keep a floor on junctions; town clearance already consumes much of their area.
            int target = Mathf.RoundToInt(38 * Mathf.Max(scale * scale, .6f) * density);
            var nodes = new List<Vector2>();
            for (int attempt = 0; attempt < target * 30 && nodes.Count < target; attempt++)
            {
                var p = new Vector2(Rand(random, -half * .95f, half * .95f), Rand(random, -half * .95f, half * .95f));
                if (!InOutline(p / size) || TownDistance(p) < TownTrailClearance + 15) continue;
                if (nodes.Exists(n => (n - p).sqrMagnitude < spacing * spacing)) continue;
                nodes.Add(p);
            }
            int junctions = nodes.Count;
            foreach (var pin in Pins) if (pin.kind != "town" && TownDistance(XZ(pin.position)) > TownTrailClearance) nodes.Add(XZ(pin.position));

            var linked = new HashSet<long>();
            for (int i = 0; i < nodes.Count; i++)
            {
                int links = i < junctions ? (random.NextDouble() < .3 ? 3 : 2) : 1;
                var order = new List<int>();
                for (int j = 0; j < nodes.Count; j++) if (j != i) order.Add(j);
                order.Sort((a, b) => (nodes[a] - nodes[i]).sqrMagnitude.CompareTo((nodes[b] - nodes[i]).sqrMagnitude));
                int made = 0;
                foreach (int j in order)
                {
                    if (made >= links || (nodes[j] - nodes[i]).magnitude > spacing * 3.4f) break;
                    long key = i < j ? (long)i << 32 | (uint)j : (long)j << 32 | (uint)i;
                    if (linked.Contains(key)) { made++; continue; }
                    if (AddTrail(nodes[i], nodes[j], random, false)) { linked.Add(key); made++; }
                }
                // Every third junction and every point of interest also has a dirt road out to the paved network.
                if ((i < junctions && i % 3 == 0) || i >= junctions)
                {
                    Vector2 road = NearestRoadPoint(nodes[i]);
                    if ((road - nodes[i]).magnitude < spacing * 2.6f) AddTrail(nodes[i], road, random, true);
                }
            }
        }

        bool AddTrail(Vector2 a, Vector2 b, System.Random random, bool toRoad)
        {
            float length = Vector2.Distance(a, b);
            if (length < 25) return false;
            double kind = random.NextDouble();
            bool dirtRoad = toRoad || kind < .3;
            float width = dirtRoad ? Rand(random, 6, 8) : kind < .7 ? Rand(random, 4, 5.5f) : Rand(random, 2.4f, 3.4f);
            // Each trail has its own gentle bow plus two wiggles, all fading to zero at the junctions it joins.
            float bow = Rand(random, -.16f, .16f) * length, sway = Rand(random, -.06f, .06f) * length, swayFreq = Rand(random, 1.2f, 3.2f);
            float wiggle = Rand(random, -.022f, .022f) * length, wiggleFreq = Rand(random, 4, 8), phase = Rand(random, 0, 6.28f), phase2 = Rand(random, 0, 6.28f);
            Vector2 dir = (b - a) / length, normal = new Vector2(-dir.y, dir.x);
            int count = Mathf.Max(8, Mathf.CeilToInt(length * 1.15f / 2));
            for (float damping = 1; damping > .2f; damping *= .5f)
            {
                var points = new Vector2[count];
                bool valid = true;
                for (int i = 0; i < count && valid; i++)
                {
                    float u = i / (float)(count - 1), envelope = Mathf.Sin(u * Mathf.PI);
                    float offset = envelope * (bow + sway * Mathf.Sin(u * Mathf.PI * 2 * swayFreq + phase)) + envelope * wiggle * Mathf.Sin(u * Mathf.PI * 2 * wiggleFreq + phase2);
                    points[i] = Vector2.Lerp(a, b, u) + normal * offset * damping;
                    // Trails skirt town centres and stay inside the state outline.
                    valid = InOutline(points[i] / size) && TownDistance(points[i]) > TownTrailClearance;
                }
                if (!valid) continue;
                var trail = new Trail { points = points, width = width, dirtRoad = dirtRoad };
                trails.Add(trail); IndexTrail(trail);
                return true;
            }
            return false;
        }

        void IndexTrail(Trail trail)
        {
            float halfWidth = trail.width * .5f;
            for (int i = 1; i < trail.points.Length; i++)
            {
                Vector2 a = trail.points[i - 1], b = trail.points[i];
                int index = trailSegments.Count;
                trailSegments.Add(new TrailSegment { a = a, b = b, half = halfWidth });
                float reach = halfWidth + TrailQueryReach;
                int x0 = Mathf.FloorToInt((Mathf.Min(a.x, b.x) - reach) / TrailCell), x1 = Mathf.FloorToInt((Mathf.Max(a.x, b.x) + reach) / TrailCell);
                int z0 = Mathf.FloorToInt((Mathf.Min(a.y, b.y) - reach) / TrailCell), z1 = Mathf.FloorToInt((Mathf.Max(a.y, b.y) + reach) / TrailCell);
                for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++)
                {
                    long key = (long)x << 32 | (uint)z;
                    if (!trailCells.TryGetValue(key, out var list)) trailCells[key] = list = new List<int>();
                    list.Add(index);
                }
            }
        }

        /// <summary>Distance from a point to the nearest trail edge: negative on a trail, large when none is near.</summary>
        float TrailEdgeDistance(Vector2 p)
        {
            long key = (long)Mathf.FloorToInt(p.x / TrailCell) << 32 | (uint)Mathf.FloorToInt(p.y / TrailCell);
            if (!trailCells.TryGetValue(key, out var list)) return TrailQueryReach;
            float best = TrailQueryReach;
            foreach (int index in list)
            {
                var s = trailSegments[index];
                Vector2 d = s.b - s.a;
                float u = Mathf.Clamp01(Vector2.Dot(p - s.a, d) / Mathf.Max(.0001f, d.sqrMagnitude));
                best = Mathf.Min(best, Vector2.Distance(s.a + d * u, p) - s.half);
            }
            return best;
        }

        Vector2 NearestRoadPoint(Vector2 p)
        {
            Vector2 best = p; float bestSq = float.MaxValue;
            foreach (var segment in segments)
            {
                Vector2 d = segment.b - segment.a;
                Vector2 q = segment.a + d * Mathf.Clamp01(Vector2.Dot(p - segment.a, d) / Mathf.Max(.01f, d.sqrMagnitude));
                float sq = (q - p).sqrMagnitude;
                if (sq < bestSq && TownDistance(q) > TownTrailClearance) { bestSq = sq; best = q; }
            }
            return best;
        }

        void BuildTrails()
        {
            if (trails.Count == 0) return;
            Transform root = Group("Dirt trail network", transform, Vector3.zero);
            var blend = Shader.Find("MadeInArizona/TrailBlend");
            var dirtRoad = TrailMaterial(blend, new Color(.52f, .40f, .27f), 8, 1f, 0, 0);
            var track = TrailMaterial(blend, new Color(.58f, .45f, .30f), 12, .85f, 1, 0);
            var footpath = TrailMaterial(blend, new Color(.64f, .52f, .36f), 12, 0, 0, 1);
            for (int t = 0; t < trails.Count; t++)
            {
                var trail = trails[t];
                // The ribbon extends past the trail edge so the shader can feather it into the terrain.
                // Extension covers the shader's widest noise reach (edge lobes, clumps and dust spill, ~2.8 x feather), so nothing is clipped.
                float halfWidth = trail.width * .5f, feather = Mathf.Clamp(trail.width * .22f, .7f, 1.4f);
                float outer = halfWidth + (blend ? feather * 2.9f + .6f : 0);
                int rows = trail.points.Length, columns = Mathf.Clamp(Mathf.CeilToInt(outer * 2 / 1.2f) + 1, 4, 12);
                var grid = new Vector3[rows, columns];
                var uv = new Vector4[rows * columns]; var shape = new Vector2[rows * columns];
                // Below the asphalt (0.1 m) and above the gravel shoulders; a per-trail offset avoids z-fighting at crossings.
                float lift = .045f + (t % 7) * .0035f, across = outer * 2 / (columns - 1) * .5f;
                float length = 0;
                for (int i = 1; i < rows; i++) length += Vector2.Distance(trail.points[i - 1], trail.points[i]);
                float along = 0;
                for (int i = 0; i < rows; i++)
                {
                    if (i > 0) along += Vector2.Distance(trail.points[i - 1], trail.points[i]);
                    Vector2 tangent = (trail.points[Mathf.Min(rows - 1, i + 1)] - trail.points[Mathf.Max(0, i - 1)]).normalized;
                    Vector2 normal = new Vector2(-tangent.y, tangent.x);
                    Vector2 left = trail.points[i] + normal * outer, right = trail.points[i] - normal * outer;
                    for (int j = 0; j < columns; j++)
                    {
                        float u = j / (float)(columns - 1);
                        grid[i, j] = TrailSurfacePoint(Vector2.Lerp(left, right, u), tangent, normal, across, .6f, lift);
                        uv[i * columns + j] = new Vector4(Mathf.Lerp(outer, -outer, u), along, halfWidth, length);
                        shape[i * columns + j] = new Vector2(outer, feather);
                    }
                }
                var material = trail.dirtRoad ? dirtRoad : trail.width > 3.6f ? track : footpath;
                TrailSurface(trail.dirtRoad ? "Graded dirt road" : trail.width > 3.6f ? "Two-track trail" : "Footpath", grid, uv, shape, material, root);
            }
        }

        /// <summary>Feathered, height-blended soil material; falls back to the opaque ground material without the shader.</summary>
        static Material TrailMaterial(Shader blend, Color color, int textureIndex, float ruts, float crown, float worn)
        {
            if (!blend) return GroundMaterial(color, textureIndex);
            var material = new Material(blend) { name = "MIA_Trail_" + textureIndex, enableInstancing = false };
            material.SetColor("_Tint", color);
            var set = GroundTextureSet.Load();
            if (set)
            {
                if (set.Diffuse(textureIndex)) material.SetTexture("_Diffuse", set.Diffuse(textureIndex));
                if (set.Normal(textureIndex)) material.SetTexture("_Normal", set.Normal(textureIndex));
                if (set.Height(textureIndex)) material.SetTexture("_Height", set.Height(textureIndex));
                if (set.Occlusion(textureIndex)) material.SetTexture("_AO", set.Occlusion(textureIndex));
            }
            material.SetFloat("_Ruts", ruts); material.SetFloat("_Crown", crown); material.SetFloat("_Worn", worn);
            return material;
        }

        static GameObject TrailSurface(string name, Vector3[,] grid, Vector4[] uv, Vector2[] shape, Material material, Transform parent)
        {
            int rows = grid.GetLength(0), cols = grid.GetLength(1);
            var vertices = new Vector3[rows * cols];
            for (int i = 0; i < rows; i++) for (int j = 0; j < cols; j++) vertices[i * cols + j] = grid[i, j];
            var triangles = new int[(rows - 1) * (cols - 1) * 6]; int t = 0;
            for (int i = 0; i < rows - 1; i++) for (int j = 0; j < cols - 1; j++)
            { int a = i * cols + j, b = a + 1, c = a + cols, d = c + 1; triangles[t++] = a; triangles[t++] = c; triangles[t++] = b; triangles[t++] = b; triangles[t++] = c; triangles[t++] = d; }
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(parent, false);
            var mesh = new Mesh { name = name }; mesh.vertices = vertices; mesh.SetUVs(0, uv); mesh.SetUVs(1, shape); mesh.triangles = triangles;
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.AddComponent<GeneratedMeshOwner>().Mesh = mesh;
            return go;
        }

        /// <summary>Trail vertex on the highest nearby terrain, so the unpaved surface never sinks into a crease.</summary>
        Vector3 TrailSurfacePoint(Vector2 p, Vector2 tangent, Vector2 normal, float across, float along, float lift)
        {
            float h = HeightAt(new Vector3(p.x, 0, p.y));
            foreach (Vector2 o in new[] { tangent * along, -tangent * along, normal * across, -normal * across })
            { Vector2 q = p + o; h = Mathf.Max(h, HeightAt(new Vector3(q.x, 0, q.y))); }
            return new Vector3(p.x, h + lift, p.y);
        }

        static float Rand(System.Random random, float a, float b) => Mathf.Lerp(a, b, (float)random.NextDouble());
    }
}
