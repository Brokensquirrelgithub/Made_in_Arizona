using System.Collections.Generic;
using UnityEngine;

namespace MadeInArizona
{
    public sealed partial class GeneratedWorld
    {
        // Leave room below the navigation limit for climbing from rest and crossing slopes diagonally.
        internal const float ExperimentalMaxGrade = .36f;
        const float ClearWashDepth = 1.5f;
        float[,] experimentalRelief;
        internal int DryWashCount { get; private set; }
        internal int FoothillCount { get; private set; }

        void ShapeExperimentalTerrain(float step)
        {
            if (!cfg.experimentalTerrain || amp <= 0) return;

            int grid = Chunks * Cells;
            experimentalRelief = new float[grid + 1, grid + 1];
            var random = new System.Random(unchecked(seed * 213199517 + 137));
            var centres = new List<Vector2>();
            float widthScale = Mathf.Min(1.45f, Mathf.Sqrt(size / MinSize));
            float heightScale = Mathf.Min(1, amp / 65f);
            int targetWashes = Mathf.RoundToInt(3 * size / MinSize);
            int targetFoothills = Mathf.RoundToInt(5 * size / MinSize);

            for (int attempt = 0; attempt < (targetWashes + targetFoothills) * 18; attempt++)
            {
                if (DryWashCount == targetWashes && FoothillCount == targetFoothills) break;
                bool wash = DryWashCount < targetWashes && (FoothillCount == targetFoothills || attempt % 2 == 0);
                var center = new Vector2(Rand(random, -.44f, .44f), Rand(random, -.43f, .44f)) * size;
                if (!InOutline(center / size) || ExperimentalReliefAllowance(center, step) < .9f) continue;
                if (centres.Exists(other => (other - center).sqrMagnitude < 10000 * widthScale * widthScale)) continue;
                var biome = BiomeAt(new Vector3(center.x, 0, center.y));
                if (wash && biome.sonoran + biome.redRock < .7f) continue;
                if (!wash && (biome.forest > .75f || PeakFactor(new Vector3(center.x, 0, center.y)) > .2f)) continue;

                float angle = Rand(random, 0, Mathf.PI * 2);
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                float length = widthScale * (wash ? Rand(random, 105, 160) : Rand(random, 95, 150));
                float width = widthScale * (wash ? Rand(random, 32, 47) : Rand(random, 65, 95));
                float rise = heightScale * (wash ? -Rand(random, 4, 6) : Rand(random, 10, 16));
                float bend = wash ? Rand(random, 9, 22) * widthScale : Rand(random, 5, 13) * widthScale;
                float phase = Rand(random, 0, Mathf.PI * 2);
                if (!TryShapeRelief(center, direction, length, width, rise, bend, phase, step)) continue;

                centres.Add(center);
                if (wash) DryWashCount++;
                else FoothillCount++;
            }
            ShapeDryLakes(step, widthScale, heightScale);
            ShapeTerraces(step, widthScale, heightScale);
            ShapeAlluvialFans(step, widthScale, heightScale);
            ShapeCanyonNarrows(step, widthScale, heightScale);
        }

        float ExperimentalReliefAllowance(Vector2 point, float step)
        {
            // Protect the road mesh and its sampling envelope, town pads, river banks and authored objectives.
            float roadMargin = RoadKeepOut + 2 * step;
            float allowance = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(roadMargin, roadMargin + 44, RoadDistance(point)));
            allowance *= Mathf.SmoothStep(0, 1, Mathf.InverseLerp(72, 132, TownDistance(point)));
            if (riverWidth > 0)
            {
                float bank = Mathf.Abs(point.x - RiverX(point.y)) - RiverReach(point.y);
                allowance *= Mathf.SmoothStep(0, 1, Mathf.InverseLerp(2 * step, 2 * step + 40, bank));
            }
            if (cfg.pins != null)
                foreach (var pin in cfg.pins)
                    if (pin != null)
                        allowance = Mathf.Min(allowance, Mathf.SmoothStep(0, 1,
                            Mathf.InverseLerp(26, 62, Vector2.Distance(point, XZ(pin.position)))));
            return allowance;
        }

        bool TryShapeRelief(Vector2 center, Vector2 direction, float length, float width, float rise, float bend, float phase, float step)
        {
            var across = new Vector2(-direction.y, direction.x);
            RectInt bounds = ReliefBounds(center, direction, length, width + bend, step);
            var delta = new float[bounds.width, bounds.height];
            bool wash = rise < 0;
            float deepest = 0;
            for (int z = bounds.yMin; z < bounds.yMax; z++)
                for (int x = bounds.xMin; x < bounds.xMax; x++)
                {
                    var point = new Vector2(-half + x * step, -half + z * step);
                    Vector2 local = point - center;
                    float along = Vector2.Dot(local, direction) / length;
                    if (Mathf.Abs(along) >= 1) continue;
                    float lateral = Mathf.Abs(Vector2.Dot(local, across) - bend * Mathf.Sin(along * 3.4f + phase));
                    if (lateral >= width) continue;
                    if (!InOutline(point / size)) return false;

                    float taper = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.38f, 1, Mathf.Abs(along)));
                    // A broad wash floor and softly rising banks. Foothills have two rounded shoulders and a low saddle.
                    float profile = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(wash ? width * .25f : 0, width, lateral));
                    if (!wash) profile *= Mathf.Lerp(.48f, 1, Mathf.SmoothStep(0, 1, Mathf.Abs(along) / .4f));
                    float change = rise * taper * profile * ExperimentalReliefAllowance(point, step);
                    delta[x - bounds.xMin, z - bounds.yMin] = change;
                    deepest = Mathf.Max(deepest, Mathf.Abs(change));
                }

            float scale = AllowedReliefScale(bounds, delta, step);
            if (scale < .55f || deepest * scale < Mathf.Abs(rise) * .45f) return false;
            ApplyRelief(bounds, delta, scale);
            return true;
        }

        RectInt ReliefBounds(Vector2 center, Vector2 direction, float length, float width, float step)
        {
            // Include one untouched row beyond the relief so its seam is checked too.
            float extentX = Mathf.Abs(direction.x) * length + Mathf.Abs(direction.y) * width;
            float extentZ = Mathf.Abs(direction.y) * length + Mathf.Abs(direction.x) * width;
            int grid = Chunks * Cells;
            int x0 = Mathf.Max(0, Mathf.FloorToInt((center.x + half - extentX) / step) - 1);
            int z0 = Mathf.Max(0, Mathf.FloorToInt((center.y + half - extentZ) / step) - 1);
            int x1 = Mathf.Min(grid, Mathf.CeilToInt((center.x + half + extentX) / step) + 1);
            int z1 = Mathf.Min(grid, Mathf.CeilToInt((center.y + half + extentZ) / step) + 1);
            return new RectInt(x0, z0, x1 - x0 + 1, z1 - z0 + 1);
        }

        float AllowedReliefScale(RectInt bounds, float[,] delta, float step, float fixedGrade = 0)
        {
            float scale = 1;
            for (int z = 0; z < bounds.height - 1; z++)
                for (int x = 0; x < bounds.width - 1; x++)
                {
                    float a = delta[x, z], b = delta[x + 1, z], c = delta[x, z + 1], d = delta[x + 1, z + 1];
                    int gx = bounds.xMin + x, gz = bounds.yMin + z;
                    float ha = heights[gx, gz], hb = heights[gx + 1, gz], hc = heights[gx, gz + 1], hd = heights[gx + 1, gz + 1];
                    scale = Mathf.Min(scale, ReliefScale(new Vector2(hb - ha, hc - ha), new Vector2(b - a, c - a), step, fixedGrade));
                    scale = Mathf.Min(scale, ReliefScale(new Vector2(hd - hc, hd - hb), new Vector2(d - c, d - b), step, fixedGrade));
                    if (scale == 0) return 0;
                }
            return scale;
        }

        void ApplyRelief(RectInt bounds, float[,] delta, float scale)
        {
            for (int z = bounds.yMin; z < bounds.yMax; z++)
                for (int x = bounds.xMin; x < bounds.xMax; x++)
                {
                    float change = delta[x - bounds.xMin, z - bounds.yMin] * scale;
                    heights[x, z] += change;
                    experimentalRelief[x, z] += change;
                }
        }

        // Each pair describes both legs of one terrain triangle. The triangle inequality bounds the final grade in
        // all directions, including downhill and diagonals. Leave triangles with zero change in grade alone.
        static float ReliefScale(Vector2 original, Vector2 change, float step, float fixedGrade)
        {
            float magnitude = change.magnitude;
            if (magnitude == 0) return 1;
            // Replacing the slope needs to preserve the finished floor or bench, so accept the whole patch or skip it.
            float allowedRise = fixedGrade * step;
            if (fixedGrade > 0) return (original + change).sqrMagnitude <= allowedRise * allowedRise ? 1 : 0;
            return Mathf.Clamp01((ExperimentalMaxGrade * step - original.magnitude) / magnitude);
        }

        internal float ExperimentalReliefAt(Vector3 point) => experimentalRelief == null ? 0 : SampleHeightGrid(experimentalRelief, point);

        static Color TintDryWash(Color color, float relief)
        {
            float amount = .48f * Mathf.InverseLerp(-.35f, -3.5f, relief);
            return Color.Lerp(color, new Color(.78f, .68f, .51f), amount);
        }

        internal static Color MapReliefColor(Color color, float relief, float lake, float terrace = 0, float riser = 0)
        {
            if (terrace > 0) return TintTerrace(color, terrace, riser, .68f);
            if (lake > 0) return TintDryLake(color, lake, .84f);
            if (relief < 0) return Color.Lerp(color, new Color(.72f, .54f, .35f), .48f * Mathf.InverseLerp(-.35f, -3.5f, relief));
            if (relief <= .8f) return color;
            Color foothill = new Color(color.r * 1.12f, color.g * .89f, color.b * .76f);
            return Color.Lerp(color, foothill, .68f * Mathf.InverseLerp(.8f, 7, relief));
        }

        bool ExperimentalFloorNear(Vector2 center, float radius)
        {
            if (experimentalRelief == null || (DryWashCount == 0 && dryLakeCoverage == null && terraceCoverage == null && fanCoverage == null && canyonRoutes == null)) return false;
            int grid = Chunks * Cells;
            float step = size / grid;
            // Include a cell at the edge of the object's footprint when checking for drivable features.
            float reach = radius + step;
            int x0 = Mathf.Clamp(Mathf.CeilToInt((center.x + half - reach) / step), 0, grid);
            int x1 = Mathf.Clamp(Mathf.FloorToInt((center.x + half + reach) / step), 0, grid);
            int z0 = Mathf.Clamp(Mathf.CeilToInt((center.y + half - reach) / step), 0, grid);
            int z1 = Mathf.Clamp(Mathf.FloorToInt((center.y + half + reach) / step), 0, grid);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    if (experimentalRelief[x, z] <= -ClearWashDepth || (dryLakeCoverage != null && dryLakeCoverage[x, z] >= ClearLakeBank) ||
                        (terraceCoverage != null && terraceCoverage[x, z] >= ClearTerraceRamp) ||
                        (fanCoverage != null && fanCoverage[x, z] >= ClearFanApron) ||
                        (canyonRoutes != null && canyonRoutes[x, z] >= ClearCanyonRoute)) return true;
            return false;
        }
    }
}
