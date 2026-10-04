using System;
using System.Collections.Generic;
using UnityEngine;

namespace MadeInArizona
{
    /// <summary>What a round does to a vehicle it hits, on top of its direct damage.</summary>
    public enum ShotEffect { None, Burn, Stall, Spine, Knockback, Harpoon, Sticky, Pop }

    /// <summary>
    /// Optional behaviour for a round: homing, piercing, a boomerang return, a wider sweep, an on-hit effect and a
    /// bomblet payload. Plain bullets pass null.
    /// </summary>
    public sealed class ShotFx
    {
        public VehicleController homing; public float turnRate = 150;
        public int pierce;
        public bool boomerang; public float returnAfter = .5f;
        public float castRadius;
        public ShotEffect effect; public float power, duration, effectRadius;
        /// <summary>Stall arcs jump to this many other hostile cars within 14 m.</summary>
        public int chain;
        public int bomblets; public float bombletDamage, bombletRadius;
        /// <summary>Round spawned away from its owner (bomblets, sentries): skip the muzzle sweep from the car.</summary>
        public bool detached;
        /// <summary>Leaves a flame trail (weed burner).</summary>
        public bool flame;
        public bool coneBlast;
    }

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
            public float speed, damage, radius, remaining, trailAt, born;
            public GameObject source;
            public bool friendly, returning;
            public Color color;
            public ExplosionKind kind;
            public ShotFx fx;
            public readonly HashSet<VehicleController> struck = new HashSet<VehicleController>();
        }
        static ProjectileSystem instance;
        readonly List<Round> active = new List<Round>(256);
        readonly Stack<Round> pool = new Stack<Round>(256);
        readonly RaycastHit[] hits = new RaycastHit[32];
        static readonly RaycastHit[] scanHits = new RaycastHit[48];
        static readonly List<VehicleController> chained = new List<VehicleController>();
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
        public static void Fire(Vector3 position, Vector3 direction, float speed, float damage, float radius, GameObject source, Color color, ExplosionKind kind, float lifetime, ShotFx fx = null)
        {
            Get().Launch(position, direction, speed, damage, radius, source, color, kind, lifetime, fx);
        }
        /// <summary>True when <paramref name="target"/> is on the other side from the vehicle that owns <paramref name="source"/>.</summary>
        public static bool Hostile(GameObject source, VehicleController target)
        {
            if (!target || target.Damage == null || target.Damage.IsDead) return false;
            var shooter = source ? source.GetComponentInParent<VehicleController>() : null;
            if (!shooter) return true;
            return shooter != target && !VehicleController.Allied(shooter, target);
        }
        void Launch(Vector3 position, Vector3 direction, float speed, float damage, float radius, GameObject source, Color color, ExplosionKind kind, float lifetime, ShotFx fx)
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
            round.color = color; round.kind = kind; round.remaining = lifetime; round.trailAt = 0; round.born = Time.time;
            round.fx = fx; round.returning = false; round.struck.Clear();
            var vehicle = source != null ? source.GetComponentInParent<VehicleController>() : null;
            var ai = vehicle != null ? vehicle.GetComponent<EnemyAI>() : null;
            round.friendly = vehicle != null && (vehicle.IsPlayer || (ai != null && ai.IsFriendly));
            round.transform.SetPositionAndRotation(position, Quaternion.LookRotation(round.direction));
            bool heavy = radius > .1f || (fx != null && (fx.boomerang || fx.effect == ShotEffect.Harpoon || fx.effect == ShotEffect.Knockback));
            round.transform.localScale = heavy ? new Vector3(.24f, .24f, 1.3f) : fx != null && fx.flame ? new Vector3(.3f, .3f, .5f) : new Vector3(.1f, .1f, 1.15f);
            if (fx != null && fx.coneBlast) round.transform.localScale *= 1.1f;
            block.SetColor("_BaseColor", color * 2.4f); block.SetColor("_Color", color * 2.4f); round.renderer.SetPropertyBlock(block);
            round.view.SetActive(true);
            round.trail.Clear();
            round.trail.time = heavy ? .24f : kind == ExplosionKind.Ammunition ? .075f : .12f;
            round.trail.startWidth = heavy ? .18f : .065f;
            round.trail.endWidth = 0;
            round.trail.startColor = new Color(color.r, color.g, color.b, .82f);
            round.trail.endColor = new Color(color.r, color.g, color.b, 0);
            active.Add(round);
            if (source != null && (fx == null || !fx.detached))
            {
                Vector3 origin = source.transform.position + Vector3.up * .85f;
                Vector3 delta = position - origin;
                if (delta.sqrMagnitude > .0001f && Cast(round, origin, delta.normalized, delta.magnitude, out RaycastHit hit))
                {
                    if (Resolve(round, hit)) Retire(active.Count - 1);
                }
            }
        }
        void Update()
        {
            if (GameManager.Instance == null || !GameManager.Instance.IsPlaying) return;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                if (i >= active.Count) continue;
                var r = active[i];
                Steer(r);
                // Do not travel beyond the configured lifetime on a long frame.
                float stepTime = Mathf.Min(Time.deltaTime, Mathf.Max(0, r.remaining));
                float distance = r.speed * stepTime;
                if (Cast(r, r.position, r.direction, distance, out RaycastHit hit))
                {
                    if (Resolve(r, hit)) { Retire(i); continue; }
                    // A piercing round carries on from the car it passed through.
                    r.position = hit.point + r.direction * .05f;
                    r.transform.position = r.position;
                }
                else
                {
                    r.position += r.direction * distance;
                    r.transform.SetPositionAndRotation(r.position, Quaternion.LookRotation(r.direction));
                }
                r.remaining -= stepTime;
                if (Time.time > r.trailAt && (r.radius > .1f || (r.fx != null && r.fx.flame)))
                {
                    r.trailAt = Time.time + (r.fx != null && r.fx.flame ? .03f : .045f);
                    if (r.fx != null && r.fx.flame) ExplosionSystem.Burst(r.position, new Color(1, .5f + UnityEngine.Random.value * .3f, .1f), 2, 1.2f);
                    else ExplosionSystem.Burst(r.position - r.direction * .4f, new Color(.7f, .48f, .28f, .45f), 2, .9f);
                }
                if (r.fx != null && r.fx.boomerang && r.returning && r.fx.homing == null && r.source && (r.source.transform.position - r.position).sqrMagnitude < 6) { Retire(i); continue; }
                if (r.remaining <= 0)
                {
                    Expire(r);
                    Retire(i);
                }
            }
        }
        /// <summary>Homing spines curve toward their target; boomerangs turn for home halfway through their flight.</summary>
        void Steer(Round r)
        {
            var fx = r.fx;
            if (fx == null) return;
            if (fx.boomerang && !r.returning && Time.time - r.born >= fx.returnAfter) { r.returning = true; r.struck.Clear(); }
            Vector3 goal; float rate;
            if (fx.boomerang && r.returning && r.source) { goal = r.source.transform.position + Vector3.up * .9f; rate = 900; }
            else if (fx.homing && fx.homing.Damage != null && !fx.homing.Damage.IsDead) { goal = fx.homing.transform.position + Vector3.up * .8f; rate = fx.turnRate; }
            else return;
            Vector3 want = goal - r.position;
            if (want.sqrMagnitude < .01f) return;
            r.direction = Vector3.RotateTowards(r.direction, want.normalized, rate * Mathf.Deg2Rad * Time.deltaTime, 0).normalized;
        }
        bool Cast(Round round, Vector3 origin, Vector3 direction, float distance, out RaycastHit closest)
        {
            closest = default;
            if (distance < .0001f) return false;
            float width = round.fx != null && round.fx.castRadius > 0 ? round.fx.castRadius : round.radius > .1f ? .18f : .075f;
            int count = Physics.SphereCastNonAlloc(origin, width, direction, hits, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            for (int h = 0; h < count; h++)
            {
                var candidate = hits[h];
                if (candidate.collider == null) continue;
                if (round.source != null && candidate.collider.transform.IsChildOf(round.source.transform)) continue;
                var vehicle = candidate.collider.GetComponentInParent<VehicleController>();
                if (vehicle != null)
                {
                    if (vehicle.Damage == null || vehicle.Damage.IsDead || round.struck.Contains(vehicle)) continue;
                    var ai = vehicle.GetComponent<EnemyAI>();
                    bool friendly = vehicle.IsPlayer || (ai != null && ai.IsFriendly);
                    if (friendly == round.friendly) continue;
                }
                if (candidate.distance < nearest) { nearest = candidate.distance; closest = candidate; }
            }
            // A sweep that starts inside a collider reports no contact point; use the sweep origin.
            if (nearest < float.MaxValue && closest.distance <= 0) closest.point = origin;
            return nearest < float.MaxValue;
        }
        /// <summary>Applies a hit. Returns false when the round keeps flying (piercing blades, boomerangs through cars).</summary>
        bool Resolve(Round round, RaycastHit hit)
        {
            var vehicle = hit.collider.GetComponentInParent<VehicleController>();
            var fx = round.fx;
            if (vehicle && fx != null && (fx.boomerang || fx.pierce > 0))
            {
                Hit(round, hit);
                round.struck.Add(vehicle);
                if (!fx.boomerang) fx.pierce--;
                return false;
            }
            if (!vehicle && fx != null && fx.boomerang && !round.returning)
            {
                // Hubcaps glance off walls and head home early.
                round.returning = true; round.struck.Clear();
                ExplosionSystem.Burst(hit.point, round.color, 6, 3);
                return false;
            }
            Hit(round, hit);
            return true;
        }
        void Hit(Round round, RaycastHit hit)
        {
            var fx = round.fx;
            if (round.radius > .1f)
            {
                if (fx != null && fx.coneBlast) ExplosionSystem.DetonateCone(hit.point, round.direction, round.radius, round.damage, round.source);
                else ExplosionSystem.Detonate(hit.point, round.radius, round.damage, round.source, round.kind);
                Payload(round, hit.point);
                return;
            }
            var weakpoint = hit.collider.GetComponentInParent<BossWeakPoint>();
            var vehicle = hit.collider.GetComponentInParent<VehicleController>();
            if (weakpoint != null) weakpoint.ApplyDamage(round.damage, hit.point, round.source);
            else
            {
                var damage = hit.collider.GetComponentInParent<VehicleDamage>();
                if (damage != null) damage.ApplyDamage(round.damage, hit.point, round.source);
                else hit.collider.GetComponentInParent<DestructionSystem>()?.ApplyProjectileHit(round.damage, hit.point, round.source, round.direction);
            }
            if (fx != null && fx.effect != ShotEffect.None) Afflict(round, vehicle, hit.point);
            ExplosionSystem.Burst(hit.point, round.color, 4, 2);
        }
        void Afflict(Round round, VehicleController vehicle, Vector3 point)
        {
            var fx = round.fx;
            if (fx.effect == ShotEffect.Pop) { ExplosionSystem.Pop(point, fx.effectRadius, fx.power, round.source, round.color); return; }
            if (fx.effect == ShotEffect.Sticky)
            {
                var owner = round.source ? round.source.GetComponentInParent<VehicleController>() : null;
                if (vehicle && Hostile(round.source, vehicle)) VehicleAfflictions.For(vehicle).AttachCharge(point, fx.duration, fx.power, fx.effectRadius, round.source);
                else if (owner) FieldOrdnance.PlantCharge(owner, point, fx.duration, fx.power, fx.effectRadius, round.color);
                return;
            }
            if (!vehicle || !Hostile(round.source, vehicle)) return;
            var afflictions = VehicleAfflictions.For(vehicle);
            switch (fx.effect)
            {
                case ShotEffect.Burn: afflictions.Ignite(fx.power, fx.duration, round.source); break;
                case ShotEffect.Spine: afflictions.AddSpine(round.source, fx.power, fx.effectRadius); break;
                case ShotEffect.Stall:
                    afflictions.Stall(fx.duration);
                    if (fx.chain > 0) Chain(round, vehicle);
                    break;
                case ShotEffect.Knockback:
                    if (vehicle.Body && !vehicle.Body.isKinematic)
                        vehicle.Body.AddForce(Vector3.ProjectOnPlane(round.direction, Vector3.up).normalized * fx.power + Vector3.up * 3, ForceMode.VelocityChange);
                    ExplosionSystem.Burst(point, new Color(.85f, .8f, .7f, .6f), 10, 5);
                    break;
                case ShotEffect.Harpoon:
                    var shooter = round.source ? round.source.GetComponentInParent<VehicleController>() : null;
                    if (shooter && vehicle.Body && !vehicle.Body.isKinematic)
                    {
                        Vector3 pull = Vector3.ProjectOnPlane(shooter.transform.position - vehicle.transform.position, Vector3.up);
                        if (pull.sqrMagnitude > 1) vehicle.Body.AddForce(pull.normalized * fx.power + Vector3.up * 2, ForceMode.VelocityChange);
                        WeaponFx.Tether(shooter.transform, vehicle.transform, round.color, .6f);
                    }
                    afflictions.Stall(fx.duration);
                    break;
            }
        }
        /// <summary>Stall bolts arc to the nearest other hostile cars, stalling and hurting them too.</summary>
        void Chain(Round round, VehicleController first)
        {
            chained.Clear(); chained.Add(first);
            var from = first;
            for (int jump = 0; jump < round.fx.chain; jump++)
            {
                VehicleController best = null; float bestDistance = 14 * 14;
                foreach (var candidate in VehicleController.Active)
                {
                    if (!candidate || chained.Contains(candidate) || !Hostile(round.source, candidate)) continue;
                    float d = (candidate.transform.position - from.transform.position).sqrMagnitude;
                    if (d < bestDistance) { bestDistance = d; best = candidate; }
                }
                if (!best) break;
                WeaponFx.Lightning(from.transform.position + Vector3.up, best.transform.position + Vector3.up, round.color);
                best.Damage.ApplyDamage(round.damage * .6f, best.transform.position + Vector3.up, round.source);
                VehicleAfflictions.For(best).Stall(round.fx.duration * .75f);
                chained.Add(best); from = best;
            }
        }
        void Expire(Round round)
        {
            if (round.radius > .1f)
            {
                if (round.fx != null && round.fx.coneBlast) ExplosionSystem.DetonateCone(round.position, round.direction, round.radius, round.damage, round.source);
                else ExplosionSystem.Detonate(round.position, round.radius, round.damage, round.source, round.kind);
                Payload(round, round.position);
            }
            else if (round.fx != null && round.fx.effect == ShotEffect.Pop) ExplosionSystem.Pop(round.position, round.fx.effectRadius, round.fx.power, round.source, round.color);
        }
        /// <summary>Cluster payloads burst into bouncing bomblets.</summary>
        void Payload(Round round, Vector3 point)
        {
            var fx = round.fx;
            if (fx == null || fx.bomblets <= 0) return;
            var owner = round.source ? round.source.GetComponentInParent<VehicleController>() : null;
            if (!owner) return;
            for (int i = 0; i < fx.bomblets; i++)
            {
                float angle = (i + UnityEngine.Random.value * .6f) / fx.bomblets * Mathf.PI * 2;
                Vector3 velocity = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * UnityEngine.Random.Range(6f, 12f) + Vector3.up * UnityEngine.Random.Range(7f, 11f);
                FieldOrdnance.Bomblet(owner, point + Vector3.up * .8f, velocity, fx.bombletDamage, fx.bombletRadius, round.color);
            }
        }

        /// <summary>
        /// Instant ray from <paramref name="origin"/>: fills <paramref name="struck"/> with the hostile vehicles it
        /// crosses (all of them when piercing, else the first) and returns where the beam stops. Solid scenery
        /// stops it; a breakable prop in the way is damaged by <paramref name="propDamage"/>.
        /// </summary>
        public static Vector3 Hitscan(Vector3 origin, Vector3 direction, float range, GameObject source, bool pierce, float propDamage, List<RaycastHit> struck)
        {
            struck.Clear();
            direction = direction.sqrMagnitude > .0001f ? direction.normalized : Vector3.forward;
            int count = Physics.SphereCastNonAlloc(origin, .25f, direction, scanHits, range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            Array.Sort(scanHits, 0, count, HitDistance.Instance);
            Vector3 end = origin + direction * range;
            for (int i = 0; i < count; i++)
            {
                var hit = scanHits[i];
                if (!hit.collider || (source && hit.collider.transform.IsChildOf(source.transform))) continue;
                var vehicle = hit.collider.GetComponentInParent<VehicleController>();
                if (vehicle)
                {
                    if (!Hostile(source, vehicle)) continue;
                    bool seen = false;
                    foreach (var previous in struck) if (previous.collider.GetComponentInParent<VehicleController>() == vehicle) { seen = true; break; }
                    if (hit.distance <= 0) hit.point = hit.collider.ClosestPointOnBounds(origin);
                    if (!seen) struck.Add(hit);
                    if (!pierce) { end = hit.distance > 0 ? hit.point : origin; break; }
                    continue;
                }
                var prop = hit.collider.GetComponentInParent<DestructionSystem>();
                if (prop && propDamage > 0) prop.ApplyProjectileHit(propDamage, hit.point, source, direction);
                end = hit.distance > 0 ? hit.point : origin;
                break;
            }
            return end;
        }
        sealed class HitDistance : IComparer<RaycastHit>
        {
            public static readonly HitDistance Instance = new HitDistance();
            public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
        }

        void Retire(int index)
        {
            var round = active[index]; round.trail.Clear(); round.view.SetActive(false); round.source = null; round.fx = null; round.struck.Clear();
            pool.Push(round); active.RemoveAt(index);
        }
        void OnDestroy() { if (instance == this) instance = null; if (material != null) Destroy(material); }
    }
}
