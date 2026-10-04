using System;
using UnityEngine;
namespace MadeInArizona
{
    public static class GarageManager
    {
        public static string LastMessage { get; private set; }
        public static bool Compatible(VehiclePart part,VehicleDefinition vehicle)
        { return part.compatibleVehicles==null||part.compatibleVehicles.Length==0||Array.IndexOf(part.compatibleVehicles,vehicle.id)>=0; }
        public static VehicleStats StatsFor(VehicleDefinition definition,SaveData save)
        {
            ContentCatalog.EnsureLoaded(); var stats=VehicleStats.From(definition);
            if(save==null)return stats;
            foreach(var p in ContentCatalog.Parts)
            {
                if(!save.installedParts.Contains(p.id)||!save.ownedParts.Contains(p.id)||!Compatible(p,definition))continue;
                stats.mass+=p.mass; stats.horsepower*=p.hpMultiplier; stats.torque*=p.torqueMultiplier; stats.grip*=p.gripMultiplier;
                stats.cooling*=p.coolingMultiplier; stats.maxHealth+=p.healthBonus; stats.maxSpeed*=p.maxSpeedMultiplier; stats.turnSpeed*=p.turnMultiplier;
                stats.rideHeight+=p.rideHeightBonus; stats.suspensionTravel*=p.suspensionMultiplier; stats.springStiffness*=p.springMultiplier;
                stats.damping*=p.dampingMultiplier; stats.finalDrive*=p.finalDriveMultiplier;
                if(p.changesDrivetrain)stats.drivetrain=p.drivetrain; if(p.changesDifferential)stats.differential=p.differential;
                if(p.turbocharger)stats.turbocharged=true;
            }
            var driver=ContentCatalog.Drivers[Mathf.Clamp(save.selectedDriver,0,ContentCatalog.Drivers.Length-1)];
            stats.horsepower*=driver.powerMultiplier; stats.torque*=driver.powerMultiplier; stats.grip*=driver.gripMultiplier;
            stats.finalDrive*=save.finalDriveTuning; stats.maxSpeed/=save.finalDriveTuning; stats.rideHeight+=save.rideHeightTuning;
            stats.mass=Mathf.Max(400,stats.mass); stats.cooling=Mathf.Clamp(stats.cooling,.25f,3); stats.grip=Mathf.Clamp(stats.grip,.4f,2.2f);
            return stats;
        }
        public static bool BuyPart(VehiclePart part,SaveData save)
        {
            if(save.ownedParts.Contains(part.id)){LastMessage="Already owned.";return false;}
            if(save.unlockedMission<part.unlockMission){LastMessage="Complete more campaign jobs to unlock this part.";return false;}
            if(save.money<part.cost||save.salvage<part.salvageCost){LastMessage="Insufficient cash or salvage.";return false;}
            save.money-=part.cost; save.salvage-=part.salvageCost; save.ownedParts.Add(part.id); LastMessage="Purchased "+part.displayName+". Install it to apply its effects.";
            SaveSystem.Save(save); return true;
        }
        public static bool BuyOrSelectWeapon(WeaponDefinition weapon, SaveData save)
        {
            if (weapon == null || !WeaponRules.GarageWeapon(weapon.id)) { LastMessage = "That weapon can only be found in the field."; return false; }
            if (!save.ownedWeapons.Contains(weapon.id))
            {
                int cost = WeaponRules.ScrapCost(weapon.id);
                if (save.salvage < cost) { LastMessage = "Need " + cost + " scrap to buy this weapon."; return false; }
                save.salvage -= cost; save.ownedWeapons.Add(weapon.id);
            }
            save.selectedWeapon = weapon.id;
            LastMessage = weapon.displayName + " fitted to RT.";
            SaveSystem.Save(save); return true;
        }
        public static void TogglePart(VehiclePart part,SaveData save)
        {
            if(save.installedParts.Contains(part.id)){save.installedParts.Remove(part.id);LastMessage="Removed "+part.displayName;SaveSystem.Save(save);return;}
            if(!save.ownedParts.Contains(part.id)){LastMessage="Purchase this part first.";return;}
            if(!Compatible(part,ContentCatalog.Vehicles[save.selectedVehicle])){LastMessage="This part does not fit the selected vehicle.";return;}
            foreach(var p in ContentCatalog.Parts)if(p.category==part.category)save.installedParts.Remove(p.id);
            save.installedParts.Add(part.id); LastMessage="Installed "+part.displayName; SaveSystem.Save(save);
        }
        public static bool SelectVehicle(int index,SaveData save)
        {
            ContentCatalog.EnsureLoaded();if(index<0||index>=ContentCatalog.Vehicles.Length)return false;
            var car=ContentCatalog.Vehicles[index];if(save.unlockedMission<car.unlockMission){LastMessage="Vehicle unlocks after more campaign jobs.";return false;}
            if(!save.ownedVehicles.Contains(car.id))
            {
                if(save.money<car.cost){LastMessage="Need $"+car.cost+" to purchase this vehicle.";return false;}
                save.money-=car.cost;save.ownedVehicles.Add(car.id);
            }
            save.selectedVehicle=index; LastMessage=car.displayName+" selected."; SaveSystem.Save(save);return true;
        }
        public static void Tune(float finalDrive,float rideHeight,SaveData save)
        { save.finalDriveTuning=Mathf.Clamp(finalDrive,.85f,1.2f);save.rideHeightTuning=Mathf.Clamp(rideHeight,-.08f,.15f);SaveSystem.Save(save); }
    }
}
