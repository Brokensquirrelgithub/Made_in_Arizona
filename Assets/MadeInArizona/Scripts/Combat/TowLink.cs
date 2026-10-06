using System.Collections.Generic;
using UnityEngine;

namespace MadeInArizona
{
    /// <summary>
    /// The tow-hook harpoon's cable. It holds a hooked car for several seconds as a slack rope: it reels in to a short
    /// tow, then only stops the car moving further away, so a driver who swings round drags it out wide on the line.
    /// The towed car's engine is stalled and its tyres skid sideways, and slamming it into anything solid (rock, wall,
    /// building, another car) at speed wrecks it, credited to the driver holding the cable. On the host the link
    /// simulates; co-op guests draw the replicated cable only (<see cref="ShowNetwork"/>).
    /// </summary>
    public sealed class TowLink : MonoBehaviour
    {
        /// <summary>Share of normal sideways tyre grip a towed car keeps.</summary>
        public const float TowedGrip = .18f;
        /// <summary>Seconds the hook holds before it slips free.</summary>
        public const float HoldTime = 5.5f;
        const float MinLength = 7, ReelSpeed = 24, Stiffness = 30, SnapDistance = 70;
        /// <summary>Closing speed (m/s) into something solid that starts to hurt a towed car; about 16 m/s wrecks it.</summary>
        const float WhipMinImpact = 6.5f, WhipLethalImpact = 16f;
        static readonly List<TowLink> links = new List<TowLink>();
        static Material cableMaterial;
        VehicleController owner, target;
        LineRenderer cable;
        float until, length, reelTo, lastWhipAt, networkSeenAt;
        bool network;
        Color color = new Color(.9f, .75f, .4f);

        /// <summary>Hooks <paramref name="target"/> to <paramref name="owner"/>. Each driver holds one cable; a new hook replaces it.</summary>
        public static void Attach(VehicleController owner, VehicleController target, Color color, float hold = HoldTime)
        {
            if (!owner || !target || owner == target || !target.Body || target.Body.isKinematic || target.Damage == null || target.Damage.IsDead) return;
            if (owner.Towing && owner.Towing.GetComponent<TowLink>() is TowLink previous) previous.Release();
            if (target.GetComponent<TowLink>() is TowLink existing) existing.Release();
            var link = target.gameObject.AddComponent<TowLink>();
            link.owner = owner; link.target = target; link.color = color;
            float distance = Flat(target.transform.position - owner.transform.position).magnitude;
            link.length = Mathf.Max(MinLength, distance);
            // Reel in to a short tow, about a car and a half behind the bumper.
            link.reelTo = Mathf.Clamp(distance * .45f, MinLength, 12);
            link.until = Time.time + hold;
            owner.Towing = target; target.TowedBy = owner;
            VehicleAfflictions.For(target)?.Stall(hold);
            links.Add(link);
        }

        /// <summary>Co-op guests: draw the host's cable between two replicated cars, refreshed by each vehicle snapshot.</summary>
        public static void ShowNetwork(VehicleController owner, VehicleController target)
        {
            if (!owner || !target) return;
            var link = target.GetComponent<TowLink>();
            if (!link) { link = target.gameObject.AddComponent<TowLink>(); link.network = true; links.Add(link); }
            link.owner = owner; link.target = target; link.networkSeenAt = Time.unscaledTime;
        }

        void FixedUpdate()
        {
            if (network) return;
            var game = GameManager.Instance;
            if (!game || !game.IsPlaying) return;
            if (!owner || !target || owner.Damage == null || target.Damage == null || owner.Damage.IsDead || target.Damage.IsDead || Time.time > until || !owner.Body || !target.Body)
            { Release(); return; }
            length = Mathf.MoveTowards(length, reelTo, ReelSpeed * Time.fixedDeltaTime);
            Vector3 delta = Flat(target.Body.position - owner.Body.position);
            float distance = delta.magnitude;
            if (distance > SnapDistance) { Release(); return; }
            if (distance < .1f || distance <= length) return;
            Vector3 n = delta / distance;
            // A rope only pulls: cancel the hooked car's motion away from the driver (most of it lands on the lighter
            // towed car), and pull back any stretch. Its sideways swing survives, which is what whips it round.
            Vector3 relative = target.Body.linearVelocity - owner.Body.linearVelocity;
            float outward = Vector3.Dot(relative, n);
            float targetShare = Mathf.Clamp(owner.Body.mass / (owner.Body.mass + target.Body.mass), .65f, .92f);
            if (outward > 0)
            {
                target.Body.AddForce(-n * outward * targetShare, ForceMode.VelocityChange);
                owner.Body.AddForce(n * outward * (1 - targetShare) * .5f, ForceMode.VelocityChange);
            }
            target.Body.AddForce(-n * Mathf.Min((distance - length) * Stiffness, 70), ForceMode.Acceleration);
        }

        void LateUpdate()
        {
            if (network && Time.unscaledTime - networkSeenAt > .35f) { Release(); return; }
            if (!owner || !target) { if (!network) Release(); return; }
            DrawCable();
        }

        /// <summary>
        /// A towed car hit something solid. <paramref name="impact"/> is the closing speed into it; past about 16 m/s
        /// (58 km/h) the towed car is wrecked, and a car it is slammed into takes most of the same hit.
        /// </summary>
        public static void WhipImpact(VehicleController car, VehicleDamage other, float impact, Vector3 point)
        {
            var link = car ? car.GetComponent<TowLink>() : null;
            if (!link || link.network || !link.owner || impact < WhipMinImpact || Time.time < link.lastWhipAt + .3f) return;
            // Reeled into the tow car's own bumper: an ordinary ram, not a whip (and never the driver's own damage).
            if (other && other.gameObject == link.owner.gameObject) return;
            link.lastWhipAt = Time.time;
            float share = Mathf.InverseLerp(WhipMinImpact, WhipLethalImpact, impact);
            float damage = car.Damage.MaxHealth * Mathf.Lerp(.3f, 1.25f, share);
            // Bosses and command vehicles shrug off most of it; a whip still hurts.
            var ai = car.GetComponent<EnemyAI>();
            if (ai && ai.Archetype == 7) damage *= .35f;
            var source = link.owner.gameObject;
            car.Damage.ApplyDamage(damage, point, source);
            if (other && other != car.Damage) other.ApplyDamage(damage * .7f, point, source);
            ExplosionSystem.Burst(point, new Color(1, .7f, .25f), 18, 6);
            ExplosionSystem.ScatterDebris(point, 6, 5, new Color(.42f, .4f, .38f), car.Body ? car.Body.linearVelocity * .3f : Vector3.zero);
            AudioManager.Instance?.PlayCrash(point, share);
            if (link.owner.IsPlayer) CameraController.Instance?.Shake(.12f + share * .25f);
        }

        void DrawCable()
        {
            if (!cable)
            {
                if (!cableMaterial)
                {
                    var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
                    cableMaterial = new Material(shader) { name = "Tow cable" };
                    cableMaterial.color = Color.white;
                }
                var go = new GameObject("Tow-hook cable"); go.transform.SetParent(transform, false);
                cable = go.AddComponent<LineRenderer>();
                cable.useWorldSpace = true; cable.positionCount = 12; cable.widthMultiplier = .07f; cable.numCapVertices = 2;
                cable.sharedMaterial = cableMaterial;
                cable.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; cable.receiveShadows = false;
            }
            Color c = Color.Lerp(color, new Color(.2f, .19f, .17f), .55f);
            cable.startColor = cable.endColor = c;
            // From the driver's tow point behind the car to the hook in the towed car; sags when slack.
            Vector3 a = owner.transform.position - owner.transform.forward * 1.9f + Vector3.up * .75f;
            Vector3 b = target.transform.position + Vector3.up * .8f;
            float span = Vector3.Distance(a, b);
            float slack = network ? .4f : Mathf.Clamp01((length - Flat(target.transform.position - owner.transform.position).magnitude) / 6);
            float sag = Mathf.Lerp(.08f, .9f, slack) * Mathf.Clamp01(span / 6);
            for (int i = 0; i < cable.positionCount; i++)
            {
                float t = i / (float)(cable.positionCount - 1);
                cable.SetPosition(i, Vector3.Lerp(a, b, t) + Vector3.down * sag * 4 * t * (1 - t));
            }
        }

        void Release()
        {
            if (!network)
            {
                if (owner && owner.Towing == target) owner.Towing = null;
                if (target && target.TowedBy == owner) target.TowedBy = null;
            }
            links.Remove(this);
            Destroy(this);
        }
        void OnDestroy()
        {
            links.Remove(this);
            if (cable) Destroy(cable.gameObject);
            if (!network && target && target.TowedBy == owner) target.TowedBy = null;
            if (!network && owner && owner.Towing == target) owner.Towing = null;
        }
        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }
    }
}
