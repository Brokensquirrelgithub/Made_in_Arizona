#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace MadeInArizona.Editor
{
    /// <summary>Turns the Asset Store package's editor paths into build-safe references.</summary>
    public sealed class OutdoorGroundTextureImporter : AssetPostprocessor
    {
        const string GroundResourcePath = "Assets/MadeInArizona/Resources/GroundTextureSet.asset";
        const string WallResourcePath = "Assets/MadeInArizona/Resources/WallTextureSet.asset";

        [MenuItem("Made in Arizona/Import Downloaded Ground Texture Pack")]
        static void ImportDownloadedGroundPack() => ImportDownloadedPackage(
            "A dogs life software/Textures MaterialsGround/Outdoor Ground Textures.unitypackage");

        [MenuItem("Made in Arizona/Import Downloaded Wall Texture Pack")]
        static void ImportDownloadedWallPack() => ImportDownloadedPackage(
            "A dogs life software/Textures MaterialsBricks/18 High Resolution Wall Textures.unitypackage");

        [InitializeOnLoadMethod]
        static void QueueRefresh() => EditorApplication.delayCall += RefreshIfInstalled;

        [MenuItem("Made in Arizona/Refresh Licensed Environment Textures")]
        public static void RefreshIfInstalled()
        {
            if (!AssetDatabase.IsValidFolder("Assets/ADG_Textures")) return;

            var diffuse = new Texture2D[GroundTextureSet.TextureCount];
            var normal = new Texture2D[GroundTextureSet.TextureCount];
            var occlusion = new Texture2D[GroundTextureSet.TextureCount];
            var height = new Texture2D[GroundTextureSet.TextureCount];
            int found = 0;
            for (int i = 0; i < GroundTextureSet.TextureCount; i++)
            {
                string stem = "ground" + (i + 1);
                diffuse[i] = FindExact(stem + "_Diffuse.tga") ?? FindExact(stem + "_Diffuse.tif");
                normal[i] = FindExact(stem + "_Normal.tga");
                occlusion[i] = FindExact(stem + "_Ambient_Occlusion.tga");
                height[i] = FindExact(stem + "_Height.tga");
                if (diffuse[i]) found++;
                ConfigureNormal(normal[i]);
            }
            if (found > 0)
            {
                var set = AssetDatabase.LoadAssetAtPath<GroundTextureSet>(GroundResourcePath);
                if (!set)
                {
                    set = ScriptableObject.CreateInstance<GroundTextureSet>();
                    AssetDatabase.CreateAsset(set, GroundResourcePath);
                }
                set.diffuse = diffuse; set.normal = normal; set.occlusion = occlusion; set.height = height;
                EditorUtility.SetDirty(set);
                Debug.Log("MIA_GROUND_TEXTURES linked " + found + "/" + GroundTextureSet.TextureCount + " Outdoor Ground Textures into the player build.");
            }
            RefreshWalls(); AssetDatabase.SaveAssets();
        }

        static void RefreshWalls()
        {
            var diffuse = new Texture2D[WallTextureSet.TextureCount];
            var normal = new Texture2D[WallTextureSet.TextureCount];
            var occlusion = new Texture2D[WallTextureSet.TextureCount];
            var metallic = new Texture2D[WallTextureSet.TextureCount];
            int found = 0;
            for (int i = 0; i < WallTextureSet.TextureCount; i++)
            {
                string stem = "wall" + (i + 1).ToString("00");
                diffuse[i] = FindExact(stem + "_Diffuse.tga") ?? FindExact(stem + "_Diffuse.tif");
                normal[i] = FindExact(stem + "_Normal.tga");
                occlusion[i] = FindExact(stem + "_Ambient_Occlusion.tga");
                metallic[i] = FindExact(stem + "_Metallic.tga");
                if (diffuse[i]) found++;
                ConfigureNormal(normal[i]);
            }
            if (found == 0) return;
            var set = AssetDatabase.LoadAssetAtPath<WallTextureSet>(WallResourcePath);
            if (!set)
            {
                set = ScriptableObject.CreateInstance<WallTextureSet>();
                AssetDatabase.CreateAsset(set, WallResourcePath);
            }
            set.diffuse = diffuse; set.normal = normal; set.occlusion = occlusion; set.metallic = metallic;
            EditorUtility.SetDirty(set);
            Debug.Log("MIA_WALL_TEXTURES linked " + found + "/" + WallTextureSet.TextureCount + " wall textures into the player build.");
        }

        static Texture2D FindExact(string fileName)
        {
            string query = fileName.Substring(0, fileName.Length - 4) + " t:Texture2D";
            foreach (string guid in AssetDatabase.FindAssets(query, new[] { "Assets/ADG_Textures" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith("/" + fileName, StringComparison.OrdinalIgnoreCase)) return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            return null;
        }

        static void ImportDownloadedPackage(string relativePath)
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string packagePath = System.IO.Path.Combine(home, "Library/Unity/Asset Store-5.x", relativePath);
            if (!System.IO.File.Exists(packagePath))
            {
                Debug.LogError("Downloaded Unity package was not found: " + packagePath);
                return;
            }
            Debug.Log("Importing licensed Asset Store package: " + System.IO.Path.GetFileName(packagePath));
            AssetDatabase.ImportPackage(packagePath, false);
        }

        static void ConfigureNormal(Texture2D texture)
        {
            if (!texture) return;
            string path = AssetDatabase.GetAssetPath(texture);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer && importer.textureType != TextureImporterType.NormalMap)
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.SaveAndReimport();
            }
        }

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (string path in imported)
                if (path.IndexOf("ADG_Textures", StringComparison.OrdinalIgnoreCase) >= 0) { EditorApplication.delayCall += RefreshIfInstalled; return; }
        }
    }
}
#endif
