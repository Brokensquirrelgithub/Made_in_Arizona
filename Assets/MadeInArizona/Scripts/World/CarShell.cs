using System.Collections.Generic;
using UnityEngine;
using static MadeInArizona.WorldArt;

namespace MadeInArizona
{
    /// <summary>
    /// A low-poly car body lofted from cross-sections. A body is described the way one is drawn: a row of stations from
    /// nose to tail, each giving the beltline, the height of the hood, roof or deck, and the widths there. The shell is
    /// skinned between them, which gives a tapered nose and tail, raked glass with painted pillars, tumblehome, wheel
    /// arches with wells behind them, and an open tub for a roadster's cockpit or a pickup's bed. Faces are grouped by
    /// material, one mesh each, so a body is a handful of renderers.
    /// </summary>
    sealed class CarShell
    {
        /// <summary>The skin over the top of the body from one station back to the next.</summary>
        public enum Top { Paint, Roof, Glass, Tub }
        /// <summary>The skin between the beltline and the edge of the top from one station back to the next.</summary>
        public enum Side { Paint, Glass }

        struct Station { public float z, low, belt, crown, half, edge, drop, floor; public Top over; public Side flank; public bool pillar; }
        struct Arch { public float z, y, radius, cap; }
        sealed class Part { public string name; public readonly List<Vector3> vertices = new List<Vector3>(); public readonly List<int> triangles = new List<int>(); }

        // A cross-section's points on the right-hand side, from the middle of the floor round the outside to the middle
        // of the roof. Between wheels the three well points sit on the sill; in an arch they step in and up round the wheel.
        const int Floor = 0, WellFloor = 1, WellTop = 2, Sill = 3, Crease = 4, Shoulder = 5, Edge = 6, Flat = 7, Crown = 8;
        const float ArchDrop = .012f;
        /// <summary>Straight sides to a wheel arch: six reads as round, four as a squared truck arch.</summary>
        public int archSides = 6;

        public Material paint, lower, roof, frame, glass, dark, tubWall, tubFloor;
        /// <summary>Half-width at the widest line of the body side, and the height of the sills.</summary>
        public float half = .8f, rocker = .2f;
        /// <summary>How far the edge of the hood or roof sits below its middle, and how far the sills and shoulders tuck in.</summary>
        public float drop = .035f, tuck = .04f, shoulderTuck = .03f;
        /// <summary>Where the widest line runs between sill (0) and beltline (1); a two-tone body divides here.</summary>
        public float crease = .5f;
        /// <summary>Share of the top that is flat across the middle; the rest falls away to the edges.</summary>
        public float flat = .62f;
        public float wellDepth = .3f;
        public float beltFrame = .025f, railFrame = .05f, pillarFrame = .04f, screenFrame = .045f, headerFrame = .04f;

        readonly List<Station> stations = new List<Station>();
        readonly List<Arch> arches = new List<Arch>();
        readonly Dictionary<Material, Part> parts = new Dictionary<Material, Part>();
        readonly List<Material> order = new List<Material>();

        /// <summary>
        /// Adds a station. Stations run from the nose (largest z) to the tail. <paramref name="belt"/> is the height of
        /// the shoulder, <paramref name="crown"/> the height of the top on the centre line and <paramref name="edge"/>
        /// the half-width of the top's edge. <paramref name="over"/> and <paramref name="flank"/> describe the skin from
        /// this station back to the next; in a tub the top is the rail and <paramref name="floor"/> the tub's floor.
        /// </summary>
        public CarShell At(float z, float belt, float crown, float edge, Top over = Top.Paint, Side flank = Side.Paint, bool pillar = false,
            float half = -1, float low = float.NaN, float drop = -1, float floor = 0)
        {
            stations.Add(new Station { z = z, belt = belt, crown = crown, edge = edge, over = over, flank = flank, pillar = pillar,
                half = half < 0 ? this.half : half, low = float.IsNaN(low) ? rocker : low, drop = drop < 0 ? this.drop : drop, floor = floor });
            return this;
        }

        /// <summary>Cuts a wheel arch centred on (<paramref name="y"/>, <paramref name="z"/>); a cap flattens its top (fender skirts).</summary>
        public void Wheel(float z, float y, float radius, float cap = float.MaxValue) => arches.Add(new Arch { z = z, y = y, radius = radius, cap = cap });

        public float NoseZ => stations[0].z;
        public float TailZ => stations[stations.Count - 1].z;

        static Vector3 Mirror(Vector3 p) => new Vector3(-p.x, p.y, p.z);

        Part PartFor(Material material)
        {
            if (!parts.TryGetValue(material, out var part))
            {
                part = new Part { name = material == paint ? "paint" : material == glass ? "glass" : material == lower ? "lower body" : material == roof ? "roof"
                    : material == frame ? "window frames" : material == dark ? "underside" : material == tubWall ? "tub" : material == tubFloor ? "tub floor" : material.name };
                parts[material] = part; order.Add(material);
            }
            return part;
        }

        /// <summary>A quad seen from outside as a, b, c, d clockwise. Its triangles share corners, so a twisted quad shades as one facet.</summary>
        public void Quad(Material material, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            bool first = Vector3.Cross(b - a, c - a).sqrMagnitude > 1e-9f, second = Vector3.Cross(c - a, d - a).sqrMagnitude > 1e-9f;
            if (!first && !second) return;
            var part = PartFor(material);
            int n = part.vertices.Count;
            if (first && second)
            {
                part.vertices.Add(a); part.vertices.Add(b); part.vertices.Add(c); part.vertices.Add(d);
                part.triangles.Add(n); part.triangles.Add(n + 1); part.triangles.Add(n + 2); part.triangles.Add(n); part.triangles.Add(n + 2); part.triangles.Add(n + 3);
            }
            else
            {
                part.vertices.Add(a); part.vertices.Add(first ? b : c); part.vertices.Add(first ? c : d);
                part.triangles.Add(n); part.triangles.Add(n + 1); part.triangles.Add(n + 2);
            }
        }
        /// <summary>A quad on the right-hand side and its reflection on the left.</summary>
        public void Both(Material material, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            Quad(material, a, b, c, d);
            Quad(material, Mirror(a), Mirror(d), Mirror(c), Mirror(b));
        }
        /// <summary>A quad turned to face <paramref name="toward"/>, and its reflection.</summary>
        public void Facing(Material material, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 toward)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a) + Vector3.Cross(c - a, d - a), toward) >= 0) Both(material, a, b, c, d);
            else Both(material, a, d, c, b);
        }

        /// <summary>
        /// A window: glass set in a painted frame. The margins are in metres along a-b (front, rear) and across it
        /// (lower, upper); a zero margin runs the glass to that edge, where the next pane carries on.
        /// </summary>
        void Glazed(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float front, float rear, float lower, float upper)
        {
            float along = (Vector3.Distance(a, b) + Vector3.Distance(d, c)) * .5f, across = (Vector3.Distance(a, d) + Vector3.Distance(b, c)) * .5f;
            if (along < .01f || across < .01f) { Both(frame, a, b, c, d); return; }
            float u0 = Mathf.Min(front / along, .45f), u1 = 1 - Mathf.Min(rear / along, .45f), v0 = Mathf.Min(lower / across, .45f), v1 = 1 - Mathf.Min(upper / across, .45f);
            Vector3 P(float u, float v) => Vector3.Lerp(Vector3.Lerp(a, b, u), Vector3.Lerp(d, c, u), v);
            Vector3 ia = P(u0, v0), ib = P(u1, v0), ic = P(u1, v1), id = P(u0, v1);
            Both(frame, a, b, ib, ia); Both(frame, b, c, ic, ib); Both(frame, c, d, id, ic); Both(frame, d, a, ia, id);
            Both(glass, ia, ib, ic, id);
        }

        /// <summary>A window that stands free of the skin (an open car's windscreen), turned to face <paramref name="toward"/>.</summary>
        public void Pane(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float front, float rear, float lower, float upper, Vector3 toward)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a) + Vector3.Cross(c - a, d - a), toward) >= 0) Glazed(a, b, c, d, front, rear, lower, upper);
            else Glazed(a, d, c, b, lower, upper, front, rear);
        }

        /// <summary>
        /// Corners of an arch from front to back, as (z, height). Over a sill that runs above the wheel centre the arch
        /// is the arc between the two points where it meets the sill. Over a lower sill it is the half circle above the
        /// centre with straight drops to the sill, as a real arch is cut, rather than curling back in under the wheel.
        /// </summary>
        List<Vector2> ArchOutline(Arch arch)
        {
            var corners = new List<Vector2>();
            float start = Mathf.Asin(Mathf.Clamp((rocker - arch.y) / arch.radius, -1, 1));
            bool drops = start < 0;
            if (drops) { start = 0; corners.Add(new Vector2(arch.z + arch.radius + ArchDrop, rocker)); }
            for (int k = 0; k <= archSides; k++)
            {
                float angle = Mathf.Lerp(start, Mathf.PI - start, k / (float)archSides);
                corners.Add(new Vector2(arch.z + Mathf.Cos(angle) * arch.radius, Mathf.Min(arch.y + Mathf.Sin(angle) * arch.radius, arch.cap)));
            }
            if (drops) corners.Add(new Vector2(arch.z - arch.radius - ArchDrop, rocker));
            return corners;
        }
        /// <summary>
        /// Height of the wheel opening at <paramref name="z"/>, read off the arch's outline so that every cross-section
        /// lies on it; below every sill where there is no arch.
        /// </summary>
        float ArchHeight(float z)
        {
            float best = float.MinValue;
            foreach (var arch in arches)
            {
                var corners = ArchOutline(arch);
                for (int corner = 0; corner < corners.Count - 1; corner++)
                {
                    Vector2 a = corners[corner], b = corners[corner + 1];
                    if (z > a.x + 1e-4f || z < b.x - 1e-4f || a.x - b.x < 1e-5f) continue;
                    best = Mathf.Max(best, Mathf.Lerp(a.y, b.y, Mathf.InverseLerp(a.x, b.x, z)));
                }
            }
            return best;
        }

        Vector3[] Ring(Station a, Station b, float z)
        {
            float t = Mathf.Abs(a.z - b.z) < 1e-5f ? 0 : Mathf.Clamp01((a.z - z) / (a.z - b.z));
            float low = Mathf.Lerp(a.low, b.low, t), belt = Mathf.Lerp(a.belt, b.belt, t), crown = Mathf.Lerp(a.crown, b.crown, t);
            float width = Mathf.Lerp(a.half, b.half, t), edge = Mathf.Lerp(a.edge, b.edge, t), fall = Mathf.Lerp(a.drop, b.drop, t);
            float arch = ArchHeight(z);
            bool well = arch > low + .001f;
            float sill = Mathf.Max(low, arch), sillX = width - tuck, wellX = well ? sillX - wellDepth : sillX;
            float creaseY = Mathf.Max(low + (belt - low) * crease, sill), edgeY = crown - fall;
            float shoulderY = Mathf.Min(Mathf.Max(belt, creaseY + .01f), edgeY - .008f);
            var ring = new Vector3[Crown + 1];
            ring[Floor] = new Vector3(0, low, z);
            ring[WellFloor] = new Vector3(wellX, low, z);
            ring[WellTop] = new Vector3(wellX, sill, z);
            ring[Sill] = new Vector3(sillX, sill, z);
            ring[Crease] = new Vector3(width, creaseY, z);
            ring[Shoulder] = new Vector3(width - shoulderTuck, shoulderY, z);
            ring[Edge] = new Vector3(edge, edgeY, z);
            if (a.over == Top.Tub)
            {
                // Open top: over the rail, down the inside wall and across the tub's floor.
                float tub = Mathf.Lerp(a.floor, b.floor, t);
                ring[Flat] = new Vector3(edge, tub, z); ring[Crown] = new Vector3(0, tub, z);
            }
            else { ring[Flat] = new Vector3(edge * flat, crown, z); ring[Crown] = new Vector3(0, crown, z); }
            return ring;
        }

        /// <summary>The cross-section at <paramref name="z"/>, for fitting trim to the skin.</summary>
        Vector3[] RingAt(float z)
        {
            for (int i = 0; i < stations.Count - 1; i++)
                if (z <= stations[i].z + 1e-4f && z >= stations[i + 1].z - 1e-4f) return Ring(stations[i], stations[i + 1], z);
            return z > stations[0].z ? Ring(stations[0], stations[1], stations[0].z) : Ring(stations[stations.Count - 2], stations[stations.Count - 1], TailZ);
        }
        /// <summary>Half-width of the body side at <paramref name="z"/>.</summary>
        public float HalfAt(float z) => RingAt(z)[Crease].x;
        /// <summary>Height of the top on the centre line at <paramref name="z"/> (the tub floor where the body is open).</summary>
        public float CrownAt(float z) => RingAt(z)[Crown].y;
        public float BeltAt(float z) => RingAt(z)[Shoulder].y;
        public float ShoulderAt(float z) => RingAt(z)[Shoulder].x;
        public float EdgeAt(float z) => RingAt(z)[Edge].x;

        /// <summary>Builds the skin from the stations. Call once, after every station and wheel is in.</summary>
        public void Skin()
        {
            Vector3[] previous = null; Top previousOver = Top.Paint;
            for (int i = 0; i < stations.Count - 1; i++)
            {
                Station a = stations[i], b = stations[i + 1];
                var cuts = new List<float> { a.z, b.z };
                foreach (var arch in arches)
                {
                    // An arch is a polygon; a cross-section at each of its corners keeps the opening crisp.
                    foreach (Vector2 corner in ArchOutline(arch))
                        if (corner.x < a.z - .004f && corner.x > b.z + .004f) cuts.Add(corner.x);
                }
                cuts.Sort((p, q) => q.CompareTo(p));
                var rings = new List<Vector3[]>();
                for (int k = 0; k < cuts.Count; k++) if (k == 0 || cuts[k - 1] - cuts[k] > .0005f || k == cuts.Count - 1) rings.Add(Ring(a, b, cuts[k]));
                if (previous != null) Span(previous, rings[0], previousOver == Top.Tub || a.over == Top.Tub ? Top.Tub : Top.Paint, Side.Paint, 0, 0, 0, 0);
                // Glass is framed where it starts and stops, and at pillars; panes that run on into the next meet edge to edge.
                bool screenStarts = i == 0 || stations[i - 1].over != Top.Glass, screenStops = i + 2 >= stations.Count || b.over != Top.Glass;
                bool windowStarts = a.pillar || i == 0 || stations[i - 1].flank != Side.Glass, windowStops = b.pillar || i + 2 >= stations.Count || b.flank != Side.Glass;
                for (int k = 0; k < rings.Count - 1; k++)
                {
                    bool first = k == 0, last = k == rings.Count - 2;
                    Span(rings[k], rings[k + 1], a.over, a.flank,
                        first && windowStarts ? pillarFrame : 0, last && windowStops ? pillarFrame : 0,
                        first && screenStarts ? headerFrame : 0, last && screenStops ? headerFrame : 0);
                }
                previous = rings[rings.Count - 1]; previousOver = a.over;
            }
            Cap(Ring(stations[0], stations[1], stations[0].z), true, stations[0].over == Top.Tub);
            Cap(previous, false, previousOver == Top.Tub);
        }

        void Strip(Material material, Vector3[] front, Vector3[] rear, int point) => Both(material, front[point], rear[point], rear[point + 1], front[point + 1]);

        void Span(Vector3[] front, Vector3[] rear, Top over, Side flank, float windowFront, float windowRear, float screenFront, float screenRear)
        {
            Strip(dark, front, rear, Floor); Strip(dark, front, rear, WellFloor); Strip(dark, front, rear, WellTop);
            Strip(lower, front, rear, Sill); Strip(paint, front, rear, Crease);
            if (flank == Side.Glass && over != Top.Tub)
                Glazed(front[Shoulder], rear[Shoulder], rear[Edge], front[Edge], windowFront, windowRear, beltFrame, railFrame);
            else Strip(paint, front, rear, Shoulder);
            switch (over)
            {
                case Top.Glass:
                    Glazed(front[Edge], rear[Edge], rear[Flat], front[Flat], screenFront, screenRear, screenFrame, 0);
                    Glazed(front[Flat], rear[Flat], rear[Crown], front[Crown], screenFront, screenRear, 0, 0);
                    break;
                case Top.Tub: Strip(tubWall, front, rear, Edge); Strip(tubFloor, front, rear, Flat); break;
                case Top.Roof: Strip(roof, front, rear, Edge); Strip(roof, front, rear, Flat); break;
                default: Strip(paint, front, rear, Edge); Strip(paint, front, rear, Flat); break;
            }
        }

        /// <summary>Closes an end of the body with level bands across it; the lowest takes the two-tone colour.</summary>
        void Cap(Vector3[] ring, bool nose, bool tub)
        {
            for (int point = Sill; point < Crown; point++)
            {
                Material material = point == Sill ? lower : tub && point >= Edge ? tubWall : paint;
                Vector3 a = Mirror(ring[point]), b = ring[point], c = ring[point + 1], d = Mirror(ring[point + 1]);
                if (nose) Quad(material, a, b, c, d); else Quad(material, b, a, d, c);
            }
        }

        /// <summary>
        /// A beam across the car with rounded ends, wrapped back along the sides: bumpers, spoilers, sills. It runs from
        /// <paramref name="tipZ"/> (its outer face, at half-width <paramref name="tipHalf"/>) to <paramref name="bodyZ"/>.
        /// </summary>
        public void Bar(Material material, float tipZ, float bodyZ, float y0, float y1, float tipHalf, float bodyHalf, float corner = .1f)
        {
            float chamfer = Mathf.Min(.03f, (y1 - y0) / 3), direction = Mathf.Sign(bodyZ - tipZ);
            Vector3[] Section(float z, float width) => new[] { new Vector3(0, y0, z), new Vector3(width - chamfer, y0, z), new Vector3(width, y0 + chamfer, z),
                new Vector3(width, y1 - chamfer, z), new Vector3(width - chamfer, y1, z), new Vector3(0, y1, z) };
            corner = Mathf.Min(corner, Mathf.Abs(bodyZ - tipZ) * .8f);
            var rings = new List<Vector3[]> { Section(tipZ, tipHalf), Section(tipZ + direction * corner, bodyHalf), Section(bodyZ, bodyHalf) };
            if (direction > 0) rings.Reverse();
            Loft(material, rings);
        }

        /// <summary>Skins a row of cross-sections (nose first, each from the bottom centre round to the top centre) in one material.</summary>
        public void Loft(Material material, List<Vector3[]> rings)
        {
            for (int i = 0; i < rings.Count - 1; i++)
                for (int point = 0; point < rings[i].Length - 1; point++) Strip(material, rings[i], rings[i + 1], point);
            Vector3[] nose = rings[0], tail = rings[rings.Count - 1];
            for (int point = 0; point < nose.Length - 1; point++)
            {
                Quad(material, Mirror(nose[point]), nose[point], nose[point + 1], Mirror(nose[point + 1]));
                Quad(material, tail[point], Mirror(tail[point]), Mirror(tail[point + 1]), tail[point + 1]);
            }
        }

        /// <summary>A raised lip round each wheel arch near <paramref name="z"/>: fender flares and cladding.</summary>
        public void Flare(Material material, float z, float lip, float proud)
        {
            foreach (var arch in arches)
            {
                if (Mathf.Abs(arch.z - z) > .05f) continue;
                var corners = ArchOutline(arch);
                Vector3 inner = default, outer = default;
                for (int k = 0; k < corners.Count; k++)
                {
                    // The lip stands off the arch's edge, away from the wheel.
                    Vector2 edge = corners[k], away = (edge - new Vector2(arch.z, arch.y)).normalized, rim = edge + away * lip;
                    Vector3 nextInner = new Vector3(HalfAt(Mathf.Clamp(edge.x, TailZ, NoseZ)), edge.y, edge.x);
                    Vector3 nextOuter = new Vector3(nextInner.x, rim.y, rim.x), out_ = Vector3.right * proud;
                    if (k > 0)
                    {
                        Vector3 mid = (inner + nextInner) * .5f, radial = new Vector3(0, mid.y - arch.y, mid.z - arch.z).normalized;
                        Facing(material, inner + out_, nextInner + out_, nextOuter + out_, outer + out_, Vector3.right);
                        Facing(material, outer + out_, nextOuter + out_, nextOuter - out_ * .5f, outer - out_ * .5f, radial);
                        Facing(material, inner - out_ * .5f, nextInner - out_ * .5f, nextInner + out_, inner + out_, -radial);
                    }
                    else
                    {
                        // Close the lip's lower ends.
                        Facing(material, nextInner - out_ * .5f, nextInner + out_, nextOuter + out_, nextOuter - out_ * .5f, Vector3.down);
                    }
                    if (k == corners.Count - 1) Facing(material, nextInner - out_ * .5f, nextInner + out_, nextOuter + out_, nextOuter - out_ * .5f, Vector3.down);
                    inner = nextInner; outer = nextOuter;
                }
            }
        }

        /// <summary>A dark hairline down the body side at <paramref name="z"/>, from the sill to the beltline: a door shut line.</summary>
        public void Seam(Material material, float z, float width = .014f)
        {
            Vector3[] ring = RingAt(z);
            Vector3 proud = Vector3.right * .004f, ahead = Vector3.forward * width * .5f;
            for (int point = Sill; point < Shoulder; point++)
            {
                Vector3 p = ring[point] + proud, q = ring[point + 1] + proud;
                Both(material, p + ahead, p - ahead, q - ahead, q + ahead);
            }
        }

        /// <summary>A strip of trim along the body side at <paramref name="height"/> (0 sill, 1 beltline) from <paramref name="z0"/> back to <paramref name="z1"/>.</summary>
        public void Strake(Material material, float z0, float z1, float height, float tall = .04f, float proud = .008f)
        {
            // A point on the skin, or nothing where the strip would cross a wheel opening.
            Vector3? At(float z, float lift)
            {
                Vector3[] ring = RingAt(z);
                Vector3 sill = ring[Sill], mid = ring[Crease], high = ring[Shoulder];
                float y = Mathf.Lerp(ring[Floor].y, high.y, height) + lift;
                if (y < sill.y - .001f) return null;
                // The side is two facets meeting at the crease: follow whichever the strip lies on.
                float x = y <= mid.y ? Mathf.Lerp(sill.x, mid.x, Mathf.InverseLerp(sill.y, mid.y, y)) : Mathf.Lerp(mid.x, high.x, Mathf.InverseLerp(mid.y, high.y, y));
                return new Vector3(x + proud, y, z);
            }
            int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(z0 - z1) / .1f));
            for (int i = 0; i < steps; i++)
            {
                float a = Mathf.Lerp(z0, z1, i / (float)steps), b = Mathf.Lerp(z0, z1, (i + 1) / (float)steps);
                Vector3? p = At(a, -tall * .5f), q = At(b, -tall * .5f), r = At(b, tall * .5f), s = At(a, tall * .5f);
                if (p.HasValue && q.HasValue && r.HasValue && s.HasValue) Both(material, p.Value, q.Value, r.Value, s.Value);
            }
        }

        /// <summary>Creates one mesh per material under <paramref name="parent"/>.</summary>
        public void Emit(Transform parent, string label)
        {
            foreach (var material in order)
            {
                var part = parts[material];
                if (part.triangles.Count == 0) continue;
                Use(MeshObject(label + " • " + part.name, parent, part.vertices.ToArray(), part.triangles.ToArray(), Color.white), material);
            }
        }
    }
}
