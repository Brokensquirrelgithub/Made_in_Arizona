# Physical engine audio (experimental)

Dev menu → ENGINE → **C • PHYSICAL** selects it; A and B keep the original loop bank for comparison. New and migrated saves start on C.

## Model

The approach follows AngeTheGreat's [engine-sim](https://github.com/ange-yaghi/engine-sim) (MIT, © 2022 Ange Yaghi): sound is the exhaust pressure of each cylinder, delayed by its header length, summed per collector and shaped by the exhaust system. No engine-sim code or impulse-response recordings are used; the expensive stages are replaced:

| engine-sim | Here | Why |
|---|---|---|
| Rigid-body crank/rod/piston solver at 10–40 kHz | Kinematic crank from `VehicleController.RPM` | It only produces RPM, which the game already has |
| 4 compressible flows × 8 substeps per cylinder | Closed-form compression/combustion/expansion per cycle; only exhaust blowdown is integrated, once per sample, with a choke-limited orifice | The audio only reads the exhaust runner |
| Per-cylinder header delay | Damped round-trip reflection in each primary, plus a separate engine-bay radiation path | Each runner's length now gives a short resonant ring instead of one delayed copy of the firing pulse |
| Up to 10,000-tap IR convolution per exhaust | Tailpipe waveguide (open-end reflection, loop damping) + muffler modes over a low-passed path | Same resonances for a fraction of the cost; no recordings |
| Full auto-leveller | One-time loudness calibration per layout + a leveller that only lifts quiet passages | Keeps on-throttle vs overrun loudness differences |

Runs at half the output rate (24 kHz at 48 kHz output), linearly upsampled, on the audio thread via `OnAudioFilterRead`. The smoke test measures the worst smoothed load in the Windows Mono player against a 5% of one core budget per voice.

The physical voice now mixes the warm header path with the muffled tailpipe path before a short exhaust/body decay. The default **Header / tailpipe balance** is 0.7 toward header; 0 isolates the tailpipe and 1 isolates the headers. It is a per-car ENGINE control. Older saves and per-car profiles inherit the new default. **Exhaust resonance** still changes how strongly waves reflect in both paths.

The physical voice also has two independent, optional non-exhaust layers. **Intake induction** is filtered air noise that rises with throttle opening and RPM. **Accessory whir** follows engine RPM through a rounded pulley tone and remains present when the throttle is released. Each has a per-car ENGINE level slider and an adjacent one-click ON/OFF button. A level of zero skips that layer's DSP; ON restores the default level of 1. Older saves inherit both layers on, while a deliberately saved zero stays off. Neither layer affects the original exhaust random stream, so A/B comparison stays meaningful.

The per-car **Drive-aware response** control defaults to 1 and has an ON/OFF button. At 0 it sends the original `RPM` and `Throttle × ShiftDrive` into the physical voice exactly, so the earlier sound is always available. At 1 the sound's charge follows the throttle actually used for wheel drive (including steering alignment, drift and shift cuts) with a short filling lag. Wheelspin or loss of drive contact lets the sound RPM rise above the wheel-derived RPM, then settle back when traction returns. The response runs once per game frame; the audio thread's per-sample cost is unchanged. Older saves start with it on, while a saved zero stays off.

Engine load remains an approximation: petrol cylinder pressure follows a simplified throttle-dependent trapped charge; diesel burn follows the load command; turbo spool follows throttle and revs. The quiet-passages leveller reduces, but does not erase, on-throttle loudness contrast. The model does not solve actual wheel torque, combustion timing, or crank acceleration from road load. A hard pull against an obstacle stays loud at low RPM rather than interpreting low vehicle acceleration as low load.

## Files

- `Scripts/Data/EngineLayout.cs`: layout data and presets (`EngineLayouts`). UnityEngine-free.
- `Scripts/Audio/EngineVoice.cs`: the DSP. UnityEngine-free, allocation-free after construction.
- `Scripts/Audio/EngineVoiceSource.cs`: Unity audio-thread host.
- `VehicleDefinition.engineLayout`: per-vehicle layout id, set in each authored vehicle asset.

## Vehicles

| Vehicle | Layout | Notes |
|---|---|---|
| Atlas Shift / Scarface (Geo Metro) | `inline3` | G10 1.0 L, 1-3-2 every 240°, 3-into-1 cast manifold, 800–6000 rpm |
| Juniper JX | `inline4` | 1.3 L, 1-3-4-2 |
| Volkswerks Monsoon (2007 Jetta) | `inline5` | Base 2.5 L five, 1-2-4-5-3 firing order |
| Sixstar Dustback (2002 Outback) | `boxer4` | 2.5 L horizontally opposed four, unequal exhaust runners |
| Takumi AE 867 Runner (AE86) | `inline4-sport` | 1.6 L DOHC four, 7500 rpm redline |
| Coronado Executive (1990 Sedan de Ville) | `v8-luxury` | Quiet 4.5 L cross-plane V8 |
| Sunskip Trophy 900 | `v8-crossplane` | Dual long-tube, banks kept separate |
| Foreclosure 6.8D | `inline6-diesel` | Turbo-diesel, block knock, 700–3300 rpm |
| Side Hustle Tradesman | `v6` | 60° V6, Y-pipe with longer crossover side |
| Perennial Half-Ton | `v8-truck` | Cross-plane, single exhaust |
| Skitter Sport 1000 | `v3-bike` | 1.0 V3, uneven 255°/210°/255° firing, to 11,000 rpm |
| VINcent | `v8-flatplane` | Two even-firing fours, one pipe per bank |

The other stock cars select the corresponding three-, four-, six- or eight-cylinder layout in `EngineLayouts.ForVehicle`; the smoke test checks all 22 assignments and their authored assets. The game tachometer (850–7200) maps onto each layout's own idle–redline range. The 1.8 L salvage-yard engine swap replaces the stock voice with a four-cylinder `inline4-swap` layout.

The [2007 Jetta specifications](https://files.directechs.com/directwirebrochures/brochure2249_0.pdf) list the base 2.5 L inline five and its firing order. [Subaru's Outback information](https://www.subaru.ca/WebPage.aspx?ArticleID=680&WebPageID=4749&WebSiteID=282) identifies the 2.5 L boxer four. [Toyota's AE86 history](https://www.toyota-global.com/company/history_of_toyota/75years/vehicle_lineage/car/id60003763/index.html) identifies the 1.6 L DOHC inline four. The game's power and speed remain gameplay values, not manufacturer specifications.

## Automatic shifts

The automatic shifts at 6900 game tach RPM under full throttle (about 95% of the selected layout's idle–redline span), easing toward 5000 at light load. Cars use representative four-, five- or six-speed ratios chosen for their inspirations. The tach ratio is calibrated to each car's game speed range so upper gears are reachable on the compressed roads; wheel force still uses the mechanical ratio. An upshift takes 0.34 seconds, with a short gear hold: the tach drops toward the next ratio immediately, engine load dips, and wheel drive eases back in. The physical audio response suppresses free-rev during that shift so wheelspin cannot hide the drop. The HUD shows the current gear and total. The shared game tach range remains an abstraction of each engine's real RPM.

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

`-miaSmokeTest -miaEngineAudioTest`: every vehicle resolves a valid layout and the roster's cylinder counts match the stock inspirations; every layout renders finite, audible, unclipped audio under 5% of a core; the three-cylinder and cross-plane V8 idle retain body between firing pulses; the header/tailpipe control selects distinct audible paths; intake induction responds to throttle and accessory whir remains on overrun, each with a true zero-level bypass; drive-aware response passes through the original RPM/load exactly when off, keeps a hard pull loaded at low RPM, raises sound RPM during wheelspin, and follows effective drive throttle after cuts; older saves get the new defaults while saved zeroes remain off; the Shift's three-cylinder firing period is present at 3000 rpm; the live voice follows the player's vehicle, renders on the audio thread and goes silent when A or B is chosen; the Shift turbo spools under load, coasts down on lift and softens the pulses; installing/removing the turbo or engine swap rebuilds the live voice. Set `MIA_AUDIO_DIR` to write a 4-second idle → full-throttle sweep → overrun WAV per layout.

## Not modelled yet

The Roots blower, compressor surge flutter, valve overlap reversion, collector coupling between cylinders, crank speed ripple, starter/cranking and shutdown transients, and detailed valvetrain motion. Gear shifts and turbo whistle are already present, but continuous gearbox whine is not. Enemy vehicles still have no engine voice.
