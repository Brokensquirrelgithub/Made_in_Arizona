using UnityEngine;

namespace MadeInArizona
{
    /// <summary>
    /// Procedural lens dirt for URP bloom. Bloom adds this texture multiplied by the bloom itself, so the dirt only shows
    /// inside the halo of exceptionally bright pixels (explosions, fire, sun glints) and is invisible everywhere else.
    /// It is deliberately faint: dust specks, a few out-of-focus blobs, greasy smudges and a fibre or two, with a little
    /// more grime toward the edges of the lens. If the pattern is visible on an ordinary frame, it is too strong.
    /// </summary>
    public static class LensDirt
    {
        const int Width = 512, Height = 288;
        static Texture2D texture;

        public static Texture2D Texture
        {
            get
            {
                if (texture) return texture;
                var map = new float[Width * Height];
                var random = new System.Random(4471);
                // Haze: a very faint greasy film, heavier toward the rim of the lens.
                for (int y = 0; y < Height; y++)
                    for (int x = 0; x < Width; x++)
                    {
                        float u = x / (float)Width, v = y / (float)Height;
                        float rim = Mathf.Clamp01(new Vector2((u - .5f) * 1.78f, v - .5f).magnitude * 1.6f - .35f);
                        map[y * Width + x] = SunGlint.Fbm(u, v, 5, 3, 211) * (.018f + rim * .05f);
                    }
                // Smudges: large soft smears with a mottled, fingerprint-like interior.
                for (int i = 0; i < 7; i++)
                    Smear(map, Next(random) * Width, Next(random) * Height, 30 + Next(random) * 70, 12 + Next(random) * 30, Next(random) * Mathf.PI, .04f + Next(random) * .05f, i);
                // Out-of-focus dust: soft discs with a slightly brighter rim, the way defocused specks catch light.
                for (int i = 0; i < 46; i++)
                    Bokeh(map, Next(random) * Width, Next(random) * Height, 5 + Next(random) * 17, .05f + Next(random) * .1f);
                // Fine specks closer to focus.
                for (int i = 0; i < 520; i++)
                    Disc(map, Next(random) * Width, Next(random) * Height, .6f + Next(random) * 1.7f, .12f + Next(random) * .38f);
                // A couple of fibres.
                for (int i = 0; i < 4; i++)
                    Fibre(map, random, Next(random) * Width, Next(random) * Height, 30 + Next(random) * 60, .1f + Next(random) * .08f);
                var pixels = new Color[map.Length];
                for (int i = 0; i < map.Length; i++)
                {
                    float d = Mathf.Clamp01(map[i]);
                    pixels[i] = new Color(d, d * .97f, d * .92f, 1);
                }
                texture = new Texture2D(Width, Height, TextureFormat.RGBA32, true, true) { name = "Procedural lens dirt", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
                texture.SetPixels(pixels); texture.Apply(true, true);
                return texture;
            }
        }

        static float Next(System.Random random) => (float)random.NextDouble();
        static void Add(float[] map, int x, int y, float value)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height) return;
            map[y * Width + x] += value;
        }
        static void Disc(float[] map, float cx, float cy, float radius, float value)
        {
            int reach = Mathf.CeilToInt(radius + 1);
            for (int dy = -reach; dy <= reach; dy++)
                for (int dx = -reach; dx <= reach; dx++)
                {
                    float d = new Vector2(dx + Mathf.Floor(cx) - cx, dy + Mathf.Floor(cy) - cy).magnitude;
                    Add(map, Mathf.FloorToInt(cx) + dx, Mathf.FloorToInt(cy) + dy, value * Mathf.Clamp01(radius + .5f - d));
                }
        }
        static void Bokeh(float[] map, float cx, float cy, float radius, float value)
        {
            int reach = Mathf.CeilToInt(radius + 2);
            for (int dy = -reach; dy <= reach; dy++)
                for (int dx = -reach; dx <= reach; dx++)
                {
                    float d = new Vector2(dx, dy).magnitude / radius;
                    if (d > 1.15f) continue;
                    float body = Mathf.Clamp01((1.08f - d) * 6), rim = Mathf.Exp(-Mathf.Pow((d - .92f) * 9, 2));
                    Add(map, Mathf.RoundToInt(cx) + dx, Mathf.RoundToInt(cy) + dy, value * (body * .7f + rim * .45f));
                }
        }
        static void Smear(float[] map, float cx, float cy, float length, float width, float angle, float value, int seed)
        {
            float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
            int reach = Mathf.CeilToInt(length);
            for (int dy = -reach; dy <= reach; dy++)
                for (int dx = -reach; dx <= reach; dx++)
                {
                    float along = (dx * cos + dy * sin) / length, across = (-dx * sin + dy * cos) / width;
                    float d = along * along + across * across;
                    if (d > 1) continue;
                    float x = cx + dx, y = cy + dy;
                    float mottle = SunGlint.Fbm(x / Width, y / Height, 24, 2, 300 + seed);
                    // Curved ridges, like a thumbprint wiped across the glass.
                    float ridges = .5f + .5f * Mathf.Sin((along * 3 + across * across * 2) * 9 + mottle * 4);
                    Add(map, Mathf.RoundToInt(x), Mathf.RoundToInt(y), value * (1 - d) * (.4f + mottle * .6f) * (.6f + ridges * .4f));
                }
        }
        static void Fibre(float[] map, System.Random random, float x, float y, float length, float value)
        {
            float angle = Next(random) * Mathf.PI * 2, bend = (Next(random) - .5f) * .12f;
            for (float t = 0; t < length; t += .5f)
            {
                angle += bend * .5f;
                x += Mathf.Cos(angle) * .5f; y += Mathf.Sin(angle) * .5f;
                Disc(map, x, y, .55f, value * .35f);
            }
        }
    }
}
