using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace MadeInArizona
{
    public sealed partial class CoopSession
    {
        const double TraceRetentionSeconds = 20;
        const double TraceCaptureSeconds = 15;
        const int TraceMaxRows = 16384;
        const int TraceChunkCharacters = 4096;
        const int TraceMaxChunks = 256;
        const int TraceMaxBytes = 4 * 1024 * 1024;
        const string TraceChunkMessage = "mia.coop.trace-chunk.v1";
        const string TraceReceiptMessage = "mia.coop.trace-receipt.v1";

        [Serializable] sealed class TraceCapturePacket { public int protocol; public string id; public double serverTime; }
        [Serializable] sealed class TraceChunkPacket { public string id, data; public int index, count; }
        [Serializable] sealed class TraceReceiptPacket { public string id; }
        sealed class IncomingTrace { public string[] chunks; public int received; }
        struct TraceRow
        {
            public double serverTime;
            public float localTime;
            public int frame, owner, vehicleId, inputSent, inputReceived, inputApplied, snapshotTick, replaySteps;
            public string kind;
            public Vector3 position, velocity, referencePosition, referenceVelocity;
            public float yaw, referenceYaw, errorMeters, yawErrorDegrees, leadMilliseconds;
        }

        readonly Queue<TraceRow> traceRows = new Queue<TraceRow>();
        readonly Dictionary<ulong, IncomingTrace> incomingTraces = new Dictionary<ulong, IncomingTrace>();
        readonly HashSet<ulong> expectedTraceClients = new HashSet<ulong>();
        readonly HashSet<ulong> receivedTraceClients = new HashSet<ulong>();
        string activeTraceId, activeTraceDirectory, lastSavedTraceId;
        double activeTraceTime;
        int ownVehicleId, ownSnapshotTick, ownAppliedSequence;
        float lastOwnSnapshotAt;
        public string LastTracePath { get; private set; }
        public bool LastTraceUploadConfirmed { get; private set; }

        void RegisterTraceMessages(CustomMessagingManager messages)
        {
            messages.RegisterNamedMessageHandler(TraceChunkMessage, ReceiveTraceChunk);
            messages.RegisterNamedMessageHandler(TraceReceiptMessage, ReceiveTraceReceipt);
            RegisterBenchmarkMessages(messages);
            RegisterPingMessages(messages);
        }

        /// <summary>Save the previous 15 seconds on every peer and collect all guest files at the host.</summary>
        public string RequestDiagnosticCapture()
        {
            if ((!IsHost && !IsClient) || !network.IsListening) return "Join or host a co-op session first.";
            if (benchmarkRunning) return "A co-op benchmark is running; its capture will save shortly.";
            if (IsHost)
            {
                TriggerTraceCapture();
                return "Co-op trace saved and guest logs requested. Folder: " + LastTracePath;
            }
            Send(TraceRequestMessage, NetworkManager.ServerClientId,
                new ReadyPacket { protocol = Protocol }, NetworkDelivery.ReliableSequenced);
            return "Requested a synchronized co-op trace from the host. Type cooplog path to see your local copy.";
        }

        void ReceiveTraceRequest(ulong sender, FastBufferReader reader)
        {
            if (!IsHost || sender == NetworkManager.ServerClientId || !network.ConnectedClients.ContainsKey(sender)) return;
            reader.ReadValueSafe(out string json);
            var request = JsonUtility.FromJson<ReadyPacket>(json);
            if (request != null && request.protocol == Protocol && !benchmarkRunning) TriggerTraceCapture();
        }

        void TriggerTraceCapture(string captureId = null)
        {
            var packet = new TraceCapturePacket {
                protocol = Protocol,
                id = ValidTraceId(captureId) ? captureId : NewTraceId(),
                serverTime = network.ServerTime.Time
            };
            activeTraceId = packet.id;
            activeTraceTime = packet.serverTime;
            activeTraceDirectory = TraceDirectory(packet.id);
            incomingTraces.Clear(); expectedTraceClients.Clear(); receivedTraceClients.Clear();
            foreach (ulong clientId in network.ConnectedClientsIds)
                if (clientId != NetworkManager.ServerClientId)
                {
                    expectedTraceClients.Add(clientId);
                    Send(TraceCaptureMessage, clientId, packet, NetworkDelivery.ReliableSequenced);
                }
            SaveLocalTrace(packet);
            WriteTraceManifest();
            WriteBenchmarkReport();
        }

        static string NewTraceId() => DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ", CultureInfo.InvariantCulture) +
            "_" + Guid.NewGuid().ToString("N").Substring(0, 8);

        void ReceiveTraceCapture(ulong sender, FastBufferReader reader)
        {
            if (!IsClient || sender != NetworkManager.ServerClientId) return;
            reader.ReadValueSafe(out string json);
            var packet = JsonUtility.FromJson<TraceCapturePacket>(json);
            if (packet == null || packet.protocol != Protocol || !ValidTraceId(packet.id) || packet.id == lastSavedTraceId) return;
            string csv = SaveLocalTrace(packet);
            if (csv != null) StartCoroutine(UploadTrace(packet.id, csv));
        }

        string SaveLocalTrace(TraceCapturePacket packet)
        {
            if (!ValidTraceId(packet.id)) return null;
            lastSavedTraceId = packet.id;
            string csv = FormatTrace(packet.serverTime);
            LastTraceUploadConfirmed = false;
            string directory = TraceDirectory(packet.id);
            string role = IsHost ? "host" : "guest";
            string path = Path.Combine(directory, role + "-" + network.LocalClientId + ".csv");
            try
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(path, csv, new UTF8Encoding(false));
                LastTracePath = directory;
                Debug.Log("MIA_COOP_TRACE_SAVED: " + path);
            }
            catch (Exception exception) { Debug.LogError("MIA_COOP_TRACE_SAVE_FAILED: " + exception.Message); }
            return csv;
        }

        IEnumerator UploadTrace(string id, string csv)
        {
            string encoded;
            using (var stream = new MemoryStream())
            {
                using (var zip = new GZipStream(stream, System.IO.Compression.CompressionLevel.Fastest, true))
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(csv);
                    zip.Write(bytes, 0, bytes.Length);
                }
                encoded = Convert.ToBase64String(stream.ToArray());
            }
            int count = (encoded.Length + TraceChunkCharacters - 1) / TraceChunkCharacters;
            if (count > TraceMaxChunks) { Debug.LogError("MIA_COOP_TRACE_UPLOAD_FAILED: trace exceeds chunk limit."); yield break; }
            for (int index = 0; index < count && IsClient && network.IsListening; index++)
            {
                int offset = index * TraceChunkCharacters;
                Send(TraceChunkMessage, NetworkManager.ServerClientId,
                    new TraceChunkPacket { id = id, index = index, count = count,
                        data = encoded.Substring(offset, Math.Min(TraceChunkCharacters, encoded.Length - offset)) },
                    NetworkDelivery.ReliableFragmentedSequenced);
                yield return null;
            }
        }

        void ReceiveTraceChunk(ulong sender, FastBufferReader reader)
        {
            if (!IsHost || sender == NetworkManager.ServerClientId || !expectedTraceClients.Contains(sender) ||
                receivedTraceClients.Contains(sender)) return;
            reader.ReadValueSafe(out string json);
            var packet = JsonUtility.FromJson<TraceChunkPacket>(json);
            if (packet == null || packet.id != activeTraceId || packet.count <= 0 || packet.count > TraceMaxChunks ||
                packet.index < 0 || packet.index >= packet.count || string.IsNullOrEmpty(packet.data) ||
                packet.data.Length > TraceChunkCharacters) return;
            if (!incomingTraces.TryGetValue(sender, out var incoming) || incoming.chunks.Length != packet.count)
            {
                incoming = new IncomingTrace { chunks = new string[packet.count] };
                incomingTraces[sender] = incoming;
            }
            if (incoming.chunks[packet.index] == null)
            {
                incoming.chunks[packet.index] = packet.data;
                incoming.received++;
            }
            if (incoming.received != incoming.chunks.Length) return;
            incomingTraces.Remove(sender);
            try
            {
                byte[] compressed = Convert.FromBase64String(string.Concat(incoming.chunks));
                using (var stream = new GZipStream(new MemoryStream(compressed), CompressionMode.Decompress))
                using (var output = new MemoryStream())
                {
                    byte[] buffer = new byte[8192];
                    int read;
                    while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (output.Length + read > TraceMaxBytes) throw new InvalidDataException("Trace exceeds size limit.");
                        output.Write(buffer, 0, read);
                    }
                    Directory.CreateDirectory(activeTraceDirectory);
                    string path = Path.Combine(activeTraceDirectory, "guest-" + sender + ".csv");
                    File.WriteAllBytes(path, output.ToArray());
                    receivedTraceClients.Add(sender);
                    WriteTraceManifest();
                    WriteBenchmarkReport();
                    Debug.Log("MIA_COOP_TRACE_COLLECTED: " + path);
                    Send(TraceReceiptMessage, sender, new TraceReceiptPacket { id = packet.id }, NetworkDelivery.ReliableSequenced);
                }
            }
            catch (Exception exception) { Debug.LogError("MIA_COOP_TRACE_COLLECT_FAILED: " + exception.Message); }
        }

        void ReceiveTraceReceipt(ulong sender, FastBufferReader reader)
        {
            if (!IsClient || sender != NetworkManager.ServerClientId) return;
            reader.ReadValueSafe(out string json);
            var packet = JsonUtility.FromJson<TraceReceiptPacket>(json);
            if (packet == null || packet.id != lastSavedTraceId) return;
            LastTraceUploadConfirmed = true;
            Debug.Log("MIA_COOP_TRACE_UPLOADED: " + LastTracePath);
        }

        void WriteTraceManifest()
        {
            if (!IsHost || string.IsNullOrEmpty(activeTraceDirectory)) return;
            var lines = new List<string> {
                "Made in Arizona co-op trace v1",
                "capture_id=" + activeTraceId,
                "server_time=" + activeTraceTime.ToString("F6", CultureInfo.InvariantCulture),
                "window_seconds=" + TraceCaptureSeconds.ToString(CultureInfo.InvariantCulture),
                "host_file=host-" + network.LocalClientId + ".csv"
            };
            if (activeTraceId == benchmarkId)
            {
                lines.Add("type=coopbench");
                lines.Add("probe_duration_seconds=" + BenchmarkProbeSeconds.ToString(CultureInfo.InvariantCulture));
                lines.Add("probe_interval_seconds=" + BenchmarkProbeInterval.ToString(CultureInfo.InvariantCulture));
            }
            foreach (ulong clientId in expectedTraceClients)
                lines.Add("guest_" + clientId + "=" + (receivedTraceClients.Contains(clientId) ? "received" : "pending"));
            try { File.WriteAllLines(Path.Combine(activeTraceDirectory, "manifest.txt"), lines); }
            catch (Exception exception) { Debug.LogError("MIA_COOP_TRACE_MANIFEST_FAILED: " + exception.Message); }
        }

        static bool ValidTraceId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 64) return false;
            foreach (char c in id)
                if (!char.IsLetterOrDigit(c) && c != '_' && c != '-') return false;
            return true;
        }

        static string TraceDirectory(string id)
        {
            string root = SmokeTestRunner.Active ? Path.Combine(Application.temporaryCachePath, "IntegrationTestCoopDiagnostics") :
                Path.Combine(Application.persistentDataPath, "CoopDiagnostics");
            return Path.Combine(root, id);
        }

        void FixedUpdate()
        {
            if ((!IsHost && !IsClient) || !GameManager.Instance || !GameManager.Instance.IsPlaying) return;
            if (IsHost)
            {
                var local = GameManager.Instance.Player;
                if (local) AddTrace(MakeTrace("physics", (int)NetworkManager.ServerClientId,
                    vehicleIds.TryGetValue(local, out int localId) ? localId : 0, local));
                foreach (var pair in guestCars)
                    if (pair.Value) AddTrace(MakeTrace("physics", (int)pair.Key,
                        vehicleIds.TryGetValue(pair.Value, out int id) ? id : 0, pair.Value));
            }
            else
            {
                var local = GameManager.Instance.Player;
                if (local && local.IsLocallyPredicted)
                {
                    var row = MakeTrace("physics", (int)network.LocalClientId, ownVehicleId, local);
                    row.inputSent = inputSequence;
                    row.inputApplied = ownAppliedSequence;
                    row.snapshotTick = ownSnapshotTick;
                    row.leadMilliseconds = (Time.unscaledTime - lastOwnSnapshotAt) * 1000;
                    AddTrace(row);
                }
            }
        }

        TraceRow MakeTrace(string kind, int owner, int id, VehicleController car)
        {
            var row = new TraceRow { kind = kind, serverTime = network.ServerTime.Time,
                localTime = Time.unscaledTime, frame = Time.frameCount, owner = owner, vehicleId = id };
            if (car)
            {
                row.position = car.Body ? car.Body.position : car.transform.position;
                row.velocity = car.Body ? car.Body.linearVelocity : Vector3.zero;
                row.yaw = car.Body ? car.Body.rotation.eulerAngles.y : car.transform.eulerAngles.y;
                row.inputReceived = car.HasRemoteInput ? car.RemoteControls.sequence : 0;
                row.inputApplied = car.LastAppliedInputSequence;
                row.replaySteps = car.LastPredictionReplaySteps;
            }
            return row;
        }

        void RecordInputSent(int sequence)
        {
            if (!IsClient) return;
            var row = MakeTrace("input_sent", (int)network.LocalClientId, ownVehicleId, GameManager.Instance.Player);
            row.inputSent = sequence;
            AddTrace(row);
        }

        void RecordInputReceived(ulong sender, VehicleController car, int sequence)
        {
            if (!IsHost) return;
            var row = MakeTrace("input_received", (int)sender,
                vehicleIds.TryGetValue(car, out int id) ? id : 0, car);
            row.inputReceived = sequence;
            AddTrace(row);
        }

        void RecordSnapshotSent(VehiclePacket packet, VehicleController car)
        {
            if (!IsHost) return;
            var row = MakeTrace("snapshot_sent", packet.owner, packet.id, car);
            row.inputApplied = packet.appliedInputSequence;
            row.snapshotTick = packet.tick;
            row.position = packet.position;
            row.velocity = packet.velocity;
            row.yaw = packet.rotation.eulerAngles.y;
            AddTrace(row);
        }

        void RecordOwnSnapshot(VehiclePacket packet, VehicleController car, float lead)
        {
            ownVehicleId = packet.id;
            ownSnapshotTick = packet.tick;
            ownAppliedSequence = packet.appliedInputSequence;
            lastOwnSnapshotAt = Time.unscaledTime;
            var row = MakeTrace("snapshot_received", packet.owner, packet.id, car);
            row.inputSent = inputSequence;
            row.inputApplied = packet.appliedInputSequence;
            row.snapshotTick = packet.tick;
            row.referencePosition = packet.position;
            row.referenceVelocity = packet.velocity;
            row.referenceYaw = packet.rotation.eulerAngles.y;
            row.leadMilliseconds = Mathf.Clamp(lead, 0, .3f) * 1000;
            row.errorMeters = Vector3.Distance(row.position, packet.position + packet.velocity * Mathf.Clamp(lead, 0, .3f));
            row.yawErrorDegrees = Mathf.DeltaAngle(row.yaw,
                (packet.rotation * Quaternion.Euler(0, packet.yawRate * Mathf.Rad2Deg * Mathf.Clamp(lead, 0, .3f), 0)).eulerAngles.y);
            AddTrace(row);
        }

        void AddTrace(TraceRow row)
        {
            traceRows.Enqueue(row);
            while (traceRows.Count > 0 &&
                (traceRows.Count > TraceMaxRows || row.serverTime - traceRows.Peek().serverTime > TraceRetentionSeconds))
                traceRows.Dequeue();
        }

        string FormatTrace(double triggerTime)
        {
            var csv = new StringBuilder(262144);
            csv.AppendLine("kind,server_time,local_time,frame,owner,vehicle_id,input_sent,input_received,input_applied,snapshot_tick,px,py,pz,vx,vy,vz,yaw,host_px,host_py,host_pz,host_vx,host_vy,host_vz,host_yaw,error_m,yaw_error_deg,lead_ms,replay_steps");
            foreach (var row in traceRows)
            {
                if (row.serverTime < triggerTime - TraceCaptureSeconds || row.serverTime > triggerTime) continue;
                csv.Append(row.kind).Append(','); Number(csv, row.serverTime); csv.Append(','); Number(csv, row.localTime);
                csv.Append(',').Append(row.frame).Append(',').Append(row.owner).Append(',').Append(row.vehicleId)
                    .Append(',').Append(row.inputSent).Append(',').Append(row.inputReceived).Append(',')
                    .Append(row.inputApplied).Append(',').Append(row.snapshotTick).Append(',');
                Vector(csv, row.position); csv.Append(','); Vector(csv, row.velocity); csv.Append(','); Number(csv, row.yaw);
                csv.Append(','); Vector(csv, row.referencePosition); csv.Append(','); Vector(csv, row.referenceVelocity);
                csv.Append(','); Number(csv, row.referenceYaw); csv.Append(','); Number(csv, row.errorMeters);
                csv.Append(','); Number(csv, row.yawErrorDegrees); csv.Append(','); Number(csv, row.leadMilliseconds);
                csv.Append(',').Append(row.replaySteps);
                csv.AppendLine();
            }
            return csv.ToString();
        }

        static void Vector(StringBuilder csv, Vector3 value)
        {
            Number(csv, value.x); csv.Append(','); Number(csv, value.y); csv.Append(','); Number(csv, value.z);
        }
        static void Number(StringBuilder csv, float value) => csv.Append(value.ToString("F4", CultureInfo.InvariantCulture));
        static void Number(StringBuilder csv, double value) => csv.Append(value.ToString("F6", CultureInfo.InvariantCulture));

        void ResetTrace()
        {
            ResetBenchmark();
            ResetPing();
            traceRows.Clear(); incomingTraces.Clear(); expectedTraceClients.Clear(); receivedTraceClients.Clear();
            activeTraceId = activeTraceDirectory = lastSavedTraceId = null;
            activeTraceTime = 0;
            ownVehicleId = ownSnapshotTick = ownAppliedSequence = 0;
            lastOwnSnapshotAt = 0;
            LastTraceUploadConfirmed = false;
        }
    }
}
