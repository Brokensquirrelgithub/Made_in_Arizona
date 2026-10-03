using System.Collections.Generic;
using UnityEngine;
using static MadeInArizona.WorldArt;

namespace MadeInArizona
{
    /// <summary>
    /// Large impassable landforms (mesas, buttes, cliff walls, boulder piles) that give the open country shape and
    /// something to hide behind, plus medium rocks that stop gunfire. Landforms are planned late, after roads, towns and
    /// points of interest, so they never sit on a road, in a town or beside an objective, and before trails, which bend
    /// around them. Cover rocks come after trails and sit beside them, often in loose rings around objectives.
    /// Each landform is a set of circular footprints: navigation, scenery, spawns and the map all read those. Overlapping
    /// cliff models also ring every edge of the state outline, with smaller stones along the foot of each mountain.
    /// Procedural placeholders stand in until models are assigned in the <see cref="DesertLandformCatalog"/>.
    /// </summary>
    public sealed partial class GeneratedWorld
    {
        public enum LandformKind { Mesa, Butte, Cliff, Boulders, Cover }
        sealed class Landform { public LandformKind kind; public Vector2 center, inward; public float reach, height, yaw; public int first, count; public bool boundary; }
        struct Footprint { public Vector2 center; public float radius, height; public bool cover; public int nextSpine; }

        /// <summary>A conservative first pass: 15-25 landforms on the default 1.6 km map, proportionally fewer on small maps.</summary>
        public const int MinLandforms = 15, MaxLandforms = 25;
        const float LandformRoadClearance = 24, LandformTownClearance = 130, LandformPinClearance = 70, LandformSpacing = 50,
            LandformRiverClearance = 30, TrailLandformClearance = 6, JunctionLandformClearance = 40;
        const float CoverRoadClearance = 12, CoverTownClearance = 75, CoverObjectiveClearance = 12, CoverGap = 8;
        const float ObstacleCell = 48, ObstacleQueryReach = 48;
        readonly List<Landform> landforms = new List<Landform>();
        readonly List<Landform> boundaryChains = new List<Landform>();
        readonly List<Footprint> footprints = new List<Footprint>();
        readonly Dictionary<long, List<int>> footprintCells = new Dictionary<long, List<int>>();
        Material landformMaterial;

        public int LandformCount { get; private set; }
        public int CoverCount { get; private set; }
        public int BoundaryRockCount { get; private set; }
        public int MountainBarrierCount { get; private set; }
        public int BaseRockCount { get; private set; }
        /// <summary>Landforms and cover rocks in placement order (landforms first), for review captures and tests.</summary>
        public int LandformEntries => landforms.Count;
        public Vector3 LandformCenter(int index, out LandformKind kind, out float reach)
        {
            var landform = landforms[index]; kind = landform.kind; reach = landform.reach;
            var p = new Vector3(landform.center.x, 0, landform.center.y); p.y = SampleHeight(p); return p;
        }

        /// <summary>
        /// Regression check of the placement rules: null when they all hold, otherwise the first one broken. Covers road,
        /// town, point-of-interest and junction clearance, trails, navigation blocking and town-to-town routes.
        /// </summary>
        public string LandformIssue()
        {
            foreach (var landform in landforms)
            {
                bool cover = landform.kind == LandformKind.Cover;
                for (int k = landform.first; k < landform.first + landform.count; k++)
                {
                    var f = footprints[k];
                    if (RoadDistance(f.center) < f.radius + (cover ? CoverRoadClearance : LandformRoadClearance) - .01f) return landform.kind + " beside a road at " + f.center;
                }
                if (TownDistance(landform.center) < landform.reach + (cover ? CoverTownClearance : LandformTownClearance) - .01f) return landform.kind + " crowds a town at " + landform.center;
                foreach (var pin in Pins)
                    if (pin.kind != "town" && Vector2.Distance(landform.center, XZ(pin.position)) < landform.reach + (cover ? CoverObjectiveClearance : LandformPinClearance) - .01f)
                        return landform.kind + " crowds " + pin.label + " at " + landform.center;
            }
            foreach (var junction in TrailJunctions)
                if (ObstacleDistance(XZ(junction)) < CoverObjectiveClearance - .01f) return "Obstacle crowds a trail junction at " + junction;
            foreach (var trail in trails)
                foreach (var point in trail.points)
                    if (ObstacleDistance(point) < 0) return "Trail runs through an obstacle at " + point;
            BuildNavigation();
            // Routes go round landforms. Cover rocks are smaller than a navigation cell; AI steering avoids those locally.
            foreach (var landform in landforms)
                for (int k = landform.first; k < landform.first + landform.count && landform.kind != LandformKind.Cover; k++)
                {
                    var f = footprints[k];
                    if (navCost != null && f.radius >= 4 && navCost[NavIndex(new Vector3(f.center.x, 0, f.center.y))] > 0) return "Navigation crosses " + landform.kind + " at " + f.center;
                }
            var route = new List<Vector3>();
            for (int i = 1; i < Towns.Count; i++)
                if (!FindPath(Towns[0], Towns[i], route)) return "No route from the starter town to town " + (i + 1);
            if (footprints.Count > 0)
            {
                var inside = new Vector3(footprints[0].center.x, 0, footprints[0].center.y);
                if (ObstacleDistance(ClearOfObstacles(inside)) < 2.9f) return "Spawn push-out left a point inside an obstacle";
            }
            for (int edge = 0; edge < Outline.Length; edge++)
            {
                Vector2 a = Outline[edge] * size, b = Outline[(edge + 1) % Outline.Length] * size;
                int samples = Mathf.CeilToInt(Vector2.Distance(a, b) / 10);
                for (int i = 0; i <= samples; i++)
                    if (ObstacleDistance(Vector2.Lerp(a, b, i / (float)samples)) >= 0)
                        return "Mountain boundary has a gap on edge " + edge;
            }
            return null;
        }

        /// <summary>
        /// Distance from a point to the nearest landform or cover rock footprint: negative inside one, and capped at
        /// 48 m (every caller tests a shorter radius).
        /// </summary>
        public float ObstacleDistance(Vector2 p)
        {
            if (!footprintCells.TryGetValue(ObstacleKey(Mathf.FloorToInt(p.x / ObstacleCell), Mathf.FloorToInt(p.y / ObstacleCell)), out var list)) return ObstacleQueryReach;
            float best = ObstacleQueryReach;
            foreach (int i in list) best = Mathf.Min(best, Vector2.Distance(p, footprints[i].center) - footprints[i].radius);
            return best;
        }
        public float ObstacleDistance(Vector3 p) => ObstacleDistance(XZ(p));
        /// <summary>As <see cref="ObstacleDistance(Vector2)"/>, ignoring cover rocks.</summary>
        float LandformDistance(Vector2 p)
        {
            if (!footprintCells.TryGetValue(ObstacleKey(Mathf.FloorToInt(p.x / ObstacleCell), Mathf.FloorToInt(p.y / ObstacleCell)), out var list)) return ObstacleQueryReach;
            float best = ObstacleQueryReach;
            foreach (int i in list) if (!footprints[i].cover) best = Mathf.Min(best, Vector2.Distance(p, footprints[i].center) - footprints[i].radius);
            return best;
        }

        /// <summary>
        /// Moves a point (a spawn or pickup) out of any landform or cover rock, keeping <paramref name="margin"/> metres
        /// of open ground; its height above the terrain is kept.
        /// </summary>
        public Vector3 ClearOfObstacles(Vector3 p, float margin = 3)
        {
            float lift = p.y - SampleHeight(p);
            Vector2 original = XZ(p);
            bool moved = false;
            // A point can leave one circle of a long ridge only to enter the next one.
            for (int pass = 0; pass < 24; pass++)
            {
                Vector2 q = XZ(p);
                if (!footprintCells.TryGetValue(ObstacleKey(Mathf.FloorToInt(q.x / ObstacleCell), Mathf.FloorToInt(q.y / ObstacleCell)), out var list)) break;
                int nearest = -1; float worst = margin;
                foreach (int i in list) { float d = Vector2.Distance(q, footprints[i].center) - footprints[i].radius; if (d < worst) { worst = d; nearest = i; } }
                if (nearest < 0) break;
                Vector2 away = q - footprints[nearest].center;
                if (away.sqrMagnitude < .0001f) away = Vector2.right;
                q = footprints[nearest].center + away.normalized * (footprints[nearest].radius + margin + .1f);
                p = new Vector3(q.x, p.y, q.y); moved = true;
            }
            // Overlapping circles can bounce the projection between adjacent ridge rocks.
            // In that case, choose the nearest clear point around the original position.
            if (ObstacleDistance(XZ(p)) < margin)
            {
                bool found = false;
                for (float distance = Mathf.Max(4, margin); distance <= 192 && !found; distance += 4)
                    for (int angle = 0; angle < 24; angle++)
                    {
                        float radians = angle * Mathf.PI * 2 / 24;
                        Vector2 candidate = original + new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * distance;
                        if (!InOutline(candidate / size) || ObstacleDistance(candidate) < margin + .1f) continue;
                        p = new Vector3(candidate.x, p.y, candidate.y); moved = found = true; break;
                    }
            }
            if (moved) p.y = SampleHeight(p) + lift;
            return p;
        }

        static long ObstacleKey(int x, int z) => (long)x << 32 | (uint)z;
        int AddFootprint(Vector2 center, float radius, float height, bool cover = false)
        {
            int index = footprints.Count;
            footprints.Add(new Footprint { center = center, radius = radius, height = height, cover = cover, nextSpine = -1 });
            float reach = radius + ObstacleQueryReach;
            int x0 = Mathf.FloorToInt((center.x - reach) / ObstacleCell), x1 = Mathf.FloorToInt((center.x + reach) / ObstacleCell);
            int z0 = Mathf.FloorToInt((center.y - reach) / ObstacleCell), z1 = Mathf.FloorToInt((center.y + reach) / ObstacleCell);
            for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++)
            {
                long key = ObstacleKey(x, z);
                if (!footprintCells.TryGetValue(key, out var list)) footprintCells[key] = list = new List<int>();
                list.Add(index);
            }
            return index;
        }

        void LinkSpine(int from, int to)
        {
            if (from < 0) return;
            var footprint = footprints[from]; footprint.nextSpine = to; footprints[from] = footprint;
        }

        void PlanLandforms()
        {
            // Separate stream: landforms never shift towns, pins, trails or scenery drawn from the other generators.
            var random = new System.Random(unchecked(seed * 668265263 + 2029));
            float scale = size / 1600f;
            int target = Mathf.RoundToInt(Rand(random, MinLandforms, MaxLandforms) * Mathf.Clamp(scale * scale, .35f, 1));
            var shape = new List<Vector3>();
            for (int attempt = 0; attempt < target * 80 && landforms.Count < target; attempt++)
            {
                var center = new Vector2(Rand(random, -half * .92f, half * .92f), Rand(random, -half * .92f, half * .92f));
                if (!InOutline(center / size)) continue;
                var kind = PickLandform(random, Mathf.InverseLerp(-half, half, center.y));
                float yaw = Rand(random, 0, 360), height = ShapeLandform(kind, random, shape);
                Quaternion turn = Quaternion.Euler(0, yaw, 0);
                float reach = 0;
                for (int i = 0; i < shape.Count; i++)
                {
                    Vector3 local = turn * new Vector3(shape[i].x, 0, shape[i].y);
                    shape[i] = new Vector3(center.x + local.x, center.y + local.z, shape[i].z);
                    reach = Mathf.Max(reach, Vector2.Distance(center, new Vector2(shape[i].x, shape[i].y)) + shape[i].z);
                }
                if (!LandformFits(center, reach, height, shape)) continue;
                var landform = new Landform { kind = kind, center = center, reach = reach, height = height, yaw = yaw, first = footprints.Count, count = shape.Count };
                int previous = -1;
                foreach (var circle in shape)
                {
                    int next = AddFootprint(new Vector2(circle.x, circle.y), circle.z, height);
                    if (kind == LandformKind.Cliff) LinkSpine(previous, next);
                    previous = next;
                }
                landforms.Add(landform);
            }
            LandformCount = landforms.Count;
        }

        /// <summary>Overlapping rock footprints make a continuous impassable mountain rim at every outline edge.</summary>
        void PlanBoundary()
        {
            var random = new System.Random(unchecked(seed * 397 + 5189));
            float radius = Mathf.Clamp(size * .013f, 13, 24);
            for (int edge = 0; edge < Outline.Length; edge++)
            {
                Vector2 a = Outline[edge] * size, b = Outline[(edge + 1) % Outline.Length] * size;
                Vector2 tangent = (b - a).normalized, inward = new Vector2(tangent.y, -tangent.x);
                if (!InOutline((Vector2.Lerp(a, b, .5f) + inward * radius) / size)) inward = -inward;
                int segments = Mathf.CeilToInt(Vector2.Distance(a, b) / (radius * 1.2f));
                var chain = new Landform { kind = LandformKind.Cliff, boundary = true, center = (a + b) * .5f,
                    inward = inward, reach = radius, height = Rand(random, 26, 38), yaw = Mathf.Atan2(-tangent.y, tangent.x) * Mathf.Rad2Deg,
                    first = footprints.Count, count = segments + 1 };
                int previous = -1;
                float phase = Rand(random, 0, Mathf.PI * 2);
                for (int i = 0; i <= segments; i++)
                {
                    float u = i / (float)segments;
                    float wander = Mathf.Sin(u * Mathf.PI * 5 + phase) * .18f + Mathf.Sin(u * Mathf.PI * 11 + phase * .7f) * .08f;
                    Vector2 center = Vector2.Lerp(a, b, u) + inward * radius * (-.18f + wander);
                    int next = AddFootprint(center, radius * Rand(random, .9f, 1.1f), chain.height);
                    LinkSpine(previous, next);
                    previous = next;
                    BoundaryRockCount++;
                }
                boundaryChains.Add(chain);
            }
        }

        LandformKind PickLandform(System.Random random, float north)
        {
            double roll = random.NextDouble();
            // The compact test map needs shorter ridges and more small landmarks between its towns and roads.
            if (size < 1000) return roll < .5 ? LandformKind.Cliff : roll < .68 ? LandformKind.Mesa : roll < .82 ? LandformKind.Butte : LandformKind.Boulders;
            // Most impassable mountains are ridges. A few isolated mesas, buttes and rubble fields remain as landmarks.
            if (north < .4f) return roll < .68 ? LandformKind.Cliff : roll < .8 ? LandformKind.Mesa : roll < .89 ? LandformKind.Butte : LandformKind.Boulders;
            if (north < .68f) return roll < .77 ? LandformKind.Cliff : roll < .84 ? LandformKind.Mesa : roll < .9 ? LandformKind.Butte : LandformKind.Boulders;
            return roll < .82 ? LandformKind.Cliff : roll < .91 ? LandformKind.Butte : LandformKind.Boulders;
        }

        /// <summary>Fills <paramref name="shape"/> with local footprint circles (x, z, radius) and returns the landform height.</summary>
        float ShapeLandform(LandformKind kind, System.Random random, List<Vector3> shape)
        {
            shape.Clear();
            switch (kind)
            {
                case LandformKind.Mesa: shape.Add(new Vector3(0, 0, Rand(random, 22, 34))); return Rand(random, 19, 29);
                case LandformKind.Butte: shape.Add(new Vector3(0, 0, Rand(random, 11, 17))); return Rand(random, 23, 35);
                case LandformKind.Cliff:
                {
                    // A long, uneven spine; two bends prevent a capsule-shaped silhouette from above.
                    float scale = size < 1000 ? .4f : Mathf.Clamp(size / 1600f, 1, 1.3f);
                    float length = Rand(random, 115, 170) * scale, thickness = Rand(random, 10, 13) * Mathf.Sqrt(scale);
                    float bow = Rand(random, -.13f, .13f) * length;
                    float kink = Rand(random, -.075f, .075f) * length;
                    int stacks = Mathf.CeilToInt(length / (thickness * 1.2f)) + 1;
                    for (int i = 0; i < stacks; i++)
                    {
                        float u = i / (float)(stacks - 1), x = (u - .5f) * length;
                        float z = bow * Mathf.Sin(u * Mathf.PI) + kink * Mathf.Sin(u * Mathf.PI * 2);
                        shape.Add(new Vector3(x, z, thickness * Rand(random, .82f, 1.12f) * (i == 0 || i == stacks - 1 ? .78f : 1)));
                    }
                    return Rand(random, 22, 34);
                }
                default:
                {
                    // A pile of big rounded boulders, each overlapping the one it leans on.
                    float first = Rand(random, 6.5f, 9);
                    shape.Add(new Vector3(0, 0, first));
                    int count = random.Next(3, 6);
                    for (int i = 1; i < count; i++)
                    {
                        var lean = shape[random.Next(shape.Count)];
                        float r = Rand(random, 4.5f, 7), angle = Rand(random, 0, Mathf.PI * 2), gap = (lean.z + r) * Rand(random, .5f, .75f);
                        shape.Add(new Vector3(lean.x + Mathf.Cos(angle) * gap, lean.y + Mathf.Sin(angle) * gap, r));
                    }
                    return first * Rand(random, 1.2f, 1.45f);
                }
            }
        }

        /// <summary>
        /// A landform may not touch a road (roads are the only guaranteed links between towns), crowd a town, a point of
        /// interest or the river, stand near another landform (a car always fits between two), leave the state outline,
        /// or straddle ground so uneven that its base would float.
        /// </summary>
        bool LandformFits(Vector2 center, float reach, float height, List<Vector3> shape)
        {
            if (TownDistance(center) < reach + LandformTownClearance) return false;
            foreach (var pin in Pins) if (pin.kind != "town" && Vector2.Distance(center, XZ(pin.position)) < reach + LandformPinClearance) return false;
            foreach (var other in landforms) if (Vector2.Distance(center, other.center) < reach + other.reach + LandformSpacing) return false;
            float low = float.MaxValue, high = float.MinValue;
            foreach (var circle in shape)
            {
                var c = new Vector2(circle.x, circle.y); float r = circle.z;
                if (RoadDistance(c) < r + LandformRoadClearance) return false;
                if (riverWidth > 0 && Mathf.Abs(c.x - RiverX(c.y)) < r + riverWidth + LandformRiverClearance) return false;
                for (int k = 0; k < 5; k++)
                {
                    Vector2 q = k == 0 ? c : c + new Vector2(k == 1 ? r : k == 2 ? -r : 0, k == 3 ? r : k == 4 ? -r : 0);
                    if (!InOutline((c + (q - c) * 1.3f) / size)) return false;
                    float h = SampleHeight(new Vector3(q.x, 0, q.y)); low = Mathf.Min(low, h); high = Mathf.Max(high, h);
                }
            }
            return high - low < height * .55f;
        }

        /// <summary>
        /// Bends a trail point around any landform the trail would cross. The detour keeps to the side of the landform
        /// that the trail's straight chord passes, so the trail curves round the rock instead of being dropped or zig-zagging.
        /// Returns true when the point moved.
        /// </summary>
        bool SkirtLandforms(ref Vector2 p, Vector2 a, Vector2 dir, Vector2 normal)
        {
            bool moved = false;
            foreach (var landform in landforms)
            {
                float reach = landform.reach + TrailLandformClearance + 2;
                if ((landform.center - p).sqrMagnitude > reach * reach) continue;
                float side = Vector2.Dot(landform.center - a, normal) > 0 ? -1 : 1;
                for (int pass = 0; pass < 2; pass++)
                    for (int k = landform.first; k < landform.first + landform.count; k++)
                    {
                        var f = footprints[k]; float need = f.radius + TrailLandformClearance + 2;
                        Vector2 d = p - f.center; float along = Vector2.Dot(d, dir);
                        if (Mathf.Abs(along) >= need) continue;
                        float lateral = Vector2.Dot(d, normal), required = Mathf.Sqrt(need * need - along * along);
                        if (lateral * side >= required) continue;
                        p = f.center + dir * along + normal * side * required; moved = true;
                    }
            }
            return moved;
        }

        /// <summary>
        /// Medium cover rocks: tall enough to hide a car and stop rounds, placed beside trails (never on them or on
        /// roads) with a car-width gap to anything else. Most objectives get a loose ring of a few rocks, far enough out
        /// that the objective itself stays open.
        /// </summary>
        void PlanCover()
        {
            var random = new System.Random(unchecked(seed * 1103515245 + 4099));
            float scale = size / 1600f;
            int target = Mathf.RoundToInt(Rand(random, 45, 65) * Mathf.Clamp(scale * scale, .35f, 1.6f));
            var objectives = new List<Vector2>();
            foreach (var junction in TrailJunctions) objectives.Add(XZ(junction));
            foreach (var pin in Pins) if (pin.kind != "town") objectives.Add(XZ(pin.position));
            int start = landforms.Count;
            foreach (var objective in objectives)
            {
                if (landforms.Count - start >= target * .6f) break;
                if (random.NextDouble() > .7) continue;
                int rocks = random.Next(2, 5); float ring = Rand(random, 18, 30), turn = Rand(random, 0, Mathf.PI * 2);
                for (int k = 0; k < rocks; k++)
                {
                    float angle = turn + k * Mathf.PI * 2 / rocks + Rand(random, -.35f, .35f), distance = ring * Rand(random, .85f, 1.15f);
                    TryCover(objective + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance, random, objectives);
                }
            }
            for (int attempt = 0; attempt < target * 40 && landforms.Count - start < target; attempt++)
            {
                var p = new Vector2(Rand(random, -half * .95f, half * .95f), Rand(random, -half * .95f, half * .95f));
                TryCover(p, random, objectives);
            }
            CoverCount = landforms.Count - start;
        }

        bool TryCover(Vector2 p, System.Random random, List<Vector2> objectives)
        {
            // Footprint radius bounds a slab about 4-6 m long; 2.1-3.4 m tall hides a car (1.5 m) and stops rounds at bumper height.
            float radius = Rand(random, 2, 3.2f), height = Rand(random, 2.1f, 3.4f), yaw = Rand(random, 0, 360);
            if (!InOutline(p / size) || RoadDistance(p) < radius + CoverRoadClearance || TownDistance(p) < radius + CoverTownClearance) return false;
            if (TrailEdgeDistance(p) < radius + 1.5f || ObstacleDistance(p) < radius + CoverGap) return false;
            if (riverWidth > 0 && Mathf.Abs(p.x - RiverX(p.y)) < radius + riverWidth + 4) return false;
            foreach (var objective in objectives) if ((objective - p).sqrMagnitude < (radius + CoverObjectiveClearance) * (radius + CoverObjectiveClearance)) return false;
            Vector3 at = new Vector3(p.x, 0, p.y);
            float slope = Mathf.Abs(SampleHeight(at + Vector3.right * radius) - SampleHeight(at - Vector3.right * radius)) + Mathf.Abs(SampleHeight(at + Vector3.forward * radius) - SampleHeight(at - Vector3.forward * radius));
            if (slope > radius * .9f) return false;
            landforms.Add(new Landform { kind = LandformKind.Cover, center = p, reach = radius, height = height, yaw = yaw, first = footprints.Count, count = 1 });
            AddFootprint(p, radius, height, true);
            return true;
        }

        void BuildLandforms()
        {
            if (landforms.Count == 0 && boundaryChains.Count == 0) return;
            Transform root = Group("Landforms and cover", transform, Vector3.zero);
            landformMaterial = new Material(desert) { name = "Landform rock (see-through over the car)" };
            landformMaterial.SetFloat("_OccluderCut", 1);
            var catalog = DesertLandformCatalog.Load();
            var random = new System.Random(unchecked(seed * 214013 + 2531011));
            var builder = new LandformMesh();
            var spine = new List<Vector2>(); var spineRadii = new List<float>();
            var all = new List<Landform>(landforms); all.AddRange(boundaryChains);
            foreach (var landform in all)
            {
                float ground = 0;
                for (int k = landform.first; k < landform.first + landform.count; k++)
                {
                    var f = footprints[k];
                    for (int s = 0; s < 5; s++)
                    {
                        float angle = s * Mathf.PI * .4f; Vector2 q = s == 0 ? f.center : f.center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * f.radius * .8f;
                        ground += SampleHeight(new Vector3(q.x, 0, q.y));
                    }
                }
                ground /= landform.count * 5;
                var prefab = catalog ? catalog.Pick(landform.kind, random) : null;
                if (prefab && landform.kind == LandformKind.Cliff && prefab.GetComponentInChildren<Renderer>())
                {
                    // One imported cliff per footprint makes the planned wall read as a mountain chain from above.
                    var chain = Group(landform.boundary ? "Boundary mountain chain" : "Mountain chain", root, Vector3.zero);
                    for (int k = landform.first; k < landform.first + landform.count; k++)
                    {
                        var f = footprints[k];
                        PlaceScaledModel(prefab, f.center, f.radius * .98f, landform.height * Rand(random, .85f, 1.15f),
                            landform.yaw + Rand(random, -5, 5), chain, catalog.useTerrainMaterial, "Mountain rock");
                    }
                    AddChainBarriers(landform, chain);
                    ScatterBaseRocks(landform, chain, catalog, random);
                    continue;
                }
                if (prefab && landform.kind != LandformKind.Cliff && PlaceLandformModel(prefab, landform, root, catalog.useTerrainMaterial))
                {
                    if (landform.kind != LandformKind.Cover) ScatterBaseRocks(landform, root, catalog, random);
                    continue;
                }
                builder.Clear();
                Color tint = BiomeColor(new Vector3(landform.center.x, 0, landform.center.y)); tint.a = 1;
                if (landform.kind == LandformKind.Cliff)
                {
                    spine.Clear(); spineRadii.Clear();
                    // The wall stays inside the chain of footprint circles, whose waist between centres is narrower than a circle.
                    for (int k = landform.first; k < landform.first + landform.count; k++) { spine.Add(footprints[k].center); spineRadii.Add(footprints[k].radius * .88f); }
                    builder.Wall(this, spine, spineRadii, landform.height, ground, CliffProfile, .2f, .03f, tint, random);
                }
                else for (int k = landform.first; k < landform.first + landform.count; k++)
                {
                    var f = footprints[k];
                    bool rounded = landform.kind == LandformKind.Boulders || landform.kind == LandformKind.Cover;
                    float stackHeight = landform.kind == LandformKind.Boulders ? f.radius * Rand(random, 1.15f, 1.4f) : landform.height;
                    int sides = landform.kind == LandformKind.Mesa ? 26 : landform.kind == LandformKind.Cover ? 9 : landform.kind == LandformKind.Boulders ? 11 : 16;
                    float roughness = landform.kind == LandformKind.Mesa ? .16f : landform.kind == LandformKind.Cover ? .22f : .2f;
                    // Cover rocks are slabs: long across their yaw, narrow along it.
                    Vector2 stretch = landform.kind == LandformKind.Cover ? new Vector2(1, Rand(random, .55f, .75f)) : Vector2.one;
                    float groundRef = rounded ? SampleHeight(new Vector3(f.center.x, 0, f.center.y)) - .35f : ground;
                    // Rounded rocks get a domed top; a flat one read as a pale disc of ground texture from above.
                    builder.Round(this, f.center, f.radius, stackHeight, groundRef, Profile(landform.kind), sides, roughness, stretch, landform.yaw, rounded ? .14f : .015f, tint, random);
                }
                var go = new GameObject(landform.kind == LandformKind.Cover ? "Cover rock" : "Landform • " + landform.kind, typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
                go.transform.SetParent(root, false);
                var mesh = builder.ToMesh(go.name);
                go.GetComponent<MeshFilter>().sharedMesh = mesh; go.GetComponent<MeshRenderer>().sharedMaterial = landformMaterial;
                go.GetComponent<MeshCollider>().sharedMesh = mesh;
                go.AddComponent<GeneratedMeshOwner>().Mesh = mesh;
                if (landform.kind == LandformKind.Cliff) AddChainBarriers(landform, root);
                if (landform.kind != LandformKind.Cover) ScatterBaseRocks(landform, root, catalog, random);
            }
        }

        void AddChainBarriers(Landform landform, Transform root)
        {
            // Asset silhouettes have crevices. Overlapping simple colliders close those gaps along the impassable spine.
            for (int k = landform.first; k < landform.first + landform.count - 1; k++)
            {
                var a = footprints[k]; var b = footprints[k + 1];
                Vector2 delta = b.center - a.center, center = (a.center + b.center) * .5f;
                float low = Mathf.Min(SampleHeight(new Vector3(a.center.x, 0, a.center.y)), SampleHeight(new Vector3(b.center.x, 0, b.center.y))) - 3;
                float top = Mathf.Max(SampleHeight(new Vector3(a.center.x, 0, a.center.y)), SampleHeight(new Vector3(b.center.x, 0, b.center.y))) + landform.height * .8f;
                var blocker = new GameObject("Mountain barrier", typeof(BoxCollider));
                blocker.transform.SetParent(root, false);
                blocker.transform.SetPositionAndRotation(new Vector3(center.x, (low + top) * .5f, center.y),
                    Quaternion.Euler(0, Mathf.Atan2(-delta.y, delta.x) * Mathf.Rad2Deg, 0));
                blocker.GetComponent<BoxCollider>().size = new Vector3(delta.magnitude + Mathf.Min(a.radius, b.radius) * .7f,
                    top - low, Mathf.Min(a.radius, b.radius) * 1.1f);
                MountainBarrierCount++;
            }
        }

        /// <summary>Places a catalog model on a planned footprint. Returns false (placeholder used instead) if it has no renderers.</summary>
        bool PlaceLandformModel(GameObject prefab, Landform landform, Transform root, bool terrainMaterial)
        {
            return PlaceScaledModel(prefab, landform.center, landform.reach * .95f, landform.height, landform.yaw,
                root, terrainMaterial, prefab.name + " • " + landform.kind);
        }

        bool PlaceScaledModel(GameObject prefab, Vector2 center, float radius, float height, float yaw, Transform root,
            bool terrainMaterial, string label)
        {
            var go = Instantiate(prefab, root);
            go.name = label;
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) { Destroy(go); return false; }
            go.transform.SetPositionAndRotation(new Vector3(center.x, 0, center.y), Quaternion.Euler(0, yaw, 0));
            go.transform.localScale = Vector3.one;
            Bounds bounds = ModelBounds(renderers);
            float footprint = Mathf.Max(.01f, Mathf.Max(bounds.size.x, bounds.size.z) * .5f), tall = Mathf.Max(.01f, bounds.size.y);
            float horizontal = radius / footprint;
            float vertical = Mathf.Clamp(height / tall, horizontal * .75f, horizontal * 2.4f);
            go.transform.localScale = new Vector3(horizontal, vertical, horizontal);
            float ground = SampleHeight(new Vector3(center.x, 0, center.y));
            for (int i = 0; i < 4; i++)
            {
                float angle = i * Mathf.PI * .5f;
                Vector2 q = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius * .7f;
                ground = Mathf.Min(ground, SampleHeight(new Vector3(q.x, 0, q.y)));
            }
            bounds = ModelBounds(renderers);
            go.transform.position += Vector3.up * (ground - .65f - bounds.min.y);
            if (terrainMaterial && landformMaterial)
                foreach (var renderer in renderers) { var materials = renderer.sharedMaterials; for (int i = 0; i < materials.Length; i++) materials[i] = landformMaterial; renderer.sharedMaterials = materials; }
            if (!go.GetComponentInChildren<Collider>())
                foreach (var filter in go.GetComponentsInChildren<MeshFilter>()) if (filter.sharedMesh) filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
            return true;
        }

        void ScatterBaseRocks(Landform landform, Transform root, DesertLandformCatalog catalog, System.Random random)
        {
            int stride = landform.boundary ? 3 : landform.kind == LandformKind.Cliff ? 2 : 1;
            for (int k = landform.first; k < landform.first + landform.count; k += stride)
            {
                var f = footprints[k];
                for (int side = -1; side <= 1; side += 2)
                {
                    float angle = Rand(random, 0, Mathf.PI * 2);
                    Vector2 direction = landform.boundary ?
                        (landform.inward + new Vector2(-landform.inward.y, landform.inward.x) * side * .28f).normalized :
                        new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * side;
                    Vector2 at = f.center + direction * f.radius * Rand(random, .65f, .78f);
                    float radius = Mathf.Min(f.radius * .2f, Rand(random, 2.2f, 4.4f));
                    var rock = catalog ? catalog.Pick(LandformKind.Cover, random) : null;
                    if (!rock || !PlaceScaledModel(rock, at, radius, radius * Rand(random, .8f, 1.3f),
                        Rand(random, 0, 360), root, catalog.useTerrainMaterial, "Mountain base rock"))
                    {
                        Vector3 p = new Vector3(at.x, SampleHeight(new Vector3(at.x, 0, at.y)), at.y);
                        Boulder(root, p, radius * 1.4f);
                    }
                    BaseRockCount++;
                }
            }
        }
        static Bounds ModelBounds(Renderer[] renderers)
        {
            Bounds bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        // Profiles: (height fraction, radius fraction). A negative height is the buried skirt, below the local ground.
        static readonly Vector2[] MesaProfile = { new Vector2(-1, 1), new Vector2(0, .98f), new Vector2(.14f, .9f), new Vector2(.22f, .85f), new Vector2(.55f, .83f), new Vector2(.58f, .79f), new Vector2(1, .77f) };
        static readonly Vector2[] ButteProfile = { new Vector2(-1, 1), new Vector2(0, .96f), new Vector2(.18f, .83f), new Vector2(.3f, .76f), new Vector2(.68f, .69f), new Vector2(.72f, .62f), new Vector2(1, .58f) };
        static readonly Vector2[] CliffProfile = { new Vector2(-1, 1), new Vector2(0, .96f), new Vector2(.16f, .86f), new Vector2(.85f, .72f), new Vector2(1, .6f) };
        static readonly Vector2[] BoulderProfile = { new Vector2(-1, .92f), new Vector2(0, .98f), new Vector2(.3f, 1), new Vector2(.62f, .86f), new Vector2(.86f, .56f), new Vector2(1, .2f) };
        static readonly Vector2[] CoverProfile = { new Vector2(-1, .88f), new Vector2(0, .96f), new Vector2(.45f, 1), new Vector2(.8f, .74f), new Vector2(1, .36f) };
        static Vector2[] Profile(LandformKind kind) => kind == LandformKind.Mesa ? MesaProfile : kind == LandformKind.Butte ? ButteProfile
            : kind == LandformKind.Cliff ? CliffProfile : kind == LandformKind.Boulders ? BoulderProfile : CoverProfile;

        /// <summary>Paints landforms and cover rocks onto the map: a lit rock top with a dark rim.</summary>
        Color MapLandform(Vector2 p, Color ground)
        {
            float d = ObstacleDistance(p);
            if (d >= 0) return ground;
            float ridge = float.MaxValue;
            if (footprintCells.TryGetValue(ObstacleKey(Mathf.FloorToInt(p.x / ObstacleCell), Mathf.FloorToInt(p.y / ObstacleCell)), out var nearby))
                foreach (int index in nearby)
                {
                    var a = footprints[index];
                    if (a.nextSpine < 0) continue;
                    var b = footprints[a.nextSpine];
                    Vector2 span = b.center - a.center;
                    float u = Mathf.Clamp01(Vector2.Dot(p - a.center, span) / Mathf.Max(.001f, span.sqrMagnitude));
                    float width = Mathf.Lerp(a.radius, b.radius, u);
                    ridge = Mathf.Min(ridge, Vector2.Distance(p, a.center + span * u) / width);
                }
            if (ridge < 1)
            {
                Color flank = new Color(.28f, .18f, .13f);
                Color crest = new Color(.72f, .5f, .33f);
                return Color.Lerp(crest, flank, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.18f, .95f, ridge)));
            }
            return d > -2.5f ? new Color(.25f, .17f, .12f) : new Color(.62f, .42f, .29f) * Mathf.Lerp(1, 1.18f, Mathf.Clamp01(-d / 12));
        }

        /// <summary>
        /// Lofted rock: rings of a rim outline stacked by a profile (buried skirt, talus foot, walls with a ledge) and
        /// capped. Every rim vertex hangs off an anchor, the centre of a round landform or a point on a cliff's spine, so
        /// one loft builds both round stacks and continuous walls.
        /// </summary>
        sealed class LandformMesh
        {
            struct Rim { public int anchor; public Vector2 offset; }
            readonly List<Vector3> vertices = new List<Vector3>();
            readonly List<int> triangles = new List<int>();
            readonly List<Color> colors = new List<Color>();
            readonly List<Rim> rim = new List<Rim>();
            readonly List<Vector2> anchors = new List<Vector2>();
            readonly List<float> heights = new List<float>();
            public void Clear() { vertices.Clear(); triangles.Clear(); colors.Clear(); }

            /// <summary>A round (or, stretched, slab-shaped) stack: mesas, buttes, boulders and cover rocks.</summary>
            public void Round(GeneratedWorld world, Vector2 center, float radius, float height, float ground, Vector2[] profile, int sides, float roughness,
                Vector2 stretch, float yaw, float crown, Color tint, System.Random random)
            {
                rim.Clear(); anchors.Clear(); heights.Clear();
                anchors.Add(center); heights.Add(height);
                Quaternion turn = Quaternion.Euler(0, yaw, 0);
                for (int i = 0; i < sides; i++)
                {
                    float angle = i * Mathf.PI * 2 / sides;
                    Vector3 local = turn * new Vector3(Mathf.Cos(angle) * radius * stretch.x, 0, Mathf.Sin(angle) * radius * stretch.y);
                    rim.Add(new Rim { anchor = 0, offset = new Vector2(local.x, local.z) });
                }
                Loft(world, ground, profile, roughness, crown, tint, random);
            }

            /// <summary>
            /// A continuous cliff wall along a spine of footprint circles: down one side, round the far end, back along the
            /// other side and round the near end (counter-clockwise from above, like the round stacks). The crest height
            /// wanders along the wall.
            /// </summary>
            public void Wall(GeneratedWorld world, List<Vector2> spine, List<float> radii, float height, float ground, Vector2[] profile, float roughness,
                float crown, Color tint, System.Random random)
            {
                rim.Clear(); anchors.Clear(); heights.Clear();
                int n = spine.Count; const int arc = 4;
                var crest = new float[n];
                for (int i = 0; i < n; i++) crest[i] = height * Rand(random, .78f, 1.05f);
                for (int i = 0; i < n; i++) { anchors.Add(spine[i]); heights.Add((crest[Mathf.Max(0, i - 1)] + crest[i] * 2 + crest[Mathf.Min(n - 1, i + 1)]) * .25f); }
                Vector2 Tangent(int i) => (spine[Mathf.Min(n - 1, i + 1)] - spine[Mathf.Max(0, i - 1)]).normalized;
                Vector2 Left(Vector2 t) => new Vector2(-t.y, t.x);
                for (int i = 0; i < n; i++) rim.Add(new Rim { anchor = i, offset = -Left(Tangent(i)) * radii[i] });
                for (int j = 1; j <= arc; j++)
                {
                    float angle = -90 + 180f * j / (arc + 1); Vector2 t = Tangent(n - 1);
                    rim.Add(new Rim { anchor = n - 1, offset = (t * Mathf.Cos(angle * Mathf.Deg2Rad) + Left(t) * Mathf.Sin(angle * Mathf.Deg2Rad)) * radii[n - 1] });
                }
                for (int i = n - 1; i >= 0; i--) rim.Add(new Rim { anchor = i, offset = Left(Tangent(i)) * radii[i] });
                for (int j = 1; j <= arc; j++)
                {
                    float angle = 90 + 180f * j / (arc + 1); Vector2 t = Tangent(0);
                    rim.Add(new Rim { anchor = 0, offset = (t * Mathf.Cos(angle * Mathf.Deg2Rad) + Left(t) * Mathf.Sin(angle * Mathf.Deg2Rad)) * radii[0] });
                }
                Loft(world, ground, profile, roughness, crown, tint, random);
            }

            void Loft(GeneratedWorld world, float ground, Vector2[] profile, float roughness, float crown, Color tint, System.Random random)
            {
                int count = rim.Count;
                // One noise per rim vertex, shared by every ring, keeps bays and buttresses running from the foot to the rim.
                var shape = new float[count];
                for (int i = 0; i < count; i++) shape[i] = 1 - roughness * (float)random.NextDouble();
                var smooth = new float[count];
                for (int i = 0; i < count; i++) smooth[i] = (shape[(i + count - 1) % count] + shape[i] * 2 + shape[(i + 1) % count]) * .25f;
                var rings = new Vector3[profile.Length, count];
                for (int r = 0; r < profile.Length; r++)
                    for (int i = 0; i < count; i++)
                    {
                        var v = rim[i];
                        Vector2 xz = anchors[v.anchor] + v.offset * profile[r].y * smooth[i] * Rand(random, .97f, 1.03f);
                        Vector3 p = new Vector3(xz.x, 0, xz.y);
                        p.y = profile[r].x < 0 ? Mathf.Min(world.SampleHeight(p), ground) - 2.5f : ground + profile[r].x * heights[v.anchor];
                        rings[r, i] = p;
                    }
                // Each band has its own vertices, so ledges between bands stay crisp while each band is smooth around.
                for (int r = 0; r < profile.Length - 1; r++)
                {
                    int start = vertices.Count;
                    for (int i = 0; i < count; i++) { vertices.Add(rings[r, i]); vertices.Add(rings[r + 1, i]); }
                    Color band = tint * Mathf.Lerp(.9f, 1.04f, r / (float)profile.Length); band.a = 1;
                    for (int i = 0; i < count * 2; i++) colors.Add(band);
                    for (int i = 0; i < count; i++)
                    {
                        int j = (i + 1) % count, bi = start + i * 2, ti = bi + 1, bj = start + j * 2, tj = bj + 1;
                        triangles.Add(bi); triangles.Add(ti); triangles.Add(tj);
                        triangles.Add(bi); triangles.Add(tj); triangles.Add(bj);
                    }
                }
                // Cap: each pair of neighbouring rim vertices joins its anchors, raised by the crown (domed boulder tops).
                int top = profile.Length - 1, cap = vertices.Count, hub = cap + count;
                for (int i = 0; i < count; i++) vertices.Add(rings[top, i]);
                for (int a = 0; a < anchors.Count; a++) vertices.Add(new Vector3(anchors[a].x, ground + heights[a] * (1 + crown), anchors[a].y));
                Color lid = tint * 1.06f; lid.a = 1;
                for (int i = 0; i < count + anchors.Count; i++) colors.Add(lid);
                for (int i = 0; i < count; i++)
                {
                    int j = (i + 1) % count, A = hub + rim[i].anchor, B = hub + rim[j].anchor;
                    triangles.Add(A); triangles.Add(cap + j); triangles.Add(cap + i);
                    if (A != B) { triangles.Add(A); triangles.Add(B); triangles.Add(cap + j); }
                }
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.SetColors(colors);
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
