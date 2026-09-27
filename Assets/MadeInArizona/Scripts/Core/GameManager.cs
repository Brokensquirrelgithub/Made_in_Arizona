using System;
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
            Sun.shadows = LightShadows.Soft;
            sunObj.transform.rotation = Quaternion.Euler(68, -35, 0);
            CreatePostProcessing();
            gameObject.AddComponent<GameUI>();
            gameObject.AddComponent<SmokeTestRunner>();
            ApplySettings();
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

        public void ApplySettings()
        {
            var s = Save.settings;
            s.quality = Mathf.Clamp(s.quality, 0, 3);
            s.frameSync = Mathf.Clamp(s.frameSync, 0, 2);
            QualitySettings.vSyncCount = s.frameSync == 0 ? 1 : 0;
            Application.targetFrameRate = s.frameSync == 1 ? 120 : -1;
            QualitySettings.globalTextureMipmapLimit = s.quality==0?2:s.quality==1?1:0;
            QualitySettings.shadows = s.quality == 0 ? UnityEngine.ShadowQuality.Disable : UnityEngine.ShadowQuality.All;
            QualitySettings.shadowDistance = new[] { 0f, 65f, 110f, 160f }[s.quality];
            QualitySettings.shadowResolution = s.quality >= 2 ? UnityEngine.ShadowResolution.VeryHigh : UnityEngine.ShadowResolution.Medium;
            QualitySettings.lodBias = new[] { .65f, 1f, 1.5f, 2f }[s.quality];
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (pipeline) {
                pipeline.renderScale = new[] { .75f, .9f, 1f, 1f }[s.quality];
                pipeline.shadowDistance = QualitySettings.shadowDistance;
                CameraController.RefreshDepthEffects(); // re-adds the camera's backed-off distance
                pipeline.msaaSampleCount = s.quality < 2 ? 1 : 4;
                pipeline.mainLightShadowmapResolution = s.quality == 3 ? 4096 : 2048;
                pipeline.maxAdditionalLightsCount = s.quality == 0 ? 0 : s.quality == 3 ? 8 : 4;
            }
            Shader.SetGlobalFloat("_ArizonaDetail", s.quality >= 2 ? 1 : 0);
            AudioListener.volume = AudioManager.OutputVolume(s.master);
            if (!Application.isBatchMode) {
                var mode = s.fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
                if (Screen.width != s.width || Screen.height != s.height || Screen.fullScreenMode != mode)
                    Screen.SetResolution(s.width, s.height, mode);
            }
            DevTuning.Apply();
            SaveSystem.Save(Save);
        }

        void SpawnPlayer(bool garage)
        {
            int index = Mathf.Clamp(Save.selectedVehicle, 0, ContentCatalog.Vehicles.Length - 1);
            var def = ContentCatalog.Vehicles[index];
            if (!garage && !IsCombatTrial) {
                string forced = ContentCatalog.Missions[SelectedMission].forcedVehicle;
                if (!string.IsNullOrEmpty(forced)) def = Array.Find(ContentCatalog.Vehicles, v => v.id == forced) ?? def;
            }
            CurrentVehicle = def;
            var obj = new GameObject(def.displayName + " • Player");
            obj.transform.SetParent(World.transform);
            obj.transform.position = World.PlayerSpawn;
            Player = obj.AddComponent<VehicleController>();
            Player.Initialize(def, GarageManager.StatsFor(def, Save), true);
            if (!garage) {
                var dog = SuzukiDog.Create(Player.Visual, new Vector3(.48f, 1.1f, .1f), Save.dogCosmetic, true);
                dog.transform.localScale = Vector3.one * .55f;
            }
            if (garage) { Player.Body.isKinematic = true; }
            CameraController.Instance.Target = Player.transform;
        }

        public bool IsCombatTrial { get; private set; }
        public void StartCombatTrial()
        {
            Time.timeScale=1;IsCombatTrial=true;State=GameState.Playing;NotificationUntil=0;DevVisuals.Apply();
            World.BuildCombatArena();SpawnPlayer(false);InputManager.Instance.SetEnabled(true);
            Mission.BeginCombatTrial();AudioManager.Instance.SetCombat(true);CameraController.Instance.Snap();StateChanged?.Invoke();
        }
        public void RetryMission() { if(IsCombatTrial)StartCombatTrial();else StartMission(SelectedMission); }

        public void StartMission(int index)
        {
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
        }

        public void ReturnToGarage()
        {
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
        }

        public void RefreshGarageVehicle()
        {
            if (State != GameState.Garage) return;
            if (Player) Destroy(Player.gameObject);
            SpawnPlayer(true);
            SaveSystem.Save(Save);
        }

        public void Pause()
        {
            if (State != GameState.Playing) return;
            State = GameState.Paused; Time.timeScale = 0;
            InputManager.Instance.SetEnabled(false); StateChanged?.Invoke();
        }

        public void Resume()
        {
            if (State != GameState.Paused) return;
            State = GameState.Playing; Time.timeScale = 1;
            InputManager.Instance.SetEnabled(true); StateChanged?.Invoke();
        }

        public void CompleteMission()
        {
            if (!IsPlaying) return;
            State = GameState.Won;
            InputManager.Instance.SetEnabled(false);
            AudioManager.Instance.SetCombat(false);
            SaveSystem.Save(Save);
            StateChanged?.Invoke();
        }

        public void FailMission()
        {
            if (!IsPlaying) return;
            State = GameState.Lost;
            InputManager.Instance.SetEnabled(false);
            DialogueSystem.Instance.Say("JOHNNY • 117° AUTO CARE", "You're insured. Emotionally. Suzuki is fine. Bring back whatever still rolls.");
            StateChanged?.Invoke();
        }

        public void Notify(string text)
        {
            Notification = text; NotificationUntil = Time.unscaledTime + 4;
        }

        void Update()
        {
            PollWorldConfig();
            if (InputManager.Instance.PausePressed) { if (State == GameState.Playing) Pause(); else if (State == GameState.Paused) Resume(); }
            if (!IsPlaying) return;
            Mission.Tick(Time.deltaTime);
            if (Player && Player.Damage.IsDead) FailMission();
            autosave += Time.unscaledDeltaTime;
            if (autosave > 60) { autosave = 0; SaveSystem.Save(Save); }
        }

        void OnApplicationFocus(bool focus) { if (!focus && IsPlaying && !SmokeTestRunner.Active) Pause(); }
        void OnApplicationQuit() { if (Save != null) SaveSystem.Save(Save); Time.timeScale = 1; }
        void OnDestroy()
        {
            RenderPipelineManager.activeRenderPipelineCreated -= OnRenderPipelineCreated;
            if (PresentationVolume && PresentationVolume.profile) Destroy(PresentationVolume.profile);
            if (Instance == this) Instance = null;
        }
    }
}
