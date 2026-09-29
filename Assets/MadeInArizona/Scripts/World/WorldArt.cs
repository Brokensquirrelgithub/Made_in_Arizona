using System.Collections.Generic;
using UnityEngine;

namespace MadeInArizona
{
    /// <summary>Original, reusable procedural prototype art. All dimensions are metres.</summary>
    public static class WorldArt
    {
        static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();
        public static readonly Color Sand = new Color(.66f, .40f, .23f);
        public static readonly Color Rust = new Color(.50f, .20f, .09f);
        public static readonly Color Ink = new Color(.055f, .07f, .085f);
        public static readonly Color Cream = new Color(.93f, .81f, .57f);
        public static readonly Color Turquoise = new Color(.09f, .63f, .61f);
        public static readonly Color Orange = new Color(1f, .31f, .07f);
        public static Material Material(Color color, float metal = 0, float smooth = .25f, float glow = 0)
        {
            string key = ColorUtility.ToHtmlStringRGBA(color) + "/" + metal + "/" + smooth + "/" + glow;
            if (Materials.TryGetValue(key, out Material result) && result) return result;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            result = new Material(shader) { name = "MIA_Procedural_" + key, enableInstancing = true };
            result.color = color;
            if (result.HasProperty("_BaseColor")) result.SetColor("_BaseColor", color);
            result.SetFloat("_Metallic", metal); result.SetFloat("_Smoothness", smooth);
            if(smooth<.6f && glow==0) { result.SetTexture("_BaseMap",SurfaceAlbedo()); result.SetTexture("_BumpMap",SurfaceNormal()); result.SetFloat("_BumpScale",metal>.2f?.18f:.4f); result.EnableKeyword("_NORMALMAP"); }
            if (glow > 0) { result.EnableKeyword("_EMISSION"); result.SetColor("_EmissionColor", color * glow); }
            Materials[key] = result;
            return result;
        }
        /// <summary>
        /// Glossy clear-coated car paint (MadeInArizona/CarPaint). Falls back to a smooth URP Lit finish when the
        /// custom shader is unavailable on the current graphics device.
        /// </summary>
        public static Material CarPaint(Color color, float metallic = .35f, float clearCoat = 1)
        {
            string key = "paint/" + ColorUtility.ToHtmlStringRGBA(color) + "/" + metallic + "/" + clearCoat;
            if (Materials.TryGetValue(key, out Material result) && result) return result;
            Shader shader = Shader.Find("MadeInArizona/CarPaint");
            if (!shader || !shader.isSupported) return Material(color, metallic * .6f, .86f);
            result = new Material(shader) { name = "MIA_CarPaint_" + key };
            result.SetColor("_BaseColor", color); result.SetFloat("_Metallic", metallic); result.SetFloat("_ClearCoat", clearCoat);
            result.SetFloat("_GlintType", SunGlint.Paint);
            Materials[key] = result;
            return result;
        }
        /// <summary>Vehicle window glass: the car-paint shader with a near-mirror coat, no flake and lighter wear.</summary>
        public static Material CarGlass(Color color)
        {
            string key = "carglass/" + ColorUtility.ToHtmlStringRGBA(color);
            if (Materials.TryGetValue(key, out Material result) && result) return result;
            Shader shader = Shader.Find("MadeInArizona/CarPaint");
            if (!shader || !shader.isSupported) return Material(color, 0, .92f);
            result = new Material(shader) { name = "MIA_CarGlass_" + key };
            result.SetColor("_BaseColor", color); result.SetFloat("_Metallic", 0); result.SetFloat("_Flake", 0); result.SetFloat("_Smoothness", .95f);
            result.SetFloat("_GlintType", SunGlint.Glass); result.SetFloat("_Wear", .6f);
            Materials[key] = result;
            return result;
        }
        /// <summary>
        /// Sun-reflective material (MadeInArizona/Reflective) for chrome, window glass, signs, metal and glossy plastic.
        /// <paramref name="type"/> is one of the SunGlint material types; <paramref name="wear"/> scales dust, scratches
        /// and oxidation (weathered props above 1, fresh parts below).
        /// </summary>
        public static Material Reflective(Color color, int type, float smoothness, float metallic, float wear = 1, float micro = 1)
        {
            string key = "reflect/" + ColorUtility.ToHtmlStringRGBA(color) + "/" + type + "/" + smoothness + "/" + metallic + "/" + wear + "/" + micro;
            if (Materials.TryGetValue(key, out Material result) && result) return result;
            Shader shader = Shader.Find("MadeInArizona/Reflective");
            if (!shader || !shader.isSupported) return Material(color, metallic, Mathf.Min(smoothness, .9f));
            result = new Material(shader) { name = "MIA_Reflective_" + key };
            result.color = color; result.SetColor("_BaseColor", color);
            result.SetFloat("_GlintType", type); result.SetFloat("_Smoothness", smoothness); result.SetFloat("_Metallic", metallic);
            result.SetFloat("_Wear", wear); result.SetFloat("_Micro", micro);
            Materials[key] = result;
            return result;
        }
        public static Material Chrome(Color color, float smoothness = .9f, float wear = 1) => Reflective(color, SunGlint.Chrome, smoothness, 1, wear);
        public static Material Glass(Color color, float wear = 1) => Reflective(color, SunGlint.Glass, .94f, 0, wear, .4f);
        /// <summary>Assigns <paramref name="material"/> to a primitive built by Box/Shape/Cylinder/Beam and returns it.</summary>
        public static GameObject Use(GameObject part, Material material)
        {
            if (part && material) part.GetComponent<Renderer>().sharedMaterial = material;
            return part;
        }
        public static Material GroundMaterial(Color color, int textureIndex = 0)
        {
            string key = "ground/" + ColorUtility.ToHtmlStringRGBA(color) + "/" + textureIndex;
            if (Materials.TryGetValue(key, out Material result) && result) return result;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            result = new Material(shader) { name = "MIA_Ground_" + textureIndex, enableInstancing = true };
            result.color = color; if (result.HasProperty("_BaseColor")) result.SetColor("_BaseColor", color);
            result.SetFloat("_Metallic", 0); result.SetFloat("_Smoothness", .12f);
            var set = GroundTextureSet.Load();
            Texture2D diffuse = set ? set.Diffuse(textureIndex) : null;
            Texture2D normal = set ? set.Normal(textureIndex) : null;
            Texture2D occlusion = set ? set.Occlusion(textureIndex) : null;
            result.SetTexture("_BaseMap", diffuse ? diffuse : SurfaceAlbedo());
            result.SetTexture("_BumpMap", normal ? normal : SurfaceNormal()); result.SetFloat("_BumpScale", normal ? .7f : .42f); result.EnableKeyword("_NORMALMAP");
            if (occlusion) { result.SetTexture("_OcclusionMap", occlusion); result.SetFloat("_OcclusionStrength", .62f); result.EnableKeyword("_OCCLUSIONMAP"); }
            Materials[key] = result; return result;
        }
        public static Material WallMaterial(Color color, int textureIndex = 0)
        {
            string key = "wall/" + ColorUtility.ToHtmlStringRGBA(color) + "/" + textureIndex;
            if (Materials.TryGetValue(key, out Material result) && result) return result;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            result = new Material(shader) { name = "MIA_Wall_" + textureIndex, enableInstancing = true };
            result.color = color; if (result.HasProperty("_BaseColor")) result.SetColor("_BaseColor", color);
            result.SetFloat("_Smoothness", .18f);
            var set = WallTextureSet.Load();
            Texture2D diffuse = set ? set.Diffuse(textureIndex) : null;
            Texture2D normal = set ? set.Normal(textureIndex) : null;
            Texture2D occlusion = set ? set.Occlusion(textureIndex) : null;
            Texture2D metallic = set ? set.Metallic(textureIndex) : null;
            result.SetTexture("_BaseMap", diffuse ? diffuse : SurfaceAlbedo());
            result.SetTexture("_BumpMap", normal ? normal : SurfaceNormal()); result.SetFloat("_BumpScale", normal ? .76f : .34f); result.EnableKeyword("_NORMALMAP");
            if (occlusion) { result.SetTexture("_OcclusionMap", occlusion); result.SetFloat("_OcclusionStrength", .72f); result.EnableKeyword("_OCCLUSIONMAP"); }
            if (metallic) { result.SetTexture("_MetallicGlossMap", metallic); result.SetFloat("_Metallic", .08f); result.EnableKeyword("_METALLICSPECGLOSSMAP"); }
            Materials[key] = result; return result;
        }
        public static void ApplyWall(GameObject go, Color color, int textureIndex, Vector3 size)
        {
            if (!go) return;
            var renderer = go.GetComponent<Renderer>(); if (!renderer) return;
            renderer.sharedMaterial = WallMaterial(color, textureIndex);
            float horizontal = Mathf.Max(size.x, size.z);
            var block = new MaterialPropertyBlock(); block.SetVector("_BaseMap_ST", new Vector4(Mathf.Max(1, horizontal / 3.5f), Mathf.Max(1, size.y / 3.5f), 0, 0));
            renderer.SetPropertyBlock(block);
        }
        public static void ConfigureBiomeTerrain(Material material)
        {
            if (!material) return;
            material.SetTexture("_BumpMap", SurfaceNormal());
            bool ready = GroundArrays(out var albedoArray, out var normalArray);
            if (ready) { material.SetTexture("_GroundAlbedoArray", albedoArray); material.SetTexture("_GroundNormalArray", normalArray); }
            material.SetFloat("_UseGroundTextures", ready ? 1 : 0);
        }

        const int GroundArraySize = 512;
        static RenderTexture groundAlbedo, groundNormal;
        /// <summary>
        /// Packs all Outdoor Ground Textures into two 512 px texture arrays (albedo+height, normal+AO) on the GPU.
        /// The source maps differ in size and compression, so they are resampled by blits instead of copied.
        /// </summary>
        public static bool GroundArrays(out RenderTexture albedoArray, out RenderTexture normalArray)
        {
            albedoArray = groundAlbedo; normalArray = groundNormal;
            if (groundAlbedo && groundNormal && groundAlbedo.IsCreated() && groundNormal.IsCreated()) return true;
            var set = GroundTextureSet.Load();
            var pack = Shader.Find("Hidden/MadeInArizona/GroundPack");
            if (!set || !pack || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return false;
            for (int i = 0; i < GroundTextureSet.TextureCount; i++) if (!set.Diffuse(i)) return false;
            groundAlbedo = MakeArray("Ground albedo + height array", RenderTextureReadWrite.sRGB);
            groundNormal = MakeArray("Ground normal + AO array", RenderTextureReadWrite.Linear);
            var material = new Material(pack);
            for (int i = 0; i < GroundTextureSet.TextureCount; i++)
            {
                material.SetTexture("_Second", set.Height(i) ? set.Height(i) : Texture2D.grayTexture);
                Graphics.Blit(set.Diffuse(i), groundAlbedo, material, 0, i);
                material.SetTexture("_Second", set.Occlusion(i) ? set.Occlusion(i) : Texture2D.whiteTexture);
                Graphics.Blit(set.Normal(i) ? set.Normal(i) : Texture2D.normalTexture, groundNormal, material, 1, i);
            }
            Object.Destroy(material);
            groundAlbedo.GenerateMips(); groundNormal.GenerateMips();
            albedoArray = groundAlbedo; normalArray = groundNormal;
            return true;
        }
        static RenderTexture MakeArray(string name, RenderTextureReadWrite space)
        {
            var array = new RenderTexture(GroundArraySize, GroundArraySize, 0, RenderTextureFormat.ARGB32, space)
            {
                name = name, dimension = UnityEngine.Rendering.TextureDimension.Tex2DArray, volumeDepth = GroundTextureSet.TextureCount,
                useMipMap = true, autoGenerateMips = false, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4
            };
            array.Create();
            return array;
        }
        static Texture2D albedo;
        static Texture2D SurfaceAlbedo()
        {
            if(albedo)return albedo;
            const int size=128;
            albedo=new Texture2D(size,size,TextureFormat.RGBA32,true,true){name="Masked dust and pitted paint",wrapMode=TextureWrapMode.Repeat,anisoLevel=4};
            var pixels=new Color[size*size];
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                float u=x/(float)size,v=y/(float)size;
                float mask=(Mathf.Sin(u*Mathf.PI*8+Mathf.Sin(v*Mathf.PI*6)) + Mathf.Cos(v*Mathf.PI*10+Mathf.Sin(u*Mathf.PI*4)))*.25f+.5f;
                float grain=Mathf.Repeat(Mathf.Sin(x*127.1f+y*311.7f)*43758.5453f,1);
                float tone=Mathf.Lerp(.77f,.98f,Mathf.SmoothStep(.2f,.8f,mask))-(grain>.95f?.12f:0);
                pixels[y*size+x]=new Color(tone,tone,tone,1);
            }
            albedo.SetPixels(pixels);albedo.Apply(true,true);return albedo;
        }
        static Texture2D grain;
        public static Texture2D SurfaceNormal()
        {
            if(grain) return grain;
            grain=new Texture2D(128,128,TextureFormat.RGBA32,true,true){ name="Arizona fine hammered surface normal",wrapMode=TextureWrapMode.Repeat };
            var colors=new Color[128*128];
            for(int y=0;y<128;y++) for(int x=0;x<128;x++) {
                float dx=(Mathf.PerlinNoise((x+1)*.18f,y*.18f)-Mathf.PerlinNoise((x-1)*.18f,y*.18f))*.6f;
                float dy=(Mathf.PerlinNoise(x*.18f,(y+1)*.18f)-Mathf.PerlinNoise(x*.18f,(y-1)*.18f))*.6f;
                Vector3 n=new Vector3(dx,dy,1).normalized;
                // Unity's RG/AG unpack paths both receive X in red and alpha.
                colors[y*128+x]=new Color(n.x*.5f+.5f,n.y*.5f+.5f,n.z*.5f+.5f,n.x*.5f+.5f);
            }
            grain.SetPixels(colors);grain.Apply(true,false);return grain;
        }
        public static Material ScatteringMaterial(Color color)
        {
            string key="scatter/"+ColorUtility.ToHtmlStringRGBA(color);
            if(Materials.TryGetValue(key,out var found)&&found) return found;
            var shader=Shader.Find("MadeInArizona/Scattering");
            if(!shader) return Material(color);
            var mat=new Material(shader){name="White husky fur / waxy cactus scattering"};
            mat.SetColor("_BaseColor",color); Materials[key]=mat;return mat;
        }
        public static Transform Group(string name, Transform parent, Vector3 position)
        {
            Transform t = new GameObject(name).transform;
            t.SetParent(parent, false); t.localPosition = position;
            return t;
        }
        public static GameObject Shape(string name, Transform parent, PrimitiveType type, Vector3 pos, Vector3 size, Color color, bool solid = false, float metal = 0, float smooth = .25f, float glow = 0)
        {
            GameObject go = GameObject.CreatePrimitive(type); go.name = name;
            go.transform.SetParent(parent, false); go.transform.localPosition = pos; go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = Material(color, metal, smooth, glow);
            if(size.x>40 && size.z>40) { go.GetComponent<Renderer>().sharedMaterial=GroundMaterial(color,Mathf.Abs(Mathf.RoundToInt(color.r*13+color.g*7+color.b*5))%GroundTextureSet.TextureCount);var block=new MaterialPropertyBlock();block.SetVector("_BaseMap_ST",new Vector4(size.x/5,size.z/5,0,0));go.GetComponent<Renderer>().SetPropertyBlock(block); }
            Collider collider = go.GetComponent<Collider>();
            if (collider && !solid) { collider.enabled = false; if (Application.isPlaying) Object.Destroy(collider); else Object.DestroyImmediate(collider); }
            return go;
        }
        public static GameObject Box(string name, Transform parent, Vector3 pos, Vector3 size, Color color, bool solid = false, float glow = 0)
            => Shape(name, parent, PrimitiveType.Cube, pos, size, color, solid, 0, .28f, glow);
        public static GameObject Cylinder(string name, Transform parent, Vector3 pos, float radius, float height, Color color, bool solid = false)
            => Shape(name, parent, PrimitiveType.Cylinder, pos, new Vector3(radius * 2, height / 2, radius * 2), color, solid);
        public static GameObject Beam(string name, Transform parent, Vector3 a, Vector3 b, float radius, Color color, bool solid = false)
        {
            GameObject go = Cylinder(name, parent, (a+b)/2, radius, Vector3.Distance(a,b), color, solid);
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, b-a); return go;
        }
        public static GameObject Text(string words, Transform parent, Vector3 pos, float size, Color color, Quaternion? rotation = null)
        {
            Transform t = Group("Lettering_" + words.Replace('\n', '_'), parent, pos);
            t.localRotation = rotation ?? Quaternion.identity;
            TextMesh label = t.gameObject.AddComponent<TextMesh>(); label.text = words; label.fontSize = 80;
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.GetComponent<MeshRenderer>().sharedMaterial = label.font.material;
            label.characterSize = size / 8f; label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center; label.color = color;
            return t.gameObject;
        }
        public static Light Lamp(string name, Transform parent, Vector3 position, Color color, float intensity, float range)
        {
            Light light = Group(name, parent, position).gameObject.AddComponent<Light>();
            light.type = LightType.Point; light.color = color; light.intensity = intensity; light.range = range;
            light.shadows = LightShadows.None; return light;
        }
        public static GameObject MeshObject(string name, Transform parent, Vector3[] vertices, int[] triangles, Color color)
        {
            GameObject go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(parent, false);
            Mesh mesh = new Mesh { name = name + "_OriginalMesh", vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            go.GetComponent<MeshFilter>().sharedMesh = mesh; go.GetComponent<MeshRenderer>().sharedMaterial = Material(color);
            if (Application.isPlaying) go.AddComponent<GeneratedMeshOwner>().Mesh = mesh;
            return go;
        }
        public static GameObject Wedge(string name, Transform parent, Vector3 pos, Vector3 size, Color color, bool solid = false)
        {
            Vector3[] v = { new Vector3(-.5f,0,-.5f),new Vector3(.5f,0,-.5f),new Vector3(-.5f,0,.5f),new Vector3(.5f,0,.5f),new Vector3(-.5f,1,.5f),new Vector3(.5f,1,.5f) };
            int[] tris = {0,4,2,0,1,5,0,5,4,1,3,5,2,4,5,2,5,3,0,2,3,0,3,1};
            var go = MeshObject(name,parent,v,tris,color); go.transform.localPosition=pos; go.transform.localScale=size;
            if (solid) { var c=go.AddComponent<MeshCollider>(); c.sharedMesh=go.GetComponent<MeshFilter>().sharedMesh; }
            return go;
        }
        public static void MakeBreakable(Transform root, float health = 40, bool explosive = false, ExplosionKind kind = ExplosionKind.Gasoline, int score = 20)
        {
            var d = root.gameObject.AddComponent<DestructionSystem>(); d.Configure(health,kind,explosive,score);
        }
        public static void BoundsCollider(Transform t, Vector3 center, Vector3 size)
        {
            BoxCollider collider=t.gameObject.AddComponent<BoxCollider>(); collider.center=center; collider.size=size;
        }
    }
}
