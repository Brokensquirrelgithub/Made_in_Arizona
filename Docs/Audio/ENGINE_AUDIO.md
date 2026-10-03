# Engine audio

`engine_before.wav` and `engine_after.wav` are the same 15-second drive (idle, full-throttle pull, lift-off coast, cruise, second pull, lift) rendered offline from `AudioSynthesis.cs` with the in-game mixing rules. Both are level-matched so the difference is tone, not volume. In game the new engine also plays about 1.5× louder. `hurt_and_heartbeat.wav` has the three hull-impact variants followed by the critical-damage heartbeat.

## How studios usually do it

- **RPM-layered loops (granular or crossfade).** Racing and vehicle-combat games record an engine on a dyno at a handful of fixed RPMs, separately **on load** (throttle open) and **off load** (overrun). At runtime, neighbouring loops are crossfaded by RPM and blended by throttle. Middleware such as FMOD/Wwise blend containers, or dedicated tools like REV, handle the mixing. Each loop is only pitch-shifted a little, so the exhaust and intake resonances stay put instead of sounding like a chipmunk.
- **Firing-order pulses.** The sound is a train of exhaust pressure pulses at the firing rate (RPM ÷ 60 × cylinders ÷ 2 for a four-stroke). A V8's growl and "burble" come from **uneven pulse strength between cylinder banks**, which adds energy at the crank and cycle rate below the firing tone. Small timing and strength variation between combustions adds roughness.
- **Fixed resonances plus saturation.** Pipes, mufflers and the body act as filters with fixed formants. Driving the signal into saturation creates dense harmonics above each pulse, which is where aggression lives. It is pushed harder on load.
- **Load character and detail.** Overrun is softer and duller, often with crackle and pops from unburnt fuel. Gear-shift cuts, turbo whine and intake noise are separate layers.

## What the game does now

The old engine was two pure-sine loops pitch-shifted across a 5× range. There was no bank imbalance, no load change and little saturation, so it sounded like a hum.

The new bank (`AudioSynthesis.EngineLayer`, `AudioManager.UpdateEngineBank`) applies the techniques above in synthesis:

- Three layers at 48, 104 and 200 Hz firing rates, each on load and on overrun. Each loop holds a whole number of engine cycles and wraps seamlessly.
- The cross-plane V8 bank pattern (firing order 1‑8‑4‑3‑6‑5‑7‑2), with ±12% pulse strength and ±3.5% timing jitter.
- Each pulse excites a pressure front plus fixed 68 Hz body, 185 Hz pipe and 540 Hz rasp resonances, plus combustion noise.
- tanh saturation, harder on load; overrun is low-passed.
- Equal-power crossfade across RPM (in octaves) and throttle, plus overrun pops after lifting off at high RPM.

Offline measurement of the drive above: energy in the 150–1500 Hz "growl" band rose from 28% to 42% of the total. The loop seams are smaller than ordinary sample-to-sample steps, so they don't click.

Tuning points: `BankPulse` (imbalance, which controls lope), the resonance frequencies and decays in `EngineLayer`, `drive` (saturation), and the `level` line in `UpdateEngineBank`.

The in-game Dev Tuning → Engine tab exposes the synthesis parameters for combustion variation, low body resonance, high exhaust rasp and saturation. These regenerate the six engine loops after slider movement settles, keeping synthesis out of the audio update loop. Pitch, on-load and overrun levels, turbo whine, nitro roar, exhaust pops and shift levels update live. All values are saved with developer tuning; older saves receive the original sound as their default.

## Pulse and smoothing A/B

Dev Tuning → Engine now has **A • Original** and **B • Preserved Edges** buttons. A keeps the original one-pole smoothing applied to the whole generated waveform. B generates the same source pulse at 44.1 kHz, skips that broad smoothing, applies the same saturation, then downsamples to the 22.05 kHz clip rate with a 63-tap windowed-sinc low-pass filter. The filter passes the audible edge below roughly 8 kHz and rejects frequencies above the output Nyquist to avoid aliasing. B is RMS-matched to A per RPM/load layer, and the two banks crossfade briefly when switched. Pulse/tone edits regenerate both banks after movement stops; A/B switching itself requires no regeneration. Select A or B and use Save & Resume to compare while driving.

**Source pulse pressure** changes the pressure front relative to body, pipe, rasp and noise. **Source pulse attack** sets how quickly the front rises; higher values make a sharper edge. **Source pulse decay** sets how quickly it dies away; higher values make a shorter hit. The existing **Combustion variation / V8 lope** control changes firing timing, strength variation and bank imbalance. Set these with A selected to hear the underlying pulse, then switch between A and B at the same RPM and throttle to judge what the smoothing removes. Older saves start on A so their sound is preserved.
