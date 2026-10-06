using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace MadeInArizona
{
    public sealed partial class CoopSession
    {
        const string LoadoutMessage = "mia.coop.loadout.v2";
        const string BalanceMessage = "mia.coop.balance.v2";
        [Serializable] sealed class LoadoutPacket
        {
            public int protocol;
            public string carId, weaponId;
            public VehicleStats stats;
            public CarTuning car;
            public WeaponTuning slot1, slot2;
        }
        [Serializable] sealed class CarBalanceEntry
        {
            public int owner, users;
            public CarTuning profile;
        }
        [Serializable] sealed class WeaponBalanceEntry
        {
            public int owner, users;
            public WeaponTuning profile;
        }
        [Serializable] sealed class BalancePacket
        {
            public int protocol;
            public List<CarBalanceEntry> cars = new List<CarBalanceEntry>();
            public List<WeaponBalanceEntry> weapons = new List<WeaponBalanceEntry>();
        }

        readonly Dictionary<ulong, LoadoutPacket> guestLoadouts = new Dictionary<ulong, LoadoutPacket>();
        readonly Dictionary<ulong, string> guestLoadoutJson = new Dictionary<ulong, string>();
        readonly Dictionary<string, CarTuning> resolvedCars = new Dictionary<string, CarTuning>();
        readonly Dictionary<string, WeaponTuning> resolvedWeapons = new Dictionary<string, WeaponTuning>();
        readonly Dictionary<string, int> carOwners = new Dictionary<string, int>();
        readonly Dictionary<string, int> weaponOwners = new Dictionary<string, int>();
        float loadoutAt, balanceAt;
        string lastBalanceJson;

        void RegisterBalanceMessages(CustomMessagingManager messages)
        {
            messages.RegisterNamedMessageHandler(LoadoutMessage, ReceiveLoadout);
            messages.RegisterNamedMessageHandler(BalanceMessage, ReceiveBalance);
        }

        LoadoutPacket GuestLoadout(ulong clientId) => guestLoadouts.TryGetValue(clientId, out var packet) ? packet : null;

        LoadoutPacket LocalLoadout()
        {
            var game = GameManager.Instance;
            var save = game?.Save;
            if (save == null) return null;
            var player = game.Player;
            string carId = player && player.Definition ? player.Definition.id : ContentCatalog.Vehicles[save.selectedVehicle].id;
            string weaponId = player && player.Weapons && player.Weapons.GarageWeapon
                ? player.Weapons.GarageWeapon.id : save.selectedWeapon;
            string fieldId = player && player.Weapons && player.Weapons.FieldWeapon
                ? player.Weapons.FieldWeapon.id : null;
            return new LoadoutPacket { protocol = Protocol, carId = carId, weaponId = weaponId,
                stats = GarageManager.StatsFor(Array.Find(ContentCatalog.Vehicles, value => value && value.id == carId), save),
                car = BalanceTuning.LocalCar(carId), slot1 = BalanceTuning.LocalWeapon(weaponId),
                slot2 = string.IsNullOrEmpty(fieldId) ? null : BalanceTuning.LocalWeapon(fieldId) };
        }

        void SendLocalLoadout()
        {
            if (!IsClient) return;
            var packet = LocalLoadout();
            if (packet == null) return;
            Send(LoadoutMessage, NetworkManager.ServerClientId, packet, NetworkDelivery.ReliableFragmentedSequenced);
        }

        void ReceiveLoadout(ulong sender, FastBufferReader reader)
        {
            if (!IsHost || sender == NetworkManager.ServerClientId) return;
            reader.ReadValueSafe(out string json);
            var packet = JsonUtility.FromJson<LoadoutPacket>(json);
            if (packet == null || packet.protocol != Protocol) { network.DisconnectClient(sender); return; }
            var car = Array.Find(ContentCatalog.Vehicles, value => value && value.id == packet.carId);
            var weapon = WeaponRules.Find(packet.weaponId);
            if (!car || !weapon || !WeaponRules.GarageWeapon(packet.weaponId))
            { network.DisconnectClient(sender); return; }
            if (guestCars.TryGetValue(sender, out var existing) && existing)
            {
                // A guest can tune live, but changing the car or garage weapon requires another sortie.
                if (existing.Definition.id != packet.carId || existing.Weapons.GarageWeapon.id != packet.weaponId) return;
            }
            if (packet.car == null || packet.car.id != packet.carId)
                packet.car = CarTuning.FromLegacy(packet.carId, new DevTuning());
            packet.stats = SafeStats(packet.stats, car);
            packet.car.Clamp();
            if (packet.slot1 == null || packet.slot1.id != packet.weaponId)
                packet.slot1 = WeaponTuning.FromDefinition(weapon);
            packet.slot1.Clamp(weapon);
            string activeField = existing && existing.Weapons && existing.Weapons.FieldWeapon
                ? existing.Weapons.FieldWeapon.id : null;
            if (packet.slot2 == null || packet.slot2.id != activeField) packet.slot2 = null;
            else packet.slot2.Clamp(WeaponRules.Find(activeField));
            string normalized = JsonUtility.ToJson(packet);
            bool changed = !guestLoadoutJson.TryGetValue(sender, out string previous) || previous != normalized;
            guestLoadouts[sender] = packet;
            guestLoadoutJson[sender] = normalized;
            if (!existing && (GameManager.Instance.IsPlaying || GameManager.Instance.State == GameState.Paused)) SpawnGuest(sender);
            if (changed) lastBalanceJson = null;
        }

        static VehicleStats SafeStats(VehicleStats received, VehicleDefinition car)
        {
            var basis = VehicleStats.From(car);
            if (received == null) return basis;
            received.mass = Bound(received.mass, basis.mass, .5f, 2f);
            received.horsepower = Bound(received.horsepower, basis.horsepower, .25f, 8f);
            received.torque = Bound(received.torque, basis.torque, .25f, 8f);
            received.maxSpeed = Bound(received.maxSpeed, basis.maxSpeed, .5f, 3f);
            received.grip = Bound(received.grip, basis.grip, .25f, 4f);
            received.turnSpeed = Bound(received.turnSpeed, basis.turnSpeed, .4f, 3f);
            received.maxHealth = Bound(received.maxHealth, basis.maxHealth, .5f, 5f);
            received.rideHeight = Mathf.Clamp(float.IsNaN(received.rideHeight) || float.IsInfinity(received.rideHeight)
                ? basis.rideHeight : received.rideHeight, basis.rideHeight - .5f, basis.rideHeight + .5f);
            received.wheelbase = Bound(received.wheelbase, basis.wheelbase, .7f, 1.3f);
            received.trackWidth = Bound(received.trackWidth, basis.trackWidth, .7f, 1.3f);
            received.suspensionTravel = Bound(received.suspensionTravel, basis.suspensionTravel, .5f, 2f);
            received.springStiffness = Bound(received.springStiffness, basis.springStiffness, .3f, 4f);
            received.damping = Bound(received.damping, basis.damping, .3f, 4f);
            received.finalDrive = Bound(received.finalDrive, basis.finalDrive, .4f, 2f);
            received.cooling = Mathf.Clamp(float.IsNaN(received.cooling) || float.IsInfinity(received.cooling)
                ? 1 : received.cooling, .25f, 3f);
            if (!Enum.IsDefined(typeof(Drivetrain), received.drivetrain)) received.drivetrain = basis.drivetrain;
            if (!Enum.IsDefined(typeof(Differential), received.differential)) received.differential = basis.differential;
            return received;
        }
        static float Bound(float value, float basis, float low, float high) =>
            Mathf.Clamp(float.IsNaN(value) || float.IsInfinity(value) ? basis : value, basis * low, basis * high);

        void AddCar(BalancePacket packet, string id, int owner, CarTuning profile)
        {
            if (profile == null) return;
            var found = packet.cars.Find(value => value.profile.id == id);
            if (found != null) { found.users++; return; }
            packet.cars.Add(new CarBalanceEntry { owner = owner, users = 1, profile = profile });
        }

        void AddWeapon(BalancePacket packet, string id, int owner, WeaponTuning profile)
        {
            if (profile == null) return;
            var found = packet.weapons.Find(value => value.profile.id == id);
            if (found != null) { found.users++; return; }
            packet.weapons.Add(new WeaponBalanceEntry { owner = owner, users = 1, profile = profile });
        }

        BalancePacket BuildBalance()
        {
            var packet = new BalancePacket { protocol = Protocol };
            var game = GameManager.Instance;
            if (game && game.Player && game.Player.Weapons)
            {
                var host = game.Player;
                AddCar(packet, host.Definition.id, (int)NetworkManager.ServerClientId,
                    BalanceTuning.LocalCar(host.Definition.id));
                if (host.Weapons.GarageWeapon)
                    AddWeapon(packet, host.Weapons.GarageWeapon.id, (int)NetworkManager.ServerClientId,
                        BalanceTuning.LocalWeapon(host.Weapons.GarageWeapon.id));
                if (host.Weapons.FieldWeapon)
                    AddWeapon(packet, host.Weapons.FieldWeapon.id, (int)NetworkManager.ServerClientId,
                        BalanceTuning.LocalWeapon(host.Weapons.FieldWeapon.id));
            }
            var ids = new List<ulong>(guestCars.Keys);
            ids.Sort(); // NGO client IDs rise in join order; the first guest owns a shared item unless the host uses it.
            foreach (ulong id in ids)
            {
                if (!guestCars.TryGetValue(id, out var car) || !car || !guestLoadouts.TryGetValue(id, out var loadout)) continue;
                AddCar(packet, car.Definition.id, (int)id, loadout.car);
                if (car.Weapons.GarageWeapon)
                    AddWeapon(packet, car.Weapons.GarageWeapon.id, (int)id, loadout.slot1);
                if (car.Weapons.FieldWeapon)
                {
                    var field = car.Weapons.FieldWeapon;
                    AddWeapon(packet, field.id, (int)id,
                        loadout.slot2 != null && loadout.slot2.id == field.id
                            ? loadout.slot2 : WeaponTuning.FromDefinition(field));
                }
            }
            packet.cars.Sort((a,b)=>string.CompareOrdinal(a.profile.id,b.profile.id));
            packet.weapons.Sort((a,b)=>string.CompareOrdinal(a.profile.id,b.profile.id));
            return packet;
        }

        void SetResolved(BalancePacket packet)
        {
            resolvedCars.Clear(); resolvedWeapons.Clear(); carOwners.Clear(); weaponOwners.Clear();
            foreach (var item in packet.cars)
            {
                if (item?.profile == null) continue;
                resolvedCars[item.profile.id] = item.profile;
                carOwners[item.profile.id] = item.owner;
            }
            foreach (var item in packet.weapons)
            {
                if (item?.profile == null) continue;
                resolvedWeapons[item.profile.id] = item.profile;
                weaponOwners[item.profile.id] = item.owner;
            }
            DevTuning.Apply();
        }

        void TickBalance()
        {
            if (IsClient && Time.unscaledTime >= loadoutAt)
            {
                loadoutAt = Time.unscaledTime + .4f;
                SendLocalLoadout();
            }
            if (!IsHost || Time.unscaledTime < balanceAt) return;
            balanceAt = Time.unscaledTime + .4f;
            var packet = BuildBalance();
            string json = JsonUtility.ToJson(packet);
            if (json == lastBalanceJson) return;
            lastBalanceJson = json;
            SetResolved(packet);
            foreach (ulong clientId in network.ConnectedClientsIds)
                if (clientId != NetworkManager.ServerClientId)
                    Send(BalanceMessage, clientId, packet, NetworkDelivery.ReliableFragmentedSequenced);
        }

        void ReceiveBalance(ulong sender, FastBufferReader reader)
        {
            if (!IsClient || sender != NetworkManager.ServerClientId) return;
            reader.ReadValueSafe(out string json);
            var packet = JsonUtility.FromJson<BalancePacket>(json);
            if (packet == null || packet.protocol != Protocol || packet.cars == null || packet.weapons == null) return;
            if (packet.cars.Count > MaxPlayers || packet.weapons.Count > MaxPlayers * 2) return;
            foreach (var item in packet.cars)
            {
                if (item?.profile == null || !Array.Exists(ContentCatalog.Vehicles, car => car && car.id == item.profile.id)) return;
                item.profile.Clamp();
            }
            foreach (var item in packet.weapons)
            {
                var weapon = item?.profile == null ? null : WeaponRules.Find(item.profile.id);
                if (!weapon) return;
                item.profile.Clamp(weapon);
            }
            SetResolved(packet);
        }

        public bool TryCarTuning(string id, out CarTuning profile)
        {
            profile = null;
            if (string.IsNullOrEmpty(id) || !resolvedCars.TryGetValue(id, out profile)) return false;
            if (IsClient && carOwners.TryGetValue(id, out int owner) && owner == (int)network.LocalClientId)
            { profile = null; return false; }
            if (IsHost && carOwners.TryGetValue(id, out int host) && host == (int)NetworkManager.ServerClientId)
            { profile = null; return false; }
            return true;
        }
        public bool TryWeaponTuning(string id, out WeaponTuning profile)
        {
            profile = null;
            if (string.IsNullOrEmpty(id) || !resolvedWeapons.TryGetValue(id, out profile)) return false;
            if (IsClient && weaponOwners.TryGetValue(id, out int owner) && owner == (int)network.LocalClientId)
            { profile = null; return false; }
            if (IsHost && weaponOwners.TryGetValue(id, out int host) && host == (int)NetworkManager.ServerClientId)
            { profile = null; return false; }
            return true;
        }
        public bool CanEditCar(string id) => !IsClient || !carOwners.TryGetValue(id, out int owner) || owner == (int)network.LocalClientId;
        public bool CanEditWeapon(string id) => !IsClient || !weaponOwners.TryGetValue(id, out int owner) || owner == (int)network.LocalClientId;

        void AdoptWinningProfiles()
        {
            if (!IsClient || !GameManager.Instance || !GameManager.Instance.Player) return;
            var player = GameManager.Instance.Player;
            string carId = player.Definition ? player.Definition.id : null;
            if (!string.IsNullOrEmpty(carId) && !CanEditCar(carId) && resolvedCars.TryGetValue(carId, out var winningCar))
            {
                var list = GameManager.Instance.Save.settings.cars;
                list.RemoveAll(item => item != null && item.id == carId);
                list.Add(JsonUtility.FromJson<CarTuning>(JsonUtility.ToJson(winningCar)));
            }
            if (!player.Weapons) return;
            AdoptWeapon(player.Weapons.GarageWeapon);
            AdoptWeapon(player.Weapons.FieldWeapon);
        }

        void AdoptWeapon(WeaponDefinition weapon)
        {
            if (!weapon || CanEditWeapon(weapon.id) || !resolvedWeapons.TryGetValue(weapon.id, out var winning)) return;
            var list = GameManager.Instance.Save.settings.weaponsBalance;
            list.RemoveAll(item => item != null && item.id == weapon.id);
            list.Add(JsonUtility.FromJson<WeaponTuning>(JsonUtility.ToJson(winning)));
        }

        void GuestLeftBalance(ulong clientId)
        {
            guestLoadouts.Remove(clientId);
            guestLoadoutJson.Remove(clientId);
            lastBalanceJson = null;
        }
        void ClearBalance()
        {
            guestLoadouts.Clear(); guestLoadoutJson.Clear(); resolvedCars.Clear(); resolvedWeapons.Clear();
            carOwners.Clear(); weaponOwners.Clear();
            lastBalanceJson = null;
        }
    }
}
