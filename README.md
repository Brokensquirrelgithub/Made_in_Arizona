# MADE IN ARIZONA

**Technically informed stupidity.** A native Unity twin-stick vehicle-combat prototype set in a fictional Arizona, where one unpaid repair becomes everyone's problem.

## Play

- **macOS:** open `Builds/macOS/Made in Arizona.app`.
- **Windows:** copy the complete `Builds/Windows` folder to a PC and open `Made in Arizona.exe`. Keep the Data folder and DLLs beside it.
- **Playtesters:** install once from the [Releases page](https://github.com/Brokensquirrelgithub/Made_in_Arizona/releases), then use **Versions & Updates** in the main menu (or **UPDATES** in the garage) to move to any newer or older published build without reinstalling. Publishing and tester setup: [playtest builds and updates](Docs/UPDATES.md).
- **Unity:** open this folder with **Unity 6000.5.5f1**, open `Assets/MadeInArizona/Scenes/Main.unity`, and press Play. Packages restore automatically. If using an external drive causes Unity asset-database errors, put a working copy on an APFS/NTFS volume.

From the main menu, choose a **seed** and **map width (1.6–4.8 km)**, then click **Start Campaign**. Generation creates an Arizona-shaped world with towns, winding roads, hills, forest, rivers and hidden salvage. Saved upgrades and completed jobs are retained. Open **Garage** for vehicle selection, parts and Dispatch. The first job takes you from chasing a delinquent customer through a junkyard fight to recovering evidence and returning it to the garage. Follow the gold objective ring; turquoise rings mark optional salvage.

For immediate vehicle combat, choose **Combat Trial** in Dispatch or press **F2** / gamepad **X** from the garage. Fight two waves of three vehicles using your selected build. The trial has its own replay and does not award money or advance the campaign.

Enemy colors reveal six factions. Red Sunsprawl Security fights at range; sand-colored Courtesy Compliance controls roads with mines and mortars; green Road Scavengers rush with shotguns and miniguns. Coral Open House Realty circles you like a persistent agent, zoning with mines and rockets. Pale blue Snowbird Convoys are slower, tougher rolling roadblocks with mines and shotguns. Purple Car Otaku Club cars make fast flanking passes with miniguns, rams, and precision shots. The combat trial introduces three factions in each wave; missions, hideouts, and road patrols use their own mixes. Each crew has its own weapon drops. Warning lines show committed special attacks; move across the line to dodge. Destroyed enemies sometimes drop repair, nitro, scrap, or a weapon. Hostile crews never hurt each other with bullets or rams, but their explosions still catch their own cars. Drive over supplies to collect them. Every destroyed hostile drops a weapon from its crew, carrying three magazines of ammunition. Drive over a weapon to equip an empty field slot, or press **F / Y** nearby to swap your current field weapon; a drop of the weapon you already carry is pulled to your car and added to your ammo. Hostiles left off screen close the gap quickly, then fight at normal pace once visible. Enemy labels hint at the field weapons they may drop. Stronger and stranger weapons have limited ammunition. Green hit marks confirm damage and a banner confirms a disabled hostile vehicle.

## Controls

### Co-op playtest

The main menu can host a session for up to four players or join a friend by code through Unity Relay. The host PC runs the world and owns campaign progress. Guests bring their selected car, garage weapon, and vehicle parts. The host controls world balance; car and weapon tuning is owned by a player using each item. This repository is linked to the Made in Arizona Unity Cloud project; setup and current playtest limits are in [co-op setup](Docs/COOP.md).

| Action | Keyboard / mouse | Xbox-style gamepad |
|---|---|---|
| Drive toward direction | WASD | Left stick |
| Aim turret | Mouse | Right stick |
| Garage weapon | Left mouse | Right trigger |
| Field weapon pickup | Right mouse | Left trigger |
| Swap field weapon | F near a drop | Y near a drop |
| Drift | Space | B |
| Boost | Left Shift | Right shoulder (or A: Settings → Controls → Nitro button) |
| Reverse (optional, Settings → Controls) | R | Left shoulder |
| Recover marked objective | E | A (or RB with nitro on A) |
| World map / waypoint / crafting | M or click minimap | — |
| Pause | Escape | Start |
| Settings | F1 | Y in garage or pause |
| Garage station | Tab | LB / RB |
| Garage selection / confirm | Mouse / Enter | D-pad / A |

The car turns toward your movement input while the turret aims independently. At low speed, hold a direction behind the car to engage reverse and back away from a wall; push ahead to return to forward drive. Settings → Controls → Reverse switches the gamepad to a held reverse button (LB): the left stick then never selects reverse, LB alone backs straight up, and LB with the stick swings the tail toward the stick. Release movement to brake; hold drift while steering to flick the tail out and slide through corners without scrubbing much speed (tune it under Dev Tuning → Drift). There is no field repair; repair drops restore the chassis. Nitro shoves four times harder than it used to and hits hardest from a standstill, with blue-to-red flame from the tailpipes and a roaring burn (Dev Tuning → Driving → Nitro thrust scales it). The N2O tank holds three times its old charge but never refills on its own: nitro drops are the only top-up. Cars climb hills at speed: the chassis and collision hull tilt to match the slope under the wheels. Supply drops pull toward the car from within 12 metres. The player takes no damage from landing on or scraping the ground. Scenery smaller than half the car is driven straight through (and knocked apart); pieces between half and the full length of the car break away without costing speed above about 22 km/h. Loose rocks, cacti and dead snags of any size are ploughed through without losing pace (snags snap into trunk sections and limbs). Every rock that stays solid is at least three car lengths across (about 13 m); there are no small unbreakable stones to snag a tyre. Crashes are judged by the closing speed into a surface, so scraping along a cliff or rock costs nothing, while slamming into one head-on hurts the same as hitting a wall. Destroyed cars are smoking shells only — they no longer block or slow anyone. Weapon drops stay anchored for deliberate pickup and swapping. Buy garage weapons with scrap and choose one for the right trigger: twenty scrap weapons, ten core roles plus ten oddball hybrids such as the Lawn-Sprinkler Firebomb (a grenade that sprays a ring of fire) and the Shop-Vac Black Hole. Each has a stated strength and weakness in the garage; see [the arsenal](Docs/WEAPONS.md). The left trigger is reserved for enemy weapon drops, including the seeking Dynamite Go-Kart. Suzuki is always safe.

## Generated world

The wilderness now uses dense streamed grass, shrubs, branching trees, stones, boulders, flowers and deadwood, with wind and small bird flocks. Roads have smooth curves, gravel shoulders and worn markings; a web of dirt roads, two-track trails and footpaths of varying width winds between them, linking every point of interest. Towns have storefronts lining their main street and lot clutter.

Every map has at least seven towns joined by a highway network. Three elevation levels (about 13 m apart) are tinted by altitude and hill-shaded on the map; cliff walls between them are impassable. Towns are charted; off-road secrets reveal as you approach. **Regenerate Map** (pause menu, world map or main menu) rolls a new seed and rebuilds the world, restarting the current job with progress kept. The escort van plans its route along the roads with A* over the terrain and never tries to climb a cliff. Press **E** near specialty salvage. Open **M** to place a waypoint and craft weapon improvements using recovered alloy, circuits and propellant. Northern hideouts are stronger; drivetrain, suspension and weapon upgrades help with difficult terrain and fights.

Edit the live `world-generation.json` path shown in the main menu to change terrain, vegetation, rivers, towns and authored locations. Valid edits regenerate the active world automatically, **restarting the current sortie** while keeping saved progression and discoveries. See [JSON configuration](Docs/WORLD_GENERATION_CONFIG.md).

## Developer tuning

During a mission, press **Escape → Dev Tuning / Mouse**. The header names exactly what the current tab edits (EDITING › the car, the weapon in that slot, world balance or this PC's presentation), the weapon tabs show the equipped weapon's name, and rows on mixed tabs that are world settings are tagged. Tabs expose driving, drift, combat, engine sound, two equipped-weapon balance profiles, camera, light/color, reflection controls (sun glints: intensity, angular tolerance, bloom contribution, fade and which materials can flash), and Dirt & Sky (cloud shadows, lens dirt, tyre smoke, skid marks, gravel spray and how fast dust builds up on cars). Driving, drift, and engine sound are stored per car; weapon balance is stored per weapon. Changes apply immediately and save automatically across launches in solo play. Use **Save & Resume** to drive with the new values. **Reset Local Tuning** restores the baseline profiles and effects. In co-op, the host decides whether to save or discard everyone's session tuning when choosing **Leave Co-op**; shared car and weapon sliders are locked for players other than the item's owner. Health changes preserve the current health percentage. Small-prop momentum applies only when the prop breaks; solid walls and large structures still resist the car.

Suzuki is your white husky girl and senior recovery specialist. She leaves her bed and wanders a garage route, pausing to sniff. If the car comes back dusty she fetches the garden hose from the reel by the side wall, trots round the lift spraying each side, the tail and the nose until it is clean, and the reel winds the hose back in.

The streamed wilderness uses the Pandazole Nature pack with batched conifers, broadleaf trees, cacti, rocks and ground cover. Occasional hostile road patrols spawn off screen after leaving the starter area. Explosions cast bright local light and quality-dependent soft shadows. Pause → Dev Tuning controls live bloom, vignette and an orthographic depth blur that keeps the vehicle in focus. The remaining requested Asset Store packs are tracked in [Docs/ASSET_PACKS.md](Docs/ASSET_PACKS.md).

## Included

- Garage and a complete four-stage opening mission that introduces Johnny's repair shop, Stallion's first field job and Suzuki's unusual command of paperwork, with replay, scoring, optional salvage and extraction.
- Fifteen original campaign jobs across recovery, demolition, convoy escort, racing/escape, defense, collection/rescue and component-based bosses. Campaign sorties use the same seeded regional layout, with mission-specific objectives.
- Eight distinct vehicles in glossy clear-coat paint (the Geo Metro–based Thimble Sprint wears Geo Metro green, #4d8a78), a fixed protagonist named Stallion, mechanical parts with compatibility and tradeoffs, drivetrain/differential changes, final-drive and ride-height tuning.
- Nine terrain types, localized component damage, twenty scrap garage weapons and limited-ammo field weapons, enemy supply drops, tactical enemy archetypes, destructible structures/props, nine explosion profiles, bounded debris and projectile pools.
- Suzuki the white husky with four cosmetics, discoveries, achievements, a Wonton Destruction food truck, a 14-track supplied soundtrack, imported weapon recordings and synthesized engine/interface effects.
- HDR bloom, normal-mapped surfaces, fur backlighting, garage light shafts, blast heat shimmer, animated six-way-lit smoke/fire using Unity’s free fluid samples, lingering embers, and layered engine/exhaust sound.
- Saved progression/settings, keyboard/controller rebinding (including optional shoulder reverse and nitro on A), audio sliders, subtitles, aim assist, dynamic camera zoom, terrain camera sway and camera style (Settings → Graphics & Camera; the sway tips the camera a few degrees as you climb, dip and travel so hills read in 3D, and the style switches the gameplay camera between the original orthographic view and a 45° perspective view with the same angle and framing at the car), shadow detail (match preset, High 4K soft or Ultra 8K soft for strong GPUs), a **sunset** time of day (Settings → Graphics & Camera → Time of Day: a low, warm western sun with long shadows, a dusk sky in reflections, darker overall and easier on the eyes, with headlights on), difficulty, shake/UI scale, window/resolution controls and four graphics presets including **Arizona Summer**.

## Scope and development

This is a **playable vertical slice and campaign framework**, with original procedural prototype art. The full requested production game, bespoke campaign regions, AAA effects, comprehensive hardware profiling, advanced vehicle simulation and storefront signing are further development work. There are no missing-art dependencies that prevent playing.

See [development notes](Docs/DEVELOPMENT.md) for architecture, extension points, save paths and build commands, and [test results](Docs/VALIDATION.md) for the exact verification status.
