using System.Collections.Generic;
using UnityEngine;

namespace MadeInArizona
{
    /// <summary>A held, steerable melee weapon. The host sweeps its head through physics; guests draw replicated motion.</summary>
    public sealed class WashingMachineFlail : MonoBehaviour
    {
        const float Reach = 7.2f;
        const float StowedReach = 1.05f;
        const float HeadRadius = .95f;
        const int LinkCount = 14;
        readonly Collider[] contacts = new Collider[48];
        readonly RaycastHit[] sweepHits = new RaycastHit[48];
        readonly Dictionary<Component, float> hitAgainAt = new Dictionary<Component, float>();
        readonly Transform[] links = new Transform[LinkCount];
        VehicleController owner;
        Transform visual, washer;
        float angle, targetAngle, angularSpeed, reach = StowedReach, triggerAt = -10, networkAt = -10, baseDamage = 60;
        Vector3 previousHead, networkHead;
        bool equipped, networkActive;
        static Material casing, steel, dark;

        public bool Extended => equipped && reach > StowedReach + .25f;
        public Vector3 HeadPosition { get; private set; }

        public void Bind(VehicleController vehicle)
        {
            owner = vehicle;
            Vector3 forward = vehicle.transform.forward;
            angle = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            targetAngle = angle;
            HeadPosition = Pivot + forward * StowedReach;
            previousHead = HeadPosition;
            if (!visual) BuildVisual();
        }

        public void SetEquipped(bool value)
        {
            equipped = value;
            if (visual) visual.gameObject.SetActive(value);
            if (!value) { reach = StowedReach; triggerAt = -10; }
        }

        public void Swing(Vector3 direction, float damage)
        {
            if (!equipped || !owner || owner.IsNetworkProxy) return;
            triggerAt = Time.time;
            baseDamage = damage;
            direction.y = 0;
            if (direction.sqrMagnitude > .001f)
                targetAngle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        }

        Vector3 Pivot => owner ? owner.transform.position + Vector3.up * 1.65f : transform.position;
        Vector3 Direction => new Vector3(Mathf.Sin(angle * Mathf.Deg2Rad), 0, Mathf.Cos(angle * Mathf.Deg2Rad));

        void FixedUpdate()
        {
            if (!equipped || !owner || owner.IsNetworkProxy) return;
            bool active = owner.Damage && !owner.Damage.IsDead && GameManager.Instance && GameManager.Instance.IsPlaying &&
                Time.time - triggerAt < .14f;
            float dt = Time.fixedDeltaTime;
            float desired = active ? Reach : StowedReach;
            reach = Mathf.MoveTowards(reach, desired, dt * (active ? 12f : 18f));
            // The head has angular momentum: moving the pointer steers the swing instead of teleporting it.
            float error = Mathf.DeltaAngle(angle, active ? targetAngle : Mathf.Atan2(owner.transform.forward.x, owner.transform.forward.z) * Mathf.Rad2Deg);
            float acceleration = Mathf.Clamp(error * 24f - angularSpeed * 4.2f, -950f, 950f);
            angularSpeed = Mathf.Clamp(angularSpeed + acceleration * dt, -720f, 720f);
            angle += angularSpeed * dt;
            HeadPosition = Pivot + Direction * reach;
            if (active && reach > 2.5f) HitSweep(previousHead, HeadPosition, dt);
            previousHead = HeadPosition;
        }

        void HitSweep(Vector3 start, Vector3 end, float dt)
        {
            Vector3 travel = end - start;
            float distance = travel.magnitude;
            float speed = distance / Mathf.Max(.001f, dt);
            if (speed < 4f) return;
            int count = Physics.OverlapSphereNonAlloc(end, HeadRadius, contacts, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++) Hit(contacts[i], end, travel, speed);
            if (distance < .01f) return;
            count = Physics.SphereCastNonAlloc(start, HeadRadius, travel / distance, sweepHits, distance, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++) Hit(sweepHits[i].collider, sweepHits[i].point, travel, speed);
        }

        void Hit(Collider collider, Vector3 point, Vector3 travel, float speed)
        {
            if (!collider || collider.transform.IsChildOf(owner.transform)) return;
            var car = collider.GetComponentInParent<VehicleController>();
            var prop = car ? null : collider.GetComponentInParent<DestructionSystem>();
            if (!car && !prop) return;
            if (car && !ProjectileSystem.Hostile(owner.gameObject, car)) return;
            Component key = car ? (Component)car : prop;
            if (hitAgainAt.TryGetValue(key, out float next) && Time.time < next) return;
            hitAgainAt[key] = Time.time + .48f;
            float damage = Mathf.Clamp(baseDamage * speed / 22f, baseDamage * .2f, baseDamage * 1.6f);
            Vector3 push = travel.normalized;
            if (car)
            {
                car.Damage.ApplyDamage(damage, point, owner.gameObject);
                if (car.Body && !car.Body.isKinematic) car.Body.AddForceAtPosition(push * Mathf.Min(7f, speed * .16f), point, ForceMode.VelocityChange);
            }
            else prop.ApplyProjectileHit(damage, point, owner.gameObject, push);
            AudioManager.Instance?.PlayCrash(point, Mathf.Clamp01(speed / 32f));
            if (owner.IsPlayer) CameraController.Instance?.Shake(.08f);
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
            if (owner.IsNetworkProxy)
            {
                Vector3 destination = networkActive && Time.unscaledTime - networkAt < .45f
                    ? networkHead : pivot + owner.transform.forward * StowedReach;
                HeadPosition = Vector3.Lerp(HeadPosition, destination, 1f - Mathf.Exp(-20f * Time.unscaledDeltaTime));
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
            washer.rotation = Quaternion.LookRotation(span, Vector3.up) * Quaternion.Euler(0, 0, angle * 1.7f);
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
