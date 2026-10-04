using System.Collections.Generic;
using UnityEngine;

namespace MadeInArizona
{
    /// <summary>
    /// Original low-poly Monument Valley formations, built as meshes from a seed: mesas and buttes (a talus slope of
    /// shale, a sheer fluted sandstone wall, a thin caprock ledge and a flat top), needle spires, mitten buttes (a butte
    /// with a spire thumb) and natural arches. Faces are flat-shaded. UV v is the strata coordinate the
    /// <see cref="StrataTexture"/> is painted in (0 talus foot, .3 wall foot, .9 caprock, 1 top), and u runs round the
    /// rock in metres / 24 so desert-varnish streaks keep a constant width.
    /// </summary>
    public sealed class MonumentMesh
    {
        // Ring of a loft: height and radius fractions, strata v, and how rough (jitter) and fluted the ring is.
        struct Ring { public float h, scale, v, jitter, flute; public Ring(float h, float scale, float v, float jitter, float flute) { this.h = h; this.scale = scale; this.v = v; this.jitter = jitter; this.flute = flute; } }

        // Organ Rock shale talus, a De Chelly sandstone wall, a caprock ledge with a slight overhang, and a flat top.
        static readonly Ring[] MesaRings = {
            new Ring(-.12f, 1.0f, 0, .04f, 0), new Ring(0, .985f, 0, .06f, 0), new Ring(.08f, .9f, .08f, .07f, 0), new Ring(.17f, .8f, .17f, .07f, .01f),
            new Ring(.26f, .71f, .27f, .05f, .02f), new Ring(.28f, .66f, .3f, .02f, .04f), new Ring(.55f, .64f, .58f, .015f, .045f),
            new Ring(.84f, .625f, .86f, .015f, .045f), new Ring(.86f, .64f, .89f, .01f, .02f), new Ring(.94f, .64f, .95f, .01f, .02f),
            new Ring(.955f, .62f, .97f, .01f, .01f), new Ring(1, .61f, 1, .01f, 0) };
        static readonly Ring[] ButteRings = {
            new Ring(-.1f, 1.0f, 0, .04f, 0), new Ring(0, .98f, 0, .07f, 0), new Ring(.12f, .82f, .1f, .08f, 0), new Ring(.24f, .66f, .2f, .07f, .01f),
            new Ring(.34f, .55f, .29f, .04f, .02f), new Ring(.36f, .5f, .31f, .02f, .05f), new Ring(.62f, .48f, .6f, .02f, .05f),
            new Ring(.88f, .45f, .87f, .02f, .05f), new Ring(.9f, .47f, .9f, .01f, .02f), new Ring(.96f, .46f, .96f, .01f, .02f), new Ring(1, .43f, 1, .01f, 0) };
        static readonly Ring[] SpireRings = {
            new Ring(-.08f, 1.0f, 0, .05f, 0), new Ring(0, .97f, 0, .08f, 0), new Ring(.14f, .64f, .15f, .09f, 0), new Ring(.26f, .42f, .27f, .07f, .01f),
            new Ring(.3f, .36f, .31f, .05f, .03f), new Ring(.55f, .34f, .55f, .07f, .04f), new Ring(.78f, .3f, .8f, .06f, .04f),
            new Ring(.92f, .24f, .92f, .05f, .02f), new Ring(.98f, .15f, .98f, .03f, 0), new Ring(1, .06f, 1, 0, 0) };

        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<int> triangles = new List<int>();
        readonly System.Random random;
        public MonumentMesh(int seed) { random = new System.Random(seed); }
        float Rand(float a, float b) => a + (b - a) * (float)random.NextDouble();

        public enum Formation { Mesa, Butte, Spire }

        /// <summary>A lofted formation whose talus foot fills a footprint circle (centre, radius) exactly.</summary>
        public void Loft(Formation formation, Vector3 center, float radius, float height, float elongation = 0)
        {
            var rings = formation == Formation.Mesa ? MesaRings : formation == Formation.Butte ? ButteRings : SpireRings;
            int sides = formation == Formation.Mesa ? 34 : formation == Formation.Butte ? 24 : 14;
            // Outline: an oval with a few lumps, scaled so its widest point is the footprint radius.
            float axis = Rand(0, Mathf.PI), a = Rand(0, 6.3f), b = Rand(0, 6.3f), c = Rand(0, 6.3f);
            float lump = formation == Formation.Spire ? .08f : .1f;
            var outline = new float[sides]; float widest = 0;
            for (int j = 0; j < sides; j++)
            {
                float t = j * Mathf.PI * 2 / sides;
                outline[j] = 1 + elongation * Mathf.Cos(2 * (t - axis)) + lump * Mathf.Sin(3 * t + a) + lump * .6f * Mathf.Sin(5 * t + b) + lump * .4f * Mathf.Sin(7 * t + c);
                widest = Mathf.Max(widest, outline[j] * (1 + .08f));
            }
            for (int j = 0; j < sides; j++) outline[j] /= widest;
            // Vertical flutes: the same angular ripple on every wall ring, so grooves run down the cliff.
            float fa = Rand(0, 6.3f), fb = Rand(0, 6.3f); int fk = random.Next(9, 15), fl = random.Next(15, 23);
            var grid = new Vector3[rings.Length, sides]; var v = new float[rings.Length];
            for (int i = 0; i < rings.Length; i++)
            {
                v[i] = rings[i].v;
                float y = rings[i].h * height + (i > 0 && i < 5 ? Rand(-.4f, .4f) : 0);
                for (int j = 0; j < sides; j++)
                {
                    float t = j * Mathf.PI * 2 / sides;
                    float flute = rings[i].flute * (Mathf.Sin(fk * t + fa) * .6f + Mathf.Sin(fl * t + fb) * .4f);
                    float r = radius * rings[i].scale * outline[j] * (1 + Rand(-rings[i].jitter, rings[i].jitter) + flute);
                    grid[i, j] = center + new Vector3(Mathf.Cos(t) * r, y, Mathf.Sin(t) * r);
                }
            }
            for (int i = 0; i < rings.Length - 1; i++)
                for (int j = 0; j < sides; j++)
                {
                    int k = (j + 1) % sides;
                    Vector3 p00 = grid[i, j], p01 = grid[i, k], p10 = grid[i + 1, j], p11 = grid[i + 1, k];
                    float u0 = j * radius * Mathf.PI * 2 / sides / 24, u1 = (j + 1) * radius * Mathf.PI * 2 / sides / 24;
                    Vector3 outward = new Vector3(p00.x - center.x, 0, p00.z - center.z);
                    Quad(p00, p01, p11, p10, new Vector2(u0, v[i]), new Vector2(u1, v[i]), new Vector2(u1, v[i + 1]), new Vector2(u0, v[i + 1]), outward);
                }
            // Flat top with a faint swell, so the caprock reads as a table.
            int top = rings.Length - 1;
            Vector3 middle = Vector3.zero; for (int j = 0; j < sides; j++) middle += grid[top, j]; middle /= sides;
            middle.y += formation == Formation.Spire ? height * .02f : Rand(.1f, .5f);
            for (int j = 0; j < sides; j++)
            {
                int k = (j + 1) % sides;
                Tri(grid[top, j], grid[top, k], middle, new Vector2(0, 1), new Vector2(.05f, 1), new Vector2(.02f, 1), Vector3.up);
            }
        }

        /// <summary>
        /// A natural arch spanning local x (legs at ±(inner + leg/2)), standing on y = 0, depth along z; a car drives
        /// through the opening (inner half-width × inner height).
        /// </summary>
        public void Arch(Vector3 center, Quaternion turn, float inner, float innerHeight, float leg, float lintel, float depth)
        {
            const int K = 26;
            float outer = inner + leg, outerHeight = innerHeight + lintel;
            float bumpA = Rand(0, 6.3f), bumpB = Rand(0, 6.3f);
            var outerCurve = new Vector3[K + 1]; var innerCurve = new Vector3[K + 1]; var halfDepth = new float[K + 1];
            for (int s = 0; s <= K; s++)
            {
                float t = s / (float)K, arc = Mathf.Sin(t * Mathf.PI);
                float bump = 1 + .05f * Mathf.Sin(t * 13 + bumpA) + .03f * Mathf.Sin(t * 29 + bumpB);
                // Squarer outer shoulders, a smooth inner opening; the legs flare a little where they meet the ground.
                float flare = 1 + .12f * Mathf.Pow(1 - arc, 3);
                outerCurve[s] = new Vector3(Mathf.Cos(t * Mathf.PI) * outer * flare * bump, outerHeight * Mathf.Pow(arc, .42f) * bump, 0);
                innerCurve[s] = new Vector3(Mathf.Cos(t * Mathf.PI) * inner / flare, innerHeight * arc, 0);
                halfDepth[s] = depth * .5f * (1 - .22f * arc) * (1 + Rand(-.06f, .06f));
            }
            // Bury the feet so uneven ground never shows a gap.
            outerCurve[0].y = innerCurve[0].y = outerCurve[K].y = innerCurve[K].y = -2.5f;
            Vector3 P(Vector3 local, float z) => center + turn * new Vector3(local.x, local.y, z);
            Vector3 Dir(Vector3 local) => turn * local;
            for (int s = 0; s < K; s++)
            {
                float u0 = s * .35f, u1 = (s + 1) * .35f;
                float va = Mathf.Lerp(.3f, .97f, Mathf.Clamp01(outerCurve[s].y / outerHeight)), vb = Mathf.Lerp(.3f, .97f, Mathf.Clamp01(outerCurve[s + 1].y / outerHeight));
                float ia = Mathf.Lerp(.3f, .97f, Mathf.Clamp01(innerCurve[s].y / outerHeight)), ib = Mathf.Lerp(.3f, .97f, Mathf.Clamp01(innerCurve[s + 1].y / outerHeight));
                float d0 = halfDepth[s], d1 = halfDepth[s + 1];
                // Front and back faces (between the inner and outer curves).
                Quad(P(innerCurve[s], d0), P(outerCurve[s], d0), P(outerCurve[s + 1], d1), P(innerCurve[s + 1], d1),
                    new Vector2(u0, ia), new Vector2(u0, va), new Vector2(u1, vb), new Vector2(u1, ib), Dir(Vector3.forward));
                Quad(P(innerCurve[s], -d0), P(outerCurve[s], -d0), P(outerCurve[s + 1], -d1), P(innerCurve[s + 1], -d1),
                    new Vector2(u0, ia), new Vector2(u0, va), new Vector2(u1, vb), new Vector2(u1, ib), Dir(Vector3.back));
                // Outer skin (facing away from the opening) and the underside of the opening.
                Vector3 outward = new Vector3(outerCurve[s].x + outerCurve[s + 1].x, outerCurve[s].y + outerCurve[s + 1].y + 1, 0);
                Quad(P(outerCurve[s], d0), P(outerCurve[s], -d0), P(outerCurve[s + 1], -d1), P(outerCurve[s + 1], d1),
                    new Vector2(u0, va), new Vector2(u0 + .3f, va), new Vector2(u1 + .3f, vb), new Vector2(u1, vb), Dir(outward));
                Vector3 inward = -new Vector3(innerCurve[s].x + innerCurve[s + 1].x, innerCurve[s].y + innerCurve[s + 1].y + .01f, 0);
                Quad(P(innerCurve[s], d0), P(innerCurve[s], -d0), P(innerCurve[s + 1], -d1), P(innerCurve[s + 1], d1),
                    new Vector2(u0, ia), new Vector2(u0 + .3f, ia), new Vector2(u1 + .3f, ib), new Vector2(u1, ib), Dir(inward));
            }
        }

        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, Vector3 facing)
        {
            Tri(a, b, c, ua, ub, uc, facing);
            Tri(a, c, d, ua, uc, ud, facing);
        }

        /// <summary>Adds a triangle wound so its face normal points along <paramref name="facing"/> (flat shaded: own vertices).</summary>
        void Tri(Vector3 a, Vector3 b, Vector3 c, Vector2 ua, Vector2 ub, Vector2 uc, Vector3 facing)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-8f) return;
            if (Vector3.Dot(n, facing) < 0) { var t = b; b = c; c = t; var tu = ub; ub = uc; uc = tu; }
            int i = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c); uvs.Add(ua); uvs.Add(ub); uvs.Add(uc);
            triangles.Add(i); triangles.Add(i + 1); triangles.Add(i + 2);
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            if (vertices.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices); mesh.SetUVs(0, uvs); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        static Texture2D strata;
        /// <summary>
        /// Monument Valley layering by strata v (rows) with desert-varnish streaks hanging from the caprock (columns):
        /// banded red-brown shale talus, an orange-red sandstone wall with faint cross-bedding, a darker contact and a pale
        /// caprock. Generated once.
        /// </summary>
        public static Texture2D StrataTexture
        {
            get
            {
                if (strata) return strata;
                const int width = 128, rows = 256;
                strata = new Texture2D(width, rows, TextureFormat.RGBA32, true) { name = "Monument strata", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
                var pixels = new Color[width * rows];
                var shaleDark = new Color(.47f, .2f, .13f); var shaleLight = new Color(.62f, .31f, .19f);
                var wallLow = new Color(.74f, .36f, .19f); var wallHigh = new Color(.82f, .45f, .25f);
                var contact = new Color(.42f, .24f, .17f); var cap = new Color(.7f, .58f, .47f); var varnish = new Color(.3f, .16f, .13f);
                for (int y = 0; y < rows; y++)
                {
                    float v = y / (rows - 1f);
                    Color band;
                    if (v < .3f) band = Color.Lerp(shaleDark, shaleLight, .5f + .5f * Mathf.Sin(v * 140) * Mathf.Sin(v * 37 + 1));
                    else if (v < .87f) band = Color.Lerp(wallLow, wallHigh, Mathf.InverseLerp(.3f, .87f, v)) * (1 + .05f * Mathf.Sin(v * 90));
                    else if (v < .91f) band = contact;
                    else band = Color.Lerp(cap, cap * .9f, Mathf.Sin(v * 200) * .5f + .5f);
                    for (int x = 0; x < width; x++)
                    {
                        float u = x / (float)width * Mathf.PI * 2;
                        // Varnish: dark streaks of varying width and length running down the wall from the caprock.
                        float streak = Mathf.Max(0, Mathf.Sin(u * 7 + Mathf.Sin(u * 3) * 2) * .6f + Mathf.Sin(u * 17 + 1.3f) * .4f);
                        float length = .87f - (.2f + .35f * (Mathf.Sin(u * 5 + 2) * .5f + .5f));
                        float onWall = v > .3f && v < .87f ? Mathf.SmoothStep(0, 1, Mathf.InverseLerp(length, .87f, v)) : 0;
                        pixels[y * width + x] = Color.Lerp(band, varnish, Mathf.Clamp01(streak * streak * onWall * .8f));
                    }
                }
                strata.SetPixels(pixels); strata.Apply(true, true);
                return strata;
            }
        }
    }
}
