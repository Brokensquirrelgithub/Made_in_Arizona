using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MadeInArizona
{
    /// <summary>
    /// Opt-in play telemetry (-miaTelemetry): logs player input and chassis state to telemetry.log beside the executable,
    /// flags tipped-and-hanging or stuck moments automatically, and dumps the last five seconds when F9 is pressed.
    /// </summary>
    public sealed class TelemetryRecorder : MonoBehaviour
    {
        public static bool Enabled => Array.IndexOf(Environment.GetCommandLineArgs(), "-miaTelemetry") >= 0;
        const int HistoryLength = 250; // five seconds of physics steps
        readonly Queue<string> history = new Queue<string>();
        readonly RaycastHit[] hits = new RaycastHit[8];
        StreamWriter writer;
        int step; float hangTime, stuckTime; bool hangLogged, stuckLogged;
        VehicleController cachedCar; Transform cachedVisual; readonly Transform[] wheelCache = new Transform[4];
        static readonly string[] WheelNames = { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" };

        void Start()
        {
            string path = Path.Combine(Path.GetDirectoryName(Application.dataPath), "telemetry.log");
            writer = new StreamWriter(path, false) { AutoFlush = true };
            writer.WriteLine($"START {DateTime.Now:HH:mm:ss} telemetry at 20 Hz; events flagged automatically; F9 = manual mark");
        }

        void OnDestroy() { writer?.Dispose(); }

        void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f9Key.wasPressedThisFrame)
            {
                Dump("MARK (F9)");
                GameManager.Instance?.Notify("TELEMETRY MARK SAVED");
            }
        }

        void FixedUpdate()
        {
            var game = GameManager.Instance; var car = game ? game.Player : null;
            if (writer == null || !car || !car.Visual || car.Body == null) return;
            var input = InputManager.Instance;
            Vector2 move = input ? input.Move : Vector2.zero;
            Vector3 v = car.Body.linearVelocity, p = car.Body.position;
            float visualPitch = Mathf.DeltaAngle(0, car.Visual.localEulerAngles.x), visualRoll = Mathf.DeltaAngle(0, car.Visual.localEulerAngles.z);
            string gaps = WheelGaps(car, out float worstGap);
            string line = $"t={Time.time:F2} {game.State} in=({move.x:F2},{move.y:F2}) drift={(input && input.Drift ? 1 : 0)} boost={(input && input.Boost ? 1 : 0)} " +
                          $"spd={v.magnitude:F1} vy={v.y:F1} pos=({p.x:F1},{p.y:F1},{p.z:F1}) yaw={car.Body.rotation.eulerAngles.y:F0} body=({Mathf.DeltaAngle(0, car.Body.rotation.eulerAngles.x):F1},{Mathf.DeltaAngle(0, car.Body.rotation.eulerAngles.z):F1}) vis=({visualPitch:F0},{visualRoll:F0}) {car.TelemetryState()} gaps={gaps}";
            history.Enqueue(line); if (history.Count > HistoryLength) history.Dequeue();
            if (step++ % 3 == 0) writer.WriteLine(line);

            // Tipped and hanging: body past 20° with a wheel well off the ground while the car sits on something.
            bool hanging = Mathf.Max(Mathf.Abs(visualPitch), Mathf.Abs(visualRoll)) > 20 && worstGap > .4f && (car.Grounded || car.Beached);
            hangTime = hanging ? hangTime + Time.fixedDeltaTime : 0;
            if (hangTime > .4f && !hangLogged) { hangLogged = true; Dump("EVENT tipped-and-hanging"); }
            if (hangTime == 0) hangLogged = false;
            // Stuck: off its wheels, a direction held, and not moving.
            bool stuck = !car.Grounded && move.sqrMagnitude > .25f && new Vector2(v.x, v.z).magnitude < .5f;
            stuckTime = stuck ? stuckTime + Time.fixedDeltaTime : 0;
            if (stuckTime > 1 && !stuckLogged) { stuckLogged = true; Dump("EVENT stuck-off-wheels"); }
            if (stuckTime == 0) stuckLogged = false;
        }

        /// <summary>Vertical distance from each wheel to the ground below it (FL FR RL RR), plus what that ground is.</summary>
        string WheelGaps(VehicleController car, out float worst)
        {
            worst = 0; var text = new StringBuilder();
            if (car != cachedCar || car.Visual != cachedVisual)
            {
                cachedCar = car; cachedVisual = car.Visual;
                for (int i = 0; i < 4; i++) wheelCache[i] = Find(car.Visual, WheelNames[i]);
            }
            foreach (var wheel in wheelCache)
            {
                if (!wheel) { text.Append("-,"); continue; }
                int count = Physics.RaycastNonAlloc(wheel.position + Vector3.up, Vector3.down, hits, 8, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                float best = float.MaxValue; string under = "none";
                for (int i = 0; i < count; i++) if (hits[i].rigidbody != car.Body && hits[i].distance < best) { best = hits[i].distance; under = hits[i].collider.name; }
                float gap = best == float.MaxValue ? 7 : best - 1.43f;
                worst = Mathf.Max(worst, gap);
                text.Append($"{gap:F2}:{under},");
            }
            return text.ToString();
        }

        static Transform Find(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root) { var found = Find(child, name); if (found) return found; }
            return null;
        }

        void Dump(string reason)
        {
            writer.WriteLine($"==== {reason} at t={Time.time:F2}; last {history.Count} physics steps follow ====");
            foreach (var line in history) writer.WriteLine("  " + line);
            writer.WriteLine($"==== end {reason} ====");
        }
    }
}
