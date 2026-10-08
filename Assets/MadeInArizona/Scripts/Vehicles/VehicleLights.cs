using UnityEngine;

namespace MadeInArizona
{
    /// <summary>
    /// Headlights come on as the sun goes down (TimeOfDay): one spot light between the car's headlamps, throwing a warm
    /// pool of light down the road. Only cars near the camera light up, which keeps the light count bounded in big
    /// fights; the low graphics preset has no extra lights at all.
    /// </summary>
    public sealed class VehicleLights : MonoBehaviour
    {
        const float Range = 26, VisibleRange = 70;
        VehicleController vehicle;
        Light beam;

        public void Bind(VehicleController owner)
        {
            vehicle = owner;
            if (beam) Destroy(beam.gameObject);
            if (!owner.Visual) return;
            Vector3 sum = Vector3.zero; int count = 0;
            foreach (var part in owner.Visual.GetComponentsInChildren<Transform>(true))
                if (part.name == "Headlight") { sum += owner.Visual.InverseTransformPoint(part.position); count++; }
            var go = new GameObject("Headlight beams");
            go.transform.SetParent(owner.Visual, false);
            go.transform.localPosition = (count > 0 ? sum / count : new Vector3(0, .75f, 2)) + Vector3.forward * .15f;
            go.transform.localRotation = Quaternion.Euler(6, 0, 0); // dipped beams
            beam = go.AddComponent<Light>();
            beam.type = LightType.Spot; beam.color = new Color(1f, .9f, .74f); beam.range = Range;
            beam.spotAngle = 64; beam.innerSpotAngle = 30; beam.shadows = LightShadows.None; beam.enabled = false;
        }

        void LateUpdate()
        {
            if (!beam) return;
            float on = Mathf.InverseLerp(.35f, .85f, TimeOfDay.Blend);
            bool show = on > 0 && vehicle && vehicle.Damage != null && !vehicle.Damage.IsDead
                && (!CameraController.HasFocus || (transform.position - CameraController.FocusPoint).sqrMagnitude < VisibleRange * VisibleRange);
            if (beam.enabled != show) beam.enabled = show;
            if (show) beam.intensity = 18 * on;
        }
    }
}
