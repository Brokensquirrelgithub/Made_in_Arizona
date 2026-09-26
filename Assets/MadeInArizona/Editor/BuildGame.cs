using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.InputSystem;

namespace MadeInArizona.Editor
{
    public static class BuildGame
    {
        const string Root = "Assets/MadeInArizona";
        const string ScenePath = Root + "/Scenes/Main.unity";
        [MenuItem("Made in Arizona/1 · Prepare project and content")]
        public static void Prepare()
        {
            Directory.CreateDirectory(Root + "/Resources/Content");
            Directory.CreateDirectory(Root + "/Rendering");
            Directory.CreateDirectory(Root + "/Prefabs");
            Directory.CreateDirectory(Root + "/Resources/Prefabs");
            Directory.CreateDirectory(Root + "/Resources/Rendering");
            Directory.CreateDirectory(Root + "/Scenes");
            Directory.CreateDirectory(Root + "/Materials");
            PlayerSettings.companyName = "117 Degree Games";
            PlayerSettings.productName = "Made in Arizona";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "com.117degreegames.madeinarizona");
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            EditorUserBuildSettings.SetPlatformSettings("OSXUniversal", "Architecture", "x64ARM64");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Standalone, ApiCompatibilityLevel.NET_Standard);
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneOSX, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneOSX, new[] { GraphicsDeviceType.Metal });
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11, GraphicsDeviceType.Direct3D12 });
            PlayerSettings.allowUnsafeCode = false;
            var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            var input = settings.FindProperty("activeInputHandler"); if (input != null) input.intValue = 2;
            settings.ApplyModifiedPropertiesWithoutUndo();
            PandazoleNatureCatalogBuilder.Build();
            SetPipeline();
            ContentCatalog.EnsureLoaded();
            Persist(ContentCatalog.Vehicles); Persist(ContentCatalog.Parts); Persist(ContentCatalog.Weapons); Persist(ContentCatalog.Missions); Persist(ContentCatalog.Drivers);
            CreatePrefabs();
            var main = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("MADE IN ARIZONA • Play starts here").AddComponent<GameManager>();
            EditorSceneManager.SaveScene(main, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            Debug.Log("MIA_PREPARE_OK: content, prefabs, URP, Main scene, native platform configuration authored.");
        }

        static void SetPipeline()
        {
            string rendererPath = Root + "/Rendering/ArizonaRenderer.asset", pipelinePath = Root + "/Rendering/ArizonaURP.asset";
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            if (!renderer) { renderer = ScriptableObject.CreateInstance<UniversalRendererData>(); AssetDatabase.CreateAsset(renderer, rendererPath); }
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
            if (!pipeline) { pipeline = UniversalRenderPipelineAsset.Create(renderer); AssetDatabase.CreateAsset(pipeline, pipelinePath); }
            // CreateInstance does not initialize the renderer's post-process resources.
            // Without this asset every volume slider silently renders no effect in players.
            renderer.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(
                "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
            if(!renderer.postProcessData) throw new BuildFailedException("URP post-process resources are missing.");
            pipeline.supportsHDR = true; pipeline.msaaSampleCount = 4; pipeline.shadowDistance = 110;
            pipeline.mainLightShadowmapResolution = 2048;
            pipeline.maxAdditionalLightsCount = 4;
            var pipelineData=new SerializedObject(pipeline);pipelineData.FindProperty("m_AdditionalLightShadowsSupported").boolValue=true;pipelineData.ApplyModifiedPropertiesWithoutUndo();
            pipeline.supportsCameraDepthTexture = true; pipeline.supportsCameraOpaqueTexture = true;
            EnsureAmbientOcclusion(renderer);
            var depthBlur=renderer.rendererFeatures.OfType<OrthographicDepthBlurFeature>().FirstOrDefault();
            if(!depthBlur)
            {
                depthBlur=ScriptableObject.CreateInstance<OrthographicDepthBlurFeature>();depthBlur.name="Orthographic depth blur";
                AssetDatabase.AddObjectToAsset(depthBlur,renderer);renderer.rendererFeatures.Add(depthBlur);
                var data=new SerializedObject(renderer);var map=data.FindProperty("m_RendererFeatureMap");
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(depthBlur,out _,out long id);map.arraySize++;map.GetArrayElementAtIndex(map.arraySize-1).longValue=id;data.ApplyModifiedPropertiesWithoutUndo();
            }
            depthBlur.SetActive(true);EditorUtility.SetDirty(renderer);
            GraphicsSettings.defaultRenderPipeline = pipeline;
            for (int i = 0; i < QualitySettings.names.Length; i++) { QualitySettings.SetQualityLevel(i); QualitySettings.renderPipeline = pipeline; }
            // Runtime-generated materials still need their shaders explicitly included in players.
            var gs = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
            var shaders = gs.FindProperty("m_AlwaysIncludedShaders");
            for (int i = shaders.arraySize - 1; i >= 0; i--) {
                var shader = shaders.GetArrayElementAtIndex(i).objectReferenceValue as Shader;
                if (shader && (shader.hideFlags & HideFlags.DontSaveInBuild) != 0) {
                    shaders.GetArrayElementAtIndex(i).objectReferenceValue = null;
                    shaders.DeleteArrayElementAtIndex(i);
                }
            }
            var include = new[] { "Sprites/Default", "MadeInArizona/HeatHaze", "MadeInArizona/Scattering", "MadeInArizona/LightShaft", "MadeInArizona/SixWaySmoke", "MadeInArizona/BiomeTerrain", "MadeInArizona/FlowRiver", "MadeInArizona/LivingScenery", "MadeInArizona/OrthographicDepthBlur", "MadeInArizona/TrailBlend", "MadeInArizona/CarPaint", "Hidden/MadeInArizona/GroundPack" };
            foreach (string shaderName in include) {
                var shader = Shader.Find(shaderName); if (!shader) continue;
                bool exists = false; for (int i = 0; i < shaders.arraySize; i++) if (shaders.GetArrayElementAtIndex(i).objectReferenceValue == shader) exists = true;
                if (!exists) { shaders.InsertArrayElementAtIndex(shaders.arraySize); shaders.GetArrayElementAtIndex(shaders.arraySize - 1).objectReferenceValue = shader; }
            }
            gs.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(pipeline);
            string normalPath=Root+"/Resources/Rendering/SurfaceNormal.asset";
            var normal=AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
            if(!normal) { normal=WorldArt.SurfaceNormal();AssetDatabase.CreateAsset(normal,normalPath); }
            string detailPath=Root+"/Resources/Rendering/SurfaceDetail.mat";
            if(!File.Exists(detailPath)) {
                var detail=new Material(Shader.Find("Universal Render Pipeline/Lit"));detail.SetTexture("_BumpMap",normal);detail.EnableKeyword("_NORMALMAP");detail.EnableKeyword("_EMISSION");detail.SetColor("_EmissionColor",Color.white);AssetDatabase.CreateAsset(detail,detailPath);
            }
            string[] materialShaders = { "Universal Render Pipeline/Lit", "Universal Render Pipeline/Unlit", "Universal Render Pipeline/Particles/Unlit" };
            for (int i = 0; i < materialShaders.Length; i++) {
                string path = Root + "/Resources/Rendering/RuntimeShader" + i + ".mat";
                if (!File.Exists(path)) {
                    var mat = new Material(Shader.Find(materialShaders[i]));
                    if (i == 2) { mat.SetFloat("_Surface", 1); mat.SetFloat("_Blend", 0); mat.SetFloat("_SrcBlend", 5); mat.SetFloat("_DstBlend", 10); mat.SetFloat("_ZWrite", 0); mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); mat.renderQueue = 3000; }
                    AssetDatabase.CreateAsset(mat, path);
                }
            }
        }

        // SSAO is a real URP renderer feature. Keep it downsampled with low samples so the tuning control
        // remains suitable for the game's wide orthographic camera on desktop hardware.
        static void EnsureAmbientOcclusion(UniversalRendererData renderer)
        {
            var ao = renderer.rendererFeatures.OfType<ScreenSpaceAmbientOcclusion>().FirstOrDefault();
            if (!ao) {
                ao = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
                ao.name = "Screen Space Ambient Occlusion";
                AssetDatabase.AddObjectToAsset(ao, renderer);
                renderer.rendererFeatures.Add(ao);
                var data = new SerializedObject(renderer);
                var featureMap = data.FindProperty("m_RendererFeatureMap");
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(ao, out _, out long localId);
                featureMap.arraySize++;
                featureMap.GetArrayElementAtIndex(featureMap.arraySize - 1).longValue = localId;
                data.ApplyModifiedPropertiesWithoutUndo();
            }
            var serializedAo = new SerializedObject(ao);
            var settings = serializedAo.FindProperty("m_Settings");
            settings.FindPropertyRelative("Downsample").boolValue = true;
            settings.FindPropertyRelative("Source").enumValueIndex = 0; // Depth avoids a normal prepass.
            settings.FindPropertyRelative("NormalSamples").enumValueIndex = 0;
            settings.FindPropertyRelative("Intensity").floatValue = .45f;
            settings.FindPropertyRelative("DirectLightingStrength").floatValue = .2f;
            settings.FindPropertyRelative("Radius").floatValue = .035f;
            settings.FindPropertyRelative("Samples").enumValueIndex = 2; // Four samples.
            settings.FindPropertyRelative("BlurQuality").enumValueIndex = 2; // Kawase blur.
            settings.FindPropertyRelative("Falloff").floatValue = 80f;
            serializedAo.ApplyModifiedPropertiesWithoutUndo();
            ao.SetActive(true);
            renderer.SetDirty();
            EditorUtility.SetDirty(ao);
            EditorUtility.SetDirty(renderer);
        }

        static void Persist<T>(IEnumerable<T> items) where T : ScriptableObject
        {
            string folder = Root + "/Resources/Content/" + typeof(T).Name;
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(Root + "/Resources/Content", typeof(T).Name);
            foreach (var item in items) {
                string path = folder + "/" + item.name + ".asset";
                if (AssetDatabase.Contains(item)) {
                    string current = AssetDatabase.GetAssetPath(item);
                    if (current != path && !File.Exists(path)) AssetDatabase.MoveAsset(current, path);
                } else if (!File.Exists(path)) AssetDatabase.CreateAsset(item, path);
            }
        }
        static void PersistGeometry(GameObject root)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>()) {
                foreach (var m in r.sharedMaterials) {
                    if (!m || AssetDatabase.Contains(m)) continue;
                    string name = "material_" + Hash128.Compute(m.name + m.color).ToString();
                    string path = Root + "/Materials/" + name + ".mat";
                    var old = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (old) { var mats = r.sharedMaterials; for (int i = 0; i < mats.Length; i++) if (mats[i] == m) mats[i] = old; r.sharedMaterials = mats; }
                    else AssetDatabase.CreateAsset(m, path);
                }
            }
            foreach (var f in root.GetComponentsInChildren<MeshFilter>()) {
                var mesh = f.sharedMesh;
                if (!mesh || AssetDatabase.Contains(mesh)) continue;
                string path = Root + "/Materials/mesh_" + Hash128.Compute(f.name + mesh.vertexCount + mesh.bounds.ToString()).ToString() + ".asset";
                var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (old) f.sharedMesh = old; else AssetDatabase.CreateAsset(mesh, path);
            }
        }
        static void CreatePrefabs()
        {
            foreach (var v in ContentCatalog.Vehicles) {
                var root = new GameObject(v.displayName + " • Procedural visual");
                VehicleVisual.Build(v, root.transform, false);
                PersistGeometry(root);
                PrefabUtility.SaveAsPrefabAsset(root, Root + "/Resources/Prefabs/" + v.id + ".prefab");
                UnityEngine.Object.DestroyImmediate(root);
            }
            var props = new GameObject("Propane • Chain reaction");
            RoadsideProps.Propane(props.transform, Vector3.zero, true); PersistGeometry(props);
            PrefabUtility.SaveAsPrefabAsset(props, Root + "/Prefabs/PropaneTank.prefab"); UnityEngine.Object.DestroyImmediate(props);
            props = new GameObject("Shipping crate • Destructible");
            RoadsideProps.Crate(props.transform, Vector3.zero, 1.2f); PersistGeometry(props);
            PrefabUtility.SaveAsPrefabAsset(props, Root + "/Prefabs/SalvageCrate.prefab"); UnityEngine.Object.DestroyImmediate(props);
        }

        [MenuItem("Made in Arizona/2 · Build macOS Universal")]
        public static void BuildMac() { Prepare(); Build(BuildTarget.StandaloneOSX, "Builds/macOS/Made in Arizona.app"); }
        [MenuItem("Made in Arizona/3 · Build Windows x64")]
        public static void BuildWindows() { Prepare(); Build(BuildTarget.StandaloneWindows64, "Builds/Windows/Made in Arizona.exe"); }
        [MenuItem("Made in Arizona/4 · Build both desktop players")]
        public static void BuildAll() { Prepare(); Validate(); Build(BuildTarget.StandaloneOSX, "Builds/macOS/Made in Arizona.app"); Build(BuildTarget.StandaloneWindows64, "Builds/Windows/Made in Arizona.exe"); }
        static void Build(BuildTarget target, string destination)
        {
            string buildRoot = Environment.GetEnvironmentVariable("MIA_BUILD_ROOT");
            if (!string.IsNullOrEmpty(buildRoot)) destination = Path.Combine(buildRoot, destination);
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, target)) throw new BuildFailedException("Install Unity Hub module for " + target + " before building.");
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { ScenePath }, locationPathName = destination, target = target, options = BuildOptions.Development });
            string summary = target + " " + report.summary.result + " | " + report.summary.totalErrors + " errors | " + report.summary.totalSize + " bytes | " + report.summary.totalTime;
            string reportDirectory = string.IsNullOrEmpty(buildRoot) ? "Builds" : Path.Combine(buildRoot, "Builds");
            Directory.CreateDirectory(reportDirectory); File.WriteAllText(Path.Combine(reportDirectory, target + "-report.txt"), summary);
            Debug.Log("MIA_BUILD: " + summary);
            if (report.summary.result != BuildResult.Succeeded) throw new BuildFailedException(summary);
        }

        [MenuItem("Made in Arizona/Validate content and systems")]
        public static void Validate()
        {
            ContentCatalog.EnsureLoaded();
            if (ContentCatalog.Missions.Length != 15 || ContentCatalog.Vehicles.Length != 8) throw new Exception("Catalog count mismatch");
            var ids = new HashSet<string>();
            foreach (var m in ContentCatalog.Missions) {
                if (!ids.Add(m.id) || string.IsNullOrEmpty(m.opening) || string.IsNullOrEmpty(m.closing) || m.timeLimit <= 0) throw new Exception("Invalid mission: " + m.id);
            }
            var save = new SaveData(); save.Normalize();
            var baseStats = GarageManager.StatsFor(ContentCatalog.Vehicles[0], save);
            save.ownedParts.Add(ContentCatalog.Parts[0].id); save.installedParts.Add(ContentCatalog.Parts[0].id);
            if (GarageManager.StatsFor(ContentCatalog.Vehicles[0], save).horsepower <= baseStats.horsepower) throw new Exception("Upgrade does not affect vehicle");
            Debug.Log("MIA_VALIDATION_PASS: catalog identities, narrative completeness, tuning and content bounds.");
        }
    }
}
