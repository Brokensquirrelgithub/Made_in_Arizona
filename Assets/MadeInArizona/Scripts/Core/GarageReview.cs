using System;
using System.Collections;
using UnityEngine;

namespace MadeInArizona
{
    /// <summary>
    /// Visual review captures (-miaSmokeTest -miaGarageReview, with MIA_CAPTURE_DIR set and a minimized window): the
    /// garage from an orbit of high angles and from low angles at the ramps, lift, benches, shelving and desk; Suzuki
    /// washing a dusty car; then, in the open world, a cliff face too steep to drive, cloud shadows, and the death ray.
    /// Also checks that the garage ramps' faces point outward and that the wash rinses dust off.
    /// </summary>
    public static class GarageReview
    {
        public static IEnumerator Run(Action<string, bool> check, Action<string> capture)
        {
            var game = GameManager.Instance;
            game.ReturnToGarage();
            yield return new WaitForSecondsRealtime(2);
            var ui = game.GetComponent<GameUI>();
            var view = Camera.main; var rig = CameraController.Instance;
            ui.enabled = false; rig.enabled = false;
            view.orthographic = false; view.fieldOfView = 45;

            // Ramps: every triangle of the drive-on wedges faces away from the wedge's centre.
            int ramps = 0, inward = 0;
            foreach (var filter in game.World.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.name != "Drive on ramp" || !filter.sharedMesh) continue;
                ramps++;
                var mesh = filter.sharedMesh; var v = mesh.vertices; var t = mesh.triangles; Vector3 centre = mesh.bounds.center;
                for (int i = 0; i < t.Length; i += 3)
                {
                    Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                    if (Vector3.Dot(Vector3.Cross(b - a, c - a), (a + b + c) / 3 - centre) <= 0) inward++;
                }
            }
            check("garage ramps exist and every face points outward (" + ramps + " ramps, " + inward + " inward faces)", ramps == 2 && inward == 0);
            var strikes = Resources.LoadAll<AudioClip>("Audio/Bowling");
            check("both bowling strike recordings load (" + strikes.Length + ")", strikes.Length == 2 && Array.TrueForAll(strikes, c => c && c.length > 1));

            Vector3 shopCentre = new Vector3(0, 1.6f, 2);
            for (int i = 0; i < 10; i++)
            {
                float angle = i * 36 * Mathf.Deg2Rad;
                Shoot(view, shopCentre + new Vector3(Mathf.Sin(angle) * 24, 10, -Mathf.Cos(angle) * 24), shopCentre);
                yield return new WaitForSecondsRealtime(.35f); capture("garage-orbit-" + i); yield return new WaitForSecondsRealtime(.35f);
            }
            var closeUps = new (string name, Vector3 eye, Vector3 at)[]
            {
                ("garage-ramps-front", new Vector3(0, 1.1f, -9.5f), new Vector3(0, .6f, -4)),
                ("garage-ramps-side", new Vector3(5.5f, 1.3f, -6.5f), new Vector3(.9f, .5f, -4)),
                ("garage-ramps-left", new Vector3(-5.5f, .9f, -3.5f), new Vector3(-.9f, .5f, -4)),
                ("garage-lift-under", new Vector3(4.2f, .9f, .5f), new Vector3(0, .8f, 0)),
                ("garage-benches", new Vector3(0, 3.2f, 3), new Vector3(-8.85f, 2, 11)),
                ("garage-bench-right", new Vector3(3, 2.8f, 5), new Vector3(8.85f, 2, 11)),
                ("garage-planning-board", new Vector3(-6, 3, 3), new Vector3(-12, 2.8f, 7.6f)),
                ("garage-shelves", new Vector3(5.5f, 2.4f, 0), new Vector3(9.75f, 1.6f, 5.4f)),
                ("garage-desk", new Vector3(5, 2.6f, -3), new Vector3(8, 1.5f, -6.6f)),
                ("garage-rear-outside", new Vector3(22, 7, 22), new Vector3(10, 3, 11)),
                ("garage-dog-bed", new Vector3(7.5f, 1.8f, -6.5f), new Vector3(4.7f, .3f, -3.5f)),
            };
            foreach (var shot in closeUps)
            {
                Shoot(view, shot.eye, shot.at);
                yield return new WaitForSecondsRealtime(.35f); capture(shot.name); yield return new WaitForSecondsRealtime(.35f);
            }

            // Suzuki washes a filthy car.
            var dust = game.Player ? game.Player.GetComponent<VehicleDust>() : null;
            var dog = Array.Find(UnityEngine.Object.FindObjectsByType<SuzukiDog>(), d => !d.IsRiding);
            if (dust) dust.SoilForReview(1);
            float start = dust ? dust.Amount : 0, waited = 0;
            while (dog && !dog.IsSpraying && waited < 60) { waited += Time.unscaledDeltaTime; yield return null; }
            check("Suzuki starts washing a dusty car (" + waited.ToString("0") + " s)", dog && dog.IsSpraying);
            for (int i = 0; i < 3 && dog; i++)
            {
                // Over her shoulder: Suzuki in front, the jet crossing to the car behind her.
                Vector3 at = dog.transform.position + Vector3.up * .6f, car = game.Player.transform.position + Vector3.up * .8f;
                Vector3 back = at - car; back.y = 0; back = back.normalized;
                Vector3 side = Vector3.Cross(Vector3.up, back);
                Shoot(view, at + back * 3.4f + side * 1.6f + Vector3.up * 1.8f, Vector3.Lerp(at, car, .45f));
                yield return new WaitForSecondsRealtime(.4f); capture("garage-wash-" + i); yield return new WaitForSecondsRealtime(2.6f);
            }
            Shoot(view, new Vector3(0, 12, -16), new Vector3(0, 0, 0));
            yield return new WaitForSecondsRealtime(.3f); capture("garage-wash-hose"); yield return new WaitForSecondsRealtime(.3f);
            if (dog)
            {
                // Suzuki's face: ears and nozzle.
                Vector3 head = dog.transform.position + Vector3.up * .85f;
                Shoot(view, head + dog.transform.forward * 1.6f + dog.transform.right * .7f + Vector3.up * .25f, head);
                yield return new WaitForSecondsRealtime(.3f); capture("garage-suzuki-face"); yield return new WaitForSecondsRealtime(.3f);
            }
            check("the hose rinses dust off (" + start.ToString("0.00") + " -> " + (dust ? dust.Amount : 0).ToString("0.00") + ")", dust && dust.Amount < start - .1f);

            view.orthographic = true; rig.enabled = true; ui.enabled = true;

            // Open world: a face too steep to drive, cloud shadows and the death ray.
            game.StartCampaign(173, 1600);
            // The world session may regenerate once when it sees the new config: wait for one world to stay put.
            GeneratedWorld world = null; float stable = 0, waitedForWorld = 0;
            while (stable < 4 && waitedForWorld < 60)
            {
                var current = game.State == GameState.Playing ? GeneratedWorld.Active : null;
                stable = current && current == world ? stable + Time.unscaledDeltaTime : 0;
                world = current; waitedForWorld += Time.unscaledDeltaTime;
                yield return null;
            }
            check("generated world ready for the outdoor review", world);
            if (!world) yield break;
            // The steepest open-country terrain on a 4 m grid: a cliff wall between elevation levels.
            Vector3 cliff = Vector3.zero; float steepest = 0, half = world.WorldBounds.size.x * .5f;
            for (float x = -half; x < half; x += 4)
                for (float z = -half; z < half; z += 4)
                {
                    var p = new Vector3(x, 0, z);
                    if (!GeneratedWorld.Contains(p) || world.ObstacleDistance(p) < 25) continue;
                    float slope = Mathf.Abs(GeneratedWorld.HeightAt(p + Vector3.right * 3) - GeneratedWorld.HeightAt(p - Vector3.right * 3))
                        + Mathf.Abs(GeneratedWorld.HeightAt(p + Vector3.forward * 3) - GeneratedWorld.HeightAt(p - Vector3.forward * 3));
                    if (slope > steepest) { steepest = slope; cliff = p; }
                }
            cliff.y = GeneratedWorld.HeightAt(cliff);
            Debug.Log("MIA_REVIEW cliff at " + cliff + " rise " + steepest.ToString("0.0"));
            check("found a cliff to review (rise " + steepest.ToString("0.0") + " m over 6 m)", steepest > 5);
            var focus = new GameObject("Cliff review focus");
            var carTarget = rig.Target;
            focus.transform.position = cliff; rig.Target = focus.transform;
            game.Player.Body.position = cliff + new Vector3(30, 30, 0); game.Player.Body.isKinematic = true;
            rig.Snap(); yield return new WaitForSecondsRealtime(1.2f); capture("world-cliff-overhead"); yield return new WaitForSecondsRealtime(.4f);
            rig.enabled = false; ui.enabled = false; view.orthographic = false; view.fieldOfView = 40;
            for (int i = 0; i < 3; i++)
            {
                float angle = i * 120 * Mathf.Deg2Rad;
                Shoot(view, cliff + new Vector3(Mathf.Sin(angle) * 26, 9, Mathf.Cos(angle) * 26), cliff + Vector3.up * 2);
                yield return new WaitForSecondsRealtime(.6f); capture("world-cliff-" + i); yield return new WaitForSecondsRealtime(.4f);
            }
            view.orthographic = true; rig.enabled = true; ui.enabled = true;

            // Cloud shadows from the gameplay camera, zoomed out with plenty of cloud about, then the same view clear.
            var tuning = DevTuning.Local; float cover = tuning.cloudCover, zoom = tuning.cameraZoom, darkness = tuning.cloudShadows, speed = tuning.cloudSpeed;
            tuning.cloudCover = .4f; tuning.cloudSpeed = 0;
            yield return new WaitForSecondsRealtime(3);
            // Clouds are a few hundred metres across: look over far more ground than the gameplay view shows. The
            // camera rig still ticks the clouds; only its framing is overridden for the shots.
            rig.Snap(); yield return null;
            view.orthographicSize = 260; view.transform.SetPositionAndRotation(cliff + new Vector3(0, 420, -300), Quaternion.Euler(55, 0, 0));
            view.farClipPlane = 2000; rig.enabled = false; CloudShadows.Tick(true);
            yield return new WaitForSecondsRealtime(.6f); capture("world-cloud-shadows"); yield return new WaitForSecondsRealtime(.4f);
            Debug.Log("MIA_REVIEW clouds " + CloudShadows.Diagnostics());
            tuning.cloudShadows = 0; CloudShadows.Tick(true); yield return new WaitForSecondsRealtime(.4f); capture("world-cloud-shadows-off"); yield return new WaitForSecondsRealtime(.4f);
            tuning.cloudCover = cover; tuning.cameraZoom = zoom; tuning.cloudShadows = darkness; tuning.cloudSpeed = speed;
            rig.enabled = true;

            // The death ray held on for a second and a half.
            rig.Target = carTarget; Destroy(focus);
            var player = game.Player;
            check("player car present for the death ray review", player && player.Body && player.Weapons);
            if (!player || !player.Body || !player.Weapons) yield break;
            player.Body.isKinematic = false;
            Vector3 open = cliff + new Vector3(40, 0, 40);
            if (GeneratedWorld.Active) open = GeneratedWorld.Active.ClearOfObstacles(open, 12);
            open.y = GeneratedWorld.HeightAt(open) + 1;
            player.Body.position = open; player.Body.linearVelocity = Vector3.zero;
            player.Weapons.ConfigurePlayerPrimary("deathray");
            rig.Snap(); yield return new WaitForSecondsRealtime(1);
            Vector3 aim = player.transform.forward;
            for (float t = 0; t < 1.6f; t += Time.deltaTime)
            {
                player.Weapons.AimAt(aim);
                player.Weapons.FirePrimary(aim);
                if (t > 1.1f && t - Time.deltaTime <= 1.1f) capture("world-death-ray");
                yield return null;
            }
            {
                Vector3 muzzle = player.Weapons.MuzzlePoint, end = player.Weapons.BeamEnd;
                var hits = Physics.SphereCastAll(muzzle, .25f, aim.normalized, 48, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                string first = "";
                foreach (var hit in hits) { if (hit.collider.transform.IsChildOf(player.transform)) continue; first += hit.collider.name + "@" + hit.distance.ToString("0.0") + " "; if (first.Length > 120) break; }
                Debug.Log("MIA_REVIEW beam muzzle=" + muzzle + " end=" + end + " length=" + Vector3.Distance(muzzle, end).ToString("0.0") + " car=" + player.transform.position + " hits: " + first);
            }
            check("the death ray stays on as one beam while held", player.Weapons.BeamActive);
            yield return new WaitForSecondsRealtime(.5f);
            check("the beam goes out when the trigger is released", !player.Weapons.BeamActive);
        }

        static void Shoot(Camera view, Vector3 eye, Vector3 at) => view.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(at - eye, Vector3.up));
        static void Destroy(UnityEngine.Object target) { if (target) UnityEngine.Object.Destroy(target); }
    }
}
