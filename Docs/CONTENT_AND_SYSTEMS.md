# Campaign and systems implementation

The opening mission, **First Day, Final Notice**, is the handcrafted vertical slice and Stallion's first shift at Johnny's repair shop: a stolen loaner leads into an armed junkyard, the player clears three opponents, explicitly collects a finance ledger using E / gamepad South, then escapes to 117° Auto Care. Its completion dialogue reveals the $173 invoice. The chase route, encounters, evidence interaction and extraction are separate state-machine stages. The character and dialogue rules are defined in [STORY_BIBLE.md](STORY_BIBLE.md).

The following fourteen jobs provide an original, playable campaign framework. They have individual briefings, opening/midpoint/completion dialogue, rewards, optional criteria and enemy budgets. They reuse the modular desert map and mode implementations; they are not fourteen separately handcrafted production environments or cinematics. Mission 6 forces the tiny hatchback through mountain checkpoints. Missions 8/14 hold a broadcast perimeter against escalating waves. Missions 4/9 escort a damageable vehicle that waits when the player is over 32 m away. Missions 11/15 use command vehicles with external weakpoints. Other modes cover timed checkpoints/escape, component recovery, data-cache rescue, repossession combat and demolition. Recovery and rescue currently concern vehicles/data, not animated human passengers. Weather and regional geometry are WorldBuilder's presentation layer.

Hostile vehicles belong to six factions: Sunsprawl Security, Courtesy Compliance, Road Scavengers, Open House Realty, Snowbird Convoy, and Car Otaku Club. Existing vehicle roles combine with faction loadouts and steering. Sunsprawl keeps range; Courtesy Compliance deploys mines and mortars; scavengers rush; real estate agents circle and zone; snowbirds hold roads in tougher, slower vehicles; and car otaku make fast flanking passes. Each faction has its own paint, HUD color, and weapon drop pool; the three newer crews also have roof or wing accessories. Campaign jobs choose factions to fit their setting; open-world hideouts have a stable faction and roaming patrols vary. The two combat-trial waves introduce all six factions.

Content starts in `Assets/MadeInArizona/Scripts/Data`. ScriptableObject defaults are original authored fallback data. Resources/Content assets with matching ids override fallback entries, allowing editor-exported assets to be customized without editing the catalog. Extend the catalog array to register additional ids.

## Garage

Eight mechanically distinct original vehicles, the fixed protagonist Stallion and 24 individual mechanical parts are defined. First two vehicles are owned and starting cash is $1100. Other vehicles require both campaign clearance and purchase. Parts have price, mass, nitro-recovery implications, actual performance modifiers and optional model compatibility. One part per category can be installed. Switching vehicles preserves the inventory; incompatible components are inactive. Stats are rebuilt from a clean vehicle definition to avoid stacking drift.

`GarageManager.StatsFor` includes driver power/traction, installed compatible parts and gearing/ride-height tuning. Parts can modify drivetrain and differential type, power, torque, final drive, speed, grip, nitro recovery, weight, health, suspension travel, springs and damping. Global tire grip and compliance are the current tire abstraction; there is not a complete per-tire/per-surface simulation. Nitro recovery, local damage and drivetrain behavior are interpreted by VehicleController. Repair passives are applied at the controller's repair integration point.

- `Compatible(part, vehicle)` reports fitment.
- `LastMessage` provides purchase/install failure or success text.
- `Tune(finalDrive, rideHeight, save)` persists bounded final-drive multiplier .85–1.2 and height delta -.08–.15 m.
- `SelectVehicle` purchases an unlocked unowned car when funds suffice.

## Mission and progression API

In addition to the contract, MissionManager exposes `Definition`, `OptionalComplete`, `LastAward`, `Debrief`, `AwardedMoney`, `AwardedSalvage`, `EscortHealth` (0–1), and `HoldProgress` (seconds). It owns all mission payouts. GameManager.CompleteMission must only transition state and save. First completions pay the job reward; replays pay 30% plus optional and bounded score bonuses. Best scores are retained. Completion unlocks the next mission and corresponding purchases. Salvage and collectibles also persist when found, including on failed missions.

Gold rings mark main objectives; cyan rings mark three optional discoveries. `RegisterPickup("plate:<id>")` / `RegisterPickup("workorder:<id>")` add persistent garage-archive entries. Other generic pickups award salvage without advancing primary objectives. `RegisterPickup("wonton")` awards the food distinction and score. World destruction owns detection of the food truck. Suzuki is never a combat target.

The pickup archive and distinctions are data backed. Dog cosmetics are stored as `dogCosmetic` 0–3; the garage UI/world render the selected accessory. `achievements` contains `insurance`, `airborne`, `wonton`, `dog_director` and `paid_full` as earned. Additional awards can be added without a schema change.

## Save behavior

SaveData uses Unity's platform-appropriate `Application.persistentDataPath`. A versioned JSON envelope contains a SHA-256 checksum of its JSON payload. Writing flushes a temporary file to disk then atomically replaces the main save and retains a backup where supported. A backup-preserving move fallback supports platforms without File.Replace. Load checks main, backup, then recoverable temp data; corrupt files do not prevent launch. Settings, lists, indices, cash and tuning are bounded and normalized. There is no cloud synchronization or encryption.

`SaveSystem.LastError` exposes I/O failures and `RecoveredBackup` reports recovery. Audio sliders and UI shake values are clamped 0–1. This is a local single-player save format, not an anti-cheat system.

## Original audio

AudioSynthesis creates deterministic original PCM assets: layered engine harmonics, compressor whine, wind/road noise, gun reports with mechanical action and tails, three sizes of pressure/noise/debris explosions, radio squelch and interface chirps. MusicManager generates and crossfades two original loop compositions: **Invoice at 174 BPM** (174 BPM rave/happy-hardcore instrumentation) and **Last Bay on the Left** (96 BPM garage groove). No third-party songs or samples are embedded.

AudioManager owns 28 reusable spatial effect sources plus engine, turbo, road and wind loops. RPM/load, boost, gear changes, drifting, surfaces and death drive its mix. Larger blasts duck engine/music before recovering. Master, music, engines, weapons, dialogue-radio and environment sliders route independently. Dialogue is text with radio sounds; there is no recorded voice acting. The current synthesized engine is a shared harmonic model with pitch/load variation, not an individually recorded sound set for each vehicle. No audio middleware is required.
