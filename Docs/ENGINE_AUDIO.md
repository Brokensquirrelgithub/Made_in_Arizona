# Physical engine audio (experimental)

Branch `experiment/engine-sound-upgrade`. Dev menu → ENGINE → **C • PHYSICAL** selects it; A and B keep the original loop bank for comparison. New and migrated saves start on C.

## Model

The approach follows AngeTheGreat's [engine-sim](https://github.com/ange-yaghi/engine-sim) (MIT, © 2022 Ange Yaghi): sound is the exhaust pressure of each cylinder, delayed by its header length, summed per collector and shaped by the exhaust system. No engine-sim code or impulse-response recordings are used; the expensive stages are replaced:

| engine-sim | Here | Why |
|---|---|---|
| Rigid-body crank/rod/piston solver at 10–40 kHz | Kinematic crank from `VehicleController.RPM` | It only produces RPM, which the game already has |
| 4 compressible flows × 8 substeps per cylinder | Closed-form compression/combustion/expansion per cycle; only exhaust blowdown is integrated, once per sample, with a choke-limited orifice | The audio only reads the exhaust runner |
| Per-cylinder header delay | Same | This is where uneven-header lope comes from |
| Up to 10,000-tap IR convolution per exhaust | Tailpipe waveguide (open-end reflection, loop damping) + muffler modes over a low-passed path | Same resonances for a fraction of the cost; no recordings |
| Full auto-leveller | One-time loudness calibration per layout + a leveller that only lifts quiet passages | Keeps on-throttle vs overrun loudness differences |

Runs at half the output rate (24 kHz at 48 kHz output), linearly upsampled, on the audio thread via `OnAudioFilterRead`. Measured: ~0.5–1% of one core per voice under .NET 8; the worst smoothed load in the Windows Mono player was 2.7%.

## Files

- `Scripts/Data/EngineLayout.cs`: layout data and presets (`EngineLayouts`). UnityEngine-free.
- `Scripts/Audio/EngineVoice.cs`: the DSP. UnityEngine-free, allocation-free after construction.
- `Scripts/Audio/EngineVoiceSource.cs`: Unity audio-thread host.
- `VehicleDefinition.engineLayout`: per-vehicle layout id, set in each authored vehicle asset.

## Vehicles

| Vehicle | Layout | Notes |
|---|---|---|
| Thimble Sprint (Geo Metro) | `inline3` | G10 1.0 L, 1-3-2 every 240°, 3-into-1 cast manifold, 800–6000 rpm |
| Juniper JX | `inline4` | 1.3 L, 1-3-4-2 |
| Sunskip Trophy 900 | `v8-crossplane` | Dual long-tube, banks kept separate |
| Foreclosure 6.8D | `inline6-diesel` | Turbo-diesel, block knock, 700–3300 rpm |
| Side Hustle Tradesman | `v6` | 60° V6, Y-pipe with longer crossover side |
| Perennial Half-Ton | `v8-truck` | Cross-plane, single exhaust |
| Skitter Sport 1000 | `v3-bike` | 1.0 V3, uneven 255°/210°/255° firing, to 11,000 rpm |
| VINcent | `v8-flatplane` | Two even-firing fours, one pipe per bank |

The game tachometer (850–7200) maps onto each layout's own idle–redline range.

## Turbo

Parts with `turbocharger` set (the Thimble's wastegated three-pot turbo) make `VehicleStats.turbocharged` true, and the voice is rebuilt with a turbo between collector and tailpipe:

- **Spool:** shaft speed follows load × revs, reaching about 0.6 a second into a full-throttle pull and coasting down more slowly after a lift.
- **Boost:** up to 0.7 bar, growing with spool² under load. It raises the trapped charge, so the cylinders fire harder.
- **Turbine:** smears each pulse's edge and absorbs up to 40% of it as the shaft speeds up. The turbo Metro is softer and rounder at the tailpipe at about the same loudness; its exhaust energy above 2 kHz drops from 2.2% to 1.5%.
- **Whistle and hiss:** the whistle rises from 1.8 to 7.2 kHz with shaft speed and follows the existing "Turbo whine level" slider. With this voice, the generic whine layer steps aside.
- **Blow-off:** the blow-off sound only plays when there was real boost to vent, louder the faster the shaft was spinning.

Loudness calibration runs without the turbo, so fitting one changes the engine against its stock sound instead of being normalised away.

## Adding an engine

Add a layout in `EngineLayouts.Build()`. `firingAngle[i]` is cylinder i's combustion TDC in crank degrees (0–720) after cylinder 0's, taken from the firing order and crank layout. Set `headerLength`, `collector` and per-collector `pipeLength`/`pan`, then point a vehicle's `engineLayout` at it. Loudness is calibrated automatically.

## Tests

`-miaSmokeTest -miaEngineAudioTest`: every vehicle resolves a valid layout; every layout renders finite, audible, unclipped audio under 5% of a core; the Thimble's three-cylinder firing period is present at 3000 rpm; the live voice follows the player's vehicle, renders on the audio thread and goes silent when A or B is chosen; the Thimble turbo spools under load, coasts down on lift and softens the pulses; installing and removing the turbo rebuilds the live voice. Set `MIA_AUDIO_DIR` to write a 4-second idle → full-throttle sweep → overrun WAV per layout.

## Not modelled yet

The Roots blower and the engine swap part (the voice keeps the vehicle's original layout), compressor surge flutter, intake noise, valve overlap reversion, collector coupling between cylinders, and crank speed ripple. Enemy vehicles still have no engine voice.
