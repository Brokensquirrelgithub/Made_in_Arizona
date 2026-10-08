using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MadeInArizona
{
    /// <summary>Applies the developer-facing presentation controls to URP's live render state.</summary>
    public static class DevVisuals
    {
        /// <summary>Far-blur eye-depth range (start, full, radius) for the original 40 m camera framing.</summary>
        public static Vector4 OrthoDof = new Vector4(43f, 64f, 0f, 0f);
        static readonly FieldInfo AmbientOcclusionSettings = typeof(ScreenSpaceAmbientOcclusion)
            .GetField("m_Settings", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>Updates post effects, scene lighting, fog, and the configured URP SSAO feature.</summary>
        public static void Apply()
        {
            var tuning = DevTuning.Current;
            if (tuning == null) return;

            ApplyPostProcessing(tuning);
            ApplyLighting(tuning);
            ApplyAmbientOcclusion(tuning.ao);
            SunGlint.Apply(tuning);
        }

        /// <summary>The time-of-day part of <see cref="Apply"/> (sun, sky, haze and grading), cheap enough for every frame of a transition.</summary>
        public static void ApplyTimeOfDay()
        {
            var tuning = DevTuning.Current;
            if (tuning == null) return;
            ApplyPostProcessing(tuning);
            ApplyLighting(tuning);
        }

        static void ApplyPostProcessing(DevTuning tuning)
        {
            var volume = GameManager.Instance ? GameManager.Instance.PresentationVolume : null;
            var profile = volume ? volume.profile : null;
            if (profile == null) return;

            var tonemap = GetOrAdd<Tonemapping>(profile);
            tonemap.active = true;
            tonemap.mode.Override(TonemappingMode.Neutral);

            var look = TimeOfDay.Current;
            var bloom = GetOrAdd<Bloom>(profile);
            bloom.active = GameManager.Instance.Save.settings.quality > 0 && tuning.bloom > 0f;
            bloom.intensity.Override(Mathf.Clamp(tuning.bloom * (1 + look.bloom), 0f, 10f));
            // High threshold: ordinary sunlit surfaces stay crisp and only HDR highlights (sun glints, fire) bloom.
            bloom.threshold.Override(Mathf.Clamp(tuning.bloomThreshold, .1f, 10f));
            bloom.scatter.Override(.72f);
            bloom.highQualityFiltering.Override(GameManager.Instance.Save.settings.quality>=2);
            bloom.maxIterations.Override(5);
            // Lens dirt is scaled by the bloom itself, so it only appears in the halo of explosions and sun glints.
            bloom.dirtTexture.Override(LensDirt.Texture);
            bloom.dirtIntensity.Override(Mathf.Clamp(tuning.lensDirt, 0f, 10f));

            var color = GetOrAdd<ColorAdjustments>(profile);
            color.active = true;
            color.postExposure.Override(Mathf.Clamp(tuning.exposure + look.exposure, -10f, 10f));
            color.contrast.Override(Mathf.Clamp(tuning.contrast + look.contrast, -100f, 100f));
            color.saturation.Override(Mathf.Clamp(tuning.saturation + look.saturation, -100f, 100f));

            // Sunset grading: a warmer white balance, and split toning that cools the shadows and warms the highlights.
            var balance = GetOrAdd<WhiteBalance>(profile);
            balance.active = look.temperature != 0 || look.tint != 0;
            balance.temperature.Override(look.temperature);
            balance.tint.Override(look.tint);
            var toning = GetOrAdd<SplitToning>(profile);
            toning.active = look.shadowTone != look.highlightTone;
            toning.shadows.Override(look.shadowTone);
            toning.highlights.Override(look.highlightTone);
            toning.balance.Override(-10f);

            var chromatic = GetOrAdd<ChromaticAberration>(profile);
            chromatic.active = tuning.chromatic > 0f;
            chromatic.intensity.Override(Mathf.Clamp01(tuning.chromatic));

            var motionBlur = GetOrAdd<MotionBlur>(profile);
            motionBlur.active = tuning.motionBlur > 0f;
            motionBlur.mode.Override(MotionBlurMode.CameraOnly);
            motionBlur.quality.Override(GameManager.Instance.Save.settings.quality==3?MotionBlurQuality.High:MotionBlurQuality.Low);
            motionBlur.intensity.Override(Mathf.Clamp01(tuning.motionBlur));
            motionBlur.clamp.Override(.04f);

            var depthOfField = GetOrAdd<DepthOfField>(profile);
            float dof = Mathf.Clamp01(tuning.depthOfField);
            // URP's Gaussian implementation calls LinearEyeDepth without its orthographic
            // conversion, so it cannot produce valid gameplay-camera CoC. The renderer feature
            // uses this range with LinearDepthToEyeDepth instead.
            depthOfField.active = false;
            depthOfField.mode.Override(DepthOfFieldMode.Off);
            float focus = Mathf.SmoothStep(0f, 1f, dof);
            // Eye-depth range for the original 40 m framing; CameraController adds its backed-off distance.
            OrthoDof = new Vector4(43f, 64f, Mathf.Lerp(0f, 3.2f, focus), 0f);
            Shader.SetGlobalVector("_ArizonaOrthoDofParams", OrthoDof + new Vector4(CameraController.DepthOffset, CameraController.DepthOffset, 0, 0));
            CameraController.RefreshDepthEffects();

            var vignette = GetOrAdd<Vignette>(profile);
            float vignetteAmount = Mathf.Clamp01(tuning.vignette + look.vignette);
            vignette.active = vignetteAmount > 0f;
            vignette.intensity.Override(vignetteAmount);
            vignette.color.Override(Color.black);
            vignette.center.Override(new Vector2(.5f, .5f));
            vignette.rounded.Override(true);
            vignette.smoothness.Override(Mathf.Lerp(.48f, .78f, vignetteAmount));
        }

        static void ApplyLighting(DevTuning tuning)
        {
            // Midday or sunset (TimeOfDay): sun height and colour, the sky's fill light, haze and background.
            var look = TimeOfDay.Current;
            if (GameManager.Instance != null && GameManager.Instance.Sun != null)
            {
                GameManager.Instance.Sun.transform.rotation = look.sun;
                GameManager.Instance.Sun.color = look.sunColor;
                GameManager.Instance.Sun.intensity = look.sunIntensity * Mathf.Clamp(tuning.sunlight, 0f, 3f);
                RenderSettings.sun = GameManager.Instance.Sun;
            }

            float ambient = Mathf.Clamp(tuning.ambient, 0f, 3f);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = look.ambientSky * ambient;
            RenderSettings.ambientEquatorColor = look.ambientEquator * ambient;
            RenderSettings.ambientGroundColor = look.ambientGround * ambient;
            RenderSettings.fog = tuning.haze > 0f;
            RenderSettings.fogColor = look.fog;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = Mathf.Clamp(tuning.haze * look.fogScale, 0f, .03f);
            var camera = CameraController.Instance ? CameraController.Instance.GetComponent<Camera>() : Camera.main;
            if (camera) camera.backgroundColor = look.background;
            Shader.SetGlobalVector("_SkySunset", new Vector4(Mathf.SmoothStep(0, 1, TimeOfDay.Blend), 0, 0, 0));
            TimeOfDay.ApplySky(Mathf.SmoothStep(0, 1, TimeOfDay.Blend), TimeOfDay.Settled);
        }

        static T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
        {
            return profile.TryGet<T>(out var component) ? component : profile.Add<T>();
        }

        // URP exposes SSAO's quality settings only through its renderer feature. Reflection keeps this runtime
        // control in player builds while the editor setup below creates the real feature and its shader variants.
        static void SetAmbientOcclusionField(object settings, string name, float value)
        {
            var field = settings.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field != null && field.FieldType == typeof(float)) field.SetValue(settings, value);
        }
        static void SetAmbientOcclusionEnum(object settings, string name, int value)
        {
            var field = settings.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field != null && field.FieldType.IsEnum) field.SetValue(settings, System.Enum.ToObject(field.FieldType, value));
        }
        static void SetAmbientOcclusionBool(object settings, string name, bool value)
        {
            var field = settings.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field != null && field.FieldType == typeof(bool)) field.SetValue(settings, value);
        }
        static void ApplyAmbientOcclusion(float amount)
        {
            if (AmbientOcclusionSettings == null) return;
            foreach (var feature in Resources.FindObjectsOfTypeAll<ScreenSpaceAmbientOcclusion>()) {
                object settings = AmbientOcclusionSettings.GetValue(feature);
                if (settings == null) continue;
                amount = Mathf.Clamp01(amount);
                // Scaled for the distant orthographic camera: the stock 3.5 cm radius was invisible from there, and the
                // 80 m falloff faded occlusion out entirely, since the lens sits more than 80 m from the car.
                SetAmbientOcclusionField(settings, "Intensity", amount * 4f);
                SetAmbientOcclusionField(settings, "Radius", .3f + amount * .9f);
                SetAmbientOcclusionField(settings, "Falloff", 2000f);
                SetAmbientOcclusionField(settings, "DirectLightingStrength", .15f + amount * .5f);
                // Grain and flicker came from the renderer asset's SSAO setup: blue noise, which swaps its texture every
                // frame (it expects temporal anti-aliasing, which this game does not use), 4 samples, half resolution and
                // the Kawase blur. The asset now uses fixed interleaved-gradient noise, 8 samples and medium normal
                // reconstruction. Those are shader keywords, so they stay as set there: the build strips variants the
                // asset does not use. Resolution and blur are not keywords and follow the graphics preset.
                int quality = GameManager.Instance && GameManager.Instance.Save != null ? GameManager.Instance.Save.settings.quality : 2;
                SetAmbientOcclusionEnum(settings, "BlurQuality", quality >= 2 ? 0 : 1);     // bilateral, else Gaussian
                SetAmbientOcclusionBool(settings, "Downsample", quality < 2);
                feature.SetActive(amount > 0f);
            }
        }
    }
}
