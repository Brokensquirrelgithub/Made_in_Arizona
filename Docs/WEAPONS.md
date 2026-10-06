# Arsenal

Every weapon is bolted together from whatever was lying around the shed. Names describe the backyard engineering, not the paperwork. Each weapon has one job it does best and a clear way to lose with it, in the spirit of Halo's weapon sandbox: the assault rifle is the reliable all-rounder, the needler tracks and supercombines, the plasma pistol shuts vehicles down, the Spartan laser deletes a vehicle but punishes a miss, and so on.

Garage weapons are bought with **scrap** and have unlimited ammunition (right trigger / left mouse). Prices are ten times the original prices; the nail gun is free. Field weapons come off wrecked hostiles with limited rounds (left trigger / right mouse).

## Scrap weapons: core roles

| Weapon | Scrap | Halo analogue | Strength | Weakness |
|---|---|---|---|---|
| **Chain-Fed Nail Gun** | 0 | Assault Rifle | Steady damage at any sensible range and it never runs dry. | Low damage per nail; the chain spreads its aim at long range. |
| **Coffee-Can Scattergun** | 120 | Shotgun | Shreds anything at bumper distance, several cars at once. | Pellets fizzle out past twenty metres. |
| **Rain-Gutter Repeater** | 200 | Battle Rifle / DMR | Accurate at range; picks off weak points and fleeing cars. | Slow rate of fire; misses cost you and swarms overwhelm it. |
| **Sawblade Slingshot** | 140 | Heatwave (horizontal spread) | Blades pass through the first car and keep cutting. | Short reach and light damage per blade. |
| **Tennis-Ball Mortar** | 180 | Brute Shot / Cindershot (bouncing lob) | Arcs over cover and bounces into hiding spots. | Small blast and slow shells; hopeless against fast movers. |
| **Cactus-Spine Needler** | 240 | Needler (homing, supercombine) | Spines chase moving cars; seven hits set off a big rupture. | Slow spines, weak until they stack, and cover eats them. |
| **Jumper-Cable Zapper** | 220 | Plasma Pistol overcharge / Shock Rifle (vehicle EMP, arcs) | Stalls engines and guns; arcs through a pack. | Barely scratches the paint on its own. |
| **Weed-Burner Torch** | 260 | Flamethrower / Ravager (burn) | Burning damage keeps ticking after contact. | Fourteen metres of reach and nothing more. |
| **Arc-Welder Railgun** | 320 | Spartan Laser / Skewer (heavy one-shot) | Instant hit at long range that passes through whole convoys. | Two seconds to recharge; a miss hurts. |
| **Leaf-Blower Air Cannon** | 200 | Concussion Rifle / Gravity Hammer (knockback) | Five times the original blast force shoves cars into hazards and off ledges. | Low damage; it moves problems rather than ending them. |

## Scrap weapons: oddballs

Each oddball splices two firing modes into one weapon — the grenade that bursts into a radial spray of fire is a grenade crossed with a flamethrower.

| Weapon | Scrap | Combination | Strength | Weakness |
|---|---|---|---|---|
| **Lawn-Sprinkler Firebomb** | 300 | Grenade + Flamethrower | Radial fire jets ignite cars they hit. | Weak blast and a slow lob; the burn does the work. |
| **Piñata Bottle Rocket** | 340 | Rocket + Cluster bomb | One hit carpets a wide area with bomblets. | Bomblets scatter randomly; poor against a single fast car. |
| **Shop-Vac Black Hole** | 380 | Grenade + Gravity well | Pulls a whole pack together for one big blast. | Long recharge and a short delay before it pays off. |
| **Satellite-Dish Death Ray** | 360 | Beam rifle + Focus ramp | One continuous, humming beam for as long as the trigger is held; it burns a car after half a second of contact. | Weak until it warms up; switching targets resets it. |
| **Dynamite Crossbow** | 280 | Precision rifle + Sticky bomb | Triple the original blast radius, with damage falling off toward the edge. | A second's fuse, and misses stick to the ground instead. |
| **Bowling-Ball Cannon** | 260 | Shell + Ram | A 20% wider sweep hurls cars aside (34 m/s shove), and the ball hops off each car to the crash of a rack of pins. | Only goes where the ground goes; hills and walls deflect it. |
| **Hubcap Boomerang** | 240 | Sawblade + Return trip | Triple damage on both passes; grows to double size on return. | Useless beyond its turn-around point. |
| **Scatter Mortar** | 360 | Shotgun + Mortar | Nine bomblets spread near and far while inheriting the car's velocity. | No target assist; each charge needs to land near its target. |
| **Tow-Hook Harpoon** | 260 | Sniper + Winch | Holds a car on the cable for 5.5 s: swing round to whip it into a rock, wall or another car and wreck it. | Single target and pulls danger toward you. |
| **Firecracker Blunderbuss** | 300 | Shotgun + Explosive rounds | Splash on every pellet rewards near misses. | Short range, and the pops hurt you up close. |
| **Lawn-Chair Sentry** | 400 | Mine + Auto-turret | Keeps shooting while you drive elsewhere; two at a time. | Stationary; slow to redeploy; light damage per nail. |

## Field weapons (enemy drops)

| Weapon | Role | Strength | Weakness |
|---|---|---|---|
| **Tailpipe Bazooka** | Heavy anti-vehicle | Deletes a car or a cluster of them in one blast. | Slow rockets that fast cars can dodge, and very little ammunition. |
| **Potato Cannon** | Arcing demolition | Lands over cover and wrecks clusters. | Slow shells; limited rounds; hard to land on moving cars. |
| **Propane-Tank Mortar** | Long-range artillery | Hits from beyond the enemy's reach. | Long flight time; useless up close; three rounds a drop. |
| **Lawnmower Gatling** | Suppression | Overwhelming close-range damage while you hold the line. | Wild spread and a hungry belt: ammunition drains fast. |
| **.50 Cal Weed Whacker** | Long-range precision | Enormous single hits from across the map. | Slow to cycle; nearly useless against a swarm on your bumper. |
| **Bottle-Rocket Rack** | Area saturation | Three rockets cover a wide spread. | Few volleys; each rocket alone is modest. |
| **Drainpipe Double-Barrel** | Point-blank shotgun | Two-shot kills on anything touching your bumper. | Slow reload and almost no reach. |
| **Dynamite Go-Kart** | Seeking bomb | Chases targets around cover and corners. | Slow; can be outrun or blocked by walls; three a drop. |

The **Dynamite Go-Kart** replaces the old Lien Mines as a field weapon: it drives off ahead of the car, hunts the nearest hostile around cover and detonates on contact. Hostile crews still lay telegraphed **Tripwire Pipe Bombs** on the road as a special attack; those are enemy-only.

## Mechanics

- **Friendly fire:** rounds, rams, burns and arcs never hurt a vehicle on the attacker's own side (hostile crews on each other, or the player and the escort). Explosions hurt everyone in range, so a crew's rockets and pipe bombs can still catch its own cars.
- **Burn** (Weed-Burner Torch, Sprinkler Firebomb, Death Ray) keeps damaging a car after contact; the ray needs half a second of continuous focus first. Burn ticks deal three times the weapon's stated burn rate (`VehicleAfflictions.BurnTickMultiplier`).
- **Stall** (Jumper-Cable Zapper) cuts a car's engine and guns for about a second; zapper bolts arc to two more hostiles within 14 m.
- **Tow cable** (Tow-Hook Harpoon, `TowLink`): the hook yanks the car in, reels the cable to a short tow and holds it for 5.5 s with its engine stalled. The cable only pulls, and the towed car's tyres skid sideways, so turning hard swings it out wide. A towed car that hits something solid at more than about 6.5 m/s closing speed takes damage, and at about 16 m/s (58 km/h) it is wrecked; a car it is slammed into takes most of the same hit. Bosses take a third. Kills are credited to the driver holding the cable.
- **Spines** (Cactus-Spine Needler) stick; seven within a few seconds rupture together in a large blast.
- **Sticky dynamite** (Dynamite Crossbow) fizzes for a second, then explodes on whatever it hit.
- **Knockback / pull** (Air Cannon, Bowling Ball, Harpoon) moves cars — into hazards, each other, off ledges, into your bumper or, on the tow cable, into the scenery.
- **Deployables** (Lawn-Chair Sentry, Go-Kart) act on their own; two sentries can be out at once.

Implementation: `ContentCatalog.BuildWeapons` (stats and text), `WeaponRules` (garage list, scrap prices, ammo, drop pools, sound donors), `WeaponSystem.Fire` (firing patterns), `ProjectileSystem`/`ShotFx` (homing, piercing, boomerang, on-hit effects, bomblets, hitscan), `FieldOrdnance` (shells, bomblets, roller, go-kart, sentry, vortex), `WeaponEffects` (`VehicleAfflictions` and railgun/lightning visuals), `DeathRayBeam` (the continuous beam and its hum) and `TowLink` (the harpoon cable). The death ray hum, its ignition zap and the bowling pin crash are synthesized in `AudioSynthesis`. New weapons without their own recordings borrow a similar weapon's clips.

Research references: Halo sandbox roles (plasma pistol vehicle disable, needler supercombine, Spartan laser/Skewer one-shot anti-vehicle, concussion rifle knockback, Shock Rifle arcs and vehicle EMP, Heatwave spread, Cindershot bounce, Ravager burn) and Mad Max's improvised vehicle weapons (thundersticks, the thunderpoon harpoon, flamethrowers).
