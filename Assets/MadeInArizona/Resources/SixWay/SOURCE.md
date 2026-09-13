# Unity six-way fluid maps

Downloaded 11 September 2026 from the free ready-to-use texture library linked by Unity's article, “Realistic smoke lighting with 6-way lighting in VFX Graph” (Mathieu Muller, 20 January 2023).

Article: https://unity.com/blog/engine-platform/realistic-smoke-with-6-way-lighting-in-vfx-graph
Publisher's library: https://drive.google.com/drive/folders/1_oh0UkAOW6hISqouCXjYwKQ0lkoj0CIF

These are Unity-provided third-party textures, not original Made in Arizona artwork. The article describes the library as free samples ready for use in games. No separate license file was present among the downloaded maps. Preserve this source notice with the project.

| Project file | Original file | Publisher file ID |
|---|---|---|
| Fireball_P.tga | TX_Pyro_Fireball_A_P.tga | 1fTsFqeiAwq7I-cmgXlHjK5yaXvmJ3wxT |
| Fireball_N.tga | TX_Pyro_Fireball_A_N.tga | 1-Wa5ZMXcMPuiM8PDZYYmu4YxjoRy7qdl |
| Smoke_P.tga | TXT_Pyro_SmokeLoop_A_P.tga | 14Xctf7szyJJgdx1j0j_ZMbDS2YPRcTqu |
| Smoke_N.tga | TXT_Pyro_SmokeLoop_A_N.tga | 1UkZPqpxOc3H_T-WV1gLxPue4v9GFrK9N |

Files are the unmodified downloaded TGA assets. Positive RGB stores right/top/back light response and alpha stores opacity. Negative RGB stores left/bottom/front response; fireball negative alpha stores emission. Fireball is a 16 × 4 atlas (64 rectangular frames); smoke is 8 × 8 (64 square frames). Import linear, preserve all packed channels, and disable alpha-color dilation.
