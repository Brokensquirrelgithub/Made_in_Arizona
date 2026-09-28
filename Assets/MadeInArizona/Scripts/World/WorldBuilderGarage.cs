using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using static MadeInArizona.WorldArt;

namespace MadeInArizona
{
    /// <summary>
    /// The garage's surroundings, built with the open world's materials: BiomeTerrain ground (several height-blended
    /// ground textures, masked by noise), feathered TrailBlend soil for the driveway and forecourt, streamed-style
    /// desert plants and rocks, and a textured, stained concrete shop floor.
    /// </summary>
    public partial class WorldBuilder
    {
        // Shop footprint (the foundation spans x -13.5..13.5, z -12..14); the ground stays flat just outside it.
        const float ShopHalfX = 14, ShopFrontZ = -12.5f, ShopBackZ = 14.5f;
        static readonly Color GarageSoil = new Color(.56f, .43f, .30f);
        static Texture2D stainTexture;

        /// <summary>Terrain height around the garage: level beside the shop, rolling gently further out.</summary>
        static float GarageGroundHeight(float x, float z)
        {
            float dx = Mathf.Max(0, Mathf.Abs(x) - ShopHalfX), dz = Mathf.Max(0, Mathf.Max(ShopFrontZ - z, z - ShopBackZ));
            float outside = Mathf.Sqrt(dx * dx + dz * dz);
            float rise = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(5, 30, outside));
            float swell = (Mathf.PerlinNoise(x * .03f + 11, z * .03f - 7) - .45f) * 4f + (Mathf.PerlinNoise(x * .1f - 3, z * .1f + 5) - .5f) * .7f;
            return -.125f + rise * swell;
        }

        void BuildGarageGrounds(Transform shop)
        {
            var root = Group("Desert around the shop", transform, Vector3.zero);
            BuildGarageTerrain(root);
            BuildGarageSoil(root);
            BuildGarageScenery(root);
            DressShopFloor(shop);
        }

        void BuildGarageTerrain(Transform root)
        {
            const float minX = -90, maxX = 90, minZ = -80, maxZ = 100, step = 2;
            int cols = Mathf.RoundToInt((maxX - minX) / step) + 1, rows = Mathf.RoundToInt((maxZ - minZ) / step) + 1;
            var vertices = new Vector3[cols * rows]; var colors = new Color[vertices.Length]; var uv = new Vector2[vertices.Length];
            for (int r = 0; r < rows; r++) for (int c = 0; c < cols; c++)
            {
                float x = minX + c * step, z = minZ + r * step; int k = r * cols + c;
                vertices[k] = new Vector3(x, GarageGroundHeight(x, z), z); uv[k] = new Vector2(x, z) / 12;
                // Sonoran lowland palette (as the southern biome in the open world), patched by broad noise.
                float patch = Mathf.PerlinNoise(x * .025f + 40, z * .025f - 12), fine = Mathf.PerlinNoise(x * .09f - 8, z * .09f + 21);
                Color c0 = Color.Lerp(new Color(.76f, .62f, .40f), new Color(.64f, .35f, .18f), Mathf.SmoothStep(0, 1, patch));
                colors[k] = Color.Lerp(c0, new Color(.70f, .55f, .36f), fine * .35f); colors[k].a = 1;
            }
            var triangles = new int[(rows - 1) * (cols - 1) * 6]; int t = 0;
            for (int r = 0; r < rows - 1; r++) for (int c = 0; c < cols - 1; c++)
            { int a = r * cols + c, b = a + 1, d = a + cols, e = d + 1; triangles[t++] = a; triangles[t++] = d; triangles[t++] = b; triangles[t++] = b; triangles[t++] = d; triangles[t++] = e; }
            var mesh = new Mesh { name = "Garage desert terrain", indexFormat = IndexFormat.UInt32, vertices = vertices, colors = colors, uv = uv, triangles = triangles };
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
            var go = new GameObject("Garage desert terrain", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider)); go.transform.SetParent(root, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh; go.GetComponent<MeshCollider>().sharedMesh = mesh;
            var material = new Material(Shader.Find("MadeInArizona/BiomeTerrain")) { name = "Garage biome terrain" };
            ConfigureBiomeTerrain(material);
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            go.AddComponent<GeneratedMeshOwner>().Mesh = mesh;
            // No elevation levels here: switch off the altitude tint a previous open world may have left enabled.
            Shader.SetGlobalVector("_ElevationRange", new Vector4(0, 1, 0, 0));
        }

        /// <summary>Feathered soil: the forecourt, the driveway out to the desert, and tyre tracks dragged into the shop.</summary>
        void BuildGarageSoil(Transform root)
        {
            var blend = Shader.Find("MadeInArizona/TrailBlend");
            if (!blend) return;
            var drive = GeneratedWorld.TrailMaterial(blend, GarageSoil, 7, .8f, 0, .35f);
            var forecourt = GeneratedWorld.TrailMaterial(blend, new Color(.60f, .47f, .33f), 6, 0, 0, .2f);
            var tracks = GeneratedWorld.TrailMaterial(blend, new Color(.40f, .31f, .22f), 7, 0, 0, 0);
            SoilRibbon("Graded driveway", Curve(new Vector2(0, -11), new Vector2(0, -30), new Vector2(-10, -55), new Vector2(-30, -85)), 3.6f, 1.2f, drive, .05f, root, true);
            SoilRibbon("Packed forecourt", Curve(new Vector2(-2, -12), new Vector2(0, -16), new Vector2(2, -21)), 10, 1.6f, forecourt, .04f, root, true);
            SoilRibbon("Dirt along the side wall", Curve(new Vector2(16, -8), new Vector2(17, 4), new Vector2(15.5f, 16)), 1.8f, 1, forecourt, .04f, root, true);
            // Dusty tyre tracks from the door to the lift ramps, fading on the concrete.
            for (int side = -1; side <= 1; side += 2)
                SoilRibbon("Tracked-in dust", Curve(new Vector2(side * .87f, -13), new Vector2(side * .87f, -9), new Vector2(side * .87f, -5.4f)), .22f, .35f, tracks, .045f, root, false);
        }

        static Vector2[] Curve(params Vector2[] points)
        {
            var result = new List<Vector2>();
            for (int i = 0; i < points.Length - 1; i++)
            {
                Vector2 p0 = points[Mathf.Max(0, i - 1)], p1 = points[i], p2 = points[i + 1], p3 = points[Mathf.Min(points.Length - 1, i + 2)];
                int steps = Mathf.Max(2, Mathf.CeilToInt(Vector2.Distance(p1, p2) / 1.2f));
                for (int s = 0; s < steps; s++)
                {
                    float t = s / (float)steps, t2 = t * t, t3 = t2 * t;
                    result.Add(.5f * (2 * p1 + (p2 - p0) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (3 * p1 - p0 - 3 * p2 + p3) * t3));
                }
            }
            result.Add(points[points.Length - 1]);
            return result.ToArray();
        }

        /// <summary>A TrailBlend ribbon on the garage ground (or on the shop floor when <paramref name="outdoors"/> is false).</summary>
        static void SoilRibbon(string name, Vector2[] points, float halfWidth, float feather, Material material, float lift, Transform parent, bool outdoors)
        {
            float outer = halfWidth + feather * 2.9f + .6f;
            int rows = points.Length, columns = Mathf.Clamp(Mathf.CeilToInt(outer * 2 / 1.2f) + 1, 3, 48);
            float length = 0; for (int i = 1; i < rows; i++) length += Vector2.Distance(points[i - 1], points[i]);
            var grid = new Vector3[rows, columns]; var uv = new Vector4[rows * columns]; var shape = new Vector2[rows * columns];
            float along = 0;
            for (int i = 0; i < rows; i++)
            {
                if (i > 0) along += Vector2.Distance(points[i - 1], points[i]);
                Vector2 tangent = (points[Mathf.Min(rows - 1, i + 1)] - points[Mathf.Max(0, i - 1)]).normalized, normal = new Vector2(-tangent.y, tangent.x);
                for (int j = 0; j < columns; j++)
                {
                    float offset = Mathf.Lerp(outer, -outer, j / (float)(columns - 1));
                    Vector2 p = points[i] + normal * offset;
                    float ground = outdoors && !InsideShop(p) ? GarageGroundHeight(p.x, p.y) : .03f;
                    grid[i, j] = new Vector3(p.x, ground + lift, p.y);
                    uv[i * columns + j] = new Vector4(offset, along, halfWidth, length); shape[i * columns + j] = new Vector2(outer, feather);
                }
            }
            GeneratedWorld.TrailSurface(name, grid, uv, shape, material, parent);
        }

        static bool InsideShop(Vector2 p) => Mathf.Abs(p.x) < 13.5f && p.y > -12 && p.y < 14;

        /// <summary>Desert plants, rocks and cacti around the shop, from the same models and shader as the open world.</summary>
        void BuildGarageScenery(Transform root)
        {
            var nature = PandazoleNatureCatalog.Load();
            var shader = Shader.Find("MadeInArizona/LivingScenery");
            if (!shader) return;
            var material = new Material(shader) { name = "Garage scenery", enableInstancing = true };
            if (nature && nature.atlas) material.SetTexture("_NeedleAtlas", nature.atlas);
            Shader.SetGlobalVector("_SceneryVehicle", new Vector4(0, -1000, 0, 0));
            var mesh = new SceneryMesh(); var rng = new System.Random(1170);
            var driveway = Curve(new Vector2(0, -11), new Vector2(0, -30), new Vector2(-10, -55), new Vector2(-30, -85));
            for (int i = 0; i < 1400; i++)
            {
                float angle = (float)rng.NextDouble() * Mathf.PI * 2, radius = Mathf.Lerp(18, 85, Mathf.Pow((float)rng.NextDouble(), .8f));
                var p2 = new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius + 2);
                if (InsideShop(p2) || Mathf.Abs(p2.x) < ShopHalfX + 3 && p2.y > ShopFrontZ - 3 && p2.y < ShopBackZ + 3) continue;
                if (DistanceToLine(p2, driveway) < 6.5f) continue;
                // Clumped like the open world: plants gather where broad noise favours them.
                float patch = Mathf.PerlinNoise(p2.x * .045f + 3, p2.y * .045f - 9);
                var p = new Vector3(p2.x, GarageGroundHeight(p2.x, p2.y), p2.y);
                float scale = Mathf.Lerp(.65f, 1.35f, (float)rng.NextDouble());
                if (i % 23 == 0) { float s = Mathf.Lerp(.25f, .9f, (float)rng.NextDouble()); Color c = Color.Lerp(new Color(.43f, .29f, .20f), new Color(.68f, .48f, .31f), (float)rng.NextDouble()); if (!mesh.Nature(nature ? nature.Pick(nature.rocks, rng) : null, p, s, rng, c)) mesh.Rock(p, new Vector3(s, s * .6f, s * .8f), c, rng); }
                else if (i % 17 == 0) { float h = Mathf.Lerp(1.8f, 4.2f, (float)rng.NextDouble()); if (!mesh.Nature(nature ? nature.Pick(nature.cacti, rng) : null, p, h, rng, Color.white)) mesh.Cactus(p, h, rng); }
                else if (i % 5 == 0) { float h = Mathf.Lerp(.5f, 1.3f, (float)rng.NextDouble()); var c = new Color(.37f, .41f, .19f); if (!mesh.Nature(nature ? nature.Pick(nature.bushes, rng) : null, p, h, rng, c)) mesh.Bush(p, h, c, rng); }
                else if (patch > .45f) { var c = new Color(.57f, .48f, .23f); if (!mesh.Nature(nature ? nature.Pick(nature.grasses, rng) : null, p, .42f * scale, rng, c)) mesh.Grass(p, .42f * scale, c, rng, i % 13 == 0); }
            }
            var scenery = new GameObject("Garage desert scenery"); scenery.transform.SetParent(root, false);
            mesh.Build(scenery, "Batched garage scenery", material);
        }

        static float DistanceToLine(Vector2 p, Vector2[] line)
        {
            float best = float.MaxValue;
            for (int i = 1; i < line.Length; i++)
            {
                Vector2 a = line[i - 1], ab = line[i] - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, .0001f));
                best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
            }
            return best;
        }

        /// <summary>Textured shop concrete with oil stains under the lift and by the bench.</summary>
        void DressShopFloor(Transform shop)
        {
            var floor = shop.Find("Concrete floor");
            if (floor)
            {
                var renderer = floor.GetComponent<Renderer>();
                renderer.sharedMaterial = WallMaterial(new Color(.62f, .62f, .58f), 1);
                var block = new MaterialPropertyBlock(); block.SetVector("_BaseMap_ST", new Vector4(24 / 4f, 23 / 4f, 0, 0)); renderer.SetPropertyBlock(block);
            }
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Universal Render Pipeline/Unlit");
            if (!shader) return;
            var stain = new Material(shader) { name = "Shop oil stains", renderQueue = 2950 };
            stain.SetTexture("_BaseMap", StainTexture()); stain.SetTexture("_MainTex", StainTexture());
            stain.SetFloat("_Surface", 1); stain.SetFloat("_Blend", 0);
            stain.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); stain.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            stain.SetFloat("_ZWrite", 0); stain.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            stain.SetColor("_BaseColor", new Color(.05f, .045f, .04f, .55f));
            var spots = new[] { new Vector4(0, -.4f, 2.6f, 20), new Vector4(.5f, 1.4f, 1.3f, 110), new Vector4(-.8f, -2.2f, 1f, 200), new Vector4(-7.2f, 7.8f, 1.8f, 60), new Vector4(6.8f, 1.2f, 1.5f, 300), new Vector4(-3.5f, -7.5f, 1.2f, 140) };
            foreach (var s in spots)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad); quad.name = "Oil stain";
                Destroy(quad.GetComponent<Collider>());
                quad.transform.SetParent(shop, false);
                quad.transform.localPosition = new Vector3(s.x, .037f, s.y); quad.transform.localRotation = Quaternion.Euler(90, s.w, 0);
                quad.transform.localScale = new Vector3(s.z, s.z * .8f, 1);
                var r = quad.GetComponent<Renderer>(); r.sharedMaterial = stain; r.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        static Texture2D StainTexture()
        {
            if (stainTexture) return stainTexture;
            const int size = 128;
            stainTexture = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "Irregular oil stain", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                float u = x / (size - 1f) * 2 - 1, v = y / (size - 1f) * 2 - 1, r = Mathf.Sqrt(u * u + v * v);
                float edge = .72f + (Mathf.PerlinNoise(u * 2.5f + 5, v * 2.5f + 9) - .5f) * .45f;
                float alpha = Mathf.Clamp01((edge - r) / .25f) * Mathf.Lerp(.55f, 1, Mathf.PerlinNoise(u * 6 + 1, v * 6 - 4));
                pixels[y * size + x] = new Color(1, 1, 1, alpha);
            }
            stainTexture.SetPixels(pixels); stainTexture.Apply(true, true);
            return stainTexture;
        }
    }
}
