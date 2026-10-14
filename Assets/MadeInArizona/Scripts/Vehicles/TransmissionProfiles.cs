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
        /// At the stock speed cap, top gear sits at about 90% of the full-throttle shift point. This makes every
        /// forward gear usable on the game's compressed roads without multiplying mechanical wheel torque.
        /// </summary>
        public static float TachScale(VehicleDefinition vehicle, float[] ratios)
        {
            const float wheelRadius = .34f;
            float stockSpeed = Mathf.Max(50f, vehicle.maxSpeed * VehicleStats.StockSpeedMultiplier) / 3.6f;
            float wheelRpm = stockSpeed / (2f * Mathf.PI * wheelRadius) * 60f;
            return VehicleController.FullThrottleUpshiftRpm * .9f /
                (wheelRpm * Mathf.Max(2.5f, vehicle.finalDrive) * ratios[ratios.Length - 1]);
        }
    }
}
