using System.Collections.Generic;
using UnityEngine;

namespace MadeInArizona
{
    public sealed partial class GeneratedWorld
    {
        const float CanyonRouteGrade = .30f, CanyonWallGrade = .44f, ClearCanyonRoute = .23f;
        static readonly Color CanyonFloorColor = new Color(.78f, .64f, .48f);
        static readonly Color CanyonWallColor = new Color(.43f, .29f, .25f);
        readonly List<CanyonSite> narrows = new List<CanyonSite>();
        float[,] canyonRoutes, canyonWalls;
        internal IReadOnlyList<CanyonSite> Narrows => narrows;

        internal readonly struct CanyonSite
        {
            public readonly Vector2 center, forward, lateral;
            public readonly float halfLength, pinch, wallRun, bypassWidth, bend, phase;

            public CanyonSite(Vector2 center, Vector2 forward, float halfLength, float pinch, float wallRun,
                float bypassWidth, float bend, float phase)
            {
                this.center = center; this.forward = forward; lateral = new Vector2(-forward.y, forward.x);
                this.halfLength = halfLength; this.pinch = pinch; this.wallRun = wallRun;
                this.bypassWidth = bypassWidth; this.bend = bend; this.phase = phase;
            }

            public float HalfFloorAt(float along) => pinch * (1 + .75f * Mathf.SmoothStep(0, 1,
                Mathf.InverseLerp(.22f, .91f, Mathf.Abs(along))));

            public float BendAt(float along) => bend * Mathf.Sin(along * 2.6f + phase);

            public float BypassOffsetAt(float along) => HalfFloorAt(along) + wallRun + bypassWidth * .5f;

            public Vector3 Point(float along, float across = 0)
            {
                Vector2 p = center + forward * (along * halfLength) + lateral * (BendAt(along) + across);
                return new Vector3(p.x, 0, p.y);
            }

            public Vector3 BypassPoint(float along) => Point(along, BypassOffsetAt(along));
        }

        internal float CanyonRouteAt(Vector3 point) => canyonRoutes == null ? 0 : SampleHeightGrid(canyonRoutes, point);
        internal float CanyonWallAt(Vector3 point) => canyonWalls == null ? 0 : SampleHeightGrid(canyonWalls, point);

        void ShapeCanyonNarrows(float step, float widthScale, float heightScale)
        {
            var random = new System.Random(unchecked(seed * 87821039 + 821));
            int target = Mathf.RoundToInt(size / MinSize);
            for (int attempt = 0; attempt < target * 380 && narrows.Count < target; attempt++)
            {
                var center = new Vector2(Rand(random, -.40f, .40f), Rand(random, -.36f, .36f)) * size;
                var position = new Vector3(center.x, 0, center.y);
                if (!InOutline(center / size) || ExperimentalReliefAllowance(center, step) < 1) continue;
                var biome = BiomeAt(position);
                if (biome.forest > .55f || PeakFactor(position) > .24f || Mathf.Abs(ExperimentalReliefAt(position)) > .8f ||
                    DryLakeCoverageAt(position) > 0 || TerraceCoverageAt(position) > 0 || FanCoverageAt(position) > 0 || CanyonRouteAt(position) > 0) continue;

                bool compact = attempt >= target * 108;
                float angle = Rand(random, 0, Mathf.PI * 2), phase = Rand(random, -.65f, .65f);
                var forward = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                float halfLength = Rand(random, 83, 111) * widthScale * (compact ? .74f : 1);
                float pinch = Mathf.Max(15, Rand(random, 15, 18) * widthScale);
                float wallRun = Rand(random, 36, 44) * widthScale * (compact ? .90f : 1), bypassWidth = 40 * widthScale;
                float depth = Rand(random, 3.5f, 4.8f) * heightScale, rise = Rand(random, 4.6f, 6.6f) * heightScale;
                if (compact) { depth *= .78f; rise *= .78f; }
                var site = new CanyonSite(center, forward, halfLength, pinch, wallRun, bypassWidth, Rand(random, 4, 8) * widthScale, phase);
                if (TryShapeNarrows(site, depth, rise, step)) narrows.Add(site);
            }
        }

        bool TryShapeNarrows(CanyonSite site, float depth, float rise, float step)
        {
            float outerBlend = site.wallRun * .84f;
            float extent = site.HalfFloorAt(1) + site.wallRun + site.bypassWidth + outerBlend + site.bend;
            RectInt bounds = ReliefBounds(site.center, site.forward, site.halfLength * 1.15f, extent, step);
            var routes = new float[bounds.width, bounds.height];
            var walls = new float[bounds.width, bounds.height];
            var delta = new float[bounds.width, bounds.height];

            float run = site.halfLength * 2;
            float baseHeight = SampleHeight(site.Point(0));
            float pitch = Mathf.Clamp((SampleHeight(site.Point(1)) - SampleHeight(site.Point(-1))) / run, -.06f, .06f);
            for (int z = 0; z < bounds.height; z++)
                for (int x = 0; x < bounds.width; x++)
                {
                    int gx = bounds.xMin + x, gz = bounds.yMin + z;
                    var point = new Vector2(-half + gx * step, -half + gz * step);
                    Vector2 offset = point - site.center;
                    float along = Vector2.Dot(offset, site.forward) / site.halfLength;
                    if (Mathf.Abs(along) >= 1.13f) continue;
                    float across = Vector2.Dot(offset, site.lateral) - site.BendAt(along);
                    float floor = site.HalfFloorAt(along), rim = floor + site.wallRun;
                    float fadeFrom = rim + (across > 0 ? site.bypassWidth : 0);
                    if (Mathf.Abs(across) >= fadeFrom + outerBlend) continue;
                    float ends = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.28f, 1, Mathf.Abs(along)));
                    float sides = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(fadeFrom, fadeFrom + outerBlend, Mathf.Abs(across)));
                    float profile = ends * sides, route = CanyonRouteMask(site, along, across);
                    if (profile <= 0 && route <= 0) continue;
                    if (!InOutline(point / size))
                    {
                        if (profile > .08f || route > .02f) return false;
                        continue;
                    }

                    float allowance = ExperimentalReliefAllowance(point, step);
                    // Mouths may meet existing roads at their original grade. The floor and shelf stay fully formed inside.
                    if (route > .35f && ((ends > .65f && allowance < .98f) ||
                        (riverWidth > 0 && Mathf.Abs(point.x - RiverX(point.y)) < riverWidth + 2 * step))) return false;
                    bool overlaps = (dryLakeCoverage != null && dryLakeCoverage[gx, gz] > .12f) ||
                        (terraceCoverage != null && terraceCoverage[gx, gz] > .12f) || (fanCoverage != null && fanCoverage[gx, gz] > .12f) ||
                        (canyonRoutes != null && canyonRoutes[gx, gz] > .08f) || (canyonWalls != null && canyonWalls[gx, gz] > .08f);
                    if (overlaps)
                    {
                        if (profile > .16f || route > .24f) return false;
                        continue;
                    }
                    if ((profile > .45f || route > .45f) && Mathf.Abs(experimentalRelief[gx, gz]) > .85f) return false;

                    float bank = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(floor, rim, Mathf.Abs(across)));
                    float wallRim = rise * (1 + .10f * Mathf.Sin(along * 6.3f + site.phase + (across > 0 ? 1.3f : 0)));
                    float target = baseHeight + along * site.halfLength * pitch + Mathf.Lerp(-depth, wallRim, bank);
                    float weight = profile * allowance;
                    routes[x, z] = route;
                    walls[x, z] = 4 * bank * (1 - bank) * weight;
                    delta[x, z] = (target - heights[gx, gz]) * weight;
                }

            if (AllowedReliefScale(bounds, delta, step, fixedGrade: CanyonWallGrade) < 1 ||
                !CanyonRoutesFit(bounds, delta, routes, step)) return false;
            ApplyRelief(bounds, delta, 1);
            if (canyonRoutes == null)
            {
                canyonRoutes = new float[heights.GetLength(0), heights.GetLength(1)];
                canyonWalls = new float[heights.GetLength(0), heights.GetLength(1)];
            }
            for (int z = 0; z < bounds.height; z++)
                for (int x = 0; x < bounds.width; x++)
                {
                    if (routes[x, z] > 0) canyonRoutes[bounds.xMin + x, bounds.yMin + z] = routes[x, z];
                    if (walls[x, z] > 0) canyonWalls[bounds.xMin + x, bounds.yMin + z] = walls[x, z];
                }
            return true;
        }

        static float CanyonRouteMask(CanyonSite site, float along, float across)
        {
            float t = Mathf.Abs(along), floor = site.HalfFloorAt(along), bypass = site.BypassOffsetAt(along);
            float ends = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(1.07f, 1.13f, t));
            float passage = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(floor - 3, floor + 3, Mathf.Abs(across)));
            float shelf = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(site.bypassWidth * .38f, site.bypassWidth * .58f, Mathf.Abs(across - bypass)));
            // Join the lanes across their full width outside both mouths, where the raised walls have tapered away.
            float join = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.97f, 1.055f, t)) *
                Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-floor - 4, -floor, across)) *
                (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(bypass + 7, bypass + site.bypassWidth * .55f, across)));
            return Mathf.Max(Mathf.Max(passage, shelf), join) * ends;
        }

        bool CanyonRoutesFit(RectInt bounds, float[,] delta, float[,] routes, float step)
        {
            float riseLimit = CanyonRouteGrade * step, limitSquared = riseLimit * riseLimit;
            for (int z = 0; z < bounds.height - 1; z++)
                for (int x = 0; x < bounds.width - 1; x++)
                {
                    float ra = routes[x, z], rb = routes[x + 1, z], rc = routes[x, z + 1], rd = routes[x + 1, z + 1];
                    if (Mathf.Max(Mathf.Max(ra, rb), Mathf.Max(rc, rd)) < .40f) continue;
                    int gx = bounds.xMin + x, gz = bounds.yMin + z;
                    float a = heights[gx, gz] + delta[x, z], b = heights[gx + 1, gz] + delta[x + 1, z];
                    float c = heights[gx, gz + 1] + delta[x, z + 1], d = heights[gx + 1, gz + 1] + delta[x + 1, z + 1];
                    if (Mathf.Max(ra, Mathf.Max(rb, rc)) >= .4f && new Vector2(b - a, c - a).sqrMagnitude > limitSquared) return false;
                    if (Mathf.Max(rd, Mathf.Max(rb, rc)) >= .4f && new Vector2(d - c, d - b).sqrMagnitude > limitSquared) return false;
                }
            return true;
        }

        internal static Color TintNarrows(Color ground, float route, float wall, float strength)
        {
            Color cut = Color.Lerp(ground, CanyonFloorColor, .36f * route * strength);
            return Color.Lerp(cut, CanyonWallColor, wall * strength);
        }
    }
}
