#if UNITY_EDITOR
using System.IO;
using System.Collections.Generic;
using UnityEngine.Rendering;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MadeInArizona.Editor
{
    public static class PandazoleNatureCatalogBuilder
    {
        const string Pack="Assets/Pandazole_Ultimate_Pack/Pandazole Nature Environment Pack";
        const string Output="Assets/MadeInArizona/Resources/Nature/PandazoleNatureCatalog.asset";

        [MenuItem("Made in Arizona/Build Pandazole nature catalog")]
        public static void Build()
        {
            if(!AssetDatabase.IsValidFolder(Pack)) { Debug.LogWarning("Pandazole nature pack is not imported; procedural ecology remains available.");return; }
            Directory.CreateDirectory(Path.GetDirectoryName(Output));
            AssetDatabase.Refresh();
            var catalog=AssetDatabase.LoadAssetAtPath<PandazoleNatureCatalog>(Output);
            if(!catalog){catalog=ScriptableObject.CreateInstance<PandazoleNatureCatalog>();AssetDatabase.CreateAsset(catalog,Output);}
            catalog.atlas=AssetDatabase.LoadAssetAtPath<Texture2D>(Pack+"/Textures/PandaMat.png");
            // Native model review: 01–04 are conifers, 05 is broadleaf; 06–12
            // contain palms and tropical shapes unsuited to northern Arizona forests.
            catalog.pines=Find("Tree_*_Spring",4);catalog.broadleafTrees=Find("Tree_05_Spring",1);
            catalog.cacti=Find("Cactus_*_A",14);catalog.rocks=Find("HardRock_*",14);
            catalog.grasses=Find("Grass_*",12);catalog.bushes=Find("Bush_*",8);
            EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
            Debug.Log("MIA_PANDAZOLE_OK: shared low-poly tree, cactus, rock, grass and bush meshes cataloged for streamed runtime batches.");
        }
        static Mesh[] Find(string pattern,int limit)
        {
            var paths=AssetDatabase.FindAssets("t:Model",new[]{Pack+"/Models"}).Select(AssetDatabase.GUIDToAssetPath)
                .Where(p=>Match(Path.GetFileNameWithoutExtension(p).ToLowerInvariant(),pattern.ToLowerInvariant())&&!PandazoleNatureCatalog.IsExcluded(Path.GetFileNameWithoutExtension(p))).OrderBy(p=>p).Take(limit).ToArray();
            foreach(string path in paths)if(AssetImporter.GetAtPath(path) is ModelImporter importer&&!importer.isReadable){importer.isReadable=true;importer.SaveAndReimport();}
            // A model can contain separate trunk and canopy MeshFilters. Catalog complete
            // models, otherwise runtime height normalization scales each loose part as a tree.
            string folder=Path.GetDirectoryName(Output)+"/Meshes";
            if(!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(Path.GetDirectoryName(Output),"Meshes");
            var result=new List<Mesh>();
            foreach(string path in paths)
            {
                var model=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var pieces=new List<CombineInstance>();
                foreach(var filter in model.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mesh=filter.sharedMesh;if(!mesh||mesh.vertexCount==0)continue;
                    for(int sub=0;sub<mesh.subMeshCount;sub++) pieces.Add(new CombineInstance {
                        mesh=mesh,subMeshIndex=sub,transform=model.transform.worldToLocalMatrix*filter.transform.localToWorldMatrix });
                }
                if(pieces.Count==0)continue;
                string name=Path.GetFileNameWithoutExtension(path);
                var combined=new Mesh { name=name,indexFormat=IndexFormat.UInt32 };
                combined.CombineMeshes(pieces.ToArray(),true,true);combined.RecalculateBounds();
                string destination=folder+"/"+name+".asset";
                var saved=AssetDatabase.LoadAssetAtPath<Mesh>(destination);
                if(saved){EditorUtility.CopySerialized(combined,saved);Object.DestroyImmediate(combined);EditorUtility.SetDirty(saved);}
                else {saved=combined;AssetDatabase.CreateAsset(saved,destination);}
                result.Add(saved);
            }
            return result.ToArray();
        }
        static bool Match(string name,string pattern)
        {
            string[] parts=pattern.Split('*');int at=0;
            foreach(string part in parts){if(part.Length==0)continue;at=name.IndexOf(part,at,System.StringComparison.Ordinal);if(at<0)return false;at+=part.Length;}
            return true;
        }
    }
}
#endif
