using UnityEngine;
using UnityEngine.Rendering;

namespace MadeInArizona
{
    /// <summary>Authored at runtime so the cannon's burst works without the optional VFX Graph package.</summary>
    public sealed class FireworkBurstVisual : MonoBehaviour
    {
        // The former fallback sparks left the centre at 8 m/s.
        const float StrandSpeed = 8f * 2.5f;
        static readonly Color[][] Palettes =
        {
            new[] { new Color(1f, .05f, .025f), new Color(1f, .55f, .04f), new Color(.85f, .93f, 1f) },
            new[] { new Color(.04f, .25f, 1f), new Color(.85f, .93f, 1f), new Color(1f, .55f, .04f) },
            new[] { new Color(.04f, 1f, .16f), new Color(1f, .55f, .04f), new Color(.85f, .93f, 1f) },
            new[] { new Color(1f, .08f, .42f), new Color(.85f, .93f, 1f), new Color(1f, .55f, .04f) },
            new[] { new Color(1f, .05f, .025f), new Color(.04f, 1f, .16f), new Color(.04f, .25f, 1f),
                new Color(1f, .55f, .04f), new Color(.85f, .93f, 1f) }
        };
        static Material additive;
        static Texture2D softDisc;
        static int nextPalette;
        Light flash;
        float started;
        float flashIntensity;

        public static FireworkBurstVisual Spawn(Vector3 position)
        {
            var go = new GameObject("Firework cannon bloom");
            if (GameManager.Instance && GameManager.Instance.World)
                go.transform.SetParent(GameManager.Instance.World.transform);
            go.transform.position = position + Vector3.up * 1.1f;
            var effect = go.AddComponent<FireworkBurstVisual>();
            effect.Begin(Palettes[nextPalette++ % Palettes.Length]);
            Destroy(go, 2.5f);
            return effect;
        }

        static Material GlowMaterial()
        {
            if (additive) return additive;
            softDisc = new Texture2D(64, 64, TextureFormat.RGBA32, false)
                { name = "Firework soft glow", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[64 * 64];
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
            {
                float dx = (x - 31.5f) / 31.5f, dy = (y - 31.5f) / 31.5f;
                float alpha = Mathf.Pow(Mathf.Clamp01(1f - dx * dx - dy * dy), 1.4f);
                pixels[y * 64 + x] = new Color(1, 1, 1, alpha);
            }
            softDisc.SetPixels(pixels); softDisc.Apply(false, true);
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Particles/Standard Unlit");
            additive = new Material(shader) { name = "Firework additive HDR", renderQueue = 3001 };
            additive.SetTexture("_BaseMap", softDisc); additive.SetTexture("_MainTex", softDisc);
            additive.SetFloat("_Surface", 1); additive.SetFloat("_Blend", 2);
            additive.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            additive.SetFloat("_DstBlend", (float)BlendMode.One);
            additive.SetFloat("_ZWrite", 0); additive.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            // Vertex colors choose the hue; this HDR multiplier makes the whole burst bloom strongly.
            additive.SetColor("_BaseColor", new Color(4f, 4f, 4f, 1));
            return additive;
        }

        ParticleSystem MakeParticles(string label, int capacity, bool trails)
        {
            var child = new GameObject(label);
            child.transform.SetParent(transform, false);
            var particles = child.AddComponent<ParticleSystem>();
            var main = particles.main;
            main.playOnAwake = false; main.loop = false; main.duration = 2f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0; main.maxParticles = capacity;
            var emission = particles.emission; emission.enabled = false;
            var shape = particles.shape; shape.enabled = false;
            var color = particles.colorOverLifetime; color.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, .42f),
                    new GradientAlphaKey(0, 1) });
            color.color = new ParticleSystem.MinMaxGradient(fade);
            if (trails)
            {
                var trail = particles.trails;
                trail.enabled = true;
                trail.mode = ParticleSystemTrailMode.PerParticle;
                trail.ratio = 1f;
                trail.lifetime = .65f;
                trail.inheritParticleColor = true;
                trail.widthOverTrail = new ParticleSystem.MinMaxCurve(1f,
                    new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, 0)));
            }
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = GlowMaterial();
            renderer.trailMaterial = additive;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            particles.Play();
            return particles;
        }

        void Begin(Color[] palette)
        {
            started = Time.time;
            var strands = MakeParticles("Fast colored strands", 120, true);
            var glitter = MakeParticles("Silver and gold glints", 90, false);
            Vector3 centre = transform.position;
            for (int i = 0; i < 104; i++)
            {
                Vector3 direction = Random.onUnitSphere;
                direction.y = Mathf.Abs(direction.y) * .75f + .12f;
                direction.Normalize();
                var particle = new ParticleSystem.EmitParams
                {
                    position = centre + Random.insideUnitSphere * .22f,
                    velocity = direction * StrandSpeed * Random.Range(.85f, 1.2f),
                    startColor = palette[i % palette.Length],
                    startSize = Random.Range(.16f, .29f),
                    startLifetime = Random.Range(.72f, 1.15f)
                };
                strands.Emit(particle, 1);
            }
            for (int i = 0; i < 72; i++)
            {
                Vector3 direction = Random.onUnitSphere;
                direction.y = Mathf.Abs(direction.y) * .65f + .1f;
                var particle = new ParticleSystem.EmitParams
                {
                    position = centre + Random.insideUnitSphere * .45f,
                    velocity = direction.normalized * Random.Range(5f, 14f),
                    startColor = i % 3 == 0 ? new Color(1f, .55f, .08f) : new Color(.8f, .9f, 1f),
                    startSize = Random.Range(.1f, .22f),
                    startLifetime = Random.Range(.6f, 1.35f)
                };
                glitter.Emit(particle, 1);
            }
            for (int i = 0; i < 9; i++)
                glitter.Emit(new ParticleSystem.EmitParams
                {
                    position = centre + Random.insideUnitSphere * .2f,
                    velocity = Random.insideUnitSphere * 1.5f,
                    startColor = i % 2 == 0 ? new Color(1f, .9f, .7f) : palette[i % palette.Length],
                    startSize = Mathf.Lerp(1.7f, 5.5f, i / 8f),
                    startLifetime = Mathf.Lerp(.16f, .35f, i / 8f)
                }, 1);
            flash = gameObject.AddComponent<Light>();
            flash.type = LightType.Point;
            flash.color = Color.Lerp(Color.white, palette[1], .25f);
            flash.range = 28f;
            flashIntensity = 12f;
            flash.intensity = flashIntensity;
            flash.shadows = LightShadows.None;
        }

        void Update()
        {
            if (!flash) return;
            float fade = 1f - Mathf.Clamp01((Time.time - started) / .55f);
            flash.intensity = flashIntensity * fade * fade;
        }
    }
}
