using System;
using UnityEngine;
namespace MadeInArizona
{
    /// <summary>Deterministic original PCM assets. No samples, recordings or external melodies.</summary>
    public static class AudioSynthesis
    {
        public const int Rate=22050;
        static float Wave(float phase){return Mathf.Sin(phase*6.2831853f);}
        static float Noise(ref uint state){state=state*1664525u+1013904223u;return ((state>>8)&65535)/32767.5f-1;}
        // The generated effects can have several correlated layers at their attack.
        // A soft limit keeps that impact while guaranteeing PCM stays below full scale.
        static float SoftLimit(float sample){return (float)Math.Tanh(sample);}
        static AudioClip Clip(string name,float[] samples)
        {var clip=AudioClip.Create(name,samples.Length,1,Rate,false);clip.SetData(samples,0);return clip;}
        /// <summary>Firing rates of the engine bank. Each loop holds a whole number of four-stroke V8 cycles.</summary>
        public static readonly float[] EngineLayerHz={48,104,200};
        // Cross-plane V8 firing order 1-8-4-3-6-5-7-2 alternates banks L R R L R L R L. The listener sits
        // nearer the left pipe, so the uneven bank pulses create the cycle-rate lope heard as "burble" and growl.
        static readonly float[] BankPulse={1,.62f,.62f,1,.62f,1,.62f,1};
        // The B path renders the nonlinear waveform at 2x rate, then removes energy above the output Nyquist.
        // This keeps the audible pulse edge without the original full-band one-pole smoothing.
        static readonly float[] EngineDownsampleFilter=MakeEngineDownsampleFilter();
        static float[] MakeEngineDownsampleFilter()
        {
            const int taps=63; const float cutoff=.22f; // 9.7 kHz at 44.1 kHz; 11 kHz stop band.
            var filter=new float[taps];float sum=0;
            for(int i=0;i<taps;i++)
            {
                int offset=i-taps/2;
                float sinc=offset==0?2*cutoff:Mathf.Sin(2*Mathf.PI*cutoff*offset)/(Mathf.PI*offset);
                float window=.54f-.46f*Mathf.Cos(2*Mathf.PI*i/(taps-1));
                filter[i]=sinc*window;sum+=filter[i];
            }
            for(int i=0;i<taps;i++)filter[i]/=sum;
            return filter;
        }
        static float[] DownsampleEngine(float[] oversampled)
        {
            var output=new float[oversampled.Length/2];int length=oversampled.Length,half=EngineDownsampleFilter.Length/2;
            for(int i=0;i<output.Length;i++)
            {
                float value=0;int center=i*2;
                for(int tap=0;tap<EngineDownsampleFilter.Length;tap++)
                {
                    int index=(center+tap-half+length)%length;
                    value+=oversampled[index]*EngineDownsampleFilter[tap];
                }
                output[i]=value;
            }
            return output;
        }
        public static void MatchEngineLevel(AudioClip reference,AudioClip candidate)
        {
            var original=new float[reference.samples];var changed=new float[candidate.samples];
            if(!reference.GetData(original,0)||!candidate.GetData(changed,0))return;
            double originalEnergy=0,changedEnergy=0;float peak=0;
            for(int i=0;i<original.Length;i++)originalEnergy+=original[i]*original[i];
            for(int i=0;i<changed.Length;i++){changedEnergy+=changed[i]*changed[i];peak=Mathf.Max(peak,Mathf.Abs(changed[i]));}
            if(changedEnergy<=0||peak<=0)return;
            float gain=Mathf.Min((float)System.Math.Sqrt(originalEnergy/changedEnergy),.98f/peak);
            for(int i=0;i<changed.Length;i++)changed[i]*=gain;
            candidate.SetData(changed,0);
        }
        /// <summary>
        /// One RPM layer of the engine bank, built the way recorded car audio is: a loop captured at a fixed
        /// firing rate under load (on) or on overrun (off). The game crossfades neighbouring layers by RPM,
        /// so each clip is only pitch-shifted a little and its exhaust resonances stay put.
        /// </summary>
        public static AudioClip EngineLayer(int layer,bool onLoad,DevTuning tuning,bool preserveEdges=false)
        {
            float firing=EngineLayerHz[Mathf.Clamp(layer,0,EngineLayerHz.Length-1)],cycleHz=firing/8;
            int renderRate=preserveEdges?Rate*2:Rate;
            int length=renderRate*2,cycles=Mathf.RoundToInt(cycleHz*2),tail=Mathf.RoundToInt(renderRate*.09f);
            var samples=new float[length];uint seed=(uint)(5101+layer*977+(onLoad?0:31));
            var pulse=new float[tail];var noiseWeight=new float[tail];
            for(int j=0;j<tail;j++)
            {
                float t=j/(float)renderRate;
                float front=Mathf.Exp(-t*260*tuning.enginePulseDecay)
                    *(1-Mathf.Exp(-t*2400*tuning.enginePulseAttack))*1.4f*tuning.enginePulsePressure;
                float body=Wave(68*t)*Mathf.Exp(-t*34)*tuning.engineBody;
                float pipe=Wave(185*t+.08f)*Mathf.Exp(-t*52)*.62f;
                float rasp=Wave(540*t)*Mathf.Exp(-t*120)*(onLoad?.34f:.14f)*tuning.engineRasp;
                pulse[j]=front+body+pipe+rasp;
                noiseWeight[j]=Mathf.Exp(-t*170)*(onLoad?.42f:.2f);
            }
            for(int c=0;c<cycles;c++)for(int k=0;k<8;k++)
            {
                // Real combustion is never perfectly even: small timing and strength variation adds roughness.
                float start=(c+(k+Noise(ref seed)*.035f*tuning.enginePulseVariation)/8)/cycleHz;
                float strength=Mathf.LerpUnclamped(1,BankPulse[k],tuning.enginePulseVariation)
                    *(1+Noise(ref seed)*.12f*tuning.enginePulseVariation)*(onLoad?1:.72f);
                // Jitter can nudge the very first pulse before zero; wrap it to the loop end like every other tail.
                int first=((Mathf.RoundToInt(start*renderRate)%length)+length)%length;
                for(int j=0;j<tail;j++)
                {
                    float n=Noise(ref seed);
                    samples[(first+j)%length]+=strength*(pulse[j]+n*noiseWeight[j]);
                }
            }
            float mean=0;foreach(float v in samples)mean+=v;mean/=length;
            float energy=0;
            if(preserveEdges)
            {
                for(int i=0;i<length;i++)samples[i]-=mean;
            }
            else
            {
                // Original A path. The first traversal warms the state across the loop seam.
                float low=0,smoothing=onLoad?.62f:.3f;
                for(int pass=0;pass<2;pass++)for(int i=0;i<length;i++){low=Mathf.Lerp(low,samples[i]-mean,smoothing);if(pass==1)samples[i]=low;}
            }
            foreach(float v in samples)energy+=v*v;
            float scale=1/Mathf.Sqrt(Mathf.Max(1e-6f,energy/length));
            // Saturation is where "growl" lives: it adds dense harmonics above each firing pulse, more under load.
            float drive=(onLoad?.95f:.5f)*tuning.engineSaturation;
            for(int i=0;i<length;i++)samples[i]=SoftLimit(samples[i]*scale*drive)*(onLoad?.78f:.72f);
            if(preserveEdges)samples=DownsampleEngine(samples);
            return Clip((preserveEdges?"Preserved edges":"Original smoothing")+" • V8 bank "+firing+"Hz "+(onLoad?"on load":"overrun"),samples);
        }
        /// <summary>A single unburnt-fuel overrun pop for crackle after lifting off the throttle.</summary>
        public static AudioClip Backfire()
        {
            var samples=new float[Mathf.RoundToInt(Rate*.16f)];uint seed=6071;float low=0;
            for(int i=0;i<samples.Length;i++)
            {
                float t=i/(float)Rate,n=Noise(ref seed);low=Mathf.Lerp(low,n,.3f);
                samples[i]=SoftLimit((n*Mathf.Exp(-t*140)*.9f+low*Mathf.Exp(-t*60)*.9f+Wave(120*t-80*t*t)*Mathf.Exp(-t*45)*.7f)*1.4f)*Mathf.Min(1,t*2000);
            }
            return Clip("Original • overrun pop",samples);
        }
        /// <summary>The player's hull taking a hit: body thump, a low clang of struck sheet steel and a crunch of bending metal.</summary>
        public static AudioClip Hurt(int variant)
        {
            var samples=new float[Mathf.RoundToInt(Rate*.55f)];uint seed=(uint)(7207+variant*613);float low=0,band=0;
            float spread=1+variant*.07f;
            for(int i=0;i<samples.Length;i++)
            {
                float t=i/(float)Rate,n=Noise(ref seed);low=Mathf.Lerp(low,n,.08f);band=Mathf.Lerp(band,n-low,.5f);
                float thump=Wave((95-70*Mathf.Min(1,t*9))*t)*Mathf.Exp(-t*16)*.95f;
                // Low, detuned partial pairs beat against each other and die quickly, so the hit reads as a dull clang
                // of a car panel rather than the sparse, long ring of a bell. The ring-modulated pair adds clank.
                float f=spread;
                float panel=(Wave(151*f*t)*.5f+Wave(158*f*t)*.38f)*Mathf.Exp(-t*10)
                    +(Wave(263*f*t)*.34f+Wave(281*f*t)*.26f)*Mathf.Exp(-t*15)
                    +(Wave(419*f*t)*.2f+Wave(452*f*t)*.15f)*Mathf.Exp(-t*22)
                    +Wave(207*f*t)*Wave(653*f*t)*.32f*Mathf.Exp(-t*24);
                float crunch=band*Mathf.Exp(-t*26)*(1+.6f*Wave(37*t))*1.2f;
                float rattle=n*Mathf.Exp(-Mathf.Abs(t-.09f)*80)*.25f;
                samples[i]=SoftLimit((thump+panel*.6f+crunch+rattle)*1.25f)*Mathf.Min(1,t*900);
            }
            return Clip("Original • hull impact "+variant,samples);
        }
        /// <summary>
        /// Hit confirmation for the player's shots, in the style of shooter hitmarkers: a crisp high-passed click, a
        /// short bright metallic "tink" and a small low knock for weight, all over within ~80 ms so rapid fire stays
        /// clean. Variants shift the pitch slightly so repeated hits do not sound machine-gunned.
        /// </summary>
        public static AudioClip HitMarker(int variant)
        {
            var samples=new float[Mathf.RoundToInt(Rate*.085f)];uint seed=(uint)(8117+variant*977);float low=0;
            float pitch=1+(variant-1)*.045f;
            for(int i=0;i<samples.Length;i++)
            {
                float t=i/(float)Rate,n=Noise(ref seed);low=Mathf.Lerp(low,n,.22f);
                float click=(n-low)*Mathf.Exp(-t*480)*.95f;
                float tink=(Wave(2150*pitch*t)*.55f+Wave(3260*pitch*t)*.3f+Wave(4870*pitch*t)*.12f)*Mathf.Exp(-t*75);
                float knock=Wave(185*t)*Mathf.Exp(-t*95)*.4f;
                samples[i]=SoftLimit((click+tink*.75f+knock)*1.25f)*Mathf.Min(1,t*4000);
            }
            return Clip("Original • hit marker "+variant,samples);
        }
        /// <summary>Two metallic rack clicks and a rising confirmation tone for a field weapon pickup.</summary>
        public static AudioClip WeaponPickup()
        {
            var samples = new float[Mathf.RoundToInt(Rate * .32f)]; uint seed = 6173;
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)Rate, n = Noise(ref seed);
                float rack = n * (Mathf.Exp(-t * 125) + (t > .075f ? Mathf.Exp(-(t - .075f) * 120) : 0)) * .36f;
                float tone = (Wave((610 + t * 1450) * t) + Wave((920 + t * 1200) * t) * .4f) * Mathf.Exp(-t * 13) * .38f;
                samples[i] = SoftLimit(rack + tone) * Mathf.Min(1, t * 3000);
            }
            return Clip("Original • weapon pickup", samples);
        }
        /// <summary>Chamberlin state-variable band-pass: one step, returns the band output.</summary>
        struct BandPass
        {
            float low, band;
            public float Step(float input, float hz, float q)
            {
                float f = 2 * Mathf.Sin(Mathf.PI * Mathf.Min(hz, Rate * .22f) / Rate);
                low += f * band; float high = input - low - band / q; band += f * high;
                return band;
            }
        }
        /// <summary>
        /// Folds the last <paramref name="fade"/> samples over the start with an equal-power crossfade, so a buffer of
        /// length + fade becomes a seamless loop of length samples with no level dip at the seam.
        /// </summary>
        static float[] LoopSeamless(float[] raw, int fade)
        {
            int length = raw.Length - fade; var loop = new float[length];
            for (int i = 0; i < length; i++) loop[i] = raw[i];
            for (int i = 0; i < fade; i++)
            {
                float t = (i + .5f) / fade;
                loop[i] = raw[i] * Mathf.Sin(t * Mathf.PI * .5f) + raw[length + i] * Mathf.Cos(t * Mathf.PI * .5f);
            }
            return loop;
        }
        static void NormalizeRms(float[] samples, float rms, float ceiling = .92f)
        {
            double energy = 0; foreach (float v in samples) energy += v * v;
            float scale = rms / Mathf.Max(1e-6f, (float)Math.Sqrt(energy / samples.Length));
            for (int i = 0; i < samples.Length; i++) samples[i] = SoftLimit(samples[i] * scale / ceiling) * ceiling;
        }
        /// <summary>
        /// Rubber squeal on pavement, for drifts and burnouts. A tyre squeals because the tread sticks and slips at a few
        /// hundred cycles a second, so the voice is a narrow, ringing band of noise around 1 kHz with its overtones, and
        /// a rasping tone under it whose pitch wanders at random (never a steady vibrato) and whose level chatters.
        /// Three seconds, crossfaded into a seamless loop.
        /// </summary>
        public static AudioClip TyreSqueal()
        {
            int fade = Rate / 4, length = Rate * 3; var raw = new float[length + fade];
            uint seed = 4497; BandPass b1 = default, b2 = default, b3 = default;
            float walk = 0, walkTarget = 0, level = .8f, levelTarget = .8f, chatter = 0, phase = 0, hissLow = 0;
            int retarget = 0;
            for (int i = 0; i < raw.Length; i++)
            {
                float n = Noise(ref seed);
                if (i >= retarget)
                {
                    // A new pitch and level every 40-150 ms, glided toward: stick-slip is irregular, not periodic.
                    walkTarget = Noise(ref seed); levelTarget = .55f + .45f * Mathf.Abs(Noise(ref seed));
                    retarget = i + Mathf.RoundToInt(Rate * (.04f + .11f * Mathf.Abs(Noise(ref seed))));
                }
                walk += (walkTarget - walk) * .0016f; level += (levelTarget - level) * .003f;
                chatter = Mathf.Lerp(chatter, n, .018f);
                float hz = 1040 * (1 + .065f * walk + .01f * n);
                phase += hz / Rate; phase -= Mathf.Floor(phase);
                // Rasping tone: a soft-cornered pulse rich in odd and even partials.
                float tone = Wave(phase) * .55f + Wave(phase * 2) * .3f + Wave(phase * 3) * .18f + Wave(phase * 4) * .08f;
                float ring = b1.Step(n, hz, 16) + b2.Step(n, hz * 2.06f, 20) * .55f + b3.Step(n, hz * 3.1f, 24) * .22f;
                hissLow = Mathf.Lerp(hissLow, n, .5f);
                raw[i] = (tone * .16f * (.65f + chatter * 3.2f) + ring) * level + (n - hissLow) * .025f;
            }
            var samples = LoopSeamless(raw, fade);
            NormalizeRms(samples, .3f);
            return Clip("Original • tyre squeal", samples);
        }
        /// <summary>
        /// Tyres sliding on loose ground: grit crunching under the tread (dense random grains through two resonant
        /// bands), a low gravel rumble and a dry hiss of thrown dirt. Seamless three-second loop.
        /// </summary>
        public static AudioClip GravelScrub()
        {
            int fade = Rate / 4, length = Rate * 3; var raw = new float[length + fade];
            uint seed = 7741; BandPass crunchLow = default, crunchHigh = default;
            float rumble = 0, rumbleLow = 0, hissLow = 0, swell = 1, swellTarget = 1; int retarget = 0;
            for (int i = 0; i < raw.Length; i++)
            {
                float n = Noise(ref seed);
                if (i >= retarget) { swellTarget = .6f + .4f * Mathf.Abs(Noise(ref seed)); retarget = i + Mathf.RoundToInt(Rate * (.06f + .1f * Mathf.Abs(Noise(ref seed)))); }
                swell += (swellTarget - swell) * .002f;
                // About 1100 grains a second, each a random-sized tick.
                float grain = Mathf.Abs(Noise(ref seed)) < 1100f / Rate ? Noise(ref seed) * 4 : 0;
                float crunch = crunchLow.Step(grain, 1350, 3) + crunchHigh.Step(grain, 3300, 4) * .6f;
                rumble = Mathf.Lerp(rumble, n, .06f); rumbleLow = Mathf.Lerp(rumbleLow, rumble, .05f);
                hissLow = Mathf.Lerp(hissLow, n, .45f);
                raw[i] = (crunch * .9f + (rumble - rumbleLow) * 1.6f + (n - hissLow) * .07f) * swell;
            }
            var samples = LoopSeamless(raw, fade);
            NormalizeRms(samples, .28f);
            return Clip("Original • gravel scrub", samples);
        }
        /// <summary>
        /// The death ray's sustained beam: a thick, slowly phasing electrical hum (two detuned buzzing saws over a sub),
        /// a bright singing whine and a crackling sizzle where the focused light burns. Every oscillator completes whole
        /// cycles in the two-second loop and the sizzle is crossfaded, so the seam is silent.
        /// </summary>
        public static AudioClip DeathRayLoop()
        {
            int fade = Rate / 5, length = Rate * 2; var raw = new float[length + fade];
            uint seed = 3301; BandPass sizzle = default, crackle = default;
            float spark = 0;
            for (int i = 0; i < raw.Length; i++)
            {
                float t = i / (float)Rate, n = Noise(ref seed);
                float saw = 0, partner = 0;
                // Band-limited saws: 110 Hz and 110.5 Hz beat once every two seconds; harmonics stop below 4.5 kHz.
                for (int k = 1; k <= 40; k++) { float amp = 1f / k; saw += Wave(110 * k * t) * amp; partner += Wave(110.5f * k * t) * amp; }
                float sub = Wave(55 * t);
                float whine = Wave(1760 * t + .004f * 1760 / 6 * Wave(6 * t)) * (.6f + .4f * Wave(.5f * t));
                // Sparks: random bursts of crackle on top of a steady sizzle band.
                if (Mathf.Abs(Noise(ref seed)) < 30f / Rate) spark = 1;
                spark *= .9985f;
                float hiss = sizzle.Step(n, 4200, 2.2f) + crackle.Step(n * spark, 2400, 6) * 1.6f;
                raw[i] = (saw + partner) * .16f + sub * .32f + whine * .06f + hiss * .35f;
            }
            var samples = LoopSeamless(raw, fade);
            NormalizeRms(samples, .3f);
            return Clip("Original • death ray beam", samples);
        }
        /// <summary>The ray striking up: a rising electrical zap that lands on the beam's hum.</summary>
        public static AudioClip DeathRayStart()
        {
            var samples = new float[Mathf.RoundToInt(Rate * .4f)]; uint seed = 3307; BandPass zap = default; float phase = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)Rate, n = Noise(ref seed);
                float hz = 180 * Mathf.Pow(12, Mathf.Clamp01(t / .22f));
                phase += hz / Rate;
                float sweep = (Wave(phase) + Wave(phase * 2) * .4f) * Mathf.Exp(-t * 7);
                float burst = zap.Step(n, 2600, 3) * Mathf.Exp(-t * 22);
                samples[i] = SoftLimit((sweep * .5f + burst * 1.2f) * 1.1f) * Mathf.Min(1, t * 2000) * Mathf.Min(1, (samples.Length - i) / (Rate * .03f));
            }
            return Clip("Original • death ray ignition", samples);
        }
        /// <summary>
        /// Bowling pins: the ball's heavy wooden thump into the pins, then a cascade of hollow maple clacks (each pin
        /// a few inharmonic body modes) thinning out and quietening as they tumble and roll on the boards.
        /// </summary>
        public static AudioClip PinStrike(int variant)
        {
            var samples = new float[Mathf.RoundToInt(Rate * 1.5f)];
            uint seed = (uint)(6271 + variant * 811);
            int pins = 14 + variant * 2;
            var at = new float[pins]; var hz = new float[pins]; var gain = new float[pins]; var decay = new float[pins];
            for (int p = 0; p < pins; p++)
            {
                // Dense at first, spreading out: most clacks land in the first 0.35 s, stragglers to about 1.1 s.
                float u = (p + .5f * (Noise(ref seed) * .5f + .5f)) / pins;
                at[p] = .012f + u * u * 1.05f;
                hz[p] = (p % 4 == 3 ? 520 : 760) * (1 + .22f * Noise(ref seed));
                gain[p] = Mathf.Lerp(1, .22f, u) * (.7f + .3f * Mathf.Abs(Noise(ref seed)));
                decay[p] = 26 + 14 * Mathf.Abs(Noise(ref seed));
            }
            float low = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)Rate, n = Noise(ref seed); low = Mathf.Lerp(low, n, .2f);
                float thump = Wave(150 * t - 60 * t * t) * Mathf.Exp(-t * 26) * Mathf.Min(1, t * 900) * .9f
                    + (n - low) * Mathf.Exp(-t * 90) * .5f;
                float clacks = 0;
                for (int p = 0; p < pins; p++)
                {
                    float local = t - at[p]; if (local < 0) continue;
                    float env = Mathf.Exp(-local * decay[p]) * Mathf.Min(1, local * 3000) * gain[p];
                    if (env < .002f) continue;
                    // Inharmonic modes of a hollow wooden bottle shape, plus the click of the strike.
                    clacks += (Wave(hz[p] * local) * .55f + Wave(hz[p] * 2.32f * local) * .3f + Wave(hz[p] * 3.87f * local) * .16f) * env
                        + (n - low) * Mathf.Exp(-local * 400) * gain[p] * .35f;
                }
                // Rumble of pins rolling on the lane.
                float roll = low * Mathf.Exp(-Mathf.Abs(t - .45f) * 3) * .18f;
                samples[i] = SoftLimit((thump + clacks * .55f + roll) * 1.05f) * Mathf.Min(1, (samples.Length - i) / (Rate * .05f));
            }
            return Clip("Original • bowling pin strike " + variant, samples);
        }
        /// <summary>A short impact and tumbling fragments when a car breaks scenery; wood and stone have different bodies.</summary>
        public static AudioClip DebrisBreak(bool wood)
        {
            var samples = new float[Mathf.RoundToInt(Rate * .55f)];
            uint seed = wood ? 84617u : 41773u;
            float low = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)Rate, noise = Noise(ref seed);
                low = Mathf.Lerp(low, noise, wood ? .16f : .06f);
                float attack = Mathf.Exp(-t * (wood ? 22f : 17f));
                float body = Wave((wood ? 155f : 92f) * t - 35f * t * t) * attack * (wood ? .45f : .65f);
                float crack = (noise - low) * attack * (wood ? .62f : .48f);
                float pieces = 0;
                for (int p = 0; p < 4; p++)
                {
                    float local = t - .035f - p * (wood ? .052f : .065f);
                    if (local < 0) continue;
                    float envelope = Mathf.Exp(-local * (wood ? 38f : 24f)) * (1 - p * .16f);
                    pieces += ((noise - low) * .42f + Wave((wood ? 470f : 310f) * local) * .23f) * envelope;
                }
                samples[i] = SoftLimit((body + crack + pieces) * .9f) * Mathf.Min(1, t * 2000f) *
                    Mathf.Min(1, (samples.Length - i) / (Rate * .025f));
            }
            return Clip(wood ? "Original • wood break and splinters" : "Original • rock break and debris", samples);
        }
        /// <summary>
        /// Kill confirmation: a heavy punched thunk with a short crunch, topped by a bright two-note ding, so a kill
        /// lands clearly above the ordinary hit tick.
        /// </summary>
        public static AudioClip KillConfirm()
        {
            var samples=new float[Mathf.RoundToInt(Rate*.42f)];uint seed=5521;float low=0;
            for(int i=0;i<samples.Length;i++)
            {
                float t=i/(float)Rate,n=Noise(ref seed);low=Mathf.Lerp(low,n,.15f);
                float thunk=Wave((125-70*Mathf.Min(1,t*14))*t)*Mathf.Exp(-t*16)*.95f;
                float crunch=(n-low)*Mathf.Exp(-t*55)*.45f;
                float ding=0;
                float[] notes={1318.5f,1760f};
                for(int k=0;k<2;k++){float local=t-.012f-k*.055f;if(local<0)continue;
                    ding+=(Wave(notes[k]*local)*.6f+Wave(notes[k]*2.01f*local)*.15f)*Mathf.Exp(-local*11)*Mathf.Min(1,local*900)*.4f;}
                samples[i]=SoftLimit((thunk+crunch+ding)*1.15f)*Mathf.Min(1,t*3000);
            }
            return Clip("Original • kill confirm",samples);
        }
        /// <summary>Objective progress (checkpoints, optional objectives): a quick rising major arpeggio over an airy sweep.</summary>
        public static AudioClip ObjectiveChime()
        {
            var samples=new float[Mathf.RoundToInt(Rate*.85f)];uint seed=9151;float low=0,air=0;
            float[] notes={523.25f,659.25f,783.99f,1046.5f};
            for(int i=0;i<samples.Length;i++)
            {
                float t=i/(float)Rate,n=Noise(ref seed),s=0;low=Mathf.Lerp(low,n,.25f);air=Mathf.Lerp(air,n-low,.6f);
                for(int k=0;k<notes.Length;k++)
                {
                    float start=k*.075f,local=t-start;if(local<0)continue;
                    float env=Mathf.Min(1,local*140)*Mathf.Exp(-local*(k==notes.Length-1?4.5f:9));
                    s+=(Wave(notes[k]*local)*.6f+Wave(notes[k]*2.003f*local)*.18f+Wave(notes[k]*3.01f*local)*.07f)*env*.42f;
                }
                // Rising breath of air under the notes.
                s+=air*Mathf.Sin(Mathf.PI*Mathf.Clamp01(t/.5f))*.18f*(1+.5f*Wave((300+900*t)*t));
                samples[i]=SoftLimit(s*1.1f)*Mathf.Min(1,(samples.Length-i)/(Rate*.05f));
            }
            return Clip("Original • objective chime",samples);
        }
        /// <summary>
        /// Repair pickup: three soft socket-wrench ratchet clicks over a faint warm hum. Deliberately quieter and duller
        /// than the hull-impact clang, so being hurt always reads louder than being patched up.
        /// </summary>
        public static AudioClip Repair()
        {
            var samples=new float[Mathf.RoundToInt(Rate*.42f)];uint seed=4409;float low=0,band=0;
            for(int i=0;i<samples.Length;i++)
            {
                float t=i/(float)Rate,n=Noise(ref seed);low=Mathf.Lerp(low,n,.35f);band=Mathf.Lerp(band,low-band*.2f,.5f);
                float clicks=0;
                for(int k=0;k<3;k++){float local=t-.03f-k*.055f;if(local>=0)clicks+=band*Mathf.Exp(-local*260)*(1-k*.18f);}
                float hum=(Wave(196*t)*.7f+Wave(294*t)*.3f)*Mathf.Sin(Mathf.PI*Mathf.Clamp01(t/.42f))*.16f;
                samples[i]=SoftLimit((clicks*.9f+hum)*1.1f)*Mathf.Min(1,t*600);
            }
            return Clip("Original • repair ratchet",samples);
        }
        /// <summary>A two-beat low heartbeat loop layered in while the player's vehicle is critically damaged.</summary>
        public static AudioClip Heartbeat()
        {
            var samples=new float[Rate];
            for(int i=0;i<samples.Length;i++)
            {
                float t=i/(float)Rate,lub=t,dub=t-.27f;
                float s=Wave(52*lub-30*lub*lub)*Mathf.Exp(-lub*20)*Mathf.Min(1,lub*400);
                if(dub>0)s+=Wave(60*dub-30*dub*dub)*Mathf.Exp(-dub*24)*Mathf.Min(1,dub*400)*.72f;
                samples[i]=SoftLimit(s*1.2f)*.85f;
            }
            return Clip("Original • critical damage heartbeat",samples);
        }
        public static AudioClip Mechanical(bool release)
        {
            var samples=new float[Mathf.RoundToInt(Rate*(release?.48f:.17f))];uint seed=3081;float low=0;
            for(int i=0;i<samples.Length;i++) {
                float t=i/(float)Rate,n=Noise(ref seed);low=Mathf.Lerp(low,n,.16f);
                samples[i]=release?(n-low)*Mathf.Exp(-t*9)*Mathf.Min(1,t*100)*.55f:
                    (low*.7f+Wave(94*t)*.35f)*Mathf.Exp(-t*35);
            }
            return Clip(release?"Original • compressor bypass hiss":"Original • loaded gear engagement",samples);
        }
        public static AudioClip Wind()
        {
            var samples=new float[Rate*3];uint seed=4919;float low=0;
            for(int i=0;i<samples.Length;i++){low=Mathf.Lerp(low,Noise(ref seed),.025f);samples[i]=low*2;}
            int fade=500;for(int i=0;i<fade;i++){float mix=i/(float)fade;samples[i]*=mix;samples[samples.Length-1-i]*=mix;}
            return Clip("Original • desert air and road texture",samples);
        }
        /// <summary>
        /// Loopable nitrous burn: a deep rumble under a rushing flame roar with a fast combustion flutter and a thin
        /// hiss of gas on top. Every modulation completes whole cycles inside the loop, so the seam is silent.
        /// </summary>
        public static AudioClip NitroRoar()
        {
            int length=Rate*2;var noise=new float[length];uint seed=9337;
            for(int i=0;i<length;i++)noise[i]=Noise(ref seed);
            var samples=new float[length];float rumble=0,roar=0,roarLow=0,hissLow=0,energy=0;
            // Two passes around the loop settle the filter state so the wrap point is seamless.
            for(int pass=0;pass<2;pass++)for(int i=0;i<length;i++)
            {
                float n=noise[i];
                rumble=Mathf.Lerp(rumble,n,.012f);roar=Mathf.Lerp(roar,n,.16f);roarLow=Mathf.Lerp(roarLow,roar,.03f);hissLow=Mathf.Lerp(hissLow,n,.55f);
                if(pass==0)continue;
                float t=i/(float)Rate;
                float flutter=1+.35f*Wave(31*t)+.15f*Wave(47*t+.3f),swell=1+.12f*Wave(.5f*t);
                samples[i]=rumble*9*swell+(roar-roarLow)*2.2f*flutter+(n-hissLow)*.35f;
                energy+=samples[i]*samples[i];
            }
            float scale=1/Mathf.Sqrt(Mathf.Max(1e-6f,energy/length));
            for(int i=0;i<length;i++)samples[i]=SoftLimit(samples[i]*scale*.75f)*.8f;
            return Clip("Original • nitro burn",samples);
        }
        /// <summary>Nitro lighting off: a hollow whump as the charge catches, then a tearing rush of flame and gas hiss.</summary>
        public static AudioClip NitroIgnite()
        {
            var samples=new float[Mathf.RoundToInt(Rate*.9f)];uint seed=12011;float low=0,band=0,hissLow=0;
            float fade=Rate*.05f;
            for(int i=0;i<samples.Length;i++)
            {
                float t=i/(float)Rate,n=Noise(ref seed);
                low=Mathf.Lerp(low,n,.05f);band=Mathf.Lerp(band,n,.25f);hissLow=Mathf.Lerp(hissLow,n,.6f);
                float whump=Wave(110*t-70*t*t)*Mathf.Exp(-t*9)*Mathf.Min(1,t*400)*1.1f;
                float tear=(band-low)*(1-Mathf.Exp(-t*40))*Mathf.Exp(-t*2.8f)*3.2f;
                float roar=low*Mathf.Exp(-t*3.5f)*Mathf.Min(1,t*60)*4;
                float hiss=(n-hissLow)*Mathf.Exp(-t*7)*.5f;
                samples[i]=SoftLimit((whump+tear+roar+hiss)*1.1f)*Mathf.Min(1,t*800)*Mathf.Min(1,(samples.Length-i)/fade);
            }
            return Clip("Original • nitro ignition",samples);
        }
        public static AudioClip Turbo()
        {
            var samples=new float[Rate];uint seed=44;
            for(int i=0;i<samples.Length;i++){float t=i/(float)Rate;samples[i]=Wave(t*610)*.35f+Wave(t*915)*.15f+Noise(ref seed)*.045f;}
            return Clip("Original • compressor and gear whine",samples);
        }
        public static AudioClip Shot(int kind)
        {
            float length=kind==1?.7f:kind==2?.46f:.23f;
            var samples=new float[Mathf.RoundToInt(Rate*length)];uint seed=(uint)(9283+kind*439);
            for(int i=0;i<samples.Length;i++)
            {
                float t=i/(float)Rate,n=Noise(ref seed);
                float report=n*Mathf.Exp(-t*(kind==1?12:kind==2?24:58));
                float thump=Wave((kind==2?110:160)*t-40*t*t)*Mathf.Exp(-t*30)*.46f;
                float bodyHz=kind==2?72:92;
                float body=Wave(bodyHz*t-16*t*t)*Mathf.Exp(-t*(kind==1?13:20))*Mathf.Min(1,t*150)*.42f;
                float mech=n*Mathf.Exp(-Mathf.Abs(t-.042f)*230)*.25f;
                float tail=n*Mathf.Exp(-t*7)*.13f;
                // The low layer arrives just after the report, retaining a crisp initial hit.
                samples[i]=SoftLimit((report+thump+body+mech+tail)*.62f);
            }
            return Clip("Original • weapon report "+kind,samples);
        }
        public static AudioClip Explosion(int size)
        {
            float seconds=1.2f+size*.65f;var samples=new float[Mathf.RoundToInt(Rate*seconds)];uint seed=(uint)(3781+size*41);float low=0;
            for(int i=0;i<samples.Length;i++)
            {
                float t=i/(float)Rate,n=Noise(ref seed);low=Mathf.Lerp(low,n,.045f);
                float snap=n*Mathf.Exp(-t*45)*.5f;
                float pressure=Wave(53*t-7*t*t)*Mathf.Exp(-t*(3.6f-size*.55f))*.48f;
                float sub=Wave((38+size*4)*t-4*t*t)*Mathf.Exp(-t*(4.2f-size*.45f))*Mathf.Min(1,t*95)*(.34f+size*.09f);
                float roar=low*Mathf.Exp(-t*(2.7f-size*.4f))*1.15f;
                float debris=n*Mathf.Exp(-Mathf.Abs(t-.2f)*6)*.11f;
                // Keep the broadband snap above the new low pressure wave for definition.
                samples[i]=SoftLimit((snap+pressure+sub+roar+debris)*.76f);
            }
            return Clip("Original • layered blast "+size,samples);
        }
        public static AudioClip Chirp(bool radio)
        {
            var samples=new float[Rate/6];uint seed=19;
            for(int i=0;i<samples.Length;i++){float t=i/(float)Rate;float f=radio?480:740;samples[i]=(Wave(f*t+500*t*t)*.45f+(radio?Noise(ref seed)*.12f:0))*Mathf.Sin(Mathf.PI*i/samples.Length)*Mathf.Exp(-t*12);}
            return Clip(radio?"Original • radio squelch":"Original • garage switch",samples);
        }
        static float Hz(int midi){return 440*Mathf.Pow(2,(midi-69)/12f);}
        public static AudioClip Music(bool combat)
        {
            float bpm=combat?174:96,beat=60/bpm;int bars=combat?16:8;
            var samples=new float[Mathf.RoundToInt(Rate*beat*4*bars)];uint seed=combat?73193u:83117u;
            int[] garageNotes={0,7,12,7};int[] roots={50,57,54,55};int[] melody={74,78,81,78,76,81,83,81,78,76,74,81,85,83,81,78};
            float kickPhase=0;
            for(int i=0;i<samples.Length;i++)
            {
                float t=i/(float)Rate,b=t/beat;int beatIndex=(int)b,bar=beatIndex/4,root=roots[(bar/2)%4];float phase=b-beatIndex,bt=phase*beat;
                float n=Noise(ref seed);float signal=0;
                if(combat)
                {
                    float kickHz=45+135*Mathf.Exp(-bt*40);kickPhase+=kickHz/Rate;
                    signal+=Wave(kickPhase)*Mathf.Exp(-bt*15)*.48f;
                    if((beatIndex%4)==1||(beatIndex%4)==3)signal+=n*Mathf.Exp(-bt*22)*.2f+Wave(bt*185)*Mathf.Exp(-bt*28)*.1f;
                    float hatTime=(b*2-Mathf.Floor(b*2))*beat*.5f;signal+=n*Mathf.Exp(-hatTime*95)*.065f;
                    float offbeat=Mathf.Max(0,(phase-.5f)*beat);
                    if(phase>.5f)signal+=(Wave(t*Hz(root))+.18f*Wave(t*Hz(root)*2))*Mathf.Exp(-offbeat*12)*.15f;
                    int note=melody[((int)(b*2)+bar/4*3)%melody.Length];float noteT=(b*2-Mathf.Floor(b*2))*beat*.5f;
                    float duck=.35f+.65f*Mathf.Clamp01(bt/.08f);
                    signal+=(Wave(t*Hz(note))*.075f+Wave(t*Hz(note)*1.004f)*.055f+Wave(t*Hz(note)*2)*.018f)*Mathf.Exp(-noteT*4)*duck;
                    signal+=(Wave(t*Hz(root+12))+Wave(t*Hz(root+19))+Wave(t*Hz(root+24)))*.017f*duck;
                }
                else
                {
                    if(beatIndex%2==0){kickPhase+=(48+50*Mathf.Exp(-bt*35))/Rate;signal+=Wave(kickPhase)*Mathf.Exp(-bt*19)*.22f;}
                    if(beatIndex%4==1||beatIndex%4==3)signal+=n*Mathf.Exp(-bt*40)*.09f;
                    float sub=(b*2-Mathf.Floor(b*2))*beat*.5f;signal+=n*Mathf.Exp(-sub*150)*.024f;
                    signal+=Wave(t*Hz(root-12))*Mathf.Exp(-bt*5)*.13f;
                    int note=root+12+garageNotes[beatIndex%4];
                    signal+=(Wave(t*Hz(note))+.3f*Wave(t*Hz(note)*2)+.12f*Wave(t*Hz(note)*3))*Mathf.Exp(-bt*5)*.15f;
                    signal+=Wave(t*Hz(root+19))*.015f;
                }
                samples[i]=Mathf.Clamp(signal,-.92f,.92f);
            }
            int edge=Rate/50;for(int i=0;i<edge;i++){float gain=i/(float)edge;samples[i]*=gain;samples[samples.Length-1-i]*=gain;}
            return Clip(combat?"Original soundtrack • Invoice at 174 BPM":"Original soundtrack • Last Bay on the Left",samples);
        }
    }
}
