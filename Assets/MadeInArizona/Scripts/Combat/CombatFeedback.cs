using System;
using UnityEngine;

namespace MadeInArizona
{
    /// <summary>One authoritative stream of player hit confirmations for combat UI and audio.</summary>
    public static class CombatFeedback
    {
        public static float LastHitTime { get; private set; } = float.NegativeInfinity;
        public static float LastKillTime { get; private set; } = float.NegativeInfinity;
        public static float LastHitDamage { get; private set; }
        public static Vector3 LastHitPoint { get; private set; }
        /// <summary>Whether the latest confirmation came from the player (rather than a friendly AI), for hit sounds.</summary>
        public static bool LastHitByPlayer { get; private set; }

        public static event Action<float, Vector3> HitConfirmed;
        public static event Action<Vector3> KillConfirmed;

        /// <summary>
        /// Reports damage after health has been changed. Only hits by the player or a friendly AI
        /// against a hostile vehicle become confirmations.
        /// </summary>
        public static void ReportHit(VehicleController target, float damage, Vector3 point, GameObject source, bool killed)
        {
            if (target == null || source == null || damage <= 0) return;

            var sourceVehicle = source.GetComponentInParent<VehicleController>();
            if (sourceVehicle == null) return;
            var sourceAI = sourceVehicle.GetComponent<EnemyAI>();
            bool sourceFriendly = sourceVehicle.IsPlayer || (sourceAI != null && sourceAI.IsFriendly);

            var targetAI = target.GetComponent<EnemyAI>();
            bool targetFriendly = target.IsPlayer || (targetAI != null && targetAI.IsFriendly);
            if (!sourceFriendly || targetFriendly) return;

            LastHitTime = Time.time;
            LastHitByPlayer = sourceVehicle.IsPlayer;
            LastHitDamage = damage;
            LastHitPoint = point;
            HitConfirmed?.Invoke(damage, point);
            if (!killed) return;

            LastKillTime = Time.time;
            KillConfirmed?.Invoke(point);
        }
    }
}
