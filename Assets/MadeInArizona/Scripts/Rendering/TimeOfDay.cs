using UnityEngine;

namespace MadeInArizona
{
    /// <summary>
    /// Midday or sunset (Settings > Display &amp; Access > Time of day). Everything a time-of-day system changes for
    /// a desert sunset: a low western sun throwing long shadows, warm orange sunlight with a blue-violet dusk sky
    /// filling the shadows, dusky haze and background, a sunset skybox for reflections (URP Lit materials and the
    /// river) and the matching sky in the custom shaders' reflections (_SkySunset), warmer grading with cool shadows
    /// and warm highlights, a little less exposure, softer cloud shadows, and headlights on (VehicleLights).
    /// Switching eases over a few seconds, so the sun visibly goes down. Midday is exactly the original look.
    /// </summary>
    public static class TimeOfDay
    {
        public struct Look
        {
            public Quaternion sun;
            public Color sunColor, ambientSky, ambientEquator, ambientGround, fog, background;
            public Color shadowTone, highlightTone;
            public float sunIntensity, fogScale, exposure, contrast, saturation, temperature, tint, vignette, bloom, clouds;
        }

        static readonly Look Midday = new Look
        {
            sun = Quaternion.Euler(68, -35, 0), sunColor = new Color(1f, .95f, .83f), sunIntensity = 2.6f,
            ambientSky = new Color(.70f, .80f, .92f), ambientEquator = new Color(.68f, .59f, .45f), ambientGround = new Color(.44f, .35f, .25f),
            fog = new Color(.80f, .76f, .65f), fogScale = 1, background = new Color(.64f, .78f, .88f),
            shadowTone = new Color(.5f, .5f, .5f), highlightTone = new Color(.5f, .5f, .5f), clouds = 1
        };
        // The sun sits 15 degrees up in the west-southwest, so shadows stretch about four times an object's height
        // toward the east-northeast: across the screen and slightly away from the camera, leaving the faces the
        // camera sees partly lit.
        static readonly Look Sunset = new Look
        {
            sun = Quaternion.Euler(15, 72, 0), sunColor = new Color(1f, .56f, .27f), sunIntensity = 2.5f,
            ambientSky = new Color(.31f, .34f, .58f), ambientEquator = new Color(.64f, .41f, .35f), ambientGround = new Color(.21f, .15f, .15f),
            fog = new Color(.78f, .47f, .37f), fogScale = 1.5f, background = new Color(.36f, .27f, .36f),
            shadowTone = new Color(.42f, .44f, .62f), highlightTone = new Color(.68f, .55f, .42f),
            exposure = -.2f, contrast = 8, saturation = 8, temperature = 8, tint = 3, vignette = .1f, bloom = .25f, clouds = .55f
        };

        const float TransitionSeconds = 3f;
        static float blend = -1;
        static Material sky;
        static float refreshedAt = -1;
        static float skyDayThickness = 1, skyDayExposure = 1.3f;
        static Color skyDayTint = new Color(.5f, .5f, .5f), skyDayGround = new Color(.37f, .35f, .34f);

        static float Target
        {
            get
            {
                var game = GameManager.Instance;
                return game != null && game.Save != null && game.Save.settings.sunset ? 1 : 0;
            }
        }
        /// <summary>0 at midday, 1 at sunset; starts at the saved setting and eases toward it after a change.</summary>
        public static float Blend
        {
            get
            {
                if (blend < 0)
                {
                    var game = GameManager.Instance;
                    if (game == null || game.Save == null) return 0;
                    blend = Target;
                }
                return blend;
            }
        }

        /// <summary>The look at the current point of the blend.</summary>
        public static Look Current
        {
            get
            {
                float t = Mathf.SmoothStep(0, 1, Blend);
                if (t <= 0) return Midday;
                if (t >= 1) return Sunset;
                return new Look
                {
                    sun = Quaternion.Slerp(Midday.sun, Sunset.sun, t),
                    sunColor = Color.Lerp(Midday.sunColor, Sunset.sunColor, t), sunIntensity = Mathf.Lerp(Midday.sunIntensity, Sunset.sunIntensity, t),
                    ambientSky = Color.Lerp(Midday.ambientSky, Sunset.ambientSky, t), ambientEquator = Color.Lerp(Midday.ambientEquator, Sunset.ambientEquator, t),
                    ambientGround = Color.Lerp(Midday.ambientGround, Sunset.ambientGround, t),
                    fog = Color.Lerp(Midday.fog, Sunset.fog, t), fogScale = Mathf.Lerp(Midday.fogScale, Sunset.fogScale, t),
                    background = Color.Lerp(Midday.background, Sunset.background, t),
                    shadowTone = Color.Lerp(Midday.shadowTone, Sunset.shadowTone, t), highlightTone = Color.Lerp(Midday.highlightTone, Sunset.highlightTone, t),
                    exposure = Mathf.Lerp(Midday.exposure, Sunset.exposure, t), contrast = Mathf.Lerp(Midday.contrast, Sunset.contrast, t),
                    saturation = Mathf.Lerp(Midday.saturation, Sunset.saturation, t), temperature = Mathf.Lerp(Midday.temperature, Sunset.temperature, t),
                    tint = Mathf.Lerp(Midday.tint, Sunset.tint, t), vignette = Mathf.Lerp(Midday.vignette, Sunset.vignette, t),
                    bloom = Mathf.Lerp(Midday.bloom, Sunset.bloom, t), clouds = Mathf.Lerp(Midday.clouds, Sunset.clouds, t)
                };
            }
        }

        /// <summary>Called every frame by the camera (also while paused, so the change shows from the settings menu).</summary>
        public static void Tick()
        {
            if (GameManager.Instance == null || GameManager.Instance.Save == null) return;
            float from = Blend, target = Target;
            if (Mathf.Approximately(from, target)) return;
            blend = Mathf.MoveTowards(from, target, Time.unscaledDeltaTime / TransitionSeconds);
            DevVisuals.ApplyTimeOfDay();
        }
        /// <summary>True once the blend has reached the selected time of day.</summary>
        public static bool Settled => Mathf.Approximately(Blend, Target);

        /// <summary>
        /// Sky for reflections: Unity's procedural skybox, which already reddens as the sun drops, given a thicker,
        /// warmer atmosphere at sunset. The camera itself never sees it (it clears to the background colour).
        /// <paramref name="refresh"/> re-renders the environment reflection, which is too costly to redo every frame
        /// of a transition.
        /// </summary>
        public static void ApplySky(float t, bool refresh)
        {
            if (!sky)
            {
                var source = RenderSettings.skybox;
                if (!source || !source.shader || source.shader.name != "Skybox/Procedural") return;
                sky = new Material(source) { name = "Arizona sky (time of day)" };
                if (sky.HasProperty("_AtmosphereThickness")) skyDayThickness = sky.GetFloat("_AtmosphereThickness");
                if (sky.HasProperty("_Exposure")) skyDayExposure = sky.GetFloat("_Exposure");
                if (sky.HasProperty("_SkyTint")) skyDayTint = sky.GetColor("_SkyTint");
                if (sky.HasProperty("_GroundColor")) skyDayGround = sky.GetColor("_GroundColor");
                RenderSettings.skybox = sky;
            }
            sky.SetFloat("_AtmosphereThickness", Mathf.Lerp(skyDayThickness, 1.75f, t));
            sky.SetFloat("_Exposure", Mathf.Lerp(skyDayExposure, 1.05f, t));
            sky.SetColor("_SkyTint", Color.Lerp(skyDayTint, new Color(.62f, .42f, .52f), t));
            sky.SetColor("_GroundColor", Color.Lerp(skyDayGround, new Color(.27f, .19f, .18f), t));
            if (refresh && !Mathf.Approximately(t, refreshedAt)) { refreshedAt = t; DynamicGI.UpdateEnvironment(); }
        }
    }
}
