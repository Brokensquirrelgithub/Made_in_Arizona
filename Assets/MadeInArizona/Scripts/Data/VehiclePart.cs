using UnityEngine;
namespace MadeInArizona
{
    [CreateAssetMenu(menuName = "Made In Arizona/Mechanical part")]
    public sealed class VehiclePart : ScriptableObject
    {
        public string id, displayName, category; [TextArea] public string description;
        public int contentOrder, cost, unlockMission, salvageCost;
        public float mass, hpMultiplier = 1, torqueMultiplier = 1, gripMultiplier = 1, coolingMultiplier = 1, healthBonus;
        public float maxSpeedMultiplier = 1, turnMultiplier = 1, rideHeightBonus, suspensionMultiplier = 1, springMultiplier = 1, dampingMultiplier = 1, finalDriveMultiplier = 1;
        public bool changesDrivetrain, changesDifferential;
        /// <summary>Fits a turbocharger: the physical engine voice adds boost, turbine damping and whistle.</summary>
        public bool turbocharger;
        public Drivetrain drivetrain; public Differential differential;
        public string[] compatibleVehicles = new string[0];
    }
}
