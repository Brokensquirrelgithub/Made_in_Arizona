using UnityEngine;

namespace MadeInArizona
{
    /// <summary>
    /// The satellite-dish death ray drawn as one continuous beam for as long as the trigger is held, rather than a
    /// separate flash per damage tick. Layered additive lines (wide glow, hot body, white core) carry a slowly scrolling
    /// streak texture so energy visibly flows along the beam, with a glow at the muzzle and a burning point where it
    /// lands. A looping electrical hum plays while it is on, rising in pitch as the focus heats up. WeaponSystem feeds
    /// it every damage tick; on co-op guests the host's vehicle snapshots feed it (<see cref="SetNetwork"/>).
    /// </summary>
    public sealed class DeathRayBeam : MonoBehaviour
    {
        static Texture2D streaks;
        LineRenderer glow, body, core;
        Material material;
        Light impact, muzzleGlow;
        AudioSource hum;
        WeaponSystem weapons;
        Vector3 end, smoothedEnd;
        float heat, lastFireAt = -10, networkSeenAt = -10, sparkAt, level;
        bool networkActive, wasOn;
        /// <summary>Seconds after the last damage tick that the beam still counts as held (ticks arrive 12 times a second).</summary>
        const float HoldGrace = .15f;

        public bool Active => weapons && (weapons.IsNetworkBeam ? networkActive && Time.unscaledTime - networkSeenAt < .3f : Time.time - lastFireAt < HoldGrace);
        public Vector3 End => end;
        public float Heat => heat;

        public void Bind(WeaponSystem owner) { weapons = owner; }

        /// <summary>A damage tick on the host (or in solo): the beam reaches <paramref name="hit"/> at the given focus heat.</summary>
        public void Fire(Vector3 hit, float focus) { end = hit; heat = focus; lastFireAt = Time.time; }

        /// <summary>Co-op guests: the host's beam state for this car, refreshed by each vehicle snapshot.</summary>
        public void SetNetwork(bool active, Vector3 hit, float focus)
        {
            networkActive = active; networkSeenAt = Time.unscaledTime;
            if (active) { end = hit; heat = focus; }
        }

        void LateUpdate()
        {
            var game = GameManager.Instance;
            bool playing = game && game.IsPlaying;
            bool on = Active && weapons && weapons.Owner && weapons.Owner.Damage != null && !weapons.Owner.Damage.IsDead;
            if (!on && !wasOn && level <= 0) return;
            if (!playing)
            {
                // Paused: the beam holds still and falls silent.
                if (hum && hum.isPlaying) hum.Pause();
                return;
            }
            Ensure();
            level = Mathf.MoveTowards(level, on ? 1 : 0, Time.deltaTime * (on ? 25 : 12));
            if (on && !wasOn) AudioManager.Instance?.PlayRayStart(transform.position);
            wasOn = on;
            Vector3 muzzle = weapons.MuzzlePoint;
            // Between ticks the visible beam tracks the turret, so it sweeps smoothly with the aim.
            if (on && !weapons.IsNetworkBeam) end = weapons.TraceBeam(muzzle);
            smoothedEnd = level < .05f || (smoothedEnd - end).sqrMagnitude > 400 ? end : Vector3.Lerp(smoothedEnd, end, 1 - Mathf.Exp(-Time.deltaTime * 30));
            bool visible = level > .01f;
            glow.enabled = body.enabled = core.enabled = visible;
            impact.enabled = muzzleGlow.enabled = visible;
            if (visible)
            {
                Place(glow, muzzle, smoothedEnd); Place(body, muzzle, smoothedEnd); Place(core, muzzle, smoothedEnd);
                // Constant width (a steady beam, not a pulse) with a faint high-frequency shimmer, widening as it heats.
                float shimmer = 1 + (Mathf.PerlinNoise(Time.time * 23, 0) - .5f) * .08f;
                glow.widthMultiplier = (.68f + heat * .3f) * level * shimmer;
                body.widthMultiplier = (.25f + heat * .09f) * level;
                core.widthMultiplier = (.08f + heat * .03f) * level;
                Color hot = Color.Lerp(new Color(1.6f, .85f, .2f), new Color(2.2f, .55f, .12f), heat);
                Paint(glow, new Color(hot.r * .7f, hot.g * .5f, hot.b * .4f, .32f * level));
                Paint(body, new Color(hot.r, hot.g, hot.b, .65f * level));
                Paint(core, new Color(1.9f, 1.8f, 1.45f, .95f * level));
                material.SetTextureOffset("_BaseMap", new Vector2(-Time.time * 3.2f, 0));
                impact.transform.position = smoothedEnd - (smoothedEnd - muzzle).normalized * .4f;
                impact.intensity = (6 + heat * 10) * level; impact.color = hot.linear;
                muzzleGlow.transform.position = muzzle; muzzleGlow.intensity = 4 * level;
                if (on && Time.time >= sparkAt)
                {
                    sparkAt = Time.time + .07f;
                    ExplosionSystem.Burst(smoothedEnd, Color.Lerp(new Color(1, .93f, .5f), new Color(1, .4f, .1f), heat), 1 + Mathf.RoundToInt(heat * 2), 1.5f + heat * 2);
                }
            }
            var settings = game.Save?.settings;
            float volume = settings != null ? settings.weapons * .55f : .4f;
            hum.volume = volume * level;
            hum.pitch = 1 + heat * .28f;
            if (level > 0 && !hum.isPlaying) { if (hum.time > 0) hum.UnPause(); else hum.Play(); }
            if (level <= 0 && hum.isPlaying) hum.Stop();
        }

        void Ensure()
        {
            if (glow) return;
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            material = new Material(shader) { name = "Death ray beam", renderQueue = 3001 };
            material.SetFloat("_Surface", 1); material.SetFloat("_Blend", 2);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha); material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_ZWrite", 0); material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetColor("_BaseColor", new Color(2.6f, 2.6f, 2.6f, 1));
            material.SetTexture("_BaseMap", Streaks); material.mainTexture = Streaks;
            glow = Line("Death ray glow"); body = Line("Death ray body"); core = Line("Death ray core");
            impact = Lamp("Death ray burn point", 6); muzzleGlow = Lamp("Death ray dish glow", 4);
            hum = gameObject.AddComponent<AudioSource>();
            hum.clip = AudioManager.Instance ? AudioManager.Instance.RayLoop : null; hum.loop = true; hum.playOnAwake = false;
            hum.spatialBlend = .5f; hum.minDistance = 16; hum.maxDistance = 160; hum.rolloffMode = AudioRolloffMode.Linear;
            hum.dopplerLevel = 0; hum.priority = 50; hum.volume = 0;
        }
        LineRenderer Line(string name)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true; line.positionCount = 2; line.numCapVertices = 3; line.textureMode = LineTextureMode.Tile;
            line.sharedMaterial = material; line.alignment = LineAlignment.View;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; line.receiveShadows = false;
            return line;
        }
        Light Lamp(string name, float range)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            var light = go.AddComponent<Light>(); light.type = LightType.Point; light.range = range; light.shadows = LightShadows.None;
            light.color = new Color(1, .7f, .3f); light.intensity = 0;
            return light;
        }
        static void Place(LineRenderer line, Vector3 a, Vector3 b) { line.SetPosition(0, a); line.SetPosition(1, b); }
        static void Paint(LineRenderer line, Color color) { line.startColor = color; line.endColor = color; }

        /// <summary>Soft lengthwise streaks that scroll along the beam: flowing energy without breaking it into pulses.</summary>
        static Texture2D Streaks
        {
            get
            {
                if (streaks) return streaks;
                const int width = 128, height = 16;
                var pixels = new Color[width * height];
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        float u = x / (float)width, v = (y + .5f) / height;
                        float across = Mathf.Sin(v * Mathf.PI);
                        // Periodic in u, so the tiling texture has no seam along the beam.
                        float flow = .78f + .14f * Mathf.Sin(u * Mathf.PI * 2 * 3 + v * 2.1f) + .08f * Mathf.Sin(u * Mathf.PI * 2 * 7 - v * 5.3f);
                        float a = Mathf.Clamp01(across * flow);
                        pixels[y * width + x] = new Color(1, 1, 1, a);
                    }
                streaks = new Texture2D(width, height, TextureFormat.RGBA32, true) { name = "Death ray streaks", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
                streaks.SetPixels(pixels); streaks.Apply(true, true);
                return streaks;
            }
        }
        void OnDestroy() { if (material) Destroy(material); }
    }
}
