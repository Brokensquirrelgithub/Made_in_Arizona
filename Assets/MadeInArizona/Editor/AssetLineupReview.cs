using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace MadeInArizona.Editor
{
    /// <summary>
    /// Renders contact sheets of candidate art (models not yet used by the game) so they can be chosen by eye.
    /// Run without -nographics: Unity.exe -batchmode -quit -projectPath . -executeMethod MadeInArizona.Editor.AssetLineupReview.Render
    /// Writes Builds/AssetReview/&lt;group&gt;.png plus &lt;group&gt;.txt listing the tiles row by row.
    /// </summary>
    public static class AssetLineupReview
    {
        const int Tile = 220, Columns = 8;
        const string Pandazole = "Assets/Pandazole_Ultimate_Pack/Pandazole Nature Environment Pack/Models/";

        [MenuItem("Made in Arizona/Review/Render candidate asset lineups")]
        public static void Render()
        {
            var groups = new List<(string name, List<string> paths)>
            {
                ("trees-spring", Range(13, 37, i => Pandazole + $"Tree_{i:00}_Spring.fbx")),
                ("trees-fall", Range(1, 37, i => Pandazole + $"Tree_{i:00}_Fall.fbx")),
                ("trees-winter", Range(1, 12, i => Pandazole + $"Tree_{i:00}_Winter.fbx")),
                ("cacti", Range(1, 39, i => Pandazole + $"Cactus_{i:00}_A.fbx")),
                ("softrocks", Range(1, 32, i => Pandazole + $"SoftRock_{i:00}.fbx")),
                ("bushes", Range(1, 15, i => Pandazole + $"Bush_{i:00}.fbx")),
                ("desert-packs", new List<string> {
                    "Assets/Runemark Studio/Freebies/Polygon Desert Pack/Prefabs/Aloe.prefab",
                    "Assets/Runemark Studio/Freebies/Polygon Desert Pack/Prefabs/Cactus1.prefab",
                    "Assets/Runemark Studio/Freebies/Polygon Desert Pack/Prefabs/Cactus2.prefab",
                    "Assets/Runemark Studio/Freebies/Polygon Desert Pack/Prefabs/Cactus3.prefab",
                    "Assets/Tiny Teacup Studio/Low Poly Desert Environment/Prefabs/Cactus_01.prefab",
                    "Assets/Tiny Teacup Studio/Low Poly Desert Environment/Prefabs/Cactus_02.prefab",
                    "Assets/Tiny Teacup Studio/Low Poly Desert Environment/Prefabs/Cactus_03.prefab",
                    "Assets/Tiny Teacup Studio/Low Poly Desert Environment/Prefabs/Tree_01.prefab",
                    "Assets/Tiny Teacup Studio/Low Poly Desert Environment/Prefabs/Ground_01.prefab" }),
            };
            string directory = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Builds", "AssetReview");
            Directory.CreateDirectory(directory);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var sun = new GameObject("Review sun").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.2f; sun.transform.rotation = Quaternion.Euler(45, -35, 0);
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.55f, .55f, .58f);
            var camera = new GameObject("Review camera").AddComponent<Camera>();
            camera.orthographic = true; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.82f, .8f, .74f);
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            foreach (var (name, paths) in groups)
            {
                var found = paths.Where(p => AssetDatabase.LoadAssetAtPath<GameObject>(p)).ToList();
                if (found.Count == 0) continue;
                int rows = Mathf.CeilToInt(found.Count / (float)Columns);
                var sheet = new Texture2D(Columns * Tile, rows * Tile, TextureFormat.RGB24, false);
                var fill = new Color[sheet.width * sheet.height]; for (int i = 0; i < fill.Length; i++) fill[i] = camera.backgroundColor; sheet.SetPixels(fill);
                var target = RenderTexture.GetTemporary(Tile, Tile, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                var listing = new List<string>();
                for (int i = 0; i < found.Count; i++)
                {
                    var instance = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(found[i]));
                    var renderers = instance.GetComponentsInChildren<Renderer>();
                    // Pack materials use the built-in shader: redraw them with URP Lit and the same texture and colour.
                    foreach (var renderer in renderers)
                    {
                        var materials = renderer.sharedMaterials;
                        for (int m = 0; m < materials.Length; m++)
                        {
                            var source = materials[m]; var copy = new Material(lit);
                            if (source)
                            {
                                Texture texture = source.HasProperty("_BaseMap") ? source.GetTexture("_BaseMap") : null;
                                if (!texture && source.HasProperty("_MainTex")) texture = source.GetTexture("_MainTex");
                                if (texture) copy.SetTexture("_BaseMap", texture);
                                if (source.HasProperty("_Color")) copy.SetColor("_BaseColor", source.GetColor("_Color"));
                            }
                            materials[m] = copy;
                        }
                        renderer.sharedMaterials = materials;
                    }
                    if (renderers.Length > 0)
                    {
                        Bounds b = renderers[0].bounds; foreach (var r in renderers) b.Encapsulate(r.bounds);
                        // A three-quarter view from slightly above, framing the model's bounds.
                        camera.transform.rotation = Quaternion.Euler(18, -30, 0);
                        camera.transform.position = b.center - camera.transform.forward * (b.extents.magnitude * 4 + 5);
                        camera.orthographicSize = Mathf.Max(b.extents.y, Mathf.Max(b.extents.x, b.extents.z)) * 1.15f;
                        camera.nearClipPlane = .01f; camera.farClipPlane = b.extents.magnitude * 10 + 20;
                        RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
                        var previous = RenderTexture.active; RenderTexture.active = target;
                        var tile = new Texture2D(Tile, Tile, TextureFormat.RGB24, false);
                        tile.ReadPixels(new Rect(0, 0, Tile, Tile), 0, 0); tile.Apply(); RenderTexture.active = previous;
                        int column = i % Columns, row = rows - 1 - i / Columns;
                        sheet.SetPixels(column * Tile, row * Tile, Tile, Tile, tile.GetPixels());
                        Object.DestroyImmediate(tile);
                        listing.Add($"{i,3}  row {i / Columns + 1} col {column + 1}  {Path.GetFileNameWithoutExtension(found[i])}  size {b.size.x:0.0} x {b.size.y:0.0} x {b.size.z:0.0}");
                    }
                    Object.DestroyImmediate(instance);
                }
                RenderTexture.ReleaseTemporary(target);
                File.WriteAllBytes(Path.Combine(directory, name + ".png"), sheet.EncodeToPNG());
                File.WriteAllLines(Path.Combine(directory, name + ".txt"), listing);
                Object.DestroyImmediate(sheet);
                Debug.Log("MIA_ASSET_REVIEW " + name + ": " + found.Count + " models");
            }
        }

        static List<string> Range(int first, int last, System.Func<int, string> path)
        {
            var list = new List<string>();
            for (int i = first; i <= last; i++) list.Add(path(i));
            return list;
        }
    }
}
