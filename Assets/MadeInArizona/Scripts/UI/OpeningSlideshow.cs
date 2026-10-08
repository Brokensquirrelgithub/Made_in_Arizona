using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MadeInArizona
{
    public sealed partial class GameUI
    {
        const float OpeningSecondsPerSlide = 6f;
        const float OpeningFadeSeconds = .8f;
        static readonly string[] OpeningImagePaths = {
            "Intro/slide-01", "Intro/slide-02", "Intro/slide-03", "Intro/slide-04", "Intro/slide-05"
        };
        static readonly Vector2[] OpeningDrift = {
            new Vector2(-.5f, .2f), new Vector2(.5f, -.2f), new Vector2(-.3f, -.3f),
            new Vector2(.35f, .25f), new Vector2(-.45f, .1f)
        };

        bool introActive;
        int introSlide;
        float introSlideStarted;
        int introSeed;
        float introWorldSize;
        Texture2D[] introImages;

        static string OpeningYearCaption()
        {
            try { return "The year is " + (DateTime.Now.Year + 2); }
            catch { return "The year is 2026"; }
        }

        string OpeningCaption(int slide)
        {
            switch (slide)
            {
                case 0: return OpeningYearCaption();
                case 1: return "Arizona has become a lawless, gearhead haven full of dust, gas and gunfire.";
                case 2: return "It was like that 2 years ago, just now it’s a little bit more intense.";
                case 3: return "Nothing in particular happened to spur this on, it was completely natural sequence of events.";
                default: return "A young man who goes by “Stallion” has gotten a job at 117 degrees garage.";
            }
        }

        void BeginOpening(int seed, float size)
        {
            introSeed = seed;
            introWorldSize = size;
            introImages = new Texture2D[OpeningImagePaths.Length];
            for (int i = 0; i < introImages.Length; i++)
                introImages[i] = Resources.Load<Texture2D>(OpeningImagePaths[i]);
            introSlide = 0;
            introSlideStarted = Time.unscaledTime;
            introActive = true;
            GUI.FocusControl(null);
        }

        void FinishOpening()
        {
            if (!introActive) return;
            introActive = false;
            introImages = null;
            game.StartCampaign(introSeed, introWorldSize);
        }

        void TickOpening()
        {
            var keyboard = Keyboard.current;
            var pad = Gamepad.current;
            if ((keyboard != null && (keyboard.escapeKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame)) ||
                (pad != null && (pad.buttonEast.wasPressedThisFrame || pad.startButton.wasPressedThisFrame)))
            {
                FinishOpening();
                return;
            }
            while (Time.unscaledTime - introSlideStarted >= OpeningSecondsPerSlide)
            {
                introSlide++;
                introSlideStarted += OpeningSecondsPerSlide;
                if (introSlide >= OpeningImagePaths.Length) { FinishOpening(); return; }
            }
        }

        void DrawOpening()
        {
            Rect(0, 0, width, height, Color.black);
            float elapsed = Mathf.Clamp(Time.unscaledTime - introSlideStarted, 0, OpeningSecondsPerSlide);
            float progress = elapsed / OpeningSecondsPerSlide;
            var image = introImages[introSlide];
            if (image)
            {
                float viewportAspect = width / height;
                float imageAspect = (float)image.width / image.height;
                float zoom = Mathf.Lerp(1.06f, 1.16f, Mathf.SmoothStep(0, 1, progress));
                float uvWidth = Mathf.Min(1, viewportAspect / imageAspect) / zoom;
                float uvHeight = Mathf.Min(1, imageAspect / viewportAspect) / zoom;
                Vector2 drift = OpeningDrift[introSlide];
                float u = (1 - uvWidth) * (.5f + drift.x * (progress - .5f));
                float v = (1 - uvHeight) * (.5f + drift.y * (progress - .5f));
                GUI.DrawTextureWithTexCoords(new Rect(0, 0, width, height), image,
                    new Rect(u, v, uvWidth, uvHeight), true);
            }

            float panelWidth = Mathf.Min(width - 80, 1050);
            float x = (width - panelWidth) * .5f;
            float y = height - 178;
            Rect(x - 22, y - 16, panelWidth + 44, 119, new Color(0, 0, 0, .58f));
            Text(x, y, panelWidth, 88, OpeningCaption(introSlide),
                Mathf.RoundToInt(Mathf.Clamp(width / 40f, 24, 36)), Color.white,
                true, TextAnchor.MiddleCenter);
            Text(34, 28, 220, 25, (introSlide + 1) + " / " + OpeningImagePaths.Length,
                14, new Color(1, 1, 1, .72f), true);
            if (Button(width - 190, 25, 155, 36, "SKIP  /  ESC", false, true, 13)) FinishOpening();
            // Cover the image and UI together so each transition reaches a completely black frame.
            float visibility = Mathf.Min(elapsed / OpeningFadeSeconds,
                (OpeningSecondsPerSlide - elapsed) / OpeningFadeSeconds);
            Rect(0, 0, width, height, new Color(0, 0, 0, 1 - Mathf.Clamp01(visibility)));
        }
    }
}
