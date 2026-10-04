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
        float smokeAt, baseHealth, durabilityMultiplier = 1;
        public float LastDamageTime { get; private set; } = float.NegativeInfinity;
        /// <summary>When hull health was last restored, and by how much, for the heal tell.</summary>
        public float LastRepairTime { get; private set; } = float.NegativeInfinity;
        public float LastRepairAmount { get; private set; }
        public Vector3 LastDamagePoint { get; private set; }
        public GameObject LastDamageSource { get; private set; }
        /// <summary>Share of maximum health removed by the most recent hit, for player hurt feedback.</summary>
        public float LastDamageFraction { get; private set; }

        public void Initialize(VehicleController owner, float health)
        {
            vehicle = owner; baseHealth=health; durabilityMultiplier=1; MaxHealth = Health = Mathf.Max(1,health*HealthMultiplier); IsDead = false;
            Engine = Radiator = Transmission = Wheels = Suspension = 1;
        }
        public void SetDurabilityMultiplier(float multiplier)
        {
            durabilityMultiplier=Mathf.Max(1,multiplier);
            ApplyHealthTuning();
        }
        public void ApplyHealthTuning()
        {
            if(vehicle==null||IsDead)return;
            float fraction=Health/Mathf.Max(1,MaxHealth);
            MaxHealth=Mathf.Max(1,baseHealth*HealthMultiplier*durabilityMultiplier);
            Health=MaxHealth*fraction;
        }
        float HealthMultiplier
        {
            get
            {
                if(vehicle.IsPlayer)return DevTuning.Current.playerHealth;
                var ai=vehicle.GetComponent<EnemyAI>();
                return DevTuning.Current.enemyHealth*(ai&&ai.IsFriendly?1f:SpawnManager.EnemyHealthMultiplier);
            }
        }
        /// <summary>
        /// Friendly fire: rounds, rams and burns never hurt a vehicle on the attacker's own side. Explosions can hurt
        /// allies except the escort, which only takes direct hostile attacks. Environmental blasts carry their own flag.
        /// </summary>
        public void ApplyDamage(float amount, Vector3 hitPoint, GameObject source, bool explosive = false, bool environmental = false)
        {
            if (IsDead || amount <= 0 || vehicle == null) return;
            var attacker = source ? source.GetComponentInParent<VehicleController>() : null;
            // Escort health is a mission objective. Scenery, wrecks, self damage and allied splash never deplete it.
            if (!vehicle.IsPlayer && vehicle.FriendlyToPlayer && (environmental || !attacker || attacker.FriendlyToPlayer)) return;
            if (!explosive && attacker && attacker != vehicle && VehicleController.Allied(attacker, vehicle)) return;
            if (vehicle.IsPlayer && GameManager.Instance != null && GameManager.Instance.Save != null)
            {
                int difficulty = GameManager.Instance.Save.settings.difficulty;
                amount *= difficulty == 0 ? .55f : difficulty == 2 ? 1.2f : .85f;
            }
            if(vehicle.IsPlayer)amount*=DevTuning.Current.incomingDamage;
            else if(attacker && attacker.IsPlayer) amount*=DevTuning.Current.playerDamage;
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
            LastDamageFraction=(previousHealth-Health)/Mathf.Max(1,MaxHealth);
            if (vehicle.IsPlayer)
            {
                AudioManager.Instance?.PlayHurt(LastDamageFraction);
                CameraController.Instance?.Shake(Mathf.Clamp(.06f+LastDamageFraction*3f,.06f,.6f));
            }
            if (Health <= 0) Die(source);
            CombatFeedback.ReportHit(vehicle,previousHealth-Health,hitPoint,source,IsDead);
        }
        /// <summary>
        /// A destroyed car keeps its smoking shell but stops being an obstacle: it no longer collides with other
        /// vehicles, and moves to the Ignore Raycast layer so shots, suspension, AI sight and blasts pass through it.
        /// It still rests on the ground.
        /// </summary>
        void BecomeWreck()
        {
            foreach (var t in GetComponentsInChildren<Transform>(true)) t.gameObject.layer = WreckLayer;
            var own = GetComponentsInChildren<Collider>();
            foreach (var other in VehicleController.Active)
            {
                if (!other || other == vehicle) continue;
                foreach (var theirs in other.GetComponentsInChildren<Collider>())
                    foreach (var mine in own) Physics.IgnoreCollision(mine, theirs, true);
            }
            // Weak points and other child colliders are decoration now; only the body keeps the shell on the ground.
            Collider body = vehicle ? vehicle.Hull : null;
            foreach (var mine in own) if (mine != body) mine.enabled = false;
        }
        public const int WreckLayer = 2;
        /// <summary>New vehicles ignore wrecks that are still burning out.</summary>
        public static void IgnoreWrecks(VehicleController vehicle)
        {
            if (!vehicle) return;
            var colliders = vehicle.GetComponentsInChildren<Collider>();
            foreach (var other in VehicleController.Active)
            {
                if (!other || other == vehicle || other.Damage == null || !other.Damage.IsDead) continue;
                foreach (var wreck in other.GetComponentsInChildren<Collider>())
                    foreach (var mine in colliders) Physics.IgnoreCollision(mine, wreck, true);
            }
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
            float before = Health;
            Health = Mathf.Min(MaxHealth, Health + amount);
            if (Health - before > .5f) { LastRepairTime = Time.time; LastRepairAmount = Health - before; }
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
            ExplosionSystem.Detonate(transform.position + Vector3.up * .9f, vehicle.IsPlayer ? 7 : 6, 65, source != null ? source : gameObject, ExplosionKind.Vehicle, environmental:true);
            if (vehicle.Visual != null)
            {
                foreach (var renderer in vehicle.Visual.GetComponentsInChildren<Renderer>())
                {
                    var block = new MaterialPropertyBlock(); block.SetColor("_BaseColor", new Color(.13f, .11f, .1f));
                    // Only legacy materials have _Color; setting it on the others logged a warning per panel.
                    if (renderer.sharedMaterial && renderer.sharedMaterial.HasProperty("_Color")) block.SetColor("_Color", new Color(.13f, .11f, .1f));
                    // Burnt paint loses its clear coat and gloss.
                    block.SetFloat("_ClearCoat", 0); block.SetFloat("_Smoothness", .12f); block.SetFloat("_Metallic", .05f);
                    renderer.SetPropertyBlock(block);
                }
            }
            BecomeWreck();
            if (vehicle.IsPlayer) GameManager.Instance?.BeginPlayerDeath();
            else
            {
                // Salvage is credited by MissionManager; the wreck is visual only and burns out shortly.
                Destroy(gameObject, 3.5f);
            }
        }
    }
}
