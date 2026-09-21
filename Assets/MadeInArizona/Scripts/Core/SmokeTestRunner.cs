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
            Application.logMessageReceived += OnLog;
            yield return new WaitForSecondsRealtime(2);
            var game = GameManager.Instance;
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-miaGeneratedCampaignTest")>=0)
            {
                var campaignKeyboard=InputSystem.AddDevice<Keyboard>();
                game.WorldConfig.seed=173;game.WorldConfig.size=1600;
                yield return TestCampaign(campaignKeyboard,0);
                InputSystem.RemoveDevice(campaignKeyboard);
                FinishResults();yield break;
            }
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
            target.transform.position = game.Player.transform.position + Vector3.forward * 11 + Vector3.up * .85f;
            target.transform.localScale = Vector3.one * 2;
            var targetDamage = target.AddComponent<DestructionSystem>(); targetDamage.Configure(4, ExplosionKind.Propane, true, 30);
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
            game.Player.Repair(200);
            Check("field repair", game.Player.Damage.Health > health - 40);
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
            var nature=PandazoleNatureCatalog.Load();
            Check("Pandazole nature meshes and atlas packaged",nature&&nature.atlas&&nature.pines.Length>0&&nature.rocks.Length>0&&nature.grasses.Length>0);
            yield return TestNatureModelReview();
            yield return TestPresentationControls();
            Check("Arizona outline excludes rectangular corners",GeneratedWorld.Contains(game.World.PlayerSpawn)&&!GeneratedWorld.Contains(new Vector3(-790,0,-790))&&world.MapTexture.GetPixel(0,0).a<.1f);
            Check("generated terrain has collision chunks",world.GetComponentsInChildren<MeshCollider>().Length>=50);
            float low=1000,high=-1000;
            for(int i=0;i<20;i++){float h=GeneratedWorld.HeightAt(new Vector3(-220+i*22,0,-250+i*24));low=Mathf.Min(low,h);high=Mathf.Max(high,h);}
            Check("world contains real elevation variation",high-low>12);
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
            Check("road patrol population is bounded",director&&director.LivePatrols<=2);
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
            tuning.playerHealth=2;tuning.bloom=1.7f;tuning.chromatic=.23f;tuning.depthOfField=.5f;
            tuning.incomingDamage=0;DevTuning.Apply();
            game.Player.Damage.ApplyDamage(100,game.Player.transform.position,null);
            Check("live health capacity and zero incoming damage",Mathf.Approximately(game.Player.Damage.MaxHealth,max*2)&&Mathf.Approximately(game.Player.Damage.Health,max*2));
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
            Check("enemy rocket drop fits LT with limited ammo",game.Player.Weapons.FieldWeapon!=null&&game.Player.Weapons.FieldAmmo==4);
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
            Check("real projectiles defeat both vehicle waves",game.State==GameState.Won&&game.Mission.Kills>=6);
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
        void DestroyHostiles()
        {
            foreach (var enemy in new List<VehicleController>(VehicleController.Active)) {
                if (!enemy || enemy.IsPlayer || enemy.Damage.IsDead) continue;
                var ai = enemy.GetComponent<EnemyAI>(); if (ai && ai.IsFriendly) continue;
                enemy.Damage.ApplyDamage(100000, enemy.transform.position, GameManager.Instance.Player.gameObject);
            }
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
