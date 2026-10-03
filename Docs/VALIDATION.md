# Validation — 12 September 2026

Unity 6000.5.5f1 / URP 17.5.0 / Input System 1.19.0. Native runtime verification uses the Apple M1 host, Metal and Arizona Summer graphics. Recent native captures are 2880 × 1800. This is a playable prototype with automated integration coverage, not a completed production QA cycle.

## Builds

- macOS Universal (arm64 + x86_64): succeeded, zero build errors.
- Windows x64 Mono: succeeded, zero build errors; PE32+ executable verified.
- Editor content validation: passed catalog identity/count, narrative fields, mission limits and upgrade-stat checks.
- Authored resources contain 15 mission definitions, eight vehicles, five drivers, three weapons and 24 parts. Type-specific resource folders prevent the first mission and primary weapon's shared ID from colliding.

Exact build output: [build-results.txt](Validation/build-results.txt).

## Native runtime checks

Run the Mac player with `-miaSmokeTest -miaCampaignTest -miaUltraTest -miaCombatTest -miaHandlingTest -miaDevTest`. Set `MIA_CAPTURE_DIR` to capture the garage, mission, junkyard, completion, blast and persistent fire. Test saves are isolated from normal player progression.

**Final result: PASS — all 60 checks, exit code 0; no runtime exceptions or kinematic-body warnings in the final run.**

The 60 checks cover garage boot, white-husky presence, save round trip, world destruction setup, injected keyboard driving through Rigidbody physics, all three weapons, swept projectile hits, injected gamepad movement/autofire, damage/repair, pause/resume, all opening-mission stages, extraction/rewards/persistence, garage return, mechanical upgrades and vehicle selection. Extended checks cover all remaining campaign objective sequences, the forced-vehicle job and damageable components on both bosses. Ultra verifies the custom heat-refraction, subsurface and six-way smoke shaders and the presence of all four downloaded fluid texture maps while rendering the effects.

Mission progression tests use proximity teleports, direct enemy/component damage and accelerated defense timers to exercise the actual objective state machines. They do not establish human difficulty balance or validate every AI navigation path. Keyboard and controller checks inject Input System devices; physical controller hardware remains untested.

Exact final runtime output: [runtime-results.txt](Validation/runtime-results.txt).

## Visual inspection and fixes

Native screenshots were inspected for readable garage/HUD layout, world visibility, Suzuki's white coat and curled tail, normal-map detail, bloom, light shafts, blast debris, persistent smoke/fire and refracted road markings. Rendering runs found and led to fixes for prohibited constructor-time MaterialPropertyBlock allocation, washed-out UI colors, delayed screenshot capture and velocity assignments to a kinematic garage vehicle. Final captures are in [Screenshots](Screenshots).

## Six-way effects revision

The radial smoke/fire blobs have been replaced with Unity's free 64-frame fireball and smoke simulations from the library linked in the requested reference article. A custom URP shader blends the six directional light-response channels and animated frames, applies ambient/local lighting and depth intersection fading, and uses the separate fire emissive mask. Native comparisons include daylight, front/back lighting with the same frozen simulation, and an orange local light. Exposure and ground-placement issues found in the first captures were corrected; persistent fires now also cast pooled flickering light.

The final campaign run passed all 49 checks with exit code 0 and no runtime exceptions. Both final native builds succeeded with zero errors. Capture 06 shows the final effects at gameplay distance; captures 07–11 show the controlled close-up comparison. This implements six-way lighting in the existing URP/ParticleSystem renderer, not HDRP/VFX Graph or a real-time fluid solver.

## Vehicle combat revision — 12 September

The native full run passed 49 checks. Eight added checks cover combat-trial entry, sustained-fire overheating, cooling recovery, enemy weapon damage, clearing both three-vehicle waves via real projectiles, hit/kill confirmation, unchanged campaign money/unlocks, and trial replay. The integration driver steers and leads moving targets and uses normal weapon/projectile/collision systems; it receives continuous repairs and increased test-only health capacity so both waves can be covered. It does not directly damage enemies or teleport them during the combat fight. The preceding overheat-only phase temporarily stops enemy inputs.

Visual captures 12–14 show vehicle combat, trial completion and a committed enemy attack warning. Enemy role/health labels, radar contacts, weapon readiness, hit confirmation and attack direction lines are rendered in the game. The garage trial button is available with mouse, F2 or gamepad X. A garage caption overlap was found during inspection and corrected.

Sol implemented weapon/projectile feedback; Terra implemented enemy targeting and committed attacks. The main task integrated the arena, mission/replay flow, damage notifications, UI and native verification.

## First playtest revision — 12 September

Both builds succeeded with zero errors. The native Mac run passed all 53 checks with exit code 0, including a responsive 90-degree starter launch, head-on wall reversal, and angled wall reversal. Both escape tests require observed physical wall contact before successful retreat under ordinary keyboard-driven wheel forces; fixtures reposition the car only before and after each test. Four new checks cover these three scenarios plus distinct road depth planes and disabled overlay shadow casting. The existing combat and campaign checks also passed.

Three camera-position captures at the reported northern crossing (`15-ground-motion-0` through `2`) were inspected. The broad ground bands are absent and the asphalt remains continuous over the dirt route. Road overlays retain received shadows and use metre-scaled surface detail.

Sol handled vehicle physics and contact regressions; Terra handled audio synthesis; the parent integrated, corrected road geometry, built both platforms and ran the native tests. Deterministic audio analysis measured stronger engine fundamental body and low-frequency weapon/explosion energy, with generated shot peaks at or below 0.760 and blast peaks at or below 0.752. This is sample-level headroom validation, not a full mix loudness or speaker listening assessment.

## Developer tuning revision — 12 September

Both final builds succeeded with zero errors. The final native Mac suite passed all 60 checks and exited with code 0.

Added a persisted mouse-only pause-menu tuning panel, nimble player defaults, live health/damage multipliers, real URP volume effects and SSAO, masked surface albedo, and compact upper-left radio. Suzuki's garage and campaign references now use she/her. Screenshot 16 verifies the panel layout; screenshot 02 verifies the radio placement and textured world. A HUD overlap found in the first panel capture was fixed before final delivery.

The new checks verify health capacity and immunity, enemy capacity and outgoing damage, save/reload persistence, live chromatic-aberration state, real AO-feature presence, menu resume, and small-prop momentum under physics. Fixtures set initial position/velocity; movement through and out of collisions uses the normal controller. Desktop mouse-click testing was attempted but blocked by the locked Mac; native rendering and programmatic integration checks continued successfully. Windows runtime and subjective handling/effect balance still require playtesting.

## Generated Arizona revision — 12 September

The final Mac and Windows builds succeeded with zero errors. The corrected final build passed all 60 legacy checks (exit 0), including both real-projectile combat waves and all campaign objective sequences. The native generated-world run (`-miaSmokeTest -miaWorldTest`) passed all 14 checks and exited with code 0. Exact output: [world-results.txt](Validation/world-results.txt).

Coverage includes main-menu campaign startup, the Arizona outline, physical terrain chunks and elevation, keyboard-driven travel beyond the old map bounds, deterministic regeneration, a changed seed, safe handling of partial JSON, automatic valid JSON reload, authored locations, E-key salvage collection, persisted discovery, player-only crafting and generation at the 3200-metre maximum. Salvage fixtures teleport to a test cache and provide the second crafting ingredient; the driving check uses ordinary vehicle physics. These checks do not establish natural campaign balance or navigation across every seed.

Native inspection of captures 17–21 verified the main menu, map/minimap, generated starter town, pine geometry and river surface. Testing exposed and corrected NaN heights from fractional noise powers, a starter town overlapping the river, a mission objective incorrectly pointing to the home town, missed interaction edges, JSON polling timing, and coincident town-paving depth. The final starter-driving check covered about 22 metres under keyboard input. The legacy regression run also exposed an unintended legacy enemy-spawn height change, which was corrected. A chain explosion killed the repairing integration driver between frames; the combat fixture now uses increased player health capacity in addition to repairs, leaving normal game health and enemy damage unchanged.

World terrain is a bounded, seeded heightfield with an approximate Arizona polygon, regional biome coloring/vegetation, a river, roads and code-defined town blueprints. Art remains procedural prototype geometry. Progression uses stronger hideouts and vehicle-sensitive terrain rather than hard biome unlock barriers. JSON hot reload and new sorties rebuild transient enemies/destruction while retaining saved campaign progress, discoveries and collected materials. Town blueprints are not yet JSON-authored. Windows runtime remains untested.

## Remaining hardware and production verification

Windows/D3D11/D3D12 execution, Intel Mac execution, RTX 3080 profiling, sustained frame-time/memory soak tests, physical gamepads, audio listening on different speaker systems, human campaign balancing, accessibility review, signing/notarization and distribution testing remain. No RTX 3080 frame-rate claim is made. The art remains original procedural prototype geometry; fur transmission and dust light shafts are inexpensive artistic approximations rather than physical subsurface or volumetric simulation.


## Asset and presentation revision — 13 September

Mac and Windows builds succeeded with zero errors. The native Mac generated-world run passed all 27 checks (exit 0); exact results are in [asset-polish-world-results.txt](Validation/asset-polish-world-results.txt). It covers Suzuki leaving her bed for the garage floor, packaged Nature models and atlas, live vignette state, orthographic blur, bright shadow-enabled explosion lights, and bounded off-screen road patrol spawning, alongside world-generation regressions.

Native screenshot review found and corrected two issues that state-only checks had missed: an unassigned URP post-process resource asset disabled the volume effects, and treating individual FBX mesh components as trees separated canopies from trunks. Models are now combined with their full hierarchy transforms before runtime height scaling. A labeled native model review identified the pack's conifers and broadleaf tree so palms/tropical variants are excluded from the forest catalog. Source vertex arrays are cached, and nearby ecology remains batched in bounded streaming tiles.

Captures 23 (baseline), 24 (depth blur), 27 (vignette), and 26 (explosion flash) were inspected separately. Vignette visibly darkens the edges; blur preserves the near vehicle while softening distant scenery; the explosion lights the ground and produces a bloom halo. Shadow support is checked in the pipeline and light state. Captures 20–22 verify complete Nature assets in the generated landscape, and 25 shows Suzuki on the floor. A non-convex terrain closest-point warning was corrected with a bounds distance estimate. Point-light shadow resolution tiers now limit atlas demand.

Only the already-cached Pandazole Nature package is integrated. The other nine requested packs await authenticated Unity downloads; see [ASSET_PACKS.md](ASSET_PACKS.md). Windows was built, not executed. These checks do not establish RTX 3080 performance or Valheim-equivalent world detail.

## Campaign continuity audit — 13 September

The checkpoint passed the existing 60-check native Mac suite before changes. The updated Mac and Windows builds succeeded with zero errors. A new native generated-terrain campaign suite passed **66 checks**, including all 15 jobs on seed 173 at 1600 metres, sequential mission unlocks, persisted completion/rewards/best scores, the forced vehicle, both bosses' weak points, and grounded versus airborne kill achievements. Exact results: [generated-campaign-results.txt](Validation/generated-campaign-results.txt). Run with `-miaSmokeTest -miaGeneratedCampaignTest`; saves and world configuration use the isolated integration-test directories.

The older campaign suite deliberately uses compact legacy maps; the new flag closes the gap between that coverage and the generated maps used in normal play. It starts from a fresh campaign and advances unlocks normally. Objective fixtures teleport, apply direct damage and accelerate defense timers, so this verifies state-machine and persistence continuity, not natural traversal, AI route reliability or difficulty balance across every seed.

Fixed an achievement regression: absolute world height classified grounded hill kills as airborne. Scoring now requires the player to be ungrounded and more than 2.8 metres above the local terrain (or the legacy zero-height baseline).

The requested audio packs remain absent. The asset-cache, imported-file and runtime-code audit is recorded in [ASSET_PACKS.md](ASSET_PACKS.md).

Final updated-build verification: the full legacy/combat/handling/dev-tuning suite passed **88 checks**, and the world/presentation suite passed **27 checks**, both with exit code 0. Together with the 66-check generated campaign run, all 181 checks passed. Exact current outputs are in `Validation/runtime-results.txt`, `Validation/asset-polish-world-results.txt` and `Validation/generated-campaign-results.txt`. Windows was rebuilt but not executed.

## Pickup, terrain and sunny presentation revision — 26 September 2026

The updated Windows x64 build succeeded with zero errors. Unity content validation passed. The native Windows/D3D11 targeted suite (`-miaSmokeTest -miaPlaytestRevision`) passed all **22 checks**, and the generated-terrain campaign suite passed all **66 checks** across the 15 missions. Results are in [playtest revision](Validation/playtest-revision-results.txt) and [generated campaign](Validation/playtest-generated-campaign-results.txt).

The broader Windows suite (`-miaSmokeTest -miaCombatTest -miaDevTest`) passed all **42 checks**, bringing this revision to **130 passing checks**. [Core results](Validation/playtest-core-results.txt) cover keyboard/controller driving, field pickup/swapping, mission progression, live dev tuning, and defeating both combat waves through real projectiles. Windows test fixtures now keep injected devices active while hidden, exclude physical input that can override those devices, place the projectile target along the actual muzzle ray, and compare health tuning against the player's existing health fraction. These changes are restricted to the opt-in smoke runner.

The targeted run verifies larger supply drops, the spawn delay, paused magnetism, repair/scrap attraction and collection from ten metres away, full-charge nitro retention, stationary weapon drops, finite gentle terrain relief, saved-default migration with custom-slider preservation, low-quality bloom behavior, and playback of a temporary authored weapon clip through the audio pool. No third-party gun recordings were available in the workspace or Windows Unity cache; pack integration still requires its name or location.

Startup and an unrelated driving-slider refresh render through the same session-owned volume. URP settings reapply after pipeline creation, when renderer features exist, and the gameplay camera updates volume state every frame. A full URP render request captures the hidden Windows player's actual scene into a texture, including post-processing. The latest measured center-region image luminance was **0.5474064 before** and **0.5474073 after** the refresh. [Startup](Screenshots/34-sunny-startup.png) and [after refresh](Screenshots/35-sunny-after-driving-slider.png) were visually inspected for sunny colors and scene readability. This verifies consistent startup presentation; it does not establish that the original report was caused by rendering post-processing twice.

## Supplied soundtrack revision — 26 September 2026

Copied all 14 MP3s unchanged from `C:\Users\Broke\Downloads\Made in Arizona Music` to `Assets/MadeInArizona/Resources/Audio/Music`. Every SHA-256 hash matches the original, and all recordings decoded successfully through FFmpeg. Unity imports stereo, 48 kHz streaming clips without preloading decoded album data. Arizona Nation loops in the main menu, Arizonaland loops in the garage/results, and the remaining 12 tracks form a shuffled driving/combat playlist with three-second crossfades.

The updated Windows x64 build succeeded with zero errors. Native Windows/D3D11 soundtrack verification (`-miaSmokeTest -miaMusicTest`) passed all **15 checks**: packaged recordings, streaming format, actual theme playback, automatic advancement through all 12 driving tracks, no immediate repeat across shuffle cycles, reduced pause gain with playback continuing, music mute, explosion ducking, return-to-garage playback, and safe imported-clip cleanup. Exact results: [music-results.txt](Validation/music-results.txt). Track-ending fixtures seek near each recording's end; they do not constitute listening through the full album or subjective mix review.

The soundtrack audit also found legacy sun-intensity assignments in mission, combat-trial and garage transitions. Those transitions now apply the same saved presentation settings as the dev sliders. Native checks verify the sunny intensity before any slider refresh and identical mission lighting after a refresh.

The updated build also passed the existing **22-check** pickup/terrain/presentation suite again. Sunny rendering remains stable across an unrelated slider refresh; the refreshed screenshots above are from this build.

## Soundtrack playback revision — 1 October 2026

The Windows x64 build succeeded with zero errors. Native verification (`-miaSmokeTest -miaMusicExistingTest`) passed all **17 checks** for the 14 currently available recordings: import settings, menu and garage loops, automatic playback through all 12 combat tracks, no immediate repeat at the shuffle boundary, priority over combat effects, pause/mute/ducking behavior, and silence on death while the reserved death recordings are absent. [Exact results](Validation/music-existing-2026-10-01-results.txt). The 12-track check seeks near each recording's end; subjective listening through the entire album remains a manual playtest.

Arizona Highlands and Arizona Lowlands have not been found among the supplied audio files, so their death-screen playback has not been validated. `-miaSmokeTest -miaMusicTest` is the full 16-track check to run after those files are imported.

## Temporary mountain chains — 1 October 2026

Existing desert cliff prefabs now repeat along longer interior impassable ridges and around the entire Arizona outline. Mesas, buttes, and boulder formations are larger, with smaller cover-rock models at their bases. Overlapping box colliders bridge gaps between cliff meshes. The Windows x64 build succeeded with zero errors. Focused native verification (`-miaSmokeTest -miaMountainTest`) passed all six checks across 800 m, 1.6 km, and 3.2 km maps: continuous boundary coverage, solid barriers, open town routes, and retained interior landforms and cover. [Exact results](Validation/mountain-results-2026-10-01.txt). The 1.6 km map generated 246 boundary rocks, 244 base rocks, and 251 chain barriers. Native top-down captures were inspected; the temporary oversized rocks remain visually simple until a better pack is imported.

Follow-up ridge-shape adjustment: interior mountains now favor longer, bent cliff spines over round mesas and boulder piles; the boundary spine wanders slightly, and the minimap shades cliff crests as continuous ridges. Ridge lengths scale down on the 800 m map, and spawn push-out finds a clear opening when overlapping ridge footprints defeat direct projection. The revised Windows x64 build succeeded with zero errors. Focused native verification passed all six checks across 800 m, 1.6 km, and 3.2 km maps; the separate minimap capture check passed. [Exact ridge results](Validation/ridge-results-2026-10-01.txt). See the [current in-game minimap screenshot](Screenshots/mountain-minimap-screen.png) and [larger minimap graphic](Screenshots/mountain-minimap-detail.png).

The proposed [Customizable Rocks and Cliffs PBR - Sample](https://assetstore.unity.com/packages/3d/environments/landscapes/customizable-rocks-and-cliffs-pbr-sample-305629) has not yet been imported because the Windows desktop was locked during this run. The existing desert pack remains assigned in `DesertLandformCatalog`.

## Enemy density and weapon recordings — 26 September 2026

The Windows x64 build succeeded with zero errors. The native enemy-balance suite passed **20 checks**, including fourfold mission/trial/hideout populations, 0.2 enemy health, preserved player/escort health, live health tuning, larger kill gates, unique boss companions and off-screen patrol squads capped at eight. [Enemy results](Validation/enemy-balance-results.txt). Fixtures pause/freeze encounters, unlock missions in the isolated test save and use controlled kills; these checks verify rules and progression gates rather than natural combat balance.

The imported-weapon-audio suite passed **20 checks**, covering all 13 weapon references, actual player/enemy primary fire, enemy rockets, imported ordnance explosions and weapon-volume muting. [Weapon audio results](Validation/weapon-audio-results.txt). All 40 imported WAV payload hashes match the downloaded package, and every file decoded successfully. The final rebuild only corrected enemy-test fixtures; production audio was unchanged after its passing run. At the user's request, broader campaign/combat regression reruns were deferred to manual playtesting to conserve remaining usage.
