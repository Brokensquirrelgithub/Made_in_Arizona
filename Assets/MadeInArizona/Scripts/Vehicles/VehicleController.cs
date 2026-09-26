using System.Collections.Generic;
using UnityEngine;

namespace MadeInArizona
{
    [RequireComponent(typeof(Rigidbody))]
    public sealed class VehicleController : MonoBehaviour
    {
        public static readonly List<VehicleController> Active = new List<VehicleController>();
        public VehicleDamage Damage { get; private set; }
        public WeaponSystem Weapons { get; private set; }
        public Rigidbody Body { get; private set; }
        public Transform Visual { get; private set; }
        public VehicleStats Stats { get; private set; }
        public bool IsPlayer { get; private set; }
        public float SpeedKph => Body != null ? Body.linearVelocity.magnitude * 3.6f : 0;
        public float RPM { get; private set; } = 900;
        public float Throttle { get; private set; }
        public float BoostCharge { get; private set; } = 1;
        public float RepairCharge { get; private set; } = 1;
        public float DriftAmount { get; private set; }
        public bool Grounded { get; private set; }
        public int Gear { get; private set; } = 1;
        /// <summary>0–1 blend of the player's drift handling; eases out over the tuned recovery time.</summary>
        public float DriftBlend { get; private set; }
        /// <summary>AI pace multiplier on acceleration and top speed, raised by EnemyAI to catch up from off screen.</summary>
        public float Pace { get; set; } = 1;
        Vector2 aiMove;
        Vector3 aiAim = Vector3.forward;
        bool aiFire;
        Vector3 lastVelocity, bodyAcceleration;
        Vector3 preCollisionVelocity;
        float wheelAngle, visualPitch, visualRoll, dustTimer, collisionCooldown;
        int driveDirection = 1;
        bool wasDrifting;
        float paceSettleUntil;
        Transform[] wheels;
        readonly Vector3[] suspensionPoints = new Vector3[4];
        readonly RaycastHit[] groundHits = new RaycastHit[12];
        float[] gears = { 3.5f, 2.25f, 1.55f, 1.12f, .86f, .68f };
        bool initialized;

        void OnEnable() { if (!Active.Contains(this)) Active.Add(this); }
        void OnDisable() { Active.Remove(this); }
        public void Initialize(VehicleDefinition definition, VehicleStats stats, bool isPlayer, EnemyFaction faction = EnemyFaction.Sunsprawl)
        {
            Stats = stats; IsPlayer = isPlayer;
            Body = GetComponent<Rigidbody>();
            Body.mass = Mathf.Max(300, stats.mass);
            Body.linearDamping = .08f; Body.angularDamping = 3f;
            Body.interpolation = RigidbodyInterpolation.Interpolate;
            Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            Body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            Body.centerOfMass = new Vector3(0, .36f, .08f);
            Body.maxAngularVelocity = 4;
            var box = GetComponent<BoxCollider>();
            if (box == null) box = gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0, .83f, 0);
            box.size = new Vector3(Mathf.Max(1.3f, stats.trackWidth + .25f), 1.13f, Mathf.Max(2.6f, stats.wheelbase + .9f));
            var material = new PhysicsMaterial("Sliding body") { dynamicFriction = .18f, staticFriction = .25f, bounciness = .12f };
            box.material = material;
            if (Visual != null) Destroy(Visual.gameObject);
            Visual = VehicleVisual.Build(definition, transform, !isPlayer, faction);
            wheels = new Transform[4];
            string[] names = { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" };
            for (int i = 0; i < 4; i++)
            {
                wheels[i] = FindChild(Visual, names[i]);
                suspensionPoints[i] = new Vector3((i % 2 == 0 ? -1 : 1) * stats.trackWidth * .44f, .85f, (i < 2 ? 1 : -1) * stats.wheelbase * .43f);
            }
            Damage = GetComponent<VehicleDamage>();
            if (Damage == null) Damage = gameObject.AddComponent<VehicleDamage>();
            Damage.Initialize(this, Mathf.Max(60, stats.maxHealth));
            Weapons = GetComponent<WeaponSystem>();
            if (Weapons == null) Weapons = gameObject.AddComponent<WeaponSystem>();
            Weapons.Initialize(this);
            ExplosionSystem.IgnoreVehicleCollisions(this);
            initialized = true;
        }
        public static Transform FindChild(Transform parent, string childName)
        {
            if (parent == null) return null;
            foreach (Transform child in parent)
            {
                if (child.name == childName) return child;
                var found = FindChild(child, childName); if (found != null) return found;
            }
            return null;
        }
        public void SetAIInput(Vector2 move, Vector3 aim, bool fire) { aiMove = move; aiAim = aim; aiFire = fire; }
        public void Repair(float amount) { Damage?.Repair(amount); }
        public void RefillNitro(float amount) { BoostCharge = Mathf.Clamp01(BoostCharge + amount); }

        void Update()
        {
            if (!initialized || Damage.IsDead || GameManager.Instance == null || !GameManager.Instance.IsPlaying) return;
            var input = InputManager.Instance;
            Vector3 aimDirection = aiAim;
            if (IsPlayer && input != null)
            {
                aimDirection = new Vector3(input.Aim.x, input.AimElevation, input.Aim.y);
                Weapons.AimAt(aimDirection);
                if (input.Primary) Weapons.FirePrimary(aimDirection);
                if (input.Secondary) Weapons.FireSecondary(aimDirection);
                if (input.Repair && RepairCharge > 0 && Damage.Health < Damage.MaxHealth)
                {
                    float rate = 34;
                    var save = GameManager.Instance.Save;
                    if (save != null && ContentCatalog.Drivers.Length > 0)
                        rate *= ContentCatalog.Drivers[Mathf.Clamp(save.selectedDriver, 0, ContentCatalog.Drivers.Length - 1)].repairMultiplier;
                    Repair(rate * Time.deltaTime);
                    RepairCharge = Mathf.Max(0, RepairCharge - .09f * Time.deltaTime);
                    if (Time.frameCount % 8 == 0) ExplosionSystem.Burst(transform.position + Vector3.up, new Color(.2f, 1, .68f), 2, .5f);
                }
                else RepairCharge = Mathf.Min(1, RepairCharge + Time.deltaTime * .007f);
            }
            else
            {
                Weapons.AimAt(aimDirection);
                if (aiFire) Weapons.FirePrimary(aimDirection);
            }
            AnimateBody();
        }
        void FixedUpdate()
        {
            if (!initialized || Damage.IsDead || Body.isKinematic) return;
            if (GameManager.Instance == null || !GameManager.Instance.IsPlaying)
            {
                Body.linearVelocity = Vector3.zero; Body.angularVelocity = Vector3.zero; return;
            }
            Vector2 input = IsPlayer && InputManager.Instance != null ? InputManager.Instance.Move : aiMove;
            bool drifting = IsPlayer && InputManager.Instance != null && InputManager.Instance.Drift;
            var tuning = DevTuning.Current;
            bool boosting = IsPlayer && InputManager.Instance != null && InputManager.Instance.Boost && BoostCharge > .03f && input.sqrMagnitude > .1f;
            Throttle = Mathf.MoveTowards(Throttle, input.magnitude, Time.fixedDeltaTime * 6);
            Grounded = SupportSuspension();
            Vector3 planar = Vector3.ProjectOnPlane(Body.linearVelocity, Vector3.up);
            float forwardSpeed = Vector3.Dot(planar, transform.forward);
            float lateralSpeed = Vector3.Dot(planar, transform.right);
            float speed = planar.magnitude;
            // Drift engages quickly and hands grip back over the tuned recovery time so exits feel controllable.
            DriftBlend = drifting && speed > 3 ? Mathf.MoveTowards(DriftBlend, 1, Time.fixedDeltaTime / .08f)
                : Mathf.MoveTowards(DriftBlend, 0, Time.fixedDeltaTime / Mathf.Max(.05f, tuning.driftRecovery));
            SurfaceKind surface = WorldBuilder.SurfaceAt(transform.position);
            float surfaceGrip = SurfaceGrip(surface);
            float wheelGrip = Mathf.Lerp(.5f, 1f, Damage.Wheels);
            float diffGrip = Stats.differential == Differential.Locked ? 1.13f : Stats.differential == Differential.LimitedSlip ? 1.07f : .92f;
            float grip = Mathf.Max(.35f, Stats.grip) * surfaceGrip * wheelGrip * diffGrip;
            if (IsPlayer) grip *= DevTuning.Current.grip;
            float drivetrain = Stats.drivetrain == Drivetrain.AWD ? 1.16f : Stats.drivetrain == Drivetrain.FWD ? 1.04f : .98f;
            if (input.sqrMagnitude > .04f)
            {
                Vector3 desired = new Vector3(input.x, 0, input.y);
                float forwardAlignment = Vector3.Dot(transform.forward, desired.normalized);
                driveDirection = SelectDriveDirection(forwardAlignment, forwardSpeed, driveDirection);
                if (driveDirection < 0) boosting = false;
                Vector3 driveForward = transform.forward * driveDirection;
                float angle = Vector3.SignedAngle(driveForward, desired, Vector3.up);
                float steering = Mathf.Clamp(angle / 42, -1, 1);
                float handling = Mathf.Lerp(.5f, 1, Damage.Suspension) * Mathf.Lerp(.72f, 1, Damage.Wheels);
                float lockPenalty = Stats.differential == Differential.Locked ? .84f : 1;
                float turnRate = Mathf.Clamp(Stats.turnSpeed, 35, 240) * LowSpeedTurnMultiplier(speed) * handling * lockPenalty;
                if (IsPlayer) turnRate *= DevTuning.Current.steering;
                turnRate *= Mathf.Lerp(1, Mathf.Max(1, tuning.driftYaw), DriftBlend);
                float yaw = steering * turnRate * Mathf.Deg2Rad;
                // Pressing drift while steering flicks the tail out, like a handbrake entry without losing speed.
                if (drifting && !wasDrifting && Grounded && speed > 6 && Mathf.Abs(steering) > .15f)
                    Body.AddTorque(Vector3.up * Mathf.Sign(steering) * tuning.driftKick * Mathf.Deg2Rad, ForceMode.VelocityChange);
                float steeringResponse = Mathf.Lerp(12, 8, Mathf.Clamp01(speed / 8));
                if (Grounded) Body.AddTorque(Vector3.up * (yaw - Body.angularVelocity.y) * steeringResponse, ForceMode.Acceleration);
                float alignment = Mathf.Clamp01((180 - Mathf.Abs(angle)) / 110);
                float targetThrottle = Throttle * Mathf.Lerp(.2f, 1, alignment) * Mathf.Lerp(1, tuning.driftThrottle, DriftBlend);
                float wheelRPM = Mathf.Abs(forwardSpeed) / (2 * Mathf.PI * .34f) * 60;
                if (driveDirection < 0) Gear = 1;
                float ratio = gears[Gear - 1] * Mathf.Max(2.5f, Stats.finalDrive);
                float desiredRPM = Mathf.Max(900 + targetThrottle * 900, wheelRPM * ratio);
                RPM = Mathf.Lerp(RPM, desiredRPM, Time.fixedDeltaTime * 8);
                if (driveDirection > 0 && RPM > 6100 && Gear < gears.Length) { Gear++; RPM *= .72f; }
                if (RPM < 1900 && Gear > 1) Gear--;
                RPM = Mathf.Clamp(RPM, 850, 7200);
                float torqueCurve = .62f + .38f * Mathf.Sin(Mathf.Clamp01((RPM - 800) / 7000) * Mathf.PI);
                float wheelForce = Stats.torque * torqueCurve * ratio / .34f;
                float powerForce = Mathf.Max(40, Stats.horsepower) * 745.7f / Mathf.Max(7, speed);
                float acceleration = Mathf.Clamp(Mathf.Min(wheelForce, powerForce) / Body.mass * 2.65f, 3.2f, 27);
                acceleration *= Mathf.Lerp(.32f, 1, Damage.Engine) * drivetrain;
                if (IsPlayer) acceleration *= DevTuning.Current.acceleration;
                if (surface == SurfaceKind.Sand || surface == SurfaceKind.Mud) acceleration *= Stats.drivetrain == Drivetrain.AWD ? .88f : .62f;
                if (!IsPlayer) acceleration *= Pace;
                if (boosting) { acceleration *= 1.65f; BoostCharge -= Time.fixedDeltaTime * .22f; }
                else BoostCharge = Mathf.Min(1, BoostCharge + Time.fixedDeltaTime * .075f * Mathf.Max(.25f, Stats.cooling));
                float maxSpeed = Mathf.Max(50, Stats.maxSpeed) / 3.6f * Mathf.Lerp(.55f, 1, Damage.Transmission) * (boosting ? 1.25f : 1) * (IsPlayer ? 1 : Pace);
                float speedInDriveDirection = forwardSpeed * driveDirection;
                float directionalMaxSpeed = driveDirection < 0 ? Mathf.Min(maxSpeed * .34f, 13f) : maxSpeed;
                float directionalAcceleration = driveDirection < 0 ? acceleration * .78f : acceleration;
                if (Grounded && speedInDriveDirection < directionalMaxSpeed)
                    Body.AddForce(driveForward * directionalAcceleration * targetThrottle, ForceMode.Acceleration);
                if (Damage.Wheels < .45f && Grounded) Body.AddTorque(Vector3.up * Mathf.Sin(Time.time * 6) * 1.4f * (1 - Damage.Wheels) * speed / 12, ForceMode.Acceleration);
            }
            else
            {
                driveDirection = 1;
                RPM = Mathf.Lerp(RPM, 900, Time.fixedDeltaTime * 3);
                BoostCharge = Mathf.Min(1, BoostCharge + Time.fixedDeltaTime * .075f * Mathf.Max(.25f, Stats.cooling));
                if (Grounded) Body.AddForce(-planar * 1.8f, ForceMode.Acceleration);
            }
            if (Grounded)
            {
                float lateralDamping = Mathf.Lerp(Mathf.Clamp(grip * 5.5f, 1.5f, 12), tuning.driftGrip, DriftBlend);
                if (Stats.drivetrain == Drivetrain.RWD && Throttle > .8f && speed < 15) lateralDamping *= .8f;
                Body.AddForce(-transform.right * lateralSpeed * lateralDamping, ForceMode.Acceleration);
                if (DriftBlend > 0 && speed > 1) Body.AddForce(-planar / speed * tuning.driftSpeedLoss * DriftBlend, ForceMode.Acceleration);
                // After a catch-up burst ends, AI settles back to its normal top speed instead of coasting in fast.
                if (!IsPlayer && Pace > 1.01f) paceSettleUntil = Time.time + 2.5f;
                if (!IsPlayer && Time.time < paceSettleUntil)
                {
                    float cap = Mathf.Max(50, Stats.maxSpeed) / 3.6f * Pace * 1.05f;
                    if (speed > cap) Body.AddForce(-planar / speed * Mathf.Min(10, (speed - cap) * 1.5f), ForceMode.Acceleration);
                }
                if (surface == SurfaceKind.Water || surface == SurfaceKind.Mud) Body.AddForce(-planar * .65f, ForceMode.Acceleration);
            }
            DriftAmount = Mathf.Clamp01(Mathf.Abs(lateralSpeed) / 12);
            wasDrifting = drifting;
            if(GeneratedWorld.Active)
            {
                if(!GeneratedWorld.Contains(transform.position) || transform.position.y<GeneratedWorld.HeightAt(transform.position)-12)
                {
                    Vector3 safe=GameManager.Instance.World.PlayerSpawn;
                    Body.position=safe;Body.linearVelocity=Vector3.zero;
                }
            }
            else if (transform.position.y < -8 || Mathf.Abs(transform.position.x) > 118 || transform.position.z < -105 || transform.position.z > 160)
            {
                Vector3 safe = transform.position;
                safe.x = Mathf.Clamp(safe.x, -98, 98); safe.z = Mathf.Clamp(safe.z, -84, 139); safe.y = 2;
                Body.position = safe; Body.linearVelocity *= .1f;
            }
            if (Grounded && speed > 5 && Time.time > dustTimer)
            {
                dustTimer = Time.time + (surface == SurfaceKind.Asphalt ? .18f : .065f);
                Color dust = surface == SurfaceKind.Water ? new Color(.38f, .65f, .7f, .5f) : new Color(.7f, .49f, .28f, .4f);
                if (surface != SurfaceKind.Asphalt || DriftAmount > .18f) ExplosionSystem.Burst(transform.position - transform.forward * 1.5f + Vector3.up * .25f, dust, 2, 1.5f + speed * .035f);
            }
            preCollisionVelocity = Body.linearVelocity;
            bodyAcceleration = Vector3.Lerp(bodyAcceleration, (Body.linearVelocity - lastVelocity) / Time.fixedDeltaTime, .5f);
            lastVelocity = Body.linearVelocity;
        }
        bool SupportSuspension()
        {
            int contacts = 0;
            float restLength = .85f + Mathf.Clamp(Stats.rideHeight * .2f, .03f, .18f);
            float travel = Mathf.Clamp(Stats.suspensionTravel, .15f, 1.2f);
            for (int i = 0; i < 4; i++)
            {
                Vector3 origin = transform.TransformPoint(suspensionPoints[i]);
                int count = Physics.RaycastNonAlloc(origin, Vector3.down, groundHits, restLength + travel, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                float nearest = float.MaxValue;
                for (int h = 0; h < count; h++)
                {
                    if (groundHits[h].rigidbody == Body || groundHits[h].normal.y < .3f) continue;
                    nearest = Mathf.Min(nearest, groundHits[h].distance);
                }
                if (nearest == float.MaxValue) continue;
                contacts++;
                float spring = Mathf.Clamp(Stats.springStiffness / Body.mass, 20, 100) * Mathf.Lerp(.45f, 1, Damage.Suspension);
                float damp = Mathf.Clamp(Stats.damping / Body.mass, 2, 16);
                float compression = restLength - nearest;
                float acceleration = 9.81f / 4 + compression * spring - Body.GetPointVelocity(origin).y * damp;
                Body.AddForceAtPosition(Vector3.up * Mathf.Clamp(acceleration, -3, 65), origin, ForceMode.Acceleration);
            }
            return contacts > 1;
        }
        void AnimateBody()
        {
            if (Visual == null) return;
            Vector3 localVelocity = transform.InverseTransformDirection(Body.linearVelocity);
            // Acceleration is sampled per physics step; per-render-frame sampling alternated between zero and double.
            Vector3 acceleration = transform.InverseTransformDirection(bodyAcceleration);
            float narrow = Mathf.Clamp(Stats.rideHeight / Mathf.Max(1, Stats.trackWidth), .15f, 1);
            float targetRoll = -localVelocity.x * (1 + narrow * 1.5f) - Body.angularVelocity.y * localVelocity.z * .18f;
            targetRoll += (1 - Damage.Suspension) * 9;
            visualRoll = Mathf.Lerp(visualRoll, Mathf.Clamp(targetRoll, -19, 19), Time.deltaTime * 6);
            visualPitch = Mathf.Lerp(visualPitch, Mathf.Clamp(acceleration.z * .23f - Body.linearVelocity.y * 1.1f, -10, 10), Time.deltaTime * 7);
            Visual.localRotation = Quaternion.Euler(visualPitch, 0, visualRoll);
            wheelAngle += localVelocity.z * Time.deltaTime * 150;
            if (wheels != null)
                for (int i = 0; i < wheels.Length; i++)
                    if (wheels[i] != null) wheels[i].localRotation = Quaternion.Euler(wheelAngle, i < 2 ? Body.angularVelocity.y * 12 : 0, 0);
        }
        public static float SurfaceGrip(SurfaceKind surface)
        {
            switch (surface)
            {
                case SurfaceKind.Asphalt: return 1.25f;
                case SurfaceKind.Dirt: return .9f;
                case SurfaceKind.Sand: return .63f;
                case SurfaceKind.Gravel: return .72f;
                case SurfaceKind.Mud: return .5f;
                case SurfaceKind.Water: return .62f;
                case SurfaceKind.Rocks: return .82f;
                case SurfaceKind.Oil: return .22f;
                case SurfaceKind.Debris: return .68f;
                default: return 1;
            }
        }
        public static int SelectDriveDirection(float forwardAlignment, float forwardSpeed, int currentDirection)
        {
            // Hysteresis keeps diagonal input from flickering between first and reverse.
            if (currentDirection < 0)
                return forwardAlignment < .08f && forwardSpeed < 4f ? -1 : 1;
            return forwardAlignment < -.35f && Mathf.Abs(forwardSpeed) < 5.5f ? -1 : 1;
        }
        public static float LowSpeedTurnMultiplier(float speed)
        {
            return Mathf.Lerp(1.15f, 1f, Mathf.Clamp01(speed / 7f));
        }
        void OnCollisionEnter(Collision collision)
        {
            if (!initialized || Damage.IsDead || GameManager.Instance == null || !GameManager.Instance.IsPlaying) return;
            float force = collision.relativeVelocity.magnitude;
            if(force<2)return;
            Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
            var prop = collision.collider.GetComponentInParent<DestructionSystem>();
            if (prop != null)
            {
                float propDamage = IsPlayer ? DevTuning.Current.propDamage : 1f;
                if (prop.TryDestroyFromVehicle(force * Mathf.Sqrt(Body.mass) * .35f * propDamage, point, gameObject, out bool smallProp) && smallProp)
                {
                    float retainedMomentum = IsPlayer ? DevTuning.Current.propMomentum : .65f;
                    Body.linearVelocity = Vector3.Lerp(Body.linearVelocity, preCollisionVelocity, Mathf.Clamp01(retainedMomentum));
                }
            }
            if(force<6 || Time.time<collisionCooldown)return;
            collisionCooldown=Time.time+.25f;
            var other = collision.collider.GetComponentInParent<VehicleDamage>();
            if (other != null && other != Damage) other.ApplyDamage(force * 2.4f * Mathf.Clamp(Body.mass / 1000, .5f, 3), point, gameObject);
            Damage.ApplyDamage(Mathf.Max(0, force - 11) * .5f, point, collision.gameObject);
            ExplosionSystem.Burst(point, new Color(1, .65f, .17f), 9, 4);
            if (IsPlayer) CameraController.Instance?.Shake(Mathf.Clamp01(force / 28) * .25f);
        }
    }
}
