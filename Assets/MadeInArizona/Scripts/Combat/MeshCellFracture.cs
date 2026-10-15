using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MadeInArizona
{
    /// <summary>
    /// Cuts a prop's actual rendered triangles by the bisector planes of a small set of seeds.
    /// The resulting Voronoi cells fill the source mesh at their original positions; cut faces
    /// are capped so the pieces have solid interiors when physics pulls them apart.
    /// Built only when a prop breaks, with a strict input and cell budget.
    /// </summary>
    internal static class MeshCellFracture
    {
        internal sealed class Cell
        {
            public Mesh mesh;
            public Vector3 center;
            public Vector3 size;
        }

        struct Triangle
        {
            public Vector3 a, b, c;
            public Triangle(Vector3 a, Vector3 b, Vector3 c) { this.a = a; this.b = b; this.c = c; }
        }
        struct Segment
        {
            public Vector3 a, b;
            public Segment(Vector3 a, Vector3 b) { this.a = a; this.b = b; }
        }

        const int MaxInputTriangles = 4000;
        const float PlaneEpsilon = 0f;
        const float JoinDistanceSq = .0001f * .0001f;
        const float SegmentMinSq = .000001f * .000001f;

        internal static bool TryBuild(Transform source, int requestedCells, out List<Cell> cells)
        {
            cells = new List<Cell>();
            if (!source || requestedCells < 2) return false;
            var surface = new List<Triangle>(256);
            var bounds = new Bounds();
            bool haveBounds = false;
            foreach (var filter in source.GetComponentsInChildren<MeshFilter>())
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                // DestructionSystem hides the original renderers just before asking for cells.
                if (!renderer || !filter.gameObject.activeInHierarchy || !filter.sharedMesh) continue;
                var mesh = filter.sharedMesh;
                if (!mesh.isReadable) return false;
                var vertices = mesh.vertices;
                Matrix4x4 matrix = filter.transform.localToWorldMatrix;
                bool mirrored = matrix.determinant < 0;
                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                {
                    if (mesh.GetTopology(submesh) != MeshTopology.Triangles) continue;
                    var indices = mesh.GetTriangles(submesh);
                    if (surface.Count + indices.Length / 3 > MaxInputTriangles) return false;
                    for (int i = 0; i + 2 < indices.Length; i += 3)
                    {
                        Vector3 a = matrix.MultiplyPoint3x4(vertices[indices[i]]);
                        Vector3 b = matrix.MultiplyPoint3x4(vertices[indices[i + (mirrored ? 2 : 1)]]);
                        Vector3 c = matrix.MultiplyPoint3x4(vertices[indices[i + (mirrored ? 1 : 2)]]);
                        if (Vector3.Cross(b - a, c - a).sqrMagnitude < .00000001f) continue;
                        surface.Add(new Triangle(a, b, c));
                        if (!haveBounds) { bounds = new Bounds(a, Vector3.zero); haveBounds = true; }
                        bounds.Encapsulate(a); bounds.Encapsulate(b); bounds.Encapsulate(c);
                    }
                }
            }
            if (!haveBounds || surface.Count < 4 || bounds.size.sqrMagnitude < .01f) return false;

            int count = Mathf.Clamp(requestedCells, 2, 16);
            int seed = source.name.GetHashCode() ^ Mathf.RoundToInt(bounds.center.x * 101f) ^
                Mathf.RoundToInt(bounds.center.y * 37f) ^ Mathf.RoundToInt(bounds.center.z * 59f);
            var seeds = ChooseSeeds(surface, bounds, count, seed);
            for (int i = 0; i < seeds.Count; i++)
            {
                var clipped = new List<Triangle>(surface);
                for (int other = 0; other < seeds.Count && clipped.Count > 0; other++)
                {
                    if (other == i) continue;
                    Vector3 difference = seeds[other] - seeds[i];
                    if (difference.sqrMagnitude < .0001f) continue;
                    Vector3 normal = difference.normalized;
                    float distance = Vector3.Dot((seeds[other] + seeds[i]) * .5f, normal);
                    clipped = Clip(clipped, normal, distance);
                }
                if (clipped.Count < 4) continue;
                var cellBounds = BoundsOf(clipped);
                if (cellBounds.size.sqrMagnitude < .0025f) continue;
                Vector3 center = cellBounds.center;
                var positions = new Vector3[clipped.Count * 3];
                var indices = new int[positions.Length];
                for (int triangle = 0; triangle < clipped.Count; triangle++)
                {
                    int start = triangle * 3;
                    positions[start] = clipped[triangle].a - center;
                    positions[start + 1] = clipped[triangle].b - center;
                    positions[start + 2] = clipped[triangle].c - center;
                    indices[start] = start; indices[start + 1] = start + 1; indices[start + 2] = start + 2;
                }
                var mesh = new Mesh { name = "Voronoi fracture cell " + i };
                if (positions.Length > 65535) mesh.indexFormat = IndexFormat.UInt32;
                mesh.vertices = positions; mesh.triangles = indices;
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
                cells.Add(new Cell { mesh = mesh, center = center, size = cellBounds.size });
            }
            if (cells.Count >= 2) return true;
            foreach (var cell in cells) UnityEngine.Object.Destroy(cell.mesh);
            cells.Clear();
            return false;
        }

        static List<Vector3> ChooseSeeds(List<Triangle> surface, Bounds bounds, int count, int identity)
        {
            var random = new System.Random(identity ^ 0x5f3759df);
            var areas = new float[surface.Count];
            float totalArea = 0;
            for (int i = 0; i < surface.Count; i++)
            {
                totalArea += Vector3.Cross(surface[i].b - surface[i].a, surface[i].c - surface[i].a).magnitude;
                areas[i] = totalArea;
            }
            var candidates = new List<Vector3>(Mathf.Max(64, count * 12));
            int samples = Mathf.Max(64, count * 12);
            for (int i = 0; i < samples; i++)
            {
                float pick = (float)random.NextDouble() * totalArea;
                int index = Array.BinarySearch(areas, pick);
                if (index < 0) index = ~index;
                var tri = surface[Mathf.Clamp(index, 0, surface.Count - 1)];
                float u = Mathf.Sqrt((float)random.NextDouble()), v = (float)random.NextDouble();
                candidates.Add(tri.a * (1 - u) + tri.b * (u * (1 - v)) + tri.c * (u * v));
            }
            var seeds = new List<Vector3>(count);
            Vector3 metric = new Vector3(
                Mathf.Max(bounds.size.x, bounds.size.magnitude * .18f),
                Mathf.Max(bounds.size.y, bounds.size.magnitude * .18f),
                Mathf.Max(bounds.size.z, bounds.size.magnitude * .18f));
            for (int i = 0; i < count; i++)
            {
                float best = float.NegativeInfinity;
                Vector3 choice = candidates[0];
                foreach (Vector3 candidate in candidates)
                {
                    float separation = float.MaxValue;
                    if (seeds.Count == 0)
                    {
                        // Begin near the centre so the first cell is not a tiny corner sliver.
                        separation = -Vector3.Scale(candidate - bounds.center,
                            new Vector3(1 / metric.x, 1 / metric.y, 1 / metric.z)).sqrMagnitude;
                    }
                    else foreach (Vector3 seed in seeds)
                    {
                        Vector3 delta = candidate - seed;
                        delta = new Vector3(delta.x / metric.x, delta.y / metric.y, delta.z / metric.z);
                        separation = Mathf.Min(separation, delta.sqrMagnitude);
                    }
                    if (separation <= best) continue;
                    best = separation; choice = candidate;
                }
                seeds.Add(choice);
            }
            return seeds;
        }

        static List<Triangle> Clip(List<Triangle> input, Vector3 normal, float distance)
        {
            var output = new List<Triangle>(input.Count);
            var segments = new List<Segment>();
            var polygon = new Vector3[4];
            var vertices = new Vector3[3];
            foreach (var triangle in input)
            {
                vertices[0] = triangle.a; vertices[1] = triangle.b; vertices[2] = triangle.c;
                int used = 0, crossings = 0;
                Vector3 firstCross = Vector3.zero, secondCross = Vector3.zero;
                for (int edge = 0; edge < 3; edge++)
                {
                    Vector3 a = vertices[edge], b = vertices[(edge + 1) % 3];
                    float da = Vector3.Dot(a, normal) - distance;
                    float db = Vector3.Dot(b, normal) - distance;
                    bool aInside = da <= PlaneEpsilon, bInside = db <= PlaneEpsilon;
                    if (aInside) polygon[used++] = a;
                    if (aInside == bInside) continue;
                    Vector3 intersection = Vector3.Lerp(a, b, da / (da - db));
                    polygon[used++] = intersection;
                    if (crossings++ == 0) firstCross = intersection; else secondCross = intersection;
                }
                for (int i = 1; i + 1 < used; i++)
                    AddIfArea(output, polygon[0], polygon[i], polygon[i + 1]);
                if (crossings == 2 && (firstCross - secondCross).sqrMagnitude > SegmentMinSq)
                    segments.Add(new Segment(firstCross, secondCross));
            }
            CapOpenCuts(output, segments, normal);
            return output;
        }

        static void CapOpenCuts(List<Triangle> output, List<Segment> segments, Vector3 normal)
        {
            if (segments.Count < 3) return;
            var used = new bool[segments.Count];
            Vector3 axis = Mathf.Abs(normal.y) < .9f ? Vector3.up : Vector3.right;
            Vector3 u = Vector3.Cross(axis, normal).normalized;
            Vector3 v = Vector3.Cross(normal, u);
            for (int start = 0; start < segments.Count; start++)
            {
                if (used[start]) continue;
                used[start] = true;
                var loop = new List<Vector3> { segments[start].a, segments[start].b };
                Vector3 current = segments[start].b;
                bool closed = false;
                for (int step = 0; step < segments.Count; step++)
                {
                    if ((current - loop[0]).sqrMagnitude <= JoinDistanceSq) { closed = true; break; }
                    int next = -1;
                    Vector3 nextPoint = Vector3.zero;
                    for (int s = 0; s < segments.Count; s++)
                    {
                        if (used[s]) continue;
                        if ((segments[s].a - current).sqrMagnitude <= JoinDistanceSq)
                        { next = s; nextPoint = segments[s].b; break; }
                        if ((segments[s].b - current).sqrMagnitude <= JoinDistanceSq)
                        { next = s; nextPoint = segments[s].a; break; }
                    }
                    if (next < 0) break;
                    used[next] = true;
                    if ((nextPoint - loop[0]).sqrMagnitude <= JoinDistanceSq) { closed = true; break; }
                    loop.Add(nextPoint); current = nextPoint;
                }
                if (!closed || loop.Count < 3) continue;
                TriangulateCap(output, loop, u, v);
            }
        }

        static void TriangulateCap(List<Triangle> output, List<Vector3> loop, Vector3 u, Vector3 v)
        {
            var projected = new Vector2[loop.Count];
            float area = 0;
            for (int i = 0; i < loop.Count; i++)
            {
                projected[i] = new Vector2(Vector3.Dot(loop[i], u), Vector3.Dot(loop[i], v));
                Vector3 next = loop[(i + 1) % loop.Count];
                area += projected[i].x * Vector3.Dot(next, v) - Vector3.Dot(next, u) * projected[i].y;
            }
            if (Mathf.Abs(area) < .000001f) return;
            if (area < 0) { loop.Reverse(); Array.Reverse(projected); }
            var remaining = new List<int>(loop.Count);
            for (int i = 0; i < loop.Count; i++) remaining.Add(i);
            for (int guard = 0; remaining.Count > 3 && guard < loop.Count * loop.Count; guard++)
            {
                bool found = false;
                for (int i = 0; i < remaining.Count; i++)
                {
                    int previous = remaining[(i + remaining.Count - 1) % remaining.Count];
                    int current = remaining[i], next = remaining[(i + 1) % remaining.Count];
                    Vector2 a = projected[previous], b = projected[current], c = projected[next];
                    if (Cross(b - a, c - b) <= .00000001f) continue;
                    bool contains = false;
                    foreach (int point in remaining)
                        if (point != previous && point != current && point != next && InTriangle(projected[point], a, b, c))
                        { contains = true; break; }
                    if (contains) continue;
                    AddIfArea(output, loop[previous], loop[current], loop[next]);
                    remaining.RemoveAt(i); found = true; break;
                }
                if (!found)
                {
                    // A nearly collinear point can defeat ear clipping while contributing no area.
                    int remove = -1; float smallest = float.MaxValue;
                    for (int i = 0; i < remaining.Count; i++)
                    {
                        Vector2 a = projected[remaining[(i + remaining.Count - 1) % remaining.Count]];
                        Vector2 b = projected[remaining[i]];
                        Vector2 c = projected[remaining[(i + 1) % remaining.Count]];
                        float turn = Mathf.Abs(Cross(b - a, c - b));
                        if (turn < smallest) { smallest = turn; remove = i; }
                    }
                    if (smallest < .000001f && remove >= 0) remaining.RemoveAt(remove);
                    else break;
                }
            }
            if (remaining.Count == 3)
                AddIfArea(output, loop[remaining[0]], loop[remaining[1]], loop[remaining[2]]);
        }

        static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c) =>
            Cross(b - a, p - a) >= -.00000001f && Cross(c - b, p - b) >= -.00000001f && Cross(a - c, p - c) >= -.00000001f;
        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        static void AddIfArea(List<Triangle> output, Vector3 a, Vector3 b, Vector3 c)
        {
            if (Vector3.Cross(b - a, c - a).sqrMagnitude > .000000000001f) output.Add(new Triangle(a, b, c));
        }
        static Bounds BoundsOf(List<Triangle> surface)
        {
            var bounds = new Bounds(surface[0].a, Vector3.zero);
            foreach (var triangle in surface)
            { bounds.Encapsulate(triangle.a); bounds.Encapsulate(triangle.b); bounds.Encapsulate(triangle.c); }
            return bounds;
        }
    }
}
