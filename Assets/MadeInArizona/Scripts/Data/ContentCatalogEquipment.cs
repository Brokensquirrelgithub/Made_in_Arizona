using System;
using System.Collections.Generic;
using UnityEngine;

namespace MadeInArizona
{
    public static partial class ContentCatalog
    {
        static VehiclePart Upgrade(string id,string name,EquipmentSlot slot,string description,int cost,int unlock=0)
        {
            var p=Part(id,name,slot.ToString(),description,cost,unlock);
            p.equipmentSlot=slot;
            p.healthCategory=slot==EquipmentSlot.Engine?HealthCategory.Engine:
                slot==EquipmentSlot.Transmission||slot==EquipmentSlot.Drivetrain?HealthCategory.Drivetrain:
                slot==EquipmentSlot.Tires||slot==EquipmentSlot.Suspension?HealthCategory.Suspension:
                slot==EquipmentSlot.Body?HealthCategory.Armor:HealthCategory.Equipment;
            p.partDurability=slot==EquipmentSlot.Body?65:slot==EquipmentSlot.Accessory?30:40;
            return p;
        }
        static VehiclePart Signature(string id,string name,EquipmentSlot slot,string vehicle,string description,int cost)
        {
            var p=Upgrade(id,name,slot,description,cost,2);
            p.compatibilityClass=PartCompatibility.CarSpecific;p.compatibleVehicles=new[]{vehicle};return p;
        }
        static VehiclePart Chassis(string id,string name,EquipmentSlot slot,string tag,string description,int cost)
        {
            var p=Upgrade(id,name,slot,description,cost,1);
            p.compatibilityClass=PartCompatibility.Chassis;p.compatibleChassis=new[]{tag};return p;
        }
        static void ConfigureEquipment()
        {
            // Authored roster assets predate chassis metadata. Keep the classification in one place;
            // future vehicle assets can author chassisTags directly.
            foreach(var car in Vehicles)
            {
                if(car.chassisTags!=null&&car.chassisTags.Length>0)continue;
                string tag;
                switch(car.id)
                {
                    case "shift":case "monsoon":case "citizen":case "scarface":tag="compact";break;
                    case "ronin":case "juniper":tag="trail";break;
                    case "sunskip":case "foreclosure":case "perennial":case "copperado":tag="truck";break;
                    case "sidehustle":tag="van";break;
                    case "skitter":tag="buggy";break;
                    case "vincent":tag="heavy";break;
                    case "dustback":tag="wagon";break;
                    case "sonora":case "monument":case "executive":tag="sedan";break;
                    case "heat":case "espresso":tag="roadster";break;
                    case "ae867":case "contender":tag="coupe";break;
                    case "roam4":tag="crossover";break;
                    default:tag="passenger";break;
                }
                car.chassisTags=new[]{tag};
            }
            foreach(var existing in Parts)
            {
                switch(existing.id)
                {
                    case "crawler":existing.equipmentSlot=EquipmentSlot.Transmission;break;
                    case "lsd":case "locker":case "welded":case "awd":existing.equipmentSlot=EquipmentSlot.Drivetrain;break;
                    case "street":case "allterrain":case "mud":case "used":case "wheels":case "lugs":existing.equipmentSlot=EquipmentSlot.Tires;break;
                    case "rally":case "longtravel":case "alignment":case "brakes":case "shocks":existing.equipmentSlot=EquipmentSlot.Suspension;break;
                    case "cage":case "bumpers":existing.equipmentSlot=EquipmentSlot.Body;break;
                    case "boards":case "seat":existing.equipmentSlot=EquipmentSlot.Accessory;break;
                    default:existing.equipmentSlot=EquipmentSlot.Engine;break;
                }
                int fits=existing.compatibleVehicles==null?0:existing.compatibleVehicles.Length;
                existing.compatibilityClass=fits==1?PartCompatibility.CarSpecific:
                    fits>1?PartCompatibility.Chassis:PartCompatibility.Universal;
                if(fits>1)
                {
                    var tags=new HashSet<string>();
                    foreach(string id in existing.compatibleVehicles)
                    {
                        var vehicle=Array.Find(Vehicles,v=>v.id==id);
                        if(vehicle!=null&&vehicle.chassisTags!=null)foreach(string tag in vehicle.chassisTags)tags.Add(tag);
                    }
                    existing.compatibleChassis=new List<string>(tags).ToArray();
                }
                // The ECU is an engine tune even though the old equipment layer carried its health.
                if(existing.id=="ecu")existing.healthCategory=HealthCategory.Engine;
                if(existing.equipmentSlot==EquipmentSlot.Accessory)existing.healthCategory=HealthCategory.Equipment;
            }
            var added=new List<VehiclePart>();
            VehiclePart p;
            p=Upgrade("close_ratio","Close-ratio gearbox",EquipmentSlot.Transmission,"Short gearing and eager acceleration; lower cruise speed.",450);p.finalDriveMultiplier=1.13f;p.maxSpeedMultiplier=.93f;added.Add(p);
            p=Upgrade("long_ratio","Long-ratio gearbox",EquipmentSlot.Transmission,"Long legs at the cost of launch response.",470);p.finalDriveMultiplier=.90f;p.maxSpeedMultiplier=1.10f;added.Add(p);
            p=Upgrade("quick_shift","Quick-shift transmission",EquipmentSlot.Transmission,"Quicker torque recovery between gears; more weight and heat.",590);p.shiftTimeMultiplier=.72f;p.mass=14;p.coolingMultiplier=.95f;added.Add(p);
            p=Upgrade("reinforced_gearbox","Reinforced gearbox",EquipmentSlot.Transmission,"More drive-layer durability, but its steel case adds weight.",520);p.mass=34;p.partDurability=100;added.Add(p);
            p=Upgrade("street_diff","Street traction differential",EquipmentSlot.Drivetrain,"Mild limited slip for cleaner exits, with some steering drag.",330);p.changesDifferential=true;p.differential=Differential.LimitedSlip;p.gripMultiplier=1.04f;p.turnMultiplier=.97f;added.Add(p);
            p=Upgrade("drift_diff","Drift-bias differential",EquipmentSlot.Drivetrain,"Locks the driven wheels for longer slides; less straight-line bite.",420);p.changesDifferential=true;p.differential=Differential.Locked;p.gripMultiplier=.94f;p.turnMultiplier=1.08f;added.Add(p);
            p=Upgrade("lightweight_body","Lightweight body panels",EquipmentSlot.Body,"Less mass and better response, with reduced armor protection.",560);p.mass=-90;p.turnMultiplier=1.06f;p.partDurability=0;added.Add(p);
            p=Upgrade("ram_frame","Ramming frame",EquipmentSlot.Body,"More contact damage and armor, offset by significant mass.",710);p.mass=75;p.ramDamageMultiplier=1.22f;p.partDurability=95;added.Add(p);
            p=Chassis("compact_coilovers","Compact sport coilovers",EquipmentSlot.Suspension,"compact","Fast steering on pavement, reduced travel on rough roads.",530);p.turnMultiplier=1.12f;p.suspensionMultiplier=.86f;added.Add(p);
            p=Chassis("truck_armor","Truck frame armor",EquipmentSlot.Body,"truck","Deep armor reserve with a slow, heavy nose.",860);p.mass=125;p.turnMultiplier=.94f;p.partDurability=110;added.Add(p);
            p=Chassis("trail_diff","Trail traction differential",EquipmentSlot.Drivetrain,"trail","Low-speed traction and slow paved corner exits.",490);p.changesDifferential=true;p.differential=Differential.Locked;p.gripMultiplier=1.10f;p.turnMultiplier=.91f;added.Add(p);
            p=Chassis("roadster_aero","Roadster aero shell",EquipmentSlot.Body,"roadster","Less drag at speed, less structural protection.",610);p.maxSpeedMultiplier=1.07f;p.mass=-25;p.partDurability=0;added.Add(p);
            p=Chassis("truck_beadlocks","Truck-rated beadlocks",EquipmentSlot.Tires,"truck","Sure-footed on loose ground, heavy at each corner.",560);p.terrainGripMultiplier=1.18f;p.mass=48;p.maxSpeedMultiplier=.96f;added.Add(p);
            p=Signature("ronin_crawl","Ronin crawler transfer",EquipmentSlot.Drivetrain,"ronin","Four-wheel crawl traction; heavy and slower on road.",1350);p.changesDrivetrain=true;p.drivetrain=Drivetrain.AWD;p.mass=120;p.maxSpeedMultiplier=.91f;p.terrainGripMultiplier=1.18f;added.Add(p);
            p=Signature("juniper_trail","Juniper trail springs",EquipmentSlot.Suspension,"juniper","More washboard travel with softer paved response.",680);p.suspensionMultiplier=1.42f;p.turnMultiplier=.93f;added.Add(p);
            p=Signature("sunskip_desert","Sunskip desert gearbox",EquipmentSlot.Transmission,"sunskip","Short lower gearing; sacrifices long straight speed.",720);p.finalDriveMultiplier=1.17f;p.maxSpeedMultiplier=.91f;added.Add(p);
            p=Signature("foreclosure_tow","Foreclosure tow tune",EquipmentSlot.Engine,"foreclosure","More low-end torque and cooling, less high-speed power.",790);p.torqueMultiplier=1.15f;p.hpMultiplier=.94f;p.coolingMultiplier=1.12f;added.Add(p);
            p=Signature("sidehustle_cage","Side Hustle cargo cage",EquipmentSlot.Body,"sidehustle","Cargo frame takes hits but makes the van even heavier.",640);p.mass=88;p.partDurability=100;added.Add(p);
            p=Signature("perennial_leaf","Perennial load springs",EquipmentSlot.Suspension,"perennial","Settles heavy loads; slower direction changes.",620);p.springMultiplier=1.18f;p.turnMultiplier=.95f;added.Add(p);
            p=Signature("skitter_sprint","Skitter sprint final drive",EquipmentSlot.Transmission,"skitter","Explosive launch with a shorter top end.",650);p.finalDriveMultiplier=1.16f;p.maxSpeedMultiplier=.91f;added.Add(p);
            p=Signature("vincent_plating","VINcent donor plating",EquipmentSlot.Body,"vincent","Abundant scrap armor at an obvious weight cost.",880);p.mass=135;p.partDurability=115;added.Add(p);
            p=Signature("shift_feather","Shift featherweight shell",EquipmentSlot.Body,"shift","Light body and nimble steering, little added protection.",480);p.mass=-55;p.turnMultiplier=1.08f;p.partDurability=0;added.Add(p);
            p=Signature("monsoon_turbo","Monsoon balanced turbo",EquipmentSlot.Engine,"monsoon","More top-end power, less low-end torque and more heat.",720);p.hpMultiplier=1.22f;p.torqueMultiplier=.93f;p.coolingMultiplier=.85f;p.turbocharger=true;added.Add(p);
            p=Signature("sonora_comfort","Sonora comfort dampers",EquipmentSlot.Suspension,"sonora","More composed rough-road ride, softer steering.",430);p.dampingMultiplier=1.28f;p.turnMultiplier=.94f;added.Add(p);
            p=Signature("dustback_gravel","Dustback gravel tires",EquipmentSlot.Tires,"dustback","All-wheel gravel traction, less paved speed.",570);p.terrainGripMultiplier=1.20f;p.maxSpeedMultiplier=.96f;added.Add(p);
            p=Signature("monument_cruise","Monument highway gearbox",EquipmentSlot.Transmission,"monument","Relaxed highway gearing, slower starts.",630);p.finalDriveMultiplier=.88f;p.maxSpeedMultiplier=1.09f;added.Add(p);
            p=Signature("executive_armor","Executive security shell",EquipmentSlot.Body,"executive","Heavy discreet armor, lazier turn-in.",820);p.mass=100;p.turnMultiplier=.95f;p.partDurability=100;added.Add(p);
            p=Signature("heat_midship","Heat midship tune",EquipmentSlot.Engine,"heat","Free-revving top-end power, weaker low-speed pull.",640);p.hpMultiplier=1.19f;p.torqueMultiplier=.91f;added.Add(p);
            p=Signature("citizen_turn","Citizen response kit",EquipmentSlot.Suspension,"citizen","Sharper turn-in, less washboard travel.",560);p.turnMultiplier=1.13f;p.suspensionMultiplier=.87f;added.Add(p);
            p=Signature("roam4_trail","Roam4 trail differential",EquipmentSlot.Drivetrain,"roam4","Off-road traction with slower paved rotation.",590);p.terrainGripMultiplier=1.15f;p.turnMultiplier=.94f;added.Add(p);
            p=Signature("ae867_slide","AE 867 drift carrier",EquipmentSlot.Drivetrain,"ae867","Easy sustained slides at the cost of straight grip.",590);p.changesDifferential=true;p.differential=Differential.Locked;p.turnMultiplier=1.12f;p.gripMultiplier=.92f;added.Add(p);
            p=Signature("espresso_spool","Espresso spool tune",EquipmentSlot.Engine,"espresso","A punchier turbo exit that runs hotter.",670);p.hpMultiplier=1.19f;p.coolingMultiplier=.82f;p.turbocharger=true;added.Add(p);
            p=Signature("copperado_ram","Copperado ram frame",EquipmentSlot.Body,"copperado","Heavy reinforced nose and harder impacts.",780);p.mass=105;p.ramDamageMultiplier=1.35f;p.partDurability=100;added.Add(p);
            p=Signature("contender_torque","Contender torque cam",EquipmentSlot.Engine,"contender","More bottom-end shove, less top-end power.",720);p.torqueMultiplier=1.17f;p.hpMultiplier=.95f;added.Add(p);
            p=Signature("scarface_scramble","Scarface scramble tires",EquipmentSlot.Tires,"scarface","Bare-front desert traction and slower highway running.",510);p.terrainGripMultiplier=1.19f;p.maxSpeedMultiplier=.93f;added.Add(p);
            p=Upgrade("light_bar","Light Bar",EquipmentSlot.Accessory,"Visible roof lighting that automatically follows the car lights.",350);p.specialBehavior="light_bar";p.mass=8;added.Add(p);
            p=Upgrade("tire_spikes","Tire Spikes",EquipmentSlot.Accessory,"Wheel-mounted spikes hurt hostile cars on contact.",470);p.specialBehavior="tire_spikes";p.spikeDamage=18;p.mass=16;added.Add(p);
            p=Upgrade("drift_n2o","Drift N2O Regenerator",EquipmentSlot.Accessory,"Sustained moving slides refill the nitrous tank.",510);p.specialBehavior="drift_n2o";p.driftNitroRegen=.055f;added.Add(p);
            p=Upgrade("magnetic_collector","Magnetic Collector",EquipmentSlot.Accessory,"Collect pickups from a little farther away.",280);p.pickupRadiusMultiplier=1.35f;added.Add(p);
            p=Upgrade("repair_foam","Emergency Repair Foam",EquipmentSlot.Accessory,"Automatically repairs non-engine damage once per mission at critical health.",540);p.specialBehavior="repair_foam";added.Add(p);
            p=Upgrade("reinforced_wiring","Reinforced Wiring",EquipmentSlot.Accessory,"Protects the equipment and electronics damage layer.",330);p.electronicsResistance=.22f;p.mass=6;added.Add(p);
            p=Upgrade("collision_padding","Collision Padding",EquipmentSlot.Accessory,"Reduces collision damage without adding body armor.",390);p.collisionProtection=.20f;p.mass=12;added.Add(p);
            p=Upgrade("cooling_fans","Auxiliary Cooling Fans",EquipmentSlot.Accessory,"Draw heat away from the engine and improve cooling.",430);p.coolingMultiplier=1.20f;p.mass=9;added.Add(p);
            int legacyCount=Parts.Length,order=legacyCount;foreach(var part in added)part.contentOrder=order++;
            var merged=new VehiclePart[legacyCount+added.Count];Array.Copy(Parts,merged,legacyCount);added.CopyTo(merged,legacyCount);Parts=merged;
            // Once Prepare has authored the new ScriptableObjects, use those assets in play builds.
            // Legacy assets still receive their migration metadata above without rewriting them.
            foreach(var authored in Resources.LoadAll<VehiclePart>("Content"))
                for(int i=legacyCount;i<Parts.Length;i++)if(Parts[i].id==authored.id){Parts[i]=authored;break;}
        }
    }
}
