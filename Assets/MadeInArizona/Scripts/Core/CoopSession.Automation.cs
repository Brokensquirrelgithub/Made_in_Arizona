using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace MadeInArizona
{
    public sealed partial class CoopSession
    {
        const string AutomationRequestMessage = "mia.coop.automation-request.v1";
        const string AutomationResponseMessage = "mia.coop.automation-response.v1";

        [Serializable] sealed class AutomationPacket
        {
            public int protocol;
            public string id, json;
        }

        [Serializable] sealed class BridgePeerList { public BridgePeer[] peers; }
        [Serializable] sealed class BridgePeer
        {
            public int slot, clientId, inputSent, inputReceived, inputApplied, snapshotTick;
            public bool local;
            public float x, z, speedKph, snapshotAgeMs;
        }

        void RegisterAutomationMessages(CustomMessagingManager messages)
        {
            messages.RegisterNamedMessageHandler(AutomationRequestMessage, ReceiveAutomationRequest);
            messages.RegisterNamedMessageHandler(AutomationResponseMessage, ReceiveAutomationResponse);
        }

        /// <summary>Relay a restricted development command to one connected, opted-in guest.</summary>
        public bool RequestGuestAutomation(int slot, string id, string json, out ulong clientId, out string error)
        {
            clientId = 0;
            error = null;
            if (!Debug.isDebugBuild || !LiveTestBridge.Instance || !IsHost || !network.IsListening)
                error = "A bridged development host is required";
            else if (slot < 2 || slot > MaxPlayers)
                error = "Guest slot must be 2–4";
            else if (string.IsNullOrEmpty(json) || json.Length > 4096)
                error = "Guest request exceeds 4 KB";
            else
            {
                foreach (var pair in playerSlots)
                    if (pair.Value == slot && network.ConnectedClients.ContainsKey(pair.Key))
                    {
                        clientId = pair.Key;
                        Send(AutomationRequestMessage, clientId,
                            new AutomationPacket { protocol = Protocol, id = id, json = json },
                            NetworkDelivery.ReliableFragmentedSequenced);
                        return true;
                    }
                error = "That guest slot is not connected";
            }
            return false;
        }

        void ReceiveAutomationRequest(ulong sender, FastBufferReader reader)
        {
            if (!Debug.isDebugBuild || !IsClient || sender != NetworkManager.ServerClientId || !LiveTestBridge.Instance) return;
            reader.ReadValueSafe(out string json);
            if (json.Length > 8192) return;
            var packet = JsonUtility.FromJson<AutomationPacket>(json);
            if (packet == null || packet.protocol != Protocol ||
                !Guid.TryParseExact(packet.id, "N", out _) || string.IsNullOrEmpty(packet.json) ||
                packet.json.Length > 4096) return;
            string answer = LiveTestBridge.Instance.ExecuteRemote(packet.json);
            if (answer.Length > 32768) answer = "{\"ok\":false,\"message\":\"Guest response exceeded 32 KB\"}";
            Send(AutomationResponseMessage, NetworkManager.ServerClientId,
                new AutomationPacket { protocol = Protocol, id = packet.id, json = answer },
                NetworkDelivery.ReliableFragmentedSequenced);
        }

        void ReceiveAutomationResponse(ulong sender, FastBufferReader reader)
        {
            if (!Debug.isDebugBuild || !IsHost || !network.ConnectedClients.ContainsKey(sender) || !LiveTestBridge.Instance) return;
            reader.ReadValueSafe(out string json);
            if (json.Length > 40000) return;
            var packet = JsonUtility.FromJson<AutomationPacket>(json);
            if (packet == null || packet.protocol != Protocol ||
                !Guid.TryParseExact(packet.id, "N", out _) || string.IsNullOrEmpty(packet.json) ||
                packet.json.Length > 32768) return;
            LiveTestBridge.Instance.CompleteGuestRequest(packet.id, sender, packet.json);
        }

        /// <summary>Same car measurements on both screens make pose divergence visible without a full trace.</summary>
        public string BridgePeerSnapshot()
        {
            var rows = new List<BridgePeer>();
            var game = GameManager.Instance;
            if (game && game.Player)
            {
                int localSlot = IsHost ? 1 : playerSlots.TryGetValue(network ? network.LocalClientId : 0, out int ownSlot) ? ownSlot : 0;
                var row = MakeBridgePeer(game.Player, localSlot, network ? (int)network.LocalClientId : 0, true);
                row.inputSent = IsClient ? inputSequence : 0;
                row.snapshotTick = IsClient ? ownSnapshotTick : snapshotTick;
                row.snapshotAgeMs = IsClient && lastOwnSnapshotAt > 0 ? (Time.unscaledTime - lastOwnSnapshotAt) * 1000f : -1f;
                rows.Add(row);
            }
            if (IsHost)
            {
                foreach (var pair in guestCars)
                    if (pair.Value)
                    {
                        var row = MakeBridgePeer(pair.Value,
                            playerSlots.TryGetValue(pair.Key, out int guestSlot) ? guestSlot : 0, (int)pair.Key, false);
                        row.inputReceived = pair.Value.RemoteControls.sequence;
                        rows.Add(row);
                    }
            }
            else if (IsClient)
            {
                foreach (var pair in proxies)
                    if (pair.Value && pair.Value.IsPlayer && !pair.Value.IsLocallyPredicted)
                    {
                        int owner = proxyOwners.TryGetValue(pair.Value, out int id) ? id : -1;
                        var row = MakeBridgePeer(pair.Value,
                            playerSlots.TryGetValue((ulong)Math.Max(0, owner), out int remoteSlot) ? remoteSlot : 0, owner, false);
                        row.snapshotTick = lastVehicleTick.TryGetValue(pair.Key, out int tick) ? tick : 0;
                        row.snapshotAgeMs = seenAt.TryGetValue(pair.Key, out float time) ?
                            (Time.unscaledTime - time) * 1000f : -1f;
                        rows.Add(row);
                    }
            }
            return JsonUtility.ToJson(new BridgePeerList { peers = rows.ToArray() });
        }

        static BridgePeer MakeBridgePeer(VehicleController car, int slot, int clientId, bool local)
        {
            Vector3 position = car.Body ? car.Body.position : car.transform.position;
            return new BridgePeer { slot = slot, clientId = clientId, local = local,
                x = position.x, z = position.z, speedKph = car.SpeedKph,
                inputApplied = car.LastAppliedInputSequence, snapshotAgeMs = -1f };
        }
    }
}
