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
            public string[] cars;
            public int[] paints;
            public bool[] ready;
            public float countdown;
        }

        sealed class GaragePreview
        {
            public GameObject root;
            public string carId;
            public int paint, spot, slot;
        }

        readonly Dictionary<ulong, int> playerSlots = new Dictionary<ulong, int>();
        readonly Dictionary<ulong, string> lobbyCars = new Dictionary<ulong, string>();
        readonly Dictionary<ulong, int> lobbyPaints = new Dictionary<ulong, int>();
        readonly Dictionary<ulong, GaragePreview> garagePreviews = new Dictionary<ulong, GaragePreview>();
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
        internal int GaragePreviewCount => garagePreviews.Count;
        internal bool GaragePreviewForSlot(int slot, string carId)
        {
            foreach (var preview in garagePreviews.Values)
                if (preview.root && preview.slot == slot && (carId == null || preview.carId == carId)) return true;
            return false;
        }

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
            RefreshHostileStrength();
        }

        void HostClientLeft(ulong id)
        {
            playerSlots.Remove(id); readyPlayers.Remove(id);
            countdownEnds = 0; lobbySendAt = 0;
            RefreshHostileStrength();
        }

        void RefreshHostileStrength()
        {
            foreach (var car in VehicleController.Active)
                if (car && !car.FriendlyToPlayer && car.Damage && !car.Damage.IsDead)
                    car.Damage.ApplyHealthTuning();
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
            if (ready && !LocalReady) AudioManager.Instance?.PlayReady();
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
            if (wasReady != ready) { countdownEnds = 0; lobbySendAt = 0; if (ready) AudioManager.Instance?.PlayReady(); }
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
                else if (countdownEnds <= 0) { countdownEnds = Time.unscaledTime + 5; AudioManager.Instance?.PlayCountdown(); }
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
            SyncGaragePreviews();
            var ids = new List<ulong>(playerSlots.Keys);
            ids.Sort((a, b) => playerSlots[a].CompareTo(playerSlots[b]));
            var packet = new LobbyStatePacket { protocol = Protocol, mission = game.SelectedMission,
                ids = new int[ids.Count], slots = new int[ids.Count], ready = new bool[ids.Count],
                cars = new string[ids.Count], paints = new int[ids.Count],
                countdown = InLobby ? CountdownRemaining : 0 };
            for (int i = 0; i < ids.Count; i++)
            {
                packet.ids[i] = (int)ids[i]; packet.slots[i] = playerSlots[ids[i]];
                packet.ready[i] = readyPlayers.Contains(ids[i]);
                var loadout = ids[i] == NetworkManager.ServerClientId ? LocalLoadout() : GuestLoadout(ids[i]);
                packet.cars[i] = loadout?.carId;
                packet.paints[i] = loadout?.paintIndex ?? 0;
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
                packet.ready == null || packet.cars == null || packet.paints == null ||
                packet.ids.Length != packet.slots.Length || packet.ids.Length != packet.ready.Length ||
                packet.ids.Length != packet.cars.Length || packet.ids.Length != packet.paints.Length ||
                packet.ids.Length > MaxPlayers) return;
            var previouslyReady = new HashSet<ulong>(readyPlayers);
            float previousCountdown = guestCountdown;
            playerSlots.Clear(); readyPlayers.Clear(); lobbyCars.Clear(); lobbyPaints.Clear();
            for (int i = 0; i < packet.ids.Length; i++)
            {
                playerSlots[(ulong)packet.ids[i]] = packet.slots[i];
                lobbyCars[(ulong)packet.ids[i]] = packet.cars[i];
                lobbyPaints[(ulong)packet.ids[i]] = Mathf.Clamp(packet.paints[i], 0, 4);
                if (packet.ready[i])
                {
                    readyPlayers.Add((ulong)packet.ids[i]);
                    if (!previouslyReady.Contains((ulong)packet.ids[i]) && (ulong)packet.ids[i] != network.LocalClientId)
                        AudioManager.Instance?.PlayReady();
                }
            }
            if (InLobby && lobbyMission >= 0 && lobbyMission != packet.mission && localReadyRequested)
            {
                localReadyRequested = false;
                SendLocalLoadout();
            }
            lobbyMission = packet.mission; guestCountdown = packet.countdown;
            if (previousCountdown <= 0 && guestCountdown > 0) AudioManager.Instance?.PlayCountdown();
            if (InLobby) GameManager.Instance.SelectedMission = packet.mission;
            SyncGaragePreviews();
        }

        static readonly Vector3[] GarageParkingSpots = {
            new Vector3(-5.5f, .08f, -4.5f),
            new Vector3(7f, .08f, -7f),
            new Vector3(-6f, .08f, 4.2f)
        };

        void SyncGaragePreviews()
        {
            if (!InLobby) { ClearGaragePreviews(); return; }
            var game = GameManager.Instance;
            ulong localId = IsHost ? NetworkManager.ServerClientId : network.LocalClientId;
            var ids = new List<ulong>(playerSlots.Keys);
            ids.Sort((a, b) => playerSlots[a].CompareTo(playerSlots[b]));
            var visible = new HashSet<ulong>();
            int spot = 0;
            foreach (ulong id in ids)
            {
                if (id == localId || spot >= GarageParkingSpots.Length) continue;
                var loadout = IsHost ? GuestLoadout(id) : null;
                string carId = IsHost ? loadout?.carId : lobbyCars.TryGetValue(id, out string known) ? known : null;
                int paint = IsHost ? loadout?.paintIndex ?? 0 : lobbyPaints.TryGetValue(id, out int knownPaint) ? knownPaint : 0;
                var definition = Array.Find(ContentCatalog.Vehicles, car => car && car.id == carId);
                if (!definition) continue;
                visible.Add(id);
                int slot = playerSlots[id];
                paint = Mathf.Clamp(paint, 0, 4);
                if (garagePreviews.TryGetValue(id, out var preview) && preview.root &&
                    preview.carId == carId && preview.paint == paint && preview.spot == spot &&
                    preview.slot == slot && preview.root.transform.parent == game.World.transform)
                { spot++; continue; }
                if (preview != null && preview.root) Destroy(preview.root);
                var root = new GameObject("Player " + slot + " • " + definition.displayName + " • Parked in garage");
                root.transform.SetParent(game.World.transform, false);
                Vector3 parking = GarageParkingSpots[spot];
                root.transform.SetPositionAndRotation(new Vector3(parking.x, parking.y + VehicleVisual.TyreDrop(definition), parking.z),
                    Quaternion.Euler(0, 180, 0));
                VehicleVisual.Build(definition, root.transform, false, EnemyFaction.Sunsprawl,
                    paint > 0 ? GameUI.GaragePaint(paint) : (Color?)null);
                WorldArt.Text("PLAYER " + slot, root.transform, new Vector3(0, 3.2f, 0), .55f,
                    new Color(.92f, .73f, .35f), Quaternion.Euler(45, 0, 0)).AddComponent<WorldBillboard>();
                garagePreviews[id] = new GaragePreview { root = root, carId = carId, paint = paint, spot = spot, slot = slot };
                spot++;
            }
            var removed = new List<ulong>();
            foreach (var pair in garagePreviews)
                if (!visible.Contains(pair.Key))
                {
                    if (pair.Value.root) Destroy(pair.Value.root);
                    removed.Add(pair.Key);
                }
            foreach (ulong id in removed) garagePreviews.Remove(id);
        }

        void ClearGaragePreviews()
        {
            foreach (var preview in garagePreviews.Values)
                if (preview.root) Destroy(preview.root);
            garagePreviews.Clear();
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

        public int[] LobbySlots()
        {
            var slots = new List<int>(playerSlots.Values);
            slots.Sort();
            return slots.ToArray();
        }

        public bool SlotReady(int slot)
        {
            foreach (var pair in playerSlots)
                if (pair.Value == slot) return readyPlayers.Contains(pair.Key);
            return false;
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
            lobbyCars.Clear(); lobbyPaints.Clear();
            countdownEnds = lobbySendAt = guestCountdown = 0;
            lobbyMission = -1; launchingMatch = localReadyRequested = false;
        }
    }
}
