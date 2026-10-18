using UnityEngine;

namespace MadeInArizona
{
    /// <summary>Visible pulse around a car while the rally horn's drive bonus is active.</summary>
    public sealed class RallyAura : MonoBehaviour
    {
        VehicleController owner;
        LineRenderer ring;
        Material material;
        Light glow;

        public void Bind(VehicleController vehicle)
        {
            owner = vehicle;
            if (ring) return;
            var visual = new GameObject("Rally horn aura");
            visual.transform.SetParent(transform, false);
            ring = visual.AddComponent<LineRenderer>();
            ring.useWorldSpace = true;
            ring.positionCount = 49;
            ring.widthMultiplier = .22f;
            ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default"));
            ring.sharedMaterial = material;
            glow = visual.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = new Color(.3f, 1f, .72f);
            glow.range = 8f;
            glow.shadows = LightShadows.None;
        }

        void LateUpdate()
        {
            if (!owner || !owner.RallyActive) { Destroy(this); return; }
            if (!ring) return;
            float pulse = .82f + .18f * Mathf.Sin(Time.time * 8f);
            float radius = 3.6f * pulse;
            Vector3 center = owner.transform.position + Vector3.up * .24f;
            for (int i = 0; i < 49; i++)
            {
                float angle = i * Mathf.PI * 2 / 48f;
                ring.SetPosition(i, center + new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius));
            }
            ring.startColor = ring.endColor = new Color(.35f, 1.5f, .85f, .85f * pulse);
            glow.intensity = 3.5f * pulse;
        }

        void OnDestroy()
        {
            if (ring) Destroy(ring.gameObject);
            if (material) Destroy(material);
        }
    }
}
