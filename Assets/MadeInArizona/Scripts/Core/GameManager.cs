using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MadeInArizona
{
    public enum GameState { Garage, Playing, Paused, Won, Lost, MainMenu, Generating }

    /// <summary>Owns session transitions; simulation systems remain independent components.</summary>
    public sealed partial class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }
        public VehicleController Player { get; private set; }
        public WorldBuilder World { get; private set; }
        public MissionManager Mission { get; private set; }
        public SaveData Save { get; private set; }
        public GameState State { get; private set; }
        public int SelectedMission;
        public VehicleDefinition CurrentVehicle { get; private set; }
        public bool IsPlaying => State == GameState.Playing;
        public string Notification { get; private set; }
        public float NotificationUntil { get; private set; }
        public event Action StateChanged;
        public Light Sun { get; private set; }
        public Volume PresentationVolume { get; private set; }
        float autosave;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (FindFirstObjectByType<GameManager>() == null)
                new GameObject("Made in Arizona • Session").AddComponent<GameManager>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            RenderPipelineManager.activeRenderPipelineCreated += OnRenderPipelineCreated;
            ContentCatalog.EnsureLoaded();
            Save = SaveSystem.Load();
            SelectedMission = Mathf.Clamp(Save.unlockedMission, 0, ContentCatalog.Missions.Length - 1);
            Application.targetFrameRate = 120;
            Application.runInBackground = true;
            Time.fixedDeltaTime = 1f / 60f;
            Time.maximumDeltaTime = .1f;
            Physics.defaultSolverIterations = 8;
            gameObject.AddComponent<InputManager>();
            gameObject.AddComponent<DialogueSystem>();
            gameObject.AddComponent<AudioManager>();
            Mission = gameObject.AddComponent<MissionManager>();
            World = new GameObject("Arizona • World").AddComponent<WorldBuilder>();
            var camera = Camera.main;
            if (camera == null) {
                var camObject = new GameObject("Main Camera");
                camObject.tag = "MainCamera";
                camera = camObject.AddComponent<Camera>();
            }
            camera.backgroundColor = new Color(.64f, .78f, .88f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.nearClipPlane = .2f;
            camera.farClipPlane = 450;
            camera.allowHDR = true;
            var additional = camera.GetUniversalAdditionalCameraData();
            camera.SetVolumeFrameworkUpdateMode(VolumeFrameworkUpdateMode.EveryFrame);
            additional.renderPostProcessing = true;
            additional.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            if (!camera.GetComponent<AudioListener>()) camera.gameObject.AddComponent<AudioListener>();
            if (!camera.GetComponent<CameraController>()) camera.gameObject.AddComponent<CameraController>();
            var sunObj = new GameObject("Arizona • Midday sun");
            sunObj.transform.SetParent(transform, false);
            Sun = sunObj.AddComponent<Light>();
            Sun.type = LightType.Directional;
            Sun.shadows = LightShadows.Soft; // ApplySettings chooses filtering and disables shadows on Simple.
            sunObj.transform.rotation = Quaternion.Euler(68, -35, 0);
            CreatePostProcessing();
            gameObject.AddComponent<GameUpdater>();
            gameObject.AddComponent<CoopSession>();
            gameObject.AddComponent<GameUI>();
            gameObject.AddComponent<SmokeTestRunner>();
            if (LiveTestBridge.ShouldStart()) gameObject.AddComponent<LiveTestBridge>();
            if (TelemetryRecorder.Enabled) gameObject.AddComponent<TelemetryRecorder>();
            ApplySettings();
            ApplyDisplaySettings();
        }

        void Start() { ReturnToGarage(); DevTuning.Apply(); if(!SmokeTestRunner.Active)ShowMainMenu(); }

        void OnRenderPipelineCreated()
        {
            // Awake can run before URP creates its renderer features and volume framework.
            // Apply again once they exist so startup matches the live dev-slider path.
            DevTuning.Apply();
        }

        void CreatePostProcessing()
        {
            if (PresentationVolume) return;
            var host = new GameObject("Arizona • Color and atmosphere");
            host.transform.SetParent(transform, false);
            PresentationVolume = host.AddComponent<Volume>();
            PresentationVolume.isGlobal = true;
            PresentationVolume.weight = 1f;
            PresentationVolume.profile = ScriptableObject.CreateInstance<VolumeProfile>();
            // DevVisuals is the sole writer, at startup and when any slider changes.
        }

        /// <summary>
        /// Whether shadow-casting effect lights use URP's filtered edges. The sun is filtered whenever shadows are enabled.
        /// </summary>
        public static bool SoftShadows { get; private set; }
        /// <summary>Shadow map size for the sun: the preset's own, or 4096 / 8192 at high / ultra shadow detail.</summary>
        public static int SunShadowResolution(GameSettings s) => s.shadowDetail == 2 ? 8192 : s.shadowDetail == 1 ? 4096 : s.quality == 3 ? 4096 : 2048;
        /// <summary>Shadow atlas size for explosion, fire and muzzle lights at each graphics preset.</summary>
        public static int AdditionalShadowAtlas(int quality) => quality >= 2 ? 4096 : 1024;

        public void ApplySettings()
        {
            var s = Save.settings;
            s.quality = Mathf.Clamp(s.quality, 0, 3);
            // The game exposes four presets while Unity has six project quality levels. Selecting the matching
            // engine level is important: particle raycast budgets, anisotropic filtering, LOD cross-fades and the
            // other built-in settings otherwise remained at the project's startup level (Ultra), even on Low.
            int engineQuality = new[] { 0, 2, 4, 5 }[s.quality];
            if (QualitySettings.GetQualityLevel() != engineQuality)
                QualitySettings.SetQualityLevel(engineQuality, true);
            s.frameSync = Mathf.Clamp(s.frameSync, 0, 2);
            QualitySettings.vSyncCount = s.frameSync == 0 ? 1 : 0;
            Application.targetFrameRate = s.frameSync == 1 ? 120 : -1;
            QualitySettings.globalTextureMipmapLimit = s.quality==0?2:s.quality==1?1:0;
            s.shadowDetail = Mathf.Clamp(s.shadowDetail, 0, 2);
            bool detailedShadows = s.shadowDetail > 0;
            bool castShadows = s.quality > 0 || detailedShadows;
            SoftShadows = castShadows && (s.quality >= 2 || detailedShadows);
            QualitySettings.shadows = castShadows ? UnityEngine.ShadowQuality.All : UnityEngine.ShadowQuality.Disable;
            // Detailed shadows spend the bigger map on the play area only: 110 m past the framing distance covers the
            // view at full dynamic zoom (the Arizona Summer preset's 160 m spread its map over hills nobody sees).
            QualitySettings.shadowDistance = detailedShadows ? 110f : new[] { 0f, 65f, 110f, 160f }[s.quality];
            QualitySettings.shadowResolution = s.quality >= 2 || detailedShadows ? UnityEngine.ShadowResolution.VeryHigh : UnityEngine.ShadowResolution.Medium;
            if (Sun)
            {
                Sun.shadows = castShadows ? LightShadows.Soft : LightShadows.None;
                var sunData = Sun.GetUniversalAdditionalLightData();
                // Low uses a small PCF filter; Medium and High use a wider tent filter. The 8K override keeps
                // the highest filter quality. Filtering hides hard texel steps without allocating a blur texture.
                if (sunData) sunData.softShadowQuality = s.shadowDetail == 2 ? SoftShadowQuality.High :
                    s.quality == 1 && !detailedShadows ? SoftShadowQuality.Low : SoftShadowQuality.Medium;
            }
            QualitySettings.lodBias = new[] { .65f, 1f, 1.5f, 2f }[s.quality];
            var camera = Camera.main;
            if (camera)
            {
                camera.allowHDR = s.quality > 0;
                var cameraData = camera.GetUniversalAdditionalCameraData();
                cameraData.renderPostProcessing = s.quality > 0;
            }
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (pipeline) {
                // Simple targets integrated GPUs: 58% linear resolution is 40% of the native pixel work. The UI is
                // composited at output resolution, so gauges and text remain sharp.
                pipeline.renderScale = new[] { .58f, .82f, 1f, 1f }[s.quality];
                // Heat haze is disabled by the Low effects path, so do not pay for an opaque scene copy there.
                pipeline.supportsCameraOpaqueTexture = s.quality > 0;
                pipeline.shadowDistance = QualitySettings.shadowDistance;
                CameraController.RefreshDepthEffects(); // re-adds the camera's backed-off distance
                pipeline.msaaSampleCount = s.quality < 2 ? 1 : 4;
                pipeline.mainLightShadowmapResolution = SunShadowResolution(s);
                pipeline.maxAdditionalLightsCount = s.quality == 0 ? 0 : s.quality == 3 ? 8 : 4;
                // Explosion and fire lights cast shadows on High Octane and Arizona Summer. A big fight puts up to 36
                // point-light shadow maps in this atlas, and at 2048 URP halved their resolution. The lower presets'
                // effect lights cast no shadows, so they keep a small atlas and the video memory it saves.
                pipeline.additionalLightsShadowmapResolution = AdditionalShadowAtlas(s.quality);
            }
            DevVisuals.Apply(); // ambient occlusion quality follows the preset
            Shader.SetGlobalFloat("_ArizonaDetail", s.quality >= 2 ? 1 : 0);
            AudioListener.volume = AudioManager.OutputVolume(s.master);
            DevTuning.Apply();
            SaveSystem.Save(Save);
        }

        /// <summary>Only startup and explicit display controls change the native window.</summary>
        public void ApplyDisplaySettings()
        {
            if (Application.isBatchMode || Save == null) return;
            var s = Save.settings;
            if (!s.displayConfigured && !s.fullscreen) return;
            if (s.fullscreen)
            {
                // OS maximized mode uses the work area, so the taskbar cannot cover the bottom of the game.
                if (Screen.fullScreenMode != FullScreenMode.MaximizedWindow)
                    Screen.fullScreenMode = FullScreenMode.MaximizedWindow;
            }
            else
            {
                var size = FitWindowToWorkArea(s.width, s.height, Screen.mainWindowDisplayInfo.workArea);
                if (Screen.fullScreenMode != FullScreenMode.Windowed || Screen.width != size.x || Screen.height != size.y)
                    Screen.SetResolution(size.x, size.y, FullScreenMode.Windowed);
            }
        }

        public static Vector2Int FitWindowToWorkArea(int width, int height, RectInt workArea)
        {
            if (workArea.width <= 0 || workArea.height <= 0) return new Vector2Int(width, height);
            // Leave room for the title bar and borders when returning to a movable window.
            float scale = Mathf.Min(1f, (workArea.width - 32f) / width, (workArea.height - 64f) / height);
            return new Vector2Int(Mathf.Max(1, Mathf.RoundToInt(width * scale)), Mathf.Max(1, Mathf.RoundToInt(height * scale)));
        }

        public void ChooseWindowMode(bool maximized)
        {
            Save.settings.fullscreen = maximized;
            Save.settings.displayConfigured = true;
            ApplyDisplaySettings();
            SaveSystem.Save(Save);
        }

        public void ChooseWindowResolution(int width, int height)
        {
            var s = Save.settings;
            s.width = width; s.height = height; s.displayConfigured = true;
            if (!s.fullscreen) ApplyDisplaySettings();
            SaveSystem.Save(Save);
        }

        void SpawnPlayer(bool garage)
        {
            int index = Mathf.Clamp(Save.selectedVehicle, 0, ContentCatalog.Vehicles.Length - 1);
            var def = ContentCatalog.Vehicles[index];
            CurrentVehicle = def;
            var obj = new GameObject(def.displayName + " • Player");
            obj.transform.SetParent(World.transform);
            obj.transform.position = World.PlayerSpawn;
            // In the garage the car sits backed onto the lift, nose out toward the open bay and the camera.
            if (garage) { obj.transform.rotation = Quaternion.Euler(0, 180, 0); obj.transform.position += Vector3.up * VehicleVisual.TyreDrop(def); }
            Player = obj.AddComponent<VehicleController>();
            Player.Initialize(def, GarageManager.StatsFor(def, Save), true, EnemyFaction.Sunsprawl, GarageManager.DamageProfiles(def, Save));
            if (!garage) {
                var dog = SuzukiDog.Create(Player.Visual, VehicleVisual.PassengerSeat(def), Save.dogCosmetic, true);
                dog.transform.localScale = Vector3.one * .55f;
            }
            if (garage) { Player.Body.isKinematic = true; }
            CameraController.Instance.Target = Player.transform;
        }

        public bool IsCombatTrial { get; private set; }
        public void StartCombatTrial()
        {
            if (CoopSession.IsRemoteClient) return;
            if (CoopSession.Instance && CoopSession.Instance.IsHost &&
                (State == GameState.Garage || State == GameState.MainMenu) && !CoopSession.Instance.CanLaunchMatch) return;
            Time.timeScale=1;IsCombatTrial=true;State=GameState.Playing;NotificationUntil=0;DevVisuals.Apply();
            World.BuildCombatArena();SpawnPlayer(false);InputManager.Instance.SetEnabled(true);
            Mission.BeginCombatTrial();AudioManager.Instance.SetCombat(true);CameraController.Instance.Snap();StateChanged?.Invoke();
            CoopSession.Instance?.HostWorldChanged();
        }
        public void RetryMission() { if(CoopSession.Instance && CoopSession.Instance.IsHost)ReturnToGarage();else if(IsCombatTrial)StartCombatTrial();else StartMission(SelectedMission); }

        public void StartMission(int index)
        {
            if (CoopSession.IsRemoteClient) return;
            if (CoopSession.Instance && CoopSession.Instance.IsHost &&
                (State == GameState.Garage || State == GameState.MainMenu) && !CoopSession.Instance.CanLaunchMatch) return;
            if (index < 0 || index >= ContentCatalog.Missions.Length || index > Save.unlockedMission) return;
            Time.timeScale = 1;
            IsCombatTrial=false;
            SelectedMission = index;
            State = GameState.Playing;
            NotificationUntil = 0;
            DevVisuals.Apply();
            if(UseGeneratedWorld)World.BuildGeneratedMission(index,WorldConfig);else World.BuildMission(index);
            SpawnPlayer(false);
            InputManager.Instance.SetEnabled(true);
            Mission.Begin(ContentCatalog.Missions[index], index);
            if(GeneratedWorld.Active && !GetComponent<WorldExploration>())gameObject.AddComponent<WorldExploration>();
            AudioManager.Instance.SetCombat(true);
            CameraController.Instance.Snap();
            StateChanged?.Invoke();
            CoopSession.Instance?.HostWorldChanged();
        }

        public void ReturnToGarage()
        {
            if (CoopSession.IsRemoteClient) return;
            if (State == GameState.Won && !IsCombatTrial)
                SelectedMission = Mathf.Clamp(SelectedMission + 1, 0, Save.unlockedMission);
            Time.timeScale = 1;
            IsCombatTrial=false;
            State = GameState.Garage;
            NotificationUntil = 0;
            DevVisuals.Apply();
            InputManager.Instance.SetEnabled(false);
            World.BuildGarage();
            SpawnPlayer(true);
            AudioManager.Instance.SetCombat(false);
            DialogueSystem.Instance.Skip();
            SaveSystem.Save(Save);
            CameraController.Instance.Snap();
            StateChanged?.Invoke();
            CoopSession.Instance?.HostWorldChanged();
        }

        public void RefreshGarageVehicle()
        {
            if (State != GameState.Garage) return;
            if (Player) Destroy(Player.gameObject);
            SpawnPlayer(true);
            SaveSystem.Save(Save);
            CoopSession.Instance?.GarageLoadoutChanged();
        }

        /// <summary>True while the player's wreck plays out its death sequence, before the debrief appears.</summary>
        public bool Dying { get; private set; }
        public float DyingStarted { get; private set; }
        public const float DeathSequenceSeconds = 2.8f;

        /// <summary>
        /// The player's car has been destroyed: the wreck is thrown into a tumble, secondary blasts and fire follow in slow
        /// motion while the camera closes in, and only then does the mission fail.
        /// </summary>
        public void BeginPlayerDeath()
        {
            if (Dying || !IsPlaying) return;
            if (CoopSession.Instance && CoopSession.Instance.IsHost && CoopSession.Instance.AnyPlayerAlive) return;
            StartCoroutine(PlayerDeathSequence());
        }

        IEnumerator PlayerDeathSequence()
        {
            Dying = true; DyingStarted = Time.unscaledTime;
            InputManager.Instance.SetEnabled(false);
            var car = Player;
            if (car && car.Body && !car.Body.isKinematic)
            {
                // Released from the yaw-only lock, the wreck is thrown up and tumbles.
                car.Body.constraints = RigidbodyConstraints.None;
                car.Body.linearDamping = .15f; car.Body.angularDamping = .5f; car.Body.maxAngularVelocity = 12;
                Vector3 side = car.transform.right * (UnityEngine.Random.value < .5f ? -1 : 1);
                car.Body.AddForce(Vector3.up * 8 + side * 2.5f, ForceMode.VelocityChange);
                car.Body.AddTorque(car.transform.forward * -Vector3.Dot(side, car.transform.right) * 6 + car.transform.right * UnityEngine.Random.Range(-2.5f, 2.5f), ForceMode.VelocityChange);
            }
            CameraController.Instance?.Shake(1.5f);
            bool secondBlast = false, thirdBlast = false; float fireAt = 0;
            while (Time.unscaledTime - DyingStarted < DeathSequenceSeconds)
            {
                if (State != GameState.Playing) { Time.timeScale = State == GameState.Paused ? 0 : 1; Dying = false; yield break; }
                float t = Time.unscaledTime - DyingStarted;
                // Slow motion at the moment of death, easing back to full speed before the debrief.
                Time.timeScale = Mathf.Lerp(.35f, 1f, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.9f, 2.1f, t)));
                if (car)
                {
                    Vector3 at = car.transform.position + Vector3.up;
                    if (!secondBlast && t > .55f) { secondBlast = true; ExplosionSystem.Detonate(at, 4, 12, car.gameObject, ExplosionKind.Gasoline); CameraController.Instance?.Shake(.8f); }
                    if (!thirdBlast && t > 1.35f) { thirdBlast = true; ExplosionSystem.Detonate(at, 3, 8, car.gameObject, ExplosionKind.Gasoline); CameraController.Instance?.Shake(.5f); }
                    if (t > fireAt) { fireAt = t + .1f; ExplosionSystem.Burst(at, new Color(1, .42f, .1f, .85f), 5, 2.2f); ExplosionSystem.Burst(at + Vector3.up, new Color(.12f, .12f, .13f, .6f), 3, 3f); }
                }
                yield return null;
            }
            Time.timeScale = 1; Dying = false;
            FailMission();
        }

        public void Pause()
        {
            if (State != GameState.Playing || Dying) return;
            State = GameState.Paused; Time.timeScale = CoopSession.IsRemoteClient ? 1 : 0;
            InputManager.Instance.SetEnabled(false); StateChanged?.Invoke();
        }

        public void Resume()
        {
            if (State != GameState.Paused) return;
            State = GameState.Playing; Time.timeScale = 1;
            InputManager.Instance.SetEnabled(!(CoopSession.IsRemoteClient && CoopSession.Instance.HostPaused)); StateChanged?.Invoke();
        }

        public void CompleteMission()
        {
            if (CoopSession.IsRemoteClient) return;
            if (!IsPlaying) return;
            State = GameState.Won;
            InputManager.Instance.SetEnabled(false);
            AudioManager.Instance.SetCombat(false);
            SaveSystem.Save(Save);
            StateChanged?.Invoke();
        }

        public void FailMission()
        {
            if (CoopSession.IsRemoteClient) return;
            if (!IsPlaying) return;
            State = GameState.Lost;
            InputManager.Instance.SetEnabled(false);
            AudioManager.Instance.SetCombat(false);
            DialogueSystem.Instance.Say("JOHNNY • 117° AUTO CARE", "You're insured. Emotionally. Suzuki is fine. Bring back whatever still rolls.");
            StateChanged?.Invoke();
        }

        public void Notify(string text)
        {
            Notification = text; NotificationUntil = Time.unscaledTime + 4;
        }

        void Update()
        {
            if (!CoopSession.IsRemoteClient) PollWorldConfig();
            if (InputManager.Instance.PausePressed) { if (State == GameState.Playing) Pause(); else if (State == GameState.Paused) Resume(); }
            if (CoopSession.IsRemoteClient) return;
            if (!IsPlaying) return;
            Mission.Tick(Time.deltaTime);
            if (Player && Player.Damage.IsDead && !(CoopSession.Instance && CoopSession.Instance.IsHost && CoopSession.Instance.AnyPlayerAlive)) BeginPlayerDeath();
            autosave += Time.unscaledDeltaTime;
            if (autosave > 60) { autosave = 0; SaveSystem.Save(Save); }
        }

        void OnApplicationFocus(bool focus) { if (!focus && IsPlaying && !SmokeTestRunner.Active && !(CoopSession.Instance && CoopSession.Instance.IsHost)) Pause(); }
        void OnApplicationQuit() { if (Save != null) SaveSystem.Save(Save); Time.timeScale = 1; }
        void OnDestroy()
        {
            RenderPipelineManager.activeRenderPipelineCreated -= OnRenderPipelineCreated;
            if (PresentationVolume && PresentationVolume.profile) Destroy(PresentationVolume.profile);
            if (Instance == this) Instance = null;
        }
    }
}
