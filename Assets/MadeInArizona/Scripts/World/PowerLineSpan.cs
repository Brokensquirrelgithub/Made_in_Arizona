using UnityEngine;
using UnityEngine.Rendering;

namespace MadeInArizona
{
    /// <summary>Three roadside cables that drop onto the terrain when either supporting pole topples.</summary>
    public sealed class PowerLineSpan : MonoBehaviour
    {
        const int Samples = 15;
        const float ArmHeight = 8.95f;
        const float DropTime = .85f, FallTime = 1.4f, FadeTime = .7f;
        static readonly Color CableColor = new Color(.12f, .105f, .09f, 1);
        static readonly float[] Offsets = { -1.22f, 0, 1.22f };
        static Material cableMaterial;

        Transform first, second;
        DestructionSystem firstDamage, secondDamage;
        Vector3 firstBase, secondBase;
        Quaternion firstRotation, secondRotation;
        readonly LineRenderer[] cables = new LineRenderer[3];
        float collapse;
        float fallenAt = -1;
        bool lastFallenA, lastFallenB;
        public float Collapse => collapse;

        public static PowerLineSpan Connect(Transform parent, Transform a, Transform b)
        {
            if (!a || !b) return null;
            var root = new GameObject("Power line span");
            root.transform.SetParent(parent, false);
            var span = root.AddComponent<PowerLineSpan>();
            span.Configure(a, b);
            return span;
        }

        public bool Connects(Transform pole) => pole && (pole == first || pole == second);

        void Configure(Transform a, Transform b)
        {
            first = a; second = b;
            firstDamage = a.GetComponent<DestructionSystem>();
            secondDamage = b.GetComponent<DestructionSystem>();
            firstBase = a.position; secondBase = b.position;
            firstRotation = a.rotation; secondRotation = b.rotation;
            if (!cableMaterial)
            {
                var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
                cableMaterial = new Material(shader) { name = "Roadside power cable", renderQueue = 3000 };
                cableMaterial.SetFloat("_Surface", 1);
                cableMaterial.SetFloat("_Blend", 0);
                cableMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                cableMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                cableMaterial.SetFloat("_ZWrite", 0);
                cableMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                if (cableMaterial.HasProperty("_BaseColor")) cableMaterial.SetColor("_BaseColor", Color.white);
                if (cableMaterial.HasProperty("_Color")) cableMaterial.SetColor("_Color", Color.white);
            }
            for (int i = 0; i < cables.Length; i++)
            {
                var wire = new GameObject("Power line " + (i + 1));
                wire.transform.SetParent(transform, false);
                var line = wire.AddComponent<LineRenderer>();
                line.sharedMaterial = cableMaterial;
                line.useWorldSpace = true;
                line.positionCount = Samples;
                line.widthMultiplier = .06f;
                line.numCornerVertices = 2;
                line.numCapVertices = 2;
                line.shadowCastingMode = ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.startColor = line.endColor = CableColor;
                cables[i] = line;
            }
            Draw(false, false);
        }

        void LateUpdate()
        {
            bool fallenA = !first || firstDamage && firstDamage.IsDestroyed;
            bool fallenB = !second || secondDamage && secondDamage.IsDestroyed;
            if (!fallenA && !fallenB) return;
            if (fallenAt < 0) fallenAt = Time.time;
            if (collapse < 1 || fallenA != lastFallenA || fallenB != lastFallenB)
            {
                collapse = Mathf.MoveTowards(collapse, 1, Time.deltaTime / DropTime);
                Draw(fallenA, fallenB);
            }
            // The supporting pole waits through its fall, then fades over .7 s; the dropped wires follow it.
            float opacity = 1 - Mathf.Clamp01((Time.time - fallenAt - FallTime) / FadeTime);
            Color ink = CableColor; ink.a = opacity;
            foreach (var cable in cables)
            {
                cable.startColor = cable.endColor = ink;
                if (opacity <= 0) cable.enabled = false;
            }
            if (opacity <= 0) enabled = false;
            lastFallenA = fallenA;
            lastFallenB = fallenB;
        }

        void Draw(bool fallenA, bool fallenB)
        {
            for (int wire = 0; wire < cables.Length; wire++)
            {
                float offset = Offsets[wire];
                Vector3 a = Anchor(first, firstBase, firstRotation, offset);
                Vector3 b = Anchor(second, secondBase, secondRotation, offset);
                Vector3 groundA = Ground(0, offset), groundB = Ground(1, offset);
                for (int i = 0; i < Samples; i++)
                {
                    float t = i / (float)(Samples - 1);
                    Vector3 floor = Ground(t, offset);
                    float bow = 4 * t * (1 - t);
                    Vector3 high = Vector3.Lerp(a, b, t) - Vector3.up * (1.15f * bow);
                    high.y = Mathf.Max(high.y, floor.y + 4f);
                    Vector3 low = Vector3.Lerp(fallenA ? groundA : a, fallenB ? groundB : b, t) - Vector3.up * (4.4f * bow);
                    low.y = Mathf.Max(low.y, floor.y);
                    low += Vector3.Cross((secondBase - firstBase).normalized, Vector3.up) * (.24f * Mathf.Sin(t * Mathf.PI * 3) * collapse);
                    cables[wire].SetPosition(i, Vector3.Lerp(high, low, collapse));
                }
            }
        }

        static Vector3 Anchor(Transform pole, Vector3 basis, Quaternion rotation, float offset)
            => pole ? pole.TransformPoint(new Vector3(offset, ArmHeight, 0)) : basis + rotation * new Vector3(offset, ArmHeight, 0);

        Vector3 Ground(float t, float offset)
        {
            Vector3 a = firstBase + firstRotation * Vector3.right * offset;
            Vector3 b = secondBase + secondRotation * Vector3.right * offset;
            Vector3 p = Vector3.Lerp(a, b, t);
            p.y = (GeneratedWorld.Active ? GeneratedWorld.HeightAt(p) : Mathf.Lerp(firstBase.y, secondBase.y, t)) + .16f;
            return p;
        }
    }
}
