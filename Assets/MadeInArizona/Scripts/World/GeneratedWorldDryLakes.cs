using System.Collections.Generic;
using UnityEngine;

namespace MadeInArizona
{
    public sealed partial class GeneratedWorld
    {
        const float DryLakeMaxGrade = .30f, ClearLakeFloor = .68f, ClearLakeBank = .30f;
        static readonly Color LakeShoreColor = new Color(.68f, .56f, .43f);
        static readonly Color LakeFloorColor = new Color(.91f, .85f, .72f);
        readonly List<Vector2> dryLakeCenters = new List<Vector2>();
        float[,] dryLakeCoverage;
        internal IReadOnlyList<Vector2> DryLakeCenters => dryLakeCenters;
        internal float DryLakeCoverageAt(Vector3 point) => dryLakeCoverage == null ? 0 : SampleHeightGrid(dryLakeCoverage, point);

        void ShapeDryLakes(float step, float widthScale, float heightScale)
        {
            var random = new System.Random(unchecked(seed * 1580030173 + 173));
            int target = Mathf.RoundToInt(size / MinSize);
            for (int attempt = 0; attempt < target * 160 && dryLakeCenters.Count < target; attempt++)
            {
                var center = new Vector2(Rand(random, -.43f, .43f), Rand(random, -.41f, .31f)) * size;
                var point = new Vector3(center.x, 0, center.y);
                if (!InOutline(center / size) || BiomeAt(point).sonoran < .65f || ExperimentalReliefAllowance(center, step) < 1) continue;
                if (Mathf.Abs(ExperimentalReliefAt(point)) > .65f || DryLakeCoverageAt(point) > 0) continue;

                float angle = Rand(random, 0, Mathf.PI * 2), phase = Rand(random, 0, Mathf.PI * 2);
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                // Smaller basins also fit on compact maps with more densely spaced roads and towns.
                float footprintScale = attempt >= target * 80 ? widthScale * .8f : widthScale;
                float length = Rand(random, 99, 136) * footprintScale, width = Rand(random, 72, 100) * footprintScale;
                float depth = Rand(random, 2.5f, 3.8f) * heightScale;
                if (TryShapeDryLake(center, direction, length, width, depth, phase, step)) dryLakeCenters.Add(center);
            }
        }

        bool TryShapeDryLake(Vector2 center, Vector2 direction, float length, float width, float depth, float phase, float step)
        {
            var across = new Vector2(-direction.y, direction.x);
            RectInt bounds = ReliefBounds(center, direction, length * 1.11f, width * 1.11f, step);
            var coverage = new float[bounds.width, bounds.height];
            float centerHeight = SampleHeight(new Vector3(center.x, 0, center.y));
            float floorHeight = centerHeight - depth;

            for (int z = bounds.yMin; z < bounds.yMax; z++)
                for (int x = bounds.xMin; x < bounds.xMax; x++)
                {
                    var point = new Vector2(-half + x * step, -half + z * step);
                    Vector2 offset = point - center;
                    var local = new Vector2(Vector2.Dot(offset, direction) / length, Vector2.Dot(offset, across) / width);
                    if (local.sqrMagnitude >= 1.11f * 1.11f) continue;
                    float angle = Mathf.Atan2(local.y, local.x);
                    float edge = 1 + .065f * Mathf.Sin(3 * angle + phase) + .045f * Mathf.Cos(5 * angle - phase);
                    float radius = local.magnitude / edge;
                    if (radius >= 1) continue;
                    if (!InOutline(point / size) || (dryLakeCoverage != null && dryLakeCoverage[x, z] > 0)) return false;

                    float allowance = ExperimentalReliefAllowance(point, step);
                    float profile = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.59f, 1, radius));
                    if (profile > .25f && Mathf.Abs(experimentalRelief[x, z]) > .65f) return false;
                    if (radius <= .59f)
                    {
                        if (allowance < 1) return false;
                        floorHeight = Mathf.Min(floorHeight, heights[x, z]);
                    }
                    coverage[x - bounds.xMin, z - bounds.yMin] = profile * allowance;
                }
            if (centerHeight - floorHeight > 8) return false;

            var delta = new float[bounds.width, bounds.height];
            for (int z = 0; z < bounds.height; z++)
                for (int x = 0; x < bounds.width; x++)
                    delta[x, z] = (floorHeight - heights[bounds.xMin + x, bounds.yMin + z]) * coverage[x, z];

            if (AllowedReliefScale(bounds, delta, step, fixedGrade: DryLakeMaxGrade) < 1) return false;
            ApplyRelief(bounds, delta, 1);
            if (dryLakeCoverage == null) dryLakeCoverage = new float[heights.GetLength(0), heights.GetLength(1)];
            for (int z = 0; z < bounds.height; z++)
                for (int x = 0; x < bounds.width; x++)
                    if (coverage[x, z] > 0) dryLakeCoverage[bounds.xMin + x, bounds.yMin + z] = coverage[x, z];
            return true;
        }

        static Color TintDryLake(Color color, float lake, float strength)
        {
            Color sediment = Color.Lerp(LakeShoreColor, LakeFloorColor, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.22f, .88f, lake)));
            return Color.Lerp(color, sediment, strength * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0, .56f, lake)));
        }
    }
}
