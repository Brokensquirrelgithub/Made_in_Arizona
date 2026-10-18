using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MadeInArizona
{
    /// <summary>A moving plume that leaves a short-lived wall of reflective dust.</summary>
    public sealed class MirrorDustCloud : MonoBehaviour
    {
        static readonly List<MirrorDustCloud> active = new List<MirrorDustCloud>();
        static float nextRayScatterAt;
        VehicleController owner;
        Vector3 velocity;
        float launchedAt;
        Material material;
        ParticleSystem particles;
        Vector3 start;
        static Texture2D softTexture;
        const float TravelTime = .55f;
        const float Duration = 3.55f;
        const float WallRadius = 3.2f;

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
            var go = new GameObject("Mirror dust cloud");
            go.name = "Mirror dust cloud";
            if (GameManager.Instance && GameManager.Instance.World)
                go.transform.SetParent(GameManager.Instance.World.transform);
            var cloud = go.AddComponent<MirrorDustCloud>();
            cloud.owner = owner; cloud.velocity = velocity; cloud.launchedAt = Time.time;
            cloud.start = Ground(position);
            go.transform.position = cloud.start + Vector3.up * 1.15f;
            cloud.CreatePlume();
            ExplosionSystem.Burst(go.transform.position, new Color(.7f, .9f, 1.2f), 12, 4);
        }

        static Texture2D SoftTexture()
        {
            if (softTexture) return softTexture;
            softTexture = new Texture2D(64, 64, TextureFormat.RGBA32, false) { name = "Mirror dust soft particle" };
            var pixels = new Color[64 * 64];
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
            {
                float dx = (x - 31.5f) / 31.5f, dy = (y - 31.5f) / 31.5f;
                float fade = Mathf.Pow(Mathf.Clamp01(1f - dx * dx - dy * dy), 1.7f);
                pixels[y * 64 + x] = new Color(1, 1, 1, fade);
            }
            softTexture.SetPixels(pixels); softTexture.Apply(false, true);
            return softTexture;
        }

        void CreatePlume()
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Particles/Standard Unlit");
            material = new Material(shader) { name = "Mirror dust vapor", renderQueue = 3000 };
            material.SetTexture("_BaseMap", SoftTexture()); material.SetTexture("_MainTex", softTexture);
            material.SetFloat("_Surface", 1); material.SetFloat("_Blend", 0);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0); material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetColor("_BaseColor", Color.white);
            particles = gameObject.AddComponent<ParticleSystem>();
            var main = particles.main;
            main.playOnAwake = false; main.loop = true; main.duration = 4f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3.55f, 3.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.15f, .9f);
            main.startSize = new ParticleSystem.MinMaxCurve(2.5f, 4.4f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(.62f, .8f, .88f, .52f), new Color(.9f, .96f, 1f, .7f));
            main.maxParticles = 600;
            var emission = particles.emission; emission.rateOverTime = 260f;
            var shape = particles.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 1.1f;
            var color = particles.colorOverLifetime; color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .12f),
                    new GradientAlphaKey(.85f, .7f), new GradientAlphaKey(0, 1) });
            color.color = new ParticleSystem.MinMaxGradient(gradient);
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            particles.Play();
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
            if (age >= Duration) { Destroy(gameObject); return; }
            if (age < TravelTime) transform.position = Ground(transform.position + velocity * Time.deltaTime) + Vector3.up * 1.15f;
            else if (particles && particles.emission.enabled)
            {
                var emission = particles.emission;
                emission.enabled = false;
            }
        }

        /// <summary>First active cloud a horizontal projectile segment crosses.</summary>
        public static bool Intersect(Vector3 from, Vector3 direction, float distance, out Vector3 point, out VehicleController reflector)
            => Intersect(from, direction, distance, out point, out reflector, out _);

        public static bool Intersect(Vector3 from, Vector3 direction, float distance, out Vector3 point,
            out VehicleController reflector, out Vector3 wallNormal)
        {
            point = default; reflector = null; wallNormal = default;
            float nearest = distance + .001f;
            bool found = false;
            foreach (var cloud in active)
            {
                if (!cloud || Time.time - cloud.launchedAt >= Duration) continue;
                Vector2 p = new Vector2(from.x, from.z);
                Vector2 shot = new Vector2(direction.x, direction.z) * distance;
                Vector2 a = new Vector2(cloud.start.x, cloud.start.z);
                Vector2 b = new Vector2(cloud.transform.position.x, cloud.transform.position.z);
                Vector2 wall = b - a;
                float shotLength = shot.sqrMagnitude, wallLength = wall.sqrMagnitude;
                if (shotLength < .0001f) continue;
                float u, v;
                if (wallLength < .0001f) { u = Mathf.Clamp01(Vector2.Dot(a - p, shot) / shotLength); v = 0; }
                else
                {
                    float cross = Vector2.Dot(shot, wall), determinant = shotLength * wallLength - cross * cross;
                    u = determinant > .0001f ? Mathf.Clamp01((Vector2.Dot(a - p, shot) * wallLength - Vector2.Dot(a - p, wall) * cross) / determinant) : 0;
                    v = Mathf.Clamp01((Vector2.Dot(p - a, wall) + u * cross) / wallLength);
                    u = Mathf.Clamp01(Vector2.Dot(a + wall * v - p, shot) / shotLength);
                    v = Mathf.Clamp01(Vector2.Dot(p + shot * u - a, wall) / wallLength);
                }
                if ((p + shot * u - a - wall * v).sqrMagnitude > WallRadius * WallRadius) continue;
                float along = distance * u;
                if (along >= nearest) continue;
                Vector3 closest = from + direction * along;
                if (Mathf.Abs(closest.y - cloud.start.y) > 3.5f) continue;
                Vector3 tangent = wallLength > .0001f ? new Vector3(wall.y, 0, -wall.x).normalized : -Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
                if (Vector3.Dot(tangent, direction) > 0) tangent = -tangent;
                nearest = along; point = closest; reflector = cloud.owner; wallNormal = tangent; found = true;
            }
            return found;
        }

        public static Vector3 Ricochet(Vector3 incoming, Vector3 wallNormal)
        {
            Vector3 reflected = Vector3.Reflect(incoming, wallNormal).normalized;
            // Even a shot grazing along the plume visibly changes course.
            return (Quaternion.AngleAxis(38f, Vector3.up) * reflected).normalized;
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
