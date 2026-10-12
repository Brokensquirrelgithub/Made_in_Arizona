using UnityEngine;

namespace MadeInArizona
{
    public static class HealthRules
    {
        public const int SegmentCount = 6;
        public const float ReactivationFraction = .25f;
        public static readonly string[] Labels = { "BODY", "ARMOR", "GEAR", "SUSP", "DRIVE", "ENGINE" };

        public static float PartHealth(VehiclePart part) => part == null ? 0 :
            Mathf.Max(0, part.mass) * DevTuning.Current.partHpPerKg * Mathf.Clamp(part.partDurability, 0, 125) * .01f;

        public static float[] StockSegments(float stockHealth) => new[]
        {
            stockHealth * .5f, 0f, 0f, stockHealth * .15f, stockHealth * .15f, stockHealth * .2f
        };

        public static float[] Shares(float[] segments)
        {
            float total = 0;
            for (int i = 0; i < SegmentCount; i++) total += segments[i];
            var shares = new float[SegmentCount];
            if (total <= 0) return new[] { .5f, 0, 0, .15f, .15f, .2f };
            for (int i = 0; i < SegmentCount; i++) shares[i] = segments[i] / total;
            return shares;
        }
    }
}
