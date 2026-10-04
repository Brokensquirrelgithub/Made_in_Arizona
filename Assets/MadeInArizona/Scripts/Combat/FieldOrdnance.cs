using System.Collections.Generic;
using UnityEngine;

namespace MadeInArizona
{
    /// <summary>
    /// Physical ordnance that lives in the world: lobbed shells (some bounce, spray fire or open a vortex), road
    /// charges, bomblets, stuck dynamite, the rolling bowling ball, the seeking go-kart bomb and the lawn-chair
    /// sentry. Targets are chosen by side: ordnance never homes on, triggers on or shoots its owner's allies,
    /// although its explosions hurt anyone in range.
    /// </summary>
    public sealed class FieldOrdnance : MonoBehaviour
    {
        enum Mode { Shell, Mine, Bomblet, Fuse, Roller, Crawler, Sentry, Vortex }
        enum Payload { None, FireRing, Vortex }
        readonly RaycastHit[] hits = new RaycastHit[16];
        static readonly List<FieldOrdnance> sentries = new List<FieldOrdnance>();
        static readonly ShotFx SentryRound = new ShotFx { detached = true };
        const float BowlingHitRadius = 2.88f; // 20% wider than the original 2.4 m sweep.
        VehicleController owner, assistedTarget, quarry;
        GameObject source;
        bool ownerFriendly;
        Vector3 assistedLanding, velocity;
        float damage, radius, armedAt, expiresAt, proximityRadius, nextShot, retargetAt, cruise, blinkAt;
        int bounces;
        Mode mode; Payload payload;
        Color color;
        Transform head, beaconBeam;
        Material ownMaterial, beaconMaterial;
        static readonly Color BeaconRed = new Color(1, .06f, .03f);
        readonly HashSet<VehicleController> struck = new HashSet<VehicleController>();

        public static void LaunchShell(VehicleController owner, Vector3 origin, Vector3 aim, WeaponDefinition weapon, float damage, float horizontalSpeed = 0, VehicleController assistedTarget = null)
        {
            var shell = Create("Arc shell • " + weapon.displayName, origin, weapon.projectileColor, owner, PrimitiveType.Cylinder);
            shell.mode = Mode.Shell; shell.damage = damage; shell.radius = weapon.blastRadius;
            shell.payload = weapon.id == "sprinkler" ? Payload.FireRing : weapon.id == "shopvac" ? Payload.Vortex : Payload.None;
            shell.bounces = weapon.id == "pothole" ? 1 : 0;
            if (weapon.id == "shopvac") shell.transform.localScale = new Vector3(.7f, .55f, .7f);
            shell.assistedTarget = assistedTarget;
            if (assistedTarget)
            {
                shell.assistedLanding = PredictLandingPoint(assistedTarget, origin, weapon);
                float flightTime = FlightTime(Vector3.ProjectOnPlane(shell.assistedLanding - origin, Vector3.up).magnitude, weapon);
                Vector3 displacement = shell.assistedLanding - origin;
                shell.velocity = new Vector3(displacement.x / flightTime,
                    (displacement.y + 16 * flightTime * flightTime) / flightTime,
                    displacement.z / flightTime);
                shell.proximityRadius = Mathf.Clamp(weapon.blastRadius * .38f, 2.1f, 4.2f);
                shell.expiresAt = Time.time + flightTime + 1.1f;
            }
            else
            {
                Vector3 horizontal = new Vector3(aim.x, 0, aim.z).normalized;
                shell.velocity = horizontal * (horizontalSpeed > 0 ? horizontalSpeed : weapon.speed) + Vector3.up * (weapon.id == "mortar" ? 31 : 17);
                shell.expiresAt = Time.time + (weapon.id == "mortar" ? 5 : 3);
            }
            if (shell.bounces > 0) shell.expiresAt += 1.2f;
            shell.armedAt = Time.time + .12f;
            // Only large, slow hostile shells get a ground warning. Fast rockets and small
            // bomblets keep their own readable projectile trails without covering the map.
            if (owner && !owner.FriendlyToPlayer && weapon.blastRadius >= 8f &&
                (weapon.id == "mortar" || weapon.id == "grenade") &&
                PredictGroundImpact(origin, shell.velocity, out Vector3 landing, out float predictedFlightTime))
                shell.gameObject.AddComponent<AttackWarningVisual>().BeginImpact(landing, weapon.blastRadius, predictedFlightTime);
        }
        static bool PredictGroundImpact(Vector3 origin, Vector3 initialVelocity, out Vector3 landing, out float flightTime)
        {
            var candidates = new RaycastHit[16];
            Vector3 point = origin, velocity = initialVelocity;
            flightTime = 0;
            for (int stepIndex = 0; stepIndex < 65; stepIndex++)
            {
                const float interval = .08f;
                Vector3 step = velocity * interval;
                int count = Physics.SphereCastNonAlloc(point, .22f, step.normalized, candidates, step.magnitude,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                float nearest = float.MaxValue; Vector3 hitPoint = Vector3.zero;
                if (flightTime >= .12f)
                    for (int i = 0; i < count; i++)
                    {
                        var hit = candidates[i];
                        if (!hit.collider || hit.collider.GetComponentInParent<VehicleController>()) continue;
                        if (hit.distance < nearest) { nearest = hit.distance; hitPoint = hit.point; }
                    }
                if (nearest < float.MaxValue)
                {
                    flightTime += interval * Mathf.Clamp01(nearest / Mathf.Max(.001f, step.magnitude));
                    landing = hitPoint;
                    return true;
                }
                point += step;
                velocity += Vector3.down * 32f * interval;
                flightTime += interval;
            }
            landing = point;
            return false;
        }
        public static Vector3 PredictLandingPoint(VehicleController target, Vector3 origin, WeaponDefinition weapon)
        {
            if (!target) return origin;
            Vector3 velocity = target.Body ? Vector3.ProjectOnPlane(target.Body.linearVelocity, Vector3.up) : Vector3.zero;
            Vector3 point = target.transform.position + Vector3.up * .28f;
            float time = FlightTime(Vector3.ProjectOnPlane(point - origin, Vector3.up).magnitude, weapon);
            point += velocity * time * .82f;
            time = FlightTime(Vector3.ProjectOnPlane(point - origin, Vector3.up).magnitude, weapon);
            return target.transform.position + Vector3.up * .28f + velocity * time * .82f;
        }
        static float FlightTime(float distance, WeaponDefinition weapon)
        {
            if (weapon && weapon.id == "mortar") return Mathf.Clamp(distance / Mathf.Max(24, weapon.speed * .84f), 1.05f, 2.5f);
            if (weapon && weapon.id == "pothole") return Mathf.Clamp(distance / Mathf.Max(21, weapon.speed * .68f), .68f, 1.4f);
            return Mathf.Clamp(distance / Mathf.Max(22, weapon ? weapon.speed * .68f : 28), .64f, 1.55f);
        }
        /// <summary>Enemy road charge dropped behind (or ahead of) a hostile car.</summary>
        public static void PlaceMine(VehicleController owner, WeaponDefinition weapon, float damage, Vector3? dropPosition = null)
        {
            Vector3 position = dropPosition ?? owner.transform.position - owner.transform.forward * 3.3f + Vector3.up * .4f;
            if (Physics.Raycast(position + Vector3.up * 6, Vector3.down, out var hit, 15, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                position = hit.point + Vector3.up * .25f;
            var charge = Create("Road charge • " + weapon.displayName, position, weapon.projectileColor, owner, PrimitiveType.Cylinder);
            charge.mode = Mode.Mine; charge.damage = damage; charge.radius = weapon.blastRadius;
            charge.armedAt = Time.time + .6f; charge.expiresAt = Time.time + 25;
            charge.transform.localScale = new Vector3(.8f, .25f, .8f);
            charge.AddWarningBeacon();
        }
        /// <summary>
        /// A red warning lamp on top of the mine with a spotlight sweeping the ground around it, so a mine on the road
        /// reads from the overhead camera. On the lowest preset (no extra lights) the flashing lamp still shows.
        /// </summary>
        void AddWarningBeacon()
        {
            var lamp = GameObject.CreatePrimitive(PrimitiveType.Sphere); lamp.name = "Mine warning beacon";
            Destroy(lamp.GetComponent<Collider>());
            lamp.transform.SetParent(transform, false);
            // The mine body is scaled (.8, .25, .8); counter-scale so the lamp is a small round dome on top.
            lamp.transform.localPosition = new Vector3(0, .85f, 0);
            lamp.transform.localScale = new Vector3(.36f / .8f, .28f / .25f, .36f / .8f);
            beaconMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            beaconMaterial.color = BeaconRed; beaconMaterial.EnableKeyword("_EMISSION"); beaconMaterial.SetColor("_EmissionColor", BeaconRed * 3);
            var renderer = lamp.GetComponent<Renderer>(); renderer.sharedMaterial = beaconMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            beaconBeam = new GameObject("Rotating beacon beam").transform; beaconBeam.SetParent(lamp.transform, false);
            var light = beaconBeam.gameObject.AddComponent<Light>();
            light.type = LightType.Spot; light.color = BeaconRed; light.range = 11; light.spotAngle = 62; light.innerSpotAngle = 22;
            light.intensity = 34; light.shadows = LightShadows.None; light.renderMode = LightRenderMode.ForcePixel;
        }
        void SpinBeacon()
        {
            if (!beaconBeam) return;
            // Tilted down so the beam sweeps a red arc across the ground, like a rotating road-works lamp.
            float angle = Time.time * 400;
            beaconBeam.rotation = Quaternion.Euler(32, angle, 0);
            // The lamp flares as the beam swings past the camera, as a real rotating reflector does.
            var view = Camera.main;
            float flare = 0;
            if (view)
            {
                Vector3 beam = Quaternion.Euler(0, angle, 0) * Vector3.forward, toView = view.transform.position - transform.position; toView.y = 0;
                flare = Mathf.Pow(Mathf.Max(0, Vector3.Dot(beam, toView.normalized)), 6);
            }
            beaconMaterial.SetColor("_EmissionColor", BeaconRed * (3.5f + 8 * flare));
        }
        /// <summary>A dynamite bolt stuck in the ground or a wall; it goes off after <paramref name="delay"/>.</summary>
        public static void PlantCharge(VehicleController owner, Vector3 point, float delay, float damage, float radius, Color color)
        {
            var charge = Create("Stuck dynamite", point, color, owner, PrimitiveType.Cylinder);
            charge.mode = Mode.Fuse; charge.damage = damage; charge.radius = radius; charge.expiresAt = Time.time + delay;
            charge.transform.localScale = new Vector3(.18f, .32f, .18f);
            if (owner && !owner.FriendlyToPlayer)
                charge.gameObject.AddComponent<AttackWarningVisual>().BeginImpact(point, radius, delay);
        }
        /// <summary>A small bouncing charge thrown out by a cluster payload.</summary>
        public static void Bomblet(VehicleController owner, Vector3 point, Vector3 velocity, float damage, float radius, Color color, float fuse = 2.2f, int bounces = 1)
        {
            var bomb = Create("Bomblet", point, color, owner, PrimitiveType.Sphere);
            bomb.mode = Mode.Bomblet; bomb.damage = damage; bomb.radius = radius; bomb.velocity = velocity;
            bomb.armedAt = Time.time + .15f; bomb.expiresAt = Time.time + fuse; bomb.bounces = bounces;
            bomb.transform.localScale = Vector3.one * .3f;
        }
        /// <summary>Bowling-ball cannon: a heavy ball that rolls along the ground through every hostile car in its lane.</summary>
        public static void Roll(VehicleController owner, Vector3 origin, Vector3 aim, WeaponDefinition weapon, float damage)
        {
            Vector3 flat = Vector3.ProjectOnPlane(aim, Vector3.up); if (flat.sqrMagnitude < .01f) flat = owner.transform.forward; flat.Normalize();
            var ball = Create("Bowling ball", origin + flat, new Color(.18f, .2f, .32f), owner, PrimitiveType.Sphere);
            ball.mode = Mode.Roller; ball.damage = damage; ball.velocity = flat * weapon.speed + Vector3.ProjectOnPlane(owner.Body.linearVelocity, Vector3.up) * .5f;
            ball.expiresAt = Time.time + 3.4f; ball.transform.localScale = Vector3.one * 1.08f;
            ball.ownMaterial.SetFloat("_Smoothness", .9f); ball.ownMaterial.SetColor("_EmissionColor", weapon.projectileColor * .6f);
        }
        /// <summary>Dynamite go-kart: drives off ahead of the car and hunts the nearest hostile.</summary>
        public static void LaunchGoKart(VehicleController owner, WeaponDefinition weapon, float damage)
        {
            Vector3 start = owner.transform.position + owner.transform.forward * 3.2f;
            var kart = Create("Dynamite go-kart", start, weapon.projectileColor, owner, null);
            kart.mode = Mode.Crawler; kart.damage = damage; kart.radius = weapon.blastRadius; kart.cruise = Mathf.Max(12, weapon.speed);
            kart.velocity = owner.transform.forward * kart.cruise; kart.armedAt = Time.time + .4f; kart.expiresAt = Time.time + 8;
            var t = kart.transform; t.rotation = Quaternion.LookRotation(owner.transform.forward);
            WorldArt.Box("Go-kart tub", t, new Vector3(0, .22f, 0), new Vector3(.8f, .22f, 1.25f), weapon.projectileColor);
            WorldArt.Box("Dynamite bundle", t, new Vector3(0, .45f, -.15f), new Vector3(.5f, .28f, .5f), new Color(.8f, .12f, .08f));
            WorldArt.Shape("Car-alarm beacon", t, PrimitiveType.Sphere, new Vector3(0, .66f, -.15f), Vector3.one * .16f, new Color(1, .1f, .05f), false, 0, .3f, 3);
            for (int x = -1; x <= 1; x += 2) for (int z = -1; z <= 1; z += 2)
            {
                var wheel = WorldArt.Cylinder("Kart wheel", t, new Vector3(x * .45f, .15f, z * .45f), .15f, .12f, WorldArt.Ink);
                wheel.transform.localRotation = Quaternion.Euler(0, 0, 90);
            }
        }
        /// <summary>Lawn-chair sentry: a nail gun zip-tied to a lawn chair, dropped beside the car. Two at a time.</summary>
        public static void DeploySentry(VehicleController owner, WeaponDefinition weapon, float damage)
        {
            sentries.RemoveAll(s => !s);
            var mine = sentries.FindAll(s => s.owner == owner);
            if (mine.Count >= 2) { var oldest = mine[0]; sentries.Remove(oldest); ExplosionSystem.Burst(oldest.transform.position + Vector3.up, weapon.projectileColor, 8, 2); Destroy(oldest.gameObject); }
            Vector3 at = owner.transform.position - owner.transform.forward * 3.4f + owner.transform.right * 1.6f;
            if (Physics.Raycast(at + Vector3.up * 6, Vector3.down, out var ground, 15, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) at = ground.point;
            var sentry = Create("Lawn-chair sentry", at, weapon.projectileColor, owner, null);
            sentry.mode = Mode.Sentry; sentry.damage = damage; sentry.expiresAt = Time.time + 12; sentry.nextShot = Time.time + .5f;
            var t = sentry.transform;
            Color webbing = new Color(.2f, .62f, .45f), aluminium = new Color(.78f, .8f, .8f);
            WorldArt.Box("Chair seat webbing", t, new Vector3(0, .45f, 0), new Vector3(.7f, .06f, .6f), webbing);
            var back = WorldArt.Box("Chair back webbing", t, new Vector3(0, .8f, -.3f), new Vector3(.7f, .7f, .06f), webbing); back.transform.localRotation = Quaternion.Euler(-12, 0, 0);
            for (int x = -1; x <= 1; x += 2) for (int z = -1; z <= 1; z += 2) WorldArt.Beam("Chair leg", t, new Vector3(x * .32f, 0, z * .26f), new Vector3(x * .32f, .45f, -z * .1f), .025f, aluminium);
            sentry.head = WorldArt.Group("Nail gun head", t, new Vector3(0, 1.25f, 0));
            WorldArt.Box("Zip-tied nail gun", sentry.head, new Vector3(0, 0, .15f), new Vector3(.18f, .22f, .55f), new Color(.95f, .55f, .1f));
            WorldArt.Box("Nail strip", sentry.head, new Vector3(0, -.14f, .05f), new Vector3(.06f, .12f, .3f), aluminium);
            sentries.Add(sentry);
        }

        static FieldOrdnance Create(string name, Vector3 position, Color color, VehicleController owner, PrimitiveType? shape)
        {
            var go = shape.HasValue ? GameObject.CreatePrimitive(shape.Value) : new GameObject();
            go.name = name; go.transform.position = position;
            if (shape.HasValue) go.transform.localScale = Vector3.one * .42f;
            if (GameManager.Instance != null && GameManager.Instance.World != null) go.transform.SetParent(GameManager.Instance.World.transform);
            var collider = go.GetComponent<Collider>(); if (collider) Destroy(collider);
            Material material = null;
            if (shape.HasValue)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                material.color = color; material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", color * 2);
                go.GetComponent<Renderer>().sharedMaterial = material;
            }
            var ordnance = go.AddComponent<FieldOrdnance>();
            ordnance.owner = owner; ordnance.source = owner ? owner.gameObject : null; ordnance.ownerFriendly = owner && owner.FriendlyToPlayer;
            ordnance.color = color; ordnance.ownMaterial = material;
            return ordnance;
        }
        bool Hostile(VehicleController vehicle) =>
            vehicle && vehicle != owner && vehicle.Damage != null && !vehicle.Damage.IsDead && vehicle.FriendlyToPlayer != ownerFriendly;
        VehicleController NearestHostile(Vector3 from, float range)
        {
            VehicleController best = null; float bestDistance = range * range;
            foreach (var vehicle in VehicleController.Active)
            {
                if (!Hostile(vehicle)) continue;
                float d = Vector3.ProjectOnPlane(vehicle.transform.position - from, Vector3.up).sqrMagnitude;
                if (d < bestDistance) { bestDistance = d; best = vehicle; }
            }
            return best;
        }

        void Update()
        {
            if (!GameManager.Instance || !GameManager.Instance.IsPlaying) return;
            switch (mode)
            {
                case Mode.Mine: UpdateMine(); break;
                case Mode.Fuse:
                    if (Time.time >= blinkAt) { blinkAt = Time.time + .15f; ExplosionSystem.Burst(transform.position + Vector3.up * .2f, new Color(1, .8f, .3f), 2, 1.5f); }
                    if (Time.time >= expiresAt) Detonate();
                    break;
                case Mode.Shell: case Mode.Bomblet: UpdateFlight(); break;
                case Mode.Vortex: UpdateVortex(); break;
                case Mode.Roller: UpdateRoller(); break;
                case Mode.Crawler: UpdateCrawler(); break;
                case Mode.Sentry: UpdateSentry(); break;
            }
        }
        void UpdateMine()
        {
            transform.Rotate(Vector3.up, 90 * Time.deltaTime);
            SpinBeacon();
            if (Time.time > armedAt)
                foreach (var vehicle in VehicleController.Active)
                    if (Hostile(vehicle) && (vehicle.transform.position - transform.position).sqrMagnitude < 12.25f) { Detonate(); return; }
            if (Time.time > expiresAt) Destroy(gameObject);
        }
        void UpdateFlight()
        {
            Vector3 step = velocity * Time.deltaTime;
            int count = Physics.SphereCastNonAlloc(transform.position, .22f, step.normalized, hits, step.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue; RaycastHit first = default;
            for (int i = 0; i < count; i++)
            {
                if (owner && hits[i].collider.GetComponentInParent<VehicleController>() == owner) continue;
                if (hits[i].distance < nearest) { nearest = hits[i].distance; first = hits[i]; }
            }
            if (nearest < float.MaxValue && Time.time > armedAt)
            {
                // Tennis-ball charges and bomblets skip off the ground once before they go off.
                bool ground = !first.collider.GetComponentInParent<VehicleController>();
                if (ground && bounces > 0 && first.distance > 0)
                {
                    bounces--; transform.position += step.normalized * nearest + first.normal * .25f;
                    velocity = Vector3.Reflect(velocity, first.normal) * .55f;
                    ExplosionSystem.Burst(first.point, new Color(.6f, .46f, .3f, .5f), 4, 1.5f);
                    return;
                }
                transform.position += step.normalized * nearest; Land(); return;
            }
            transform.position += step;
            velocity += Vector3.down * 32 * Time.deltaTime;
            transform.Rotate(Vector3.right, 420 * Time.deltaTime);
            if (Time.time > armedAt && velocity.y <= 1 && assistedTarget && assistedTarget.Damage != null && !assistedTarget.Damage.IsDead)
            {
                Vector3 targetDelta = assistedTarget.transform.position + Vector3.up * .7f - transform.position;
                Vector3 landingDelta = assistedLanding - transform.position;
                bool nearTarget = Vector3.ProjectOnPlane(targetDelta, Vector3.up).sqrMagnitude <= proximityRadius * proximityRadius && Mathf.Abs(targetDelta.y) < 6;
                bool nearLanding = Vector3.ProjectOnPlane(landingDelta, Vector3.up).sqrMagnitude <= proximityRadius * proximityRadius && Mathf.Abs(landingDelta.y) < 5;
                if (nearTarget || nearLanding) { Land(); return; }
            }
            if (Time.time > expiresAt) Land();
        }
        void Land()
        {
            if (payload != Payload.Vortex) { Detonate(); return; }
            // The shop-vac lands, spins up and drags nearby hostiles into a heap before it blows.
            mode = Mode.Vortex; velocity = Vector3.zero; expiresAt = Time.time + 1.8f;
            transform.rotation = Quaternion.identity;
            if (Physics.Raycast(transform.position + Vector3.up * 3, Vector3.down, out var ground, 8, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) transform.position = ground.point + Vector3.up * .3f;
        }
        void UpdateVortex()
        {
            transform.Rotate(Vector3.up, 900 * Time.deltaTime);
            const float reach = 16;
            foreach (var vehicle in VehicleController.Active)
            {
                if (!Hostile(vehicle) || !vehicle.Body || vehicle.Body.isKinematic) continue;
                Vector3 toward = Vector3.ProjectOnPlane(transform.position - vehicle.transform.position, Vector3.up);
                float distance = toward.magnitude;
                if (distance > reach || distance < .5f) continue;
                Vector3 inward = toward / distance, swirl = Vector3.Cross(Vector3.up, inward);
                float pull = Mathf.Lerp(34, 10, distance / reach);
                vehicle.Body.AddForce((inward * pull + swirl * 9) * Time.deltaTime, ForceMode.VelocityChange);
            }
            if (Time.time >= blinkAt)
            {
                blinkAt = Time.time + .04f;
                float angle = Random.value * Mathf.PI * 2, at = Random.Range(4f, reach);
                ExplosionSystem.Burst(transform.position + new Vector3(Mathf.Cos(angle) * at, .4f, Mathf.Sin(angle) * at), new Color(.62f, .52f, .9f, .55f), 2, 1);
                ExplosionSystem.Burst(transform.position + Vector3.up * .5f, color, 2, 2);
            }
            if (Time.time >= expiresAt) Detonate();
        }
        void UpdateRoller()
        {
            Vector3 flat = Vector3.ProjectOnPlane(velocity, Vector3.up);
            float speed = flat.magnitude, dt = Time.deltaTime;
            Vector3 direction = speed > .1f ? flat / speed : transform.forward;
            if (speed > .1f && Blocked(direction, speed * dt + .42f, .48f, out RaycastHit wall))
            {
                var prop = wall.collider.GetComponentInParent<DestructionSystem>();
                if (prop && !prop.IsDestroyed && prop.Size < 3.5f) prop.SmashFromVehicle(wall.point, source, flat * .7f);
                else if (wall.normal.y < .6f) { flat = Vector3.Reflect(flat, Vector3.ProjectOnPlane(wall.normal, Vector3.up).normalized) * .75f; velocity = flat + Vector3.up * velocity.y; ExplosionSystem.Burst(wall.point, new Color(.8f, .8f, .9f), 5, 3); }
            }
            foreach (var vehicle in VehicleController.Active)
            {
                if (!Hostile(vehicle) || struck.Contains(vehicle)) continue;
                Vector3 delta = vehicle.transform.position - transform.position;
                if (Vector3.ProjectOnPlane(delta, Vector3.up).sqrMagnitude > BowlingHitRadius * BowlingHitRadius || Mathf.Abs(delta.y) > 3f) continue;
                struck.Add(vehicle);
                vehicle.Damage.ApplyDamage(damage, transform.position, source);
                if (vehicle.Body && !vehicle.Body.isKinematic) vehicle.Body.AddForce(direction * 20 + Vector3.up * 5, ForceMode.VelocityChange);
                ExplosionSystem.Burst(transform.position + Vector3.up * .5f, new Color(1, .75f, .3f), 14, 5);
                velocity *= .82f;
                if (owner && owner.IsPlayer) CameraController.Instance?.Shake(.12f);
            }
            GroundFollow(.54f, true);
            velocity *= 1 - .12f * dt;
            transform.Rotate(Vector3.Cross(Vector3.up, direction), speed * dt / .54f * Mathf.Rad2Deg, Space.World);
            if (Time.time >= expiresAt || speed < 2) { ExplosionSystem.Burst(transform.position, new Color(.5f, .45f, .4f, .6f), 8, 2); Destroy(gameObject); }
        }
        void UpdateCrawler()
        {
            if (Time.time >= retargetAt) { retargetAt = Time.time + .4f; quarry = NearestHostile(transform.position, 90); }
            Vector3 flat = Vector3.ProjectOnPlane(velocity, Vector3.up);
            Vector3 heading = flat.sqrMagnitude > .01f ? flat.normalized : transform.forward;
            Vector3 desired = quarry ? Vector3.ProjectOnPlane(quarry.transform.position - transform.position, Vector3.up).normalized : heading;
            // Slide along walls and buildings instead of butting into them.
            if (Blocked(desired, 4, .45f, out RaycastHit wall) && wall.normal.y < .6f && !wall.collider.GetComponentInParent<DestructionSystem>())
            {
                Vector3 along = Vector3.Cross(Vector3.up, Vector3.ProjectOnPlane(wall.normal, Vector3.up).normalized);
                if (Vector3.Dot(along, desired) < 0) along = -along;
                desired = along;
            }
            heading = Vector3.RotateTowards(heading, desired, 220 * Mathf.Deg2Rad * Time.deltaTime, 0);
            velocity = heading * cruise + Vector3.up * velocity.y;
            GroundFollow(.05f, false);
            transform.rotation = Quaternion.LookRotation(heading);
            if (Time.time >= blinkAt) { blinkAt = Time.time + .2f; ExplosionSystem.Burst(transform.position + Vector3.up * .7f, new Color(1, .15f, .05f), 2, 1); }
            if (Time.time > armedAt)
                foreach (var vehicle in VehicleController.Active)
                    if (Hostile(vehicle) && (vehicle.transform.position - transform.position).sqrMagnitude < 2.9f * 2.9f) { Detonate(); return; }
            if (Time.time >= expiresAt) Detonate();
        }
        void UpdateSentry()
        {
            if (!owner || Time.time >= expiresAt)
            {
                ExplosionSystem.Burst(transform.position + Vector3.up, new Color(.6f, .6f, .55f, .6f), 10, 2);
                Destroy(gameObject); return;
            }
            if (Time.time < nextShot) return;
            nextShot = Time.time + .14f;
            Vector3 muzzle = head.position + head.forward * .45f;
            VehicleController target = null; float best = 42 * 42;
            foreach (var vehicle in VehicleController.Active)
            {
                if (!Hostile(vehicle)) continue;
                Vector3 delta = vehicle.transform.position + Vector3.up * .8f - muzzle;
                if (delta.sqrMagnitude >= best) continue;
                if (Physics.Raycast(muzzle, delta.normalized, out var sight, delta.magnitude + 1, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) &&
                    sight.collider.GetComponentInParent<VehicleController>() != vehicle) continue;
                best = delta.sqrMagnitude; target = vehicle;
            }
            if (!target) { head.Rotate(Vector3.up, 40 * Time.deltaTime * 7, Space.World); return; }
            Vector3 aim = target.transform.position + Vector3.up * .8f + (target.Body ? target.Body.linearVelocity * Mathf.Sqrt(best) / 90f : Vector3.zero) - muzzle;
            head.rotation = Quaternion.LookRotation(aim);
            aim = Quaternion.AngleAxis(Random.Range(-2.5f, 2.5f), Vector3.up) * aim;
            ProjectileSystem.Fire(muzzle, aim, 90, damage, 0, source, color, ExplosionKind.Ammunition, .6f, SentryRound);
            ExplosionSystem.Burst(muzzle, color, 2, 1);
            AudioManager.Instance?.PlayShot(muzzle, WeaponRules.Find("riveter"));
        }
        /// <summary>First solid, non-vehicle obstacle along <paramref name="direction"/> at wheel height.</summary>
        bool Blocked(Vector3 direction, float distance, float width, out RaycastHit obstacle)
        {
            obstacle = default;
            int count = Physics.SphereCastNonAlloc(transform.position + Vector3.up * .45f, width, direction, hits, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i];
                if (!hit.collider || hit.distance <= 0 || hit.collider.GetComponentInParent<VehicleController>()) continue;
                if (hit.normal.y > .6f) continue;
                if (hit.distance < nearest) { nearest = hit.distance; obstacle = hit; }
            }
            return nearest < float.MaxValue;
        }
        /// <summary>Moves by the current velocity and keeps the ordnance on the ground; rollers speed up downhill.</summary>
        void GroundFollow(float lift, bool rolls)
        {
            float dt = Time.deltaTime;
            Vector3 p = transform.position + Vector3.ProjectOnPlane(velocity, Vector3.up) * dt;
            float groundY = p.y - 50; Vector3 normal = Vector3.up;
            int count = Physics.RaycastNonAlloc(p + Vector3.up * 3, Vector3.down, hits, 12, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (!hits[i].collider || hits[i].collider.GetComponentInParent<VehicleController>() || hits[i].normal.y < .5f) continue;
                if (hits[i].distance < nearest) { nearest = hits[i].distance; groundY = hits[i].point.y; normal = hits[i].normal; }
            }
            float rest = groundY + lift;
            if (p.y > rest + .5f) { velocity += Vector3.down * 25 * dt; p.y += velocity.y * dt; }
            else
            {
                p.y = rest; velocity.y = 0;
                if (rolls) velocity += Vector3.ProjectOnPlane(Physics.gravity, normal) * dt;
            }
            transform.position = p;
        }
        void Detonate()
        {
            Vector3 at = transform.position;
            var kind = mode == Mode.Mine ? ExplosionKind.Ammunition : mode == Mode.Crawler ? ExplosionKind.Gasoline : mode == Mode.Vortex ? ExplosionKind.Massive : ExplosionKind.Grenade;
            ExplosionSystem.Detonate(at, radius, damage, source, kind, falloffPower: mode == Mode.Fuse ? 2f : 0f);
            if (payload == Payload.FireRing && source)
            {
                // Sprinkler firebomb: a spinning ring of burning fuel sprays out from the landing point.
                // Start at car-body height above the ground so the flame jets reach cars before terrain.
                Vector3 fireOrigin = at + Vector3.up * 1.05f;
                int groundCount = Physics.RaycastNonAlloc(at + Vector3.up * 3f, Vector3.down, hits, 10f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                float groundDistance = float.MaxValue;
                for (int h = 0; h < groundCount; h++)
                {
                    var hit = hits[h];
                    if (!hit.collider || hit.normal.y < .5f || hit.collider.GetComponentInParent<VehicleController>()) continue;
                    if (hit.distance < groundDistance) { groundDistance = hit.distance; fireOrigin = hit.point + Vector3.up * 1.05f; }
                }
                for (int i = 0; i < 16; i++)
                {
                    float angle = (i + Random.value * .4f) / 16f * Mathf.PI * 2;
                    var burn = new ShotFx { effect = ShotEffect.Burn, power = 12, duration = 3.5f, detached = true, flame = true, castRadius = .5f };
                    ProjectileSystem.Fire(fireOrigin, new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)), 22, 5, 0, source, new Color(1, .5f, .1f), ExplosionKind.Ammunition, .75f, burn);
                }
            }
            sentries.Remove(this);
            Destroy(gameObject);
        }
        void OnDestroy() { sentries.Remove(this); if (ownMaterial) Destroy(ownMaterial); if (beaconMaterial) Destroy(beaconMaterial); }
    }
}
