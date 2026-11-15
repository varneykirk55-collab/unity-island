# Scene inspection report: Phase 1 mapping

Generated from a read-only inspection of the project on 2026-11-14 (`Tools > Island > Map > Inspect Island Scenes`). The full raw output is in `scene_inspection_raw.txt`. No scene, terrain or asset was modified.

## Where this ran (and why not in the cloud)

The brief asked for a cloud run. That was not possible with real data, and the project owner approved running locally instead.

* The terrain, the five island scenes and every placed building come from **git-ignored Asset Store packs**:
  - `Free Island Collection`
  - `JTS_*`
  - `DongGungComplex`
  - `GeoncheonggungComplex`
  - `Waldemarst`
  - `Japanese Garden Pack`
* The GitHub repository therefore contains **none** of the data an accurate map needs. A cloud machine would also need a GPU-capable Unity 2022.3.44f1 editor to render the map.
* The work was done on the owner's PC: Unity 2022.3.44f1 in batch mode, using the real local assets.
* The **generated outputs** are committed: images, JSON, data asset and tools. Anyone can use the maps from the repository. Only *regenerating* them requires the local packs.

## The five island scenes

All five are in `Assets/Free Island Collection/Scenes/`. Each scene uses its **own** terrain data asset (`Terrain 1`–`Terrain 5.asset`), so they are five different islands. They share:

* the same footprint: 2500 × 2500 m at the world origin, X 0–2500, Z 0–2500, no rotation or scale
* the same heightmap resolution (2049) and alphamap resolution (2048)
* the same 8 texture layers (Sand, Dirt_2, Mossy_Rock, Dirt_1, Grass_1, Grass_2, Cliff_1, Cliff_2)

| Scene | Terrain data | Height range (world Y) | Land above Y 45 | Trees | Water | Other content |
|---|---|---|---|---|---|---|
| **Scene 1** | Terrain 1 (height scale 650) | 17.5 – 408.2 | 61.5 % | **27,099** black pines (8 prototypes) | **Ocean plane at Y 45** | **All project content** (below) |
| Scene 2 | Terrain 2 (650) | 24.6 – 409.8 | 60.2 % | 0 | none | one test Cube at (1145, 67, 1566) |
| Scene 3 | Terrain 3 (750) | 21.4 – 293.7 | 83.6 % | 0 | none | none |
| Scene 4 | Terrain 4 (650) | 20.3 – 361.5 | 61.4 % | 0 | none | none |
| Scene 5 | Terrain 5 (650) | 19.5 – 426.1 | 58.6 % | 0 | none | none |

Scenes 2–5 are the pack's untouched sample islands: just a terrain, a camera, a light and a reflection probe. None of the scenes is in the Build Settings list.

## Scene used for the map: **Scene 1**

Scene 1 is the only scene that has been developed. It contains:

* the ocean
* the painted forest
* the player (`Player_Setup`)
* all placed structures under `Island_Environment`

It is the scene to keep building on.

Two related points:

* The sword-integration branch has a copy, `Assets/_IslandScenes/Island_Main.unity`. It has the same terrain and the same structures, plus the sword setup on the player, so its geography is identical.
* No scene was replaced, discarded, re-saved or modified.

### Scene 1 terrain

* `Terrain` → `Terrain 1.asset`
  - position (0, 0, 0), size 2500 × 650 × 2500
  - heightmap 2049² (1.22 m between height samples)
* Ground: Y 17.5 (sea floor at the map edge) to **Y 408.1** (summit at about X 1407, Z 1045, cell F6). That is 363 m above sea level.
* Sea level: **Y 45**, from `Island_Environment/Ocean`, a 12 × 12 km plane centred on the terrain.
  - Land is 3.85 km² and the ocean inside the mapped square is 2.40 km².
* Orientation: Unity +Z is treated as north; the maps are drawn with +Z up. The terrain is not rotated.

### Existing structures (numbered as on the maps)

| # | Object | Category | Centre X, Z (Y) | Cell |
|---|---|---|---|---|
| 1 | Bay_Shore (39 objects: rocks, lanterns, fences) | Bay_Shore | 1513, 1828 (45) | G3 |
| 2 | Garden (89 objects) | Garden | 457, 1886 (106) | B3 |
| 3 | Goju_Approach (25 objects: torii, lanterns, platforms) | Goju_Approach | 990, 1488 (220) | D5 |
| 4 | KR_Donggung_Court_01 | Korean_Buildings | 2254, 814 (96) | J7 |
| 5 | KR_Donggung_Court_02 | Korean_Buildings | 575, 1115 (142) | C6 |
| 6 | KR_Donggung_Court_03 | Korean_Buildings | 1789, 2017 (46) | H2 |
| 7 | KR_Donggung_Court_04 | Korean_Buildings | 2120, 800 (108) | I7 |
| 8 | KR_Donggung_EastHall | Korean_Buildings | 417, 1257 (114) | B5 |
| 9 | KR_Geoncheonggung_Palace | Korean_Buildings | 1211, 2279 (52) | E1 |
| 10 | KR_Gyedong_House | Korean_Buildings | 500, 2000 (98) | C2 |
| 11 | Pagoda_Goju_Main (site 2) | Landmarks | 1066, 1430 (227) | E5 |
| 12 | Pagoda_Sanju_South (site 3) | Landmarks | 1572, 602 (192) | G8 |
| 13 | Sanju_Shrine (6 objects: torii, lanterns, votive) | Sanju_Shrine | 1582, 589 (191) | G8 |
| 14 | PlayerArmature (player spawn point) | Player start | 918, 1544 (225) | D4 |

Footprints, heights and source prefabs are in `island_map.json` (`ExistingFeatures`) and `scene_inspection_raw.txt`.

## What the source data does and does not contain

| Brief asked to show | Status |
|---|---|
| Coastlines and beaches | ✅ Coastline is where the terrain crosses Y 45 (drawn on the maps). Beaches are the painted Sand layer, visible in the render. |
| Mountains, hills, elevation | ✅ From the heightmap: contours every 10 m, spot heights, hillshade, elevation tint. |
| Forest and vegetation | ✅ 27,099 terrain trees, rendered and plotted. Grass detail layers exist but are below map resolution, so they are not drawn. |
| Rivers, lakes, waterways | ⚠️ **None exist.** There are no river or lake objects, and no enclosed basin below sea level. The only inland-looking water is the **bay**, an arm of the ocean around G3. |
| Roads and paths | ⚠️ **None exist as objects.** The terrain has dirt layers, but nothing distinguishes painted trails from bare ground, so no roads are drawn. The Goju torii approach (3) is the only built route. |
| Buildings and landmarks | ✅ All 14 groups and buildings above, from the scene hierarchy, with footprints. |
| Open terrain for development | ✅ Shown as **geometric** flat open ground on the analysis map: slope ≤ 12°, ≥ 2 m above sea, no trees nearby, ≥ 15 m from structures. Per-cell percentages are in the JSON. This is measurement, not site selection. |

## Missing or limited assets

* **Not in the repository:** all the Asset Store packs listed above. Cloning the repo gives the maps and tools but cannot regenerate the maps or open Scene 1 with content.
* **Sword branch content:** the sword packs (`EEJANAI_Team`, `JTS_TheSamuraiSword`) are git-ignored. They're not needed for mapping.
* Nothing required for the map was missing locally.
