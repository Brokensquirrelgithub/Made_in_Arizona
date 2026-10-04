# World-generation configuration

`WorldConfigStore.Load()` creates the editable file at `Application.persistentDataPath/world-generation.json`. In the automated smoke run it uses the isolated `Application.temporaryCachePath/IntegrationTestWorld` directory instead. The generated file starts with this schema:

```json
{
  "seed": 117,
  "townCount": 7,
  "poiCount": 18,
  "size": 1600.0,
  "terrainHeight": 65.0,
  "vegetation": 1.0,
  "riverWidth": 10.0,
  "trailDensity": 1.0,
  "biomeThresholds": {
    "lowland": 0.28,
    "scrub": 0.52,
    "highland": 0.76
  },
  "pins": []
}
```

`seed` is saved as part of the file, so a chosen seed reproduces the same generated layout. `size` is measured in world units and generates from 1600 through 4800 (terrain chunks scale with it: 8×8 at 1.6 km up to 16×16 at 4.8 km). Older files asking for 800–1599 still load and generate at 1600. `townCount` accepts 7–10 (older files asking for fewer towns are raised to 7) and `poiCount` accepts 4–40. `terrainHeight` accepts 0–150, `vegetation` 0–4 (density multiplier), `riverWidth` 0–30 metres, and `trailDensity` 0–3. Biome threshold values must be ordered from 0 through 1.

**Biomes** follow Arizona's real geography rather than north-south bands (see `GeneratedWorldBiomes.cs`): the Mogollon Rim runs diagonally from the west-north-west to the east-centre with a belt of ponderosa forest; north of it lies red rock in the east (Monument Valley country: mesas, buttes, spires and arches) and canyon and plateau country with pinyon-juniper in the west; south of it is the Sonoran desert (saguaros, organ pipes, barrels, prickly pear, mesquite), with a red-rock pocket just below the Rim (Sedona). Borders meander with the seed. Of the thresholds, only `scrub` is still read: it moves the Rim north (higher) or south (lower) from its default of 0.52. Terrain is craggy on the plateau, Rim and red rock and smooth on the Sonoran floor, and the snow-capped San Francisco Peaks rise north of the Rim (gentle enough to drive). Landform and cover counts scale with map area (up to 4× the 1.6 km counts); every ridge long enough has car-sized passes. `lowland` and `highland` are kept so existing files stay valid but no longer have any effect.

`pins` is optional and contains hand-authored map markers. Every pin needs an `id`; the other available fields are `label`, `kind`, `position` (`x`, `y`, and `z`), `requiredTier`, and `discovered`.

The running game calls `WorldConfigStore.TryReload(out config)` automatically. **A valid edit regenerates the active world and restarts the current sortie.** Saved upgrades, completed campaign jobs, collected salvage and discoveries remain; temporary enemies and destruction are rebuilt. It checks the file at most once per second and returns `true` only after a changed file has parsed and validated. Save a complete JSON document, preferably with an editor that writes atomically. A temporary, malformed, or out-of-range edit records `WorldConfigStore.LastError` and leaves the currently generated world and last valid configuration unchanged. `WorldConfigStore.Save(config)` validates before writing through a temporary file.

The main menu displays the exact live file path. On macOS the normal location is `~/Library/Application Support/117 Degree Games/Made in Arizona/world-generation.json`; on Windows it is `%USERPROFILE%/AppData/LocalLow/117 Degree Games/Made in Arizona/world-generation.json`. The repository example in `WorldGeneration/world-generation.example.json` is a template, not the file watched by the game.

Authored pins are placed at world-space `x`/`z` coordinates and snapped to terrain height. Pins outside the Arizona outline are skipped. Use unique IDs; `kind` can be `hideout`, `salvage-tech`, `salvage-alloy`, `salvage-fuel`, or `landmark`. For example:

```json
{"id":"old-relay","label":"Abandoned relay","kind":"salvage-tech","position":{"x":120,"y":0,"z":180},"requiredTier":1,"discovered":false}
```

The generator uses bounded terrain chunks, seeded biome noise, a carved river, graded road corridors and predefined town geometry. Broad grades now use 65% of their original relief; crag and mesa relief use about 40%, keeping hills gentler under the top-down camera even with an existing saved config. `terrainHeight` still scales the landscape; roads, towns, river surfaces and collision all use the same revised heightfield. Increasing map size spreads the same bounded terrain resolution across a larger area. This is a finite region; town blueprints are currently defined in code. Regeneration is synchronous after the loading screen is drawn and can briefly stall on large or dense configurations.

## Landscape detail

`trailDensity` scales the dirt trail network (default 1, range 0–3; 0 disables it). Trails link scattered junctions, every point of interest, and the main roads. Each junction joins its nearest two or three neighbours, so the map fills with a loose web of routes. Every trail curves on its own and is a dirt road (6–8 m), a track (4–5.5 m) or a footpath (2.4–3.4 m). Trails avoid town centres, can ford the river, keep scenery clear, show on the minimap and drive as dirt.

`vegetation` controls deterministic ground-cover and larger-plant density (default 1, range 0–4). Nearby ecology streams in 32-metre tiles: Pandazole tree canopies, riparian trees, cacti, shrubs, grass and rocks, supplemented by procedural flowers, leaf litter and deadwood. Large trunks and boulders have collision; fine plants bend around the car without adding driving resistance. Tiles unload beyond the camera region and reproduce from their seed when revisited. Scenery collision is currently static; this pass does not implement persistent felled trees.

Roads use approximately 1.5-metre curve samples, gravel shoulders and worn center lines. Town businesses have storefront trim, roof equipment, planters and back-lot clutter. Terrain combines slope/biome masks, gravel and micro-relief shading; foliage moves with wind and the river has layered flowing ripples. Scenic ravens provide background movement. Nature meshes come from the licensed Pandazole Nature pack. Each complete model is combined at build time, preserving its component transforms, atlas UVs and normals; runtime tiles batch those reusable models. The world generator and supplemental details remain project code. No Valheim assets are used.
