using UnityEngine;

namespace MadeInArizona
{
    public enum Biome { Sonoran, RedRock, Forest, Plateau }

    /// <summary>Blend of the biomes at one point (the weights sum to 1).</summary>
    public struct BiomeWeights
    {
        public float sonoran, redRock, forest, plateau;
        public Biome Dominant
        {
            get
            {
                float best = sonoran; Biome biome = Biome.Sonoran;
                if (redRock > best) { best = redRock; biome = Biome.RedRock; }
                if (forest > best) { best = forest; biome = Biome.Forest; }
                if (plateau > best) biome = Biome.Plateau;
                return biome;
            }
        }
    }

    /// <summary>
    /// Arizona's biomes laid out as in the real state rather than in north-south bands. The Mogollon Rim runs diagonally
    /// from the west-north-west to the east-centre and carries a belt of ponderosa forest. North of it the Colorado Plateau
    /// is red rock in the east (Monument Valley, the Painted Desert) and grey-brown canyon and plateau country in the west.
    /// South of it lies the Sonoran desert, and a pocket of red rock sits just below the Rim (Sedona). Every border is
    /// warped by the seed's own noise, so each map differs but still reads as Arizona.
    /// </summary>
    public sealed partial class GeneratedWorld
    {
        // The Rim (normalised x, z), from the west edge to the east edge.
        static readonly Vector2[] RimLine = { new Vector2(-.5f, .1f), new Vector2(-.25f, .16f), new Vector2(-.05f, .19f), new Vector2(.15f, .1f), new Vector2(.35f, 0), new Vector2(.5f, -.04f) };
        static readonly Vector2 SedonaPocket = new Vector2(-.07f, .1f);
        public static readonly Color SonoranGround = new Color(.76f, .62f, .40f), RedRockGround = new Color(.70f, .33f, .18f),
            ForestGround = new Color(.25f, .37f, .20f), PlateauGround = new Color(.46f, .39f, .32f);

        public static BiomeWeights BiomesAt(Vector3 p) => Active ? Active.BiomeAt(p) : new BiomeWeights { sonoran = 1 };

        public BiomeWeights BiomeAt(Vector3 p)
        {
            float nx = p.x / size, nz = p.z / size;
            // Low-frequency warp: borders meander by up to ~5% of the map, differently for every seed.
            float wx = nx + (Noise(nx * 3.3f + 11, nz * 3.3f - 7) - .5f) * .1f, wz = nz + (Noise(nx * 3.1f - 5, nz * 3.1f + 13) - .5f) * .1f;
            // The config's "scrub" threshold (default .52) moves the Rim north or south.
            float rim = RimZ(wx) + (cfg.biomeThresholds.scrub - .52f);
            float d = wz - rim;
            float forest = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.035f, .085f, Mathf.Abs(d - .015f)));
            float north = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-.02f, .06f, d));
            float east = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-.12f, .06f, wx + (Noise(nx * 2.2f + 3, nz * 2.2f + 29) - .5f) * .16f));
            var w = new BiomeWeights
            {
                forest = forest,
                sonoran = (1 - north) * (1 - forest),
                redRock = north * (1 - forest) * east,
                plateau = north * (1 - forest) * (1 - east),
            };
            // Sedona: red rock spilling below the Rim.
            float pocket = (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.025f, .065f, Vector2.Distance(new Vector2(wx, wz), SedonaPocket)))) * .85f;
            if (pocket > 0) { w.redRock += pocket * (w.sonoran + w.forest); w.sonoran *= 1 - pocket; w.forest *= 1 - pocket; }
            float sum = w.sonoran + w.redRock + w.forest + w.plateau;
            if (sum > 0) { w.sonoran /= sum; w.redRock /= sum; w.forest /= sum; w.plateau /= sum; }
            return w;
        }

        /// <summary>
        /// Open ground deep inside a biome (weight at least <paramref name="minWeight"/>, clear of roads, towns and rock),
        /// nearest to that biome's own centre of mass. Null when the biome is missing from this map. Deterministic.
        /// </summary>
        public Vector3? FindBiomeSpot(Biome biome, float minWeight = .8f)
        {
            const int grid = 48;
            var spots = new System.Collections.Generic.List<Vector3>(); Vector3 centre = Vector3.zero;
            for (int z = 0; z < grid; z++)
                for (int x = 0; x < grid; x++)
                {
                    var p = new Vector3(Mathf.Lerp(-half, half, (x + .5f) / grid), 0, Mathf.Lerp(-half, half, (z + .5f) / grid));
                    if (!Contains(p) || RoadDistance(XZ(p)) < 30 || TownDistance(XZ(p)) < 80 || ObstacleDistance(XZ(p)) < 12) continue;
                    var w = BiomeAt(p);
                    float weight = biome == Biome.Sonoran ? w.sonoran : biome == Biome.RedRock ? w.redRock : biome == Biome.Forest ? w.forest : w.plateau;
                    if (weight < minWeight) continue;
                    spots.Add(p); centre += p;
                }
            if (spots.Count == 0) return null;
            centre /= spots.Count;
            Vector3 best = spots[0];
            foreach (var p in spots) if ((p - centre).sqrMagnitude < (best - centre).sqrMagnitude) best = p;
            best.y = SampleHeight(best);
            return best;
        }

        static float RimZ(float x)
        {
            if (x <= RimLine[0].x) return RimLine[0].y;
            for (int i = 1; i < RimLine.Length; i++)
                if (x <= RimLine[i].x) return Mathf.Lerp(RimLine[i - 1].y, RimLine[i].y, Mathf.InverseLerp(RimLine[i - 1].x, RimLine[i].x, x));
            return RimLine[RimLine.Length - 1].y;
        }

        /// <summary>Ground tint of the biome blend; the Sonoran floor varies between pale sand and reddish earth.</summary>
        Color BiomeGround(Vector3 p)
        {
            var w = BiomeAt(p);
            Color sonoran = Color.Lerp(SonoranGround, new Color(.66f, .45f, .28f), Mathf.SmoothStep(0, 1, Noise(p.x / size * 6 + 41, p.z / size * 6 - 3)) * .6f);
            return sonoran * w.sonoran + RedRockGround * w.redRock + ForestGround * w.forest + PlateauGround * w.plateau;
        }
    }
}
