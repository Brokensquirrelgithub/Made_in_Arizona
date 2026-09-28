using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace MadeInArizona
{
    /// <summary>
    /// Scripted driving measurements (-miaDriveProbe): a full-throttle straight, a 90 degree turn at speed and the
    /// same turn drifting, on a flat test deck and across generated terrain, each with the extra air gravity on and
    /// off. Logs MIA_DRIVE lines for comparing handling between builds and settings.
    /// </summary>
    public static class DriveProbe
    {
        public static IEnumerator Run(Action<string, bool> check)
        {
            var game = GameManager.Instance;
            // The player's own saved tuning, so every build is measured the way it is actually driven.
            game.Save.settings.dev = new DevTuning { steering = 2.0526f, acceleration = 1.6013f, driftYaw = 1.401f };
            game.StartCampaign(173, 1600);
            yield return new WaitUntil(() => game.State == GameState.Playing);
            yield return new WaitForSecondsRealtime(1);
            var keyboard = InputSystem.AddDevice<Keyboard>();
            // A flat deck above the terrain, inside the state outline, so the generated world's bounds reset stays quiet.
            var deck = GameObject.CreatePrimitive(PrimitiveType.Cube); deck.name = "Drive probe deck";
            deck.transform.position = new Vector3(0, 300, -100); deck.transform.localScale = new Vector3(260, 1, 900);
            var results = new Dictionary<string, float>();
            foreach (bool gravity in new[] { true, false })
            {
                VehicleController.ExtraAirGravityEnabled = gravity;
                WorldBuilder.SurfaceOverride = SurfaceKind.Asphalt; // the deck is a paved test surface in every build
                yield return Straight(keyboard, "flat", new Vector3(0, 301.3f, -520), 8, gravity, results);
                yield return Corner(keyboard, "flat", new Vector3(0, 301.3f, -520), false, gravity, results);
                yield return Corner(keyboard, "flat", new Vector3(0, 301.3f, -520), true, gravity, results);
                WorldBuilder.SurfaceOverride = null;
                Vector3 terrain = new Vector3(260, 0, -560); terrain.y = GeneratedWorld.HeightAt(terrain) + 1.2f;
                yield return Straight(keyboard, "terrain", terrain, 10, gravity, results);
            }
            VehicleController.ExtraAirGravityEnabled = true;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.RemoveDevice(keyboard);
            UnityEngine.Object.Destroy(deck);
            check("drive probe completed", results.Count > 0);
        }

        static void Place(VehicleController player, Vector3 at)
        {
            player.Body.position = at; player.Body.rotation = Quaternion.identity; player.transform.SetPositionAndRotation(at, Quaternion.identity);
            player.Body.linearVelocity = Vector3.zero; player.Body.angularVelocity = Vector3.zero; player.Repair(10000);
            Physics.SyncTransforms();
        }

        static float Planar(VehicleController player) { var v = player.Body.linearVelocity; v.y = 0; return v.magnitude; }

        static IEnumerator Straight(Keyboard keyboard, string ground, Vector3 start, float seconds, bool gravity, Dictionary<string, float> results)
        {
            var player = GameManager.Instance.Player;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); Place(player, start);
            yield return new WaitForSeconds(1);
            int steps = 0, grounded = 0; VehicleController.ExtraGravitySteps = 0; float maxRpm = 0, t = 0, to60 = -1;
            var marks = new List<string>(); Vector3 from = player.transform.position;
            var trace = new System.Text.StringBuilder();
            VehicleController.HullGroundSteps = 0; VehicleController.LastHitName = ""; VehicleController.ProbeCounting = true;
            int hullAtMark = 0;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
            while (t < seconds)
            {
                yield return new WaitForFixedUpdate(); t += Time.fixedDeltaTime; steps++;
                if (player.Grounded) grounded++;
                maxRpm = Mathf.Max(maxRpm, player.RPM);
                float mph = Planar(player) * 2.23694f;
                if (to60 < 0 && mph >= 60) to60 = t;
                if (Mathf.Repeat(t + Time.fixedDeltaTime * .5f, 2) < Time.fixedDeltaTime) marks.Add($"{Mathf.Round(t)}s:{mph:F0}");
                if (ground == "terrain" && Mathf.Repeat(t + Time.fixedDeltaTime * .5f, .5f) < Time.fixedDeltaTime)
                {
                    Vector3 p = player.transform.position, f = player.transform.forward;
                    float slope = (GeneratedWorld.HeightAt(p + f * 2) - GeneratedWorld.HeightAt(p - f * 2)) / 4;
                    trace.Append($"\n   t={t:F1} {mph:F0}mph {WorldBuilder.SurfaceAt(p)} G={(player.Grounded ? 1 : 0)} grade={slope:F2} hullScrape={VehicleController.HullGroundSteps - hullAtMark} rpm={player.RPM:F0} gear={player.Gear} hit={VehicleController.LastHitName}");
                    hullAtMark = VehicleController.HullGroundSteps; VehicleController.LastHitName = "";
                }
            }
            VehicleController.ProbeCounting = false;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            float distance = Vector3.Distance(new Vector3(from.x, 0, from.z), new Vector3(player.transform.position.x, 0, player.transform.position.z));
            float top = Planar(player) * 2.23694f;
            results[ground + "/" + gravity + "/top"] = top;
            Debug.Log($"MIA_DRIVE straight {ground} extraGravity={gravity}: mph@{string.Join(" ", marks)} top={top:F1}mph 0-60={(to60 < 0 ? "n/a" : to60.ToString("F2") + "s")} distance={distance:F0}m maxRPM={maxRpm:F0} gear={player.Gear} grounded={100f * grounded / steps:F0}% gravitySteps={VehicleController.ExtraGravitySteps} hullScrapeSteps={VehicleController.HullGroundSteps}{trace}");
        }

        static IEnumerator Corner(Keyboard keyboard, string ground, Vector3 start, bool drift, bool gravity, Dictionary<string, float> results)
        {
            var player = GameManager.Instance.Player;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); Place(player, start);
            yield return new WaitForSeconds(1);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
            yield return new WaitForSeconds(5);
            float entry = Planar(player) * 2.23694f, t = 0, minSpeed = entry, turned = -1, peakYaw = 0; int grounded = 0, steps = 0;
            Vector3 entryPoint = player.transform.position;
            InputSystem.QueueStateEvent(keyboard, drift ? new KeyboardState(Key.D, Key.Space) : new KeyboardState(Key.D));
            while (t < 4)
            {
                yield return new WaitForFixedUpdate(); t += Time.fixedDeltaTime; steps++;
                if (player.Grounded) grounded++;
                minSpeed = Mathf.Min(minSpeed, Planar(player) * 2.23694f);
                peakYaw = Mathf.Max(peakYaw, Mathf.Abs(player.Body.angularVelocity.y) * Mathf.Rad2Deg);
                float heading = Vector3.SignedAngle(Vector3.forward, player.transform.forward, Vector3.up);
                if (turned < 0 && heading > 80) { turned = t; break; }
            }
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            float exit = Planar(player) * 2.23694f;
            Vector3 travel = player.transform.position - entryPoint;
            results[ground + "/" + gravity + "/" + (drift ? "drift" : "turn")] = turned;
            Debug.Log($"MIA_DRIVE {(drift ? "drift" : "turn")} {ground} extraGravity={gravity}: entry={entry:F0}mph 80deg-in={(turned < 0 ? "not reached" : turned.ToString("F2") + "s")} exit={exit:F0}mph min={minSpeed:F0}mph peakYaw={peakYaw:F0}deg/s forwardRun={travel.z:F1}m grounded={100f * grounded / Mathf.Max(1, steps):F0}%");
        }
    }
}
