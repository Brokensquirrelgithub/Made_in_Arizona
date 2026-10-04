using System;
using System.Collections;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace MadeInArizona
{
    /// <summary>
    /// Opt-in chassis tilt probe (-miaSmokeTest -miaTiltProbe). Drives the player through situations that roll the car
    /// and measures how quickly all four wheels are back on the ground once the cause is gone.
    /// </summary>
    public static class TiltProbe
    {
        const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        const float AirGap = .3f, Settle = 5f;

        public static IEnumerator Run(Keyboard keyboard, Action<string, bool> check)
        {
            var game = GameManager.Instance; var player = game.Player;
            foreach (var ai in UnityEngine.Object.FindObjectsByType<EnemyAI>(FindObjectsSortMode.None)) ai.enabled = false;
            Vector3 o = new Vector3(40, 120, 40);
            var arena = new GameObject("Tilt probe arena");
            Box(arena, "Floor", o + new Vector3(0, -.5f, 20), new Vector3(90, 1, 170), Quaternion.identity);
            float track = player.Stats.trackWidth * .44f;
            var none = new KeyboardState(); var w = new KeyboardState(Key.W);

            // Kicker ramp under the left wheels only, taken at speed. Recovery counts from leaving the ramp.
            var kicker = Box(arena, "Left-wheel kicker", Vector3.zero, new Vector3(1.4f, .3f, 5), Quaternion.Euler(-13, 0, 0));
            kicker.transform.position = o + new Vector3(-track, .45f, -8);
            yield return Drive(player, keyboard, o + new Vector3(0, 0, -30), 17, "kicker", check, () => player.transform.position.z > o.z - 5, (0f, w));
            kicker.SetActive(false);

            // Drift: a hard sliding turn, then straighten out. Recovery counts from releasing drift.
            yield return Drive(player, keyboard, o + new Vector3(-20, 0, -40), 22, "drift", check, null,
                (0f, w), (.4f, new KeyboardState(Key.W, Key.D, Key.Space)), (1.6f, w));

            // Straddling a 6 m drop (left wheels over the edge), then steering fully onto the ledge.
            var ledge = Box(arena, "Ledge", o + new Vector3(10 - .25f, 3, 50), new Vector3(20, 6, 140), Quaternion.identity);
            yield return Drive(player, keyboard, o + new Vector3(0, 6.2f, -12), 0, "cliff-edge", check, null,
                (0f, none), (1f, new KeyboardState(Key.D)), (1.8f, w));
            ledge.SetActive(false);

            // Crossing a 20 degree side slope onto flat ground. Recovery counts from leaving the slope.
            var slope = Box(arena, "Side slope", o + new Vector3(0, -.5f, 0), new Vector3(14, 1, 30), Quaternion.Euler(0, 0, 20));
            yield return Drive(player, keyboard, o + new Vector3(4, 2.2f, -12), 12, "side-slope", check, () => player.transform.position.z > o.z + 15, (0f, w));
            slope.SetActive(false);

            // Full-width ramp jump at speed: tilt in the air and on landing.
            var jump = Box(arena, "Jump ramp", Vector3.zero, new Vector3(6, .3f, 7), Quaternion.Euler(-18, 0, 0));
            jump.transform.position = o + new Vector3(10, .9f, -8);
            yield return Drive(player, keyboard, o + new Vector3(10, 0, -32), 22, "jump", check, () => player.transform.position.z > o.z - 4, (0f, w));
            jump.SetActive(false);

            // Beached on a ridge: a 0.9 m block narrower than the track under the belly, then throttle.
            var ridge = Box(arena, "Ridge", o + new Vector3(0, .45f, 0), new Vector3(1, .9f, 8), Quaternion.identity);
            yield return Drive(player, keyboard, o + new Vector3(0, .9f, 1), 0, "ridge", check, null, (0f, none), (.6f, w));
            ridge.SetActive(false);

            // Crawling diagonally off a 1.2 m ledge, so the belly catches the lip with one wheel still on top.
            var lip = Box(arena, "Diagonal lip", o + new Vector3(-8, .6f, -8), new Vector3(20, 1.2f, 20), Quaternion.Euler(0, 45, 0));
            yield return Drive(player, keyboard, o + new Vector3(-4, 1.5f, -8), 3, "diagonal-lip", check, () => player.transform.position.z > o.z + 2.5f, (0f, w));
            lip.SetActive(false);

            // A sideways shove (mine, ram or air-cannon style) while driving. Recovery counts from the shove.
            bool shoved = false;
            yield return Drive(player, keyboard, o + new Vector3(-20, 0, -40), 14, "shove", check, () => shoved,
                t => { if (!shoved && t >= .8f) { shoved = true; player.Body.AddForce(Vector3.right * 11 + Vector3.up * 4, ForceMode.VelocityChange); } }, (0f, w));

            // Half-damaged suspension on flat ground: the car should still sit on four wheels.
            var damage = player.Damage; var suspension = typeof(VehicleDamage).GetProperty("Suspension");
            yield return Drive(player, keyboard, o + new Vector3(-20, 0, -40), 10, "damaged-suspension", check, null,
                t => suspension.GetSetMethod(true).Invoke(damage, new object[] { .5f }), (0f, w), (.5f, w));
            player.Repair(10000);

            UnityEngine.Object.Destroy(arena);
            InputSystem.QueueStateEvent(keyboard, none);
        }

        /// <summary>
        /// Places the car heading +z at <paramref name="speed"/> and plays the timed key script. Recovery starts at the last
        /// script step, or when <paramref name="causeGone"/> first becomes true, and ends once the visual roll is under 5°
        /// with no wheel hanging off the ground.
        /// </summary>
        static IEnumerator Drive(VehicleController player, Keyboard keyboard, Vector3 start, float speed, string name, Action<string, bool> check,
            Func<bool> causeGone, params (float time, KeyboardState keys)[] script) => Drive(player, keyboard, start, speed, name, check, causeGone, null, script);
        static IEnumerator Drive(VehicleController player, Keyboard keyboard, Vector3 start, float speed, string name, Action<string, bool> check,
            Func<bool> causeGone, Action<float> hook, params (float time, KeyboardState keys)[] script)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            player.Body.position = start; player.Body.rotation = Quaternion.identity; player.transform.SetPositionAndRotation(start, Quaternion.identity);
            player.Body.linearVelocity = Vector3.zero; player.Body.angularVelocity = Vector3.zero; player.Repair(10000);
            Physics.SyncTransforms();
            yield return new WaitForSeconds(.4f);
            player.Body.linearVelocity = Vector3.forward * speed;
            var wheels = (Transform[])typeof(VehicleController).GetField("wheels", Private).GetValue(player);
            var trace = new StringBuilder();
            float t = 0, maxRoll = 0, recoverFrom = -1, recoveredAt = -1; int step = 0;
            float end = script[script.Length - 1].time + 3.5f;
            while (t < end)
            {
                if (step < script.Length && t >= script[step].time) { InputSystem.QueueStateEvent(keyboard, script[step].keys); step++; }
                hook?.Invoke(t);
                yield return new WaitForFixedUpdate();
                t += Time.fixedDeltaTime;
                float roll = Mathf.DeltaAngle(0, player.Visual.localEulerAngles.z);
                maxRoll = Mathf.Max(maxRoll, Mathf.Abs(roll));
                float gapL = Mathf.Max(Gap(wheels[0].position, player.Body), Gap(wheels[2].position, player.Body));
                float gapR = Mathf.Max(Gap(wheels[1].position, player.Body), Gap(wheels[3].position, player.Body));
                if (recoverFrom < 0 && (causeGone != null ? causeGone() : step >= script.Length)) recoverFrom = t;
                bool level = Mathf.Abs(roll) < Settle && gapL < AirGap && gapR < AirGap;
                // Airtime is not hanging: only count wheels up while the car is on the ground.
                if (!player.Grounded && !player.Beached) level = true;
                if (recoverFrom >= 0) { if (!level) recoveredAt = -1; else if (recoveredAt < 0) recoveredAt = t; }
                if (Mathf.RoundToInt(t / Time.fixedDeltaTime) % 6 == 0)
                    trace.Append($" t={t:F1} roll={roll:F0} gapL={gapL:F2} gapR={gapR:F2} y={player.Body.position.y - start.y:F2} x={player.Body.position.x - start.x:F1} z={player.Body.position.z - start.z:F0} g={(player.Grounded ? 1 : 0)}\n");
            }
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            float recovery = recoverFrom < 0 ? 99 : recoveredAt < 0 ? 99 : recoveredAt - recoverFrom;
            Debug.Log($"MIA_TILT {name}: maxVisualRoll={maxRoll:F1} recovery={recovery:F2}s (from t={recoverFrom:F2})\n{trace}");
            // Driving off a ridge takes a moment; a sideways shove keeps its slide lean briefly.
            float allowed = name == "ridge" ? 2f : name == "shove" ? 1f : .5f;
            check($"{name}: back on four wheels within {allowed:F1} s", recovery <= allowed);
        }

        /// <summary>
        /// Free driving in the generated world (-miaSmokeTest -miaTiltWorld): logs every stretch where a wheel hangs off
        /// the ground while the car is grounded, with what lies under each wheel, to find real-world causes.
        /// </summary>
        public static IEnumerator World(Keyboard keyboard, Action<string, bool> check)
        {
            var game = GameManager.Instance;
            game.StartCampaign(173, 1600);
            yield return new WaitUntil(() => game.State == GameState.Playing);
            yield return new WaitForSecondsRealtime(2);
            var player = game.Player;
            foreach (var ai in UnityEngine.Object.FindObjectsByType<EnemyAI>(FindObjectsSortMode.None)) ai.enabled = false;
            var wheels = (Transform[])typeof(VehicleController).GetField("wheels", Private).GetValue(player);
            var legs = new[] { new KeyboardState(Key.W, Key.LeftShift), new KeyboardState(Key.W, Key.D), new KeyboardState(Key.D, Key.Space), new KeyboardState(Key.D),
                new KeyboardState(Key.W, Key.A, Key.LeftShift), new KeyboardState(Key.A), new KeyboardState(Key.S, Key.A, Key.Space), new KeyboardState(Key.W) };
            var log = new StringBuilder();
            float t = 0, hang = 0, total = 0, longest = 0, maxRoll = 0; string cause = ""; int events = 0, leg = -1;
            while (t < 30)
            {
                int want = (int)(t / 4) % legs.Length;
                if (want != leg) { leg = want; InputSystem.QueueStateEvent(keyboard, legs[leg]); }
                if (player.Damage.Health < player.Damage.MaxHealth * .5f) player.Repair(10000);
                yield return new WaitForFixedUpdate();
                t += Time.fixedDeltaTime;
                float roll = Mathf.DeltaAngle(0, player.Visual.localEulerAngles.z);
                bool anyUp = false; foreach (var wheel in wheels) if (wheel && Gap(wheel.position, player.Body) > AirGap) anyUp = true;
                if (anyUp && player.Grounded)
                {
                    if (hang == 0) { cause = UnderWheels(wheels, player.Body); maxRoll = 0; }
                    hang += Time.fixedDeltaTime; maxRoll = Mathf.Max(maxRoll, Mathf.Abs(roll));
                }
                else if (hang > 0)
                {
                    total += hang; longest = Mathf.Max(longest, hang);
                    if (hang > .25f) { events++; log.Append($" t={t - hang:F1} hang={hang:F2}s maxRoll={maxRoll:F0} speed={player.Body.linearVelocity.magnitude:F0} under: {cause}\n"); }
                    hang = 0;
                }
            }
            // Aggressive driving on two maps (nitro, drifts, hard turns): log every stretch the body stays tilted past 20°
            // and every time the car will not move with a direction held.
            GeneratedWorld world;
            var pitchF = typeof(VehicleController).GetField("chassisPitch", Private); var rollF = typeof(VehicleController).GetField("chassisRoll", Private);
            var visualRollF = typeof(VehicleController).GetField("visualRoll", Private);
            int tiltEvents = 0, stuckEvents = 0; float worstTilt = 0;
            foreach (int mapSeed in new[] { 173, 42 })
            {
                game.StartCampaign(mapSeed, 1600);
                yield return new WaitUntil(() => game.State == GameState.Playing);
                yield return new WaitForSecondsRealtime(1);
                player = game.Player; wheels = (Transform[])typeof(VehicleController).GetField("wheels", Private).GetValue(player);
                foreach (var ai in UnityEngine.Object.FindObjectsByType<EnemyAI>(FindObjectsSortMode.None)) ai.enabled = false;
                var hard = new[] { new KeyboardState(Key.W, Key.LeftShift), new KeyboardState(Key.D, Key.Space, Key.LeftShift), new KeyboardState(Key.S, Key.LeftShift), new KeyboardState(Key.A, Key.Space),
                    new KeyboardState(Key.W, Key.D, Key.LeftShift), new KeyboardState(Key.S, Key.A, Key.LeftShift), new KeyboardState(Key.D, Key.LeftShift), new KeyboardState(Key.W, Key.A, Key.Space) };
                float d = 0, tilted = 0, still = 0, bodyTiltMax = 0; int dl = -1; string tiltInfo = "";
                while (d < 45)
                {
                    int want = (int)(d / 2.5f) % hard.Length;
                    if (want != dl) { dl = want; InputSystem.QueueStateEvent(keyboard, hard[dl]); }
                    if (player.Damage.Health < player.Damage.MaxHealth * .5f) player.Repair(10000);
                    yield return new WaitForFixedUpdate(); d += Time.fixedDeltaTime;
                    bodyTiltMax = Mathf.Max(bodyTiltMax, Vector3.Angle(player.Body.rotation * Vector3.up, Vector3.up));
                    float vp = Mathf.DeltaAngle(0, player.Visual.localEulerAngles.x), vr = Mathf.DeltaAngle(0, player.Visual.localEulerAngles.z);
                    float tilt = Mathf.Max(Mathf.Abs(vp), Mathf.Abs(vr));
                    // Tilted and hanging: past 20° with a wheel visibly off the ground (matching a slope is fine).
                    float worstGap = 0; foreach (var wheel in wheels) if (wheel) worstGap = Mathf.Max(worstGap, Gap(wheel.position, player.Body));
                    if (tilt > 20 && worstGap > .4f && (player.Grounded || player.Beached)) { if (tilted == 0) tiltInfo = $"pitch={vp:F0} roll={vr:F0} chassisP={(float)pitchF.GetValue(player):F0} chassisR={(float)rollF.GetValue(player):F0} lean={(float)visualRollF.GetValue(player):F0} g={(player.Grounded ? 1 : 0)} b={(player.Beached ? 1 : 0)} v={player.Body.linearVelocity.magnitude:F0} vy={player.Body.linearVelocity.y:F1} under: {UnderWheels(wheels, player.Body)}"; tilted += Time.fixedDeltaTime; worstTilt = Mathf.Max(worstTilt, tilt); }
                    else { if (tilted > .4f) { tiltEvents++; log.Append($" seed {mapSeed} t={d - tilted:F1} tilted {tilted:F2}s: {tiltInfo}\n"); } tilted = 0; }
                    // Stuck off its wheels (the reported symptom); nosing into a building on four wheels is not a tilt problem.
                    if (!player.Grounded && new Vector2(player.Body.linearVelocity.x, player.Body.linearVelocity.z).magnitude < .5f) still += Time.fixedDeltaTime; else still = 0;
                    if (still > 1 && still - Time.fixedDeltaTime <= 1) { stuckEvents++; log.Append($" seed {mapSeed} t={d:F1} STUCK: pitch={vp:F0} roll={vr:F0} g={(player.Grounded ? 1 : 0)} b={(player.Beached ? 1 : 0)} under: {UnderWheels(wheels, player.Body)} hull={Under(player)}\n"); }
                }
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                log.Append($" seed {mapSeed} physics body max tilt={bodyTiltMax:F1} deg (should be 0: rotation is locked to yaw) inertiaRot={player.Body.inertiaTensorRotation.eulerAngles}\n");
            }
            log.Append($" aggressive: tiltEvents={tiltEvents} stuckEvents={stuckEvents} worstTilt={worstTilt:F0}\n");
            check("aggressive drive: never hangs tilted on its wheels for 0.4 s", tiltEvents == 0);
            check("aggressive drive: never stuck with a direction held", stuckEvents == 0);
            game.StartCampaign(173, 1600);
            yield return new WaitUntil(() => game.State == GameState.Playing);
            yield return new WaitForSecondsRealtime(1);
            player = game.Player; wheels = (Transform[])typeof(VehicleController).GetField("wheels", Private).GetValue(player);
            world = GeneratedWorld.Active;

            // Real cliff lips: the car set down half over the drop, along the edge and facing it, then throttle.
            int lipTests = 0, stuck = 0;
            foreach (var cliff in world.FindCliffs(4))
            {
                Vector3 down3 = new Vector3(cliff.downhill.x, 0, cliff.downhill.y);
                Vector3 lip = cliff.position;
                for (int k = 0; k < 40; k++) { Vector3 next = lip - down3; next.y = GeneratedWorld.HeightAt(next); if (next.y - GeneratedWorld.HeightAt(lip) < .25f && k > 3) break; lip = next; }
                lip.y = GeneratedWorld.HeightAt(lip);
                foreach (var facing in new[] { Quaternion.LookRotation(Vector3.Cross(Vector3.up, down3)), Quaternion.LookRotation(down3) })
                {
                    Vector3 at = lip + down3 * .4f + Vector3.up * 1.2f;
                    player.Body.position = at; player.Body.rotation = facing; player.transform.SetPositionAndRotation(at, facing);
                    player.Body.linearVelocity = Vector3.zero; player.Body.angularVelocity = Vector3.zero; player.Repair(10000); Physics.SyncTransforms();
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                    yield return new WaitForSeconds(.6f);
                    bool beachedAtRest = player.Beached;
                    // Hold the direction a player would use to get off the lip: toward the lower ground.
                    var off = new System.Collections.Generic.List<Key>();
                    if (down3.z > .38f) off.Add(Key.W); else if (down3.z < -.38f) off.Add(Key.S);
                    if (down3.x > .38f) off.Add(Key.D); else if (down3.x < -.38f) off.Add(Key.A);
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState(off.ToArray()));
                    float held = 0, recoveredAt = -1; Vector3 from = player.transform.position;
                    var lipTrace = new StringBuilder(); var pitchField = typeof(VehicleController).GetField("chassisPitch", Private);
                    while (held < 3)
                    {
                        yield return new WaitForFixedUpdate(); held += Time.fixedDeltaTime;
                        bool down = true; foreach (var wheel in wheels) if (wheel && Gap(wheel.position, player.Body) > AirGap) down = false;
                        if (player.Grounded && down) { if (recoveredAt < 0) recoveredAt = held; } else recoveredAt = -1; // slopes may roll the body legitimately
                        if (Mathf.RoundToInt(held / Time.fixedDeltaTime) % 18 == 0)
                        {
                            float gl = Mathf.Max(Gap(wheels[0].position, player.Body), Gap(wheels[2].position, player.Body)), gr = Mathf.Max(Gap(wheels[1].position, player.Body), Gap(wheels[3].position, player.Body));
                            float gf = Mathf.Max(Gap(wheels[0].position, player.Body), Gap(wheels[1].position, player.Body)), gb = Mathf.Max(Gap(wheels[2].position, player.Body), Gap(wheels[3].position, player.Body));
                            lipTrace.Append($"   t={held:F1} roll={Mathf.DeltaAngle(0, player.Visual.localEulerAngles.z):F0} pitch={(float)pitchField.GetValue(player):F0} gapL={gl:F2} gapR={gr:F2} gapF={gf:F2} gapB={gb:F2} g={(player.Grounded ? 1 : 0)} b={(player.Beached ? 1 : 0)} v={player.Body.linearVelocity.magnitude:F1} wc={typeof(VehicleController).GetField("wheelContacts", Private).GetValue(player)} sL={typeof(VehicleController).GetField("supportLeft", Private).GetValue(player)} sR={typeof(VehicleController).GetField("supportRight", Private).GetValue(player)} belly={typeof(VehicleController).GetField("bellyNormal", Private).GetValue(player)} ahead={GeneratedWorld.HeightAt(player.transform.position + player.transform.forward * 3) - GeneratedWorld.HeightAt(player.transform.position):F2} dir={typeof(VehicleController).GetField("driveDirection", Private).GetValue(player)} thr={player.Throttle:F2} hullBottom={player.Hull.bounds.min.y - GeneratedWorld.HeightAt(player.Hull.bounds.center):F2} sleeping={player.Body.IsSleeping()} under={Under(player)}\n");
                        }
                    }
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                    float moved = Vector3.Distance(from, player.transform.position);
                    lipTests++; if (recoveredAt < 0 || moved < 2) stuck++;
                    log.Append($" lip {lipTests}: beachedAtRest={beachedAtRest} moved={moved:F1}m fourWheelsAt={(recoveredAt < 0 ? "never" : recoveredAt.ToString("F2") + "s")}\n");
                    if (recoveredAt < 0 || moved < 2) log.Append(lipTrace);
                }
            }
            check("cliff lips: the car drives off and lands on four wheels", stuck == 0);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            Debug.Log($"MIA_TILT world: hangEvents={events} totalHang={total:F2}s longest={longest:F2}s\n{log}");
            check("world drive: no wheel hangs off the ground for more than 0.5 s", longest <= .5f);
        }

        static string Under(VehicleController player)
        {
            var b = player.Hull.bounds; var names = new StringBuilder();
            foreach (var c in Physics.OverlapBox(b.center - Vector3.up * b.extents.y, new Vector3(b.extents.x, .35f, b.extents.z)))
                if (c.attachedRigidbody != player.Body) names.Append(c.name).Append('/').Append(c.GetComponentInParent<DestructionSystem>() ? "breakable" : "solid").Append(':').Append(c.bounds.size.ToString("F1")).Append(' ');
            return names.ToString();
        }

        static string UnderWheels(Transform[] wheels, Rigidbody body)
        {
            var text = new StringBuilder();
            string[] names = { "FL", "FR", "RL", "RR" };
            for (int i = 0; i < wheels.Length; i++)
            {
                if (!wheels[i]) continue;
                var hits = Physics.RaycastAll(wheels[i].position + Vector3.up, Vector3.down, 7, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                RaycastHit best = default; float nearest = float.MaxValue;
                foreach (var hit in hits) if (hit.rigidbody != body && hit.distance < nearest) { nearest = hit.distance; best = hit; }
                text.Append(names[i]).Append('=').Append(nearest == float.MaxValue ? "none" : $"{best.collider.name}@{nearest - 1.43f:F2}").Append(' ');
            }
            return text.ToString();
        }

        /// <summary>Distance from a wheel's lowest point to the ground straight below it (6 = nothing below).</summary>
        static float Gap(Vector3 wheel, Rigidbody body)
        {
            var hits = Physics.RaycastAll(wheel + Vector3.up, Vector3.down, 7, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            foreach (var hit in hits) if (hit.rigidbody != body && hit.distance < best) best = hit.distance;
            return best == float.MaxValue ? 6 : best - 1 - .43f;
        }

        static GameObject Box(GameObject parent, string name, Vector3 position, Vector3 scale, Quaternion rotation)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name; box.transform.SetParent(parent.transform);
            box.transform.SetPositionAndRotation(position, rotation); box.transform.localScale = scale;
            return box;
        }
    }
}
