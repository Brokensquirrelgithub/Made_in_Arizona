using System;
using UnityEngine;

namespace MadeInArizona
{
    [Serializable]
    public sealed class VehicleStats
    {
        // Gameplay baseline. Authored vehicle assets retain their inspiration-sheet figures; parts and drivers stack on top.
        public const float StockPowerMultiplier = 1.18f;
        public const float StockGripMultiplier = 1.10f;
        public const float StockSpeedMultiplier = 1.15f;
        public float mass, horsepower, torque, maxSpeed, grip, turnSpeed, maxHealth;
        public float rideHeight, wheelbase, trackWidth, suspensionTravel, springStiffness, damping, finalDrive;
        public float cooling = 1f;
        public float shiftTimeMultiplier = 1f, terrainGripMultiplier = 1f, ramDamageMultiplier = 1f, pickupRadiusMultiplier = 1f;
        public float collisionProtection, electronicsResistance, driftNitroRegen, spikeDamage;
        public bool lightBar, repairFoam;
        /// <summary>Exterior to engine; totals one. Capacity changes scale all six layers equally.</summary>
        public float[] healthShares = { .5f, 0, 0, .15f, .15f, .2f };
        /// <summary>A turbocharger part is installed (physical engine voice: boost, turbine damping, whistle).</summary>
        public bool turbocharged;
        /// <summary>The salvage-yard 1.8 L engine replaces the original cylinder layout.</summary>
        public bool engineSwapped;
        /// <summary>Ignition cuts briefly at top-gear redline and prevents sustained over-rev damage.</summary>
        public bool revLimiter;
        public Drivetrain drivetrain;
        public Differential differential;

        public static VehicleStats From(VehicleDefinition v)
        {
            return new VehicleStats
            {
                mass = v.mass, horsepower = v.horsepower * StockPowerMultiplier,
                torque = v.torque * StockPowerMultiplier, maxSpeed = v.maxSpeed * StockSpeedMultiplier,
                grip = v.grip * StockGripMultiplier, turnSpeed = v.turnSpeed, maxHealth = v.maxHealth, rideHeight = v.rideHeight,
                wheelbase = v.wheelbase, trackWidth = v.trackWidth, suspensionTravel = v.suspensionTravel,
                springStiffness = v.springStiffness, damping = v.damping, finalDrive = v.finalDrive,
                drivetrain = v.drivetrain, differential = v.differential, cooling = 1f
            };
        }
    }
}
