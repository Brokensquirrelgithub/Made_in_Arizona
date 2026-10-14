using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace MadeInArizona
{
    public sealed partial class GameUI
    {
        const int ReticlePatch = 24;
        const float ReticleInterval = .12f;
        RenderTexture reticleCapture;
        bool reticleReadPending;
        float nextReticleRead;
        Color reticleInk = Color.white;

        Color ReticleInk => reticleInk;
        internal Color ReticleInkForTest => reticleInk;

        IEnumerator SampleReticleContrast()
        {
            if (!SystemInfo.supportsAsyncGPUReadback) yield break;
            var frameEnd = new WaitForEndOfFrame();
            while (true)
            {
                yield return frameEnd;
                if (reticleReadPending || Time.unscaledTime < nextReticleRead || !ReticleSamplePoint(out Vector2 point)) continue;
                nextReticleRead = Time.unscaledTime + ReticleInterval;
                if (!reticleCapture || reticleCapture.width != Screen.width || reticleCapture.height != Screen.height)
                {
                    ReleaseReticleCapture();
                    reticleCapture = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32)
                    { name = "Reticle contrast sample", antiAliasing = 1, useMipMap = false };
                    reticleCapture.Create();
                }
                // Capture the displayed scene, then read only a 24-pixel patch; the GPU readback never stalls the HUD.
                ScreenCapture.CaptureScreenshotIntoRenderTexture(reticleCapture);
                int x = Mathf.Clamp(Mathf.RoundToInt(point.x) - ReticlePatch / 2, 0, Screen.width - ReticlePatch);
                int y = Mathf.Clamp(Mathf.RoundToInt(point.y) - ReticlePatch / 2, 0, Screen.height - ReticlePatch);
                int centerX = Mathf.RoundToInt(point.x) - x, centerY = Mathf.RoundToInt(point.y) - y;
                reticleReadPending = true;
                AsyncGPUReadback.Request(reticleCapture, 0, x, ReticlePatch, y, ReticlePatch, 0, 1,
                    TextureFormat.RGBA32, result => ReadReticleContrast(result, centerX, centerY));
            }
        }

        bool ReticleSamplePoint(out Vector2 point)
        {
            point = default;
            if (!game || game.State != GameState.Playing || settings || worldMap || updatesMenu || introActive || Screen.width < ReticlePatch || Screen.height < ReticlePatch)
                return false;
            if (InputManager.Instance && InputManager.Instance.UsingGamepad)
            {
                if (!game.Player || !Camera.main) return false;
                Vector2 aim = InputManager.Instance.Aim;
                Vector3 projected = Camera.main.WorldToScreenPoint(game.Player.transform.position + new Vector3(aim.x, 0, aim.y) * 14 + Vector3.up * .8f);
                if (projected.z <= 0) return false;
                point = projected;
            }
            else
            {
                if (Mouse.current == null) return false;
                point = Mouse.current.position.ReadValue();
            }
            return point.x >= 0 && point.y >= 0 && point.x < Screen.width && point.y < Screen.height;
        }

        void ReadReticleContrast(AsyncGPUReadbackRequest result, int centerX, int centerY)
        {
            reticleReadPending = false;
            if (!this || result.hasError) return;
            var pixels = result.GetData<Color32>();
            long brightness = 0;
            int count = 0;
            for (int y = 0; y < ReticlePatch; y++)
                for (int x = 0; x < ReticlePatch; x++)
                {
                    // Ignore the reticle's own horizontal and vertical strokes in the screen capture.
                    if (Mathf.Abs(x - centerX) <= 2 || Mathf.Abs(y - centerY) <= 2) continue;
                    Color32 pixel = pixels[y * ReticlePatch + x];
                    brightness += 54L * pixel.r + 183L * pixel.g + 19L * pixel.b;
                    count++;
                }
            if (count > 0) reticleInk = brightness > count * 256L * 255L / 2 ? Color.black : Color.white;
        }

        void DisposeReticleContrast()
        {
            if (reticleReadPending) AsyncGPUReadback.WaitAllRequests();
            ReleaseReticleCapture();
        }

        void ReleaseReticleCapture()
        {
            if (!reticleCapture) return;
            reticleCapture.Release();
            Destroy(reticleCapture);
            reticleCapture = null;
        }
    }
}
