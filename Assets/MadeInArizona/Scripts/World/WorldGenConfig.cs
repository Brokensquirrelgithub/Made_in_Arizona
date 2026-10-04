using System;
using System.Collections.Generic;
using UnityEngine;

namespace MadeInArizona
{
    /// <summary>
    /// Editable inputs for a deterministic generated world. This type deliberately
    /// contains data only; generation and scene ownership live elsewhere.
    /// </summary>
    [Serializable]
    public sealed class WorldGenConfig
    {
        /// <summary>Every generated Arizona has at least this many towns; older configs asking for fewer are raised.</summary>
        public const int MinTowns = 7, MaxTowns = 10;
        public int seed = 117;
        public int townCount = MinTowns;
        public int poiCount = 18;
        public float size = 1600f;
        public float terrainHeight = 65f;
        public float vegetation = 1f;
        public float riverWidth = 10f;
        public float trailDensity = 1f;
        public BiomeThresholds biomeThresholds = new BiomeThresholds();
        public List<WorldPin> pins = new List<WorldPin>();

        /// <summary>Validates external JSON before it can replace a live world.</summary>
        public bool Validate(out string error)
        {
            // Files from before 0.8 km maps were retired still load; the generator raises them to the 1.6 km minimum.
            if (size < 800f || size > GeneratedWorld.MaxSize || !Finite(size))
            {
                error = "size must be a finite value up to 4800 (sizes under 1600 are generated at 1600).";
                return false;
            }

            if (townCount < MinTowns || townCount > MaxTowns)
            {
                error = "townCount must be between " + MinTowns + " and " + MaxTowns + ".";
                return false;
            }
            if (poiCount < 4 || poiCount > 40)
            {
                error = "poiCount must be between 4 and 40.";
                return false;
            }
            if (!Finite(terrainHeight) || terrainHeight < 0f || terrainHeight > 150f)
            {
                error = "terrainHeight must be a finite value from 0 to 150.";
                return false;
            }
            if (!Finite(vegetation) || vegetation < 0f || vegetation > 4f)
            {
                error = "vegetation must be a finite value from 0 to 4.";
                return false;
            }
            if (!Finite(riverWidth) || riverWidth < 0f || riverWidth > 30f)
            {
                error = "riverWidth must be finite, non-negative, and no more than 30 metres.";
                return false;
            }
            if (!Finite(trailDensity) || trailDensity < 0f || trailDensity > 3f)
            {
                error = "trailDensity must be a finite value from 0 to 3.";
                return false;
            }
            if (biomeThresholds == null)
            {
                error = "biomeThresholds cannot be null.";
                return false;
            }
            if (!biomeThresholds.Validate(out error)) return false;
            if (pins == null)
            {
                error = "pins cannot be null.";
                return false;
            }
            if (pins.Count > 512)
            {
                error = "pins may contain at most 512 entries.";
                return false;
            }
            for (int i = 0; i < pins.Count; i++)
            {
                if (pins[i] == null)
                {
                    error = "pins[" + i + "]: entry cannot be null.";
                    return false;
                }
                if (!pins[i].Validate(out error))
                {
                    error = "pins[" + i + "]: " + error;
                    return false;
                }
            }

            error = null;
            return true;
        }

        internal static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }

    /// <summary>Optional noise cutoffs used by terrain generators to select biomes.</summary>
    [Serializable]
    public sealed class BiomeThresholds
    {
        public float lowland = .28f;
        public float scrub = .52f;
        public float highland = .76f;

        public bool Validate(out string error)
        {
            if (!WorldGenConfig.Finite(lowland) || !WorldGenConfig.Finite(scrub) || !WorldGenConfig.Finite(highland))
            {
                error = "biome thresholds must all be finite.";
                return false;
            }
            if (lowland < 0f || lowland > scrub || scrub > highland || highland > 1f)
            {
                error = "biome thresholds must be ordered from 0 to 1 (lowland <= scrub <= highland).";
                return false;
            }
            error = null;
            return true;
        }
    }

    [Serializable]
    public sealed class WorldPin
    {
        public string id = "";
        public string label = "";
        public string kind = "poi";
        public Vector3 position;
        public int requiredTier;
        public bool discovered;

        public bool Validate(out string error)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 128)
            {
                error = "id is required and must be 128 characters or fewer.";
                return false;
            }
            if (label == null || label.Length > 256 || kind == null || kind.Length > 64)
            {
                error = "label or kind is too long.";
                return false;
            }
            if (!WorldGenConfig.Finite(position.x) || !WorldGenConfig.Finite(position.y) || !WorldGenConfig.Finite(position.z))
            {
                error = "position must contain finite coordinates.";
                return false;
            }
            if (requiredTier < 0 || requiredTier > 100)
            {
                error = "requiredTier must be between 0 and 100.";
                return false;
            }
            error = null;
            return true;
        }
    }
}
