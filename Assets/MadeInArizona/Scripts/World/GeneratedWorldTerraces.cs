using System.Collections.Generic;
using UnityEngine;

namespace MadeInArizona
{
    public sealed partial class GeneratedWorld
    {
        const float TerraceMaxGrade = .32f, ClearTerraceRamp = .10f, TerraceDrivingSurface = .24f;
        const float LowerShelfEdge = -.34f, UpperShelfEdge = .10f;
        internal const float LowerShelfLane = -.50f, UpperShelfLane = .29f;
        static readonly Color TerraceShelfColor = new Color(.80f, .50f, .34f);
        static readonly Color TerraceRiserColor = new Color(.48f, .27f, .19f);

        readonly List<TerraceSite> terraces = new List<TerraceSite>();
        float[,] terraceCoverage, terraceRisers;
        internal IReadOnlyList<TerraceSite> Terraces => terraces;

        internal readonly struct TerraceSite
        {
            public readonly Vector2 center, direction;
            public readonly float length, width;
            public TerraceSite(Vector2 center, Vector2 direction, float length, float width)
            { this.center = center; this.direction = direction; this.length = length; this.width = width; }

            public Vector3 Point(float along, float across)
            {
                Vector2 point = center + direction * (along * length) + new Vector2(-direction.y, direction.x) * (across * width);
                return new Vector3(point.x, 0, point.y);
            }
        }

        internal float TerraceCoverageAt(Vector3 point) => terraceCoverage == null ? 0 : SampleHeightGrid(terraceCoverage, point);
        float TerraceRiserAt(Vector3 point) => terraceRisers == null ? 0 : SampleHeightGrid(terraceRisers, point);

        void ShapeTerraces(float step, float widthScale, float heightScale)
        {
            var random = new System.Random(unchecked(seed * 1123595159 + 151));
            int target = Mathf.RoundToInt(size / MinSize);
            for (int attempt = 0; attempt < target * 180 && terraces.Count < target; attempt++)
            {
                var center = new Vector2(Rand(random, -.15f, .40f), Rand(random, .04f, .39f)) * size;
                var point = new Vector3(center.x, 0, center.y);
                if (!InOutline(center / size) || BiomeAt(point).redRock < .58f || ExperimentalReliefAllowance(center, step) < 1) continue;
                if (Mathf.Abs(ExperimentalReliefAt(point)) > .70f || TerraceCoverageAt(point) > 0 || DryLakeCoverageAt(point) > 0) continue;

                float angle = Rand(random, 0, Mathf.PI * 2), phase = Rand(random, 0, Mathf.PI * 2);
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                var across = new Vector3(-direction.y, 0, direction.x) * 36;
                if (SampleHeight(point + across) < SampleHeight(point - across)) direction = -direction;

                float footprint = widthScale * (attempt >= target * 90 ? .84f : 1);
                float length = Rand(random, 105, 144) * footprint, width = Rand(random, 72, 96) * footprint;
                float rise = Rand(random, 6.6f, 8.8f) * heightScale * (attempt >= target * 90 ? .85f : 1);
                var site = new TerraceSite(center, direction, length, width);
                if (TryShapeTerrace(site, rise, phase, step)) terraces.Add(site);
            }
        }

        bool TryShapeTerrace(TerraceSite site, float rise, float phase, float step)
        {
            RectInt bounds = ReliefBounds(site.center, site.direction, site.length, site.width * 1.07f, step);
            var coverage = new float[bounds.width, bounds.height];
            var risers = new float[bounds.width, bounds.height];
            var delta = new float[bounds.width, bounds.height];
            var across = new Vector2(-site.direction.y, site.direction.x);
            // Give the shelves a little pitch along their length so they follow the surrounding hillside.
            float fall = SampleHeight(site.Point(.42f, 0)) - SampleHeight(site.Point(-.42f, 0));
            float benchPitch = Mathf.Clamp(fall / (site.length * .84f), -.025f, .025f);
            float baseHeight = SampleHeight(site.Point(0, 0)) - .30f * rise;

            for (int z = 0; z < bounds.height; z++)
                for (int x = 0; x < bounds.width; x++)
                {
                    int gx = bounds.xMin + x, gz = bounds.yMin + z;
                    var point = new Vector2(-half + gx * step, -half + gz * step);
                    Vector2 offset = point - site.center;
                    float along = Vector2.Dot(offset, site.direction) / site.length;
                    if (Mathf.Abs(along) >= 1) continue;
                    float lateral = Vector2.Dot(offset, across) / site.width - .06f * Mathf.Sin(along * 3.5f + phase);
                    if (Mathf.Abs(lateral) >= 1) continue;
                    if (!InOutline(point / size) || (dryLakeCoverage != null && dryLakeCoverage[gx, gz] > 0) ||
                        (terraceCoverage != null && terraceCoverage[gx, gz] > 0)) return false;

                    float ends = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.43f, 1, Mathf.Abs(along)));
                    float sides = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-1, -.67f, lateral)) *
                        (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.48f, 1, lateral)));
                    float profile = ends * sides;
                    float allowance = ExperimentalReliefAllowance(point, step);
                    // Keep the ledges and both ramp lanes continuous, even near the road and mission setbacks.
                    if (profile > .16f && lateral > -.65f && lateral < .46f && allowance < .98f) return false;
                    if (profile > .35f && Mathf.Abs(experimentalRelief[gx, gz]) > .70f) return false;

                    float level = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(LowerShelfEdge, UpperShelfEdge, lateral));
                    float target = baseHeight + rise * Mathf.Lerp(.48f, 1, level) + along * site.length * benchPitch;
                    float weight = profile * allowance;
                    coverage[x, z] = weight;
                    risers[x, z] = 4 * level * (1 - level) * weight;
                    delta[x, z] = (target - heights[gx, gz]) * weight;
                }

            if (AllowedReliefScale(bounds, delta, step, fixedGrade: TerraceMaxGrade) < 1) return false;
            ApplyRelief(bounds, delta, 1);
            if (terraceCoverage == null)
            {
                terraceCoverage = new float[heights.GetLength(0), heights.GetLength(1)];
                terraceRisers = new float[heights.GetLength(0), heights.GetLength(1)];
            }
            for (int z = 0; z < bounds.height; z++)
                for (int x = 0; x < bounds.width; x++)
                {
                    if (coverage[x, z] <= 0) continue;
                    terraceCoverage[bounds.xMin + x, bounds.yMin + z] = coverage[x, z];
                    terraceRisers[bounds.xMin + x, bounds.yMin + z] = risers[x, z];
                }
            return true;
        }

        static Color TintTerrace(Color ground, float coverage, float riser, float strength)
        {
            Color sandstone = Color.Lerp(TerraceShelfColor, TerraceRiserColor, Mathf.Clamp01(riser));
            return Color.Lerp(ground, sandstone, Mathf.SmoothStep(0, 1, coverage) * strength);
        }
    }
}
