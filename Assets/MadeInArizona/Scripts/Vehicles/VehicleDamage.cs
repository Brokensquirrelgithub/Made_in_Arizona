using UnityEngine;

namespace MadeInArizona
{
    public sealed class VehicleDamage : MonoBehaviour
    {
        public float Health { get; private set; }
        public float MaxHealth { get; private set; }
        public bool IsDead { get; private set; }
        public float Engine { get; private set; } = 1;
        public float Radiator { get; private set; } = 1;
        public float Transmission { get; private set; } = 1;
        public float Wheels { get; private set; } = 1;
        public float Suspension { get; private set; } = 1;
        VehicleController vehicle;
        float smokeAt, baseHealth;
        public float LastDamageTime { get; private set; } = float.NegativeInfinity;
        public Vector3 LastDamagePoint { get; private set; }
        public GameObject LastDamageSource { get; private set; }

        public void Initialize(VehicleController owner, float health)
        {
            vehicle = owner; baseHealth=health; MaxHealth = Health = health*(owner.IsPlayer?DevTuning.Current.playerHealth:DevTuning.Current.enemyHealth); IsDead = false;
            Engine = Radiator = Transmission = Wheels = Suspension = 1;
        }
        public void ApplyHealthTuning()
        {
            if(vehicle==null||IsDead)return;
            float fraction=Health/Mathf.Max(1,MaxHealth);
            MaxHealth=Mathf.Max(1,baseHealth*(vehicle.IsPlayer?DevTuning.Current.playerHealth:DevTuning.Current.enemyHealth));
            Health=MaxHealth*fraction;
        }
        public void ApplyDamage(float amount, Vector3 hitPoint, GameObject source)
        {
            if (IsDead || amount <= 0 || vehicle == null) return;
            if (vehicle.IsPlayer && GameManager.Instance != null && GameManager.Instance.Save != null)
            {
                int difficulty = GameManager.Instance.Save.settings.difficulty;
                amount *= difficulty == 0 ? .55f : difficulty == 2 ? 1.2f : .85f;
            }
            if(vehicle.IsPlayer)amount*=DevTuning.Current.incomingDamage;
            else if(source && source.GetComponentInParent<VehicleController>() is VehicleController attacker && attacker.IsPlayer) amount*=DevTuning.Current.playerDamage;
            if(amount<=0)return;
            float previousHealth=Health;
            LastDamageTime=Time.time;LastDamagePoint=hitPoint;LastDamageSource=source;
            Health = Mathf.Max(0, Health - amount);
            Vector3 hit = transform.InverseTransformPoint(hitPoint);
            float componentDamage = amount / MaxHealth * 1.5f;
            if (hit.z > .7f)
            {
                Radiator = Mathf.Max(0, Radiator - componentDamage * 1.1f);
                Engine = Mathf.Max(0, Engine - componentDamage * .55f);
            }
            else if (Mathf.Abs(hit.x) > .55f)
            {
                Wheels = Mathf.Max(0, Wheels - componentDamage);
                Suspension = Mathf.Max(0, Suspension - componentDamage * .85f);
            }
            else
            {
                Transmission = Mathf.Max(0, Transmission - componentDamage * .9f);
                Engine = Mathf.Max(0, Engine - componentDamage * .4f);
            }
            if (Health <= 0) Die(source);
            CombatFeedback.ReportHit(vehicle,previousHealth-Health,hitPoint,source,IsDead);
        }
        public void DamageComponent(string component, float amount)
        {
            amount = Mathf.Clamp01(amount);
            switch (component)
            {
                case "Engine": Engine = Mathf.Max(0, Engine - amount); break;
                case "Radiator": Radiator = Mathf.Max(0, Radiator - amount); break;
                case "Transmission": Transmission = Mathf.Max(0, Transmission - amount); break;
                case "Wheels": Wheels = Mathf.Max(0, Wheels - amount); break;
                case "Suspension": Suspension = Mathf.Max(0, Suspension - amount); break;
            }
        }
        public void Repair(float amount)
        {
            if (IsDead || amount <= 0) return;
            Health = Mathf.Min(MaxHealth, Health + amount);
            float restore = amount / MaxHealth * 1.8f;
            Engine = Mathf.Min(1, Engine + restore); Radiator = Mathf.Min(1, Radiator + restore);
            Transmission = Mathf.Min(1, Transmission + restore); Wheels = Mathf.Min(1, Wheels + restore);
            Suspension = Mathf.Min(1, Suspension + restore);
        }
        void Update()
        {
            if (IsDead || vehicle == null || GameManager.Instance == null || !GameManager.Instance.IsPlaying) return;
            if (Health / MaxHealth < .52f && Time.time > smokeAt)
            {
                smokeAt = Time.time + .15f;
                bool burning = Health / MaxHealth < .23f;
                ExplosionSystem.Burst(transform.position + transform.forward * .8f + Vector3.up * 1.5f,
                    burning ? new Color(.95f, .27f, .07f, .8f) : new Color(.2f, .22f, .23f, .5f), burning ? 5 : 3, 1.6f);
            }
        }
        void Die(GameObject source)
        {
            IsDead = true;
            vehicle.Body.linearDamping = 2;
            vehicle.Body.angularDamping = 3;
            var ai = GetComponent<EnemyAI>();
            if (ai != null) ai.enabled = false;
            var sourceVehicle = source != null ? source.GetComponentInParent<VehicleController>() : null;
            var friendly = ai != null && ai.IsFriendly;
            if (!vehicle.IsPlayer && !friendly)
            {
                GameManager.Instance?.Mission?.RegisterKill();
                CombatPickup.DropFromEnemy(vehicle, ai != null ? ai.Archetype : 0, ai != null ? ai.Faction : EnemyFaction.Sunsprawl);
            }
            ExplosionSystem.Detonate(transform.position + Vector3.up * .9f, vehicle.IsPlayer ? 7 : 6, 65, source != null ? source : gameObject, ExplosionKind.Vehicle);
            if (vehicle.Visual != null)
            {
                foreach (var renderer in vehicle.Visual.GetComponentsInChildren<Renderer>())
                {
                    var block = new MaterialPropertyBlock(); block.SetColor("_BaseColor", new Color(.13f, .11f, .1f));
                    block.SetColor("_Color", new Color(.13f, .11f, .1f)); renderer.SetPropertyBlock(block);
                }
            }
            if (vehicle.IsPlayer) GameManager.Instance?.FailMission();
            else
            {
                // Salvage is credited by MissionManager; a wreck stays briefly as physical cover.
                Destroy(gameObject, 3.5f);
            }
        }
    }
}
