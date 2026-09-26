using UnityEngine;

namespace MadeInArizona
{
    /// <summary>Player hurt and critical-damage tells: edge vignette, hit direction, trailing health and warnings.</summary>
    public sealed partial class GameUI
    {
        const float CriticalHealth = .3f;
        Texture2D edgeVignette;
        float ghostHealth = 1, ghostHoldUntil, seenDamageTime = float.NegativeInfinity, hurtStrength;
        Vector2 hurtDirection;

        void TrackPlayerDamage(VehicleController player)
        {
            var damage = player.Damage;
            float health = damage.Health / Mathf.Max(1, damage.MaxHealth);
            if (damage.LastDamageTime != seenDamageTime)
            {
                float previous = seenDamageTime;
                seenDamageTime = damage.LastDamageTime;
                // Big hits flash harder; rapid small hits keep the flash topped up instead of stacking.
                float fresh = Mathf.Clamp01(.45f + damage.LastDamageFraction * 6);
                float remaining = Mathf.Clamp01(1 - (seenDamageTime - previous) / .6f);
                hurtStrength = Mathf.Max(hurtStrength * remaining, fresh);
                Vector3 from = damage.LastDamageSource ? damage.LastDamageSource.transform.position : damage.LastDamagePoint;
                Vector2 a = ScreenPoint(player.transform.position), b = ScreenPoint(from);
                hurtDirection = (b - a).sqrMagnitude > 16 ? (b - a).normalized : Vector2.zero;
                ghostHoldUntil = Time.time + .55f;
            }
            if (health > ghostHealth) ghostHealth = health;
            else if (Time.time > ghostHoldUntil) ghostHealth = Mathf.MoveTowards(ghostHealth, health, Time.deltaTime * .45f);
        }

        void DrawDamageFeedback(VehicleController player)
        {
            if (Event.current.type != EventType.Repaint) return;
            TrackPlayerDamage(player);
            var damage = player.Damage;
            float health = damage.Health / Mathf.Max(1, damage.MaxHealth);
            float since = Time.time - damage.LastDamageTime;

            // Persistent low-health pulse, faster and stronger as the hull gets closer to failing.
            if (health < CriticalHealth && !damage.IsDead)
            {
                float danger = Mathf.InverseLerp(CriticalHealth, .05f, health);
                float rate = Mathf.Lerp(1.1f, 2.1f, danger);
                float beat = Mathf.Pow(.5f + .5f * Mathf.Sin(Time.time * rate * Mathf.PI * 2), 3);
                DrawEdgeVignette(new Color(.75f, .02f, .0f, Mathf.Lerp(.28f, .5f, danger) + beat * Mathf.Lerp(.18f, .32f, danger)));
                float blink = .55f + .45f * Mathf.Sin(Time.time * rate * Mathf.PI * 2);
                string repair = player.RepairCharge > .02f ? (InputManager.Instance.UsingGamepad ? "  •  HOLD LB TO REPAIR" : "  •  HOLD R TO REPAIR") : "  •  FIND A REPAIR DROP";
                Text(width * .5f - 320, height - 250, 640, 34, "CRITICAL DAMAGE" + repair, 22, new Color(1, .22f, .12f, blink), true, TextAnchor.MiddleCenter);
            }

            // Fresh hit: a strong red flash that fades out, plus a bright wedge toward the attacker.
            if (since < .6f)
            {
                float fade = 1 - since / .6f;
                float alpha = hurtStrength * fade * fade;
                DrawEdgeVignette(new Color(1, .1f, .03f, alpha * .85f));
                Color border = new Color(1, .16f, .04f, alpha * .7f);
                float t = 6 + 10 * hurtStrength;
                Rect(0, 0, width, t, border); Rect(0, height - t, width, t, border); Rect(0, 0, t, height, border); Rect(width - t, 0, t, height, border);
                if (hurtDirection != Vector2.zero) DrawHitDirection(hurtDirection, alpha);
            }
        }

        void DrawHitDirection(Vector2 direction, float alpha)
        {
            Vector2 center = new Vector2(width * .5f, height * .5f);
            float radius = Mathf.Min(width, height) * .36f, angle = Mathf.Atan2(direction.y, direction.x);
            Color color = new Color(1, .25f, .1f, Mathf.Clamp01(alpha * 1.3f));
            const int segments = 7; const float arc = .42f;
            for (int i = 0; i < segments; i++)
            {
                float a0 = angle - arc * .5f + arc * i / segments, a1 = angle - arc * .5f + arc * (i + 1) / segments;
                Vector2 p0 = center + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * radius, p1 = center + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * radius;
                CombatLine(p0, p1, 9, color);
            }
            Vector2 tip = center + direction * (radius + 22);
            Vector2 side = new Vector2(-direction.y, direction.x);
            CombatLine(tip, tip - direction * 18 + side * 14, 5, color);
            CombatLine(tip, tip - direction * 18 - side * 14, 5, color);
        }

        void DrawEdgeVignette(Color color)
        {
            if (color.a <= .005f) return;
            if (!edgeVignette) edgeVignette = BuildEdgeVignette();
            var previous = GUI.color;
            GUI.color = QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color;
            GUI.DrawTexture(new Rect(0, 0, width, height), edgeVignette, ScaleMode.StretchToFill, true);
            GUI.color = previous;
        }

        static Texture2D BuildEdgeVignette()
        {
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Damage edge vignette", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + .5f) / size * 2 - 1, v = (y + .5f) / size * 2 - 1;
                    // Rounded-rectangle distance keeps the tint hugging all four screen edges, clear in the middle.
                    float edge = Mathf.Pow(Mathf.Pow(Mathf.Abs(u), 4) + Mathf.Pow(Mathf.Abs(v), 4), .25f);
                    float a = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.62f, 1.02f, edge));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255));
                }
            texture.SetPixels32(pixels); texture.Apply(false, true);
            return texture;
        }

        /// <summary>Health bar with a trailing chunk showing the damage just taken and a critical blink.</summary>
        void DrawHealthBar(VehicleController player, float x, float y, float w, float h)
        {
            var damage = player.Damage;
            float health = Mathf.Clamp01(damage.Health / Mathf.Max(1, damage.MaxHealth));
            float since = Time.time - damage.LastDamageTime;
            bool critical = health < CriticalHealth;
            float pulse = critical ? .5f + .5f * Mathf.Sin(Time.time * 9) : 0;
            if (critical) Rect(x - 3, y - 3, w + 6, h + 6, new Color(1, .15f, .05f, .35f + .45f * pulse));
            Rect(x, y, w, h, new Color(.2f, .26f, .27f));
            if (ghostHealth > health) Rect(x + w * health, y, w * (Mathf.Clamp01(ghostHealth) - health), h, new Color(1, .82f, .55f));
            Color fill = critical ? Color.Lerp(Orange, new Color(1, .1f, .05f), pulse) : health > .55f ? Lime : Color.Lerp(Orange, Lime, (health - CriticalHealth) / (.55f - CriticalHealth));
            if (since < .12f) fill = Color.white;
            Rect(x, y, w * health, h, fill);
        }

        void OnDisable() { if (edgeVignette) { Destroy(edgeVignette); edgeVignette = null; } }
    }
}
