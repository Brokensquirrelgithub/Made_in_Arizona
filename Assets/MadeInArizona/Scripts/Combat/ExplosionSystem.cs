using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MadeInArizona
{
    public enum ExplosionKind { Gasoline, Propane, Ammunition, Electrical, Grenade, Rocket, FuelTank, Vehicle, Massive }

    /// <summary>World-scoped effect pools with a bounded chain-reaction queue and quality-dependent budgets.</summary>
    public sealed class ExplosionSystem : MonoBehaviour
    {
        struct Blast { public Vector3 point; public float radius, damage; public GameObject source; public ExplosionKind kind; }
        sealed class Fragment { public GameObject view; public Transform t; public Rigidbody body; public Collider collider; public Renderer renderer; public float born, until; public Vector3 scale; }
        sealed class Flash { public Light light; public float start, until, power; }
        sealed class Wave { public LineRenderer line; public float start, until, radius; public Color color; }
        sealed class Burn { public Vector3 point;public Color flame;public float until,radius,next;public Light light; }
        readonly List<Burn> burns=new List<Burn>();
        readonly List<Light> burnLights=new List<Light>();
        sealed class Scorch { public Transform t; public Renderer renderer; public float born, fadeAt, until; public Color color; public Vector3 scale; }
        static ExplosionSystem instance;
        readonly Queue<Blast> queued = new Queue<Blast>();
        readonly Collider[] overlaps = new Collider[384];
        readonly HashSet<VehicleDamage> damagedVehicles = new HashSet<VehicleDamage>();
        readonly HashSet<DestructionSystem> damagedProps = new HashSet<DestructionSystem>();
        readonly HashSet<Rigidbody> pushed = new HashSet<Rigidbody>();
        readonly HashSet<BossWeakPoint> damagedPoints = new HashSet<BossWeakPoint>();
        readonly List<Fragment> fragments = new List<Fragment>();
        readonly Stack<Fragment> fragmentPool = new Stack<Fragment>();
        readonly List<Flash> flashes = new List<Flash>();
        readonly List<Wave> waves = new List<Wave>();
        readonly List<Scorch> scorches = new List<Scorch>();
        ParticleSystem fire, smoke, sparks, tireSmoke;
        Material smokeMaterial,fireMaterial,particleMaterial, debrisMaterial, waveMaterial, scorchMaterial, tireSmokeMaterial;
        Texture2D softTexture, scorchTexture;
        MaterialPropertyBlock block;
        int quality;
        int DebrisLimit => quality == 0 ? 45 : quality == 1 ? 100 : quality == 2 ? 220 : 640;
        public int ActiveDebris => fragments.Count;
        public int QueuedExplosions => queued.Count;

        public static void IgnoreVehicleCollisions(VehicleController vehicle)
        {
            if (instance == null || vehicle == null) return;
            var colliders = vehicle.GetComponentsInChildren<Collider>();
            foreach (var fragment in instance.fragments)
                foreach (var collider in colliders)
                    if (collider.enabled) Physics.IgnoreCollision(fragment.collider, collider, true);
        }

        static ExplosionSystem Get()
        {
            if (instance != null) return instance;
            var go = new GameObject("Pooled desert spectacle");
            if (GameManager.Instance != null && GameManager.Instance.World != null) go.transform.SetParent(GameManager.Instance.World.transform);
            instance = go.AddComponent<ExplosionSystem>();
            return instance;
        }
        void Awake()
        {
            block = new MaterialPropertyBlock();
            quality = GameManager.Instance != null && GameManager.Instance.Save != null ? GameManager.Instance.Save.settings.quality : 2;
            softTexture = new Texture2D(32, 32, TextureFormat.RGBA32, false) { name = "Procedural soft particle", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[32 * 32];
            for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
            {
                float distance = new Vector2((x - 15.5f) / 15.5f, (y - 15.5f) / 15.5f).magnitude;
                pixels[y * 32 + x] = new Color(1, 1, 1, Mathf.Pow(Mathf.Clamp01(1 - distance), 1.5f));
            }
            softTexture.SetPixels(pixels); softTexture.Apply(false, true);
            scorchTexture = new Texture2D(128, 128, TextureFormat.RGBA32, true) { name = "Irregular explosion soot", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            var soot = new Color[128 * 128];
            for (int y = 0; y < 128; y++) for (int x = 0; x < 128; x++)
            {
                Vector2 uv = new Vector2((x - 63.5f) / 63.5f, (y - 63.5f) / 63.5f);
                float angle = Mathf.Atan2(uv.y, uv.x);
                float distance = uv.magnitude;
                float coarse = Mathf.PerlinNoise(x * .057f + 11.7f, y * .057f + 4.3f);
                float fine = Mathf.PerlinNoise(x * .19f + 37.1f, y * .19f + 19.8f);
                float raggedEdge = .78f + (coarse - .5f) * .25f + Mathf.Sin(angle * 7 + coarse * 5) * .035f;
                float core = 1 - Mathf.SmoothStep(.05f, raggedEdge, distance);
                float blastRing = Mathf.Exp(-Mathf.Pow((distance - .45f - (coarse - .5f) * .08f) * 8.5f, 2));
                float alpha = Mathf.Clamp01((core * (.46f + coarse * .40f) + blastRing * .28f) * Mathf.Lerp(.72f, 1.12f, fine));
                soot[y * 128 + x] = new Color(.20f + coarse * .08f, .12f + coarse * .035f, .065f, alpha);
            }
            scorchTexture.SetPixels(soot); scorchTexture.Apply(true, true);
            var particleShader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Universal Render Pipeline/Unlit");
            particleMaterial = new Material(particleShader) { name = "Soft desert particles", renderQueue = 3000, enableInstancing = true };
            particleMaterial.SetTexture("_BaseMap", softTexture); particleMaterial.SetTexture("_MainTex", softTexture);
            particleMaterial.SetFloat("_Surface", 1); particleMaterial.SetFloat("_Blend", 0);
            particleMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); particleMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            particleMaterial.SetFloat("_ZWrite", 0); particleMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            particleMaterial.SetColor("_BaseColor", Color.white);
            var unlit = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            waveMaterial = new Material(unlit) { name = "Pressure wave", enableInstancing = true };
            waveMaterial.SetColor("_BaseColor", new Color(2.3f, 1.2f, .3f));
            // Tumbling scraps flash in the sun: reflective metal that the wear map dulls in patches.
            var reflective = Shader.Find("MadeInArizona/Reflective");
            if (reflective && reflective.isSupported)
            {
                debrisMaterial = new Material(reflective) { name = "Fractured steel and adobe" };
                debrisMaterial.SetFloat("_GlintType", SunGlint.Metal); debrisMaterial.SetFloat("_Metallic", .7f);
                debrisMaterial.SetFloat("_Smoothness", .72f); debrisMaterial.SetFloat("_Wear", 1.3f);
            }
            else
            {
                debrisMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) { name = "Fractured steel and adobe", enableInstancing = true };
                debrisMaterial.SetFloat("_Smoothness", .22f);
            }
            scorchMaterial = new Material(particleMaterial) { name = "Explosion scorch" };
            scorchMaterial.SetTexture("_BaseMap", scorchTexture); scorchMaterial.SetTexture("_MainTex", scorchTexture);
            scorchMaterial.SetColor("_BaseColor", new Color(.12f, .07f, .045f, .68f));
            smokeMaterial=SixWayMaterial("Smoke",0,2.4f);
            // HDR flame values deliberately clear the post-process bloom threshold during a blast.
            fireMaterial=SixWayMaterial("Fireball",20,3.8f);
            fire = Bank("Six-way fluid fireballs", 100 + quality * 100, 1.15f, false);
            smoke = Bank("Six-way rolling smoke", 200 + quality * 200, 1.6f, true);
            sparks = Bank("Incandescent fragments", 2000 + quality * 2200, .15f, false);
            // Tyre smoke has its own budget, so a long burnout never starves explosions of smoke.
            tireSmokeMaterial = SixWayMaterial("Smoke", 0, 1.35f); tireSmokeMaterial.name = "Tyre smoke • baked six-direction fluid lighting";
            tireSmokeMaterial.SetFloat("_Bright", .62f);
            tireSmoke = Bank("Six-way tyre smoke", 160 + quality * 150, 3f, true);
            tireSmoke.GetComponent<ParticleSystemRenderer>().sharedMaterial = tireSmokeMaterial;
        }
        Material SixWayMaterial(string name,float emission,float density)
        {
            var material=new Material(Shader.Find("MadeInArizona/SixWaySmoke")){name=name+" • baked six-direction fluid lighting"};
            material.SetTexture("_Positive",Resources.Load<Texture2D>("SixWay/"+name+"_P"));
            material.SetTexture("_Negative",Resources.Load<Texture2D>("SixWay/"+name+"_N"));
            material.SetFloat("_Emission",emission);material.SetFloat("_Density",density);return material;
        }
        ParticleSystem Bank(string name, int budget, float growth, bool noisy)
        {
            var go = new GameObject(name); go.transform.SetParent(transform);
            var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.loop = true; main.playOnAwake = false; main.maxParticles = budget;
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.startSpeed = 0;
            main.gravityModifier = noisy ? -.015f : .12f;
            var emission = ps.emission; emission.enabled = false;
            var shape = ps.shape; shape.enabled = false;
            var size = ps.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(new Keyframe(0, .3f), new Keyframe(.25f, .8f), new Keyframe(1, growth)));
            var color = ps.colorOverLifetime; color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .08f), new GradientAlphaKey(.6f, .45f), new GradientAlphaKey(0, 1) });
            color.color = gradient;
            var noise = ps.noise; noise.enabled = noisy && quality > 0; noise.strength = .45f; noise.frequency = .4f; noise.scrollSpeed = .25f;
            var renderer = ps.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = particleMaterial;
            bool fluid=name.Contains("Six-way");
            if(fluid) {
                bool flame=name.Contains("fireballs");main.startSize3D=flame;renderer.sharedMaterial=flame?fireMaterial:smokeMaterial;
                var sheet=ps.textureSheetAnimation;sheet.enabled=true;sheet.numTilesX=flame?16:8;sheet.numTilesY=flame?4:8;
                sheet.animation=ParticleSystemAnimationType.WholeSheet;sheet.frameOverTime=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,0,1,.999f));
                sheet.cycleCount=1;sheet.startFrame=flame?new ParticleSystem.MinMaxCurve(0):new ParticleSystem.MinMaxCurve(0,1);
                renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>{ParticleSystemVertexStream.Position,ParticleSystemVertexStream.Normal,ParticleSystemVertexStream.Tangent,ParticleSystemVertexStream.Color,ParticleSystemVertexStream.UV,ParticleSystemVertexStream.UV2,ParticleSystemVertexStream.AnimBlend});
                main.gravityModifier=0;
                size.size=new ParticleSystem.MinMaxCurve(1,new AnimationCurve(new Keyframe(0,flame?.85f:.65f),new Keyframe(.35f,1),new Keyframe(1,growth)));
                gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(flame?1:0,0),new GradientAlphaKey(1,.12f),new GradientAlphaKey(.9f,.65f),new GradientAlphaKey(0,1)});color.color=gradient;
            }
            renderer.renderMode = ParticleSystemRenderMode.Billboard; renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false; renderer.sortMode = ParticleSystemSortMode.Distance;
            ps.Play(); return ps;
        }
        public static void Detonate(Vector3 position, float radius, float damage, GameObject source, ExplosionKind kind)
        {
            var system = Get();
            if (system.queued.Count < 256) system.queued.Enqueue(new Blast { point = position, radius = Mathf.Clamp(radius, 1, 36), damage = damage, source = source, kind = kind });
        }
        /// <summary>
        /// A small, immediate blast (firecracker pellets): sparks, a puff of flame and explosive damage to every car
        /// and prop in <paramref name="radius"/>, without the flash, scorch, debris and chain queue of a full detonation.
        /// </summary>
        public static void Pop(Vector3 position, float radius, float damage, GameObject source, Color color)
        {
            var system = Get();
            radius = Mathf.Clamp(radius, .5f, 4);
            Burst(position, color * 1.6f, 10, radius * 2.2f);
            Burst(position, new Color(.4f, .36f, .3f, .55f), 3, radius);
            system.Emit(system.fire, position, Vector3.up, new Color(2, 1.1f, .35f), radius * 1.1f, .45f);
            system.damagedVehicles.Clear(); system.damagedProps.Clear();
            int count = Physics.OverlapSphereNonAlloc(position, radius, system.overlaps, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var collider = system.overlaps[i]; if (collider == null) continue;
                float falloff = Mathf.Lerp(.35f, 1, 1 - Mathf.Clamp01(Vector3.Distance(collider.bounds.ClosestPoint(position), position) / radius));
                var vehicle = collider.GetComponentInParent<VehicleDamage>();
                if (vehicle != null && system.damagedVehicles.Add(vehicle))
                    vehicle.ApplyDamage(damage * falloff * (source != null && vehicle.gameObject == source ? .18f : 1), position, source, true);
                var prop = collider.GetComponentInParent<DestructionSystem>();
                if (prop != null && system.damagedProps.Add(prop)) prop.ApplyDamage(damage * falloff, position, source, BlastPush(prop, position, falloff));
            }
            if (Time.time >= system.popSoundAt) { system.popSoundAt = Time.time + .06f; AudioManager.Instance?.PlayExplosion(position, 1.5f, ExplosionKind.Ammunition); }
        }
        float popSoundAt;
        /// <summary>
        /// One puff of pale tyre smoke from a burnout or a slide on pavement. It billows up to about three times its start
        /// size and lingers; <paramref name="opacity"/> is 0-1.
        /// </summary>
        public static void TireSmoke(Vector3 position, Vector3 velocity, float size, float life, float opacity)
        {
            var system = Get();
            if (system.tireSmoke == null) return;
            var tint = new Color(.93f, .92f, .9f, Mathf.Clamp01(opacity));
            system.tireSmoke.Emit(new ParticleSystem.EmitParams { position = position, velocity = velocity, startColor = tint, startSize = size, startLifetime = life, rotation = Random.Range(-30, 30) }, 1);
        }
        public static void Burst(Vector3 position, Color color, int count, float speed)
        {
            var system = Get();
            count = Mathf.Clamp(count, 1, 80);
            bool dusty = color.a < .85f;
            if(dusty)count=Mathf.Max(1,count/3);
            for (int i = 0; i < count; i++)
            {
                Vector3 velocity = Random.insideUnitSphere * speed;
                velocity.y = Mathf.Abs(velocity.y) * .6f + .5f;
                system.Emit(dusty ? system.smoke : system.sparks, position, velocity, color, dusty ? Random.Range(.65f, 1.5f) : Random.Range(.08f, .24f), dusty ? 1.4f : .38f);
            }
        }
        void Emit(ParticleSystem bank, Vector3 position, Vector3 velocity, Color color, float size, float life)
        {
            var particle = new ParticleSystem.EmitParams { position = position, velocity = velocity, startColor = color, startSize = size, startLifetime = life, rotation = bank==sparks?Random.Range(0,360):Random.Range(-18,18) };
            if(bank==fire) { particle.startSize3D=new Vector3(size,size*2,size); particle.position=position+Vector3.up*size*.85f; }
            bank.Emit(particle, 1);
        }
        void LateUpdate()
        {
            if (GameManager.Instance != null && !GameManager.Instance.IsPlaying) return;
            for(int i=burns.Count-1;i>=0;i--) {
                var burn=burns[i];if(Time.time>burn.until){if(burn.light)burn.light.enabled=false;burns.RemoveAt(i);continue;}
                if(burn.light)burn.light.intensity=(11+Mathf.PerlinNoise(Time.time*7,burn.point.x)*16)*Mathf.Clamp01((burn.until-Time.time)*2);
                if(Time.time<burn.next)continue;burn.next=Time.time+.24f;
                for(int j=0;j<(quality==3?2:1);j++) {
                    Vector3 p=burn.point+Random.insideUnitSphere*burn.radius*.2f;p.y=Mathf.Max(.15f,p.y);
                    Emit(fire,p,Vector3.up*Random.Range(1.3f,3f),burn.flame,Random.Range(.75f,1.2f)*burn.radius*.7f,Random.Range(1.2f,2.1f));
                    Emit(smoke,p+Vector3.up*.6f,new Vector3(.4f,1.7f,.2f),new Color(.28f,.27f,.25f,.8f),burn.radius*.8f,4+quality*.5f);
                    if(quality==3)Emit(sparks,p,new Vector3(Random.Range(-1f,1f),3,Random.Range(-1f,1f)),new Color(2,1,.2f),.06f,2);
                }
            }
            int count = Mathf.Min(12, queued.Count);
            // New chain reactions wait until the next frame, making the propagation readable.
            for (int i = 0; i < count; i++) Perform(queued.Dequeue());
            var camera = Camera.main;
            for (int i = fragments.Count - 1; i >= 0; i--)
            {
                var f = fragments[i];
                if (Time.time > f.until - 1) f.t.localScale = f.scale * Mathf.Clamp01(f.until - Time.time);
                bool offscreen = false;
                if (camera != null && Time.time > f.born + .35f)
                {
                    Vector3 viewport = camera.WorldToViewportPoint(f.t.position);
                    offscreen = viewport.z < 0 || viewport.x < -.15f || viewport.x > 1.15f || viewport.y < -.15f || viewport.y > 1.15f;
                }
                if (Time.time > f.until || f.t.position.y < -4 || offscreen) RetireFragment(i);
            }
            foreach (var flash in flashes)
            {
                if (!flash.light.enabled) continue;
                float life = Mathf.InverseLerp(flash.until, flash.start, Time.time);
                flash.light.intensity = flash.power * life * life;
                if (Time.time >= flash.until) flash.light.enabled = false;
            }
            foreach (var wave in waves)
            {
                if (!wave.line.enabled) continue;
                float t = Mathf.InverseLerp(wave.start, wave.until, Time.time);
                wave.line.transform.localScale = Vector3.one * Mathf.Lerp(.08f, wave.radius * 1.5f, t);
                wave.line.widthMultiplier = Mathf.Lerp(.35f, .01f, t);
                wave.line.startColor = wave.line.endColor = wave.color * (1 - t);
                if (Time.time > wave.until) wave.line.enabled = false;
            }
            foreach (var scorch in scorches)
            {
                if (!scorch.t.gameObject.activeSelf) continue;
                if (Time.time >= scorch.until) { scorch.t.gameObject.SetActive(false); continue; }
                float fade = Time.time < scorch.fadeAt ? 1 : Mathf.InverseLerp(scorch.until, scorch.fadeAt, Time.time);
                scorch.t.localScale = scorch.scale * Mathf.Lerp(1, 1.035f, Mathf.InverseLerp(scorch.born, scorch.until, Time.time));
                block.SetColor("_BaseColor", new Color(scorch.color.r, scorch.color.g, scorch.color.b, scorch.color.a * fade));
                scorch.renderer.SetPropertyBlock(block);
            }
        }
        void Perform(Blast blast)
        {
            float radius = blast.radius;
            Color flame = new Color(1.6f, .56f, .08f, .9f);
            Color dust = new Color(.35f, .27f, .2f, .58f);
            float fireScale = 1, smokeScale = 1, sparkScale = 1, upward = 1;
            switch (blast.kind)
            {
                case ExplosionKind.Propane: flame = new Color(.35f, .75f, 2.4f, .9f); upward = 2.7f; sparkScale = .5f; break;
                case ExplosionKind.Ammunition: flame = new Color(2.4f, 1.6f, .38f); sparkScale = 3; fireScale = .55f; break;
                case ExplosionKind.Electrical: flame = new Color(.3f, 1.6f, 2.8f); sparkScale = 3.5f; smokeScale = .4f; fireScale = .6f; break;
                case ExplosionKind.Grenade: flame = new Color(1.3f, .9f, .38f); fireScale = .65f; sparkScale = 1.7f; break;
                case ExplosionKind.Rocket: sparkScale = 2; upward = .8f; break;
                case ExplosionKind.Gasoline: fireScale = 1.3f; smokeScale = 1.4f; break;
                case ExplosionKind.FuelTank: fireScale = 1.8f; smokeScale = 2; upward = 1.5f; break;
                case ExplosionKind.Vehicle: sparkScale = 1.8f; smokeScale = 1.5f; break;
                case ExplosionKind.Massive: fireScale = 2; smokeScale = 2.5f; sparkScale = 3; upward = 2; break;
            }
            int multiplier = quality + 1;
            int fireCount = Mathf.Clamp(Mathf.RoundToInt((1+quality*.5f)*fireScale),1,4);
            int smokeCount = Mathf.Clamp(Mathf.RoundToInt((quality+2)*smokeScale),3,14);
            int sparkCount = Mathf.RoundToInt((12 + radius * 5) * multiplier * sparkScale);
            for (int i = 0; i < fireCount; i++)
            {
                Vector3 velocity = Random.insideUnitSphere * radius * .32f;
                velocity.y = Mathf.Abs(velocity.y) * upward + 1;
                Emit(fire, blast.point + Random.insideUnitSphere * radius * .13f, velocity, Color.Lerp(flame, new Color(2, 1.7f, .6f), Random.value * .45f), Random.Range(.8f, 1.2f) * radius * 1.6f, Random.Range(1.6f, 2.7f));
            }
            for (int i = 0; i < smokeCount; i++)
            {
                Vector3 velocity = Random.insideUnitSphere * radius * .24f; velocity.y = Mathf.Abs(velocity.y) + .8f;
                Emit(smoke, blast.point + Random.insideUnitSphere, velocity, Color.Lerp(dust, new Color(.24f, .23f, .21f, .92f), Random.value), Random.Range(.85f, 1.5f) * radius, Random.Range(3.5f, 5.5f + quality * .5f));
            }
            for (int i = 0; i < sparkCount; i++)
            {
                Vector3 velocity = Random.onUnitSphere * radius * Random.Range(2, 4.8f); velocity.y = Mathf.Abs(velocity.y) * .65f;
                Emit(sparks, blast.point, velocity, flame * 1.5f, Random.Range(.06f, .19f), Random.Range(.25f, .9f));
            }
            AtmosphereSystem.Heat(blast.point,radius*.75f,quality==3?7:4);
            bool fuel=blast.kind==ExplosionKind.Gasoline||blast.kind==ExplosionKind.FuelTank||blast.kind==ExplosionKind.Vehicle||blast.kind==ExplosionKind.Massive;
            if(fuel && quality>0 && burns.Count<(quality==3?32:12)) burns.Add(new Burn{point=blast.point,flame=flame,radius=Mathf.Min(radius,8),until=Time.time+3+quality*2,light=BurnLight(blast.point,radius)});
            MakeFlash(blast.point + Vector3.up * 2, radius, flame);
            // Expanding dust and refraction replace the neon-ring blast outline.
            Burst(blast.point,new Color(.52f,.39f,.26f,.7f),12+quality*6,radius*.7f);
            MakeScorch(blast.point, radius * .5f);
            Scatter(blast.point, radius, 4 + multiplier * 3, new Color(.24f, .2f, .15f), Vector3.zero);
            AudioManager.Instance?.PlayExplosion(blast.point, radius,blast.kind);
            var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
            if (player != null)
            {
                float distance = Vector3.Distance(player.transform.position, blast.point);
                CameraController.Instance?.Shake(Mathf.Clamp01(radius / Mathf.Max(5, distance)) * .75f);
            }
            foreach (var fragment in fragments)
                if (!fragment.body.isKinematic && (fragment.t.position - blast.point).sqrMagnitude < radius * radius)
                    fragment.body.AddExplosionForce(radius * 22, blast.point, radius, radius * .18f, ForceMode.Impulse);
            damagedVehicles.Clear(); damagedProps.Clear(); pushed.Clear(); damagedPoints.Clear();
            int count = Physics.OverlapSphereNonAlloc(blast.point, radius, overlaps, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var collider = overlaps[i]; if (collider == null) continue;
                // Unity rejects ClosestPoint on non-convex terrain/ecology meshes.
                // Their overlap is already confirmed; use bounds for the distance estimate.
                Vector3 point = collider is MeshCollider meshCollider&&!meshCollider.convex
                    ? collider.bounds.ClosestPoint(blast.point) : collider.ClosestPoint(blast.point);
                float falloff = Mathf.Lerp(.15f, 1, 1 - Mathf.Clamp01(Vector3.Distance(point, blast.point) / radius));
                var weakpoint = collider.GetComponentInParent<BossWeakPoint>();
                if (weakpoint != null && damagedPoints.Add(weakpoint)) weakpoint.ApplyDamage(blast.damage * falloff * .6f, point, blast.source, true);
                var vehicle = collider.GetComponentInParent<VehicleDamage>();
                if (vehicle != null && damagedVehicles.Add(vehicle))
                {
                    float self = blast.source != null && vehicle.gameObject == blast.source ? .18f : 1;
                    vehicle.ApplyDamage(blast.damage * falloff * self, point, blast.source, true);
                }
                var prop = collider.GetComponentInParent<DestructionSystem>();
                if (prop != null && damagedProps.Add(prop)) prop.ApplyDamage(blast.damage * falloff, point, blast.source, BlastPush(prop, blast.point, falloff));
                var body = collider.attachedRigidbody;
                if (body != null && !body.isKinematic && pushed.Add(body)) body.AddExplosionForce(radius * 390, blast.point, radius, radius * .18f, ForceMode.Impulse);
            }
        }
        public static void ScatterDebris(Vector3 point, float force, int count, Color color) { Get().Scatter(point, force, count, color, Vector3.zero); }
        /// <summary>Debris thrown by a hit: <paramref name="push"/> (m/s) carries the pieces along a shot, away from a blast or with a car.</summary>
        public static void ScatterDebris(Vector3 point, float force, int count, Color color, Vector3 push) { Get().Scatter(point, force, count, color, push); }
        /// <summary>Velocity a blast gives the pieces of a prop it breaks: outwards and a little upwards, stronger close in.</summary>
        static Vector3 BlastPush(DestructionSystem prop, Vector3 center, float falloff)
        {
            Vector3 away = prop.WorldBounds.center - center; away.y = Mathf.Max(0, away.y);
            if (away.sqrMagnitude < .01f) away = Vector3.up;
            return (away.normalized + Vector3.up * .35f) * (4 + 9 * falloff);
        }
        void Scatter(Vector3 point, float force, int count, Color color, Vector3 push)
        {
            count = Mathf.Min(count, quality == 0 ? 5 : quality == 1 ? 12 : quality==3?48:24);
            for (int i = 0; i < count; i++)
            {
                if (fragments.Count >= DebrisLimit) RetireFragment(0);
                Fragment f;
                if (fragmentPool.Count > 0) f = fragmentPool.Pop();
                else
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = "Pooled debris"; go.layer = 2; go.transform.SetParent(transform);
                    var rb = go.AddComponent<Rigidbody>(); rb.mass = 4; rb.linearDamping = .2f; rb.angularDamping = .2f;
                    rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
                    var renderer = go.GetComponent<Renderer>(); renderer.sharedMaterial = debrisMaterial; renderer.shadowCastingMode = quality > 1 ? ShadowCastingMode.On : ShadowCastingMode.Off;
                    f = new Fragment { view = go, t = go.transform, body = rb, collider = go.GetComponent<Collider>(), renderer = renderer };
                }
                f.view.SetActive(true); f.collider.enabled = true; f.body.isKinematic = false;
                foreach (var vehicle in VehicleController.Active)
                    if (vehicle != null)
                        foreach (var collider in vehicle.GetComponentsInChildren<Collider>())
                            if (collider.enabled) Physics.IgnoreCollision(f.collider, collider, true);
                f.scale = new Vector3(Random.Range(.1f, .45f), Random.Range(.07f, .24f), Random.Range(.15f, .7f));
                f.t.localScale = f.scale;
                f.t.SetPositionAndRotation(point + Random.insideUnitSphere * .5f + Vector3.up * .3f, Random.rotation);
                Vector3 velocity = Random.onUnitSphere * Random.Range(force * .7f, force * 1.6f); velocity.y = Mathf.Abs(velocity.y) + 2;
                velocity += push * Random.Range(.55f, 1.1f);
                f.body.linearVelocity = velocity; f.body.angularVelocity = Random.insideUnitSphere * 18;
                f.born = Time.time; f.until = Time.time + 2.8f + Random.value * 1.4f;
                block.SetColor("_BaseColor", color * Random.Range(.75f, 1.3f)); f.renderer.SetPropertyBlock(block);
                fragments.Add(f);
            }
        }
        void RetireFragment(int i)
        {
            var f = fragments[i]; f.view.SetActive(false); f.body.isKinematic = true;
            fragmentPool.Push(f); fragments.RemoveAt(i);
        }
        Light BurnLight(Vector3 point,float radius)
        {
            if(quality<2)return null;
            Light light=null;
            foreach(var item in burnLights)if(!item.enabled){light=item;break;}
            if(!light && burnLights.Count<(quality==3?12:6)) {
                light=WorldArt.Lamp("Pooled flickering fire illumination",transform,point, new Color(1,.27f,.045f),5,radius*3);burnLights.Add(light);
            }
            if(light){
                light.transform.position=point+Vector3.up*2;light.range=Mathf.Clamp(radius*3f,8,30);light.renderMode=LightRenderMode.ForcePixel;
                light.GetUniversalAdditionalLightData().additionalLightsShadowResolutionTier=0;
                light.shadows=quality==3?LightShadows.Soft:LightShadows.None;light.shadowStrength=.72f;light.enabled=true;
            }
            return light;
        }
        void MakeFlash(Vector3 point, float radius, Color color)
        {
            Flash flash = null;
            foreach (var f in flashes) if (!f.light.enabled) { flash = f; break; }
            if (flash == null && flashes.Count < (quality == 0 ? 2 : quality == 3 ? 10 : quality == 2 ? 6 : 4))
            {
                var go = new GameObject("Pooled explosion light"); go.transform.SetParent(transform);
                var light = go.AddComponent<Light>(); light.type = LightType.Point; light.renderMode = LightRenderMode.ForcePixel;
                light.GetUniversalAdditionalLightData().additionalLightsShadowResolutionTier=1;
                light.shadows = quality >= 2 ? LightShadows.Soft : LightShadows.None; light.shadowStrength = .9f; light.shadowBias = .08f; light.shadowNormalBias = .2f;
                flash = new Flash { light = light }; flashes.Add(flash);
            }
            if (flash == null) return;
            flash.light.transform.position = point; flash.light.color = color; flash.light.range = Mathf.Clamp(radius * 3.2f, 9f, 42f);
            flash.power = 78 + radius * 29; flash.light.intensity = flash.power;
            flash.start = Time.time; flash.until = Time.time + .35f; flash.light.enabled = true;
        }
        void MakeWave(Vector3 point, float radius, Color color)
        {
            Wave wave = null;
            foreach (var w in waves) if (!w.line.enabled) { wave = w; break; }
            if (wave == null && waves.Count < 24)
            {
                var go = new GameObject("Pooled pressure ring"); go.transform.SetParent(transform);
                var line = go.AddComponent<LineRenderer>(); line.useWorldSpace = false; line.loop = true; line.positionCount = 64;
                line.sharedMaterial = waveMaterial; line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false;
                for (int i = 0; i < 64; i++) line.SetPosition(i, new Vector3(Mathf.Sin(i * Mathf.PI / 32), 0, Mathf.Cos(i * Mathf.PI / 32)));
                wave = new Wave { line = line }; waves.Add(wave);
            }
            if (wave == null) return;
            wave.line.transform.position = new Vector3(point.x, Mathf.Max(.08f, point.y - .6f), point.z);
            wave.line.transform.localScale = Vector3.one * .1f;
            wave.start = Time.time; wave.until = Time.time + .65f; wave.radius = radius; wave.color = color;
            wave.line.enabled = true;
        }
        void MakeScorch(Vector3 point, float radius)
        {
            if (quality == 0) return;
            Scorch scorch = null;
            foreach (var s in scorches) if (!s.t.gameObject.activeSelf) { scorch = s; break; }
            if (scorch == null && scorches.Count < 48)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad); go.name = "Pooled blast scorch"; go.transform.SetParent(transform);
                Destroy(go.GetComponent<Collider>()); var renderer = go.GetComponent<Renderer>(); renderer.sharedMaterial = scorchMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = true;
                scorch = new Scorch { t = go.transform, renderer = renderer }; scorches.Add(scorch);
            }
            if (scorch == null) return;
            Vector3 position = point, normal = Vector3.up;
            float castHeight = Mathf.Max(4, radius + 2);
            if (Physics.Raycast(point + Vector3.up * castHeight, Vector3.down, out RaycastHit ground, castHeight * 2 + 8, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                position = ground.point; normal = ground.normal;
            }
            Vector3 tangent = Vector3.ProjectOnPlane(Random.value < .5f ? Vector3.forward : Vector3.right, normal).normalized;
            if (tangent.sqrMagnitude < .2f) tangent = Vector3.Cross(normal, Vector3.right).normalized;
            Quaternion rotation = Quaternion.LookRotation(-normal, tangent) * Quaternion.AngleAxis(Random.Range(0, 360), Vector3.forward);
            float width = radius * Random.Range(1.75f, 2.35f), depth = radius * Random.Range(1.5f, 2.15f);
            scorch.t.gameObject.SetActive(true);
            scorch.t.SetPositionAndRotation(position + normal * (.022f + Random.value * .006f), rotation);
            scorch.scale = new Vector3(width, depth, 1); scorch.t.localScale = scorch.scale;
            scorch.born = Time.time; scorch.until = Time.time + Random.Range(58, 76) + quality * 10; scorch.fadeAt = scorch.until - Random.Range(14, 22);
            scorch.color = Color.Lerp(new Color(.10f, .055f, .025f, .78f), new Color(.17f, .105f, .055f, .64f), Random.value);
            block.SetColor("_BaseColor", scorch.color); scorch.renderer.SetPropertyBlock(block);
        }
        void OnDestroy()
        {
            if (instance == this) instance = null;
            if(smokeMaterial)Destroy(smokeMaterial);if(fireMaterial)Destroy(fireMaterial);if(tireSmokeMaterial)Destroy(tireSmokeMaterial);
            if (particleMaterial != null) Destroy(particleMaterial);
            if (waveMaterial != null) Destroy(waveMaterial);
            if (debrisMaterial != null) Destroy(debrisMaterial);
            if (scorchMaterial != null) Destroy(scorchMaterial);
            if (softTexture != null) Destroy(softTexture);
            if (scorchTexture != null) Destroy(scorchTexture);
        }
    }
}
