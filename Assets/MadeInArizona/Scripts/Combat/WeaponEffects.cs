using System.Collections.Generic;
using UnityEngine;

namespace MadeInArizona
{
    /// <summary>
    /// Lingering weapon effects on one vehicle: burning, a stalled engine, stacked needler spines and stuck
    /// dynamite bolts. Added on first use by <see cref="For"/>. Burns count as direct damage (no friendly
    /// fire); spine ruptures and dynamite are explosions and hurt anyone nearby.
    /// </summary>
    public sealed class VehicleAfflictions : MonoBehaviour
    {
        public const int SpinesToRupture = 7;
        /// <summary>Fire ticks hit three times harder than the burn rates weapons declare (torch, sprinkler, death ray).</summary>
        public const float BurnTickMultiplier = 3;
        VehicleController vehicle;
        float burnUntil, burnDps, burnTick, stallUntil, stallFx, spineDecayAt, spineDamage, spineRadius;
        GameObject burnSource, spineSource;
        sealed class Charge { public Transform marker; public float at, damage, radius; public GameObject source; }
        readonly List<Charge> charges = new List<Charge>();
        public int Spines { get; private set; }
        public bool Burning => Time.time < burnUntil;
        public bool IsStalled => Time.time < stallUntil;

        public static VehicleAfflictions For(VehicleController target)
        {
            if (!target) return null;
            if (!target.Afflictions)
            {
                target.Afflictions = target.GetComponent<VehicleAfflictions>();
                if (!target.Afflictions) target.Afflictions = target.gameObject.AddComponent<VehicleAfflictions>();
                target.Afflictions.vehicle = target;
            }
            return target.Afflictions;
        }
        public static bool Stalled(VehicleController target) => target && target.Afflictions && target.Afflictions.IsStalled;

        public void Ignite(float damagePerSecond, float duration, GameObject source)
        {
            if (Dead) return;
            burnDps = Burning ? Mathf.Max(burnDps, damagePerSecond) : damagePerSecond;
            burnUntil = Mathf.Max(burnUntil, Time.time + duration); burnSource = source;
        }
        public void Stall(float duration)
        {
            if (Dead) return;
            stallUntil = Mathf.Max(stallUntil, Time.time + duration);
        }
        /// <summary>Adds one needler spine; the seventh within a few seconds ruptures them all at once.</summary>
        public void AddSpine(GameObject source, float ruptureDamage, float ruptureRadius)
        {
            if (Dead) return;
            if (Time.time > spineDecayAt) Spines = 0;
            Spines++; spineDecayAt = Time.time + 2.6f; spineSource = source; spineDamage = ruptureDamage; spineRadius = ruptureRadius;
            if (Spines < SpinesToRupture) return;
            Spines = 0;
            ExplosionSystem.Detonate(transform.position + Vector3.up * .9f, spineRadius, spineDamage, spineSource, ExplosionKind.Electrical);
            ExplosionSystem.Burst(transform.position + Vector3.up, new Color(1, .35f, .85f), 30, 7);
        }
        /// <summary>Sticks a fizzing charge to the car at <paramref name="point"/>; it explodes after <paramref name="delay"/>.</summary>
        public void AttachCharge(Vector3 point, float delay, float damage, float radius, GameObject source)
        {
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            marker.name = "Stuck dynamite bolt"; Destroy(marker.GetComponent<Collider>());
            marker.transform.SetParent(transform, true); marker.transform.position = point;
            marker.transform.localScale = new Vector3(.16f, .35f, .16f);
            marker.GetComponent<Renderer>().sharedMaterial = WorldArt.Material(new Color(.85f, .12f, .08f), 0, .3f, 2.5f);
            var shooter = source ? source.GetComponentInParent<VehicleController>() : null;
            if (shooter && !shooter.FriendlyToPlayer)
                marker.AddComponent<AttackWarningVisual>().BeginImpact(point, radius, delay, marker.transform);
            charges.Add(new Charge { marker = marker.transform, at = Time.time + delay, damage = damage, radius = radius, source = source });
        }
        bool Dead => !vehicle || vehicle.Damage == null || vehicle.Damage.IsDead;

        void Update()
        {
            if (!vehicle || GameManager.Instance == null || !GameManager.Instance.IsPlaying) return;
            if (Burning && !Dead && Time.time >= burnTick)
            {
                burnTick = Time.time + .25f;
                vehicle.Damage.ApplyDamage(burnDps * .25f * BurnTickMultiplier, transform.position + Vector3.up, burnSource);
                ExplosionSystem.Burst(transform.position + Vector3.up * 1.2f + Random.insideUnitSphere * .6f, new Color(1, .42f, .08f), 6, 2.2f);
            }
            if (IsStalled && !Dead && Time.time >= stallFx)
            {
                stallFx = Time.time + .12f;
                ExplosionSystem.Burst(transform.position + Vector3.up * 1.1f + Random.insideUnitSphere * .8f, new Color(.45f, .85f, 1), 3, 3);
            }
            for (int i = charges.Count - 1; i >= 0; i--)
            {
                var charge = charges[i];
                if (charge.marker && Mathf.Repeat(Time.time * 8, 1) < .5f) ExplosionSystem.Burst(charge.marker.position, new Color(1, .8f, .3f), 1, 1.5f);
                if (Time.time < charge.at) continue;
                Vector3 at = charge.marker ? charge.marker.position : transform.position;
                if (charge.marker) Destroy(charge.marker.gameObject);
                charges.RemoveAt(i);
                ExplosionSystem.Detonate(at, charge.radius, charge.damage, charge.source, ExplosionKind.Grenade, falloffPower: 2f);
            }
        }
    }

    /// <summary>Short-lived pooled line effects: railgun beams and lightning arcs.</summary>
    public sealed class WeaponFx : MonoBehaviour
    {
        sealed class Line { public LineRenderer renderer; public float start, until, width; public Color color; public Transform from, to; public Vector3 fromOffset, toOffset; public float jag; }
        static WeaponFx instance;
        readonly List<Line> lines = new List<Line>();
        Material material;

        static WeaponFx Get()
        {
            if (instance) return instance;
            var go = new GameObject("Pooled weapon beams");
            if (GameManager.Instance != null && GameManager.Instance.World != null) go.transform.SetParent(GameManager.Instance.World.transform);
            instance = go.AddComponent<WeaponFx>();
            return instance;
        }
        void Awake()
        {
            // Additive particle shading keeps the line's vertex colour and alpha; the HDR base colour feeds bloom.
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            material = new Material(shader) { name = "Weapon beam glow", renderQueue = 3000 };
            material.SetFloat("_Surface", 1); material.SetFloat("_Blend", 2);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha); material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_ZWrite", 0); material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetColor("_BaseColor", new Color(3, 3, 3, 1));
        }
        /// <summary>A straight beam (railgun) that fades over <paramref name="duration"/>.</summary>
        public static void Beam(Vector3 a, Vector3 b, Color color, float width, float duration) => Get().Show(a, b, null, null, color, width, duration, 0);
        /// <summary>A jagged electric arc between two points.</summary>
        public static void Lightning(Vector3 a, Vector3 b, Color color, float duration = .18f) => Get().Show(a, b, null, null, color, .09f, duration, .7f);

        void Show(Vector3 a, Vector3 b, Transform from, Transform to, Color color, float width, float duration, float jag)
        {
            Line line = null;
            foreach (var candidate in lines) if (!candidate.renderer.enabled) { line = candidate; break; }
            if (line == null)
            {
                if (lines.Count >= 48) line = lines[0];
                else
                {
                    var go = new GameObject("Pooled beam"); go.transform.SetParent(transform, false);
                    var renderer = go.AddComponent<LineRenderer>();
                    renderer.useWorldSpace = true; renderer.sharedMaterial = material; renderer.numCapVertices = 2;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
                    line = new Line { renderer = renderer }; lines.Add(line);
                }
            }
            line.from = from; line.to = to; line.fromOffset = Vector3.up * .9f; line.toOffset = Vector3.up * .9f;
            line.start = Time.time; line.until = Time.time + duration; line.width = width; line.color = color; line.jag = jag;
            line.renderer.positionCount = jag > 0 ? 9 : 2;
            Place(line, a, b);
            line.renderer.enabled = true;
            Paint(line, 1);
        }
        void Place(Line line, Vector3 a, Vector3 b)
        {
            int count = line.renderer.positionCount;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)(count - 1);
                Vector3 p = Vector3.Lerp(a, b, t);
                if (line.jag > 0 && i > 0 && i < count - 1) p += Random.insideUnitSphere * line.jag;
                line.renderer.SetPosition(i, p);
            }
        }
        void Paint(Line line, float strength)
        {
            line.renderer.widthMultiplier = line.width * Mathf.Lerp(.35f, 1, strength);
            Color c = line.color; c.a *= strength;
            line.renderer.startColor = c; line.renderer.endColor = c;
        }
        void LateUpdate()
        {
            foreach (var line in lines)
            {
                if (!line.renderer.enabled) continue;
                if (Time.time >= line.until) { line.renderer.enabled = false; continue; }
                if (line.from || line.to)
                {
                    if (!line.from || !line.to) { line.renderer.enabled = false; continue; }
                    Place(line, line.from.position + line.fromOffset, line.to.position + line.toOffset);
                }
                else if (line.jag > 0 && Random.value < .5f) Place(line, line.renderer.GetPosition(0), line.renderer.GetPosition(line.renderer.positionCount - 1));
                Paint(line, Mathf.InverseLerp(line.until, line.start, Time.time));
            }
        }
        void OnDestroy() { if (instance == this) instance = null; if (material) Destroy(material); }
    }
}
