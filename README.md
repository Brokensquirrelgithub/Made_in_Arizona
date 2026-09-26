# MADE IN ARIZONA

**Technically informed stupidity.** A native Unity twin-stick vehicle-combat prototype set in a fictional Arizona, where one unpaid repair becomes everyone's problem.

## Play

- **macOS:** open `Builds/macOS/Made in Arizona.app`.
- **Windows:** copy the complete `Builds/Windows` folder to a PC and open `Made in Arizona.exe`. Keep the Data folder and DLLs beside it.
- **Unity:** open this folder with **Unity 6000.5.5f1**, open `Assets/MadeInArizona/Scenes/Main.unity`, and press Play. Packages restore automatically. If using an external drive causes Unity asset-database errors, put a working copy on an APFS/NTFS volume.

From the main menu, choose a **seed** and **map width (0.8–3.2 km)**, then click **Start Campaign**. Generation creates an Arizona-shaped world with towns, winding roads, hills, forest, rivers and hidden salvage. Saved upgrades and completed jobs are retained. Open **Garage** for vehicle selection, parts and Dispatch. The first job takes you from chasing a delinquent customer through a junkyard fight to recovering evidence and returning it to the garage. Follow the gold objective ring; turquoise rings mark optional salvage.

For immediate vehicle combat, choose **Combat Trial** in Dispatch or press **F2** / gamepad **X** from the garage. Fight two waves of three vehicles using your selected build. The trial has its own replay and does not award money or advance the campaign.

Enemy colors reveal six factions. Red Sunsprawl Security fights at range; sand-colored Courtesy Compliance controls roads with mines and mortars; green Road Scavengers rush with shotguns and miniguns. Coral Open House Realty circles you like a persistent agent, zoning with mines and rockets. Pale blue Snowbird Convoys are slower, tougher rolling roadblocks with mines and shotguns. Purple Car Otaku Club cars make fast flanking passes with miniguns, rams, and precision shots. The combat trial introduces three factions in each wave; missions, hideouts, and road patrols use their own mixes. Each crew has its own weapon drops. Warning lines show committed special attacks; move across the line to dodge. Destroyed enemies sometimes drop repair, nitro, scrap, or a weapon. Drive over supplies to collect them. Drive over a weapon to equip an empty field slot, or press **F / Y** nearby to swap your current field weapon. Enemy labels hint at the field weapons they may drop. Stronger and stranger weapons have limited ammunition. Green hit marks confirm damage and a banner confirms a disabled hostile vehicle.

## Controls

| Action | Keyboard / mouse | Xbox-style gamepad |
|---|---|---|
| Drive toward direction | WASD | Left stick |
| Aim turret | Mouse | Right stick |
| Garage weapon | Left mouse | Right trigger |
| Field weapon pickup | Right mouse | Left trigger |
| Swap field weapon | F near a drop | Y near a drop |
| Handbrake | Space | B |
| Boost | Left Shift | Right shoulder |
| Field repair | R | Left shoulder |
| Recover marked objective | E | A |
| World map / waypoint / crafting | M or click minimap | — |
| Pause | Escape | Start |
| Settings | F1 | Y in garage or pause |
| Garage station | Tab | LB / RB |
| Garage selection / confirm | Mouse / Enter | D-pad / A |

The car turns toward your movement input while the turret aims independently. At low speed, hold a direction behind the car to engage reverse and back away from a wall; push ahead to return to forward drive. Release movement to brake; use the handbrake to rotate. Field repair and boost recharge, while larger enemy supply drops pull toward the car from within 12 metres. Weapon drops stay anchored for deliberate pickup and swapping. Buy garage weapons with scrap and choose one for the right trigger. The left trigger is reserved for enemy weapon drops. Suzuki is always safe.

## Generated world

The wilderness now uses dense streamed grass, shrubs, branching trees, stones, boulders, flowers and deadwood, with wind and small bird flocks. Roads have smooth curves, gravel shoulders and worn markings; towns have detailed storefronts and lot clutter.

Towns are charted; off-road secrets reveal as you approach. Press **E** near specialty salvage. Open **M** to place a waypoint and craft weapon improvements using recovered alloy, circuits and propellant. Northern hideouts are stronger; drivetrain, suspension and weapon upgrades help with difficult terrain and fights.

Edit the live `world-generation.json` path shown in the main menu to change terrain, vegetation, rivers, towns and authored locations. Valid edits regenerate the active world automatically, **restarting the current sortie** while keeping saved progression and discoveries. See [JSON configuration](Docs/WORLD_GENERATION_CONFIG.md).

## Developer tuning

During a mission, press **Escape → Dev Tuning / Mouse**. Four tabs expose driving, combat, camera, and light/color controls. Changes apply immediately and save automatically across launches. Use **Save & Resume** to drive with the new values. **Reset All Defaults** restores the nimble baseline and default effects. Health changes preserve the current health percentage. Small-prop momentum applies only when the prop breaks; solid walls and large structures still resist the car.

Suzuki is your white husky girl and senior recovery specialist. She leaves her bed and wanders a garage route, pausing to sniff.

The streamed wilderness uses the Pandazole Nature pack with batched conifers, broadleaf trees, cacti, rocks and ground cover. Occasional hostile road patrols spawn off screen after leaving the starter area. Explosions cast bright local light and quality-dependent soft shadows. Pause → Dev Tuning controls live bloom, vignette and an orthographic depth blur that keeps the vehicle in focus. The remaining requested Asset Store packs are tracked in [Docs/ASSET_PACKS.md](Docs/ASSET_PACKS.md).

## Included

- Garage and a complete four-stage opening mission that introduces Johnny's repair shop, Stallion's first field job and Suzuki's unusual command of paperwork, with replay, scoring, optional salvage and extraction.
- Fifteen original campaign jobs across recovery, demolition, convoy escort, racing/escape, defense, collection/rescue and component-based bosses. Campaign sorties use the same seeded regional layout, with mission-specific objectives.
- Eight distinct vehicles, a fixed protagonist named Stallion, mechanical parts with compatibility and tradeoffs, drivetrain/differential changes, final-drive and ride-height tuning.
- Nine terrain types, localized component damage, garage and limited-ammo field weapons, enemy supply drops, tactical enemy archetypes, destructible structures/props, nine explosion profiles, bounded debris and projectile pools.
- Suzuki the white husky with four cosmetics, discoveries, achievements, a Wonton Destruction food truck, a 14-track supplied soundtrack, imported weapon recordings and synthesized engine/interface effects.
- HDR bloom, normal-mapped surfaces, fur backlighting, garage light shafts, blast heat shimmer, animated six-way-lit smoke/fire using Unity’s free fluid samples, lingering embers, and layered engine/exhaust sound.
- Saved progression/settings, keyboard/controller rebinding, audio sliders, subtitles, aim assist, difficulty, shake/UI scale, window/resolution controls and four graphics presets including **Arizona Summer**.

## Scope and development

This is a **playable vertical slice and campaign framework**, with original procedural prototype art. The full requested production game, bespoke campaign regions, AAA effects, comprehensive hardware profiling, advanced vehicle simulation and storefront signing are further development work. There are no missing-art dependencies that prevent playing.

See [development notes](Docs/DEVELOPMENT.md) for architecture, extension points, save paths and build commands, and [test results](Docs/VALIDATION.md) for the exact verification status.
