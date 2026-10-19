using System;
using System.IO;
using UnityEngine;

namespace MadeInArizona
{
    /// <summary>Opt-in, local filesystem control for development players. Never listens on a network socket.</summary>
    public sealed class LiveTestBridge : MonoBehaviour
    {
        [Serializable] sealed class Request
        {
            public string id, command;
            public float moveX, moveY, seconds;
            public bool boost, drift;
        }

        [Serializable] sealed class Response
        {
            public string id, message, state, coopStatus, ping, tracePath, benchmarkId, screenshotPath;
            public bool ok, isHost, isGuest, benchmarkRunning;
            public int players;
            public float x, y, z, speedKph;
            public long utcTicks;
        }

        string root, requests, responses;
        float nextPoll, nextHeartbeat;

        public static bool ShouldStart()
        {
            if (!Debug.isDebugBuild) return false;
            return Array.IndexOf(Environment.GetCommandLineArgs(), "-miaAutomationBridge") >= 0;
        }

        void Awake()
        {
            string configured = null;
            foreach (string arg in Environment.GetCommandLineArgs())
                if (arg.StartsWith("-miaBridgeDir=", StringComparison.Ordinal)) configured = arg.Substring(14);
            root = string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(Application.persistentDataPath, "AutomationBridge", System.Diagnostics.Process.GetCurrentProcess().Id.ToString())
                : Path.GetFullPath(configured);
            requests = Path.Combine(root, "requests");
            responses = Path.Combine(root, "responses");
            Directory.CreateDirectory(requests);
            Directory.CreateDirectory(responses);
            WriteAtomic(Path.Combine(root, "session.json"), JsonUtility.ToJson(Snapshot("session", true, "Ready")));
            Debug.Log("MIA_AUTOMATION_BRIDGE: " + root);
        }

        void Update()
        {
            if (Time.unscaledTime >= nextHeartbeat)
            {
                nextHeartbeat = Time.unscaledTime + 1f;
                WriteAtomic(Path.Combine(root, "session.json"), JsonUtility.ToJson(Snapshot("session", true, "Ready")));
            }
            if (Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + .05f;
            string[] pending = Directory.GetFiles(requests, "*.json");
            Array.Sort(pending, StringComparer.Ordinal);
            for (int i = 0; i < Math.Min(4, pending.Length); i++) Handle(pending[i]);
        }

        void Handle(string path)
        {
            string id = Path.GetFileNameWithoutExtension(path);
            if (id.Length != 32 || !Guid.TryParseExact(id, "N", out _)) { File.Delete(path); return; }
            Response result;
            try
            {
                if (new FileInfo(path).Length > 4096) throw new InvalidDataException("Request exceeds 4 KB");
                var request = JsonUtility.FromJson<Request>(File.ReadAllText(path));
                if (request == null || request.id != id) throw new InvalidDataException("Request ID mismatch");
                result = Run(request);
            }
            catch (Exception e) { result = Snapshot(id, false, e.Message); }
            try
            {
                WriteAtomic(Path.Combine(responses, id + ".json"), JsonUtility.ToJson(result));
                File.Delete(path);
            }
            catch (Exception e) { Debug.LogWarning("Automation bridge response failed: " + e.Message); }
        }

        Response Run(Request request)
        {
            var game = GameManager.Instance;
            var coop = CoopSession.Instance;
            switch (request.command)
            {
                case "status": return Snapshot(request.id, true, "Ready");
                case "coopbench": return Snapshot(request.id, coop != null && game && game.IsPlaying,
                    coop != null && game && game.IsPlaying ? coop.RequestCoopBenchmark() : "A live co-op sortie is required");
                case "cooplog": return Snapshot(request.id, coop != null && game && game.IsPlaying,
                    coop != null && game && game.IsPlaying ? coop.RequestDiagnosticCapture() : "A live co-op sortie is required");
                case "drive":
                    if (!game || !game.IsPlaying || !game.Player || !InputManager.Instance)
                        return Snapshot(request.id, false, "A local player in a sortie is required");
                    if (float.IsNaN(request.moveX) || float.IsNaN(request.moveY) || float.IsNaN(request.seconds) ||
                        float.IsInfinity(request.moveX) || float.IsInfinity(request.moveY) || float.IsInfinity(request.seconds) ||
                        request.seconds < .05f || request.seconds > 5f)
                        return Snapshot(request.id, false, "Drive duration must be 0.05–5 seconds with finite inputs");
                    InputManager.Instance.SetAutomationDrive(new Vector2(request.moveX, request.moveY), request.seconds, request.boost, request.drift);
                    return Snapshot(request.id, true, "Driving for " + request.seconds.ToString("0.00") + " seconds");
                case "stop":
                    if (InputManager.Instance) InputManager.Instance.StopAutomationDrive();
                    return Snapshot(request.id, true, "Stopped automation input");
                case "screenshot":
                    string screenshot = Path.Combine(root, "screen-" + request.id + ".png");
                    ScreenCapture.CaptureScreenshot(screenshot);
                    var captured = Snapshot(request.id, true, "Screenshot queued");
                    captured.screenshotPath = screenshot;
                    return captured;
                default: return Snapshot(request.id, false, "Unknown bridge command");
            }
        }

        Response Snapshot(string id, bool ok, string message)
        {
            var game = GameManager.Instance;
            var coop = CoopSession.Instance;
            var car = game ? game.Player : null;
            var p = car ? car.transform.position : Vector3.zero;
            return new Response {
                id = id, ok = ok, message = message, utcTicks = DateTime.UtcNow.Ticks,
                state = game ? game.State.ToString() : "Loading", coopStatus = coop ? coop.Status : "Offline",
                isHost = coop && coop.IsHost, isGuest = coop && coop.IsClient,
                players = coop ? coop.PlayerCount : 1, ping = coop ? coop.PingSummary : "--",
                tracePath = coop ? coop.LastTracePath : "", benchmarkId = coop ? coop.LastBenchmarkId : "",
                benchmarkRunning = coop && coop.BenchmarkRunning,
                x = p.x, y = p.y, z = p.z, speedKph = car ? car.SpeedKph : 0
            };
        }

        static void WriteAtomic(string path, string json)
        {
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, json);
            if (File.Exists(path)) File.Delete(path);
            File.Move(temporary, path);
        }
    }
}
