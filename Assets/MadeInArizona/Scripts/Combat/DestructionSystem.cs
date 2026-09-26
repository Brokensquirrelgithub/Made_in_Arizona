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
            // Hostiles plough through scenery too; only the player's (or unattributed) destruction earns score.
            var sourceCar = source ? source.GetComponentInParent<VehicleController>() : null;
            bool hostileCaused = sourceCar && !sourceCar.IsPlayer;
            if (!hostileCaused) GameManager.Instance?.Mission?.RegisterDestruction(score, bounds.center);
            float scale = Mathf.Clamp(bounds.size.magnitude, 1, 12);
            ExplosionSystem.ScatterDebris(bounds.center, 3 + scale * .65f, Mathf.Clamp(Mathf.RoundToInt(3 + scale * .45f), 4, 8), color);
            ExplosionSystem.Burst(bounds.center, new Color(.58f, .42f, .26f, .5f), Mathf.RoundToInt(8 + scale * 2), 2 + scale * .3f);
            if (Explosive)
            {
                float radius = Kind == ExplosionKind.FuelTank ? 12 : Kind == ExplosionKind.Massive ? 22 : Kind == ExplosionKind.Propane ? 8 : Kind == ExplosionKind.Electrical ? 7 : 6;
                ExplosionSystem.Detonate(bounds.center, radius, radius * 18, source, Kind);
            }
            if (!hostileCaused) GetComponent<WorldDiscovery>()?.OnDestroyed(source);
            Destroy(gameObject, .05f);
        }
        public bool TryDestroyFromVehicle(float amount, Vector3 hitPoint, GameObject source, out bool smallProp)
        {
            smallProp = Size <= 3f;
            if (IsDestroyed || amount <= 0) return false;
            ApplyDamage(amount, hitPoint, source);
            return IsDestroyed;
        }
        /// <summary>Knocks the prop apart as a car drives through it: full destruction, explosions included.</summary>
        public void SmashFromVehicle(Vector3 hitPoint, GameObject source)
        {
            if (IsDestroyed) return;
            ApplyDamage(Health + 1, hitPoint, source);
        }
        /// <summary>Enables or disables contact between every collider of this prop and a vehicle body.</summary>
        public void SetVehicleCollision(Collider vehicleBody, bool enabled)
        {
            if (!vehicleBody) return;
            foreach (var collider in GetComponentsInChildren<Collider>())
                if (collider && collider != vehicleBody) Physics.IgnoreCollision(vehicleBody, collider, !enabled);
        }
        float size = -1;
        /// <summary>Largest dimension of the prop's collision volume (renderers when it has none), measured once.</summary>
        public float Size
        {
            get
            {
                if (size < 0) { var bounds = WorldBounds; size = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z)); }
                return size;
            }
        }
        Bounds? cachedBounds;
        /// <summary>World bounds of the prop, measured once: scenery props never move until they are destroyed.</summary>
        public Bounds WorldBounds => cachedBounds ?? (cachedBounds = MeasureBounds()).Value;
        Bounds MeasureBounds()
        {
            Bounds bounds = new Bounds(transform.position, Vector3.zero);
            bool found = false;
            foreach (var collider in GetComponentsInChildren<Collider>())
            {
                if (!collider.enabled) continue;
                if (!found) { bounds = collider.bounds; found = true; }
                else bounds.Encapsulate(collider.bounds);
            }
            if (!found)
                foreach (var renderer in GetComponentsInChildren<Renderer>())
                {
                    if (!found) { bounds = renderer.bounds; found = true; }
                    else bounds.Encapsulate(renderer.bounds);
                }
            return bounds;
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
