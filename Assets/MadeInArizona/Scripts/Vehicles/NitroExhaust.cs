using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MadeInArizona
{
    /// <summary>
    /// Nitro flames from the player's tailpipes: a short jet that runs from a white-blue core at the pipe to red at
    /// its ragged tip, a flickering blue glow, a spit of sparks when the charge lights and embers left behind while
    /// it burns. Driven by <see cref="VehicleController.Boosting"/>; the roar lives in <see cref="AudioManager"/>.
    /// </summary>
    public sealed class NitroExhaust : MonoBehaviour
    {
        /// <summary>Name of the empty VehicleVisual places at each tailpipe mouth, facing out of the pipe.</summary>
        public const string ExitName = "Tailpipe exit";
        const float FlameRate = 180;
        static Material flameMaterial;
        static Texture2D flameTexture;
        VehicleController vehicle;
        Transform[] exits = new Transform[0];
        ParticleSystem[] flames = new ParticleSystem[0];
        Light glow;
        float intensity, emberAt, lastBoostAt = -10;
        bool wasBoosting;

        /// <summary>Attaches flames to the tailpipes of the vehicle's current model (Initialize rebuilds it).</summary>
        public void Bind(VehicleController owner)
        {
            vehicle = owner;
            var found = new List<Transform>();
            Collect(owner.Visual, found);
            exits = found.ToArray();
            flames = new ParticleSystem[exits.Length];
            for (int i = 0; i < exits.Length; i++) flames[i] = Flame(exits[i]);
            if (glow == null)
            {
                var go = new GameObject("Nitro glow");
                go.transform.SetParent(transform, false);
                glow = go.AddComponent<Light>();
                glow.type = LightType.Point; glow.shadows = LightShadows.None; glow.range = 5.5f;
                glow.color = new Color(.35f, .55f, 1f);
            }
            glow.enabled = false; intensity = 0; wasBoosting = false;
        }

        static void Collect(Transform parent, List<Transform> found)
        {
            if (parent == null) return;
            foreach (Transform child in parent)
            {
                if (child.name == ExitName) found.Add(child);
                Collect(child, found);
            }
        }

        void Update()
        {
            if (vehicle == null) return;
            bool boosting = vehicle.Boosting && vehicle.Damage != null && !vehicle.Damage.IsDead;
            if (boosting && !wasBoosting && Time.time - lastBoostAt > .3f) Ignite();
            if (boosting) lastBoostAt = Time.time;
            wasBoosting = boosting;
            intensity = Mathf.MoveTowards(intensity, boosting ? 1 : 0, Time.deltaTime * (boosting ? 14 : 6));
            // A quick, uneven flutter keeps the jet from reading as a steady cone.
            float flutter = 1 + .18f * Mathf.Sin(Time.time * 53) + .1f * Mathf.Sin(Time.time * 97 + 1.3f);
            float reach = Mathf.Lerp(.45f, 1, intensity) * flutter;
            foreach (var flame in flames)
            {
                if (!flame) continue;
                var emission = flame.emission; emission.rateOverTime = FlameRate * intensity;
                var main = flame.main; main.startSpeed = new ParticleSystem.MinMaxCurve(8 * reach, 12.5f * reach);
            }
            if (glow == null) return;
            Vector3 center = Vector3.zero, back = Vector3.zero;
            int live = 0;
            foreach (var exit in exits) if (exit) { center += exit.position; back += exit.forward; live++; }
            glow.enabled = intensity > .01f && live > 0;
            if (!glow.enabled) return;
            glow.transform.position = center / live + back.normalized * .7f;
            glow.intensity = intensity * 3.2f * flutter;
            if (boosting && Time.time >= emberAt)
            {
                emberAt = Time.time + .045f;
                var exit = exits[Random.Range(0, exits.Length)];
                if (exit) ExplosionSystem.Burst(exit.position + exit.forward * .9f, new Color(1, Random.Range(.2f, .5f), .08f), 1, 2.2f);
            }
        }

        /// <summary>The charge catching: a spit of blue sparks from every pipe and a small kick of the camera.</summary>
        void Ignite()
        {
            foreach (var exit in exits)
                if (exit) ExplosionSystem.Burst(exit.position + exit.forward * .3f, new Color(.45f, .75f, 1), 9, 4.5f);
            CameraController.Instance?.Shake(.12f);
        }

        ParticleSystem Flame(Transform exit)
        {
            var go = new GameObject("Nitro flame");
            go.transform.SetParent(exit, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.playOnAwake = false; main.maxParticles = 96;
            // Local space keeps the jet attached to the pipe at any speed instead of smearing into a long trail.
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.08f, .15f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(8, 12.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(.2f, .34f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            main.startColor = Color.white;
            main.gravityModifier = 0;
            var emission = ps.emission; emission.enabled = true; emission.rateOverTime = 0;
            // Cones emit along local +Z; the exit is turned to face out of the pipe.
            var shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 5; shape.radius = .03f;
            var color = ps.colorOverLifetime; color.enabled = true; color.color = FlameGradient();
            var size = ps.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(new Keyframe(0, .45f), new Keyframe(.3f, 1), new Keyframe(1, .6f)));
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = FlameMaterial();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            ps.Play();
            return ps;
        }

        /// <summary>White-hot blue at the pipe, through violet, to orange-red and a deep red, ragged tip.</summary>
        static Gradient FlameGradient()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] {
                    new GradientColorKey(new Color(.82f, .94f, 1f), 0),
                    new GradientColorKey(new Color(.18f, .48f, 1f), .16f),
                    new GradientColorKey(new Color(.55f, .25f, .95f), .42f),
                    new GradientColorKey(new Color(1f, .28f, .1f), .68f),
                    new GradientColorKey(new Color(.85f, .05f, .02f), 1)
                },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(.9f, .55f), new GradientAlphaKey(0, 1) });
            return gradient;
        }

        /// <summary>Additive and HDR-bright so the flame clears the bloom threshold.</summary>
        static Material FlameMaterial()
        {
            if (flameMaterial) return flameMaterial;
            flameTexture = new Texture2D(32, 32, TextureFormat.RGBA32, false) { name = "Nitro flame puff", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[32 * 32];
            for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
            {
                float distance = new Vector2((x - 15.5f) / 15.5f, (y - 15.5f) / 15.5f).magnitude;
                pixels[y * 32 + x] = new Color(1, 1, 1, Mathf.Pow(Mathf.Clamp01(1 - distance), 1.8f));
            }
            flameTexture.SetPixels(pixels); flameTexture.Apply(false, true);
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Universal Render Pipeline/Unlit");
            flameMaterial = new Material(shader) { name = "Nitro flame (additive)", renderQueue = 3100 };
            flameMaterial.SetTexture("_BaseMap", flameTexture); flameMaterial.SetTexture("_MainTex", flameTexture);
            flameMaterial.SetFloat("_Surface", 1); flameMaterial.SetFloat("_Blend", 2);
            flameMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); flameMaterial.SetFloat("_DstBlend", (float)BlendMode.One);
            flameMaterial.SetFloat("_ZWrite", 0); flameMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            flameMaterial.SetColor("_BaseColor", new Color(2.6f, 2.6f, 2.6f, 1));
            return flameMaterial;
        }
    }
}
