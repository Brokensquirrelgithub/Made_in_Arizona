using UnityEngine;

namespace MadeInArizona
{
    public sealed class VehicleDamage : MonoBehaviour
    {
        public float Health { get; private set; }
        public float MaxHealth { get; private set; }
        public bool IsDead { get; private set; }
        public bool Invulnerable { get; set; }
        /// <summary>Some defeated hostiles keep moving briefly and can collide with live cars.</summary>
        public bool IsSpinningWreck { get; private set; }
        public int FailedDepth { get; private set; }
        readonly float[] shares = { .5f, 0, 0, .15f, .15f, .2f };
        public float SegmentMax(HealthCategory category) => MaxHealth * shares[(int)category];
        public float SegmentHealth(HealthCategory category)
        {
            int index = (int)category;
            float inside = 0;
            for (int i = index + 1; i < HealthRules.SegmentCount; i++) inside += SegmentMax((HealthCategory)i);
            return Mathf.Clamp(Health - inside, 0, SegmentMax(category));
        }
        public bool EngineReached => Health < SegmentMax(HealthCategory.Engine);
        public bool ExteriorGone => SegmentHealth(HealthCategory.Exterior) <= .00001f;
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

        public void Initialize(VehicleController owner, float health, float[] healthShares = null)
        {
            vehicle = owner; baseHealth=health; durabilityMultiplier=1; MaxHealth = Health = Mathf.Max(1,health*HealthMultiplier); IsDead = false; IsSpinningWreck = false;
            FailedDepth=0;
            var source=healthShares!=null&&healthShares.Length==HealthRules.SegmentCount?healthShares:owner.Stats.healthShares;
            for(int i=0;i<shares.Length;i++)shares[i]=source[i];
        }
        public void SetNetworkHealth(float health, float maximum, int failedDepth = -1)
        {
            MaxHealth = Mathf.Max(1, maximum);
            Health = Mathf.Clamp(health, 0, MaxHealth);
            IsDead = Health <= 0;
            if(failedDepth>=0)SetFailedDepth(Mathf.Clamp(failedDepth,0,4));
            else UpdateFailures();
        }
        void SetFailedDepth(int depth)
        {
            if(depth==FailedDepth)return;
            int previous=FailedDepth;
            FailedDepth=depth;
            vehicle?.ApplyDamageProfile(depth,previous);
        }
        void UpdateFailures()
        {
            int depth=FailedDepth;
            for(int i=1;i<=4;i++)
                if(SegmentMax((HealthCategory)i)>.00001f && SegmentHealth((HealthCategory)i)<=.00001f)
                    depth=Mathf.Max(depth,i);
            while(depth>0 && (SegmentMax((HealthCategory)depth)<=.00001f ||
                SegmentHealth((HealthCategory)depth)>=SegmentMax((HealthCategory)depth)*HealthRules.ReactivationFraction-.00001f))depth--;
            SetFailedDepth(depth);
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
            UpdateFailures();
        }
        /// <summary>Keep the reached layer and its fill when installed-part durability changes.</summary>
        public void Reconfigure(float health,float[] healthShares)
        {
            if(IsDead || healthShares==null || healthShares.Length!=HealthRules.SegmentCount)return;
            int front=5;
            float fill=1;
            if(Health<MaxHealth-.001f)
                for(int i=0;i<HealthRules.SegmentCount;i++)
                {
                    float maximum=SegmentMax((HealthCategory)i);
                    if(maximum<=.001f || SegmentHealth((HealthCategory)i)<=.001f)continue;
                    front=i;fill=SegmentHealth((HealthCategory)i)/maximum;break;
                }
            bool full=Health>=MaxHealth-.001f;
            baseHealth=health;
            MaxHealth=Mathf.Max(1,baseHealth*HealthMultiplier*durabilityMultiplier);
            for(int i=0;i<shares.Length;i++)shares[i]=healthShares[i];
            if(full)Health=MaxHealth;
            else
            {
                Health=0;
                for(int i=front+1;i<HealthRules.SegmentCount;i++)Health+=SegmentMax((HealthCategory)i);
                Health+=SegmentMax((HealthCategory)front)*fill;
            }
            UpdateFailures();
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
        public void ApplyDamage(float amount, Vector3 hitPoint, GameObject source, bool explosive = false, bool environmental = false, bool forceSpinoutOnKill = false)
        {
            if (CoopSession.IsRemoteClient) return;
            if (IsDead || amount <= 0 || vehicle == null) return;
            if (vehicle.IsPlayer && Invulnerable) return;
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
            UpdateFailures();
            LastDamageFraction=(previousHealth-Health)/Mathf.Max(1,MaxHealth);
            if (vehicle.IsPlayer)
            {
                AudioManager.Instance?.PlayHurt(LastDamageFraction);
                CameraController.Instance?.Shake(Mathf.Clamp(.06f+LastDamageFraction*3f,.06f,.6f));
            }
            if (Health <= 0) Die(source, forceSpinoutOnKill);
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
                if (IsSpinningWreck || !other || other == vehicle) continue;
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
                if (!other || other == vehicle || other.Damage == null || !other.Damage.IsDead || other.Damage.IsSpinningWreck) continue;
                foreach (var wreck in other.GetComponentsInChildren<Collider>())
                    foreach (var mine in colliders) Physics.IgnoreCollision(mine, wreck, true);
            }
        }
        public void Repair(float amount)
        {
            if (IsDead || amount <= 0) return;
            float before = Health;
            Health = Mathf.Min(MaxHealth, Health + amount);
            if (Health - before > .5f) { LastRepairTime = Time.time; LastRepairAmount = Health - before; }
            UpdateFailures();
        }
        void Update()
        {
            if (IsDead || vehicle == null || GameManager.Instance == null || !GameManager.Instance.IsPlaying) return;
            if (ExteriorGone && Time.time > smokeAt)
            {
                smokeAt = Time.time + .15f;
                bool burning = EngineReached;
                ExplosionSystem.Burst(transform.position + transform.forward * .8f + Vector3.up * 1.5f,
                    burning ? new Color(.95f, .27f, .07f, .8f) : new Color(.2f, .22f, .23f, .5f), burning ? 5 : 3, 1.6f);
            }
        }
        public void DetonateSpinningWreck(Vector3 impact)
        {
            if (!IsSpinningWreck || CoopSession.IsRemoteClient) return;
            IsSpinningWreck = false;
            ExplosionSystem.Detonate(impact, 8f, 90f, LastDamageSource ? LastDamageSource : gameObject,
                ExplosionKind.Vehicle, environmental: true);
            Destroy(gameObject);
        }

        void Die(GameObject source, bool forceSpinoutOnKill)
        {
            IsDead = true;
            var ai = GetComponent<EnemyAI>();
            if (ai != null) ai.enabled = false;
            var sourceVehicle = source != null ? source.GetComponentInParent<VehicleController>() : null;
            var friendly = ai != null && ai.IsFriendly;
            IsSpinningWreck = !vehicle.IsPlayer && !friendly && (forceSpinoutOnKill || Random.value < .25f);
            vehicle.Body.linearDamping = IsSpinningWreck ? .35f : 2;
            vehicle.Body.angularDamping = IsSpinningWreck ? .22f : 3;
            if (!vehicle.IsPlayer && !friendly)
            {
                GameManager.Instance?.Mission?.RegisterKill(sourceVehicle);
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
            if (IsSpinningWreck && !vehicle.Body.isKinematic)
            {
                float side = Random.value < .5f ? -1f : 1f;
                vehicle.Body.maxAngularVelocity = 10f;
                vehicle.Body.linearVelocity = Vector3.ClampMagnitude(vehicle.Body.linearVelocity, 30f) +
                    transform.right * side * Random.Range(7f, 12f) + transform.forward * 3f;
                vehicle.Body.angularVelocity += Vector3.up * side * Random.Range(6f, 10f);
            }
            if (vehicle.IsPlayer) GameManager.Instance?.BeginPlayerDeath();
            else
            {
                // The soot colour still marks it as defeated; only the brief spinout can hit another car.
                Destroy(gameObject, IsSpinningWreck ? 5f : 3.5f);
            }
        }
    }
}
