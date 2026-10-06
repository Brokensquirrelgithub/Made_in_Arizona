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
        public enum LandformKind { Mesa, Butte, Cliff, Boulders, Cover, Arch }
        // Butte variants: a plain butte, a mitten (butte plus a spire thumb) or a cluster of needle spires.
        enum ButteVariant { Plain, Mitten, Spires }
        sealed class Landform { public LandformKind kind; public Vector2 center, inward; public float reach, height, yaw; public int first, count, variant, shapeSeed; public bool boundary; public Vector4 arch; public float archDepth; }
        struct Footprint { public Vector2 center; public float radius, height; public bool cover; public int nextSpine; }

        /// <summary>18-30 landforms on the 1.6 km map, scaling with area up to four times that on the largest maps.</summary>
        public const int MinLandforms = 18, MaxLandforms = 30;
        const float LandformRoadClearance = 24, LandformTownClearance = 130, LandformPinClearance = 70, LandformSpacing = 50,
            LandformRiverClearance = 30, TrailLandformClearance = 6, JunctionLandformClearance = 40;
        const float CoverRoadClearance = 12, CoverTownClearance = 75, CoverObjectiveClearance = 12, CoverGap = 8;
        /// <summary>
        /// Smallest footprint radius of any solid, unbreakable rock: 6.5 m, so the smallest is about 13 m across, three
        /// car lengths. Smaller impassable stones read as pebbles and snagged tyres like hooks; anything smaller is a
        /// breakable prop the car ploughs through.
        /// </summary>
        public const float MinSolidRockRadius = 6.5f;
        const float ObstacleCell = 48, ObstacleQueryReach = 48;
        readonly List<Landform> landforms = new List<Landform>();
        readonly List<Landform> boundaryChains = new List<Landform>();
        readonly List<Footprint> footprints = new List<Footprint>();
        readonly Dictionary<long, List<int>> footprintCells = new Dictionary<long, List<int>>();
        Material landformMaterial;
        readonly Dictionary<Material, Material> packRockMaterials = new Dictionary<Material, Material>();
        bool keepPackColours;
        Material monumentMaterial;
        // Per circle of the shape being planned (parallel to it): height scale, and whether the spine breaks before it (a pass).
        readonly List<float> shapeHeight = new List<float>();
        readonly List<bool> shapePass = new List<bool>();
        int shapeVariant; Vector4 shapeArch; float shapeArchDepth; // set by ShapeLandform for the landform being planned

        public int LandformCount { get; private set; }
        public int CoverCount { get; private set; }
        public int BoundaryRockCount { get; private set; }
        public int MountainBarrierCount { get; private set; }
        public int BaseRockCount { get; private set; }
        /// <summary>Interior mountain ridges (cliff chains) among the landforms.</summary>
        public int RidgeCount { get; private set; }
        /// <summary>Planned passes through interior ridges: midpoint of each opening (ground level 0) and its width.</summary>
        public readonly List<Vector3> PassPoints = new List<Vector3>();
        public readonly List<float> PassWidths = new List<float>();
        /// <summary>The ground under each natural arch's span, where a car drives through.</summary>
        public readonly List<Vector3> ArchPoints = new List<Vector3>();
        readonly List<Landform> arches = new List<Landform>();
        /// <summary>Landforms and cover rocks in placement order (landforms first), for review captures and tests.</summary>
        public int LandformEntries => landforms.Count;
        /// <summary>Footprint circles (x, z, radius) of a landform, or of a boundary chain when <paramref name="boundary"/>.</summary>
        public void Footprints(int index, List<Vector3> into, bool boundary = false)
        {
            into.Clear();
            var landform = boundary ? boundaryChains[index] : landforms[index];
            for (int k = landform.first; k < landform.first + landform.count; k++) into.Add(new Vector3(footprints[k].center.x, footprints[k].center.y, footprints[k].radius));
        }
        public int BoundaryChainCount => boundaryChains.Count;
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
                if (landform.kind == LandformKind.Cliff)
                {
                    // Ridges are held to the same clearances stack by stack (see TrimRidge).
                    for (int k = landform.first; k < landform.first + landform.count; k++)
                    {
                        var f = footprints[k];
                        if (TownDistance(f.center) < f.radius + LandformTownClearance - .01f) return "Ridge crowds a town at " + f.center;
                        foreach (var pin in Pins)
                            if (pin.kind != "town" && Vector2.Distance(f.center, XZ(pin.position)) < f.radius + LandformPinClearance - .01f) return "Ridge crowds " + pin.label + " at " + f.center;
                    }
                    continue;
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
        /// <summary>
        /// Distance from a point to the drive-through under the nearest arch (the strip between its legs, the arch's depth
        /// plus 3 m either side); negative inside. Scenery and cover rocks keep out; navigation treats it as open ground.
        /// </summary>
        public float ArchSpanDistance(Vector2 p)
        {
            float best = ObstacleQueryReach;
            foreach (var arch in arches)
            {
                Vector2 a = footprints[arch.first].center, b = footprints[arch.first + 1].center, span = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, span) / Mathf.Max(.001f, span.sqrMagnitude));
                best = Mathf.Min(best, Vector2.Distance(p, a + span * t) - (arch.archDepth * .5f + 3));
            }
            return best;
        }
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
            int target = Mathf.RoundToInt(Rand(random, MinLandforms, MaxLandforms) * Mathf.Clamp(scale * scale, 1, 4));
            var shape = new List<Vector3>();
            for (int attempt = 0; attempt < target * 80 && landforms.Count < target; attempt++)
            {
                var center = new Vector2(Rand(random, -half * .92f, half * .92f), Rand(random, -half * .92f, half * .92f));
                if (!InOutline(center / size)) continue;
                // Every map gets at least one arch: the last slot tries for one first if none has been placed.
                bool wantArch = landforms.Count == target - 1 && ArchPoints.Count == 0 && attempt < target * 60;
                // The guaranteed arch looks for red rock first, then settles for anywhere.
                if (wantArch && attempt < target * 30 && BiomeAt(new Vector3(center.x, 0, center.y)).redRock < .3f) continue;
                var kind = wantArch ? LandformKind.Arch : PickLandform(random, center);
                // Arches are small and fit where big formations do not, so without a cap they crowd the mix.
                if (kind == LandformKind.Arch && ArchPoints.Count >= (size < 1000 ? 1 : size < 2000 ? 3 : 4)) continue;
                float yaw = Rand(random, 0, 360), height = ShapeLandform(kind, random, shape);
                // Basin and range: the Sonoran's desert ranges run north-west to south-east, roughly parallel.
                if (kind == LandformKind.Cliff && BiomeAt(new Vector3(center.x, 0, center.y)).sonoran > .5f) yaw = 45 + Rand(random, -20, 20);
                Quaternion turn = Quaternion.Euler(0, yaw, 0);
                float reach = 0;
                for (int i = 0; i < shape.Count; i++)
                {
                    Vector3 local = turn * new Vector3(shape[i].x, 0, shape[i].y);
                    shape[i] = new Vector3(center.x + local.x, center.y + local.z, shape[i].z);
                    reach = Mathf.Max(reach, Vector2.Distance(center, new Vector2(shape[i].x, shape[i].y)) + shape[i].z);
                }
                // A long ridge that clips a road or crowds a town is trimmed to its longest clear run, not dropped.
                bool ridge = kind == LandformKind.Cliff;
                if (ridge && !TrimRidge(random, shape, ref center, ref reach)) continue;
                if (!LandformFits(center, reach, height, shape, ridge)) continue;
                var landform = new Landform { kind = kind, center = center, reach = reach, height = height, yaw = yaw, first = footprints.Count, count = shape.Count,
                    variant = shapeVariant, shapeSeed = random.Next(), arch = kind == LandformKind.Arch ? shapeArch : Vector4.zero, archDepth = shapeArchDepth };
                if (kind == LandformKind.Arch)
                {
                    // Under the arch is open ground: record it for the drive-through check.
                    ArchPoints.Add(new Vector3(center.x, 0, center.y));
                    arches.Add(landform);
                }
                int previous = -1;
                for (int i = 0; i < shape.Count; i++)
                {
                    var circle = shape[i];
                    float rise = i < shapeHeight.Count ? shapeHeight[i] : 1;
                    int next = AddFootprint(new Vector2(circle.x, circle.y), circle.z, height * rise);
                    bool pass = kind == LandformKind.Cliff && i < shapePass.Count && shapePass[i];
                    if (kind == LandformKind.Cliff && !pass) LinkSpine(previous, next);
                    if (pass)
                    {
                        Footprint a = footprints[previous], b = footprints[next];
                        Vector2 dir = (b.center - a.center).normalized;
                        Vector2 mid = ((a.center + dir * a.radius) + (b.center - dir * b.radius)) * .5f;
                        PassPoints.Add(new Vector3(mid.x, 0, mid.y));
                        PassWidths.Add(Vector2.Distance(a.center, b.center) - a.radius - b.radius);
                    }
                    previous = next;
                }
                landforms.Add(landform);
                if (kind == LandformKind.Cliff) RidgeCount++;
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
                int segments = Mathf.CeilToInt(Vector2.Distance(a, b) / (radius * 1.1f));
                var chain = new Landform { kind = LandformKind.Cliff, boundary = true, center = (a + b) * .5f,
                    inward = inward, reach = radius, height = Rand(random, 26, 38), yaw = Mathf.Atan2(-tangent.y, tangent.x) * Mathf.Rad2Deg,
                    first = footprints.Count };
                int previous = -1;
                float phase = Rand(random, 0, Mathf.PI * 2), widthA = Rand(random, 0, 6.3f), widthB = Rand(random, 0, 6.3f), riseA = Rand(random, 0, 6.3f);
                Vector2 lastCenter = Vector2.zero; float lastRadius = 0;
                var buttresses = new List<Vector4>(); // centre xy, radius, height
                for (int i = 0; i <= segments; i++)
                {
                    float u = i / (float)segments;
                    // The rim swells, thins, rises and dips along each edge instead of repeating one block.
                    float width = Mathf.Clamp(.85f + .3f * Mathf.Sin(u * Mathf.PI * 3.7f + widthA) + .15f * Mathf.Sin(u * Mathf.PI * 9.1f + widthB) + Rand(random, -.07f, .07f), .62f, 1.45f);
                    float rise = Mathf.Clamp(.85f + .25f * Mathf.Sin(u * Mathf.PI * 4.3f + riseA) + .12f * Mathf.Sin(u * Mathf.PI * 10.7f + riseA * 1.3f) + Rand(random, -.1f, .1f), .55f, 1.35f);
                    float r = radius * width;
                    float wander = Mathf.Sin(u * Mathf.PI * 5 + phase) * .18f + Mathf.Sin(u * Mathf.PI * 11 + phase * .7f) * .08f;
                    Vector2 center = Vector2.Lerp(a, b, u) + inward * r * (-.18f + wander);
                    // It must stay sealed: neighbours always overlap (and so cover the outline between them).
                    if (previous >= 0) { float need = Vector2.Distance(lastCenter, center) * 1.2f; if (lastRadius + r < need) r = need - lastRadius; }
                    int next = AddFootprint(center, r, chain.height * rise);
                    LinkSpine(previous, next);
                    previous = next; lastCenter = center; lastRadius = r;
                    BoundaryRockCount++;
                    // Now and then a buttress juts inward from the rim, away from roads, towns and objectives.
                    if (random.NextDouble() < .12)
                    {
                        float br = r * Rand(random, .45f, .75f);
                        Vector2 at = center + inward * r * Rand(random, .8f, 1.2f) + tangent * r * Rand(random, -.3f, .3f);
                        if (ButtressFits(at, br)) buttresses.Add(new Vector4(at.x, at.y, br, chain.height * rise * Rand(random, .5f, .85f)));
                    }
                }
                foreach (var buttress in buttresses) AddFootprint(new Vector2(buttress.x, buttress.y), buttress.z, buttress.w);
                chain.count = footprints.Count - chain.first;
                boundaryChains.Add(chain);
            }
        }

        bool ButtressFits(Vector2 at, float radius)
        {
            if (!InOutline(at / size) || RoadDistance(at) < radius + 14 || TownDistance(at) < radius + 80) return false;
            if (riverWidth > 0 && Mathf.Abs(at.x - RiverX(at.y)) < radius + riverWidth + 12) return false;
            foreach (var pin in Pins) if (pin.kind != "town" && Vector2.Distance(at, XZ(pin.position)) < radius + 40) return false;
            return true;
        }

        /// <summary>The landform suited to the biome at a site (see <see cref="BiomeAt"/>).</summary>
        LandformKind PickLandform(System.Random random, Vector2 site)
        {
            double roll = random.NextDouble();
            var biome = BiomeAt(new Vector3(site.x, 0, site.y));
            // Red rock is monument country: mesas, buttes, spires and arches, with few ridges.
            if (biome.redRock > .5f) return roll < .15 ? LandformKind.Cliff : roll < .45 ? LandformKind.Mesa : roll < .8 ? LandformKind.Butte : roll < .93 ? LandformKind.Arch : LandformKind.Boulders;
            // Canyon and plateau country: long ridges and broad mesas.
            if (biome.plateau > .5f) return roll < .62 ? LandformKind.Cliff : roll < .84 ? LandformKind.Mesa : roll < .93 ? LandformKind.Butte : LandformKind.Boulders;
            // The Rim forest: ridges and boulder piles.
            if (biome.forest > .5f) return roll < .74 ? LandformKind.Cliff : roll < .92 ? LandformKind.Boulders : LandformKind.Mesa;
            // Sonoran: desert ranges, boulder piles and the odd butte.
            return roll < .7 ? LandformKind.Cliff : roll < .88 ? LandformKind.Boulders : roll < .96 ? LandformKind.Butte : LandformKind.Mesa;
        }

        /// <summary>Fills <paramref name="shape"/> with local footprint circles (x, z, radius) and returns the landform height.</summary>
        float ShapeLandform(LandformKind kind, System.Random random, List<Vector3> shape)
        {
            shape.Clear(); shapeHeight.Clear(); shapePass.Clear();
            switch (kind)
            {
                // Monument Valley proportions: broad tables and tall, slender buttes, with the talus slope inside the footprint.
                case LandformKind.Mesa: shapeVariant = 0; shape.Add(new Vector3(0, 0, Rand(random, 26, 38))); return Rand(random, 26, 38);
                case LandformKind.Butte:
                {
                    double roll = random.NextDouble();
                    float r = Rand(random, 13, 19), height = Rand(random, 32, 46);
                    shape.Add(new Vector3(0, 0, r));
                    if (roll < .35)
                    {
                        // Mitten: a thin spire stands just off one shoulder, overlapping the butte's talus.
                        shapeVariant = (int)ButteVariant.Mitten;
                        shape.Add(new Vector3(r * .95f, 0, r * Rand(random, .45f, .55f)));
                    }
                    else if (roll < .6)
                    {
                        // A cluster of two or three needle spires, each on its own talus cone, leaning on the first.
                        shapeVariant = (int)ButteVariant.Spires;
                        shape[0] = new Vector3(0, 0, r * .6f);
                        int count = random.Next(2, 4);
                        for (int i = 1; i < count; i++)
                        {
                            var lean = shape[random.Next(shape.Count)];
                            float sr = r * Rand(random, .4f, .55f), angle = Rand(random, 0, Mathf.PI * 2), gap = (lean.z + sr) * Rand(random, .6f, .8f);
                            shape.Add(new Vector3(lean.x + Mathf.Cos(angle) * gap, lean.y + Mathf.Sin(angle) * gap, sr));
                        }
                    }
                    else shapeVariant = (int)ButteVariant.Plain;
                    return height;
                }
                case LandformKind.Arch:
                {
                    // Two leg footprints; the opening between them stays open ground, so cars drive under the arch.
                    float inner = Rand(random, 9, 13), innerHeight = Rand(random, 9, 14), leg = Rand(random, 6, 9), lintel = Rand(random, 5, 8), depth = Rand(random, 7, 10);
                    shapeArch = new Vector4(inner, innerHeight, leg, lintel); shapeArchDepth = depth;
                    float legRadius = .5f * Mathf.Sqrt(leg * leg * 1.25f + depth * depth) + .5f;
                    shape.Add(new Vector3(inner + leg * .5f, 0, legRadius)); shape.Add(new Vector3(-(inner + leg * .5f), 0, legRadius));
                    shapeVariant = 0;
                    return innerHeight + lintel;
                }
                case LandformKind.Cliff:
                {
                    // A long, uneven spine; two bends prevent a capsule-shaped silhouette from above.
                    float scale = size < 1000 ? .4f : Mathf.Clamp(size / 1600f, 1, 1.3f);
                    float length = Rand(random, 150, 240) * scale, thickness = Rand(random, 10, 13) * Mathf.Sqrt(scale);
                    float bow = Rand(random, -.13f, .13f) * length;
                    float kink = Rand(random, -.075f, .075f) * length;
                    int stacks = Mathf.CeilToInt(length / (thickness * 1.2f)) + 1;
                    float widthA = Rand(random, 0, 6.3f), widthB = Rand(random, 0, 6.3f), riseA = Rand(random, 0, 6.3f), riseB = Rand(random, 0, 6.3f);
                    for (int i = 0; i < stacks; i++)
                    {
                        float u = i / (float)(stacks - 1), x = (u - .5f) * length;
                        // Stacks wander off the spine line, so the ridge spreads and pinches instead of running as a row.
                        float z = bow * Mathf.Sin(u * Mathf.PI) + kink * Mathf.Sin(u * Mathf.PI * 2) + thickness * Rand(random, -.22f, .22f);
                        // Width and height drift slowly along the ridge, with jitter, and taper to low shoulders at the ends.
                        // Sin(pi) is a hair below zero in floats; unclamped, its square root made the last stack's width NaN.
                        float ends = Mathf.Sqrt(Mathf.Max(0, Mathf.Sin(u * Mathf.PI)));
                        float width = (.8f + .22f * Mathf.Sin(u * Mathf.PI * 2.3f + widthA) + .12f * Mathf.Sin(u * Mathf.PI * 5.1f + widthB) + Rand(random, -.06f, .06f)) * Mathf.Lerp(.7f, 1, ends);
                        float rise = (.82f + .22f * Mathf.Sin(u * Mathf.PI * 3 + riseA) + .14f * Mathf.Sin(u * Mathf.PI * 7.3f + riseB) + Rand(random, -.08f, .08f)) * Mathf.Lerp(.5f, 1, ends);
                        shape.Add(new Vector3(x, z, thickness * width)); shapeHeight.Add(rise); shapePass.Add(false);
                    }
                    OpenPasses(random, shape);
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
        /// Interior ridges get car-sized passes (one per ~125 m of ridge): a stack is dropped and the rock shoulders either side are narrowed
        /// until 8-11 m of open ground remains. Everywhere else neighbouring stacks are made to overlap, so the ridge stays
        /// sealed apart from its passes. The spine (and the barriers that follow it) breaks at each pass.
        /// </summary>
        void OpenPasses(System.Random random, List<Vector3> shape)
        {
            int count = shape.Count;
            var removed = new bool[count];
            double roll = random.NextDouble();
            // Every ridge long enough for one gets a pass, about one per 125 m, so long ranges never seal off ground.
            int passes = count < 8 ? 0 : Mathf.Max(1, Mathf.RoundToInt(count / 9f + (float)roll - .5f));
            for (int p = 0, attempt = 0; p < passes && attempt < 20; attempt++)
            {
                int index = random.Next(2, count - 2);
                bool crowded = false;
                for (int k = index - 2; k <= index + 2; k++) crowded |= removed[k];
                if (crowded) continue;
                removed[index] = true; p++;
            }
            var keptShape = new List<Vector3>(); var keptHeight = new List<float>(); var keptPass = new List<bool>();
            bool passNext = false;
            for (int i = 0; i < count; i++)
            {
                if (removed[i]) { passNext = true; continue; }
                keptShape.Add(shape[i]); keptHeight.Add(shapeHeight[i]); keptPass.Add(passNext); passNext = false;
            }
            shape.Clear(); shape.AddRange(keptShape); shapeHeight.Clear(); shapeHeight.AddRange(keptHeight); shapePass.Clear(); shapePass.AddRange(keptPass);
            var shoulder = new bool[shape.Count];
            for (int i = 1; i < shape.Count; i++)
            {
                if (!shapePass[i]) continue;
                float room = Vector2.Distance(shape[i - 1], shape[i]) - Rand(random, 8, 11);
                if (room < 6) { shapePass[i] = false; continue; } // too short a span for a pass between real shoulders
                shoulder[i - 1] = shoulder[i] = true;
                float sum = shape[i - 1].z + shape[i].z;
                if (sum > room) { float s = room / sum; shape[i - 1] = Scaled(shape[i - 1], s); shape[i] = Scaled(shape[i], s); }
            }
            for (int i = 1; i < shape.Count; i++)
            {
                if (shapePass[i]) continue;
                float need = Vector2.Distance(shape[i - 1], shape[i]) * 1.15f, sum = shape[i - 1].z + shape[i].z;
                if (sum >= need) continue;
                // Grow the stacks that are not pass shoulders, so passes keep their width.
                if (shoulder[i - 1] && !shoulder[i]) shape[i] = Grown(shape[i], need - sum);
                else if (shoulder[i] && !shoulder[i - 1]) shape[i - 1] = Grown(shape[i - 1], need - sum);
                else { float s = need / sum; shape[i - 1] = Scaled(shape[i - 1], s); shape[i] = Scaled(shape[i], s); }
            }
        }
        static Vector3 Scaled(Vector3 circle, float s) => new Vector3(circle.x, circle.y, circle.z * s);
        static Vector3 Grown(Vector3 circle, float extra) => new Vector3(circle.x, circle.y, circle.z + extra);

        /// <summary>
        /// A landform may not touch a road (roads are the only guaranteed links between towns), crowd a town, a point of
        /// interest or the river, stand near another landform (a car always fits between two), leave the state outline,
        /// or straddle ground so uneven that its base would float.
        /// </summary>
        bool LandformFits(Vector2 center, float reach, float height, List<Vector3> shape, bool perStack = false)
        {
            // Ridges are long: their town, objective and spacing rules apply per stack (TrimRidge), not to one circle
            // round the whole ridge, which would keep any ridge a quarter-kilometre from every town.
            if (!perStack)
            {
                if (TownDistance(center) < reach + LandformTownClearance) return false;
                foreach (var pin in Pins) if (pin.kind != "town" && Vector2.Distance(center, XZ(pin.position)) < reach + LandformPinClearance) return false;
                foreach (var other in landforms) if (Vector2.Distance(center, other.center) < reach + other.reach + LandformSpacing) return false;
            }
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
            // Each ridge rock is seated on its own ground, so a long ridge may cross rolling country.
            return high - low < height * (perStack ? .9f : .55f);
        }

        /// <summary>
        /// Keeps the longest run of a ridge's stacks that clears every road, town, objective, the river, the outline and
        /// other landforms (each stack by its own radius). Fails when fewer than six stacks survive.
        /// </summary>
        bool TrimRidge(System.Random random, List<Vector3> shape, ref Vector2 center, ref float reach)
        {
            int bestStart = 0, bestLength = 0, start = 0;
            for (int i = 0; i <= shape.Count; i++)
            {
                bool clear = i < shape.Count && StackClear(new Vector2(shape[i].x, shape[i].y), shape[i].z);
                if (clear) continue;
                if (i - start > bestLength) { bestLength = i - start; bestStart = start; }
                start = i + 1;
            }
            if (bestLength < 6) return false;
            if (bestLength < shape.Count)
            {
                var keptShape = shape.GetRange(bestStart, bestLength);
                var keptHeight = shapeHeight.Count == shape.Count ? shapeHeight.GetRange(bestStart, bestLength) : null;
                var keptPass = shapePass.Count == shape.Count ? shapePass.GetRange(bestStart, bestLength) : null;
                shape.Clear(); shape.AddRange(keptShape);
                if (keptHeight != null) { shapeHeight.Clear(); shapeHeight.AddRange(keptHeight); }
                if (keptPass != null) { shapePass.Clear(); shapePass.AddRange(keptPass); shapePass[0] = false; }
                // The cut ends taper like natural ones.
                shape[0] = Scaled(shape[0], .8f); shape[shape.Count - 1] = Scaled(shape[shape.Count - 1], .8f);
                // A long run that lost its passes to the cut gets new ones, so it cannot seal ground off.
                if (shape.Count >= 8 && !shapePass.Contains(true) && shapePass.Count == shape.Count && shapeHeight.Count == shape.Count) OpenPasses(random, shape);
            }
            center = Vector2.zero; foreach (var c in shape) center += new Vector2(c.x, c.y); center /= shape.Count;
            reach = 0; foreach (var c in shape) reach = Mathf.Max(reach, Vector2.Distance(center, new Vector2(c.x, c.y)) + c.z);
            return true;
        }

        bool StackClear(Vector2 c, float r)
        {
            if (!InOutline(c / size) || !InOutline((c + new Vector2(r * 1.3f, 0)) / size) || !InOutline((c - new Vector2(r * 1.3f, 0)) / size)
                || !InOutline((c + new Vector2(0, r * 1.3f)) / size) || !InOutline((c - new Vector2(0, r * 1.3f)) / size)) return false;
            if (RoadDistance(c) < r + LandformRoadClearance || TownDistance(c) < r + LandformTownClearance) return false;
            if (riverWidth > 0 && Mathf.Abs(c.x - RiverX(c.y)) < r + riverWidth + LandformRiverClearance) return false;
            foreach (var pin in Pins) if (pin.kind != "town" && Vector2.Distance(c, XZ(pin.position)) < r + LandformPinClearance) return false;
            // Exact distance to every other landform's rock (the cell query is capped at 48 m, short of the spacing).
            foreach (var other in landforms)
                for (int k = other.first; k < other.first + other.count; k++)
                    if (Vector2.Distance(c, footprints[k].center) - footprints[k].radius < r + LandformSpacing) return false;
            return true;
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
            int target = Mathf.RoundToInt(Rand(random, 30, 44) * Mathf.Clamp(scale * scale, 1, 4));
            var objectives = new List<Vector2>();
            foreach (var junction in TrailJunctions) objectives.Add(XZ(junction));
            foreach (var pin in Pins) if (pin.kind != "town") objectives.Add(XZ(pin.position));
            int start = landforms.Count;
            foreach (var objective in objectives)
            {
                if (landforms.Count - start >= target * .6f) break;
                if (random.NextDouble() > .7) continue;
                int rocks = random.Next(2, 5); float ring = Rand(random, 24, 36), turn = Rand(random, 0, Mathf.PI * 2);
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
            // Footprint radius bounds a slab 13-18 m long (three to four car lengths); 3-5.5 m tall hides a car and stops rounds.
            float radius = Rand(random, MinSolidRockRadius, 9), height = Rand(random, 3, 5.5f), yaw = Rand(random, 0, 360);
            if (!InOutline(p / size) || RoadDistance(p) < radius + CoverRoadClearance || TownDistance(p) < radius + CoverTownClearance) return false;
            if (TrailEdgeDistance(p) < radius + 1.5f || ObstacleDistance(p) < radius + CoverGap || ArchSpanDistance(p) < radius) return false;
            if (riverWidth > 0 && Mathf.Abs(p.x - RiverX(p.y)) < radius + riverWidth + 4) return false;
            foreach (var objective in objectives) if ((objective - p).sqrMagnitude < (radius + CoverObjectiveClearance) * (radius + CoverObjectiveClearance)) return false;
            Vector3 at = new Vector3(p.x, 0, p.y);
            float slope = Mathf.Abs(SampleHeight(at + Vector3.right * radius) - SampleHeight(at - Vector3.right * radius)) + Mathf.Abs(SampleHeight(at + Vector3.forward * radius) - SampleHeight(at - Vector3.forward * radius));
            if (slope > radius * .9f) return false;
            landforms.Add(new Landform { kind = LandformKind.Cover, center = p, reach = radius, height = height, yaw = yaw, first = footprints.Count, count = 1 });
            AddFootprint(p, radius, height, true);
            return true;
        }

        /// <summary>Distance from a point to the nearest trail junction.</summary>
        float JunctionDistance(Vector2 p)
        {
            float best = float.MaxValue;
            foreach (var junction in TrailJunctions) best = Mathf.Min(best, Vector2.Distance(p, XZ(junction)));
            return best;
        }

        void BuildLandforms()
        {
            if (landforms.Count == 0 && boundaryChains.Count == 0) return;
            Transform root = Group("Landforms and cover", transform, Vector3.zero);
            landformMaterial = new Material(desert) { name = "Landform rock (see-through over the car)" };
            landformMaterial.SetFloat("_OccluderCut", 1);
            var catalog = DesertLandformCatalog.Load();
            keepPackColours = catalog && catalog.keepPackColours; packRockMaterials.Clear();
            monumentMaterial = new Material(landformMaterial) { name = "Monument sandstone (see-through over the car)" };
            monumentMaterial.SetTexture("_ModelAlbedo", MonumentMesh.StrataTexture); monumentMaterial.SetColor("_ModelTint", Color.white);
            monumentMaterial.SetFloat("_UseModelAlbedo", 1); monumentMaterial.SetFloat("_ModelGrade", 0);
            monumentMaterial.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
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
                if (landform.kind == LandformKind.Mesa || landform.kind == LandformKind.Butte || landform.kind == LandformKind.Arch)
                {
                    BuildMonument(landform, root);
                    // Fallen blocks round the talus; never under an arch's span.
                    if (landform.kind != LandformKind.Arch) ScatterBaseRocks(landform, root, catalog, random);
                    continue;
                }
                if (landform.kind == LandformKind.Cliff && catalog && catalog.HasMountainRocks)
                {
                    // Each footprint carries a broad rock mass plus a taller crag set off to one side, from a mix of the packs'
                    // large rocks at random turns, so the ridge reads as rugged stone with a broken skyline. The barriers
                    // below keep the chain sealed between the rocks.
                    var chain = Group(landform.boundary ? "Boundary mountain chain" : "Mountain chain", root, Vector3.zero);
                    for (int k = landform.first; k < landform.first + landform.count; k++)
                    {
                        var f = footprints[k];
                        // Beside a pass the rock stays inside its footprint, so the planned opening stays drivable.
                        bool shoulder = !landform.boundary && IsPassShoulder(landform, k);
                        PlaceScaledModel(catalog.PickMountainRock(random), f.center, f.radius * (shoulder ? Rand(random, .85f, .95f) : Rand(random, .92f, 1.15f)), f.height * Rand(random, .55f, .85f),
                            Rand(random, 0, 360), chain, catalog.useTerrainMaterial, "Mountain rock", shoulder ? default : Tilt(random), shoulder);
                        // None, one or two crags per footprint, so peaks cluster and thin out rather than one per block.
                        double roll = random.NextDouble();
                        int crags = roll < .25 ? 0 : roll < .75 ? 1 : 2;
                        for (int c = 0; c < crags; c++)
                        {
                            float angle = Rand(random, 0, Mathf.PI * 2);
                            float cragRadius = f.radius * Rand(random, .35f, .7f);
                            float offset = shoulder ? Mathf.Max(0, f.radius * .9f - cragRadius) * Rand(random, 0, 1) : f.radius * Rand(random, .15f, .6f);
                            Vector2 crag = f.center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * offset;
                            PlaceScaledModel(catalog.PickMountainRock(random), crag, cragRadius, f.height * Rand(random, .75f, 1.25f),
                                Rand(random, 0, 360), chain, catalog.useTerrainMaterial, "Mountain crag", shoulder ? default : Tilt(random), shoulder);
                        }
                        // Occasional outlying boulders break up the chain's edge.
                        if (!shoulder && random.NextDouble() < .22)
                        {
                            float angle = Rand(random, 0, Mathf.PI * 2), radius = Mathf.Max(MinSolidRockRadius, f.radius * Rand(random, .25f, .42f));
                            Vector2 at = f.center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * f.radius * Rand(random, 1f, 1.25f);
                            if (RoadDistance(at) >= radius + 6 && TrailEdgeDistance(at) >= radius + 1.5f && TownDistance(at) >= radius + CoverTownClearance && ArchSpanDistance(at) >= radius && JunctionDistance(at) >= radius + CoverObjectiveClearance)
                                if (PlaceScaledModel(catalog.PickMountainRock(random), at, radius, Mathf.Max(3, f.height * Rand(random, .2f, .45f)),
                                    Rand(random, 0, 360), chain, catalog.useTerrainMaterial, "Mountain outcrop", Tilt(random)))
                                    AddFootprint(at, radius * .85f, f.height * .3f, true);
                        }
                    }
                    AddChainBarriers(landform, chain);
                    ScatterBaseRocks(landform, chain, catalog, random);
                    continue;
                }
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
                    // Cover rocks are slabs: long across their yaw, narrower along it (still two car lengths and more).
                    Vector2 stretch = landform.kind == LandformKind.Cover ? new Vector2(1, Rand(random, .65f, .85f)) : Vector2.one;
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

        /// <summary>
        /// Our own Monument Valley formations (see <see cref="MonumentMesh"/>), each seated on the lowest ground under its
        /// footprints so no edge floats; the buried skirt absorbs the rest of the slope.
        /// </summary>
        void BuildMonument(Landform landform, Transform root)
        {
            var mesh = new MonumentMesh(landform.shapeSeed);
            var random = new System.Random(landform.shapeSeed ^ 0x5bd1e995);
            float Seat(Footprint f)
            {
                float low = SampleHeight(new Vector3(f.center.x, 0, f.center.y));
                for (int s = 0; s < 8; s++)
                {
                    float angle = s * Mathf.PI * .25f; Vector2 q = f.center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * f.radius * .95f;
                    low = Mathf.Min(low, SampleHeight(new Vector3(q.x, 0, q.y)));
                }
                return low - .4f;
            }
            Vector3 At(Footprint f, float y) => new Vector3(f.center.x, y, f.center.y);
            var first = footprints[landform.first];
            string label;
            switch (landform.kind)
            {
                case LandformKind.Mesa:
                    mesh.Loft(MonumentMesh.Formation.Mesa, At(first, Seat(first)), first.radius, first.height, Rand(random, .08f, .22f));
                    label = "Mesa"; break;
                case LandformKind.Arch:
                {
                    var other = footprints[landform.first + 1];
                    float seat = Mathf.Min(Seat(first), Seat(other));
                    mesh.Arch(new Vector3(landform.center.x, seat, landform.center.y), Quaternion.Euler(0, landform.yaw, 0),
                        landform.arch.x, landform.arch.y, landform.arch.z, landform.arch.w, landform.archDepth);
                    label = "Arch"; break;
                }
                default:
                    if (landform.variant == (int)ButteVariant.Spires)
                    {
                        for (int k = landform.first; k < landform.first + landform.count; k++)
                        {
                            var f = footprints[k];
                            mesh.Loft(MonumentMesh.Formation.Spire, At(f, Seat(f)), f.radius, f.height * (k == landform.first ? 1 : Rand(random, .6f, .9f)));
                        }
                        label = "Spires";
                    }
                    else
                    {
                        mesh.Loft(MonumentMesh.Formation.Butte, At(first, Seat(first)), first.radius, first.height, Rand(random, 0, .12f));
                        if (landform.variant == (int)ButteVariant.Mitten)
                        {
                            var thumb = footprints[landform.first + 1];
                            mesh.Loft(MonumentMesh.Formation.Spire, At(thumb, Seat(thumb)), thumb.radius, first.height * Rand(random, .75f, .9f));
                        }
                        label = landform.variant == (int)ButteVariant.Mitten ? "Mitten butte" : "Butte";
                    }
                    break;
            }
            var go = new GameObject("Landform • " + label, typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
            go.transform.SetParent(root, false);
            var built = mesh.ToMesh(go.name);
            go.GetComponent<MeshFilter>().sharedMesh = built; go.GetComponent<MeshRenderer>().sharedMaterial = monumentMaterial;
            go.GetComponent<MeshCollider>().sharedMesh = built;
            go.AddComponent<GeneratedMeshOwner>().Mesh = built;
        }

        void AddChainBarriers(Landform landform, Transform root)
        {
            // Asset silhouettes have crevices. Overlapping simple colliders close those gaps along the impassable spine.
            // Only along the spine: passes in interior ridges stay open, and buttresses need no seal.
            for (int k = landform.first; k < landform.first + landform.count - 1; k++)
            {
                if (footprints[k].nextSpine != k + 1) continue;
                var a = footprints[k]; var b = footprints[k + 1];
                Vector2 delta = b.center - a.center, center = (a.center + b.center) * .5f;
                float low = Mathf.Min(SampleHeight(new Vector3(a.center.x, 0, a.center.y)), SampleHeight(new Vector3(b.center.x, 0, b.center.y))) - 3;
                float top = Mathf.Max(SampleHeight(new Vector3(a.center.x, 0, a.center.y)), SampleHeight(new Vector3(b.center.x, 0, b.center.y))) + Mathf.Max(a.height, b.height) * .8f;
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
            float radius = landform.kind == LandformKind.Cover ? Mathf.Max(MinSolidRockRadius, landform.reach * .95f) : landform.reach * .95f;
            return PlaceScaledModel(prefab, landform.center, radius, landform.height, landform.yaw,
                root, terrainMaterial, prefab.name + " • " + landform.kind);
        }

        /// <summary>A ridge footprint that borders a pass (its spine link to a neighbour is broken).</summary>
        bool IsPassShoulder(Landform landform, int k)
        {
            if (landform.kind != LandformKind.Cliff) return false;
            int last = landform.first + landform.count - 1;
            return (k < last && footprints[k].nextSpine != k + 1) || (k > landform.first && footprints[k - 1].nextSpine != k);
        }
        Vector2 Tilt(System.Random random) => new Vector2(Rand(random, -7, 7), Rand(random, -7, 7));

        bool PlaceScaledModel(GameObject prefab, Vector2 center, float radius, float height, float yaw, Transform root,
            bool terrainMaterial, string label, Vector2 tilt = default, bool strict = false)
        {
            if (!prefab) return false;
            var go = Instantiate(prefab, root);
            go.name = label;
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) { Destroy(go); return false; }
            go.transform.SetPositionAndRotation(new Vector3(center.x, 0, center.y), Quaternion.Euler(tilt.x, yaw, tilt.y));
            go.transform.localScale = Vector3.one;
            Bounds bounds = ModelBounds(renderers);
            // Strict: fit the bounds' diagonal, so even a corner of the rotated model stays inside the radius (beside passes).
            float footprint = Mathf.Max(.01f, strict ? new Vector2(bounds.size.x, bounds.size.z).magnitude * .5f : Mathf.Max(bounds.size.x, bounds.size.z) * .5f), tall = Mathf.Max(.01f, bounds.size.y);
            float horizontal = radius / footprint;
            float vertical = Mathf.Clamp(height / tall, horizontal * .75f, horizontal * 2.4f);
            go.transform.localScale = new Vector3(horizontal, vertical, horizontal);
            // Pack pivots are rarely at the mesh centre, and scaling multiplies the gap (cliffs landed 4-10 m and mesas up
            // to 30 m off their planned footprints, the boundary rim partly past the terrain edge). Centre the model's
            // bounds on the footprint, which navigation, spawns and the road and town clearances all read.
            bounds = ModelBounds(renderers);
            go.transform.position += new Vector3(center.x - bounds.center.x, 0, center.y - bounds.center.z);
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
                foreach (var renderer in renderers) { var materials = renderer.sharedMaterials; for (int i = 0; i < materials.Length; i++) materials[i] = keepPackColours ? PackRockMaterial(materials[i]) : landformMaterial; renderer.sharedMaterials = materials; }
            if (!go.GetComponentInChildren<Collider>())
                foreach (var filter in go.GetComponentsInChildren<MeshFilter>()) if (filter.sharedMesh) filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
            return true;
        }

        /// <summary>
        /// The terrain rock shader carrying a pack material's own texture and colour (the packs ship built-in Standard
        /// materials, which URP cannot draw). Keeps the terrain lighting and the see-through dither over the car, and
        /// draws both faces so one-sided models (cliff faces) never vanish from behind. One material per source.
        /// </summary>
        Material PackRockMaterial(Material source)
        {
            if (!source) return landformMaterial;
            if (packRockMaterials.TryGetValue(source, out var material)) return material;
            material = new Material(landformMaterial) { name = source.name + " (terrain-lit pack rock)" };
            Texture albedo = source.HasProperty("_BaseMap") ? source.GetTexture("_BaseMap") : null;
            if (!albedo && source.HasProperty("_MainTex")) albedo = source.GetTexture("_MainTex");
            Color tint = source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white;
            if (albedo) material.SetTexture("_ModelAlbedo", albedo);
            material.SetColor("_ModelTint", tint);
            material.SetFloat("_UseModelAlbedo", 1);
            material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            packRockMaterials[source] = material;
            return material;
        }
        void ScatterBaseRocks(Landform landform, Transform root, DesertLandformCatalog catalog, System.Random random)
        {
            int stride = landform.boundary ? 3 : landform.kind == LandformKind.Cliff ? 2 : 1;
            for (int k = landform.first; k < landform.first + landform.count; k += stride)
            {
                if (!landform.boundary && IsPassShoulder(landform, k)) continue;
                var f = footprints[k];
                for (int side = -1; side <= 1; side += 2)
                {
                    float angle = Rand(random, 0, Mathf.PI * 2);
                    Vector2 direction = landform.boundary ?
                        (landform.inward + new Vector2(-landform.inward.y, landform.inward.x) * side * .28f).normalized :
                        new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * side;
                    // Big fallen blocks, at least three car lengths across: clearly an obstacle to steer round. The old
                    // 6-8 m stones read as pebbles yet stopped cars dead, like hooks for the tyres.
                    float radius = Rand(random, MinSolidRockRadius, 8.5f), height = Rand(random, 3, 5);
                    Vector2 at = f.center + direction * f.radius * Rand(random, .7f, .85f);
                    // Larger stones reach further from the mountain: keep them off roads, trails, junctions and town pads.
                    if (RoadDistance(at) < radius + 4 || TrailEdgeDistance(at) < radius + 1.5f || TownDistance(at) < radius + CoverTownClearance || ArchSpanDistance(at) < radius || JunctionDistance(at) < radius + CoverObjectiveClearance) continue;
                    var rock = catalog ? catalog.Pick(LandformKind.Cover, random) : null;
                    if (!rock || !PlaceScaledModel(rock, at, radius, height,
                        Rand(random, 0, 360), root, catalog.useTerrainMaterial, "Mountain base rock"))
                    {
                        Vector3 p = new Vector3(at.x, SampleHeight(new Vector3(at.x, 0, at.y)), at.y);
                        Boulder(root, p, radius * 1.4f);
                    }
                    // Navigation, spawns and scenery steer clear of it like any other cover rock.
                    AddFootprint(at, radius * .85f, height, true);
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
