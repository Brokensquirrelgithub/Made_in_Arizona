using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MadeInArizona
{
    /// <summary>Opt-in, local filesystem control for development players. Never listens on a network socket.</summary>
    public sealed class LiveTestBridge : MonoBehaviour
    {
        [Serializable] sealed class Request
        {
            public string id, command, roomCode, field;
            public float moveX, moveY, seconds, value;
            public int guestSlot;
            public bool boost, drift;
        }

        [Serializable] sealed class Response
        {
            public string id, message, state, coopStatus, ping, tracePath, benchmarkId, screenshotPath;
            public string joinCode, updaterPhase, updaterStatus, updateResult, currentBuild, latestBuild;
            public string peerSnapshot, recentErrors, tuningField, tuningScope, tuningControls;
            public bool ok, isHost, isGuest, coopBusy, benchmarkRunning, updateAvailable, updateRunning;
            public int players, guestSlot, playerContactsSent, playerContactsApplied;
            public float x, y, z, speedKph, updateProgress, tuningValue, tuningMinimum, tuningMaximum;
            public long utcTicks;
        }

        string root, requests, responses;
        readonly Dictionary<string, PendingGuest> pendingGuests = new Dictionary<string, PendingGuest>();
        readonly Queue<string> recentErrors = new Queue<string>();
        struct PendingGuest { public ulong clientId; public int slot; public float deadline; }
        public static LiveTestBridge Instance { get; private set; }
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
            Instance = this;
            Application.logMessageReceived += RecordLog;
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

        void OnDestroy()
        {
            Application.logMessageReceived -= RecordLog;
            if (Instance == this) Instance = null;
        }

        void RecordLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            recentErrors.Enqueue(DateTime.UtcNow.ToString("o") + " " + condition.Substring(0, Math.Min(240, condition.Length)));
            while (recentErrors.Count > 12) recentErrors.Dequeue();
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
            var expired = new List<string>();
            foreach (var pair in pendingGuests)
                if (Time.unscaledTime > pair.Value.deadline) expired.Add(pair.Key);
            foreach (string id in expired)
            {
                pendingGuests.Remove(id);
                WriteAtomic(Path.Combine(responses, id + ".json"),
                    JsonUtility.ToJson(Snapshot(id, false, "Guest did not answer within 12 seconds; check its bridge and connection")));
            }
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
                if (result != null) WriteAtomic(Path.Combine(responses, id + ".json"), JsonUtility.ToJson(result));
                File.Delete(path);
            }
            catch (Exception e) { Debug.LogWarning("Automation bridge response failed: " + e.Message); }
        }

        Response Run(Request request)
        {
            var game = GameManager.Instance;
            var coop = CoopSession.Instance;
            if (request.command != null && request.command.StartsWith("guest_", StringComparison.Ordinal))
            {
                string command = request.command.Substring(6);
                if (!RemoteAllowed(command)) return Snapshot(request.id, false, "Guest command is not allowed");
                if (!coop) return Snapshot(request.id, false, "Co-op is unavailable");
                request.command = command;
                string json = JsonUtility.ToJson(request);
                if (!coop.RequestGuestAutomation(request.guestSlot, request.id, json, out ulong clientId, out string error))
                    return Snapshot(request.id, false, error);
                pendingGuests[request.id] = new PendingGuest { clientId = clientId,
                    slot = request.guestSlot, deadline = Time.unscaledTime + 12f };
                return null;
            }
            switch (request.command)
            {
                case "status": return Snapshot(request.id, true, "Ready");
                case "peers": return Snapshot(request.id, true, "Peer positions and input counters");
                case "errors": return Snapshot(request.id, true, "Recent Unity errors on this player");
                case "list_tuning":
                    var controls = new List<string>();
                    foreach (var control in DevControl.All)
                        controls.Add(control.field.Name + " [" + control.min + ", " + control.max + "] " + control.scope);
                    var listed = Snapshot(request.id, true, "Available runtime tuning fields");
                    listed.tuningControls = string.Join("\n", controls);
                    return listed;
                case "get_tuning": return TuningResponse(request.id, request.field, false, 0);
                case "set_tuning": return TuningResponse(request.id, request.field, true, request.value);
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
                case "nudge_car":
                    if (!game || !game.IsPlaying || !game.Player || !game.Player.Body)
                        return Snapshot(request.id, false, "A local player in a sortie is required");
                    if (float.IsNaN(request.moveX) || float.IsNaN(request.moveY) ||
                        float.IsInfinity(request.moveX) || float.IsInfinity(request.moveY) ||
                        new Vector2(request.moveX, request.moveY).magnitude > 20f)
                        return Snapshot(request.id, false, "Nudge must be a finite world X/Z offset within 20 metres");
                    var body = game.Player.Body;
                    body.position += new Vector3(request.moveX, 0, request.moveY);
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                    Physics.SyncTransforms();
                    CameraController.Instance?.Snap();
                    return Snapshot(request.id, true, "Car nudged to a repeatable test position");
                case "stop":
                    if (InputManager.Instance) InputManager.Instance.StopAutomationDrive();
                    return Snapshot(request.id, true, "Stopped automation input");
                case "leave_coop":
                    if (!coop || (!coop.IsHost && !coop.IsClient)) return Snapshot(request.id, false, "Not in co-op");
                    bool wasHost = coop.IsHost;
                    coop.Leave();
                    if (wasHost && game && game.IsPlaying) game.ReturnToGarage();
                    return Snapshot(request.id, true, "Left the co-op room");
                case "return_garage":
                    if (!game || (game.State != GameState.Playing && game.State != GameState.Paused &&
                        game.State != GameState.Won && game.State != GameState.Lost) || (coop && coop.IsClient))
                        return Snapshot(request.id, false, "A host or solo sortie is required");
                    game.ReturnToGarage();
                    return Snapshot(request.id, true, "Returned to the garage");
                case "ready":
                case "unready":
                    if (!coop || !coop.InLobby) return Snapshot(request.id, false, "A co-op garage lobby is required");
                    coop.SetReady(request.command == "ready");
                    return Snapshot(request.id, true, request.command == "ready" ? "Ready" : "Choosing loadout");
                case "screenshot":
                    string screenshot = Path.Combine(root, "screen-" + request.id + ".png");
                    ScreenCapture.CaptureScreenshot(screenshot);
                    var captured = Snapshot(request.id, true, "Screenshot queued");
                    captured.screenshotPath = screenshot;
                    return captured;
                default: return Snapshot(request.id, false, "Unknown bridge command");
            }
        }

        static bool RemoteAllowed(string command)
        {
            switch (command)
            {
                case "status": case "peers": case "errors": case "drive": case "stop": case "nudge_car":
                case "list_tuning": case "get_tuning": case "set_tuning":
                case "cooplog": case "coopbench": case "update_game": case "ready": case "unready": return true;
                default: return false;
            }
        }

        /// <summary>Only the connected host may reach this through the co-op automation handler.</summary>
        public string ExecuteRemote(string json)
        {
            try
            {
                if (json.Length > 4096) throw new InvalidDataException("Remote request exceeds 4 KB");
                var request = JsonUtility.FromJson<Request>(json);
                if (request == null || !Guid.TryParseExact(request.id, "N", out _) || !RemoteAllowed(request.command))
                    throw new InvalidDataException("Remote command is not allowed");
                if (request.command == "update_game")
                {
                    if (updateRunning || !GameUpdater.Instance || GameUpdater.Instance.InstallBlocker != null)
                        return JsonUtility.ToJson(Snapshot(request.id, false, "Updater is unavailable or busy"));
                    updateRunning = true;
                    updateResult = "Leaving co-op to update";
                    StartCoroutine(LeaveAndUpdate());
                    return JsonUtility.ToJson(Snapshot(request.id, true, "Guest update started; this player will leave co-op and restart"));
                }
                return JsonUtility.ToJson(Run(request));
            }
            catch (Exception e) { return JsonUtility.ToJson(Snapshot("remote", false, e.Message)); }
        }

        IEnumerator LeaveAndUpdate()
        {
            // Let the reliable response leave before shutting down this transport.
            yield return null;
            CoopSession.Instance?.Leave();
            float deadline = Time.unscaledTime + 10f;
            while (Time.unscaledTime < deadline && GameManager.Instance &&
                   GameManager.Instance.State != GameState.MainMenu && GameManager.Instance.State != GameState.Garage)
                yield return null;
            if (!GameManager.Instance || (GameManager.Instance.State != GameState.MainMenu &&
                GameManager.Instance.State != GameState.Garage))
            {
                updateResult = "Could not return to the menu for update";
                updateRunning = false;
                yield break;
            }
            yield return UpdateLatest();
        }

        public void CompleteGuestRequest(string id, ulong sender, string json)
        {
            if (!pendingGuests.TryGetValue(id, out var pending) || pending.clientId != sender) return;
            pendingGuests.Remove(id);
            Response response;
            try { response = JsonUtility.FromJson<Response>(json); }
            catch { response = null; }
            if (response == null) response = Snapshot(id, false, "Guest returned an invalid response");
            response.id = id;
            response.guestSlot = pending.slot;
            WriteAtomic(Path.Combine(responses, id + ".json"), JsonUtility.ToJson(response));
        }

        Response TuningResponse(string id, string field, bool set, float value)
        {
            DevControl selected = null;
            foreach (var control in DevControl.All)
                if (string.Equals(control.field.Name, field, StringComparison.OrdinalIgnoreCase)) { selected = control; break; }
            if (selected == null) return Snapshot(id, false, "Unknown tuning field; call list_tuning");
            if (set)
            {
                if (float.IsNaN(value) || float.IsInfinity(value) || value < selected.min || value > selected.max)
                    return Snapshot(id, false, "Value must be within the field's finite allowed range");
                if (CoopSession.IsRemoteClient && selected.scope == DevScope.Shared)
                    return Snapshot(id, false, "Shared tuning is controlled by the host");
                selected.field.SetValue(DevTuning.Local, value);
                DevTuning.Apply();
            }
            var result = Snapshot(id, true, set ? "Runtime tuning applied" : "Runtime tuning value");
            result.tuningField = selected.field.Name;
            result.tuningScope = selected.scope.ToString();
            result.tuningValue = (float)selected.field.GetValue(DevTuning.Local);
            result.tuningMinimum = selected.min;
            result.tuningMaximum = selected.max;
            return result;
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
                playerContactsSent = coop ? coop.PlayerContactsSent : 0,
                playerContactsApplied = coop ? coop.PlayerContactsApplied : 0,
                tracePath = coop ? coop.LastTracePath : "", benchmarkId = coop ? coop.LastBenchmarkId : "",
                benchmarkRunning = coop && coop.BenchmarkRunning,
                x = p.x, y = p.y, z = p.z, speedKph = car ? car.SpeedKph : 0,
                updaterPhase = updater ? updater.State.ToString() : "Unavailable",
                updaterStatus = updater ? updater.Status : "Unavailable", updateResult = updateResult,
                currentBuild = updater && updater.Current != null ? updater.Current.ShortCommit : "unpublished",
                latestBuild = updater && updater.Builds.Count > 0 ? updater.Builds[0].ShortCommit : "",
                updateProgress = updater ? updater.Progress : 0,
                updateAvailable = updater && updater.UpdateAvailable, updateRunning = updateRunning,
                peerSnapshot = coop ? coop.BridgePeerSnapshot() : "{\"peers\":[]}",
                recentErrors = string.Join("\n", recentErrors.ToArray())
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
