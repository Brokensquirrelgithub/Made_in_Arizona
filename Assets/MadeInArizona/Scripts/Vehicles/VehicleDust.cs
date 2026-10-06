using System.Collections.Generic;
using UnityEngine;

namespace MadeInArizona
{
    /// <summary>
    /// Dust that builds up on a car as it drives (Shaders/CarDust.hlsl). Loose ground coats it fastest, sand and mud
    /// most of all; pavement barely adds any; fording water rinses some off. At the default rate a car driven off-road
    /// for about ten minutes is filthy, and it looks it: tan lower panels, caked wheel wells, dusty bumpers and a dirty
    /// rear. The colour follows the ground it picked up: pale sand, brown dirt, red rock.
    /// The car gets its own copies of its materials, which carry its body transform and dust state; parts that used
    /// plain URP Lit (bumpers, frame, tyres, trim) switch to the Reflective shader so they can take dust too. The
    /// player's dust per vehicle lasts the session, garage visits included; hostile cars arrive already dusty.
    /// </summary>
    public sealed class VehicleDust : MonoBehaviour
    {
        /// <summary>Build-up per second at exposure 1: dust reaches 90% after ten minutes (ln 10 / 600).</summary>
        const float BuildUp = .00384f;
        static readonly Dictionary<string, Vector4> PlayerDust = new Dictionary<string, Vector4>();
        static Texture2D noise;
        VehicleController vehicle;
        Transform body;
        string vehicleId;
        readonly List<Material> materials = new List<Material>();
        float amount, applied = -1;
        Color tint = new Color(.7f, .58f, .44f);
        Vector4 shape;

        public float Amount => amount;
        /// <summary>Review captures only: sets the dust level outright.</summary>
        internal void SoilForReview(float value) { amount = Mathf.Clamp01(value); tint = DustColor(SurfaceKind.Sand); }
        /// <summary>Washes some dust off (Suzuki's hose in the garage). The player's dust per car is remembered.</summary>
        public void Rinse(float rinsed)
        {
            amount = Mathf.Max(0, amount - Mathf.Max(0, rinsed));
            if (vehicle && vehicle.IsPlayer) PlayerDust[vehicleId] = new Vector4(amount, tint.r, tint.g, tint.b);
        }

        public void Bind(VehicleController owner, VehicleDefinition definition)
        {
            vehicle = owner; body = owner.Visual; vehicleId = definition != null ? definition.id : "car";
            Release();
            if (!body) return;
            Shader.SetGlobalTexture("_GrimeNoise", Noise);
            var copies = new Dictionary<Material, Material>();
            var bounds = new Bounds(Vector3.zero, Vector3.zero);
            Matrix4x4 toBody = body.worldToLocalMatrix;
            foreach (var renderer in body.GetComponentsInChildren<MeshRenderer>(true))
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter && filter.sharedMesh) Encapsulate(ref bounds, filter.sharedMesh.bounds, toBody * renderer.transform.localToWorldMatrix);
                var source = Dustable(renderer.sharedMaterial);
                if (!source) continue;
                if (!copies.TryGetValue(source, out Material copy))
                {
                    copy = new Material(source) { name = source.name + " (dusty)" };
                    copies[source] = copy; materials.Add(copy);
                }
                renderer.sharedMaterial = copy;
            }
            // Wheel centre and radius from the front-left wheel; axle lines from the front and rear wheels.
            Bounds wheel = SubtreeBounds(owner.Wheel(0), toBody);
            float frontAxle = owner.Wheel(0) ? toBody.MultiplyPoint3x4(owner.Wheel(0).position).z : bounds.max.z * .6f;
            float rearAxle = owner.Wheel(2) ? toBody.MultiplyPoint3x4(owner.Wheel(2).position).z : bounds.min.z * .6f;
            shape = new Vector4(wheel.size.sqrMagnitude > 0 ? wheel.center.y : .4f, wheel.size.sqrMagnitude > 0 ? wheel.extents.y : .42f, bounds.max.y, 0);
            foreach (var material in materials) material.SetVector("_DustAxles", new Vector4(frontAxle, rearAxle, bounds.max.z, bounds.min.z));
            if (owner.IsPlayer && PlayerDust.TryGetValue(vehicleId, out Vector4 saved))
            {
                amount = saved.x; tint = new Color(saved.y, saved.z, saved.w);
            }
            else if (!owner.IsPlayer)
            {
                // Hostile crews live out here: they turn up already dusty, in the colour of the local ground.
                amount = Random.Range(.3f, .85f);
                var ground = WorldBuilder.SurfaceAt(owner.transform.position);
                tint = DustColor(ground == SurfaceKind.Asphalt || ground == SurfaceKind.Water ? SurfaceKind.Dirt : ground);
            }
            else amount = 0;
            applied = -1;
            Apply();
        }

        /// <summary>The material to copy for a car part, or null to leave the part alone (lights, glowing trim).</summary>
        static Material Dustable(Material material)
        {
            if (!material) return null;
            string shader = material.shader ? material.shader.name : "";
            if (shader == "MadeInArizona/CarPaint" || shader == "MadeInArizona/Reflective") return material;
            if (shader != "Universal Render Pipeline/Lit" || material.IsKeywordEnabled("_EMISSION")) return null;
            float metallic = material.HasProperty("_Metallic") ? material.GetFloat("_Metallic") : 0;
            float smoothness = material.HasProperty("_Smoothness") ? material.GetFloat("_Smoothness") : .25f;
            Color color = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : material.color;
            var reflective = WorldArt.Reflective(color, metallic > .3f ? SunGlint.Metal : SunGlint.Plastic, smoothness, metallic, 1.2f, .7f);
            return reflective && reflective.shader && reflective.shader.name == "MadeInArizona/Reflective" ? reflective : null;
        }

        void Update()
        {
            if (!vehicle || vehicle.Damage == null || vehicle.Damage.IsDead) return;
            var game = GameManager.Instance;
            if (game && game.IsPlaying && vehicle.Body && !vehicle.Body.isKinematic && (vehicle.Grounded || vehicle.Beached)) Accumulate(Time.deltaTime);
            if (Mathf.Abs(amount - applied) > .003f) Apply();
            if (vehicle.IsPlayer) PlayerDust[vehicleId] = new Vector4(amount, tint.r, tint.g, tint.b);
        }

        void Accumulate(float dt)
        {
            SurfaceKind surface = vehicle.Surface;
            float speed = vehicle.SpeedKph / 3.6f;
            if (surface == SurfaceKind.Water)
            {
                // Fording rinses some of it off.
                amount = Mathf.MoveTowards(amount, amount * .5f, dt * .04f * Mathf.Clamp01(speed / 4));
                return;
            }
            float exposure;
            switch (surface)
            {
                case SurfaceKind.Sand: exposure = 1.4f; break;
                case SurfaceKind.Mud: exposure = 1.6f; break;
                case SurfaceKind.Dirt: exposure = 1.15f; break;
                case SurfaceKind.Gravel: exposure = 1; break;
                case SurfaceKind.Rocks: exposure = .75f; break;
                case SurfaceKind.Debris: exposure = .7f; break;
                default: exposure = .12f; break; // pavement: road dust, slowly
            }
            // Faster driving, slides and wheelspin kick up more of it.
            float motion = Mathf.Clamp(speed / 14f, 0, 1.8f) * (1 + vehicle.DriftAmount * .8f + vehicle.WheelSpin * .5f);
            float rate = BuildUp * exposure * motion * Mathf.Max(0, DevTuning.Current.carDust);
            float gained = (1 - amount) * (1 - Mathf.Exp(-rate * dt));
            if (gained <= 0) return;
            amount += gained;
            tint = Color.Lerp(tint, DustColor(surface), gained / Mathf.Max(amount, .02f));
        }

        void Apply()
        {
            applied = amount;
            var state = new Vector4(amount, shape.x, shape.y, shape.z);
            Vector4 color = tint.linear;
            foreach (var material in materials)
                if (material) { material.SetVector("_DustState", state); material.SetVector("_DustTint", color); }
        }

        void LateUpdate()
        {
            // The body's pose after this frame's movement and suspension animation, so the dust sticks to the panels.
            if (!body || materials.Count == 0 || amount <= .001f) return;
            Matrix4x4 toBody = body.worldToLocalMatrix;
            Vector4 row0 = toBody.GetRow(0), row1 = toBody.GetRow(1), row2 = toBody.GetRow(2);
            foreach (var material in materials)
                if (material) { material.SetVector("_DustRow0", row0); material.SetVector("_DustRow1", row1); material.SetVector("_DustRow2", row2); }
        }

        static Color DustColor(SurfaceKind surface)
        {
            switch (surface)
            {
                case SurfaceKind.Sand: return new Color(.8f, .7f, .55f);
                case SurfaceKind.Gravel: return new Color(.66f, .62f, .56f);
                case SurfaceKind.Rocks: return new Color(.68f, .5f, .38f);
                case SurfaceKind.Mud: return new Color(.4f, .31f, .22f);
                case SurfaceKind.Debris: return new Color(.6f, .56f, .5f);
                case SurfaceKind.Asphalt: case SurfaceKind.Oil: return new Color(.58f, .56f, .52f);
                default: return new Color(.7f, .58f, .44f);
            }
        }

        static void Encapsulate(ref Bounds bounds, Bounds local, Matrix4x4 matrix)
        {
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = local.center + Vector3.Scale(local.extents, new Vector3(i % 2 == 0 ? -1 : 1, (i / 2) % 2 == 0 ? -1 : 1, i < 4 ? -1 : 1));
                Vector3 p = matrix.MultiplyPoint3x4(corner);
                if (bounds.size == Vector3.zero && bounds.center == Vector3.zero) bounds = new Bounds(p, Vector3.zero);
                else bounds.Encapsulate(p);
            }
        }
        static Bounds SubtreeBounds(Transform root, Matrix4x4 toBody)
        {
            var bounds = new Bounds(Vector3.zero, Vector3.zero);
            if (!root) return bounds;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh) Encapsulate(ref bounds, filter.sharedMesh.bounds, toBody * filter.transform.localToWorldMatrix);
            return bounds;
        }

        /// <summary>
        /// Tileable 128² noise for the dust mask: R large patches, G mid detail, B long horizontal streaks (dust thrown
        /// back along the body), A fine speckle.
        /// </summary>
        static Texture2D Noise
        {
            get
            {
                if (noise) return noise;
                const int size = 128;
                var pixels = new Color[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float u = x / (float)size, v = y / (float)size;
                        float streak = 0, weight = 0, a = .5f;
                        for (int o = 0; o < 3; o++)
                        {
                            int px = 2 << o, py = 16 << o;
                            streak += Stretched(u * px, v * py, px, py, 91 + o) * a; weight += a; a *= .5f;
                        }
                        float speck = SunGlint.Hash(x, y, 7);
                        pixels[y * size + x] = new Color(SunGlint.Fbm(u, v, 3, 4, 17), SunGlint.Fbm(u, v, 8, 3, 23), streak / weight, speck);
                    }
                noise = new Texture2D(size, size, TextureFormat.RGBA32, true, true) { name = "Procedural grime noise", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear };
                noise.SetPixels(pixels); noise.Apply(true, true);
                return noise;
            }
        }
        /// <summary>Value noise with separate horizontal and vertical periods, for streaks that still tile.</summary>
        static float Stretched(float x, float y, int periodX, int periodY, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            int ax = ((x0 % periodX) + periodX) % periodX, ay = ((y0 % periodY) + periodY) % periodY;
            int bx = (ax + 1) % periodX, by = (ay + 1) % periodY;
            return Mathf.Lerp(Mathf.Lerp(SunGlint.Hash(ax, ay, seed), SunGlint.Hash(bx, ay, seed), fx), Mathf.Lerp(SunGlint.Hash(ax, by, seed), SunGlint.Hash(bx, by, seed), fx), fy);
        }

        void Release()
        {
            foreach (var material in materials) if (material) Destroy(material);
            materials.Clear();
        }
        void OnDestroy() { Release(); }
    }
}
