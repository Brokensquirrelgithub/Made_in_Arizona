using System.Collections.Generic;
using UnityEngine;

namespace MadeInArizona
{
    /// <summary>
    /// Three broad elevation levels. Level boundaries are mostly gentle ramps; some stretches become cliff walls.
    /// Cliffs never form near towns, roads or the river, never ring small rises (no mesas), and a flood fill from the
    /// starting town opens a ramp into any area that cliffs would otherwise seal off.
    /// </summary>
    public sealed partial class GeneratedWorld
    {
        const float LevelLow = .42f, LevelHigh = .60f, MaxDriveGrade = .65f; // tan(33°)
        readonly List<Vector2> routeLineA = new List<Vector2>(), routeLineB = new List<Vector2>();
        readonly List<Vector3> rampOpenings = new List<Vector3>(); // xz centre, radius
        float levelHeight;
        public int RampOpenings => rampOpenings.Count;
        public int UnreachableVertices { get; private set; }

        /// <summary>Called once towns and routes are planned, before any height is sampled for them.</summary>
        void PrepareElevation()
        {
            levelHeight = Mathf.Clamp(amp * .13f, 0, 14);
            routeLineA.Clear(); routeLineB.Clear();
            foreach (var route in routes)
                for (int i = 0; i < 48; i++) { routeLineA.Add(RoutePoint(route, i / 48f)); routeLineB.Add(RoutePoint(route, (i + 1) / 48f)); }
        }

        float LevelField(float nx, float nz) => Noise(nx * 2.1f + 31, nz * 2.1f - 17) * .72f + Noise(nx * 5.3f - 4, nz * 5.3f + 9) * .28f;

        /// <summary>Height added by the level terraces at a world position.</summary>
        float LevelOffset(float x, float z)
        {
            if (levelHeight <= 0) return 0;
            float nx = x / size, nz = z / size, e = 2f / size;
            float t = LevelField(nx, nz);
            // Field gradient in normalised units; the floor keeps rises around local maxima gentle instead of popping up.
            float gx = (LevelField(nx + e, nz) - LevelField(nx - e, nz)) / (2 * e), gz = (LevelField(nx, nz + e) - LevelField(nx, nz - e)) / (2 * e);
            float gradient = Mathf.Sqrt(gx * gx + gz * gz), metres = size / Mathf.Max(gradient, .6f);
            float cliff = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.56f, .64f, Noise(nx * 9 + 71, nz * 9 - 23)));
            // Only well-defined boundaries become cliffs: flat saddles and small rises stay ramps (no mesas).
            cliff *= Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.8f, 1.4f, gradient));
            if (cliff > 0) cliff *= CliffAllowance(new Vector2(x, z));
            float ramp = Mathf.Max(18, levelHeight * 2.4f), wall = 1.2f;
            float halfLength = Mathf.Lerp(ramp, wall, cliff);
            return levelHeight * (Step((t - LevelLow) * metres, halfLength) + Step((t - LevelHigh) * metres, halfLength));
        }

        static float Step(float distance, float halfLength) { float s = Mathf.Clamp01(distance / (2 * halfLength) + .5f); return s * s * (3 - 2 * s); }

        /// <summary>0 where a cliff must not form (towns, roads, river, opened ramps), 1 in open country.</summary>
        float CliffAllowance(Vector2 p)
        {
            float allow = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(140, 200, TownDistance(p)));
            if (allow <= 0) return 0;
            float road = float.MaxValue;
            for (int i = 0; i < routeLineA.Count; i++)
            {
                Vector2 a = routeLineA[i], d = routeLineB[i] - a;
                float u = Mathf.Clamp01(Vector2.Dot(p - a, d) / Mathf.Max(.01f, d.sqrMagnitude));
                road = Mathf.Min(road, (a + d * u - p).sqrMagnitude);
            }
            allow *= Mathf.SmoothStep(0, 1, Mathf.InverseLerp(45, 90, Mathf.Sqrt(road)));
            if (riverWidth > 0) allow *= Mathf.SmoothStep(0, 1, Mathf.InverseLerp(riverWidth * 2.2f + 40, riverWidth * 2.2f + 90, Mathf.Abs(p.x - RiverX(p.y))));
            foreach (var opening in rampOpenings)
                allow *= Mathf.SmoothStep(0, 1, Mathf.InverseLerp(opening.z, opening.z + 25, Vector2.Distance(p, new Vector2(opening.x, opening.y))));
            return allow;
        }

        /// <summary>
        /// Flood fill from the first town over drivable grades. Each sealed-off area gets a ramp opening at the cliff
        /// nearest the reachable region, and the affected heights are rebuilt, until everything connects.
        /// </summary>
        void EnsureReachable(float step)
        {
            int grid = Chunks * Cells;
            for (int pass = 0; pass < 12; pass++)
            {
                var reached = Flood(grid, step, out int unreachable);
                UnreachableVertices = 0;
                if (unreachable == 0) return;
                // Open one ramp per sealed component, where it meets the reachable region.
                var seen = new bool[grid + 1, grid + 1]; int opened = 0;
                for (int z = 0; z <= grid; z++) for (int x = 0; x <= grid; x++)
                {
                    if (reached[x, z] || seen[x, z] || !InsideVertex(x, z, step)) continue;
                    var component = Component(x, z, reached, seen, grid, step, out Vector2Int border);
                    // Tiny patches of naturally steep ground are not areas worth a ramp.
                    if (component < 12 || border.x < 0) continue;
                    UnreachableVertices += component;
                    var opening = new Vector3(-half + border.x * step, -half + border.y * step, Mathf.Max(30, levelHeight * 3));
                    // A repeat means cliffs are not what seals this area (e.g. the state outline); stop trying there.
                    if (rampOpenings.Exists(o => Vector2.Distance(new Vector2(o.x, o.y), new Vector2(opening.x, opening.y)) < o.z)) { Debug.LogWarning("MIA_ELEVATION sealed area not caused by cliffs at " + opening); continue; }
                    rampOpenings.Add(opening);
                    opened++;
                }
                if (opened == 0) return;
                UnreachableVertices = 0;
                for (int z = 0; z <= grid; z++) for (int x = 0; x <= grid; x++) heights[x, z] = HeightInternal(-half + x * step, -half + z * step);
            }
        }

        /// <summary>Cliff base positions (steepest grid edges), for review captures; downhill points away from the wall.</summary>
        public List<(Vector3 position, Vector2 downhill, float rise)> FindCliffs(int count)
        {
            var found = new List<(Vector3, Vector2, float)>();
            if (heights == null) return found;
            int grid = Chunks * Cells; float step = size / grid;
            for (int z = 2; z < grid - 2; z += 2) for (int x = 2; x < grid - 2; x += 2)
            {
                float dx = heights[x + 1, z] - heights[x - 1, z], dz = heights[x, z + 1] - heights[x, z - 1];
                float grade = Mathf.Sqrt(dx * dx + dz * dz) / (2 * step);
                if (grade < 1.1f || !InsideVertex(x, z, step)) continue;
                Vector2 down = -new Vector2(dx, dz).normalized; Vector3 p = new Vector3(-half + x * step, 0, -half + z * step);
                if (found.Exists(f => Vector3.Distance(f.Item1, p) < 150)) continue;
                p += new Vector3(down.x, 0, down.y) * 6; p.y = HeightAt(p);
                found.Add((p, down, grade));
                if (found.Count >= count) return found;
            }
            return found;
        }

        bool InsideVertex(int x, int z, float step) => InOutline(new Vector2((-half + x * step) / size, (-half + z * step) / size));

        bool[,] Flood(int grid, float step, out int unreachable)
        {
            var reached = new bool[grid + 1, grid + 1];
            var queue = new Queue<Vector2Int>();
            int sx = Mathf.Clamp(Mathf.RoundToInt((Towns[0].x + half) / step), 0, grid), sz = Mathf.Clamp(Mathf.RoundToInt((Towns[0].z + half) / step), 0, grid);
            reached[sx, sz] = true; queue.Enqueue(new Vector2Int(sx, sz));
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                for (int k = 0; k < 4; k++)
                {
                    int nx = c.x + (k == 0 ? 1 : k == 1 ? -1 : 0), nz = c.y + (k == 2 ? 1 : k == 3 ? -1 : 0);
                    if (nx < 0 || nz < 0 || nx > grid || nz > grid || reached[nx, nz] || !InsideVertex(nx, nz, step)) continue;
                    if (Mathf.Abs(heights[nx, nz] - heights[c.x, c.y]) > MaxDriveGrade * step) continue;
                    reached[nx, nz] = true; queue.Enqueue(new Vector2Int(nx, nz));
                }
            }
            unreachable = 0;
            for (int z = 0; z <= grid; z++) for (int x = 0; x <= grid; x++) if (!reached[x, z] && InsideVertex(x, z, step)) unreachable++;
            return reached;
        }

        /// <summary>Marks one sealed component; returns its size and the member vertex that borders the reachable region.</summary>
        int Component(int x0, int z0, bool[,] reached, bool[,] seen, int grid, float step, out Vector2Int border)
        {
            var queue = new Queue<Vector2Int>(); queue.Enqueue(new Vector2Int(x0, z0)); seen[x0, z0] = true;
            int count = 0; border = new Vector2Int(-1, -1);
            while (queue.Count > 0)
            {
                var c = queue.Dequeue(); count++;
                for (int k = 0; k < 4; k++)
                {
                    int nx = c.x + (k == 0 ? 1 : k == 1 ? -1 : 0), nz = c.y + (k == 2 ? 1 : k == 3 ? -1 : 0);
                    if (nx < 0 || nz < 0 || nx > grid || nz > grid || !InsideVertex(nx, nz, step)) continue;
                    if (reached[nx, nz]) { if (border.x < 0) border = c; continue; }
                    if (seen[nx, nz]) continue;
                    seen[nx, nz] = true; queue.Enqueue(new Vector2Int(nx, nz));
                }
            }
            return count;
        }
    }
}
