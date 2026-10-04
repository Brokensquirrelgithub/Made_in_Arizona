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
            // 23 and 24 (layered conifers) were added after the contact-sheet review of 13–37.
            catalog.pines=Find("Tree_*_Spring",4).Concat(Named("Tree_23_Spring","Tree_24_Spring")).ToArray();catalog.broadleafTrees=Find("Tree_05_Spring",1);
            catalog.rocks=Find("HardRock_*",14);
            catalog.grasses=Find("Grass_*",12);catalog.bushes=Find("Bush_*",8);
            // Biome picks (Builds/AssetReview contact sheets): Sonoran organ pipes, columns, barrels, prickly pear and an
            // ocotillo; the chunky blocks, bent single stems and thin segmented shapes are left out.
            catalog.cacti=Named("Cactus_03_A","Cactus_05_A","Cactus_08_A","Cactus_09_A","Cactus_10_A","Cactus_11_A","Cactus_13_A","Cactus_14_A","Cactus_15_A",
                "Cactus_18_A","Cactus_19_A","Cactus_20_A","Cactus_21_A","Cactus_22_A","Cactus_23_A","Cactus_24_A","Cactus_26_A","Cactus_27_A","Cactus_28_A");
            catalog.saguaros=Named("Cactus_17_A","Cactus_35_A","Cactus_36_A","Cactus_37_A","Cactus_38_A");
            catalog.redRockPlants=Named("Cactus_27_A","Cactus_28_A","Cactus_13_A","Cactus_15_A","Cactus_24_A");
            catalog.desertTrees=Named("Tree_26_Spring","Tree_27_Spring","Tree_28_Spring");
            // Pack greens are saturated lime; a tint only darkens them. Bake the canopies to a dusty juniper blue-green.
            catalog.junipers=Baked(System.Array.ConvertAll(new[]{"Tree_16_Spring","Tree_17_Spring"},n=>Pack+"/Models/"+n+".fbx"),JuniperColour,catalog.atlas);
            catalog.redRocks=Named(Enumerable.Range(17,16).Select(i=>"SoftRock_"+i.ToString("00")).ToArray());
            // White-trunked 18 and 20, green and in autumn gold, as aspens; the first four conifers under snow for the peaks.
            catalog.aspens=Named("Tree_18_Spring","Tree_20_Spring","Tree_18_Fall","Tree_20_Fall");
            catalog.snowPines=Named("Tree_01_Winter","Tree_02_Winter","Tree_03_Winter","Tree_04_Winter");
            catalog.agaves=Baked("Assets/Runemark Studio/Freebies/Polygon Desert Pack/Prefabs/Aloe.prefab");
            catalog.deadTrees=Baked("Assets/Tiny Teacup Studio/Low Poly Desert Environment/Prefabs/Tree_01.prefab");
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
        /// <summary>
        /// A model from another pack, combined into one mesh whose colours are baked per face from its own materials
        /// (colour × texture at the face's UVs). UVs are set to -1, which the scenery shader reads as "no atlas", so it
        /// batches with the nature meshes.
        /// </summary>
        static Color JuniperColour(Color c)
        {
            // Greens (canopy) become juniper blue-green at their own brightness; trunks and other colours are kept.
            if(c.g<=c.r||c.g<=c.b)return c;
            float luma=c.r*.299f+c.g*.587f+c.b*.114f;
            // Dusty juniper blue-green (sRGB; Baked converts to linear).
            return new Color(.24f,.36f,.3f)*Mathf.Lerp(.75f,1.1f,luma);
        }
        static Mesh[] Baked(params string[] prefabPaths)=>Baked(prefabPaths,null,null);
        /// <param name="fallback">Texture for materials that reference none (Pandazole FBX materials leave the atlas to the pack material).</param>
        static Mesh[] Baked(string[] prefabPaths,System.Func<Color,Color> recolour,Texture2D fallback)
        {
            string folder=Path.GetDirectoryName(Output)+"/Meshes";
            if(!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(Path.GetDirectoryName(Output),"Meshes");
            var result=new List<Mesh>();
            foreach(string path in prefabPaths)
            {
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(!prefab)continue;
                var vertices=new List<Vector3>();var normals=new List<Vector3>();var colors=new List<Color>();var triangles=new List<int>();
                foreach(var renderer in prefab.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var filter=renderer.GetComponent<MeshFilter>();var mesh=filter?filter.sharedMesh:null;if(!mesh)continue;
                    if(AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(mesh)) is ModelImporter model&&!model.isReadable){model.isReadable=true;model.SaveAndReimport();mesh=filter.sharedMesh;}
                    var matrix=prefab.transform.worldToLocalMatrix*renderer.transform.localToWorldMatrix;
                    var sourceVertices=mesh.vertices;var sourceNormals=mesh.normals;var sourceUVs=mesh.uv;
                    for(int sub=0;sub<mesh.subMeshCount;sub++)
                    {
                        var material=sub<renderer.sharedMaterials.Length?renderer.sharedMaterials[sub]:null;
                        Color tint=material&&material.HasProperty("_BaseColor")?material.GetColor("_BaseColor"):material&&material.HasProperty("_Color")?material.color:Color.white;
                        Texture2D texture=null;
                        if(material)foreach(string slot in new[]{"_BaseMap","_MainTex"})if(material.HasProperty(slot)&&material.GetTexture(slot) is Texture2D found){texture=found;break;}
                        if(!texture){texture=fallback;if(fallback)tint=Color.white;}
                        if(texture&&AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) is TextureImporter importer&&!importer.isReadable){importer.isReadable=true;importer.SaveAndReimport();}
                        var indices=mesh.GetTriangles(sub);
                        for(int i=0;i<indices.Length;i+=3)
                        {
                            // One colour per face keeps the low-poly look: the texture sampled at the face's centre UV.
                            Color face=tint;
                            if(texture&&sourceUVs.Length==sourceVertices.Length)
                            {
                                Vector2 uv=(sourceUVs[indices[i]]+sourceUVs[indices[i+1]]+sourceUVs[indices[i+2]])/3;
                                face*=texture.GetPixelBilinear(uv.x,uv.y);
                            }
                            if(recolour!=null)face=recolour(face);
                            // Textures are linearised when sampled but vertex colours are not: store the baked colour linear.
                            face=face.linear;face.a=1;
                            for(int k=0;k<3;k++)
                            {
                                int v=indices[i+k];triangles.Add(vertices.Count);
                                vertices.Add(matrix.MultiplyPoint3x4(sourceVertices[v]));
                                normals.Add(sourceNormals.Length==sourceVertices.Length?matrix.MultiplyVector(sourceNormals[v]).normalized:Vector3.up);
                                colors.Add(face);
                            }
                        }
                    }
                }
                if(vertices.Count==0)continue;
                string name=Path.GetFileNameWithoutExtension(path)+"_baked";
                var baked=new Mesh{name=name,indexFormat=IndexFormat.UInt32};
                baked.SetVertices(vertices);baked.SetNormals(normals);baked.SetColors(colors);
                baked.SetUVs(0,Enumerable.Repeat(new Vector2(-1,-1),vertices.Count).ToList());baked.SetTriangles(triangles,0);baked.RecalculateBounds();
                string destination=folder+"/"+name+".asset";
                var saved=AssetDatabase.LoadAssetAtPath<Mesh>(destination);
                if(saved){EditorUtility.CopySerialized(baked,saved);Object.DestroyImmediate(baked);EditorUtility.SetDirty(saved);}
                else{saved=baked;AssetDatabase.CreateAsset(saved,destination);}
                result.Add(saved);
            }
            return result.ToArray();
        }
        static Mesh[] Named(params string[] names)=>names.SelectMany(name=>Find(name,1)).ToArray();
        static bool Match(string name,string pattern)
        {
            string[] parts=pattern.Split('*');int at=0;
            foreach(string part in parts){if(part.Length==0)continue;at=name.IndexOf(part,at,System.StringComparison.Ordinal);if(at<0)return false;at+=part.Length;}
            return true;
        }
    }
}
#endif
