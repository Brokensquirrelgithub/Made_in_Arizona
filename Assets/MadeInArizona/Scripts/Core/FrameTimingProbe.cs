using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Debug = UnityEngine.Debug;

namespace MadeInArizona
{
    /// <summary>Opt-in frame pacing probe (-miaSmokeTest -miaFrameTimingTest). Drives the generated world and attributes hitches.</summary>
    public static class FrameTimingProbe
    {
        // Streaming systems add their own work here so a spike can be attributed to them.
        public static double StreamingMs;
        public static int StreamingTiles;
        public static string Events = "";
        public static readonly Stopwatch Clock = new Stopwatch();

        public static IEnumerator Run(Action<string, bool> check)
        {
            var game = GameManager.Instance;
            game.WorldConfig.seed = 173; game.WorldConfig.size = 1600;
            game.StartCampaign(173, 1600);
            yield return new WaitUntil(() => game.State == GameState.Playing);
            yield return new WaitForSecondsRealtime(3);
            var ecology = GeneratedWorld.Active.GetComponent<LivingWorldDetail>();
            var build = typeof(LivingWorldDetail).GetMethod("BuildTile", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var tileTimes = new List<double>(); var sliceTimes = new List<double>();
            for (int i = 0; i < 24; i++)
            {
                var sw = Stopwatch.StartNew();
                var tile = new GameObject("Probe tile");
                var steps = (IEnumerator)build.Invoke(ecology, new object[] { new Vector2Int(-6 + i % 12, 3 + i / 12), tile });
                double longest = 0; var slice = Stopwatch.StartNew();
                while (steps.MoveNext()) { longest = Math.Max(longest, slice.Elapsed.TotalMilliseconds); slice.Restart(); }
                longest = Math.Max(longest, slice.Elapsed.TotalMilliseconds);
                tileTimes.Add(sw.Elapsed.TotalMilliseconds); sliceTimes.Add(longest);
                UnityEngine.Object.Destroy(tile);
                yield return null;
            }
            tileTimes.Sort(); sliceTimes.Sort();
            Debug.Log($"MIA_TILE_BUILD camera={(bool)Camera.main} loadedTiles={ecology.LoadedTiles} median={tileTimes[tileTimes.Count / 2]:F2}ms max={tileTimes[tileTimes.Count - 1]:F2}ms longestSlice={sliceTimes[sliceTimes.Count - 1]:F2}ms all={string.Join(",", tileTimes.ConvertAll(t => t.ToString("F1")))}");
            var keyboard = InputSystem.AddDevice<Keyboard>();
            var frames = new List<float>(); var lines = new List<string>();
            int fixedSteps = 0; float lastFixed = Time.fixedTime; int gcStart = GC.CollectionCount(0);
            double streamingTotal = 0; int streamingFrames = 0;
            Key[] route = { Key.W, Key.D, Key.W, Key.A, Key.S, Key.D };
            float start = Time.realtimeSinceStartup;
            int leg = -1;
            while (Time.realtimeSinceStartup - start < 36)
            {
                int wanted = Mathf.Min(route.Length - 1, (int)((Time.realtimeSinceStartup - start) / 6));
                if (wanted != leg) { leg = wanted; InputSystem.QueueStateEvent(keyboard, new KeyboardState(route[leg], Key.LeftShift)); }
                StreamingMs = 0; StreamingTiles = 0; Events = "";
                int gc = GC.CollectionCount(0);
                yield return null;
                float ms = Time.unscaledDeltaTime * 1000;
                int steps = Mathf.RoundToInt((Time.fixedTime - lastFixed) / Time.fixedDeltaTime); lastFixed = Time.fixedTime; fixedSteps += steps;
                frames.Add(ms);
                if (StreamingTiles > 0) { streamingTotal += StreamingMs; streamingFrames++; }
                bool collected = GC.CollectionCount(0) != gc;
                if (ms > 20 || StreamingMs > 4 || collected)
                    lines.Add($"frame {Time.frameCount} {ms:F1}ms fixedSteps={steps} streaming={StreamingMs:F1}ms tiles={StreamingTiles} gc={collected} vehicles={VehicleController.Active.Count} patrols={(GeneratedWorld.Active.GetComponent<RoadPatrolDirector>() ? GeneratedWorld.Active.GetComponent<RoadPatrolDirector>().EncountersSpawned : -1)} speed={game.Player.Body.linearVelocity.magnitude:F1} events={Events}");
            }
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.RemoveDevice(keyboard);
            var sorted = new List<float>(frames); sorted.Sort();
            float P(float q) => sorted[Mathf.Clamp((int)(q * sorted.Count), 0, sorted.Count - 1)];
            string summary = $"frames={frames.Count} median={P(.5f):F2}ms p95={P(.95f):F2}ms p99={P(.99f):F2}ms max={sorted[sorted.Count - 1]:F2}ms gcCollections={GC.CollectionCount(0) - gcStart} streamingFrames={streamingFrames} streamingAvg={(streamingFrames > 0 ? streamingTotal / streamingFrames : 0):F2}ms";
            string output = Path.Combine(Application.temporaryCachePath, "mia-frame-timing.txt");
            File.WriteAllText(output, summary + "\n" + string.Join("\n", lines));
            Debug.Log("MIA_FRAME_TIMING " + summary + "\n" + string.Join("\n", lines) + "\n" + output);
            check("frame timing probe completed", frames.Count > 100);
        }
    }
}
