using System;
using System.Collections.Generic;
using UnityEngine;

namespace MadeInArizona.Editor
{
    public static class EquipmentValidator
    {
        public static void Validate()
        {
            ContentCatalog.EnsureLoaded();
            var ids=new HashSet<string>();
            foreach(var part in ContentCatalog.Parts)
            {
                if(!part||string.IsNullOrEmpty(part.id)||!ids.Add(part.id)||!Enum.IsDefined(typeof(EquipmentSlot),part.equipmentSlot)||
                    !Enum.IsDefined(typeof(PartCompatibility),part.compatibilityClass))throw new Exception("Invalid equipment identity: "+(part?part.id:"null"));
                if(part.compatibilityClass==PartCompatibility.CarSpecific &&
                    (part.compatibleVehicles==null||part.compatibleVehicles.Length!=1))throw new Exception("Invalid car-specific part: "+part.id);
                if(part.compatibilityClass==PartCompatibility.Chassis &&
                    (part.compatibleChassis==null||part.compatibleChassis.Length==0))throw new Exception("Invalid chassis part: "+part.id);
            }
            foreach(var car in ContentCatalog.Vehicles)
            {
                var save=new SaveData();save.Normalize();
                var loadout=GarageManager.Loadout(save,car.id);
                if(loadout==null||loadout.mechanical.Length!=6||loadout.accessories.Length!=4||string.IsNullOrEmpty(loadout.weapon))
                    throw new Exception("Incomplete loadout: "+car.id);
                var counts=new int[6];int accessories=0,signatures=0;
                foreach(var part in ContentCatalog.Parts)
                {
                    if(!GarageManager.Compatible(part,car))continue;
                    if(part.equipmentSlot==EquipmentSlot.Accessory)accessories++;
                    else counts[(int)part.equipmentSlot]++;
                    if(part.compatibilityClass==PartCompatibility.CarSpecific)signatures++;
                }
                for(int i=0;i<6;i++)if(counts[i]<4)throw new Exception(car.id+" has only "+counts[i]+" "+((EquipmentSlot)i)+" choices");
                if(accessories<4||signatures<1)throw new Exception(car.id+" lacks accessories or signature part");
                int weapons=0;foreach(var id in WeaponRules.GarageIds)if(WeaponRules.Find(id))weapons++;
                if(weapons<4)throw new Exception(car.id+" lacks garage weapons");
                Debug.Log("MIA_EQUIPMENT_COMPAT "+car.id+": "+string.Join("/",counts)+"; accessories="+accessories+"; weapons="+weapons);
            }
            var selected=ContentCatalog.Vehicles[0];var stock=new SaveData();stock.Normalize();
            var baseline=GarageManager.StatsFor(selected,stock);
            var candidate=Array.Find(ContentCatalog.Parts,p=>p.id=="close_ratio");stock.ownedParts.Add(candidate.id);
            if(!GarageManager.TryEquip(candidate,stock,EquipmentSlot.Transmission,persist:false))throw new Exception("Compatible part cannot equip");
            float geared=GarageManager.StatsFor(selected,stock).finalDrive;
            if(geared<=baseline.finalDrive||!GarageManager.TryEquip(candidate,stock,EquipmentSlot.Transmission,persist:false)||
                !Mathf.Approximately(geared,GarageManager.StatsFor(selected,stock).finalDrive))throw new Exception("Equipment stat accumulation");
            if(!GarageManager.Unequip(stock,EquipmentSlot.Transmission,persist:false)||
                !Mathf.Approximately(baseline.finalDrive,GarageManager.StatsFor(selected,stock).finalDrive))throw new Exception("Factory baseline not restored");
            var foreign=Array.Find(ContentCatalog.Parts,p=>p.id=="ae867_slide");stock.ownedParts.Add(foreign.id);
            if(GarageManager.TryEquip(foreign,stock,EquipmentSlot.Drivetrain,persist:false))throw new Exception("Incompatible part installed");
            var legacy=new SaveData();legacy.ownedParts.Add("intake");legacy.ownedParts.Add("cam");
            legacy.installedParts.Add("intake");legacy.installedParts.Add("cam");legacy.Normalize();
            if(GarageManager.Installed(legacy,selected.id,EquipmentSlot.Engine)?.id!="cam"||
                !legacy.ownedParts.Contains("intake"))throw new Exception("Legacy loadout migration lost a purchased part");
            Debug.Log("MIA_EQUIPMENT_VALIDATION_PASS: slots, counts, compatibility, stat recomputation, and legacy migration.");
        }
    }
}
