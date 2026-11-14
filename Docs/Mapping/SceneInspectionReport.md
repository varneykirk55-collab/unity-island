# Scene Inspection Report — Phase 1

Repository inspected: `varneykirk55-collab/unity-island`, `main` @ `14e2bc6` (single commit). Inspection was static (file/YAML reading); no Unity Editor ran.

## Result: the island terrain is NOT in this repository
`.gitignore` excludes the Asset Store packs, including `/Assets/Free Island Collection/`, which is where the project's own scripts expect the island scenes and terrain (`Assets/Free Island Collection/Scenes/Scene 1.unity`, referenced by `IslandBuild`, `IslandPopulate`, `IslandPads`, `IslandSurvey`, `IslandDoorFix`, and `IslandTraversalTests`).

Scenes actually present:
| Scene | Contents |
|---|---|
| `Assets/Scenes/SampleScene.unity` | 7 KB: Directional Light + Main Camera only. No terrain. |
| `Assets/Starter Assets/Sample/ThirdPersonController/Playground.unity` | Starter Assets test playground. No Terrain component. |
| `Assets/Starter Assets/Sample/FirstPersonController/Playground.unity` | Same. No Terrain component. |

No `TerrainData` assets, heightmaps, or splat maps exist (searched `*.terraindata`, `TerrainData*`, `.unity`, `.asset`). The eight `_TerrainAutoUpgrade/*.terrainlayer` files are layer definitions only (Dirt_1/2, Grass_1/2, Cliff_1/2, Mossy_Rock, Sand) and carry no spatial data.

## Therefore, not determinable
- The five island terrain scenes, their differences, and which to develop further (only "Scene 1" is referenced by code, so it is the scene the existing tooling targets — a pointer, not a verified choice).
- Terrain dimensions, world origin, elevation range, orientation, existing roads/lakes/rivers, vegetation, and placed objects in the island scenes.

## Hints recorded in committed scripts (unverified, from code only)
- Scene 1 holds one `Terrain`, and `IslandBuild` uses a sea level of **45 m** and a 12×12 km ocean plane centred on the terrain (`OceanGrid_12km.asset` exists).
- Pagoda sites (X,Z): Goju (1064, 1432), Sanju (1568, 608) → the terrain is at least ~1.6 km on a side, if these are world coordinates.
- Placed objects go under a root named `Island_Environment`; the generator reads that root for structures.
- Committed (non-ignored) assets: 8 Korean palace prefabs (`KR_*`, mesh data from ignored packs), tree-variant collider prefabs, ocean shader/material, Starter Assets. Prefabs reference ignored packs, so they may show missing references locally.

**No island map was produced; no terrain data was fabricated.**

## Needed to finish
Run the generator on a machine with the Free Island Collection imported (or commit/attach the scene + its `TerrainData`). See `Docs/Mapping/README.md`.
