using UnityEngine;

namespace MadeInArizona
{
    public sealed class FieldOrdnance : MonoBehaviour
    {
        readonly RaycastHit[] hits = new RaycastHit[16];
        VehicleController owner;
        Vector3 velocity;
        float damage, radius, armedAt, expiresAt;
        bool mine;

        public static void LaunchShell(VehicleController owner, Vector3 origin, Vector3 aim, WeaponDefinition weapon, float damage, float horizontalSpeed = 0)
        {
            var shell = Create("Arc shell • " + weapon.displayName, origin, weapon.projectileColor);
            shell.owner = owner; shell.mine = false; shell.damage = damage; shell.radius = weapon.blastRadius;
            Vector3 horizontal = new Vector3(aim.x, 0, aim.z).normalized;
            shell.velocity = horizontal * (horizontalSpeed > 0 ? horizontalSpeed : weapon.speed) + Vector3.up * (weapon.id == "mortar" ? 31 : 17);
            shell.armedAt = Time.time + .12f; shell.expiresAt = Time.time + (weapon.id == "mortar" ? 5 : 3);
        }
        public static void PlaceMine(VehicleController owner, WeaponDefinition weapon, float damage, Vector3? dropPosition = null)
        {
            Vector3 position = dropPosition ?? owner.transform.position - owner.transform.forward * 3.3f + Vector3.up * .4f;
            if (Physics.Raycast(position + Vector3.up * 6, Vector3.down, out var hit, 15, ~0, QueryTriggerInteraction.Ignore))
                position = hit.point + Vector3.up * .25f;
            var charge = Create("Road mine • " + weapon.displayName, position, weapon.projectileColor);
            charge.owner = owner; charge.mine = true; charge.damage = damage; charge.radius = weapon.blastRadius;
            charge.armedAt = Time.time + .6f; charge.expiresAt = Time.time + 25;
            charge.transform.localScale = new Vector3(.8f, .25f, .8f);
        }
        static FieldOrdnance Create(string name, Vector3 position, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name; go.transform.position = position; go.transform.localScale = Vector3.one * .42f;
            if (GameManager.Instance != null && GameManager.Instance.World != null) go.transform.SetParent(GameManager.Instance.World.transform);
            Destroy(go.GetComponent<Collider>());
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            material.color = color; material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", color * 2);
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go.AddComponent<FieldOrdnance>();
        }
        void Update()
        {
            if (!GameManager.Instance || !GameManager.Instance.IsPlaying || !owner) return;
            if (mine)
            {
                transform.Rotate(Vector3.up, 90 * Time.deltaTime);
                if (Time.time > armedAt)
                {
                    foreach (var vehicle in VehicleController.Active)
                    {
                        if (!vehicle || vehicle == owner || vehicle.Damage.IsDead) continue;
                        var ai = vehicle.GetComponent<EnemyAI>();
                        if (owner.IsPlayer && (ai == null || ai.IsFriendly)) continue;
                        if (!owner.IsPlayer && !vehicle.IsPlayer && (ai == null || !ai.IsFriendly)) continue;
                        if ((vehicle.transform.position - transform.position).sqrMagnitude < 12.25f) { Detonate(); return; }
                    }
                }
                if (Time.time > expiresAt) Destroy(gameObject);
                return;
            }
            Vector3 step = velocity * Time.deltaTime;
            int count = Physics.SphereCastNonAlloc(transform.position, .22f, step.normalized, hits, step.magnitude, ~0, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            bool impact = false;
            for (int i = 0; i < count; i++)
            {
                if (hits[i].collider.GetComponentInParent<VehicleController>() == owner) continue;
                if (hits[i].distance < nearest) { nearest = hits[i].distance; impact = true; }
            }
            if (impact && Time.time > armedAt) { transform.position += step.normalized * nearest; Detonate(); return; }
            transform.position += step;
            velocity += Vector3.down * 32 * Time.deltaTime;
            transform.Rotate(Vector3.right, 420 * Time.deltaTime);
            if (Time.time > expiresAt) Detonate();
        }
        void Detonate()
        {
            ExplosionSystem.Detonate(transform.position, radius, damage, owner.gameObject, mine ? ExplosionKind.Ammunition : ExplosionKind.Grenade);
            Destroy(gameObject);
        }
        void OnDestroy() { var renderer = GetComponent<Renderer>(); if (renderer && renderer.sharedMaterial) Destroy(renderer.sharedMaterial); }
    }
}
