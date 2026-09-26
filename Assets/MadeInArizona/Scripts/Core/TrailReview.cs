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
            game.StartCampaign(173, 1600);
            yield return new WaitUntil(() => game.State == GameState.Playing);
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
            Debug.Log("MIA_TRAIL_REVIEW " + directory);
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
