using System.Collections.Generic;
using UnityEngine;

namespace MadeInArizona
{
    /// <summary>
    /// Road-aware A* over the terrain heightfield for AI vehicles that travel between map locations. Steps whose
    /// grade exceeds the drivable limit (cliff walls between elevation levels) are blocked outright; paved roads
    /// are cheapest, then shoulders, dirt trails and open country, so routes follow the road network whenever one
    /// goes the right way and only cut across country when it genuinely saves distance.
    /// </summary>
    public sealed partial class GeneratedWorld
    {
        const int NavStride = 2; // every second heightfield vertex: ~8 m cells on the default 1.6 km map
        const float RoadCost = 1, ShoulderCost = 1.8f, TrailCost = 2.6f, OpenCost = 5, WaterCost = 9;
        int navSize; float navStep;
        float[] navCost, navHeight;
        float[] navG; int[] navParent; bool[] navClosed; int[] navStamp; int navSearch;
        readonly List<int> navHeap = new List<int>();
        readonly List<float> navHeapF = new List<float>();
        readonly List<int> navNodes = new List<int>();

        void BuildNavigation()
        {
            if (navCost != null || heights == null) return;
            int grid = Chunks * Cells; float step = size / grid;
            navSize = grid / NavStride + 1; navStep = step * NavStride;
            int count = navSize * navSize;
            navCost = new float[count]; navHeight = new float[count];
            navG = new float[count]; navParent = new int[count]; navClosed = new bool[count]; navStamp = new int[count];
            for (int z = 0; z < navSize; z++)
                for (int x = 0; x < navSize; x++)
                {
                    int gx = Mathf.Min(grid, x * NavStride), gz = Mathf.Min(grid, z * NavStride), i = z * navSize + x;
                    navHeight[i] = heights[gx, gz];
                    if (!InsideVertex(gx, gz, step)) { navCost[i] = -1; continue; }
                    Vector2 p = new Vector2(-half + gx * step, -half + gz * step);
                    float road = RoadDistance(p), cost;
                    if (road < 6.5f || TownDistance(p) < 30) cost = RoadCost;
                    else if (road < 11) cost = ShoulderCost;
                    else if (TrailEdgeDistance(p) < 0) cost = TrailCost;
                    else cost = OpenCost;
                    if (cost > RoadCost && riverWidth > 0 && Mathf.Abs(p.x - RiverX(p.y)) < riverWidth) cost = WaterCost;
                    // Landforms and cover rocks are solid; the margin keeps routes from clipping their edges. Landforms also
                    // block half a cell round them, so one lying between nodes on a coarse (large-map) grid is still avoided.
                    if (ObstacleDistance(p) < 2.5f || LandformDistance(p) < Mathf.Max(2.5f, navStep * .5f)) cost = -1;
                    navCost[i] = cost;
                }
        }

        int NavIndex(Vector3 world)
        {
            int x = Mathf.Clamp(Mathf.RoundToInt((world.x + half) / navStep), 0, navSize - 1);
            int z = Mathf.Clamp(Mathf.RoundToInt((world.z + half) / navStep), 0, navSize - 1);
            return z * navSize + x;
        }
        Vector3 NavPoint(int index) => new Vector3(-half + index % navSize * navStep, navHeight[index], -half + index / navSize * navStep);

        /// <summary>Nearest passable node, searching outwards when the point itself sits off the map.</summary>
        int NavOpen(Vector3 world)
        {
            int start = NavIndex(world);
            if (navCost[start] > 0) return start;
            int sx = start % navSize, sz = start / navSize;
            for (int r = 1; r < 12; r++)
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != r) continue;
                        int x = sx + dx, z = sz + dz;
                        if (x < 0 || z < 0 || x >= navSize || z >= navSize) continue;
                        if (navCost[z * navSize + x] > 0) return z * navSize + x;
                    }
            return -1;
        }

        /// <summary>
        /// Fills <paramref name="path"/> with ground-level waypoints (about 10 m apart, centred on roads) from
        /// <paramref name="from"/> to <paramref name="to"/>. Returns false, with a direct two-point path, when the
        /// goal cannot be reached without climbing a blocked grade.
        /// </summary>
        /// <param name="offRoad">Scales how much more than road the other ground costs: 1 keeps routes on the road network
        /// wherever it goes the right way; lower values let a vehicle cut across country rather than follow a winding road.</param>
        /// <param name="maxExpansions">Search budget in nodes; past it the search gives up as if there were no route
        /// (keeps an unreachable goal from scanning the whole map during play).</param>
        public bool FindPath(Vector3 from, Vector3 to, List<Vector3> path, float offRoad = 1, int maxExpansions = int.MaxValue)
        {
            path.Clear();
            BuildNavigation();
            if (navCost == null) { path.Add(from); path.Add(to); return false; }
            int start = NavOpen(from), goal = NavOpen(to);
            if (start < 0 || goal < 0) { path.Add(from); path.Add(to); return false; }
            navSearch++; navHeap.Clear(); navHeapF.Clear();
            Touch(start); navG[start] = 0; navParent[start] = -1; Push(start, Heuristic(start, goal));
            bool found = false; int expanded = 0;
            while (navHeap.Count > 0)
            {
                int current = Pop();
                if (navClosed[current]) continue;
                navClosed[current] = true;
                if (++expanded > maxExpansions) break;
                if (current == goal) { found = true; break; }
                int cx = current % navSize, cz = current / navSize;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        int nx = cx + dx, nz = cz + dz;
                        if (nx < 0 || nz < 0 || nx >= navSize || nz >= navSize) continue;
                        int next = nz * navSize + nx;
                        if (navCost[next] <= 0) continue;
                        Touch(next);
                        if (navClosed[next]) continue;
                        float run = navStep * (dx != 0 && dz != 0 ? 1.41421f : 1);
                        float rise = Mathf.Abs(navHeight[next] - navHeight[current]);
                        // Elevation blockage: cliff walls and anything steeper than a car can climb.
                        if (rise > MaxDriveGrade * run) continue;
                        float grade = rise / run;
                        float step = (navCost[current] + navCost[next]) * .5f;
                        if (navCost[current] < WaterCost && navCost[next] < WaterCost) step = RoadCost + (step - RoadCost) * offRoad; // never cheapen fording
                        float g = navG[current] + run * step * (1 + grade * 2.5f);
                        if (g >= navG[next]) continue;
                        navG[next] = g; navParent[next] = current;
                        Push(next, g + Heuristic(next, goal));
                    }
            }
            if (!found) { path.Add(from); path.Add(to); return false; }

            navNodes.Clear();
            for (int node = goal; node >= 0; node = navParent[node]) navNodes.Add(node);
            navNodes.Reverse();
            Vector3 last = from;
            for (int i = 1; i < navNodes.Count - 1; i++)
            {
                Vector3 p = NavPoint(navNodes[i]);
                // Centre waypoints on the carriageway so vehicles drive the road instead of its shoulder.
                if (roadIndex.Nearest(XZ(p), out _, out Vector2 centre) < 11) { p.x = centre.x; p.z = centre.y; }
                if (Vector2.Distance(XZ(p), XZ(last)) < 10) continue;
                p.y = HeightAt(p);
                path.Add(p); last = p;
            }
            path.Add(to);
            return true;
        }

        /// <summary>
        /// Whether a car could drive the straight line between two points: no landform, cover rock, map edge or grade
        /// steeper than the climb limit on the way (cliff walls between elevation levels). Hostiles check this before
        /// spending an A* search on a detour. The first and last few metres are not checked for obstacles, so a car
        /// already tucked against a rock still sees open ground ahead.
        /// </summary>
        public bool DirectDrivable(Vector3 from, Vector3 to)
        {
            if (heights == null) return true;
            Vector2 a = XZ(from), b = XZ(to);
            float length = Vector2.Distance(a, b);
            int steps = Mathf.Max(1, Mathf.CeilToInt(length / 4));
            float stepLength = length / steps, previous = SampleHeight(from);
            for (int i = 1; i <= steps; i++)
            {
                Vector2 p = Vector2.Lerp(a, b, i / (float)steps);
                float h = SampleHeight(new Vector3(p.x, 0, p.y));
                if (Mathf.Abs(h - previous) > MaxDriveGrade * stepLength * 1.15f) return false;
                previous = h;
                float along = i * stepLength;
                if (along > 6 && length - along > 6 && (ObstacleDistance(p) < 1 || !InOutline(p / size))) return false;
            }
            return true;
        }

        void Touch(int node)
        {
            if (navStamp[node] == navSearch) return;
            navStamp[node] = navSearch; navG[node] = float.MaxValue; navClosed[node] = false; navParent[node] = -1;
        }
        float Heuristic(int a, int b)
        {
            float dx = (a % navSize - b % navSize) * navStep, dz = (a / navSize - b / navSize) * navStep;
            return Mathf.Sqrt(dx * dx + dz * dz) * RoadCost;
        }
        void Push(int node, float f)
        {
            navHeap.Add(node); navHeapF.Add(f);
            int i = navHeap.Count - 1;
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (navHeapF[parent] <= navHeapF[i]) break;
                Swap(i, parent); i = parent;
            }
        }
        int Pop()
        {
            int top = navHeap[0], last = navHeap.Count - 1;
            navHeap[0] = navHeap[last]; navHeapF[0] = navHeapF[last];
            navHeap.RemoveAt(last); navHeapF.RemoveAt(last);
            int i = 0;
            while (true)
            {
                int l = i * 2 + 1, r = l + 1, smallest = i;
                if (l < navHeap.Count && navHeapF[l] < navHeapF[smallest]) smallest = l;
                if (r < navHeap.Count && navHeapF[r] < navHeapF[smallest]) smallest = r;
                if (smallest == i) break;
                Swap(i, smallest); i = smallest;
            }
            return top;
        }
        void Swap(int a, int b)
        {
            int n = navHeap[a]; navHeap[a] = navHeap[b]; navHeap[b] = n;
            float f = navHeapF[a]; navHeapF[a] = navHeapF[b]; navHeapF[b] = f;
        }
    }
}
