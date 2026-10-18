using System.Collections.Generic;
using UnityEngine;

namespace MadeInArizona
{
    /// <summary>A moving mirror-dust packet that settles into a brief ground-level reflector.</summary>
    public sealed class MirrorDustCloud : MonoBehaviour
    {
        static readonly List<MirrorDustCloud> active = new List<MirrorDustCloud>();
        static float nextRayScatterAt;
        VehicleController owner;
        Vector3 velocity;
        float launchedAt;
        Material material;

        public static void Launch(VehicleController owner, Vector3 muzzle, Vector3 direction)
        {
            if (!owner) return;
            Vector3 flat = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
            if (flat.sqrMagnitude < .01f) flat = owner.transform.forward;
            Create(owner, muzzle, flat * 60f);
            if (CoopSession.Instance && CoopSession.Instance.IsHost)
                CoopSession.Instance.PublishFx(CoopFx.MirrorDustLaunch, muzzle, flat * 60f);
        }

        public static void Replica(Vector3 position, Vector3 velocity) => Create(null, position, velocity);

        static void Create(VehicleController owner, Vector3 position, Vector3 velocity)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "Mirror dust cloud";
            if (GameManager.Instance && GameManager.Instance.World)
                go.transform.SetParent(GameManager.Instance.World.transform);
            var collider = go.GetComponent<Collider>();
            if (collider) Destroy(collider);
            var cloud = go.AddComponent<MirrorDustCloud>();
            cloud.owner = owner; cloud.velocity = velocity; cloud.launchedAt = Time.time;
            cloud.material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            cloud.material.color = new Color(.55f, .75f, .82f);
            cloud.material.EnableKeyword("_EMISSION");
            cloud.material.SetColor("_EmissionColor", new Color(.8f, 1.15f, 1.4f));
            go.GetComponent<Renderer>().sharedMaterial = cloud.material;
            go.transform.localScale = new Vector3(4.5f, .06f, 4.5f);
            go.transform.position = Ground(position);
            ExplosionSystem.Burst(go.transform.position, new Color(.7f, .9f, 1.2f), 18, 7);
        }

        static Vector3 Ground(Vector3 point)
        {
            if (Physics.Raycast(point + Vector3.up * 20, Vector3.down, out var hit, 60,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) point.y = hit.point.y;
            return point + Vector3.up * .18f;
        }

        void OnEnable() { active.Add(this); }
        void OnDisable() { active.Remove(this); }
        void OnDestroy() { if (material) Destroy(material); }

        void Update()
        {
            float age = Time.time - launchedAt;
            if (age >= 3.55f) { Destroy(gameObject); return; }
            if (age < .55f) transform.position = Ground(transform.position + velocity * Time.deltaTime);
            float pulse = 1f + .1f * Mathf.Sin(Time.time * 12f);
            transform.localScale = new Vector3(4.5f * pulse, .06f, 4.5f * pulse);
            if (age > 3f && material) material.SetColor("_EmissionColor", new Color(.8f, 1.15f, 1.4f) * (3.55f - age) / .55f);
        }

        /// <summary>First active cloud a horizontal projectile segment crosses.</summary>
        public static bool Intersect(Vector3 from, Vector3 direction, float distance, out Vector3 point, out VehicleController reflector)
        {
            point = default; reflector = null;
            float nearest = distance + .001f;
            bool found = false;
            foreach (var cloud in active)
            {
                if (!cloud || Time.time - cloud.launchedAt >= 3.55f) continue;
                Vector3 delta = cloud.transform.position - from;
                float along = Vector3.Dot(delta, direction);
                if (along < 0 || along > distance || along >= nearest) continue;
                Vector3 closest = from + direction * along;
                Vector3 horizontal = cloud.transform.position - closest; horizontal.y = 0;
                if (horizontal.sqrMagnitude > 5f * 5f || Mathf.Abs(closest.y - cloud.transform.position.y) > 3f) continue;
                nearest = along; point = closest; reflector = cloud.owner; found = true;
            }
            return found;
        }

        public static void ScatterDeathRay(Vector3 point, VehicleController reflector, bool damage)
        {
            if (damage)
            {
                if (Time.time < nextRayScatterAt) return;
                nextRayScatterAt = Time.time + .35f;
            }
            var color = new Color(.65f, 1.4f, 2f);
            ExplosionSystem.Burst(point, color, 30, 12);
            for (int i = 0; i < 10; i++)
            {
                float angle = i * Mathf.PI * 2f / 10f;
                Vector3 direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                Vector3 start = point + Vector3.up * .3f;
                Vector3 end = start + direction * 42f;
                if (damage && reflector)
                {
                    var struck = new List<RaycastHit>();
                    end = ProjectileSystem.Hitscan(start, direction, 42f, reflector.gameObject, true, 0, struck);
                    foreach (var hit in struck)
                    {
                        var car = hit.collider.GetComponentInParent<VehicleController>();
                        if (car && ProjectileSystem.Hostile(reflector.gameObject, car))
                            car.Damage.ApplyDamage(38f, hit.point, reflector.gameObject);
                    }
                }
                WeaponFx.Beam(start, end, color, .42f, .28f);
            }
            if (damage && CoopSession.Instance && CoopSession.Instance.IsHost)
                CoopSession.Instance.PublishFx(CoopFx.MirrorDustStrike, point, Vector3.zero);
        }
    }
}
