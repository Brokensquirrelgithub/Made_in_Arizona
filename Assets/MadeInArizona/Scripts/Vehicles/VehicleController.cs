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
        /// <summary>Burn, stall and stuck-charge state, created the first time a weapon applies one.</summary>
        public VehicleAfflictions Afflictions { get; internal set; }
        public bool IsPlayer { get; private set; }
        public float SpeedKph => Body != null ? Body.linearVelocity.magnitude * 3.6f : 0;
        public float RPM { get; private set; } = 900;
        public float Throttle { get; private set; }
        public float BoostCharge { get; private set; } = 1;
        /// <summary>True on physics steps where nitro is actually firing (held, charged, driving forward).</summary>
        public bool Boosting { get; private set; }
        public float DriftAmount { get; private set; }
        public bool Grounded { get; private set; }
        public int Gear { get; private set; } = 1;
        /// <summary>0–1 blend of the player's drift handling; eases out over the tuned recovery time.</summary>
        public float DriftBlend { get; private set; }
        /// <summary>AI pace multiplier on acceleration and top speed, raised by EnemyAI to catch up from off screen.</summary>
        public float Pace { get; set; } = 1;
        Vector2 aiMove;
        Vector3 aiAim = Vector3.forward;
        bool aiFire, aiReverse;
        /// <summary>
        /// AI vehicles only engage reverse when their controller asks for it (stuck recovery). Friendly route
        /// followers such as the escort van turn round instead of backing across the map.
        /// </summary>
        public bool AutoReverse { get; set; } = true;
        /// <summary>Nitro tank size relative to the original tank. There is no passive refill; pickups top it up.</summary>
        public const float NitroCapacity = 3f;
        const float NitroBurnRate = .22f / NitroCapacity;
        /// <summary>Drive multiplier while boosting: four times the old 65% extra shove.</summary>
        public const float NitroThrust = 1 + .65f * 4;
        /// <summary>Nitro hits hardest from a standstill: an extra multiplier that fades out by about 65 km/h.</summary>
        const float NitroLaunch = 1.6f, NitroLaunchFadeSpeed = 18f;
        /// <summary>
        /// Collision hull on a child transform. The rigidbody only yaws, so the hull is tilted on its own to lie
        /// parallel to the ground under the wheels; a level box plowed its front edge into every climb.
        /// </summary>
        public BoxCollider Hull { get; private set; }
        /// <summary>Chassis tilt that follows the ground under the wheels (Unity Euler: +pitch is nose down, +roll lifts the right side).</summary>
        float chassisPitch, chassisRoll, appliedHullPitch, appliedHullRoll;
        const float MaxChassisPitch = 38, MaxChassisRoll = 32;
        /// <summary>Wheel rays reach this far past full droop so the chassis can still read the slope ahead and behind.</summary>
        const float GroundProbe = 1.5f;
        Quaternion ChassisRotation => Body.rotation * Quaternion.Euler(chassisPitch, 0, chassisRoll);
        Vector3 lastVelocity, bodyAcceleration;
        Vector3 preCollisionVelocity;
        float wheelAngle, visualTilt, visualWeight, visualRoll, dustTimer, collisionCooldown;
        int driveDirection = 1;
        bool wasDrifting;
        float paceSettleUntil;
        Transform[] wheels;
        readonly Vector3[] suspensionPoints = new Vector3[4];
        readonly RaycastHit[] groundHits = new RaycastHit[12];
        float[] gears = { 3.5f, 2.25f, 1.55f, 1.12f, .86f, .68f };
        bool initialized;
        const float LaunchBoost = 1.45f, LaunchFadeSpeed = 16f;

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
            if (Hull == null)
            {
                var hullObject = new GameObject("Collision hull");
                hullObject.transform.SetParent(transform, false);
                Hull = hullObject.AddComponent<BoxCollider>();
            }
            chassisPitch = chassisRoll = appliedHullPitch = appliedHullRoll = 0;
            Hull.transform.localRotation = Quaternion.identity;
            Hull.center = new Vector3(0, .83f, 0);
            Hull.size = new Vector3(Mathf.Max(1.3f, stats.trackWidth + .25f), 1.13f, Mathf.Max(2.6f, stats.wheelbase + .9f));
            var material = new PhysicsMaterial("Sliding body") { dynamicFriction = .18f, staticFriction = .25f, bounciness = .12f };
            Hull.material = material;
            if (Visual != null) Destroy(Visual.gameObject);
            Visual = VehicleVisual.Build(definition, transform, !isPlayer, faction);
            if (isPlayer)
            {
                var exhaust = GetComponent<NitroExhaust>();
                if (exhaust == null) exhaust = gameObject.AddComponent<NitroExhaust>();
                exhaust.Bind(this);
            }
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
            VehicleDamage.IgnoreWrecks(this);
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
        public void SetAIInput(Vector2 move, Vector3 aim, bool fire, bool reverse = false) { aiMove = move; aiAim = aim; aiFire = fire; aiReverse = reverse; }
        public void Repair(float amount) { Damage?.Repair(amount); }
        /// <summary>Adds a share of the tank; nitro-recovery parts scale what a pickup restores.</summary>
        public void RefillNitro(float amount) { BoostCharge = Mathf.Clamp01(BoostCharge + amount * Mathf.Clamp(Stats != null ? Stats.cooling : 1, .25f, 3)); }
        /// <summary>The player and any friendly AI are one side; hostile crews are the other.</summary>
        public bool FriendlyToPlayer { get { if (IsPlayer) return true; var ai = GetComponent<EnemyAI>(); return ai != null && ai.IsFriendly; } }
        public static bool Allied(VehicleController a, VehicleController b) => a && b && a.FriendlyToPlayer == b.FriendlyToPlayer;
        /// <summary>Largest horizontal dimension of the body collider; scenery is judged against it.</summary>
        public float BodyLength => Hull ? Mathf.Max(Hull.size.x, Hull.size.z) : 3.5f;

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
            Boosting = false;
            if (!initialized || Damage.IsDead || Body.isKinematic) return;
            if (GameManager.Instance == null || !GameManager.Instance.IsPlaying)
            {
                Body.linearVelocity = Vector3.zero; Body.angularVelocity = Vector3.zero; return;
            }
            Vector2 input = IsPlayer && InputManager.Instance != null ? InputManager.Instance.Move : aiMove;
            // A stalled engine (shock weapons) coasts; steering and throttle return when it restarts.
            if (VehicleAfflictions.Stalled(this)) input = Vector2.zero;
            bool drifting = IsPlayer && InputManager.Instance != null && InputManager.Instance.Drift;
            var tuning = DevTuning.Current;
            bool boosting = IsPlayer && InputManager.Instance != null && InputManager.Instance.Boost && BoostCharge > .002f && input.sqrMagnitude > .1f;
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
                if (IsPlayer || AutoReverse) driveDirection = SelectDriveDirection(forwardAlignment, forwardSpeed, driveDirection);
                else driveDirection = aiReverse ? -1 : 1;
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
                // Launch assist: an extra shove from a standstill that fades out by ~58 km/h, so starts feel
                // responsive and controllable without raising top speed.
                acceleration *= Mathf.Lerp(LaunchBoost, 1, Mathf.Clamp01(speed / LaunchFadeSpeed));
                acceleration *= Mathf.Lerp(.32f, 1, Damage.Engine) * drivetrain;
                if (IsPlayer) acceleration *= DevTuning.Current.acceleration;
                if (surface == SurfaceKind.Sand || surface == SurfaceKind.Mud) acceleration *= Stats.drivetrain == Drivetrain.AWD ? .88f : .62f;
                if (!IsPlayer) acceleration *= Pace;
                if (boosting)
                {
                    float thrust = 1 + (NitroThrust - 1) * tuning.nitro;
                    acceleration *= thrust * Mathf.Lerp(NitroLaunch, 1, Mathf.Clamp01(speed / NitroLaunchFadeSpeed));
                    BoostCharge = Mathf.Max(0, BoostCharge - Time.fixedDeltaTime * NitroBurnRate);
                }
                Boosting = boosting;
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
            SweepScenery(speed);
            preCollisionVelocity = Body.linearVelocity;
            bodyAcceleration = Vector3.Lerp(bodyAcceleration, (Body.linearVelocity - lastVelocity) / Time.fixedDeltaTime, .5f);
            lastVelocity = Body.linearVelocity;
        }
        bool SupportSuspension()
        {
            int contacts = 0;
            float restLength = .85f + Mathf.Clamp(Stats.rideHeight * .2f, .03f, .18f);
            float travel = Mathf.Clamp(Stats.suspensionTravel, .15f, 1.2f);
            float reach = restLength + travel;
            // The wheels hang off a chassis tilted to match the ground, so on a climb all four still reach the slope
            // and the body rides at its normal height instead of perching on the front axle.
            Quaternion chassis = ChassisRotation;
            Vector3 down = chassis * Vector3.down;
            Vector3 frontSum = Vector3.zero, rearSum = Vector3.zero, leftSum = Vector3.zero, rightSum = Vector3.zero;
            int front = 0, rear = 0, left = 0, right = 0;
            for (int i = 0; i < 4; i++)
            {
                Vector3 origin = Body.position + chassis * suspensionPoints[i];
                int count = Physics.RaycastNonAlloc(origin, down, groundHits, reach + GroundProbe, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                float nearest = float.MaxValue;
                Vector3 point = Vector3.zero, normal = Vector3.up;
                for (int h = 0; h < count; h++)
                {
                    if (groundHits[h].rigidbody == Body || groundHits[h].normal.y < .3f || groundHits[h].distance >= nearest) continue;
                    nearest = groundHits[h].distance; point = groundHits[h].point; normal = groundHits[h].normal;
                }
                if (nearest == float.MaxValue) continue;
                if (i < 2) { frontSum += point; front++; } else { rearSum += point; rear++; }
                if (i % 2 == 0) { leftSum += point; left++; } else { rightSum += point; right++; }
                if (nearest > reach) continue;
                contacts++;
                float spring = Mathf.Clamp(Stats.springStiffness / Body.mass, 20, 100) * Mathf.Lerp(.45f, 1, Damage.Suspension);
                float damp = Mathf.Clamp(Stats.damping / Body.mass, 2, 16);
                float compression = restLength - nearest;
                // Damp the wheel's motion relative to the ground it rolls over. Damping absolute vertical speed (the
                // old behaviour) fought every climb and sank the car into the hillside; on flat ground they match.
                float compressionRate = -Vector3.Dot(Body.GetPointVelocity(origin), normal) / Mathf.Max(.3f, -Vector3.Dot(normal, down));
                float acceleration = 9.81f / 4 + compression * spring + compressionRate * damp;
                Body.AddForceAtPosition(Vector3.up * Mathf.Clamp(acceleration, -3, 65), origin, ForceMode.Acceleration);
            }
            TiltChassis(front, rear, left, right, frontSum, rearSum, leftSum, rightSum);
            return contacts > 1;
        }
        /// <summary>
        /// Eases the chassis toward the plane under the wheels and tilts the collision hull with it. With no ground
        /// in reach the car slowly levels out, so jumps land nose-first only when the terrain below asks for it.
        /// </summary>
        void TiltChassis(int front, int rear, int left, int right, Vector3 frontSum, Vector3 rearSum, Vector3 leftSum, Vector3 rightSum)
        {
            float follow = 1 - Mathf.Exp(-Time.fixedDeltaTime * 16), settle = Time.fixedDeltaTime * 30;
            Vector3 forward = Body.rotation * Vector3.forward, side = Body.rotation * Vector3.right;
            if (front > 0 && rear > 0)
            {
                Vector3 span = frontSum / front - rearSum / rear;
                float run = Vector3.Dot(span, forward);
                if (run > .4f) chassisPitch = Mathf.Lerp(chassisPitch, Mathf.Clamp(-Mathf.Atan2(span.y, run) * Mathf.Rad2Deg, -MaxChassisPitch, MaxChassisPitch), follow);
            }
            else if (front + rear == 0) chassisPitch = Mathf.MoveTowards(chassisPitch, 0, settle);
            if (left > 0 && right > 0)
            {
                Vector3 span = rightSum / right - leftSum / left;
                float run = Vector3.Dot(span, side);
                if (run > .4f) chassisRoll = Mathf.Lerp(chassisRoll, Mathf.Clamp(Mathf.Atan2(span.y, run) * Mathf.Rad2Deg, -MaxChassisRoll, MaxChassisRoll), follow);
            }
            else if (left + right == 0) chassisRoll = Mathf.MoveTowards(chassisRoll, 0, settle);
            // Only re-pose the hull for a visible change; every pose change is a collider update for the physics scene.
            if (Mathf.Abs(chassisPitch - appliedHullPitch) > .2f || Mathf.Abs(chassisRoll - appliedHullRoll) > .2f)
            {
                appliedHullPitch = chassisPitch; appliedHullRoll = chassisRoll;
                Hull.transform.localRotation = Quaternion.Euler(appliedHullPitch, 0, appliedHullRoll);
            }
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
            // The body sits on the slope, then squats under power and dives under braking. The old pitch dipped the
            // nose under acceleration, which read as the car tipping forward into every hill it tried to climb.
            visualTilt = Mathf.Lerp(visualTilt, chassisPitch, Time.deltaTime * 18);
            visualWeight = Mathf.Lerp(visualWeight, Mathf.Clamp(-acceleration.z * .23f, -7, 7), Time.deltaTime * 7);
            Visual.localRotation = Quaternion.Euler(visualTilt + visualWeight, 0, chassisRoll + visualRoll);
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
                bool belowCarSize = prop.Size < BodyLength;
                if (prop.TryDestroyFromVehicle(force * Mathf.Sqrt(Body.mass) * .35f * propDamage, point, gameObject, out _) && belowCarSize)
                {
                    float retainedMomentum = IsPlayer ? DevTuning.Current.propMomentum : .65f;
                    Body.linearVelocity = Vector3.Lerp(Body.linearVelocity, preCollisionVelocity, Mathf.Clamp01(retainedMomentum));
                }
            }
            if(force<6 || Time.time<collisionCooldown)return;
            collisionCooldown=Time.time+.25f;
            var other = collision.collider.GetComponentInParent<VehicleDamage>();
            // VehicleDamage refuses non-explosive damage between vehicles on the same side, so crews never ram-kill each other.
            if (other != null && other != Damage) other.ApplyDamage(force * 2.4f * Mathf.Clamp(Body.mass / 1000, .5f, 3), point, gameObject);
            // No vehicle is hurt by landing on, scraping or bottoming out against the ground.
            if (!IsGroundContact(collision, other, prop)) Damage.ApplyDamage(Mathf.Max(0, force - 11) * .5f, point, collision.gameObject);
            ExplosionSystem.Burst(point, new Color(1, .65f, .17f), 9, 4);
            if (IsPlayer) CameraController.Instance?.Shake(Mathf.Clamp01(force / 28) * .25f);
        }
        /// <summary>Terrain meshes, graded surfaces and any mostly horizontal contact count as ground.</summary>
        static bool IsGroundContact(Collision collision, VehicleDamage vehicle, DestructionSystem prop)
        {
            if (vehicle != null || prop != null) return false;
            if (collision.collider is MeshCollider mesh && !mesh.convex) return true;
            for (int i = 0; i < collision.contactCount; i++)
                if (Mathf.Abs(collision.GetContact(i).normal.y) < .55f) return false;
            return collision.contactCount > 0;
        }

        // ---- Scenery sweep (every vehicle; hostiles get the same small-obstacle rules as the player) ----
        // Props smaller than half the car are driven straight through (and knocked apart); props between half and
        // the full car length break away without costing speed while the car is moving briskly. Anything as large
        // as the car stays solid and uses ordinary collision.
        public const float BreakAwaySpeed = 6f; // m/s, about 22 km/h
        readonly Collider[] sweepHits = new Collider[48];
        readonly HashSet<DestructionSystem> ghosted = new HashSet<DestructionSystem>();
        readonly List<DestructionSystem> ghostScratch = new List<DestructionSystem>();
        void SweepScenery(float speed)
        {
            var body = Hull;
            if (!body) return;
            float length = BodyLength;
            Vector3 center = body.transform.TransformPoint(body.center);
            Vector3 lead = Body.linearVelocity * Time.fixedDeltaTime * 3;
            Vector3 extents = body.size * .5f + Vector3.one * .45f;
            int count = Physics.OverlapBoxNonAlloc(center + lead * .5f, extents + new Vector3(Mathf.Abs(lead.x), Mathf.Abs(lead.y), Mathf.Abs(lead.z)) * .5f + Vector3.one * .25f,
                sweepHits, body.transform.rotation, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            Bounds car = new Bounds(center, Vector3.zero);
            car.Encapsulate(body.bounds); car.Expand(.3f);
            for (int i = 0; i < count; i++)
            {
                var hit = sweepHits[i];
                if (!hit || hit.attachedRigidbody == Body) continue;
                var prop = hit.GetComponentInParent<DestructionSystem>();
                if (!prop || prop.IsDestroyed) continue;
                float size = prop.Size;
                if (size >= length) continue;
                bool small = size < length * .5f;
                if (!small && speed < BreakAwaySpeed) continue;
                if (ghosted.Add(prop)) prop.SetVehicleCollision(body, false);
                if (car.Intersects(prop.WorldBounds)) prop.SmashFromVehicle(hit.ClosestPointOnBounds(center), gameObject);
            }
            // Medium props approached too slowly become solid again, unless the car is already inside them.
            if (ghosted.Count == 0) return;
            ghostScratch.Clear();
            foreach (var prop in ghosted)
            {
                if (!prop || prop.IsDestroyed) { ghostScratch.Add(prop); continue; }
                bool small = prop.Size < length * .5f;
                if (!small && speed < BreakAwaySpeed && !car.Intersects(prop.WorldBounds)) { prop.SetVehicleCollision(body, true); ghostScratch.Add(prop); }
            }
            foreach (var prop in ghostScratch) ghosted.Remove(prop);
        }
    }
}
