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
            var ledge = Box(arena, "Ledge", o + new Vector3(10 - .25f, 3, 10), new Vector3(20, 6, 60), Quaternion.identity);
            yield return Drive(player, keyboard, o + new Vector3(0, 6.2f, -12), 0, "cliff-edge", check, null,
                (0f, none), (1f, new KeyboardState(Key.D)), (1.8f, w));
            ledge.SetActive(false);

            // Crossing a 20 degree side slope onto flat ground. Recovery counts from leaving the slope.
            var slope = Box(arena, "Side slope", o + new Vector3(0, -.5f, 0), new Vector3(14, 1, 30), Quaternion.Euler(0, 0, 20));
            yield return Drive(player, keyboard, o + new Vector3(4, 2.2f, -12), 12, "side-slope", check, () => player.transform.position.z > o.z + 15, (0f, w));
            slope.SetActive(false);

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
                if (!player.Grounded && gapL > AirGap && gapR > AirGap) level = true;
                if (recoverFrom >= 0) { if (!level) recoveredAt = -1; else if (recoveredAt < 0) recoveredAt = t; }
                if (Mathf.RoundToInt(t / Time.fixedDeltaTime) % 6 == 0)
                    trace.Append($" t={t:F1} roll={roll:F0} gapL={gapL:F2} gapR={gapR:F2} y={player.Body.position.y - start.y:F2} x={player.Body.position.x - start.x:F1} z={player.Body.position.z - start.z:F0} g={(player.Grounded ? 1 : 0)}\n");
            }
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            float recovery = recoverFrom < 0 ? 99 : recoveredAt < 0 ? 99 : recoveredAt - recoverFrom;
            Debug.Log($"MIA_TILT {name}: maxVisualRoll={maxRoll:F1} recovery={recovery:F2}s (from t={recoverFrom:F2})\n{trace}");
            check($"{name}: back on four wheels within 0.5 s", recovery <= .5f);
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
            while (t < 60)
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
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            Debug.Log($"MIA_TILT world: hangEvents={events} totalHang={total:F2}s longest={longest:F2}s\n{log}");
            check("world drive: no wheel hangs off the ground for more than 0.5 s", longest <= .5f);
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
