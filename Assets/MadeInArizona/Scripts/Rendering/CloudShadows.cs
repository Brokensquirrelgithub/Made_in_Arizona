using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace MadeInArizona
{
    /// <summary>
    /// Occasional cloud shadows drifting across the desert. A tileable cloud map is the sun's URP light cookie, so every
    /// sunlit surface darkens under a cloud: URP Lit materials through the cookie itself, and the custom shaders through
    /// Shaders/CloudShadows.hlsl, which samples the same cookie. The map is mostly clear sky; a few large clouds cover a
    /// small share of it, so a shadow sweeps over the car now and then instead of all the time.
    /// Cumulus over the desert casts a crisp-edged shadow with a lumpy outline, so the map is fine (about 1.6 m a texel),
    /// carries small-scale billows along its outline, and fades from sun to shade over only a few metres.
    /// Tuned under Dev Tuning > Dirt &amp; Sky.
    /// </summary>
    public static class CloudShadows
    {
        /// <summary>World size of one tile of the cloud map, in metres (clouds come out roughly 120-300 m across).</summary>
        const float TileSize = 1600f;
        const int Size = 1024;
        static Texture2D cookie;
        static float[] field;
        static Task<float[]> fieldTask;
        static float threshold, builtCover = -1, builtStrength = -1;
        static Vector2 offset;

        /// <summary>State for review logs: whether the cloud map is ready and on the sun.</summary>
        internal static string Diagnostics()
        {
            var sun = GameManager.Instance ? GameManager.Instance.Sun : null;
            return "field=" + (field != null) + " task=" + (fieldTask == null ? "none" : fieldTask.Status + (fieldTask.Exception != null ? " " + fieldTask.Exception.InnerException?.Message : "")) +
                " cookie=" + (cookie != null) + " onSun=" + (sun && cookie && sun.cookie == cookie) + " threshold=" + threshold + " params=" + Shader.GetGlobalVector("_CloudShadowParams");
        }
        /// <summary>Called every frame by the camera. Clouds only pass over the open world, never the garage.</summary>
        public static void Tick(bool outdoors)
        {
            var sun = GameManager.Instance ? GameManager.Instance.Sun : null;
            if (!sun) return;
            var tuning = DevTuning.Current;
            float strength = Mathf.Clamp(tuning.cloudShadows, 0, .95f), cover = Mathf.Clamp(tuning.cloudCover, 0, .8f);
            // The fine cloud map takes a moment to compute, off the main thread; skies stay clear until it is ready.
            if (field == null)
            {
                if (fieldTask == null) fieldTask = Task.Run(BuildField);
                if (fieldTask.IsCompleted && !fieldTask.IsFaulted) field = fieldTask.Result;
            }
            if (!outdoors || strength < .01f || cover < .005f || field == null)
            {
                if (sun.cookie && sun.cookie == cookie) sun.cookie = null;
                Shader.SetGlobalVector("_CloudShadowParams", Vector4.zero);
                return;
            }
            if (!cookie || !Mathf.Approximately(cover, builtCover) || !Mathf.Approximately(strength, builtStrength)) Build(cover, strength);
            // The wind veers slowly, so the clouds do not cross the map on one rail.
            float heading = .7f + Mathf.Sin(Time.time * .004f) * .5f;
            offset += new Vector2(Mathf.Cos(heading), Mathf.Sin(heading)) * Mathf.Max(0, tuning.cloudSpeed) * Time.deltaTime;
            offset = new Vector2(Mathf.Repeat(offset.x, TileSize), Mathf.Repeat(offset.y, TileSize));
            var data = sun.GetUniversalAdditionalLightData();
            data.lightCookieSize = new Vector2(TileSize, TileSize);
            data.lightCookieOffset = offset;
            if (sun.cookie != cookie) sun.cookie = cookie;
            Shader.SetGlobalVector("_CloudShadowParams", new Vector4(1, strength, 0, 0));
        }

        /// <summary>
        /// Cloud density per texel: broad domain-warped masses (heaped, lumpy fair-weather cumulus), plus fine billows
        /// that only matter near the outline, where they break the edge into small rounded lobes.
        /// </summary>
        static float[] BuildField()
        {
            var values = new float[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float u = x / (float)Size, v = y / (float)Size;
                    float wx = SunGlint.Fbm(u, v, 3, 3, 41) - .5f, wy = SunGlint.Fbm(u, v, 3, 3, 59) - .5f;
                    float wu = Mathf.Repeat(u + wx * .14f, 1), wv = Mathf.Repeat(v + wy * .14f, 1);
                    float mass = SunGlint.Fbm(wu, wv, 4, 5, 73);
                    float billows = SunGlint.Fbm(wu, wv, 64, 3, 89) - .5f;
                    values[y * Size + x] = mass + billows * .045f;
                }
            return values;
        }

        /// <summary>
        /// Light multiplier per texel: 1 in clear sky, down to 1 - strength under a thick cloud. <paramref name="cover"/>
        /// is the share of the sky that is cloud. The edge goes from sun to full shade over roughly 3-6 m, with a
        /// slightly thinner rim before the dense core.
        /// </summary>
        static void Build(float cover, float strength)
        {
            if (!Mathf.Approximately(cover, builtCover))
            {
                var sorted = (float[])field.Clone();
                System.Array.Sort(sorted);
                threshold = sorted[Mathf.Clamp(Mathf.RoundToInt((1 - cover) * (sorted.Length - 1)), 0, sorted.Length - 1)];
            }
            builtCover = cover; builtStrength = strength;
            var pixels = new Color32[Size * Size];
            for (int i = 0; i < pixels.Length; i++)
            {
                float d = field[i];
                float shade = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(threshold - .004f, threshold + .006f, d));
                // Thicker cores block more of the sun than the thinner rim.
                shade *= Mathf.Lerp(.8f, 1, Mathf.InverseLerp(threshold, threshold + .05f, d));
                byte light = (byte)Mathf.RoundToInt(Mathf.Clamp01(1 - strength * shade) * 255);
                pixels[i] = new Color32(light, light, light, 255);
            }
            if (!cookie)
                cookie = new Texture2D(Size, Size, TextureFormat.RGBA32, true, true) { name = "Drifting cloud shadows", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            cookie.SetPixels32(pixels); cookie.Apply(true);
        }
    }
}
