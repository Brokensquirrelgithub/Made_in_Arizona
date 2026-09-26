using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MadeInArizona
{
    /// <summary>Applies the developer-facing presentation controls to URP's live render state.</summary>
    public static class DevVisuals
    {
        static readonly Color DefaultAmbientSky = new Color(.70f, .80f, .92f);
        static readonly Color DefaultAmbientEquator = new Color(.68f, .59f, .45f);
        static readonly Color DefaultAmbientGround = new Color(.44f, .35f, .25f);
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
            var volume = GameManager.Instance ? GameManager.Instance.PresentationVolume : null;
            var profile = volume ? volume.profile : null;
            if (profile == null) return;

            var tonemap = GetOrAdd<Tonemapping>(profile);
            tonemap.active = true;
            tonemap.mode.Override(TonemappingMode.Neutral);

            var bloom = GetOrAdd<Bloom>(profile);
            bloom.active = GameManager.Instance.Save.settings.quality > 0 && tuning.bloom > 0f;
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
            {
                GameManager.Instance.Sun.color = new Color(1f, .95f, .83f);
                GameManager.Instance.Sun.intensity = 2.6f * Mathf.Clamp(tuning.sunlight, 0f, 3f);
                RenderSettings.sun = GameManager.Instance.Sun;
            }

            float ambient = Mathf.Clamp(tuning.ambient, 0f, 3f);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = DefaultAmbientSky * ambient;
            RenderSettings.ambientEquatorColor = DefaultAmbientEquator * ambient;
            RenderSettings.ambientGroundColor = DefaultAmbientGround * ambient;
            RenderSettings.fog = tuning.haze > 0f;
            RenderSettings.fogColor = new Color(.80f, .76f, .65f);
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = Mathf.Clamp(tuning.haze, 0f, .03f);
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
