using UnityEngine;

namespace MadeInArizona
{
    /// <summary>
    /// What each tyre leaves behind, from the car's wheelspin, side slip and the ground under it:
    /// - Pavement: burnouts and slides smoke the tyres (pale, lingering smoke rather than dust) and lay dark rubber. A
    ///   tyre spinning in place keeps stacking rubber on one patch, and repeated passes darken a spot further.
    /// - Loose ground: spinning and sliding tyres throw pebbles, grit and clods; fast rolling tyres flick the odd stone.
    ///   Slides cut ruts, and sand and mud keep faint tracks behind the rear tyres even without slip.
    /// Effects run only near the camera. Tuned under Dev Tuning > Dirt &amp; Sky.
    /// </summary>
    public sealed class TireEffects : MonoBehaviour
    {
        sealed class Track { public bool active; public Vector3 left, right, center; public float along, strength, patchAt; }
        VehicleController vehicle;
        readonly Track[] tracks = { new Track(), new Track(), new Track(), new Track() };
        readonly float[] smokeCarry = new float[4], gritCarry = new float[4];
        const float TyreWidth = .27f, MinSegment = .3f, EffectRange = 90;
        static readonly Color Rubber = new Color(.14f, .135f, .13f);

        public void Bind(VehicleController owner)
        {
            vehicle = owner;
            for (int i = 0; i < 4; i++) { tracks[i].active = false; smokeCarry[i] = gritCarry[i] = 0; }
        }

        void FixedUpdate()
        {
            var game = GameManager.Instance;
            if (!vehicle || vehicle.Body == null || vehicle.Damage == null || vehicle.Damage.IsDead || vehicle.Body.isKinematic || !game || !game.IsPlaying) { EndAll(); return; }
            if (CameraController.HasFocus && (vehicle.Body.position - CameraController.FocusPoint).sqrMagnitude > EffectRange * EffectRange) { EndAll(); return; }
            var tuning = DevTuning.Current;
            int quality = game.Save != null ? game.Save.settings.quality : 2;
            float scale = quality == 0 ? .45f : quality == 1 ? .7f : quality == 2 ? 1 : 1.35f;
            SurfaceKind surface = vehicle.Surface;
            bool paved = surface == SurfaceKind.Asphalt || surface == SurfaceKind.Oil;
            Vector3 velocity = vehicle.Body.linearVelocity;
            float speed = new Vector2(velocity.x, velocity.z).magnitude;
            for (int i = 0; i < 4; i++)
            {
                if (!vehicle.WheelContact(i, out Vector3 point, out Vector3 normal)) { End(i); continue; }
                // Centre the effects under the tyre itself rather than the suspension probe just inboard of it.
                var wheel = vehicle.Wheel(i);
                if (wheel) point = wheel.position - normal * Vector3.Dot(wheel.position - point, normal);
                bool driven = vehicle.Driven(i), rear = i >= 2;
                float slip = Mathf.Max(driven ? vehicle.WheelSpin : 0, vehicle.SideSlip * (rear ? 1 : .6f));
                Vector3 pointVelocity = Vector3.ProjectOnPlane(vehicle.Body.GetPointVelocity(point), normal);
                if (surface == SurfaceKind.Water) { End(i); continue; }
                Marks(i, point, normal, pointVelocity, slip, driven, rear, surface, paved, tuning);
                if (paved) Smoke(i, point, normal, pointVelocity, slip, driven, tuning, scale);
                else Grit(i, point, normal, pointVelocity, slip, driven, rear, surface, speed, tuning, scale);
            }
        }

        void Marks(int i, Vector3 point, Vector3 normal, Vector3 pointVelocity, float slip, bool driven, bool rear, SurfaceKind surface, bool paved, DevTuning tuning)
        {
            var track = tracks[i];
            float strength;
            if (paved) strength = slip > .18f ? Mathf.Lerp(.3f, .75f, Mathf.InverseLerp(.18f, 1, slip)) : 0;
            else
            {
                bool soft = surface == SurfaceKind.Sand || surface == SurfaceKind.Mud;
                strength = Mathf.Max(soft && rear && pointVelocity.sqrMagnitude > 1 ? .26f : 0, slip > .2f ? Mathf.Lerp(.3f, .6f, slip) : 0);
            }
            if (strength <= 0 || tuning.skidMarks <= 0) { End(i); return; }
            Vector3 heading = Vector3.ProjectOnPlane(vehicle.transform.forward, normal).normalized;
            Vector3 direction = pointVelocity.sqrMagnitude > .3f ? pointVelocity.normalized : heading;
            // A tyre sliding sideways scrubs a patch as long as its contact, not just as wide as its tread.
            float sideways = 1 - Mathf.Abs(Vector3.Dot(direction, heading));
            Vector3 across = Vector3.Cross(normal, direction).normalized * Mathf.Lerp(TyreWidth, .5f, sideways) * .5f;
            Vector3 center = point + normal * .03f;
            Color tint = paved ? Rubber : SoilTint(surface);
            if (!track.active || (center - track.center).sqrMagnitude > 9)
            {
                track.active = true; track.left = center - across; track.right = center + across; track.center = center;
                track.strength = strength; track.along = 0;
                return;
            }
            float moved = Vector3.Distance(center, track.center);
            if (moved >= Mathf.Clamp(pointVelocity.magnitude * .045f, MinSegment, 1.2f))
            {
                TireMarks.Segment(track.left, track.right, center - across, center + across, tint, track.strength, strength, track.along, track.along + moved, !paved);
                track.left = center - across; track.right = center + across; track.center = center;
                track.strength = strength; track.along += moved;
            }
            else if (paved && driven && vehicle.WheelSpin > .45f && moved < .15f && Time.time >= track.patchAt)
            {
                // Burnout on the spot: rubber piles up on one patch, darker with every layer.
                track.patchAt = Time.time + .2f;
                Vector3 along = Quaternion.AngleAxis(Random.Range(-6f, 6f), normal) * heading * .3f;
                Vector3 side = Vector3.Cross(normal, along).normalized * TyreWidth * .55f;
                float layer = .14f * vehicle.WheelSpin;
                TireMarks.Segment(center - along - side, center - along + side, center + along - side, center + along + side, Rubber, layer, layer, 0, .6f, false);
            }
        }

        void Smoke(int i, Vector3 point, Vector3 normal, Vector3 pointVelocity, float slip, bool driven, DevTuning tuning, float scale)
        {
            if (tuning.tireSmoke <= 0 || slip < .25f) { smokeCarry[i] = 0; return; }
            smokeCarry[i] = Mathf.Min(smokeCarry[i] + (slip - .2f) * 34 * tuning.tireSmoke * scale * Time.fixedDeltaTime, 4);
            Vector3 back = -vehicle.transform.forward;
            float spin = driven ? vehicle.WheelSpin : 0;
            while (smokeCarry[i] >= 1)
            {
                smokeCarry[i] -= 1;
                // Wheelspin blows smoke back off the tread; a slide leaves it hanging along the line the tyre scrubbed.
                Vector3 drift = pointVelocity * .25f + back * spin * Random.Range(2f, 4.5f) + Random.insideUnitSphere * .8f + normal * Random.Range(.4f, 1.1f);
                Vector3 at = point + normal * .3f + back * .25f + Random.insideUnitSphere * .15f;
                ExplosionSystem.TireSmoke(at, drift, Random.Range(.8f, 1.3f) * (.7f + slip * .5f), Random.Range(2.2f, 3.6f), Mathf.Lerp(.3f, .75f, slip));
            }
        }

        void Grit(int i, Vector3 point, Vector3 normal, Vector3 pointVelocity, float slip, bool driven, bool rear, SurfaceKind surface, float speed, DevTuning tuning, float scale)
        {
            if (tuning.gravelSpray <= 0) { gritCarry[i] = 0; return; }
            float loose, chunks, grain;
            switch (surface)
            {
                case SurfaceKind.Gravel: loose = 1.3f; chunks = .12f; grain = 1; break;
                case SurfaceKind.Sand: loose = .9f; chunks = .03f; grain = .6f; break;
                case SurfaceKind.Rocks: loose = .5f; chunks = .22f; grain = 1.1f; break;
                case SurfaceKind.Mud: loose = .6f; chunks = .5f; grain = 1.2f; break;
                case SurfaceKind.Debris: loose = .7f; chunks = .3f; grain = 1; break;
                default: loose = .8f; chunks = .25f; grain = 1; break;
            }
            // Rolling tyres flick the odd stone at speed; spinning or sliding ones throw a spray.
            float rolling = Mathf.Max(0, speed - 5) * loose * (rear ? .5f : .25f);
            float spray = slip > .12f ? slip * 55 * loose : 0;
            gritCarry[i] = Mathf.Min(gritCarry[i] + (rolling + spray) * tuning.gravelSpray * scale * Time.fixedDeltaTime, 6);
            Vector3 forward = vehicle.transform.forward, side = vehicle.transform.right;
            float spin = driven ? vehicle.WheelSpin : 0;
            Vector3 slide = Vector3.Dot(pointVelocity, side) * side;
            while (gritCarry[i] >= 1)
            {
                gritCarry[i] -= 1;
                // Roost: the tread flings material back and up; a sideways slide plows it out ahead of the slide.
                Vector3 throwVelocity = pointVelocity * Random.Range(.3f, .7f) - forward * spin * Random.Range(3f, 8.5f) + slide * Random.Range(.3f, .9f)
                    + normal * Random.Range(1.5f, 4.5f) * (.55f + Mathf.Max(spin, slip) * .7f) + Random.insideUnitSphere * 1.2f;
                Vector3 at = point + normal * .08f - forward * .3f + side * Random.Range(-.12f, .12f);
                bool chunk = Random.value < chunks;
                float size = chunk ? Random.Range(.12f, .22f) : Random.Range(.045f, .1f) * grain;
                TireMarks.Throw(at, throwVelocity, size, GritColor(surface, chunk));
            }
        }

        /// <summary>Multiplier for ruts at full strength, by ground.</summary>
        static Color SoilTint(SurfaceKind surface)
        {
            switch (surface)
            {
                case SurfaceKind.Sand: return new Color(.74f, .67f, .58f);
                case SurfaceKind.Gravel: return new Color(.64f, .61f, .57f);
                case SurfaceKind.Rocks: return new Color(.68f, .58f, .5f);
                case SurfaceKind.Mud: return new Color(.5f, .43f, .36f);
                default: return new Color(.66f, .58f, .5f);
            }
        }
        /// <summary>Linear albedo of thrown material; clods of soil are darker than the stones.</summary>
        static Color GritColor(SurfaceKind surface, bool chunk)
        {
            float v = Random.value;
            switch (surface)
            {
                case SurfaceKind.Gravel: return chunk ? Color.Lerp(new Color(.2f, .17f, .13f), new Color(.3f, .25f, .19f), v) : Color.Lerp(new Color(.2f, .19f, .17f), new Color(.38f, .35f, .31f), v);
                case SurfaceKind.Sand: return Color.Lerp(new Color(.42f, .32f, .2f), new Color(.56f, .44f, .29f), v);
                case SurfaceKind.Rocks: return Color.Lerp(new Color(.26f, .15f, .1f), new Color(.4f, .26f, .18f), v);
                case SurfaceKind.Mud: return Color.Lerp(new Color(.06f, .045f, .03f), new Color(.12f, .09f, .06f), v);
                case SurfaceKind.Debris: return Color.Lerp(new Color(.16f, .15f, .14f), new Color(.3f, .28f, .26f), v);
                default: return chunk ? Color.Lerp(new Color(.18f, .12f, .07f), new Color(.28f, .19f, .12f), v) : Color.Lerp(new Color(.22f, .17f, .12f), new Color(.36f, .28f, .2f), v);
            }
        }

        void End(int i) { tracks[i].active = false; }
        void EndAll() { for (int i = 0; i < 4; i++) tracks[i].active = false; }
    }
}
