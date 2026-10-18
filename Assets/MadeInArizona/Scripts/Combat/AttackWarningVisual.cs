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
        LineRenderer beamGlow, beamHalo, beamCore, impactRing, timerRing, zoneOutline, zoneCenter;
        LineRenderer[] strikeLines;
        float strikeUntil;
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

        /// <summary>Draw a fixed, ground-hugging danger lane or fan while the enemy winds up.</summary>
        public void ShowZone(Vector3 origin, Vector3 direction, bool cone, float remaining, float duration)
        {
            if (!zoneOutline)
            {
                zoneOutline = MakeLine("Area attack boundary", 0, .3f, new Color(1, .12f, .05f, .92f));
                zoneCenter = MakeLine("Area attack center", 0, .15f, new Color(1, .45f, .12f, .7f));
            }
            Vector3 forward = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
            if (forward.sqrMagnitude < .01f) return;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 start = origin + forward * 2.5f;
            float length = cone ? 34f : 42f;
            if (cone)
            {
                const int steps = 20;
                zoneOutline.positionCount = steps + 3;
                zoneOutline.SetPosition(0, Ground(start));
                for (int i = 0; i <= steps; i++)
                {
                    float angle = Mathf.Lerp(-27f, 27f, i / (float)steps) * Mathf.Deg2Rad;
                    zoneOutline.SetPosition(i + 1, Ground(start + (forward * Mathf.Cos(angle) + right * Mathf.Sin(angle)) * length));
                }
                zoneOutline.SetPosition(steps + 2, Ground(start));
            }
            else
            {
                zoneOutline.positionCount = 5;
                zoneOutline.SetPosition(0, Ground(start - right * 4.5f));
                zoneOutline.SetPosition(1, Ground(start + forward * length - right * 4.5f));
                zoneOutline.SetPosition(2, Ground(start + forward * length + right * 4.5f));
                zoneOutline.SetPosition(3, Ground(start + right * 4.5f));
                zoneOutline.SetPosition(4, Ground(start - right * 4.5f));
            }
            zoneCenter.positionCount = 2;
            zoneCenter.SetPosition(0, Ground(start));
            zoneCenter.SetPosition(1, Ground(start + forward * length));
            float charge = 1f - Mathf.Clamp01(remaining / duration);
            float pulse = .7f + .3f * Mathf.Sin(Time.time * 16f);
            zoneOutline.widthMultiplier = (.22f + .22f * charge) * pulse;
            zoneCenter.widthMultiplier = (.12f + .11f * charge) * pulse;
            zoneOutline.enabled = zoneCenter.enabled = true;
        }

        public void HideZone()
        {
            if (zoneOutline) zoneOutline.enabled = false;
            if (zoneCenter) zoneCenter.enabled = false;
        }

        /// <summary>A bright, full-footprint discharge that persists long enough to read after the warning ends.</summary>
        public void StrikeZone(Vector3 origin, Vector3 direction, bool cone)
        {
            if (strikeLines == null)
            {
                strikeLines = new LineRenderer[20];
                for (int i = 0; i < strikeLines.Length; i++)
                {
                    strikeLines[i] = MakeLine("Area attack discharge " + i, 2, i < 13 ? 2.9f : .8f,
                        new Color(2.5f, .45f, .08f, .8f));
                    strikeLines[i].sharedMaterial = coreMaterial;
                }
            }
            Vector3 forward = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
            if (forward.sqrMagnitude < .01f) return;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 start = origin + forward * 2.5f;
            float length = cone ? 34f : 42f;
            for (int i = 0; i < 13; i++)
            {
                float lane = (i - 6) / 6f;
                float farSide = cone ? length * Mathf.Tan(27f * Mathf.Deg2Rad) : 4.5f;
                float nearSide = cone ? .15f : 4.5f;
                strikeLines[i].SetPosition(0, Ground(start + right * lane * nearSide));
                strikeLines[i].SetPosition(1, Ground(start + forward * length + right * lane * farSide));
                strikeLines[i].enabled = true;
            }
            for (int i = 13; i < 20; i++)
            {
                float distance = length * (i - 12) / 8f;
                float side = cone ? distance * Mathf.Tan(27f * Mathf.Deg2Rad) : 4.5f;
                strikeLines[i].SetPosition(0, Ground(start + forward * distance - right * side));
                strikeLines[i].SetPosition(1, Ground(start + forward * distance + right * side));
                strikeLines[i].enabled = true;
            }
            strikeUntil = Time.time + .65f;
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
            if (strikeLines != null)
            {
                float strikeRemaining = Mathf.Clamp01((strikeUntil - Time.time) / .65f);
                foreach (var line in strikeLines)
                {
                    line.enabled = strikeRemaining > 0;
                    if (line.enabled)
                    {
                        line.widthMultiplier = 2.9f * strikeRemaining;
                        var color = new Color(2.5f, .45f + .4f * strikeRemaining, .08f, strikeRemaining * .85f);
                        line.startColor = line.endColor = color;
                    }
                }
            }
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

        void OnDisable()
        {
            HideZone(); HideSniper();
            if (strikeLines != null) foreach (var line in strikeLines) if (line) line.enabled = false;
        }

        void OnDestroy()
        {
            if (material) Destroy(material);
            if (coreMaterial) Destroy(coreMaterial);
        }
    }

    /// <summary>Guest-side zone warning that follows the interpolated car between host snapshots.</summary>
    public sealed class NetworkAttackWarning : MonoBehaviour
    {
        AttackWarningVisual visual;
        EnemyAttackTelegraph attack;
        Vector3 direction;
        float remaining, receivedAt;

        public void Set(EnemyAttackTelegraph kind, Vector3 aim, float seconds)
        {
            attack = kind; direction = aim; remaining = seconds; receivedAt = Time.unscaledTime;
            if ((kind != EnemyAttackTelegraph.Line && kind != EnemyAttackTelegraph.Cone) && visual)
                visual.HideZone();
        }

        void LateUpdate()
        {
            if (attack != EnemyAttackTelegraph.Line && attack != EnemyAttackTelegraph.Cone) return;
            float timeLeft = remaining - (Time.unscaledTime - receivedAt);
            if (timeLeft <= 0 || Time.unscaledTime - receivedAt > .3f)
            { if (visual) visual.HideZone(); return; }
            if (!visual) visual = gameObject.AddComponent<AttackWarningVisual>();
            visual.ShowZone(transform.position, direction, attack == EnemyAttackTelegraph.Cone, timeLeft, 1.45f);
        }

        void OnDisable() { if (visual) visual.HideZone(); }
    }
}
