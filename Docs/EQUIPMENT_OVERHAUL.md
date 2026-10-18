# Garage equipment overhaul

The garage has **Dispatch**, **Motor Pool**, **Parts & Tuning**, **Weapons**, and **Suzuki** tabs. Dispatch, Motor Pool, and Suzuki retain their original layouts. The equipment controls live on Parts & Tuning. Each car keeps its own six mechanical parts, four interchangeable accessories, one garage weapon, and paint choice. A missing mechanical part uses the car's factory specification. The combat field weapon pickup remains a temporary mission pickup.

Parts & Tuning shows a car-specific service-manual plan view based on the vehicle's wheelbase, track width, and chassis. The vehicle runs front to rear across a light technical plate with cutaway mechanical details, panel seams, dimension marks, and six numbered callouts. Their numbers match the six system cards around the plate; four accessory cards sit below. Every card shows a small illustrated icon; an unfilled mechanical card is labeled with its stock part. The inventory list and selected part show the same icons.

## Reused and reclassified equipment

All 37 original `VehiclePart` assets and their IDs, costs, unlocks, modifiers, durability, and ownership remain. The old categories map as follows:

| New slot | Existing part IDs |
|---|---|
| Engine | `intake`, `headers`, `cam`, `turbo`, `blower`, `intercooler`, `radiator`, `ecu`, `swap`, `nitrous`, `pistons`, `plugs`, `radcap`, `mounts`, `muffler`, `timing`, `revlimiter` |
| Transmission | `crawler` |
| Drivetrain | `lsd`, `locker`, `welded`, `awd` |
| Tires | `street`, `allterrain`, `mud`, `used`, `wheels`, `lugs` |
| Suspension | `rally`, `longtravel`, `alignment`, `brakes`, `shocks` |
| Body | `cage`, `bumpers` |
| Accessories | `boards`, `seat` |

The ECU is now an Engine part, so its power benefit remains active until engine destruction. Legacy car restrictions remain enforced. Parts with several legacy model IDs are classified under their matching chassis tags while retaining those original fit restrictions.

## Added equipment

New definitions are `VehiclePart` ScriptableObjects in `Assets/MadeInArizona/Resources/Content/VehiclePart`. Stats are calculated by `GarageManager.StatsFor` and applied by `VehicleController`.

**Universal (16):** `close_ratio`, `long_ratio`, `quick_shift`, `reinforced_gearbox`, `street_diff`, `drift_diff`, `lightweight_body`, `ram_frame`, `light_bar`, `tire_spikes`, `drift_n2o`, `magnetic_collector`, `repair_foam`, `reinforced_wiring`, `collision_padding`, `cooling_fans`.

**Chassis (5):** `compact_coilovers` (compact), `truck_armor` (truck), `trail_diff` (trail), `roadster_aero` (roadster), `truck_beadlocks` (truck).

**Car-specific (22):** `ronin_crawl`, `juniper_trail`, `sunskip_desert`, `foreclosure_tow`, `sidehustle_cage`, `perennial_leaf`, `skitter_sprint`, `vincent_plating`, `shift_feather`, `monsoon_turbo`, `sonora_comfort`, `dustback_gravel`, `monument_cruise`, `executive_armor`, `heat_midship`, `citizen_turn`, `roam4_trail`, `ae867_slide`, `espresso_spool`, `copperado_ram`, `contender_torque`, `scarface_scramble`.

Every signature part has a tradeoff. Only the Ronin's transfer conversion explicitly changes drivetrain layout. Chassis tags describe body size/use and are separate from FWD/RWD/AWD.

## Compatibility counts

Order: **Engine / Transmission / Drivetrain / Tires / Suspension / Body**. Factory specifications are excluded. Every car also has 10 accessory choices and 15 garage weapon choices.

| Vehicle ID | Compatible mechanical parts |
|---|---|
| ronin | 15 / 5 / 7 / 6 / 4 / 4 |
| juniper | 15 / 5 / 6 / 6 / 6 / 4 |
| sunskip | 15 / 6 / 4 / 7 / 5 / 5 |
| foreclosure | 16 / 5 / 4 / 7 / 4 / 5 |
| sidehustle | 15 / 5 / 4 / 6 / 4 / 5 |
| perennial | 15 / 5 / 4 / 7 / 6 / 5 |
| skitter | 14 / 6 / 5 / 6 / 5 / 4 |
| vincent | 14 / 5 / 5 / 6 / 5 / 5 |
| shift | 16 / 5 / 4 / 6 / 5 / 5 |
| monsoon | 15 / 5 / 4 / 6 / 5 / 4 |
| sonora | 14 / 5 / 4 / 6 / 5 / 4 |
| dustback | 14 / 5 / 4 / 7 / 4 / 4 |
| monument | 14 / 6 / 4 / 6 / 4 / 4 |
| executive | 14 / 5 / 4 / 6 / 4 / 5 |
| heat | 15 / 5 / 4 / 6 / 4 / 5 |
| citizen | 14 / 5 / 4 / 6 / 6 / 4 |
| roam4 | 14 / 5 / 5 / 6 / 4 / 4 |
| ae867 | 14 / 5 / 5 / 6 / 4 / 4 |
| espresso | 15 / 5 / 4 / 6 / 4 / 5 |
| copperado | 14 / 5 / 4 / 7 / 4 / 6 |
| contender | 15 / 5 / 4 / 6 / 4 / 4 |
| scarface | 16 / 5 / 4 / 7 / 5 / 4 |

## Controls and save migration

In the garage, LB/RB changes tabs. On Parts & Tuning, D-pad or left stick moves between diagram cards or part rows. A opens a card and equips a selected part; B closes the list; X restores the stock part; Y toggles detailed stats; Start opens settings. Dispatch selects missions and Motor Pool selects cars. Keyboard users can use Tab, arrow keys, Enter, Escape, Backspace, and Y. Mouse users can click a card and part, or drag a part from its list onto a compatible card. The preview shows current and projected effective stats. Incompatible parts can be revealed, but cannot be installed. Movement and weapon input remain inactive in the garage.

Version 1 saves are still read. At first load, each legacy globally installed part is applied to every compatible vehicle loadout. When several old parts target one new slot, the last installed choice remains fitted; all displaced parts remain owned and can be re-equipped. Existing money, salvage, unlocks, and owned weapons are preserved. Each car subsequently remembers its own installed parts, accessories, garage weapon, and paint. Invalid, unowned, or incompatible saved installations are cleared on load without deleting ownership.

## Runtime effects and testing

The accessory light bar and tire spikes use visible procedural mounts, so they work without new art. The bar illuminates at night; spikes add cooldown-limited wheel-area collision damage. The drift accessory fills the existing N2O tank only during moving, grounded sideways slides and marks the HUD with `+DRIFT`. The magnet increases supply pickup reach. Repair foam gives one critical non-engine repair per vehicle life. Wiring protects the equipment layer, padding reduces collision damage, and fans reduce over-rev heat buildup via the existing cooling stat.

`BuildGame.Validate` calls `EquipmentValidator.Validate`, which reports each vehicle's counts and checks compatibility, migration, baseline restoration, and stat accumulation. Native smoke switches: `-miaSmokeTest -miaEquipmentTest` for controller navigation, mouse drag, and accessory shutdown; `-miaSmokeTest -miaHealthTest` for segmented damage. Both use isolated test saves.

The equipment suite uses synthetic Input System gamepad and mouse events; a physical controller has not been checked by hand.

The mounts are procedural placeholders. A physical controller and visual art pass are still recommended before a public release.
