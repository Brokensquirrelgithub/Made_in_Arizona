using UnityEngine;

namespace MadeInArizona
{
    public sealed class DestructionSystem : MonoBehaviour
    {
        [field: SerializeField] public float Health { get; private set; } = 40;
        [field: SerializeField] public float MaxHealth { get; private set; } = 40;
        public bool IsDestroyed { get; private set; }
        [field: SerializeField] public bool Explosive { get; private set; }
        [field: SerializeField] public ExplosionKind Kind { get; private set; }
        [SerializeField] int score = 10;
        public void Configure(float health, ExplosionKind kind, bool explosive, int destructionScore)
        {
            Health = MaxHealth = Mathf.Max(1, health); Kind = kind; Explosive = explosive; score = destructionScore;
        }
        public void ApplyDamage(float amount, Vector3 hitPoint, GameObject source)
        {
            if (IsDestroyed || amount <= 0) return;
            Health -= amount;
            if (Health > 0)
            {
                if (amount > 10) ExplosionSystem.Burst(hitPoint, new Color(1, .65f, .18f), 4, 2);
                return;
            }
            IsDestroyed = true;
            var renderers = GetComponentsInChildren<Renderer>();
            var bounds = new Bounds(transform.position, Vector3.one);
            Color color = new Color(.53f, .33f, .19f);
            if (renderers.Length > 0)
            {
                bounds = renderers[0].bounds;
                foreach (var renderer in renderers) { bounds.Encapsulate(renderer.bounds); renderer.enabled = false; }
                var material = renderers[0].sharedMaterial;
                if (material != null && material.HasProperty("_BaseColor")) color = material.GetColor("_BaseColor");
            }
            foreach (var collider in GetComponentsInChildren<Collider>()) collider.enabled = false;
            GameManager.Instance?.Mission?.RegisterDestruction(score, bounds.center);
            float scale = Mathf.Clamp(bounds.size.magnitude, 1, 12);
            ExplosionSystem.ScatterDebris(bounds.center, 3 + scale * .65f, Mathf.RoundToInt(4 + scale), color);
            ExplosionSystem.Burst(bounds.center, new Color(.58f, .42f, .26f, .5f), Mathf.RoundToInt(8 + scale * 2), 2 + scale * .3f);
            if (Explosive)
            {
                float radius = Kind == ExplosionKind.FuelTank ? 12 : Kind == ExplosionKind.Massive ? 22 : Kind == ExplosionKind.Propane ? 8 : Kind == ExplosionKind.Electrical ? 7 : 6;
                ExplosionSystem.Detonate(bounds.center, radius, radius * 18, source, Kind);
            }
            GetComponent<WorldDiscovery>()?.OnDestroyed(source);
            Destroy(gameObject, .05f);
        }
        public bool TryDestroyFromVehicle(float amount, Vector3 hitPoint, GameObject source, out bool smallProp)
        {
            smallProp = IsSmallProp();
            if (IsDestroyed || amount <= 0) return false;
            ApplyDamage(amount, hitPoint, source);
            return IsDestroyed;
        }
        bool IsSmallProp()
        {
            Bounds bounds = new Bounds(transform.position, Vector3.zero);
            bool foundBounds = false;
            foreach (var collider in GetComponentsInChildren<Collider>())
            {
                if (!collider.enabled) continue;
                if (!foundBounds) { bounds = collider.bounds; foundBounds = true; }
                else bounds.Encapsulate(collider.bounds);
            }
            if (!foundBounds)
                foreach (var renderer in GetComponentsInChildren<Renderer>())
                {
                    if (!foundBounds) { bounds = renderer.bounds; foundBounds = true; }
                    else bounds.Encapsulate(renderer.bounds);
                }
            if (!foundBounds) return false;
            Vector3 size = bounds.size;
            return Kind != ExplosionKind.Massive && Mathf.Max(size.x, Mathf.Max(size.y, size.z)) <= 3f && size.magnitude <= 4.25f;
        }
        void OnCollisionEnter(Collision collision)
        {
            if (IsDestroyed || collision.relativeVelocity.magnitude < 5) return;
            // VehicleController owns vehicle impacts so a single collision cannot damage the prop twice.
            if (collision.collider.GetComponentInParent<VehicleController>() != null) return;
            float mass = collision.rigidbody != null ? collision.rigidbody.mass : 20;
            float damage = collision.relativeVelocity.magnitude * Mathf.Sqrt(mass) * .25f;
            ApplyDamage(damage, collision.contactCount > 0 ? collision.GetContact(0).point : transform.position, collision.gameObject);
        }
    }
}
