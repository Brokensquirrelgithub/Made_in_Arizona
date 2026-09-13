using System;
using System.Collections.Generic;
using UnityEngine;

namespace MadeInArizona
{
    /// <summary>Shared, swept projectiles. Pool lifetime is scoped to the current world.</summary>
    public sealed class ProjectileSystem : MonoBehaviour
    {
        sealed class Round
        {
            public GameObject view;
            public Transform transform;
            public Renderer renderer;
            public TrailRenderer trail;
            public Vector3 position, direction;
            public float speed, damage, radius, remaining, trailAt;
            public GameObject source;
            public bool friendly;
            public Color color;
            public ExplosionKind kind;
        }
        static ProjectileSystem instance;
        readonly List<Round> active = new List<Round>(256);
        readonly Stack<Round> pool = new Stack<Round>(256);
        readonly RaycastHit[] hits = new RaycastHit[32];
        Material material;
        MaterialPropertyBlock block;
        public int ActiveCount => active.Count;
        public static float LastHitTime => CombatFeedback.LastHitTime;
        public static float LastKillTime => CombatFeedback.LastKillTime;
        public static float LastHitDamage => CombatFeedback.LastHitDamage;
        public static event Action<float, Vector3> HitConfirmed
        {
            add { CombatFeedback.HitConfirmed += value; }
            remove { CombatFeedback.HitConfirmed -= value; }
        }
        public static event Action<Vector3> KillConfirmed
        {
            add { CombatFeedback.KillConfirmed += value; }
            remove { CombatFeedback.KillConfirmed -= value; }
        }
        const int Maximum = 320;

        static ProjectileSystem Get()
        {
            if (instance != null) return instance;
            var root = new GameObject("Pooled projectiles");
            if (GameManager.Instance != null && GameManager.Instance.World != null) root.transform.SetParent(GameManager.Instance.World.transform);
            instance = root.AddComponent<ProjectileSystem>();
            return instance;
        }
        void Awake()
        {
            block = new MaterialPropertyBlock();
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            material = new Material(shader) { name = "Tracer glow", enableInstancing = true };
        }
        public static void Fire(Vector3 position, Vector3 direction, float speed, float damage, float radius, GameObject source, Color color, ExplosionKind kind, float lifetime)
        {
            Get().Launch(position, direction, speed, damage, radius, source, color, kind, lifetime);
        }
        void Launch(Vector3 position, Vector3 direction, float speed, float damage, float radius, GameObject source, Color color, ExplosionKind kind, float lifetime)
        {
            if (active.Count >= Maximum) Retire(0);
            Round round;
            if (pool.Count > 0) round = pool.Pop();
            else
            {
                var view = GameObject.CreatePrimitive(PrimitiveType.Cube);
                view.name = "Pooled tracer"; Destroy(view.GetComponent<Collider>());
                view.transform.SetParent(transform);
                var renderer = view.GetComponent<Renderer>(); renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
                var trail = view.AddComponent<TrailRenderer>();
                trail.sharedMaterial = material;
                trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                trail.receiveShadows = false;
                trail.textureMode = LineTextureMode.Stretch;
                round = new Round { view = view, transform = view.transform, renderer = renderer, trail = trail };
            }
            Vector3 fallback = source != null ? source.transform.forward : Vector3.forward;
            round.position = position; round.direction = direction.sqrMagnitude > .0001f ? direction.normalized : fallback;
            round.speed = speed; round.damage = damage; round.radius = radius; round.source = source;
            round.color = color; round.kind = kind; round.remaining = lifetime; round.trailAt = 0;
            var vehicle = source != null ? source.GetComponentInParent<VehicleController>() : null;
            var ai = vehicle != null ? vehicle.GetComponent<EnemyAI>() : null;
            round.friendly = vehicle != null && (vehicle.IsPlayer || (ai != null && ai.IsFriendly));
            round.transform.SetPositionAndRotation(position, Quaternion.LookRotation(round.direction));
            round.transform.localScale = radius > .1f ? new Vector3(.22f, .22f, 1.3f) : new Vector3(.1f, .1f, 1.15f);
            block.SetColor("_BaseColor", color * 2.4f); block.SetColor("_Color", color * 2.4f); round.renderer.SetPropertyBlock(block);
            round.view.SetActive(true);
            round.trail.Clear();
            round.trail.time = radius > .1f ? .24f : kind == ExplosionKind.Ammunition ? .075f : .12f;
            round.trail.startWidth = radius > .1f ? .18f : .065f;
            round.trail.endWidth = 0;
            round.trail.startColor = new Color(color.r, color.g, color.b, .82f);
            round.trail.endColor = new Color(color.r, color.g, color.b, 0);
            active.Add(round);
            if (source != null)
            {
                Vector3 origin = source.transform.position + Vector3.up * .85f;
                Vector3 delta = position - origin;
                if (delta.sqrMagnitude > .0001f && Cast(round, origin, delta.normalized, delta.magnitude, out RaycastHit hit))
                {
                    Hit(round, hit); Retire(active.Count - 1);
                }
            }
        }
        void Update()
        {
            if (GameManager.Instance == null || !GameManager.Instance.IsPlaying) return;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var r = active[i];
                // Do not travel beyond the configured lifetime on a long frame.
                float stepTime = Mathf.Min(Time.deltaTime, Mathf.Max(0, r.remaining));
                float distance = r.speed * stepTime;
                if (Cast(r, r.position, r.direction, distance, out RaycastHit hit))
                {
                    Hit(r, hit); Retire(i); continue;
                }
                r.position += r.direction * distance;
                r.transform.position = r.position;
                r.remaining -= stepTime;
                if (r.radius > .1f && Time.time > r.trailAt)
                {
                    r.trailAt = Time.time + .045f;
                    ExplosionSystem.Burst(r.position - r.direction * .4f, new Color(.7f, .48f, .28f, .45f), 2, .9f);
                }
                if (r.remaining <= 0)
                {
                    if (r.radius > .1f) ExplosionSystem.Detonate(r.position, r.radius, r.damage, r.source, r.kind);
                    Retire(i);
                }
            }
        }
        bool Cast(Round round, Vector3 origin, Vector3 direction, float distance, out RaycastHit closest)
        {
            closest = default;
            if (distance < .0001f) return false;
            int count = Physics.SphereCastNonAlloc(origin, round.radius > .1f ? .18f : .075f, direction, hits, distance, ~0, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            for (int h = 0; h < count; h++)
            {
                var candidate = hits[h];
                if (candidate.collider == null) continue;
                if (round.source != null && candidate.collider.transform.IsChildOf(round.source.transform)) continue;
                var vehicle = candidate.collider.GetComponentInParent<VehicleController>();
                if (vehicle != null)
                {
                    if (vehicle.Damage == null || vehicle.Damage.IsDead) continue;
                    var ai = vehicle.GetComponent<EnemyAI>();
                    bool friendly = vehicle.IsPlayer || (ai != null && ai.IsFriendly);
                    if (friendly == round.friendly) continue;
                }
                if (candidate.distance < nearest) { nearest = candidate.distance; closest = candidate; }
            }
            return nearest < float.MaxValue;
        }
        void Hit(Round round, RaycastHit hit)
        {
            if (round.radius > .1f)
            {
                ExplosionSystem.Detonate(hit.point, round.radius, round.damage, round.source, round.kind);
                return;
            }
            var weakpoint = hit.collider.GetComponentInParent<BossWeakPoint>();
            if (weakpoint != null) weakpoint.ApplyDamage(round.damage, hit.point, round.source);
            else
            {
                var damage = hit.collider.GetComponentInParent<VehicleDamage>();
                if (damage != null) damage.ApplyDamage(round.damage, hit.point, round.source);
                else hit.collider.GetComponentInParent<DestructionSystem>()?.ApplyDamage(round.damage, hit.point, round.source);
            }
            ExplosionSystem.Burst(hit.point, round.color, 4, 2);
        }
        void Retire(int index)
        {
            var round = active[index]; round.trail.Clear(); round.view.SetActive(false); round.source = null;
            pool.Push(round); active.RemoveAt(index);
        }
        void OnDestroy() { if (instance == this) instance = null; if (material != null) Destroy(material); }
    }
}
