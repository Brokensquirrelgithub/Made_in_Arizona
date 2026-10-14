using System;
using System.Collections.Generic;
namespace MadeInArizona
{
    /// <summary>
    /// Cylinder, exhaust and muffler description of one engine for the physical engine voice. The approach follows
    /// AngeTheGreat's engine-sim (MIT): per-cylinder exhaust blowdown, header delays and pipe resonance, here reduced
    /// to what is audible. Angles are crank degrees in the 720° four-stroke cycle, measured from each cylinder's
    /// combustion top dead centre. Free of UnityEngine so the voice can also be rendered offline.
    /// </summary>
    [Serializable]
    public sealed class EngineLayout
    {
        public string id, displayName;
        /// <summary>Whole-engine displacement in litres, and the static compression ratio.</summary>
        public float displacement = 2, compression = 9.5f;
        /// <summary>The game's 850–7200 tachometer range is mapped onto this engine's own idle–redline range.</summary>
        public float idleRpm = 800, redlineRpm = 6500;
        /// <summary>Per cylinder: crank angle of its combustion TDC after cylinder 0's, from the firing order.</summary>
        public float[] firingAngle;
        /// <summary>Per cylinder: header primary length in metres (port to collector). Unequal lengths make the lope.</summary>
        public float[] headerLength;
        /// <summary>Per cylinder: which collector/tailpipe its header feeds.</summary>
        public int[] collector;
        /// <summary>Per collector: pipe length from collector to tailpipe (m) and stereo position (-1 left … 1 right).</summary>
        public float[] pipeLength, pan;
        /// <summary>Exhaust valve opening after combustion TDC, and how long it stays open (crank degrees).</summary>
        public float exhaustOpen = 130, exhaustDuration = 240;
        /// <summary>Relative exhaust valve flow; small engines blow down faster per litre.</summary>
        public float exhaustFlow = 1;
        /// <summary>Open-end reflection of the tailpipe (pipe drone) and the high-frequency loss inside the pipe loop.</summary>
        public float pipeReflection = .55f, pipeDamping = .35f;
        /// <summary>Muffler chamber modes (Hz, Q, gain) added to the low-passed straight-through path.</summary>
        public float[] mufflerHz = { 110, 420, 1250 }, mufflerQ = { 2, 3, 3 }, mufflerGain = { .5f, .35f, .2f };
        public float mufflerCutoff = 3000;
        /// <summary>Cycle-to-cycle combustion spread, flow turbulence noise and overrun afterfire chance.</summary>
        public float combustionVariation = .06f, turbulence = .25f, burble = .05f;
        /// <summary>How sharp each exhaust pulse's front is: high for small, free-revving petrol engines (buzz and rasp),
        /// low for a turbo-diesel (dull thud). Adds the harmonics that separate a petrol bark from a diesel drone.</summary>
        public float edge = .5f;
        /// <summary>Diesel: no throttle plate (load is fuel quantity) and a combustion knock radiated by the block.</summary>
        public bool diesel;
        public float knock;
        public int Cylinders => firingAngle != null ? firingAngle.Length : 0;
        public int Collectors => pipeLength != null ? pipeLength.Length : 0;
    }

    /// <summary>Engine layouts used by vehicle definitions (VehicleDefinition.engineLayout).</summary>
    public static class EngineLayouts
    {
        static readonly Dictionary<string, EngineLayout> all = new Dictionary<string, EngineLayout>();
        public static IEnumerable<EngineLayout> All { get { EnsureBuilt(); return all.Values; } }
        public const string Default = "v8-crossplane";
        static void EnsureBuilt() { if (all.Count == 0) Build(); }
        public static EngineLayout Find(string id)
        {
            EnsureBuilt();
            return id != null && all.TryGetValue(id, out var layout) ? layout : all[Default];
        }
        static void Add(EngineLayout layout) => all[layout.id] = layout;
        static void Build()
        {
            // Geo Metro G10: 993 cc SOHC inline three, firing 1-3-2 every 240° (1.5 pulses a revolution), idling near
            // 800 and revving to about 6000. A short cast 3-into-1 manifold with nearly equal runners feeds a thin pipe
            // through a resonator and a small economy muffler: a buzzy, slightly uneven thrum that turns thin and raspy
            // up the revs, never deep. Sharp pulse edges, damped pipe drone, muffler resonances well above the firing rate.
            Add(new EngineLayout
            {
                id = "inline3", displayName = "1.0 inline three (Geo Metro G10)", displacement = 1f, compression = 9.5f,
                idleRpm = 800, redlineRpm = 6000,
                firingAngle = new float[] { 0, 480, 240 }, headerLength = new[] { .3f, .27f, .33f },
                collector = new[] { 0, 0, 0 }, pipeLength = new[] { 3.3f }, pan = new[] { 0f },
                exhaustOpen = 130, exhaustDuration = 230, exhaustFlow = 1.1f,
                pipeReflection = .3f, pipeDamping = .55f,
                mufflerHz = new float[] { 180, 620, 1800 }, mufflerQ = new float[] { 2.2f, 3, 3 }, mufflerGain = new[] { .4f, .35f, .2f },
                mufflerCutoff = 3000, combustionVariation = .05f, turbulence = .3f, burble = .02f, edge = .6f
            });
            // 1.3 L inline four, 1-3-4-2, cast 4-into-1, economy muffler.
            Add(new EngineLayout
            {
                id = "inline4", displayName = "1.3 inline four", displacement = 1.3f, compression = 8.9f,
                idleRpm = 850, redlineRpm = 6000,
                firingAngle = new float[] { 0, 540, 180, 360 }, headerLength = new[] { .36f, .3f, .3f, .36f },
                collector = new[] { 0, 0, 0, 0 }, pipeLength = new[] { 2.8f }, pan = new[] { 0f },
                mufflerHz = new float[] { 170, 520, 1500 }, mufflerQ = new float[] { 2, 3, 3 }, mufflerGain = new[] { .3f, .35f, .25f },
                mufflerCutoff = 3000, turbulence = .25f, edge = .6f
            });
            // AE86-inspired 1.6 L DOHC four: it revs freely, but it is a car engine rather than a superbike.
            Add(new EngineLayout
            {
                id = "inline4-sport", displayName = "1.6 high-revving inline four", displacement = 1.6f, compression = 9.4f,
                idleRpm = 900, redlineRpm = 7500,
                firingAngle = new float[] { 0, 540, 180, 360 }, headerLength = new[] { .55f, .52f, .52f, .55f },
                collector = new[] { 0, 0, 0, 0 }, pipeLength = new[] { 1.8f }, pan = new[] { .05f },
                exhaustOpen = 125, exhaustDuration = 245, exhaustFlow = 1.1f, pipeReflection = .48f, pipeDamping = .32f,
                mufflerHz = new float[] { 165, 580, 1750 }, mufflerQ = new float[] { 2, 2.8f, 3 }, mufflerGain = new[] { .4f, .4f, .25f },
                mufflerCutoff = 4300, combustionVariation = .04f, turbulence = .15f, burble = .05f, edge = .65f
            });
            // Garage 1.8 L swap: a conventional salvage-yard four with a freer exhaust than the stock economy engine.
            Add(new EngineLayout
            {
                id = "inline4-swap", displayName = "1.8 salvage-yard inline four", displacement = 1.8f, compression = 9.5f,
                idleRpm = 850, redlineRpm = 6500,
                firingAngle = new float[] { 0, 540, 180, 360 }, headerLength = new[] { .48f, .44f, .44f, .48f },
                collector = new[] { 0, 0, 0, 0 }, pipeLength = new[] { 2.2f }, pan = new[] { 0f },
                mufflerHz = new float[] { 150, 500, 1500 }, mufflerQ = new float[] { 2, 2.7f, 3 }, mufflerGain = new[] { .45f, .4f, .2f },
                mufflerCutoff = 3800, combustionVariation = .05f, turbulence = .21f, edge = .6f
            });
            // Jetta-inspired 2.5 L five, firing 1-2-4-5-3 at 144-degree intervals.
            Add(new EngineLayout
            {
                id = "inline5", displayName = "2.5 inline five", displacement = 2.5f, compression = 9.5f,
                idleRpm = 750, redlineRpm = 6200,
                firingAngle = new float[] { 0, 144, 576, 288, 432 }, headerLength = new[] { .41f, .38f, .43f, .36f, .4f },
                collector = new[] { 0, 0, 0, 0, 0 }, pipeLength = new[] { 3f }, pan = new[] { 0f },
                mufflerHz = new float[] { 115, 420, 1250 }, mufflerQ = new float[] { 2, 2.7f, 3 }, mufflerGain = new[] { .45f, .35f, .2f },
                mufflerCutoff = 3200, combustionVariation = .045f, turbulence = .22f, edge = .55f
            });
            // Outback-inspired 2.5 L boxer four; unequal runner lengths give its low off-beat character.
            Add(new EngineLayout
            {
                id = "boxer4", displayName = "2.5 boxer four", displacement = 2.5f, compression = 10f,
                idleRpm = 750, redlineRpm = 6200,
                firingAngle = new float[] { 0, 360, 180, 540 }, headerLength = new[] { .42f, .93f, .38f, 1.02f },
                collector = new[] { 0, 0, 0, 0 }, pipeLength = new[] { 2.7f }, pan = new[] { 0f },
                mufflerHz = new float[] { 105, 360, 1080 }, mufflerQ = new float[] { 2, 2.5f, 3 }, mufflerGain = new[] { .5f, .35f, .2f },
                mufflerCutoff = 3000, combustionVariation = .05f, turbulence = .2f, edge = .5f
            });
            // Skitter: a 1.0 L four-stroke V3, two cylinders in the front bank and one at the back. The shared crank gives
            // uneven firing (255°, 210°, 255°) and an off-beat lope; a short 3-into-1 (the rear runner longer) into a
            // barely muffled can. Revs high like a bike engine.
            Add(new EngineLayout
            {
                id = "v3-bike", displayName = "1.0 V3", displacement = 1f, compression = 12.5f,
                idleRpm = 1200, redlineRpm = 11000,
                firingAngle = new float[] { 0, 255, 465 }, headerLength = new[] { .45f, .5f, .75f },
                collector = new[] { 0, 0, 0 }, pipeLength = new[] { .9f }, pan = new[] { .1f },
                exhaustOpen = 125, exhaustDuration = 250, exhaustFlow = 1.15f, pipeReflection = .55f, pipeDamping = .3f,
                mufflerHz = new float[] { 190, 680, 2100 }, mufflerQ = new float[] { 1.8f, 2.5f, 3 }, mufflerGain = new[] { .4f, .4f, .25f },
                mufflerCutoff = 5000, combustionVariation = .06f, turbulence = .15f, burble = .1f, edge = .6f
            });
            // 3.0 L 60° V6, 1-4-2-5-3-6, two manifolds joined by a Y-pipe (the crossover side runs longer).
            Add(new EngineLayout
            {
                id = "v6", displayName = "3.0 V6", displacement = 3f, compression = 9.3f,
                idleRpm = 700, redlineRpm = 5600,
                firingAngle = new float[] { 0, 240, 480, 120, 360, 600 }, headerLength = new[] { .42f, .38f, .45f, 1.05f, 1f, 1.1f },
                collector = new[] { 0, 0, 0, 0, 0, 0 }, pipeLength = new[] { 3.6f }, pan = new[] { 0f },
                mufflerHz = new float[] { 82, 300, 900 }, mufflerQ = new float[] { 2, 3, 3 }, mufflerGain = new[] { .5f, .3f, .15f },
                mufflerCutoff = 2600, turbulence = .22f
            });
            // Cross-plane V8, GM order 1-8-4-3-6-5-7-2, odd cylinders left. Each bank's pulses arrive 270/180/90/180°
            // apart: dual exhausts keep the banks separate, which is where the burble comes from.
            Add(new EngineLayout
            {
                id = Default, displayName = "6.2 cross-plane V8, dual long-tube", displacement = 6.2f, compression = 10.4f,
                idleRpm = 750, redlineRpm = 6600,
                firingAngle = new float[] { 0, 630, 270, 180, 450, 360, 540, 90 },
                headerLength = new[] { .92f, .95f, .9f, .97f, .94f, .9f, .96f, .93f },
                collector = new[] { 0, 1, 0, 1, 0, 1, 0, 1 }, pipeLength = new[] { 1.3f, 1.25f }, pan = new[] { -.35f, .35f },
                pipeReflection = .65f, pipeDamping = .3f,
                mufflerHz = new float[] { 78, 290, 880 }, mufflerQ = new float[] { 1.8f, 2.5f, 3 }, mufflerGain = new[] { .6f, .4f, .25f },
                mufflerCutoff = 4600, combustionVariation = .07f, turbulence = .28f, burble = .1f
            });
            // Truck cross-plane V8, Ford order 1-5-4-2-6-3-7-8, single exhaust: the left bank reaches the Y-pipe
            // through a long crossover, so the banks' pulses merge with a lag.
            Add(new EngineLayout
            {
                id = "v8-truck", displayName = "5.0 cross-plane V8, single exhaust", displacement = 5f, compression = 9f,
                idleRpm = 700, redlineRpm = 5200,
                firingAngle = new float[] { 0, 270, 450, 180, 90, 360, 540, 630 },
                headerLength = new[] { .5f, .48f, .52f, .5f, 1.3f, 1.28f, 1.32f, 1.3f },
                collector = new[] { 0, 0, 0, 0, 0, 0, 0, 0 }, pipeLength = new[] { 3.8f }, pan = new[] { .15f },
                mufflerHz = new float[] { 70, 250, 760 }, mufflerQ = new float[] { 2, 3, 3 }, mufflerGain = new[] { .55f, .35f, .2f },
                mufflerCutoff = 2600, combustionVariation = .08f, turbulence = .25f, burble = .08f
            });
            // The DeVille-inspired sedan has a quiet 4.5 L cross-plane V8, not the truck's 5.0 L voice.
            Add(new EngineLayout
            {
                id = "v8-luxury", displayName = "4.5 luxury cross-plane V8", displacement = 4.5f, compression = 9.5f,
                idleRpm = 700, redlineRpm = 5200,
                firingAngle = new float[] { 0, 630, 270, 180, 450, 360, 540, 90 },
                headerLength = new[] { .45f, .48f, .46f, .5f, .47f, .45f, .49f, .46f },
                collector = new[] { 0, 0, 0, 0, 0, 0, 0, 0 }, pipeLength = new[] { 4.1f }, pan = new[] { 0f },
                pipeReflection = .4f, pipeDamping = .55f,
                mufflerHz = new float[] { 68, 230, 700 }, mufflerQ = new float[] { 2, 2.5f, 3 }, mufflerGain = new[] { .55f, .35f, .15f },
                mufflerCutoff = 2200, combustionVariation = .04f, turbulence = .14f, burble = .03f, edge = .35f
            });
            // Flat-plane V8, 1-5-3-7-4-8-2-6: each bank is an even-firing four, one collector per bank. Raw and high.
            Add(new EngineLayout
            {
                id = "v8-flatplane", displayName = "6.0 flat-plane V8", displacement = 6f, compression = 11f,
                idleRpm = 900, redlineRpm = 7800,
                firingAngle = new float[] { 0, 540, 180, 360, 90, 630, 270, 450 },
                headerLength = new[] { .7f, .68f, .68f, .7f, .7f, .68f, .68f, .7f },
                collector = new[] { 0, 0, 0, 0, 1, 1, 1, 1 }, pipeLength = new[] { 1.6f, 1.6f }, pan = new[] { -.4f, .4f },
                exhaustOpen = 125, exhaustDuration = 250, pipeReflection = .6f, pipeDamping = .28f,
                mufflerHz = new float[] { 120, 440, 1500 }, mufflerQ = new float[] { 1.8f, 2.5f, 3 }, mufflerGain = new[] { .45f, .45f, .3f },
                mufflerCutoff = 5200, combustionVariation = .05f, turbulence = .2f, burble = .12f, edge = .6f
            });
            // 6.7 L turbo-diesel inline six, 1-5-3-6-2-4. The turbine absorbs much of the pulse energy; the block
            // radiates combustion knock.
            Add(new EngineLayout
            {
                id = "inline6-diesel", displayName = "6.7 turbo-diesel inline six", displacement = 6.7f, compression = 17.3f,
                idleRpm = 700, redlineRpm = 3300,
                firingAngle = new float[] { 0, 480, 240, 600, 120, 360 },
                headerLength = new[] { .62f, .5f, .38f, .3f, .42f, .55f },
                collector = new[] { 0, 0, 0, 0, 0, 0 }, pipeLength = new[] { 4.5f }, pan = new[] { .2f },
                exhaustOpen = 120, exhaustDuration = 235, pipeReflection = .45f, pipeDamping = .5f,
                mufflerHz = new float[] { 62, 210, 640 }, mufflerQ = new float[] { 2, 3, 3 }, mufflerGain = new[] { .6f, .35f, .2f },
                mufflerCutoff = 1800, combustionVariation = .05f, turbulence = .18f, burble = 0, edge = .3f,
                diesel = true, knock = .5f
            });
        }
        /// <summary>Layout for vehicles whose authored asset predates the engineLayout field.</summary>
        public static string ForVehicle(string vehicleId)
        {
            switch (vehicleId)
            {
                case "shift": case "scarface": case "heat": case "espresso": return "inline3";
                case "ronin": case "citizen": case "roam4": return "inline4";
                case "monsoon": return "inline5";
                case "dustback": return "boxer4";
                case "ae867": return "inline4-sport";
                case "sonora": case "monument": return "v6";
                case "executive": return "v8-luxury";
                case "copperado": return "v8-truck";
                case "contender": return Default;
                case "juniper": return "inline4";
                case "skitter": return "v3-bike";
                case "sidehustle": return "v6";
                case "perennial": return "v8-truck";
                case "foreclosure": return "inline6-diesel";
                case "vincent": return "v8-flatplane";
                default: return Default;
            }
        }
    }
}
