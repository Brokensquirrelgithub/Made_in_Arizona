using System;
namespace MadeInArizona
{
    /// <summary>
    /// Real-time physical engine sound, after AngeTheGreat's engine-sim (MIT) with its expensive stages simplified:
    /// <list type="bullet">
    /// <item>The rigid-body crank solver is replaced by a kinematic crank driven by the game's RPM.</item>
    /// <item>Gas dynamics are reduced to the exhaust side: compression, combustion and expansion are solved in closed
    /// form per cycle, and only the blowdown through the opening exhaust valve is integrated, once per sample,
    /// with an incompressible orifice (choke-limited) instead of eight compressible-flow substeps.</item>
    /// <item>Each cylinder's exhaust runner pressure travels a header delay to its collector, as in engine-sim.</item>
    /// <item>The 10,000-tap impulse response convolution becomes a tailpipe waveguide (open-end reflection with
    /// loop damping) and a few muffler modes over a low-passed straight-through path.</item>
    /// </list>
    /// The simulation runs at half the output rate when the output is 44.1 kHz or more and is upsampled linearly.
    /// Allocation-free after construction; safe to render on the audio thread while the main thread writes inputs.
    /// </summary>
    public sealed class EngineVoice
    {
        const float Gamma = 1.32f, SoundSpeed = 520f; // hot exhaust gas
        const float CalibrationRms = .2f;

        public readonly EngineLayout Layout;
        public readonly int OutputRate, SimRate;
        // Inputs, written from the main thread and read once per rendered block.
        public volatile float TargetRpm, Throttle, Gain;
        /// <summary>Live shaping from the dev tuning sliders (1 = layout as authored).</summary>
        public volatile float Variation = 1, Rasp = 1, Body = 1, Drive = 1, OverrunLevel = 1, LoadLevel = 1;
        /// <summary>Turbo whistle and hiss level (dev slider; the exhaust-side turbo effects are not scaled).</summary>
        public volatile float TurboLevel = 1;
        /// <summary>Fraction of real time spent rendering, smoothed (0.01 = 1% of one core).</summary>
        public float Load { get; private set; }
        /// <summary>A turbocharger sits between the collectors and the tailpipes.</summary>
        public readonly bool Turbocharged;
        /// <summary>Turbo shaft speed, 0 (idle) to 1 (full boost). Read by the game for blow-off timing.</summary>
        public float Spool => spool;

        // Turbo: a small wastegated unit. Exhaust energy (load × revs) spins the shaft up over about a second and it
        // coasts down more slowly; boost raises the trapped charge, and the turbine wheel takes energy out of each
        // exhaust pulse and smears its sharp edge, so a turbo engine is softer at the tailpipe but whistles.
        const float MaxBoost = .7f, SpoolUp = 1.6f, SpoolDown = .9f, TurbineCutoff = 900, TurbineSmoothing = .55f, TurbineAbsorb = .4f;
        const float WhistleLevel = .07f, HissLevel = .12f;
        float spool, whistlePhase; readonly float turbineCoefficient; readonly float[] turbineLow; Biquad hissBand;

        readonly int cylinders, collectors, divider;
        readonly float clearance, flowConstant, crankStep, idleRpm, redlineRpm;
        readonly float[] firing, runnerVolume, cylinderGas, cylinderPressure, runnerGas, runnerDrain;
        readonly bool[] open;
        readonly float[] lastAngle;
        readonly float[][] headerLine; readonly int[] headerMask; readonly float[] headerDelay; int headerWrite;
        readonly int[] collector;
        readonly float[] collectorSum, dcState, panLeft, panRight;
        readonly float[][] pipeLine; readonly int pipeMask; readonly float[] pipeDelay, pipeLow; int pipeWrite;
        readonly Biquad[][] modes; readonly Biquad[] lowPass;
        Biquad knockMode; float knockAmount;
        float crank, rpm, throttle, noiseLow, outputGain = 1, appliedGain, envelope, leveler = 1, lastLeft, lastRight, nextLeft, nextRight;
        int phase; uint seed = 0x2545F491;

        struct Biquad
        {
            float b0, b1, b2, a1, a2, z1, z2;
            public static Biquad BandPass(float hz, float q, float rate)
            {
                float w = 2 * MathF.PI * MathF.Min(hz, rate * .45f) / rate, alpha = MathF.Sin(w) / (2 * q), a0 = 1 + alpha;
                return new Biquad { b0 = alpha / a0, b1 = 0, b2 = -alpha / a0, a1 = -2 * MathF.Cos(w) / a0, a2 = (1 - alpha) / a0 };
            }
            public static Biquad LowPass(float hz, float q, float rate)
            {
                float w = 2 * MathF.PI * MathF.Min(hz, rate * .45f) / rate, alpha = MathF.Sin(w) / (2 * q), cos = MathF.Cos(w), a0 = 1 + alpha;
                return new Biquad { b0 = (1 - cos) * .5f / a0, b1 = (1 - cos) / a0, b2 = (1 - cos) * .5f / a0, a1 = -2 * cos / a0, a2 = (1 - alpha) / a0 };
            }
            public float Step(float x)
            {
                float y = b0 * x + z1; z1 = b1 * x - a1 * y + z2; z2 = b2 * x - a2 * y;
                return y;
            }
            public void Clear() { z1 = z2 = 0; }
        }

        public EngineVoice(EngineLayout layout, int outputRate, bool turbocharged = false)
        {
            Layout = layout; OutputRate = outputRate; Turbocharged = turbocharged;
            divider = outputRate >= 44100 ? 2 : 1; SimRate = outputRate / divider;
            cylinders = layout.Cylinders; collectors = Math.Max(1, layout.Collectors);
            idleRpm = layout.idleRpm; redlineRpm = Math.Max(layout.idleRpm + 500, layout.redlineRpm);
            // Volumes are in units of one cylinder's swept volume; pressures in atmospheres; gas amount = p·V.
            clearance = 1 / Math.Max(1.5f, layout.compression - 1);
            float cylinderLitres = layout.displacement / Math.Max(1, cylinders);
            // Valve area grows with bore², the charge with bore³: small cylinders empty faster per unit volume.
            flowConstant = 640 * layout.exhaustFlow * MathF.Pow(.5f / Math.Max(.05f, cylinderLitres), 1 / 3f);
            crankStep = 360f / 60f / SimRate;
            firing = new float[cylinders]; runnerVolume = new float[cylinders]; runnerDrain = new float[cylinders];
            cylinderGas = new float[cylinders]; cylinderPressure = new float[cylinders]; runnerGas = new float[cylinders];
            open = new bool[cylinders]; lastAngle = new float[cylinders]; collector = new int[cylinders];
            headerLine = new float[cylinders][]; headerMask = new int[cylinders]; headerDelay = new float[cylinders];
            for (int i = 0; i < cylinders; i++)
            {
                firing[i] = layout.firingAngle[i];
                float length = layout.headerLength != null && i < layout.headerLength.Length ? layout.headerLength[i] : .5f;
                runnerVolume[i] = .6f + 1.2f * length;
                runnerDrain[i] = runnerVolume[i] / .0015f; // about 1.5 ms to settle back to the collector
                runnerGas[i] = runnerVolume[i];
                collector[i] = layout.collector != null && i < layout.collector.Length ? Math.Clamp(layout.collector[i], 0, collectors - 1) : 0;
                headerDelay[i] = length / SoundSpeed * SimRate;
                int size = NextPowerOfTwo((int)headerDelay[i] + 4);
                headerLine[i] = new float[size]; headerMask[i] = size - 1;
            }
            collectorSum = new float[collectors]; dcState = new float[collectors];
            panLeft = new float[collectors]; panRight = new float[collectors];
            pipeDelay = new float[collectors]; pipeLow = new float[collectors];
            float longest = 0;
            for (int c = 0; c < collectors; c++)
            {
                float pipe = layout.pipeLength != null && c < layout.pipeLength.Length ? layout.pipeLength[c] : 2;
                pipeDelay[c] = 2 * pipe / SoundSpeed * SimRate; // round trip to the open end and back
                longest = Math.Max(longest, pipeDelay[c]);
                float pan = layout.pan != null && c < layout.pan.Length ? Math.Clamp(layout.pan[c], -1, 1) : 0;
                float angle = (pan + 1) * MathF.PI * .25f;
                panLeft[c] = MathF.Cos(angle); panRight[c] = MathF.Sin(angle);
            }
            int pipeSize = NextPowerOfTwo((int)longest + 4); pipeMask = pipeSize - 1;
            pipeLine = new float[collectors][];
            int modeCount = layout.mufflerHz != null ? layout.mufflerHz.Length : 0;
            modes = new Biquad[collectors][]; lowPass = new Biquad[collectors];
            for (int c = 0; c < collectors; c++)
            {
                pipeLine[c] = new float[pipeSize];
                modes[c] = new Biquad[modeCount];
                for (int m = 0; m < modeCount; m++)
                    modes[c][m] = Biquad.BandPass(layout.mufflerHz[m], layout.mufflerQ != null && m < layout.mufflerQ.Length ? layout.mufflerQ[m] : 3, SimRate);
                lowPass[c] = Biquad.LowPass(layout.mufflerCutoff, .707f, SimRate);
            }
            knockMode = Biquad.BandPass(1900, 6, SimRate);
            turbineLow = new float[collectors];
            turbineCoefficient = 1 - MathF.Exp(-2 * MathF.PI * TurbineCutoff / SimRate);
            hissBand = Biquad.BandPass(3200, 1.2f, SimRate);
            Reset(idleRpm);
            Calibrate();
        }

        static int NextPowerOfTwo(int value) { int size = 8; while (size < value) size <<= 1; return size; }
        float Noise() { seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5; return (seed & 0xFFFFFF) / 8388608f - 1; }
        float Volume(float angle) => clearance + .5f * (1 - MathF.Cos(angle * (MathF.PI / 180)));

        void Reset(float startRpm)
        {
            crank = 0; rpm = startRpm; throttle = 0; noiseLow = 0; knockAmount = 0; phase = 0;
            envelope = CalibrationRms * CalibrationRms; leveler = 1;
            lastLeft = lastRight = nextLeft = nextRight = 0; headerWrite = pipeWrite = 0;
            for (int i = 0; i < cylinders; i++)
            {
                open[i] = false; runnerGas[i] = runnerVolume[i]; cylinderGas[i] = 0; cylinderPressure[i] = 1;
                lastAngle[i] = Wrap(-firing[i]);
                Array.Clear(headerLine[i], 0, headerLine[i].Length);
            }
            for (int c = 0; c < collectors; c++)
            {
                dcState[c] = pipeLow[c] = 0; Array.Clear(pipeLine[c], 0, pipeLine[c].Length);
                for (int m = 0; m < modes[c].Length; m++) modes[c][m].Clear();
                lowPass[c].Clear();
            }
            knockMode.Clear();
            spool = whistlePhase = 0; Array.Clear(turbineLow, 0, collectors); hissBand.Clear();
        }

        /// <summary>
        /// Different layouts produce very different raw levels. Render a short full-load run at mid revs once and set
        /// the output gain so every engine sits at the same reference loudness before the game's mix is applied.
        /// Calibration runs without the turbo, so fitting one changes loudness and character against the stock engine.
        /// </summary>
        void Calibrate()
        {
            float midRpm = idleRpm + .6f * (redlineRpm - idleRpm);
            Reset(midRpm); TargetRpm = midRpm; Throttle = 1; Gain = 1; outputGain = 1;
            int warm = SimRate / 4, measure = SimRate / 4; double energy = 0;
            for (int n = 0; n < warm + measure; n++)
            {
                StepSimulation(midRpm, 1, out float left, out float right, false);
                if (n >= warm) energy += .5 * (left * left + right * right);
            }
            float rms = (float)Math.Sqrt(energy / measure);
            outputGain = rms > 1e-6f ? CalibrationRms / rms : 1;
            Reset(idleRpm); TargetRpm = idleRpm; Throttle = 0; Gain = 0;
        }

        static float Wrap(float angle) { angle %= 720; return angle < 0 ? angle + 720 : angle; }

        /// <summary>Fills an interleaved buffer (any channel count; channels beyond two copy the mix).</summary>
        public void Render(float[] data, int channels)
        {
            int frames = data.Length / channels;
            // Silent and staying silent (the other engine model is selected, or no player): skip the simulation.
            if (Gain <= 0 && appliedGain <= 0) { Array.Clear(data, 0, data.Length); Load = 0; return; }
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            float targetRpm = Math.Clamp(TargetRpm, idleRpm * .5f, redlineRpm * 1.1f), targetThrottle = Math.Clamp(Throttle, 0, 1);
            // Ramp the block's gain so frame-rate volume changes never step.
            float gainStep = (Gain - appliedGain) / Math.Max(1, frames);
            for (int f = 0; f < frames; f++)
            {
                appliedGain += gainStep; float gain = appliedGain;
                if (phase == 0)
                {
                    lastLeft = nextLeft; lastRight = nextRight;
                    StepSimulation(targetRpm, targetThrottle, out nextLeft, out nextRight, true);
                }
                float t = divider == 1 ? 1 : (phase + 1) / (float)divider;
                float left = (lastLeft + (nextLeft - lastLeft) * t) * gain, right = (lastRight + (nextRight - lastRight) * t) * gain;
                phase = (phase + 1) % divider;
                int o = f * channels;
                if (channels == 1) data[o] = .5f * (left + right);
                else { data[o] = left; data[o + 1] = right; for (int c = 2; c < channels; c++) data[o + c] = .5f * (left + right); }
            }
            double seconds = (System.Diagnostics.Stopwatch.GetTimestamp() - start) / (double)System.Diagnostics.Stopwatch.Frequency;
            float load = (float)(seconds * OutputRate / Math.Max(1, frames));
            Load = Load <= 0 ? load : Load + (load - Load) * .05f;
        }

        void StepSimulation(float targetRpm, float targetThrottle, out float left, out float right, bool live)
        {
            // Inputs arrive at frame rate: glide them so tachometer steps never click.
            rpm += (targetRpm - rpm) * .0025f;
            throttle += (targetThrottle - throttle) * .004f;
            float advance = rpm * crankStep;
            crank = Wrap(crank + advance);
            float dt = 1f / SimRate, revs = Math.Clamp((rpm - idleRpm) / (redlineRpm - idleRpm), 0, 1);
            bool fuelCut = throttle < .04f && revs > .25f;
            // Diesels have no throttle plate; petrol engines trap less charge as the throttle closes.
            float manifold = Layout.diesel ? 1 : .32f + .68f * MathF.Pow(throttle, .8f);
            float volumetric = .78f + .17f * MathF.Sin(MathF.PI * Math.Min(1, revs * 1.1f));
            float heat = Layout.diesel ? .3f + 2.6f * throttle : 2.8f;
            float variation = Layout.combustionVariation * (live ? Variation : 1);
            noiseLow += (Noise() - noiseLow) * .35f;
            float turbulence = Layout.turbulence * (live ? Rasp : 1);
            bool turbo = Turbocharged && live;
            if (turbo)
            {
                float target = throttle * (.15f + .85f * revs);
                spool += (target - spool) * (target > spool ? SpoolUp : SpoolDown) * dt;
                // Boost grows with shaft speed squared and only under load; the wastegate caps it at MaxBoost.
                manifold *= 1 + MaxBoost * spool * spool * throttle;
            }

            for (int i = 0; i < collectors; i++) collectorSum[i] = 0;
            for (int i = 0; i < cylinders; i++)
            {
                float angle = Wrap(crank - firing[i]), previous = lastAngle[i];
                lastAngle[i] = angle;
                if (angle < previous) // passed combustion TDC: compression and combustion in closed form
                {
                    float burn = fuelCut ? (Noise() * .5f + .5f < Layout.burble * (live ? OverrunLevel : 1) ? 2.5f : 0) : heat;
                    burn *= 1 + variation * Noise();
                    float peak = manifold * MathF.Pow(1 + 1 / clearance, Gamma) * (1 + Math.Max(0, burn) * volumetric);
                    cylinderPressure[i] = peak; // held until the exhaust valve opens
                    if (Layout.knock > 0 && !fuelCut) knockAmount += Layout.knock * burn * .02f;
                    open[i] = false;
                }
                float sinceOpen = angle - Layout.exhaustOpen;
                if (!open[i] && sinceOpen >= 0 && sinceOpen < Layout.exhaustDuration && previous < Layout.exhaustOpen)
                {
                    // Isentropic expansion from TDC to exhaust valve opening.
                    float volume = Volume(angle);
                    cylinderPressure[i] *= MathF.Pow(clearance / volume, Gamma);
                    cylinderGas[i] = cylinderPressure[i] * volume;
                    open[i] = true;
                }
                if (open[i])
                {
                    if (sinceOpen >= Layout.exhaustDuration || sinceOpen < 0) open[i] = false;
                    else
                    {
                        float x = sinceOpen / Layout.exhaustDuration, lift = 16 * x * x * (1 - x) * (1 - x);
                        float volume = Volume(angle), oldVolume = Volume(angle - advance);
                        float p = cylinderPressure[i], runner = runnerGas[i] / runnerVolume[i];
                        float up = Math.Max(p, runner), difference = p - runner;
                        // Incompressible orifice, limited to the choked rate of the upstream pressure.
                        float rate = MathF.Min(MathF.Sqrt(up * MathF.Abs(difference)), .58f * up) * MathF.Sign(difference);
                        float moved = flowConstant * lift * rate * dt;
                        // Explicit flow is stiff near TDC, where the cylinder holds little gas: never move more than half
                        // of what would equalise the two pressures in one step, so the exchange cannot overshoot and ring.
                        float equalise = MathF.Abs(difference) / (Gamma / volume + 1 / runnerVolume[i]);
                        moved = Math.Clamp(moved, -.5f * equalise, .5f * equalise);
                        float gas = cylinderGas[i];
                        cylinderGas[i] = gas - moved;
                        cylinderPressure[i] = Math.Max(.05f, p * (1 + Gamma * (-moved / Math.Max(1e-4f, gas) - (volume - oldVolume) / volume)));
                        runnerGas[i] += moved;
                    }
                }
                // Runner drains to the collector; its gauge pressure is the acoustic source, roughened by turbulence.
                float gauge = runnerGas[i] / runnerVolume[i] - 1;
                runnerGas[i] -= runnerDrain[i] * gauge * dt;
                float source = gauge * (1 + turbulence * noiseLow * 2);
                float[] line = headerLine[i];
                line[headerWrite & headerMask[i]] = source;
                collectorSum[collector[i]] += ReadDelay(line, headerMask[i], headerWrite, headerDelay[i]);
            }
            headerWrite++;

            float drive = live ? Drive : 1, body = live ? Body : 1, rasp = live ? Rasp : 1;
            float mixLeft = 0, mixRight = 0;
            for (int c = 0; c < collectors; c++)
            {
                float x = collectorSum[c];
                dcState[c] += (x - dcState[c]) * (2 * MathF.PI * 18 / SimRate);
                x -= dcState[c];
                if (turbo)
                {
                    // The turbine wheel smears each pulse's edge and takes energy out of it as the shaft speeds up.
                    turbineLow[c] += (x - turbineLow[c]) * turbineCoefficient;
                    x = (x + (turbineLow[c] - x) * TurbineSmoothing) * (1 - TurbineAbsorb * spool);
                }
                // Tailpipe waveguide: the open end reflects inverted pressure back up the pipe, damped at high frequency.
                float[] pipe = pipeLine[c];
                float returning = ReadDelay(pipe, pipeMask, pipeWrite, pipeDelay[c]);
                pipeLow[c] += (returning - pipeLow[c]) * (1 - Layout.pipeDamping);
                float inPipe = x - Layout.pipeReflection * pipeLow[c];
                pipe[pipeWrite & pipeMask] = inPipe;
                float muffled = lowPass[c].Step(inPipe);
                var bank = modes[c];
                for (int m = 0; m < bank.Length; m++)
                {
                    float modeGain = Layout.mufflerGain[m] * (m == 0 ? body : rasp);
                    muffled += modeGain * bank[m].Step(inPipe);
                }
                mixLeft += muffled * panLeft[c]; mixRight += muffled * panRight[c];
            }
            pipeWrite++;
            if (Layout.knock > 0)
            {
                float knock = knockMode.Step(knockAmount * Noise());
                knockAmount *= .9985f;
                mixLeft += knock; mixRight += knock;
            }
            // Calibration measures the unlimited signal; the limiter would hide how hot the raw mix is.
            if (!live) { left = mixLeft; right = mixRight; return; }
            // engine-sim levels its output fully; here quiet passages (idle, overrun) are only lifted, halving their
            // distance in dB from the full-load reference, so load still changes loudness the way it should.
            float calibrated = .5f * (MathF.Abs(mixLeft) + MathF.Abs(mixRight)) * outputGain;
            envelope += (calibrated * calibrated - envelope) * (1 - MathF.Exp(-1f / (.25f * SimRate)));
            if ((headerWrite & 31) == 0)
                leveler = Math.Clamp(MathF.Pow(CalibrationRms * CalibrationRms / Math.Max(1e-8f, envelope), .25f), 1, 3.5f);
            float level = outputGain * leveler * (LoadLevel * throttle + OverrunLevel * (1 - throttle));
            float whistle = 0;
            if (turbo)
            {
                // Compressor whistle rises with shaft speed; the hiss is air rushing through compressor and turbine.
                whistlePhase += (1800 + 5400 * spool) / SimRate; if (whistlePhase >= 1) whistlePhase -= 1;
                float amount = spool * spool * (.35f + .65f * throttle) * TurboLevel;
                whistle = amount * (WhistleLevel * MathF.Sin(2 * MathF.PI * whistlePhase) + HissLevel * hissBand.Step(Noise()));
            }
            left = SoftLimit((mixLeft * level + whistle) * drive) / drive;
            right = SoftLimit((mixRight * level + whistle) * drive) / drive;
        }

        static float ReadDelay(float[] line, int mask, int write, float delay)
        {
            float position = write - delay;
            int whole = (int)MathF.Floor(position); float fraction = position - whole;
            float a = line[whole & mask], b = line[(whole + 1) & mask];
            return a + (b - a) * fraction;
        }
        /// <summary>Rational tanh: saturation adds the harmonics heard as growl and keeps peaks below full scale.</summary>
        static float SoftLimit(float x)
        {
            x = Math.Clamp(x, -3, 3);
            return x * (27 + x * x) / (27 + 9 * x * x);
        }
    }
}
