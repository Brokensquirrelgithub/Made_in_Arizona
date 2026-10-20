using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace MadeInArizona
{
    public sealed partial class CoopSession
    {
        const string PlayerContactMessage = "mia.coop.player-contact.v1";

        [Serializable] sealed class PlayerContactPacket
        {
            public int protocol, sequence;
            public Vector3 deltaVelocity;
        }

        readonly Dictionary<ulong, float> lastPlayerContactSentAt = new Dictionary<ulong, float>();
        int playerContactSequence, lastPlayerContactReceived;
        public int PlayerContactsSent { get; private set; }
        public int PlayerContactsApplied { get; private set; }

        void RegisterPlayerContactMessages(CustomMessagingManager messages)
        {
            messages.RegisterNamedMessageHandler(PlayerContactMessage, ReceivePlayerContact);
        }

        void ResetPlayerContacts()
        {
            lastPlayerContactSentAt.Clear();
            playerContactSequence = lastPlayerContactReceived = 0;
            PlayerContactsSent = PlayerContactsApplied = 0;
        }

        /// <summary>The host hitbox's collision kick must reach the guest that owns that car's motion.</summary>
        public void ReportGuestPlayerContact(VehicleController car, Collision collision, Vector3 velocityBefore)
        {
            if (!IsHost || !network.IsListening || !car || !car.Body || !collision.collider ||
                !GameManager.Instance || !GameManager.Instance.IsPlaying) return;
            var other = collision.collider.GetComponentInParent<VehicleController>();
            if (!other || !other.IsPlayer || other == car) return;
            ulong owner = 0;
            bool found = false;
            foreach (var pair in guestCars)
                if (pair.Value == car) { owner = pair.Key; found = true; break; }
            if (!found || !network.ConnectedClients.ContainsKey(owner)) return;
            if (lastPlayerContactSentAt.TryGetValue(owner, out float sentAt) &&
                Time.unscaledTime - sentAt < .2f) return;
            Vector3 delta = car.Body.linearVelocity - velocityBefore;
            delta.y = 0;
            if (!FiniteContact(delta) || delta.sqrMagnitude < .25f) return;
            delta = Vector3.ClampMagnitude(delta, 25f);
            lastPlayerContactSentAt[owner] = Time.unscaledTime;
            var packet = new PlayerContactPacket {
                protocol = Protocol, sequence = ++playerContactSequence, deltaVelocity = delta
            };
            Send(PlayerContactMessage, owner, packet, NetworkDelivery.ReliableSequenced);
            PlayerContactsSent++;
            var row = MakeTrace("player_contact_sent", (int)owner,
                vehicleIds.TryGetValue(car, out int id) ? id : 0, car);
            row.referenceVelocity = delta;
            AddTrace(row);
        }

        void ReceivePlayerContact(ulong sender, FastBufferReader reader)
        {
            if (!IsClient || sender != NetworkManager.ServerClientId || !GameManager.Instance ||
                !GameManager.Instance.IsPlaying) return;
            reader.ReadValueSafe(out string json);
            if (json.Length > 512) return;
            var packet = JsonUtility.FromJson<PlayerContactPacket>(json);
            if (packet == null || packet.protocol != Protocol || packet.sequence <= lastPlayerContactReceived ||
                !FiniteContact(packet.deltaVelocity) || packet.deltaVelocity.magnitude > 25.01f) return;
            lastPlayerContactReceived = packet.sequence;
            var local = GameManager.Instance.Player;
            if (!local || !local.ApplyNetworkPlayerContact(packet.deltaVelocity)) return;
            PlayerContactsApplied++;
            var row = MakeTrace("player_contact_applied", (int)network.LocalClientId, ownVehicleId, local);
            row.referenceVelocity = packet.deltaVelocity;
            AddTrace(row);
        }

        static bool FiniteContact(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }
}
