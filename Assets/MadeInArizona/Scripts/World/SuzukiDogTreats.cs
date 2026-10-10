using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using static MadeInArizona.WorldArt;

namespace MadeInArizona
{
    /// <summary>
    /// Fuss for the garage dog. A click on Suzuki's head sends a heart up from it; a click on her bowl drops a bone in.
    /// Neither does anything but please her: nothing is saved, and a bowl holding five bones is emptied before the next.
    /// </summary>
    public partial class SuzukiDog
    {
        // Both targets are small on screen from the garage camera, so a click may land a little wide of them.
        const float HeadReach = .42f, BowlReach = .46f;
        const float HeartLife = 1.5f, HeartRise = .8f, BoneDropHeight = 1.7f, FussTime = 1.8f;
        /// <summary>Height of the bowl's rim above its centre (the bowl is a cylinder 0.21 m tall).</summary>
        const float BowlRim = .105f;
        const int MaxHearts = 8, MaxBones = 5;

        struct Heart { public Transform body; public Vector3 from; public float born, phase; }
        sealed class Bone { public Transform body; public float rest, speed, spin, turn, yaw, tilt, ease; public bool landed, settled; }
        struct Drop { public Transform body; public Vector3 velocity; public float born; }
        readonly List<Heart> hearts = new List<Heart>();
        readonly List<Bone> bones = new List<Bone>();
        readonly List<Drop> drops = new List<Drop>();
        Transform bowl;
        float fussUntil;

        /// <summary>Her bowl in the garage. Only a dog with a bowl answers clicks; the one riding in the car has none.</summary>
        public Transform Bowl { get => bowl; set => bowl = value; }
        public Vector3 HeadPosition => head ? head.position : transform.position;
        /// <summary>For a moment after a pat or a bone her tail goes double time and she looks up.</summary>
        public bool IsFussed => clock < fussUntil;
        public int HeartsGiven { get; private set; }
        public int BonesGiven { get; private set; }
        public int HeartsShowing => hearts.Count;
        public int BonesInBowl { get { int lying = 0; foreach (var bone in bones) if (bone.settled) lying++; return lying; } }
        /// <summary>Where the newest bone is.</summary>
        public Vector3 NewestBone => bones.Count > 0 && bones[bones.Count - 1].body ? bones[bones.Count - 1].body.position : Vector3.zero;

        void UpdateTreats()
        {
            if (!bowl) return;
            Animate();
            var mouse = Mouse.current; var game = GameManager.Instance; var view = Camera.main;
            if (mouse == null || !view || !game || game.State != GameState.Garage || !mouse.leftButton.wasPressedThisFrame) return;
            Vector2 pointer = mouse.position.ReadValue();
            if (!GameUI.ShopUnderPointer(pointer)) return;
            Ray ray = view.ScreenPointToRay(pointer);
            bool onHead = Reaches(ray, HeadPosition, HeadReach, out float headAt), onBowl = Reaches(ray, bowl.position, BowlReach, out float bowlAt);
            if (!onHead && !onBowl) return;
            bool pat = onHead && (!onBowl || headAt <= bowlAt);
            // Anything solid in the way (the car, a lift column, a wall) takes the click instead.
            if (Physics.Raycast(ray, Mathf.Max(0, (pat ? headAt : bowlAt) - .5f), Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return;
            if (pat) Pat(); else Treat();
        }

        /// <summary>Whether a ray passes within <paramref name="reach"/> of a point ahead of it, and how far along.</summary>
        static bool Reaches(Ray ray, Vector3 point, float reach, out float along)
        {
            Vector3 to = point - ray.origin; along = Vector3.Dot(to, ray.direction);
            return along > 0 && (to - ray.direction * along).sqrMagnitude <= reach * reach;
        }

        /// <summary>A pat on the head: a heart floats up from it.</summary>
        public void Pat()
        {
            HeartsGiven++; fussUntil = clock + FussTime;
            if (AudioManager.Instance) AudioManager.Instance.PlayUI();
            if (hearts.Count >= MaxHearts) return;
            Vector3 from = HeadPosition + Vector3.up * .36f;
            hearts.Add(new Heart { body = BuildHeart(transform.parent, from), from = from, born = clock, phase = HeartsGiven * 2.4f });
        }

        /// <summary>A bone for the bowl: it drops in from above, hops twice and lies where it lands.</summary>
        public void Treat()
        {
            BonesGiven++; fussUntil = clock + FussTime;
            if (bones.Count >= MaxBones)
            {
                foreach (var old in bones) if (old.body) Destroy(old.body.gameObject);
                bones.Clear();
            }
            // Each lands a little off the last and turned a different way, so a bowlful reads as a pile.
            float angle = BonesGiven * 2.4f, off = .02f + .035f * (bones.Count % 3);
            Transform body = BuildBone(bowl.parent);
            body.position = bowl.position + new Vector3(Mathf.Cos(angle) * off, BoneDropHeight, Mathf.Sin(angle) * off);
            bones.Add(new Bone { body = body, rest = bowl.position.y + BowlRim + .035f + bones.Count * .045f, spin = 420 + BonesGiven % 3 * 90,
                yaw = angle * Mathf.Rad2Deg * 1.7f, tilt = BonesGiven % 2 == 0 ? 14 : -11 });
        }

        void Animate()
        {
            Transform view = Camera.main ? Camera.main.transform : null;
            float dt = Time.deltaTime;
            for (int i = hearts.Count - 1; i >= 0; i--)
            {
                Heart heart = hearts[i];
                float age = clock - heart.born, t = age / HeartLife;
                if (t >= 1 || !heart.body) { if (heart.body) Destroy(heart.body.gameObject); hearts.RemoveAt(i); continue; }
                // Pops out past full size and settles, drifts up with a sway and a pulse, and shrinks away at the top.
                float pop = Mathf.Clamp01(age / .2f) - 1; pop = 1 + 1.6f * pop * pop * pop + .6f * pop * pop;
                float size = pop * Mathf.Clamp01((HeartLife - age) / .35f) * (1 + .07f * Mathf.Sin(age * 13));
                heart.body.position = heart.from + Vector3.up * HeartRise * (1 - (1 - t) * (1 - t)) + (view ? view.right : Vector3.right) * Mathf.Sin(age * 4.5f + heart.phase) * .06f;
                heart.body.localScale = Vector3.one * Mathf.Max(0, size);
                if (view) heart.body.rotation = view.rotation;
            }
            foreach (var bone in bones)
            {
                if (!bone.body || (bone.settled && bone.ease >= 1)) continue;
                Vector3 p = bone.body.position;
                if (!bone.settled)
                {
                    bone.speed += 9.81f * dt; p.y -= bone.speed * dt;
                    if (p.y <= bone.rest)
                    {
                        p.y = bone.rest;
                        if (!bone.landed) { bone.landed = true; Splash(p); }
                        if (bone.speed > 1.4f) { bone.speed *= -.34f; bone.spin *= .35f; }
                        else { bone.settled = true; bone.speed = 0; }
                    }
                    bone.turn += bone.spin * dt;
                    bone.body.position = p;
                }
                // It tumbles end over end on the way down and comes to rest lying across the bowl.
                if (bone.landed) bone.ease = Mathf.Min(1, bone.ease + dt * 5);
                bone.body.rotation = Quaternion.Slerp(Quaternion.Euler(bone.turn, bone.yaw, bone.turn * .6f), Quaternion.Euler(0, bone.yaw, bone.tilt), bone.ease);
            }
            for (int i = drops.Count - 1; i >= 0; i--)
            {
                Drop drop = drops[i];
                float age = clock - drop.born;
                if (age >= .55f || !drop.body) { if (drop.body) Destroy(drop.body.gameObject); drops.RemoveAt(i); continue; }
                drop.velocity.y -= 9.81f * dt;
                drop.body.position += drop.velocity * dt;
                drop.body.localScale = Vector3.one * .045f * (1 - age / .55f);
                drops[i] = drop;
            }
        }

        /// <summary>It is a water bowl: the bone goes in with a splash.</summary>
        void Splash(Vector3 at)
        {
            if (AudioManager.Instance) AudioManager.Instance.PlayUI();
            for (int i = 0; i < 6; i++)
            {
                float angle = i * Mathf.PI / 3 + BonesGiven;
                var drop = Shape("Bowl splash", bowl.parent, PrimitiveType.Sphere, Vector3.zero, Vector3.one * .045f, Turquoise);
                drop.transform.position = at;
                drop.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                drops.Add(new Drop { body = drop.transform, velocity = new Vector3(Mathf.Cos(angle) * .9f, 2.1f + i % 2 * .5f, Mathf.Sin(angle) * .9f), born = clock });
            }
        }

        /// <summary>A heart: a square stood on its corner with a round lobe on each upper edge, glowing so it reads from the garage camera.</summary>
        static Transform BuildHeart(Transform parent, Vector3 at)
        {
            Transform heart = Group("Suzuki heart", parent, Vector3.zero);
            heart.position = at; heart.localScale = Vector3.zero;
            const float size = .16f;
            float corner = size / Mathf.Sqrt(2);
            Color red = new Color(1f, .16f, .32f);
            var size3 = new Vector3(size, size, size * .55f);
            Shape("Heart point", heart, PrimitiveType.Cube, Vector3.zero, size3, red, false, 0, .5f, 1.5f).transform.localRotation = Quaternion.Euler(0, 0, 45);
            for (int side = -1; side <= 1; side += 2)
                Shape("Heart lobe", heart, PrimitiveType.Sphere, new Vector3(side * corner * .5f, corner * .5f, 0), size3, red, false, 0, .5f, 1.5f);
            foreach (var renderer in heart.GetComponentsInChildren<Renderer>()) renderer.shadowCastingMode = ShadowCastingMode.Off;
            return heart;
        }

        /// <summary>A cartoon bone: a shaft with a pair of knuckles at each end.</summary>
        static Transform BuildBone(Transform parent)
        {
            Transform bone = Group("Suzuki bone", parent, Vector3.zero);
            Color ivory = new Color(.95f, .92f, .82f);
            Beam("Bone shaft", bone, new Vector3(-.11f, 0, 0), new Vector3(.11f, 0, 0), .028f, ivory);
            for (int end = -1; end <= 1; end += 2)
                for (int knuckle = -1; knuckle <= 1; knuckle += 2)
                    Shape("Bone knuckle", bone, PrimitiveType.Sphere, new Vector3(end * .125f, 0, knuckle * .034f), Vector3.one * .075f, ivory);
            return bone;
        }
    }
}
