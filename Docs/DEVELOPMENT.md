# Made in Arizona — development notes

This project implements a native Unity vertical slice with original procedural prototype models, synthesized original audio, a garage, a four-stage opening mission, and a 15-job campaign built on reusable mission state machines. It is an early playable prototype, not a finished AAA game.

## Runtime

Open `Assets/MadeInArizona/Scenes/Main.unity` and press Play. `GameManager` owns session state and initializes independent systems. `WorldBuilder` builds deterministic garage/desert layouts; scene changes clear the previous world before enabling new colliders. Player physics uses a rigidbody with four suspension rays, torque/horsepower limits, gearing, terrain friction, drivetrain/differential response, heat and localized damage. Pitch/roll are animated; full mechanical articulation and rollover simulation are future work.

`ContentCatalog` loads editable ScriptableObjects from `Resources/Content`, with original code-authored defaults. Add a new definition to the catalog and save it as a matching resource. Parts enforce vehicle compatibility and one installed part per category. Power, weight, cooling, torque, grip, suspension and gearing tradeoffs affect driving.

`MissionManager` implements recovery, demolition, defense, escort, checkpoint race/escape, collection/rescue, and boss stages. Mission 1 has a pursuit, junkyard fight, interactable evidence and extraction. Later missions share one modular map with scenery/weather variations; their narratives and objective sequences are complete, but bespoke regions/cinematics are not.

`ProjectileSystem` sweeps and pools up to 320 projectiles. `ExplosionSystem` bounds particle banks, queued chain reactions, debris, shockwaves, light flashes and scorch marks. There are nine explosion profiles. Its particles use Unity ParticleSystem, not a custom GPU/VFX Graph simulation. Arizona Summer increases real budgets; 3080/Apple Silicon performance must be profiled on those machines before making a 60 FPS claim.

`EnemyAI` uses local obstacle/hazard avoidance and distinct movement/weapon tactics. The boss exposes five damageable machine components. Global navigation through arbitrarily complex maps, a towing solver, winch simulation, streamed regions, volumetric smoke and physically simulated refraction remain extension work. The implemented heat shimmer samples the opaque scene through a soft animated refraction mask.

Suzuki is a white husky with blue eyes, upright ears, a curled plume tail and a soft neck ruff. He is a cosmetic animated dog with no colliders or damage component, present in the garage and riding with the player. Four original cosmetic variants are available. Pixel portraits and geometry are original procedural prototype art. Smoke/fire use Unity-provided baked fluid maps with source attribution; no copyrighted vehicle meshes, music samples or dialogue were used.

## Vehicle combat trial and feedback

`GameManager.StartCombatTrial()` creates a separate proving-ground arena with two waves of three hostiles, using the selected vehicle and parts. `MissionManager.BeginCombatTrial()` owns its temporary mission definition; its completion path never indexes campaign arrays or changes money/unlocks. Retry dispatches to the same mode. `WorldBuilder.BuildCombatArena()` provides driving space, breakable cover, propane hazards and concrete boundaries.

`CombatFeedback.ReportHit` is called once by `VehicleDamage` after actual damage and death state are known. It filters player/friendly attacks against hostile vehicles, so direct bullets, shotgun pellets, rockets and collision damage share hit/kill confirmation. The UI draws hit markers, enemy health/role labels, attack warnings, damage-edge feedback and weapon readiness. Tracers and muzzle flashes remain pooled or attached to each weapon; turret recoil is visual.

Enemy destination steering is independent of its combat target. Short primary bursts leave recovery windows. Six factions change primary loadouts, steering, special attacks, HUD/radar colors, and field-weapon drop pools. Snowbird and Car Otaku vehicles also change durability or speed, and the three newer crews add roof or wing accessories. Sunsprawl Security favors accurate range fire; Courtesy Compliance holds a wider ring with mines and mortars; Road Scavengers close distance; Open House Realty circles and zones; Snowbird Convoys block routes in tougher, slower vehicles; Car Otaku Club makes fast flanking passes. Rocket, sniper, ram, mine and mortar attacks commit their aim before launching with visible warnings. Missions assign authored factions; the combat trial uses all six, and hideouts and road patrols populate the open world. The garage weapon fires without a heat lock. Hostile wrecks can drop health, nitro, scrap and faction-specific limited-ammo field weapons; the player drives over supplies and uses F / Y to swap an occupied field slot.

Add `-miaCombatTest` to the native smoke-test command to exercise the proving ground. It checks the three opening factions and their loadouts, all six crews across both waves, the empty field slot and limited-ammo pickup weapon, then drives and aims an automated player against moving, firing enemy vehicles. Hostiles are destroyed by real projectiles; the integration driver receives continuous repairs to keep both waves observable. It also checks enemy damage, feedback, replay, and unchanged campaign progression. This is functional combat coverage, not a human difficulty-balancing assessment.

## Six-way smoke and fire

The user-supplied reference is Unity's [six-way lighting article](https://unity.com/blog/engine-platform/realistic-smoke-with-6-way-lighting-in-vfx-graph). The game now uses the free fireball and smoke flipbooks linked by that article: 64 simulation frames per effect, six directional response channels in paired textures, separate opacity and fire emission. Exact downloads and source attribution are in `Assets/MadeInArizona/Resources/SixWay/SOURCE.md`.

`SixWaySmoke.shader` implements the technique directly in URP with pooled ParticleSystem renderers. It is not an HDRP migration or VFX Graph asset. Particle tangent/bitangent/normal vectors transform each light direction into the baked simulation's basis; squared directional weights blend the six maps. A softened main-light shadow contribution, up to eight local lights, ambient lighting, frame blending and depth-softened intersections integrate the fluid sprites with the scene. Fire uses the separate emissive mask with an orange-to-hot-core gradient, preserving non-emissive folds. Opaque ground depth clips particles correctly. This is simulated fluid baked into animated sprites, not live 3D fluid simulation or inter-particle shadowing.

A few large rolling lobes replace hundreds of radial blobs. Persistent fuel fires emit animated flame and smoke with buoyant drift and pooled flickering point lights; sparks and rigidbody debris remain separate. The neon pressure-ring outline has been removed from detonations. `-miaSmokeTest -miaUltraTest -miaVfxReview` creates close-up day/front/back/local-light captures with the simulation frozen for the lighting comparison.

## Effects and quality

High and Arizona Summer enable screen-space heat refraction around blasts and persistent fuel fires. Garage light shafts use additive, dust-modulated billboards: an artistic scattering approximation, not ray-marched volumetric lighting. White fur uses wrapped diffuse lighting, backlighting transmission and grazing-angle sheen as an inexpensive subsurface approximation. Shared procedural normal maps add surface grain to matte bodies and scenery. HDR emissive neon and blast particles feed URP bloom, with ACES tonemapping.

Low/Medium/High/Arizona Summer cap active physical debris at 45/100/220/640. High/Ultra cap refraction patches at 12/32 and persistent fires at 12/32; old patches expire and reuse objects. Low and Medium use reduced texture mip levels; High and Ultra retain full 4K flipbooks. Persistent fire lights are capped at six on High and twelve on Ultra. Ultra enables 4096-pixel sun shadows, eight additional lights per object, higher bloom filtering, more embers and longer-lived smoke. Quality changes fully refresh world-owned pools when entering the next garage or mission. There is no artificial workload added solely to heat the GPU, and no RTX 3080 performance claim.

Engine audio combines firing harmonics, a separately synthesized loaded exhaust, compressor whine, road texture, wind, gear engagement and throttle-release bypass hiss. RPM and throttle drive pitch and layer gain; explosions duck engine/music levels. All PCM is generated from original synthesis code.

## Native builds

Unity **6000.5.5f1**, Universal Render Pipeline **17.5.0**, Input System **1.19.0**. The editor menu `Made in Arizona` prepares content, validates definitions and builds macOS Universal or Windows x64. Both use Mono; macOS uses Metal, Windows defaults to Direct3D 11 with Direct3D 12 as an additional configured API. Development builds are intended for local testing and profiling, not signed storefront distribution.

Run `Tools/build.sh mac`, `Tools/build.sh windows`, or `Tools/build.sh all`. Set `UNITY_EDITOR` to the editor executable and optionally `MIA_PROJECT_PATH` to an APFS/NTFS working copy. `MIA_BUILD_ROOT` selects the output project directory. Unity's Windows Mono support module is required when building Windows from Mac.

For external drives with database/locking issues, use a local working copy. Do not share the same Unity Library between simultaneously running editors. Library, package caches and native binaries are ignored by Git. The temporary compilation workspace used during development is not required to run either native build.

## Save files

macOS: `~/Library/Application Support/117 Degree Games/Made in Arizona/made-in-arizona.save.json`

Windows: `%USERPROFILE%/AppData/LocalLow/117 Degree Games/Made in Arizona/made-in-arizona.save.json`

SaveSystem uses Unity's platform path, a checksummed envelope, atomic replacement where supported, a backup and recovery from interrupted writes. Progress saves on rewards, purchases, selections and application exit. Missions restart from their briefing; there is no mid-mission checkpoint serialization. Settings, parts, vehicles, best scores, collectibles and achievements persist.

## Validation

`Made in Arizona/Validate content and systems` checks content counts, unique mission IDs, narrative fields and mechanical part effects. Run a native player with `-miaSmokeTest` (add `-miaCampaignTest` for all jobs and `-miaUltraTest` for the highest effects preset) for the automated runtime integration check. It injects an isolated keyboard device, drives through physics, exercises weapons/damage/repair, advances the opening mission using its normal conditions, checks progression, and exits with status 0 on success. Test saves are isolated in the application's temporary cache. The log contains `MIA_SMOKE_RESULTS` and individual checks. This is automated coverage, not a substitute for human playtesting, real Xbox controller testing or Windows hardware validation.

## Next production integration points

- Replace `VehicleVisual` and `RoadsideProps` output with authored LOD prefabs; keep named turret/wheel anchors and forward +Z.
- Implement advanced wheel/axle articulation behind `VehicleController`, preserving `VehicleStats` and component damage APIs.
- Replace individual mission layouts behind `WorldBuilder.BuildMission(index)`, preserving objective/spawn/extraction coordinates.
- Extend `AudioManager` with recorded multichannel vehicle layers while retaining category gain/ducking and original `MusicManager` transitions.
- Extend `ExplosionSystem` with GPU effects and hardware profiles; maintain its bounded budgets and damage queue.
- Add localization/recorded dialogue to `DialogueSystem`; current dialogue is subtitles with radio cues.
- Sign/notarize Mac distributions and sign Windows installers in the release pipeline using owner-provided credentials.

## First playtest handling, audio and road revision

Low-speed directional driving now retains useful steering authority from rest. An automatic reverse gear engages when the requested direction is behind the vehicle at low speed, with heading hysteresis to prevent diagonal gear chatter. Reverse uses ordinary wheel force and collision physics, with a lower speed cap and no boost. Upgrades still affect torque, mass, grip and turn rate. No reset is needed to retreat from a wall.

Roads now use flat, shadow-receiving overlays instead of thin shadow-casting cubes. Asphalt and dirt intersections have distinct heights, asphalt stays above dirt, and crossing paint leaves the junction clear. Ground patches also stop casting shadows onto the surface immediately beneath them. Overlay normals use metre-scaled UVs.

Engine synthesis adds 41/61.5 Hz body, weapons add 72/92 Hz thump, and explosions add 38–46 Hz pressure. Soft limiting replaces hard clipping on shots and blasts. These layers are generated once with the existing clips, with no per-frame synthesis allocations.

The opt-in `-miaHandlingTest` flag adds camera-motion captures of the reported road crossing, road depth/shadow invariants, and starter-vehicle physics checks through injected keyboard input. Test fixtures reposition the vehicle before each scenario and restore it afterwards; escape movement itself uses normal driving forces without resets.

## Persistent developer tuning

Escape opens the pause menu; Dev Tuning opens a mouse-operated panel with Driving, Combat, Camera, and Light & Color tabs. Values live in GameSettings.dev, serialize with the existing checksummed save, clamp finite ranges on load, and migrate old saves to defaults. Slider edits apply immediately, debounce disk saves, and flush on close, loss of focus, or quit. Reset All Defaults changes only developer tuning. Health capacity changes preserve current health fraction.

The baseline player now has 1.6× steering agility and 1.2× acceleration. Small broken props retain 85% of pre-impact velocity through the collision handler; large props and solid walls are excluded. Prop damage and retained momentum are exposed separately. Enemy capacity, outgoing player damage to vehicles, and incoming player damage can be adjusted independently.

DevVisuals applies real URP volume components and the renderer's SSAO feature. Effects include bloom, exposure, contrast, saturation, chromatic aberration, camera-only motion blur, Gaussian depth of field, vignette, sunlight, ambient light and haze. DOF starts disabled. Camera zoom/shake are live. Masked procedural albedo adds dust/pitting over existing surface normals.

`-miaDevTest` adds health/damage, persistence, effect, AO-feature and menu-resume coverage to the native smoke suite; `-miaHandlingTest` includes the stronger turn and breakable momentum tests.

## Generated campaign regression

Run the native player with `-miaSmokeTest -miaGeneratedCampaignTest` to exercise all 15 jobs on generated terrain (seed 173, size 1600). This is separate from `-miaWorldTest` and the compact-map suite. It verifies sequential unlocks, persisted completion/rewards/scores, boss pods, the forced vehicle and airborne achievements. The test uses isolated saves and controlled objective fixtures, and does not replace human navigation or balance playtests.
