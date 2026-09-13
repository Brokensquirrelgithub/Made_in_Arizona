using UnityEngine;

namespace MadeInArizona
{
    public sealed class CameraController : MonoBehaviour
    {
        public static CameraController Instance { get; private set; }
        public Transform Target;
        Camera view;
        Vector3 velocity;
        float shake;
        void Awake() { Instance = this; view = GetComponent<Camera>(); }
        public void Shake(float amount) { shake = Mathf.Min(1.5f, shake + amount); }
        public void Snap() { velocity = Vector3.zero; Position(true); }
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
            transform.rotation = Quaternion.LookRotation((focus-desired).normalized, Vector3.up);
            view.orthographic = true;
            float zoom = Mathf.Clamp(DevTuning.Current.cameraZoom, 8f, 40f);
            float size = garage ? 10f * zoom / 21f : zoom;
            // Preserve vertical play space at ultrawide and playable horizontal space at narrow aspects.
            size *= Mathf.Max(1, 1.55f / view.aspect);
            view.orthographicSize = snap ? size : Mathf.Lerp(view.orthographicSize, size, Time.unscaledDeltaTime * 8);
            shake = Mathf.MoveTowards(shake, 0, Time.unscaledDeltaTime * 2.5f);
            if (!garage && shake > 0) transform.position += Random.insideUnitSphere * shake * GameManager.Instance.Save.settings.shake * Mathf.Clamp(DevTuning.Current.shake, 0f, 3f) * .35f;
        }
    }
}
