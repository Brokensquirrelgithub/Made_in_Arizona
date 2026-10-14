using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using static MadeInArizona.WorldArt;

namespace MadeInArizona
{
    /// <summary>
    /// A whip aerial several car-heights tall, wound with rope light, with a small safety pennant at its head: the
    /// triangle of bright cloth that lets others see a low car over a rise. The whip is thin sprung metal: the wind of
    /// the car's passage lays it back, it lashes when the car brakes or pulls away, swings to the outside of a turn and
    /// wobbles for a few beats afterwards; the pennant ripples, harder the faster the car goes. A rope light winds
    /// around the mast and brightens at dusk, when a lamp at the masthead also picks the pennant out.
    /// </summary>
    public sealed class WhipFlag : MonoBehaviour
    {
        /// <summary>The pennant: a triangle this long from the whip to its point, and this tall where it meets the whip.</summary>
        public const float PennantLength = .46f, PennantHeight = .3f;
        const int Sections = 5, Columns = 6, Rows = 2;
        // The whip as a spring: a shade under one swing a second, damped lightly enough to take a few to settle.
        const float Stiffness = 30, Damping = 1.9f;
        // Degrees it leans per metre its tip is left behind, and metres the wind holds the tip back per metre a second.
        const float LeanPerMetre = 29, WindLag = .031f;
        // The most it leans, the furthest its tip strays, and a move in one step that is being placed, not driving.
        const float MaxLean = 38, Reach = 1.6f, Placed = 5;
        static readonly Dictionary<Color, Material> Cloths = new Dictionary<Color, Material>();
        readonly Transform[] sections = new Transform[Sections];
        readonly LineRenderer[] rope = new LineRenderer[Sections];
        MaterialPropertyBlock ropeBlock;
        Color ropeColour;
        float ropeIntensity = -1;
        Mesh cloth; Vector3[] rest, waved; Light lamp;
        Vector3 lastFoot, drift, tip, tipVelocity; bool standing; float clock;

        /// <summary>Length of the whip from its foot to its tip.</summary>
        public float Height { get; private set; }
        /// <summary>The tip of the whip, where the pennant flies.</summary>
        public Vector3 Tip => sections[Sections - 1] ? sections[Sections - 1].TransformPoint(Vector3.up * Height / Sections) : transform.position;
        /// <summary>How far the whip leans from upright, in degrees: x toward the car's right, y toward its nose.</summary>
        public Vector2 Lean { get; private set; }
        float Speed => new Vector2(drift.x, drift.z).magnitude;

        public static WhipFlag Build(Transform parent, Vector3 foot, float height, Color pennant, Color glow)
        {
            var whip = Group("Whip flag", parent, foot).gameObject.AddComponent<WhipFlag>();
            whip.Height = height;
            whip.ropeColour = glow;
            float length = height / Sections;
            Cylinder("Whip spring base", whip.transform, Vector3.up * .05f, .035f, .1f, Ink);
            Transform joint = whip.transform;
            for (int i = 0; i < Sections; i++)
            {
                joint = whip.sections[i] = Group("Whip section", joint, i == 0 ? Vector3.zero : Vector3.up * length);
                // The dark sprung rod tapers to the tip. A separate luminous strand winds around it.
                float radius = Mathf.Lerp(.02f, .011f, i / (Sections - 1f));
                Shape("Whip core", joint, PrimitiveType.Cylinder, Vector3.up * length * .5f,
                    new Vector3(radius * 2, length * .5f, radius * 2), new Color(.12f, .10f, .09f));
                var winding = Group("Rope light winding", joint, Vector3.zero).gameObject.AddComponent<LineRenderer>();
                winding.useWorldSpace = false;
                winding.widthMultiplier = .022f;
                winding.numCapVertices = 2;
                winding.numCornerVertices = 2;
                winding.shadowCastingMode = ShadowCastingMode.Off;
                winding.receiveShadows = false;
                winding.sharedMaterial = Material(glow, 0, .45f, 1);
                int points = Mathf.CeilToInt(length / .11f) * 8;
                winding.positionCount = points + 1;
                for (int p = 0; p <= points; p++)
                {
                    float t = p / (float)points, turn = (i * length + t * length) / .11f * Mathf.PI * 2;
                    winding.SetPosition(p, new Vector3(Mathf.Cos(turn) * (radius + .007f), t * length,
                        Mathf.Sin(turn) * (radius + .007f)));
                }
                whip.rope[i] = winding;
            }
            // The pennant flies from the last section, its tall edge against the whip and its point trailing behind the car.
            var banner = new GameObject("Whip pennant", typeof(MeshFilter), typeof(MeshRenderer));
            banner.transform.SetParent(joint, false); banner.transform.localPosition = Vector3.up * (length - PennantHeight - .04f);
            int count = (Columns + 1) * (Rows + 1);
            whip.rest = new Vector3[count]; whip.waved = new Vector3[count];
            var triangles = new int[Columns * Rows * 6];
            for (int row = 0; row <= Rows; row++)
                for (int column = 0; column <= Columns; column++)
                {
                    // Each column is shorter than the last, closing on the middle of the tall edge.
                    float u = column / (float)Columns, v = row / (float)Rows;
                    whip.rest[row * (Columns + 1) + column] = new Vector3(0, PennantHeight * (.5f + (v - .5f) * (1 - u)), -u * PennantLength);
                }
            for (int row = 0, t = 0; row < Rows; row++)
                for (int column = 0; column < Columns; column++)
                {
                    int a = row * (Columns + 1) + column, b = a + 1, c = a + Columns + 1, d = c + 1;
                    triangles[t++] = a; triangles[t++] = c; triangles[t++] = b; triangles[t++] = b; triangles[t++] = c; triangles[t++] = d;
                }
            whip.rest.CopyTo(whip.waved, 0);
            whip.cloth = new Mesh { name = "Whip pennant", vertices = whip.rest, triangles = triangles };
            whip.cloth.MarkDynamic(); whip.cloth.RecalculateNormals(); whip.cloth.RecalculateBounds();
            banner.GetComponent<MeshFilter>().sharedMesh = whip.cloth;
            banner.GetComponent<MeshRenderer>().sharedMaterial = Cloth(pennant);
            if (Application.isPlaying) banner.AddComponent<GeneratedMeshOwner>().Mesh = whip.cloth;
            whip.lamp = Lamp("Pennant lamp", joint, new Vector3(.2f, length - PennantHeight * .5f, -PennantLength * .5f), glow, 0, 4);
            whip.lamp.enabled = false;
            whip.UpdateRopeGlow(0);
            return whip;
        }

        void UpdateRopeGlow(float dusk)
        {
            float intensity = Mathf.Lerp(.15f, 6f, dusk);
            if (Mathf.Abs(intensity - ropeIntensity) < .015f) return;
            ropeIntensity = intensity;
            ropeBlock ??= new MaterialPropertyBlock();
            ropeBlock.SetColor("_EmissionColor", ropeColour * intensity);
            for (int i = 0; i < rope.Length; i++) if (rope[i]) rope[i].SetPropertyBlock(ropeBlock);
        }

        /// <summary>Pennant cloth: seen from both sides, and faintly lit from within so it still reads at dusk.</summary>
        static Material Cloth(Color colour)
        {
            if (Cloths.TryGetValue(colour, out var cloth) && cloth) return cloth;
            cloth = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) { name = "Pennant cloth " + ColorUtility.ToHtmlStringRGB(colour) };
            cloth.color = colour; cloth.SetColor("_BaseColor", colour);
            cloth.SetFloat("_Smoothness", .1f); cloth.SetFloat("_Cull", (float)CullMode.Off);
            cloth.SetColor("_EmissionColor", colour * .35f); cloth.EnableKeyword("_EMISSION");
            Cloths[colour] = cloth;
            return cloth;
        }

        /// <summary>
        /// One step of the whip. Its tip is a weight on a springy rod, followed through the world rather than fixed to
        /// the car: it is left behind when the car pulls away, runs on when it brakes and swings to the outside of a
        /// turn, and it wobbles for a few beats after each. The wind of the car's passage holds it downwind throughout.
        /// </summary>
        public void Sway(float dt)
        {
            if (dt <= 0) return;
            Vector3 foot = transform.position, upright = foot + transform.up * Height, moved = foot - lastFoot;
            lastFoot = foot;
            // Set down rather than driven there (a spawn, a tow home): it arrives standing still.
            if (!standing || moved.sqrMagnitude > Placed * Placed) { standing = true; moved = drift = tipVelocity = Vector3.zero; tip = upright; }
            // Physics moves the car in steps; the whip is slow enough to want the speed between them. A long frame is
            // still measured whole for that speed, then taken as a short one so the spring keeps its footing.
            drift = Vector3.Lerp(drift, moved / dt, 1 - Mathf.Exp(-dt * 12));
            dt = Mathf.Min(dt, .05f);
            // The tip is carried on before the spring pulls at it: the other way round it would be measured against a
            // foot one step ahead of it, and lean less the lower the frame rate.
            tip += tipVelocity * dt;
            tipVelocity += ((upright - drift * WindLag - tip) * Stiffness - (tipVelocity - drift) * Damping) * dt;
            Vector3 lag = Vector3.ClampMagnitude(tip - upright, Reach);
            tip = upright + lag;
            lag = transform.InverseTransformDirection(lag);
            Lean = Vector2.ClampMagnitude(new Vector2(lag.x, lag.z) * LeanPerMetre, MaxLean);
            // A breath of air keeps it from ever standing dead still. The lean is shared out so the whip curves, most near the tip.
            Vector2 lean = Lean + new Vector2(Mathf.Sin(clock * 1.7f), Mathf.Cos(clock * 2.3f) * .4f) * (1.1f + Speed * .05f);
            for (int i = 0; i < Sections; i++)
                if (sections[i]) sections[i].localRotation = Quaternion.Euler(lean.y * (i + 1) / 15f, 0, -lean.x * (i + 1) / 15f);
        }

        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, .05f);
            if (dt <= 0 || !cloth) return;
            clock += dt;
            Sway(Time.deltaTime);
            float wind = Mathf.Clamp01(Speed / 18);
            float ripple = Mathf.Lerp(.025f, .07f, wind), rate = Mathf.Lerp(3.2f, 14, wind), droop = Mathf.Lerp(.12f, .015f, wind);
            for (int i = 0; i < rest.Length; i++)
            {
                Vector3 p = rest[i];
                float u = -p.z / PennantLength;
                p.x = Mathf.Sin(u * 6 - clock * rate + p.y * 3) * ripple * u;
                p.y -= droop * u * u;
                waved[i] = p;
            }
            cloth.vertices = waved; cloth.RecalculateNormals(); cloth.RecalculateBounds();
            if (lamp)
            {
                float dusk = Mathf.InverseLerp(.35f, .85f, TimeOfDay.Blend);
                UpdateRopeGlow(dusk);
                if (lamp.enabled != dusk > 0) lamp.enabled = dusk > 0;
                if (dusk > 0) lamp.intensity = 3 * dusk;
            }
        }
    }
}
