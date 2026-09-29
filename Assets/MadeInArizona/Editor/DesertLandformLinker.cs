#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MadeInArizona.Editor
{
    /// <summary>Assigns the imported desert packs (Tiny Teacup cliffs/rocks, Runemark rocks) to the landform catalog.</summary>
    public static class DesertLandformLinker
    {
        const string Tiny = "Assets/Tiny Teacup Studio/Low Poly Desert Environment/Prefabs/";
        const string Rune = "Assets/Runemark Studio/Freebies/Polygon Desert Pack/Prefabs/";
        const string CatalogPath = "Assets/MadeInArizona/Resources/Landforms/DesertLandformCatalog.asset";

        static GameObject[] Load(string folder, params string[] names) =>
            names.Select(n => AssetDatabase.LoadAssetAtPath<GameObject>(folder + n + ".prefab")).Where(p => p).ToArray();

        [MenuItem("Made in Arizona/Link desert packs to landform catalog")]
        public static void Link()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<DesertLandformCatalog>(CatalogPath);
            if (!catalog) { Debug.LogWarning("Landform catalog missing; run 'Create desert landform catalog' first."); return; }
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Tiny Teacup Studio", "Assets/Runemark Studio" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var rs = go.GetComponentsInChildren<Renderer>(true);
                if (rs.Length == 0) continue;
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                Debug.Log($"MIA_BOUNDS {path} size={b.size:F1} colliders={go.GetComponentsInChildren<Collider>(true).Length}");
            }
            Assign(catalog);
        }

        static void Assign(DesertLandformCatalog c)
        {
            // Models are scaled uniformly to the planned footprint, so pick by proportion: chunky cliff corners for
            // mesas, tall Runemark rocks for buttes, low wide rocks for boulders, ~1:1 rocks for cover.
            c.mesas = Load(Tiny, "CliffCorner_01", "CliffCorner_02");
            c.buttes = Load(Rune, "rock04", "rock06");
            c.cliffs = Load(Tiny, "Cliff_01");
            c.boulders = Load(Tiny, "Rock_01", "Rock_02", "Rock_03");
            c.coverRocks = Load(Rune, "rock01", "rock02", "rock03", "rock05", "rock07", "rock08");
            EditorUtility.SetDirty(c); AssetDatabase.SaveAssets();
            Debug.Log("MIA_LINK_OK");
        }
    }
}
#endif
