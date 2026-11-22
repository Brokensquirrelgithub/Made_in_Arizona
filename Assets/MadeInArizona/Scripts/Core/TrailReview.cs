using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MadeInArizona
{
    /// <summary>Opt-in trail edge review (-miaSmokeTest -miaTrailReview). Renders offscreen, so it also works in batch mode.</summary>
    public static class TrailReview
    {
        public static IEnumerator Run(Action<string, bool> check)
        {
            var game = GameManager.Instance;
            float started = Time.realtimeSinceStartup;
            game.StartCampaign(173, 1600);
            yield return new WaitUntil(() => game.State == GameState.Playing);
            Debug.Log("MIA_GENERATION_SECONDS " + (Time.realtimeSinceStartup - started).ToString("F2"));
            yield return new WaitForSecondsRealtime(1);
            var world = GeneratedWorld.Active;
            check("generated world has trails", world && world.TrailCount > 0);
            string directory = Environment.GetEnvironmentVariable("MIA_CAPTURE_DIR");
            if (string.IsNullOrEmpty(directory)) directory = Path.Combine(Application.temporaryCachePath, "trail-review");
            Directory.CreateDirectory(directory);
            var camera = CameraController.Instance; var focus = new GameObject("Trail review focus");
            camera.Target = focus.transform;
            bool road = false, track = false, path = false;
            for (int i = 0; i < world.TrailCount && !(road && track && path); i++)
            {
                Vector3 p = world.TrailMidpoint(i, out float width, out bool dirtRoad, out Vector2 direction);
                string kind = dirtRoad ? "dirt-road" : width > 3.6f ? "two-track" : "footpath";
                if ((dirtRoad && road) || (!dirtRoad && width > 3.6f && track) || (!dirtRoad && width <= 3.6f && path)) continue;
                if (dirtRoad) road = true; else if (width > 3.6f) track = true; else path = true;
                game.Player.Body.position = p + new Vector3(40, 3, 40); // keep the car out of frame
                focus.transform.position = p; camera.Snap();
                for (int f = 0; f < 90; f++) yield return null; // let the ecology stream in
                Render(Camera.main, Path.Combine(directory, "trail-" + kind + "-gameplay.png"));
                // Close perspective of the edge.
                var cam = Camera.main; bool ortho = cam.orthographic; camera.enabled = false;
                Vector3 side = new Vector3(-direction.y, 0, direction.x);
                cam.orthographic = false; cam.fieldOfView = 50;
                cam.transform.position = p + side * (width * .5f + 7) + Vector3.up * 5 - new Vector3(direction.x, 0, direction.y) * 6;
                cam.transform.LookAt(p + side * (width * .5f));
                yield return null;
                Render(cam, Path.Combine(directory, "trail-" + kind + "-edge.png"));
                cam.orthographic = ortho; camera.enabled = true;
            }
            check("reviewed all trail kinds", road && track && path);
            // The trail merge fade is only exercised where ribbons meet, so include actual junctions.
            for (int j = 0; j < Mathf.Min(3, world.TrailJunctions.Count); j++)
            {
                Vector3 p = world.TrailJunctions[j];
                game.Player.Body.position = p + new Vector3(60, 3, 60);
                focus.transform.position = p; camera.Snap();
                for (int f = 0; f < 90; f++) yield return null;
                Render(Camera.main, Path.Combine(directory, "trail-junction-" + j + ".png"));
            }
            check("reviewed path junctions", world.TrailJunctions.Count > 0);
            // Road shoulder and town apron, from the gameplay camera and a low oblique view.
            Vector3 town = world.Towns[0], roadPoint = world.NearestPatrolRoad((world.Towns[0] + world.Towns[1]) * .5f);
            foreach (var shot in new[] { ("town", town + new Vector3(0, 0, 0)), ("town-edge", town + new Vector3(26, 0, 0)), ("road", roadPoint) })
            {
                Vector3 p = shot.Item2; p.y = GeneratedWorld.HeightAt(p);
                game.Player.Body.position = p + new Vector3(60, 3, 60);
                focus.transform.position = p; camera.Snap();
                for (int f = 0; f < 90; f++) yield return null;
                Render(Camera.main, Path.Combine(directory, shot.Item1 + "-gameplay.png"));
                var cam = Camera.main; bool ortho = cam.orthographic; camera.enabled = false;
                cam.orthographic = false; cam.fieldOfView = 50;
                cam.transform.position = p + new Vector3(-14, 7, -16); cam.transform.LookAt(p);
                yield return null;
                Render(cam, Path.Combine(directory, shot.Item1 + "-oblique.png"));
                cam.orthographic = ortho; camera.enabled = true;
            }
            // Ground texture collections across the biomes (south desert to northern forest), at gameplay and wide zoom.
            float half = world.WorldBounds.size.x * .5f;
            for (int b = 0; b < 4; b++)
            {
                Vector3 p = new Vector3(world.WorldBounds.size.x * .12f, 0, Mathf.Lerp(-half * .7f, half * .75f, b / 3f));
                p = world.NearestPatrolRoad(p) + new Vector3(55, 0, 30); p.y = GeneratedWorld.HeightAt(p);
                game.Player.Body.position = p + new Vector3(80, 3, 80);
                focus.transform.position = p; camera.Snap();
                for (int f = 0; f < 90; f++) yield return null;
                Render(Camera.main, Path.Combine(directory, "biome-" + b + "-gameplay.png"));
                camera.enabled = false; float size = Camera.main.orthographicSize; Camera.main.orthographicSize = size * 3.2f;
                yield return null;
                Render(Camera.main, Path.Combine(directory, "biome-" + b + "-wide.png"));
                Camera.main.orthographicSize = size; camera.enabled = true;
            }
            // Elevation levels: cliff walls, from gameplay zoom and a low oblique view.
            var cliffs = world.FindCliffs(2);
            Debug.Log("MIA_ELEVATION cliffs=" + cliffs.Count + " rampOpenings=" + world.RampOpenings + " unreachableVertices=" + world.UnreachableVertices);
            check("every area reachable from the first town", world.UnreachableVertices == 0);
            for (int c = 0; c < cliffs.Count; c++)
            {
                Vector3 p = cliffs[c].position; Vector2 down = cliffs[c].downhill;
                game.Player.Body.position = p + new Vector3(70, 3, 70);
                focus.transform.position = p; camera.Snap();
                for (int f = 0; f < 90; f++) yield return null;
                Render(Camera.main, Path.Combine(directory, "cliff-" + c + "-gameplay.png"));
                var cam = Camera.main; bool ortho = cam.orthographic; camera.enabled = false;
                cam.orthographic = false; cam.fieldOfView = 55;
                cam.transform.position = p + new Vector3(down.x, 0, down.y) * 34 + Vector3.up * 9; cam.transform.LookAt(p + Vector3.up * 4);
                yield return null;
                Render(cam, Path.Combine(directory, "cliff-" + c + "-oblique.png"));
                cam.orthographic = ortho; camera.enabled = true;
            }
            // Zoomed-out gameplay camera over forest and cliff bases: tall trees and higher ground at the bottom
            // of the screen must render whole (not sliced open by the near plane).
            float zoom = DevTuning.Current.cameraZoom; DevTuning.Current.cameraZoom = 40;
            var zoomShots = new System.Collections.Generic.List<Vector3>();
            Vector3 forestSpot = new Vector3(world.WorldBounds.size.x * .12f, 0, world.WorldBounds.size.x * .5f * .75f);
            zoomShots.Add(world.NearestPatrolRoad(forestSpot) + new Vector3(55, 0, 30));
            foreach (var cliff in world.FindCliffs(3)) zoomShots.Add(cliff.position + new Vector3(cliff.downhill.x, 0, cliff.downhill.y) * 12);
            for (int z = 0; z < zoomShots.Count; z++)
            {
                Vector3 p = zoomShots[z]; p.y = GeneratedWorld.HeightAt(p);
                game.Player.Body.position = p + new Vector3(90, 3, 90);
                focus.transform.position = p; camera.Snap();
                for (int f = 0; f < 90; f++) yield return null;
                Render(Camera.main, Path.Combine(directory, "zoomout-" + z + ".png"));
                Debug.Log($"MIA_CAMERA zoomout-{z} orthoSize={Camera.main.orthographicSize:F1} depthOffset={CameraController.DepthOffset:F1} distance={Vector3.Distance(Camera.main.transform.position, p):F1}");
            }
            DevTuning.Current.cameraZoom = zoom; camera.Snap();
            yield return Reachability(check);
            Debug.Log("MIA_TRAIL_REVIEW " + directory);
        }

        /// <summary>Every generated map must be drivable from the first town (-miaSmokeTest -miaReachabilityTest; no rendering needed).</summary>
        public static IEnumerator Reachability(Action<string, bool> check)
        {
            var game = GameManager.Instance;
            var cases = new[] { (173, 1600f), (1, 1600f), (42, 1600f), (7, 2000f), (1234, 2400f), (5, 1800f), (999, 3200f), (2024, 4800f), (77, 3600f), (31337, 1600f), (88, 4000f), (600, 2800f) };
            foreach (var (seed, mapSize) in cases)
            {
                float t0 = Time.realtimeSinceStartup;
                game.StartCampaign(seed, mapSize);
                yield return new WaitUntil(() => game.State == GameState.Playing);
                var generated = GeneratedWorld.Active;
                Debug.Log($"MIA_ELEVATION seed={seed} size={mapSize} towns={generated.Towns.Count} seconds={Time.realtimeSinceStartup - t0:F2} cliffs={generated.FindCliffs(50).Count} rampOpenings={generated.RampOpenings} unreachableVertices={generated.UnreachableVertices}");
                check($"seed {seed} at {mapSize} m fully reachable", generated.UnreachableVertices == 0);
                string landformIssue = generated.LandformIssue();
                Debug.Log($"MIA_LANDFORMS seed={seed} size={mapSize} landforms={generated.LandformCount} cover={generated.CoverCount} issue={landformIssue ?? "none"}");
                check($"seed {seed} at {mapSize} m landforms keep roads, objectives and routes open", landformIssue == null && generated.LandformCount <= GeneratedWorld.MaxLandforms * Mathf.Clamp(mapSize * mapSize / (1600f * 1600f), 1, 4));
            }
        }

        public static IEnumerator ExperimentalTerrain(Action<string, bool> check)
        {
            var game = GameManager.Instance;
            game.StartCampaign(173, 1600, false);
            yield return new WaitUntil(() => game.State == GameState.Playing);
            var baseline = GeneratedWorld.Active;
            check("experimental terrain is off on the baseline map", baseline.DryWashCount == 0 && baseline.FoothillCount == 0 &&
                baseline.DryLakeCenters.Count == 0 && baseline.Terraces.Count == 0 && baseline.Fans.Count == 0 && baseline.Narrows.Count == 0);
            var baselinePins = baseline.Pins.ConvertAll(pin => new Vector2(pin.position.x, pin.position.z));
            Color neutral = GeneratedWorld.SonoranGround;
            Color lake = GeneratedWorld.MapReliefColor(neutral, -4, 1);
            Color wash = GeneratedWorld.MapReliefColor(neutral, -4, 0);
            Color hill = GeneratedWorld.MapReliefColor(neutral, 8, 0);
            check("map colors separate dry lakes, washes and foothills", lake.b > neutral.b + .18f &&
                wash.r < neutral.r && wash.g < neutral.g - .025f && hill.r > neutral.r + .04f && hill.b < wash.b);

            float[] first = null, other = null;
            bool sawLake = false, sawTerrace = false, sawFan = false, sawNarrows = false;
            var path = new List<Vector3>();
            foreach (var (seed, size) in new[] { (173, 1600f), (271, 1600f), (2024, 4800f), (173, 1600f) })
            {
                game.StartCampaign(seed, size, true);
                yield return new WaitUntil(() => game.State == GameState.Playing);
                var world = GeneratedWorld.Active;
                string label = $"seed {seed} at {size} m";
                check(label + " has washes and foothills", world.DryWashCount > 0 && world.FoothillCount > 0);
                check(label + " keeps solid landforms clear of roads and objectives", world.LandformIssue() == null);
                Vector3 start = game.World.PlayerSpawn;
                bool routesOpen = world.Towns.TrueForAll(town => world.FindPath(start, town, path)) &&
                    game.World.ObjectivePoints.TrueForAll(beat => world.FindPath(start, beat, path));
                check(label + " routes from the starter car reach towns and mission objectives", routesOpen);
                bool lakeFlat = true, lakeRoutes = true, lakeVisible = true;
                sawLake |= world.DryLakeCenters.Count > 0;
                foreach (var site in world.DryLakeCenters)
                {
                    var center = new Vector3(site.x, 0, site.y);
                    center.y = GeneratedWorld.HeightAt(center);
                    lakeRoutes &= world.FindPath(start, center, path);
                    int paleSamples = 0, exits = 0;
                    foreach (var direction in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
                    {
                        Vector3 near = center + direction * 16;
                        lakeFlat &= world.DryLakeCoverageAt(near) > .99f && Mathf.Abs(GeneratedWorld.HeightAt(near) - center.y) < .05f &&
                            GeneratedWorld.SurfaceAt(near) == SurfaceKind.Dirt;
                        Color pixel = world.MapTexture.GetPixelBilinear(.5f + near.x / size, .5f + near.z / size);
                        if (pixel.b > pixel.r * .69f) paleSamples++;
                        Vector3 bank = near;
                        for (int distance = 16; distance < 256 && world.DryLakeCoverageAt(bank) > .35f; distance += 8)
                            bank += direction * 8;
                        if (world.DryLakeCoverageAt(bank) <= .35f && world.DirectDrivable(center, bank)) exits++;
                    }
                    lakeVisible &= paleSamples >= 2;
                    lakeRoutes &= exits >= 3;
                }
                if (world.DryLakeCenters.Count > 0)
                {
                    check(label + " has a flat, firm dry lake floor", lakeFlat);
                    check(label + " dry lake floors have an AI route and broad driving exits", lakeRoutes);
                    check(label + " shows pale dry lake beds in the map texture", lakeVisible);
                }
                sawTerrace |= world.Terraces.Count > 0;
                sawFan |= world.Fans.Count > 0;
                sawNarrows |= world.Narrows.Count > 0;
                if (world.Terraces.Count > 0) CheckTerraces(world, start, size, label, path, check);
                if (world.Fans.Count > 0) CheckFans(world, start, size, label, path, check);
                if (world.Narrows.Count > 0) CheckNarrows(world, start, size, label, path, check);
                if (seed == 173)
                {
                    bool samePins = world.Pins.Count == baselinePins.Count;
                    for (int i = 0; samePins && i < baselinePins.Count; i++)
                        samePins = baselinePins[i] == new Vector2(world.Pins[i].position.x, world.Pins[i].position.z);
                    check(label + " keeps the original town and pin coordinates", samePins);
                    if (first == null) first = SampleRelief(world, size);
                    else check("the same seed reproduces the same terrain changes", SameRelief(first, SampleRelief(world, size)));
                }
                if (seed == 271) other = SampleRelief(world, size);
            }
            check("the sampled maps include each optional terrain feature", sawLake && sawTerrace && sawFan && sawNarrows);
            check("different seeds place terrain changes in different locations", !SameRelief(first, other));
        }

        static void CheckTerraces(GeneratedWorld world, Vector3 start, float size, string label, List<Vector3> path, Action<string, bool> check)
        {
            bool shelves = world.Terraces.Count > 0, open = shelves, visible = shelves;
            foreach (var terrace in world.Terraces)
            {
                Vector3 lower = terrace.Point(0, GeneratedWorld.LowerShelfLane);
                Vector3 upper = terrace.Point(0, GeneratedWorld.UpperShelfLane);
                lower.y = GeneratedWorld.HeightAt(lower);
                upper.y = GeneratedWorld.HeightAt(upper);
                shelves &= upper.y > lower.y + 2.5f && GeneratedWorld.SurfaceAt(lower) == SurfaceKind.Dirt &&
                    GeneratedWorld.SurfaceAt(upper) == SurfaceKind.Dirt;
                open &= world.FindPath(start, upper, path) && world.DirectDrivable(lower, upper);
                foreach (float lane in new[] { GeneratedWorld.LowerShelfLane, GeneratedWorld.UpperShelfLane })
                {
                    Vector3 end = terrace.Point(1, lane), otherEnd = terrace.Point(-1, lane);
                    open &= world.DirectDrivable(otherEnd, end);
                }
                int distinctBands = 0;
                foreach (float along in new[] { -.25f, 0, .25f })
                {
                    Vector3 shelf = terrace.Point(along, GeneratedWorld.UpperShelfLane);
                    Vector3 riser = terrace.Point(along, -.12f);
                    Color shelfPixel = world.MapTexture.GetPixelBilinear(.5f + shelf.x / size, .5f + shelf.z / size);
                    Color riserPixel = world.MapTexture.GetPixelBilinear(.5f + riser.x / size, .5f + riser.z / size);
                    if (shelfPixel.r > riserPixel.r + .03f) distinctBands++;
                }
                visible &= distinctBands >= 2;
            }
            check(label + " has two firm sandstone shelves at distinct elevations", shelves);
            check(label + " terraces have AI access and a through route with ramps at both ends", open);
            check(label + " terrace shelves and risers are distinct in the map texture", visible);
        }

        static void CheckFans(GeneratedWorld world, Vector3 start, float size, string label, List<Vector3> path, Action<string, bool> check)
        {
            bool slopes = world.Fans.Count > 0, routes = slopes, visible = slopes;
            foreach (var fan in world.Fans)
            {
                Vector3 near = fan.Point(.24f), middle = fan.Point(.55f), far = fan.Point(.76f);
                float drop = GeneratedWorld.HeightAt(near) - GeneratedWorld.HeightAt(far);
                SurfaceKind surface = GeneratedWorld.SurfaceAt(middle);
                slopes &= drop > 1 && fan.WidthAt(.76f) > fan.WidthAt(.24f) * 1.6f &&
                    world.FanCoverageAt(middle) > .9f && (surface == SurfaceKind.Gravel || surface == SurfaceKind.Dirt);
                routes &= world.FindPath(start, middle, path) && world.DirectDrivable(near, far) &&
                    world.DirectDrivable(fan.Point(.58f, -.62f), fan.Point(.58f, .62f));

                float lightest = 0, darkest = 1;
                for (int i = 0; i <= 12; i++)
                {
                    Vector3 sample = fan.Point(.64f, Mathf.Lerp(-.60f, .60f, i / 12f));
                    Color pixel = world.MapTexture.GetPixelBilinear(.5f + sample.x / size, .5f + sample.z / size);
                    lightest = Mathf.Max(lightest, pixel.r);
                    darkest = Mathf.Min(darkest, pixel.r);
                }
                visible &= lightest - darkest > .025f;
            }
            check(label + " has gravel fans that widen and descend away from their source", slopes);
            check(label + " has AI access and drivable routes down and across the fans", routes);
            check(label + " shows the fan ribs in the map texture", visible);
        }

        static void CheckNarrows(GeneratedWorld world, Vector3 start, float size, string label, List<Vector3> path, Action<string, bool> check)
        {
            bool fits = world.Narrows.Count > 0, routes = fits, visible = fits;
            foreach (var canyon in world.Narrows)
            {
                Vector3 floor = canyon.Point(0), shelf = canyon.BypassPoint(0);
                fits &= canyon.HalfFloorAt(0) * 2 >= 30 && canyon.HalfFloorAt(1) > canyon.HalfFloorAt(0) * 1.5f &&
                    GeneratedWorld.HeightAt(shelf) - GeneratedWorld.HeightAt(floor) > 5 &&
                    GeneratedWorld.SurfaceAt(floor) == SurfaceKind.Dirt && GeneratedWorld.SurfaceAt(shelf) == SurfaceKind.Dirt;

                Vector3 entry = canyon.Point(-1.06f), exit = canyon.Point(1.06f);
                Vector3 passage = entry, bypass = canyon.BypassPoint(-1.06f);
                routes &= world.FindPath(start, entry, path) && world.DirectDrivable(entry, bypass);
                for (int i = 1; i <= 8; i++)
                {
                    float along = Mathf.Lerp(-1.06f, 1.06f, i / 8f);
                    Vector3 nextPassage = canyon.Point(along), nextBypass = canyon.BypassPoint(along);
                    routes &= world.DirectDrivable(passage, nextPassage) && world.DirectDrivable(bypass, nextBypass);
                    passage = nextPassage; bypass = nextBypass;
                }
                routes &= world.DirectDrivable(bypass, exit);

                int distinctBands = 0;
                foreach (float along in new[] { -.24f, 0, .24f })
                {
                    float wall = canyon.HalfFloorAt(along) + canyon.wallRun * .5f;
                    Vector3 opening = canyon.Point(along), left = canyon.Point(along, -wall), right = canyon.Point(along, wall);
                    Color lane = world.MapTexture.GetPixelBilinear(.5f + opening.x / size, .5f + opening.z / size);
                    Color leftBank = world.MapTexture.GetPixelBilinear(.5f + left.x / size, .5f + left.z / size);
                    Color rightBank = world.MapTexture.GetPixelBilinear(.5f + right.x / size, .5f + right.z / size);
                    if (lane.r > Mathf.Max(leftBank.r, rightBank.r) + .04f) distinctBands++;
                }
                visible &= distinctBands >= 2;
            }
            check(label + " canyon narrows have wide floors, flared mouths and high bypasses", fits);
            check(label + " has AI access to connected canyon passages and bypasses", routes);
            check(label + " shows a visible corridor between canyon walls on the map", visible);
        }

        static float[] SampleRelief(GeneratedWorld world, float size)
        {
            const int samples = 48;
            var result = new float[samples * samples];
            for (int z = 0; z < samples; z++)
                for (int x = 0; x < samples; x++)
                    result[z * samples + x] = world.ExperimentalReliefAt(new Vector3(
                        (x / (float)samples - .5f) * size, 0, (z / (float)samples - .5f) * size));
            return result;
        }

        static bool SameRelief(float[] a, float[] b)
        {
            for (int i = 0; i < a.Length; i++)
                if (Mathf.Abs(a[i] - b[i]) > .001f) return false;
            return true;
        }

        static void Render(Camera camera, string path)
        {
            var target = RenderTexture.GetTemporary(1600, 900, 24, RenderTextureFormat.ARGB32);
            var previous = camera.targetTexture; camera.targetTexture = target; camera.Render(); camera.targetTexture = previous;
            var active = RenderTexture.active; RenderTexture.active = target;
            var image = new Texture2D(1600, 900, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); image.Apply();
            RenderTexture.active = active; RenderTexture.ReleaseTemporary(target);
            File.WriteAllBytes(path, image.EncodeToPNG()); UnityEngine.Object.Destroy(image);
        }
    }
}
