using System.Collections.Generic;
using UnityEngine;

namespace MadeInArizona
{
    public enum WorldHazardKind { FuelTanker, GasPump, BlastingCrates, Fireworks, ChemicalDrum, Transformer }

    /// <summary>Distinct warning and aftermath for destructible roadside explosives.</summary>
    [RequireComponent(typeof(DestructionSystem))]
    public sealed class WorldHazard : MonoBehaviour
    {
        [field: SerializeField] public WorldHazardKind Kind { get; private set; }
        public bool IsArmed { get; private set; }
        public float DetonateAt { get; private set; }
        DestructionSystem damage;
        Light warning;
        Vector3 hit;
        GameObject source;
        float nextFx,nextBurn;

        public void Configure(WorldHazardKind kind)
        {
            Kind = kind;
            damage = GetComponent<DestructionSystem>();
            warning = GetComponentInChildren<Light>();
            if (warning) warning.enabled = false;
        }

        /// <summary>The first shot starts a visible leak or fuse; a vehicle impact detonates immediately.</summary>
        public bool ArmFromHit(Vector3 point, GameObject attacker)
        {
            if (IsArmed || (Kind != WorldHazardKind.FuelTanker && Kind != WorldHazardKind.BlastingCrates)) return false;
            if (attacker && attacker.GetComponentInParent<VehicleController>()) return false;
            IsArmed = true;
            hit = Kind == WorldHazardKind.FuelTanker ? transform.TransformPoint(new Vector3(0,3.5f,-1.2f)) : point;
            source = attacker;
            DetonateAt = Time.time + (Kind == WorldHazardKind.FuelTanker ? 2.2f : .9f);
            if (warning) warning.enabled = true;
            return true;
        }

        void Update()
        {
            if (!IsArmed || !damage || damage.IsDestroyed || CoopSession.IsRemoteClient) return;
            if (Time.time >= DetonateAt)
            {
                damage.ApplyDamage(damage.Health + 1, hit, source);
                return;
            }
            if (warning) warning.intensity = Mathf.Repeat(Time.time * (Kind == WorldHazardKind.FuelTanker ? 4 : 9), 1) < .5f ? 7 : 1;
            if (Time.time < nextFx) return;
            nextFx = Time.time + (Kind == WorldHazardKind.FuelTanker ? .14f : .09f);
            if (Kind == WorldHazardKind.FuelTanker)
            {
                ExplosionSystem.FuelLeak(hit);
                if(Time.time>=nextBurn)
                {
                    nextBurn=Time.time+.4f;
                    int count=Physics.OverlapSphereNonAlloc(transform.position+Vector3.up*1.3f,3.5f,nearby,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
                    for(int i=0;i<count;i++)
                    {
                        var car=nearby[i]?nearby[i].GetComponentInParent<VehicleController>():null;
                        if(car&&car.Damage!=null&&!car.Damage.IsDead)VehicleAfflictions.For(car)?.Ignite(2.5f,1f,source);
                    }
                }
            }
            else ExplosionSystem.Burst(transform.position + Vector3.up * 1.5f, new Color(2.4f, .32f, .08f), 3, 2.5f);
        }

        public void Detonate(Vector3 point, GameObject attacker)
        {
            if (warning) warning.enabled = false;
            switch (Kind)
            {
                case WorldHazardKind.FuelTanker:
                    ExplosionSystem.Detonate(point, 14, 230, attacker, ExplosionKind.FuelTank, environmental:true);
                    break;
                case WorldHazardKind.GasPump:
                    ExplosionSystem.Detonate(point, 6.5f, 100, attacker, ExplosionKind.Gasoline, environmental:true);
                    break;
                case WorldHazardKind.BlastingCrates:
                    ExplosionSystem.Detonate(point, 8, 125, attacker, ExplosionKind.Ammunition, environmental:true);
                    break;
                case WorldHazardKind.Fireworks:
                    ExplosionSystem.Detonate(point, 4.5f, 48, attacker, ExplosionKind.Ammunition, environmental:true);
                    WorldHazardAftermath.Spawn(Kind, point, attacker);
                    break;
                case WorldHazardKind.ChemicalDrum:
                    ExplosionSystem.Detonate(point, 4.5f, 55, attacker, ExplosionKind.Chemical, environmental:true);
                    WorldHazardAftermath.Spawn(Kind, point, attacker);
                    break;
                case WorldHazardKind.Transformer:
                    ExplosionSystem.Detonate(point, 7, 70, attacker, ExplosionKind.Electrical, environmental:true);
                    StallNearby(point);
                    break;
            }
        }

        public void NetworkBreak(Vector3 point)
        {
            if (Kind == WorldHazardKind.Fireworks || Kind == WorldHazardKind.ChemicalDrum)
                WorldHazardAftermath.Spawn(Kind, point, null, true);
        }

        static readonly Collider[] nearby = new Collider[64];
        static readonly HashSet<VehicleController> stalled = new HashSet<VehicleController>();
        static void StallNearby(Vector3 point)
        {
            stalled.Clear();
            int count = Physics.OverlapSphereNonAlloc(point, 9, nearby, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var vehicle = nearby[i] ? nearby[i].GetComponentInParent<VehicleController>() : null;
                if (!vehicle || !stalled.Add(vehicle) || vehicle.Damage == null || vehicle.Damage.IsDead) continue;
                VehicleAfflictions.For(vehicle)?.Stall(2.5f);
                WeaponFx.Lightning(point, vehicle.transform.position + Vector3.up, new Color(.5f, 1.6f, 2.4f), .28f);
            }
        }
    }

    /// <summary>Short-lived fireworks and chemical spill effects that outlive the destroyed prop.</summary>
    public sealed class WorldHazardAftermath : MonoBehaviour
    {
        public WorldHazardKind Kind { get; private set; }
        public bool VisualOnly { get; private set; }
        public float Until { get; private set; }
        GameObject source;
        float next;
        int bursts;
        Transform spill;
        static readonly Collider[] nearby = new Collider[64];
        readonly HashSet<VehicleDamage> damaged = new HashSet<VehicleDamage>();

        public static WorldHazardAftermath Spawn(WorldHazardKind kind, Vector3 point, GameObject attacker, bool visualOnly = false)
        {
            var go = new GameObject(kind == WorldHazardKind.Fireworks ? "Fireworks barrage" : "Corrosive chemical spill");
            if (GameManager.Instance && GameManager.Instance.World) go.transform.SetParent(GameManager.Instance.World.transform);
            go.transform.position = point;
            var effect = go.AddComponent<WorldHazardAftermath>();
            effect.Kind = kind;
            effect.VisualOnly = visualOnly;
            effect.source = attacker;
            effect.Until = Time.time + (kind == WorldHazardKind.Fireworks ? 1.8f : 7f);
            effect.next = Time.time + .12f;
            if (kind == WorldHazardKind.ChemicalDrum)
            {
                float ground = GeneratedWorld.Active ? GeneratedWorld.HeightAt(point) : point.y - .6f;
                go.transform.position = new Vector3(point.x, ground, point.z);
                var disk = WorldArt.Shape("Bright green spill", go.transform, PrimitiveType.Sphere,
                    new Vector3(0,.055f,0), new Vector3(7.5f,.07f,7.5f), new Color(.27f,.67f,.12f), glow:1.3f);
                effect.spill = disk.transform;
            }
            return effect;
        }

        void Update()
        {
            if (Time.time >= Until) { Destroy(gameObject); return; }
            if (Kind == WorldHazardKind.Fireworks)
            {
                if (Time.time < next || bursts >= 7) return;
                next += .22f;
                bursts++;
                Vector3 at = transform.position + new Vector3(Random.Range(-7f,7f),Random.Range(.4f,2.2f),Random.Range(-7f,7f));
                Color color = bursts % 3 == 0 ? new Color(.3f,1.7f,2.4f) : bursts % 3 == 1 ? new Color(2.4f,.3f,1.2f) : new Color(2.4f,1.6f,.2f);
                WeaponFx.Beam(transform.position,at,color,.09f,.2f);
                ExplosionSystem.Burst(at,color,24,7);
                if (!VisualOnly && !CoopSession.IsRemoteClient) ExplosionSystem.Pop(at,2.4f,22,source,color);
                return;
            }
            if (Time.time < next) return;
            next += .3f;
            Vector3 center = transform.position + Vector3.up * .35f;
            ExplosionSystem.Burst(center + Random.insideUnitSphere * 1.6f,new Color(.42f,.85f,.2f,.6f),9,1.6f);
            if (spill) spill.localScale = new Vector3(7.5f,.07f,7.5f) * Mathf.Clamp01((Until-Time.time)*2f);
            if (VisualOnly || CoopSession.IsRemoteClient) return;
            damaged.Clear();
            int count = Physics.OverlapSphereNonAlloc(center, 3.8f, nearby, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i=0;i<count;i++)
            {
                var car = nearby[i] ? nearby[i].GetComponentInParent<VehicleDamage>() : null;
                if (car && damaged.Add(car)) car.ApplyDamage(1.5f,center,source,true,true);
            }
        }
    }
}
