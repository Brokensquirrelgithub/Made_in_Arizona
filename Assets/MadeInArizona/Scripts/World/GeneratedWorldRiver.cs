using UnityEngine;
using static MadeInArizona.WorldArt;

namespace MadeInArizona
{
    /// <summary>
    /// The Salt River. Its surface follows one smooth, grade-limited level along the course, so the water never steps
    /// up and down with the terrain. The channel is carved after roads and towns are graded, so no embankment dams it,
    /// and roads cross on bridge decks. The surface uses the Simple Water Shader (a copy corrected for the
    /// orthographic camera); a wet gravel bed is painted into the terrain underneath.
    /// </summary>
    public sealed partial class GeneratedWorld
    {
        static readonly float[] BankSamples = { -2.6f, -1.6f, 1.6f, 2.6f };
        const float RiverStep = 4, RiverSurfaceDrop = 2.2f, RiverMaxGrade = .07f, RiverBedDepth = 2f, RiverShelf = .6f;
        float[] riverLevels, riverReach;

        // ---- Roads and the river ----
        // Straight town-to-town lines used to run along the river for hundreds of metres, and their embankments cut
        // the water into pieces. Roads now keep clear of the river corridor, and a road joining towns on opposite
        // banks holds its side until one steep, short crossing.
        float RouteRiverClearance => riverWidth * 1.8f + 24;

        /// <summary>A route's point at u: the planned line, pushed out of the river corridor.</summary>
        Vector2 RoutePoint(Route r, float u)
        {
            Vector2 p = BaseRoutePoint(r, u);
            if (riverWidth <= 0) return p;
            float centre = RiverX(p.y), offset = p.x - centre, side = Mathf.Sign(r.a.x - RiverX(r.a.y)), clearance = RouteRiverClearance;
            float start = centre + side * SoftFloor(side * offset, clearance);
            if (r.cross < 0) return new Vector2(start, p.y);
            float end = centre - side * SoftFloor(-side * offset, clearance);
            return new Vector2(Mathf.Lerp(start, end, Mathf.SmoothStep(0, 1, (u - r.cross) / r.crossWindow + .5f)), p.y);
        }

        /// <summary>Smooth max(x, floor): equals x well above the floor and never drops below it.</summary>
        static float SoftFloor(float x, float floor)
        {
            const float k = 8; float z = (x - floor) / k;
            return floor + (z > 12 ? z * k : k * Mathf.Log(1 + Mathf.Exp(z)));
        }

        /// <summary>For towns on opposite banks: where the route crosses (nearest mid-route) and over how much of it.</summary>
        void PlanRiverCrossing(ref Route r)
        {
            if (riverWidth <= 0) return;
            float sa = r.a.x - RiverX(r.a.y), sb = r.b.x - RiverX(r.b.y);
            if (sa * sb >= 0) return;
            const int steps = 400;
            var offsets = new float[steps + 1];
            for (int i = 0; i <= steps; i++) { Vector2 p = BaseRoutePoint(r, i / (float)steps); offsets[i] = p.x - RiverX(p.y); }
            int best = -1;
            for (int i = 1; i <= steps; i++)
                if (Mathf.Sign(offsets[i]) != Mathf.Sign(offsets[i - 1]) && (best < 0 || Mathf.Abs(i - steps / 2) < Mathf.Abs(best - steps / 2))) best = i;
            if (best < 0) best = steps / 2;
            float clearance = RouteRiverClearance; int first = best, last = best;
            while (first > 0 && Mathf.Abs(offsets[first - 1]) < clearance) first--;
            while (last < steps && Mathf.Abs(offsets[last]) < clearance) last++;
            float length = Mathf.Max(1, Vector2.Distance(r.a, r.b));
            r.cross = (best - .5f) / steps;
            // Long enough for gentle bends (tighter S-curves folded the road's inside edge over itself), short
            // enough that the bridge still crosses at a clear angle.
            r.crossWindow = Mathf.Clamp((last - first) / (float)steps, 140 / length, 240 / length);
        }

        /// <summary>Plans the water level and bank reach along the course. Needs the elevation levels.</summary>
        void PlanRiver()
        {
            riverLevels = riverReach = null;
            if (riverWidth <= 0) return;
            int n = Mathf.CeilToInt(size / RiverStep) + 1;
            // Surface below the lowest ground across the channel and its banks: where the river runs along a slope, a
            // level taken at the centre line floated the water over the low bank.
            var cut = new float[n]; var high = new float[n];
            for (int i = 0; i < n; i++)
            {
                float z = -half + i * RiverStep, x = RiverX(z), w = RiverHalfWidth(z), low = RawHeight(x, z), top = low;
                foreach (float k in BankSamples) { float h = RawHeight(x + k * w, z); low = Mathf.Min(low, h); top = Mathf.Max(top, h); }
                cut[i] = low - RiverSurfaceDrop; high[i] = top;
            }
            var level = Smooth(Smooth(cut, 12), 12);
            // Smoothing lifts the surface over dips in the ground; the water must stay below the ground it is cut from,
            // or bridge decks resting on that ground end up underwater.
            for (int i = 0; i < n; i++) level[i] = Mathf.Min(level[i], cut[i]);
            // Grade limit that only ever lowers the surface: across a level ramp the river cuts a short gorge
            // instead of the water running up and down the slope.
            float step = RiverMaxGrade * RiverStep;
            for (int i = 1; i < n; i++) level[i] = Mathf.Min(level[i], level[i - 1] + step);
            for (int i = n - 2; i >= 0; i--) level[i] = Mathf.Min(level[i], level[i + 1] + step);
            riverLevels = level;
            // Deeper cuts get wider banks so they stay under the drivable grade.
            var reach = new float[n];
            for (int i = 0; i < n; i++)
            {
                float z = -half + i * RiverStep, w = RiverHalfWidth(z);
                float rise = Mathf.Max(0, high[i] - (level[i] - RiverShelf)) + 3;
                reach[i] = RiverBed(z) + Mathf.Max(w * 1.35f, rise / .3f);
            }
            riverReach = Smooth(reach, 6);
        }

        static float[] Smooth(float[] values, int radius)
        {
            var result = new float[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                float sum = 0; int count = 0;
                for (int k = Mathf.Max(0, i - radius); k <= Mathf.Min(values.Length - 1, i + radius); k++) { sum += values[k]; count++; }
                result[i] = sum / count;
            }
            return result;
        }

        static float Along(float[] values, float z, float half)
        {
            float f = Mathf.Clamp((z + half) / RiverStep, 0, values.Length - 1.001f); int i = (int)f;
            return Mathf.Lerp(values[i], values[i + 1], f - i);
        }

        /// <summary>Water surface height at a point along the river's course.</summary>
        public float RiverLevel(float z) => riverLevels == null ? float.MinValue : Along(riverLevels, z, half);
        float RiverReach(float z) => riverReach == null ? 0 : Along(riverReach, z, half);
        float RiverHalfWidth(float z) => riverWidth * (.86f + .12f * Mathf.Sin(z * .035f) + .07f * Mathf.Sin(z * .19f + SeedOffset(seed, 1, SinePeriod)));
        /// <summary>
        /// Half-width of the channel bed. At least 1.6 terrain cells: a narrower bed fell between grid vertices, and on
        /// large maps the steep banks of a deep cut then filled the channel, so the river ended at a ridge.
        /// </summary>
        float RiverBed(float z) => Mathf.Max(RiverHalfWidth(z) * .85f, size / (Chunks * Cells) * 1.6f);
        /// <summary>Inside the carved river corridor (channel plus banks).</summary>
        bool OverRiver(Vector2 p) => riverLevels != null && Mathf.Abs(p.x - RiverX(p.y)) < RiverReach(p.y);

        /// <summary>Carves the channel into already-graded ground: a flat-bottomed bed, then banks rising to the ground.</summary>
        float CarveRiver(float x, float z, float ground)
        {
            if (riverLevels == null) return ground;
            float d = Mathf.Abs(x - RiverX(z)), reach = RiverReach(z);
            if (d >= reach) return ground;
            float level = RiverLevel(z), inner = RiverBed(z), shelf = level - RiverShelf;
            if (d < inner) { float t = d / inner; return shelf - (RiverBedDepth - RiverShelf) * (1 - t * t); }
            float u = (d - inner) / (reach - inner);
            float bank = Mathf.Lerp(shelf, ground, Mathf.SmoothStep(0, 1, u));
            // A low levee guarantees the bank clears the water even where the ground beyond is lower, so the river
            // never spills sideways over it.
            float crest = level + .8f;
            float levee = u < .4f ? Mathf.Lerp(shelf, crest, Mathf.SmoothStep(0, 1, u / .4f))
                : u < .6f ? crest : Mathf.Lerp(crest, Mathf.Min(crest, ground), Mathf.SmoothStep(0, 1, (u - .6f) / .4f));
            // Smooth maximum: a hard max left a crease where the levee meets the bank, enough to lift a wheel at speed.
            // The blend width tapers to zero at both ends of the bank, so the profile stays continuous there.
            float gap = bank - levee, blend = Mathf.Sin(Mathf.PI * u);
            return (bank + levee + Mathf.Sqrt(gap * gap + blend * blend)) * .5f;
        }

        /// <summary>Carve inputs at a point on the centre line, for the capture-mode channel audit.</summary>
        public string RiverDebug(float z)
        {
            float x = RiverX(z);
            string text = $"x={x:F1} reach={RiverReach(z):F1} inner={RiverBed(z):F1} raw={RawHeight(x, z):F1} ground={GroundHeight(x, z):F1} carved={HeightInternal(x, z):F1} sampled={SampleHeight(new Vector3(x, 0, z)):F1}";
            if (heights == null) return text;
            // The grid vertices around the point: stored height against a fresh evaluation.
            int grid = Chunks * Cells; float step = size / grid;
            int gx = Mathf.FloorToInt((x + half) / step), gz = Mathf.FloorToInt((z + half) / step);
            for (int dz = 0; dz <= 1; dz++) for (int dx = 0; dx <= 1; dx++)
            {
                int vx = Mathf.Clamp(gx + dx, 0, grid), vz = Mathf.Clamp(gz + dz, 0, grid);
                float wx = -half + vx * step, wz = -half + vz * step;
                text += $" | v({wx:F1},{wz:F1}) d={Mathf.Abs(wx - RiverX(wz)):F1} stored={heights[vx, vz]:F1} fresh={HeightInternal(wx, wz):F1}";
            }
            return text;
        }

        /// <summary>Terrain vertex colour alpha: 1 dry ground, 0.5 at the waterline, 0 at the deepest bed.</summary>
        float RiverBedMask(Vector3 v)
        {
            if (riverLevels == null || Mathf.Abs(v.x - RiverX(v.z)) >= RiverReach(v.z)) return 1;
            float below = RiverLevel(v.z) - v.y;
            return below >= 0 ? .5f - .5f * Mathf.Clamp01(below / RiverBedDepth) : .5f + .5f * Mathf.Clamp01(-below / 1.2f);
        }

        Material RiverWaterMaterial()
        {
            var template = Resources.Load<Material>("RiverWater");
            if (template) return new Material(template) { name = "Salt River water" };
            var flow = new Material(Shader.Find("MadeInArizona/FlowRiver")); flow.SetTexture("_BumpMap", SurfaceNormal());
            return flow;
        }

        void BuildRiver()
        {
            if (riverLevels == null) return;
            int rows = Mathf.CeilToInt(size / 3) + 1; const int columns = 7;
            var vertices = new Vector3[rows * columns]; var uv = new Vector2[vertices.Length];
            var centre = new Vector3[rows];
            for (int i = 0; i < rows; i++)
            {
                float z = Mathf.Lerp(-half * .93f, half * .94f, i / (float)(rows - 1)), x = RiverX(z), level = RiverLevel(z);
                Vector2 tangent = new Vector2(RiverX(z + 1) - RiverX(z - 1), 2).normalized, n = new Vector2(-tangent.y, tangent.x);
                // Each edge reaches just past the waterline on its own bank, so it always tucks under the ground.
                float left = WaterEdge(x, z, n, level), right = WaterEdge(x, z, -n, level);
                for (int j = 0; j < columns; j++)
                {
                    float across = Mathf.Lerp(left, -right, j / (float)(columns - 1));
                    var p = new Vector3(x + n.x * across, level, z + n.y * across);
                    vertices[i * columns + j] = p; uv[i * columns + j] = new Vector2(p.x, p.z) / 7f;
                }
                centre[i] = new Vector3(x, level, z);
            }
            var triangles = new int[(rows - 1) * (columns - 1) * 6]; int t = 0;
            for (int i = 0; i < rows - 1; i++) for (int j = 0; j < columns - 1; j++)
            {
                int a = i * columns + j, b = a + 1, c = a + columns, d = c + 1;
                triangles[t++] = a; triangles[t++] = c; triangles[t++] = b; triangles[t++] = b; triangles[t++] = c; triangles[t++] = d;
            }
            // Front faces must point up; flip if the column order runs the other way.
            if (Vector3.Cross(vertices[columns] - vertices[0], vertices[1] - vertices[0]).y < 0)
                for (int k = 0; k < triangles.Length; k += 3) { int swap = triangles[k + 1]; triangles[k + 1] = triangles[k + 2]; triangles[k + 2] = swap; }
            var mesh = new Mesh { name = "Salt River", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32, vertices = vertices, uv = uv, triangles = triangles };
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
            var go = new GameObject("Salt River", typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(roads, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<GeneratedMeshOwner>().Mesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>(); renderer.sharedMaterial = water;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            for (int i = 2; i < rows - 2; i += 30) for (int side = -1; side <= 1; side += 2)
            {
                Vector3 p = centre[i]; p.x += side * (RiverReach(p.z) + R(2, 8)); p.z += R(-6, 6); p.y = HeightInternal(p.x, p.z);
                Tree(Detail(p), p, R(5, 9));
            }
        }

        /// <summary>Distance from the centre line, along one bank normal, where the carved ground clears the water.</summary>
        float WaterEdge(float x, float z, Vector2 normal, float level)
        {
            float inner = RiverBed(z), reach = RiverReach(z);
            for (float d = inner; d < reach; d += .75f)
                if (SampleHeight(new Vector3(x + normal.x * d, 0, z + normal.y * d)) > level + .3f) return d + 1;
            return reach;
        }

        /// <summary>Bridge parapets, deck fascia and piers wherever a road's deck runs clear of the carved channel.</summary>
        void BuildBridges(Vector3[,] surface, Vector3[] centers, Vector2[] normals)
        {
            if (riverLevels == null) return;
            int count = centers.Length, start = -1;
            for (int i = 0; i <= count; i++)
            {
                bool deck = i < count && OverRiver(XZ(centers[i])) && centers[i].y - SampleHeight(centers[i]) > .9f;
                if (deck && start < 0) start = i;
                if (!deck && start >= 0) { BridgeSpan(surface, centers, normals, Mathf.Max(0, start - 3), Mathf.Min(count - 1, i + 2)); start = -1; }
            }
        }

        void BridgeSpan(Vector3[,] surface, Vector3[] centers, Vector2[] normals, int first, int last)
        {
            var concrete = new Color(.58f, .55f, .50f); int columns = surface.GetLength(1);
            Transform bridge = Group("Salt River bridge", roads, Vector3.zero);
            for (int i = first; i < last; i += 3)
            {
                int j = Mathf.Min(last, i + 3);
                for (int side = 0; side < 2; side++)
                {
                    float outward = side == 0 ? 1 : -1;
                    Vector3 a = surface[i, side == 0 ? 0 : columns - 1], b = surface[j, side == 0 ? 0 : columns - 1];
                    Vector3 offsetA = new Vector3(normals[i].x, 0, normals[i].y) * outward, offsetB = new Vector3(normals[j].x, 0, normals[j].y) * outward;
                    a += offsetA * .22f; b += offsetB * .22f;
                    Vector3 along = b - a; float length = along.magnitude + .3f;
                    if (length < .5f) continue;
                    Quaternion facing = Quaternion.LookRotation(along.normalized, Vector3.up);
                    // Parapet: solid, so a car cannot drive off the deck into the river.
                    Box("Bridge parapet", bridge, (a + b) * .5f + Vector3.up * .45f, new Vector3(.34f, .9f, length), concrete, true).transform.localRotation = facing;
                    // Fascia hides the deck's edge from the overhead camera.
                    Box("Bridge deck fascia", bridge, (a + b) * .5f + Vector3.down * .55f, new Vector3(.3f, 1.1f, length), concrete * .82f).transform.localRotation = facing;
                }
            }
            for (int i = first + 4; i < last - 3; i += 8)
            {
                Vector3 top = centers[i]; float ground = SampleHeight(top), height = top.y - .35f - (ground - .6f);
                if (height < 2.2f) continue;
                Vector2 n = normals[i]; Quaternion facing = Quaternion.LookRotation(new Vector3(n.y, 0, -n.x), Vector3.up);
                var pier = Box("Bridge pier", bridge, new Vector3(top.x, (top.y - .35f + ground - .6f) * .5f, top.z), new Vector3(Mathf.Max(6, (surface[i, 0] - surface[i, columns - 1]).magnitude * .8f), height, 1.1f), concrete * .9f, true);
                pier.transform.localRotation = facing;
            }
        }
    }
}
