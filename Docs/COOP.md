# Host PC co-op playtest

Co-op supports one host and up to three guests. The host PC runs vehicle physics, enemies, damage, pickups, world exploration, missions, and saving. Guests send driving and combat inputs and render the host's world state. The session uses Unity Relay and an invite code, so players do not need to forward a router port.

## Unity Services setup

This repository is linked to the Made in Arizona Unity Cloud project (`77d063f5-15d6-4012-b5fa-7025d9caeb87`). Sign into a Unity account with access to that project when building in Unity 6000.5.5f1. Enable Relay / listen-server networking for the project in the Unity Dashboard. Build all PCs from the linked project so they share the same Unity project ID and game code. See [Unity's project-link instructions](https://docs.unity.com/en-us/services/getting-started) and [Relay with Netcode for GameObjects](https://docs.unity.com/en-us/mps-sdk/tutorials/relay-and-ngo).

The project pins `com.unity.netcode.gameobjects` 2.13.3 and `com.unity.services.multiplayer` 2.3.2, compatible with this Unity editor. Services sign in anonymously when players choose Host or Join. The menu displays a service or transport error if connection setup fails.

## Play

1. On the host PC, open **Co-op** on the main menu and choose **Host Co-op**. Share the displayed join code.
2. On each guest PC, enter that code on the main menu and choose **Join Host**. Wait for the host to launch a campaign or combat trial.
3. The host chooses the map and mission and starts it. Everyone appears in the same generated world. Each guest drives the car and garage weapon selected in their own save, with their installed vehicle parts. The host's campaign save determines world progress.
4. The host controls retry, regeneration, and returning to the garage. Guests can view the map and pause their own controls; only the host may craft upgrades or change the world. The host can copy the code from the main menu and sees it while driving.

## Co-op playtest tuning

Open **Pause → Dev Tuning / Mouse** while playing. **Combat** changes world balance, including enemy health, and only the host can edit it. **Impact damage to props** on the Driving tab is also host controlled. **Camera**, **Light & Color**, **Reflections**, and **Dirt & Sky** affect local presentation and remain editable on each PC. Personal graphics, audio output, and input preferences also remain local.

**Driving**, **Drift**, and **Engine** are saved as profiles for the current car. **Slot 1** tunes the equipped garage weapon; **Slot 2** tunes an equipped field weapon. Weapon tabs expose damage per hit, shots per second, projectile speed, and blast radius when that weapon has one. Car and weapon changes apply during play and sync to everyone using the same item.

If players share a car or weapon, one player owns its tuning sliders for the session. The host owns any item they use. Otherwise, the first guest who joined among players using that item owns it. Everyone else sees the shared item's controls locked and receives the owner's values. A guest using a unique car or weapon keeps control of that item's tuning.

Co-op tuning is a draft until the host chooses **Leave Co-op**. The host then chooses **Save Defaults** or **Discard** once for all players. Save makes the current tuning each player's local default; guests adopt the host's world-balance values, and players sharing a car or weapon save its owner's winning profile. Discard restores every player's pre-session tuning. Campaign progress can still save during the session. A guest who leaves early, or a session that ends unexpectedly, discards that player's draft.

The code works while the host is connected to Relay. Closing the host ends the session. All players should use the same build; the world protocol rejects incompatible versions. Relay availability and usage are subject to the linked Unity project's service settings.

## Implementation and playtest status

The host sends car, pickup, mission, projectile, explosion, prop-break, and discovered-map-pin updates. Guest inputs are clamped on the host; guest physics, damage, patrol spawning, and exploration do not control outcomes or write campaign progress. Objective proximity, interaction, enemy targeting, supplies, and discoveries include all live player cars. Late joiners generate the host's world config and receive the current pin state and broken-prop history after loading. A player whose car is destroyed watches a surviving teammate until the host ends or restarts the mission.

Unity script compilation and 44 native player smoke checks pass in 6000.5.5f1. Live Relay sessions connected a host and three guests with distinct and duplicate car and weapon loadouts; all three guest cars drove from remote inputs. Three-player sessions checked both Save Defaults and Discard, including shared-item ownership, unique guest tuning, and host-to-guest decision delivery. A separate session let a guest join a generated campaign after it started and verified driving and a previously discovered map pin. These checks ran as separate player processes on one PC. Multi-PC playtesting is still needed for latency, reconnect/disconnect, and visual fidelity. Projectile replication recreates ordinary round visuals; unusual field ordnance and transient weather effects may still look different on guests. Local camera prediction is not implemented in this playtest pass.
