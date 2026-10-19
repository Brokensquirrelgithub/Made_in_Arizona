using System.Collections.Generic;
using UnityEngine;

namespace MadeInArizona
{
    /// <summary>A launched, steerable melee weapon. The host sweeps its head through physics; guests draw replicated motion.</summary>
    public sealed class WashingMachineFlail : MonoBehaviour
    {
        const float Reach = 7.92f;
        const float StowedReach = 1.05f;
        const float HeadRadius = .95f;
        const float MinSwingSpeed = 4.8f;
        const float RetractSpeed = 25f;
        const int LinkCount = 14;
        readonly Collider[] contacts = new Collider[48];
        readonly RaycastHit[] sweepHits = new RaycastHit[48];
        readonly RaycastHit[] groundHits = new RaycastHit[24];
        readonly Dictionary<Component, float> hitAgainAt = new Dictionary<Component, float>();
        readonly Transform[] links = new Transform[LinkCount];
        VehicleController owner;
        Transform visual, washer;
        float angle, targetAngle, angularSpeed, triggerAt = -10, launchedAt = -10, networkAt = -10, baseDamage = 60;
        float nextChainAt = -10, nextBangAt = -10;
        Vector3 previousHead, previousPivot, networkPreviousPivot, networkHead, headVelocity, groundNormal = Vector3.up;
        Quaternion rollingRotation = Quaternion.identity;
        bool equipped, deployed, grounded, networkActive;
        static Material casing, steel, dark;

        public bool Extended => equipped && owner && Vector3.Distance(HeadPosition, Pivot) > StowedReach + .25f;
        public bool HeadGrounded => grounded;
        public Vector3 HeadPosition { get; private set; }

        public void Bind(VehicleController vehicle)
        {
            owner = vehicle;
            Vector3 forward = vehicle.transform.forward;
            angle = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            targetAngle = angle;
            HeadPosition = Pivot + forward * StowedReach;
            previousHead = HeadPosition;
            previousPivot = Pivot;
            networkPreviousPivot = Pivot;
            headVelocity = Vector3.zero;
            deployed = grounded = false;
            rollingRotation = Quaternion.identity;
            if (!visual) BuildVisual();
        }

        public void SetEquipped(bool value)
        {
            equipped = value;
            if (visual) visual.gameObject.SetActive(value);
            if (!value)
            {
                triggerAt = launchedAt = -10;
                nextChainAt = nextBangAt = -10;
                deployed = grounded = false;
                headVelocity = Vector3.zero;
                rollingRotation = Quaternion.identity;
                if (owner)
                {
                    HeadPosition = previousHead = Pivot + owner.transform.forward * StowedReach;
                    previousPivot = networkPreviousPivot = Pivot;
                }
            }
        }

        public void Swing(Vector3 direction, float damage)
        {
            if (!equipped || !owner || owner.IsNetworkProxy) return;
            triggerAt = Time.time;
            baseDamage = damage;
            direction.y = 0;
            if (direction.sqrMagnitude > .001f)
                targetAngle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            if (!deployed) Launch(direction.sqrMagnitude > .001f ? direction.normalized : owner.transform.forward);
        }

        public void Release() => triggerAt = -10;

        void Launch(Vector3 direction)
        {
            deployed = true;
            grounded = false;
            launchedAt = Time.time;
            hitAgainAt.Clear();
            HeadPosition = Pivot + direction * StowedReach;
            previousHead = HeadPosition;
            previousPivot = Pivot;
            // Velocity is relative to the car; its translation is carried separately each physics step.
            headVelocity = direction * 34f + Vector3.up * 3.5f;
            ExplosionSystem.Burst(HeadPosition, new Color(1f, .65f, .22f, .85f), 12, 2.2f);
            Bang(HeadPosition, .75f);
            if (owner.IsLocalPlayer) CameraController.Instance?.Shake(.13f);
        }

        Vector3 Pivot => owner ? owner.transform.position + Vector3.up * 1.65f : transform.position;
        Vector3 Direction => new Vector3(Mathf.Sin(angle * Mathf.Deg2Rad), 0, Mathf.Cos(angle * Mathf.Deg2Rad));

        void FixedUpdate()
        {
            if (!equipped || !owner || owner.IsNetworkProxy) return;
            bool active = owner.Damage && !owner.Damage.IsDead && GameManager.Instance && GameManager.Instance.IsPlaying &&
                Time.time - triggerAt < .09f;
            float dt = Time.fixedDeltaTime;
            Vector3 vehicleTravel = Pivot - previousPivot;
            Vector3 worldStart = HeadPosition;
            HeadPosition += vehicleTravel;
            // The head has angular momentum: moving the pointer steers the swing instead of teleporting it.
            float error = Mathf.DeltaAngle(angle, active ? targetAngle : Mathf.Atan2(owner.transform.forward.x, owner.transform.forward.z) * Mathf.Rad2Deg);
            float acceleration = Mathf.Clamp(error * 32f - angularSpeed * 4.2f, -1250f, 1250f);
            angularSpeed = Mathf.Clamp(angularSpeed + acceleration * dt, -900f, 900f);
            angle += angularSpeed * dt;
            if (active && deployed) MoveHead(dt, worldStart);
            else
            {
                HeadPosition = Vector3.MoveTowards(HeadPosition, Pivot + owner.transform.forward * StowedReach, RetractSpeed * dt);
                headVelocity = Vector3.zero;
                grounded = false;
                if (Vector3.Distance(HeadPosition, Pivot + owner.transform.forward * StowedReach) < .02f) deployed = false;
            }
            if (active && deployed && Vector3.Distance(HeadPosition, Pivot) > 2.5f)
                HitSweep(previousHead, HeadPosition, vehicleTravel, dt, Time.time - launchedAt < .35f);
            if (deployed) ChainSound(HeadPosition - previousHead, vehicleTravel, dt);
            previousHead = HeadPosition;
            previousPivot = Pivot;
        }

        void MoveHead(float dt, Vector3 worldStart)
        {
            Vector3 desired = Pivot + Direction * Reach;
            Vector3 planarVelocity = Vector3.ProjectOnPlane(headVelocity, Vector3.up);
            Vector3 pull = Vector3.ProjectOnPlane(desired - HeadPosition, Vector3.up) * 38f - planarVelocity * 4.5f;
            headVelocity += Vector3.ClampMagnitude(pull, 120f) * dt;
            headVelocity.y -= 24f * dt;
            headVelocity = Vector3.ClampMagnitude(headVelocity, 48f);
            HeadPosition += headVelocity * dt;

            // A loose chain permits a launch arc and a trailing head, but never exceeds its full length.
            Vector3 span = HeadPosition - Pivot;
            if (span.magnitude > Reach)
            {
                Vector3 outward = span.normalized;
                HeadPosition = Pivot + outward * Reach;
                headVelocity -= outward * Mathf.Max(0, Vector3.Dot(headVelocity, outward));
            }

            bool wasGrounded = grounded;
            grounded = GroundBelow(HeadPosition, out float floor, out groundNormal);
            if (grounded)
            {
                float restingHeight = floor + HeadRadius * .82f;
                if (HeadPosition.y <= restingHeight)
                {
                    if (!wasGrounded && headVelocity.y < -4f)
                        Bang(HeadPosition, Mathf.Clamp01(-headVelocity.y / 15f));
                    HeadPosition = new Vector3(HeadPosition.x, restingHeight, HeadPosition.z);
                    headVelocity.y = Mathf.Max(0, -headVelocity.y * .12f);
                    headVelocity.x *= .97f;
                    headVelocity.z *= .97f;
                    if (!wasGrounded && washer) rollingRotation = washer.rotation;
                    RollFrom(worldStart, HeadPosition, groundNormal);
                }
                else grounded = false;
            }
            // A rising road or bridge must not let the ground lift the washer beyond the chain's reach.
            span = HeadPosition - Pivot;
            if (span.magnitude > Reach)
            {
                HeadPosition = Pivot + span.normalized * Reach;
                if (grounded && HeadPosition.y < floor + HeadRadius * .8f) grounded = false;
            }
        }

        void RollFrom(Vector3 start, Vector3 end, Vector3 normal)
        {
            Vector3 move = Vector3.ProjectOnPlane(end - start, normal);
            if (move.sqrMagnitude < .0001f) return;
            Vector3 axis = Vector3.Cross(normal, move.normalized);
            rollingRotation = Quaternion.AngleAxis(move.magnitude / HeadRadius * Mathf.Rad2Deg * .6f, axis) * rollingRotation;
        }

        bool GroundBelow(Vector3 position, out float floor, out Vector3 normal)
        {
            floor = float.NegativeInfinity;
            normal = Vector3.up;
            int count = Physics.RaycastNonAlloc(position + Vector3.up * 6f, Vector3.down, groundHits, 25f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var hit = groundHits[i];
                if (!hit.collider || hit.collider.transform.IsChildOf(owner.transform) || hit.normal.y < .5f ||
                    hit.point.y > position.y + HeadRadius || hit.collider.GetComponentInParent<VehicleController>() ||
                    hit.collider.GetComponentInParent<DestructionSystem>()) continue;
                if (hit.point.y > floor) { floor = hit.point.y; normal = hit.normal; }
            }
            return floor > float.NegativeInfinity;
        }

        internal static float RelativeSwingSpeed(Vector3 headTravel, Vector3 vehicleTravel, float dt) =>
            (headTravel - vehicleTravel).magnitude / Mathf.Max(.001f, dt);

        void ChainSound(Vector3 headTravel, Vector3 vehicleTravel, float dt)
        {
            float speed = RelativeSwingSpeed(headTravel, vehicleTravel, dt);
            if (speed < 1.5f || Time.time < nextChainAt) return;
            nextChainAt = Time.time + .25f;
            AudioManager.Instance?.PlayFlailChain(Vector3.Lerp(Pivot, HeadPosition, .55f), Mathf.Clamp01(speed / 24f));
        }

        void Bang(Vector3 position, float strength)
        {
            if (Time.time < nextBangAt) return;
            nextBangAt = Time.time + .1f;
            AudioManager.Instance?.PlayWasherBang(position, strength);
        }

        void HitSweep(Vector3 start, Vector3 end, Vector3 vehicleTravel, float dt, bool ejection)
        {
            Vector3 travel = end - start;
            float distance = travel.magnitude;
            Vector3 swingTravel = travel - vehicleTravel;
            float speed = RelativeSwingSpeed(travel, vehicleTravel, dt);
            if (!ejection && speed < MinSwingSpeed) return;
            int count = Physics.OverlapSphereNonAlloc(end, HeadRadius, contacts, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++) Hit(contacts[i], end, swingTravel, speed, ejection);
            if (distance < .01f) return;
            count = Physics.SphereCastNonAlloc(start, HeadRadius, travel / distance, sweepHits, distance, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++) Hit(sweepHits[i].collider, sweepHits[i].point, swingTravel, speed, ejection);
        }

        void Hit(Collider collider, Vector3 point, Vector3 travel, float speed, bool ejection)
        {
            if (!collider || collider.transform.IsChildOf(owner.transform)) return;
            var car = collider.GetComponentInParent<VehicleController>();
            var prop = car ? null : collider.GetComponentInParent<DestructionSystem>();
            if (!car && !prop) return;
            if (car && !ProjectileSystem.Hostile(owner.gameObject, car)) return;
            Component key = car ? (Component)car : prop;
            if (hitAgainAt.TryGetValue(key, out float next) && Time.time < next) return;
            hitAgainAt[key] = Time.time + .48f;
            float damage = ejection ? baseDamage * .5f : Mathf.Clamp(baseDamage * speed / 22f, baseDamage * .2f, baseDamage * 1.6f);
            Vector3 push = travel.sqrMagnitude > .0001f ? travel.normalized : Direction;
            if (car)
            {
                car.Damage.ApplyDamage(damage, point, owner.gameObject);
                if (car.Body && !car.Body.isKinematic)
                    car.Body.AddForceAtPosition((push + Vector3.up * .08f).normalized *
                        (ejection ? 9f : Mathf.Clamp(speed * .28f, 3f, 10f)), point, ForceMode.VelocityChange);
            }
            else prop.ApplyProjectileHit(damage, point, owner.gameObject, push);
            Bang(point, Mathf.Clamp01(speed / 32f));
            if (owner.IsLocalPlayer) CameraController.Instance?.Shake(.08f);
            angularSpeed *= .74f;
        }

        public void SetNetwork(bool active, Vector3 head)
        {
            if (!owner || !owner.IsNetworkProxy) return;
            networkActive = active;
            networkHead = head;
            networkAt = Time.unscaledTime;
        }

        void LateUpdate()
        {
            if (!equipped || !visual || !owner) return;
            Vector3 pivot = Pivot;
            if (!owner.IsNetworkProxy)
            {
                // The Rigidbody moves after FixedUpdate; keep the rendered head on the current car pose.
                Vector3 carry = pivot - previousPivot;
                if (carry.sqrMagnitude > .000001f)
                {
                    Vector3 before = HeadPosition;
                    HeadPosition += carry;
                    previousHead += carry;
                    previousPivot = pivot;
                    if (grounded) RollFrom(before, HeadPosition, groundNormal);
                }
                if (!deployed)
                {
                    HeadPosition = previousHead = pivot + owner.transform.forward * StowedReach;
                    previousPivot = pivot;
                }
            }
            if (owner.IsNetworkProxy)
            {
                Vector3 prior = HeadPosition;
                Vector3 destination = networkActive && Time.unscaledTime - networkAt < .45f
                    ? networkHead : pivot + owner.transform.forward * StowedReach;
                HeadPosition = Vector3.Lerp(HeadPosition, destination, 1f - Mathf.Exp(-20f * Time.unscaledDeltaTime));
                if (networkActive) ChainSound(HeadPosition - prior, pivot - networkPreviousPivot, Time.deltaTime);
                networkPreviousPivot = pivot;
                bool wasGrounded = grounded;
                grounded = networkActive && GroundBelow(HeadPosition, out float floor, out groundNormal) &&
                    HeadPosition.y <= floor + HeadRadius * .9f;
                if (grounded)
                {
                    if (!wasGrounded) rollingRotation = washer.rotation;
                    RollFrom(prior, HeadPosition, groundNormal);
                }
            }
            Vector3 span = HeadPosition - pivot;
            float length = span.magnitude;
            if (length < .01f) return;
            Quaternion linkRotation = Quaternion.FromToRotation(Vector3.up, span);
            for (int i = 0; i < LinkCount; i++)
            {
                Transform link = links[i];
                link.position = pivot + span * ((i + .5f) / LinkCount);
                link.rotation = linkRotation * Quaternion.Euler(0, i * 90, 0);
                link.localScale = new Vector3(.13f, length / LinkCount * .65f, .25f);
            }
            washer.position = HeadPosition;
            washer.rotation = grounded ? rollingRotation : Quaternion.LookRotation(span, Vector3.up) * Quaternion.Euler(0, 0, angle * 1.7f);
        }

        void BuildVisual()
        {
            if (!casing)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                casing = new Material(shader) { color = new Color(.72f, .79f, .83f), hideFlags = HideFlags.DontSave };
                steel = new Material(shader) { color = new Color(.32f, .36f, .39f), hideFlags = HideFlags.DontSave };
                dark = new Material(shader) { color = new Color(.09f, .12f, .15f), hideFlags = HideFlags.DontSave };
            }
            visual = new GameObject("Washing machine flail").transform;
            visual.SetParent(transform, false);
            for (int i = 0; i < LinkCount; i++) links[i] = Part("Chain link " + (i + 1), PrimitiveType.Capsule, visual, steel);
            washer = new GameObject("Washer head").transform;
            washer.SetParent(visual, false);
            Part("Enamel cabinet", PrimitiveType.Cube, washer, casing).localScale = new Vector3(1.45f, 1.45f, 1.5f);
            var panel = Part("Control panel", PrimitiveType.Cube, washer, steel);
            panel.localPosition = new Vector3(0, .59f, .79f); panel.localScale = new Vector3(1.35f, .23f, .12f);
            var door = Part("Round drum door", PrimitiveType.Cylinder, washer, steel);
            door.localPosition = new Vector3(0, -.12f, .79f);
            door.localRotation = Quaternion.Euler(90, 0, 0);
            door.localScale = new Vector3(.52f, .1f, .52f);
            var glass = Part("Drum window", PrimitiveType.Cylinder, washer, dark);
            glass.localPosition = new Vector3(0, -.12f, .88f);
            glass.localRotation = Quaternion.Euler(90, 0, 0);
            glass.localScale = new Vector3(.39f, .11f, .39f);
        }

        static Transform Part(string name, PrimitiveType type, Transform parent, Material material)
        {
            var part = GameObject.CreatePrimitive(type);
            part.name = name;
            part.layer = 2; // Ignore Raycast: the visual cannot intercept the player's pointer.
            var collider = part.GetComponent<Collider>();
            collider.enabled = false;
            Destroy(collider);
            part.transform.SetParent(parent, false);
            part.GetComponent<Renderer>().sharedMaterial = material;
            return part.transform;
        }
    }
}
