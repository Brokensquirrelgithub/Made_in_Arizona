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
        /// <summary>The vehicle this controller was built from (engine layout, display data).</summary>
        public VehicleDefinition Definition { get; private set; }
        /// <summary>Burn, stall and stuck-charge state, created the first time a weapon applies one.</summary>
        public VehicleAfflictions Afflictions { get; internal set; }
        public bool IsPlayer { get; private set; }
        public bool IsNetworkProxy { get; private set; }
        Vector3 networkPosition;
        Vector3 networkVelocity;
        Quaternion networkRotation;
        float networkYawRate;
        public bool HasRemoteInput { get; private set; }
        public CoopControls RemoteControls { get; private set; }
        float remoteInputAt;
        CoopControls FreshRemoteControls => Time.unscaledTime - remoteInputAt < .4f ? RemoteControls : default;
        public void SetRemoteControls(CoopControls controls)
        {
            HasRemoteInput = true;
            RemoteControls = controls;
            remoteInputAt = Time.unscaledTime;
        }
        public void SetNetworkProxy()
        {
            IsNetworkProxy = true;
            Body.isKinematic = true;
            Body.detectCollisions = false;
            Body.interpolation = RigidbodyInterpolation.None;
            networkPosition = transform.position;
            networkRotation = transform.rotation;
        }
        public void ApplyNetworkMotion(Vector3 position, Quaternion rotation, Vector3 velocity,
            float health, float maximumHealth, float boost, float rpm, float throttle, float yawRate, bool boosting)
        {
            networkPosition = position;
            networkRotation = rotation;
            networkVelocity = velocity;
            networkYawRate = yawRate;
            Damage?.SetNetworkHealth(health, maximumHealth);
            BoostCharge = Mathf.Clamp01(boost);
            RPM = rpm;
            Throttle = throttle;
            Boosting = boosting;
        }
        public float SpeedKph => IsNetworkProxy ? networkVelocity.magnitude * 3.6f
            : Body != null ? Body.linearVelocity.magnitude * 3.6f : 0;
        public float RPM { get; private set; } = 900;
        public float Throttle { get; private set; }
        public float BoostCharge { get; private set; } = 1;
        /// <summary>True on physics steps where nitro is actually firing (held, charged, driving forward).</summary>
        public bool Boosting { get; private set; }
        public float DriftAmount { get; private set; }
        /// <summary>0-1 wheelspin of the driven wheels: the drive asking for more than the surface grips, mostly at launch.</summary>
        public float WheelSpin { get; private set; }
        /// <summary>0-1 sideways scrub of the tyres (drifts, slides, flat-out U-turns).</summary>
        public float SideSlip { get; private set; }
        /// <summary>Surface under the car's centre, from the last physics step.</summary>
        public SurfaceKind Surface { get; private set; } = SurfaceKind.Dirt;
        public bool Grounded { get; private set; }
        public int Gear { get; private set; } = 1;
        /// <summary>0–1 blend of the player's drift handling; eases out over the tuned recovery time.</summary>
        public float DriftBlend { get; private set; }
        /// <summary>
        /// The car holding this one on a tow-hook cable (<see cref="TowLink"/>). A towed car slides sideways freely so it
        /// can be whipped round, and a hard hit into anything solid while towed wrecks it.
        /// </summary>
        public VehicleController TowedBy { get; internal set; }
        /// <summary>The car this one is currently towing, for co-op cable replication.</summary>
        public VehicleController Towing { get; internal set; }
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
        const float MaxChassisPitch = 38, MaxChassisRoll = 32, MaxCorneringLean = 8, MaxAirTilt = 20;
        /// <summary>
        /// Resting on the underside (the hull touching supporting ground) with fewer than two wheels in suspension reach:
        /// high-centred on a ridge, a lip or a cliff edge, or landed across one. The car keeps some traction to drive off,
        /// and the chassis settles onto the surface it rests on instead of hanging tilted with its wheels in the air.
        /// </summary>
        public bool Beached { get; private set; }
        Vector3 bellyNormal = Vector3.up;
        readonly RaycastHit[] bellyHits = new RaycastHit[8];
        const float BeachedTraction = 1f;
        /// <summary>Wheels within suspension reach, and wheels resting on drivable ground on each side (straight-down probes).</summary>
        int wheelContacts, supportLeft, supportRight;
        /// <summary>Sideways push that tips a car balanced with one whole side over nothing off the edge, as gravity would.</summary>
        const float TipOffAcceleration = 5f;
        /// <summary>Added to normal gravity while airborne (about 2.2 g in total), so jumps are short and punchy.</summary>
        const float ExtraAirGravity = 12f;
        /// <summary>Drive probe controls: switch the extra air gravity off, and count the player's steps under it.</summary>
        internal static bool ExtraAirGravityEnabled = true;
        internal static int ExtraGravitySteps;
        /// <summary>
        /// Safety net for any geometry that still catches a car off its wheels (hung by the underside on a lip or face):
        /// after this long with throttle held and no movement, the car hops toward the direction being asked for.
        /// </summary>
        const float StuckHopDelay = .8f;
        float stuckTime;
        /// <summary>How far past full droop a wheel may be and still count as supporting the car sideways.</summary>
        const float SideSupportMargin = .3f;
        /// <summary>Steepest face (about 53°) a suspension spring pushes against. Drivable grades top out near 33°.</summary>
        const float MinSpringNormal = .6f;
        /// <summary>Steepest surface (45°) that counts as ground to rest on; cliff faces are walls, not support.</summary>
        const float MinSupportNormal = .7f;
        /// <summary>Tilt probes start this far above each suspension point, so ground rising ahead on a climb is still read.</summary>
        const float TiltProbeRise = 1f;
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
        /// <summary>Per wheel (FL, FR, RL, RR): ground under the suspension in the last physics step, for tyre effects.</summary>
        readonly bool[] wheelOnGround = new bool[4];
        readonly Vector3[] wheelGroundPoint = new Vector3[4], wheelGroundNormal = new Vector3[4];
        float[] gears = { 3.5f, 2.25f, 1.55f, 1.12f, .86f, .68f };
        bool initialized;
        const float LaunchBoost = 1.45f, LaunchFadeSpeed = 16f;

        void OnEnable() { if (!Active.Contains(this)) Active.Add(this); }
        void OnDisable() { Active.Remove(this); }
        public void Initialize(VehicleDefinition definition, VehicleStats stats, bool isPlayer, EnemyFaction faction = EnemyFaction.Sunsprawl)
        {
            Stats = stats; IsPlayer = isPlayer; Definition = definition;
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
            // Fix the inertia from the level hull. Left automatic, Unity recomputed it every time the hull tilted, which
            // rotated the principal axes the X/Z rotation locks act in, and the "locked" body rolled and pitched for real.
            Physics.SyncTransforms();
            Body.ResetInertiaTensor();
            Vector3 inertia = Body.inertiaTensor;
            Body.automaticInertiaTensor = false;
            Body.inertiaTensor = inertia;
            Body.inertiaTensorRotation = Quaternion.identity;
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
            // Tyre smoke, skid marks and gravel spray; dust that builds up on the body; headlights at sunset.
            var tires = GetComponent<TireEffects>();
            if (tires == null) tires = gameObject.AddComponent<TireEffects>();
            tires.Bind(this);
            var dust = GetComponent<VehicleDust>();
            if (dust == null) dust = gameObject.AddComponent<VehicleDust>();
            dust.Bind(this, definition);
            var lights = GetComponent<VehicleLights>();
            if (lights == null) lights = gameObject.AddComponent<VehicleLights>();
            lights.Bind(this);
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
        /// <summary>Visual wheel <paramref name="index"/> (FL, FR, RL, RR).</summary>
        public Transform Wheel(int index) => wheels != null && index >= 0 && index < wheels.Length ? wheels[index] : null;
        /// <summary>Whether wheel <paramref name="index"/> had ground under it in the last physics step, and where.</summary>
        public bool WheelContact(int index, out Vector3 point, out Vector3 normal)
        {
            point = wheelGroundPoint[index]; normal = wheelGroundNormal[index];
            return wheelOnGround[index];
        }
        /// <summary>Wheels the engine drives: the rear pair, the front pair, or all four.</summary>
        public bool Driven(int index) => Stats == null || Stats.drivetrain == Drivetrain.AWD || (Stats.drivetrain == Drivetrain.FWD ? index < 2 : index >= 2);
        public void SetAIInput(Vector2 move, Vector3 aim, bool fire, bool reverse = false) { aiMove = move; aiAim = aim; aiFire = fire; aiReverse = reverse; }
        public void Repair(float amount) { Damage?.Repair(amount); }
        /// <summary>Adds a share of the tank. Pickups are the only refill; no part changes how much they restore.</summary>
        public void RefillNitro(float amount) { BoostCharge = Mathf.Clamp01(BoostCharge + amount); }
        /// <summary>The player and any friendly AI are one side; hostile crews are the other.</summary>
        public bool FriendlyToPlayer { get { if (IsPlayer) return true; var ai = GetComponent<EnemyAI>(); return ai != null && ai.IsFriendly; } }
        public static bool Allied(VehicleController a, VehicleController b) => a && b && a.FriendlyToPlayer == b.FriendlyToPlayer;
        /// <summary>Largest horizontal dimension of the body collider; scenery is judged against it.</summary>
        public float BodyLength => Hull ? Mathf.Max(Hull.size.x, Hull.size.z) : 3.5f;

        void Update()
        {
            if (IsNetworkProxy)
            {
                float blend = 1 - Mathf.Exp(-15 * Time.unscaledDeltaTime);
                Vector3 position = Vector3.Distance(transform.position, networkPosition) > 30 ? networkPosition : Vector3.Lerp(transform.position, networkPosition, blend);
                Quaternion rotation = Quaternion.Slerp(transform.rotation, networkRotation, blend);
                transform.SetPositionAndRotation(position, rotation);
                if (Body) { Body.position = position; Body.rotation = rotation; }
                // Guests do not simulate the host's cars; slide state for tyre audio is read from the replicated motion.
                Vector3 flat = new Vector3(networkVelocity.x, 0, networkVelocity.z);
                float sideways = Mathf.Abs(Vector3.Dot(flat, transform.right));
                DriftAmount = Mathf.Clamp01(sideways / 12);
                SideSlip = Mathf.MoveTowards(SideSlip, Mathf.Clamp01((sideways - 1.5f) / 5f), Time.unscaledDeltaTime * 6);
                Surface = WorldBuilder.SurfaceAt(transform.position);
                Grounded = true;
                if (initialized && Damage != null && !Damage.IsDead && GameManager.Instance != null && GameManager.Instance.IsPlaying) AnimateBody();
                return;
            }
            if (!initialized || Damage.IsDead || GameManager.Instance == null || !GameManager.Instance.IsPlaying) return;
            var input = InputManager.Instance;
            Vector3 aimDirection = aiAim;
            if (IsPlayer && HasRemoteInput)
            {
                var controls = FreshRemoteControls;
                aimDirection = new Vector3(controls.aim.x, controls.elevation, controls.aim.y);
                Weapons.AimAt(aimDirection);
                if (controls.primary) Weapons.FirePrimary(aimDirection);
                if (controls.secondary) Weapons.FireSecondary(aimDirection);
            }
            else if (IsPlayer && input != null)
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
            if (!initialized || Damage.IsDead || Body.isKinematic || IsNetworkProxy) return;
            KeepYawOnly();
            if (GameManager.Instance == null || !GameManager.Instance.IsPlaying)
            {
                Body.linearVelocity = Vector3.zero; Body.angularVelocity = Vector3.zero; return;
            }
            CoopControls remote = FreshRemoteControls;
            Vector2 input = IsPlayer && HasRemoteInput ? remote.move : IsPlayer && InputManager.Instance != null ? InputManager.Instance.Move : aiMove;
            // Shoulder reverse (Settings > Controls): the button selects reverse and the stick never does. Held with the
            // stick centred it backs straight up; with the stick pushed, the tail swings toward that direction.
            bool reverseHeld = IsPlayer && (HasRemoteInput ? remote.reverse : InputManager.Instance != null && InputManager.Instance.Reverse);
            bool manualReverse = IsPlayer && (HasRemoteInput ? remote.manualReverse : InputManager.Instance != null && InputManager.Instance.ManualReverse);
            if (reverseHeld && input.sqrMagnitude < .04f) input = new Vector2(-transform.forward.x, -transform.forward.z);
            // A stalled engine (shock weapons) coasts; steering and throttle return when it restarts.
            if (VehicleAfflictions.Stalled(this)) input = Vector2.zero;
            bool drifting = IsPlayer && (HasRemoteInput ? remote.drift : InputManager.Instance != null && InputManager.Instance.Drift);
            var tuning = IsPlayer ? DevTuning.ForCar(this) : DevTuning.Current;
            bool boosting = IsPlayer && (HasRemoteInput ? remote.boost : InputManager.Instance != null && InputManager.Instance.Boost) && BoostCharge > .002f && input.sqrMagnitude > .1f;
            Throttle = Mathf.MoveTowards(Throttle, input.magnitude, Time.fixedDeltaTime * 6);
            Grounded = SupportSuspension();
            // Cars come back down quickly: extra gravity whenever no wheel is on the ground (player and hostiles alike).
            if (!Grounded && wheelContacts == 0 && !Beached && ExtraAirGravityEnabled)
            {
                Body.AddForce(Vector3.down * ExtraAirGravity, ForceMode.Acceleration);
                if (IsPlayer) ExtraGravitySteps++;
            }
            // Any wheel down or the belly resting on ground still gives drive, so a car hung on a lip can get itself off.
            bool traction = Grounded || Beached || wheelContacts > 0;
            if (!Grounded && input.sqrMagnitude > .25f && new Vector2(Body.linearVelocity.x, Body.linearVelocity.z).sqrMagnitude < .25f && (Beached || wheelContacts > 0 || Body.linearVelocity.y > -.5f))
            {
                stuckTime += Time.fixedDeltaTime;
                if (stuckTime > StuckHopDelay)
                {
                    stuckTime = 0;
                    Vector3 asked = new Vector3(input.x, 0, input.y).normalized;
                    Body.AddForce(asked * 5 + Vector3.up * 3.5f, ForceMode.VelocityChange);
                }
            }
            else stuckTime = 0;
            // Balanced on a lip with one whole side over nothing: tip off toward the drop instead of hanging there.
            if (!Grounded && (wheelContacts > 0 || Beached) && (supportLeft == 0) != (supportRight == 0))
                Body.AddForce(Body.rotation * Vector3.right * (supportLeft == 0 ? -1 : 1) * TipOffAcceleration, ForceMode.Acceleration);
            Vector3 planar = Vector3.ProjectOnPlane(Body.linearVelocity, Vector3.up);
            float forwardSpeed = Vector3.Dot(planar, transform.forward);
            float lateralSpeed = Vector3.Dot(planar, transform.right);
            float speed = planar.magnitude;
            // Drift engages quickly and hands grip back over the tuned recovery time so exits feel controllable.
            DriftBlend = drifting && speed > 3 ? Mathf.MoveTowards(DriftBlend, 1, Time.fixedDeltaTime / .08f)
                : Mathf.MoveTowards(DriftBlend, 0, Time.fixedDeltaTime / Mathf.Max(.05f, tuning.driftRecovery));
            SurfaceKind surface = WorldBuilder.SurfaceAt(transform.position);
            Surface = surface;
            float spin = 0;
            float surfaceGrip = SurfaceGrip(surface);
            float wheelGrip = Mathf.Lerp(.5f, 1f, Damage.Wheels);
            float diffGrip = Stats.differential == Differential.Locked ? 1.13f : Stats.differential == Differential.LimitedSlip ? 1.07f : .92f;
            float grip = Mathf.Max(.35f, Stats.grip) * surfaceGrip * wheelGrip * diffGrip;
            if (IsPlayer) grip *= tuning.grip;
            float drivetrain = Stats.drivetrain == Drivetrain.AWD ? 1.16f : Stats.drivetrain == Drivetrain.FWD ? 1.04f : .98f;
            if (input.sqrMagnitude > .04f)
            {
                Vector3 desired = new Vector3(input.x, 0, input.y);
                float forwardAlignment = Vector3.Dot(transform.forward, desired.normalized);
                if (IsPlayer && (reverseHeld || manualReverse)) driveDirection = reverseHeld ? -1 : 1;
                else if (IsPlayer || AutoReverse) driveDirection = SelectDriveDirection(forwardAlignment, forwardSpeed, driveDirection);
                else driveDirection = aiReverse ? -1 : 1;
                if (driveDirection < 0) boosting = false;
                Vector3 driveForward = transform.forward * driveDirection;
                float angle = Vector3.SignedAngle(driveForward, desired, Vector3.up);
                float steering = Mathf.Clamp(angle / 42, -1, 1);
                float handling = Mathf.Lerp(.5f, 1, Damage.Suspension) * Mathf.Lerp(.72f, 1, Damage.Wheels);
                float lockPenalty = Stats.differential == Differential.Locked ? .84f : 1;
                float turnRate = Mathf.Clamp(Stats.turnSpeed, 35, 240) * LowSpeedTurnMultiplier(speed) * handling * lockPenalty;
                if (IsPlayer) turnRate *= tuning.steering;
                turnRate *= Mathf.Lerp(1, Mathf.Max(1, tuning.driftYaw), DriftBlend);
                float yaw = steering * turnRate * Mathf.Deg2Rad;
                // Pressing drift while steering flicks the tail out, like a handbrake entry without losing speed.
                if (drifting && !wasDrifting && Grounded && speed > 6 && Mathf.Abs(steering) > .15f)
                    Body.AddTorque(Vector3.up * Mathf.Sign(steering) * tuning.driftKick * Mathf.Deg2Rad, ForceMode.VelocityChange);
                float steeringResponse = Mathf.Lerp(12, 8, Mathf.Clamp01(speed / 8));
                if (traction) Body.AddTorque(Vector3.up * (yaw - Body.angularVelocity.y) * steeringResponse, ForceMode.Acceleration);
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
                if (IsPlayer) acceleration *= tuning.acceleration;
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
                // Wheelspin for the tyre effects: the drive asks for more than the surface can grip, mostly from a
                // standstill or pinned against something. Strong cars smoke their tyres on asphalt, everyone roosts dirt.
                float gripLimit = 9.81f * surfaceGrip * (Stats.drivetrain == Drivetrain.AWD ? 1.45f : 1f);
                float launchSpin = 1 - Mathf.Clamp01(speed / (boosting ? 17f : 11f));
                if (traction && driveDirection > 0) spin = Mathf.Clamp01((directionalAcceleration * targetThrottle / gripLimit - 1.5f) * .5f) * launchSpin;
                if (traction && speedInDriveDirection < directionalMaxSpeed)
                    Body.AddForce(driveForward * directionalAcceleration * targetThrottle * (Grounded ? 1 : BeachedTraction), ForceMode.Acceleration);
                if (Damage.Wheels < .45f && Grounded) Body.AddTorque(Vector3.up * Mathf.Sin(Time.time * 6) * 1.4f * (1 - Damage.Wheels) * speed / 12, ForceMode.Acceleration);
            }
            else
            {
                driveDirection = 1;
                RPM = Mathf.Lerp(RPM, 900, Time.fixedDeltaTime * 3);
                if (Grounded && !TowedBy) Body.AddForce(-planar * 1.8f, ForceMode.Acceleration);
            }
            if (Grounded)
            {
                float lateralDamping = Mathf.Lerp(Mathf.Clamp(grip * 5.5f, 1.5f, 12), tuning.driftGrip, DriftBlend);
                if (Stats.drivetrain == Drivetrain.RWD && Throttle > .8f && speed < 15) lateralDamping *= .8f;
                // On a tow cable the tyres skid sideways, so the car swings out wide on the line instead of tracking.
                if (TowedBy) lateralDamping *= TowLink.TowedGrip;
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
            WheelSpin = Mathf.MoveTowards(WheelSpin, Grounded || Beached ? spin : 0, Time.fixedDeltaTime * 5);
            SideSlip = Grounded ? Mathf.Clamp01((Mathf.Abs(lateralSpeed) - 1.5f) / 5f) : 0;
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
                // Pavement throws no dust: sliding and burnouts there make tyre smoke instead (TireEffects).
                if (surface != SurfaceKind.Asphalt && surface != SurfaceKind.Oil) ExplosionSystem.Burst(transform.position - transform.forward * 1.5f + Vector3.up * .25f, dust, 2, 1.5f + speed * .035f);
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
            Vector3 frontSum = Vector3.zero, rearSum = Vector3.zero, leftSum = Vector3.zero, rightSum = Vector3.zero, supportNormal = Vector3.zero;
            int front = 0, rear = 0, left = 0, right = 0;
            for (int i = 0; i < 4; i++)
            {
                wheelOnGround[i] = false;
                Vector3 origin = Body.position + chassis * suspensionPoints[i];
                int count = Physics.RaycastNonAlloc(origin, down, groundHits, reach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                float nearest = float.MaxValue;
                Vector3 point = Vector3.zero, normal = Vector3.up;
                for (int h = 0; h < count; h++)
                {
                    // Faces steeper than ~53 degrees are walls, not road: a spring compressed against a rock face used to
                    // launch the car up and over it (the "bonk" off boulders and lips).
                    if (groundHits[h].rigidbody == Body || groundHits[h].normal.y < MinSpringNormal || groundHits[h].distance >= nearest) continue;
                    nearest = groundHits[h].distance; point = groundHits[h].point; normal = groundHits[h].normal;
                }
                if (nearest == float.MaxValue || nearest > reach) continue;
                contacts++;
                wheelOnGround[i] = true; wheelGroundPoint[i] = point; wheelGroundNormal[i] = normal;
                float spring = Mathf.Clamp(Stats.springStiffness / Body.mass, 20, 100) * Mathf.Lerp(.45f, 1, Damage.Suspension);
                float damp = Mathf.Clamp(Stats.damping / Body.mass, 2, 16);
                float compression = restLength - nearest;
                // Damp the wheel's motion relative to the ground it rolls over. Damping absolute vertical speed (the
                // old behaviour) fought every climb and sank the car into the hillside; on flat ground they match.
                float compressionRate = -Vector3.Dot(Body.GetPointVelocity(origin), normal) / Mathf.Max(.3f, -Vector3.Dot(normal, down));
                float acceleration = 9.81f / 4 + compression * spring + compressionRate * damp;
                Body.AddForceAtPosition(Vector3.up * Mathf.Clamp(acceleration, -3, 65), origin, ForceMode.Acceleration);
            }
            // Pitch and roll read the ground with straight-down probes at each wheel, independent of the current tilt:
            // from 1 m above the suspension point (the ground ahead on a climb) to just past full droop, drivable
            // surfaces only. Rays cast along the tilted chassis fed the tilt back into itself: a nose pitched up in
            // the air or a side leaning at a cliff kept reading ground that held it there, standing the car on its
            // back wheels or wedging it on a lip.
            for (int i = 0; i < 4; i++)
            {
                Vector3 probe = Body.position + Body.rotation * suspensionPoints[i] + Vector3.up * TiltProbeRise;
                int count = Physics.RaycastNonAlloc(probe, Vector3.down, groundHits, TiltProbeRise + reach + SideSupportMargin, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                float nearest = float.MaxValue; RaycastHit best = default;
                for (int h = 0; h < count; h++)
                    if (groundHits[h].rigidbody != Body && groundHits[h].normal.y >= MinSupportNormal && groundHits[h].distance < nearest) { nearest = groundHits[h].distance; best = groundHits[h]; }
                if (nearest == float.MaxValue) continue;
                if (i < 2) { frontSum += best.point; front++; } else { rearSum += best.point; rear++; }
                if (i % 2 == 0) { leftSum += best.point; left++; } else { rightSum += best.point; right++; }
                supportNormal += best.normal;
            }
            wheelContacts = contacts; supportLeft = left; supportRight = right;
            Beached = contacts < 2 && BellySupported(out bellyNormal);
            TiltChassis(front, rear, left, right, frontSum, rearSum, leftSum, rightSum, supportNormal);
            return contacts > 1;
        }
        /// <summary>
        /// Eases the chassis toward the plane under the wheels and tilts the collision hull with it. With no ground
        /// in reach the car slowly levels out, so jumps land nose-first only when the terrain below asks for it.
        /// </summary>
        void TiltChassis(int front, int rear, int left, int right, Vector3 frontSum, Vector3 rearSum, Vector3 leftSum, Vector3 rightSum, Vector3 supportNormal)
        {
            // In the air the body only drifts gently toward the ground below (and never far), so jumps stay calm.
            bool airborne = wheelContacts == 0 && !Beached;
            float follow = 1 - Mathf.Exp(-Time.fixedDeltaTime * (airborne ? 5 : 16)), settle = Time.fixedDeltaTime * 60;
            float pitchLimit = airborne ? MaxAirTilt : MaxChassisPitch, rollLimit = airborne ? MaxAirTilt : MaxChassisRoll;
            Vector3 forward = Body.rotation * Vector3.forward, side = Body.rotation * Vector3.right;
            // Ground seen under the wheels always wins; the belly contact only guides a car with no wheel over ground.
            bool bellyOnly = Beached && front + rear + left + right == 0;
            if (bellyOnly)
            {
                // Lie on the surface the hull rests on, so the wheels come down onto it.
                Vector3 n = Quaternion.Inverse(Body.rotation) * bellyNormal;
                chassisPitch = Mathf.Lerp(chassisPitch, Mathf.Clamp(Mathf.Atan2(n.z, n.y) * Mathf.Rad2Deg, -MaxChassisPitch, MaxChassisPitch), follow);
                chassisRoll = Mathf.Lerp(chassisRoll, Mathf.Clamp(Mathf.Atan2(-n.x, n.y) * Mathf.Rad2Deg, -MaxChassisRoll, MaxChassisRoll), follow);
            }
            else if (front > 0 && rear > 0)
            {
                Vector3 span = frontSum / front - rearSum / rear;
                float run = Vector3.Dot(span, forward);
                if (run > .4f) chassisPitch = Mathf.Lerp(chassisPitch, Mathf.Clamp(-Mathf.Atan2(span.y, run) * Mathf.Rad2Deg, -pitchLimit, pitchLimit), follow);
            }
            else if (front + rear > 0)
            {
                // Nose or tail over a drop: lie along the ground under the wheels that are down.
                Vector3 n = Quaternion.Inverse(Body.rotation) * supportNormal.normalized;
                chassisPitch = Mathf.Lerp(chassisPitch, Mathf.Clamp(Mathf.Atan2(n.z, n.y) * Mathf.Rad2Deg, -pitchLimit, pitchLimit), follow);
            }
            else chassisPitch = Mathf.MoveTowards(chassisPitch, 0, settle); // nothing below: level out in the air
            if (!bellyOnly && left > 0 && right > 0)
            {
                Vector3 span = rightSum / right - leftSum / left;
                float run = Vector3.Dot(span, side);
                if (run > .4f) chassisRoll = Mathf.Lerp(chassisRoll, Mathf.Clamp(Mathf.Atan2(span.y, run) * Mathf.Rad2Deg, -rollLimit, rollLimit), follow);
            }
            else if (!bellyOnly && left + right > 0)
            {
                // One side over a drop: lie along the ground under the wheels that are down instead of hanging tilted.
                Vector3 n = Quaternion.Inverse(Body.rotation) * supportNormal.normalized;
                chassisRoll = Mathf.Lerp(chassisRoll, Mathf.Clamp(Mathf.Atan2(-n.x, n.y) * Mathf.Rad2Deg, -rollLimit, rollLimit), follow);
            }
            else if (!bellyOnly) chassisRoll = Mathf.MoveTowards(chassisRoll, 0, settle);
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
            Vector3 localVelocity = transform.InverseTransformDirection(IsNetworkProxy ? networkVelocity : Body.linearVelocity);
            // Acceleration is sampled per physics step; per-render-frame sampling alternated between zero and double.
            Vector3 acceleration = transform.InverseTransformDirection(bodyAcceleration);
            float narrow = Mathf.Clamp(Stats.rideHeight / Mathf.Max(1, Stats.trackWidth), .15f, 1);
            // Cornering lean follows turn force (yaw rate x speed) with a little slip, capped low so fast turns never
            // look like the car tipping over; it settles back twice as fast as it leans in.
            float yawRate = IsNetworkProxy ? networkYawRate : Body.angularVelocity.y;
            float targetRoll = -yawRate * localVelocity.z * .1f * (1 + narrow * .5f) - localVelocity.x * .25f * (1 + narrow);
            targetRoll = Mathf.Clamp(targetRoll, -MaxCorneringLean, MaxCorneringLean) + (1 - Damage.Suspension) * 4;
            visualRoll = Mathf.Lerp(visualRoll, targetRoll, Time.deltaTime * (Mathf.Abs(targetRoll) < Mathf.Abs(visualRoll) ? 12 : 6));
            // The body sits on the slope, then squats under power and dives under braking. The old pitch dipped the
            // nose under acceleration, which read as the car tipping forward into every hill it tried to climb.
            visualTilt = Mathf.Lerp(visualTilt, chassisPitch, Time.deltaTime * 18);
            visualWeight = Mathf.Lerp(visualWeight, Mathf.Clamp(-acceleration.z * .23f, -4, 4), Time.deltaTime * 7);
            Visual.localRotation = Quaternion.Euler(visualTilt + visualWeight, 0, chassisRoll + visualRoll);
            wheelAngle += localVelocity.z * Time.deltaTime * 150;
            if (wheels != null)
                for (int i = 0; i < wheels.Length; i++)
                    if (wheels[i] != null) wheels[i].localRotation = Quaternion.Euler(wheelAngle, i < 2 ? yawRate * 12 : 0, 0);
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
        /// <summary>Internal chassis state for the opt-in telemetry recorder (-miaTelemetry).</summary>
        internal string TelemetryState() =>
            $"cp={chassisPitch:F1} cr={chassisRoll:F1} lean={visualRoll:F1} tilt={visualTilt:F1} squat={visualWeight:F1} wc={wheelContacts} sL={supportLeft} sR={supportRight} " +
            $"G={(Grounded ? 1 : 0)} B={(Beached ? 1 : 0)} drift={DriftBlend:F2} boost={(Boosting ? 1 : 0)} thr={Throttle:F2} dir={driveDirection} hull=({appliedHullPitch:F0},{appliedHullRoll:F0}) stuck={stuckTime:F2}";
        /// <summary>The body only ever yaws; pitch and roll are the chassis tilt's job. Any leak is removed immediately.</summary>
        void KeepYawOnly()
        {
            Quaternion rotation = Body.rotation;
            if (Mathf.Abs(Mathf.DeltaAngle(0, rotation.eulerAngles.x)) < .01f && Mathf.Abs(Mathf.DeltaAngle(0, rotation.eulerAngles.z)) < .01f) return;
            Body.rotation = Quaternion.Euler(0, rotation.eulerAngles.y, 0);
            Vector3 spin = Body.angularVelocity; Body.angularVelocity = new Vector3(0, spin.y, 0);
        }
        public static float LowSpeedTurnMultiplier(float speed)
        {
            return Mathf.Lerp(1.15f, 1f, Mathf.Clamp01(speed / 7f));
        }
        /// <summary>
        /// Ground (not another car) directly under the hull, within a few centimetres of its underside. A direct query
        /// rather than collision callbacks, which stop arriving once a resting rigidbody falls asleep.
        /// </summary>
        bool BellySupported(out Vector3 normal)
        {
            normal = Vector3.up;
            if (!Hull) return false;
            Vector3 half = Hull.size * .5f;
            Vector3 center = Hull.transform.TransformPoint(Hull.center);
            Vector3 box = new Vector3(half.x, half.y * .5f, half.z);
            int count = Physics.BoxCastNonAlloc(center, box, Vector3.down, bellyHits, Hull.transform.rotation, half.y * .5f + .2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            // Rest on the flattest surface touched (a lip's top, not the cliff face beside it). A belly held only by
            // steep faces lies level rather than tipping toward the wall.
            bool supported = false; float flattest = -1;
            for (int i = 0; i < count; i++)
            {
                var hit = bellyHits[i];
                if (hit.rigidbody == Body || (hit.rigidbody && hit.rigidbody.GetComponent<VehicleController>())) continue;
                Vector3 n = hit.distance <= 0 ? Vector3.up : hit.normal; // already touching: treat as resting
                if (n.y <= .35f) continue;
                supported = true;
                if (n.y > flattest) { flattest = n.y; normal = n; }
            }
            if (supported && flattest < MinSupportNormal) normal = Vector3.up;
            return supported;
        }
        /// <summary>Drive probe counters: physics steps with the hull touching ground, and the last thing hit.</summary>
        internal static int HullGroundSteps; internal static string LastHitName = "";
        void OnCollisionStay(Collision collision)
        {
            if (!IsPlayer || !ProbeCounting) return;
            if (collision.collider is MeshCollider mesh && !mesh.convex || collision.collider.GetComponent<Terrain>()) HullGroundSteps++;
        }
        internal static bool ProbeCounting;
        void OnCollisionEnter(Collision collision)
        {
            if (IsPlayer && ProbeCounting) LastHitName = collision.collider.name + " @" + collision.relativeVelocity.magnitude.ToString("F0");
            if (!initialized || Damage.IsDead || GameManager.Instance == null || !GameManager.Instance.IsPlaying) return;
            float force = collision.relativeVelocity.magnitude;
            if(force<2)return;
            Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
            var prop = collision.collider.GetComponentInParent<DestructionSystem>();
            if (prop != null)
            {
                float propDamage = IsPlayer ? DevTuning.Current.propDamage : 1f;
                bool plowable = prop.Plowable || prop.Size < BodyLength * BreakAwayScale;
                // A prop the car plows through costs no speed: the velocity from before the contact is restored in full.
                if (prop.TryDestroyFromVehicle(force * Mathf.Sqrt(Body.mass) * .35f * propDamage, point, gameObject, out _) && plowable)
                {
                    Body.linearVelocity = preCollisionVelocity;
                    return;
                }
            }
            var other = collision.collider.GetComponentInParent<VehicleDamage>();
            bool ground = IsGroundContact(collision, other, prop);
            // Crash strength is the closing speed into the surface; sliding along a wall or rock is a scrape, not a crash.
            float impact = ground ? 0 : ImpactSpeed(collision);
            if (TowedBy && !ground && other != Damage) TowLink.WhipImpact(this, other, impact, point);
            if(force<6 || Time.time<collisionCooldown)return;
            collisionCooldown=Time.time+.25f;
            // VehicleDamage refuses non-explosive damage between vehicles on the same side, so crews never ram-kill each other.
            if (other != null && other != Damage) other.ApplyDamage(force * 2.4f * Mathf.Clamp(Body.mass / 1000, .5f, 3), point, gameObject);
            // Landing on, scraping or bottoming out against the ground never hurts and throws no sparks.
            if (ground) return;
            Damage.ApplyDamage(Mathf.Max(0, impact - 9) * .55f, point, collision.gameObject);
            if (impact < 4 && other == null) return;
            ExplosionSystem.Burst(point, new Color(1, .65f, .17f), 9, 4);
            if (IsPlayer) CameraController.Instance?.Shake(Mathf.Clamp01(impact / 28) * .25f);
        }
        /// <summary>
        /// Ground is judged by the contact normals alone: any mostly horizontal contact (the terrain, roads, a rock's
        /// flat top). Rock and mesa faces are mesh colliders just like the terrain, and the old rule that every mesh
        /// collider counted as ground meant slamming into a cliff or boulder never registered as a crash.
        /// </summary>
        static bool IsGroundContact(Collision collision, VehicleDamage vehicle, DestructionSystem prop)
        {
            if (vehicle != null || prop != null) return false;
            for (int i = 0; i < collision.contactCount; i++)
                if (Mathf.Abs(collision.GetContact(i).normal.y) < .55f) return false;
            return collision.contactCount > 0;
        }
        /// <summary>Closing speed along the contact normal, the part of the relative velocity that is a head-on hit.</summary>
        static float ImpactSpeed(Collision collision)
        {
            float best = 0;
            for (int i = 0; i < collision.contactCount; i++)
                best = Mathf.Max(best, Mathf.Abs(Vector3.Dot(collision.relativeVelocity, collision.GetContact(i).normal)));
            return collision.contactCount > 0 ? best : collision.relativeVelocity.magnitude;
        }

        // ---- Scenery sweep (every vehicle; hostiles get the same small-obstacle rules as the player) ----
        // Props smaller than 60% of the car are driven straight through (and knocked apart); props up to 120% of the
        // car's length break away without costing speed while the car is moving briskly. Anything larger stays solid
        // and uses ordinary collision.
        public const float BreakAwaySpeed = 6f; // m/s, about 22 km/h
        public const float SmallPropScale = .6f, BreakAwayScale = 1.2f;
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
                // Rocks, cacti and dead snags are plowable at any size: they shatter in front of the car.
                bool small = prop.Plowable || size < length * SmallPropScale;
                if (!small && size >= length * BreakAwayScale) continue;
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
                bool small = prop.Plowable || prop.Size < length * SmallPropScale;
                if (!small && speed < BreakAwaySpeed && !car.Intersects(prop.WorldBounds)) { prop.SetVehicleCollision(body, true); ghostScratch.Add(prop); }
            }
            foreach (var prop in ghostScratch) ghosted.Remove(prop);
        }
    }
}
