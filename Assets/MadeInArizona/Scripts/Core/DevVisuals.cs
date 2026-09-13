using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MadeInArizona
{
    /// <summary>Applies the developer-facing presentation controls to URP's live render state.</summary>
    public static class DevVisuals
    {
        static readonly Color DefaultAmbientSky = new Color(.55f, .62f, .71f);
        static readonly Color DefaultAmbientEquator = new Color(.49f, .39f, .29f);
        static readonly Color DefaultAmbientGround = new Color(.24f, .18f, .15f);
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
        }

        static void ApplyPostProcessing(DevTuning tuning)
        {
            var profile = FindOrCreateProfile();
            if (profile == null) return;

            var bloom = GetOrAdd<Bloom>(profile);
            bloom.active = true;
            bloom.intensity.Override(Mathf.Clamp(tuning.bloom, 0f, 10f));
            bloom.threshold.Override(.95f);
            bloom.scatter.Override(.72f);
            bloom.highQualityFiltering.Override(GameManager.Instance.Save.settings.quality>=2);
            bloom.maxIterations.Override(5);

            var color = GetOrAdd<ColorAdjustments>(profile);
            color.active = true;
            color.postExposure.Override(Mathf.Clamp(tuning.exposure, -10f, 10f));
            color.contrast.Override(Mathf.Clamp(tuning.contrast, -100f, 100f));
            color.saturation.Override(Mathf.Clamp(tuning.saturation, -100f, 100f));

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
            Shader.SetGlobalVector("_ArizonaOrthoDofParams", new Vector4(43f, 64f, Mathf.Lerp(0f, 3.2f, focus), 0f));

            var vignette = GetOrAdd<Vignette>(profile);
            vignette.active = tuning.vignette > 0f;
            vignette.intensity.Override(Mathf.Clamp01(tuning.vignette));
            vignette.color.Override(Color.black);
            vignette.center.Override(new Vector2(.5f, .5f));
            vignette.rounded.Override(true);
            vignette.smoothness.Override(Mathf.Lerp(.48f, .78f, Mathf.Clamp01(tuning.vignette)));
        }

        static void ApplyLighting(DevTuning tuning)
        {
            if (GameManager.Instance != null && GameManager.Instance.Sun != null)
                GameManager.Instance.Sun.intensity = 2.15f * Mathf.Clamp(tuning.sunlight, 0f, 3f);

            float ambient = Mathf.Clamp(tuning.ambient, 0f, 3f);
            RenderSettings.ambientSkyColor = DefaultAmbientSky * ambient;
            RenderSettings.ambientEquatorColor = DefaultAmbientEquator * ambient;
            RenderSettings.ambientGroundColor = DefaultAmbientGround * ambient;
            RenderSettings.fogDensity = Mathf.Clamp(tuning.haze, 0f, .03f);
        }

        static VolumeProfile FindOrCreateProfile()
        {
            foreach (var volume in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
                if (volume.isGlobal && volume.profile != null && volume.profile.TryGet<Bloom>(out _)) return volume.profile;

            var host = new GameObject("Arizona • Developer visuals");
            var fallback = host.AddComponent<Volume>();
            fallback.isGlobal = true;
            fallback.priority = 1f;
            fallback.profile = ScriptableObject.CreateInstance<VolumeProfile>();
            return fallback.profile;
        }

        static T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
        {
            return profile.TryGet<T>(out var component) ? component : profile.Add<T>();
        }

        // URP exposes SSAO's quality settings only through its renderer feature. Reflection keeps this runtime
        // control in player builds while the editor setup below creates the real feature and its shader variants.
        static void ApplyAmbientOcclusion(float amount)
        {
            if (AmbientOcclusionSettings == null) return;
            foreach (var feature in Resources.FindObjectsOfTypeAll<ScreenSpaceAmbientOcclusion>()) {
                object settings = AmbientOcclusionSettings.GetValue(feature);
                if (settings == null) continue;
                var intensity = settings.GetType().GetField("Intensity", BindingFlags.Instance | BindingFlags.NonPublic);
                if (intensity != null) intensity.SetValue(settings, Mathf.Clamp(amount, 0f, 4f));
                feature.SetActive(amount > 0f);
            }
        }
    }
}
