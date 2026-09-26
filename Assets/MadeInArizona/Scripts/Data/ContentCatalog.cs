using System;
using UnityEngine;
namespace MadeInArizona
{
    /// <summary>Authored Resources override the original procedural content on a per-asset basis.</summary>
    public static partial class ContentCatalog
    {
        public static VehicleDefinition[] Vehicles { get; private set; }
        public static VehiclePart[] Parts { get; private set; }
        public static WeaponDefinition[] Weapons { get; private set; }
        public static MissionDefinition[] Missions { get; private set; }
        public static DriverDefinition[] Drivers { get; private set; }
        public static void EnsureLoaded()
        {
            if (Vehicles != null) return;
            BuildVehicles(); BuildParts(); BuildDrivers(); BuildWeapons(); BuildCampaign();
            OverrideResources(Vehicles, v => v.id); OverrideResources(Parts, v => v.id);
            OverrideResources(Weapons, v => v.id); OverrideResources(Missions, v => v.id); OverrideResources(Drivers, v => v.id);
        }
        static void OverrideResources<T>(T[] defaults, Func<T,string> key) where T : ScriptableObject
        {
            var authored = Resources.LoadAll<T>("Content");
            foreach (var asset in authored)
                for (int i = 0; i < defaults.Length; i++) if (key(defaults[i]) == key(asset)) { defaults[i] = asset; break; }
        }
        static T Create<T>(string name) where T : ScriptableObject { var asset = ScriptableObject.CreateInstance<T>(); asset.name = name; return asset; }
        static VehicleDefinition Car(int i, string id, string name, string desc, float mass, float hp, float torque, float speed, float grip, float turn, float hpMax, Drivetrain drive, Differential diff, Color color, int cost, int unlock, float travel, float ride, float wheelbase, float track, float finalDrive)
        {
            var v = Create<VehicleDefinition>(id); v.contentOrder = i; v.id = id; v.displayName = name; v.description = desc;
            v.mass = mass; v.horsepower = hp; v.torque = torque; v.maxSpeed = speed; v.grip = grip; v.turnSpeed = turn; v.maxHealth = hpMax;
            v.drivetrain = drive; v.differential = diff; v.color = color; v.cost = cost; v.unlockMission = unlock;
            v.suspensionTravel = travel; v.rideHeight = ride; v.wheelbase = wheelbase; v.trackWidth = track; v.finalDrive = finalDrive;
            v.springStiffness = mass * (mass > 1700 ? 44f : 38f); v.damping = mass * 4.5f;
            return v;
        }
        static void BuildVehicles()
        {
            Vehicles = new[] {
                Car(0,"thimble","1991 Thimble Sprint","Three cylinders, 760 kilos, one outstanding invoice. Low mass makes every honest horsepower count.",760,58,85,102,1.08f,112,240,Drivetrain.FWD,Differential.Open,new Color(.15f,.78f,.68f),0,0,.23f,.56f,2.23f,1.36f,4.1f),
                Car(1,"juniper","1988 Juniper JX","A tiny 4x4 with a huge sense of duty. Narrow track rewards smooth inputs; dirt is its natural habitat.",980,72,110,88,1.14f,94,310,Drivetrain.AWD,Differential.LimitedSlip,new Color(.9f,.58f,.22f),0,0,.42f,.82f,2.08f,1.34f,4.56f),
                Car(2,"sunskip","Sunskip Trophy 900","Long travel, wide stance, enough speed to discover a wash the hard way. Heavy tires cost acceleration.",1900,420,570,148,1.1f,72,450,Drivetrain.RWD,Differential.LimitedSlip,new Color(.98f,.25f,.14f),2200,3,.78f,.95f,3.15f,2.15f,4.88f),
                Car(3,"foreclosure","Foreclosure 6.8D","Its torque curve is a mesa. Its turning circle is a county. Includes a payment schedule longer than either.",3000,290,920,99,.94f,48,640,Drivetrain.RWD,Differential.Locked,new Color(.25f,.29f,.34f),2800,5,.32f,.82f,3.55f,1.94f,3.73f),
                Car(4,"sidehustle","Side Hustle Tradesman","Former AC van. The ladders are structural. Weapon room and armor at the expense of everything involving a corner.",2250,195,320,105,.92f,59,520,Drivetrain.RWD,Differential.Open,new Color(.86f,.82f,.67f),1800,4,.3f,.68f,3.3f,1.84f,3.9f),
                Car(5,"perennial","Perennial Half-Ton","The odometer stopped when the mechanic graduated. Forgiving weight distribution and a frame made of stubbornness.",1770,168,285,109,1.04f,76,540,Drivetrain.RWD,Differential.LimitedSlip,new Color(.42f,.62f,.48f),1400,2,.4f,.73f,2.95f,1.76f,4.1f),
                Car(6,"skitter","Skitter Sport 1000","A roll cage surrounding an argument. Extraordinary response, almost no protection from the consequences.",640,112,103,126,1.15f,126,180,Drivetrain.AWD,Differential.LimitedSlip,new Color(.68f,.34f,.95f),2600,7,.61f,.78f,2.2f,1.7f,5.1f),
                Car(7,"vincent","VINcent, The Uninsurable","Four donor cars; three conflicting wheelbases; a transfer case with judicial immunity. Built from the campaign's worst ideas.",2550,520,780,134,1.03f,65,790,Drivetrain.AWD,Differential.Locked,new Color(.72f,.26f,.12f),5400,11,.57f,1.03f,3.45f,2.03f,4.56f)
            };
        }
        static VehiclePart Part(string id, string name, string category, string desc, int cost, int unlock, float mass = 0, float hp = 1, float grip = 1, float cooling = 1, float health = 0)
        {
            var p = Create<VehiclePart>(id); p.id = id; p.displayName = name; p.category = category; p.description = desc; p.cost = cost;
            p.unlockMission = unlock; p.mass = mass; p.hpMultiplier = hp; p.gripMultiplier = grip; p.coolingMultiplier = cooling; p.healthBonus = health;
            return p;
        }
        static void BuildParts()
        {
            Parts = new[] {
                Part("intake","Sealed desert airbox","Intake","Keeps the filter out of the fan wash. +6% power; +3 kg. A real seal beats a chrome hot-air cone.",180,0,3,1.06f),
                Part("headers","Equal-length headers","Exhaust","Scavenging improves power 9%; nitro recovery -5%. +5 kg.",240,0,5,1.09f,1,.95f),
                Part("cam","Mild tow cam","Camshaft","More usable torque, 8% power and 12% torque. Lumpy idle remains a personality flaw. +2 kg.",350,1,2,1.08f),
                Part("turbo","Wastegated three-pot turbo","Induction","+48% power and 32% torque; nitro recovery -23%, +34 kg. Thimble only.",760,2,34,1.48f,1,.77f),
                Part("blower","Roots blower with honest belt","Induction","+32% power, +38% torque; +68 kg and 18% less nitro recovery. Larger RWD vehicles only.",950,4,68,1.32f,1,.82f),
                Part("intercooler","Front-mount intercooler","Charge cooling","+7% power, +20% nitro recovery, +18 kg. Useful even when the marketing sticker is missing.",460,2,18,1.07f,1,1.2f),
                Part("radiator","Three-row aluminum radiator","Cooling","+42% nitro recovery, +12 kg. Keeps the boost plumbing ready for another sprint.",260,0,12,1,1,1.42f),
                Part("ecu","Wideband ECU and fuel pump","Fuel & ECU","A calibrated map adds 16% power and 12% torque. +4 kg; nitro recovery -6%.",540,3,4,1.16f,1,.94f),
                Part("swap","1.8L salvage-yard engine swap","Engine","Thimble or Juniper: +70% power, +55% torque; +96 kg, nitro recovery -16%. Mounts included.",1700,6,96,1.7f,1,.84f),
                Part("lsd","Helical limited-slip carrier","Differential","Shares useful torque across the axle. +7% grip, +8 kg. Retains civilized corner exits.",380,0,8,1,1.07f),
                Part("locker","Selectable trail locker","Differential","AWD rigs: locked axles, +15% grip, -12% turn response and +16 kg. The pavement will notice.",440,1,16,1,1.15f),
                Part("welded","Welded spare differential","Differential","+10% grip, -18% turn response. Costs $90; the tire bill arrives separately.",90,0,3,1,1.1f),
                Part("crawler","4.88 crawler final drive","Final drive","+22% final drive improves acceleration; top speed -15%. +6 kg. Gearing is a lever, not free horsepower.",330,1,6),
                Part("awd","Floorpan surgery AWD kit","Drivetrain","Thimble only. AWD and +12% grip, +145 kg, -5% top speed. The original carpet will never fit again.",1350,5,145,1,1.12f),
                Part("rally","Progressive rally dampers","Suspension","+35% travel, +12% grip, +0.10 m height, +22 kg. Composed over washboard without a skyscraper lift.",410,0,22,1,1.12f),
                Part("longtravel","Long-travel desert package","Suspension","Juniper, truck and UTV: +65% travel, +0.18 m height, +9% grip, -8% turning and +65 kg.",820,4,65,1,1.09f),
                Part("street","Summer street tires","Tires","+17% base grip and +5% speed, -5 kg. Road-biased construction lowers suspension compliance by 12%.",270,0,-5,1,1.17f),
                Part("allterrain","All-terrain reinforced tires","Tires","+11% grip, +8% suspension compliance, +25 kg. Less dramatic than a tire named after a crime.",310,0,25,1,1.11f),
                Part("mud","Mud-terrain beadlock set","Tires","+18% grip and +12% travel; -9% speed, +51 kg. Heavy rubber is still rotating mass.",500,2,51,1,1.18f),
                Part("used","Four almost matching used tires","Tires","Only $35. -12% grip and -7% damping, -8 kg. Customer states the wobble is seasonal.",35,0,-8,1,.88f),
                Part("cage","Triangulated cage and skid plates","Armor","+160 chassis health, +95 kg. Protects occupants and underbody; slows every acceleration.",560,1,95,1,1,1,160),
                Part("bumpers","Salvaged steel bumpers","Armor","+95 chassis health for +72 kg. Includes an extremely confident tow rating.",220,0,72,1,1,1,95),
                Part("boards","Recovery boards and tool roll","Utility","+45 health, +8% nitro recovery, +24 kg. Tools are less cinematic than fire.",290,0,24,1,1,1.08f,45),
                Part("nitrous","Purge-first nitrous kit","Induction","+25% power, +12% torque, -20% nitro recovery, +20 kg. Replaces other forced induction.",690,4,20,1.25f,1,.8f)
            };
            for (int i = 0; i < Parts.Length; i++) Parts[i].contentOrder = i;
            Find("cam").torqueMultiplier = 1.12f;
            Find("turbo").torqueMultiplier = 1.32f; Find("turbo").compatibleVehicles = new[] { "thimble" };
            Find("blower").torqueMultiplier = 1.38f; Find("blower").compatibleVehicles = new[] { "foreclosure", "perennial", "sidehustle", "sunskip" };
            Find("ecu").torqueMultiplier = 1.12f; Find("swap").torqueMultiplier = 1.55f; Find("swap").compatibleVehicles = new[] { "thimble", "juniper" };
            foreach (string id in new[] { "lsd", "locker", "welded" }) Find(id).changesDifferential = true;
            Find("lsd").differential = Differential.LimitedSlip;
            Find("locker").differential = Differential.Locked; Find("locker").turnMultiplier = .88f; Find("locker").compatibleVehicles = new[] { "juniper", "skitter", "vincent" };
            Find("welded").differential = Differential.Locked; Find("welded").turnMultiplier = .82f;
            Find("crawler").finalDriveMultiplier = 1.22f; Find("crawler").maxSpeedMultiplier = .85f;
            Find("awd").changesDrivetrain = true; Find("awd").drivetrain = Drivetrain.AWD; Find("awd").compatibleVehicles = new[] { "thimble" }; Find("awd").maxSpeedMultiplier = .95f;
            Find("rally").suspensionMultiplier = 1.35f; Find("rally").dampingMultiplier = 1.15f; Find("rally").rideHeightBonus = .1f;
            Find("longtravel").suspensionMultiplier = 1.65f; Find("longtravel").rideHeightBonus = .18f; Find("longtravel").turnMultiplier = .92f; Find("longtravel").compatibleVehicles = new[] { "juniper", "sunskip", "perennial", "skitter", "vincent" };
            Find("street").maxSpeedMultiplier = 1.05f; Find("street").suspensionMultiplier = .88f;
            Find("allterrain").suspensionMultiplier = 1.08f; Find("mud").maxSpeedMultiplier = .91f; Find("mud").suspensionMultiplier = 1.12f;
            Find("used").dampingMultiplier = .93f; Find("nitrous").torqueMultiplier = 1.12f;
        }
        static VehiclePart Find(string id) { return Array.Find(Parts, p => p.id == id); }
        static void BuildDrivers()
        {
            Drivers = new[] {
                Driver(0,"stallion","Stallion","The new runner at 117° Auto Care. Thirty-something, immaculate Afro, sunglasses in all weather. He speaks rarely and solves mechanical problems with decisive engineering.","Field mechanic: repairs restore 25% more; power +3%.","What needs fixing?",new Color(.18f,.76f,.7f),1.25f,1.03f,1)
            };
        }
        static DriverDefinition Driver(int i,string id,string name,string bio,string perk,string line,Color color,float repair,float power,float grip)
        { var d = Create<DriverDefinition>(id); d.contentOrder=i; d.id=id; d.displayName=name; d.biography=bio; d.perk=perk; d.line=line; d.color=color; d.repairMultiplier=repair; d.powerMultiplier=power; d.gripMultiplier=grip; return d; }
        static void BuildWeapons()
        {
            Weapons = new[] {
                Weapon(0,"riveter","Belt-fed Riveter","Reliable mid-range automatic fire. Garage weapon; unlimited ammunition.",12,11,90,0,new Color(1,.79f,.2f)),
                Weapon(1,"invoice","Past-Due Rocket","A rare high-explosive notice. Huge blast, only a few shots.",110,.9f,49,8,new Color(1,.35f,.08f)),
                Weapon(2,"sweeper","Shop-floor Sweeper","Garage shotgun. Eight close-range pellets; clear a path through a crowd.",10,1.7f,96,0,new Color(.3f,1,1)),
                Weapon(3,"carbine","Surveyor Carbine","Garage precision rifle. Accurate sustained fire at medium range.",24,4.2f,135,0,new Color(.85f,1,.55f)),
                Weapon(4,"grenade","Mailbox Grenadier","Arcing demolition rounds for clustered cars and barricades.",195,1.3f,43,10,new Color(1,.65f,.18f)),
                Weapon(5,"mortar","HOA Mortar","A slow long-range shell with a very rude landing.",145,.55f,42,10,new Color(1,.24f,.18f)),
                Weapon(6,"mines","Lien Mines","Drop charges behind the car; punish pursuers and narrow roads.",105,1.2f,0,6,new Color(1,.48f,.1f)),
                Weapon(7,"minigun","Circular Saw Minigun","A short-range storm of scrap. Chase a target and hold the line.",8,20,105,0,new Color(1,.92f,.32f)),
                Weapon(8,"sniper","Long Receipt","An uncommon accurate precision shot for distant weak points.",155,.65f,210,0,new Color(.45f,.95f,1)),
                Weapon(9,"cluster","Tax Audit","Uncommon cluster launcher. The paperwork is explosive and limited.",75,.7f,54,5,new Color(1,.18f,.65f)),
                Weapon(10,"boomstick","Double-Owed Boomstick","Field shotgun. Twelve heavy pellets; devastating at bumper distance.",15,.95f,98,0,new Color(.75f,.42f,1)),
                Weapon(11,"shredder","Receipt Shredder","Garage weapon. Three light blades fan out; track close targets to land the full volley.",7,5,88,0,new Color(.95f,.84f,.48f)),
                Weapon(12,"pothole","Pothole Popper","Garage weapon. Lob a small charge over obstacles; slow reload and modest blast.",32,.85f,38,2.4f,new Color(.98f,.52f,.22f))
            };
        }
        static WeaponDefinition Weapon(int i,string id,string name,string desc,float damage,float rate,float speed,float radius,Color color)
        { var w=Create<WeaponDefinition>(id); w.contentOrder=i; w.id=id; w.displayName=name; w.description=desc; w.damage=damage; w.fireRate=rate; w.speed=speed; w.blastRadius=radius; w.projectileColor=color; return w; }
    }
}
