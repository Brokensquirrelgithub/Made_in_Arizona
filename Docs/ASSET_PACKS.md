# Requested Asset Store packs

Status verified 13 September 2026. Only the Nature package is present in the local Unity Asset Store cache. No unacquired package is represented as integrated.

| Pack | Status |
|---|---|
| [Pandazole Nature Environment](https://assetstore.unity.com/packages/3d/environments/pandazole-nature-environment-low-poly-pack-212621) | Imported from the existing Unity package cache; integrated into streamed ecology. |
| [Rockets, Missiles & Bombs](https://assetstore.unity.com/packages/3d/props/weapons/rockets-missiles-bombs-cartoon-low-poly-pack-73141) | Awaiting authenticated download and integration. |
| [Mobile Optimize-Free Low Poly Cars](https://assetstore.unity.com/packages/3d/vehicles/mobile-optimize-free-low-poly-cars-327313) | Awaiting download; existing handmade vehicles are retained. |
| [US Road Signs Free](https://assetstore.unity.com/packages/3d/props/exterior/us-road-signs-free-164941) | Awaiting download and integration. |
| [Low poly Road Pack](https://assetstore.unity.com/packages/3d/environments/roadways/low-poly-road-pack-created-with-fastmesh-asset-293643) | Awaiting download; existing smooth generated roads remain until replacement. |
| [Pandazole City Town](https://assetstore.unity.com/packages/3d/props/exterior/pandazole-city-town-lowpoly-pack-205787) | Awaiting download and integration. |
| [Explosives Package](https://assetstore.unity.com/packages/3d/props/explosives-package-8093) | Awaiting download and integration. |
| [Shooting Sound](https://assetstore.unity.com/packages/audio/sound-fx/shooting-sound-177096) | Awaiting download and audition. |
| [Post Apocalypse Guns Demo](https://assetstore.unity.com/packages/audio/sound-fx/weapons/post-apocalypse-guns-demo-33515) | Awaiting download and audition. |
| [Western Audio & Music](https://assetstore.unity.com/packages/audio/sound-fx/western-audio-music-67788) | Awaiting download and audition. |

The Pandazole Nature models and atlas remain under their original `Assets/Pandazole_Ultimate_Pack` paths with GUID metadata. These are third-party assets governed by the [Unity Asset Store license](https://unity.com/legal/as-terms), not original project artwork. The build creates a Resources catalog of complete selected models, combining trunk/canopy components with their authored transforms before runtime size normalization. Runtime batching preserves atlas UVs and authored normals, reuses cached source arrays, and unloads distant ecology tiles.

## Audio audit — 13 September 2026

The three linked audio packs have **not** made it into the project. No WAV, OGG, MP3 or AIFF files are present under `Assets`, and none of these packages are in the local Unity Asset Store cache. `AudioManager.Awake` generates the weapon, explosion, engine and interface clips through `AudioSynthesis`; `MusicManager` still supplies procedural music. Downloading a pack alone will not replace these calls: the clips must be imported, auditioned and connected to the runtime mix.

## Audio follow-up — 26 September 2026

The workspace has no imported gun sound clips, and the accessible Windows Unity asset cache has no gun sound package. The supplied gun pack's name or location is needed to complete its integration. `WeaponDefinition.fireSounds` now supports direct serialized references to imported single-shot recordings, with random variation through the existing pooled playback. Player weapons, enemy primary weapons and enemy rockets use this path; unassigned weapons retain synthesis. Native regression verifies playback with a temporary test clip, not a third-party pack recording.

## Supplied soundtrack — 26 September 2026

All 14 MP3s from `C:\Users\Broke\Downloads\Made in Arizona Music` have been copied unchanged into `Assets/MadeInArizona/Resources/Audio/Music`; SHA-256 comparison verifies each copy. This replaces procedural music with menu/garage themes and a shuffled driving/combat playlist. These are user-supplied recordings, separate from the Asset Store gun sound pack. The original Downloads files are retained.

## Supplied weapon recordings — 26 September 2026

[Free Weapon Sound Effects by SoundLab_1](https://assetstore.unity.com/packages/audio/sound-fx/weapons/free-weapon-sound-effects-388474) was imported from the user's newly downloaded Unity package cache. All 40 WAV files retain their original `Assets/FreeWeaponSounds` paths and GUIDs. These third-party recordings use the Standard Unity Asset Store EULA; they are not original project audio.

All 13 weapon definitions now reference pack recordings: rifle variations for riveter/carbine/shredder/minigun/sniper, shotgun variations for sweeper/boomstick, GL_fire for explosive launchers, and projectile-insertion foley for mine placement. A Resources weapon audio bank references GL_explosion for grenade, rocket and ammunition detonations. Vehicle/fuel/propane explosions retain synthesis. The runtime pool handles random shot variation, modest pitch variation, spatial falloff and existing weapon/master volume controls. Rapid-fire recordings use lower gain to limit overlapping-shot buildup. Imported clips are not added to the synthesized-clip destruction list.

Short weapon effects preload and decompress for prompt playback; the longer music recordings remain streamed. Other pack foley, suppressed shots and long tails remain available for future mechanics, without adding forced reloads or extra reverberation to every automatic shot. `-miaSmokeTest -miaWeaponAudioTest` verifies imported asset references and pooled playback, including real player/enemy firing and enemy rockets.
