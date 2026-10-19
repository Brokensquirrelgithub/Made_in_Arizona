using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace MadeInArizona
{
    public sealed partial class CoopSession
    {
        const string BenchmarkRequestMessage = "mia.coop.benchmark-request.v1";
        const string BenchmarkStartMessage = "mia.coop.benchmark-start.v1";
        const string BenchmarkReadyMessage = "mia.coop.benchmark-ready.v1";
        const string BenchmarkRunMessage = "mia.coop.benchmark-run.v1";
        const string BenchmarkProbeMessage = "mia.coop.benchmark-probe.v1";
        const string BenchmarkEchoMessage = "mia.coop.benchmark-echo.v1";
        const float BenchmarkProbeSeconds = 12f;
        const float BenchmarkProbeInterval = .05f;
        const float BenchmarkCaptureDelay = 2f;

        [Serializable] sealed class BenchmarkStartPacket { public int protocol; public string id; }
        [Serializable] sealed class BenchmarkProbePacket { public string id; public int sequence; public float sentAt; }

        readonly HashSet<ulong> benchmarkPeers = new HashSet<ulong>();
        readonly HashSet<ulong> benchmarkReadyPeers = new HashSet<ulong>();
        string benchmarkId;
        bool benchmarkRunning;
        int benchmarkSequence;
        Coroutine benchmarkCoroutine;
        public bool BenchmarkRunning => benchmarkRunning;
        public string LastBenchmarkId => benchmarkId;

        void RegisterBenchmarkMessages(CustomMessagingManager messages)
        {
            messages.RegisterNamedMessageHandler(BenchmarkRequestMessage, ReceiveBenchmarkRequest);
            messages.RegisterNamedMessageHandler(BenchmarkStartMessage, ReceiveBenchmarkStart);
            messages.RegisterNamedMessageHandler(BenchmarkReadyMessage, ReceiveBenchmarkReady);
            messages.RegisterNamedMessageHandler(BenchmarkRunMessage, ReceiveBenchmarkRun);
            messages.RegisterNamedMessageHandler(BenchmarkProbeMessage, ReceiveBenchmarkProbe);
            messages.RegisterNamedMessageHandler(BenchmarkEchoMessage, ReceiveBenchmarkEcho);
        }

        /// <summary>Run a 12-second bidirectional datagram probe, then collect all peers' rolling traces.</summary>
        public string RequestCoopBenchmark()
        {
            if ((!IsHost && !IsClient) || !network.IsListening || !GameManager.Instance ||
                !GameManager.Instance.IsPlaying || (IsHost && PlayerCount < 2))
                return "Start a co-op sortie with at least two connected players first.";
            if (benchmarkRunning) return "A co-op benchmark is already running.";
            if (IsHost) return StartCoopBenchmark();
            Send(BenchmarkRequestMessage, NetworkManager.ServerClientId,
                new ReadyPacket { protocol = Protocol }, NetworkDelivery.ReliableSequenced);
            return "Requested co-op benchmark from host. It will collect every player's log in about 14 seconds.";
        }

        void ReceiveBenchmarkRequest(ulong sender, FastBufferReader reader)
        {
            if (!IsHost || sender == NetworkManager.ServerClientId ||
                !network.ConnectedClients.ContainsKey(sender)) return;
            reader.ReadValueSafe(out string json);
            var request = JsonUtility.FromJson<ReadyPacket>(json);
            if (request != null && request.protocol == Protocol) StartCoopBenchmark();
        }

        string StartCoopBenchmark()
        {
            if (!IsHost || benchmarkRunning || expectedTraceClients.Count > receivedTraceClients.Count ||
                !GameManager.Instance ||
                !GameManager.Instance.IsPlaying || PlayerCount < 2)
                return benchmarkRunning ? "A co-op benchmark is already running." :
                    expectedTraceClients.Count > receivedTraceClients.Count ? "Previous co-op trace is still uploading." :
                    "Co-op sortie unavailable.";
            benchmarkId = NewTraceId();
            benchmarkSequence = 0;
            benchmarkPeers.Clear();
            benchmarkReadyPeers.Clear();
            var packet = new BenchmarkStartPacket { protocol = Protocol, id = benchmarkId };
            foreach (ulong clientId in network.ConnectedClientsIds)
                if (clientId != NetworkManager.ServerClientId)
                {
                    benchmarkPeers.Add(clientId);
                    Send(BenchmarkStartMessage, clientId, packet, NetworkDelivery.ReliableSequenced);
                }
            benchmarkRunning = true;
            Debug.Log("MIA_COOP_BENCH_ARMED: " + benchmarkId + " peers=" + benchmarkPeers.Count);
            benchmarkCoroutine = StartCoroutine(WaitForBenchmarkReady());
            return "Co-op benchmark started for all connected players. Logs will be collected in about 14 seconds.";
        }

        void ReceiveBenchmarkStart(ulong sender, FastBufferReader reader)
        {
            if (!IsClient || sender != NetworkManager.ServerClientId || benchmarkRunning) return;
            reader.ReadValueSafe(out string json);
            var packet = JsonUtility.FromJson<BenchmarkStartPacket>(json);
            if (packet == null || packet.protocol != Protocol || !ValidTraceId(packet.id)) return;
            benchmarkId = packet.id;
            benchmarkSequence = 0;
            benchmarkPeers.Clear();
            benchmarkPeers.Add(sender);
            benchmarkRunning = true;
            Send(BenchmarkReadyMessage, sender, packet, NetworkDelivery.ReliableSequenced);
            Debug.Log("MIA_COOP_BENCH_ARMED: " + benchmarkId + " peers=1");
        }

        void ReceiveBenchmarkReady(ulong sender, FastBufferReader reader)
        {
            if (!IsHost || !benchmarkRunning || !benchmarkPeers.Contains(sender)) return;
            reader.ReadValueSafe(out string json);
            var packet = JsonUtility.FromJson<BenchmarkStartPacket>(json);
            if (packet != null && packet.protocol == Protocol && packet.id == benchmarkId)
                benchmarkReadyPeers.Add(sender);
        }

        IEnumerator WaitForBenchmarkReady()
        {
            float deadline = Time.unscaledTime + 8f;
            while (benchmarkRunning && network && network.IsListening &&
                benchmarkReadyPeers.Count < benchmarkPeers.Count && Time.unscaledTime < deadline)
                yield return null;
            if (!benchmarkRunning || !network || !network.IsListening) yield break;
            if (benchmarkReadyPeers.Count < benchmarkPeers.Count)
            {
                Debug.LogError("MIA_COOP_BENCH_FAILED: not all peers acknowledged benchmark start.");
                benchmarkRunning = false;
                benchmarkCoroutine = null;
                yield break;
            }
            var packet = new BenchmarkStartPacket { protocol = Protocol, id = benchmarkId };
            foreach (ulong peer in benchmarkPeers)
                Send(BenchmarkRunMessage, peer, packet, NetworkDelivery.ReliableSequenced);
            Debug.Log("MIA_COOP_BENCH_STARTED: " + benchmarkId + " peers=" + benchmarkPeers.Count);
            yield return RunBenchmark(true);
        }

        void ReceiveBenchmarkRun(ulong sender, FastBufferReader reader)
        {
            if (!IsClient || sender != NetworkManager.ServerClientId || !benchmarkRunning ||
                benchmarkCoroutine != null) return;
            reader.ReadValueSafe(out string json);
            var packet = JsonUtility.FromJson<BenchmarkStartPacket>(json);
            if (packet == null || packet.protocol != Protocol || packet.id != benchmarkId) return;
            Debug.Log("MIA_COOP_BENCH_STARTED: " + benchmarkId + " peers=1");
            benchmarkCoroutine = StartCoroutine(RunBenchmark(false));
        }

        IEnumerator RunBenchmark(bool host)
        {
            AddTrace(MakeTrace("bench_start", (int)network.LocalClientId, 0, null));
            float endAt = Time.unscaledTime + BenchmarkProbeSeconds;
            float nextAt = Time.unscaledTime;
            float nextFrameSampleAt = Time.unscaledTime + .1f;
            float maxFrameMilliseconds = 0;
            while (benchmarkRunning && network && network.IsListening && Time.unscaledTime < endAt)
            {
                maxFrameMilliseconds = Mathf.Max(maxFrameMilliseconds, Time.unscaledDeltaTime * 1000);
                if (Time.unscaledTime >= nextFrameSampleAt)
                {
                    var frame = MakeTrace("bench_frame", (int)network.LocalClientId, 0, null);
                    frame.leadMilliseconds = maxFrameMilliseconds;
                    AddTrace(frame);
                    maxFrameMilliseconds = 0;
                    nextFrameSampleAt = Time.unscaledTime + .1f;
                }
                if (Time.unscaledTime >= nextAt)
                {
                    int sequence = ++benchmarkSequence;
                    var probe = new BenchmarkProbePacket { id = benchmarkId, sequence = sequence,
                        sentAt = Time.unscaledTime };
                    foreach (ulong peer in benchmarkPeers)
                    {
                        Send(BenchmarkProbeMessage, peer, probe, NetworkDelivery.Unreliable);
                        var row = MakeTrace("bench_sent", (int)network.LocalClientId, (int)peer, null);
                        row.inputSent = sequence;
                        AddTrace(row);
                    }
                    nextAt += BenchmarkProbeInterval;
                    if (nextAt < Time.unscaledTime) nextAt = Time.unscaledTime + BenchmarkProbeInterval;
                }
                yield return null;
            }
            if (!benchmarkRunning) yield break;
            AddTrace(MakeTrace("bench_end", (int)network.LocalClientId, 0, null));
            if (host)
            {
                yield return new WaitForSecondsRealtime(BenchmarkCaptureDelay);
                if (benchmarkRunning && IsHost && network && network.IsListening)
                {
                    TriggerTraceCapture(benchmarkId);
                    Debug.Log("MIA_COOP_BENCH_CAPTURED: " + LastTracePath);
                }
            }
            benchmarkRunning = false;
            benchmarkCoroutine = null;
        }

        void ReceiveBenchmarkProbe(ulong sender, FastBufferReader reader)
        {
            if (!benchmarkRunning || !benchmarkPeers.Contains(sender)) return;
            reader.ReadValueSafe(out string json);
            var probe = JsonUtility.FromJson<BenchmarkProbePacket>(json);
            if (probe == null || probe.id != benchmarkId || probe.sequence <= 0) return;
            var row = MakeTrace("bench_received", (int)sender, (int)network.LocalClientId, null);
            row.inputSent = probe.sequence;
            AddTrace(row);
            Send(BenchmarkEchoMessage, sender, probe, NetworkDelivery.Unreliable);
        }

        void ReceiveBenchmarkEcho(ulong sender, FastBufferReader reader)
        {
            if (!benchmarkRunning || !benchmarkPeers.Contains(sender)) return;
            reader.ReadValueSafe(out string json);
            var probe = JsonUtility.FromJson<BenchmarkProbePacket>(json);
            if (probe == null || probe.id != benchmarkId || probe.sequence <= 0 ||
                probe.sequence > benchmarkSequence) return;
            var row = MakeTrace("bench_rtt", (int)network.LocalClientId, (int)sender, null);
            row.inputSent = probe.sequence;
            row.leadMilliseconds = Mathf.Max(0, Time.unscaledTime - probe.sentAt) * 1000;
            AddTrace(row);
        }

        void ResetBenchmark()
        {
            if (benchmarkCoroutine != null) StopCoroutine(benchmarkCoroutine);
            benchmarkCoroutine = null;
            benchmarkPeers.Clear();
            benchmarkReadyPeers.Clear();
            benchmarkRunning = false;
            benchmarkId = null;
            benchmarkSequence = 0;
        }
    }
}
