using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

namespace MadeInArizona
{
    [Serializable]
    public struct CoopControls
    {
        public Vector2 move, aim;
        public float elevation;
        public bool primary, secondary, drift, boost, interact, swap;
        /// <summary>Reverse button held, and whether the guest drives with shoulder reverse (no automatic stick reverse).</summary>
        public bool reverse, manualReverse;
    }

    /// <summary>One-off effects the host replicates to guests: things guests cannot work out from vehicle snapshots.</summary>
    public enum CoopFx { BowlingLaunch = 1, BowlingStrike = 2 }

    /// <summary>
    /// A small host-authoritative bridge for the existing code-created world. Relay supplies the
    /// invitation and NGO supplies transport; the host owns simulation and all saves.
    /// </summary>
    public sealed partial class CoopSession : MonoBehaviour
    {
        const string InputMessage = "mia.coop.input.v1";
        const string WorldMessage = "mia.coop.world.v1";
        const string VehicleMessage = "mia.coop.vehicle.v1";
        const string PickupMessage = "mia.coop.pickup.v1";
        const string ProjectileMessage = "mia.coop.projectile.v1";
        const string ExplosionMessage = "mia.coop.explosion.v1";
        const string PropBreakMessage = "mia.coop.prop-break.v1";
        const string ReadyMessage = "mia.coop.ready.v1";
        const string MissionMessage = "mia.coop.mission.v1";
        const string TuningMessage = "mia.coop.tuning.v1";
        const string PinStateMessage = "mia.coop.pin-state.v1";
        const string FxMessage = "mia.coop.fx.v1";
        const int Protocol = 3;
        public const int MaxPlayers = 4;
        public static CoopSession Instance { get; private set; }
        public static bool IsRemoteClient => Instance && Instance.IsClient;
        public bool IsHost => network && network.IsHost;
        public bool IsClient => network && network.IsClient && !network.IsServer;
        public bool Busy { get; private set; }
        public string JoinCode { get; private set; } = "";
        public string Status { get; private set; } = "Offline";
        public bool HostPaused { get; private set; }
        public int HostTier { get; private set; }
        public int PlayerCount => network && network.IsListening ? network.ConnectedClientsIds.Count : 1;
        public bool AnyPlayerAlive
        {
            get
            {
                if (!IsHost) return false;
                foreach (var car in VehicleController.Active)
                    if (car && car.IsPlayer && car.Damage && !car.Damage.IsDead) return true;
                return false;
            }
        }

        NetworkManager network;
        UnityTransport transport;
        readonly Dictionary<ulong, VehicleController> guestCars = new Dictionary<ulong, VehicleController>();
        readonly Dictionary<int, VehicleController> proxies = new Dictionary<int, VehicleController>();
        readonly Dictionary<int, float> seenAt = new Dictionary<int, float>();
        readonly Dictionary<VehicleController, int> vehicleIds = new Dictionary<VehicleController, int>();
        readonly Dictionary<CombatPickup, int> pickupIds = new Dictionary<CombatPickup, int>();
        readonly Dictionary<int, CombatPickup> pickupProxies = new Dictionary<int, CombatPickup>();
        readonly Dictionary<int, float> pickupSeenAt = new Dictionary<int, float>();
        readonly List<PropBreakPacket> brokenProps = new List<PropBreakPacket>();
        int nextVehicleId;
        int nextPickupId;
        float inputAt, snapshotAt, missionAt, pickupAt, tuningAt, pinAt;
        string lastTuningJson, lastPinFlags;
        bool queuedInteract, queuedSwap;
        bool remoteWorldReady;
        int remoteMode;

        [Serializable] sealed class WorldPacket
        {
            public int protocol, mode, mission, tier;
            public string config, tuning;
        }
        [Serializable] sealed class TuningPacket { public int protocol; public string tuning; }
        [Serializable] sealed class PinStatePacket { public int protocol, seed; public string flags; }
        [Serializable] sealed class InputPacket { public CoopControls controls; }
        [Serializable] sealed class VehiclePacket
        {
            public int id, owner, archetype, faction;
            public string definition, weapon, fieldWeapon;
            public bool player, friendly;
            public Vector3 position;
            public Vector3 velocity;
            public Quaternion rotation;
            public float health, maximum, boost, rpm, throttle, yawRate;
            public bool boosting;
            public int fieldAmmo;
            // Death ray beam (continuous while held) and the car on this one's tow cable (0 = none).
            public bool beam;
            public Vector3 beamEnd;
            public float beamHeat;
            public int tow;
        }
        [Serializable] sealed class PickupPacket
        {
            public int id, kind, amount;
            public string weapon;
            public Vector3 position;
        }
        [Serializable] sealed class ProjectilePacket
        {
            public Vector3 position, direction;
            public float speed, lifetime;
            public Color color;
            public int kind;
        }
        [Serializable] sealed class ExplosionPacket
        {
            public Vector3 position;
            public float radius;
            public int kind;
        }
        [Serializable] sealed class FxPacket
        {
            public int kind;
            public Vector3 position, velocity;
        }
        [Serializable] sealed class PropBreakPacket
        {
            public Vector3 position;
            public string name;
        }
        [Serializable] sealed class ReadyPacket { public int protocol; }
        [Serializable] sealed class MissionPacket
        {
            public int state, stage, score, kills, combo, destruction, money;
            public string objective, debrief;
            public Vector3 position;
            public float progress, remaining;
        }

        void Awake() { Instance = this; }
        void OnDestroy() { Leave(); if (Instance == this) Instance = null; }

        void CreateNetworkManager()
        {
            var host = new GameObject("Arizona • Co-op transport");
            transport = host.AddComponent<UnityTransport>();
            network = host.AddComponent<NetworkManager>();
            if (network.NetworkConfig == null) network.NetworkConfig = new NetworkConfig();
            network.NetworkConfig.NetworkTransport = transport;
            network.NetworkConfig.EnableSceneManagement = false;
            network.OnClientConnectedCallback += Connected;
            network.OnClientDisconnectCallback += Disconnected;
        }

        void RegisterMessages()
        {
            var messages = network.CustomMessagingManager;
            messages.RegisterNamedMessageHandler(InputMessage, ReceiveInput);
            messages.RegisterNamedMessageHandler(WorldMessage, ReceiveWorld);
            messages.RegisterNamedMessageHandler(VehicleMessage, ReceiveVehicle);
            messages.RegisterNamedMessageHandler(PickupMessage, ReceivePickup);
            messages.RegisterNamedMessageHandler(ProjectileMessage, ReceiveProjectile);
            messages.RegisterNamedMessageHandler(ExplosionMessage, ReceiveExplosion);
            messages.RegisterNamedMessageHandler(PropBreakMessage, ReceivePropBreak);
            messages.RegisterNamedMessageHandler(ReadyMessage, ReceiveReady);
            messages.RegisterNamedMessageHandler(MissionMessage, ReceiveMission);
            messages.RegisterNamedMessageHandler(TuningMessage, ReceiveTuning);
            messages.RegisterNamedMessageHandler(PinStateMessage, ReceivePinState);
            messages.RegisterNamedMessageHandler(FxMessage, ReceiveFx);
            RegisterBalanceMessages(messages);
            RegisterDraftMessages(messages);
        }

        public async void Host()
        {
            if (Busy || network) return;
            BeginTuningDraft();
            Busy = true; Status = "Connecting to Unity Relay…";
            try
            {
                await InitializeServices();
                var allocation = await RelayService.Instance.CreateAllocationAsync(MaxPlayers - 1);
                string code = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
                CreateNetworkManager();
                transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, "dtls"));
                if (!network.StartHost()) throw new InvalidOperationException("Could not start the host transport.");
                RegisterMessages();
                JoinCode = code;
                Status = "Hosting • share code " + code;
            }
            catch (Exception error)
            {
                Status = "Host failed: " + error.Message;
                Debug.LogException(error);
                ResetTransport();
            }
            finally { Busy = false; }
        }

        public async void Join(string code)
        {
            if (Busy || network) return;
            code = (code ?? "").Trim().ToUpperInvariant();
            if (code.Length < 4 || code.Length > 16) { Status = "Enter a valid join code."; return; }
            BeginTuningDraft();
            Busy = true; Status = "Joining " + code + "…";
            try
            {
                await InitializeServices();
                var allocation = await RelayService.Instance.JoinAllocationAsync(code);
                CreateNetworkManager();
                transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, "dtls"));
                if (!network.StartClient()) throw new InvalidOperationException("Could not start the client transport.");
                RegisterMessages();
                Status = "Connecting to host…";
            }
            catch (Exception error)
            {
                Status = "Join failed: " + error.Message;
                Debug.LogException(error);
                ResetTransport();
            }
            finally { Busy = false; }
        }

        static async System.Threading.Tasks.Task InitializeServices()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized) await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        void Connected(ulong clientId)
        {
            if (IsHost)
            {
                if (clientId == NetworkManager.ServerClientId) return;
                Status = "Hosting • " + PlayerCount + "/" + MaxPlayers + " players";
                SendWorld(clientId);
            }
            else if (IsClient && clientId == network.LocalClientId)
            {
                Status = "Connected • waiting for host";
                SendLocalLoadout();
            }
        }

        void Disconnected(ulong clientId)
        {
            if (IsHost)
            {
                if (guestCars.TryGetValue(clientId, out var car) && car) Destroy(car.gameObject);
                guestCars.Remove(clientId);
                GuestLeftBalance(clientId);
                EndGuestDisconnected(clientId);
                Status = "Hosting • " + PlayerCount + "/" + MaxPlayers + " players";
            }
            else if (IsClient && (clientId == network.LocalClientId || clientId == NetworkManager.ServerClientId))
            {
                Status = "Disconnected from host";
                GameManager.Instance?.LoadCoopWorld(0, 0, null);
                ResetTransport();
            }
        }

        void SpawnGuest(ulong clientId)
        {
            if (!IsHost || guestCars.ContainsKey(clientId) ||
                (GameManager.Instance.State != GameState.Playing && GameManager.Instance.State != GameState.Paused)) return;
            var game = GameManager.Instance;
            var loadout = GuestLoadout(clientId);
            var definition = Array.Find(ContentCatalog.Vehicles, value => value && value.id == loadout?.carId) ?? game.CurrentVehicle;
            if (!definition) return;
            var carObject = new GameObject(definition.displayName + " • Co-op " + clientId);
            carObject.transform.SetParent(game.World.transform);
            carObject.transform.position = game.World.PlayerSpawn + new Vector3(4f * (guestCars.Count + 1), 0, 3f);
            var car = carObject.AddComponent<VehicleController>();
            car.Initialize(definition, loadout?.stats ?? VehicleStats.From(definition), true);
            if (loadout != null) car.Weapons.ConfigurePlayerPrimary(loadout.weaponId);
            car.SetRemoteControls(default);
            guestCars[clientId] = car;
        }

        public void HostWorldChanged()
        {
            if (!IsHost) return;
            vehicleIds.Clear(); nextVehicleId = 0;
            pickupIds.Clear(); nextPickupId = 0;
            brokenProps.Clear();
            lastPinFlags = null;
            foreach (var pair in guestCars)
                if (pair.Value) Destroy(pair.Value.gameObject);
            guestCars.Clear();
            if (GameManager.Instance.IsPlaying)
                foreach (ulong clientId in network.ConnectedClientsIds)
                    if (clientId != NetworkManager.ServerClientId && GuestLoadout(clientId) != null) SpawnGuest(clientId);
            foreach (ulong clientId in network.ConnectedClientsIds)
                if (clientId != NetworkManager.ServerClientId) SendWorld(clientId);
        }

        void SendWorld(ulong clientId)
        {
            var game = GameManager.Instance;
            if (!game) return;
            int mode = game.IsPlaying || game.State == GameState.Paused ? game.IsCombatTrial ? 2 : 1 : 0;
            var packet = new WorldPacket { protocol = Protocol, mode = mode, mission = game.SelectedMission,
                tier = WorldExploration.CurrentTier,
                config = mode == 1 ? JsonUtility.ToJson(game.WorldConfig) : "",
                tuning = JsonUtility.ToJson(DevTuning.Current) };
            Send(WorldMessage, clientId, packet, NetworkDelivery.ReliableFragmentedSequenced);
        }

        void ReceiveWorld(ulong sender, FastBufferReader reader)
        {
            if (!IsClient || sender != NetworkManager.ServerClientId) return;
            reader.ReadValueSafe(out string json);
            var packet = JsonUtility.FromJson<WorldPacket>(json);
            if (packet == null || packet.protocol != Protocol)
            {
                Leave(); Status = "Incompatible co-op protocol. Install the same game build as the host."; return;
            }
            WorldGenConfig config = null;
            if (packet.mode == 1)
            {
                try { config = JsonUtility.FromJson<WorldGenConfig>(packet.config); }
                catch (Exception) { }
                if (config == null || !config.Validate(out string error))
                {
                    Leave(); Status = "Host world settings are invalid."; return;
                }
            }
            remoteWorldReady = false;
            remoteMode = packet.mode;
            HostTier = Mathf.Clamp(packet.tier, 0, 3);
            DevTuning.SetCoopOverride(packet.tuning);
            proxies.Clear(); seenAt.Clear(); pickupProxies.Clear(); pickupSeenAt.Clear();
            GameManager.Instance.LoadCoopWorld(packet.mode, packet.mission, config);
        }

        internal void RemoteWorldReady()
        {
            remoteWorldReady = true;
            if (IsClient) Status = remoteMode == 0 ? "Connected • waiting for host to start" : "Co-op game in progress";
            if (IsClient && remoteMode != 0)
                Send(ReadyMessage, NetworkManager.ServerClientId, new ReadyPacket { protocol = Protocol }, NetworkDelivery.ReliableSequenced);
        }

        void ReceiveReady(ulong sender, FastBufferReader reader)
        {
            if (!IsHost || sender == NetworkManager.ServerClientId) return;
            reader.ReadValueSafe(out string json);
            var packet = JsonUtility.FromJson<ReadyPacket>(json);
            if (packet == null || packet.protocol != Protocol) { network.DisconnectClient(sender); return; }
            foreach (var broken in brokenProps)
                Send(PropBreakMessage, sender, broken, NetworkDelivery.ReliableSequenced);
            SendPinState(sender);
        }

        void ReceiveInput(ulong sender, FastBufferReader reader)
        {
            if (!IsHost || !guestCars.TryGetValue(sender, out var car) || !car) return;
            reader.ReadValueSafe(out string json);
            var packet = JsonUtility.FromJson<InputPacket>(json);
            if (packet == null) return;
            var input = packet.controls;
            input.move = Vector2.ClampMagnitude(input.move, 1);
            input.aim = Vector2.ClampMagnitude(input.aim, 1);
            input.elevation = Mathf.Clamp(input.elevation, -1, 1);
            car.SetRemoteControls(input);
        }

        void ReceiveVehicle(ulong sender, FastBufferReader reader)
        {
            if (!IsClient || sender != NetworkManager.ServerClientId || !remoteWorldReady || remoteMode == 0) return;
            reader.ReadValueSafe(out string json);
            var packet = JsonUtility.FromJson<VehiclePacket>(json);
            if (packet == null) return;
            if (!proxies.TryGetValue(packet.id, out var car) || !car)
            {
                var definition = Array.Find(ContentCatalog.Vehicles, value => value && value.id == packet.definition);
                if (!definition) return;
                var obj = new GameObject(definition.displayName + " • Network proxy");
                obj.transform.SetParent(GameManager.Instance.World.transform);
                obj.transform.SetPositionAndRotation(packet.position, packet.rotation);
                car = obj.AddComponent<VehicleController>();
                var stats = packet.player && packet.owner == (int)network.LocalClientId
                    ? GarageManager.StatsFor(definition, GameManager.Instance.Save) : VehicleStats.From(definition);
                car.Initialize(definition, stats, packet.player, (EnemyFaction)packet.faction);
                if (!packet.player)
                {
                    var ai = obj.AddComponent<EnemyAI>();
                    ai.Initialize(null, packet.archetype, (EnemyFaction)packet.faction);
                    ai.IsFriendly = packet.friendly;
                    ai.enabled = false;
                    if (packet.archetype == 7) car.Visual.localScale = Vector3.one * 1.8f;
                }
                car.SetNetworkProxy();
                proxies[packet.id] = car;
            }
            car.ApplyNetworkMotion(packet.position, packet.rotation, packet.velocity, packet.health,
                packet.maximum, packet.boost, packet.rpm, packet.throttle, packet.yawRate, packet.boosting);
            if (!string.IsNullOrEmpty(packet.weapon)) car.Weapons.SetNetworkGarageWeapon(packet.weapon);
            car.Weapons.SetNetworkFieldWeapon(packet.fieldWeapon, packet.fieldAmmo);
            car.Weapons.SetNetworkBeam(packet.beam, packet.beamEnd, packet.beamHeat);
            if (packet.tow > 0 && proxies.TryGetValue(packet.tow, out var towed) && towed) TowLink.ShowNetwork(car, towed);
            seenAt[packet.id] = Time.unscaledTime;
            if (packet.player && packet.owner == (int)network.LocalClientId && GameManager.Instance.Player != car)
                GameManager.Instance.SetCoopPlayer(car);
        }

        void ReceivePickup(ulong sender, FastBufferReader reader)
        {
            if (!IsClient || sender != NetworkManager.ServerClientId || !remoteWorldReady || remoteMode == 0) return;
            reader.ReadValueSafe(out string json);
            var packet = JsonUtility.FromJson<PickupPacket>(json);
            if (packet == null) return;
            if (!pickupProxies.TryGetValue(packet.id, out var pickup) || !pickup)
            {
                var weapon = string.IsNullOrEmpty(packet.weapon) ? null : WeaponRules.Find(packet.weapon);
                if ((PickupKind)packet.kind == PickupKind.Weapon && !weapon) return;
                pickup = CombatPickup.Create((PickupKind)packet.kind, packet.position, weapon, packet.amount);
                pickupProxies[packet.id] = pickup;
            }
            pickup.ApplyNetworkPosition(packet.position);
            pickupSeenAt[packet.id] = Time.unscaledTime;
        }

        public void PublishProjectile(Vector3 position, Vector3 direction, float speed, Color color, ExplosionKind kind, float lifetime)
        {
            if (!IsHost) return;
            var packet = new ProjectilePacket { position = position, direction = direction, speed = speed,
                color = color, kind = (int)kind, lifetime = lifetime };
            foreach (ulong clientId in network.ConnectedClientsIds)
                if (clientId != NetworkManager.ServerClientId)
                    Send(ProjectileMessage, clientId, packet, NetworkDelivery.Unreliable);
        }

        /// <summary>Host: sends a one-off effect to every guest (bowling balls, which are field ordnance the snapshots miss).</summary>
        public void PublishFx(CoopFx kind, Vector3 position, Vector3 velocity)
        {
            if (!IsHost) return;
            var packet = new FxPacket { kind = (int)kind, position = position, velocity = velocity };
            foreach (ulong clientId in network.ConnectedClientsIds)
                if (clientId != NetworkManager.ServerClientId)
                    Send(FxMessage, clientId, packet, NetworkDelivery.Unreliable);
        }

        void ReceiveFx(ulong sender, FastBufferReader reader)
        {
            if (!IsClient || sender != NetworkManager.ServerClientId || !remoteWorldReady || remoteMode == 0) return;
            reader.ReadValueSafe(out string json);
            var packet = JsonUtility.FromJson<FxPacket>(json);
            if (packet == null) return;
            if (packet.kind == (int)CoopFx.BowlingLaunch) FieldOrdnance.RollReplica(packet.position, packet.velocity);
            else if (packet.kind == (int)CoopFx.BowlingStrike) FieldOrdnance.ReplicaStrike(packet.position, packet.velocity);
        }

        public void PublishExplosion(Vector3 position, float radius, ExplosionKind kind)
        {
            if (!IsHost) return;
            var packet = new ExplosionPacket { position = position, radius = radius, kind = (int)kind };
            foreach (ulong clientId in network.ConnectedClientsIds)
                if (clientId != NetworkManager.ServerClientId)
                    Send(ExplosionMessage, clientId, packet, NetworkDelivery.Unreliable);
        }

        void ReceiveProjectile(ulong sender, FastBufferReader reader)
        {
            if (!IsClient || sender != NetworkManager.ServerClientId || !remoteWorldReady || remoteMode == 0) return;
            reader.ReadValueSafe(out string json);
            var packet = JsonUtility.FromJson<ProjectilePacket>(json);
            if (packet == null) return;
            ProjectileSystem.Fire(packet.position, packet.direction, packet.speed, 0, 0, null,
                packet.color, (ExplosionKind)packet.kind, packet.lifetime);
        }

        void ReceiveExplosion(ulong sender, FastBufferReader reader)
        {
            if (!IsClient || sender != NetworkManager.ServerClientId || !remoteWorldReady || remoteMode == 0) return;
            reader.ReadValueSafe(out string json);
            var packet = JsonUtility.FromJson<ExplosionPacket>(json);
            if (packet == null) return;
            ExplosionSystem.Detonate(packet.position, packet.radius, 0, null, (ExplosionKind)packet.kind);
        }

        public void PublishPropBreak(Vector3 position, string objectName)
        {
            if (!IsHost) return;
            var packet = new PropBreakPacket { position = position, name = objectName };
            brokenProps.Add(packet);
            foreach (ulong clientId in network.ConnectedClientsIds)
                if (clientId != NetworkManager.ServerClientId)
                    Send(PropBreakMessage, clientId, packet, NetworkDelivery.ReliableSequenced);
        }

        void ReceivePropBreak(ulong sender, FastBufferReader reader)
        {
            if (!IsClient || sender != NetworkManager.ServerClientId || !remoteWorldReady || remoteMode == 0) return;
            reader.ReadValueSafe(out string json);
            var packet = JsonUtility.FromJson<PropBreakPacket>(json);
            if (packet == null) return;
            var hits = Physics.OverlapSphere(packet.position, 2.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            DestructionSystem nearest = null;
            float distance = float.PositiveInfinity;
            foreach (var hit in hits)
            {
                var prop = hit.GetComponentInParent<DestructionSystem>();
                if (!prop || prop.IsDestroyed || prop.name != packet.name) continue;
                float squared = (prop.transform.position - packet.position).sqrMagnitude;
                if (squared < distance) { nearest = prop; distance = squared; }
            }
            nearest?.ApplyNetworkBreak();
        }

        void ReceiveMission(ulong sender, FastBufferReader reader)
        {
            if (!IsClient || sender != NetworkManager.ServerClientId || !remoteWorldReady) return;
            reader.ReadValueSafe(out string json);
            var packet = JsonUtility.FromJson<MissionPacket>(json);
            if (packet == null) return;
            GameManager.Instance.Mission.ApplyRemoteState(packet.objective, packet.position, packet.progress,
                packet.remaining, packet.stage, packet.score, packet.kills, packet.combo, packet.destruction,
                packet.money, packet.debrief);
            bool paused = packet.state == (int)GameState.Paused;
            if (HostPaused != paused)
            {
                HostPaused = paused;
                InputManager.Instance.SetEnabled(!paused && GameManager.Instance.IsPlaying && GameManager.Instance.Player);
            }
            GameManager.Instance.SetCoopOutcome((GameState)packet.state);
        }

        void ReceiveTuning(ulong sender, FastBufferReader reader)
        {
            if (!IsClient || sender != NetworkManager.ServerClientId) return;
            reader.ReadValueSafe(out string json);
            var packet = JsonUtility.FromJson<TuningPacket>(json);
            if (packet != null && packet.protocol == Protocol) DevTuning.SetCoopOverride(packet.tuning);
        }

        void ReceivePinState(ulong sender, FastBufferReader reader)
        {
            if (!IsClient || sender != NetworkManager.ServerClientId || !remoteWorldReady || remoteMode != 1 ||
                !GeneratedWorld.Active) return;
            reader.ReadValueSafe(out string json);
            var packet = JsonUtility.FromJson<PinStatePacket>(json);
            var world = GeneratedWorld.Active;
            if (packet == null || packet.protocol != Protocol || packet.seed != world.Seed || packet.flags == null) return;
            for (int i = 0; i < Mathf.Min(packet.flags.Length, world.Pins.Count); i++)
            {
                var pin = world.Pins[i];
                pin.discovered = packet.flags[i] == '1';
                world.Pins[i] = pin;
            }
        }

        string PinFlags()
        {
            var world = GeneratedWorld.Active;
            if (!world) return null;
            var flags = new char[world.Pins.Count];
            for (int i = 0; i < flags.Length; i++) flags[i] = world.Pins[i].discovered ? '1' : '0';
            return new string(flags);
        }

        void SendPinState(ulong clientId)
        {
            if (!IsHost || !GeneratedWorld.Active || GameManager.Instance.IsCombatTrial) return;
            Send(PinStateMessage, clientId, new PinStatePacket
                { protocol = Protocol, seed = GeneratedWorld.Active.Seed, flags = PinFlags() },
                NetworkDelivery.ReliableFragmentedSequenced);
        }

        void Update()
        {
            TickBalance();
            UpdateSpectatorCamera();
            if (IsClient && remoteWorldReady && remoteMode != 0)
            {
                queuedInteract |= InputManager.Instance.Interact;
                queuedSwap |= InputManager.Instance.SwapPressed;
                if (Time.unscaledTime >= inputAt)
                {
                    inputAt = Time.unscaledTime + .05f;
                    var input = InputManager.Instance;
                    var controls = new CoopControls { move = input.Move, aim = input.Aim, elevation = input.AimElevation,
                        primary = input.Primary, secondary = input.Secondary, drift = input.Drift, boost = input.Boost,
                        reverse = input.Reverse, manualReverse = input.ManualReverse,
                        interact = queuedInteract, swap = queuedSwap };
                    Send(InputMessage, NetworkManager.ServerClientId, new InputPacket { controls = controls }, NetworkDelivery.Unreliable);
                    queuedInteract = queuedSwap = false;
                }
                // A missed despawn packet cannot leave a ghost car on the map indefinitely.
                var expired = new List<int>();
                foreach (var pair in seenAt) if (Time.unscaledTime - pair.Value > 2f) expired.Add(pair.Key);
                foreach (int id in expired)
                {
                    if (proxies.TryGetValue(id, out var car) && car) Destroy(car.gameObject);
                    proxies.Remove(id); seenAt.Remove(id);
                }
                expired.Clear();
                foreach (var pair in pickupSeenAt) if (Time.unscaledTime - pair.Value > 1f) expired.Add(pair.Key);
                foreach (int id in expired)
                {
                    if (pickupProxies.TryGetValue(id, out var pickup) && pickup) Destroy(pickup.gameObject);
                    pickupProxies.Remove(id); pickupSeenAt.Remove(id);
                }
            }
            if (!IsHost) return;
            if (Time.unscaledTime >= tuningAt)
            {
                tuningAt = Time.unscaledTime + .5f;
                string tuning = JsonUtility.ToJson(DevTuning.Current);
                if (tuning != lastTuningJson)
                {
                    lastTuningJson = tuning;
                    var packet = new TuningPacket { protocol = Protocol, tuning = tuning };
                    foreach (ulong clientId in network.ConnectedClientsIds)
                        if (clientId != NetworkManager.ServerClientId)
                            Send(TuningMessage, clientId, packet, NetworkDelivery.ReliableFragmentedSequenced);
                }
            }
            if (GameManager.Instance.IsPlaying && !GameManager.Instance.IsCombatTrial &&
                GeneratedWorld.Active && Time.unscaledTime >= pinAt)
            {
                pinAt = Time.unscaledTime + .5f;
                string flags = PinFlags();
                if (flags != lastPinFlags)
                {
                    lastPinFlags = flags;
                    foreach (ulong clientId in network.ConnectedClientsIds)
                        if (clientId != NetworkManager.ServerClientId) SendPinState(clientId);
                }
            }
            if (GameManager.Instance.State != GameState.Playing && GameManager.Instance.State != GameState.Paused &&
                GameManager.Instance.State != GameState.Won && GameManager.Instance.State != GameState.Lost) return;
            if (Time.unscaledTime >= snapshotAt)
            {
                snapshotAt = Time.unscaledTime + .1f;
                foreach (var car in VehicleController.Active)
                {
                    if (!car || !car.Definition || !car.Damage) continue;
                    int owner = car == GameManager.Instance.Player ? (int)NetworkManager.ServerClientId : -1;
                    foreach (var guest in guestCars) if (guest.Value == car) { owner = (int)guest.Key; break; }
                    var ai = car.GetComponent<EnemyAI>();
                    if (!vehicleIds.TryGetValue(car, out int id)) { id = ++nextVehicleId; vehicleIds[car] = id; }
                    var packet = new VehiclePacket { id = id, owner = owner,
                        definition = car.Definition.id, player = car.IsPlayer,
                        archetype = ai ? ai.Archetype : 0, faction = ai ? (int)ai.Faction : 0,
                        friendly = ai && ai.IsFriendly, position = car.transform.position,
                        rotation = car.transform.rotation, velocity = car.Body ? car.Body.linearVelocity : Vector3.zero,
                        rpm = car.RPM, throttle = car.Throttle,
                        yawRate = car.Body ? car.Body.angularVelocity.y : 0, boosting = car.Boosting,
                        health = car.Damage.Health,
                        maximum = car.Damage.MaxHealth, boost = car.BoostCharge,
                        weapon = car.Weapons && car.Weapons.GarageWeapon ? car.Weapons.GarageWeapon.id : "",
                        fieldWeapon = car.Weapons && car.Weapons.FieldWeapon ? car.Weapons.FieldWeapon.id : "",
                        fieldAmmo = car.Weapons ? car.Weapons.FieldAmmo : 0,
                        beam = car.Weapons && car.Weapons.BeamActive,
                        beamEnd = car.Weapons ? car.Weapons.BeamEnd : Vector3.zero,
                        beamHeat = car.Weapons ? car.Weapons.BeamHeat : 0,
                        tow = car.Towing && vehicleIds.TryGetValue(car.Towing, out int towId) ? towId : 0 };
                    foreach (ulong clientId in network.ConnectedClientsIds)
                        if (clientId != NetworkManager.ServerClientId)
                            Send(VehicleMessage, clientId, packet, NetworkDelivery.Unreliable);
                }
            }
            if (Time.unscaledTime >= pickupAt)
            {
                pickupAt = Time.unscaledTime + .2f;
                foreach (var pickup in CombatPickup.Active)
                {
                    if (!pickup) continue;
                    if (!pickupIds.TryGetValue(pickup, out int id)) { id = ++nextPickupId; pickupIds[pickup] = id; }
                    var packet = new PickupPacket { id = id, kind = (int)pickup.Kind, amount = pickup.Amount,
                        weapon = pickup.Weapon ? pickup.Weapon.id : "", position = pickup.transform.position };
                    foreach (ulong clientId in network.ConnectedClientsIds)
                        if (clientId != NetworkManager.ServerClientId)
                            Send(PickupMessage, clientId, packet, NetworkDelivery.Unreliable);
                }
            }
            if (Time.unscaledTime >= missionAt)
            {
                missionAt = Time.unscaledTime + .2f;
                var game = GameManager.Instance;
                var mission = game.Mission;
                var packet = new MissionPacket { state = (int)game.State, stage = mission.Stage,
                    score = mission.Score, kills = mission.Kills, combo = mission.Combo,
                    destruction = mission.DestructionCount, money = mission.AwardedMoney,
                    objective = mission.Objective, debrief = mission.Debrief,
                    position = mission.ObjectivePosition, progress = mission.Progress, remaining = mission.Remaining };
                foreach (ulong clientId in network.ConnectedClientsIds)
                    if (clientId != NetworkManager.ServerClientId)
                        Send(MissionMessage, clientId, packet, NetworkDelivery.Unreliable);
            }
        }

        void UpdateSpectatorCamera()
        {
            if ((!IsHost && !IsClient) || !GameManager.Instance || !GameManager.Instance.Player ||
                !GameManager.Instance.Player.Damage || !GameManager.Instance.Player.Damage.IsDead ||
                !CameraController.Instance) return;
            VehicleController teammate = null;
            foreach (var car in VehicleController.Active)
                if (car && car.IsPlayer && car != GameManager.Instance.Player && car.Damage && !car.Damage.IsDead)
                {
                    teammate = car;
                    break;
                }
            if (teammate && CameraController.Instance.Target != teammate.transform)
            {
                CameraController.Instance.Target = teammate.transform;
                CameraController.Instance.Snap();
            }
        }

        void Send(string name, ulong clientId, object packet, NetworkDelivery delivery)
        {
            if (!network || !network.IsListening) return;
            string json = JsonUtility.ToJson(packet);
            using (var writer = new FastBufferWriter(FastBufferWriter.GetWriteSize(json), Allocator.Temp))
            {
                writer.WriteValueSafe(json);
                network.CustomMessagingManager.SendNamedMessage(name, clientId, writer, delivery);
            }
        }

        public void Leave()
        {
            bool wasClient = IsClient;
            ResetTransport();
            if (wasClient) GameManager.Instance?.LoadCoopWorld(0, 0, null);
            JoinCode = "";
            Status = "Offline";
        }

        void ResetTransport()
        {
            foreach (var pair in guestCars)
                if (pair.Value) Destroy(pair.Value.gameObject);
            if (network)
            {
                network.OnClientConnectedCallback -= Connected;
                network.OnClientDisconnectCallback -= Disconnected;
                if (network.IsListening) network.Shutdown();
                Destroy(network.gameObject);
            }
            network = null; transport = null;
            guestCars.Clear(); proxies.Clear(); seenAt.Clear(); pickupProxies.Clear(); pickupSeenAt.Clear();
            vehicleIds.Clear(); nextVehicleId = 0;
            pickupIds.Clear(); nextPickupId = 0;
            brokenProps.Clear();
            lastPinFlags = null;
            remoteWorldReady = false;
            HostPaused = false;
            HostTier = 0;
            queuedInteract = queuedSwap = false;
            lastTuningJson = null;
            ClearBalance();
            ResolveTuningDraft(false);
            ClearEndChoice();
            DevTuning.ClearCoopOverride();
        }
    }
}
