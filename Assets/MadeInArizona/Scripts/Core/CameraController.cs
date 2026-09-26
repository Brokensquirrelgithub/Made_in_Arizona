using UnityEngine;

namespace MadeInArizona
{
    public sealed class CameraController : MonoBehaviour
    {
        public static CameraController Instance { get; private set; }
        public Transform Target;
        Camera view;
        Vector3 velocity;
        float shake, dynamicZoom = 1, dynamicZoomVelocity, dynamicZoomHoldUntil;
        void Awake() { Instance = this; view = GetComponent<Camera>(); }
        public void Shake(float amount) { shake = Mathf.Min(1.5f, shake + amount); }
        public void Snap() { velocity = Vector3.zero; dynamicZoom = 1; dynamicZoomVelocity = 0; Position(true); }
        void LateUpdate() { Position(false); }
        void Position(bool snap)
        {
            if (!Target || !view || !GameManager.Instance) return;
            bool garage = GameManager.Instance.State == GameState.Garage || GameManager.Instance.State == GameState.MainMenu;
            var offset = garage ? new Vector3(10, 9, -13) : new Vector3(0, 29, -28);
            // Offset the garage composition so the car sits to the right of the mission board.
            var focus = Target.position + (garage ? new Vector3(-3.8f, .4f, -2.6f) : Vector3.zero);
            var desired = focus + offset;
            if(GeneratedWorld.Active)desired.y=Mathf.Max(desired.y,GeneratedWorld.HeightAt(desired)+9);
            transform.position = snap ? desired : Vector3.SmoothDamp(transform.position, desired, ref velocity, garage ? .22f : .13f, Mathf.Infinity, Time.unscaledDeltaTime);
            var rotation = Quaternion.LookRotation((focus-desired).normalized, Vector3.up);
            transform.rotation = rotation;
            view.orthographic = true;
            float zoom = Mathf.Clamp(DevTuning.Current.cameraZoom, 8f, 40f);
            float size = garage ? 10f * zoom / 21f : zoom;
            // Preserve vertical play space at ultrawide and playable horizontal space at narrow aspects.
            size *= Mathf.Max(1, 1.55f / view.aspect);
            UpdateDynamicZoom(garage, snap, focus, rotation, size);
            size *= dynamicZoom;
            view.orthographicSize = snap ? size : Mathf.Lerp(view.orthographicSize, size, Time.unscaledDeltaTime * 8);
            shake = Mathf.MoveTowards(shake, 0, Time.unscaledDeltaTime * 2.5f);
            if (!garage && shake > 0) transform.position += Random.insideUnitSphere * shake * GameManager.Instance.Save.settings.shake * Mathf.Clamp(DevTuning.Current.shake, 0f, 3f) * .35f;
        }

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
