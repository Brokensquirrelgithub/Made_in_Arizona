# Segmented vehicle health: audit and proposal

> Historical proposal captured on 12 October 2026. Segmented vehicle health has since been implemented; this audit reflects an earlier code and co-op protocol and must be rechecked before using its figures.

**Status: archived design draft. This document itself made no production code or asset changes.**

Every figure below comes from the 36 part assets and 22 vehicle assets in `Assets/MadeInArizona/Resources/Content`, which match the code defaults in `ContentCatalog.cs`. In this document "HP" always means health points; engine output is written as "horsepower".

## Summary

- **The design fits the project with a small change.** Damage runs strictly outside-in and repair strictly inside-out, so the segments form a stack, and the single `Health` number the game already keeps fixes the HP of every segment. `VehicleDamage.Health` and `MaxHealth` stay as they are. The only new state is the size of each segment and one integer: how many layers have failed.
- **Nothing that deals or reads damage has to change.** Weapons, crashes, hazards, AI, pickups, mission progress and the co-op health sync all go through `Health` and keep working.
- **Three things in the current game conflict with the brief** and need a ruling: the existing directional component damage, which already pushes a damaged car far below stock; the flat `healthBonus` on 11 parts; and part drawbacks that can leave a stat below stock once the bonuses offsetting them fail.
- **2 HP/kg raises the health of upgraded cars by 12% to 68% over today.** 1.5 HP/kg keeps most builds close to today's totals. I recommend starting at 1.5.
- **Nine decisions need your approval.** They are listed in section 8.

## 1. Existing system audit

### Vehicles and base stats

- `VehicleDefinition` (`Scripts/Data/VehicleDefinition.cs`) holds `mass`, `horsepower`, `torque`, `maxSpeed`, `grip`, `turnSpeed`, `maxHealth`, suspension and gearing values, `drivetrain` and `differential`.
- There are 22 vehicles. Stock HP runs from 180 (Skitter Sport 1000, 640 kg) to 790 (VINcent, 2,550 kg). It is hand-tuned, between 0.21 and 0.36 HP per kg of vehicle. The full list is in section 3.
- Code defaults are in `ContentCatalog.BuildVehicles`; the assets in `Resources/Content/VehicleDefinition` replace them at load (`ContentCatalog.OverrideResources`, `ContentCatalog.cs:31`). The two agree today.

### Parts and upgrades

- `VehiclePart` (`Scripts/Data/VehiclePart.cs`) holds `category` (a free-text slot name), `mass`, ten multipliers (`hp`, `torque`, `grip`, `cooling`, `maxSpeed`, `turn`, `suspension`, `spring`, `damping`, `finalDrive`), `healthBonus`, `rideHeightBonus`, drivetrain and differential changes, and `compatibleVehicles`.
- There are 36 parts in 27 slot categories. `GarageManager.TogglePart` (`GarageManager.cs:51`) removes any installed part with the same category string, so each slot holds one part. Turbo, blower and nitrous share "Induction"; the cage and the bumpers share "Armor".
- `mass` is the mass added over the stock component, not the component's own mass. Three parts are negative (street tyres -5, used tyres -8, muffler -3) and four are zero (wheels, plugs, alignment, radiator cap).
- Eleven parts carry a flat `healthBonus`: cage +160, bumpers +95, boards +45, lug nuts +30, radiator +25, engine mounts +25, radiator cap +15, seat +10, and three penalties: turbo -25, nitrous -20, ECU -15.
- `coolingMultiplier` is 1 on every part, and the `cooling` stat it feeds is never read by the driving code.

### Stat calculation

- `GarageManager.StatsFor(definition, save)` (`GarageManager.cs:10`) is the only place upgrades are applied. It starts from `VehicleStats.From(definition)` and, for each part that is installed, owned and compatible, adds mass, health and ride height and multiplies the rest. Driver multipliers and the final-drive and ride-height tune follow, then three clamps.
- The result is handed to `VehicleController.Initialize` once, at spawn (`GameManager.SpawnPlayer`, `GameManager.cs:181`). `Body.mass` and the hull size are set from it there and never again. Everything else reads `Stats` live each physics step.
- Nothing recalculates the player's stats during a drive. The only live edits are to AI cars (`MissionManager.cs:157`, `:173`; `WorldExploration.cs:126`).

### Health and damage

- `VehicleDamage` (`Scripts/Vehicles/VehicleDamage.cs`) keeps one `Health` and one `MaxHealth`. `MaxHealth` is `stats.maxHealth` (floor 60) times a multiplier: the Dev Tuning player or enemy health slider, 0.2 for hostiles (`SpawnManager.EnemyHealthMultiplier`), and 2.5 for the escort.
- `ApplyDamage` (`:64`) applies friendly-fire rules, the difficulty scale and the Dev Tuning damage sliders, then subtracts from `Health`. At zero it calls `Die`.
- **Component damage already exists and is directional.** Each hit also wears one or two of five 0-to-1 fractions (`Engine`, `Radiator`, `Transmission`, `Wheels`, `Suspension`), chosen by where the hit landed (`:83`-`:99`). `VehicleController` then cuts performance: drive down to 32% (`VehicleController.cs:399`), top speed to 55% (`:409`), grip to 50% (`:354`), turn rate to 36% (`:371`), spring rate to 45% (`:504`), plus a wobble and a lean. This is the opposite of "never below stock".
- The command rig has five separate hit targets (`BossWeakPoint`, `SpawnManager.cs:43`) that knock a component fraction down by 0.85 when destroyed.
- `ApplyHealthTuning` (`:44`) rescales `MaxHealth` when a slider moves and keeps the health fraction.

### Health bar UI

- The player's bar is `GameUI.DrawHealthBar` in `Scripts/UI/GameUIDamageFeedback.cs:123`, drawn at 240 x 14 from `GameUI.DrawHUD` (`GameUI.cs:419`) with the HP number beside it. It is immediate-mode GUI built from flat rectangles.
- It has a trailing chunk showing damage just taken, a white flash on hit, a green glow on repair, and a red pulse below 30% (`CriticalHealth`) with a "CRITICAL DAMAGE" banner.
- Hostile and escort cars get a plain 74 x 5 bar over the car (`GameUI.cs:506`).
- The garage shows total health as "STRUCTURE" on the dyno sheet (`GameUI.DrawVehicleStats`) and each part's `healthBonus` in the parts panel (`GameUI.DrawParts`).

### Repair and healing

- The only heal in play is the repair pickup: 55 HP from a hostile, 120 from the command rig (`CombatPickup.cs:53`), collected at `:185` through `VehicleController.Repair` to `VehicleDamage.Repair` (`:153`). It adds to `Health`, restores the component fractions, and does nothing to a dead car.
- There is no regeneration and no garage repair.
- The driver's "repairs restore 25% more" perk is not implemented: `DriverDefinition.repairMultiplier` is set to 1.25 for Stallion and never read.

### Destruction and respawn

- `VehicleDamage.Die` (`:175`) turns the car into a wreck, drops pickups from hostiles and, for the player, starts `GameManager.BeginPlayerDeath` (`GameManager.cs:258`), which ends in `FailMission`.
- There is no respawn during a job. Retry and the garage both spawn a fresh car at full health.

### Save, load and equipment

- `SaveData` (`Scripts/Core/SaveSystem.cs:57`) stores `ownedParts`, `installedParts`, `selectedVehicle`, the tune and settings. `installedParts` is one list for all vehicles; incompatible parts are skipped per car.
- **Vehicle health is never saved.** There is no such thing as a saved damaged vehicle today.
- Parts change only from the garage screen (`GameUI.cs`, `BuyOrInstall` to `GarageManager.TogglePart`), which respawns the car at full health (`GameManager.RefreshGarageVehicle`).

### Co-op

- The host simulates every car. It sends each car's `health` and `maximum` ten times a second (`CoopSession.cs:655`); clients apply them with `VehicleDamage.SetNetworkHealth`.
- A guest computes its own `StatsFor` and sends the result to the host (`CoopBalance.cs:54`), which bounds every field (`SafeStats`, `:113`). The host never sees a guest's part list. The protocol number is 3 (`CoopSession.cs:45`).

## 2. Parts and durability table

How to read it:

- **Mass** and **existing modifiers** are current data. **Health category**, **durability** and **HP** are proposals.
- HP is `max(0, mass) x hpPerKg x durability / 100` at 2 HP/kg. Mass is the existing added mass, so a stock component is never counted twice: the chassis baseline covers it and the part adds only its extra material.
- Parts with zero or negative mass give 0 HP. I have not invented masses for them.
- "Failure behaviour" lists what the car loses when the part's category fails. Mass always stays.
- No part is purely cosmetic. One is nonphysical: the alignment is a service.

| Part name | Current category | Mass (kg) | Existing modifiers | Proposed health category | Proposed durability | HP at 2 HP/kg | Failure behaviour | Rationale | Concerns |
|---|---|---|---|---|---|---|---|---|---|
| Sealed desert airbox | Intake | 3 | power +6% | Engine | 20% | 1.2 | None: stays on until the car is destroyed | Moulded plastic airbox; almost no structure. | None. |
| Equal-length headers | Exhaust | 5 | power +9%, torque -3% | Engine | 60% | 6 | None: stays on until the car is destroyed | Thin-walled welded steel tube. | Keeps its torque penalty for good, since engine parts never switch off. |
| Mild tow cam | Camshaft | 2 | power +8%, torque +12% | Engine | 50% | 2 | None: stays on until the car is destroyed | Hardened steel inside the engine; guideline engine value. | None. |
| Wastegated three-pot turbo (shift, scarface only) | Induction | 34 | power +48%, torque +32%, structure -25 | Engine | 25% | 17 | None: stays on until the car is destroyed | Guideline turbo value: thin housings and hot plumbing. | Costs 25 structure today; by mass it adds 17 HP, so its fragility trade-off disappears (decision 3). |
| Roots blower with honest belt (foreclosure, perennial, sidehustle, sunskip only) | Induction | 68 | power +32%, torque +38%, top speed -4% | Engine | 25% | 34 | None: stays on until the car is destroyed | Guideline supercharger value: a belt-driven case on top of the engine. | At 68 kg it is the second-largest engine HP part. |
| Front-mount intercooler | Charge cooling | 18 | power +7%, torque +5% | Engine | 30% | 10.8 | None: stays on until the car is destroyed | Thin aluminium core mounted at the nose. | None. |
| Three-row aluminum radiator | Cooling | 12 | power +5%, structure +25 | Engine | 40% | 9.6 | None: stays on until the car is destroyed | Aluminium core, sturdier as a three-row unit. | Gives +25 structure today; becomes about 10 HP. |
| Wideband ECU and fuel pump | Fuel & ECU | 4 | power +16%, torque +12%, structure -15 | Equipment | 10% | 0.8 | Loses power +16%, torque +12% | The only electronics in the parts list; guideline electronics value. | The only power part that can be switched off. With no other equipment fitted its segment is under 4 HP, so it fails almost as soon as the exterior is gone (decision 4). Its -15 structure penalty disappears. |
| 1.8L salvage-yard engine swap (shift, scarface, ronin, juniper only) | Engine | 96 | power +70%, torque +55%, turn response -6% | Engine | 50% | 96 | None: stays on until the car is destroyed | Iron-block engine; guideline engine value. | 96 HP into the final segment: on a Shift it triples the engine segment. Its turn penalty stays for good. |
| Helical limited-slip carrier | Differential | 8 | grip +7%, limited-slip differential | Drivetrain | 65% | 10.4 | Loses grip +7%, limited-slip differential | Steel gear carrier inside the axle; guideline transmission value. | None. |
| Selectable trail locker (juniper, skitter, vincent only) | Differential | 16 | grip +15%, turn response -12%, locked differential | Drivetrain | 65% | 20.8 | Loses grip +15%, turn response -12%, locked differential | Steel locking carrier inside the axle; guideline transmission value. | Failure also removes its turn penalty. |
| Welded spare differential | Differential | 3 | grip +10%, turn response -18%, locked differential | Drivetrain | 65% | 3.9 | Loses grip +10%, turn response -18%, locked differential | Welded spider gears in the stock housing. | Failure returns the stock differential and removes an 18% turn penalty, so failing is partly a handling gain. |
| 4.88 crawler final drive | Final drive | 6 | top speed -15%, final drive +22% | Drivetrain | 65% | 7.8 | Loses top speed -15%, final drive +22% | Ring and pinion set; guideline transmission value. | Failure restores stock top speed and removes the gearing: a change more than a loss. |
| Selectable four-wheel transfer case (ronin only) | Drivetrain | 145 | grip +12%, top speed -5%, all-wheel drive | Drivetrain | 50% | 145 | Loses grip +12%, top speed -5%, all-wheel drive | Cast transfer case, front axle and shafts. Below the 65% guideline because much of the mass is shafts and a second differential. | 145 HP even at 50% (188 at 65%): the largest HP part after the cage, and Ronin-only. On failure the Ronin returns to rear drive, which changes its road, dirt and sand figures mid-fight. |
| Progressive rally dampers | Suspension | 22 | grip +12%, travel +35%, damping +15%, ride height +0.10 m | Suspension | 85% | 37.4 | Loses grip +12%, travel +35%, damping +15%, ride height +0.10 m | Guideline suspension value: steel damper bodies. | Failure lowers the car 0.10 m and removes 35% of its travel while driving. |
| Long-travel desert package (juniper, sunskip, perennial, skitter, vincent only) | Suspension | 65 | grip +9%, turn response -8%, travel +65%, ride height +0.18 m | Suspension | 85% | 110.5 | Loses grip +9%, turn response -8%, travel +65%, ride height +0.18 m | Guideline suspension value: long steel arms and dampers. | 110 HP. Failure lowers the car 0.18 m while driving. |
| Summer street tires | Tires | -5 | grip +17%, top speed +5%, travel -12% | Suspension | 50% | 0 | Loses grip +17%, top speed +5%, travel -12% | Road tyres: rubber on a light carcass. | Existing mass is -5 kg, so it contributes 0 HP. |
| All-terrain reinforced tires | Tires | 25 | grip +11%, travel +8% | Suspension | 50% | 25 | Loses grip +11%, travel +8% | Reinforced rubber carcass. | None. |
| Mud-terrain beadlock set | Tires | 51 | grip +18%, top speed -9%, travel +12% | Suspension | 50% | 51 | Loses grip +18%, top speed -9%, travel +12% | Heavy rubber with steel beadlock rings. | 51 HP from a set of tyres, twice the all-terrains. |
| Four almost matching used tires | Tires | -8 | grip -12%, damping -7% | Suspension | 50% | 0 | Loses grip -12%, damping -7% | Worn rubber. | A downgrade part (grip -12%) with mass -8 kg, so 0 HP. When the category fails its penalty is removed, so failure improves the car. |
| Triangulated cage and skid plates | Armor | 95 | structure +160 | Armor | 125% | 237.5 | None beyond the lost HP | Guideline roll-cage value; the kit includes skid plates. | 237 HP against +160 today. Shares the Armor slot with the bumpers, so only one of the two can be fitted (decision 7). |
| Salvaged steel bumpers | Armor | 72 | structure +95 | Armor | 110% | 158.4 | None beyond the lost HP | Guideline reinforced-bumper value. | 158 HP against +95 today, for $220: the best HP per dollar in the game. |
| Recovery boards and tool roll | Utility | 24 | grip +3%, structure +45 | Equipment | 50% | 24 | Loses grip +3% | Plastic boards and a roll of steel tools, loosely stowed. | Gives +45 structure today; becomes 24 HP. Mostly serves as padding in front of the ECU. |
| Purge-first nitrous kit | Induction | 20 | power +25%, torque +12%, structure -20 | Engine | 15% | 6 | None: stays on until the car is destroyed | Pressurised bottle, lines and solenoids: little structure. | Could sit in Equipment instead and be knocked out (decision 4). Its -20 structure penalty disappears. |
| Four round wheels | Wheels | 0 | grip +5%, top speed +3% | Suspension | 100% | 0 | Loses grip +5%, top speed +3% | Steel wheels. | Existing mass is 0 kg, so 0 HP. No mass invented. |
| A piston for every cylinder | Internals | 3 | power +10%, torque +8% | Engine | 50% | 3 | None: stays on until the car is destroyed | Forged internals; guideline engine value. | None. |
| Spark plugs that match | Ignition | 0 | power +5%, torque +3% | Engine | 50% | 0 | None: stays on until the car is destroyed | Spark plugs. | Existing mass is 0 kg, so 0 HP. |
| Wheels pointed the same way | Alignment | 0 | grip +3%, turn response +10% | Suspension | none | 0 | Loses grip +3%, turn response +10% | A service, not a component: nonphysical, so it gives no HP. | Its turn and grip gains are lost when Suspension fails. |
| Brake pads that contain pad | Brakes | 4 | turn response +6% | Suspension | 50% | 4 | Loses turn response +6% | Pads and hardware: small wearing parts. | None. |
| A radiator cap that is a radiator cap | Radiator cap | 0 | power +3%, structure +15 | Engine | 50% | 0 | None: stays on until the car is destroyed | A cap. | Existing mass is 0 kg, so 0 HP; the +15 structure it gives today is lost. |
| Engine mounts made of rubber | Engine mounts | 6 | power +3%, structure +25 | Engine | 75% | 9 | None: stays on until the car is destroyed | Steel brackets with rubber bushes. | Gives +25 structure today; becomes 9 HP. |
| Shocks with fluid in them | Dampers | 2 | grip +5%, damping +18% | Suspension | 85% | 3.4 | Loses grip +5%, damping +18% | Guideline suspension value. | None. |
| Muffler attached at both ends | Muffler | -3 | power +3%, top speed +2% | Engine | 40% | 0 | None: stays on until the car is destroyed | Thin sheet steel. | Existing mass is -3 kg, so 0 HP. |
| Timing belt with all its teeth | Timing | 1 | power +3%, torque +6% | Engine | 30% | 0.6 | None: stays on until the car is destroyed | A rubber belt. | None. |
| The full set of lug nuts | Hardware | 1 | grip +3%, structure +30 | Suspension | 100% | 2 | Loses grip +3% | Solid steel fasteners. | Gives +30 structure today for $40; becomes 2 HP, so the cheap early health buy disappears. |
| Driver's seat bolted down | Interior | 3 | turn response +5%, structure +10 | Equipment | 50% | 3 | Loses turn response +5% | Seat frame and bolts. | Gives +10 structure today; becomes 3 HP. |

## 3. Proposed health category structure

The six provisional categories fit the parts list. I recommend keeping them, with these notes.

| Order | Category | HP comes from | Parts | When it reaches zero |
|---|---|---|---|---|
| 1 | Exterior | Chassis only: 50% of the vehicle's stock HP | none | Nothing. It is a buffer. |
| 2 | Armor | Parts only | cage, bumpers | Nothing more. Armor parts have no modifiers besides HP. |
| 3 | Equipment | Parts only | recovery boards, seat, ECU | Loses the boards' grip, the seat's turn response and the ECU's power. |
| 4 | Suspension | 15% of stock HP, plus parts | dampers, long-travel kit, shocks, all tyres, wheels, brakes, alignment, lug nuts | Loses grip, travel, ride height and turn response from those parts. |
| 5 | Drivetrain | 15% of stock HP, plus parts | three differentials, final drive, transfer case | Loses grip, the differential, the gearing and all-wheel drive. |
| 6 | Engine | 20% of stock HP, plus parts | every power part except the ECU | The vehicle is destroyed. Parts never switch off before that. |

- **Stock split.** A stock car has four segments: Exterior 50%, Suspension 15%, Drivetrain 15%, Engine 20%. That needs no new per-vehicle data, keeps every stock total exactly as it is now, and puts the red engine segment close to the 23% mark where cars already catch fire.
- **Armor and Equipment have no baseline**, so their segments appear only when a part is fitted.
- **Suspension is really "running gear".** It holds the tyres, wheels and brakes as well, because the list has no better home for them and losing grip when it fails reads correctly.
- **Equipment is thin.** Only three parts qualify and the list has one electronic part.
- **Hostile vehicles have no parts**, so they always have the stock four segments and nothing to switch off.

Stock segments for every vehicle:

| Vehicle | Mass (kg) | Stock HP | Exterior | Suspension | Drivetrain | Engine |
|---|---|---|---|---|---|---|
| 1989 Shizuki Ronin | 980 | 310 | 155 | 46 | 46 | 62 |
| 1988 Juniper JX | 980 | 310 | 155 | 46 | 46 | 62 |
| Sunskip Trophy 900 | 1900 | 450 | 225 | 68 | 68 | 90 |
| Foreclosure 6.8D | 3000 | 640 | 320 | 96 | 96 | 128 |
| Side Hustle Tradesman | 2250 | 520 | 260 | 78 | 78 | 104 |
| Perennial Half-Ton | 1770 | 540 | 270 | 81 | 81 | 108 |
| Skitter Sport 1000 | 640 | 180 | 90 | 27 | 27 | 36 |
| VINcent, The Uninsurable | 2550 | 790 | 395 | 118 | 118 | 158 |
| 1991 Atlas Shift | 760 | 240 | 120 | 36 | 36 | 48 |
| 2007 Volkswerks Monsoon | 1120 | 290 | 145 | 44 | 44 | 58 |
| 2001 Hanmi Sonora | 1480 | 355 | 178 | 53 | 53 | 71 |
| 2002 Sixstar Dustback | 1510 | 375 | 188 | 56 | 56 | 75 |
| 1990 Asuka Monument | 1490 | 390 | 195 | 58 | 58 | 78 |
| 1990 Coronado Executive | 1750 | 455 | 228 | 68 | 68 | 91 |
| 1991 Senda Heat | 760 | 205 | 102 | 31 | 31 | 41 |
| 1993 Senda Citizen | 1040 | 275 | 138 | 41 | 41 | 55 |
| 2005 Takumi Roam4 | 1450 | 410 | 205 | 62 | 62 | 82 |
| 1985 Takumi AE 867 Runner | 940 | 255 | 128 | 38 | 38 | 51 |
| 1991 Shizuki Espresso | 725 | 200 | 100 | 30 | 30 | 40 |
| 1988 Heartland Copperado | 2050 | 570 | 285 | 86 | 86 | 114 |
| 1970 Dodson Contender | 1720 | 460 | 230 | 69 | 69 | 92 |
| Atlas Scarface | 830 | 300 | 150 | 45 | 45 | 60 |

## 4. Build simulations

All three use the proposed table at 2 HP/kg. Each also has the twelve cheap maintenance parts fitted (wheels, pistons, plugs, alignment, brakes, radiator cap, engine mounts, shocks, muffler, timing belt, lug nuts, seat), which together add about 25 HP. Stat figures include the driver's 3% power bonus.

### Lightweight performance: 1991 Atlas Shift

Parts: Sealed desert airbox, Equal-length headers, Mild tow cam, Wastegated three-pot turbo, Front-mount intercooler, Wideband ECU and fuel pump, Helical limited-slip carrier, Summer street tires.

Mass 846 kg (stock 760). Total 313 HP: stock is 240 and the same build has 280 under the current rules. Exterior is 38% of the total. For comparison the total would be 295 at 1.5 HP/kg, 277 at 1.0 HP/kg.

| Segment | HP | Share of bar | Fails after this much damage |
|---|---|---|---|
| Exterior | 120 | 38% | 120 |
| Equipment | 4 | 1% | 124 |
| Suspension | 45 | 14% | 169 |
| Drivetrain | 46 | 15% | 216 |
| Engine | 98 | 31% | 313 (car destroyed) |

- **Trade-offs.** No armor, so the exterior is the only buffer: 120 damage and the car starts losing things. The ECU goes 4 damage later, taking power from 178 to 153 horsepower. Suspension failure at 169 drops grip from 1.58 to 1.16 and top speed from 113 to 104 km/h. The turbo, cam and headers keep working to the end.
- **What stands out.** Nearly a third of the bar is the red engine segment, because the turbo and intercooler put their HP there.

### Balanced: 1989 Shizuki Ronin

Parts: Sealed desert airbox, Equal-length headers, Three-row aluminum radiator, 1.8L salvage-yard engine swap, Helical limited-slip carrier, Selectable four-wheel transfer case, Progressive rally dampers, All-terrain reinforced tires, Salvaged steel bumpers, Recovery boards and tool roll.

Mass 1409 kg (stock 980). Total 848 HP: stock is 310 and the same build has 555 under the current rules. Exterior is 18% of the total. For comparison the total would be 714 at 1.5 HP/kg, 579 at 1.0 HP/kg.

| Segment | HP | Share of bar | Fails after this much damage |
|---|---|---|---|
| Exterior | 155 | 18% | 155 |
| Armor | 158 | 19% | 313 |
| Equipment | 27 | 3% | 340 |
| Suspension | 118 | 14% | 459 |
| Drivetrain | 202 | 24% | 661 |
| Engine | 187 | 22% | 848 (car destroyed) |

- **Trade-offs.** 429 kg of parts buys 2.7 times the stock health. The first 313 damage costs nothing. Suspension failure at 459 is the big one: grip falls from 1.85 to 1.27, travel from 0.61 to 0.42 m and the car drops 0.10 m. Drivetrain failure at 661 returns it to rear drive at stock grip, but the engine swap keeps 199 horsepower until the end.
- **What stands out.** The exterior is only 18% of the bar. The transfer case alone is 145 HP and the swap another 96.

### Heavy armored: Foreclosure 6.8D

Parts: Sealed desert airbox, Equal-length headers, Mild tow cam, Roots blower with honest belt, Front-mount intercooler, Three-row aluminum radiator, Helical limited-slip carrier, 4.88 crawler final drive, Progressive rally dampers, Mud-terrain beadlock set, Triangulated cage and skid plates, Recovery boards and tool roll.

Mass 3331 kg (stock 3000). Total 1097 HP: stock is 640 and the same build has 950 under the current rules. Exterior is 29% of the total. For comparison the total would be 983 at 1.5 HP/kg, 868 at 1.0 HP/kg.

| Segment | HP | Share of bar | Fails after this much damage |
|---|---|---|---|
| Exterior | 320 | 29% | 320 |
| Armor | 238 | 22% | 558 |
| Equipment | 27 | 2% | 584 |
| Suspension | 194 | 18% | 778 |
| Drivetrain | 114 | 10% | 892 |
| Engine | 204 | 19% | 1097 (car destroyed) |

- **Trade-offs.** The truck takes 558 damage before anything functional is at risk. It gains less from parts than the Ronin does, 1.7 times stock, because its stock HP is already large.
- **What stands out.** Its top speed rises as it is damaged: 77 km/h whole, 82 once the mud tyres fail, 97 once the crawler gears fail. Failed parts lose their drawbacks along with their benefits.

## 5. Balancing concerns and possible exploits

### Is 2 HP/kg right?

| Build | Stock | Today | 2.0 HP/kg | 1.5 HP/kg | 1.0 HP/kg |
|---|---|---|---|---|---|
| Light Shift | 240 | 280 | 313 | 295 | 277 |
| Balanced Ronin | 310 | 555 | 848 | 714 | 579 |
| Heavy Foreclosure | 640 | 950 | 1,097 | 983 | 868 |
| Ronin with every heavy part | 310 | 585 | 980 | 813 | 645 |

- At 2.0, upgraded cars have 12% to 68% more health than today while incoming damage is unchanged, so the game gets easier for built cars.
- 1.0 roughly matches today. 1.5 keeps the light and heavy builds within 5% of today and holds the Ronin builds to 30% to 40% more.
- **I recommend 1.5 as the starting value**, tuned from there with a Dev Tuning slider.

### Concerns

- **Light cars gain the most.** Part HP is absolute while the chassis baseline scales with the car, so the 980 kg Ronin nearly triples its health and the 3,000 kg truck does not double. The Ronin-only transfer case (145 kg) and the engine swap (96 kg) drive this.
- **Strong parts.** Bumpers give 158 HP for $220. The cage gives 237. The transfer case gives 145 at 50% durability and 188 at the 65% guideline. Mud tyres give 51.
- **Weak parts.** The cheap maintenance parts lose their health role: lug nuts go from +30 to 2, radiator cap from +15 to 0, engine mounts from +25 to 9, seat from +10 to 3. The radiator goes from +25 to about 10.
- **Fragility trade-offs disappear.** Turbo, ECU and nitrous cost structure today and add a little under the mass rule.
- **The ECU is exposed.** With no boards fitted, the Equipment segment is under 4 HP.
- **The engine segment can get large.** It is always red, and big engine parts make it 20% to 31% of the bar.
- **Suspension failure is abrupt.** Grip can fall by a third in one frame, with a ride-height drop. I recommend a toast and no blending, to stay simple.
- **A stat can end up below stock.** Engine parts never switch off, so their drawbacks stay. On the balanced Ronin the swap's -6% turn response is hidden by suspension and seat bonuses; once those fail, turn response is 86 against a stock 92. The fix is one rule in section 6.
- **Failing can help.** A welded differential, crawler gears, mud tyres and used tyres all carry drawbacks that vanish when their category fails.
- **Repair pickups shrink in relative terms.** 55 HP is 10% of today's 555 HP Ronin and 6.5% of the 848 HP one.

### Exploits checked

- **Swapping parts to heal.** Not possible: parts change only in the garage, where the car is rebuilt at full health, and health is never saved.
- **Toggling a category on and off with small repairs.** Prevented by the 25% rule.
- **Negative-mass parts subtracting HP.** Prevented by the zero floor.
- **A one-kilogram part creating a segment.** Harmless: the segment is as small as the part.
- **A modified co-op client claiming huge segments.** The host must bound the new fields the way `SafeStats` bounds the rest.

## 6. Recommended technical architecture

### The model

- The segments are a stack, engine at the bottom. Segment HP is derived: `clamp(Health - sum of the segments inside it, 0, its maximum)`.
- Outer layers fail first and inner layers are repaired first, so the failed categories are always an unbroken run from the outside. One integer, `FailedDepth` (0 to 4 across Armor, Equipment, Suspension, Drivetrain), describes every possible state.
- A car therefore has at most five stat profiles. They are computed once at spawn and switching is a reference swap, so a modifier cannot be applied twice or lost.

### Data changes

- `Scripts/Data/VehiclePart.cs`: add `enum HealthCategory { Exterior, Armor, Equipment, Suspension, Drivetrain, Engine }`, and on `VehiclePart` add `healthCategory` and `partDurability` (percent, 0 for no HP).
- Set both in `ContentCatalog.BuildParts` and write them into the 36 assets, because the assets replace the code objects at load. Add a smoke check that asset and code agree.
- New `Scripts/Vehicles/HealthRules.cs`, a static class in the style of `DrivetrainTraits`: the three baseline shares, the 25% reactivation threshold, and `PartHealth(part)`.
- `hpPerKg` becomes `DevTuning.partHpPerKg` with a slider on the COMBAT tab, which is already host-controlled world balance.
- `Scripts/Vehicles/VehicleStats.cs`: add `float[] healthShares` (six values summing to 1). `maxHealth` stays as the total. Shares rather than absolute HP, because existing code scales `maxHealth` after the stats are built (`SpawnManager.cs:20`, `WorldExploration.cs:126`, the escort multiplier, the health sliders).

### Stats

- `GarageManager.StatsFor(definition, save, failedDepth = 0)`: the same loop. Mass and segment HP are always added. The other modifiers are skipped for a part whose category has failed. Engine parts are never skipped.
- `GarageManager.DamageProfiles(definition, save)` returns the five profiles.
- **Stock floor.** For horsepower, torque, grip, turn response, top speed, travel and damping, a damaged profile uses `max(value, min(stock value, full-health value))`. Damage can then never take a stat below stock, or below what the player's own build had when whole.
- Existing clamps and the order of multiplication are unchanged.

### Health state

- `VehicleDamage.Initialize(owner, health, shares)` stores the segment maxima. Add `SegmentMax`, `SegmentHealth`, `FailedDepth` and an event for a category failing or recovering.
- After any change to `Health`, one small function updates `FailedDepth`: a category fails when its HP is 0 and recovers when its HP reaches 25% of its maximum.
- `ApplyDamage`, `Repair`, `Die` and `SetNetworkHealth` keep their signatures. Repair already refuses dead cars.
- Smoke starts when the exterior is gone and fire when damage reaches the engine, replacing the fixed 52% and 23% marks (`VehicleDamage.cs:167`).

### Applying failures

- `VehicleController.Initialize` takes the optional profiles. When `FailedDepth` changes it sets `Stats = profiles[depth]`. Mass and hull never change, and no other system caches the player's stats.
- The player gets a toast through the existing `GameManager.Notify`: "SUSPENSION OFFLINE" and "SUSPENSION BACK ONLINE". Categories with nothing to lose stay silent.

### Health bar

- Same place and size. Engine on the left, exterior on the right, so the bar still empties from the right.
- Each segment is as wide as its share of the maximum, with a one-pixel dark divider.
- Colour by the segment's own fill: green above 50%, yellow above 25%, red above 0, dark grey when empty. The engine is always red.
- Keep the damage trail, the hit flash and the repair glow. The critical pulse and banner start when damage reaches the engine.
- A row of small labels under the bar (ENGINE, DRIVE, SUSP, GEAR, ARMOR, BODY) for segments at least 30 pixels wide. A failed category's label turns grey. No icons.
- Hostile bars stay plain: there can be dozens on screen and they have nothing to switch off.
- Garage: the dyno sheet shows the segment breakdown in place of the single STRUCTURE number, and the parts panel shows each part's category, durability and HP.

### Recalculation policy

- **Rule: a layout change keeps the damage front.** The category the damage has reached, and how full it is, stay the same; everything inside it stays full and everything outside stays empty. No change can heal, duplicate HP or be exploited by swapping.
- Installing, removing or replacing a part, and changing a durability value, all follow that rule. Today they only happen at full health, so the result is simply the new full bar.
- Slider changes are a uniform scale, which the existing fraction-preserving code already handles.
- Loading a damaged vehicle does not arise, since health is not saved. If it ever is, save `Health` and `FailedDepth` and clamp on load.
- Repairing a car with failed categories needs no special case: `Repair` adds to `Health` and the 25% rule does the rest.

### Co-op

- `VehiclePacket` gains `failedDepth`. `LoadoutPacket` gains the guest's four damaged profiles; `SafeStats` bounds them and the shares. The protocol number goes from 3 to 4.

### Component damage

- Remove the five component fractions, the hit-position rules and the seven places `VehicleController` reads them (decision 5).
- The command rig's weak points keep their effect as a direct, boss-only stat cut in `BossWeakPoint`.

### Tests

Automated, as a new `-miaHealthTest` suite in `SmokeTestRunner`:

- Every stock vehicle's total equals its current `maxHealth`, and its exterior is 50%.
- Part HP matches the formula for all 36 parts, and negative mass gives 0.
- Damage empties segments in order and overflow carries across.
- Each category fails at exactly zero, stays failed below 25% and recovers at 25%.
- A category that never failed keeps working below 25%.
- The engine never fails early; the car dies when it reaches zero and a repair does not revive it.
- For every vehicle and every depth, no floored stat is below `min(stock, full-health)`.
- Engine-part modifiers are identical at every depth, and mass is identical at every depth.
- Fail and recover ten times: the stats return to the same values.
- A health slider change keeps every segment's fill and `FailedDepth`.
- Existing checks that still must pass: enemy one-fifth health, escort 2.5 times, part modifies stats (`SmokeTestRunner.cs:373`, `:1782`, `:1809`).

Manual:

- Drive each of the three builds into sustained fire and watch the bar, the toasts and the handling change at each failure.
- Collect repairs with a failed suspension and confirm it returns at a quarter.
- Check the bar at UI scales 0.75 and 1.5 and with a 4 HP segment.
- Host and join a co-op session and confirm the guest's bar and failures match the host.

## 7. Ordered implementation plan

1. **Data.** Add the enum, the two part fields, `HealthRules` and the slider. Fill in the code table and the 36 assets. Add the agreement check.
2. **Stats.** Add `healthShares`, the `failedDepth` argument, `DamageProfiles` and the stock floor. Add the stock-total and never-below-stock tests.
3. **Health state.** Segment maxima, derived segment HP, `FailedDepth`, the event. Add the order, failure and recovery tests.
4. **Component damage.** Remove it and rework the boss weak points, if approved.
5. **Applying failures.** Profile swap in `VehicleController`, toasts, smoke and fire thresholds.
6. **HUD and garage.** Segmented bar, labels, dyno sheet, parts panel, rewritten part descriptions.
7. **Co-op.** Packet fields, bounds, protocol number.
8. **Balance and docs.** Run the three builds by hand, settle `hpPerKg` and the durabilities, then update the README and the notes.

Each step builds and passes the suites on its own. Steps 1 to 3 change no gameplay until step 5 switches the profiles on.

## 8. Decisions needing approval

1. **HP per kg.** 2.0 as specified, or 1.5 as I recommend. Both are slider-tunable afterwards.
2. **Stock split.** Exterior 50%, Suspension 15%, Drivetrain 15%, Engine 20% (recommended), or put the whole inner half into the engine so a stock car has two segments, half of them red.
3. **Retire `healthBonus`.** Recommended: part HP comes only from mass and durability. That removes the structure penalty on turbo, ECU and nitrous and nearly all the health from the cheap maintenance parts. The alternative is to keep the flat bonus for parts under 10 kg.
4. **Where the ECU and nitrous go.** Recommended: ECU in Equipment, where it can be knocked out, and nitrous in Engine. Putting both in Equipment gives that layer real stakes; putting both in Engine makes every power part immune.
5. **Remove the existing component damage.** Recommended for all vehicles, with the command rig's weak points kept as a boss-only effect. The alternative is to keep it for hostile cars only.
6. **Failed parts and their drawbacks.** Recommended: a failed part contributes mass and nothing else, which is simplest but lets some failures help. The alternative keeps the drawbacks and removes only the gains.
7. **The Armor slot.** The cage and the bumpers cannot be fitted together today. Keep that, or give the bumpers their own slot so both feed the Armor segment.
8. **Transfer case durability.** 50% (145 HP, recommended) or the 65% guideline (188 HP).
9. **Stallion's repair perk.** It is advertised and not implemented. Wire the 25% bonus into `Repair` as part of this work, or leave it.
