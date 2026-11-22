using System.Collections.Generic;
using UnityEngine;

namespace MadeInArizona
{
    public sealed partial class GeneratedWorld
    {
        const float FanMaxGrade = .30f, ClearFanApron = .26f, FanDrivingSurface = .36f;
        static readonly Color FanSedimentColor = new Color(.66f, .57f, .45f);
        static readonly Color FanGravelColor = new Color(.83f, .74f, .61f);
        readonly List<FanSite> fans = new List<FanSite>();
        float[,] fanCoverage, fanRibs;
        internal IReadOnlyList<FanSite> Fans => fans;

        internal readonly struct FanSite
        {
            public readonly Vector2 head, downhill;
            public readonly float length, width;
            public FanSite(Vector2 head, Vector2 downhill, float length, float width)
            { this.head = head; this.downhill = downhill; this.length = length; this.width = width; }

            public float WidthAt(float progress) => width * Mathf.Lerp(.16f, 1, Mathf.Pow(Mathf.Clamp01(progress), .82f));

            public Vector3 Point(float progress, float across = 0)
            {
                var lateral = new Vector2(-downhill.y, downhill.x);
                Vector2 point = head + downhill * (progress * length) + lateral * (across * WidthAt(progress));
                return new Vector3(point.x, 0, point.y);
            }
        }

        internal float FanCoverageAt(Vector3 point) => fanCoverage == null ? 0 : SampleHeightGrid(fanCoverage, point);
        internal float FanRibsAt(Vector3 point) => fanRibs == null ? 0 : SampleHeightGrid(fanRibs, point);

        void ShapeAlluvialFans(float step, float widthScale, float heightScale)
        {
            var random = new System.Random(unchecked(seed * 1453250333 + 409));
            int target = Mathf.RoundToInt(2 * size / MinSize);
            for (int attempt = 0; attempt < target * 160 && fans.Count < target; attempt++)
            {
                var middle = new Vector3(Rand(random, -.42f, .42f) * size, 0, Rand(random, -.39f, .42f) * size);
                Vector2 center = XZ(middle);
                if (!InOutline(center / size) || BiomeAt(middle).forest > .6f || ExperimentalReliefAllowance(center, step) < .99f) continue;
                if (Mathf.Abs(ExperimentalReliefAt(middle)) > .80f || DryLakeCoverageAt(middle) > 0 ||
                    TerraceCoverageAt(middle) > 0 || FanCoverageAt(middle) > 0) continue;

                float footprint = widthScale * (attempt >= target * 80 ? .83f : 1);
                float length = Rand(random, 112, 168) * footprint, width = Rand(random, 53, 82) * footprint;
                float probe = length * .40f;
                var slope = new Vector2(
                    SampleHeight(middle - Vector3.right * probe) - SampleHeight(middle + Vector3.right * probe),
                    SampleHeight(middle - Vector3.forward * probe) - SampleHeight(middle + Vector3.forward * probe));
                if (slope.magnitude < 3 * heightScale) continue;
                Vector2 downhill = slope.normalized;
                var site = new FanSite(center - downhill * (.5f * length), downhill, length, width);
                Vector3 source = site.Point(-.48f), head = site.Point(0), toe = site.Point(1);
                if (!InOutline(XZ(source) / size) || !InOutline(XZ(toe) / size)) continue;
                float headHeight = SampleHeight(head), fall = headHeight - SampleHeight(toe);
                if (fall < 3.5f * heightScale || fall > length * .16f) continue;
                // The fan starts below a real rise and opens out along the local downhill direction.
                float sourceRise = SampleHeight(source) - headHeight;
                if (sourceRise < (attempt >= target * 80 ? 2.5f : 4f) * heightScale) continue;

                float phase = Rand(random, -.65f, .65f);
                int ribCount = random.Next(3, 5);
                if (TryShapeFan(site, headHeight, fall, phase, ribCount, heightScale, step)) fans.Add(site);
            }
        }

        bool TryShapeFan(FanSite site, float headHeight, float fall, float phase, int ribCount, float heightScale, float step)
        {
            var lateralAxis = new Vector2(-site.downhill.y, site.downhill.x);
            RectInt bounds = ReliefBounds(XZ(site.Point(.5f)), site.downhill, site.length * .5f, site.width, step);
            var coverage = new float[bounds.width, bounds.height];
            var ribMask = new float[bounds.width, bounds.height];
            var delta = new float[bounds.width, bounds.height];
            float largestChange = 0;
            for (int z = 0; z < bounds.height; z++)
                for (int x = 0; x < bounds.width; x++)
                {
                    int gx = bounds.xMin + x, gz = bounds.yMin + z;
                    var point = new Vector2(-half + gx * step, -half + gz * step);
                    Vector2 offset = point - site.head;
                    float along = Vector2.Dot(offset, site.downhill) / site.length;
                    if (along <= 0 || along >= 1) continue;
                    float across = Vector2.Dot(offset, lateralAxis) / site.WidthAt(along);
                    float toe = 1 - .18f * across * across;
                    if (Mathf.Abs(across) >= 1 || along >= toe) continue;
                    if (!InOutline(point / size) || (dryLakeCoverage != null && dryLakeCoverage[gx, gz] > 0) ||
                        (terraceCoverage != null && terraceCoverage[gx, gz] > 0) || (fanCoverage != null && fanCoverage[gx, gz] > 0)) return false;

                    float ends = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0, .19f, along)) *
                        (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.70f, toe, along)));
                    float sides = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.64f, 1, Mathf.Abs(across)));
                    float profile = ends * sides;
                    float allowance = ExperimentalReliefAllowance(point, step);
                    if (profile > .34f && (allowance < .97f || Mathf.Abs(experimentalRelief[gx, gz]) > .80f)) return false;

                    // The grade eases downslope and the same angular ribs spread farther apart toward the toe.
                    float remaining = 1 - along, crown = 1 - across * across;
                    float rib = .5f + .5f * Mathf.Cos(across * Mathf.PI * ribCount + phase + .7f * along);
                    float ribStrength = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.15f, .42f, along));
                    float target = headHeight - fall * (along + .55f * along * remaining) +
                        heightScale * ((.75f + 1.55f * remaining) * crown + (rib - .5f) * .9f * ribStrength);
                    float weight = profile * allowance;
                    coverage[x, z] = weight;
                    ribMask[x, z] = rib * ribStrength * weight;
                    delta[x, z] = (target - heights[gx, gz]) * weight;
                    largestChange = Mathf.Max(largestChange, Mathf.Abs(delta[x, z]));
                }

            if (largestChange < .6f * heightScale || AllowedReliefScale(bounds, delta, step, fixedGrade: FanMaxGrade) < 1) return false;
            ApplyRelief(bounds, delta, 1);
            if (fanCoverage == null)
            {
                fanCoverage = new float[heights.GetLength(0), heights.GetLength(1)];
                fanRibs = new float[heights.GetLength(0), heights.GetLength(1)];
            }
            for (int z = 0; z < bounds.height; z++)
                for (int x = 0; x < bounds.width; x++)
                {
                    if (coverage[x, z] <= 0) continue;
                    fanCoverage[bounds.xMin + x, bounds.yMin + z] = coverage[x, z];
                    fanRibs[bounds.xMin + x, bounds.yMin + z] = ribMask[x, z];
                }
            return true;
        }

        internal static Color TintFan(Color ground, float coverage, float ribs, float strength)
        {
            Color gravel = Color.Lerp(FanSedimentColor, FanGravelColor, Mathf.Clamp01(ribs));
            return Color.Lerp(ground, gravel, Mathf.SmoothStep(0, 1, coverage) * strength);
        }
    }
}
