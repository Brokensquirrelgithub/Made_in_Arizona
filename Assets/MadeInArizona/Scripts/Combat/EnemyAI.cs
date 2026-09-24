using UnityEngine;

namespace MadeInArizona
{
    /// <summary>The committed attack currently being announced by an enemy vehicle.</summary>
    public enum EnemyAttackTelegraph { None, Rocket, Sniper, Ram, Mine, Mortar }

    /// <summary>Vehicle combat steering, target selection, and readable special attacks.</summary>
    public sealed class EnemyAI : MonoBehaviour
    {
        public VehicleController Target;
        public Vector3 Destination;
        public bool UseDestination;
        public bool IsFriendly;
        public int Archetype { get; private set; }
        public EnemyFaction Faction { get; private set; }
        public float WeaponEfficiency { get; private set; } = 1;

        // UI can present a directional warning without inferring the enemy's weapon role.
        public EnemyAttackTelegraph AttackTelegraph { get; private set; }
        public Vector3 TelegraphDirection { get; private set; }
        public float TelegraphRemaining => Mathf.Max(0, telegraphFireAt - Time.time);
        public bool IsTelegraphingAttack => AttackTelegraph != EnemyAttackTelegraph.None && TelegraphRemaining > 0;

        VehicleController vehicle;
        float phase, specialAt, primaryAt, primaryBurstUntil, stuckTime, hazardAt, telegraphFireAt, ramUntil;
        Vector3 avoidance, committedAim;
        float committedDistance;
        float orbitSign;
        readonly RaycastHit[] hits = new RaycastHit[16];
        readonly Collider[] hazards = new Collider[24];

        public void Initialize(VehicleController target, int archetype, EnemyFaction faction = EnemyFaction.Sunsprawl)
        {
            Target = target; Archetype = archetype; Faction = faction; vehicle = GetComponent<VehicleController>();
            phase = Random.Range(0, Mathf.PI * 2); orbitSign = Random.value > .5f ? 1 : -1;
            specialAt = Time.time + Random.Range(1.5f, 3.5f); primaryAt = Time.time + Random.Range(.45f, 1.25f);
        }

        public void DisableWeapon() { WeaponEfficiency = Mathf.Max(.2f, WeaponEfficiency - .4f); }

        void Update()
        {
            if (vehicle == null || vehicle.Damage == null || vehicle.Damage.IsDead || GameManager.Instance == null || !GameManager.Instance.IsPlaying) return;
            if (Target == null || Target.Damage == null || Target.Damage.IsDead) Target = GameManager.Instance.Player;
            if (Target == null || Target.Damage == null || Target.Damage.IsDead) return;

            // A destination controls where a vehicle drives; it never replaces its combat target.
            Vector3 travelDelta = FlatDelta(UseDestination ? Destination : Target.transform.position, transform.position);
            float travelDistance = travelDelta.magnitude;
            Vector3 towardTravel = travelDistance > .01f ? travelDelta / travelDistance : Vector3.zero;
            Vector3 targetDelta = FlatDelta(Target.transform.position, transform.position);
            float targetDistance = targetDelta.magnitude;
            Vector3 towardTarget = targetDistance > .01f ? targetDelta / targetDistance : Vector3.zero;
            Vector3 tangent = Vector3.Cross(Vector3.up, towardTarget) * orbitSign;
            Vector3 desired = IsFriendly ? FriendlySteering(towardTravel, travelDistance) : CombatSteering(towardTarget, tangent, targetDistance);
            float primaryRange = PrimaryRange();
            bool clearShot = HasLineOfFire(Target, targetDelta, targetDistance);
            Vector3 aim = PredictedAim(targetDistance, AttackTelegraph == EnemyAttackTelegraph.None ? SpecialLeadTime() : 0);

            if (!IsFriendly)
            {
                UpdateTelegraph(targetDistance, clearShot, aim);
                if (AttackTelegraph != EnemyAttackTelegraph.None)
                {
                    aim = committedAim;
                    if (AttackTelegraph == EnemyAttackTelegraph.Ram || Time.time < ramUntil) desired = committedAim;
                }
                else if (Time.time < ramUntil) desired = committedAim;

                // Small, separated bursts maintain pressure without frame-perfect continuous fire.
                if (Time.time >= primaryAt && targetDistance > 7 && targetDistance < primaryRange && clearShot)
                {
                    primaryBurstUntil = Time.time + PrimaryBurstDuration();
                    primaryAt = primaryBurstUntil + PrimaryRecovery();
                }
            }

            // Mission routes still win steering, while target acquisition and weapons remain independent.
            if (UseDestination && !IsFriendly && AttackTelegraph != EnemyAttackTelegraph.Ram && Time.time >= ramUntil)
                desired = travelDistance > 4 ? towardTravel : CombatSteering(towardTarget, tangent, targetDistance) * .35f;

            UpdateHazardAvoidance(); desired = AvoidBlockedRoute(desired);
            RecoverIfStuck(ref desired, towardTarget, tangent);
            bool firePrimary = !IsFriendly && Time.time < primaryBurstUntil && targetDistance > 7 && targetDistance < primaryRange && clearShot && AttackTelegraph == EnemyAttackTelegraph.None;
            vehicle.SetAIInput(new Vector2(desired.x, desired.z), aim, firePrimary);
        }

        Vector3 FriendlySteering(Vector3 towardDestination, float destinationDistance)
        {
            return UseDestination && destinationDistance > 4 ? towardDestination * .6f : Vector3.zero;
        }

        Vector3 CombatSteering(Vector3 toward, Vector3 tangent, float distance)
        {
            Vector3 desired;
            switch (Archetype)
            {
                case 0: desired = toward + tangent * Mathf.Lerp(.75f, .12f, Mathf.Clamp01(distance / 26)); break;
                case 1: desired = toward * Mathf.Clamp((distance - 18) / 12, -1, 1) + tangent * .85f; break;
                case 2: desired = toward * Mathf.Clamp((distance - 13) / 10, -.45f, 1) + tangent * .55f; break;
                case 3: desired = distance < 29 ? -toward + tangent * .45f : distance > 47 ? toward : tangent * .7f; break;
                case 4: desired = toward * Mathf.Clamp((distance - 32) / 15, -1, 1) + tangent * .75f; break;
                case 5:
                    desired = toward;
                    if (distance < 5.3f)
                    {
                        vehicle.Damage.ApplyDamage(vehicle.Damage.MaxHealth * 2, transform.position, gameObject);
                        ExplosionSystem.Detonate(transform.position, 9, 95, gameObject, ExplosionKind.Gasoline);
                    }
                    break;
                case 6: desired = toward * Mathf.Clamp((distance - 16) / 12, -.35f, 1) + tangent * .5f; break;
                case 7:
                    bool artillery = Mathf.Sin(Time.time * .22f + phase) > 0;
                    desired = artillery ? toward * Mathf.Clamp((distance - 29) / 13, -.5f, .7f) + tangent * .5f : toward * Mathf.Clamp((distance - 16) / 10, -.3f, 1) + tangent * .35f;
                    break;
                default: desired = toward; break;
            }
            if (Faction == EnemyFaction.RoadScavengers && Archetype != 5)
                desired = Vector3.Lerp(desired, toward + tangent * .2f, .62f);
            else if (Faction == EnemyFaction.CourtesyCompliance && Archetype != 5 && Archetype != 2 && Archetype != 6)
                desired = Vector3.Lerp(desired, toward * Mathf.Clamp((distance - 25) / 12, -.8f, .8f) + tangent * .48f, .55f);
            else if (Faction == EnemyFaction.OpenHouseRealty && Archetype != 5)
                desired = Vector3.Lerp(desired, toward * Mathf.Clamp((distance - 22) / 11, -.5f, 1) + tangent * .95f, .7f);
            else if (Faction == EnemyFaction.SnowbirdConvoy && Archetype != 5)
                desired = Vector3.Lerp(desired, toward * Mathf.Clamp((distance - 18) / 9, -.5f, .55f) + tangent * .2f, .65f);
            else if (Faction == EnemyFaction.CarOtaku && Archetype != 5)
                desired = Vector3.Lerp(desired, toward * Mathf.Clamp((distance - 14) / 10, -.65f, 1) + tangent * 1.3f, .72f);
            if (vehicle.Damage.Health < vehicle.Damage.MaxHealth * .24f && Archetype != 5 && Archetype != 7 && Archetype != 2 && Faction != EnemyFaction.RoadScavengers && Faction != EnemyFaction.CarOtaku) desired = -toward + tangent * .8f;
            return desired.sqrMagnitude > .01f ? desired.normalized : Vector3.zero;
        }

        void UpdateTelegraph(float distance, bool clearShot, Vector3 aim)
        {
            if (AttackTelegraph != EnemyAttackTelegraph.None)
            {
                if (Time.time < telegraphFireAt) return;
                FireCommittedAttack(clearShot); return;
            }
            if (Time.time < specialAt || WeaponEfficiency <= .2f || !clearShot) return;
            EnemyAttackTelegraph attack = SelectSpecialAttack(distance);
            if (attack != EnemyAttackTelegraph.None) BeginTelegraph(attack, aim, distance);
        }

        EnemyAttackTelegraph SelectSpecialAttack(float distance)
        {
            if (Faction == EnemyFaction.CourtesyCompliance)
            {
                if (Archetype == 5) return EnemyAttackTelegraph.None;
                if (Archetype == 2 || Archetype == 6 || distance < 17)
                    return distance >= 7 && distance <= 28 ? EnemyAttackTelegraph.Mine : EnemyAttackTelegraph.None;
                return distance >= 17 && distance <= 70 ? EnemyAttackTelegraph.Mortar : EnemyAttackTelegraph.None;
            }
            if (Faction == EnemyFaction.RoadScavengers)
                return (Archetype == 2 || Archetype == 3 || Archetype == 6 || Archetype == 7) && distance >= 9 && distance <= 32
                    ? EnemyAttackTelegraph.Ram : EnemyAttackTelegraph.None;
            if (Faction == EnemyFaction.OpenHouseRealty)
            {
                if (Archetype == 0 || Archetype == 2 || Archetype == 6)
                    return distance >= 8 && distance <= 26 ? EnemyAttackTelegraph.Mine : EnemyAttackTelegraph.None;
                return (Archetype == 3 || Archetype == 4 || Archetype == 7) && distance >= 18 && distance <= 55
                    ? EnemyAttackTelegraph.Rocket : EnemyAttackTelegraph.None;
            }
            if (Faction == EnemyFaction.SnowbirdConvoy)
                return Archetype != 5 && distance >= 8 && distance <= 25 ? EnemyAttackTelegraph.Mine : EnemyAttackTelegraph.None;
            if (Faction == EnemyFaction.CarOtaku)
            {
                if (Archetype == 3 || Archetype == 4) return distance >= 16 && distance <= 60 ? EnemyAttackTelegraph.Sniper : EnemyAttackTelegraph.None;
                return (Archetype == 0 || Archetype == 2 || Archetype == 6 || Archetype == 7) && distance >= 10 && distance <= 31
                    ? EnemyAttackTelegraph.Ram : EnemyAttackTelegraph.None;
            }
            switch (Archetype)
            {
                case 3: return distance >= 14 && distance <= 62 ? EnemyAttackTelegraph.Sniper : EnemyAttackTelegraph.None;
                case 4: return distance >= 15 && distance <= 52 ? EnemyAttackTelegraph.Rocket : EnemyAttackTelegraph.None;
                case 2: case 6: return distance >= 10 && distance <= 29 ? EnemyAttackTelegraph.Ram : EnemyAttackTelegraph.None;
                case 7:
                    return Mathf.Sin(Time.time * .22f + phase) > 0
                        ? (distance >= 17 && distance <= 58 ? EnemyAttackTelegraph.Rocket : EnemyAttackTelegraph.None)
                        : (distance >= 10 && distance <= 28 ? EnemyAttackTelegraph.Ram : EnemyAttackTelegraph.None);
                default: return EnemyAttackTelegraph.None;
            }
        }

        void BeginTelegraph(EnemyAttackTelegraph attack, Vector3 aim, float distance)
        {
            AttackTelegraph = attack;
            committedAim = aim.sqrMagnitude > .01f ? aim.normalized : transform.forward;
            committedDistance = distance;
            TelegraphDirection = committedAim;
            float warning = attack == EnemyAttackTelegraph.Rocket ? 1.2f : attack == EnemyAttackTelegraph.Mortar ? 1.1f : attack == EnemyAttackTelegraph.Mine ? .65f : attack == EnemyAttackTelegraph.Sniper ? .9f : .8f;
            telegraphFireAt = Time.time + warning;
            // Aim is locked at the warning start, so player movement produces a real dodge window.
            specialAt = telegraphFireAt + SpecialRecovery(attack);
        }

        void FireCommittedAttack(bool clearShot)
        {
            EnemyAttackTelegraph attack = AttackTelegraph;
            AttackTelegraph = EnemyAttackTelegraph.None; telegraphFireAt = 0;
            if (attack == EnemyAttackTelegraph.Ram) { ramUntil = Time.time + 1.15f; return; }
            if (attack == EnemyAttackTelegraph.Mine)
            {
                var mines = WeaponRules.Find("mines");
                if (mines) FieldOrdnance.PlaceMine(vehicle, mines, 42, transform.position + committedAim * Mathf.Min(8, committedDistance * .45f) + Vector3.up * .4f);
                return;
            }
            // Cover cancels a launch, and a special never retargets during its warning.
            if (!clearShot || committedAim.sqrMagnitude < .01f) return;
            if (attack == EnemyAttackTelegraph.Rocket) vehicle.Weapons.FireSecondary(committedAim);
            else if (attack == EnemyAttackTelegraph.Mortar)
            {
                var mortar = WeaponRules.Find("mortar");
                if (mortar) FieldOrdnance.LaunchShell(vehicle, transform.position + Vector3.up * 1.1f + committedAim * 2.2f,
                    committedAim, mortar, 55, Mathf.Clamp(committedDistance / 1.94f, 12, 42));
            }
            else if (attack == EnemyAttackTelegraph.Sniper)
                ProjectileSystem.Fire(transform.position + Vector3.up * .85f + committedAim * 2.5f, committedAim, 150, 28, 0, gameObject, new Color(1, .16f, .34f), ExplosionKind.Ammunition, .7f);
        }

        float PrimaryRange()
        {
            string weapon = vehicle.Weapons.GarageWeapon ? vehicle.Weapons.GarageWeapon.id : "riveter";
            if (weapon == "boomstick" || weapon == "sweeper") return 23;
            if (weapon == "minigun") return 34;
            if (weapon == "carbine") return 57;
            switch (Archetype)
            {
                case 0: return 25; case 1: return 38; case 2: return 30; case 3: return 58;
                case 4: return 45; case 6: return 28; case 7: return 50; default: return 0;
            }
        }
        float PrimaryBurstDuration() { return (Faction == EnemyFaction.RoadScavengers ? .65f : Faction == EnemyFaction.CarOtaku ? .5f : Faction == EnemyFaction.CourtesyCompliance ? .25f : Faction == EnemyFaction.SnowbirdConvoy ? .28f : Archetype == 3 ? .24f : .38f) * WeaponEfficiency; }
        float PrimaryRecovery() { return (Faction == EnemyFaction.CarOtaku ? 1.2f : Faction == EnemyFaction.RoadScavengers ? 1.7f : Faction == EnemyFaction.SnowbirdConvoy ? 2.05f : Faction == EnemyFaction.CourtesyCompliance || Faction == EnemyFaction.OpenHouseRealty ? 1.55f : Archetype == 7 ? 1.15f : Archetype == 3 ? 1.8f : 1.45f) / WeaponEfficiency + Random.Range(.12f, .45f); }
        float SpecialLeadTime() { return Archetype == 3 ? .2f : Archetype == 4 || Archetype == 7 ? .38f : .1f; }
        float SpecialRecovery(EnemyAttackTelegraph attack)
        {
            float recovery = attack == EnemyAttackTelegraph.Mortar ? 5.2f : attack == EnemyAttackTelegraph.Mine ? 4.8f : attack == EnemyAttackTelegraph.Rocket ? 3.6f : attack == EnemyAttackTelegraph.Sniper ? 4.2f : 4.4f;
            return recovery / WeaponEfficiency + Random.Range(.25f, .7f);
        }
        Vector3 PredictedAim(float distance, float leadTime)
        {
            Vector3 targetVelocity = Target.Body != null ? Target.Body.linearVelocity : Vector3.zero;
            Vector3 aim = Target.transform.position + targetVelocity * (leadTime + distance / (Archetype == 3 ? 150f : 80f)) - transform.position;
            if(!GeneratedWorld.Active)aim.y = 0;
            if (aim.sqrMagnitude < .001f) aim = transform.forward;
            float inaccuracy = Faction == EnemyFaction.RoadScavengers ? 4.2f : Faction == EnemyFaction.SnowbirdConvoy ? 4.8f : Faction == EnemyFaction.OpenHouseRealty ? 2.7f : Faction == EnemyFaction.CourtesyCompliance ? 2.2f : Faction == EnemyFaction.CarOtaku ? 1.15f : Archetype == 3 ? .45f : 1.6f;
            return (Quaternion.AngleAxis(Mathf.Sin(Time.time * 3 + phase) * inaccuracy, Vector3.up) * aim.normalized).normalized;
        }

        void UpdateHazardAvoidance()
        {
            if (Time.time <= hazardAt) return;
            hazardAt = Time.time + .35f; avoidance = Vector3.zero;
            int count = Physics.OverlapSphereNonAlloc(transform.position, 7, hazards, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var prop = hazards[i].GetComponentInParent<DestructionSystem>();
                if (prop == null || !prop.Explosive || prop.IsDestroyed) continue;
                Vector3 away = FlatDelta(transform.position, prop.transform.position);
                if (away.sqrMagnitude > .1f) avoidance += away.normalized * Mathf.Clamp01(1 - away.magnitude / 7) * .8f;
            }
        }

        Vector3 AvoidBlockedRoute(Vector3 desired)
        {
            if (desired.sqrMagnitude <= .01f) return desired;
            Vector3 travel = desired.normalized;
            if (Blocked(travel, out Vector3 normal))
            {
                Vector3 side = Vector3.Cross(Vector3.up, normal);
                if (Vector3.Dot(side, travel) < 0) side = -side;
                desired = (travel + normal * 1.1f + side * 1.6f).normalized;
            }
            return Vector3.ClampMagnitude(desired + avoidance, 1);
        }

        void RecoverIfStuck(ref Vector3 desired, Vector3 towardTarget, Vector3 tangent)
        {
            if (desired.sqrMagnitude > .2f && vehicle.SpeedKph < 2)
            {
                stuckTime += Time.deltaTime;
                if (stuckTime > 1.8f) desired = (tangent - towardTarget * .6f).normalized;
                if (stuckTime > 3.8f)
                {
                    vehicle.Body.AddForce((tangent + Vector3.up * .65f) * 7, ForceMode.VelocityChange);
                    stuckTime = 0; orbitSign *= -1;
                }
            }
            else stuckTime = 0;
        }

        bool Blocked(Vector3 direction, out Vector3 normal)
        {
            normal = Vector3.zero;
            int count = Physics.SphereCastNonAlloc(transform.position + Vector3.up * .8f, .8f, direction, hits, Mathf.Clamp(vehicle.SpeedKph / 10, 4, 10), Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (hits[i].collider == null || hits[i].collider.transform.IsChildOf(transform)) continue;
                var other = hits[i].collider.GetComponentInParent<VehicleController>();
                if (other == Target) continue;
                var prop = hits[i].collider.GetComponentInParent<DestructionSystem>();
                if (prop != null && prop.MaxHealth < 80 && Archetype != 3 && Archetype != 4) continue;
                if (hits[i].normal.y > .5f) continue;
                if (hits[i].distance < nearest) { nearest = hits[i].distance; normal = hits[i].normal; }
            }
            normal.y = 0; return nearest < float.MaxValue;
        }

        bool HasLineOfFire(VehicleController target, Vector3 direction, float distance)
        {
            if (target == null || distance < .01f) return false;
            int count = Physics.RaycastNonAlloc(transform.position + Vector3.up * .85f, direction.normalized, hits, distance + .8f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue; RaycastHit first = default;
            for (int i = 0; i < count; i++)
            {
                if (hits[i].collider == null || hits[i].collider.transform.IsChildOf(transform)) continue;
                if (hits[i].distance < nearest) { nearest = hits[i].distance; first = hits[i]; }
            }
            return nearest < float.MaxValue && first.collider.GetComponentInParent<VehicleController>() == target;
        }

        static Vector3 FlatDelta(Vector3 to, Vector3 from)
        {
            Vector3 delta = to - from; delta.y = 0; return delta;
        }
    }
}
