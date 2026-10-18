using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering.Universal;

namespace MadeInArizona
{
    /// <summary>Opt-in native-player integration test. Runs only with -miaSmokeTest; uses isolated saves.</summary>
    public sealed class SmokeTestRunner : MonoBehaviour
    {
        public static bool Active => Array.IndexOf(Environment.GetCommandLineArgs(), "-miaSmokeTest") >= 0;
        public static bool UseGeneratedTerrainForSmoke { get; private set; }
        /// <summary>Seconds before a hung run is abandoned; an escort run on a larger map needs longer.</summary>
        static float TimeLimit => Array.Exists(Environment.GetCommandLineArgs(), a => a.StartsWith("-miaEscortSize=")) ? 720 : 240;
        readonly List<string> checks = new List<string>();
        readonly List<string> failures = new List<string>();
        readonly List<string> fixtureTrace = new List<string>();
        void Update()
        {
            if (Active && Time.realtimeSinceStartup > TimeLimit) { Debug.LogError("MIA_SMOKE_TIMEOUT: test did not finish; inspect earlier exceptions."); Application.Quit(2); }
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
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-miaDisplayReticleTest")>=0)
            { yield return TestDisplayAndReticle(); FinishResults(); yield break; }
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-miaWeaponDropTest")>=0)
            { TestWeaponDropsAndBazookaCone(); FinishResults(); yield break; }
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-miaTransmissionProgressTest")>=0)
            { yield return TestTransmissionAndProgressReset(); FinishResults(); yield break; }
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-miaShadowFilterTest")>=0)
            { yield return TestShadowFilters(); FinishResults(); yield break; }
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-miaHealthTest")>=0)
            { TestSegmentedHealth(); FinishResults(); yield break; }
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-miaCoopSnapshotTest")>=0)
            { yield return TestCoopSnapshotAndSmoke(); FinishResults(); yield break; }
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-miaFlailTest")>=0)
            { yield return TestWashingMachineFlail(); FinishResults(); yield break; }
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-miaReleaseFeaturesTest")>=0)
            { yield return TestReleaseFeatures(); FinishResults(); yield break; }
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-miaCoopLobbyHostTest")>=0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(),"-miaCoopLobbyGuestTest")>=0)
            {
                yield return TestCoopLobbyLive(Array.IndexOf(Environment.GetCommandLineArgs(),"-miaCoopLobbyHostTest")>=0);
                FinishResults(); yield break;
            }
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-miaMusicTest")>=0)
            { yield return TestSoundtrack(true); FinishResults(); yield break; }
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-miaMusicExistingTest")>=0)
            { yield return TestSoundtrack(false); FinishResults(); yield break; }
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-miaMountainTest")>=0)
            { yield return TestMountainWorld(); FinishResults(); yield break; }
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-miaGeneratedEnemyTest")>=0)
            { yield return TestGeneratedEnemyMovement(); FinishResults(); yield break; }
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-miaMinimapCapture")>=0)
            { yield return TestMinimapCapture(); FinishResults(); yield break; }
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-miaEnemyBalanceTest")>=0)
            { yield return TestEnemyBalance(); FinishResults(); yield break; }
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-miaWeaponAudioTest")>=0)
            { yield return TestImportedWeaponAudio(); FinishResults(); yield break; }
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-miaEngineAudioTest")>=0)
            { yield return TestEngineAudio(); FinishResults(); yield break; }
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-miaEscortTest")>=0)
            { yield return TestEscortRoute(); FinishResults(); yield break; }
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-miaAssetBehaviourTest")>=0)
            { yield return TestAssetBehaviour(); FinishResults(); yield break; }
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-miaEffectsRevisionTest")>=0)
            { yield return TestEffectsRevision(); FinishResults(); yield break; }
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-miaGeneratedCampaignTest")>=0)
            {
                var campaignKeyboard=InputSystem.AddDevice<Keyboard>();
                game.WorldConfig.seed=173;game.WorldConfig.size=1600;
                yield return TestCampaign(campaignKeyboard,0);
                InputSystem.RemoveDevice(campaignKeyboard);
                FinishResults();yield break;
            }
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-miaTiltWorld")>=0){var worldKeyboard=InputSystem.AddDevice<Keyboard>();yield return TiltProbe.World(worldKeyboard,Check);InputSystem.RemoveDevice(worldKeyboard);FinishResults();yield break;}
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-miaDeathTest")>=0)
            {
                game.StartMission(0);yield return new WaitForSecondsRealtime(1);
                game.Player.Damage.ApplyDamage(100000,game.Player.transform.position,null,true);
                yield return new WaitForSecondsRealtime(.3f);
                Check("player death plays a sequence before the debrief",game.Dying&&game.State==GameState.Playing&&Time.timeScale<.9f&&game.Player.Body.constraints==RigidbodyConstraints.None);
                yield return new WaitForSecondsRealtime(GameManager.DeathSequenceSeconds);
                Check("death sequence ends in the failure debrief at normal speed",!game.Dying&&game.State==GameState.Lost&&Mathf.Approximately(Time.timeScale,1));
                FinishResults();yield break;
            }
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-miaDriveProbe")>=0){yield return DriveProbe.Run(Check);FinishResults();yield break;}
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-miaGarageReview")>=0){yield return GarageReview.Run(Check,Capture);FinishResults();yield break;}
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-miaUiCapture")>=0)
            {
                // Visual review of the HUD, map and garage (needs a real window; run hidden, not in batch mode).
                game.StartCampaign(1243802513,2050);yield return new WaitUntil(()=>game.State==GameState.Playing);
                yield return new WaitForSecondsRealtime(2);
                var uiKeyboard=InputSystem.AddDevice<Keyboard>();
                InputSystem.QueueStateEvent(uiKeyboard,new KeyboardState(Key.W));yield return new WaitForSecondsRealtime(1.5f);
                InputSystem.QueueStateEvent(uiKeyboard,new KeyboardState());
                Capture("ui-hud-mission");yield return new WaitForSecondsRealtime(.6f);
                {
                    // Gamepad reticle at a HUD scale other than 1, where rotated HUD lines used to drift apart.
                    var reticlePad=InputSystem.AddDevice<Gamepad>();float oldScale=game.Save.settings.uiScale;game.Save.settings.uiScale=1.2f;
                    InputSystem.QueueStateEvent(reticlePad,new GamepadState{rightStick=new Vector2(.6f,.6f)});yield return new WaitForSecondsRealtime(.5f);
                    Capture("ui-gamepad-reticle");yield return new WaitForSecondsRealtime(.4f);
                    InputSystem.QueueStateEvent(reticlePad,new GamepadState());game.Save.settings.uiScale=oldScale;yield return null;InputSystem.RemoveDevice(reticlePad);
                }
                string dir=Environment.GetEnvironmentVariable("MIA_CAPTURE_DIR");
                if(!string.IsNullOrEmpty(dir)&&GeneratedWorld.Active)File.WriteAllBytes(Path.Combine(dir,"ui-map-texture.png"),GeneratedWorld.Active.MapTexture.EncodeToPNG());
                game.GetComponent<GameUI>().OpenWorldMap();yield return new WaitForSecondsRealtime(.6f);Capture("ui-world-map");yield return new WaitForSecondsRealtime(.6f);
                game.Resume();
                {
                    // The river from the gameplay camera at four points along its course.
                    var cam=CameraController.Instance;var carTarget=cam.Target;var focus=new GameObject("River review");cam.Target=focus.transform;
                    var world=GeneratedWorld.Active;float half=world.WorldBounds.size.x*.5f;
                    // Channel audit: the carved bed must sit below the water all along the course.
                    int dry=0;
                    for(float z=-half*.9f;z<half*.9f;z+=8)
                    {
                        Vector3 c=new Vector3(world.RiverCenterX(z),0,z);float bed=GeneratedWorld.HeightAt(c),level=world.RiverLevel(z);
                        if(GeneratedWorld.Contains(c)&&bed>level-.5f&&dry++<12)Debug.Log("MIA_RIVER_DRY z="+z.ToString("F0")+" bed="+bed.ToString("F1")+" level="+level.ToString("F1")+" "+world.RiverDebug(z));
                    }
                    Check("river bed stays below the water along its whole course ("+dry+" dry samples)",dry==0);
                    for(int r=0;r<4;r++)
                    {
                        float z=Mathf.Lerp(-half*.7f,half*.7f,r/3f);Vector3 p=new Vector3(world.RiverCenterX(z),0,z);p.y=GeneratedWorld.HeightAt(p);
                        game.Player.Body.position=p+new Vector3(120,20,0);focus.transform.position=p;cam.Snap();
                        yield return new WaitForSecondsRealtime(1.2f);Capture("ui-river-"+r);yield return new WaitForSecondsRealtime(.5f);
                    }
                    // Road bridges over the carved channel.
                    int shot=0;var bridges=new List<Transform>();
                    foreach(Transform t in world.GetComponentsInChildren<Transform>(true))if(t.name=="Salt River bridge")bridges.Add(t);
                    foreach(Transform t in bridges)
                    {
                        if(!t||shot>=2)continue;
                        var renderers=t.GetComponentsInChildren<Renderer>();if(renderers.Length==0)continue;
                        Bounds b=renderers[0].bounds;foreach(var r in renderers)b.Encapsulate(r.bounds);
                        focus.transform.position=b.center;cam.Snap();yield return new WaitForSecondsRealtime(1.2f);Capture("ui-bridge-"+shot++);yield return new WaitForSecondsRealtime(.5f);
                    }
                    cam.Target=carTarget;
                    // Close-up of the player's paint at the tightest gameplay zoom.
                    game.Player.Body.position=game.World.PlayerSpawn;game.Player.Body.linearVelocity=Vector3.zero;yield return new WaitForSecondsRealtime(1.5f);
                    var dev=DevTuning.Current;float zoom=dev.cameraZoom;dev.cameraZoom=8;cam.Snap();yield return new WaitForSecondsRealtime(1.2f);
                    Capture("ui-car-close");yield return new WaitForSecondsRealtime(.5f);dev.cameraZoom=zoom;
                    // Paint detail: the gameplay angle zoomed right in, then a low perspective view.
                    var ui=game.GetComponent<GameUI>();var view=Camera.main;ui.enabled=false;cam.enabled=false;Vector3 car=game.Player.transform.position+Vector3.up*.7f;
                    view.orthographicSize=3.2f;view.transform.position=car-view.transform.forward*80;yield return new WaitForSecondsRealtime(.6f);Capture("ui-car-paint-overhead");yield return new WaitForSecondsRealtime(.4f);
                    view.orthographic=false;view.fieldOfView=38;view.transform.position=car+new Vector3(4.6f,2.6f,-5.4f);view.transform.LookAt(car);
                    yield return new WaitForSecondsRealtime(.6f);Capture("ui-car-paint-side");yield return new WaitForSecondsRealtime(.4f);
                    view.orthographic=true;cam.enabled=true;ui.enabled=true;cam.Snap();
                    // Mine warning beacon (the player's own mine, so it never triggers on the car).
                    FieldOrdnance.PlaceMine(game.Player,WeaponRules.Find("mines"),0,game.Player.transform.position+game.Player.transform.forward*6+Vector3.up*.4f);
                    dev.cameraZoom=10;cam.Snap();yield return new WaitForSecondsRealtime(.9f);Capture("ui-mine-beacon");yield return new WaitForSecondsRealtime(.35f);dev.cameraZoom=zoom;cam.Snap();
                }
                game.ReturnToGarage();yield return new WaitForSecondsRealtime(1.5f);Capture("ui-garage");yield return new WaitForSecondsRealtime(.6f);
                // The far side of the garage orbit, looking out over the desert grounds.
                InputSystem.QueueStateEvent(uiKeyboard,new KeyboardState(Key.E));yield return new WaitForSecondsRealtime(1.8f);InputSystem.QueueStateEvent(uiKeyboard,new KeyboardState());
                yield return new WaitForSecondsRealtime(.5f);Capture("ui-garage-orbit");yield return new WaitForSecondsRealtime(.4f);
                {
                    // Parts & Tuning with one of the deferred-maintenance parts selected.
                    var garageUi=game.GetComponent<GameUI>();var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                    typeof(GameUI).GetField("station",flags)?.SetValue(garageUi,2);
                    typeof(GameUI).GetField("selectedPart",flags)?.SetValue(garageUi,Array.FindIndex(ContentCatalog.Parts,p=>p.id=="alignment"));
                    typeof(GameUI).GetField("listScroll",flags)?.SetValue(garageUi,new Vector2(0,Array.FindIndex(ContentCatalog.Parts,p=>p.id=="wheels")*81));
                    yield return new WaitForSecondsRealtime(.4f);Capture("ui-garage-parts");yield return new WaitForSecondsRealtime(.3f);
                    typeof(GameUI).GetField("station",flags)?.SetValue(garageUi,0);
                }
                InputSystem.RemoveDevice(uiKeyboard);FinishResults();yield break;
            }
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-miaSpawnTest")>=0)
            {
                if(Array.IndexOf(Environment.GetCommandLineArgs(),"-miaSpawnGenerated")>=0){game.StartCampaign(173,1600);yield return new WaitUntil(()=>game.State==GameState.Playing);}
                else game.StartMission(0);yield return new WaitForSecondsRealtime(1.5f);
                var before=new HashSet<VehicleController>(VehicleController.Active);
                typeof(MissionManager).GetMethod("SpawnWave",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(game.Mission,new object[]{6,game.Player.transform.position});
                int spawned=0,visible=0;float nearest=float.MaxValue;
                foreach(var v in VehicleController.Active){if(!v||before.Contains(v))continue;spawned++;nearest=Mathf.Min(nearest,Vector3.Distance(v.transform.position,game.Player.transform.position));var vp=Camera.main.WorldToViewportPoint(v.transform.position);if(vp.z>0&&vp.x>-.05f&&vp.x<1.05f&&vp.y>-.05f&&vp.y<1.05f)visible++;}
                Debug.Log("MIA_SPAWN spawned="+spawned+" visible="+visible+" nearest="+nearest.ToString("F1"));
                Check("wave spawned on the player appears off screen",spawned>0&&visible==0);
                if(GeneratedWorld.Active)Check("open-world crew starts beyond close combat range",spawned>0&&nearest>=60f);
                FinishResults();yield break;
            }
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-miaPowerLineTest")>=0)
            {
                yield return TestPowerLines();FinishResults();yield break;
            }
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-miaTownRevisionTest")>=0)
            {
                yield return TestTownRevision();FinishResults();yield break;
            }
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-miaHazardTest")>=0)
            {
                yield return TestWorldHazards();FinishResults();yield break;
            }
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-miaTiltProbe")>=0){game.StartMission(0);yield return new WaitForSecondsRealtime(1);var tiltKeyboard=InputSystem.AddDevice<Keyboard>();yield return TiltProbe.Run(tiltKeyboard,Check);InputSystem.RemoveDevice(tiltKeyboard);FinishResults();yield break;}
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-miaReachabilityTest")>=0){yield return TrailReview.Reachability(Check);FinishResults();yield break;}
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-miaTrailReview")>=0){yield return TrailReview.Run(Check);FinishResults();yield break;}
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-miaFrameTimingTest")>=0){yield return FrameTimingProbe.Run(Check);FinishResults();yield break;}
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-miaWorldTest")>=0){yield return TestWorldGeneration();yield break;}
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-miaPolishCapture")>=0)
            {
                game.Save.settings.perspectiveCamera=true;game.Save.settings.quality=3;game.ApplySettings();
                game.StartMission(0);QuietMissionHostiles();yield return new WaitForSecondsRealtime(1.2f);
                Vector3 aim=game.Player.transform.forward;aim.y=0;aim.Normalize();
                Vector3 impact=game.Player.transform.position+aim*12f+Vector3.up*.8f;
                ExplosionSystem.DetonateCone(impact,aim,11f,0,game.Player.gameObject);
                yield return new WaitForSecondsRealtime(.12f);
                bool coneVisible=false;foreach(var line in FindObjectsByType<LineRenderer>(FindObjectsSortMode.None))
                    if(line.name=="Bazooka blast cone outline"&&line.enabled)coneVisible=true;
                Check("bazooka blast cone renders while its damage area is active",coneVisible);
                SceneLuminance("polish-bazooka-cone");
                yield return new WaitForSecondsRealtime(.9f);
                ExplosionSystem.Detonate(impact+game.Player.transform.right*12f,7f,0,game.Player.gameObject,ExplosionKind.FuelTank);
                yield return new WaitForSecondsRealtime(.14f);SceneLuminance("polish-explosion");
                yield return new WaitForSecondsRealtime(12.2f);SceneLuminance("polish-scorch");
                VehicleController burnTarget=null;foreach(var car in VehicleController.Active)
                    if(car&&!car.IsPlayer&&!car.Damage.IsDead){burnTarget=car;break;}
                if(burnTarget)
                {
                    burnTarget.Body.position=game.Player.transform.position+game.Player.transform.right*7f+game.Player.transform.forward*7f;
                    burnTarget.transform.position=burnTarget.Body.position;Physics.SyncTransforms();
                    VehicleAfflictions.For(burnTarget).Ignite(1f,3f,game.Player.gameObject);
                }
                DevTuning.Current.cameraZoom=12f;CameraController.Instance.Snap();
                yield return new WaitForSecondsRealtime(.4f);
                Check("burned car retains a live fire effect",burnTarget&&burnTarget.Afflictions&&burnTarget.Afflictions.Burning);
                SceneLuminance("polish-burning-car");
                if(burnTarget)
                {
                    CameraController.Instance.enabled=false;
                    var detailCamera=Camera.main;
                    detailCamera.transform.position=burnTarget.transform.position+new Vector3(-6f,4f,-8f);
                    detailCamera.transform.LookAt(burnTarget.transform.position+Vector3.up*1.1f);
                    SceneLuminance("polish-burning-car-close",detailCamera);
                }
                FinishResults();yield break;
            }
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-miaSunsetCapture")>=0)
            {
                game.Save.settings.sunset=true;game.Save.settings.perspectiveCamera=true;game.Save.settings.quality=3;game.ApplySettings();
                game.StartCampaign(173,1600);yield return new WaitUntil(()=>game.State==GameState.Playing);
                QuietMissionHostiles();
                yield return new WaitForSecondsRealtime(4f);
                Check("sunset and perspective camera settled for capture",TimeOfDay.Settled&&TimeOfDay.Blend>.99f&&!Camera.main.orthographic);
                // A hidden player has a 512 px swapchain; render the actual gameplay camera into a full-size target.
                SceneLuminance("sunset-gameplay");
                var sunsetCamera=CameraController.Instance;var sunsetView=Camera.main;
                sunsetCamera.enabled=false;
                Vector3 car=game.Player.transform.position+Vector3.up*1.5f;
                sunsetView.transform.position=car+new Vector3(-24f,9f,-10f);
                sunsetView.transform.LookAt(car+Vector3.up*1.5f);
                SceneLuminance("sunset-scenic",sunsetView);
                yield return new WaitForSecondsRealtime(.8f);FinishResults();yield break;
            }
            if(Array.IndexOf(Environment.GetCommandLineArgs(), "-miaUltraTest")>=0) { game.Save.settings.quality=3;game.ApplySettings();game.ReturnToGarage();yield return new WaitForSecondsRealtime(.5f); }
            Check("garage startup", game && game.Player && game.State == GameState.Garage);
            Check("catalog: twenty-two vehicles and fifteen missions", ContentCatalog.Vehicles.Length == 22 && ContentCatalog.Missions.Length == 15);
            string[] datedCars={"ronin:1989","shift:1991","monsoon:2007","sonora:2001","dustback:2002","monument:1990","executive:1990","heat:1991","citizen:1993","roam4:2005","ae867:1985","espresso:1991","copperado:1988","contender:1970"};
            bool rosterYears=true;
            foreach(string entry in datedCars)
            {
                string[] parts=entry.Split(':');
                var car=Array.Find(ContentCatalog.Vehicles,v=>v.id==parts[0]);
                rosterYears&=car&&car.displayName.StartsWith(parts[1]+" ",StringComparison.Ordinal);
            }
            Check("all sheet vehicles retain their years in motor pool names",rosterYears);
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-miaRosterTest")>=0)
            { yield return TestVehicleRoster(); FinishResults(); yield break; }
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-miaSuzukiTest")>=0)
            { yield return TestSuzukiTreats(); FinishResults(); yield break; }
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-miaEquipmentTest")>=0)
            { yield return TestEquipmentGarage(); FinishResults(); yield break; }
            Check("six-way texture pairs and shader",Resources.Load<Texture2D>("SixWay/Fireball_P") && Resources.Load<Texture2D>("SixWay/Fireball_N") && Resources.Load<Texture2D>("SixWay/Smoke_P") && Resources.Load<Texture2D>("SixWay/Smoke_N") && Shader.Find("MadeInArizona/SixWaySmoke").isSupported);
            Check("Suzuki present", FindFirstObjectByType<SuzukiDog>() != null);
            Capture("01-garage");
            yield return new WaitForSecondsRealtime(.35f);
            SaveSystem.Save(game.Save);
            Check("save round trip", SaveSystem.Load().money == game.Save.money && File.Exists(SaveSystem.Path));
            bool devProbe = Array.IndexOf(Environment.GetCommandLineArgs(), "-miaDevTest") >= 0;
            game.StartMission(0);
            // The opening job spawns four hostiles about 18 m away that close in within two seconds. Left active they
            // killed the player before the weapon checks, or died beside it and magneted a drop into the empty field
            // slot. These checks cover the player's own driving, weapons, pickups and the mission state machine;
            // AI combat is covered by -miaCombatTest. Hostiles stay in place and remain damageable.
            QuietMissionHostiles();
            var trace = StartCoroutine(TraceFixture());
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
            if(devProbe)yield return TestDevTuning();
            var begin = game.Player.transform.position;
            // Reverse away from the first objective: driving forward reaches the parked suspect, which advances the job
            // and spawns the junkyard wave on top of the weapon checks. The gamepad check below covers driving forward.
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.S));
            yield return new WaitForSecondsRealtime(1.2f);
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
            QuietMissionHostiles(); // the junkyard wave; it is destroyed directly by the combat check below
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
            StopCoroutine(trace);
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
            // The regular smoke run must exercise a live hostile on generated terrain too. Flat mission fixtures
            // cannot catch a steering regression in GeneratedWorld navigation.
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-miaCombatTest") < 0 &&
                Array.IndexOf(Environment.GetCommandLineArgs(), "-miaCampaignTest") < 0)
            {
                UseGeneratedTerrainForSmoke = true;
                yield return TestGeneratedEnemyMovement();
                UseGeneratedTerrainForSmoke = false;
            }
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
            if (failures.Count > 0 && fixtureTrace.Count > 0) Debug.Log("MIA_SMOKE_TRACE\n" + string.Join("\n", fixtureTrace));
            Debug.Log("MIA_SMOKE_RESULTS\n" + result + "\n" + output);
            Application.Quit(failures.Count == 0 ? 0 : 1);
        }
        IEnumerator TestCoopLobbyLive(bool host)
        {
            UseGeneratedTerrainForSmoke = true;
            var game = GameManager.Instance;
            var coop = CoopSession.Instance;
            string codePath = Environment.GetEnvironmentVariable("MIA_COOP_CODE_PATH");
            if (string.IsNullOrEmpty(codePath)) { Check("live co-op test has a code path", false); yield break; }
            if (host)
            {
                game.WorldConfig.seed = 173;
                game.WorldConfig.size = 1600;
                coop.Host();
                float deadline = Time.unscaledTime + 60;
                while (string.IsNullOrEmpty(coop.JoinCode) && Time.unscaledTime < deadline) yield return null;
                Check("relay host opens garage lobby", coop.IsHost && coop.InLobby && !string.IsNullOrEmpty(coop.JoinCode));
                if (!coop.IsHost) yield break;
                game.SelectedMission = 0;
                string pendingCodePath = codePath + ".pending";
                File.WriteAllText(pendingCodePath, coop.JoinCode);
                File.Move(pendingCodePath, codePath);
                deadline = Time.unscaledTime + 60;
                while (coop.PlayerCount < 2 && Time.unscaledTime < deadline) yield return null;
                Check("guest joins host lobby", coop.PlayerCount == 2);
                if (coop.PlayerCount < 2) yield break;
                yield return new WaitForSecondsRealtime(1);
                Check("host cannot launch before all players ready", game.State == GameState.Garage && coop.CountdownRemaining == 0);
                coop.SetReady(true);
            }
            else
            {
                float deadline = Time.unscaledTime + 60;
                while (!File.Exists(codePath) && Time.unscaledTime < deadline) yield return null;
                if (!File.Exists(codePath)) { Check("guest receives relay code", false); yield break; }
                coop.Join(File.ReadAllText(codePath));
                deadline = Time.unscaledTime + 60;
                while ((!coop.InLobby || game.State != GameState.Garage) && Time.unscaledTime < deadline) yield return null;
                Check("guest enters own garage lobby", coop.InLobby && game.Player && game.Player.Body.isKinematic);
                if (!coop.InLobby) yield break;
                deadline = Time.unscaledTime + 15;
                while (!coop.LobbyRoster().Contains("PLAYER 2") && Time.unscaledTime < deadline) yield return null;
                Check("guest receives host lobby roster", coop.LobbyRoster().Contains("PLAYER 2"));
                var desiredCar = ContentCatalog.Vehicles[1];
                game.Save.unlockedMission = Mathf.Max(game.Save.unlockedMission, desiredCar.unlockMission);
                game.Save.money = Mathf.Max(game.Save.money, desiredCar.cost);
                bool selected = GarageManager.SelectVehicle(1, game.Save);
                if (selected) game.RefreshGarageVehicle();
                Debug.Log("MIA_COOP_GARAGE_TEST: selected=" + selected + " index=" + game.Save.selectedVehicle +
                    " preview=" + (game.Player && game.Player.Definition ? game.Player.Definition.id : "none") +
                    " expected=" + desiredCar.id + " state=" + game.State + " reason=" + GarageManager.LastMessage);
                Check("guest can choose a garage vehicle", selected && game.Save.selectedVehicle == 1 && game.Player &&
                    game.Player.Definition == ContentCatalog.Vehicles[1]);
                yield return new WaitForSecondsRealtime(1);
                coop.SetReady(true);
            }
            float launchDeadline = Time.unscaledTime + 60;
            bool sawCountdown = false;
            while (game.State != GameState.Playing && Time.unscaledTime < launchDeadline)
            {
                sawCountdown |= coop.CountdownRemaining > 0;
                yield return null;
            }
            Debug.Log("MIA_COOP_LOBBY_TEST: " + (host ? "host" : "guest") + " state=" + game.State +
                " mission=" + game.SelectedMission + " ready=" + coop.LocalReady +
                " roster=" + coop.LobbyRoster().Replace('\n', ';') + " status=" + coop.Status);
            Check("all ready starts five-second countdown", sawCountdown);
            Check("lobby launches selected mission for both players", game.State == GameState.Playing && game.SelectedMission == 0);
            if (game.State != GameState.Playing) yield break;
            float playerDeadline = Time.unscaledTime + 15;
            while ((!game.Player || VehicleController.Active.Count(v => v && v.IsPlayer) < 2) && Time.unscaledTime < playerDeadline) yield return null;
            Check("both player cars appear after launch", game.Player && VehicleController.Active.Count(v => v && v.IsPlayer) >= 2);
            if (host) Check("guest garage car reaches host spawn", VehicleController.Active.Any(v => v && v.IsPlayer &&
                v != game.Player && v.Definition == ContentCatalog.Vehicles[1]));
            Check("teammate has numbered waypoint identity", VehicleController.Active.Any(v => v && v.IsPlayer && v != game.Player && coop.PlayerSlotForCar(v) > 0));
            if (host) yield return new WaitForSecondsRealtime(3);
        }
        IEnumerator TestReleaseFeatures()
        {
            var game = GameManager.Instance;
            var limiter = Array.Find(ContentCatalog.Parts, p => p && p.id == "revlimiter");
            var loadout = new SaveData();
            loadout.ownedParts.Add("revlimiter"); loadout.installedParts.Add("revlimiter");
            Check("garage rev limiter installs on player vehicle", limiter && GarageManager.StatsFor(ContentCatalog.Vehicles[0], loadout).revLimiter);
            Check("harpoon and crossbow carry 25 shots", WeaponRules.MaxAmmo("harpoon") == 25 && WeaponRules.MaxAmmo("crossbow") == 25);
            var rewards = new SaveData();
            MissionManager.AwardCampaign(rewards, 0, 1200, 8, true, out int firstMoney, out int firstSalvage, out bool first);
            Check("campaign award advances guest save and grants first completion", first && rewards.completedMissions.Contains(0) &&
                rewards.unlockedMission == 1 && rewards.money == 1100 + firstMoney && rewards.salvage == firstSalvage && rewards.reputation == 100);
            MissionManager.AwardCampaign(rewards, 0, 1200, 8, true, out int replayMoney, out _, out bool replayFirst);
            Check("repeat co-op award uses replay rate", !replayFirst && replayMoney < firstMoney && rewards.reputation == 120);
            var otakuAtlas = Resources.Load<Texture2D>("Art/OtakuWraps");
            Check("Car Otaku eight-wrap atlas is packaged", otakuAtlas && otakuAtlas.width == 2048 && otakuAtlas.height == 1024);
            var wrapProbe = new GameObject("Otaku wrap probe");
            var wrapped = VehicleVisual.Build(ContentCatalog.Vehicles[0], wrapProbe.transform, true, EnemyFaction.CarOtaku);
            Check("Car Otaku cars wear both illustrated side decals", VehicleController.FindChild(wrapped, "Otaku mechanic wrap") &&
                VehicleController.FindChild(wrapped, "Otaku racer wrap"));
            Destroy(wrapProbe);
            game.Save.unlockedMission = 1;
            game.StartMission(0);
            QuietMissionHostiles();
            yield return new WaitForSecondsRealtime(.5f);
            int beams = 0;
            foreach (var light in game.Player.GetComponentsInChildren<Light>(true))
                if (light.name == "Left headlight beam" || light.name == "Right headlight beam") beams++;
            Check("cars have a beam at each headlight", beams == 2);
            var fractureProbe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fractureProbe.name = "Cell fracture outline probe";
            fractureProbe.transform.position = game.Player.transform.position + Vector3.up * 3;
            fractureProbe.transform.localScale = new Vector3(2, 2, 2);
            Bounds original = fractureProbe.GetComponent<Renderer>().bounds;
            ExplosionSystem.ScatterFracturedObject(fractureProbe.transform, original, 3, 8, Color.gray, Vector3.zero);
            var explosion = FindAnyObjectByType<ExplosionSystem>();
            int cellCount = 0;
            bool haveCellBounds = false;
            Bounds assembled = new Bounds();
            double cellVolume = 0;
            foreach (var mesh in explosion.GetComponentsInChildren<MeshFilter>())
            {
                if (!mesh.sharedMesh || !mesh.sharedMesh.name.StartsWith("Voronoi fracture cell")) continue;
                cellCount++;
                var pieceBounds = mesh.GetComponent<Renderer>().bounds;
                if (!haveCellBounds) { assembled = pieceBounds; haveCellBounds = true; }
                else assembled.Encapsulate(pieceBounds);
                var points = mesh.sharedMesh.vertices; var triangles = mesh.sharedMesh.triangles;
                for (int i = 0; i + 2 < triangles.Length; i += 3)
                    cellVolume += Vector3.Dot(points[triangles[i]], Vector3.Cross(points[triangles[i + 1]], points[triangles[i + 2]])) / 6.0;
            }
            Check("broken prop becomes cells within its original outline", cellCount >= 6 &&
                Vector3.Distance(assembled.min, original.min) < .03f && Vector3.Distance(assembled.max, original.max) < .03f &&
                Math.Abs(cellVolume - 8.0) < .25);
            Destroy(fractureProbe);
            yield return new WaitForSecondsRealtime(.18f);
            Check("fracture cells release into physics", explosion.GetComponentsInChildren<MeshFilter>().Any(mesh =>
                mesh.sharedMesh && mesh.sharedMesh.name.StartsWith("Voronoi fracture cell") &&
                mesh.GetComponent<Rigidbody>() && !mesh.GetComponent<Rigidbody>().isKinematic));
            var composite = new GameObject("Composite breakable prop probe");
            for (int side = -1; side <= 1; side += 2)
            {
                var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
                part.transform.SetParent(composite.transform, false);
                part.transform.localPosition = new Vector3(side * .75f, 0, 0);
            }
            bool builtComposite = MeshCellFracture.TryBuild(composite.transform, 8, out var compositeCells);
            double compositeVolume = 0;
            foreach (var cell in compositeCells)
            {
                var points = cell.mesh.vertices; var triangles = cell.mesh.triangles;
                for (int i = 0; i + 2 < triangles.Length; i += 3)
                    compositeVolume += Vector3.Dot(points[triangles[i]], Vector3.Cross(points[triangles[i + 1]], points[triangles[i + 2]])) / 6.0;
                Destroy(cell.mesh);
            }
            Check("multiple prop meshes fracture without filling gaps", builtComposite && compositeCells.Count >= 4 && Math.Abs(compositeVolume - 2.0) < .2);
            Destroy(composite);
            var rockProbe = new GameObject("Generated rock fracture probe");
            var rockShape = new SceneryMesh();
            rockShape.Rock(Vector3.zero, new Vector3(2, 1.4f, 1.6f), Color.gray, new System.Random(117), closed:true);
            var rockPart = new GameObject("Generated rock", typeof(MeshFilter), typeof(MeshRenderer));
            rockPart.transform.SetParent(rockProbe.transform, false);
            var rockMesh = rockShape.ToMesh("Closed generated rock");
            rockPart.GetComponent<MeshFilter>().sharedMesh = rockMesh;
            double originalRockVolume = 0;
            var rockPoints = rockMesh.vertices; var rockIndices = rockMesh.triangles;
            for (int i = 0; i + 2 < rockIndices.Length; i += 3)
                originalRockVolume += Vector3.Dot(rockPoints[rockIndices[i]], Vector3.Cross(rockPoints[rockIndices[i + 1]], rockPoints[rockIndices[i + 2]])) / 6.0;
            bool builtRock = MeshCellFracture.TryBuild(rockProbe.transform, 10, out var rockCells);
            double brokenRockVolume = 0;
            foreach (var cell in rockCells)
            {
                var points = cell.mesh.vertices; var triangles = cell.mesh.triangles;
                for (int i = 0; i + 2 < triangles.Length; i += 3)
                    brokenRockVolume += Vector3.Dot(points[triangles[i]], Vector3.Cross(points[triangles[i + 1]], points[triangles[i + 2]])) / 6.0;
                Destroy(cell.mesh);
            }
            Check("generated rock keeps its volume when split into cells", builtRock && rockCells.Count >= 6 &&
                Math.Abs(originalRockVolume) > .1 && Math.Abs(brokenRockVolume - originalRockVolume) < Math.Abs(originalRockVolume) * .1);
            Destroy(rockMesh); Destroy(rockProbe);
            game.CompleteMission(); game.ReturnToGarage();
            Check("garage defaults to the next unlocked mission after victory", game.SelectedMission == 1);
        }

        IEnumerator TestWashingMachineFlail()
        {
            var game = GameManager.Instance;
            Check("flail is sold in garage", WeaponRules.GarageWeapon("flail") && WeaponRules.Find("flail") && WeaponRules.ScrapCost("flail") > 0);
            Check("car travel does not count toward swing speed",
                WashingMachineFlail.RelativeSwingSpeed(Vector3.forward * 12f, Vector3.forward * 12f, 1f) < .01f &&
                Mathf.Abs(WashingMachineFlail.RelativeSwingSpeed(Vector3.forward * 17f, Vector3.forward * 12f, 1f) - 5f) < .01f);
            game.Save.selectedWeapon = "flail";
            game.StartMission(0);
            QuietMissionHostiles();
            yield return new WaitForSecondsRealtime(.5f);
            var player = game.Player;
            var flail = player ? player.GetComponent<WashingMachineFlail>() : null;
            Check("equipped car has washer and chain visual", flail && player.Weapons.GarageWeapon.id == "flail" &&
                player.transform.Find("Washing machine flail/Washer head") &&
                player.transform.Find("Washing machine flail/Chain link 1"));
            if (!flail) yield break;
            var target = new GameObject("Flail impact probe").AddComponent<VehicleController>();
            target.transform.position = player.transform.position + player.transform.forward * 5f;
            target.transform.rotation = player.transform.rotation;
            target.Initialize(ContentCatalog.Vehicles[0], VehicleStats.From(ContentCatalog.Vehicles[0]), false);
            target.Body.useGravity = false;
            target.Body.constraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotation;
            float before = target.Damage.Health;
            float peakPush = 0, maxReach = 0;
            bool chainPlayed = false;
            var audio = AudioManager.Instance;
            for (int i = 0; i < 45; i++)
            {
                flail.Swing(player.transform.forward, WeaponRules.Find("flail").damage);
                if (i == 0)
                    Check("washer launch plays a metal bang", audio && audio.GetComponentsInChildren<AudioSource>()
                        .Any(source => source.isPlaying && source.clip && source.clip.name == "Original • washer metal bang"));
                yield return new WaitForFixedUpdate();
                chainPlayed |= audio && audio.GetComponentsInChildren<AudioSource>()
                    .Any(source => source.isPlaying && source.clip && source.clip.name == "Original • loose chain rattle");
                peakPush = Mathf.Max(peakPush, Vector3.Dot(target.Body.linearVelocity, player.transform.forward));
                maxReach = Mathf.Max(maxReach, Vector3.Distance(flail.HeadPosition, player.transform.position + Vector3.up * 1.65f));
                if (i == 19)
                    Check("washer ejection deals half base swing damage", Mathf.Abs(before - target.Damage.Health -
                        WeaponRules.Find("flail").damage * .5f * DevTuning.Current.playerDamage) < 1f);
            }
            Check("held flail extends and damages an enemy in its swept path", flail.Extended && target.Damage.Health < before);
            Check("washer launch pushes enemy vehicles", peakPush > 2f);
            Check("moving chain plays its rattle", chainPlayed);
            Check("washer drags on the ground within the longer chain", flail.HeadGrounded && maxReach > 7.2f && maxReach <= 7.93f);
            var touch = new GameObject("Flail slow touch probe").AddComponent<VehicleController>();
            touch.transform.position = flail.HeadPosition;
            touch.Initialize(ContentCatalog.Vehicles[0], VehicleStats.From(ContentCatalog.Vehicles[0]), false);
            touch.Body.isKinematic = true;
            int slowContacts = 0;
            bool slowDamage = false;
            for (int i = 0; i < 30; i++)
            {
                Vector3 previous = flail.HeadPosition;
                Vector3 previousPlayer = player.transform.position;
                float health = touch.Damage.Health;
                flail.Swing(player.transform.forward, WeaponRules.Find("flail").damage);
                yield return new WaitForFixedUpdate();
                float speed = WashingMachineFlail.RelativeSwingSpeed(flail.HeadPosition - previous,
                    player.transform.position - previousPlayer, Time.fixedDeltaTime);
                if (speed < 4.8f && Vector3.Distance(touch.Hull.ClosestPoint(flail.HeadPosition), flail.HeadPosition) < .95f)
                { slowContacts++; if (touch.Damage.Health < health - .01f) slowDamage = true; }
            }
            Check("slow washer contact cannot deal swing damage", slowContacts >= 5 && !slowDamage);
            yield return new WaitForSecondsRealtime(.7f);
            Check("released flail retracts", !flail.Extended);
            Check("stowed chain is quiet", !audio || !audio.GetComponentsInChildren<AudioSource>()
                .Any(source => source.isPlaying && source.clip && source.clip.name == "Original • loose chain rattle"));
            flail.Swing(player.transform.forward, WeaponRules.Find("flail").damage);
            Check("new trigger pull launches again after reeling in", Vector3.Distance(flail.HeadPosition,
                player.transform.position + Vector3.up * 1.65f) < 1.1f);
            Destroy(target.gameObject);
            Destroy(touch.gameObject);
            yield return new WaitForFixedUpdate();
            Vector3 drive = Vector3.ProjectOnPlane(player.transform.forward, Vector3.up).normalized;
            Vector3 driveStart = player.transform.position;
            for (int i = 0; i < 35; i++)
            {
                player.Body.linearVelocity = drive * 34f;
                flail.Swing(drive, WeaponRules.Find("flail").damage);
                yield return new WaitForFixedUpdate();
            }
            float lead = Vector3.Dot(flail.HeadPosition - (player.transform.position + Vector3.up * 1.65f), drive);
            Check("washer keeps its swing reach while the car is moving fast",
                Vector3.Distance(player.transform.position, driveStart) > 12f && lead > 4f);
            flail.Release();
            Vector3 releaseStart = player.transform.position;
            for (int i = 0; i < 25; i++)
            {
                player.Body.linearVelocity = drive * 34f;
                yield return new WaitForFixedUpdate();
            }
            yield return new WaitForEndOfFrame();
            float releaseTravel = Vector3.Distance(player.transform.position, releaseStart);
            float stowError = Vector3.Distance(flail.HeadPosition, player.transform.position + Vector3.up * 1.65f +
                player.transform.forward * 1.05f);
            Check("released washer reels in at driving speed", releaseTravel > 8f && !flail.Extended && stowError < .3f);
        }

        IEnumerator TestCoopSnapshotAndSmoke()
        {
            var original = new CoopSession.VehiclePacket {
                id = 17, owner = 2, archetype = 7, faction = 3, failedDepth = 2,
                definition = "scarface", weapon = "skewer", fieldWeapon = "crossbow",
                player = true, friendly = false, position = new Vector3(123.5f, 2.25f, -456.75f),
                velocity = new Vector3(12, -1, 9), rotation = Quaternion.Euler(5, 120, 3),
                aim = new Vector3(.6f, .1f, .8f),
                health = 541.5f, maximum = 920, boost = 28.5f, rpm = 5400,
                throttle = .75f, yawRate = .3f, boosting = true, fieldAmmo = 99,
                beam = true, beamEnd = new Vector3(150, 3, -400), beamHeat = .6f, tow = 16,
                flail = true, flailHead = new Vector3(126, 2, -452)
            };
            using (var writer = new FastBufferWriter(512, Allocator.Temp))
            {
                CoopSession.WriteVehiclePacket(writer, original);
                Check("co-op vehicle snapshot fits unreliable delivery", writer.Length < 512 && writer.Length < 1264);
                using (var reader = new FastBufferReader(writer, Allocator.Temp))
                {
                    var copy = CoopSession.ReadVehiclePacket(reader);
                    Check("co-op vehicle snapshot preserves motion, loadout, health and effects",
                        copy.id == original.id && copy.owner == original.owner &&
                        copy.archetype == original.archetype && copy.faction == original.faction &&
                        copy.failedDepth == original.failedDepth && copy.definition == original.definition &&
                        copy.weapon == original.weapon && copy.fieldWeapon == original.fieldWeapon &&
                        copy.player == original.player && copy.friendly == original.friendly &&
                        copy.position == original.position && copy.velocity == original.velocity && copy.aim == original.aim &&
                        copy.rotation == original.rotation && copy.health == original.health &&
                        copy.maximum == original.maximum && copy.boost == original.boost &&
                        copy.rpm == original.rpm && copy.throttle == original.throttle &&
                        copy.yawRate == original.yawRate && copy.boosting == original.boosting &&
                        copy.fieldAmmo == original.fieldAmmo && copy.beam == original.beam &&
                        copy.beamEnd == original.beamEnd && copy.beamHeat == original.beamHeat &&
                        copy.tow == original.tow && copy.flail == original.flail && copy.flailHead == original.flailHead);
                }
            }
            ExplosionSystem.TireSmoke(new Vector3(0, 200, 0), Vector3.zero, 1, 1, 1);
            var effects = FindFirstObjectByType<ExplosionSystem>();
            int banks = 0;
            if (effects)
                foreach (var particle in effects.GetComponentsInChildren<ParticleSystem>())
                    if (particle.name == "Six-way rolling smoke" || particle.name == "Six-way tyre smoke")
                    {
                        var sheet = particle.textureSheetAnimation;
                        if (sheet.enabled && sheet.numTilesX == 8 && sheet.numTilesY == 8 &&
                            sheet.startFrame.mode == ParticleSystemCurveMode.TwoConstants &&
                            Mathf.Approximately(sheet.startFrame.constantMin, 0) &&
                            Mathf.Approximately(sheet.startFrame.constantMax, 63f/64f)) banks++;
                    }
            Check("both six-way smoke banks randomize across all 64 start frames", banks == 2);
            var preview = GameManager.Instance.Player;
            if (preview && preview.Damage)
            {
                preview.SetNetworkProxy();
                preview.ApplyNetworkMotion(preview.transform.position, preview.transform.rotation, Vector3.zero,
                    preview.Damage.Health, preview.Damage.MaxHealth, 1, 4500, 1, 0, true);
                yield return new WaitForSecondsRealtime(.15f);
                var glow = VehicleController.FindChild(preview.transform, "Nitro glow");
                Check("co-op proxy retains nitro and lights its exhaust", preview.Boosting &&
                    preview.GetComponent<NitroExhaust>() && glow && glow.GetComponent<Light>() && glow.GetComponent<Light>().enabled);
            }
            else Check("co-op proxy retains nitro and lights its exhaust", false);
        }
        IEnumerator TestEquipmentGarage()
        {
            var game=GameManager.Instance;
            var ui=FindFirstObjectByType<GameUI>();
            var car=game.Player;
            Check("garage opens on restored Dispatch tab",ui&&game.State==GameState.Garage&&ui.GarageStation==0);
            var save=game.Save;
            var vehicle=ContentCatalog.Vehicles[save.selectedVehicle];
            var stock=GarageManager.StatsFor(vehicle,save);
            var manuals=ContentCatalog.Vehicles.Select(EquipmentIllustrations.Manual).ToArray();
            Check("each car has a unique service manual diagram",manuals.All(texture=>texture&&texture.width==400&&texture.height==380)&&
                manuals.Distinct().Count()==ContentCatalog.Vehicles.Length);
            Check("factory and purchased parts have slot icons",EquipmentIllustrations.Icon(null,EquipmentSlot.Engine,vehicle.id)&&
                EquipmentIllustrations.Icon(GarageManager.FindPart("intake"),EquipmentSlot.Engine,vehicle.id));
            Capture("equipment-dispatch");
            if(!save.ownedParts.Contains("intake"))save.ownedParts.Add("intake");
            var pad=InputSystem.AddDevice<Gamepad>();
            InputSystem.QueueStateEvent(pad,new GamepadState().WithButton(GamepadButton.RightShoulder));
            yield return new WaitForSecondsRealtime(.08f);
            InputSystem.QueueStateEvent(pad,new GamepadState());yield return new WaitForSecondsRealtime(.06f);
            Check("controller opens restored Motor Pool tab",ui.GarageStation==1);
            Capture("equipment-motor-pool");
            InputSystem.QueueStateEvent(pad,new GamepadState().WithButton(GamepadButton.RightShoulder));
            yield return new WaitForSecondsRealtime(.08f);
            InputSystem.QueueStateEvent(pad,new GamepadState());yield return new WaitForSecondsRealtime(.06f);
            Check("controller opens Parts and Tuning with first slot focused",ui.GarageStation==2&&ui.EquipmentFocus==0);
            yield return new WaitForSecondsRealtime(.12f);
            Capture("equipment-service-manual");
            InputSystem.QueueStateEvent(pad,new GamepadState().WithButton(GamepadButton.South));
            yield return new WaitForSecondsRealtime(.08f);
            InputSystem.QueueStateEvent(pad,new GamepadState());yield return new WaitForSecondsRealtime(.06f);
            Check("controller A opens filtered part list",ui.EquipmentBrowsing);
            InputSystem.QueueStateEvent(pad,new GamepadState().WithButton(GamepadButton.South));
            yield return new WaitForSecondsRealtime(.08f);
            InputSystem.QueueStateEvent(pad,new GamepadState());yield return new WaitForSecondsRealtime(.08f);
            Check("controller A equips highlighted part",GarageManager.Installed(save,vehicle.id,EquipmentSlot.Engine)?.id=="intake"&&!ui.EquipmentBrowsing);
            Check("equipped part changes actual player horsepower",game.Player.Stats.horsepower>stock.horsepower);
            InputSystem.QueueStateEvent(pad,new GamepadState().WithButton(GamepadButton.West));
            yield return new WaitForSecondsRealtime(.08f);
            InputSystem.QueueStateEvent(pad,new GamepadState());yield return new WaitForSecondsRealtime(.06f);
            Check("controller X restores factory equipment",GarageManager.Installed(save,vehicle.id,EquipmentSlot.Engine)==null&&
                Mathf.Approximately(game.Player.Stats.horsepower,stock.horsepower));
            string[] accessoryIds={"light_bar","tire_spikes","drift_n2o","magnetic_collector"};
            for(int i=0;i<4;i++)
            {
                save.ownedParts.Add(accessoryIds[i]);
                GarageManager.TryEquip(GarageManager.FindPart(accessoryIds[i]),save,EquipmentSlot.Accessory,i,false);
            }
            game.Player.RefreshLocalDamageProfiles();
            var mounts=game.Player.GetComponent<VehicleAccessoryVisuals>();
            var lightMount=Array.Find(game.Player.Visual.GetComponentsInChildren<Transform>(),t=>t.name=="Light Bar Accessory");
            var spikeMount=Array.Find(game.Player.Visual.GetComponentsInChildren<Transform>(),t=>t.name=="Wheel Spikes Accessory");
            Check("four shared accessory slots activate distinct effects",game.Player.Stats.lightBar&&
                game.Player.Stats.spikeDamage>0&&game.Player.Stats.driftNitroRegen>0&&
                game.Player.Stats.pickupRadiusMultiplier>1&&mounts&&lightMount&&spikeMount);
            Check("equipment layer failure disables accessory benefits",
                GarageManager.DamageProfiles(vehicle,save)[2].driftNitroRegen==0&&
                GarageManager.DamageProfiles(vehicle,save)[2].pickupRadiusMultiplier==1);
            for(int i=0;i<4;i++)GarageManager.Unequip(save,EquipmentSlot.Accessory,i,false);
            game.Player.RefreshLocalDamageProfiles();
            InputSystem.QueueStateEvent(pad,new GamepadState().WithButton(GamepadButton.South));
            yield return new WaitForSecondsRealtime(.08f);
            InputSystem.QueueStateEvent(pad,new GamepadState());yield return new WaitForSecondsRealtime(.06f);
            Check("part list reopens for mouse drag",ui.EquipmentBrowsing&&ui.EquipmentFocus==0);
            var mouse=InputSystem.AddDevice<Mouse>();
            float scale=Mathf.Max(.45f,Mathf.Min(Screen.width/1440f,Screen.height/900f)*Mathf.Clamp(save.settings.uiScale,.85f,1.2f));
            float guiWidth=Screen.width/scale;
            Vector2 Source()=>new Vector2((guiWidth-500+18+75)*scale,Screen.height-(169+46+26)*scale);
            Vector2 Target()=>new Vector2((52+100)*scale,Screen.height-(169+93+45)*scale);
            InputSystem.QueueStateEvent(mouse,new MouseState{position=Source()});yield return null;
            InputSystem.QueueStateEvent(mouse,new MouseState{position=Source()}.WithButton(MouseButton.Left));yield return null;
            InputSystem.QueueStateEvent(mouse,new MouseState{position=Target()}.WithButton(MouseButton.Left));yield return null;
            InputSystem.QueueStateEvent(mouse,new MouseState{position=Target()});yield return null;yield return null;
            Check("mouse drag uses the same equipment installation",GarageManager.Installed(save,vehicle.id,EquipmentSlot.Engine)?.id=="intake");
            InputSystem.RemoveDevice(mouse);
            Vector3 before=game.Player.transform.position;
            InputSystem.QueueStateEvent(pad,new GamepadState{leftStick=Vector2.up,rightTrigger=1});
            yield return new WaitForSecondsRealtime(.25f);
            Check("garage blocks driving and combat input",Vector3.Distance(before,game.Player.transform.position)<.01f&&
                game.Player.Body.isKinematic);
            InputSystem.QueueStateEvent(pad,new GamepadState());InputSystem.RemoveDevice(pad);
        }
        void TestSegmentedHealth()
        {
            ContentCatalog.EnsureLoaded();
            Check("part asset durability matches catalog defaults",ContentCatalog.PartHealthAssetsAgree);
            Check("all equipment uses mass times durability for HP",ContentCatalog.Parts.Length>=80 &&
                Array.TrueForAll(ContentCatalog.Parts,p=>Mathf.Abs(HealthRules.PartHealth(p)-Mathf.Max(0,p.mass)*DevTuning.Current.partHpPerKg*p.partDurability*.01f)<.001f));
            Check("negative mass parts add no HP",Array.TrueForAll(ContentCatalog.Parts,p=>p.mass>=0||HealthRules.PartHealth(p)==0));
            bool stockTotals=true,profileFloors=true,profileMass=true,profileHealth=true;
            foreach(var car in ContentCatalog.Vehicles)
            {
                var stock=GarageManager.StatsFor(car,new SaveData());
                var playerStock=GarageManager.StatsFor(car,null);
                stockTotals&=Mathf.Approximately(stock.maxHealth,car.maxHealth)&&Mathf.Approximately(stock.healthShares[0],.5f);
                var loadout=new SaveData();
                foreach(var part in ContentCatalog.Parts)
                {
                    if(!GarageManager.Compatible(part,car))continue;
                    if(part.category!="Armor")loadout.installedParts.RemoveAll(id=>Array.Exists(ContentCatalog.Parts,p=>p.id==id&&p.category==part.category));
                    loadout.ownedParts.Add(part.id);loadout.installedParts.Add(part.id);
                }
                var profiles=GarageManager.DamageProfiles(car,loadout);
                for(int depth=1;depth<profiles.Length;depth++)
                {
                    var s=profiles[depth];var whole=profiles[0];
                    profileMass&=Mathf.Approximately(s.mass,whole.mass);
                    profileHealth&=Mathf.Approximately(s.maxHealth,whole.maxHealth);
                    profileFloors&=s.horsepower+.001f>=Mathf.Min(playerStock.horsepower,whole.horsepower) &&
                        s.torque+.001f>=Mathf.Min(playerStock.torque,whole.torque) && s.grip+.001f>=Mathf.Min(playerStock.grip,whole.grip) &&
                        s.turnSpeed+.001f>=Mathf.Min(car.turnSpeed,whole.turnSpeed) && s.maxSpeed+.001f>=Mathf.Min(car.maxSpeed,whole.maxSpeed) &&
                        s.suspensionTravel+.001f>=Mathf.Min(car.suspensionTravel,whole.suspensionTravel) &&
                        s.damping+.001f>=Mathf.Min(car.damping,whole.damping);
                }
            }
            Check("stock vehicle HP and exterior remain unchanged",stockTotals);
            Check("every damaged profile stays above its stock floor",profileFloors);
            Check("mass and HP are fixed across damage profiles",profileMass&&profileHealth);
            var shift=Array.Find(ContentCatalog.Vehicles,v=>v.id=="shift");
            var turbo=Array.Find(ContentCatalog.Parts,p=>p.id=="turbo");
            var engineOnly=new SaveData();engineOnly.ownedParts.Add(turbo.id);engineOnly.installedParts.Add(turbo.id);
            var engineProfiles=GarageManager.DamageProfiles(shift,engineOnly);
            Check("engine part power survives every layer failure",Array.TrueForAll(engineProfiles,p=>Mathf.Approximately(p.horsepower,engineProfiles[0].horsepower)));
            var armor=Array.Find(ContentCatalog.Parts,p=>p.id=="cage");
            var bumpers=Array.Find(ContentCatalog.Parts,p=>p.id=="bumpers");
            var ronin=Array.Find(ContentCatalog.Vehicles,v=>v.id=="ronin");
            var armored=new SaveData();
            armored.ownedParts.Add(armor.id);armored.ownedParts.Add(bumpers.id);
            armored.installedParts.Add(armor.id);armored.installedParts.Add(bumpers.id);
            var armoredStats=GarageManager.StatsFor(ronin,armored);
            Check("legacy Body collision equips one part and preserves purchases",
                GarageManager.Installed(armored,ronin.id,EquipmentSlot.Body)==bumpers&&
                armored.ownedParts.Contains(armor.id)&&armored.ownedParts.Contains(bumpers.id)&&
                Mathf.Abs(armoredStats.maxHealth*armoredStats.healthShares[1]-HealthRules.PartHealth(bumpers))<.01f);
            var armorSlots=new SaveData();
            armorSlots.ownedParts.Add(armor.id);armorSlots.ownedParts.Add(bumpers.id);
            GarageManager.TogglePart(armor,armorSlots);GarageManager.TogglePart(bumpers,armorSlots);
            Check("Body slot replaces part without losing ownership",
                GarageManager.Installed(armorSlots,ronin.id,EquipmentSlot.Body)==bumpers&&
                armorSlots.ownedParts.Contains(armor.id)&&armorSlots.ownedParts.Contains(bumpers.id));

            var obj=new GameObject("Segment health test probe");
            var owner=obj.AddComponent<VehicleController>();
            var damage=obj.AddComponent<VehicleDamage>();
            damage.Initialize(owner,100,new[]{.3f,.1f,.1f,.2f,.1f,.2f});
            damage.SetNetworkHealth(60.5f,100);
            Check("never failed Armor stays active below 25%",damage.FailedDepth==0);
            damage.SetNetworkHealth(60,100);
            Check("Armor fails at exactly zero",damage.FailedDepth==1);
            damage.Repair(2.4f);
            Check("Armor stays failed below the recovery threshold",damage.FailedDepth==1);
            damage.Repair(.1f);
            Check("Armor recovers at 25%",damage.FailedDepth==0);
            bool stable=true;
            for(int cycle=0;cycle<10;cycle++)
            {
                damage.SetNetworkHealth(60,100);
                stable&=damage.FailedDepth==1;
                damage.Repair(2.5f);
                stable&=damage.FailedDepth==0;
            }
            Check("ten fail and repair cycles do not stack damage",stable);
            damage.SetDurabilityMultiplier(5);
            damage.SetNetworkHealth(60,100);
            damage.Reconfigure(200,new[]{.4f,.1f,.1f,.1f,.1f,.2f});
            Check("layout changes keep the reached layer",Mathf.Approximately(damage.MaxHealth,200)&&Mathf.Approximately(damage.Health,100)&&damage.FailedDepth==1);
            damage.SetDurabilityMultiplier(10);
            Check("uniform capacity scaling keeps segment fill",Mathf.Approximately(damage.MaxHealth,400)&&Mathf.Approximately(damage.Health,200)&&damage.FailedDepth==1);
            damage.SetNetworkHealth(19,100);
            Check("damage crosses all layers and reaches engine",damage.FailedDepth==4&&damage.EngineReached);
            damage.SetNetworkHealth(0,100);
            damage.Repair(20);
            Check("dead vehicles do not revive from repair",damage.IsDead&&damage.Health==0);
            Destroy(obj);
        }
        /// <summary>Exercise hostile steering and real vehicle physics on a generated map, not the flat combat arena.</summary>
        IEnumerator TestGeneratedEnemyMovement()
        {
            var game = GameManager.Instance;
            game.StartCampaign(173, 1600);
            float deadline = Time.realtimeSinceStartup + 80;
            while ((game.State != GameState.Playing || !GeneratedWorld.Active) && Time.realtimeSinceStartup < deadline)
                yield return null;
            var world = GeneratedWorld.Active;
            Check("hostile movement test generated terrain", world && world.LandformCount >= 8 && game.IsPlaying);
            if (!world || !game.IsPlaying) yield break;

            QuietMissionHostiles();
            var player = game.Player;
            Vector3 goal = world.ClearOfObstacles(game.World.PlayerSpawn, 6);
            goal.y = GeneratedWorld.HeightAt(goal) + 1.1f;
            Teleport(goal);
            player.Body.isKinematic = true;
            Vector3 spawn = Vector3.zero;
            bool found = false;
            for (int radius = 30; radius <= 60 && !found; radius += 10)
                for (int angle = 0; angle < 24 && !found; angle++)
                {
                    float bearing = angle * Mathf.PI * 2 / 24;
                    Vector3 point = goal + new Vector3(Mathf.Cos(bearing), 0, Mathf.Sin(bearing)) * radius;
                    point = world.ClearOfObstacles(point, 6);
                    point.y = GeneratedWorld.HeightAt(point) + 1.1f;
                    if (!GeneratedWorld.Contains(point) || Vector3.Distance(point, goal) < 26 ||
                        !world.DirectDrivable(point, goal)) continue;
                    spawn = point;
                    found = true;
                }
            Check("generated terrain offers a clear hostile approach", found);
            if (!found) yield break;

            var enemy = SpawnManager.Spawn(spawn, 0, player);
            Check("hostile spawns on generated terrain", enemy && enemy.GetComponent<EnemyAI>());
            if (!enemy) yield break;
            var ai = enemy.GetComponent<EnemyAI>();
            ai.UseDestination = true;
            ai.Destination = goal;
            Vector3 start = enemy.transform.position;
            float elapsed = 0, peakSpeed = 0;
            while (elapsed < 2.5f && enemy && !enemy.Damage.IsDead && game.IsPlaying)
            {
                yield return null;
                elapsed += Time.deltaTime;
                Vector3 velocity = enemy.Body.linearVelocity;
                peakSpeed = Mathf.Max(peakSpeed, new Vector2(velocity.x, velocity.z).magnitude);
                player.Damage.Repair(1000 * Time.deltaTime);
            }
            Vector3 delta = enemy ? enemy.transform.position - start : Vector3.zero;
            float travelled = new Vector2(delta.x, delta.z).magnitude;
            Debug.Log("MIA_GENERATED_ENEMY: start=" + start.ToString("F1") + " goal=" + goal.ToString("F1") +
                " travelled=" + travelled.ToString("F1") + " peakSpeed=" + peakSpeed.ToString("F1") +
                " detouring=" + (ai && ai.Detouring));
            Check("hostile drives across clear generated terrain", travelled > 2 && peakSpeed > 2 && !ai.Detouring);
        }
        IEnumerator TestMountainWorld()
        {
            var game=GameManager.Instance;
            foreach(int mapSize in new[]{1600,3200,4800})
            {
                game.StartCampaign(173,mapSize);
                yield return new WaitUntil(()=>game.State==GameState.Playing&&GeneratedWorld.Active&&Mathf.Approximately(GeneratedWorld.Active.WorldBounds.size.x,mapSize));
                var world=GeneratedWorld.Active;
                string issue=world.LandformIssue();
                Debug.Log("MIA_MOUNTAINS: size="+mapSize+" landforms="+world.LandformCount+" ridges="+world.RidgeCount+" cover="+world.CoverCount+
                    " boundary="+world.BoundaryRockCount+" rubble="+world.BaseRockCount+" barriers="+world.MountainBarrierCount+" issue="+(issue??"none"));
                Check(mapSize+" m mountain ring and route placement",issue==null&&world.BoundaryRockCount>=80&&
                    world.MountainBarrierCount>=world.BoundaryRockCount-8&&world.BaseRockCount>=world.BoundaryRockCount/2);
                Check(mapSize+" m retains interior impassable formations and cover",world.LandformCount>=8&&world.CoverCount>=20);
                // Interior ridges may have car-sized passes; nothing built may clog them. (The boundary seal is in LandformIssue.)
                var landformsRoot=world.transform.Find("Landforms and cover");int blocked=0;float narrowest=float.MaxValue;
                for(int i=0;i<world.PassPoints.Count;i++)
                {
                    narrowest=Mathf.Min(narrowest,world.PassWidths[i]);
                    Vector3 mid=world.PassPoints[i];mid.y=GeneratedWorld.HeightAt(mid)+1.6f;
                    foreach(var hit in Physics.OverlapBox(mid,new Vector3(2,1.2f,2),Quaternion.identity,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
                        if(landformsRoot&&hit.transform.IsChildOf(landformsRoot)){blocked++;Debug.Log("MIA_PASS_BLOCKED: "+mid.ToString("0")+" by "+hit.name);break;}
                }
                Debug.Log("MIA_PASSES: size="+mapSize+" passes="+world.PassPoints.Count+" narrowest="+(world.PassPoints.Count>0?narrowest.ToString("0.0"):"-")+" blocked="+blocked);
                if(mapSize>=1600)Check(mapSize+" m ridges have open, car-sized passes",world.PassPoints.Count>0&&narrowest>=7.9f&&blocked==0);
                // Natural arches: open ground under the span, and rock overhead.
                int drivable=0;
                foreach(var arch in world.ArchPoints)
                {
                    Vector3 under=arch;under.y=GeneratedWorld.HeightAt(under);
                    bool clear=true;
                    foreach(var hit in Physics.OverlapBox(under+Vector3.up*1.6f,new Vector3(1.5f,1.2f,1.5f),Quaternion.identity,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
                        if(landformsRoot&&hit.transform.IsChildOf(landformsRoot)){clear=false;Debug.Log("MIA_ARCH_BLOCKED: "+under.ToString("0")+" by "+hit.name);break;}
                    bool roofed=Physics.Raycast(under+Vector3.up*3,Vector3.up,out var roof,60,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)&&landformsRoot&&roof.transform.IsChildOf(landformsRoot);
                    if(clear&&roofed)drivable++;else Debug.Log("MIA_ARCH: "+under.ToString("0")+" clear="+clear+" roofed="+roofed);
                }
                Debug.Log("MIA_ARCHES: size="+mapSize+" arches="+world.ArchPoints.Count+" drivable="+drivable);
                if(mapSize>=1600)Check(mapSize+" m has natural arches a car can drive under",world.ArchPoints.Count>0&&drivable==world.ArchPoints.Count);
                else Check(mapSize+" m arches (if any) can be driven under",drivable==world.ArchPoints.Count);
                if(mapSize!=1600)continue;
                var camera=CameraController.Instance;var oldTarget=camera.Target;
                var focus=new GameObject("Mountain review focus");var ui=game.GetComponent<GameUI>();bool oldUi=ui.enabled;ui.enabled=false;
                game.Pause();
                Vector3 mountain=world.LandformCenter(0,out var kind,out float reach);
                focus.transform.position=mountain;camera.Target=focus.transform;camera.Snap();
                yield return new WaitForSecondsRealtime(.3f);SceneLuminance("mountain-interior");
                focus.transform.position=new Vector3(0,0,world.WorldBounds.size.x*.47f);
                focus.transform.position=new Vector3(0,GeneratedWorld.HeightAt(focus.transform.position),focus.transform.position.z);
                camera.Snap();yield return new WaitForSecondsRealtime(.3f);SceneLuminance("mountain-boundary");
                // Mountain chains with the car parked alongside for scale, at gameplay zoom and pulled back.
                var tuning=DevTuning.Current;float oldZoom=tuning.cameraZoom;
                for(int i=0;i<world.LandformEntries;i++)
                {
                    Vector3 chain=world.LandformCenter(i,out var chainKind,out float chainReach);
                    if(chainKind!=GeneratedWorld.LandformKind.Cliff)continue;
                    Vector3 away=new Vector3(-chain.x,0,-chain.z).normalized;
                    yield return ReviewBeside(chain+away*30,"mountain-chain-interior",camera,tuning);
                    break;
                }
                if(world.PassPoints.Count>0)yield return ReviewBeside(world.PassPoints[0],"mountain-pass",camera,tuning);
                if(world.ArchPoints.Count>0)yield return ReviewBeside(world.ArchPoints[0],"monument-arch",camera,tuning);
                foreach(var wanted in new[]{GeneratedWorld.LandformKind.Mesa,GeneratedWorld.LandformKind.Butte})
                    for(int i=0;i<world.LandformEntries;i++)
                    {
                        Vector3 at=world.LandformCenter(i,out var formation,out float formationReach);
                        if(formation!=wanted)continue;
                        Vector3 away=new Vector3(-at.x,0,-at.z).normalized;
                        yield return ReviewBeside(world.ClearOfObstacles(at+away*(formationReach+10),4),"monument-"+wanted.ToString().ToLowerInvariant(),camera,tuning);
                        break;
                    }
                Transform edgeRock=null;
                foreach(var t in world.GetComponentsInChildren<Transform>())
                    if(t.name=="Mountain rock"&&t.parent&&t.parent.name=="Boundary mountain chain"&&(!edgeRock||t.position.z>edgeRock.position.z))edgeRock=t;
                if(edgeRock)
                {
                    yield return ReviewBeside(edgeRock.position+new Vector3(-edgeRock.position.x,0,-edgeRock.position.z).normalized*36,"mountain-chain-boundary",camera,tuning);
                    // Straight down over the corner: where terrain and rim actually are, without perspective ambiguity.
                    var top=new GameObject("Top-down review camera").AddComponent<Camera>();
                    top.orthographic=true;top.orthographicSize=90;top.nearClipPlane=1;top.farClipPlane=1000;
                    top.transform.SetPositionAndRotation(new Vector3(edgeRock.position.x,400,edgeRock.position.z-40),Quaternion.Euler(90,0,0));
                    SceneLuminance("mountain-chain-boundary-topdown",top);Destroy(top.gameObject);
                }
                tuning.cameraZoom=oldZoom;
                camera.Target=oldTarget;camera.Snap();ui.enabled=oldUi;game.Resume();Destroy(focus);
            }
        }
        /// <summary>Parks the player at <paramref name="spot"/> and captures it at gameplay zoom and at the widest zoom.</summary>
        IEnumerator ReviewBeside(Vector3 spot,string name,CameraController camera,DevTuning tuning)
        {
            spot.y=GeneratedWorld.HeightAt(spot)+1;Teleport(spot);
            var player=GameManager.Instance.Player;camera.Target=player.transform;
            float zoom=tuning.cameraZoom;
            camera.Snap();yield return new WaitForSecondsRealtime(.3f);SceneLuminance(name+"-gameplay");
            tuning.cameraZoom=40;camera.Snap();yield return new WaitForSecondsRealtime(.3f);SceneLuminance(name+"-wide");
            tuning.cameraZoom=zoom;
        }
        IEnumerator TestMinimapCapture()
        {
            var game=GameManager.Instance;
            game.StartCampaign(173,1600);
            yield return new WaitUntil(()=>game.State==GameState.Playing&&GeneratedWorld.Active);
            string directory=Environment.GetEnvironmentVariable("MIA_CAPTURE_DIR");
            if(string.IsNullOrEmpty(directory)){Check("minimap capture directory supplied",false);yield break;}
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory,"mountain-minimap-texture.png"),GeneratedWorld.Active.MapTexture.EncodeToPNG());
            Screen.SetResolution(512,512,FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(.7f);
            yield return new WaitForEndOfFrame();
            Capture("mountain-minimap-screen");
            yield return new WaitForSecondsRealtime(.7f);
            Check("current generated map exported for minimap review",File.Exists(Path.Combine(directory,"mountain-minimap-texture.png")));
            Debug.Log("MIA_MINIMAP_CAPTURE: screen="+Screen.width+"x"+Screen.height+" screenshot="+
                File.Exists(Path.Combine(directory,"mountain-minimap-screen.png")));
        }
        IEnumerator TestWorldHazards()
        {
            var game=GameManager.Instance;
            game.StartCampaign(173,1600);
            float deadline=Time.realtimeSinceStartup+80;
            while((game.State!=GameState.Playing||!GeneratedWorld.Active)&&Time.realtimeSinceStartup<deadline)yield return null;
            var world=GeneratedWorld.Active;
            Check("hazard test generated a playable world",world&&game.State==GameState.Playing);
            if(!world)yield break;
            yield return new WaitForSecondsRealtime(.4f);
            QuietMissionHostiles();
            var hazards=world.GetComponentsInChildren<WorldHazard>();
            var first=new WorldHazard[6];var counts=new int[6];bool breakable=true;
            foreach(var hazard in hazards)
            {
                int index=(int)hazard.Kind;counts[index]++;
                if(!first[index])first[index]=hazard;
                var prop=hazard.GetComponent<DestructionSystem>();
                if(!prop||!prop.Explosive||hazard.GetComponentsInChildren<Collider>().Length==0)breakable=false;
            }
            for(int i=0;i<first.Length;i++)Check("generated "+((WorldHazardKind)i)+" hazards",counts[i]>0);
            Check("all generated hazards have colliders and explosive damage",breakable);
            Debug.Log("MIA_HAZARDS tanker="+counts[0]+" pumps="+counts[1]+" mining="+counts[2]+" fireworks="+counts[3]+" chemical="+counts[4]+" transformer="+counts[5]);
            var camera=Camera.main;
            if(camera&&CameraController.Instance)
            {
                CameraController.Instance.enabled=false;
                for(int i=0;i<first.Length;i++)
                {
                    var hazard=first[i];if(!hazard)continue;
                    float distance=i==0?17:10;
                    camera.transform.position=hazard.transform.position-hazard.transform.forward*distance+hazard.transform.right*distance*.42f+Vector3.up*distance*.55f;
                    camera.transform.LookAt(hazard.transform.position+Vector3.up*(i==0?1.8f:1.25f));
                    yield return new WaitForSecondsRealtime(.12f);
                    SceneLuminance("hazard-"+hazard.Kind,camera);
                }
                CameraController.Instance.enabled=true;
            }
            var mining=first[(int)WorldHazardKind.BlastingCrates];
            if(mining)
            {
                var prop=mining.GetComponent<DestructionSystem>();
                prop.ApplyDamage(prop.Health+5,mining.transform.position+Vector3.up,null);
                Check("mining charges arm a visible fuse before destruction",mining.IsArmed&&!prop.IsDestroyed);
                yield return new WaitForSecondsRealtime(1.1f);
                Check("mining fuse detonates",!prop||prop.IsDestroyed);
            }
            var tanker=first[(int)WorldHazardKind.FuelTanker];
            if(tanker)
            {
                var prop=tanker.GetComponent<DestructionSystem>();
                prop.ApplyDamage(prop.Health+5,tanker.transform.position+Vector3.up*2,null);
                Check("fuel tanker leaks before detonation",tanker.IsArmed&&!prop.IsDestroyed&&tanker.DetonateAt>Time.time+1);
                yield return new WaitForSecondsRealtime(.45f);
                if(camera&&CameraController.Instance)
                {
                    CameraController.Instance.enabled=false;
                    camera.transform.position=tanker.transform.position-tanker.transform.forward*15+tanker.transform.right*6+Vector3.up*8;
                    camera.transform.LookAt(tanker.transform.position+Vector3.up*2);
                    SceneLuminance("hazard-FuelTanker-leaking",camera);
                    CameraController.Instance.enabled=true;
                }
                yield return new WaitForSecondsRealtime(2f);
                Check("leaking tanker detonates",!prop||prop.IsDestroyed);
            }
            var pump=first[(int)WorldHazardKind.GasPump];
            if(pump)
            {
                var pair=pump.transform.parent.GetComponentsInChildren<WorldHazard>();
                var prop=pump.GetComponent<DestructionSystem>();
                prop.ApplyDamage(prop.Health+5,pump.transform.position,null);
                yield return new WaitForSecondsRealtime(.35f);
                Check("gas pump blast chains to its paired pump",pair.Length==2&&(!pair[1]||pair[1].GetComponent<DestructionSystem>().IsDestroyed));
            }
            var chemical=first[(int)WorldHazardKind.ChemicalDrum];
            if(chemical)
            {
                var prop=chemical.GetComponent<DestructionSystem>();
                Vector3 center=chemical.transform.position;
                prop.ApplyDamage(prop.Health+5,chemical.transform.position,null);
                yield return new WaitForSecondsRealtime(.1f);
                bool patch=false;foreach(var effect in FindObjectsByType<WorldHazardAftermath>(FindObjectsSortMode.None))if(effect.Kind==WorldHazardKind.ChemicalDrum)patch=true;
                Check("chemical drums leave a timed corrosive spill",patch);
                yield return new WaitForSecondsRealtime(.45f);
                if(camera&&CameraController.Instance)
                {
                    CameraController.Instance.enabled=false;
                    camera.transform.position=center+new Vector3(-7,5,-8);
                    camera.transform.LookAt(center+Vector3.up*.5f);
                    SceneLuminance("hazard-ChemicalSpill",camera);
                    CameraController.Instance.enabled=true;
                }
                Vector3 inside=center;inside.y=GeneratedWorld.HeightAt(inside)+1.1f;
                Teleport(inside);yield return new WaitForSecondsRealtime(.15f);
                float before=game.Player.Damage.Health;
                yield return new WaitForSecondsRealtime(.7f);
                Check("corrosive spill damages a car left inside it",game.Player.Damage.Health<before);
                Vector3 safe=world.Towns[0]+new Vector3(80,0,80);safe.y=GeneratedWorld.HeightAt(safe)+1.1f;Teleport(safe);
            }
            var fireworks=first[(int)WorldHazardKind.Fireworks];
            if(fireworks)
            {
                var prop=fireworks.GetComponent<DestructionSystem>();
                Vector3 center=fireworks.transform.position;
                prop.ApplyDamage(prop.Health+5,fireworks.transform.position,null);
                yield return new WaitForSecondsRealtime(.3f);
                bool barrage=false;foreach(var effect in FindObjectsByType<WorldHazardAftermath>(FindObjectsSortMode.None))if(effect.Kind==WorldHazardKind.Fireworks)barrage=true;
                Check("fireworks stand launches a timed barrage",barrage);
                if(camera&&CameraController.Instance)
                {
                    CameraController.Instance.enabled=false;
                    camera.transform.position=center+new Vector3(-8,6,-10);
                    camera.transform.LookAt(center+Vector3.up*1.5f);
                    SceneLuminance("hazard-Fireworks-barrage",camera);
                    CameraController.Instance.enabled=true;
                }
            }
            var transformer=first[(int)WorldHazardKind.Transformer];
            if(transformer&&game.Player)
            {
                Vector3 near=transformer.transform.position-transformer.transform.forward*3;near.y=GeneratedWorld.HeightAt(near)+1.1f;
                Teleport(near);yield return new WaitForSecondsRealtime(.1f);
                var prop=transformer.GetComponent<DestructionSystem>();
                prop.ApplyDamage(prop.Health+5,transformer.transform.position,null);
                Check("transformer burst briefly stalls nearby cars",VehicleAfflictions.Stalled(game.Player));
            }
        }

        IEnumerator TestTownRevision()
        {
            var game=GameManager.Instance;
            foreach(int seed in new[]{173,271})
            {
                game.StartCampaign(seed,1600);
                float deadline=Time.realtimeSinceStartup+80;
                while((game.State!=GameState.Playing||!GeneratedWorld.Active||GeneratedWorld.Active.Seed!=seed)&&Time.realtimeSinceStartup<deadline)yield return null;
                var world=GeneratedWorld.Active;
                Check("seed "+seed+" generates a garage and town roads",world&&world.Seed==seed&&world.HasGarage);
                if(!world||world.Seed!=seed)yield break;
                yield return new WaitForSecondsRealtime(.3f);
                QuietMissionHostiles();
                var children=world.GetComponentsInChildren<Transform>();
                Transform garage=null;int junctions=0,stops=0,bulkPiles=0,smallPiles=0,tanks=0,logos=0;
                bool markedTanks=true,clearCaches=true;
                foreach(var child in children)
                {
                    if(child.name=="117° Auto Care Garage")garage=child;
                    if(child.name=="Paved town crossroads")junctions++;
                    if(child.name=="Crossroads stop bar")stops++;
                    bool bulk=child.name=="Propane tank pile",small=child.name=="Small propane cylinder stack";
                    if(!bulk&&!small)continue;
                    if(bulk)bulkPiles++;else smallPiles++;
                    if(world.ObstacleDistance(child.position)<6)clearCaches=false;
                    var damage=child.GetComponentsInChildren<DestructionSystem>();
                    int marks=0;foreach(var part in child.GetComponentsInChildren<Transform>())if(part.name=="Red Texas logo")marks++;
                    tanks+=damage.Length;logos+=marks;
                    if(damage.Length!=(bulk?5:9)||marks<damage.Length*2)markedTanks=false;
                    foreach(var tank in damage)if(!tank.Explosive||tank.Kind!=ExplosionKind.Propane)markedTanks=false;
                }
                Check("seed "+seed+" garage is a permanent physical building",garage&&garage.GetComponentsInChildren<Collider>().Length>=4&&garage.GetComponentsInChildren<DestructionSystem>().Length==0);
                Check("seed "+seed+" each town has a paved crossroads",junctions==world.Towns.Count&&stops==world.Towns.Count*2);
                Check("seed "+seed+" both propane cache variants have marked explosive tanks",bulkPiles>0&&smallPiles>0&&markedTanks&&tanks==bulkPiles*5+smallPiles*9&&logos>=tanks*2);
                Check("seed "+seed+" propane piles clear generated rock footprints",clearCaches);
                Check("seed "+seed+" garage is shown on world map",world.Pins.Exists(pin=>pin.id=="garage"&&pin.kind=="garage"));
                Debug.Log("MIA_TOWN_REVISION seed="+seed+" garage="+world.HasGarage+" junctions="+junctions+" bulkPiles="+bulkPiles+" smallPiles="+smallPiles+" tanks="+tanks+" logos="+logos);
                if(seed==173&&garage)
                {
                    Vector3 front=garage.position-garage.forward*9;front.y=GeneratedWorld.HeightAt(front)+1.1f;
                    Teleport(front);yield return new WaitForSecondsRealtime(.3f);
                    var camera=Camera.main;CameraController.Instance.enabled=false;
                    camera.transform.position=garage.position-garage.forward*23+Vector3.up*9;
                    camera.transform.LookAt(garage.position+Vector3.up*3);
                    SceneLuminance("town-garage",camera);
                    CameraController.Instance.enabled=true;
                    Vector3 center=world.Towns[1];center.y=GeneratedWorld.HeightAt(center)+1.1f;
                    Teleport(center);yield return new WaitForSecondsRealtime(.3f);SceneLuminance("town-crossroads");
                    Transform pile=null,smallPile=null;
                    foreach(var child in children)
                    {
                        if(!pile&&child.name=="Propane tank pile")pile=child;
                        if(!smallPile&&child.name=="Small propane cylinder stack")smallPile=child;
                    }
                    if(smallPile)
                    {
                        Vector3 p=smallPile.position+Vector3.back*7;p.y=GeneratedWorld.HeightAt(p)+1.1f;Teleport(p);
                        yield return new WaitForSecondsRealtime(.3f);
                        CameraController.Instance.enabled=false;
                        camera.transform.position=smallPile.position+new Vector3(-6,4,-8);
                        camera.transform.LookAt(smallPile.position+Vector3.up*1.2f);
                        SceneLuminance("small-propane-stack",camera);
                        CameraController.Instance.enabled=true;
                    }
                    if(pile)
                    {
                        Vector3 p=pile.position+Vector3.back*8;p.y=GeneratedWorld.HeightAt(p)+1.1f;Teleport(p);
                        yield return new WaitForSecondsRealtime(.3f);
                        CameraController.Instance.enabled=false;
                        camera.transform.position=pile.position+new Vector3(-7,5,-10);
                        camera.transform.LookAt(pile.position+Vector3.up*1.1f);
                        SceneLuminance("texas-propane-pile",camera);
                        CameraController.Instance.enabled=true;
                        var canisters=pile.GetComponentsInChildren<DestructionSystem>();
                        Vector3 safe=world.Towns[0]+new Vector3(80,0,80);safe.y=GeneratedWorld.HeightAt(safe)+1.1f;
                        Teleport(safe);
                        canisters[0].ApplyDamage(100,canisters[0].transform.position,null);
                        yield return new WaitForSecondsRealtime(.25f);
                        int detonated=0;foreach(var canister in canisters)if(!canister||canister.IsDestroyed)detonated++;
                        Check("propane pile detonates neighboring tanks",detonated>=2);
                    }
                }
                if(seed==271&&garage)
                {
                    Vector3 front=garage.position-garage.forward*8;front.y=GeneratedWorld.HeightAt(front)+1.1f;
                    Teleport(front);
                    var keyboard=InputSystem.AddDevice<Keyboard>();
                    InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.E));
                    yield return new WaitForSecondsRealtime(.5f);
                    InputSystem.QueueStateEvent(keyboard,new KeyboardState());
                    Check("physical garage interaction opens service scene",game.State==GameState.Garage);
                    InputSystem.RemoveDevice(keyboard);
                }
            }
        }

        IEnumerator TestPowerLines()
        {
            var game=GameManager.Instance;
            game.StartCampaign(173,1600);
            float deadline=Time.realtimeSinceStartup+75;
            while((game.State!=GameState.Playing||!GeneratedWorld.Active)&&Time.realtimeSinceStartup<deadline)yield return null;
            Check("generated world starts for power-line test",game.State==GameState.Playing&&GeneratedWorld.Active);
            if(game.State!=GameState.Playing||!GeneratedWorld.Active)yield break;
            yield return new WaitForSecondsRealtime(.5f);
            QuietMissionHostiles();
            var poles=new List<DestructionSystem>();
            foreach(var prop in FindObjectsByType<DestructionSystem>(FindObjectsSortMode.None))
                if(prop.name=="Utility pole")poles.Add(prop);
            var spans=FindObjectsByType<PowerLineSpan>(FindObjectsSortMode.None);
            Check("generated roads carry linked utility poles",poles.Count>15&&spans.Length>8);
            DestructionSystem shot=null,rammed=null;PowerLineSpan shotSpan=null,rammedSpan=null;
            foreach(var pole in poles)
            {
                PowerLineSpan linked=null;
                foreach(var span in spans)if(span.Connects(pole.transform)){linked=span;break;}
                if(!linked)continue;
                if(!shot){shot=pole;shotSpan=linked;}
                else if(linked!=shotSpan&&Vector3.Distance(shot.transform.position,pole.transform.position)>80){rammed=pole;rammedSpan=linked;break;}
            }
            Check("two separate poles have live cable spans",shot&&rammed&&shotSpan&&rammedSpan);
            if(!shot||!rammed)yield break;

            Vector3 target=shot.transform.position+Vector3.up*2.5f;
            Vector3 origin=target-shot.transform.forward*4f;
            ProjectileSystem.Fire(origin,(target-origin).normalized,90,5,0,null,
                VehicleLights.HeadlightAmber,ExplosionKind.Ammunition,1);
            yield return new WaitForSecondsRealtime(.3f);
            bool shotToppled=shot.IsDestroyed&&shot.GetComponent<Rigidbody>()&&!shot.Explosive;
            Check("projectile topples pole without detonating it",shotToppled);
            yield return new WaitForSecondsRealtime(.8f);
            var line=shotSpan.GetComponentInChildren<LineRenderer>();
            Vector3 middle=line?line.GetPosition(line.positionCount/2):Vector3.zero;
            Check("broken power line flops to terrain",line&&shotSpan.Collapse>.98f&&middle.y-GeneratedWorld.HeightAt(middle)<2f);

            Vector3 approach=rammed.transform.right;
            Vector3 start=rammed.transform.position+approach*7f;
            start.y=GeneratedWorld.HeightAt(start)+1.1f;
            Teleport(start);
            Quaternion facing=Quaternion.LookRotation(-approach,Vector3.up);
            game.Player.Body.rotation=facing;game.Player.transform.rotation=facing;
            game.Player.Body.linearVelocity=-approach*26f;
            yield return new WaitForSecondsRealtime(.65f);
            Check("vehicle impact topples pole",rammed.IsDestroyed&&rammed.GetComponent<Rigidbody>());
            yield return new WaitForSecondsRealtime(.9f);
            Check("vehicle-hit cables also fall",rammedSpan.Collapse>.98f);
            Debug.Log("MIA_POWER_LINES poles="+poles.Count+" spans="+spans.Length+" firstMid="+middle+" shot="+shotToppled+" rammed="+rammed.IsDestroyed);
            if(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MIA_CAPTURE_DIR")))SceneLuminance("fallen-power-lines");
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
            Check("biome nature picks packaged (saguaros, desert trees, junipers, red-rock plants and stones)",nature&&nature.saguaros?.Length==5&&nature.desertTrees?.Length==3&&nature.junipers?.Length==2&&nature.redRockPlants?.Length==5&&nature.redRocks?.Length==16&&nature.cacti?.Length==19&&nature.aspens?.Length==4&&nature.snowPines?.Length==4&&nature.agaves?.Length==1&&nature.deadTrees?.Length==1);
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
            // A retired 0.8 km size still loads (and generates at the 1.6 km minimum).
            var edited=new WorldGenConfig{seed=271,size=800};
            edited.pins.Add(new WorldPin{id="test-cache",label="Test relay cache",kind="salvage-tech",position=Vector3.zero});
            File.WriteAllText(WorldConfigStore.Path,JsonUtility.ToJson(edited,true));
            float deadline=Time.realtimeSinceStartup+25;
            while(GeneratedWorld.Active==old&&Time.realtimeSinceStartup<deadline)yield return null;
            yield return new WaitForSecondsRealtime(.7f);
            Check("valid JSON automatically regenerates changed seed",GeneratedWorld.Active&&GeneratedWorld.Active!=old&&GeneratedWorld.Active.Seed==271);
            Check("a retired 0.8 km config loads at the 1.6 km minimum",GeneratedWorld.Active&&Mathf.Approximately(GeneratedWorld.Active.WorldBounds.size.x,GeneratedWorld.MinSize));
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
            game.WorldConfig.size=GeneratedWorld.MaxSize;WorldConfigStore.Save(game.WorldConfig);game.StartMission(0);yield return new WaitForSecondsRealtime(.3f);
            Check("maximum world size generates with bounded geometry",Mathf.Approximately(GeneratedWorld.Active.WorldBounds.size.x,GeneratedWorld.MaxSize)&&GeneratedWorld.Active.transform.Find("Chunked terrain").GetComponentsInChildren<MeshCollider>().Length<=256);
            game.WorldConfig.size=1600;WorldConfigStore.Save(game.WorldConfig);game.StartMission(0);yield return new WaitForSecondsRealtime(.3f);
            float protectedHealth=game.Player.Damage.Health;
            game.Player.Damage.Invulnerable=true;
            game.Player.Damage.ApplyDamage(50,game.Player.transform.position,null);
            Check("dev invulnerability blocks player damage",Mathf.Approximately(game.Player.Damage.Health,protectedHealth));
            // Biomes follow Arizona: Sonoran south (the starter town), red rock north-east, plateau north-west, Rim forest between.
            var active=GeneratedWorld.Active;float span=active.WorldBounds.size.x;
            bool geography=active.BiomeAt(active.Towns[0]).Dominant==Biome.Sonoran
                &&active.BiomeAt(new Vector3(.3f*span,0,.38f*span)).Dominant==Biome.RedRock
                &&active.BiomeAt(new Vector3(-.32f*span,0,.36f*span)).Dominant==Biome.Plateau
                &&active.BiomeAt(new Vector3(-.3f*span,0,-.3f*span)).Dominant==Biome.Sonoran;
            bool allBiomes=active.FindBiomeSpot(Biome.Sonoran)!=null&&active.FindBiomeSpot(Biome.RedRock)!=null&&active.FindBiomeSpot(Biome.Forest)!=null&&active.FindBiomeSpot(Biome.Plateau)!=null;
            Check("biomes follow Arizona geography and all four appear",geography&&allBiomes);
            // The San Francisco Peaks: a broad cone north of the Rim.
            Vector3 peakSite=new Vector3(.05f*span,0,.26f*span);float summit=GeneratedWorld.HeightAt(peakSite),ring=0;
            for(int k=0;k<8;k++){float a=k*Mathf.PI/4;ring+=GeneratedWorld.HeightAt(peakSite+new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*span*.15f);}ring/=8;
            Debug.Log("MIA_PEAKS: summit "+summit.ToString("0.0")+" m, surrounding ring "+ring.ToString("0.0")+" m");
            Check("San Francisco Peaks rise above the surrounding country",summit-ring>12);
            // Inspect the biomes and river using camera-only targets, leaving physics untouched.
            var camera=CameraController.Instance;var target=camera.Target;var focus=new GameObject("World visual review");camera.Target=focus.transform;
            foreach(var biome in new[]{Biome.RedRock,Biome.Plateau,Biome.Sonoran})
            {
                var spot=active.FindBiomeSpot(biome);if(spot==null)continue;
                focus.transform.position=spot.Value;camera.Snap();
                yield return new WaitForSecondsRealtime(3f);Capture("23-biome-"+biome.ToString().ToLowerInvariant());yield return new WaitForSecondsRealtime(.35f);
                var names=new Dictionary<string,int>();
                foreach(var prop in FindObjectsByType<DestructionSystem>(FindObjectsSortMode.None))
                    if(prop&&!prop.IsDestroyed&&(prop.transform.position-spot.Value).sqrMagnitude<60*60){names.TryGetValue(prop.name,out int n);names[prop.name]=n+1;}
                Debug.Log("MIA_BIOME_PROPS "+biome+" (within 60 m): "+string.Join(", ",names.Select(pair=>pair.Key+" x"+pair.Value)));
            }
            // The peaks' upper slopes (snowy pines on a pale summit).
            Vector3 slope=peakSite+new Vector3(0,0,-span*.04f);slope.y=GeneratedWorld.HeightAt(slope);
            focus.transform.position=slope;camera.Snap();yield return new WaitForSecondsRealtime(3f);Capture("23-biome-peaks");yield return new WaitForSecondsRealtime(.35f);
            for(int i=0;i<2;i++)
            {
                Vector3 p=Vector3.zero;
                if(i==0)p=active.FindBiomeSpot(Biome.Forest)??active.Towns[active.Towns.Count-1];
                else foreach(var t in GeneratedWorld.Active.GetComponentsInChildren<Transform>(true))
                    if(t.name=="Salt River"){p=t.GetComponent<MeshFilter>().sharedMesh.vertices[t.GetComponent<MeshFilter>().sharedMesh.vertexCount/2]+Vector3.right*16;break;}
                p.y=GeneratedWorld.HeightAt(p);focus.transform.position=p;camera.Snap();
                yield return new WaitForSecondsRealtime(3f);Capture(i==0?"20-northern-biome":"21-river-biome");yield return new WaitForSecondsRealtime(.35f);
            }
            // A closer native view documents actual canopy/ground-cover geometry and shader detail.
            Vector3 forest=active.FindBiomeSpot(Biome.Forest)??active.Towns[active.Towns.Count-1];
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
            yield return TestLandformsAndBrittleScenery(focus);
            bool upwardMarks=true;int marks=0;
            foreach(var filter in GeneratedWorld.Active.GetComponentsInChildren<MeshFilter>())if(filter.name=="Worn center markings")
            {marks++;foreach(var n in filter.sharedMesh.normals)if(n.y<.5f)upwardMarks=false;}
            Check("road markings face upward",marks>0&&upwardMarks);
            Check("wind scenery shader supported",Shader.Find("MadeInArizona/LivingScenery")&&Shader.Find("MadeInArizona/LivingScenery").isSupported);
            camera.Target=target;camera.Snap();Destroy(focus);
            Vector3 patrolAt=GeneratedWorld.Active.Towns[1]+new Vector3(0,1,-10);patrolAt.y=GeneratedWorld.HeightAt(patrolAt)+1;
            Teleport(patrolAt);camera.Snap();yield return null;
            var director=GeneratedWorld.Active.GetComponent<RoadPatrolDirector>();
            Check("occasional road patrol spawns away from starter",director&&
                (director.EncountersSpawned>0||director.TrySpawnPatrol()));
            Check("road patrol population is bounded",director&&director.LivePatrols<=RoadPatrolDirector.PatrolLimit);
            InputSystem.RemoveDevice(keyboard);
            Application.logMessageReceived-=OnLog;
            string result=string.Join("\n",checks)+"\n"+string.Join("\n",failures)+"\nRESULT: "+(failures.Count==0?"PASS":"FAIL");
            Debug.Log("MIA_WORLD_RESULTS\n"+result);Application.Quit(failures.Count==0?0:1);
        }
        /// <summary>
        /// Landforms and cover obey their placement rules and are captured; cacti break within two weak hits and throw their
        /// pieces along the shot; a felled tree keeps its look, falls as a body rounds pass through, then fades.
        /// </summary>
        IEnumerator TestLandformsAndBrittleScenery(GameObject focus)
        {
            var game=GameManager.Instance;var world=GeneratedWorld.Active;var camera=CameraController.Instance;
            string issue=world.LandformIssue();
            Debug.Log("MIA_LANDFORMS: landforms="+world.LandformCount+" cover="+world.CoverCount+" boundary="+world.BoundaryRockCount+" base="+world.BaseRockCount+" barriers="+world.MountainBarrierCount+" issue="+(issue??"none"));
            Check("landforms stay within the conservative count",world.LandformCount>=8&&world.LandformCount<=GeneratedWorld.MaxLandforms);
            Check("cover rocks placed for gunfights",world.CoverCount>=20);
            Check("large rock chains enclose the entire map with solid barriers",world.BoundaryRockCount>=80&&world.MountainBarrierCount>=world.BoundaryRockCount-8&&world.BaseRockCount>=world.BoundaryRockCount/2);
            Check("landforms and cover keep roads, towns, objectives, trails and routes open",issue==null);
            // Review captures: the first large landform and the first cover rock, from the gameplay camera.
            int shots=0;
            for(int i=0;i<world.LandformEntries&&shots<2;i++)
            {
                Vector3 at=world.LandformCenter(i,out var kind,out float reach);bool cover=kind==GeneratedWorld.LandformKind.Cover;
                if((shots==0)==cover)continue;
                focus.transform.position=world.ClearOfObstacles(at,cover?4:10);camera.Snap();
                yield return new WaitForSecondsRealtime(2.5f);Capture(shots==0?"36-landform-"+kind.ToString().ToLowerInvariant():"37-cover-rock");shots++;
                yield return new WaitForSecondsRealtime(.35f);
            }
            // The car parked on the far side of a tall landform: the rock between it and the camera dithers open.
            for(int i=0;i<world.LandformEntries;i++)
            {
                Vector3 at=world.LandformCenter(i,out var kind,out float reach);
                if(kind!=GeneratedWorld.LandformKind.Mesa&&kind!=GeneratedWorld.LandformKind.Butte)continue;
                Vector3 behind=world.ClearOfObstacles(at+Vector3.forward*(reach+2.5f),2.5f);behind.y=GeneratedWorld.HeightAt(behind)+1;
                Teleport(behind);camera.Target=game.Player.transform;camera.Snap();
                yield return new WaitForSecondsRealtime(2.5f);Capture("39-landform-see-through");yield return new WaitForSecondsRealtime(.35f);
                camera.Target=focus.transform;break;
            }
            Check("brittle scenery never makes contact with cars",Physics.GetIgnoreLayerCollision(0,DestructionSystem.BrittleLayer));
            // Cacti near the starter town.
            focus.transform.position=game.World.PlayerSpawn;camera.Snap();yield return new WaitForSecondsRealtime(2.5f);
            DestructionSystem cactus=null;
            foreach(var prop in FindObjectsByType<DestructionSystem>(FindObjectsSortMode.None))if(!prop.IsDestroyed&&prop.name=="Breakable saguaro"){cactus=prop;break;}
            Check("desert cacti are brittle props on their own layer",cactus&&cactus.Brittle&&cactus.gameObject.layer==DestructionSystem.BrittleLayer&&cactus.GetComponent<CapsuleCollider>());
            if(cactus)
            {
                var before=new HashSet<Rigidbody>();foreach(var body in FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))if(body.name=="Pooled debris"&&body.gameObject.activeInHierarchy)before.Add(body);
                Vector3 hit=cactus.transform.position+Vector3.up;
                cactus.ApplyProjectileHit(1,hit,null,Vector3.right);bool survivedFirst=!cactus.IsDestroyed;
                cactus.ApplyProjectileHit(1,hit,null,Vector3.right);
                Check("a cactus stops one weak round and breaks on the second",survivedFirst&&cactus.IsDestroyed);
                // Fracture cells stay kinematic for 0.12 s so their intact outline reads before they scatter.
                yield return new WaitForSecondsRealtime(.18f);
                yield return new WaitForFixedUpdate();
                float along=0;int pieces=0;
                foreach(var body in FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))if(body.name=="Pooled debris"&&body.gameObject.activeInHierarchy&&!before.Contains(body)){along+=body.linearVelocity.x;pieces++;}
                Debug.Log("MIA_CACTUS_DEBRIS pieces="+pieces+" mean x velocity="+(pieces>0?along/pieces:0).ToString("F2"));
                Check("cactus pieces fly on along the shot",pieces>0&&along/pieces>1.5f);
            }
            // Pines in the Rim forest.
            Vector3 forest=world.FindBiomeSpot(Biome.Forest)??world.Towns[world.Towns.Count-1];
            focus.transform.position=forest;camera.Snap();yield return new WaitForSecondsRealtime(3f);
            DestructionSystem tree=null;float nearest=float.MaxValue;
            foreach(var prop in FindObjectsByType<DestructionSystem>(FindObjectsSortMode.None))
                if(!prop.IsDestroyed&&prop.name=="Breakable ponderosa"){float d=(prop.transform.position-forest).sqrMagnitude;if(d<nearest){nearest=d;tree=prop;}}
            Check("forest pines are breakable",tree);
            if(tree)
            {
                var trunk=tree.gameObject;Vector3 start=trunk.transform.position;
                tree.SmashFromVehicle(start+Vector3.up,null,new Vector3(12,0,0));
                var body=trunk.GetComponent<Rigidbody>();var renderer=trunk.GetComponentInChildren<Renderer>();
                Check("a felled tree keeps its look and falls as a heavy body",body&&!body.isKinematic&&body.mass>=300&&renderer&&renderer.enabled&&trunk.layer==2);
                Physics.Raycast(start+new Vector3(-6,2,0),Vector3.right,out var ray,12,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
                Check("rounds pass through a falling tree",!ray.collider||!ray.collider.transform.IsChildOf(trunk.transform));
                Capture("38-tree-falling");
                yield return new WaitForSecondsRealtime(.9f);
                float tilt=trunk?Vector3.Angle(trunk.transform.up,Vector3.up):0;
                Debug.Log("MIA_TREE_FALL tilt="+tilt.ToString("F1")+" moved="+(trunk?(trunk.transform.position-start).magnitude:0).ToString("F2"));
                Check("a felled tree tips over away from the push",trunk&&tilt>25&&Vector3.Dot(trunk.transform.up,Vector3.right)>0);
                yield return new WaitForSecondsRealtime(1.8f);
                Check("a fallen tree fades away",!trunk);
            }
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
            var presentation=game.PresentationVolume;
            Check("vignette slider reaches live presentation volume",presentation&&presentation.profile&&
                presentation.profile.TryGet<UnityEngine.Rendering.Universal.Vignette>(out var vignette)&&Mathf.Abs(vignette.intensity.value-.6f)<.01f);
            tuning.depthOfField=1;tuning.vignette=0;DevTuning.Apply();
            yield return new WaitForSecondsRealtime(.4f);Capture("24-post-effects-on");yield return new WaitForSecondsRealtime(.3f);
            var pipeline=UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            Check("orthographic blur renderer feature packaged",Resources.FindObjectsOfTypeAll<OrthographicDepthBlurFeature>().Length>0&&Shader.Find("MadeInArizona/OrthographicDepthBlur").isSupported);
            Check("orthographic blur slider drives pixel radius",Shader.GetGlobalVector("_ArizonaOrthoDofParams").z>1);
            // Settings > Graphics & Camera > Camera style: the lens switches projection and still frames the car.
            var settings=game.Save.settings;bool oldPerspective=settings.perspectiveCamera;
            settings.perspectiveCamera=true;yield return new WaitForSecondsRealtime(.5f);Capture("40-perspective-camera");
            var lens=Camera.main;var carView=lens.WorldToViewportPoint(game.Player.transform.position);
            Check("perspective camera option frames the car",!lens.orthographic&&Mathf.Approximately(lens.fieldOfView,CameraController.PerspectiveFov)&&carView.z>0&&carView.x>.3f&&carView.x<.7f&&carView.y>.25f&&carView.y<.75f);
            settings.perspectiveCamera=false;yield return new WaitForSecondsRealtime(.3f);
            Check("camera style switches to overhead",Camera.main.orthographic);
            settings.perspectiveCamera=oldPerspective;yield return new WaitForSecondsRealtime(.3f);
            Check("camera style restores preference",Camera.main.orthographic==!oldPerspective);
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
            var factions=new HashSet<EnemyFaction>();bool factionLoadouts=true,firstWaveInside=true;
            foreach(var ai in FindObjectsByType<EnemyAI>())
            {
                factions.Add(ai.Faction);
                var weapon=ai.GetComponent<VehicleController>().Weapons.GarageWeapon;
                factionLoadouts &= weapon && weapon.id==FactionRules.PrimaryWeapon(ai.Faction,ai.Archetype);
                firstWaveInside &= Mathf.Abs(ai.transform.position.x)<51f&&Mathf.Abs(ai.transform.position.z)<43f;
            }
            Check("combat trial opens with three factions and their weapon sets",factions.Count==3&&factionLoadouts);
            Check("first combat wave spawns inside arena walls",firstWaveInside);
            foreach(var ai in FindObjectsByType<EnemyAI>(FindObjectsSortMode.None)){ai.enabled=false;ai.GetComponent<VehicleController>().SetAIInput(Vector2.zero,Vector3.forward,false);}
            Check("field weapon starts empty in combat",game.Player.Weapons.FieldWeapon==null);
            game.Player.Weapons.EquipField(WeaponRules.Find("invoice"),WeaponRules.PickupAmmo("invoice"));
            Check("enemy rocket drop fits LT with limited ammo",game.Player.Weapons.FieldWeapon!=null&&game.Player.Weapons.FieldAmmo==WeaponRules.PickupAmmo("invoice"));
            game.Player.Weapons.AddFieldAmmo(99);
            Check("field ammo is capped per weapon",game.Player.Weapons.FieldAmmo==WeaponRules.MaxAmmo("invoice"));
            game.Player.Weapons.EquipField(WeaponRules.Find("invoice"),WeaponRules.PickupAmmo("invoice"));
            foreach(var ai in FindObjectsByType<EnemyAI>(FindObjectsSortMode.None))ai.enabled=true;
            float fightStart=Time.time,deadline=Time.time+70;bool attacked=false,captured=false,warned=false,waveTwoSeen=false,waveTwoInside=true;
            while(Time.time<deadline&&game.IsPlaying) {
                foreach(var ai in FindObjectsByType<EnemyAI>())factions.Add(ai.Faction);
                if(game.Mission.Stage==1&&!waveTwoSeen)
                {
                    waveTwoSeen=true;
                    foreach(var enemy in VehicleController.Active)
                        if(enemy&&!enemy.IsPlayer&&!enemy.Damage.IsDead)
                            waveTwoInside &= Mathf.Abs(enemy.transform.position.x)<51f&&Mathf.Abs(enemy.transform.position.z)<43f;
                }
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
            if(game.State!=GameState.Won)
            {
                Debug.Log("MIA_COMBAT_TRIAL_DIAG kills="+game.Mission.Kills+" stage="+game.Mission.Stage+" state="+game.State+" player="+game.Player.transform.position+" elapsed="+(Time.time-fightStart).ToString("F1"));
                foreach(var enemy in VehicleController.Active)
                    if(enemy&&!enemy.IsPlayer&&!enemy.Damage.IsDead)
                        Debug.Log("MIA_COMBAT_TRIAL_ENEMY "+enemy.name+" pos="+enemy.transform.position+" hp="+enemy.Damage.Health.ToString("F0")+" distance="+Vector3.Distance(enemy.transform.position,game.Player.transform.position).ToString("F0"));
            }
            Check("combat trial fields all six faction crews",factions.Count==FactionRules.Count);
            Check("second combat wave spawns inside arena walls",waveTwoSeen&&waveTwoInside);
            Check("enemy vehicle weapons damage player",attacked);
            Check("real projectiles defeat both vehicle waves",game.State==GameState.Won&&game.Mission.Kills>=24);
            Debug.Log("MIA_COMBAT_TRIAL_RESULT kills="+game.Mission.Kills+" elapsed="+(Time.time-fightStart).ToString("F1")+" state="+game.State);
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

                if (index == 5) Check("forced inappropriate vehicle", game.CurrentVehicle.id == "shift");
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
            Check("other weapons retain imported recordings; crossbow has its own release",ContentCatalog.Weapons.Length==32&&
                Array.TrueForAll(ContentCatalog.Weapons,w=>w.id=="crossbow" ? w.fireSounds!=null&&w.fireSounds.Length==0&&audio.CrossbowShot :
                    w.fireSounds!=null&&w.fireSounds.Length>0&&Array.TrueForAll(w.fireSounds,c=>c&&c.channels==2&&c.frequency==44100&&c.length>.5f)));
            float oldVolume=game.Save.settings.weapons;game.Save.settings.weapons=1;
            foreach(var weapon in ContentCatalog.Weapons)
            {
                StopPooledAudio(audio);audio.PlayShot(game.Player.transform.position,weapon);yield return null;
                bool correct = weapon.id=="crossbow" ?
                    audio.GetComponentsInChildren<AudioSource>().Any(source=>source.isPlaying&&source.clip==audio.CrossbowShot) :
                    ImportedShotPlaying(audio,weapon);
                Check("weapon fires its intended sound for "+weapon.id,correct);
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
            game.Save.settings.weapons=1;
            VehicleAfflictions.For(enemy).AttachCharge(enemy.transform.position+Vector3.up,1.1f,10,3,game.Player.gameObject);
            yield return null;
            var marker=enemy.transform.Find("Stuck dynamite bolt");
            var fuseSource=marker?marker.GetComponent<AudioSource>():null;
            Check("dynamite stuck to a car sizzles from the moving marker",fuseSource&&fuseSource.isPlaying&&
                fuseSource.clip==audio.DynamiteFuse&&fuseSource.spatialBlend==1&&fuseSource.volume>0);
            game.Save.settings.weapons=oldVolume;game.ReturnToGarage();
        }
        /// <summary>
        /// Physical engine voice: every vehicle resolves a valid layout, each layout renders finite, bounded audio
        /// within the CPU budget, the Thimble's three-cylinder firing period is present, and the live voice follows
        /// the player's vehicle and the A/B/C model choice. Set MIA_AUDIO_DIR to also write each sweep as a WAV.
        /// </summary>
        IEnumerator TestEngineAudio()
        {
            var game=GameManager.Instance;var audio=AudioManager.Instance;
            bool layoutsValid=true;
            foreach(var vehicle in ContentCatalog.Vehicles)
            {
                var layout=vehicle.Engine;
                bool valid=layout!=null&&layout.Cylinders>0&&layout.headerLength.Length==layout.Cylinders&&layout.collector.Length==layout.Cylinders
                    &&layout.Collectors>0&&layout.pan.Length==layout.Collectors&&layout.redlineRpm>layout.idleRpm
                    &&Array.TrueForAll(layout.collector,c=>c>=0&&c<layout.Collectors);
                if(!valid)Debug.LogError("MIA_ENGINE_LAYOUT: invalid engine layout for "+vehicle.id);
                layoutsValid&=valid;
            }
            Check("every vehicle resolves a valid engine layout",layoutsValid);
            string[] rosterIds={"ronin","juniper","sunskip","foreclosure","sidehustle","perennial","skitter","vincent","shift",
                "monsoon","sonora","dustback","monument","executive","heat","citizen","roam4","ae867","espresso",
                "copperado","contender","scarface"};
            int[] rosterCylinders={4,4,8,6,6,8,3,8,3,5,6,4,6,8,3,4,4,4,3,8,8,3};
            bool countsMatch=ContentCatalog.Vehicles.Length==rosterIds.Length,assetsMatch=countsMatch;
            for(int i=0;i<rosterIds.Length;i++)
            {
                var vehicle=Array.Find(ContentCatalog.Vehicles,v=>v.id==rosterIds[i]);
                countsMatch&=vehicle!=null&&vehicle.Engine.Cylinders==rosterCylinders[i];
                assetsMatch&=vehicle!=null&&vehicle.engineLayout==EngineLayouts.ForVehicle(rosterIds[i]);
            }
            Check("stock roster cylinder counts match the vehicle inspirations",countsMatch);
            Check("authored engine assets agree with runtime fallback layouts",assetsMatch);
            Check("DeVille-inspired Executive has its own eight-cylinder voice",Array.Find(ContentCatalog.Vehicles,v=>v.id=="executive")?.Engine.id=="v8-luxury");
            Check("Outback-inspired Dustback has a boxer voice",Array.Find(ContentCatalog.Vehicles,v=>v.id=="dustback")?.Engine.id=="boxer4");
            Check("AE86-inspired Runner no longer revs like a motorcycle",Array.Find(ContentCatalog.Vehicles,v=>v.id=="ae867")?.Engine.redlineRpm==7500);
            var shift=Array.Find(ContentCatalog.Vehicles,v=>v.id=="shift");
            Check("Atlas Shift uses the three-cylinder layout",shift&&shift.Engine.id=="inline3"&&shift.Engine.Cylinders==3);
            var olderCarProfile=new CarTuning { engineAudioVersion=0, engineHeaderBalance=0 };
            olderCarProfile.Clamp();
            Check("older per-car audio profiles inherit the new header balance",Mathf.Abs(olderCarProfile.engineHeaderBalance-.7f)<.001f);
            Check("older per-car audio profiles enable both new layers",olderCarProfile.engineIntakeLevel==1&&olderCarProfile.engineAccessoryLevel==1);
            var olderAudioSettings=new DevTuning { audioVersion=6, engineIntakeLevel=0, engineAccessoryLevel=0 };
            olderAudioSettings.Clamp();
            Check("older global audio settings enable both new layers",olderAudioSettings.engineIntakeLevel==1&&olderAudioSettings.engineAccessoryLevel==1);
            Check("older global audio settings enable drive-aware response",olderAudioSettings.engineDriveResponse==1);
            var deliberatelyDisabled=new CarTuning { engineAudioVersion=2, engineIntakeLevel=0, engineAccessoryLevel=0 };
            deliberatelyDisabled.Clamp();
            Check("saved layer bypasses remain off",deliberatelyDisabled.engineIntakeLevel==0&&deliberatelyDisabled.engineAccessoryLevel==0);
            Check("older per-car audio profiles enable drive-aware response",deliberatelyDisabled.engineDriveResponse==1);
            var originalDriveProfile=new CarTuning { engineAudioVersion=3, engineDriveResponse=0 };
            originalDriveProfile.Clamp();
            Check("saved original drive response remains off",originalDriveProfile.engineDriveResponse==0);

            float charge=.35f,freeRev=.4f;
            AudioManager.ShapePhysicalDrive(2400,.7f,.2f,1,false,0,.02f,ref charge,ref freeRev,out float oldRpm,out float oldLoad);
            Check("drive response OFF passes through original RPM and load",oldRpm==2400&&oldLoad==.7f&&freeRev==0);
            charge=0;freeRev=0;
            for(int step=0;step<25;step++)
                AudioManager.ShapePhysicalDrive(1800,1,1,0,true,1,.02f,ref charge,ref freeRev,out _,out _);
            AudioManager.ShapePhysicalDrive(1800,1,1,0,true,1,.02f,ref charge,ref freeRev,out float loadedRpm,out float loadedSound);
            Check("a low-speed hard pull stays loaded without falsely free-revving",loadedSound>.95f&&loadedRpm==1800);
            for(int step=0;step<15;step++)
                AudioManager.ShapePhysicalDrive(1800,1,1,1,true,1,.02f,ref charge,ref freeRev,out _,out _);
            AudioManager.ShapePhysicalDrive(1800,1,1,1,true,1,.02f,ref charge,ref freeRev,out float spinningRpm,out _);
            Check("wheelspin frees the sound RPM above wheel-derived RPM",spinningRpm>3500&&spinningRpm<6900);
            for(int step=0;step<20;step++)
                AudioManager.ShapePhysicalDrive(2600,.8f,.35f,0,true,1,.02f,ref charge,ref freeRev,out _,out _);
            AudioManager.ShapePhysicalDrive(2600,.8f,.35f,0,true,1,.02f,ref charge,ref freeRev,out float settledRpm,out float settledLoad);
            Check("effective drive throttle governs charge after a shift or steering cut",settledRpm==2600&&settledLoad>.34f&&settledLoad<.4f);
            charge=0;freeRev=0;
            AudioManager.ShapePhysicalDrive(900,0,0,0,false,1,.02f,ref charge,ref freeRev,out float airborneIdle,out _);
            Check("an airborne car does not free-rev without throttle",airborneIdle==900);

            string wavDir=Environment.GetEnvironmentVariable("MIA_AUDIO_DIR");
            bool allBounded=true;float worstLoad=0;
            foreach(var layout in EngineLayouts.All)
            {
                var voice=new EngineVoice(layout,48000);
                var block=new float[2048];var pcm=new float[48000*2*4];int written=0;bool finite=true;float peak=0;double energy=0;
                for(int frame=0;frame<48000*4;frame+=1024)
                {
                    float t=frame/48000f;
                    voice.TargetRpm=t<1?layout.idleRpm:t<3?Mathf.Lerp(layout.idleRpm,layout.redlineRpm,(t-1)/2):Mathf.Lerp(layout.redlineRpm,layout.idleRpm,t-3);
                    voice.Throttle=t>=1&&t<3?1:0;voice.Gain=1;
                    voice.Render(block,2);
                    foreach(float v in block){finite&=!float.IsNaN(v)&&!float.IsInfinity(v);peak=Mathf.Max(peak,Mathf.Abs(v));energy+=v*v;if(written<pcm.Length)pcm[written++]=v;}
                    if(t>2)worstLoad=Mathf.Max(worstLoad,voice.Load);
                }
                float rms=(float)Math.Sqrt(energy/written);
                bool bounded=finite&&peak<=1&&rms>.02f;
                if(!bounded)Debug.LogError("MIA_ENGINE_RENDER: "+layout.id+" finite="+finite+" peak="+peak+" rms="+rms);
                allBounded&=bounded;
                if(layout.id=="inline3"||layout.id==EngineLayouts.Default)
                {
                    double idleEnergy=0;float idlePeak=0;int idleSamples=0;
                    for(int sample=(int)(.3f*48000)*2;sample<(int)(.9f*48000)*2;sample+=2)
                    {float value=pcm[sample];idleEnergy+=value*value;idlePeak=Mathf.Max(idlePeak,Mathf.Abs(value));idleSamples++;}
                    float idleCrest=idlePeak/Mathf.Sqrt((float)(idleEnergy/idleSamples));
                    Debug.Log("MIA_ENGINE_IDLE: "+layout.id+" crest "+idleCrest.ToString("0.00"));
                    Check(layout.id+" idle has sustained body between firing pulses",idleCrest<(layout.id=="inline3"?6f:5.2f));
                }
                if(!string.IsNullOrEmpty(wavDir)){Directory.CreateDirectory(wavDir);WriteWav(Path.Combine(wavDir,"engine-"+layout.id+".wav"),pcm,48000);}
                yield return null;
            }
            Check("every engine layout renders finite, audible, unclipped audio",allBounded);
            Debug.Log("MIA_ENGINE_CPU: worst smoothed render load "+(worstLoad*100).ToString("0.00")+"% of one core per voice");
            Check("physical engine voice stays under 5% of one core",worstLoad>0&&worstLoad<.05f);
            Check("Shift three-cylinder firing period is present at 3000 rpm",FiringPeriodPresent(shift.Engine,3000));
            var headerOnly=new EngineVoice(shift.Engine,48000){HeaderBalance=1};
            var tailOnly=new EngineVoice(shift.Engine,48000){HeaderBalance=0};
            var headerBlock=new float[2048];var tailBlock=new float[2048];
            headerOnly.Gain=tailOnly.Gain=1;headerOnly.TargetRpm=tailOnly.TargetRpm=3000;headerOnly.Throttle=tailOnly.Throttle=.7f;
            double headerEnergy=0,tailEnergy=0,contrastEnergy=0;
            for(int block=0;block<80;block++)
            {
                headerOnly.Render(headerBlock,2);tailOnly.Render(tailBlock,2);
                if(block<20)continue;
                for(int sample=0;sample<headerBlock.Length;sample+=2)
                {
                    float h=headerBlock[sample],t=tailBlock[sample];
                    headerEnergy+=h*h;tailEnergy+=t*t;contrastEnergy+=(h-t)*(h-t);
                }
            }
            Check("header and tailpipe control has two distinct audible paths",headerEnergy>1&&tailEnergy>1&&contrastEnergy>headerEnergy*.1f);
            float intakeLoaded=LayerDifference(shift.Engine,4500,1,true);
            float intakeCoast=LayerDifference(shift.Engine,4500,0,true);
            float accessoryCoast=LayerDifference(shift.Engine,4500,0,false);
            Debug.Log("MIA_ENGINE_LAYERS: induction loaded "+intakeLoaded.ToString("0.0000")+", induction coast "+intakeCoast.ToString("0.0000")+", accessory coast "+accessoryCoast.ToString("0.0000"));
            Check("intake induction follows throttle and can be bypassed",intakeLoaded>.002f&&intakeLoaded>intakeCoast*3);
            Check("accessory whir runs on overrun and can be bypassed",accessoryCoast>.001f);
            float stockHigh=HighBandShare(new EngineVoice(shift.Engine,48000),out _,out _);
            float turboHigh=HighBandShare(new EngineVoice(shift.Engine,48000,true),out float boostSpool,out float liftSpool);
            Debug.Log("MIA_ENGINE_TURBO: spool at boost "+boostSpool.ToString("0.00")+", after 1 s lift "+liftSpool.ToString("0.00")+", exhaust >2 kHz share stock "+stockHigh.ToString("0.000")+" turbo "+turboHigh.ToString("0.000"));
            Check("Shift turbo spools under load and coasts down on lift",boostSpool>.5f&&liftSpool<boostSpool*.75f);
            Check("Shift turbo turbine softens the exhaust pulses",turboHigh<stockHigh*.9f);

            game.Save.settings.dev.enginePhysical=true;
            if(game.Player==null||game.Player.Definition==null||game.Player.Definition.id!="shift")
            {GarageManager.SelectVehicle(8,game.Save);game.RefreshGarageVehicle();}
            yield return new WaitForSecondsRealtime(.6f);
            var live=audio.PhysicalEngine;
            Check("live physical voice follows the player's vehicle",live&&live.Layout!=null&&live.Layout.id==game.Player.Definition.Engine.id);
            Check("live physical voice renders on the audio thread",live&&live.GetComponent<AudioSource>().isPlaying&&live.Voice.Gain>0&&live.Voice.Load>0);
            BalanceTuning.LocalCar("shift").enginePhysical=false;
            DevTuning.Apply();
            yield return new WaitForSecondsRealtime(.4f);
            Check("choosing model A or B silences the physical voice",live.Voice.Gain==0);
            BalanceTuning.LocalCar("shift").enginePhysical=true;
            DevTuning.Apply();

            var save=game.Save;bool owned=save.ownedParts.Contains("turbo"),installed=save.installedParts.Contains("turbo");
            if(!owned)save.ownedParts.Add("turbo");if(!installed)save.installedParts.Add("turbo");
            game.RefreshGarageVehicle();yield return new WaitForSecondsRealtime(.3f);
            Check("installing the Shift turbo rebuilds the live voice with a turbocharger",game.Player.Stats.turbocharged&&live.Voice.Turbocharged&&live.Layout.id=="inline3");
            if(!installed)save.installedParts.Remove("turbo");if(!owned)save.ownedParts.Remove("turbo");
            game.RefreshGarageVehicle();yield return new WaitForSecondsRealtime(.3f);
            Check("removing the turbo restores the stock voice",!game.Player.Stats.turbocharged&&!live.Voice.Turbocharged);
            bool swapOwned=save.ownedParts.Contains("swap"),swapInstalled=save.installedParts.Contains("swap");
            if(!swapOwned)save.ownedParts.Add("swap");if(!swapInstalled)save.installedParts.Add("swap");
            game.RefreshGarageVehicle();yield return new WaitForSecondsRealtime(.3f);
            Check("1.8 L engine swap changes the Shift to four cylinders",game.Player.Stats.engineSwapped&&live.Layout.id=="inline4-swap"&&live.Layout.Cylinders==4);
            if(!swapInstalled)save.installedParts.Remove("swap");if(!swapOwned)save.ownedParts.Remove("swap");
            game.RefreshGarageVehicle();yield return new WaitForSecondsRealtime(.3f);
            Check("removing the engine swap restores the three-cylinder voice",!game.Player.Stats.engineSwapped&&live.Layout.id=="inline3");
            var physicalSource=live.GetComponent<AudioSource>();physicalSource.Stop();
            var masterBlock=new float[2048];live.Muted=false;
            for(int block=0;block<20;block++)live.RenderBlock(masterBlock,2);
            Check("physical engine generates samples with master unmuted",masterBlock.Any(sample=>Mathf.Abs(sample)>.0001f));
            live.Muted=true;live.RenderBlock(masterBlock,2);
            Check("master mute clears physical engine output samples",masterBlock.All(sample=>sample==0));
            physicalSource.Play();
        }
        static float LayerDifference(EngineLayout layout,float rpm,float throttle,bool intake)
        {
            var dry=new EngineVoice(layout,48000){IntakeLevel=0,AccessoryLevel=0,TargetRpm=rpm,Throttle=throttle,Gain=1};
            var wet=new EngineVoice(layout,48000){IntakeLevel=intake?1:0,AccessoryLevel=intake?0:1,TargetRpm=rpm,Throttle=throttle,Gain=1};
            var a=new float[2048];var b=new float[2048];double energy=0;int count=0;
            for(int block=0;block<64;block++)
            {
                dry.Render(a,2);wet.Render(b,2);
                if(block<16)continue;
                for(int i=0;i<a.Length;i+=2){float difference=b[i]-a[i];energy+=difference*difference;count++;}
            }
            return (float)Math.Sqrt(energy/count);
        }
        /// <summary>
        /// Two seconds of full throttle at 4500 rpm, then one second lifted. Returns the share of full-load energy
        /// above about 2 kHz, plus the turbo shaft speed at the end of the pull and after the lift.
        /// </summary>
        static float HighBandShare(EngineVoice voice,out float boostSpool,out float liftSpool)
        {
            var block=new float[2048];double low=0,high=0;float lowPass=0;boostSpool=0;
            // Whistle muted: this measures the turbine's effect on the exhaust pulses, not the compressor's whine.
            voice.Gain=1;voice.TargetRpm=4500;voice.Throttle=1;voice.TurboLevel=0;
            for(int frame=0;frame<96000;frame+=1024)
            {
                voice.Render(block,2);
                if(frame>=48000)for(int i=0;i<1024;i++){float x=block[2*i];lowPass+=(x-lowPass)*.23f;low+=lowPass*lowPass;high+=(x-lowPass)*(x-lowPass);}
            }
            boostSpool=voice.Spool;voice.Throttle=0;
            for(int frame=0;frame<48000;frame+=1024)voice.Render(block,2);
            liftSpool=voice.Spool;
            return (float)(high/Math.Max(1e-12,low+high));
        }
        /// <summary>
        /// Biome art keeps the game's rules: every tree topples when shot, small plants and stones shatter when a car drives
        /// into them without stopping it, and formations (mesas, buttes, arch legs, ridges, the boundary rim, cover-size
        /// base rocks) stay impassable when driven straight at. Uses real driving: the car is aimed and the throttle held.
        /// </summary>
        IEnumerator TestAssetBehaviour()
        {
            var game=GameManager.Instance;
            game.StartCampaign(173,1600);
            yield return new WaitUntil(()=>game.State==GameState.Playing&&GeneratedWorld.Active);
            yield return new WaitForSecondsRealtime(1);
            var world=GeneratedWorld.Active;var camera=CameraController.Instance;var keyboard=InputSystem.AddDevice<Keyboard>();
            float span=world.WorldBounds.size.x;

            // Trees topple when shot.
            var trees=new (string name,Vector3? where)[]{("Breakable ponderosa",world.FindBiomeSpot(Biome.Forest)),("Breakable aspen",world.FindBiomeSpot(Biome.Forest)),
                ("Breakable snowy pine",world.PeaksCentre+new Vector3(0,0,-span*.03f)),("Breakable juniper",world.FindBiomeSpot(Biome.Plateau)),
                ("Breakable mesquite",world.FindBiomeSpot(Biome.Sonoran)),("Breakable snag",world.FindBiomeSpot(Biome.RedRock))};
            foreach(var (name,where) in trees)
            {
                if(where==null){Check(name+" biome present",false);continue;}
                yield return GoTo(where.Value);
                var tree=NearestProp(name,game.Player.transform.position,140);
                if(!tree){Check(name+" spawns in its biome",false);continue;}
                Vector3 start=tree.transform.position;int shots=0;
                while(!tree.IsDestroyed&&shots<80){tree.ApplyProjectileHit(8,start+Vector3.up*1.2f,game.Player.gameObject,Vector3.right);shots++;}
                var body=tree?tree.GetComponent<Rigidbody>():null;var look=tree?tree.GetComponentInChildren<Renderer>():null;
                bool falling=body&&!body.isKinematic&&look&&look.enabled;
                yield return new WaitForSecondsRealtime(.9f);
                float tilt=tree?Vector3.Angle(tree.transform.up,Vector3.up):90;
                Debug.Log("MIA_ASSET_TREE "+name+": shots="+shots+" falling="+falling+" tilt="+tilt.ToString("0"));
                Check(name+" topples when shot",falling&&tilt>25);
            }

            // Small plants and stones shatter when driven into, without stopping the car.
            var small=new (string name,Vector3? where)[]{("Breakable saguaro",world.FindBiomeSpot(Biome.Sonoran)),("Breakable prickly pear",world.FindBiomeSpot(Biome.RedRock)),
                ("Breakable agave",world.FindBiomeSpot(Biome.Plateau)),("Breakable scenery rock",world.FindBiomeSpot(Biome.RedRock))};
            foreach(var (name,where) in small)
            {
                if(where==null)continue;
                yield return GoTo(where.Value);
                var prop=NearestProp(name,game.Player.transform.position,140,p=>ClearRunUp(p.transform.position,9,p)!=null);
                if(!prop){Check(name+" found with a clear run-up",false);continue;}
                Vector3 target=prop.transform.position;Vector3 dir=ClearRunUp(target,9,prop).Value;
                float minDistance=float.MaxValue,travelled=0;
                yield return Drive(target-dir*9,dir,14,1.3f,keyboard,car=>{minDistance=Mathf.Min(minDistance,Flat(car-target));});
                travelled=Vector3.Dot(game.Player.transform.position-(target-dir*9),dir);
                bool broke=!prop||prop.IsDestroyed;bool shattered=broke&&(!prop||!prop.GetComponent<Rigidbody>());
                Debug.Log("MIA_ASSET_SMALL "+name+": broke="+broke+" travelled="+travelled.ToString("0.0")+" speed="+game.Player.Body.linearVelocity.magnitude.ToString("0.0"));
                Check(name+" shatters when driven into and the car carries on",shattered&&travelled>11);
            }

            // Formations stay impassable.
            var circles=new List<Vector3>();
            foreach(var wanted in new[]{GeneratedWorld.LandformKind.Mesa,GeneratedWorld.LandformKind.Butte,GeneratedWorld.LandformKind.Arch,GeneratedWorld.LandformKind.Cliff})
            {
                int tested=0,breached=0;
                for(int i=0,tries=0;i<world.LandformEntries&&tested<2&&tries<8;i++)
                {
                    world.LandformCenter(i,out var kind,out _);if(kind!=wanted)continue;
                    world.Footprints(i,circles);
                    int k=circles.Count/2;Vector2 c=new Vector2(circles[k].x,circles[k].y);float r=circles[k].z;
                    // Arch: hit a leg from the outside. Ridge: square on to the spine. Others: from the map centre's side.
                    Vector2 outward=wanted==GeneratedWorld.LandformKind.Arch?(c-new Vector2(circles[1-k].x,circles[1-k].y)).normalized
                        :wanted==GeneratedWorld.LandformKind.Cliff&&circles.Count>2?Perpendicular(circles,k):(-c).normalized;
                    var result=new float[1];
                    // Try the planned side, then the other three, until one run actually reaches the rock.
                    for(int turn=0;turn<4;turn++)
                    {
                        tries++;
                        Vector2 side=turn==0?outward:turn==1?-outward:turn==2?new Vector2(-outward.y,outward.x):new Vector2(outward.y,-outward.x);
                        if(wanted==GeneratedWorld.LandformKind.Arch&&turn>0)break; // other sides of a leg face the opening
                        yield return RamFormation(c,r,side,keyboard,result);
                        Debug.Log("MIA_ASSET_SOLID "+wanted+" #"+i+" side "+turn+": radius="+r.ToString("0.0")+" closest="+(result[0]<0?"never reached":result[0].ToString("0.0")));
                        if(result[0]>=0)break;
                    }
                    if(result[0]<0)continue; // blocked before reaching it from every side: inconclusive
                    tested++;if(result[0]<r*.45f)breached++;
                }
                Check(wanted+" stays impassable when rammed",tested>0&&breached==0);
            }
            // The boundary rim, from inside the map.
            {
                world.Footprints(0,circles,true);int k=circles.Count/2;Vector2 c=new Vector2(circles[k].x,circles[k].y);float r=circles[k].z;
                var result=new float[1];
                yield return RamFormation(c,r,(-c).normalized,keyboard,result);
                Debug.Log("MIA_ASSET_SOLID boundary: radius="+r.ToString("0.0")+" closest="+result[0].ToString("0.0")+" inside map="+GeneratedWorld.Contains(game.Player.transform.position));
                Check("boundary rim stays impassable when rammed",result[0]>=r*.45f&&GeneratedWorld.Contains(game.Player.transform.position));
            }
            // Cover-size base rocks at the foot of the mountains are solid cover, not break-away props.
            {
                Transform rock=null;float best=float.MaxValue;Vector3 from=game.Player.transform.position;
                foreach(var t in world.GetComponentsInChildren<Transform>())
                    if(t.name=="Mountain base rock"&&t.parent&&t.parent.name=="Mountain chain"){float d=(t.position-from).sqrMagnitude;if(d<best&&ClearRunUp(t.position,10,null)!=null){best=d;rock=t;}}
                if(rock)
                {
                    var rs=rock.GetComponentsInChildren<Renderer>();Bounds b=rs[0].bounds;foreach(var x in rs)b.Encapsulate(x.bounds);
                    float r=Mathf.Max(b.extents.x,b.extents.z);var result=new float[1];
                    Vector2 c=new Vector2(b.center.x,b.center.z);
                    yield return RamFormation(c,r,ClearRunUp(rock.position,10,null).Value is Vector3 d?new Vector2(-d.x,-d.z):Vector2.right,keyboard,result);
                    Debug.Log("MIA_ASSET_SOLID base rock: radius="+r.ToString("0.0")+" closest="+result[0].ToString("0.0"));
                    Check("mountain base rocks are solid cover",rock&&result[0]>=r*.45f);
                }
                else Check("mountain base rock found",false);
            }
            InputSystem.RemoveDevice(keyboard);
        }
        /// <summary>
        /// Escort (convoy) jobs: each planned leg is compared with the straight line between its ends and checked for
        /// doubling back, then the van actually drives the job (the player kept beside it, hostiles held still) and its
        /// track is checked for loops: coming back within 20 m of where it was more than 25 s earlier.
        /// </summary>
        IEnumerator TestEscortRoute()
        {
            var game=GameManager.Instance;game.Save.unlockedMission=14;
            // One mission per run keeps the suite inside the runner's time limit: -miaEscortMission=8 picks the other.
            string only=Array.Find(Environment.GetCommandLineArgs(),a=>a.StartsWith("-miaEscortMission="));
            // Always the same map. The world config persists between runs, and a suite run just before (the mountain
            // suite ends on a 4.8 km map) used to leave legs three times longer than the time limit allows.
            // -miaEscortSize=3200 drives the same seed on a larger map, with the time limit scaled to match.
            string sizeArg=Array.Find(Environment.GetCommandLineArgs(),a=>a.StartsWith("-miaEscortSize="));
            float mapSize=sizeArg!=null?float.Parse(sizeArg.Substring(15),System.Globalization.CultureInfo.InvariantCulture):1600;
            game.WorldConfig.seed=173;game.WorldConfig.size=mapSize;WorldConfigStore.Save(game.WorldConfig);
            float timeLimit=150*Mathf.Max(1,mapSize/1600);
            foreach(int mission in new[]{only!=null?int.Parse(only.Substring(18)):3})
            {
                game.StartMission(mission);
                yield return new WaitUntil(()=>game.State==GameState.Playing&&GeneratedWorld.Active);
                yield return new WaitForSecondsRealtime(.5f);
                VehicleController van=null;
                foreach(var v in VehicleController.Active){var ai0=v?v.GetComponent<EnemyAI>():null;if(ai0&&ai0.IsFriendly){van=v;break;}}
                if(!van){Check("mission "+mission+" escort van spawns",false);continue;}
                var ai=van.GetComponent<EnemyAI>();
                var track=new List<(Vector3 p,float t,int leg)>();float start=Time.time;int loops=0,legs=0,worstLeg=0;float worstRatio=0;Vector3 lastGoal=Vector3.positiveInfinity;
                while(Time.time-start<timeLimit&&game.State==GameState.Playing&&game.Mission.Stage==0&&van&&!van.Damage.IsDead)
                {
                    QuietMissionHostiles();van.Damage.Repair(10000);
                    // Keep the player alongside so the van never waits to regroup.
                    Vector3 side=van.transform.position-van.transform.right*7;side.y=GeneratedWorld.HeightAt(side)+1;
                    if(Vector3.Distance(game.Player.transform.position,van.transform.position)>20)Teleport(side);
                    if(Flat(ai.Destination-lastGoal)>5&&ai.Route.Count>1)
                    {
                        // A new leg: measure the planned route.
                        lastGoal=ai.Destination;legs++;
                        float length=0;for(int i=1;i<ai.Route.Count;i++)length+=Flat(ai.Route[i]-ai.Route[i-1]);
                        float straight=Flat(ai.Destination-van.transform.position);float ratio=length/Mathf.Max(1,straight);
                        int doubles=0;float along=0;var arc=new float[ai.Route.Count];
                        for(int i=1;i<ai.Route.Count;i++){along+=Flat(ai.Route[i]-ai.Route[i-1]);arc[i]=along;}
                        for(int i=0;i<ai.Route.Count;i++)for(int j=i+1;j<ai.Route.Count;j++)if(arc[j]-arc[i]>120&&Flat(ai.Route[j]-ai.Route[i])<25){doubles++;break;}
                        Debug.Log("MIA_ESCORT_LEG mission "+mission+" leg "+legs+": straight="+straight.ToString("0")+" planned="+length.ToString("0")+" ratio="+ratio.ToString("0.00")+" doubling-back points="+doubles);
                        if(ratio>worstRatio){worstRatio=ratio;worstLeg=legs;}
                    }
                    Vector3 here=van.transform.position;
                    // Circling is a return to the same spot on the same leg. A new leg may rightly drive back down the road
                    // the last one came in on (a drop at the end of a spur or a town at the end of the highway).
                    foreach(var (p,t,leg) in track)if(leg==legs&&Time.time-t>25&&Flat(here-p)<20){loops++;if(loops<=3)Debug.Log("MIA_ESCORT_LOOP t="+(Time.time-start).ToString("0")+" at "+here.ToString("0")+" was here at t="+(t-start).ToString("0")+" goal="+ai.Destination.ToString("0")+" leg="+legs);break;}
                    if(Array.IndexOf(Environment.GetCommandLineArgs(),"-miaEscortTrace")>=0&&track.Count%4==0){float nearest=float.MaxValue;int at=-1;for(int i=0;i<ai.Route.Count;i++){float d=Flat(ai.Route[i]-here);if(d<nearest){nearest=d;at=i;}}Debug.Log("MIA_ESCORT_TRACE t="+(Time.time-start).ToString("0")+" pos="+here.ToString("0")+" toGoal="+Flat(ai.Destination-here).ToString("0")+" speed="+van.Body.linearVelocity.magnitude.ToString("0")+" nearestRoute="+at+"/"+ai.Route.Count+" off="+nearest.ToString("0")+" hold="+ai.HoldPosition+" fwd="+van.transform.forward.ToString("0.0")+" "+ai.SteerDebug+" avoid="+ai.AvoidDebug);}
                    track.Add((here,Time.time,legs));
                    yield return new WaitForSecondsRealtime(.5f);
                }
                float driven=0;for(int i=1;i<track.Count;i++)driven+=Flat(track[i].p-track[i-1].p);
                Debug.Log("MIA_ESCORT mission "+mission+" on "+mapSize.ToString("0")+" m: legs="+legs+" stage="+game.Mission.Stage+" seconds="+(Time.time-start).ToString("0")+" driven="+driven.ToString("0")+" loop samples="+loops+" worst planned ratio="+worstRatio.ToString("0.00")+" (leg "+worstLeg+")");
                Check("mission "+mission+" escort completes its transfers",game.Mission.Stage>=1);
                Check("mission "+mission+" escort never loops back over its own track",loops==0);
                Check("mission "+mission+" escort routes stay direct (planned at most 1.6x the straight line)",worstRatio<=1.6f);
                game.ReturnToGarage();yield return new WaitForSecondsRealtime(.5f);
            }
        }
        IEnumerator GoTo(Vector3 spot)
        {
            QuietMissionHostiles();
            spot=GeneratedWorld.Active.ClearOfObstacles(spot,4);spot.y=GeneratedWorld.HeightAt(spot)+1;
            Teleport(spot);CameraController.Instance.Snap();
            yield return new WaitForSecondsRealtime(3f); // streamed scenery builds round the car
        }
        static float Flat(Vector3 v){v.y=0;return v.magnitude;}
        static Vector2 Perpendicular(List<Vector3> circles,int k)
        {
            Vector2 tangent=new Vector2(circles[k+1].x-circles[k-1].x,circles[k+1].y-circles[k-1].y).normalized;
            Vector2 side=new Vector2(-tangent.y,tangent.x);
            // Pick the side towards the map centre (more likely open ground).
            return Vector2.Dot(side,-new Vector2(circles[k].x,circles[k].y))>0?side:-side;
        }
        static DestructionSystem NearestProp(string name,Vector3 from,float within,Func<DestructionSystem,bool> accept=null)
        {
            DestructionSystem best=null;float nearest=within*within;
            foreach(var prop in FindObjectsByType<DestructionSystem>(FindObjectsSortMode.None))
            {
                if(!prop||prop.IsDestroyed||prop.name!=name)continue;
                float d=(prop.transform.position-from).sqrMagnitude;
                if(d<nearest&&(accept==null||accept(prop))){nearest=d;best=prop;}
            }
            return best;
        }
        /// <summary>A flat-ish approach direction with nothing solid (other than the target) in the last <paramref name="length"/> metres.</summary>
        static Vector3? ClearRunUp(Vector3 target,float length,DestructionSystem self)
        {
            for(int a=0;a<12;a++)
            {
                float angle=a*Mathf.PI/6;Vector3 dir=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
                Vector3 start=target-dir*length;
                if(!GeneratedWorld.Contains(start)||Mathf.Abs(GeneratedWorld.HeightAt(start)-GeneratedWorld.HeightAt(target))>2.5f)continue;
                bool clear=true;
                foreach(var hit in Physics.SphereCastAll(start+Vector3.up*1.4f,1.1f,dir,length-1.5f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
                {
                    if(!hit.collider||hit.collider.gameObject.layer==DestructionSystem.BrittleLayer)continue;
                    if(hit.collider.GetComponentInParent<VehicleController>())continue;
                    if(self&&hit.collider.transform.IsChildOf(self.transform))continue;
                    if(hit.collider.transform.parent&&hit.collider.transform.parent.name=="Chunked terrain")continue;
                    clear=false;break;
                }
                if(clear)return dir;
            }
            return null;
        }
        /// <summary>Places the car at <paramref name="from"/> facing <paramref name="dir"/>, at speed with the throttle held.</summary>
        IEnumerator Drive(Vector3 from,Vector3 dir,float speed,float seconds,Keyboard keyboard,Action<Vector3> track)
        {
            var player=GameManager.Instance.Player;
            from.y=GeneratedWorld.HeightAt(from)+.9f;Teleport(from);
            var facing=Quaternion.LookRotation(dir,Vector3.up);player.Body.rotation=facing;player.transform.rotation=facing;
            yield return new WaitForFixedUpdate();
            player.Body.linearVelocity=dir*speed;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W));
            for(float t=0;t<seconds;t+=Time.fixedDeltaTime){yield return new WaitForFixedUpdate();track(player.transform.position);}
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());
        }
        /// <summary>
        /// Drives at a footprint (centre <paramref name="c"/>, radius <paramref name="r"/>) from <paramref name="outward"/>.
        /// result[0] = closest the car's centre came to it, or -1 when the car never got within 4 m of its edge.
        /// </summary>
        IEnumerator RamFormation(Vector2 c,float r,Vector2 outward,Keyboard keyboard,float[] result)
        {
            Vector3 centre=new Vector3(c.x,0,c.y),dir=new Vector3(-outward.x,0,-outward.y);
            Vector3 start=centre-dir*(r+14);
            yield return GoTo(start);
            float closest=float.MaxValue;
            yield return Drive(start,dir,16,2.6f,keyboard,car=>closest=Mathf.Min(closest,Flat(car-centre)));
            result[0]=closest<=r+4?closest:-1;
        }
        /// <summary>Steady-state autocorrelation has a peak within 3% of the firing interval.</summary>
        static bool FiringPeriodPresent(EngineLayout layout,float rpm)
        {
            const int rate=48000;
            var voice=new EngineVoice(layout,rate){TargetRpm=rpm,Throttle=1,Gain=1,Variation=0};
            var block=new float[2048];var mono=new float[rate/2];
            for(int i=0;i<40;i++)voice.Render(block,2);
            for(int w=0;w<mono.Length;){voice.Render(block,2);for(int i=0;i<1024&&w<mono.Length;i++)mono[w++]=block[2*i]+block[2*i+1];}
            float period=rate*120f/(rpm*layout.Cylinders);
            double zero=0;foreach(float v in mono)zero+=v*v;
            double best=0;
            for(int lag=Mathf.FloorToInt(period*.97f);lag<=Mathf.CeilToInt(period*1.03f);lag++)
            {double r=0;for(int i=0;i+lag<mono.Length;i++)r+=mono[i]*mono[i+lag];best=Math.Max(best,r/zero);}
            return best>.4;
        }
        static void WriteWav(string path,float[] stereo,int rate)
        {
            using var writer=new BinaryWriter(File.Create(path));
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+stereo.Length*2);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);writer.Write((short)1);writer.Write((short)2);
            writer.Write(rate);writer.Write(rate*4);writer.Write((short)4);writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));writer.Write(stereo.Length*2);
            foreach(float v in stereo)writer.Write((short)Mathf.Clamp(Mathf.RoundToInt(v*32767),-32768,32767));
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
            Check("junkyard reinforcement adds twelve hostiles ("+enemies.Count+" hostiles, stage "+game.Mission.Stage+", "+game.Mission.Objective+")",enemies.Count==16&&game.Mission.Stage==1&&game.Mission.Objective.Contains("/ 12"));
            game.Resume();for(int i=0;i<3;i++)game.Mission.RegisterKill();game.Mission.Tick(.01f);game.Pause();
            Check("old three-kill gate no longer clears junkyard",game.Mission.Stage==1);

            int convoy=Array.FindIndex(ContentCatalog.Missions,m=>m.mode==MissionMode.Convoy);
            game.StartMission(convoy);game.Pause();yield return null;
            var friend=Array.Find(FindObjectsByType<EnemyAI>(FindObjectsSortMode.None),ai=>ai.IsFriendly);
            var escort=friend?friend.GetComponent<VehicleController>():null;
            Check("escort has reinforced health while its hostile screen quadruples",escort&&Mathf.Approximately(escort.Damage.MaxHealth,Mathf.Max(60,escort.Stats.maxHealth)*2.5f)&&BalanceHostiles().Count==12);

            int bossMission=Array.FindIndex(ContentCatalog.Missions,m=>m.mode==MissionMode.Boss);
            game.StartMission(bossMission);game.Pause();yield return null;
            enemies=BalanceHostiles();FreezeBalanceEnemies(enemies);
            int expected=Mathf.Max(3,ContentCatalog.Missions[bossMission].enemyCount/3)*4;
            Check("boss security screen is four times larger ("+enemies.Count+" of "+expected+")",enemies.Count==expected);
            game.Resume();DestroyHostiles();game.Mission.Tick(.01f);game.Pause();
            enemies=BalanceHostiles();FreezeBalanceEnemies(enemies);
            var bosses=enemies.FindAll(v=>v.GetComponent<EnemyAI>().Archetype==7);
            Check("unique command rig arrives with three companions ("+bosses.Count+" rigs, "+enemies.Count+" hostiles)",bosses.Count==1&&enemies.Count==4);
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
        IEnumerator TestSoundtrack(bool withDeathTracks)
        {
            var game=GameManager.Instance;
            var music=game.GetComponent<MusicManager>();
            var tracks=Resources.LoadAll<AudioClip>("Audio/Music");
            Check("expected soundtrack recordings packaged",tracks.Length==(withDeathTracks?16:14)&&(!withDeathTracks||Array.Exists(tracks,c=>c.name=="Arizona Highlands")&&Array.Exists(tracks,c=>c.name=="Arizona Lowlands")));
            bool imported=Array.TrueForAll(tracks,c=>c&&c.loadType==AudioClipLoadType.Streaming&&c.preloadAudioData&&c.channels==2&&c.frequency==48000&&c.length>30);
            if(!imported)foreach(var clip in tracks)Debug.Log("MIA_MUSIC_IMPORT: "+clip.name+" type="+clip.loadType+" preload="+clip.preloadAudioData+" state="+clip.loadState+" channels="+clip.channels+" frequency="+clip.frequency+" length="+clip.length);
            Check("all soundtrack streams preload at original stereo sample rate",imported);
            yield return new WaitForSecondsRealtime(3.2f);
            var source=SoundtrackSource(music);
            Check("garage plays looping Arizonaland",music.CurrentTrack=="Arizonaland"&&source&&source.isPlaying&&source.loop&&source.time>0);
            game.ShowMainMenu();yield return new WaitForSecondsRealtime(3.2f);
            source=SoundtrackSource(music);
            Check("main menu plays looping Arizona Nation",music.CurrentTrack=="Arizona Nation"&&source&&source.isPlaying&&source.loop);
            game.StartCombatTrial();
            foreach(var ai in FindObjectsByType<EnemyAI>(FindObjectsSortMode.None)){ai.enabled=false;ai.GetComponent<VehicleController>().Body.isKinematic=true;}
            yield return new WaitForSecondsRealtime(3.2f);
            Check("combat excludes both death-screen songs",music.CurrentTrack!="Arizonaland"&&music.CurrentTrack!="Arizona Nation"&&music.CurrentTrack!="Arizona Highlands"&&music.CurrentTrack!="Arizona Lowlands"&&Array.Exists(tracks,c=>c.name==music.CurrentTrack));
            Check("music has priority over dense combat effects",SoundtrackSource(music).priority==0);
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
            if(withDeathTracks)
            {
                game.Player.Damage.ApplyDamage(game.Player.Damage.MaxHealth*2,game.Player.transform.position,null);
                yield return new WaitForSecondsRealtime(.3f);
                string firstDeathTrack=music.CurrentTrack;
                Check("death overlay immediately starts one reserved song",game.Dying&&(firstDeathTrack=="Arizona Highlands"||firstDeathTrack=="Arizona Lowlands")&&SoundtrackSource(music).isPlaying);
                yield return new WaitForSecondsRealtime(3.1f);
                Check("death-screen music continues through failure debrief",game.State==GameState.Lost&&music.CurrentTrack==firstDeathTrack&&SoundtrackSource(music).isPlaying);
                game.RetryMission();yield return new WaitForSecondsRealtime(.2f);
                Check("retry leaves death-only playlist",game.State==GameState.Playing&&music.CurrentTrack!="Arizona Highlands"&&music.CurrentTrack!="Arizona Lowlands");
                game.Player.Damage.ApplyDamage(game.Player.Damage.MaxHealth*2,game.Player.transform.position,null);
                yield return new WaitForSecondsRealtime(.3f);
                Check("second death plays the other reserved song",game.Dying&&music.CurrentTrack!=firstDeathTrack&&(music.CurrentTrack=="Arizona Highlands"||music.CurrentTrack=="Arizona Lowlands"));
            }
            else
            {
                game.Player.Damage.ApplyDamage(game.Player.Damage.MaxHealth*2,game.Player.transform.position,null);
                yield return new WaitForSecondsRealtime(.3f);
                Check("death screen does not retain combat music without reserved recordings",game.Dying&&music.CurrentTrack=="");
            }
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
            var urp=UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            int lowAtlas=urp?urp.additionalLightsShadowmapResolution:0;
            game.Save.settings.quality=2;game.ApplySettings();
            Check("effect-light shadow atlas is 4096 on Medium and 1024 on Simple",urp&&urp.additionalLightsShadowmapResolution==4096&&lowAtlas==1024);
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
            // The car starts this check with an empty field slot, so the nearest weapon drop flies in and equips.
            Check("empty field slot pulls in and equips the nearest weapon drop",!weapon&&player.Weapons.FieldWeapon&&player.Weapons.FieldWeapon.id=="invoice");
            if(nitro)Destroy(nitro.gameObject);if(weapon)Destroy(weapon.gameObject);
            // With a different field weapon held, a drop stays put: swapping is a deliberate choice.
            var other=CombatPickup.Create(PickupKind.Weapon,player.transform.position+Vector3.right*10+Vector3.forward*2,WeaponRules.Find("grenade"),2);
            Vector3 otherStart=other.transform.position;yield return new WaitForSeconds(1.05f);
            Check("weapon stays anchored outside contact range while a different field weapon is held",other&&Vector2.Distance(new Vector2(other.transform.position.x,other.transform.position.z),new Vector2(otherStart.x,otherStart.z))<.01f);
            if(other)Destroy(other.gameObject);
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
        /// <summary>
        /// Clicks Suzuki's head and her bowl with an injected mouse while the garage interface is up: a heart for the
        /// head, a bone for the bowl, and nothing for a click that lands on a panel or on empty air.
        /// </summary>
        IEnumerator TestSuzukiTreats()
        {
            var game=GameManager.Instance;
            var dog=Array.Find(FindObjectsByType<SuzukiDog>(FindObjectsSortMode.None),d=>!d.IsRiding);
            Check("garage Suzuki has a bowl",dog&&dog.Bowl);
            if(!dog||!dog.Bowl)yield break;
            var camera=Camera.main;var rig=CameraController.Instance;rig.enabled=false;camera.orthographic=false;camera.fieldOfView=45;
            var mouse=InputSystem.AddDevice<Mouse>();
            // An eye point 3.5 m from the target, above it, with nothing solid in between: she wanders, and the car,
            // the lift and the office furniture all take clicks meant for her.
            Vector3 EyeFor(Vector3 target)
            {
                for(int i=0;i<12;i++)
                {
                    float angle=(200+i*30)*Mathf.Deg2Rad;
                    Vector3 eye=target+new Vector3(Mathf.Sin(angle)*2.9f,1.9f,Mathf.Cos(angle)*2.9f);
                    if(!Physics.CheckSphere(eye,.35f)&&!Physics.Linecast(eye,target+(eye-target).normalized*.6f))return eye;
                }
                return target+new Vector3(0,3.4f,-.6f);
            }
            IEnumerator Press(Vector2 pointer)
            {
                InputSystem.QueueStateEvent(mouse,new MouseState{position=pointer});yield return null;
                InputSystem.QueueStateEvent(mouse,new MouseState{position=pointer}.WithButton(MouseButton.Left));yield return null;yield return null;
                InputSystem.QueueStateEvent(mouse,new MouseState{position=pointer});yield return null;
            }
            // Looks straight at a target, which puts it in the open middle of the garage screen, and clicks it. Tilted
            // down, the same view lifts the target under the bar across the top of the screen instead.
            IEnumerator Click(Func<Vector3> target,float tilt)
            {
                Vector3 at=target(),eye=EyeFor(at);
                camera.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(at-eye)*Quaternion.Euler(tilt,0,0));
                yield return null;yield return null;
                yield return Press(camera.WorldToScreenPoint(target()));
            }
            Vector3 bowl=dog.Bowl.position;

            yield return Click(()=>dog.HeadPosition,0);
            Vector2 middle=new Vector2(Screen.width*.5f,Screen.height*.5f);
            Check("clicks reach the shop between the panels, not through them",GameUI.ShopUnderPointer(middle)&&
                !GameUI.ShopUnderPointer(new Vector2(Screen.width*.5f,Screen.height-20))&&!GameUI.ShopUnderPointer(new Vector2(Screen.width*.12f,Screen.height*.5f))&&
                !GameUI.ShopUnderPointer(new Vector2(Screen.width*.5f,20)));
            Check("a click on Suzuki's head raises a heart",dog.HeartsGiven==1&&dog.HeartsShowing==1&&dog.IsFussed&&
                VehicleController.FindChild(game.World.transform,"Suzuki heart"));
            yield return new WaitForSecondsRealtime(.45f);Capture("suzuki-heart");
            yield return new WaitForSecondsRealtime(1.6f);
            Check("the heart floats off and is gone",dog.HeartsShowing==0&&!VehicleController.FindChild(game.World.transform,"Suzuki heart"));

            // The same click with her head under the top bar belongs to the bar.
            Vector3 headAt=dog.HeadPosition,headEye=EyeFor(headAt);
            camera.transform.SetPositionAndRotation(headEye,Quaternion.LookRotation(headAt-headEye)*Quaternion.Euler(20.5f,0,0));
            yield return null;yield return null;
            Vector3 covered=camera.WorldToScreenPoint(dog.HeadPosition);
            yield return Press(covered);
            Check("a click on her head under a panel does nothing (pointer "+covered.y.ToString("0")+" of "+Screen.height+")",
                covered.y>Screen.height*.9f&&!GameUI.ShopUnderPointer(covered)&&dog.HeartsGiven==1);

            yield return Click(()=>bowl,0);
            Check("a click on her bowl sends a bone down",dog.BonesGiven==1&&VehicleController.FindChild(game.World.transform,"Suzuki bone")&&dog.NewestBone.y>bowl.y+.5f);
            yield return new WaitForSecondsRealtime(.42f);Capture("suzuki-bone-falling");
            float deadline=Time.realtimeSinceStartup+4;
            while(dog.BonesInBowl<1&&Time.realtimeSinceStartup<deadline)yield return null;
            Vector3 bone=dog.NewestBone;
            Check("the bone comes to rest in the bowl ("+(bone-bowl).ToString("0.00")+" from its centre)",dog.BonesInBowl==1&&
                new Vector2(bone.x-bowl.x,bone.z-bowl.z).magnitude<.2f&&bone.y>bowl.y&&bone.y<bowl.y+.3f);
            // The capture is taken at the end of a later frame: hold the view until it has been.
            yield return new WaitForSecondsRealtime(.3f);Capture("suzuki-bone");yield return new WaitForSecondsRealtime(.4f);

            // Open sky: in the shop, but on nothing.
            camera.transform.SetPositionAndRotation(new Vector3(0,3,-9),Quaternion.LookRotation(new Vector3(0,1,-.4f)));
            yield return null;yield return null;
            yield return Press(middle);
            Check("a click on nothing gives nothing",GameUI.ShopUnderPointer(middle)&&dog.HeartsGiven==1&&dog.BonesGiven==1);

            // Five fill the bowl; the sixth empties it first.
            for(int i=0;i<4;i++)dog.Treat();
            deadline=Time.realtimeSinceStartup+4;
            while(dog.BonesInBowl<5&&Time.realtimeSinceStartup<deadline)yield return null;
            bool full=dog.BonesInBowl==5;
            dog.Treat();
            deadline=Time.realtimeSinceStartup+4;
            while(dog.BonesInBowl<1&&Time.realtimeSinceStartup<deadline)yield return null;
            yield return new WaitForSecondsRealtime(.2f);
            Check("the bowl holds five bones and is emptied for the sixth",full&&dog.BonesInBowl==1&&dog.BonesGiven==6);
            // A bowlful from straight overhead, where she cannot wander in front of the lens.
            for(int i=0;i<4;i++)dog.Treat();
            camera.transform.SetPositionAndRotation(bowl+new Vector3(0,1.5f,-.3f),Quaternion.LookRotation(new Vector3(0,-1.5f,.3f)));
            yield return new WaitForSecondsRealtime(1.6f);Capture("suzuki-bowl-full");yield return new WaitForSecondsRealtime(.4f);
            InputSystem.RemoveDevice(mouse);rig.enabled=true;
        }
        IEnumerator TestTransmissionAndProgressReset()
        {
            var game=GameManager.Instance;
            var vehicles=ContentCatalog.Vehicles;
            string[] ids={"ronin","juniper","sunskip","foreclosure","sidehustle","perennial","skitter","vincent",
                "shift","monsoon","sonora","dustback","monument","executive","heat","citizen","roam4",
                "ae867","espresso","copperado","contender","scarface"};
            int[] expected={5,5,6,6,4,4,6,6,5,5,4,5,4,4,5,5,4,5,5,4,4,5};
            bool valid=vehicles.Length==ids.Length;
            for(int i=0;i<ids.Length;i++)
            {
                var vehicle=Array.Find(vehicles,v=>v.id==ids[i]);
                var ratios=TransmissionProfiles.ForVehicle(ids[i]);
                valid&=vehicle&&ratios.Length==expected[i]&&TransmissionProfiles.TachScale(vehicle,ratios)>0;
                for(int gear=1;gear<ratios.Length;gear++)valid&=ratios[gear]<ratios[gear-1];
            }
            Check("all twenty-two vehicles have ordered, period-appropriate forward gears",valid);
            Check("gear profiles leave the original mechanical acceleration curve intact",
                Mathf.Approximately(TransmissionProfiles.DriveRatio(1),3.5f) &&
                Mathf.Approximately(TransmissionProfiles.DriveRatio(4),1.12f) &&
                Mathf.Approximately(TransmissionProfiles.DriveRatio(6),.68f));
            Check("upshift sound lifts throttle, holds overrun, then restores it",
                Mathf.Approximately(VehicleController.ShiftSoundEnvelope(0),1f) &&
                VehicleController.ShiftSoundEnvelope(VehicleController.UpshiftDuration-.10f)<.1f &&
                VehicleController.ShiftSoundEnvelope(.05f)>.5f);
            var shift=Array.Find(vehicles,v=>v.id=="shift");
            var stock=VehicleStats.From(shift);
            bool playerPower=true,allGrip=true;
            foreach(var car in vehicles)
            {
                var baseline=VehicleStats.From(car);
                var playable=GarageManager.StatsFor(car,null);
                playerPower&=Mathf.Approximately(playable.horsepower,baseline.horsepower*1.2f) &&
                    Mathf.Approximately(playable.torque,baseline.torque*1.2f);
                allGrip&=Mathf.Approximately(baseline.grip,car.grip*1.1f) &&
                    Mathf.Approximately(playable.grip,baseline.grip);
            }
            Check("every playable car has 20% more stock power and torque",playerPower);
            Check("every car has 10% more stock grip",allGrip);
            Check("stock power and road-speed boost reaches the unmodified Shift",
                stock.horsepower>shift.horsepower*1.17f&&stock.torque>shift.torque*1.17f&&stock.maxSpeed>shift.maxSpeed*1.14f);

            game.Save.selectedVehicle=Array.FindIndex(vehicles,v=>v.id=="shift");
            game.Save.settings.dev=new DevTuning();DevTuning.Apply();
            game.StartCampaign(173,1600);yield return new WaitUntil(()=>game.State==GameState.Playing);
            yield return new WaitForSecondsRealtime(.5f);
            QuietMissionHostiles();
            var player=game.Player;
            if(!player){Check("stock Shift spawns for the driving probe",false);yield break;}
            var deck=GameObject.CreatePrimitive(PrimitiveType.Cube);
            deck.name="Transmission test road";
            deck.transform.position=new Vector3(0,300,-100);
            deck.transform.localScale=new Vector3(30,1,900);
            Vector3 start=new Vector3(0,301.3f,-395);
            player.Body.position=start;player.Body.rotation=Quaternion.identity;
            player.transform.SetPositionAndRotation(start,Quaternion.identity);
            player.Body.linearVelocity=Vector3.zero;player.Body.angularVelocity=Vector3.zero;
            WorldBuilder.SurfaceOverride=SurfaceKind.Asphalt;
            Physics.SyncTransforms();
            var keyboard=InputSystem.AddDevice<Keyboard>();
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W));
            float elapsed=0,shiftStarted=-1,previousRpm=player.RPM,peakSpeed=0;
            float zeroToThirty=-1,zeroToSixty=-1,lastUpshiftSpeed=0;
            var shiftSpeeds=new System.Collections.Generic.List<string>();
            int previousGear=player.Gear,upshifts=0;
            bool rpmFalls=false,longShift=false;
            while(elapsed<18f)
            {
                yield return new WaitForFixedUpdate();elapsed+=Time.fixedDeltaTime;
                if(player.Gear>previousGear)
                {
                    upshifts++;
                    rpmFalls|=previousRpm-player.RPM>450f;
                    shiftSpeeds.Add(previousGear+"→"+player.Gear+" at "+player.SpeedKph.ToString("0.0")+" km/h ("+elapsed.ToString("0.0")+" s)");
                    lastUpshiftSpeed=player.SpeedKph;
                    previousGear=player.Gear;
                }
                if(player.IsShifting&&shiftStarted<0)shiftStarted=elapsed;
                if(!player.IsShifting&&shiftStarted>=0)
                {longShift|=elapsed-shiftStarted>=.30f;shiftStarted=-1;}
                peakSpeed=Mathf.Max(peakSpeed,player.SpeedKph);
                if(zeroToThirty<0&&player.SpeedKph>=48.28f)zeroToThirty=elapsed;
                if(zeroToSixty<0&&player.SpeedKph>=96.56f)zeroToSixty=elapsed;
                previousRpm=player.RPM;
            }
            Debug.Log("MIA_TRANSMISSION: Shift reached "+peakSpeed.ToString("0.0")+" km/h (listed "+stock.maxSpeed.ToString("0.0")+"), gear "+player.Gear+"/"+player.GearCount+
                ", upshifts "+upshifts+", rev drop "+rpmFalls+", long shift "+longShift+
                ", 0-30 mph "+zeroToThirty.ToString("0.00")+" s, 0-60 mph "+zeroToSixty.ToString("0.00")+" s; "+string.Join(", ",shiftSpeeds));
            Check("stock Shift drives through all five gears instead of stopping after second",player.GearCount==5&&upshifts>=4&&player.Gear==5);
            Check("stock Shift keeps pulling in lower gears before fifth",lastUpshiftSpeed>stock.maxSpeed*.9f);
            Check("upshifts last long enough to hear and visibly drop RPM",rpmFalls&&longShift);
            Check("stock Shift exceeds its former 102 km/h ceiling without nitro",peakSpeed>108f);
            Check("stock Shift pulls gradually past its listed speed without exceeding the 20 percent extension",
                peakSpeed>stock.maxSpeed*1.06f&&peakSpeed<stock.maxSpeed*1.21f);
            float beforeNitro=player.SpeedKph,nitroPeak=beforeNitro;
            bool nitroActive=false;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W,Key.LeftShift));
            for(float t=0;t<2.5f;t+=Time.fixedDeltaTime)
            {yield return new WaitForFixedUpdate();nitroActive|=player.Boosting;nitroPeak=Mathf.Max(nitroPeak,player.SpeedKph);}
            Check("nitro continues accelerating above the listed speed",nitroActive&&nitroPeak>beforeNitro+6f);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());InputSystem.RemoveDevice(keyboard);
            WorldBuilder.SurfaceOverride=null;
            Destroy(deck);

            game.ReturnToGarage();
            game.Save.unlockedMission=5;
            game.Save.selectedVehicle=Array.FindIndex(vehicles,v=>v.id=="scarface");
            game.StartMission(5);
            Check("mountain mission keeps the selected Scarface instead of substituting Shift",
                game.Player && game.Player.Definition.id=="scarface" && game.CurrentVehicle.id=="scarface");

            game.ShowMainMenu();
            game.Save.money=4210;game.Save.salvage=325;game.Save.unlockedMission=3;
            game.Save.completedMissions.Add(0);game.Save.ownedParts.Add("cam");game.Save.installedParts.Add("cam");
            game.Save.settings.master=.37f;game.Save.settings.quality=1;game.Save.settings.dev.acceleration=2f;
            game.Save.settings.cars.Add(new CarTuning{id="shift"});
            SaveSystem.Save(game.Save);
            bool reset=game.ResetProgress();
            Check("main-menu reset restores starter campaign, garage and selected car",reset&&game.State==GameState.MainMenu&&
                game.SelectedMission==0&&game.Save.money==1100&&game.Save.salvage==0&&game.Save.unlockedMission==0&&
                game.Save.completedMissions.Count==0&&game.Save.ownedParts.Count==0&&game.Save.installedParts.Count==0&&
                game.Save.selectedVehicle==0&&game.Player&&game.Player.Definition.id=="ronin");
            Check("progress reset keeps ordinary preferences but clears custom driving tuning",
                Mathf.Abs(game.Save.settings.master-.37f)<.001f&&game.Save.settings.quality==1&&
                Mathf.Abs(game.Save.settings.dev.acceleration-1.2f)<.001f&&game.Save.settings.cars.Count==0);
            var disk=SaveSystem.Load();
            Check("reset persists and its backup cannot restore old progress",disk.money==1100&&
                disk.completedMissions.Count==0&&File.Exists(SaveSystem.Path+".bak")&&
                File.ReadAllText(SaveSystem.Path)==File.ReadAllText(SaveSystem.Path+".bak"));
        }

        IEnumerator TestShadowFilters()
        {
            var game=GameManager.Instance;
            var settings=game.Save.settings;
            var pipeline=UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            var sunData=game.Sun.GetUniversalAdditionalLightData();
            settings.shadowDetail=0;
            settings.quality=0;game.ApplySettings();
            Check("Simple disables shadows",QualitySettings.shadows==UnityEngine.ShadowQuality.Disable&&game.Sun.shadows==LightShadows.None);
            settings.quality=1;game.ApplySettings();
            Check("Low filters its 2048 sun shadow map",pipeline&&pipeline.mainLightShadowmapResolution==2048&&
                game.Sun.shadows==LightShadows.Soft&&sunData.softShadowQuality==SoftShadowQuality.Low);
            settings.quality=2;game.ApplySettings();
            Check("Medium filters its 2048 sun shadow map more widely",pipeline&&pipeline.mainLightShadowmapResolution==2048&&
                game.Sun.shadows==LightShadows.Soft&&sunData.softShadowQuality==SoftShadowQuality.Medium&&GameManager.SoftShadows);
            settings.quality=3;game.ApplySettings();
            Check("High uses filtered 4K sun shadows",pipeline&&pipeline.mainLightShadowmapResolution==4096&&game.Sun.shadows==LightShadows.Soft);
            settings.quality=0;settings.shadowDetail=1;game.ApplySettings();
            Check("4K shadow override remains available on Simple",pipeline&&pipeline.mainLightShadowmapResolution==4096&&game.Sun.shadows==LightShadows.Soft);
            settings.quality=2;settings.shadowDetail=2;game.ApplySettings();
            Check("8K shadow override keeps its widest filter",pipeline&&pipeline.mainLightShadowmapResolution==8192&&sunData.softShadowQuality==SoftShadowQuality.High);

            settings.quality=1;settings.shadowDetail=0;settings.perspectiveCamera=true;game.ApplySettings();
            game.StartCampaign(173,1600);
            yield return new WaitUntil(()=>game.State==GameState.Playing);
            QuietMissionHostiles();
            yield return new WaitForSecondsRealtime(1);
            var rig=CameraController.Instance;var view=Camera.main;
            rig.enabled=false;view.orthographic=false;view.fieldOfView=48;
            Vector3 car=game.Player.transform.position+Vector3.up;
            view.transform.position=car+new Vector3(-14,9,-12);
            view.transform.LookAt(car);
            for(int quality=1;quality<=3;quality++)
            {
                settings.quality=quality;game.ApplySettings();
                yield return new WaitForSecondsRealtime(.35f);
                SceneLuminance("shadow-"+new[]{"simple","low","medium","high"}[quality],view);
            }
        }
        IEnumerator TestDisplayAndReticle()
        {
            var game=GameManager.Instance;
            int width=Screen.width,height=Screen.height;
            FullScreenMode mode=Screen.fullScreenMode;
            game.ApplySettings();
            yield return new WaitForSecondsRealtime(.2f);
            Check("ordinary settings leave the player window unchanged",Screen.width==width&&Screen.height==height&&Screen.fullScreenMode==mode);

            game.ChooseWindowMode(true);
            yield return new WaitForSecondsRealtime(.6f);
            var workArea=Screen.mainWindowDisplayInfo.workArea;
            Check("maximized mode fits above the taskbar",Screen.fullScreenMode==FullScreenMode.MaximizedWindow&&Screen.height<=workArea.height);

            game.ChooseWindowMode(false);
            game.ChooseWindowResolution(960,540);
            yield return new WaitForSecondsRealtime(.6f);
            Check("explicit window size applies",Screen.fullScreenMode==FullScreenMode.Windowed&&Screen.width==960&&Screen.height==540);

            game.StartMission(0);
            QuietMissionHostiles();
            yield return new WaitForSecondsRealtime(1);
            var mouse=InputSystem.AddDevice<Mouse>();
            InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(Screen.width*.5f,Screen.height*.5f)});
            var camera=Camera.main;
            camera.cullingMask=0;
            camera.clearFlags=CameraClearFlags.SolidColor;
            camera.backgroundColor=Color.white;
            yield return new WaitForSecondsRealtime(.6f);
            var ui=game.GetComponent<GameUI>();
            Check("reticle turns black over a light view",ui.ReticleInkForTest==Color.black);
            camera.backgroundColor=Color.black;
            yield return new WaitForSecondsRealtime(.6f);
            Check("reticle turns white over a dark view",ui.ReticleInkForTest==Color.white);
            InputSystem.RemoveDevice(mouse);
        }

        IEnumerator TestVehicleRoster()
        {
            var game=GameManager.Instance;
            var ids=new[]{"ronin","shift","monsoon","sonora","dustback","monument","executive","heat","citizen","roam4","ae867","espresso","copperado","contender","scarface"};
            var details=new[]{"Ronin rear spare","Shift tail strip","Monsoon shield grille","Sonora satin grille",
                "Dustback roof rail","Monument tail lamp bar","Executive eggcrate grille","Heat zebra seat fabric",
                "Citizen hatch spoiler","Roam4 rear spare","AE 867 pop-up lamp","Espresso targa hoop",
                "Copperado chrome grille","Contender hood scoop","Scarface hood muffler"};
            game.Save.unlockedMission=14;
            for(int i=0;i<ids.Length;i++)
            {
                var car=Array.Find(ContentCatalog.Vehicles,v=>v.id==ids[i]);
                if(!car) { Check("roster car exists: "+ids[i],false);continue; }
                if(!game.Save.ownedVehicles.Contains(car.id))game.Save.ownedVehicles.Add(car.id);
                Check("select roster car: "+car.id,GarageManager.SelectVehicle(car.contentOrder,game.Save));
                game.RefreshGarageVehicle();
                yield return new WaitForSecondsRealtime(.12f);
                Check("distinct model detail: "+car.id,game.Player&&game.Player.Definition.id==car.id&&
                    VehicleController.FindChild(game.Player.Visual,details[i]));
                // Each roster car is one lofted shell standing on its tyres, with lit headlamps for the beams to find.
                var shell=game.Player?VehicleController.FindChild(game.Player.Visual,"Body \u2022 paint"):null;
                var shellMesh=shell?shell.GetComponent<MeshFilter>().sharedMesh:null;
                var frontWheel=game.Player?game.Player.Wheel(0):null;
                Check("lofted body shell: "+car.id,shellMesh&&shellMesh.triangles.Length>=600&&shellMesh.bounds.size.z>car.wheelbase+.9f&&
                    VehicleController.FindChild(game.Player.Visual,"Headlight")&&frontWheel&&frontWheel.localPosition.y<.36f);
                if(car.id=="heat")
                {
                    // The zebra print is a texture on the seat panels, not modelled stripes.
                    var fabric=VehicleController.FindChild(game.Player.Visual,details[i]);
                    var map=fabric?fabric.GetComponent<Renderer>().sharedMaterial.GetTexture("_BaseMap"):null;
                    Check("Heat seats wear a zebra-print texture",map&&map.name=="Zebra print"&&!VehicleController.FindChild(game.Player.Visual,"Heat zebra cushion stripe"));
                }
                if(car.id=="scarface")
                {
                    var plateBacking=VehicleController.FindChild(game.Player.Visual,"Scarface front plate backing");
                    var plateMaterial=plateBacking?plateBacking.GetComponent<Renderer>().sharedMaterial:null;
                    bool matteFront=game.Player.Visual.GetComponentsInChildren<Renderer>().Any(r=>
                        r.gameObject.name=="Scarface tube bumper"&&r.sharedMaterial==plateMaterial);
                    Check("Scarface front plate and bumper have a matte mount",plateBacking&&plateBacking.localScale.x>.5f&&
                        plateMaterial&&plateMaterial.shader.name=="Universal Render Pipeline/Lit"&&
                        plateMaterial.GetFloat("_Smoothness")<.2f&&matteFront);
                    // What makes it the Scarface: the streaked paint and the dog on its doors, both textures; bare front
                    // wheels on off-road tread; nitro from the stack on the hood; a whip about four car-heights tall with a
                    // small orange pennant at its head, which the dust mask does not mistake for the roof.
                    bool logo=false,streaked=false;
                    foreach(var renderer in game.Player.Visual.GetComponentsInChildren<Renderer>())
                    {
                        var material=renderer.sharedMaterial;
                        var texture=material&&material.HasProperty("_BaseMap")?material.GetTexture("_BaseMap"):null;
                        if(!texture)continue;
                        logo|=texture.name=="Dog profile"&&material.HasProperty("_Cutoff")&&material.GetFloat("_Cutoff")>0;
                        streaked|=texture.name=="Streaked tan paint";
                    }
                    Check("Scarface wears its paint and the dog on its doors as textures",logo&&streaked);
                    Check("Scarface's front wheels stand clear of the body on off-road tread",frontWheel&&VehicleController.FindChild(frontWheel,"Tread block")&&
                        Mathf.Abs(frontWheel.localPosition.x)>.6f&&!VehicleController.FindChild(game.Player.Wheel(2),"Tread block"));
                    var exit=VehicleController.FindChild(game.Player.Visual,NitroExhaust.ExitName);
                    Check("Scarface's nitro leaves upward from the stack on the hood",exit&&game.Player.Visual.InverseTransformDirection(exit.forward).y>.9f&&
                        game.Player.Visual.InverseTransformPoint(exit.position).z>.5f&&VehicleController.FindChild(game.Player.Visual,"Tailpipe")==null);
                    var whip=game.Player.Visual.GetComponentInChildren<WhipFlag>();
                    float roof=shellMesh?shellMesh.bounds.max.y:1,tip=whip?game.Player.Visual.InverseTransformPoint(whip.Tip).y:0;
                    var grime=game.Player.GetComponent<VehicleDust>();
                    Check("Scarface's whip stands about four car-heights tall ("+(tip/roof).ToString("0.0")+"), and is not taken for the roof",
                        whip&&tip/roof>3.5f&&tip/roof<4.6f&&grime&&grime.BodyTop<roof+.6f);
                    var windings=whip?whip.GetComponentsInChildren<LineRenderer>().Where(r=>r.name=="Rope light winding").ToArray():Array.Empty<LineRenderer>();
                    var filaments=whip?whip.GetComponentsInChildren<LineRenderer>().Where(r=>r.name=="Rope light filament").ToArray():Array.Empty<LineRenderer>();
                    bool ropeWrapped=windings.Length==5;
                    foreach(var strand in windings)
                    {
                        ropeWrapped&=strand.positionCount>16;
                        if(strand.positionCount>16)
                        {
                            Vector3 a=strand.GetPosition(0),b=strand.GetPosition(2);
                            ropeWrapped&=Vector2.Distance(new Vector2(a.x,a.z),new Vector2(b.x,b.z))>.01f;
                        }
                    }
                    Check("Scarface's whip carries five rope-light windings around the mast",ropeWrapped);
                    Check("Scarface's full mast has a continuous visible rope-light filament",
                        filaments.Length==5&&filaments.All(r=>r.positionCount==2&&r.widthMultiplier>=.07f));
                    var glowBlock=new MaterialPropertyBlock();
                    if(windings.Length>0)windings[0].GetPropertyBlock(glowBlock);
                    float middayGlow=glowBlock.GetColor("_EmissionColor").maxColorComponent;
                    bool earlierSunset=game.Save.settings.sunset;
                    game.Save.settings.sunset=true;
                    yield return new WaitForSecondsRealtime(3.7f);
                    glowBlock.Clear();
                    if(windings.Length>0)windings[0].GetPropertyBlock(glowBlock);
                    float sunsetGlow=glowBlock.GetColor("_EmissionColor").maxColorComponent;
                    Check("Scarface's rope light brightens at sunset",windings.Length==5&&sunsetGlow>middayGlow*8f&&sunsetGlow>2f);
                    var filamentBlock=new MaterialPropertyBlock();
                    if(filaments.Length>0)filaments[0].GetPropertyBlock(filamentBlock);
                    Check("Scarface's continuous mast glows at sunset",filaments.Length==5&&
                        filamentBlock.GetColor("_EmissionColor").maxColorComponent>2f);
                    game.Save.settings.sunset=earlierSunset;
                    // The pennant is a small orange triangle: full height at the whip, closing to a point behind it.
                    var pennant=VehicleController.FindChild(game.Player.Visual,"Whip pennant");
                    var cloth=pennant?pennant.GetComponent<MeshFilter>().sharedMesh:null;
                    Color orange=pennant?pennant.GetComponent<Renderer>().sharedMaterial.GetColor("_BaseColor"):Color.black;
                    // The cloth hangs a little at rest, so the point is judged by how far its own edge spans, not where it sits.
                    float hoistLow=9,hoistHigh=-9,pointLow=9,pointHigh=-9;
                    if(cloth)foreach(var v in cloth.vertices)
                    {
                        if(v.z>-.01f){hoistLow=Mathf.Min(hoistLow,v.y);hoistHigh=Mathf.Max(hoistHigh,v.y);}
                        if(v.z<-WhipFlag.PennantLength+.01f){pointLow=Mathf.Min(pointLow,v.y);pointHigh=Mathf.Max(pointHigh,v.y);}
                    }
                    Check("Scarface flies a small orange triangle, not a flag ("+(cloth?cloth.bounds.size.z:0).ToString("0.00")+" m long, "+(hoistHigh-hoistLow).ToString("0.00")+" m at the whip, "+(pointHigh-pointLow).ToString("0.00")+" m at the point)",
                        cloth&&cloth.bounds.size.z<.6f&&cloth.bounds.size.y<.4f&&hoistHigh-hoistLow>.25f&&pointHigh>=pointLow&&pointHigh-pointLow<.02f&&
                        orange.r>.9f&&orange.g>.25f&&orange.g<.55f&&orange.b<.2f);
                    // The whip is thin sprung metal. One is driven by hand here, clear of the garage: a straight run, a
                    // turn to the right, straight again, then a dead stop.
                    var carrier=new GameObject("Whip carrier").transform;carrier.position=new Vector3(0,400,0);
                    var rod=WhipFlag.Build(carrier,Vector3.zero,5,new Color(1f,.4f,.04f),new Color(1f,.8f,.46f));rod.enabled=false;
                    const float tick=1/60f,pace=18;
                    for(int beat=0;beat<240;beat++){carrier.position+=carrier.forward*pace*tick;rod.Sway(tick);}
                    Vector2 running=rod.Lean;
                    for(int beat=0;beat<180;beat++){carrier.Rotate(0,pace/25*Mathf.Rad2Deg*tick,0);carrier.position+=carrier.forward*pace*tick;rod.Sway(tick);}
                    Vector2 turning=rod.Lean;
                    float lash=-90,swing=-90;
                    for(int beat=0;beat<150;beat++){carrier.position+=carrier.forward*pace*tick;rod.Sway(tick);swing=Mathf.Max(swing,rod.Lean.x);}
                    for(int beat=0;beat<480;beat++){rod.Sway(tick);lash=Mathf.Max(lash,rod.Lean.y);}
                    Vector2 settled=rod.Lean;
                    Destroy(carrier.gameObject);
                    Check("Scarface's whip lies back in the wind ("+running.y.ToString("0")+" deg), swings wide in a turn ("+turning.x.ToString("0")+" deg) and back past upright out of it ("+swing.ToString("0")+"), lashes forward at a stop ("+lash.ToString("0")+") and settles ("+settled.magnitude.ToString("0.0")+")",
                        running.y<-12&&running.y>-20&&Mathf.Abs(running.x)<1&&turning.x<-8&&turning.y<-10&&lash>3&&swing>2&&settled.magnitude<1.5f);
                }
                if(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MIA_CAPTURE_DIR")))continue;
                var camera=Camera.main;var rig=CameraController.Instance;var ui=game.GetComponent<GameUI>();
                rig.enabled=false;ui.enabled=false;camera.orthographic=false;camera.fieldOfView=48;
                Vector3 center=game.Player.transform.position+Vector3.up*.85f;
                Vector3 side=game.Player.transform.position+game.Player.transform.right*5.4f+
                    game.Player.transform.forward*1.2f+Vector3.up*1.85f;
                camera.transform.SetPositionAndRotation(side,Quaternion.LookRotation(center-side));
                yield return new WaitForSecondsRealtime(.1f);
                SceneLuminance("roster-profile-"+car.id,camera);
                if(car.id=="scarface")
                {
                    // The whole of it, whip and all, from the front quarter.
                    Vector3 far=game.Player.transform.position+game.Player.transform.forward*9f-game.Player.transform.right*6.5f+Vector3.up*4.2f;
                    camera.transform.SetPositionAndRotation(far,Quaternion.LookRotation(game.Player.transform.position+Vector3.up*2.6f-far));
                    yield return new WaitForSecondsRealtime(.1f);
                    SceneLuminance("roster-scarface-whip",camera);
                }
                if(i!=0&&i!=1&&i!=4&&i!=7&&i!=14){rig.enabled=true;ui.enabled=true;continue;}
                // The Heat is seen from ahead and above, down into the cockpit at its seats; the others from behind.
                Vector3 view=i==7?game.Player.transform.position+game.Player.transform.forward*2.5f+game.Player.transform.right*1.3f+Vector3.up*2.3f:
                    game.Player.transform.position-game.Player.transform.forward*5f+Vector3.up*2.2f;
                camera.transform.SetPositionAndRotation(view,Quaternion.LookRotation(center-view));
                yield return new WaitForSecondsRealtime(.1f);
                SceneLuminance("roster-"+car.id,camera);
                rig.enabled=true;ui.enabled=true;
            }
        }
        IEnumerator TestEffectsRevision()
        {
            var game=GameManager.Instance;
            var player=game.Player;
            // The shell closes the tail: the old box body left an open strip under the rear window.
            Check("Ronin has a closed body shell",player && player.Visual &&
                VehicleController.FindChild(player.Visual,"Body \u2022 paint") &&
                VehicleController.FindChild(player.Visual,"Ronin rear spare"));
            var tuning=JsonUtility.FromJson<DevTuning>("{\"explosionVersion\":0}");
            tuning.Clamp();
            Check("older saves receive neutral explosion multipliers",Mathf.Approximately(tuning.explosionFire,1) &&
                Mathf.Approximately(tuning.explosionDust,1) && Mathf.Approximately(tuning.explosionForce,1));
            int controls=0;
            foreach(var control in DevControl.All)
                if(control.field.Name=="explosionFire" || control.field.Name=="explosionDust" || control.field.Name=="explosionForce")
                    if(control.group=="COMBAT" && control.scope==DevScope.Shared) controls++;
            Check("three host-shared explosion sliders are available",controls==3);
            if(player)
            {
                var camera=Camera.main; var rig=CameraController.Instance; var ui=game.GetComponent<GameUI>();
                rig.enabled=false;ui.enabled=false;camera.orthographic=false;camera.fieldOfView=48;
                Vector3 rear=player.transform.position-player.transform.forward*5+Vector3.up*2.1f;
                camera.transform.SetPositionAndRotation(rear,Quaternion.LookRotation(player.transform.position+Vector3.up*.85f-rear));
                yield return new WaitForSecondsRealtime(.3f);
                SceneLuminance("effects-ronin-rear",camera);
                camera.orthographic=true;rig.enabled=true;ui.enabled=true;
            }
            var root=new GameObject("Power line fade test");
            var first=new GameObject("First pole");var second=new GameObject("Second pole");
            first.transform.position=new Vector3(100,0,100);second.transform.position=new Vector3(112,0,100);
            var span=PowerLineSpan.Connect(root.transform,first.transform,second.transform);
            var line=span.GetComponentInChildren<LineRenderer>();
            Destroy(first);yield return null;
            yield return new WaitForSecondsRealtime(1.65f);
            Check("fallen wire fades without narrowing",line && line.enabled && line.startColor.a<1 && line.startColor.a>0 && Mathf.Approximately(line.widthMultiplier,.06f));
            yield return new WaitForSecondsRealtime(.65f);
            Check("fallen wire disappears after the pole fade",line && !line.enabled);
            Destroy(second);Destroy(root);
        }
        static float SceneLuminance(string name,Camera view=null)
        {
            // Hidden Windows players can skip the swapchain. Request a real URP render,
            // including post-processing, into a texture instead of reading that backbuffer.
            const int width=1600,height=900;
            var target=RenderTexture.GetTemporary(width,height,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(view?view:Camera.main,
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
        void Check(string name, bool passed) { (passed ? checks : failures).Add((passed ? "PASS " : "FAIL ") + name); if (!passed) fixtureTrace.Add("MIA_SMOKE_FAIL_AT " + name); }
        void TestWeaponDropsAndBazookaCone()
        {
            string[] moved = { "scattermortar", "crossbow", "deathray", "harpoon", "aircannon", "torch", "boomerang", "sentry" };
            var available = new HashSet<string>();
            bool validDrops = true;
            for (int faction = 0; faction < FactionRules.Count; faction++)
                for (int archetype = 0; archetype <= 7; archetype++)
                    for (int roll = 0; roll < 100; roll++)
                    {
                        string id = WeaponRules.EnemyDropForRoll((EnemyFaction)faction, archetype, roll / 100f);
                        available.Add(id);
                        validDrops &= WeaponRules.Find(id) && !WeaponRules.GarageWeapon(id) &&
                            WeaponRules.PickupAmmo(id) > 0 && WeaponRules.MaxAmmo(id) >= WeaponRules.PickupAmmo(id);
                    }
            Check("all enemy drops have a field weapon and usable ammo", validDrops);
            Check("all eight moved weapons are reachable faction drops", moved.All(available.Contains));
            var fireworkPrefab = Resources.Load<GameObject>("VFX/Firework_WarmSunset");
            var fireworkEffect = fireworkPrefab ? fireworkPrefab.GetComponent<UnityEngine.VFX.VisualEffect>() : null;
            Check("firework cannon is a usable drop with its VFX graph",
                available.Contains("fireworks") && fireworkEffect && fireworkEffect.visualEffectAsset);
            Check("moved weapons are absent from the garage", moved.All(id => !WeaponRules.GarageWeapon(id) && WeaponRules.ScrapCost(id) == 0));
            var legacySave = new SaveData { salvage = 9, selectedWeapon = "deathray" };
            legacySave.ownedWeapons.Add("deathray"); legacySave.ownedWeapons.Add("crossbow");
            legacySave.Normalize();
            Check("owned weapons moved to drops refund scrap and unequip safely",
                legacySave.salvage == 649 && legacySave.selectedWeapon == "riveter" &&
                !legacySave.ownedWeapons.Contains("deathray") && !legacySave.ownedWeapons.Contains("crossbow"));
            legacySave.Normalize();
            Check("field weapon refund is applied only once", legacySave.salvage == 649);
            var vacuumSave = new SaveData { selectedWeapon = "shopvac" };
            vacuumSave.ownedWeapons.Add("shopvac"); vacuumSave.Normalize();
            Check("vacuum ownership and selection convert to garage minigun", vacuumSave.selectedWeapon == "minigun" &&
                vacuumSave.ownedWeapons.Contains("minigun") && !vacuumSave.ownedWeapons.Contains("shopvac"));
            Check("Tetrafluoro Skewer is a purchasable garage weapon", WeaponRules.GarageWeapon("skewer") &&
                WeaponRules.Find("skewer") && WeaponRules.ScrapCost("skewer") > 0);
            var skewerOwner = new GameObject("Skewer smoke owner");
            var skewerPoint = new Vector3(0, 200, 0);
            ProjectileSystem.Fire(skewerPoint, Vector3.forward, 100, 82, 0, skewerOwner,
                new Color(.55f, .94f, 1f), ExplosionKind.Ammunition, 1.15f,
                new ShotFx { effect = ShotEffect.Freeze, duration = 3.5f, pierce = 1, detached = true });
            var projectilePool = FindFirstObjectByType<ProjectileSystem>();
            int beforeBurst = projectilePool ? projectilePool.ActiveCount : 0;
            bool burstSkewer = ProjectileSystem.DetonateSkewer(skewerOwner);
            Check("flying skewer bursts into nine ice shards on second trigger",
                beforeBurst == 1 && burstSkewer && projectilePool && projectilePool.ActiveCount == 9);
            Destroy(skewerOwner);
            Check("special drops follow faction roles",
                WeaponRules.EnemyDropForRoll(EnemyFaction.Sunsprawl, 3, 0) == "crossbow" &&
                WeaponRules.EnemyDropForRoll(EnemyFaction.CourtesyCompliance, 3, 0) == "scattermortar" &&
                WeaponRules.EnemyDropForRoll(EnemyFaction.RoadScavengers, 1, 0) == "torch" &&
                WeaponRules.EnemyDropForRoll(EnemyFaction.RoadScavengers, 3, 0) == "boomerang" &&
                WeaponRules.EnemyDropForRoll(EnemyFaction.OpenHouseRealty, 3, 0) == "harpoon" &&
                WeaponRules.EnemyDropForRoll(EnemyFaction.SnowbirdConvoy, 3, 0) == "aircannon" &&
                WeaponRules.EnemyDropForRoll(EnemyFaction.CarOtaku, 3, 0) == "deathray");
            float oldWidth = 20f * Mathf.Sin(45f * Mathf.Deg2Rad);
            float newWidth = 2f * ExplosionSystem.BazookaConeLength(10f) * Mathf.Sin(ExplosionSystem.BazookaConeHalfAngle * Mathf.Deg2Rad);
            Check("bazooka cone has triple reach and half the old tip width",
                Mathf.Approximately(ExplosionSystem.BazookaConeLength(10f), 30f) && Mathf.Abs(newWidth - oldWidth * .5f) < .001f);
            Check("bazooka cone includes distant center hits and rejects wide or overlong hits",
                ExplosionSystem.WithinBazookaCone(new Vector3(3, 0, 29), Vector3.forward, 10) &&
                !ExplosionSystem.WithinBazookaCone(new Vector3(5, 0, 29), Vector3.forward, 10) &&
                !ExplosionSystem.WithinBazookaCone(new Vector3(0, 0, 31), Vector3.forward, 10));
            var needler=WeaponRules.Find("needler");
            Check("needler projectiles use the green light tint",needler &&
                needler.projectileColor.g>.9f && needler.projectileColor.r<.4f && needler.projectileColor.b<.3f);
        }
        /// <summary>Samples the mission fixture every 0.25 s; printed with the results only when a check fails.</summary>
        IEnumerator TraceFixture()
        {
            float start=Time.realtimeSinceStartup;
            while(true){fixtureTrace.Add(FixtureState("t+"+(Time.realtimeSinceStartup-start).ToString("0.0")+"s"));yield return new WaitForSecondsRealtime(.25f);}
        }
        /// <summary>One line of fixture state, so an intermittent failure can be traced to what the world did.</summary>
        static string FixtureState(string label)
        {
            var game=GameManager.Instance;var player=game?game.Player:null;
            if(!player)return "MIA_SMOKE_STATE "+label+": no player, state="+(game?game.State.ToString():"none");
            int alive=0,dead=0;float nearest=float.MaxValue;
            foreach(var vehicle in VehicleController.Active)
            {
                if(!vehicle||vehicle.IsPlayer||!vehicle.Damage)continue;
                if(vehicle.Damage.IsDead){dead++;continue;}
                alive++;nearest=Mathf.Min(nearest,Vector3.Distance(vehicle.transform.position,player.transform.position));
            }
            int weaponDrops=0;float nearestDrop=float.MaxValue;
            foreach(var pickup in CombatPickup.Active)
                if(pickup&&pickup.Kind==PickupKind.Weapon){weaponDrops++;nearestDrop=Mathf.Min(nearestDrop,Vector3.Distance(pickup.transform.position,player.transform.position));}
            return "MIA_SMOKE_STATE "+label+": state="+game.State+" stage="+game.Mission.Stage+" health="+player.Damage.Health.ToString("0")+"/"+player.Damage.MaxHealth.ToString("0")
                +" dead="+player.Damage.IsDead+" field="+(player.Weapons&&player.Weapons.FieldWeapon?player.Weapons.FieldWeapon.id+"x"+player.Weapons.FieldAmmo:"none")
                +" hostiles alive="+alive+" dead="+dead+" nearest="+(alive>0?nearest.ToString("0"):"-")+"m weaponDrops="+weaponDrops+" nearestDrop="+(weaponDrops>0?nearestDrop.ToString("0"):"-")+"m"
                +" pos="+player.transform.position.ToString("0")+" generated="+(GeneratedWorld.Active!=null);
        }
        void QuietMissionHostiles()
        {
            foreach (var vehicle in VehicleController.Active)
            {
                if (!vehicle || vehicle.IsPlayer) continue;
                var ai = vehicle.GetComponent<EnemyAI>();
                if (!ai || ai.IsFriendly) continue;
                ai.enabled = false;
                vehicle.SetAIInput(Vector2.zero, Vector3.forward, false);
                vehicle.Body.isKinematic = true;
            }
        }
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
