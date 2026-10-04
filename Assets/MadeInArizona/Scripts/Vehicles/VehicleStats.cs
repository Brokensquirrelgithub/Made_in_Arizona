using System;
using UnityEngine;

namespace MadeInArizona
{
    [Serializable]
    public sealed class VehicleStats
    {
        public float mass, horsepower, torque, maxSpeed, grip, turnSpeed, maxHealth;
        public float rideHeight, wheelbase, trackWidth, suspensionTravel, springStiffness, damping, finalDrive;
        public float cooling = 1f;
        /// <summary>A turbocharger part is installed (physical engine voice: boost, turbine damping, whistle).</summary>
        public bool turbocharged;
        public Drivetrain drivetrain;
        public Differential differential;

        public static VehicleStats From(VehicleDefinition v)
        {
            return new VehicleStats
            {
                mass = v.mass, horsepower = v.horsepower, torque = v.torque, maxSpeed = v.maxSpeed,
                grip = v.grip, turnSpeed = v.turnSpeed, maxHealth = v.maxHealth, rideHeight = v.rideHeight,
                wheelbase = v.wheelbase, trackWidth = v.trackWidth, suspensionTravel = v.suspensionTravel,
                springStiffness = v.springStiffness, damping = v.damping, finalDrive = v.finalDrive,
                drivetrain = v.drivetrain, differential = v.differential, cooling = 1f
            };
        }
    }
}
