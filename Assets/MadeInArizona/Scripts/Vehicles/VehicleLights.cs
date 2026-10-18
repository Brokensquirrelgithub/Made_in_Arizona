using UnityEngine;

namespace MadeInArizona
{
    /// <summary>
    /// Headlights come on as the sun goes down (TimeOfDay): one beam at each lamp. Only cars near the camera light up, which keeps the light count bounded in big
    /// fights; the low graphics preset has no extra lights at all.
    /// </summary>
    public sealed class VehicleLights : MonoBehaviour
    {
        // Warm sodium-amber, shared by the lit lens and the road-facing beam.
        public static readonly Color HeadlightAmber = new Color(1f, .55f, .12f);
        const float Range = 26, VisibleRange = 70;
        VehicleController vehicle;
        readonly Light[] beams = new Light[2];

        public void Bind(VehicleController owner)
        {
            vehicle = owner;
            foreach (var beam in beams) if (beam) Destroy(beam.gameObject);
            if (!owner.Visual) return;
            var sum = new Vector3[2]; var count = new int[2];
            foreach (var part in owner.Visual.GetComponentsInChildren<Transform>(true))
                if (part.name == "Headlight")
                {
                    Vector3 local = owner.Visual.InverseTransformPoint(part.position);
                    int side = local.x < 0 ? 0 : 1;
                    sum[side] += local; count[side]++;
                }
            for (int side = 0; side < 2; side++)
            {
                var go = new GameObject(side == 0 ? "Left headlight beam" : "Right headlight beam");
                go.transform.SetParent(owner.Visual, false);
                go.transform.localPosition = (count[side] > 0 ? sum[side] / count[side] : new Vector3(side == 0 ? -.45f : .45f, .75f, 2)) + Vector3.forward * .15f;
                go.transform.localRotation = Quaternion.Euler(6, side == 0 ? -3 : 3, 0);
                var beam = go.AddComponent<Light>();
                beam.type = LightType.Spot; beam.color = HeadlightAmber; beam.range = Range;
                beam.spotAngle = 54; beam.innerSpotAngle = 28; beam.shadows = LightShadows.None; beam.enabled = false;
                beams[side] = beam;
            }
        }

        void LateUpdate()
        {
            if (!beams[0]) return;
            float on = Mathf.InverseLerp(.35f, .85f, TimeOfDay.Blend);
            bool show = on > 0 && vehicle && vehicle.Damage != null && !vehicle.Damage.IsDead
                && (!CameraController.HasFocus || (transform.position - CameraController.FocusPoint).sqrMagnitude < VisibleRange * VisibleRange);
            foreach (var beam in beams)
            {
                if (!beam) continue;
                if (beam.enabled != show) beam.enabled = show;
                if (show) beam.intensity = 22 * on;
            }
        }
    }
}
