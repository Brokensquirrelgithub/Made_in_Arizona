using UnityEngine;

namespace MadeInArizona
{
    /// <summary>
    /// Feeds the sun-glint shader code (Shaders/SunGlint.hlsl) used by car paint, reflective props and the river:
    /// the Dev Tuning > Reflections values, the procedural wear and micro-normal maps, and a virtual perspective eye.
    /// The gameplay camera is orthographic, so every pixel shares one fixed view direction; reflections of the sun
    /// are instead evaluated from an eye point behind the camera, which makes highlights slide and flash across
    /// surfaces as the camera travels.
    /// </summary>
    public static class SunGlint
    {
        /// <summary>Material types understood by the shaders (eligibility per type is a Dev Tuning slider).</summary>
        public const int Paint = 0, Chrome = 1, Glass = 2, Sign = 3, Water = 4, Metal = 5, Plastic = 6;
        /// <summary>Distance of the virtual eye behind the orthographic camera, in metres.</summary>
        const float EyeBack = 70f;
        const int Size = 256;
        static Texture2D wear, micro;

        public static void Apply(DevTuning tuning)
        {
            if (tuning == null) return;
            float tolerance = Mathf.Clamp(tuning.glintTolerance, .05f, 15f) * Mathf.Deg2Rad;
            Shader.SetGlobalVector("_GlintParams", new Vector4(Mathf.Max(0, tuning.glint), Mathf.Cos(tolerance), Mathf.Clamp01(tuning.glintBloom), Mathf.Max(.5f, tuning.glintFade)));
            Shader.SetGlobalVector("_GlintTypesA", new Vector4(tuning.glintPaint, tuning.glintChrome, tuning.glintGlass, tuning.glintSigns));
            Shader.SetGlobalVector("_GlintTypesB", new Vector4(tuning.glintWater, tuning.glintMetal, tuning.glintPlastic, 0));
            Shader.SetGlobalVector("_GlintSurface", new Vector4(Mathf.Max(1, tuning.specularPeak), Mathf.Max(0, tuning.surfaceWear), Mathf.Max(0, tuning.microNormal),
                Mathf.GammaToLinearSpace(Mathf.Max(.1f, tuning.bloomThreshold))));
            Shader.SetGlobalTexture("_GlintWearMap", WearMap);
            Shader.SetGlobalTexture("_GlintMicroNormal", MicroNormalMap);
        }

        /// <summary>Called by the camera after it moves.</summary>
        public static void UpdateEye(Transform camera)
        {
            if (!camera) { Shader.SetGlobalVector("_GlintEye", Vector4.zero); return; }
            Vector3 eye = camera.position - camera.forward * EyeBack;
            Shader.SetGlobalVector("_GlintEye", new Vector4(eye.x, eye.y, eye.z, 1));
        }

        // ------------------------------------------------------------------ procedural maps

        /// <summary>
        /// Tileable 256² wear map: R dust and grime patches, G fine scratches, B fingerprints and smears,
        /// A oxidation and paint chips. The shaders turn each into extra roughness (and less glint).
        /// </summary>
        public static Texture2D WearMap
        {
            get
            {
                if (wear) return wear;
                var dust = new float[Size * Size]; var scratch = new float[Size * Size]; var smudge = new float[Size * Size]; var oxide = new float[Size * Size];
                for (int y = 0; y < Size; y++)
                    for (int x = 0; x < Size; x++)
                    {
                        float u = x / (float)Size, v = y / (float)Size;
                        float patches = Fbm(u, v, 4, 4, 11);
                        dust[y * Size + x] = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.5f, .74f, patches)) * .9f + (Hash(x, y, 3) > .985f ? .45f : 0);
                        oxide[y * Size + x] = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.63f, .8f, Fbm(u, v, 8, 3, 29)));
                    }
                var random = new System.Random(117);
                // Scratches: short, thin strokes, mostly swirling around a few directions like wash marks.
                for (int i = 0; i < 240; i++)
                {
                    float angle = (float)(random.Next(4) * Mathf.PI / 4 + (random.NextDouble() - .5) * .7);
                    Stroke(scratch, (float)random.NextDouble() * Size, (float)random.NextDouble() * Size, angle, 6 + (float)random.NextDouble() * 48, .35f + (float)random.NextDouble() * .65f);
                }
                // Fingerprints (concentric ridges) and soft smears.
                for (int i = 0; i < 22; i++) Print(smudge, (float)random.NextDouble() * Size, (float)random.NextDouble() * Size, 4 + (float)random.NextDouble() * 5, (float)random.NextDouble() * Mathf.PI, true);
                for (int i = 0; i < 14; i++) Print(smudge, (float)random.NextDouble() * Size, (float)random.NextDouble() * Size, 10 + (float)random.NextDouble() * 18, (float)random.NextDouble() * Mathf.PI, false);
                // Chips: small hard-edged dots of bare, rough metal.
                for (int i = 0; i < 70; i++) Dot(oxide, random.Next(Size), random.Next(Size), 1 + (float)random.NextDouble() * 2.5f);
                var pixels = new Color[Size * Size];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(Mathf.Clamp01(dust[i]), Mathf.Clamp01(scratch[i]), Mathf.Clamp01(smudge[i]), Mathf.Clamp01(oxide[i]));
                wear = new Texture2D(Size, Size, TextureFormat.RGBA32, true, true) { name = "Procedural surface wear", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
                wear.SetPixels(pixels); wear.Apply(true, true);
                return wear;
            }
        }

        /// <summary>Tileable 256² micro-normal map: gentle panel waviness plus coarse orange peel.</summary>
        public static Texture2D MicroNormalMap
        {
            get
            {
                if (micro) return micro;
                var height = new float[Size * Size];
                for (int y = 0; y < Size; y++)
                    for (int x = 0; x < Size; x++)
                    {
                        float u = x / (float)Size, v = y / (float)Size;
                        height[y * Size + x] = Fbm(u, v, 4, 3, 5) * .7f + Fbm(u, v, 32, 2, 17) * .3f;
                    }
                var pixels = new Color[Size * Size];
                const float slope = 6f;
                for (int y = 0; y < Size; y++)
                    for (int x = 0; x < Size; x++)
                    {
                        float dx = (height[y * Size + (x + 1) % Size] - height[y * Size + (x + Size - 1) % Size]) * slope;
                        float dy = (height[(y + 1) % Size * Size + x] - height[(y + Size - 1) % Size * Size + x]) * slope;
                        Vector3 n = new Vector3(-dx, -dy, 1).normalized;
                        pixels[y * Size + x] = new Color(n.x * .5f + .5f, n.y * .5f + .5f, n.z * .5f + .5f, 1);
                    }
                micro = new Texture2D(Size, Size, TextureFormat.RGBA32, true, true) { name = "Procedural micro-surface normal", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
                micro.SetPixels(pixels); micro.Apply(true, true);
                return micro;
            }
        }

        internal static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xffffff) / 16777215f;
            }
        }
        /// <summary>Value noise that repeats every <paramref name="period"/> cells, so the maps tile seamlessly.</summary>
        internal static float Value(float x, float y, int period, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            int ax = ((x0 % period) + period) % period, ay = ((y0 % period) + period) % period;
            int bx = (ax + 1) % period, by = (ay + 1) % period;
            return Mathf.Lerp(Mathf.Lerp(Hash(ax, ay, seed), Hash(bx, ay, seed), fx), Mathf.Lerp(Hash(ax, by, seed), Hash(bx, by, seed), fx), fy);
        }
        internal static float Fbm(float u, float v, int period, int octaves, int seed)
        {
            float sum = 0, amplitude = .55f, norm = 0;
            for (int o = 0; o < octaves; o++)
            {
                sum += Value(u * period, v * period, period, seed + o * 31) * amplitude;
                norm += amplitude; period *= 2; amplitude *= .5f;
            }
            return sum / norm;
        }
        static void Plot(float[] map, int x, int y, float value)
        {
            int i = ((y % Size) + Size) % Size * Size + ((x % Size) + Size) % Size;
            if (value > map[i]) map[i] = value;
        }
        static void Stroke(float[] map, float x, float y, float angle, float length, float strength)
        {
            float cx = Mathf.Cos(angle), cy = Mathf.Sin(angle);
            for (float t = 0; t < length; t += .5f)
            {
                float fade = strength * Mathf.Sin(t / length * Mathf.PI);
                Plot(map, Mathf.RoundToInt(x + cx * t), Mathf.RoundToInt(y + cy * t), fade);
            }
        }
        static void Print(float[] map, float cx, float cy, float radius, float angle, bool ridges)
        {
            float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
            int reach = Mathf.CeilToInt(radius * 1.4f);
            for (int dy = -reach; dy <= reach; dy++)
                for (int dx = -reach; dx <= reach; dx++)
                {
                    float lx = (dx * cos + dy * sin) / radius, ly = (-dx * sin + dy * cos) / (radius * .72f);
                    float d = Mathf.Sqrt(lx * lx + ly * ly);
                    if (d > 1) continue;
                    float falloff = 1 - d * d;
                    float value = ridges ? falloff * (.5f + .5f * Mathf.Sin(d * radius * 2.6f)) * .8f : falloff * .4f;
                    Plot(map, Mathf.RoundToInt(cx + dx), Mathf.RoundToInt(cy + dy), value);
                }
        }
        static void Dot(float[] map, int cx, int cy, float radius)
        {
            int reach = Mathf.CeilToInt(radius);
            for (int dy = -reach; dy <= reach; dy++)
                for (int dx = -reach; dx <= reach; dx++)
                    if (dx * dx + dy * dy <= radius * radius) Plot(map, cx + dx, cy + dy, 1);
        }
    }
}
