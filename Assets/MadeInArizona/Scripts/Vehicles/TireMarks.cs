using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MadeInArizona
{
    /// <summary>
    /// World-scoped pools for the tyre effects (see TireEffects): skid marks and ruts, and the gravel thrown by tyres.
    /// Marks are quads in a ring of mesh chunks. The oldest quads are overwritten first, and every mark fades out a few
    /// minutes after it was laid (Shaders/SkidMarks.shader). Only the chunk being written is re-uploaded. Gravel is one
    /// mesh-particle system of faceted pebbles and clods that bounce off the ground (Shaders/Grit.shader).
    /// </summary>
    public sealed class TireMarks : MonoBehaviour
    {
        const int QuadsPerChunk = 256;
        /// <summary>Seconds a mark stays, and the last part of that over which it fades out.</summary>
        const float Lifetime = 240, FadeTime = 70;
        sealed class Chunk
        {
            public Mesh mesh; public MeshRenderer renderer;
            public readonly Vector3[] vertices = new Vector3[QuadsPerChunk * 4];
            public readonly Color32[] colors = new Color32[QuadsPerChunk * 4];
            public readonly Vector2[] uv = new Vector2[QuadsPerChunk * 4], life = new Vector2[QuadsPerChunk * 4];
            public bool dirty, used;
        }
        static TireMarks instance;
        readonly List<Chunk> chunks = new List<Chunk>();
        int chunkIndex, quadIndex, quality;
        Material markMaterial, gritMaterial;
        ParticleSystem grit;
        Mesh[] pebbles;

        static TireMarks Get()
        {
            // A world being rebuilt deactivates its children before destroying them; start a fresh pool for the new one.
            if (instance != null && instance.isActiveAndEnabled) return instance;
            var go = new GameObject("Pooled tyre marks and thrown grit");
            if (GameManager.Instance != null && GameManager.Instance.World != null) go.transform.SetParent(GameManager.Instance.World.transform);
            instance = go.AddComponent<TireMarks>();
            return instance;
        }
        void Awake()
        {
            quality = GameManager.Instance != null && GameManager.Instance.Save != null ? GameManager.Instance.Save.settings.quality : 2;
            var shader = Shader.Find("MadeInArizona/SkidMarks");
            if (shader && shader.isSupported)
            {
                markMaterial = new Material(shader) { name = "Tyre marks and ruts" };
                int count = new[] { 6, 10, 16, 24 }[Mathf.Clamp(quality, 0, 3)];
                for (int i = 0; i < count; i++) chunks.Add(NewChunk(i));
            }
            BuildGrit();
        }

        // ------------------------------------------------------------------ marks

        /// <summary>
        /// One quad of mark from the previous edge (<paramref name="left0"/>, <paramref name="right0"/>) to the new edge.
        /// <paramref name="tint"/> is the multiplier at full strength; strengths are 0-1; along is in metres.
        /// </summary>
        public static void Segment(Vector3 left0, Vector3 right0, Vector3 left1, Vector3 right1, Color tint, float strength0, float strength1, float along0, float along1, bool soil)
        {
            var pool = Get();
            if (pool.chunks.Count == 0) return;
            var chunk = pool.chunks[pool.chunkIndex];
            int v = pool.quadIndex * 4;
            if (!chunk.used)
            {
                // Park every unused vertex on this mark so the chunk's bounds stay local until it fills.
                for (int i = 0; i < chunk.vertices.Length; i++) chunk.vertices[i] = left0;
                chunk.used = true; chunk.renderer.enabled = true;
            }
            chunk.vertices[v] = left0; chunk.vertices[v + 1] = right0; chunk.vertices[v + 2] = left1; chunk.vertices[v + 3] = right1;
            Color32 c0 = Tint(tint, strength0), c1 = Tint(tint, strength1);
            chunk.colors[v] = c0; chunk.colors[v + 1] = c0; chunk.colors[v + 2] = c1; chunk.colors[v + 3] = c1;
            chunk.uv[v] = new Vector2(0, along0); chunk.uv[v + 1] = new Vector2(1, along0); chunk.uv[v + 2] = new Vector2(0, along1); chunk.uv[v + 3] = new Vector2(1, along1);
            var born = new Vector2(Time.timeSinceLevelLoad, soil ? 1 : 0);
            chunk.life[v] = born; chunk.life[v + 1] = born; chunk.life[v + 2] = born; chunk.life[v + 3] = born;
            chunk.dirty = true;
            if (++pool.quadIndex >= QuadsPerChunk) { pool.quadIndex = 0; pool.chunkIndex = (pool.chunkIndex + 1) % pool.chunks.Count; }
        }
        static Color32 Tint(Color tint, float strength) =>
            new Color32((byte)(Mathf.Clamp01(tint.r) * 255), (byte)(Mathf.Clamp01(tint.g) * 255), (byte)(Mathf.Clamp01(tint.b) * 255), (byte)(Mathf.Clamp01(strength) * 255));

        Chunk NewChunk(int index)
        {
            var go = new GameObject("Tyre marks " + index); go.transform.SetParent(transform, false);
            var chunk = new Chunk { mesh = new Mesh { name = "Tyre marks " + index } };
            chunk.mesh.MarkDynamic();
            var triangles = new int[QuadsPerChunk * 6];
            for (int q = 0; q < QuadsPerChunk; q++)
            {
                int v = q * 4, t = q * 6;
                triangles[t] = v; triangles[t + 1] = v + 2; triangles[t + 2] = v + 1;
                triangles[t + 3] = v + 1; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
            }
            chunk.mesh.SetVertices(chunk.vertices); chunk.mesh.SetColors(chunk.colors);
            chunk.mesh.SetUVs(0, chunk.uv); chunk.mesh.SetUVs(1, chunk.life);
            chunk.mesh.SetTriangles(triangles, 0);
            go.AddComponent<MeshFilter>().sharedMesh = chunk.mesh;
            chunk.renderer = go.AddComponent<MeshRenderer>();
            chunk.renderer.sharedMaterial = markMaterial;
            chunk.renderer.shadowCastingMode = ShadowCastingMode.Off; chunk.renderer.receiveShadows = false;
            chunk.renderer.lightProbeUsage = LightProbeUsage.Off; chunk.renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            chunk.renderer.enabled = false;
            return chunk;
        }

        void LateUpdate()
        {
            if (markMaterial) markMaterial.SetVector("_MarkParams", new Vector4(Lifetime, FadeTime, Mathf.Max(0, DevTuning.Current.skidMarks), 0));
            foreach (var chunk in chunks)
            {
                if (!chunk.dirty) continue;
                chunk.dirty = false;
                chunk.mesh.SetVertices(chunk.vertices); chunk.mesh.SetColors(chunk.colors);
                chunk.mesh.SetUVs(0, chunk.uv); chunk.mesh.SetUVs(1, chunk.life);
                var bounds = new Bounds(chunk.vertices[0], Vector3.zero);
                for (int i = 1; i < chunk.vertices.Length; i++) bounds.Encapsulate(chunk.vertices[i]);
                bounds.Expand(.2f);
                chunk.mesh.bounds = bounds;
            }
        }

        // ------------------------------------------------------------------ gravel

        /// <summary>Throws one pebble or clod (<paramref name="size"/> metres across) that tumbles and bounces.</summary>
        public static void Throw(Vector3 position, Vector3 velocity, float size, Color color)
        {
            var pool = Get();
            if (pool.grit == null) return;
            pool.grit.Emit(new ParticleSystem.EmitParams
            {
                position = position, velocity = velocity, startSize = size, startColor = color,
                startLifetime = Random.Range(1.1f, 2.2f),
                rotation3D = new Vector3(Random.Range(0, 360f), Random.Range(0, 360f), Random.Range(0, 360f))
            }, 1);
        }

        void BuildGrit()
        {
            var shader = Shader.Find("MadeInArizona/Grit");
            if (!shader || !shader.isSupported) return;
            gritMaterial = new Material(shader) { name = "Thrown gravel" };
            var go = new GameObject("Thrown gravel and clods"); go.transform.SetParent(transform, false);
            grit = go.AddComponent<ParticleSystem>();
            grit.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = grit.main;
            main.loop = true; main.playOnAwake = false; main.maxParticles = 220 + quality * 170;
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.startSpeed = 0; main.gravityModifier = 1.5f;
            main.startRotation3D = true;
            var emission = grit.emission; emission.enabled = false;
            var shape = grit.shape; shape.enabled = false;
            // Stones keep their size while they fly and rest, then shrink away at the end of their life.
            var size = grit.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(new Keyframe(0, 1), new Keyframe(.82f, 1), new Keyframe(1, 0)));
            // Stones tumble as fast as they fly, so they stop turning once they come to rest.
            var tumble = grit.rotationBySpeed;
            tumble.enabled = true; tumble.separateAxes = true; tumble.range = new Vector2(0, 8);
            var slowest = AnimationCurve.Linear(0, 0, 1, -1); var fastest = AnimationCurve.Linear(0, 0, 1, 1);
            tumble.x = new ParticleSystem.MinMaxCurve(16, slowest, fastest);
            tumble.y = new ParticleSystem.MinMaxCurve(16, slowest, fastest);
            tumble.z = new ParticleSystem.MinMaxCurve(16, slowest, fastest);
            var collision = grit.collision;
            collision.enabled = true; collision.type = ParticleSystemCollisionType.World; collision.mode = ParticleSystemCollisionMode.Collision3D;
            collision.quality = ParticleSystemCollisionQuality.High; collision.enableDynamicColliders = false;
            collision.bounce = new ParticleSystem.MinMaxCurve(.18f, .42f); collision.dampen = new ParticleSystem.MinMaxCurve(.35f, .65f);
            collision.lifetimeLoss = 0; collision.radiusScale = .5f;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            pebbles = new[] { Pebble(1, .62f), Pebble(2, .75f), Pebble(3, .9f) };
            renderer.SetMeshes(pebbles);
            renderer.alignment = ParticleSystemRenderSpace.World;
            renderer.enableGPUInstancing = false;
            renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream> { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Normal, ParticleSystemVertexStream.Color });
            renderer.sharedMaterial = gritMaterial;
            renderer.shadowCastingMode = quality >= 2 ? ShadowCastingMode.On : ShadowCastingMode.Off; renderer.receiveShadows = true;
            grit.Play();
        }

        /// <summary>A faceted stone about one unit across: a jittered, flattened icosahedron with flat-shaded faces.</summary>
        static Mesh Pebble(int seed, float flatness)
        {
            float t = (1 + Mathf.Sqrt(5)) * .5f;
            var corners = new[]
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1)
            };
            var random = new System.Random(seed * 131);
            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 c = corners[i].normalized * (.36f + (float)random.NextDouble() * .2f);
                corners[i] = new Vector3(c.x, c.y * flatness, c.z);
            }
            int[] faces =
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1
            };
            var vertices = new Vector3[faces.Length];
            var triangles = new int[faces.Length];
            for (int f = 0; f < faces.Length; f += 3)
            {
                Vector3 a = corners[faces[f]], b = corners[faces[f + 1]], c = corners[faces[f + 2]];
                // Face outward whatever the source winding: Unity's front face is the one Cross(b - a, c - a) points to.
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), a + b + c) < 0) { var swap = b; b = c; c = swap; }
                vertices[f] = a; vertices[f + 1] = b; vertices[f + 2] = c;
                triangles[f] = f; triangles[f + 1] = f + 1; triangles[f + 2] = f + 2;
            }
            var mesh = new Mesh { name = "Pebble " + seed, vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
            foreach (var chunk in chunks) if (chunk.mesh) Destroy(chunk.mesh);
            if (markMaterial) Destroy(markMaterial);
            if (gritMaterial) Destroy(gritMaterial);
            if (pebbles != null) foreach (var mesh in pebbles) if (mesh) Destroy(mesh);
        }
    }
}
