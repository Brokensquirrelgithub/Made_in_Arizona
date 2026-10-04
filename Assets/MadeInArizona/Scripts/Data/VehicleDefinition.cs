using UnityEngine;
namespace MadeInArizona
{
    public enum Drivetrain { FWD, RWD, AWD }
    public enum Differential { Open, LimitedSlip, Locked }
    [CreateAssetMenu(menuName = "Made In Arizona/Vehicle")]
    public sealed class VehicleDefinition : ScriptableObject
    {
        public string id, displayName; [TextArea] public string description;
        public int contentOrder, cost, unlockMission;
        public float mass = 1000, horsepower = 100, torque = 140, maxSpeed = 110, grip = 1, turnSpeed = 90, maxHealth = 200;
        public float rideHeight = .7f, wheelbase = 2.3f, trackWidth = 1.5f, suspensionTravel = .3f, springStiffness = 1, damping = .7f, finalDrive = 4.1f;
        public Color color = Color.cyan;
        public Drivetrain drivetrain; public Differential differential;
        /// <summary>EngineLayouts id for the physical engine voice (cylinders, firing order, headers, exhaust).</summary>
        public string engineLayout;
        public EngineLayout Engine => EngineLayouts.Find(string.IsNullOrEmpty(engineLayout) ? EngineLayouts.ForVehicle(id) : engineLayout);
    }
}
