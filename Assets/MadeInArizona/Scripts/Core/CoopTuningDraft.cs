using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace MadeInArizona
{
    public sealed partial class CoopSession
    {
        [Serializable] sealed class TuningBundle
        {
            public DevTuning dev;
            public List<CarTuning> cars;
            public List<WeaponTuning> weapons;
        }

        string tuningBaseline;
        public bool HasTuningDraft => tuningBaseline != null;
        public bool EndChoicePending { get; private set; }
        public bool EndingSession { get; private set; }
        bool pausedForEnd;
        bool guestEnding;
        readonly HashSet<ulong> endAcknowledgments = new HashSet<ulong>();
        const string EndTuningMessage = "mia.coop.end-tuning.v2";
        const string EndTuningAckMessage = "mia.coop.end-tuning-ack.v2";
        [Serializable] sealed class EndTuningPacket { public int protocol; public bool save; public string tuning; }
        [Serializable] sealed class EndTuningAckPacket { public int protocol; }

        void RegisterDraftMessages(CustomMessagingManager messages)
        {
            messages.RegisterNamedMessageHandler(EndTuningMessage, ReceiveEndTuning);
            messages.RegisterNamedMessageHandler(EndTuningAckMessage, ReceiveEndTuningAck);
        }

        public void RequestEndSession()
        {
            if (!IsHost || EndChoicePending || EndingSession) return;
            EndChoicePending = true;
            pausedForEnd = GameManager.Instance && GameManager.Instance.IsPlaying;
            if (pausedForEnd) GameManager.Instance.Pause();
        }

        public void CancelEndSession()
        {
            if (!EndChoicePending) return;
            EndChoicePending = false;
            if (pausedForEnd && GameManager.Instance) GameManager.Instance.Resume();
            pausedForEnd = false;
        }

        public void FinishEndSession(bool saveAsDefault)
        {
            if (!IsHost || !EndChoicePending || EndingSession) return;
            EndChoicePending = false;
            EndingSession = true;
            StartCoroutine(EndAfterAcknowledgments(saveAsDefault));
        }

        IEnumerator EndAfterAcknowledgments(bool saveAsDefault)
        {
            endAcknowledgments.Clear();
            var packet = new EndTuningPacket { protocol = Protocol, save = saveAsDefault,
                tuning = saveAsDefault ? JsonUtility.ToJson(DevTuning.Local) : null };
            foreach (ulong clientId in network.ConnectedClientsIds)
                if (clientId != NetworkManager.ServerClientId)
                {
                    endAcknowledgments.Add(clientId);
                    Send(EndTuningMessage, clientId, packet, NetworkDelivery.ReliableFragmentedSequenced);
                }
            float deadline = Time.realtimeSinceStartup + 8f;
            float retryAt = Time.realtimeSinceStartup + .8f;
            while (endAcknowledgments.Count > 0 && Time.realtimeSinceStartup < deadline)
            {
                if (Time.realtimeSinceStartup >= retryAt)
                {
                    retryAt = Time.realtimeSinceStartup + .8f;
                    foreach (ulong clientId in endAcknowledgments)
                        Send(EndTuningMessage, clientId, packet, NetworkDelivery.ReliableFragmentedSequenced);
                }
                yield return null;
            }
            ResolveTuningDraft(saveAsDefault);
            Leave();
        }

        void ReceiveEndTuning(ulong sender, FastBufferReader reader)
        {
            if (!IsClient || sender != NetworkManager.ServerClientId) return;
            reader.ReadValueSafe(out string json);
            var packet = JsonUtility.FromJson<EndTuningPacket>(json);
            if (packet == null || packet.protocol != Protocol) return;
            if (!guestEnding)
            {
                guestEnding = true;
                if (packet.save)
                {
                    DevTuning.SetCoopOverride(packet.tuning);
                    DevTuning.AdoptHostSharedDefaults();
                    AdoptWinningProfiles();
                }
                ResolveTuningDraft(packet.save);
                StartCoroutine(FinishGuestAfterAck());
            }
            Send(EndTuningAckMessage, NetworkManager.ServerClientId,
                new EndTuningAckPacket { protocol = Protocol }, NetworkDelivery.ReliableSequenced);
        }

        IEnumerator FinishGuestAfterAck()
        {
            yield return new WaitForSecondsRealtime(.5f);
            if (IsClient) Leave();
        }

        void ReceiveEndTuningAck(ulong sender, FastBufferReader reader)
        {
            if (!IsHost || !EndingSession) return;
            reader.ReadValueSafe(out string json);
            var packet = JsonUtility.FromJson<EndTuningAckPacket>(json);
            if (packet != null && packet.protocol == Protocol)
                endAcknowledgments.Remove(sender);
        }

        void EndGuestDisconnected(ulong clientId) => endAcknowledgments.Remove(clientId);

        void BeginTuningDraft()
        {
            if (tuningBaseline != null) return;
            var settings = GameManager.Instance?.Save?.settings;
            if (settings == null) return;
            tuningBaseline = JsonUtility.ToJson(new TuningBundle
            {
                dev = settings.dev, cars = settings.cars, weapons = settings.weaponsBalance
            });
        }

        // Campaign saves may continue during a co-op session. Write the pre-session tuning to disk
        // until the host resolves the playtest draft, while the in-memory values remain live.
        internal SaveData ForPersistence(SaveData source)
        {
            if (tuningBaseline == null || source != GameManager.Instance?.Save) return source;
            var copy = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(source));
            var baseline = JsonUtility.FromJson<TuningBundle>(tuningBaseline);
            copy.settings.dev = baseline.dev;
            copy.settings.cars = baseline.cars;
            copy.settings.weaponsBalance = baseline.weapons;
            return copy;
        }

        void ResolveTuningDraft(bool saveAsDefault)
        {
            if (tuningBaseline == null) return;
            if (!saveAsDefault)
            {
                var baseline = JsonUtility.FromJson<TuningBundle>(tuningBaseline);
                var settings = GameManager.Instance?.Save?.settings;
                if (settings != null)
                {
                    settings.dev = baseline.dev ?? new DevTuning();
                    settings.cars = baseline.cars ?? new List<CarTuning>();
                    settings.weaponsBalance = baseline.weapons ?? new List<WeaponTuning>();
                }
            }
            tuningBaseline = null;
            DevTuning.Apply();
            if (GameManager.Instance?.Save != null) SaveSystem.Save(GameManager.Instance.Save);
        }

        void ClearEndChoice()
        {
            if (pausedForEnd && GameManager.Instance && GameManager.Instance.State == GameState.Paused)
                GameManager.Instance.Resume();
            EndChoicePending = EndingSession = pausedForEnd = guestEnding = false;
            endAcknowledgments.Clear();
        }
    }
}
