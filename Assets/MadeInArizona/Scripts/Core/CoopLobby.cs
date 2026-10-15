using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace MadeInArizona
{
    public sealed partial class CoopSession
    {
        const string LobbyStateMessage = "mia.coop.lobby-state.v1";
        [Serializable] sealed class LobbyStatePacket
        {
            public int protocol, mission;
            public int[] ids, slots;
            public bool[] ready;
            public float countdown;
        }

        readonly Dictionary<ulong, int> playerSlots = new Dictionary<ulong, int>();
        readonly Dictionary<VehicleController, int> proxyOwners = new Dictionary<VehicleController, int>();
        readonly HashSet<ulong> readyPlayers = new HashSet<ulong>();
        float countdownEnds, lobbySendAt, guestCountdown;
        int lobbyMission = -1;
        bool launchingMatch, localReadyRequested;

        public bool InLobby => (IsHost || IsClient) && GameManager.Instance &&
            GameManager.Instance.State == GameState.Garage && (IsHost || (remoteMode == 0 && remoteWorldReady));
        public bool LocalReady => IsHost ? readyPlayers.Contains(NetworkManager.ServerClientId) :
            IsClient && localReadyRequested;
        public float CountdownRemaining => IsHost ? Mathf.Max(0, countdownEnds - Time.unscaledTime) : guestCountdown;
        public int LobbyMission => IsHost ? GameManager.Instance.SelectedMission :
            lobbyMission >= 0 ? lobbyMission : GameManager.Instance.SelectedMission;
        public bool CanLaunchMatch => !IsHost || launchingMatch;

        void RegisterLobbyMessages(CustomMessagingManager messages)
        {
            messages.RegisterNamedMessageHandler(LobbyStateMessage, ReceiveLobbyState);
        }

        void InitializeHostLobby()
        {
            playerSlots.Clear(); readyPlayers.Clear();
            playerSlots[NetworkManager.ServerClientId] = 1;
            lobbyMission = GameManager.Instance.SelectedMission;
            countdownEnds = 0;
            GameManager.Instance.ReturnToGarage();
        }

        void HostClientJoined(ulong id)
        {
            if (id == NetworkManager.ServerClientId) return;
            for (int slot = 2; slot <= MaxPlayers; slot++)
                if (!playerSlots.ContainsValue(slot)) { playerSlots[id] = slot; break; }
            readyPlayers.Clear(); countdownEnds = 0; lobbySendAt = 0;
        }

        void HostClientLeft(ulong id)
        {
            playerSlots.Remove(id); readyPlayers.Remove(id);
            countdownEnds = 0; lobbySendAt = 0;
        }

        void LobbyWorldChanged()
        {
            readyPlayers.Clear(); countdownEnds = 0; guestCountdown = 0;
            lobbyMission = GameManager.Instance.SelectedMission;
            lobbySendAt = 0;
        }

        void GuestWorldChanged()
        {
            localReadyRequested = false;
            readyPlayers.Clear();
            guestCountdown = 0;
        }

        public void GarageLoadoutChanged()
        {
            if (!InLobby) return;
            SetReady(false);
        }

        public void SetReady(bool ready)
        {
            if (!InLobby || !network) return;
            if (IsHost)
            {
                if (ready) readyPlayers.Add(NetworkManager.ServerClientId);
                else readyPlayers.Remove(NetworkManager.ServerClientId);
                countdownEnds = 0; lobbySendAt = 0;
            }
            else
            {
                localReadyRequested = ready;
                SendLocalLoadout();
            }
        }

        void LobbyReadyFromLoadout(ulong sender, bool ready)
        {
            if (!IsHost || !InLobby || !playerSlots.ContainsKey(sender)) return;
            bool wasReady = readyPlayers.Contains(sender);
            if (ready) readyPlayers.Add(sender);
            else readyPlayers.Remove(sender);
            if (wasReady != ready) { countdownEnds = 0; lobbySendAt = 0; }
        }

        void LobbyLoadoutChanged(ulong sender)
        {
            if (!IsHost || !InLobby) return;
            readyPlayers.Remove(sender); countdownEnds = 0; lobbySendAt = 0;
        }

        void TickLobby()
        {
            if (!IsHost || !network) return;
            var game = GameManager.Instance;
            if (InLobby)
            {
                if (lobbyMission != game.SelectedMission)
                {
                    lobbyMission = game.SelectedMission;
                    readyPlayers.Clear(); countdownEnds = 0; lobbySendAt = 0;
                }
                bool allReady = playerSlots.Count == network.ConnectedClientsIds.Count;
                foreach (ulong id in network.ConnectedClientsIds)
                    allReady &= readyPlayers.Contains(id) &&
                        (id == NetworkManager.ServerClientId || GuestLoadout(id) != null);
                if (!allReady) countdownEnds = 0;
                else if (countdownEnds <= 0) countdownEnds = Time.unscaledTime + 5;
                if (countdownEnds > 0 && Time.unscaledTime >= countdownEnds)
                {
                    launchingMatch = true;
                    game.StartMission(game.SelectedMission);
                    launchingMatch = false;
                    return;
                }
            }
            if (Time.unscaledTime < lobbySendAt) return;
            lobbySendAt = Time.unscaledTime + .2f;
            var ids = new List<ulong>(playerSlots.Keys);
            ids.Sort((a, b) => playerSlots[a].CompareTo(playerSlots[b]));
            var packet = new LobbyStatePacket { protocol = Protocol, mission = game.SelectedMission,
                ids = new int[ids.Count], slots = new int[ids.Count], ready = new bool[ids.Count],
                countdown = InLobby ? CountdownRemaining : 0 };
            for (int i = 0; i < ids.Count; i++)
            {
                packet.ids[i] = (int)ids[i]; packet.slots[i] = playerSlots[ids[i]];
                packet.ready[i] = readyPlayers.Contains(ids[i]);
            }
            foreach (ulong id in network.ConnectedClientsIds)
                if (id != NetworkManager.ServerClientId)
                    Send(LobbyStateMessage, id, packet, NetworkDelivery.ReliableSequenced);
        }

        void ReceiveLobbyState(ulong sender, FastBufferReader reader)
        {
            if (!IsClient || sender != NetworkManager.ServerClientId) return;
            reader.ReadValueSafe(out string json);
            var packet = JsonUtility.FromJson<LobbyStatePacket>(json);
            if (packet == null || packet.protocol != Protocol || packet.ids == null || packet.slots == null ||
                packet.ready == null || packet.ids.Length != packet.slots.Length ||
                packet.ids.Length != packet.ready.Length || packet.ids.Length > MaxPlayers) return;
            playerSlots.Clear(); readyPlayers.Clear();
            for (int i = 0; i < packet.ids.Length; i++)
            {
                playerSlots[(ulong)packet.ids[i]] = packet.slots[i];
                if (packet.ready[i]) readyPlayers.Add((ulong)packet.ids[i]);
            }
            if (InLobby && lobbyMission >= 0 && lobbyMission != packet.mission && localReadyRequested)
            {
                localReadyRequested = false;
                SendLocalLoadout();
            }
            lobbyMission = packet.mission; guestCountdown = packet.countdown;
            if (InLobby) GameManager.Instance.SelectedMission = packet.mission;
        }

        public string LobbyRoster()
        {
            var ids = new List<ulong>(playerSlots.Keys);
            ids.Sort((a, b) => playerSlots[a].CompareTo(playerSlots[b]));
            var lines = new List<string>();
            foreach (ulong id in ids)
                lines.Add("PLAYER " + playerSlots[id] + "  " + (readyPlayers.Contains(id) ? "READY" : "CHOOSING LOADOUT"));
            return string.Join("\n", lines);
        }

        public int PlayerSlotForCar(VehicleController car)
        {
            if (!car || !car.IsPlayer) return 0;
            if (IsHost)
            {
                if (car == GameManager.Instance.Player) return 1;
                foreach (var pair in guestCars)
                    if (pair.Value == car && playerSlots.TryGetValue(pair.Key, out int slot)) return slot;
            }
            else if (IsClient && proxyOwners.TryGetValue(car, out int owner) &&
                playerSlots.TryGetValue((ulong)owner, out int slot)) return slot;
            return 0;
        }

        void ResetLobby()
        {
            playerSlots.Clear(); proxyOwners.Clear(); readyPlayers.Clear();
            countdownEnds = lobbySendAt = guestCountdown = 0;
            lobbyMission = -1; launchingMatch = localReadyRequested = false;
        }
    }
}
