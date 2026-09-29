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
    /// Each landform is a set of circular footprints: navigation, scenery, spawns and the map all read those.
    /// Procedural placeholders stand in until models are assigned in the <see cref="DesertLandformCatalog"/>.
    /// </summary>
    public sealed partial class GeneratedWorld
    {
        public enum LandformKind { Mesa, Butte, Cliff, Boulders, Cover }
        sealed class Landform { public LandformKind kind; public Vector2 center; public float reach, height, yaw; public int first, count; }
        struct Footprint { public Vector2 center; public float radius, height; public bool cover; }

        /// <summary>A conservative first pass: 15-25 landforms on the default 1.6 km map, proportionally fewer on small maps.</summary>
        public const int MinLandforms = 15, MaxLandforms = 25;
        const float LandformRoadClearance = 24, LandformTownClearance = 130, LandformPinClearance = 70, LandformSpacing = 50,
            LandformRiverClearance = 30, TrailLandformClearance = 6, JunctionLandformClearance = 40;
        const float CoverRoadClearance = 12, CoverTownClearance = 75, CoverObjectiveClearance = 12, CoverGap = 8;
        const float ObstacleCell = 48, ObstacleQueryReach = 48;
        readonly List<Landform> landforms = new List<Landform>();
        readonly List<Footprint> footprints = new List<Footprint>();
        readonly Dictionary<long, List<int>> footprintCells = new Dictionary<long, List<int>>();
        Material landformMaterial;

        public int LandformCount { get; private set; }
        public int CoverCount { get; private set; }
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
            bool moved = false;
            for (int pass = 0; pass < 4; pass++)
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
            if (moved) p.y = SampleHeight(p) + lift;
            return p;
        }

        static long ObstacleKey(int x, int z) => (long)x << 32 | (uint)z;
        void AddFootprint(Vector2 center, float radius, float height, bool cover = false)
        {
            int index = footprints.Count;
            footprints.Add(new Footprint { center = center, radius = radius, height = height, cover = cover });
            float reach = radius + ObstacleQueryReach;
            int x0 = Mathf.FloorToInt((center.x - reach) / ObstacleCell), x1 = Mathf.FloorToInt((center.x + reach) / ObstacleCell);
            int z0 = Mathf.FloorToInt((center.y - reach) / ObstacleCell), z1 = Mathf.FloorToInt((center.y + reach) / ObstacleCell);
            for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++)
            {
                long key = ObstacleKey(x, z);
                if (!footprintCells.TryGetValue(key, out var list)) footprintCells[key] = list = new List<int>();
                list.Add(index);
            }
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
                foreach (var circle in shape) AddFootprint(new Vector2(circle.x, circle.y), circle.z, height);
                landforms.Add(landform);
            }
            LandformCount = landforms.Count;
        }

        static LandformKind PickLandform(System.Random random, float north)
        {
            double roll = random.NextDouble();
            // Southern desert: mesas and buttes; the middle band: a mix; the northern forest and highlands: cliffs and boulders.
            if (north < .4f) return roll < .4 ? LandformKind.Mesa : roll < .65 ? LandformKind.Butte : roll < .9 ? LandformKind.Boulders : LandformKind.Cliff;
            if (north < .68f) return roll < .2 ? LandformKind.Mesa : roll < .4 ? LandformKind.Butte : roll < .7 ? LandformKind.Cliff : LandformKind.Boulders;
            return roll < .1 ? LandformKind.Butte : roll < .55 ? LandformKind.Cliff : LandformKind.Boulders;
        }

        /// <summary>Fills <paramref name="shape"/> with local footprint circles (x, z, radius) and returns the landform height.</summary>
        static float ShapeLandform(LandformKind kind, System.Random random, List<Vector3> shape)
        {
            shape.Clear();
            switch (kind)
            {
                case LandformKind.Mesa: shape.Add(new Vector3(0, 0, Rand(random, 16, 28))); return Rand(random, 9, 14);
                case LandformKind.Butte: shape.Add(new Vector3(0, 0, Rand(random, 8, 12.5f))); return Rand(random, 13, 19);
                case LandformKind.Cliff:
                {
                    // A bowed wall of overlapping rock stacks.
                    float length = Rand(random, 36, 64), thickness = Rand(random, 6, 8.5f), bow = Rand(random, -.22f, .22f) * length;
                    int stacks = Mathf.CeilToInt(length / (thickness * 1.15f)) + 1;
                    for (int i = 0; i < stacks; i++)
                    {
                        float u = i / (float)(stacks - 1), x = (u - .5f) * length, z = bow * (1 - (2 * u - 1) * (2 * u - 1));
                        shape.Add(new Vector3(x, z, thickness * Rand(random, .85f, 1.1f) * (i == 0 || i == stacks - 1 ? .8f : 1)));
                    }
                    return Rand(random, 8, 13);
                }
                default:
                {
                    // A pile of big rounded boulders, each overlapping the one it leans on.
                    float first = Rand(random, 4.5f, 6.5f);
                    shape.Add(new Vector3(0, 0, first));
                    int count = random.Next(3, 6);
                    for (int i = 1; i < count; i++)
                    {
                        var lean = shape[random.Next(shape.Count)];
                        float r = Rand(random, 3, 5.2f), angle = Rand(random, 0, Mathf.PI * 2), gap = (lean.z + r) * Rand(random, .5f, .75f);
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
            if (landforms.Count == 0) return;
            Transform root = Group("Landforms and cover", transform, Vector3.zero);
            landformMaterial = new Material(desert) { name = "Landform rock (see-through over the car)" };
            landformMaterial.SetFloat("_OccluderCut", 1);
            var catalog = DesertLandformCatalog.Load();
            var random = new System.Random(unchecked(seed * 214013 + 2531011));
            var builder = new LandformMesh();
            var spine = new List<Vector2>(); var spineRadii = new List<float>();
            foreach (var landform in landforms)
            {
                float ground = 0, low = float.MaxValue;
                for (int k = landform.first; k < landform.first + landform.count; k++)
                {
                    var f = footprints[k];
                    for (int s = 0; s < 5; s++)
                    {
                        float angle = s * Mathf.PI * .4f; Vector2 q = s == 0 ? f.center : f.center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * f.radius * .8f;
                        float h = SampleHeight(new Vector3(q.x, 0, q.y)); ground += h; low = Mathf.Min(low, h);
                    }
                }
                ground /= landform.count * 5;
                var prefab = catalog ? catalog.Pick(landform.kind, random) : null;
                if (prefab && PlaceLandformModel(prefab, landform, root, low, catalog.useTerrainMaterial)) continue;
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
            }
        }

        /// <summary>Places a catalog model on a planned footprint. Returns false (placeholder used instead) if it has no renderers.</summary>
        bool PlaceLandformModel(GameObject prefab, Landform landform, Transform root, float lowGround, bool terrainMaterial)
        {
            var go = Instantiate(prefab, root);
            go.name = prefab.name + " • " + landform.kind;
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) { Destroy(go); return false; }
            go.transform.SetPositionAndRotation(new Vector3(landform.center.x, lowGround, landform.center.y), Quaternion.Euler(0, landform.yaw, 0));
            go.transform.localScale = Vector3.one;
            Bounds bounds = ModelBounds(renderers);
            // Cliff models run along their longest axis; the planned wall runs along local x.
            if (landform.kind == LandformKind.Cliff && bounds.size.z > bounds.size.x) { go.transform.rotation = Quaternion.Euler(0, landform.yaw + 90, 0); bounds = ModelBounds(renderers); }
            float footprint = Mathf.Max(.01f, Mathf.Max(bounds.size.x, bounds.size.z) * .5f), tall = Mathf.Max(.01f, bounds.size.y);
            float scale = landform.kind == LandformKind.Cover ? Mathf.Min(landform.height / tall, landform.reach * 1.1f / footprint) : Mathf.Min(landform.reach * .95f / footprint, 22 / tall);
            go.transform.localScale = Vector3.one * scale;
            bounds = ModelBounds(renderers);
            go.transform.position += Vector3.up * (lowGround - .4f - bounds.min.y);
            if (terrainMaterial && landformMaterial)
                foreach (var renderer in renderers) { var materials = renderer.sharedMaterials; for (int i = 0; i < materials.Length; i++) materials[i] = landformMaterial; renderer.sharedMaterials = materials; }
            if (!go.GetComponentInChildren<Collider>())
                foreach (var filter in go.GetComponentsInChildren<MeshFilter>()) if (filter.sharedMesh) filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
            return true;
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
