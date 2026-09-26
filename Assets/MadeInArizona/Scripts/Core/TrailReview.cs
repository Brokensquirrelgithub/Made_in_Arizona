using System;
using System.Collections;
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
            yield return Reachability(check);
            Debug.Log("MIA_TRAIL_REVIEW " + directory);
        }

        /// <summary>Every generated map must be drivable from the first town (-miaSmokeTest -miaReachabilityTest; no rendering needed).</summary>
        public static IEnumerator Reachability(Action<string, bool> check)
        {
            var game = GameManager.Instance;
            var cases = new[] { (173, 1600f), (1, 1600f), (42, 800f), (7, 800f), (1234, 800f), (5, 1200f), (999, 2400f), (2024, 3200f), (77, 1600f), (31337, 800f), (88, 2000f), (600, 1000f) };
            foreach (var (seed, mapSize) in cases)
            {
                float t0 = Time.realtimeSinceStartup;
                game.StartCampaign(seed, mapSize);
                yield return new WaitUntil(() => game.State == GameState.Playing);
                var generated = GeneratedWorld.Active;
                Debug.Log($"MIA_ELEVATION seed={seed} size={mapSize} towns={generated.Towns.Count} seconds={Time.realtimeSinceStartup - t0:F2} cliffs={generated.FindCliffs(50).Count} rampOpenings={generated.RampOpenings} unreachableVertices={generated.UnreachableVertices}");
                check($"seed {seed} at {mapSize} m fully reachable", generated.UnreachableVertices == 0);
            }
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
