using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace MadeInArizona
{
    /// <summary>Opt-in, local filesystem control for development players. Never listens on a network socket.</summary>
    public sealed class LiveTestBridge : MonoBehaviour
    {
        [Serializable] sealed class Request
        {
            public string id, command, roomCode;
            public float moveX, moveY, seconds;
            public bool boost, drift;
        }

        [Serializable] sealed class Response
        {
            public string id, message, state, coopStatus, ping, tracePath, benchmarkId, screenshotPath;
            public string joinCode, updaterPhase, updaterStatus, updateResult, currentBuild, latestBuild;
            public bool ok, isHost, isGuest, coopBusy, benchmarkRunning, updateAvailable, updateRunning;
            public int players;
            public float x, y, z, speedKph, updateProgress;
            public long utcTicks;
        }

        string root, requests, responses;
        public string BridgeDirectory => root;
        float nextPoll, nextHeartbeat;
        bool updateRunning;
        string updateResult = "";

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
                case "host_coop":
                    if (!game || (game.State != GameState.MainMenu && game.State != GameState.Garage))
                        return Snapshot(request.id, false, "Open the main menu or garage before hosting");
                    if (!coop || coop.Busy || coop.IsHost || coop.IsClient || updateRunning)
                        return Snapshot(request.id, false, "Co-op or an update is already active");
                    coop.Host();
                    return Snapshot(request.id, true, "Hosting requested; poll game_status for the room code or an error");
                case "join_coop":
                    if (!game || (game.State != GameState.MainMenu && game.State != GameState.Garage))
                        return Snapshot(request.id, false, "Open the main menu or garage before joining");
                    if (!coop || coop.Busy || coop.IsHost || coop.IsClient || updateRunning)
                        return Snapshot(request.id, false, "Co-op or an update is already active");
                    string code = (request.roomCode ?? "").Trim().ToUpperInvariant();
                    if (code.Length < 4 || code.Length > 16)
                        return Snapshot(request.id, false, "Room code must be 4–16 characters");
                    foreach (char character in code)
                        if (!((character >= 'A' && character <= 'Z') || (character >= '0' && character <= '9')))
                            return Snapshot(request.id, false, "Room code must be alphanumeric");
                    coop.Join(code);
                    return Snapshot(request.id, true, "Join requested; poll game_status for connection or an error");
                case "check_updates":
                    if (!GameUpdater.Instance) return Snapshot(request.id, false, "Updater is unavailable");
                    if (GameUpdater.Instance.Busy || updateRunning)
                        return Snapshot(request.id, false, "Updater is already busy");
                    updateResult = "";
                    GameUpdater.Instance.Check();
                    return Snapshot(request.id, true, "Checking published builds; poll game_status for the result");
                case "update_game":
                    if (!game || (game.State != GameState.MainMenu && game.State != GameState.Garage))
                        return Snapshot(request.id, false, "Return to the main menu or garage before updating");
                    if (coop && (coop.Busy || coop.IsHost || coop.IsClient))
                        return Snapshot(request.id, false, "Leave co-op before updating");
                    var updater = GameUpdater.Instance;
                    if (!updater || updater.InstallBlocker != null)
                        return Snapshot(request.id, false, updater ? updater.InstallBlocker : "Updater is unavailable");
                    if (updateRunning || updater.Busy)
                        return Snapshot(request.id, false, "Updater is already busy");
                    updateRunning = true;
                    updateResult = "Checking for the latest build";
                    StartCoroutine(UpdateLatest());
                    return Snapshot(request.id, true, "Update started; poll game_status for progress");
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

        IEnumerator UpdateLatest()
        {
            var updater = GameUpdater.Instance;
            updater.Check();
            while (updater && updater.State == GameUpdater.Phase.Checking) yield return null;
            if (!updater || updater.State != GameUpdater.Phase.Ready)
            {
                updateResult = updater ? updater.Status : "Updater closed";
                updateRunning = false;
                yield break;
            }
            if (!updater.UpdateAvailable)
            {
                updateResult = "Already on the latest published build";
                updateRunning = false;
                yield break;
            }
            var target = updater.Builds[0];
            updateResult = "Downloading " + target.ShortCommit;
            updater.Download(target);
            while (updater && (updater.State == GameUpdater.Phase.Downloading || updater.State == GameUpdater.Phase.Verifying)) yield return null;
            if (!updater || !updater.IsDownloaded(target))
            {
                updateResult = updater ? updater.Status : "Updater closed";
                updateRunning = false;
                yield break;
            }
            var game = GameManager.Instance;
            var coop = CoopSession.Instance;
            if (!game || (game.State != GameState.MainMenu && game.State != GameState.Garage) ||
                (coop && (coop.Busy || coop.IsHost || coop.IsClient)))
            {
                updateResult = "Download ready; return to the menu and leave co-op before installing";
                updateRunning = false;
                yield break;
            }
            updateResult = "Installing " + target.ShortCommit + " and restarting";
            updater.InstallAndRestart();
            if (updater.State == GameUpdater.Phase.Failed)
            {
                updateResult = updater.Status;
                updateRunning = false;
            }
        }

        Response Snapshot(string id, bool ok, string message)
        {
            var game = GameManager.Instance;
            var coop = CoopSession.Instance;
            var updater = GameUpdater.Instance;
            var car = game ? game.Player : null;
            var p = car ? car.transform.position : Vector3.zero;
            return new Response {
                id = id, ok = ok, message = message, utcTicks = DateTime.UtcNow.Ticks,
                state = game ? game.State.ToString() : "Loading", coopStatus = coop ? coop.Status : "Offline",
                joinCode = coop ? coop.JoinCode : "", coopBusy = coop && coop.Busy,
                isHost = coop && coop.IsHost, isGuest = coop && coop.IsClient,
                players = coop ? coop.PlayerCount : 1, ping = coop ? coop.PingSummary : "--",
                tracePath = coop ? coop.LastTracePath : "", benchmarkId = coop ? coop.LastBenchmarkId : "",
                benchmarkRunning = coop && coop.BenchmarkRunning,
                x = p.x, y = p.y, z = p.z, speedKph = car ? car.SpeedKph : 0,
                updaterPhase = updater ? updater.State.ToString() : "Unavailable",
                updaterStatus = updater ? updater.Status : "Unavailable", updateResult = updateResult,
                currentBuild = updater && updater.Current != null ? updater.Current.ShortCommit : "unpublished",
                latestBuild = updater && updater.Builds.Count > 0 ? updater.Builds[0].ShortCommit : "",
                updateProgress = updater ? updater.Progress : 0,
                updateAvailable = updater && updater.UpdateAvailable, updateRunning = updateRunning
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
