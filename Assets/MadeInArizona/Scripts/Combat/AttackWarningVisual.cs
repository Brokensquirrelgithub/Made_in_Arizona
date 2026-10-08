using UnityEngine;
using UnityEngine.Rendering;

namespace MadeInArizona
{
    /// <summary>World-space enemy attack tells: a locked sniper beam or a timed ground impact ring.</summary>
    public sealed class AttackWarningVisual : MonoBehaviour
    {
        const int RingSteps = 48;
        static readonly Color Red = new Color(1f, .055f, .035f);
        readonly RaycastHit[] groundHits = new RaycastHit[16];
        Material material, coreMaterial;
        LineRenderer beamGlow, beamHalo, beamCore, impactRing, timerRing;
        Vector3 impact;
        Transform impactFollow;
        float radius, impactAt, impactDuration;
        bool hasImpact;

        void Awake()
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            material = new Material(shader) { name = "Enemy attack warning", renderQueue = 3000 };
            material.SetFloat("_Surface", 1); material.SetFloat("_Blend", 2);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.One);
            material.SetFloat("_ZWrite", 0); material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetColor("_BaseColor", new Color(3, .45f, .35f, 1));
            coreMaterial = new Material(material) { name = "Sniper warning bright core" };
            coreMaterial.SetColor("_BaseColor", new Color(3, 1.8f, 1.4f, 1));
        }

        LineRenderer MakeLine(string name, int points, float width, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = material; line.useWorldSpace = true; line.positionCount = points;
            line.widthMultiplier = width; line.numCapVertices = 8; line.numCornerVertices = 3;
            line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false;
            line.startColor = line.endColor = color;
            line.enabled = false;
            return line;
        }

        public void ShowSniper(Vector3 start, Vector3 end, float remaining, float duration)
        {
            if (!beamGlow)
            {
                beamGlow = MakeLine("Sniper warning glow", 2, .85f, new Color(1, .025f, .02f, .45f));
                beamHalo = MakeLine("Sniper warning halo", 2, .38f, new Color(1, .2f, .14f, .75f));
                beamCore = MakeLine("Sniper warning core", 2, .13f, new Color(1, .72f, .6f, 1));
                beamCore.sharedMaterial = coreMaterial;
            }
            float charge = 1 - Mathf.Clamp01(remaining / Mathf.Max(.01f, duration));
            float pulse = .78f + .22f * Mathf.Sin(Time.time * 19f);
            beamGlow.widthMultiplier = Mathf.Lerp(.78f, 1.15f, charge) * pulse;
            beamHalo.widthMultiplier = Mathf.Lerp(.32f, .5f, charge) * pulse;
            beamCore.widthMultiplier = Mathf.Lerp(.1f, .18f, charge) * pulse;
            SetBeamPath(beamGlow, start, end);
            SetBeamPath(beamHalo, start, end);
            SetBeamPath(beamCore, start, end);
            beamGlow.enabled = beamHalo.enabled = beamCore.enabled = true;
        }

        static void SetBeamPath(LineRenderer line, Vector3 start, Vector3 end)
        {
            line.positionCount = 2;
            line.SetPosition(0, start);
            line.SetPosition(1, end);
        }

        public void HideSniper()
        {
            if (beamGlow) beamGlow.enabled = false;
            if (beamHalo) beamHalo.enabled = false;
            if (beamCore) beamCore.enabled = false;
        }

        public void BeginImpact(Vector3 point, float blastRadius, float timeToImpact, Transform follow = null)
        {
            impact = Ground(point);
            impactFollow = follow;
            radius = blastRadius;
            impactDuration = Mathf.Max(.1f, timeToImpact);
            impactAt = Time.time + impactDuration;
            hasImpact = true;
            impactRing = MakeLine("Blast radius", RingSteps + 1, .13f, new Color(1, .055f, .035f, .78f));
            timerRing = MakeLine("Closing impact timer", RingSteps + 1, .20f, new Color(1, .16f, .09f, .95f));
            DrawRing(impactRing, radius);
            impactRing.enabled = timerRing.enabled = true;
        }

        Vector3 Ground(Vector3 point)
        {
            int count = Physics.RaycastNonAlloc(point + Vector3.up * 30f, Vector3.down, groundHits, 75f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float closest = float.MaxValue; Vector3 result = point;
            for (int i = 0; i < count; i++)
            {
                var hit = groundHits[i];
                if (!hit.collider || hit.collider.GetComponentInParent<VehicleController>()) continue;
                if (hit.distance < closest) { closest = hit.distance; result = hit.point; }
            }
            return result + Vector3.up * .12f;
        }

        void DrawRing(LineRenderer line, float ringRadius)
        {
            for (int i = 0; i <= RingSteps; i++)
            {
                float angle = i * (Mathf.PI * 2 / RingSteps);
                Vector3 at = impact + new Vector3(Mathf.Cos(angle) * ringRadius, 0, Mathf.Sin(angle) * ringRadius);
                line.SetPosition(i, Ground(at));
            }
        }

        void LateUpdate()
        {
            if (!hasImpact || !timerRing) return;
            bool playing = GameManager.Instance && GameManager.Instance.IsPlaying;
            impactRing.enabled = timerRing.enabled = playing;
            if (!playing) return;
            if (impactFollow)
            {
                impact = Ground(impactFollow.position);
                DrawRing(impactRing, radius);
            }
            float remaining = Mathf.Clamp01((impactAt - Time.time) / impactDuration);
            DrawRing(timerRing, Mathf.Max(.12f, radius * remaining));
            float pulse = .7f + .3f * Mathf.Sin(Time.time * 9f);
            Color outer = Red; outer.a = .48f + .18f * pulse;
            impactRing.startColor = impactRing.endColor = outer;
        }

        void OnDestroy()
        {
            if (material) Destroy(material);
            if (coreMaterial) Destroy(coreMaterial);
        }
    }
}
