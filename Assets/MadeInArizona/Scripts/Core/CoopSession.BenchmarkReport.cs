using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace MadeInArizona
{
    public sealed partial class CoopSession
    {
        sealed class BenchTrace
        {
            public bool started, ended;
            public readonly Dictionary<int, HashSet<int>> sent = new Dictionary<int, HashSet<int>>();
            public readonly Dictionary<int, HashSet<int>> received = new Dictionary<int, HashSet<int>>();
            public readonly Dictionary<int, List<float>> rtts = new Dictionary<int, List<float>>();
            public readonly List<float> frames = new List<float>();
            public readonly Dictionary<int, List<float>> ping = new Dictionary<int, List<float>>();
            public readonly List<float> snapshotError = new List<float>();
            public int inputSent, snapshotReceived;
        }

        static void AddSequence(Dictionary<int, HashSet<int>> routes, int peer, int sequence)
        {
            if (!routes.TryGetValue(peer, out var values)) routes[peer] = values = new HashSet<int>();
            values.Add(sequence);
        }

        static void AddSample(Dictionary<int, List<float>> routes, int peer, float value)
        {
            if (!routes.TryGetValue(peer, out var values)) routes[peer] = values = new List<float>();
            values.Add(value);
        }

        static int Int(string value) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) ? result : 0;
        static float Float(string value) => float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result) ? result : 0;

        static BenchTrace ReadBenchTrace(string path)
        {
            if (!File.Exists(path)) return null;
            var trace = new BenchTrace();
            foreach (string line in File.ReadLines(path).Skip(1))
            {
                string[] fields = line.Split(',');
                if (fields.Length < 28) continue;
                int peer = Int(fields[5]);
                switch (fields[0])
                {
                    case "bench_start": trace.started = true; break;
                    case "bench_end": trace.ended = true; break;
                    case "bench_sent": AddSequence(trace.sent, peer, Int(fields[6])); break;
                    case "bench_received": AddSequence(trace.received, Int(fields[4]), Int(fields[6])); break;
                    case "bench_rtt": AddSample(trace.rtts, peer, Float(fields[26])); break;
                    case "bench_frame": trace.frames.Add(Float(fields[26])); break;
                    case "live_ping":
                        if (trace.started && !trace.ended) AddSample(trace.ping, peer, Float(fields[26]));
                        break;
                    case "input_sent": trace.inputSent++; break;
                    case "snapshot_received":
                        trace.snapshotReceived++;
                        trace.snapshotError.Add(Float(fields[24]));
                        break;
                }
            }
            return trace;
        }

        static float Percentile(List<float> values, float fraction)
        {
            if (values == null || values.Count == 0) return -1;
            var ordered = new List<float>(values);
            ordered.Sort();
            float position = fraction * (ordered.Count - 1);
            int lower = Mathf.FloorToInt(position);
            return Mathf.Lerp(ordered[lower], ordered[Mathf.Min(lower + 1, ordered.Count - 1)], position - lower);
        }

        static string Measure(float value, string unit) => value < 0 ? "n/a" :
            value.ToString("F1", CultureInfo.InvariantCulture) + unit;

        static float Maximum(List<float> values) => values == null || values.Count == 0 ? -1 : values.Max();

        static float RttVariationP95(List<float> values)
        {
            if (values == null || values.Count < 2) return -1;
            var changes = new List<float>(values.Count - 1);
            for (int index = 1; index < values.Count; index++)
                changes.Add(Mathf.Abs(values[index] - values[index - 1]));
            return Percentile(changes, .95f);
        }

        static bool AppendRoute(StringBuilder report, string label, BenchTrace sender, int receiverId,
            BenchTrace receiver, int senderId, List<string> flags)
        {
            sender.sent.TryGetValue(receiverId, out var sent);
            receiver.received.TryGetValue(senderId, out var received);
            int count = sent == null ? 0 : sent.Count;
            int delivered = sent == null || received == null ? 0 : sent.Count(received.Contains);
            float loss = count == 0 ? -1 : 100f * (count - delivered) / count;
            sender.rtts.TryGetValue(receiverId, out var rtts);
            float p95 = Percentile(rtts, .95f);
            report.AppendLine(label + ": " + delivered + "/" + count + " delivered, " +
                Measure(loss, "% loss") + ", RTT median " + Measure(Percentile(rtts, .5f), " ms") +
                ", p95 " + Measure(p95, " ms") +
                ", RTT variation p95 " + Measure(RttVariationP95(rtts), " ms"));
            if (loss > 5) flags.Add(label + " probe loss exceeds 5%");
            if (p95 > 250) flags.Add(label + " RTT p95 exceeds 250 ms");
            return count >= 180 && delivered > 0 && rtts != null && rtts.Count > 0;
        }

        void WriteBenchmarkReport()
        {
            if (!IsHost || activeTraceId != benchmarkId || string.IsNullOrEmpty(activeTraceDirectory)) return;
            try
            {
                int hostId = (int)network.LocalClientId;
                var host = ReadBenchTrace(Path.Combine(activeTraceDirectory, "host-" + hostId + ".csv"));
                var report = new StringBuilder();
                var flags = new List<string>();
                bool complete = host != null && host.started && host.ended;
                report.AppendLine("Made in Arizona co-op benchmark");
                report.AppendLine("capture_id=" + activeTraceId);
                report.AppendLine("players=" + (expectedTraceClients.Count + 1));
                report.AppendLine("Host " + hostId + ": " + (host == null ? "missing" :
                    "start=" + host.started + ", end=" + host.ended +
                    ", frame p95=" + Measure(Percentile(host.frames, .95f), " ms") +
                    ", max=" + Measure(Maximum(host.frames), " ms")));
                if (host != null && Percentile(host.frames, .95f) > 50) flags.Add("Host frame p95 exceeds 50 ms");
                foreach (ulong clientId in expectedTraceClients.OrderBy(id => id))
                {
                    int guestId = (int)clientId;
                    var guest = ReadBenchTrace(Path.Combine(activeTraceDirectory, "guest-" + clientId + ".csv"));
                    if (guest == null)
                    {
                        report.AppendLine("Guest " + clientId + ": upload pending");
                        complete = false;
                        continue;
                    }
                    guest.ping.TryGetValue(hostId, out var guestPing);
                    List<float> hostPing = null;
                    if (host != null) host.ping.TryGetValue(guestId, out hostPing);
                    report.AppendLine("Guest " + clientId + ": start=" + guest.started + ", end=" + guest.ended +
                        ", frame p95=" + Measure(Percentile(guest.frames, .95f), " ms") +
                        ", max=" + Measure(Maximum(guest.frames), " ms") +
                        ", guest ping p95=" + Measure(Percentile(guestPing, .95f), " ms") +
                        ", host ping p95=" + Measure(Percentile(hostPing, .95f), " ms") +
                        ", owner/host pose gap p95=" + Measure(Percentile(guest.snapshotError, .95f), " m") +
                        ", max=" + Measure(Maximum(guest.snapshotError), " m") +
                        ", inputs=" + guest.inputSent + ", snapshots=" + guest.snapshotReceived);
                    complete &= guest.started && guest.ended && guest.frames.Count > 0 &&
                        guest.inputSent > 0 && guest.snapshotReceived > 0;
                    if (Percentile(guest.frames, .95f) > 50) flags.Add("Guest " + clientId + " frame p95 exceeds 50 ms");
                    if (Percentile(guest.snapshotError, .95f) > 2) flags.Add("Guest " + clientId + " owner/host pose gap p95 exceeds 2 m");
                    if (host != null)
                    {
                        complete &= AppendRoute(report, "  Host to guest " + clientId, host, guestId, guest, hostId, flags);
                        complete &= AppendRoute(report, "  Guest " + clientId + " to host", guest, hostId, host, guestId, flags);
                    }
                }
                report.AppendLine("capture_status=" + (complete ? "complete" : "incomplete"));
                report.AppendLine("attention_flags=" + (flags.Count == 0 ? "none" : string.Join("; ", flags)));
                report.AppendLine("Flags are screening thresholds, not a diagnosis. Compare with a normal run and inspect the CSV timelines.");
                string path = Path.Combine(activeTraceDirectory, "benchmark-report.txt");
                File.WriteAllText(path, report.ToString(), new UTF8Encoding(false));
                if (complete) Debug.Log("MIA_COOP_BENCH_REPORT: " + path + "\n" + report);
            }
            catch (Exception exception) { Debug.LogError("MIA_COOP_BENCH_REPORT_FAILED: " + exception.Message); }
        }
    }
}
