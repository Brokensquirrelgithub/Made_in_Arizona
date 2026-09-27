using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace MadeInArizona
{
    /// <summary>Opt-in native-player integration test. Runs only with -miaSmokeTest; uses isolated saves.</summary>
    public sealed class SmokeTestRunner : MonoBehaviour
    {
        public static bool Active => Array.IndexOf(Environment.GetCommandLineArgs(), "-miaSmokeTest") >= 0;
        readonly List<string> checks = new List<string>();
        readonly List<string> failures = new List<string>();
        void Update()
        {
            if (Active && Time.realtimeSinceStartup > 240) { Debug.LogError("MIA_SMOKE_TIMEOUT: test did not finish; inspect earlier exceptions."); Application.Quit(2); }
        }
        IEnumerator Start()
        {
            if (!Active) yield break;
            // Windows disables keyboard devices when the automated player is hidden.
            // Keep injected test input enabled; normal interactive sessions retain their settings.
            InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            // Real mouse/keyboard activity must not override the isolated injected devices.
            foreach(var device in InputSystem.devices)if(device.native)InputSystem.DisableDevice(device);
            Application.logMessageReceived += OnLog;
            yield return new WaitForSecondsRealtime(2);
            var game = GameManager.Instance;
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-miaMusicTest")>=0)
            { yield return TestSoundtrack(); FinishResults(); yield break; }
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-miaEnemyBalanceTest")>=0)
            { yield return TestEnemyBalance(); FinishResults(); yield break; }
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-miaWeaponAudioTest")>=0)
            { yield return TestImportedWeaponAudio(); FinishResults(); yield break; }
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-miaGeneratedCampaignTest")>=0)
            {
                var campaignKeyboard=InputSystem.AddDevice<Keyboard>();
                game.WorldConfig.seed=173;game.WorldConfig.size=1600;
                yield return TestCampaign(campaignKeyboard,0);
                InputSystem.RemoveDevice(campaignKeyboard);
                FinishResults();yield break;
            }
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-miaReachabilityTest")>=0){yield return TrailReview.Reachability(Check);FinishResults();yield break;}
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-miaTrailReview")>=0){yield return TrailReview.Run(Check);FinishResults();yield break;}
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-miaFrameTimingTest")>=0){yield return FrameTimingProbe.Run(Check);FinishResults();yield break;}
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-miaWorldTest")>=0){yield return TestWorldGeneration();yield break;}
            if(Array.IndexOf(Environment.GetCommandLineArgs(), "-miaUltraTest")>=0) { game.Save.settings.quality=3;game.ApplySettings();game.ReturnToGarage();yield return new WaitForSecondsRealtime(.5f); }
            Check("garage startup", game && game.Player && game.State == GameState.Garage);
            Check("catalog: eight vehicles and fifteen missions", ContentCatalog.Vehicles.Length == 8 && ContentCatalog.Missions.Length == 15);
            Check("six-way texture pairs and shader",Resources.Load<Texture2D>("SixWay/Fireball_P") && Resources.Load<Texture2D>("SixWay/Fireball_N") && Resources.Load<Texture2D>("SixWay/Smoke_P") && Resources.Load<Texture2D>("SixWay/Smoke_N") && Shader.Find("MadeInArizona/SixWaySmoke").isSupported);
            Check("Suzuki present", FindFirstObjectByType<SuzukiDog>() != null);
            Capture("01-garage");
            yield return new WaitForSecondsRealtime(.35f);
            SaveSystem.Save(game.Save);
            Check("save round trip", SaveSystem.Load().money == game.Save.money && File.Exists(SaveSystem.Path));
            game.StartMission(0);
            yield return new WaitForSecondsRealtime(1);
            Check("mission startup", game.State == GameState.Playing && game.Mission.Stage == 0);
            Check("destructible scene", FindObjectsByType<DestructionSystem>(FindObjectsSortMode.None).Length > 30);
            if(Array.IndexOf(Environment.GetCommandLineArgs(), "-miaPlaytestRevision")>=0) { yield return TestPlaytestRevision(); FinishResults(); yield break; }
            if(Array.IndexOf(Environment.GetCommandLineArgs(), "-miaTargetingTest")>=0) { yield return TestLobTargeting();FinishResults();yield break; }
            if(Array.IndexOf(Environment.GetCommandLineArgs(), "-miaDebrisReview")>=0) { yield return ReviewDebris();FinishResults();yield break; }
            if(Array.IndexOf(Environment.GetCommandLineArgs(), "-miaVfxReview")>=0) { yield return ReviewEffects();yield break; }
            Capture("02-mission");
            yield return new WaitForSecondsRealtime(.35f);
            var keyboard = InputSystem.AddDevice<Keyboard>();
            if(Array.IndexOf(Environment.GetCommandLineArgs(), "-miaHandlingTest")>=0) { yield return ReviewGround(); yield return VehicleHandlingRegression.Run(keyboard,Check); }
            if(Array.IndexOf(Environment.GetCommandLineArgs(), "-miaDevTest")>=0)yield return TestDevTuning();
            var begin = game.Player.transform.position;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
            yield return new WaitForSecondsRealtime(2.5f);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            Check("keyboard driving moves rigidbody", Vector3.Distance(begin, game.Player.transform.position) > 2f);
            var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            target.name = "Integration test propane target"; target.transform.SetParent(game.World.transform);
            // The turret can still face the mouse after driving. Place this narrow fixture
            // along the actual muzzle's firing ray rather than assuming a centered turret.
            var testMuzzle=VehicleController.FindChild(game.Player.Visual,"Muzzle");
            Vector3 shotOrigin=testMuzzle?testMuzzle.position:game.Player.transform.position;
            shotOrigin.y=game.Player.transform.position.y+.85f;
            target.transform.position = shotOrigin + Vector3.forward * 11;
            target.transform.localScale = Vector3.one * 2;
            var targetDamage = target.AddComponent<DestructionSystem>(); targetDamage.Configure(4, ExplosionKind.Propane, true, 30);
            Physics.SyncTransforms();
            game.Player.Weapons.FirePrimary(Vector3.forward);
            yield return new WaitForSecondsRealtime(.4f);
            Check("garage weapon initializes and field slot starts empty", game.Player.Weapons != null && game.Player.Weapons.GarageWeapon != null && game.Player.Weapons.FieldWeapon == null);
            Check("swept projectile destroys target", !target || targetDamage.IsDestroyed);
            if(Array.IndexOf(Environment.GetCommandLineArgs(), "-miaUltraTest")>=0) {
                var point=game.Player.transform.position+Vector3.forward*8+Vector3.right*5;
                ExplosionSystem.Detonate(point,6,0,null,ExplosionKind.FuelTank);
                yield return new WaitForSecondsRealtime(.15f);Capture("05-ultra-explosion");
                yield return new WaitForSecondsRealtime(.8f);Capture("06-ultra-fire");
                yield return new WaitForSecondsRealtime(.35f);
                Check("Ultra shaders supported",Shader.Find("MadeInArizona/HeatHaze").isSupported && Shader.Find("MadeInArizona/Scattering").isSupported);
            }
            var pad = InputSystem.AddDevice<Gamepad>();
            var padStart = game.Player.transform.position;
            InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = Vector2.up, rightStick = Vector2.right, rightTrigger = 1 });
            yield return new WaitForSecondsRealtime(.8f);
            Check("gamepad movement and right-trigger fire", InputManager.Instance.UsingGamepad && InputManager.Instance.Primary && Vector3.Distance(padStart, game.Player.transform.position) > .4f);
            InputSystem.QueueStateEvent(pad, new GamepadState());
            InputSystem.RemoveDevice(pad);
            float health = game.Player.Damage.Health;
            game.Player.Damage.ApplyDamage(40, game.Player.transform.position + Vector3.forward, null);
            Check("localized damage", game.Player.Damage.Health < health);
            float damagedHealth=game.Player.Damage.Health;
            CombatPickup.Create(PickupKind.Health,game.Player.transform.position,null,25);
            yield return new WaitForSecondsRealtime(.5f);
            Check("driving over enemy repair restores chassis",game.Player.Damage.Health>damagedHealth);
            CombatPickup.Create(PickupKind.Weapon,game.Player.transform.position,WeaponRules.Find("grenade"),2);
            yield return new WaitForSecondsRealtime(.5f);
            Check("empty LT slot equips a driven-over weapon",game.Player.Weapons.FieldWeapon&&game.Player.Weapons.FieldWeapon.id=="grenade"&&game.Player.Weapons.FieldAmmo==2);
            CombatPickup.Create(PickupKind.Weapon,game.Player.transform.position,WeaponRules.Find("invoice"),3);
            yield return new WaitForSecondsRealtime(.4f);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.F));
            yield return null;
            Check("F swaps a held field weapon",game.Player.Weapons.FieldWeapon&&game.Player.Weapons.FieldWeapon.id=="invoice");
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());
            int heldAmmo=game.Player.Weapons.FieldAmmo;
            var matching=CombatPickup.Create(PickupKind.Weapon,game.Player.transform.position+game.Player.transform.forward*9,WeaponRules.Find("invoice"),2);
            yield return new WaitForSecondsRealtime(1.2f);
            Check("matching weapon drop magnets in and adds ammo",!matching&&game.Player.Weapons.FieldWeapon&&game.Player.Weapons.FieldWeapon.id=="invoice"&&game.Player.Weapons.FieldAmmo==heldAmmo+2);
            game.Player.Repair(200);
            Check("repair restores chassis", game.Player.Damage.Health > health - 40);
            game.Pause(); Check("pause freezes simulation", Time.timeScale == 0 && game.State == GameState.Paused);
            game.Resume(); Check("resume restores simulation", Time.timeScale == 1 && game.State == GameState.Playing);
            // Advance via the same proximity, combat and interaction conditions used during normal play.
            Teleport(game.Mission.ObjectivePosition + new Vector3(0, 1, -5));
            yield return new WaitForSecondsRealtime(.3f);
            Check("recovery reaches junkyard phase", game.Mission.Stage >= 1);
            foreach (var enemy in new List<VehicleController>(VehicleController.Active)) if (enemy && !enemy.IsPlayer) enemy.Damage.ApplyDamage(10000, enemy.transform.position, game.Player.gameObject);
            yield return new WaitForSecondsRealtime(.5f);
            Check("combat advances objective", game.Mission.Stage >= 2);
            Teleport(game.Mission.ObjectivePosition + Vector3.up);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.E));
            yield return new WaitForSecondsRealtime(.4f);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return new WaitForSecondsRealtime(.4f);
            Check("ledger interaction", game.Mission.Stage >= 3);
            Capture("03-junkyard");
            yield return new WaitForSecondsRealtime(.35f);
            Teleport(game.World.ExtractionPoint + Vector3.up);
            yield return new WaitForSecondsRealtime(.6f);
            Check("mission completion", game.State == GameState.Won);
            Capture("04-completion");
            yield return new WaitForSecondsRealtime(.35f);
            Check("progression saved", SaveSystem.Load().completedMissions.Contains(0));
            game.ReturnToGarage();
            yield return new WaitForSecondsRealtime(.5f);
            Check("garage return", game.State == GameState.Garage);
            var save = game.Save;
            save.money = 10000; save.salvage = 100; save.unlockedMission = 14;
            var baseStats = GarageManager.StatsFor(ContentCatalog.Vehicles[0], save);
            var part = ContentCatalog.Parts[0]; GarageManager.BuyPart(part, save); GarageManager.TogglePart(part, save);
            Check("mechanical part modifies stats", GarageManager.StatsFor(ContentCatalog.Vehicles[0], save).horsepower > baseStats.horsepower);
            Check("second starter vehicle selectable", GarageManager.SelectVehicle(1, save));
            game.RefreshGarageVehicle();
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-miaCombatTest") >= 0) yield return TestVehicleCombat(keyboard);
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-miaCampaignTest") >= 0) yield return TestCampaign(keyboard);
            InputSystem.RemoveDevice(keyboard);
            FinishResults();
        }
        IEnumerator TestLobTargeting()
        {
            var game=GameManager.Instance;var player=game.Player;
            foreach(var vehicle in new List<VehicleController>(VehicleController.Active))if(vehicle&&vehicle!=player)Destroy(vehicle.gameObject);
            yield return null;
            Vector3 aim=Vector3.forward;
            var probe=SpawnManager.Spawn(player.transform.position+aim*24,0,player);
            probe.Body.isKinematic=true;probe.GetComponent<EnemyAI>().enabled=false;
            player.enabled=false;
            float health=probe.Damage.Health;
            player.Weapons.EquipField(WeaponRules.Find("grenade"),2);
            player.Weapons.AimAt(aim);yield return null;
            Check("lobbed weapon soft-lock selects enemy in aim cone",player.Weapons.AssistedTarget==probe);
            Capture("34-ballistic-lock-ring");yield return new WaitForSecondsRealtime(.2f);
            player.Weapons.FireSecondary(aim);
            yield return new WaitForSecondsRealtime(.72f);Capture("35-assisted-grenade-impact");
            yield return new WaitForSecondsRealtime(1.1f);
            Check("assisted grenade reaches predicted enemy position",!probe||probe.Damage.IsDead||probe.Damage.Health<health);
            if(probe)Destroy(probe.gameObject);
            player.enabled=true;
        }
        void FinishResults()
        {
            Application.logMessageReceived -= OnLog;
            string output = Path.Combine(Application.temporaryCachePath, "mia-smoke-results.txt");
            string result = string.Join("\n", checks) + "\n" + string.Join("\n", failures) + "\nRESULT: " + (failures.Count == 0 ? "PASS" : "FAIL");
            File.WriteAllText(output, result);
            Debug.Log("MIA_SMOKE_RESULTS\n" + result + "\n" + output);
            Application.Quit(failures.Count == 0 ? 0 : 1);
        }
        IEnumerator TestWorldGeneration()
        {
            var game=GameManager.Instance;
            var dog=System.Array.Find(FindObjectsByType<SuzukiDog>(FindObjectsSortMode.None),d=>!d.IsRiding);string dogIssue;
            Check("Suzuki garage roaming route ready",SuzukiDogSmoke.GarageRoamingReady(dog,out dogIssue));
            float dogDeadline=Time.realtimeSinceStartup+18;
            while(!SuzukiDogSmoke.HasReachedFloorAwayFromBed(dog)&&Time.realtimeSinceStartup<dogDeadline)yield return null;
            Check("Suzuki leaves bed and reaches garage floor",SuzukiDogSmoke.HasReachedFloorAwayFromBed(dog));
            Capture("25-suzuki-roaming");yield return new WaitForSecondsRealtime(.35f);
            game.ShowMainMenu();yield return new WaitForSecondsRealtime(.4f);Capture("17-main-menu");yield return new WaitForSecondsRealtime(.35f);
            game.StartCampaign(173,1600);
            yield return new WaitUntil(()=>game.State==GameState.Playing);
            yield return new WaitForSecondsRealtime(1);
            var world=GeneratedWorld.Active;
            Check("generated campaign starts from menu",world&&game.IsPlaying&&world.Towns.Count>=2);
            Check("dirt trail network spans the generated map",world&&world.TrailCount>=20);
            var groundTextures=GroundTextureSet.Load();
            Check("licensed ground textures and height maps linked",groundTextures&&groundTextures.Diffuse(1)&&groundTextures.Height(1));
            var nature=PandazoleNatureCatalog.Load();
            Check("Pandazole nature meshes and atlas packaged",nature&&nature.atlas&&nature.pines.Length>0&&nature.rocks.Length>0&&nature.grasses.Length>0);
            yield return TestNatureModelReview();
            yield return TestPresentationControls();
            Check("Arizona outline excludes rectangular corners",GeneratedWorld.Contains(game.World.PlayerSpawn)&&!GeneratedWorld.Contains(new Vector3(-790,0,-790))&&world.MapTexture.GetPixel(0,0).a<.1f);
            Check("generated terrain has collision chunks",world.GetComponentsInChildren<MeshCollider>().Length>=50);
            float low=1000,high=-1000;
            for(int i=0;i<20;i++){float h=GeneratedWorld.HeightAt(new Vector3(-220+i*22,0,-250+i*24));low=Mathf.Min(low,h);high=Mathf.Max(high,h);}
            Check("world retains gentle elevation variation",high-low>3 && high-low<game.WorldConfig.terrainHeight);
            var keyboard=InputSystem.AddDevice<Keyboard>();var begin=game.Player.transform.position;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W));yield return new WaitForSecondsRealtime(2);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());
            Debug.Log("MIA_WORLD_DRIVE start="+begin+" end="+game.Player.transform.position+" grounded="+game.Player.Grounded);
            Check("starter drives generated terrain beyond legacy bounds",Vector3.Distance(begin,game.Player.transform.position)>2&&Vector3.Distance(game.Player.transform.position,game.World.PlayerSpawn)<70);
            Capture("18-generated-town");yield return new WaitForSecondsRealtime(.35f);
            game.GetComponent<GameUI>().OpenWorldMap();yield return new WaitForSecondsRealtime(.35f);Capture("19-arizona-map");yield return new WaitForSecondsRealtime(.35f);game.Resume();
            Vector3 town=world.Towns[1];float first=GeneratedWorld.HeightAt(new Vector3(170,0,210));
            game.StartMission(0);yield return new WaitForSecondsRealtime(.2f);
            Check("same seed reproduces towns and terrain",Vector3.Distance(town,GeneratedWorld.Active.Towns[1])<.001f&&Mathf.Abs(first-GeneratedWorld.HeightAt(new Vector3(170,0,210)))<.001f);
            var old=GeneratedWorld.Active;
            File.WriteAllText(WorldConfigStore.Path,"{");float invalidDeadline=Time.realtimeSinceStartup+4;while(WorldConfigStore.LastError==null&&Time.realtimeSinceStartup<invalidDeadline)yield return null;
            Check("partial JSON retains playable last-valid world",GeneratedWorld.Active==old&&WorldConfigStore.LastError!=null);
            var edited=new WorldGenConfig{seed=271,size=800};
            edited.pins.Add(new WorldPin{id="test-cache",label="Test relay cache",kind="salvage-tech",position=Vector3.zero});
            File.WriteAllText(WorldConfigStore.Path,JsonUtility.ToJson(edited,true));
            float deadline=Time.realtimeSinceStartup+25;
            while(GeneratedWorld.Active==old&&Time.realtimeSinceStartup<deadline)yield return null;
            yield return new WaitForSecondsRealtime(.7f);
            Check("valid JSON automatically regenerates changed seed",GeneratedWorld.Active&&GeneratedWorld.Active!=old&&GeneratedWorld.Active.Seed==271);
            Check("different seed changes terrain",Mathf.Abs(first-GeneratedWorld.HeightAt(new Vector3(170,0,210)))>.01f);
            var pin=GeneratedWorld.Active.Pins.Find(p=>p.id=="test-cache");
            Check("JSON authored location placed",pin!=null);
            int circuits=WorldExploration.Circuits;
            Teleport(pin.position+Vector3.up*1.2f);InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.E));yield return new WaitForSecondsRealtime(.8f);InputSystem.QueueStateEvent(keyboard,new KeyboardState());
            Check("off-road specialty salvage collected through interaction",WorldExploration.Circuits==circuits+1);
            Check("map discovery persists",SaveSystem.Load().collectibles.Contains("world-seen:271:test-cache"));
            game.Save.collectibles.Add("world-material:271:alloy:test-a");game.Save.collectibles.Add("world-material:271:alloy:test-b");
            float enemyBase=ContentCatalog.Weapons[0].damage;
            Check("specialty material crafts player-only weapon upgrade",WorldExploration.TryCraft(0)&&WorldExploration.PlayerWeaponMultiplier(0)>1&&ContentCatalog.Weapons[0].damage==enemyBase&&WorldExploration.Alloy==0);
            game.WorldConfig.size=3200;WorldConfigStore.Save(game.WorldConfig);game.StartMission(0);yield return new WaitForSecondsRealtime(.3f);
            Check("maximum world size generates with bounded geometry",Mathf.Approximately(GeneratedWorld.Active.WorldBounds.size.x,3200)&&GeneratedWorld.Active.transform.Find("Chunked terrain").GetComponentsInChildren<MeshCollider>().Length<=64);
            game.WorldConfig.size=1600;WorldConfigStore.Save(game.WorldConfig);game.StartMission(0);yield return new WaitForSecondsRealtime(.3f);
            // Inspect the forest and river using camera-only targets, leaving physics untouched.
            var camera=CameraController.Instance;var target=camera.Target;var focus=new GameObject("World visual review");camera.Target=focus.transform;
            for(int i=0;i<2;i++)
            {
                Vector3 p=Vector3.zero;
                if(i==0)p=GeneratedWorld.Active.Towns[GeneratedWorld.Active.Towns.Count-1]+new Vector3(-72,0,55);
                else foreach(var t in GeneratedWorld.Active.GetComponentsInChildren<Transform>(true))
                    if(t.name=="Salt River"){p=t.GetComponent<MeshFilter>().sharedMesh.vertices[t.GetComponent<MeshFilter>().sharedMesh.vertexCount/2]+Vector3.right*16;break;}
                p.y=GeneratedWorld.HeightAt(p);focus.transform.position=p;camera.Snap();
                yield return new WaitForSecondsRealtime(3f);Capture(i==0?"20-northern-biome":"21-river-biome");yield return new WaitForSecondsRealtime(.35f);
            }
            // A closer native view documents actual canopy/ground-cover geometry and shader detail.
            Vector3 forest=GeneratedWorld.Active.Towns[GeneratedWorld.Active.Towns.Count-1]+new Vector3(-72,0,55);forest.y=GeneratedWorld.HeightAt(forest);
            focus.transform.position=forest;camera.Snap();yield return new WaitForSecondsRealtime(3f);
            var ui=game.GetComponent<GameUI>();ui.enabled=false;camera.enabled=false;
            Camera.main.orthographic=false;Camera.main.fieldOfView=58;
            Camera.main.transform.position=forest+new Vector3(14,10,-17);Camera.main.transform.LookAt(forest+Vector3.up*3);
            yield return new WaitForSecondsRealtime(1f);Capture("22-forest-detail");yield return new WaitForSecondsRealtime(.35f);
            camera.enabled=true;ui.enabled=true;camera.Snap();
            yield return ReviewGroundTextureTransitions();
            var ecology=GeneratedWorld.Active.GetComponent<LivingWorldDetail>();
            Check("dense streamed ecology generates plants and stones",ecology&&ecology.PlantClumps>500&&ecology.Stones>80&&ecology.Trees>20);
            Check("ecology streaming stays within bounded tile budget",ecology&&ecology.LoadedTiles<=121);
            Debug.Log("MIA_ECOLOGY: loaded tiles="+ecology.LoadedTiles+" generated plants="+ecology.PlantClumps+" trees="+ecology.Trees+" stones="+ecology.Stones);
            bool upwardMarks=true;int marks=0;
            foreach(var filter in GeneratedWorld.Active.GetComponentsInChildren<MeshFilter>())if(filter.name=="Worn center markings")
            {marks++;foreach(var n in filter.sharedMesh.normals)if(n.y<.5f)upwardMarks=false;}
            Check("road markings face upward",marks>0&&upwardMarks);
            Check("wind scenery shader supported",Shader.Find("MadeInArizona/LivingScenery")&&Shader.Find("MadeInArizona/LivingScenery").isSupported);
            camera.Target=target;camera.Snap();Destroy(focus);
            Vector3 patrolAt=GeneratedWorld.Active.Towns[1]+new Vector3(0,1,-10);patrolAt.y=GeneratedWorld.HeightAt(patrolAt)+1;
            Teleport(patrolAt);camera.Snap();yield return null;
            var director=GeneratedWorld.Active.GetComponent<RoadPatrolDirector>();
            Check("occasional road patrol spawns away from starter",director&&director.TrySpawnPatrol());
            Check("road patrol population is bounded",director&&director.LivePatrols<=RoadPatrolDirector.PatrolLimit);
            InputSystem.RemoveDevice(keyboard);
            Application.logMessageReceived-=OnLog;
            string result=string.Join("\n",checks)+"\n"+string.Join("\n",failures)+"\nRESULT: "+(failures.Count==0?"PASS":"FAIL");
            Debug.Log("MIA_WORLD_RESULTS\n"+result);Application.Quit(failures.Count==0?0:1);
        }
        IEnumerator TestNatureModelReview()
        {
            var catalog=PandazoleNatureCatalog.Load();if(!catalog)yield break;
            var controller=CameraController.Instance;var camera=Camera.main;var ui=GameManager.Instance.GetComponent<GameUI>();
            GameManager.Instance.Pause();controller.enabled=false;ui.enabled=false;
            var root=new GameObject("Nature model review");root.transform.position=Vector3.up*200;
            var material=new Material(Shader.Find("MadeInArizona/LivingScenery"));material.SetTexture("_NeedleAtlas",catalog.atlas);
            for(int i=0;i<catalog.pines.Length;i++)
            {
                var mesh=catalog.pines[i];var item=new GameObject(mesh.name);item.transform.SetParent(root.transform,false);
                float scale=7/mesh.bounds.size.y;item.transform.localScale=Vector3.one*scale;
                item.transform.localPosition=new Vector3((i%4-1.5f)*12,-mesh.bounds.min.y*scale,(i/4-1)*12);
                item.AddComponent<MeshFilter>().sharedMesh=mesh;item.AddComponent<MeshRenderer>().sharedMaterial=material;
                WorldArt.Text(mesh.name,root.transform,new Vector3((i%4-1.5f)*12,0,(i/4-1)*12-4),.8f,Color.white,Quaternion.Euler(45,0,0));
            }
            camera.transform.position=new Vector3(0,245,-45);camera.transform.LookAt(Vector3.up*200);camera.orthographic=true;camera.orthographicSize=27;
            yield return new WaitForSecondsRealtime(.4f);Capture("28-nature-model-review");yield return new WaitForSecondsRealtime(.4f);
            Destroy(root);Destroy(material);controller.enabled=true;ui.enabled=true;controller.Snap();GameManager.Instance.Resume();
        }
        IEnumerator ReviewGroundTextureTransitions()
        {
            var world=GeneratedWorld.Active;var controller=CameraController.Instance;var camera=Camera.main;var ui=GameManager.Instance.GetComponent<GameUI>();
            if(!world||!controller||!camera)yield break;
            bool oldOrtho=camera.orthographic;float oldFov=camera.fieldOfView;bool oldUi=ui.enabled;
            controller.enabled=false;ui.enabled=false;camera.orthographic=false;camera.fieldOfView=52;
            Vector3 ext=world.WorldBounds.extents;
            Vector3[] sites={new Vector3(ext.x*.20f,0,-ext.z*.18f),new Vector3(-ext.x*.18f,0,ext.z*.58f)};
            string[] names={"32-lowland-texture-transition","33-highland-texture-transition"};
            for(int i=0;i<sites.Length;i++)
            {
                Vector3 focus=sites[i];focus.y=GeneratedWorld.HeightAt(focus);
                camera.transform.position=focus+new Vector3(20,12,-23);camera.transform.LookAt(focus+Vector3.up*.45f);
                yield return new WaitForSecondsRealtime(1f);Capture(names[i]);yield return new WaitForSecondsRealtime(.35f);
            }
            camera.orthographic=oldOrtho;camera.fieldOfView=oldFov;controller.enabled=true;ui.enabled=oldUi;controller.Snap();
        }
        IEnumerator TestPresentationControls()
        {
            var game=GameManager.Instance;var ui=game.GetComponent<GameUI>();var tuning=DevTuning.Current;
            float oldDof=tuning.depthOfField,oldVignette=tuning.vignette;
            game.Pause();ui.enabled=false;tuning.depthOfField=0;tuning.vignette=0;DevTuning.Apply();
            yield return new WaitForSecondsRealtime(.4f);Capture("23-post-effects-off");yield return new WaitForSecondsRealtime(.3f);
            tuning.depthOfField=0;tuning.vignette=.6f;DevTuning.Apply();
            yield return new WaitForSecondsRealtime(.4f);Capture("27-vignette-only");yield return new WaitForSecondsRealtime(.3f);
            var stack=UnityEngine.Rendering.VolumeManager.instance.stack;
            Check("vignette slider reaches live volume stack",Mathf.Abs(stack.GetComponent<UnityEngine.Rendering.Universal.Vignette>().intensity.value-.6f)<.01f);
            tuning.depthOfField=1;tuning.vignette=0;DevTuning.Apply();
            yield return new WaitForSecondsRealtime(.4f);Capture("24-post-effects-on");yield return new WaitForSecondsRealtime(.3f);
            var pipeline=UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            Check("orthographic blur renderer feature packaged",Resources.FindObjectsOfTypeAll<OrthographicDepthBlurFeature>().Length>0&&Shader.Find("MadeInArizona/OrthographicDepthBlur").isSupported);
            Check("orthographic blur slider drives pixel radius",Shader.GetGlobalVector("_ArizonaOrthoDofParams").z>1);
            tuning.depthOfField=oldDof;tuning.vignette=oldVignette;DevTuning.Apply();game.Resume();
            ExplosionSystem.Detonate(game.Player.transform.position+Vector3.right*8,6,0,game.Player.gameObject,ExplosionKind.FuelTank);
            yield return new WaitForSecondsRealtime(.04f);game.Pause();
            bool flash=false;foreach(var light in FindObjectsByType<Light>(FindObjectsSortMode.None))if(light.name=="Pooled explosion light"&&light.enabled&&light.intensity>20&&light.shadows!=LightShadows.None)flash=true;
            Check("explosion flash is bright and shadow enabled",flash&&pipeline.supportsAdditionalLightShadows);
            yield return new WaitForSecondsRealtime(.15f);Capture("26-explosion-shadow-flash");yield return new WaitForSecondsRealtime(.3f);
            ui.enabled=true;game.Resume();
        }
        IEnumerator TestDevTuning()
        {
            var game=GameManager.Instance;var original=game.Save.settings.dev;
            game.Save.settings.dev=new DevTuning();
            var tuning=DevTuning.Current;
            float max=game.Player.Damage.MaxHealth;
            float healthFraction=game.Player.Damage.Health/max;
            tuning.playerHealth=2;tuning.bloom=1.7f;tuning.chromatic=.23f;tuning.depthOfField=.5f;
            tuning.incomingDamage=0;DevTuning.Apply();
            game.Player.Damage.ApplyDamage(100,game.Player.transform.position,null);
            Check("live health capacity preserves health fraction and blocks incoming damage",Mathf.Approximately(game.Player.Damage.MaxHealth,max*2)&&Mathf.Approximately(game.Player.Damage.Health,max*2*healthFraction));
            var target=new GameObject("Dev tuning damage probe").AddComponent<VehicleController>();
            var definition=ContentCatalog.Vehicles[0];
            target.transform.position=new Vector3(80,20,120);
            target.Initialize(definition,GarageManager.StatsFor(definition,game.Save),false);
            float targetBase=target.Damage.MaxHealth;
            tuning.enemyHealth=1.5f;tuning.playerDamage=2;DevTuning.Apply();
            target.Damage.ApplyDamage(10,target.transform.position,game.Player.gameObject);
            Check("enemy capacity and outgoing damage modifiers",Mathf.Approximately(target.Damage.MaxHealth,targetBase*1.5f)&&Mathf.Approximately(target.Damage.Health,targetBase*1.5f-20));
            Destroy(target.gameObject);
            SaveSystem.Save(game.Save);var loaded=SaveSystem.Load();
            Check("dev tuning persists through save reload",loaded.settings.dev.playerHealth==2&&Mathf.Approximately(loaded.settings.dev.chromatic,.23f));
            var volumes=FindObjectsByType<UnityEngine.Rendering.Volume>(FindObjectsSortMode.None);
            bool post=false;
            foreach(var volume in volumes)if(volume.profile.TryGet<UnityEngine.Rendering.Universal.ChromaticAberration>(out var ca))post|=Mathf.Approximately(ca.intensity.value,.23f);
            Check("dev controls update real post-processing",post);
            bool ao=false;foreach(var feature in Resources.FindObjectsOfTypeAll<UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion>())ao|=feature.isActive;
            Check("ambient occlusion renderer feature present",ao);
            game.Save.settings.dev=original;DevTuning.Apply();SaveSystem.Save(game.Save);
            var ui=game.GetComponent<GameUI>();ui.OpenDevMenu();
            yield return new WaitForSecondsRealtime(.4f);Capture("16-dev-tuning");
            yield return new WaitForSecondsRealtime(.4f);game.Resume();
            Check("dev menu resumes gameplay",game.IsPlaying);
        }
        IEnumerator ReviewGround()
        {
            var surfaces=new List<Renderer>();
            foreach(var renderer in GameManager.Instance.World.GetComponentsInChildren<Renderer>())
                if(renderer.name=="Road surface")surfaces.Add(renderer);
            bool separated=surfaces.Count==5,flat=true;
            for(int i=0;i<surfaces.Count;i++)
            {
                flat &= surfaces[i].shadowCastingMode==UnityEngine.Rendering.ShadowCastingMode.Off && surfaces[i].bounds.size.y<.001f;
                for(int j=0;j<i;j++) separated &= Mathf.Abs(surfaces[i].bounds.center.y-surfaces[j].bounds.center.y)>.002f;
            }
            Check("roads have distinct depth planes and no overlay shadow casters",separated&&flat);
            // Move the camera through the reported crossing with the vehicle stationary.
            var camera=CameraController.Instance;
            var original=camera.Target;
            var focus=new GameObject("Ground review camera target");
            camera.Target=focus.transform;
            for(int i=0;i<3;i++)
            {
                focus.transform.position=new Vector3((i-1)*3,0,96+i*2);
                camera.Snap();
                yield return new WaitForSecondsRealtime(.4f);
                Capture("15-ground-motion-"+i);
                yield return new WaitForSecondsRealtime(.35f);
            }
            camera.Target=original;camera.Snap();Destroy(focus);
        }
        IEnumerator TestVehicleCombat(Keyboard keyboard)
        {
            var game=GameManager.Instance;
            int money=game.Save.money,unlock=game.Save.unlockedMission,completed=game.Save.completedMissions.Count;
            // Capacity prevents a single chain blast killing the repairing test driver between frames.
            float savedCapacity=DevTuning.Current.playerHealth;
            DevTuning.Current.playerHealth=10;
            game.StartCombatTrial();yield return new WaitForSecondsRealtime(.6f);
            Check("combat trial starts without campaign unlock",game.IsCombatTrial&&game.State==GameState.Playing);
            var factions=new HashSet<EnemyFaction>();bool factionLoadouts=true;
            foreach(var ai in FindObjectsByType<EnemyAI>())
            {
                factions.Add(ai.Faction);
                var weapon=ai.GetComponent<VehicleController>().Weapons.GarageWeapon;
                factionLoadouts &= weapon && weapon.id==FactionRules.PrimaryWeapon(ai.Faction,ai.Archetype);
            }
            Check("combat trial opens with three factions and their weapon sets",factions.Count==3&&factionLoadouts);
            foreach(var ai in FindObjectsByType<EnemyAI>(FindObjectsSortMode.None)){ai.enabled=false;ai.GetComponent<VehicleController>().SetAIInput(Vector2.zero,Vector3.forward,false);}
            Check("field weapon starts empty in combat",game.Player.Weapons.FieldWeapon==null);
            game.Player.Weapons.EquipField(WeaponRules.Find("invoice"),WeaponRules.PickupAmmo("invoice"));
            Check("enemy rocket drop fits LT with limited ammo",game.Player.Weapons.FieldWeapon!=null&&game.Player.Weapons.FieldAmmo==WeaponRules.PickupAmmo("invoice"));
            game.Player.Weapons.AddFieldAmmo(99);
            Check("field ammo is capped per weapon",game.Player.Weapons.FieldAmmo==WeaponRules.MaxAmmo("invoice"));
            game.Player.Weapons.EquipField(WeaponRules.Find("invoice"),WeaponRules.PickupAmmo("invoice"));
            foreach(var ai in FindObjectsByType<EnemyAI>(FindObjectsSortMode.None))ai.enabled=true;
            float fightStart=Time.time,deadline=Time.time+70;bool attacked=false,captured=false,warned=false;
            while(Time.time<deadline&&game.IsPlaying) {
                foreach(var ai in FindObjectsByType<EnemyAI>())factions.Add(ai.Faction);
                var player=game.Player;
                if(player.Damage.LastDamageTime>=fightStart && player.Damage.LastDamageSource && player.Damage.LastDamageSource.GetComponentInParent<EnemyAI>())attacked=true;
                // Keep the integration driver alive so it can exercise both real combat waves.
                player.Damage.Repair(250*Time.deltaTime);
                VehicleController target=null;float nearest=float.MaxValue;
                foreach(var enemy in VehicleController.Active) {
                    if(!enemy||enemy.IsPlayer||enemy.Damage.IsDead)continue;
                    float d=(enemy.transform.position-player.transform.position).sqrMagnitude;
                    if(d<nearest){nearest=d;target=enemy;}
                }
                if(target) {
                    float distance=Mathf.Sqrt(nearest);
                    Vector3 aim=target.transform.position+target.Body.linearVelocity*(distance/110f)-player.transform.position;
                    player.Weapons.FirePrimary(aim.normalized);
                    Vector3 rocketAim=target.transform.position+target.Body.linearVelocity*(distance/49f*.65f)-player.transform.position;
                    if(distance>9 && player.Weapons.FieldWeapon)player.Weapons.FireSecondary(rocketAim.normalized);
                    Vector3 movement=target.transform.position-player.transform.position;
                    Key key=distance>20?(Mathf.Abs(movement.x)>Mathf.Abs(movement.z)?(movement.x>0?Key.D:Key.A):(movement.z>0?Key.W:Key.S)):
                        (Mathf.Repeat(Time.time-fightStart,8)<4?Key.A:Key.D);
                    InputSystem.QueueStateEvent(keyboard,new KeyboardState(key));
                }
                if(!warned)foreach(var ai in FindObjectsByType<EnemyAI>(FindObjectsSortMode.None))if(ai.IsTelegraphingAttack){
                    var view=Camera.main.WorldToViewportPoint(ai.transform.position);
                    if(view.z>0&&view.x>.1f&&view.x<.9f&&view.y>.2f&&view.y<.8f){Capture("14-enemy-attack-warning");warned=true;break;}
                }
                if(!captured&&Time.time-fightStart>2.3f){Capture("12-vehicle-combat");captured=true;}
                yield return null;
            }
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());
            Check("combat trial fields all six faction crews",factions.Count==FactionRules.Count);
            Check("enemy vehicle weapons damage player",attacked);
            Check("real projectiles defeat both vehicle waves",game.State==GameState.Won&&game.Mission.Kills>=24);
            Check("vehicle hits and kills confirm",CombatFeedback.LastHitTime>=fightStart&&CombatFeedback.LastKillTime>=fightStart);
            Check("combat trial preserves campaign progression",game.Save.money==money&&game.Save.unlockedMission==unlock&&game.Save.completedMissions.Count==completed);
            Capture("13-combat-trial-complete");yield return new WaitForSecondsRealtime(.35f);
            game.RetryMission();yield return new WaitForSecondsRealtime(.35f);
            Check("combat trial replay restarts its own mode",game.IsCombatTrial&&game.Mission.Kills==0&&game.IsPlaying);
            DevTuning.Current.playerHealth=savedCapacity;DevTuning.Apply();
            game.ReturnToGarage();yield return new WaitForSecondsRealtime(.3f);
        }

        IEnumerator ReviewEffects()
        {
            var game=GameManager.Instance;
            game.GetComponent<GameUI>().enabled=false;
            CameraController.Instance.enabled=false;
            var camera=Camera.main;
            Vector3 point=game.Player.transform.position+Vector3.forward*11;
            camera.orthographicSize=11;
            camera.transform.position=point+new Vector3(12,11,-19);
            camera.transform.LookAt(point+Vector3.up*5);
            ExplosionSystem.Detonate(point,5,0,null,ExplosionKind.FuelTank);
            yield return new WaitForSecondsRealtime(.9f);Capture("07-sixway-day-fire");
            yield return new WaitForSecondsRealtime(1.1f);Capture("08-sixway-day-smoke");
            yield return new WaitForSecondsRealtime(.35f);
            Time.timeScale=0;
            game.Sun.transform.rotation=Quaternion.LookRotation(camera.transform.forward);game.Sun.intensity=2;
            yield return new WaitForSecondsRealtime(.2f);Capture("09-sixway-front-light");
            yield return new WaitForSecondsRealtime(.35f);
            game.Sun.transform.rotation=Quaternion.LookRotation(-camera.transform.forward);
            yield return new WaitForSecondsRealtime(.2f);Capture("10-sixway-back-light");
            yield return new WaitForSecondsRealtime(.35f);
            game.Sun.intensity=.05f;
            RenderSettings.ambientSkyColor=new Color(.025f,.035f,.06f);RenderSettings.ambientEquatorColor=Color.black;RenderSettings.ambientGroundColor=Color.black;
            WorldArt.Lamp("VFX review orange light",game.World.transform,point+new Vector3(0,3,-2),new Color(1,.25f,.03f),12,18);
            yield return new WaitForSecondsRealtime(.3f);Capture("11-sixway-local-light");
            yield return new WaitForSecondsRealtime(.35f);
            Time.timeScale=1;
            Debug.Log("MIA_VFX_REVIEW: "+(failures.Count==0?"PASS":"FAIL")+"\n"+string.Join("\n",failures));
            Application.Quit(failures.Count==0?0:1);
        }
        void Teleport(Vector3 position)
        {
            var p = GameManager.Instance.Player;
            p.Body.linearVelocity = Vector3.zero; p.Body.angularVelocity = Vector3.zero;
            p.Body.position = position; p.transform.position = position;
            p.Damage.Repair(10000);
            Physics.SyncTransforms();
            CameraController.Instance.Snap();
        }
        IEnumerator TestCampaign(Keyboard keyboard,int firstMission=1)
        {
            var game = GameManager.Instance;
            for (int index = firstMission; index < ContentCatalog.Missions.Length; index++) {
                if(firstMission>0)game.Save.unlockedMission = 14;
                Check("campaign mission unlocked " + (index+1),game.Save.unlockedMission>=index);
                int moneyBefore=game.Save.money;
                game.StartMission(index);
                yield return new WaitForSecondsRealtime(.2f);
                if(game.UseGeneratedWorld)Check("generated terrain active for mission " + (index+1),GeneratedWorld.Active!=null);

                if (index == 5) Check("forced inappropriate vehicle", game.CurrentVehicle.id == "thimble");
                if(game.UseGeneratedWorld && index==0) {
                    Teleport(game.World.PlayerSpawn);
                    yield return new WaitForSecondsRealtime(.6f);
                    Check("starter rests on generated terrain",game.Player.Grounded);
                    game.Mission.RegisterKill();
                    Check("grounded hill kill is not airborne",!game.Save.achievements.Contains("airborne"));
                    Teleport(game.World.PlayerSpawn+Vector3.up*10);
                    yield return new WaitForFixedUpdate();yield return new WaitForFixedUpdate();
                    game.Mission.RegisterKill();
                    Check("airborne kill still unlocks achievement",game.Save.achievements.Contains("airborne"));
                }
                int checkpoint = 0;
                for (int step = 0; step < 18 && game.IsPlaying; step++) {
                    game.Player.Repair(10000);
                    var mission = game.Mission;
                    var mode = mission.Definition.mode;
                    if(index==0 && mission.Stage==0) {
                        Teleport(mission.ObjectivePosition+Vector3.up);
                    } else if(index==0 && mission.Stage==1) {
                        DestroyHostiles();
                    } else if (mode == MissionMode.Demolition && mission.Stage == 0) {
                        foreach (var prop in FindObjectsByType<DestructionSystem>(FindObjectsSortMode.None)) {
                            if (!prop || prop.Explosive) continue;
                            prop.ApplyDamage(10000, prop.transform.position, game.Player.gameObject);
                            if (mission.DestructionCount >= mission.Definition.targetCount) break;
                        }
                    } else if (mode == MissionMode.Defense && mission.Stage == 0) {
                        Teleport(mission.ObjectivePosition + Vector3.up);
                        mission.Tick(mission.Definition.objectiveDuration / 3f + .15f);
                    } else if (mode == MissionMode.Convoy && mission.Stage == 0) {
                        foreach (var ai in FindObjectsByType<EnemyAI>(FindObjectsSortMode.None)) {
                            if (!ai.IsFriendly) continue;
                            var point = game.World.ObjectivePoints[Mathf.Min(checkpoint, 2)];
                            ai.GetComponent<VehicleController>().Body.position = point + Vector3.up;
                            ai.transform.position = point + Vector3.up;
                            Teleport(point + new Vector3(0, 1, -5));
                            checkpoint++;
                            break;
                        }
                        Physics.SyncTransforms();
                    } else if ((mode == MissionMode.Race || mode == MissionMode.Escape) && mission.Stage == 0) {
                        Teleport(mission.ObjectivePosition + Vector3.up);
                    } else if (mode == MissionMode.Boss && mission.Stage == 1) {
                        var pods = FindObjectsByType<BossWeakPoint>(FindObjectsSortMode.None);
                        foreach (var pod in pods) if (pod && !pod.Disabled) pod.ApplyDamage(pod.Health + 1, pod.transform.position, game.Player.gameObject);
                        Check("boss components disable in mission " + (index + 1), pods.Length > 0 && Array.Exists(pods, p => p && p.Disabled));
                        DestroyHostiles();
                    } else if (mode == MissionMode.Boss && mission.Stage == 0 || mode == MissionMode.Recovery && mission.Stage == 0) {
                        DestroyHostiles();
                    } else {
                        Teleport(mission.ObjectivePosition + Vector3.up);
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.E));
                    }
                    yield return new WaitForSecondsRealtime(.15f);
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                    yield return new WaitForSecondsRealtime(.1f);
                }
                Check("campaign objective completion " + (index + 1) + " " + game.Mission.Definition.mode, game.State == GameState.Won);
                var persisted=SaveSystem.Load();
                Check("campaign rewards persist for mission " + (index+1),persisted.completedMissions.Contains(index) && persisted.money>moneyBefore && persisted.bestScores.Count>index && persisted.bestScores[index]>0);
                game.ReturnToGarage();
                yield return new WaitForSecondsRealtime(.1f);
            }
        }
        IEnumerator ReviewDebris()
        {
            var game=GameManager.Instance;var player=game.Player;
            Vector3 center=player.transform.position+Vector3.forward*9;
            if(Physics.Raycast(center+Vector3.up*12,Vector3.down,out var ground,30,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))center.y=ground.point.y;
            var focus=new GameObject("Debris screenshot focus");focus.transform.position=Vector3.Lerp(player.transform.position,center,.68f);
            var camera=CameraController.Instance;camera.Target=focus.transform;camera.Snap();
            var ui=game.GetComponent<GameUI>();ui.enabled=false;
            for(int i=-2;i<=2;i++)
            {
                Vector3 p=center+new Vector3(i*2.15f,0,Mathf.Abs(i)*.7f);
                RoadsideProps.Crate(game.World.transform,p,.82f+Mathf.Abs(i)*.07f);
                if(i%2==0)RoadsideProps.Rock(game.World.transform,p+new Vector3(.8f,0,1.25f),new Vector3(1.25f,.8f,1.05f));
            }
            yield return new WaitForSecondsRealtime(.35f);
            ExplosionSystem.Detonate(center+Vector3.up*.8f,9,110,player.gameObject,ExplosionKind.Grenade);
            yield return new WaitForSecondsRealtime(.12f);
            Capture("29-debris-particles-action");
            yield return new WaitForSecondsRealtime(.9f);
            Capture("30-debris-particles-falling");
            Check("debris review generated fragments",FindFirstObjectByType<ExplosionSystem>()?.ActiveDebris>0);
            yield return new WaitForSecondsRealtime(5.2f);
            Capture("31-lingering-soot-mark");
            yield return new WaitForSecondsRealtime(.35f);
            ui.enabled=true;camera.Target=player.transform;Destroy(focus);
        }
        void DestroyHostiles()
        {
            foreach (var enemy in new List<VehicleController>(VehicleController.Active)) {
                if (!enemy || enemy.IsPlayer || enemy.Damage.IsDead) continue;
                var ai = enemy.GetComponent<EnemyAI>(); if (ai && ai.IsFriendly) continue;
                enemy.Damage.ApplyDamage(100000, enemy.transform.position, GameManager.Instance.Player.gameObject);
            }
        }
        IEnumerator TestImportedWeaponAudio()
        {
            var game=GameManager.Instance;var audio=AudioManager.Instance;
            var bank=Resources.Load<WeaponAudioBank>("Audio/Weapons/WeaponAudioBank");
            Check("pack grenade explosion is packaged",bank&&bank.ordnanceExplosion&&bank.ordnanceExplosion.name=="GL_explosion");
            Check("every weapon references imported recordings (own or borrowed)",ContentCatalog.Weapons.Length==29&&Array.TrueForAll(ContentCatalog.Weapons,w=>w.fireSounds!=null&&w.fireSounds.Length>0&&Array.TrueForAll(w.fireSounds,c=>c&&c.channels==2&&c.frequency==44100&&c.length>.5f)));
            float oldVolume=game.Save.settings.weapons;game.Save.settings.weapons=1;
            foreach(var weapon in ContentCatalog.Weapons)
            {
                StopPooledAudio(audio);audio.PlayShot(game.Player.transform.position,weapon);yield return null;
                Check("imported recording plays for "+weapon.id,ImportedShotPlaying(audio,weapon));
            }
            game.StartCombatTrial();game.Pause();yield return null;
            FreezeBalanceEnemies(BalanceHostiles());game.Resume();
            StopPooledAudio(audio);game.Player.Weapons.FirePrimary(Vector3.forward);yield return null;
            Check("player primary firing uses pack recordings",ImportedShotPlaying(audio,game.Player.Weapons.GarageWeapon));
            var enemy=BalanceHostiles().Find(v=>v.GetComponent<EnemyAI>().Faction==EnemyFaction.CourtesyCompliance);
            StopPooledAudio(audio);enemy.Weapons.FirePrimary(Vector3.forward);yield return null;
            Check("enemy primary firing uses pack recordings",ImportedShotPlaying(audio,enemy.Weapons.GarageWeapon));
            StopPooledAudio(audio);enemy.Weapons.FireSecondary(Vector3.forward);yield return null;
            Check("enemy rocket firing uses launcher recording",ImportedShotPlaying(audio,WeaponRules.Find("invoice")));
            StopPooledAudio(audio);audio.PlayExplosion(game.Player.transform.position,6,ExplosionKind.Grenade);yield return null;
            bool blast=false;foreach(var source in audio.GetComponentsInChildren<AudioSource>())blast|=source.clip==bank.ordnanceExplosion&&source.isPlaying;
            Check("ordnance blast uses pack explosion recording",blast);
            game.Save.settings.weapons=0;StopPooledAudio(audio);audio.PlayShot(game.Player.transform.position,WeaponRules.Find("sweeper"));yield return null;
            bool muted=true;foreach(var source in audio.GetComponentsInChildren<AudioSource>())if(source.gameObject!=audio.gameObject&&source.isPlaying)muted&=source.volume==0;
            Check("weapons volume mutes imported shots",muted);
            game.Save.settings.weapons=oldVolume;game.ReturnToGarage();
        }
        static void StopPooledAudio(AudioManager audio)
        {
            foreach(var source in audio.GetComponentsInChildren<AudioSource>())if(source.gameObject!=audio.gameObject)source.Stop();
        }
        static bool ImportedShotPlaying(AudioManager audio,WeaponDefinition weapon)
        {
            foreach(var source in audio.GetComponentsInChildren<AudioSource>())
                if(source.isPlaying&&Array.IndexOf(weapon.fireSounds,source.clip)>=0)return true;
            return false;
        }
        IEnumerator TestEnemyBalance()
        {
            var game=GameManager.Instance;
            game.Save.settings.dev=new DevTuning();game.Save.settings.difficulty=1;game.Save.unlockedMission=14;
            game.StartCombatTrial();game.Pause();yield return null;
            var enemies=BalanceHostiles();FreezeBalanceEnemies(enemies);
            Check("combat trial opens with twelve enemies",enemies.Count==12);
            Check("enemy health is one fifth across trial archetypes",enemies.TrueForAll(v=>Mathf.Approximately(v.Damage.MaxHealth,Mathf.Max(60,v.Stats.maxHealth)*.2f)));
            Check("player keeps full baseline health",Mathf.Approximately(game.Player.Damage.MaxHealth,Mathf.Max(60,game.Player.Stats.maxHealth)));
            var probe=enemies[0];float baseline=probe.Damage.MaxHealth;
            probe.Damage.ApplyDamage(baseline*.25f,probe.transform.position,game.Player.gameObject);
            DevTuning.Current.enemyHealth=1.5f;DevTuning.Apply();
            Check("live enemy tuning preserves reduced baseline and health fraction",Mathf.Approximately(probe.Damage.MaxHealth,baseline*1.5f)&&Mathf.Approximately(probe.Damage.Health,baseline*1.5f*.75f));
            DevTuning.Current.enemyHealth=1;DevTuning.Apply();
            Check("unrelated slider refresh retains one fifth enemy health",Mathf.Approximately(probe.Damage.MaxHealth,baseline));
            game.Resume();DestroyHostiles();game.Mission.Tick(.01f);game.Pause();
            enemies=BalanceHostiles();FreezeBalanceEnemies(enemies);
            Check("trial second wave contains twelve enemies",game.Mission.Stage==1&&game.Mission.Kills==12&&enemies.Count==12);
            game.Resume();DestroyHostiles();game.Mission.Tick(.01f);
            Check("trial completes only after twenty-four kills",game.State==GameState.Won&&game.Mission.Kills==24);

            game.StartMission(0);game.Pause();yield return null;
            enemies=BalanceHostiles();FreezeBalanceEnemies(enemies);
            Check("loaner pursuit has four hostiles including its unique suspect",enemies.Count==4);
            Teleport(game.Mission.ObjectivePosition+Vector3.up);game.Resume();game.Mission.Tick(.01f);game.Pause();
            enemies=BalanceHostiles();FreezeBalanceEnemies(enemies);
            Check("junkyard reinforcement adds twelve hostiles",enemies.Count==16&&game.Mission.Stage==1&&game.Mission.Objective.Contains("/ 12"));
            game.Resume();for(int i=0;i<3;i++)game.Mission.RegisterKill();game.Mission.Tick(.01f);game.Pause();
            Check("old three-kill gate no longer clears junkyard",game.Mission.Stage==1);

            int convoy=Array.FindIndex(ContentCatalog.Missions,m=>m.mode==MissionMode.Convoy);
            game.StartMission(convoy);game.Pause();yield return null;
            var friend=Array.Find(FindObjectsByType<EnemyAI>(FindObjectsSortMode.None),ai=>ai.IsFriendly);
            var escort=friend?friend.GetComponent<VehicleController>():null;
            Check("escort keeps full health while its hostile screen quadruples",escort&&Mathf.Approximately(escort.Damage.MaxHealth,Mathf.Max(60,escort.Stats.maxHealth))&&BalanceHostiles().Count==12);

            int bossMission=Array.FindIndex(ContentCatalog.Missions,m=>m.mode==MissionMode.Boss);
            game.StartMission(bossMission);game.Pause();yield return null;
            enemies=BalanceHostiles();FreezeBalanceEnemies(enemies);
            int expected=Mathf.Max(3,ContentCatalog.Missions[bossMission].enemyCount/3)*4;
            Check("boss security screen is four times larger",enemies.Count==expected);
            game.Resume();DestroyHostiles();game.Mission.Tick(.01f);game.Pause();
            enemies=BalanceHostiles();FreezeBalanceEnemies(enemies);
            var bosses=enemies.FindAll(v=>v.GetComponent<EnemyAI>().Archetype==7);
            Check("unique command rig arrives with three companions",bosses.Count==1&&enemies.Count==4);
            Check("command rig health also falls to one fifth",bosses.Count==1&&Mathf.Approximately(bosses[0].Damage.MaxHealth,Mathf.Max(60,bosses[0].Stats.maxHealth)*.2f));

            game.StartMission(0);game.Pause();yield return null;
            enemies=BalanceHostiles();FreezeBalanceEnemies(enemies);
            var oldEnemies=new HashSet<VehicleController>(enemies);
            var world=GeneratedWorld.Active;
            // Isolate one known hideout while exercising the normal exploration update.
            foreach(var pin in world.Pins)if(pin.kind=="hideout")pin.kind="landmark";
            Vector3 center=world.Towns[1];
            world.Pins.Add(new WorldPin{id="balance-hideout",kind="hideout",label="Balance fixture",position=center,requiredTier=2});
            Teleport(center+new Vector3(100,1,0));game.Resume();yield return new WaitForSecondsRealtime(.6f);game.Pause();
            enemies=BalanceHostiles().FindAll(v=>!oldEnemies.Contains(v));FreezeBalanceEnemies(enemies);
            Check("tier-two hideout spawns sixteen enemies",enemies.Count==16);
            Check("hideout health retains tier scaling above one fifth baseline",enemies.Count==16&&enemies.TrueForAll(v=>Mathf.Approximately(v.Damage.MaxHealth,v.Stats.maxHealth*.2f)));
            foreach(var enemy in BalanceHostiles())Destroy(enemy.gameObject);yield return null;
            Teleport(world.Towns[1]+new Vector3(0,1,-10));CameraController.Instance.Snap();
            var director=world.GetComponent<RoadPatrolDirector>();
            director.enabled=false;game.Resume();bool spawned=director.TrySpawnPatrol();game.Pause();
            enemies=BalanceHostiles();FreezeBalanceEnemies(enemies);
            Check("road encounter spawns four vehicles off screen",spawned&&director.LivePatrols==4);
            bool offscreen=true;
            foreach(var enemy in enemies){var view=Camera.main.WorldToViewportPoint(enemy.transform.position);offscreen&=view.z<=0||view.x<-.1f||view.x>1.1f||view.y<-.1f||view.y>1.1f;}
            Check("larger patrol still respects camera exclusion",offscreen);
            for(int attempt=0;attempt<4&&director.LivePatrols<8;attempt++)
            {game.Resume();director.TrySpawnPatrol();game.Pause();FreezeBalanceEnemies(BalanceHostiles());}
            Check("road patrol cap increases from two to eight",director.LivePatrols==8);
            game.Resume();bool capped=!director.TrySpawnPatrol();game.Pause();
            Check("road patrol population remains bounded",capped&&director.LivePatrols==8);
            game.ReturnToGarage();
        }
        static List<VehicleController> BalanceHostiles()
        {
            return new List<VehicleController>(VehicleController.Active).FindAll(v=>v&&!v.IsPlayer&&!v.Damage.IsDead&&v.GetComponent<EnemyAI>()&&!v.GetComponent<EnemyAI>().IsFriendly);
        }
        static void FreezeBalanceEnemies(List<VehicleController> enemies)
        {
            foreach(var enemy in enemies){enemy.GetComponent<EnemyAI>().enabled=false;enemy.Body.isKinematic=true;}
        }
        IEnumerator TestSoundtrack()
        {
            var game=GameManager.Instance;
            var music=game.GetComponent<MusicManager>();
            var tracks=Resources.LoadAll<AudioClip>("Audio/Music");
            Check("all fourteen soundtrack recordings packaged",tracks.Length==14);
            Check("music streams at original stereo sample rate",Array.TrueForAll(tracks,c=>c&&c.loadType==AudioClipLoadType.Streaming&&c.channels==2&&c.frequency==48000&&c.length>120));
            yield return new WaitForSecondsRealtime(3.2f);
            var source=SoundtrackSource(music);
            Check("garage plays looping Arizonaland",music.CurrentTrack=="Arizonaland"&&source&&source.isPlaying&&source.loop&&source.time>0);
            game.ShowMainMenu();yield return new WaitForSecondsRealtime(3.2f);
            source=SoundtrackSource(music);
            Check("main menu plays looping Arizona Nation",music.CurrentTrack=="Arizona Nation"&&source&&source.isPlaying&&source.loop);
            AudioManager.Instance.SetCombat(true);yield return new WaitForSecondsRealtime(3.2f);
            Check("combat switches to supplied driving soundtrack",music.CurrentTrack!="Arizonaland"&&music.CurrentTrack!="Arizona Nation"&&Array.Exists(tracks,c=>c.name==music.CurrentTrack));
            var played=new HashSet<string>();bool advanced=true;string last="";
            for(int i=0;i<12;i++)
            {
                played.Add(music.CurrentTrack);last=music.CurrentTrack;
                source=SoundtrackSource(music);
                if(!source||!source.clip){advanced=false;break;}
                // Seek near the end instead of waiting through the whole album.
                source.time=source.clip.length-1f;
                yield return new WaitForSecondsRealtime(3.3f);
                advanced&=music.CurrentTrack!=last;
            }
            Check("track endings advance automatically through all twelve driving tracks",advanced&&played.Count==12);
            Check("new shuffle does not immediately repeat its last track",music.CurrentTrack!=last);
            game.StartCombatTrial();
            Check("combat trial keeps sunny lighting before slider refresh",Mathf.Approximately(game.Sun.intensity,2.6f*DevTuning.Current.sunlight));
            game.Pause();source=SoundtrackSource(music);float playback=source.time;
            yield return new WaitForSecondsRealtime(.25f);
            Check("pause keeps music playing at reduced gain",source.isPlaying&&source.time>playback&&Mathf.Abs(source.volume-game.Save.settings.music*.62f*.5f)<.01f);
            float oldVolume=game.Save.settings.music;
            game.Save.settings.music=0;yield return null;
            Check("music slider mutes both crossfade sources",SoundtrackGain(tracks)<.001f);
            game.Save.settings.music=oldVolume;game.Resume();yield return new WaitForSecondsRealtime(.1f);
            float fullGain=SoundtrackGain(tracks);
            AudioManager.Instance.PlayExplosion(game.Player.transform.position,10);
            yield return new WaitForSecondsRealtime(.1f);
            Check("explosions duck recorded music",SoundtrackGain(tracks)<fullGain*.8f);
            game.ReturnToGarage();yield return new WaitForSecondsRealtime(3.2f);
            Check("return to garage crossfades back to Arizonaland",music.CurrentTrack=="Arizonaland"&&SoundtrackSource(music).isPlaying);
            Check("garage keeps sunny lighting before slider refresh",Mathf.Approximately(game.Sun.intensity,2.6f*DevTuning.Current.sunlight));
            game.StartMission(0);
            float sunlight=game.Sun.intensity;DevTuning.Apply();
            Check("mission lighting matches slider refresh",Mathf.Approximately(sunlight,game.Sun.intensity)&&Mathf.Approximately(sunlight,2.6f*DevTuning.Current.sunlight));
            var lifetimeProbe=new GameObject("Music lifecycle probe").AddComponent<MusicManager>();
            Destroy(lifetimeProbe.gameObject);yield return null;
            Check("music cleanup retains imported recordings",Array.TrueForAll(tracks,c=>c&&c.length>120));
        }
        static AudioSource SoundtrackSource(MusicManager music)
        {
            foreach(var source in music.GetComponents<AudioSource>())if(source.clip&&source.clip.name==music.CurrentTrack)return source;
            return null;
        }
        static float SoundtrackGain(AudioClip[] tracks)
        {
            float gain=0;
            foreach(var source in GameManager.Instance.GetComponents<AudioSource>())if(source.clip&&Array.IndexOf(tracks,source.clip)>=0)gain+=source.volume;
            return gain;
        }
        IEnumerator TestPlaytestRevision()
        {
            var game=GameManager.Instance;
            var player=game.Player;
            var ui=game.GetComponent<GameUI>();
            var migrated=new DevTuning{bloom=1.15f,exposure=.3f,contrast=15f,saturation=10f,chromatic=.08f,vignette=.2f,haze=.002f,ao=.45f};
            migrated.Clamp();
            Check("existing shipped visual defaults migrate to sunny look",migrated.presentationVersion==1&&Mathf.Approximately(migrated.exposure,.45f)&&Mathf.Approximately(migrated.vignette,.08f));
            var custom=new DevTuning{exposure=-.7f,vignette=.35f};custom.Clamp();
            Check("visual migration preserves custom sliders",Mathf.Approximately(custom.exposure,-.7f)&&Mathf.Approximately(custom.vignette,.35f));
            game.Pause();ui.enabled=false;
            yield return null;
            float before=SceneLuminance("34-sunny-startup");
            var profile=game.PresentationVolume.profile;
            int volumeCount=FindObjectsByType<UnityEngine.Rendering.Volume>(FindObjectsSortMode.None).Length;
            // An unrelated slider used to refresh the appearance; it must now leave the image stable.
            float steering=DevTuning.Current.steering;
            DevTuning.Current.steering=steering+.01f;DevTuning.Apply();
            yield return new WaitForSecondsRealtime(.35f);
            yield return null;
            float after=SceneLuminance("35-sunny-after-driving-slider");
            Debug.Log("MIA_BRIGHTNESS startup="+before+" refreshed="+after);
            Check("sunny startup has readable brightness",before>.20f&&before<.92f);
            Check("unrelated slider does not change scene brightness",Mathf.Abs(before-after)<.015f);
            Check("startup and slider share a single presentation volume",volumeCount==1&&FindObjectsByType<UnityEngine.Rendering.Volume>(FindObjectsSortMode.None).Length==1&&game.PresentationVolume.profile==profile);
            DevTuning.Current.steering=steering;DevTuning.Apply();
            game.Save.settings.quality=0;game.ApplySettings();
            Check("low quality keeps bloom disabled after tuning",profile.TryGet<UnityEngine.Rendering.Universal.Bloom>(out var bloom)&&!bloom.active);
            game.Save.settings.quality=2;game.ApplySettings();
            ui.enabled=true;game.Resume();

            player.Damage.ApplyDamage(30,player.transform.position,null);
            // The tank no longer refills on its own; start this check from a full tank.
            player.RefillNitro(10);
            float health=player.Damage.Health;
            int scrap=game.Save.salvage;
            Vector3 origin=player.transform.position+Vector3.right*10;
            var repair=CombatPickup.Create(PickupKind.Health,origin,null,25);
            var nitro=CombatPickup.Create(PickupKind.Nitro,origin+Vector3.forward,null,25);
            var salvage=CombatPickup.Create(PickupKind.Scrap,origin-Vector3.forward,null,2);
            var weapon=CombatPickup.Create(PickupKind.Weapon,origin+Vector3.forward*2,WeaponRules.Find("invoice"),4);
            Vector3 weaponStart=weapon.transform.position;
            Vector3 nitroStart=nitro.transform.position;
            Check("supply drops are larger",repair.transform.localScale.x>=1.1f);
            yield return new WaitForSeconds(.15f);
            Check("supplies respect spawn delay",repair&&Vector2.Distance(new Vector2(repair.transform.position.x,repair.transform.position.z),new Vector2(origin.x,origin.z))<.01f);
            game.Pause();yield return new WaitForSecondsRealtime(.45f);
            Check("pause suspends supply magnetism",salvage&&Mathf.Abs(salvage.transform.position.x-origin.x)<.01f);
            game.Resume();yield return new WaitForSeconds(.9f);
            Check("repair attracts and collects beyond contact range",!repair&&player.Damage.Health>health);
            Check("scrap attracts and collects beyond contact range",!salvage&&game.Save.salvage==scrap+2);
            Check("nitro attracts without wasting a full charge",nitro&&Vector3.Distance(nitro.transform.position,nitroStart)>3&&player.BoostCharge>=.99f);
            Check("weapon stays anchored outside contact range",weapon&&Vector2.Distance(new Vector2(weapon.transform.position.x,weapon.transform.position.z),new Vector2(weaponStart.x,weaponStart.z))<.01f);
            if(nitro)Destroy(nitro.gameObject);if(weapon)Destroy(weapon.gameObject);
            var garageWeapon=player.Weapons.GarageWeapon;
            var previousSounds=garageWeapon.fireSounds;
            var audioProbe=AudioSynthesis.Shot(0);
            garageWeapon.fireSounds=new[]{audioProbe};
            AudioManager.Instance.PlayShot(player.transform.position,garageWeapon);
            bool played=false;
            foreach(var source in AudioManager.Instance.GetComponentsInChildren<AudioSource>())played|=source.clip==audioProbe&&source.isPlaying;
            Check("authored weapon recording reaches pooled audio playback",played);
            garageWeapon.fireSounds=previousSounds;
            Destroy(audioProbe);
            float low=float.MaxValue,high=float.MinValue;
            for(int i=0;i<20;i++){float h=GeneratedWorld.HeightAt(new Vector3(-220+i*22,0,-250+i*24));low=Mathf.Min(low,h);high=Mathf.Max(high,h);}
            Check("generated terrain keeps gentle finite relief",WorldGenConfig.Finite(low)&&WorldGenConfig.Finite(high)&&high-low>3&&high-low<game.WorldConfig.terrainHeight);
        }
        static float SceneLuminance(string name)
        {
            // Hidden Windows players can skip the swapchain. Request a real URP render,
            // including post-processing, into a texture instead of reading that backbuffer.
            const int width=1600,height=900;
            var target=RenderTexture.GetTemporary(width,height,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(Camera.main,
                new UnityEngine.Rendering.RenderPipeline.StandardRequest{destination=target});
            var previous=RenderTexture.active;RenderTexture.active=target;
            var texture=new Texture2D(width,height,TextureFormat.RGB24,false);
            texture.ReadPixels(new Rect(0,0,width,height),0,0);texture.Apply();
            RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);
            string directory=Environment.GetEnvironmentVariable("MIA_CAPTURE_DIR");
            if(!string.IsNullOrEmpty(directory)){Directory.CreateDirectory(directory);File.WriteAllBytes(Path.Combine(directory,name+".png"),texture.EncodeToPNG());}
            var pixels=texture.GetPixels32();float total=0;int count=0;
            for(int y=height/4;y<height*3/4;y+=8)
                for(int x=width/4;x<width*3/4;x+=8)
                {var pixel=pixels[y*width+x];total+=(pixel.r*.2126f+pixel.g*.7152f+pixel.b*.0722f)/255f;count++;}
            Destroy(texture);return total/Mathf.Max(1,count);
        }
        void Check(string name, bool passed) { (passed ? checks : failures).Add((passed ? "PASS " : "FAIL ") + name); }
        void Capture(string name)
        {
            string directory = Environment.GetEnvironmentVariable("MIA_CAPTURE_DIR");
            if (string.IsNullOrEmpty(directory) || Application.isBatchMode) return;
            Directory.CreateDirectory(directory);
            ScreenCapture.CaptureScreenshot(Path.Combine(directory, name + ".png"));
        }
        void OnLog(string condition, string trace, LogType type) { if (type == LogType.Exception || type == LogType.Error) failures.Add("ERROR " + condition); }
    }
}
