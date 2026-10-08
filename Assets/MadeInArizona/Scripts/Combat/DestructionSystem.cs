using System.Collections;
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
        [SerializeField] bool brittle, plowable, splinters;
        [SerializeField] float toppleMass;
        [SerializeField] Color debrisColor;
        /// <summary>Props up to this size (and brittle ones) break within two projectile hits.</summary>
        public const float SmallPropSize = 3.2f;
        /// <summary>Every projectile hit on a small prop takes at least this share of its health: one or two hits break it.</summary>
        const float ProjectileBreakShare = .55f;
        /// <summary>Speed (m/s) a shot adds to the pieces of the prop it breaks, along its flight.</summary>
        const float ProjectilePush = 7;
        /// <summary>Share of a push that reaches a falling tree: a trunk is far heavier than debris.</summary>
        const float TreePushShare = .18f;
        const float TreeFallTime = 1.4f, TreeFadeTime = .7f;
        /// <summary>
        /// Layer of brittle scenery (cacti). It never collides with cars (layer 0): the car's scenery sweep breaks brittle
        /// props before contact, so contact pairs would only cost time. Rounds, blasts and the sweep still find it.
        /// </summary>
        public const int BrittleLayer = 8;
        /// <summary>Fallen trees and debris: not raycast, so rounds, blasts and car sweeps pass through.</summary>
        const int FallenLayer = 2;
        static readonly int DissolveId = Shader.PropertyToID("_Dissolve");
        static readonly Collider[] nearby = new Collider[96];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void IgnoreBrittleContacts() => Physics.IgnoreLayerCollision(0, BrittleLayer, true);

        public void Configure(float health, ExplosionKind kind, bool explosive, int destructionScore)
        {
            Health = MaxHealth = Mathf.Max(1, health); Kind = kind; Explosive = explosive; score = destructionScore;
        }
        /// <summary>
        /// Plants and loose stone: sized by footprint rather than height (so a tall cactus still ploughs like a small
        /// prop) and shot apart within two hits, even by weak rounds.
        /// </summary>
        public void MakeBrittle() { brittle = true; size = -1; }
        public bool Brittle => brittle;
        /// <summary>
        /// Cars drive straight through it at any size and speed without losing pace: loose rock, cacti and dead snags.
        /// Brittle props always are; <see cref="MakePlowable"/> marks others.
        /// </summary>
        public bool Plowable => brittle || plowable;
        public void MakePlowable() { plowable = true; }
        /// <summary>Breaks into long wooden pieces (trunk sections and limbs) instead of toppling or crumbling: dead snags.</summary>
        public void MakeSplinter() { splinters = true; plowable = true; toppleMass = 0; }
        /// <summary>Colour of the pieces, for props whose material has no base colour (shared scenery shaders).</summary>
        public void SetDebrisColor(Color color) { debrisColor = color; debrisColor.a = 1; }
        /// <summary>Instead of shattering, the prop topples as one heavy body of the given mass and fades (trees).</summary>
        public void MakeTopple(float mass) { toppleMass = mass; }
        /// <summary>Breaks caused by hostile vehicles, which award the player nothing (counted for the regression suite).</summary>
        public static int UncreditedHostileBreaks { get; private set; }
        public void ApplyDamage(float amount, Vector3 hitPoint, GameObject source) => ApplyDamage(amount, hitPoint, source, Vector3.zero);
        /// <summary>
        /// Damages the prop; <paramref name="push"/> is the velocity (m/s) the hit gives its pieces if it breaks: along a
        /// shot, away from a blast, or with the car that ran it over.
        /// </summary>
        public void ApplyDamage(float amount, Vector3 hitPoint, GameObject source, Vector3 push)
        {
            if (IsDestroyed || amount <= 0) return;
            Health -= amount;
            if (Health > 0)
            {
                if (amount > 10) ExplosionSystem.Burst(hitPoint, new Color(1, .65f, .18f), 4, 2);
                return;
            }
            IsDestroyed = true;
            if (CoopSession.Instance && CoopSession.Instance.IsHost)
                CoopSession.Instance.PublishPropBreak(transform.position, name);
            var renderers = GetComponentsInChildren<Renderer>();
            var bounds = new Bounds(transform.position, Vector3.one);
            Color color = new Color(.53f, .33f, .19f);
            bool topple = toppleMass > 0;
            if (renderers.Length > 0)
            {
                bounds = renderers[0].bounds;
                foreach (var renderer in renderers) { bounds.Encapsulate(renderer.bounds); if (!topple) renderer.enabled = false; }
                var material = renderers[0].sharedMaterial;
                if (material != null && material.HasProperty("_BaseColor")) color = material.GetColor("_BaseColor");
            }
            if (debrisColor.a > 0) color = debrisColor;
            foreach (var collider in GetComponentsInChildren<Collider>()) collider.enabled = false;
            // Hostiles plough through scenery too; only the player's (or unattributed) destruction earns score.
            var sourceCar = source ? source.GetComponentInParent<VehicleController>() : null;
            bool hostileCaused = sourceCar && !sourceCar.IsPlayer;
            if (!hostileCaused) GameManager.Instance?.Mission?.RegisterDestruction(score, bounds.center);
            else UncreditedHostileBreaks++;
            float scale = Mathf.Clamp(bounds.size.magnitude, 1, 12);
            if (topple)
            {
                ExplosionSystem.Burst(new Vector3(bounds.center.x, bounds.min.y + .4f, bounds.center.z), new Color(.58f, .42f, .26f, .5f), 10, 2.5f);
                if (!hostileCaused) GetComponent<WorldDiscovery>()?.OnDestroyed(source);
                Topple(bounds, hitPoint, push);
                return;
            }
            if (splinters) Splinter(bounds, color, push);
            else ExplosionSystem.ScatterDebris(bounds.center, 3 + scale * .65f, Mathf.Clamp(Mathf.RoundToInt(3 + scale * .45f), 4, 8), color, push);
            ExplosionSystem.Burst(bounds.center, new Color(.58f, .42f, .26f, .5f), Mathf.RoundToInt(8 + scale * 2), 2 + scale * .3f);
            if (Explosive)
            {
                float radius = Kind == ExplosionKind.FuelTank ? 12 : Kind == ExplosionKind.Massive ? 22 : Kind == ExplosionKind.Propane ? 8 : Kind == ExplosionKind.Electrical ? 7 : 6;
                ExplosionSystem.Detonate(bounds.center, radius, radius * 18, source, Kind, environmental:true);
            }
            if (!hostileCaused) GetComponent<WorldDiscovery>()?.OnDestroyed(source);
            Destroy(gameObject, .05f);
        }
        public void ApplyNetworkBreak()
        {
            if (IsDestroyed) return;
            IsDestroyed = true;
            var bounds = WorldBounds;
            Color color = debrisColor.a > 0 ? debrisColor : new Color(.53f, .33f, .19f);
            foreach (var renderer in GetComponentsInChildren<Renderer>()) renderer.enabled = false;
            foreach (var collider in GetComponentsInChildren<Collider>()) collider.enabled = false;
            ExplosionSystem.Burst(transform.position + Vector3.up, new Color(.58f, .42f, .26f, .5f), 8, 2);
            // Guests see the same kind of pieces fly, though not the host's exact ones.
            if (splinters) Splinter(bounds, color, Vector3.zero);
            else if (brittle) ExplosionSystem.ScatterDebris(bounds.center, 4, 5, color);
            Destroy(gameObject, .05f);
        }
        /// <summary>A dead snag snaps into trunk sections and limbs that tumble away with whatever broke it.</summary>
        void Splinter(Bounds bounds, Color color, Vector3 push)
        {
            float height = Mathf.Max(1.5f, bounds.size.y);
            Color wood = Color.Lerp(color, new Color(.42f, .33f, .24f), .5f);
            // Thick trunk sections from the lower half, thin limbs from the crown, then bark chips.
            ExplosionSystem.ScatterPieces(new Vector3(bounds.center.x, bounds.min.y + height * .3f, bounds.center.z), 3.5f, 4, wood, push,
                new Vector3(.22f, .22f, height * .2f), new Vector3(.34f, .34f, height * .32f), height * .25f);
            ExplosionSystem.ScatterPieces(new Vector3(bounds.center.x, bounds.min.y + height * .7f, bounds.center.z), 5, 7, wood * 1.08f, push,
                new Vector3(.06f, .06f, .6f), new Vector3(.12f, .12f, 1.5f), height * .3f);
            ExplosionSystem.ScatterDebris(bounds.center, 4, 6, wood * .9f, push * .8f);
            ExplosionSystem.Burst(new Vector3(bounds.center.x, bounds.min.y + .4f, bounds.center.z), new Color(.55f, .45f, .33f, .5f), 10, 2.2f);
        }
        /// <summary>
        /// A bullet, pellet or bolt strike. Small props break within two hits, so they soak up fire without slowing a car,
        /// and their pieces fly on along the shot.
        /// </summary>
        public void ApplyProjectileHit(float amount, Vector3 hitPoint, GameObject source, Vector3 direction)
        {
            if (IsDestroyed || amount <= 0) return;
            if (brittle || Size <= SmallPropSize) amount = Mathf.Max(amount, MaxHealth * ProjectileBreakShare);
            direction.y = Mathf.Max(0, direction.y);
            ApplyDamage(amount, hitPoint, source, direction.sqrMagnitude > .0001f ? direction.normalized * ProjectilePush : Vector3.zero);
        }
        public bool TryDestroyFromVehicle(float amount, Vector3 hitPoint, GameObject source, out bool smallProp)
        {
            smallProp = Size <= 3f;
            if (IsDestroyed || amount <= 0) return false;
            ApplyDamage(amount, hitPoint, source, CarriedBy(source));
            return IsDestroyed;
        }
        /// <summary>Knocks the prop apart as a car drives through it: full destruction, explosions included.</summary>
        public void SmashFromVehicle(Vector3 hitPoint, GameObject source) => SmashFromVehicle(hitPoint, source, CarriedBy(source));
        public void SmashFromVehicle(Vector3 hitPoint, GameObject source, Vector3 push)
        {
            if (IsDestroyed) return;
            ApplyDamage(Health + 1, hitPoint, source, push);
        }
        /// <summary>Pieces of a prop a car runs through are carried along at most of the car's speed.</summary>
        static Vector3 CarriedBy(GameObject source)
        {
            var car = source ? source.GetComponentInParent<VehicleController>() : null;
            return car && car.Body ? car.Body.linearVelocity * .7f : Vector3.zero;
        }
        /// <summary>
        /// Trees fall instead of shattering: the prop keeps its look and becomes one heavy body that no longer touches cars
        /// or stops rounds. It tips away from the hit (a push moves it far less than debris), then dissolves.
        /// </summary>
        void Topple(Bounds bounds, Vector3 hitPoint, Vector3 push)
        {
            foreach (var child in GetComponentsInChildren<Transform>()) child.gameObject.layer = FallenLayer;
            float height = Mathf.Max(1, bounds.size.y);
            var trunk = gameObject.AddComponent<CapsuleCollider>();
            trunk.direction = 1; trunk.height = height * .9f; trunk.radius = Mathf.Clamp(height * .06f, .3f, 1.1f);
            trunk.center = transform.InverseTransformPoint(new Vector3(transform.position.x, bounds.min.y + height * .45f, transform.position.z));
            foreach (var vehicle in VehicleController.Active)
                if (vehicle)
                    foreach (var part in vehicle.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(trunk, part, true);
            // Only the ground and solid structures stop the trunk: it falls through neighbouring trees, rocks and props
            // (in a grove it would otherwise hang on the next trunk).
            int count = Physics.OverlapSphereNonAlloc(bounds.center, height, nearby, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (nearby[i] && nearby[i] != trunk && nearby[i].GetComponentInParent<DestructionSystem>()) Physics.IgnoreCollision(trunk, nearby[i], true);
            var body = gameObject.AddComponent<Rigidbody>();
            body.mass = toppleMass; body.linearDamping = .05f; body.angularDamping = .05f;
            body.centerOfMass = trunk.center; body.interpolation = RigidbodyInterpolation.Interpolate;
            Vector3 fall = new Vector3(push.x, 0, push.z);
            if (fall.sqrMagnitude < .01f) { fall = transform.position - hitPoint; fall.y = 0; }
            if (fall.sqrMagnitude < .01f) { var random = Random.insideUnitCircle; fall = new Vector3(random.x, 0, random.y); }
            fall.Normalize();
            // Every tree tips at least gently; a harder push starts it faster and slides the trunk a little.
            float shove = Mathf.Min(push.magnitude, 20) * TreePushShare;
            body.linearVelocity = fall * shove * .5f;
            body.angularVelocity = Vector3.Cross(Vector3.up, fall) * (1.05f + shove * .15f);
            StartCoroutine(FallAndFade());
        }
        IEnumerator FallAndFade()
        {
            yield return new WaitForSeconds(TreeFallTime);
            var renderers = GetComponentsInChildren<Renderer>();
            var block = new MaterialPropertyBlock();
            bool dissolves = renderers.Length > 0 && renderers[0].sharedMaterial && renderers[0].sharedMaterial.HasProperty(DissolveId);
            Vector3 scale = transform.localScale;
            for (float t = 0; t < TreeFadeTime; t += Time.deltaTime)
            {
                float k = t / TreeFadeTime;
                if (dissolves)
                    foreach (var renderer in renderers) { if (!renderer) continue; renderer.GetPropertyBlock(block); block.SetFloat(DissolveId, k); renderer.SetPropertyBlock(block); }
                else transform.localScale = scale * (1 - k);
                yield return null;
            }
            Destroy(gameObject);
        }
        /// <summary>Enables or disables contact between every collider of this prop and a vehicle body.</summary>
        public void SetVehicleCollision(Collider vehicleBody, bool enabled)
        {
            if (!vehicleBody) return;
            foreach (var collider in GetComponentsInChildren<Collider>())
                if (collider && collider != vehicleBody) Physics.IgnoreCollision(vehicleBody, collider, !enabled);
        }
        float size = -1;
        /// <summary>
        /// Largest dimension of the prop's collision volume (renderers when it has none), measured once. Brittle props
        /// use their footprint: a tall cactus still ploughs like a small prop.
        /// </summary>
        public float Size
        {
            get
            {
                if (size < 0) { var bounds = WorldBounds; size = Mathf.Max(bounds.size.x, bounds.size.z); if (!brittle) size = Mathf.Max(size, bounds.size.y); }
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
            Vector3 push = collision.rigidbody != null ? collision.rigidbody.linearVelocity * .5f : Vector3.zero;
            ApplyDamage(damage, collision.contactCount > 0 ? collision.GetContact(0).point : transform.position, collision.gameObject, push);
        }
    }
}
