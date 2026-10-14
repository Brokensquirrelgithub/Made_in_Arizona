using UnityEngine;

namespace MadeInArizona
{
    /// <summary>
    /// Representative gearboxes for the cars' inspirations. The game uses automatic shifting even for cars
    /// whose chosen ratios come from a manual; its short maps require tach gearing scaled to the game's speed cap.
    /// </summary>
    public static class TransmissionProfiles
    {
        static readonly float[] FourAuto = { 2.84f, 1.62f, 1f, .70f };
        static readonly float[] FourManual = { 2.66f, 1.93f, 1.39f, 1f };
        static readonly float[] FiveManual = { 3.42f, 2.05f, 1.45f, 1.08f, .81f };
        static readonly float[] SixRace = { 3.10f, 2.13f, 1.62f, 1.31f, 1.07f, .86f };
        static readonly float[] SixDiesel = { 3.80f, 2.55f, 1.78f, 1.32f, 1f, .76f };
        // Keep the pre-transmission-update wheel-force curve. Vehicle-specific ratios shape the tachometer
        // and shift sound; changing their gear count must not quietly reduce stock acceleration.
        static readonly float[] DriveRatios = { 3.5f, 2.25f, 1.55f, 1.12f, .86f, .68f };

        public static float DriveRatio(int gear) => DriveRatios[Mathf.Clamp(gear - 1, 0, DriveRatios.Length - 1)];

        public static float[] ForVehicle(string id)
        {
            switch (id)
            {
                case "sidehustle": case "perennial": case "sonora": case "monument":
                case "executive": case "roam4": case "copperado": return FourAuto;
                case "contender": return FourManual;
                case "sunskip": case "skitter": case "vincent": return SixRace;
                case "foreclosure": return SixDiesel;
                // Ronin/Juniper, Geo Metro/Scarface, Jetta, Outback, Beat, Civic, AE86 and Cappuccino
                // inspirations all have a representative five-speed manual configuration.
                default: return FiveManual;
            }
        }

        /// <summary>
        /// At the stock speed cap, top gear sits at about 75% of the full-throttle shift point. This leaves
        /// the lower gears working longer before the soft speed cap and avoids a rapid series of low-speed shifts.
        /// Mechanical wheel torque is calibrated separately by DriveRatio.
        /// </summary>
        public static float TachScale(VehicleDefinition vehicle, float[] ratios)
        {
            const float wheelRadius = .34f;
            float stockSpeed = Mathf.Max(50f, vehicle.maxSpeed * VehicleStats.StockSpeedMultiplier) / 3.6f;
            float wheelRpm = stockSpeed / (2f * Mathf.PI * wheelRadius) * 60f;
            return VehicleController.FullThrottleUpshiftRpm * .75f /
                (wheelRpm * Mathf.Max(2.5f, vehicle.finalDrive) * ratios[ratios.Length - 1]);
        }
    }
}
