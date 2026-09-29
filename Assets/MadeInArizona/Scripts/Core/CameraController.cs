using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MadeInArizona
{
    public sealed class CameraController : MonoBehaviour
    {
        public static CameraController Instance { get; private set; }
        public Transform Target;
        Camera view;
        Transform listener;
        Vector3 velocity;
        float appliedShadowOffset = -1, appliedDofOffset = -1;
        /// <summary>The point the camera frames (the car in play). Streaming and culling centre on this, not the lens.</summary>
        public static Vector3 FocusPoint { get; private set; }
        public static bool HasFocus { get; private set; }
        /// <summary>
        /// How far the gameplay camera sits behind its original 40 m framing distance. An orthographic view is unchanged
        /// by moving along its axis, so the lens backs off until tall trees and higher elevation levels at the bottom of
        /// the screen are no longer cut by the near plane. Depth-based effects (blur, shadow range) add this offset.
        /// </summary>
        public static float DepthOffset { get; private set; }
        const float Headroom = 42f; // two elevation levels (2 x 13 m) plus a tall pine above the car
        float shake, dynamicZoom = 1, dynamicZoomVelocity, dynamicZoomHoldUntil;
        /// <summary>Garage orbit around the car: right-mouse drag, Q / E, or the right stick.</summary>
        float garageYaw, manualOrbitUntil;
        /// <summary>Idle garage turntable speed (degrees per second); manual orbiting pauses it for a few seconds.</summary>
        const float GarageAutoOrbit = 7f;
        /// <summary>
        /// Look-ahead: the view shifts toward where the car is heading, by its velocity times the cameraLead dev value
        /// (seconds), capped. The first version (0.32 s, 8.5 m, 0.55 s smoothing) swung so far and so slowly through
        /// turns that the car appeared to slide across the screen and understeer.
        /// </summary>
        const float MaxLead = 4f, LeadSmoothing = .22f;
        Vector3 lead, leadVelocity; Rigidbody targetBody; Transform leadTarget;
        void Awake() { Instance = this; view = GetComponent<Camera>(); }
        void Start()
        {
            // The lens now backs away from the car, so hearing moves to a separate listener at the original framing distance.
            foreach (var existing in GetComponents<AudioListener>()) DestroyImmediate(existing);
            listener = new GameObject("Gameplay audio listener", typeof(AudioListener)).transform;
            listener.SetPositionAndRotation(transform.position, transform.rotation);
        }
        public void Shake(float amount) { shake = Mathf.Min(1.5f, shake + amount); }
        public void Snap() { velocity = Vector3.zero; dynamicZoom = 1; dynamicZoomVelocity = 0; Position(true); }
        void LateUpdate()
        {
            Position(false);
            var game = GameManager.Instance;
            CloudShadows.Tick(game && game.State != GameState.Garage && game.State != GameState.MainMenu);
        }
        void Position(bool snap)
        {
            if (!Target || !view || !GameManager.Instance) return;
            bool garage = GameManager.Instance.State == GameState.Garage || GameManager.Instance.State == GameState.MainMenu;
            if (garage && !snap) OrbitGarage();
            // In the garage the whole composition (camera offset and the framing shift that keeps the car to the right of
            // the mission board) turns around the car, so it stays framed the same way from every side.
            Quaternion orbit = garage ? Quaternion.Euler(0, garageYaw, 0) : Quaternion.identity;
            var offset = garage ? orbit * new Vector3(10, 9, -13) : new Vector3(0, 29, -28);
            // Offset the garage composition so the car sits to the right of the mission board.
            var focus = Target.position + (garage ? orbit * new Vector3(-3.8f, .4f, -2.6f) : Vector3.zero);
            FocusPoint = focus; HasFocus = true;
            if (!garage) focus += Lead(snap);
            var desired = focus + offset;
            if(GeneratedWorld.Active)desired.y=Mathf.Max(desired.y,GeneratedWorld.HeightAt(desired)+9);
            var rotation = Quaternion.LookRotation((focus-desired).normalized, Vector3.up);
            view.orthographic = true;
            float zoom = Mathf.Clamp(DevTuning.Current.cameraZoom, 8f, 40f);
            float size = garage ? 10f * zoom / 21f : zoom;
            // Preserve vertical play space at ultrawide and playable horizontal space at narrow aspects.
            size *= Mathf.Max(1, 1.55f / view.aspect);
            UpdateDynamicZoom(garage, snap, focus, rotation, size);
            size *= dynamicZoom;
            // Death sequence: close in on the wreck.
            if (!garage && GameManager.Instance.Dying) size *= Mathf.Lerp(1, .68f, Mathf.SmoothStep(0, 1, (Time.unscaledTime - GameManager.Instance.DyingStarted) / 1.4f));
            view.orthographicSize = snap ? size : Mathf.Lerp(view.orthographicSize, size, Time.unscaledDeltaTime * 8);
            // Back the lens off far enough that the bottom screen edge clears Headroom metres above the car.
            Vector3 forward = rotation * Vector3.forward;
            float extra = 0;
            if (!garage)
            {
                float sinPitch = Mathf.Max(.2f, -forward.y), cosPitch = Mathf.Sqrt(1 - sinPitch * sinPitch);
                float needed = (Mathf.Max(size, view.orthographicSize) * cosPitch + Headroom) / sinPitch + 3;
                extra = Mathf.Max(0, needed - Vector3.Distance(focus, desired));
            }
            DepthOffset = extra;
            desired -= forward * extra;
            transform.position = snap ? desired : Vector3.SmoothDamp(transform.position, desired, ref velocity, garage ? .22f : .13f, Mathf.Infinity, Time.unscaledDeltaTime);
            transform.rotation = rotation;
            SunGlint.UpdateEye(transform);
            view.farClipPlane = 450 + extra;
            if (listener) listener.SetPositionAndRotation(transform.position + forward * extra, rotation);
            ApplyDepthOffset(extra);
            shake = Mathf.MoveTowards(shake, 0, Time.unscaledDeltaTime * 2.5f);
            if (!garage && shake > 0) transform.position += Random.insideUnitSphere * shake * GameManager.Instance.Save.settings.shake * Mathf.Clamp(DevTuning.Current.shake, 0f, 3f) * .35f;
        }

        Vector3 Lead(bool snap)
        {
            if (leadTarget != Target) { leadTarget = Target; targetBody = Target.GetComponentInParent<Rigidbody>(); lead = leadVelocity = Vector3.zero; }
            // Paused: hold the framing instead of drifting back to the car.
            if (!snap && !GameManager.Instance.IsPlaying) return lead;
            Vector3 wanted = Vector3.zero;
            if (targetBody && !GameManager.Instance.Dying)
            {
                Vector3 planar = targetBody.linearVelocity; planar.y = 0;
                wanted = Vector3.ClampMagnitude(planar * Mathf.Clamp(DevTuning.Current.cameraLead, 0, .4f), MaxLead);
            }
            if (snap) { lead = wanted; leadVelocity = Vector3.zero; }
            else lead = Vector3.SmoothDamp(lead, wanted, ref leadVelocity, LeadSmoothing, Mathf.Infinity, Time.unscaledDeltaTime);
            return lead;
        }

        void OrbitGarage()
        {
            float dt = Time.unscaledDeltaTime, turn = 0;
            var mouse = Mouse.current; var keys = Keyboard.current; var pad = Gamepad.current;
            if (mouse != null && mouse.rightButton.isPressed) turn += mouse.delta.ReadValue().x * .3f;
            if (keys != null) { if (keys.qKey.isPressed) turn -= 90 * dt; if (keys.eKey.isPressed) turn += 90 * dt; }
            if (pad != null) turn += pad.rightStick.ReadValue().x * 120 * dt;
            if (Mathf.Abs(turn) > .0001f) manualOrbitUntil = Time.unscaledTime + 4;
            else if (Time.unscaledTime > manualOrbitUntil) turn = GarageAutoOrbit * dt;
            garageYaw = Mathf.Repeat(garageYaw + turn, 360);
        }

        /// <summary>Keeps the far-depth blur range and the shadow range measured from the car, not from the backed-off lens.</summary>
        void ApplyDepthOffset(float extra)
        {
            if (Mathf.Abs(extra - appliedDofOffset) > .05f)
            {
                appliedDofOffset = extra;
                Shader.SetGlobalVector("_ArizonaOrthoDofParams", DevVisuals.OrthoDof + new Vector4(extra, extra, 0, 0));
            }
            if (Mathf.Abs(extra - appliedShadowOffset) > 1f && GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset pipeline && QualitySettings.shadowDistance > 0)
            {
                appliedShadowOffset = extra;
                pipeline.shadowDistance = QualitySettings.shadowDistance + extra;
            }
        }
        /// <summary>Forces the next frame to re-apply depth-based settings (after quality or visual changes reset them).</summary>
        public static void RefreshDepthEffects() { if (Instance) { Instance.appliedDofOffset = -1; Instance.appliedShadowOffset = -1; } }

        /// <summary>
        /// Pulls the orthographic view back while a hostile sits near the screen edge, then eases back in.
        /// Enemies are measured in the camera's own screen axes, so the required size is exact for the
        /// current aspect. Zooming out is quicker than zooming in, and a short hold prevents pumping when
        /// an enemy hovers on the margin.
        /// </summary>
        void UpdateDynamicZoom(bool garage, bool snap, Vector3 focus, Quaternion rotation, float baseSize)
        {
            float target = 1;
            var game = GameManager.Instance;
            var tuning = DevTuning.Current;
            if (!garage && game.Save != null && game.Save.settings.dynamicZoom && game.Player && game.State == GameState.Playing)
            {
                float maxOut = Mathf.Clamp(tuning.dynamicZoomOut, 1f, 2.2f);
                float usable = 1 - 2 * Mathf.Clamp(tuning.dynamicZoomMargin, .05f, .35f);
                var inverse = Quaternion.Inverse(rotation);
                float aspect = Mathf.Max(.1f, view.aspect);
                foreach (var vehicle in VehicleController.Active)
                {
                    if (!vehicle || vehicle.IsPlayer || vehicle.Damage == null || vehicle.Damage.IsDead) continue;
                    var ai = vehicle.GetComponent<EnemyAI>();
                    if (ai == null || ai.IsFriendly) continue;
                    Vector3 local = inverse * (vehicle.transform.position - focus);
                    // Size needed for this enemy to sit inside the unmarginned part of the screen.
                    float required = Mathf.Max(Mathf.Abs(local.y), Mathf.Abs(local.x) / aspect) / usable / baseSize;
                    // Distant enemies well beyond the maximum pull-back are ignored rather than pinning the camera out.
                    if (required > maxOut * 1.3f) continue;
                    target = Mathf.Max(target, Mathf.Min(required, maxOut));
                }
            }
            float now = Time.unscaledTime;
            if (target >= dynamicZoom - .01f) dynamicZoomHoldUntil = now + 1.4f;
            else if (now < dynamicZoomHoldUntil) target = dynamicZoom;
            if (snap) { dynamicZoom = target; dynamicZoomVelocity = 0; return; }
            float smoothTime = target > dynamicZoom ? .9f : 1.8f;
            dynamicZoom = Mathf.SmoothDamp(dynamicZoom, target, ref dynamicZoomVelocity, smoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
        }
    }
}
