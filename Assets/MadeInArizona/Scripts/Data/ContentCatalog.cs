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
            BorrowWeaponSounds();
        }
        /// <summary>Weapons without their own recordings reuse the imported clips of a similar weapon.</summary>
        static void BorrowWeaponSounds()
        {
            foreach (var weapon in Weapons)
            {
                if (!weapon || (weapon.fireSounds != null && weapon.fireSounds.Length > 0)) continue;
                var donor = Array.Find(Weapons, w => w && w.id == WeaponRules.SoundDonor(weapon.id));
                if (donor && donor.fireSounds != null) weapon.fireSounds = donor.fireSounds;
            }
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
        /// <summary>#4d8a78, the factory green of the Geo Metro the Thimble Sprint is based on.</summary>
        public static readonly Color GeoMetroGreen = new Color(0x4d / 255f, 0x8a / 255f, 0x78 / 255f);
        static void BuildVehicles()
        {
            Vehicles = new[] {
                Car(0,"thimble","1991 Thimble Sprint","Three cylinders, 760 kilos, one outstanding invoice. Low mass makes every honest horsepower count.",760,58,85,102,1.08f,112,240,Drivetrain.FWD,Differential.Open,GeoMetroGreen,0,0,.23f,.56f,2.23f,1.36f,4.1f),
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
                Part("headers","Equal-length headers","Exhaust","Scavenging improves power 9% at the cost of 3% low-end torque. +5 kg.",240,0,5,1.09f),
                Part("cam","Mild tow cam","Camshaft","More usable torque, 8% power and 12% torque. Lumpy idle remains a personality flaw. +2 kg.",350,1,2,1.08f),
                Part("turbo","Wastegated three-pot turbo","Induction","+48% power and 32% torque; -25 structure from the hot side of the plumbing, +34 kg. Thimble only.",760,2,34,1.48f,1,1,-25),
                Part("blower","Roots blower with honest belt","Induction","+32% power, +38% torque; +68 kg and -4% top speed while the belt takes its cut. Larger RWD vehicles only.",950,4,68,1.32f),
                Part("intercooler","Front-mount intercooler","Charge cooling","+7% power, +5% torque, +18 kg. Useful even when the marketing sticker is missing.",460,2,18,1.07f),
                Part("radiator","Three-row aluminum radiator","Cooling","+5% power and +25 structure: the engine stays out of limp mode on a hot day. +12 kg.",260,0,12,1.05f,1,1,25),
                Part("ecu","Wideband ECU and fuel pump","Fuel & ECU","A calibrated map adds 16% power and 12% torque. +4 kg; -15 structure from the aggressive timing.",540,3,4,1.16f,1,1,-15),
                Part("swap","1.8L salvage-yard engine swap","Engine","Thimble or Juniper: +70% power, +55% torque; +96 kg and -6% turn response from the heavier nose. Mounts included.",1700,6,96,1.7f),
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
                Part("boards","Recovery boards and tool roll","Utility","+45 health, +3% grip, +24 kg. Tools are less cinematic than fire.",290,0,24,1,1.03f,1,45),
                Part("nitrous","Purge-first nitrous kit","Induction","+25% power, +12% torque, -20 structure, +20 kg. Replaces other forced induction.",690,4,20,1.25f,1,1,-20),
                // Deferred maintenance: each "upgrade" just brings one part of the car up to what it should have been.
                // Every one has its own category, so they stack; the gains are small because the bar is the floor.
                Part("wheels","Four round wheels","Wheels","+5% grip, +3% top speed. The previous set was listed on the invoice as \"round-adjacent.\"",150,0,0,1,1.05f),
                Part("pistons","A piston for every cylinder","Internals","+10% power, +8% torque, +3 kg. The engine had been making do with three and a good attitude.",380,1,3,1.1f),
                Part("plugs","Spark plugs that match","Ignition","+5% power, +3% torque. One of the old plugs was a bolt with a wire taped to it.",60,0,0,1.05f),
                Part("alignment","Wheels pointed the same way","Alignment","+10% turn response, +3% grip. The car no longer has a favorite ditch.",110,0,0,1,1.03f),
                Part("brakes","Brake pads that contain pad","Brakes","+6% turn response, +4 kg. The old ones were backing plate and optimism, applied firmly.",140,0,4),
                Part("radcap","A radiator cap that is a radiator cap","Radiator cap","+3% power, +15 chassis health. The engine now runs at a temperature instead of a weather event. Replaces a soda can and a hose clamp, both of which were doing their best.",45,0,0,1.03f,1,1,15),
                Part("mounts","Engine mounts made of rubber","Engine mounts","+3% power actually reaches the wheels, +25 chassis health, +6 kg. The old mounts were rope; the engine went for walks.",190,1,6,1.03f,1,1,25),
                Part("shocks","Shocks with fluid in them","Dampers","+18% damping, +5% grip, +2 kg. The originals were springs wearing shock absorber costumes.",230,1,2,1,1.05f),
                Part("muffler","Muffler attached at both ends","Muffler","+3% power, +2% top speed, -3 kg. The last one was bolted on at the front and dragging at the back, which counted as a brake.",95,0,-3,1.03f),
                Part("timing","Timing belt with all its teeth","Timing","+3% power, +6% torque, +1 kg. Valve timing is now a schedule instead of a suggestion.",210,2,1,1.03f),
                Part("lugs","The full set of lug nuts","Hardware","+30 chassis health, +3% grip, +1 kg. Each wheel was held on by two nuts and a rumor.",40,0,1,1,1.03f,1,30),
                Part("seat","Driver's seat bolted down","Interior","+5% turn response, +10 chassis health, +3 kg. Steering is easier when you are not also sliding toward the passenger door.",70,0,3,1,1,1,10)
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
            // Nitro recovery is gone: the parts that traded it now trade something that still matters.
            Find("headers").torqueMultiplier = .97f; Find("blower").maxSpeedMultiplier = .96f; Find("intercooler").torqueMultiplier = 1.05f; Find("swap").turnMultiplier = .94f;
            Find("wheels").maxSpeedMultiplier = 1.03f; Find("pistons").torqueMultiplier = 1.08f; Find("plugs").torqueMultiplier = 1.03f;
            Find("alignment").turnMultiplier = 1.1f; Find("brakes").turnMultiplier = 1.06f; Find("seat").turnMultiplier = 1.05f;
            Find("shocks").dampingMultiplier = 1.18f; Find("muffler").maxSpeedMultiplier = 1.02f; Find("timing").torqueMultiplier = 1.06f;
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
            // Every weapon is bolted together from whatever was in the shed. Each has one job it does best and a
            // clear way to lose with it, in the spirit of Halo's sandbox. Garage weapons (scrap) have unlimited
            // ammunition; field weapons come off wrecks with limited rounds. The ten oddballs splice two firing
            // modes together (see WeaponRules.Oddball).
            Weapons = new[] {
                // ---- Garage arsenal ----
                Weapon(0,"riveter","Chain-Fed Nail Gun","A framing nailer welded to a bicycle chain that feeds nails as fast as the pedals turn. Garage weapon; unlimited ammunition.",12,11,90,0,new Color(1,.79f,.2f),
                    "ALL-ROUNDER AUTOMATIC","Steady damage at any sensible range and it never runs dry.","Low damage per nail; the chain spreads its aim at long range."),
                Weapon(1,"invoice","Tailpipe Bazooka","A muffler, a road flare and far too much black powder. Huge blast, only a few shots.",110,.9f,49,8,new Color(1,.35f,.08f),
                    "HEAVY ANTI-VEHICLE","Deletes a car or a cluster of them in one blast.","Slow rockets that fast cars can dodge, and very little ammunition."),
                Weapon(2,"sweeper","Coffee-Can Scattergun","A coffee can of roofing tacks behind a shotgun shell. Eight pellets that clear a path through a crowd.",10,1.7f,96,0,new Color(.3f,1,1),
                    "CLOSE-QUARTERS CROWD CLEARER","Shreds anything at bumper distance, several cars at once.","Pellets fizzle out past twenty metres."),
                Weapon(3,"carbine","Rain-Gutter Repeater","A length of rain gutter rifled with a drill bit. Accurate sustained fire at medium range.",24,4.2f,135,0,new Color(.85f,1,.55f),
                    "PRECISION MID-RANGE","Accurate at range; picks off weak points and fleeing cars.","Slow rate of fire; misses cost you and swarms overwhelm it."),
                Weapon(4,"grenade","Potato Cannon","Hairspray-fired PVC lobbing pipe bombs in a high arc. Great for clustered cars and barricades.",195,1.3f,43,10,new Color(1,.65f,.18f),
                    "ARCING DEMOLITION","Lands over cover and wrecks clusters.","Slow shells; limited rounds; hard to land on moving cars."),
                Weapon(5,"mortar","Propane-Tank Mortar","A propane tank with the bottom cut off and a very rude landing.",145,.55f,42,10,new Color(1,.24f,.18f),
                    "LONG-RANGE ARTILLERY","Hits from beyond the enemy's reach.","Long flight time; useless up close; three rounds a drop."),
                Weapon(6,"mines","Tripwire Pipe Bomb","Enemy road charges: pipe bombs on fishing line, dropped behind a hostile car.",105,1.2f,0,6,new Color(1,.48f,.1f),
                    "ENEMY ROAD CHARGE","Punishes anyone chasing along a narrow road.","Telegraphed and stationary; drive around it."),
                Weapon(7,"minigun","Lawnmower Gatling","Six barrels spun by a pull-start mower engine. A short-range storm of scrap.",8,20,105,0,new Color(1,.92f,.32f),
                    "SUPPRESSION","Overwhelming close-range damage while you hold the line.","Wild spread and a hungry belt: ammunition drains fast."),
                Weapon(8,"sniper",".50 Cal Weed Whacker","A trimmer motor spinning up a fifty-calibre slug. One accurate, brutal shot.",155,.65f,210,0,new Color(.45f,.95f,1),
                    "LONG-RANGE PRECISION","Enormous single hits from across the map.","Slow to cycle; nearly useless against a swarm on your bumper."),
                Weapon(9,"cluster","Bottle-Rocket Rack","Three sticks of illegal fireworks taped to a roof rack. Explosive and limited.",75,.7f,54,5,new Color(1,.18f,.65f),
                    "AREA SATURATION","Three rockets cover a wide spread.","Few volleys; each rocket alone is modest."),
                Weapon(10,"boomstick","Drainpipe Double-Barrel","Two drainpipes and a nail for a firing pin. Twelve heavy pellets; devastating at bumper distance.",15,.95f,98,0,new Color(.75f,.42f,1),
                    "POINT-BLANK SHOTGUN","Two-shot kills on anything touching your bumper.","Slow reload and almost no reach."),
                Weapon(11,"shredder","Sawblade Slingshot","Surgical tubing launching three table-saw blades. Each blade cuts through one car into the next.",6,5,88,0,new Color(.95f,.84f,.48f),
                    "PIERCING FAN","Blades pass through the first car and keep cutting.","Short reach and light damage per blade."),
                Weapon(12,"pothole","Tennis-Ball Mortar","A welded stack of beer cans lobbing charges that bounce once before they go off.",32,.85f,38,2.4f,new Color(.98f,.52f,.22f),
                    "INDIRECT FIRE","Arcs over cover and bounces into hiding spots.","Small blast and slow shells; hopeless against fast movers."),
                Weapon(13,"needler","Cactus-Spine Needler","A saguaro rib packed into a leaf-spring launcher. Spines curve after cars; seven stuck spines rupture together.",6,8,58,0,new Color(.95f,.36f,.78f),
                    "TRACKING / SUPERCOMBINE","Spines chase moving cars; seven hits set off a big rupture.","Slow spines, weak until they stack, and cover eats them."),
                Weapon(14,"zapper","Jumper-Cable Zapper","Two car batteries and a pair of jumper cables. Each bolt stalls an engine and arcs to two more cars.",9,3,110,0,new Color(.45f,.85f,1),
                    "ENGINE KILLER","Stalls engines and guns; arcs through a pack.","Barely scratches the paint on its own."),
                Weapon(15,"torch","Weed-Burner Torch","A propane weed burner with the regulator removed. Sets cars alight; the fire keeps working after they flee.",5,14,34,0,new Color(1,.45f,.12f),
                    "FLAMETHROWER / BURN","Burning damage keeps ticking after contact.","Fourteen metres of reach and nothing more."),
                Weapon(16,"railgun","Arc-Welder Railgun","Two arc welders and a length of copper busbar. An instant bolt that punches through every car in line.",150,.45f,0,0,new Color(.55f,.9f,1),
                    "PIERCING HEAVY SHOT","Instant hit at long range that passes through whole convoys.","Two seconds to recharge; a miss hurts."),
                Weapon(17,"aircannon","Leaf-Blower Air Cannon","Six leaf blowers and a trash-can barrel. A pressure slug that shoves cars off roads and over cliffs.",20,1.4f,70,0,new Color(.85f,.95f,1),
                    "CROWD CONTROL / KNOCKBACK","Shoves cars into hazards, each other and off ledges.","Low damage; it moves problems rather than ending them."),
                // ---- Oddballs: two firing modes spliced together ----
                Weapon(18,"sprinkler","Lawn-Sprinkler Firebomb","GRENADE + FLAMETHROWER. A lobbed paint can that spins like a lawn sprinkler, spraying burning fuel in a ring.",40,.8f,42,3.5f,new Color(1,.5f,.1f),
                    "AREA DENIAL","Sets every car around the landing point on fire.","Weak blast and a slow lob; the burn does the work."),
                Weapon(19,"pinata","Piñata Bottle Rocket","ROCKET + CLUSTER BOMB. A papier-mâché rocket that bursts into seven bouncing bomblets.",60,.6f,55,4.5f,new Color(1,.3f,.75f),
                    "AREA SATURATION","One hit carpets a wide area with bomblets.","Bomblets scatter randomly; poor against a single fast car."),
                Weapon(20,"shopvac","Shop-Vac Black Hole","GRENADE + GRAVITY. A shop vacuum wired backwards: it lands, drags cars into a heap, then blows.",120,.35f,40,7,new Color(.6f,.45f,1),
                    "CROWD GATHERER","Pulls a whole pack together for one big blast.","Long recharge and a short delay before it pays off."),
                Weapon(21,"deathray","Satellite-Dish Death Ray","SNIPER BEAM + MAGNIFYING GLASS. A mirrored satellite dish focusing the sun; damage builds the longer it stays on one car.",5,12,0,0,new Color(1,.93f,.5f),
                    "SUSTAINED FOCUS BEAM","Melts a target you can keep it on for two seconds.","Weak until it warms up; switching targets resets it."),
                Weapon(22,"crossbow","Dynamite Crossbow","PRECISION RIFLE + STICKY BOMB. A garage-door-spring crossbow firing dynamite bolts that stick, fizz and blow.",20,.9f,170,16.5f,new Color(1,.3f,.2f),
                    "STICKY DELAYED BLAST","A wide blast hits hardest near the dynamite.","A second's fuse, and misses stick to the ground instead."),
                Weapon(23,"bowling","Bowling-Ball Cannon","SHELL + RAM. A leaf-blower barrel launching a bowling ball that rolls through every car in its lane.",65,.7f,36,0,new Color(.35f,.35f,.45f),
                    "LANE CLEARER","Rolls through several cars and knocks them aside.","Only goes where the ground goes; hills and walls deflect it."),
                Weapon(24,"boomerang","Hubcap Boomerang","SAWBLADE + RETURN. A sharpened hubcap that flies out, cuts through cars and doubles in size on the return.",78,1.3f,60,0,new Color(.8f,.85f,.9f),
                    "DOUBLE-PASS PIERCER","Triple damage and a larger second pass.","Useless beyond its turn-around point."),
                Weapon(25,"harpoon","Tow-Hook Harpoon","SNIPER + WINCH. A tow hook on a winch cable: it spears a car and yanks it into your bumper.",45,.8f,120,0,new Color(.9f,.75f,.4f),
                    "PULL / SETUP","Drags runners and snipers into ram and shotgun range.","Single target and pulls danger toward you."),
                Weapon(26,"firecracker","Firecracker Blunderbuss","SHOTGUN + EXPLOSIVES. A blunderbuss loaded with lit firecrackers; every pellet pops.",13,1.1f,80,1.8f,new Color(1,.25f,.2f),
                    "EXPLOSIVE SPREAD","Splash on every pellet rewards near misses.","Short range, and the pops hurt you up close."),
                Weapon(27,"sentry","Lawn-Chair Sentry","MINE + TURRET. A lawn chair with a nail gun zip-tied to it. Drop it and it guards the road for twelve seconds.",8,.25f,90,0,new Color(.5f,1,.55f),
                    "DEPLOYABLE TURRET","Keeps shooting while you drive elsewhere; two at a time.","Stationary; slow to redeploy; light damage per nail."),
                // ---- Field replacement for the old Lien Mines ----
                Weapon(28,"gokart","Dynamite Go-Kart","A toy go-kart with a car-alarm brain and a bundle of dynamite. It hunts down the nearest hostile and hugs it.",140,.8f,24,7,new Color(1,.62f,.1f),
                    "SEEKING BOMB","Chases targets around cover and corners.","Slow; can be outrun or blocked by walls; three a drop."),
                Weapon(29,"scattermortar","Scatter Mortar","Nine mini charges arc out in a shotgun fan. The near shells burst first, then the far shells rain down.",24,.65f,36,2.6f,new Color(1,.57f,.18f),
                    "ARCING SHOTGUN","A directed shower of explosives blankets near and far ground.","No tracking; moving targets can slip between the bursts.")
            };
        }
        static WeaponDefinition Weapon(int i,string id,string name,string desc,float damage,float rate,float speed,float radius,Color color,string role,string strength,string weakness)
        { var w=Weapon(i,id,name,desc,damage,rate,speed,radius,color); w.role=role; w.strength=strength; w.weakness=weakness; return w; }
        static WeaponDefinition Weapon(int i,string id,string name,string desc,float damage,float rate,float speed,float radius,Color color)
        { var w=Create<WeaponDefinition>(id); w.contentOrder=i; w.id=id; w.displayName=name; w.description=desc; w.damage=damage; w.fireRate=rate; w.speed=speed; w.blastRadius=radius; w.projectileColor=color; return w; }
    }
}
