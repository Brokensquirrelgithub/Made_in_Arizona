using System.Collections.Generic;
using UnityEngine;
using static MadeInArizona.WorldArt;

namespace MadeInArizona
{
    /// <summary>
    /// Suzuki's car wash. When the car in the garage is dusty she leaves her rounds at the front of the shop, takes the
    /// nozzle from the hose reel by the side wall and trots a circuit round the lift, stopping at each side, the tail and
    /// the nose to spray it down, the hose trailing along the floor behind her. Each pass rinses dust off
    /// (VehicleDust.Rinse); once the car is clean she drops the nozzle, the reel winds the hose back in along the path
    /// it was laid, and she goes back to her rounds. Purely cosmetic, like the rest of her.
    /// </summary>
    public partial class SuzukiDog
    {
        enum WashStage { None, ToReel, PickUp, Circuit, Spray, ToExit, Retract, Return }
        /// <summary>Route waypoint at the front of the shop where she leaves her rounds for the reel and rejoins them.</summary>
        const int WashStartWaypoint = 2;
        static readonly Vector3 WashGate = new Vector3(5.3f, 0, -8.6f), ReelSpot = new Vector3(11.15f, 0, -9.4f);
        /// <summary>The circuit round the lift (shop floor), clear of the columns, ramps, bed and benches.</summary>
        static readonly Vector3[] WashCircuit = {
            new Vector3(3.4f, 0, -5.6f), new Vector3(3.15f, 0, -2.2f), new Vector3(3.15f, 0, 2.4f), new Vector3(0, 0, 4.8f),
            new Vector3(-3.15f, 0, 2.4f), new Vector3(-3.15f, 0, -2.2f), new Vector3(-3.4f, 0, -5.6f), new Vector3(0, 0, -6.3f) };
        static readonly bool[] SprayStop = { false, true, true, true, true, true, false, true };
        const float DirtyEnough = .06f, CleanEnough = .004f, Trot = 1.55f, SprayTime = 2.4f, HoseStep = .35f;
        static Material waterMaterial, hoseMaterial;
        WashStage washStage;
        int washStep, washLaps;
        float washTimer;
        Transform hoseReel, nozzle;
        LineRenderer hose;
        ParticleSystem spray, mist;
        readonly List<Vector3> hoseTrail = new List<Vector3>();

        /// <summary>The hose reel she fetches the nozzle from (garage only).</summary>
        public Transform HoseReel { set => hoseReel = value; }
        public bool IsWashing => washStage != WashStage.None;
        public bool IsSpraying => washStage == WashStage.Spray;

        static VehicleDust GarageCarDust()
        {
            var game = GameManager.Instance;
            if (!game || (game.State != GameState.Garage && game.State != GameState.MainMenu) || !game.Player) return null;
            return game.Player.GetComponent<VehicleDust>();
        }
        bool WashPending
        {
            get
            {
                if (riding || !hoseReel || washStage != WashStage.None || roamingRoute == null) return false;
                var dust = GarageCarDust();
                return dust && dust.Amount > DirtyEnough;
            }
        }

        void BeginWash()
        {
            washStage = WashStage.ToReel; washLaps = 0; sniffing = false;
            hoseTrail.Clear();
        }

        /// <summary>Runs the wash this frame. Returns true while it controls her movement; sets <paramref name="walking"/>.</summary>
        bool UpdateWash(ref bool walking)
        {
            if (washStage == WashStage.None) return false;
            float dt = Time.deltaTime;
            var dust = GarageCarDust();
            switch (washStage)
            {
                case WashStage.ToReel:
                    walking = WalkTo(ReelSpot, Trot);
                    if (!walking) { washStage = WashStage.PickUp; washTimer = .7f; }
                    break;
                case WashStage.PickUp:
                    walking = false; Face(hoseReel ? hoseReel.localPosition : ReelSpot + Vector3.right);
                    LookAtFloor(true);
                    washTimer -= dt;
                    if (washTimer <= 0)
                    {
                        EnsureHose();
                        nozzle.gameObject.SetActive(true); hose.enabled = true;
                        hoseTrail.Clear(); hoseTrail.Add(transform.localPosition);
                        washStage = WashStage.Circuit; washStep = -1;
                    }
                    break;
                case WashStage.Circuit:
                {
                    // Out through the shop's front gate, then round the circuit.
                    Vector3 next = washStep < 0 ? WashGate : WashCircuit[washStep];
                    walking = WalkTo(next, Trot);
                    if (!walking)
                    {
                        if (washStep >= 0 && SprayStop[washStep] && dust && dust.Amount > CleanEnough) { washStage = WashStage.Spray; washTimer = SprayTime; }
                        else AdvanceCircuit(dust);
                    }
                    break;
                }
                case WashStage.Spray:
                {
                    walking = false;
                    Vector3 car = CarPoint();
                    Face(car);
                    washTimer -= dt;
                    // Sweep the jet along the panel she is facing.
                    float sweep = Mathf.Sin(clock * 2.6f) * 1.4f;
                    Vector3 aim = car + Vector3.Cross(Vector3.up, (car - transform.localPosition).normalized) * sweep + Vector3.up * Mathf.Sin(clock * 1.7f) * .35f;
                    AimNozzle(aim);
                    SetSpray(true);
                    if (dust) dust.Rinse(dt * Mathf.Max(.05f, dust.Amount * .17f));
                    if (washTimer <= 0 || !dust || dust.Amount <= CleanEnough) { SetSpray(false); AdvanceCircuit(dust); }
                    break;
                }
                case WashStage.ToExit:
                    walking = WalkTo(WashGate, Trot);
                    if (!walking)
                    {
                        // Clean: drop the nozzle here and let the reel wind the hose back in along its path.
                        washStage = WashStage.Retract;
                        nozzle.SetParent(transform.parent, true);
                        nozzle.localPosition = new Vector3(nozzle.localPosition.x, .06f, nozzle.localPosition.z);
                    }
                    break;
                case WashStage.Retract:
                {
                    walking = false; LookAtFloor(false);
                    float reel = 7 * dt;
                    while (reel > 0 && hoseTrail.Count > 0)
                    {
                        Vector3 last = hoseTrail[hoseTrail.Count - 1];
                        Vector3 at = nozzle.localPosition; at.y = last.y;
                        float gap = Vector3.Distance(at, last);
                        if (gap <= reel) { nozzle.localPosition = new Vector3(last.x, .06f, last.z); hoseTrail.RemoveAt(hoseTrail.Count - 1); reel -= gap; }
                        else { Vector3 moved = Vector3.MoveTowards(at, last, reel); nozzle.localPosition = new Vector3(moved.x, .06f, moved.z); reel = 0; }
                    }
                    if (hoseTrail.Count == 0)
                    {
                        // Back on the reel.
                        nozzle.SetParent(head, false); nozzle.gameObject.SetActive(false); hose.enabled = false;
                        washStage = WashStage.None; waypoint = WashStartWaypoint + 1; pauseUntil = clock + 1.2f;
                    }
                    break;
                }
            }
            if (washStage == WashStage.Circuit || washStage == WashStage.ToExit) LookAtFloor(false);
            LayHose();
            return washStage != WashStage.None;
        }

        void AdvanceCircuit(VehicleDust dust)
        {
            washStep++;
            if (washStep < WashCircuit.Length) { washStage = WashStage.Circuit; return; }
            washLaps++;
            // Another lap while dust is left (at most three); then back out to the gate.
            if (dust && dust.Amount > CleanEnough && washLaps < 3) { washStep = 0; washStage = WashStage.Circuit; }
            else washStage = WashStage.ToExit;
        }

        /// <summary>Walks (shop space) toward <paramref name="target"/>; false once there.</summary>
        bool WalkTo(Vector3 target, float speed)
        {
            Vector3 delta = target - transform.localPosition; delta.y = 0;
            if (delta.sqrMagnitude < .025f) return false;
            transform.localRotation = Quaternion.Slerp(transform.localRotation, Quaternion.LookRotation(delta), Time.deltaTime * 6);
            Vector3 next = Vector3.MoveTowards(transform.localPosition, target, Time.deltaTime * speed);
            transform.localPosition = new Vector3(next.x, 0, next.z);
            return true;
        }
        void Face(Vector3 point)
        {
            Vector3 delta = point - transform.localPosition; delta.y = 0;
            if (delta.sqrMagnitude > .01f) transform.localRotation = Quaternion.Slerp(transform.localRotation, Quaternion.LookRotation(delta), Time.deltaTime * 6);
        }
        void LookAtFloor(bool down)
        {
            if (!head) return;
            head.localRotation = Quaternion.Slerp(head.localRotation, Quaternion.Euler(down ? 30 : 4 + Mathf.Sin(clock * 3) * 3, 0, 0), Time.deltaTime * 8);
        }
        /// <summary>The middle of the garage car's body, in shop space.</summary>
        Vector3 CarPoint()
        {
            var game = GameManager.Instance;
            Vector3 world = game && game.Player ? game.Player.transform.position + Vector3.up * .55f : transform.parent.TransformPoint(new Vector3(0, 1.4f, 0));
            return transform.parent.InverseTransformPoint(world);
        }
        void AimNozzle(Vector3 point)
        {
            if (!head || !nozzle) return;
            // Head tips up toward the panel; the jet leaves the nozzle in her mouth toward the sweep point.
            Vector3 local = transform.InverseTransformPoint(transform.parent.TransformPoint(point));
            float pitch = -Mathf.Atan2(local.y - .8f, Mathf.Max(.3f, local.z)) * Mathf.Rad2Deg;
            head.localRotation = Quaternion.Slerp(head.localRotation, Quaternion.Euler(Mathf.Clamp(pitch, -35, 20), 0, 0), Time.deltaTime * 8);
            Vector3 tip = nozzle.position + nozzle.forward * .16f;
            Vector3 target = transform.parent.TransformPoint(point);
            spray.transform.SetPositionAndRotation(tip, Quaternion.LookRotation(target - tip));
            mist.transform.SetPositionAndRotation(tip, spray.transform.rotation);
        }
        void SetSpray(bool on)
        {
            if (!spray) return;
            if (on && !spray.isEmitting) { spray.Play(true); mist.Play(true); }
            if (!on && spray.isEmitting) { spray.Stop(true, ParticleSystemStopBehavior.StopEmitting); mist.Stop(true, ParticleSystemStopBehavior.StopEmitting); }
        }

        /// <summary>The hose: from the reel's outlet down to the floor, along the path she walked, up to the nozzle.</summary>
        void LayHose()
        {
            if (!hose || !hose.enabled) return;
            bool carried = nozzle.parent == head;
            Vector3 here = transform.localPosition;
            if (carried && (hoseTrail.Count == 0 || Vector3.Distance(hoseTrail[hoseTrail.Count - 1], here) > HoseStep) && hoseTrail.Count < 400)
                hoseTrail.Add(new Vector3(here.x, 0, here.z));
            var parent = transform.parent;
            int count = hoseTrail.Count + 3;
            hose.positionCount = count;
            Vector3 outlet = hoseReel ? hoseReel.position + Vector3.up * .55f : parent.TransformPoint(ReelSpot + Vector3.up * .55f);
            hose.SetPosition(0, outlet);
            hose.SetPosition(1, parent.TransformPoint(new Vector3(ReelSpot.x, .04f, ReelSpot.z)));
            for (int i = 0; i < hoseTrail.Count; i++) hose.SetPosition(i + 2, parent.TransformPoint(new Vector3(hoseTrail[i].x, .04f, hoseTrail[i].z)));
            hose.SetPosition(count - 1, nozzle.position - nozzle.forward * .1f);
        }

        void EnsureHose()
        {
            if (nozzle) return;
            var brass = new Color(.85f, .62f, .2f);
            nozzle = Group("Hose nozzle", head, new Vector3(0, -.1f, .37f));
            Cylinder("Nozzle grip", nozzle, Vector3.zero, .035f, .2f, brass).transform.localRotation = Quaternion.Euler(90, 0, 0);
            Cylinder("Nozzle tip", nozzle, new Vector3(0, 0, .12f), .022f, .07f, new Color(.55f, .56f, .52f)).transform.localRotation = Quaternion.Euler(90, 0, 0);
            if (!hoseMaterial)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                hoseMaterial = new Material(shader) { name = "Green garden hose" };
                hoseMaterial.SetColor("_BaseColor", new Color(.12f, .42f, .16f)); hoseMaterial.SetFloat("_Smoothness", .55f);
            }
            var line = new GameObject("Garden hose"); line.transform.SetParent(transform.parent, false);
            hose = line.AddComponent<LineRenderer>();
            hose.useWorldSpace = true; hose.widthMultiplier = .055f; hose.numCornerVertices = 3; hose.numCapVertices = 2;
            hose.sharedMaterial = hoseMaterial; hose.alignment = LineAlignment.View;
            hose.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            spray = WaterJet("Hose spray", .08f, .16f, 8, 10.5f, .4f, .65f, 420, 3.5f, new Color(.78f, .9f, 1, .9f), true);
            mist = WaterJet("Hose mist", .3f, .65f, 1.5f, 3.5f, .6f, 1.1f, 55, 16, new Color(.9f, .96f, 1, .2f), false);
        }

        ParticleSystem WaterJet(string name, float minSize, float maxSize, float minSpeed, float maxSpeed, float minLife, float maxLife, float rate, float cone, Color color, bool droplets)
        {
            var go = new GameObject(name); go.transform.SetParent(transform.parent, false);
            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.loop = true; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(minLife, maxLife);
            main.startSpeed = new ParticleSystem.MinMaxCurve(minSpeed, maxSpeed);
            main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
            main.gravityModifier = droplets ? 1 : .15f; main.maxParticles = droplets ? 600 : 120;
            main.startColor = color;
            var emission = system.emission; emission.rateOverTime = rate;
            var shape = system.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = cone; shape.radius = .015f;
            var fade = system.colorOverLifetime; fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(droplets ? .8f : .6f, .6f), new GradientAlphaKey(0, 1) });
            fade.color = gradient;
            if (droplets)
            {
                // Droplets splash off the car and the floor instead of passing through them.
                var collision = system.collision; collision.enabled = true; collision.type = ParticleSystemCollisionType.World;
                collision.mode = ParticleSystemCollisionMode.Collision3D; collision.bounce = .15f; collision.lifetimeLoss = .55f; collision.dampen = .6f;
                collision.quality = ParticleSystemCollisionQuality.Medium;
            }
            else { var grow = system.sizeOverLifetime; grow.enabled = true; grow.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, .6f, 1, 1.6f)); }
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = WaterMaterial;
            renderer.renderMode = droplets ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
            if (droplets) { renderer.velocityScale = .05f; renderer.lengthScale = 1.5f; }
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
            return system;
        }

        static Material WaterMaterial
        {
            get
            {
                if (waterMaterial) return waterMaterial;
                const int size = 32;
                var dot = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "Soft water droplet", wrapMode = TextureWrapMode.Clamp };
                var pixels = new Color[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float u = (x + .5f) / size * 2 - 1, v = (y + .5f) / size * 2 - 1;
                        float a = Mathf.Clamp01(1 - Mathf.Sqrt(u * u + v * v)); a *= a;
                        pixels[y * size + x] = new Color(1, 1, 1, a);
                    }
                dot.SetPixels(pixels); dot.Apply(true, true);
                var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
                waterMaterial = new Material(shader) { name = "Hose water", renderQueue = 3000 };
                waterMaterial.SetFloat("_Surface", 1); waterMaterial.SetFloat("_Blend", 0);
                waterMaterial.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                waterMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                waterMaterial.SetFloat("_ZWrite", 0); waterMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                waterMaterial.SetTexture("_BaseMap", dot); waterMaterial.mainTexture = dot;
                waterMaterial.SetColor("_BaseColor", Color.white);
                return waterMaterial;
            }
        }

        /// <summary>A wall-mounted garden hose reel and spigot by the shop's side wall (shop space).</summary>
        public static Transform BuildHoseReel(Transform shop, Vector3 at)
        {
            var reel = Group("Garden hose reel", shop, at);
            var green = new Color(.12f, .42f, .16f);
            Box("Reel wall bracket", reel, new Vector3(.32f, .55f, 0), new Vector3(.08f, 1.1f, .5f), Ink);
            Box("Reel arm", reel, new Vector3(.12f, .55f, 0), new Vector3(.42f, .07f, .07f), Ink);
            var drum = Cylinder("Reel drum", reel, new Vector3(0, .55f, 0), .34f, .36f, new Color(.2f, .21f, .2f));
            drum.transform.localRotation = Quaternion.Euler(0, 0, 90);
            for (int i = 0; i < 4; i++)
            {
                var coil = Cylinder("Coiled hose", reel, new Vector3(-.12f + i * .08f, .55f, 0), .29f, .07f, green);
                coil.transform.localRotation = Quaternion.Euler(0, 0, 90);
            }
            Box("Spigot", reel, new Vector3(.34f, .25f, .22f), new Vector3(.08f, .08f, .16f), new Color(.6f, .48f, .2f));
            return reel;
        }
    }
}
