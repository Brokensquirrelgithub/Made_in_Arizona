using System;
using System.Collections.Generic;
using UnityEngine;
namespace MadeInArizona
{
    public static class GarageManager
    {
        public const float PlayerPowerMultiplier = 1.20f;
        public static string LastMessage { get; private set; }
        public static bool Compatible(VehiclePart part,VehicleDefinition vehicle)
        {
            if(!part||!vehicle)return false;
            if(part.compatibleVehicles!=null&&part.compatibleVehicles.Length>0&&Array.IndexOf(part.compatibleVehicles,vehicle.id)<0)return false;
            if(part.compatibilityClass==PartCompatibility.CarSpecific)
                return part.compatibleVehicles!=null&&Array.IndexOf(part.compatibleVehicles,vehicle.id)>=0;
            if(part.compatibilityClass==PartCompatibility.Chassis)
            {
                if(part.compatibleChassis==null||vehicle.chassisTags==null)return false;
                foreach(string tag in part.compatibleChassis)if(Array.IndexOf(vehicle.chassisTags,tag)>=0)return true;
                return false;
            }
            return true;
        }
        public static VehicleLoadout Loadout(SaveData save,string vehicleId)
        { return save?.loadouts?.Find(l=>l!=null&&l.vehicleId==vehicleId); }
        public static VehiclePart FindPart(string id)
        { return string.IsNullOrEmpty(id)?null:Array.Find(ContentCatalog.Parts,p=>p&&p.id==id); }
        public static VehiclePart Installed(SaveData save,string vehicleId,EquipmentSlot slot,int accessoryIndex=0)
        {
            var loadout=Loadout(save,vehicleId);
            if(loadout==null)return null;
            return FindPart(slot==EquipmentSlot.Accessory?loadout.accessories[accessoryIndex]:loadout.mechanical[(int)slot]);
        }
        public static void NormalizeLoadouts(SaveData save)
        {
            if(save.loadouts==null)save.loadouts=new List<VehicleLoadout>();
            save.loadouts.RemoveAll(l=>l==null||string.IsNullOrEmpty(l.vehicleId)||
                !Array.Exists(ContentCatalog.Vehicles,v=>v.id==l.vehicleId));
            var seen=new HashSet<string>();save.loadouts.RemoveAll(l=>!seen.Add(l.vehicleId));
            foreach(var vehicle in ContentCatalog.Vehicles)
            {
                var loadout=Loadout(save,vehicle.id);
                if(loadout==null){loadout=new VehicleLoadout{vehicleId=vehicle.id,weapon=save.selectedWeapon};save.loadouts.Add(loadout);}
                if(loadout.mechanical==null||loadout.mechanical.Length!=6)loadout.mechanical=new string[6];
                if(loadout.accessories==null||loadout.accessories.Length!=4)loadout.accessories=new string[4];
                if(save.equipmentVersion==0)
                {
                    // Legacy installs affected every compatible owned car. The most recently installed
                    // part wins each consolidated slot; displaced purchases remain in ownedParts.
                    foreach(string id in save.installedParts)
                    {
                        var part=FindPart(id);if(!part||!Compatible(part,vehicle))continue;
                        if(part.equipmentSlot==EquipmentSlot.Accessory)
                        {
                            for(int i=0;i<4;i++)if(string.IsNullOrEmpty(loadout.accessories[i]))
                            {loadout.accessories[i]=id;break;}
                        }
                        else loadout.mechanical[(int)part.equipmentSlot]=id;
                    }
                }
                for(int i=0;i<6;i++)
                {
                    var part=FindPart(loadout.mechanical[i]);
                    if(!part||part.equipmentSlot!=(EquipmentSlot)i||!Compatible(part,vehicle)||!save.ownedParts.Contains(part.id))loadout.mechanical[i]=null;
                }
                var accessories=new HashSet<string>();
                for(int i=0;i<4;i++)
                {
                    var part=FindPart(loadout.accessories[i]);
                    if(!part||part.equipmentSlot!=EquipmentSlot.Accessory||!Compatible(part,vehicle)||
                        !save.ownedParts.Contains(part.id)||!accessories.Add(part.id))loadout.accessories[i]=null;
                }
                if(string.IsNullOrEmpty(loadout.weapon)||!save.ownedWeapons.Contains(loadout.weapon)||!WeaponRules.GarageWeapon(loadout.weapon))loadout.weapon="riveter";
                loadout.paintIndex=Mathf.Clamp(loadout.paintIndex,0,4);
            }
            save.equipmentVersion=1;
            save.selectedWeapon=Loadout(save,ContentCatalog.Vehicles[save.selectedVehicle].id).weapon;
        }
        public static bool TryEquip(VehiclePart part,SaveData save,EquipmentSlot slot,int accessoryIndex=0,bool persist=true)
        {
            ContentCatalog.EnsureLoaded();
            if(!part||save==null||part.equipmentSlot!=slot||slot==EquipmentSlot.Accessory&&(accessoryIndex<0||accessoryIndex>=4))
            {LastMessage="This part does not fit that slot.";return false;}
            if(FindPart(part.id)!=part){LastMessage="Unknown equipment definition.";return false;}
            if(save.equipmentVersion==0)NormalizeLoadouts(save);
            var vehicle=ContentCatalog.Vehicles[save.selectedVehicle];
            if(!Compatible(part,vehicle)){LastMessage="This part does not fit the selected vehicle.";return false;}
            if(!save.ownedParts.Contains(part.id)){LastMessage="Purchase this part first.";return false;}
            var loadout=Loadout(save,vehicle.id);if(loadout==null){LastMessage="Vehicle loadout unavailable.";return false;}
            if(slot==EquipmentSlot.Accessory)
            {
                for(int i=0;i<4;i++)if(i!=accessoryIndex&&loadout.accessories[i]==part.id)
                {LastMessage="This accessory is already installed.";return false;}
                loadout.accessories[accessoryIndex]=part.id;
            }
            else loadout.mechanical[(int)slot]=part.id;
            LastMessage="Installed "+part.displayName;if(persist)SaveSystem.Save(save);return true;
        }
        public static bool Unequip(SaveData save,EquipmentSlot slot,int accessoryIndex=0,bool persist=true)
        {
            if(save==null||slot==EquipmentSlot.Accessory&&(accessoryIndex<0||accessoryIndex>=4))return false;
            if(save.equipmentVersion==0)NormalizeLoadouts(save);
            var loadout=Loadout(save,ContentCatalog.Vehicles[save.selectedVehicle].id);if(loadout==null)return false;
            string id=slot==EquipmentSlot.Accessory?loadout.accessories[accessoryIndex]:loadout.mechanical[(int)slot];
            if(string.IsNullOrEmpty(id))return false;
            if(slot==EquipmentSlot.Accessory)loadout.accessories[accessoryIndex]=null;
            else loadout.mechanical[(int)slot]=null;
            LastMessage="Removed "+(FindPart(id)?.displayName??id);if(persist)SaveSystem.Save(save);return true;
        }
        public static VehicleStats StatsFor(VehicleDefinition definition,SaveData save,int failedDepth=0)
        {
            ContentCatalog.EnsureLoaded(); var stats=VehicleStats.From(definition);
            stats.horsepower*=PlayerPowerMultiplier; stats.torque*=PlayerPowerMultiplier;
            if(save==null)return stats;
            if(save.equipmentVersion==0)NormalizeLoadouts(save);
            var segments=HealthRules.StockSegments(definition.maxHealth);
            var loadout=Loadout(save,definition.id);
            var seenParts=new HashSet<string>();
            for(int index=0;index<10;index++)
            {
                var p=loadout==null?null:FindPart(index<6?loadout.mechanical[index]:loadout.accessories[index-6]);
                if(!p||!save.ownedParts.Contains(p.id)||!Compatible(p,definition)||
                    (index<6&&p.equipmentSlot!=(EquipmentSlot)index)||(index>=6&&p.equipmentSlot!=EquipmentSlot.Accessory)||
                    !seenParts.Add(p.id))continue;
                segments[(int)p.healthCategory]+=HealthRules.PartHealth(p);
                stats.mass+=p.mass;
                if(p.healthCategory!=HealthCategory.Engine && (int)p.healthCategory<=failedDepth)continue;
                stats.horsepower*=p.hpMultiplier; stats.torque*=p.torqueMultiplier; stats.grip*=p.gripMultiplier;
                stats.cooling*=p.coolingMultiplier; stats.maxSpeed*=p.maxSpeedMultiplier; stats.turnSpeed*=p.turnMultiplier;
                stats.rideHeight+=p.rideHeightBonus; stats.suspensionTravel*=p.suspensionMultiplier; stats.springStiffness*=p.springMultiplier;
                stats.damping*=p.dampingMultiplier; stats.finalDrive*=p.finalDriveMultiplier;
                stats.shiftTimeMultiplier*=p.shiftTimeMultiplier;stats.terrainGripMultiplier*=p.terrainGripMultiplier;
                stats.ramDamageMultiplier*=p.ramDamageMultiplier;stats.pickupRadiusMultiplier*=p.pickupRadiusMultiplier;
                stats.collisionProtection=1-(1-stats.collisionProtection)*(1-Mathf.Clamp01(p.collisionProtection));
                stats.electronicsResistance=1-(1-stats.electronicsResistance)*(1-Mathf.Clamp01(p.electronicsResistance));
                stats.driftNitroRegen+=p.driftNitroRegen;stats.spikeDamage+=p.spikeDamage;
                if(p.specialBehavior=="light_bar")stats.lightBar=true;
                if(p.specialBehavior=="repair_foam")stats.repairFoam=true;
                if(p.changesDrivetrain)stats.drivetrain=p.drivetrain; if(p.changesDifferential)stats.differential=p.differential;
                if(p.turbocharger)stats.turbocharged=true;
                if(p.id=="swap")stats.engineSwapped=true;
                if(p.id=="revlimiter")stats.revLimiter=true;
            }
            var driver=ContentCatalog.Drivers[Mathf.Clamp(save.selectedDriver,0,ContentCatalog.Drivers.Length-1)];
            stats.horsepower*=driver.powerMultiplier; stats.torque*=driver.powerMultiplier; stats.grip*=driver.gripMultiplier;
            stats.finalDrive*=save.finalDriveTuning; stats.maxSpeed/=save.finalDriveTuning; stats.rideHeight+=save.rideHeightTuning;
            stats.mass=Mathf.Max(400,stats.mass); stats.cooling=Mathf.Clamp(stats.cooling,.25f,3);
            stats.terrainGripMultiplier=Mathf.Clamp(stats.terrainGripMultiplier,.7f,1.45f);
            stats.pickupRadiusMultiplier=Mathf.Clamp(stats.pickupRadiusMultiplier,1,1.8f);
            stats.ramDamageMultiplier=Mathf.Clamp(stats.ramDamageMultiplier,.75f,1.75f);
            stats.grip=Mathf.Clamp(stats.grip,.4f,2.2f*VehicleStats.StockGripMultiplier);
            stats.maxHealth=0;
            foreach(float amount in segments)stats.maxHealth+=amount;
            stats.healthShares=HealthRules.Shares(segments);
            if(failedDepth>0)
            {
                var whole=StatsFor(definition,save);
                var stock=StatsFor(definition,null);
                stats.horsepower=Mathf.Max(stats.horsepower,Mathf.Min(stock.horsepower,whole.horsepower));
                stats.torque=Mathf.Max(stats.torque,Mathf.Min(stock.torque,whole.torque));
                stats.grip=Mathf.Max(stats.grip,Mathf.Min(stock.grip,whole.grip));
                stats.turnSpeed=Mathf.Max(stats.turnSpeed,Mathf.Min(stock.turnSpeed,whole.turnSpeed));
                stats.maxSpeed=Mathf.Max(stats.maxSpeed,Mathf.Min(stock.maxSpeed,whole.maxSpeed));
                stats.suspensionTravel=Mathf.Max(stats.suspensionTravel,Mathf.Min(stock.suspensionTravel,whole.suspensionTravel));
                stats.damping=Mathf.Max(stats.damping,Mathf.Min(stock.damping,whole.damping));
                stats.mass=Mathf.Min(stats.mass,Mathf.Max(stock.mass,whole.mass));
            }
            return stats;
        }
        public static VehicleStats[] DamageProfiles(VehicleDefinition definition,SaveData save)
        {
            var profiles=new VehicleStats[5];
            for(int depth=0;depth<profiles.Length;depth++)profiles[depth]=StatsFor(definition,save,depth);
            return profiles;
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
            var loadout=Loadout(save,ContentCatalog.Vehicles[save.selectedVehicle].id);
            if(loadout!=null)loadout.weapon=weapon.id;
            LastMessage = weapon.displayName + " fitted to RT.";
            SaveSystem.Save(save); return true;
        }
        public static void TogglePart(VehiclePart part,SaveData save)
        {
            if(!part||save==null)return;
            if(save.equipmentVersion==0)NormalizeLoadouts(save);
            var vehicle=ContentCatalog.Vehicles[save.selectedVehicle];
            int accessory=0;
            if(part.equipmentSlot==EquipmentSlot.Accessory)
            {
                var loadout=Loadout(save,vehicle.id);
                accessory=Array.FindIndex(loadout.accessories,id=>id==part.id);
                if(accessory<0)accessory=Array.FindIndex(loadout.accessories,string.IsNullOrEmpty);
                if(accessory<0)accessory=0;
            }
            if(Installed(save,vehicle.id,part.equipmentSlot,accessory)==part)Unequip(save,part.equipmentSlot,accessory);
            else TryEquip(part,save,part.equipmentSlot,accessory);
        }
        public static bool SelectVehicle(int index,SaveData save)
        {
            ContentCatalog.EnsureLoaded();if(index<0||index>=ContentCatalog.Vehicles.Length)return false;
            var car=ContentCatalog.Vehicles[index];if(save.unlockedMission<car.unlockMission&&!save.ownedVehicles.Contains(car.id)){LastMessage="Vehicle unlocks after more campaign jobs.";return false;}
            if(!save.ownedVehicles.Contains(car.id))
            {
                if(save.money<car.cost){LastMessage="Need $"+car.cost+" to purchase this vehicle.";return false;}
                save.money-=car.cost;save.ownedVehicles.Add(car.id);
            }
            save.selectedVehicle=index;
            save.selectedWeapon=Loadout(save,car.id)?.weapon??"riveter";
            LastMessage=car.displayName+" selected."; SaveSystem.Save(save);return true;
        }
        public static void Tune(float finalDrive,float rideHeight,SaveData save)
        { save.finalDriveTuning=Mathf.Clamp(finalDrive,.85f,1.2f);save.rideHeightTuning=Mathf.Clamp(rideHeight,-.08f,.15f);SaveSystem.Save(save); }
    }
}
