using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace MadeInArizona
{
    public sealed partial class CoopSession
    {
        const string PingMessage = "mia.coop.ping.v1";
        const string PongMessage = "mia.coop.pong.v1";
        const float PingIntervalSeconds = 1f;
        const float PingStaleSeconds = 3.5f;

        [Serializable] sealed class PingPacket { public int protocol, sequence; public float sentAt; }

        readonly Dictionary<ulong, float> pingMilliseconds = new Dictionary<ulong, float>();
        readonly Dictionary<ulong, float> pingLastReceivedAt = new Dictionary<ulong, float>();
        float nextPingAt;
        int pingSequence;

        void RegisterPingMessages(CustomMessagingManager messages)
        {
            messages.RegisterNamedMessageHandler(PingMessage, ReceivePing);
            messages.RegisterNamedMessageHandler(PongMessage, ReceivePong);
        }

        void UpdatePing()
        {
            if ((!IsHost && !IsClient) || !network || !network.IsListening ||
                Time.unscaledTime < nextPingAt) return;
            nextPingAt = Time.unscaledTime + PingIntervalSeconds;
            var packet = new PingPacket { protocol = Protocol, sequence = ++pingSequence,
                sentAt = Time.unscaledTime };
            if (IsHost)
            {
                foreach (ulong clientId in network.ConnectedClientsIds)
                    if (clientId != NetworkManager.ServerClientId)
                        Send(PingMessage, clientId, packet, NetworkDelivery.Unreliable);
            }
            else if (network.IsConnectedClient)
                Send(PingMessage, NetworkManager.ServerClientId, packet, NetworkDelivery.Unreliable);
        }

        void ReceivePing(ulong sender, FastBufferReader reader)
        {
            if ((!IsHost && !IsClient) || !network || !network.IsListening ||
                (IsClient && sender != NetworkManager.ServerClientId) ||
                (IsHost && !network.ConnectedClients.ContainsKey(sender))) return;
            reader.ReadValueSafe(out string json);
            var packet = JsonUtility.FromJson<PingPacket>(json);
            if (packet == null || packet.protocol != Protocol || packet.sequence <= 0) return;
            Send(PongMessage, sender, packet, NetworkDelivery.Unreliable);
        }

        void ReceivePong(ulong sender, FastBufferReader reader)
        {
            if ((!IsHost && !IsClient) || !network || !network.IsListening ||
                (IsClient && sender != NetworkManager.ServerClientId) ||
                (IsHost && !network.ConnectedClients.ContainsKey(sender))) return;
            reader.ReadValueSafe(out string json);
            var packet = JsonUtility.FromJson<PingPacket>(json);
            if (packet == null || packet.protocol != Protocol || packet.sequence <= 0 ||
                packet.sequence > pingSequence) return;
            float roundTrip = Time.unscaledTime - packet.sentAt;
            if (roundTrip < 0 || roundTrip > 10) return;
            float sample = roundTrip * 1000;
            pingMilliseconds[sender] = pingMilliseconds.TryGetValue(sender, out float previous) ?
                Mathf.Lerp(previous, sample, .35f) : sample;
            pingLastReceivedAt[sender] = Time.unscaledTime;
            var row = MakeTrace("live_ping", (int)network.LocalClientId, (int)sender, null);
            row.inputSent = packet.sequence;
            row.leadMilliseconds = sample;
            AddTrace(row);
        }

        float PingFor(ulong peer)
        {
            return pingMilliseconds.TryGetValue(peer, out float value) &&
                pingLastReceivedAt.TryGetValue(peer, out float receivedAt) &&
                Time.unscaledTime - receivedAt <= PingStaleSeconds ? value : -1;
        }

        public float WorstPingMilliseconds
        {
            get
            {
                if (IsClient) return PingFor(NetworkManager.ServerClientId);
                if (!IsHost) return -1;
                float worst = -1;
                foreach (ulong clientId in network.ConnectedClientsIds)
                    if (clientId != NetworkManager.ServerClientId)
                        worst = Mathf.Max(worst, PingFor(clientId));
                return worst;
            }
        }

        public string PingSummary
        {
            get
            {
                if (IsClient) return "PING " + PingLabel(PingFor(NetworkManager.ServerClientId));
                if (!IsHost) return "";
                var labels = new List<string>();
                foreach (ulong clientId in network.ConnectedClientsIds)
                    if (clientId != NetworkManager.ServerClientId)
                        labels.Add("P" + (clientId + 1) + " " + PingLabel(PingFor(clientId)));
                return labels.Count > 0 ? string.Join("  •  ", labels) : "WAITING FOR PLAYERS";
            }
        }

        static string PingLabel(float milliseconds) => milliseconds < 0 ? "--" :
            Mathf.RoundToInt(milliseconds) + " ms";

        void ResetPing()
        {
            pingMilliseconds.Clear();
            pingLastReceivedAt.Clear();
            pingSequence = 0;
            nextPingAt = 0;
        }
    }
}
