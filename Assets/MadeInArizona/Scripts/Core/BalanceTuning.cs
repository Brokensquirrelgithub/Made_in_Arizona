using System;
using System.Collections.Generic;
using UnityEngine;

namespace MadeInArizona
{
    [Serializable]
    public sealed class CarTuning
    {
        public string id;
        public float steering, acceleration, grip, nitro;
        public float driftGrip, driftYaw, driftKick, driftSpeedLoss, driftThrottle, driftRecovery;
        public float enginePitch, engineLoadLevel, engineOverrunLevel, turboWhineLevel, nitroRoarLevel;
        public float exhaustPopLevel, shiftLevel, enginePulseVariation, engineBody, engineRasp, engineSaturation;
        public float enginePulsePressure, enginePulseAttack, enginePulseDecay, engineBrightness, engineResonance;
        public bool enginePreserveEdges, enginePhysical;

        public static CarTuning FromLegacy(string carId, DevTuning source)
        {
            var result = new CarTuning { id = carId };
            foreach (var control in DevControl.All)
                if (control.scope == DevScope.Car)
                    typeof(CarTuning).GetField(control.field.Name).SetValue(result, control.field.GetValue(source));
            result.enginePreserveEdges = source.enginePreserveEdges;
            result.enginePhysical = source.enginePhysical;
            return result;
        }

        public void CopyTo(DevTuning target)
        {
            foreach (var control in DevControl.All)
                if (control.scope == DevScope.Car)
                    control.field.SetValue(target, typeof(CarTuning).GetField(control.field.Name).GetValue(this));
            target.enginePreserveEdges = enginePreserveEdges;
            target.enginePhysical = enginePhysical;
        }

        public void Clamp()
        {
            foreach (var control in DevControl.All)
            {
                if (control.scope != DevScope.Car) continue;
                var field = typeof(CarTuning).GetField(control.field.Name);
                float value = (float)field.GetValue(this);
                if (float.IsNaN(value) || float.IsInfinity(value))
                    value = (float)control.field.GetValue(new DevTuning());
                field.SetValue(this, Mathf.Clamp(value, control.min, control.max));
            }
        }
    }

    [Serializable]
    public sealed class WeaponTuning
    {
        public string id;
        public float damage, fireRate, speed, blastRadius;

        public static WeaponTuning FromDefinition(WeaponDefinition weapon) => new WeaponTuning
        {
            id = weapon.id, damage = weapon.damage, fireRate = weapon.fireRate,
            speed = weapon.speed, blastRadius = weapon.blastRadius
        };

        public void Clamp(WeaponDefinition weapon)
        {
            damage = Limit(damage, weapon.damage, .1f, 5f);
            fireRate = Limit(fireRate, weapon.fireRate, .25f, 4f);
            speed = Limit(speed, weapon.speed, .25f, 3f);
            blastRadius = weapon.blastRadius <= 0 ? 0 : Limit(blastRadius, weapon.blastRadius, 0, 3f);
        }

        static float Limit(float value, float basis, float low, float high)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) value = basis;
            return Mathf.Clamp(value, basis * low, basis * high);
        }
    }

    public static class BalanceTuning
    {
        public static CarTuning LocalCar(string id)
        {
            var settings = GameManager.Instance?.Save?.settings;
            if (settings == null || string.IsNullOrEmpty(id)) return null;
            if (settings.cars == null) settings.cars = new List<CarTuning>();
            var profile = settings.cars.Find(value => value != null && value.id == id);
            if (profile != null) return profile;
            profile = CarTuning.FromLegacy(id, DevTuning.Local);
            settings.cars.Add(profile);
            return profile;
        }

        public static WeaponTuning LocalWeapon(string id)
        {
            var settings = GameManager.Instance?.Save?.settings;
            var weapon = WeaponRules.Find(id);
            if (settings == null || !weapon) return null;
            if (settings.weaponsBalance == null) settings.weaponsBalance = new List<WeaponTuning>();
            var profile = settings.weaponsBalance.Find(value => value != null && value.id == id);
            if (profile != null) return profile;
            profile = WeaponTuning.FromDefinition(weapon);
            settings.weaponsBalance.Add(profile);
            return profile;
        }

        public static CarTuning Car(string id)
        {
            var coop = CoopSession.Instance;
            return coop != null && coop.TryCarTuning(id, out var profile) ? profile : LocalCar(id);
        }

        public static WeaponTuning Weapon(string id)
        {
            var coop = CoopSession.Instance;
            return coop != null && coop.TryWeaponTuning(id, out var profile) ? profile : LocalWeapon(id);
        }
    }
}
