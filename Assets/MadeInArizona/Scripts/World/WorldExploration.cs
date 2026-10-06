using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MadeInArizona
{
    /// <summary>Turns generated-world pins into persistent discoveries, salvage stops and hostile hideouts.</summary>
    public sealed class WorldExploration : MonoBehaviour
    {
        public static WorldExploration Instance { get; private set; }
        public static int CurrentTier { get { return CoopSession.IsRemoteClient ? CoopSession.Instance.HostTier : CalculateTier(GameManager.Instance); } }
        public static int Alloy { get { return CountMaterial("alloy"); } }
        public static int Circuits { get { return CountMaterial("circuit"); } }
        public static int Propellant { get { return CountMaterial("propellant"); } }
        public static int MaterialCount { get { return Alloy + Circuits + Propellant; } }
        public static string Inventory { get { return "Alloy "+Alloy+" • Circuits "+Circuits+" • Propellant "+Propellant; } }
        public static readonly string[] RecipeDescriptions = {
            "Hardened nail-gun chain • 2 alloy • +25% Chain-Fed Nail Gun damage",
            "Guided tailpipe fuse • 1 circuit + 1 propellant • +30% Tailpipe Bazooka damage and blast radius",
            "Choked coffee-can barrel • 1 alloy + 1 circuit • +25% Coffee-Can Scattergun damage"
        };

        readonly HashSet<string> activatedHideouts = new HashSet<string>();
        readonly HashSet<string> warned = new HashSet<string>();
        readonly Dictionary<string,GameObject> markers = new Dictionary<string,GameObject>();
        GeneratedWorld observedWorld;
        float refresh;

        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Update()
        {
            if (CoopSession.IsRemoteClient) return;
            var game = GameManager.Instance;
            if (!game || !game.IsPlaying || !game.Player || GeneratedWorld.Active == null) return;
            if(observedWorld!=GeneratedWorld.Active) ResetForWorld(GeneratedWorld.Active);
            refresh -= Time.deltaTime;
            bool interaction = InputManager.Instance != null && InputManager.Instance.Interact;
            if (CoopSession.Instance && CoopSession.Instance.IsHost)
                foreach (var car in VehicleController.Active)
                    if (car && car.IsPlayer && car.HasRemoteInput && car.RemoteControls.interact) interaction = true;
            if (refresh > 0 && !interaction) return;
            refresh = .2f;
            if (CoopSession.Instance && CoopSession.Instance.IsHost)
            {
                foreach (var car in VehicleController.Active)
                {
                    if (!car || !car.IsPlayer || !car.Damage || car.Damage.IsDead) continue;
                    bool use = car.HasRemoteInput ? car.RemoteControls.interact : InputManager.Instance != null && InputManager.Instance.Interact;
                    TickPins(game, car, use);
                }
            }
            else TickPins(game, game.Player, interaction);
        }

        void TickPins(GameManager game, VehicleController actor, bool interact)
        {
            Vector3 player = actor.transform.position;
            var world = GeneratedWorld.Active;
            int tier = CurrentTier;
            for (int i=0;i<world.Pins.Count;i++)
            {
                WorldPin pin = world.Pins[i];
                string key = PinKey(world, pin);
                string seen = SeenKey(pin);
                bool saved = game.Save.collectibles.Contains(key);
                if ((saved||game.Save.collectibles.Contains(seen)) && !pin.discovered) { pin.discovered=true; world.Pins[i]=pin; }
                float distance = FlatDistance(player,pin.position);
                if (distance < 150 && IsHideout(pin) && !saved) ActivateHideout(pin,actor);
                if (distance < 75 && tier < pin.requiredTier && warned.Add(key))
                    game.Notify(pin.label+" • tier "+pin.requiredTier+" terrain. Upgrade tires, suspension or drivetrain before pushing deeper.");
                if (distance < 34 && !pin.discovered)
                {
                    pin.discovered=true;world.Pins[i]=pin;
                    if(!game.Save.collectibles.Contains(seen))game.Save.collectibles.Add(seen);
                    SaveSystem.Save(game.Save);game.Notify("MAP DISCOVERED • "+pin.label);
                }
                if (distance < 7 && IsSalvage(pin) && !saved)
                {
                    EnsureSalvageMarker(pin,game);
                    if(interact) Collect(pin,key,game);
                    else if(warned.Add(key+":use")) game.Notify("Press E / gamepad South to recover specialty salvage.");
                }
                else if(distance < 11 && !saved && !IsSalvage(pin) && (!IsHideout(pin)||HideoutClear(pin.position)))
                    Discover(pin,key,game);
            }
        }

        void ActivateHideout(WorldPin pin, VehicleController player)
        {
            if (!activatedHideouts.Add(pin.id)) return;
            StartCoroutine(SpawnHideout(pin,player));
        }

        // One vehicle per frame: building a full squad at once cost ~40 ms in a single frame.
        IEnumerator SpawnHideout(WorldPin pin, VehicleController player)
        {
            var world=GeneratedWorld.Active;
            EnemyFaction faction=FactionRules.ForHideout(pin.id);
            int baseline=Mathf.Clamp(2+pin.requiredTier,2,5);
            int count=SpawnManager.EnemyCount(baseline);
            for(int i=0;i<count;i++)
            {
                float a=i*137.5f*Mathf.Deg2Rad;
                Vector3 p=pin.position+new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*(14+(i%3)*6+(i/12)*10);
                p=Ground(p);
                int archetype=Mathf.Clamp(i%baseline+pin.requiredTier*2,0,6);
                if(i>0)yield return null;
                while(GameManager.Instance&&GameManager.Instance.State==GameState.Paused)yield return null;
                if(GeneratedWorld.Active!=world||!player||!GameManager.Instance||!GameManager.Instance.IsPlaying)yield break;
                var enemy=SpawnManager.Spawn(p+Vector3.up*.8f,archetype,player,faction);
                if(enemy&&pin.requiredTier>0)
                {
                    float threat=1+pin.requiredTier*.22f;
                    enemy.Stats.horsepower*=threat;enemy.Stats.maxHealth*=threat;
                    enemy.Damage.Initialize(enemy,enemy.Stats.maxHealth);
                }
            }
            GameManager.Instance?.Notify(pin.label+" • "+FactionRules.Name(faction)+" inbound");
        }

        static bool HideoutClear(Vector3 center)
        {
            foreach(var vehicle in VehicleController.Active)
                if(vehicle&&!vehicle.IsPlayer&&vehicle.Damage!=null&&!vehicle.Damage.IsDead&&FlatDistance(vehicle.transform.position,center)<90)return false;
            return true;
        }

        void Collect(WorldPin pin,string key,GameManager game)
        {
            string material=MaterialFor(pin.kind,pin.id);
            game.Save.collectibles.Add(key);
            game.Save.collectibles.Add("world-material:"+WorldSeed()+":"+material+":"+pin.id);
            pin.discovered=true;
            int index=GeneratedWorld.Active.Pins.FindIndex(p=>p.id==pin.id);if(index>=0)GeneratedWorld.Active.Pins[index]=pin;
            SaveSystem.Save(game.Save);game.Notify("RECOVERED • "+material.ToUpperInvariant()+" specialty material");AudioManager.Instance?.PlayUI();
            if(markers.TryGetValue(pin.id,out var marker)&&marker)Destroy(marker);
        }

        void Discover(WorldPin pin,string key,GameManager game)
        {
            game.Save.collectibles.Add(key);SaveSystem.Save(game.Save);
            game.Notify(IsHideout(pin)?"HIDEOUT CLEARED • "+pin.label:"LANDMARK FILED • "+pin.label);
        }

        public static bool TryCraft(int recipe)
        {
            if (CoopSession.IsRemoteClient) return false;
            var game=GameManager.Instance;if(!game||game.Save==null||recipe<0||recipe>=RecipeDescriptions.Length)return false;
            string crafted="world-craft:"+recipe;if(game.Save.collectibles.Contains(crafted)){game.Notify("That weapon upgrade is already fitted.");return false;}
            int alloy=recipe==0?2:recipe==2?1:0,circuit=recipe>0?1:0,propellant=recipe==1?1:0;
            if(Alloy<alloy||Circuits<circuit||Propellant<propellant){game.Notify("More specialty salvage is required for that recipe.");return false;}
            game.Save.collectibles.Add(crafted);
            for(int i=0;i<alloy;i++)game.Save.collectibles.Add("world-spent:alloy:"+recipe+":"+i);
            for(int i=0;i<circuit;i++)game.Save.collectibles.Add("world-spent:circuit:"+recipe+":"+i);
            for(int i=0;i<propellant;i++)game.Save.collectibles.Add("world-spent:propellant:"+recipe+":"+i);
            SaveSystem.Save(game.Save);game.Notify("CRAFTED • "+RecipeDescriptions[recipe]);return true;
        }

        public static float PlayerWeaponMultiplier(int weaponIndex)
        { var game=GameManager.Instance;return game&&game.Save!=null&&game.Save.collectibles.Contains("world-craft:"+weaponIndex)?(weaponIndex==1?1.3f:1.25f):1; }
        public static float PlayerRocketRadiusMultiplier()
        { var game=GameManager.Instance;return game&&game.Save!=null&&game.Save.collectibles.Contains("world-craft:1")?1.3f:1; }

        static int CountMaterial(string material)
        {
            var game=GameManager.Instance;if(!game||game.Save==null)return 0;int have=0,spent=0;
            foreach(string key in game.Save.collectibles){if(key.StartsWith("world-material:")&&key.Contains(":"+material+":"))have++;else if(key.StartsWith("world-spent:"+material+":"))spent++;}
            return Mathf.Max(0,have-spent);
        }
        static int CalculateTier(GameManager game)
        {
            if(!game||game.Save==null)return 0;int tier=Mathf.Clamp(game.Save.installedParts.Count/2,0,3);
            VehicleDefinition vehicle=ContentCatalog.Vehicles[Mathf.Clamp(game.Save.selectedVehicle,0,ContentCatalog.Vehicles.Length-1)];
            VehicleStats stats=GarageManager.StatsFor(vehicle,game.Save);
            if(stats.drivetrain==Drivetrain.AWD)tier=Mathf.Max(tier,1);
            if(stats.suspensionTravel>=.55f&&stats.rideHeight>=.75f)tier=Mathf.Max(tier,2);
            if(stats.drivetrain==Drivetrain.AWD&&stats.grip>=1.2f&&stats.suspensionTravel>=.7f)tier=3;
            return tier;
        }
        static int WorldSeed(){return WorldConfigStore.LastValid!=null?WorldConfigStore.LastValid.seed:117;}
        static string PinKey(GeneratedWorld world,WorldPin pin){return "world:"+WorldSeed()+":"+pin.id;}
        static string SeenKey(WorldPin pin){return "world-seen:"+WorldSeed()+":"+pin.id;}
        static string MaterialFor(string kind,string id){string value=(kind+" "+id).ToLowerInvariant();return value.Contains("tech")||value.Contains("relay")?"circuit":value.Contains("fuel")||value.Contains("munition")?"propellant":"alloy";}
        static bool IsHideout(WorldPin pin){string kind=(pin.kind??"").ToLowerInvariant();return kind.Contains("hideout")||kind.Contains("hostile")||kind.Contains("faction")||(kind=="poi"&&StableModulo(pin.id,5)==0);}
        static bool IsSalvage(WorldPin pin){string kind=(pin.kind??"").ToLowerInvariant(),label=(pin.label??"").ToLowerInvariant();return kind.Contains("salvage")||kind.Contains("cache")||kind.Contains("material")||label.Contains("wreck")||label.Contains("fuel stop")||(kind=="poi"&&StableModulo(pin.id,5)==2);}
        static int StableModulo(string value,int divisor){int hash=17;foreach(char c in value??"")hash=unchecked(hash*31+c);return (hash&int.MaxValue)%divisor;}
        static float FlatDistance(Vector3 a,Vector3 b){a.y=b.y=0;return Vector3.Distance(a,b);}
        static Vector3 Ground(Vector3 p){if(GeneratedWorld.Active!=null){p.x=Mathf.Clamp(p.x,GeneratedWorld.Active.WorldBounds.min.x,GeneratedWorld.Active.WorldBounds.max.x);p.z=Mathf.Clamp(p.z,GeneratedWorld.Active.WorldBounds.min.z,GeneratedWorld.Active.WorldBounds.max.z);p.y=GeneratedWorld.HeightAt(p);}return p;}

        void EnsureSalvageMarker(WorldPin pin,GameManager game)
        {
            if(markers.TryGetValue(pin.id,out var existing)&&existing)return;
            markers[pin.id]=game.World.CreateMarker(Ground(pin.position),new Color(.2f,1f,.75f),"SPECIALTY SALVAGE • HOLD E");
        }
        void ResetForWorld(GeneratedWorld world)
        {
            foreach(var marker in markers.Values)if(marker)Destroy(marker);
            markers.Clear();activatedHideouts.Clear();warned.Clear();observedWorld=world;
        }
    }
}
