using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace MadeInArizona.Editor
{
    /// <summary>
    /// Renders every vehicle model from four sides onto contact sheets, so a body can be judged by eye without building
    /// a player. Run without -nographics:
    /// Unity.exe -batchmode -quit -projectPath . -executeMethod MadeInArizona.Editor.VehicleLineupReview.Render
    /// Writes Builds/VehicleReview/&lt;id&gt;.png (side, front three-quarter, rear three-quarter, gameplay angle), plus
    /// lineup-side.png and lineup-quarter.png with every car at half size. Pass -miaVehicles a,b,c to render only those ids.
    /// </summary>
    public static class VehicleLineupReview
    {
        const int TileWidth = 800, TileHeight = 500;

        [MenuItem("Made in Arizona/Review/Render vehicle lineup")]
        public static void Render()
        {
            ContentCatalog.EnsureLoaded();
            // A shader still compiling draws nothing: the first tiles came out as wheels and trim with no body.
            ShaderUtil.allowAsyncCompilation = false;
            string directory = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Builds", "VehicleReview");
            Directory.CreateDirectory(directory);
            string[] arguments = System.Environment.GetCommandLineArgs();
            int filter = System.Array.IndexOf(arguments, "-miaVehicles");
            var wanted = filter >= 0 && filter + 1 < arguments.Length ? new HashSet<string>(arguments[filter + 1].Split(',')) : null;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var sun = new GameObject("Review sun").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.25f; sun.shadows = LightShadows.Soft; sun.transform.rotation = Quaternion.Euler(48, -38, 0);
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.62f, .62f, .66f);
            SunGlint.Apply(new DevTuning());
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = Vector3.one * 6;
            var groundMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")); groundMaterial.SetColor("_BaseColor", new Color(.78f, .68f, .55f)); groundMaterial.SetFloat("_Smoothness", .05f);
            ground.GetComponent<Renderer>().sharedMaterial = groundMaterial;
            var camera = new GameObject("Review camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.62f, .74f, .86f); camera.fieldOfView = 30;
            camera.nearClipPlane = .1f; camera.farClipPlane = 200;
            // Side, front three-quarter, rear three-quarter, then the gameplay camera's steep look from behind.
            var views = new[] { new Vector3(8, 90, 0), new Vector3(16, 215, 0), new Vector3(18, 35, 0), new Vector3(52, 0, 0) };
            var target = new RenderTexture(TileWidth, TileHeight, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            bool warm = false;
            var lineups = new[] { new List<Color[]>(), new List<Color[]>() };
            foreach (var definition in ContentCatalog.Vehicles)
            {
                if (wanted != null && !wanted.Contains(definition.id)) continue;
                var holder = new GameObject(definition.id);
                var visual = VehicleVisual.Build(definition, holder.transform, false);
                var renderers = holder.GetComponentsInChildren<Renderer>();
                // Framed on the bodywork: a whip aerial would shrink the car to a speck. It gets a tile of its own below.
                Bounds bounds = renderers[0].bounds, all = renderers[0].bounds;
                foreach (var renderer in renderers) { all.Encapsulate(renderer.bounds); if (!renderer.GetComponentInParent<WhipFlag>()) bounds.Encapsulate(renderer.bounds); }
                // Stand the car on its tyres.
                ground.transform.position = new Vector3(0, bounds.min.y, 0);
                var sheet = new Texture2D(TileWidth * 2, TileHeight * 2, TextureFormat.RGB24, false);
                // The pipeline's first request comes back without the custom shaders' passes: spend one before the first tile.
                if (!warm) { camera.transform.position = bounds.center - Vector3.forward * 10; RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target }); warm = true; }
                for (int view = 0; view < views.Length; view++)
                {
                    camera.transform.rotation = Quaternion.Euler(views[view]);
                    float reach = bounds.extents.magnitude;
                    camera.transform.position = bounds.center + Vector3.down * bounds.extents.y * .1f - camera.transform.forward * reach * (view == 0 ? 2.6f : 3.1f);
                    RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
                    var previous = RenderTexture.active; RenderTexture.active = target;
                    var tile = new Texture2D(TileWidth, TileHeight, TextureFormat.RGB24, false);
                    tile.ReadPixels(new Rect(0, 0, TileWidth, TileHeight), 0, 0); tile.Apply(); RenderTexture.active = previous;
                    sheet.SetPixels(view % 2 * TileWidth, (1 - view / 2) * TileHeight, TileWidth, TileHeight, tile.GetPixels());
                    if (view < 2) lineups[view].Add(Halved(tile.GetPixels()));
                    Object.DestroyImmediate(tile);
                }
                File.WriteAllBytes(Path.Combine(directory, definition.id + ".png"), sheet.EncodeToPNG());
                if (all.size.y > bounds.size.y * 1.5f)
                {
                    camera.transform.rotation = Quaternion.Euler(6, 140, 0);
                    camera.transform.position = all.center - camera.transform.forward * all.extents.magnitude * 3.9f;
                    RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
                    var previous = RenderTexture.active; RenderTexture.active = target;
                    var tall = new Texture2D(TileWidth, TileHeight, TextureFormat.RGB24, false);
                    tall.ReadPixels(new Rect(0, 0, TileWidth, TileHeight), 0, 0); tall.Apply(); RenderTexture.active = previous;
                    File.WriteAllBytes(Path.Combine(directory, definition.id + "-whole.png"), tall.EncodeToPNG());
                    Object.DestroyImmediate(tall);
                }
                Object.DestroyImmediate(sheet);
                int triangles = 0; foreach (var meshFilter in holder.GetComponentsInChildren<MeshFilter>()) if (meshFilter.sharedMesh) triangles += meshFilter.sharedMesh.triangles.Length / 3;
                Debug.Log($"MIA_VEHICLE_REVIEW {definition.id}: {renderers.Length} renderers, {triangles} triangles, size {bounds.size.x:0.00} x {bounds.size.y:0.00} x {bounds.size.z:0.00}");
                Object.DestroyImmediate(holder);
            }
            target.Release(); Object.DestroyImmediate(target);
            for (int view = 0; view < lineups.Length; view++)
            {
                const int columns = 4;
                int count = lineups[view].Count, rows = Mathf.CeilToInt(count / (float)columns);
                if (count == 0) continue;
                var lineup = new Texture2D(columns * TileWidth / 2, rows * TileHeight / 2, TextureFormat.RGB24, false);
                var fill = new Color[lineup.width * lineup.height]; for (int i = 0; i < fill.Length; i++) fill[i] = camera.backgroundColor; lineup.SetPixels(fill);
                for (int i = 0; i < count; i++) lineup.SetPixels(i % columns * TileWidth / 2, (rows - 1 - i / columns) * TileHeight / 2, TileWidth / 2, TileHeight / 2, lineups[view][i]);
                File.WriteAllBytes(Path.Combine(directory, view == 0 ? "lineup-side.png" : "lineup-quarter.png"), lineup.EncodeToPNG());
                Object.DestroyImmediate(lineup);
            }
        }

        static Color[] Halved(Color[] tile)
        {
            int width = TileWidth / 2, height = TileHeight / 2;
            var half = new Color[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    half[y * width + x] = (tile[y * 2 * TileWidth + x * 2] + tile[y * 2 * TileWidth + x * 2 + 1] + tile[(y * 2 + 1) * TileWidth + x * 2] + tile[(y * 2 + 1) * TileWidth + x * 2 + 1]) * .25f;
            return half;
        }
    }
}
