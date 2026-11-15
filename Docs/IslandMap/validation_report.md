# Validation report: Phase 1 mapping

Run on the owner's PC on 2026-11-14 (local time), with Unity 2022.3.44f1 (URP 14) in **batch mode** and the real local assets.
No Play Mode test was needed or run: nothing in this phase changes gameplay.

## What was actually run

| Step | How | Result |
|---|---|---|
| Scene inspection (all 5 scenes) | `IslandMapInspector.Run`, batch mode, read-only | Done. See `scene_inspection_report.md` |
| Map generation from Scene 1 | `IslandMapGenerator.RunBatch`, batch mode **with GPU**, read-only | Done in 287 s. All outputs written |
| Automatic alignment checks | Built into the generator (results in `generation_log.txt`) | **All PASS** (below) |
| EditMode unit tests | `-runTests -testPlatform EditMode -assemblyNames IslandMap.EditorTests` | **9 / 9 passed** |
| Visual review | Zoomed crops of the PNGs compared with known object positions | Correct (below) |

## Alignment: does the map match Unity world positions?

1. **Camera vs formula.** For 1024 random 3D points (64 per render tile), the render camera's own projection (`Camera.WorldToViewportPoint`) was compared with the documented formula (`MapProjection.WorldToPixel`). Worst difference: **0.0004 px**.
2. **End-to-end render test.** White cubes (8 × 8 m) were placed temporarily at six known world positions. They were rendered through the *same tiled cameras* that made the map, one at a time, then located in the image.

   | Marker (world X, Z) | Expected px | Found px | Error |
   |---|---|---|---|
   | 342.5, 2152.5 | 561.2, 569.3 | 561.5, 569.5 | 0.38 px = 0.23 m |
   | 1250.0, 1250.0 (corner of 4 render tiles) | 2048.0, 2048.0 | 2048.0, 2048.0 | 0.00 px |
   | 2182.5, 322.5 | 3575.8, 3567.6 | 3575.5, 3567.5 | 0.33 px = 0.20 m |
   | 2280.0, 2257.5 | 3735.6, 397.3 | 3735.5, 397.5 | 0.20 px = 0.12 m |
   | 202.5, 185.0 | 331.8, 3792.9 | 331.5, 3792.5 | 0.48 px = 0.29 m |
   | 832.5, 1545.0 | 1364.0, 1564.7 | 1364.0, 1564.5 | 0.18 px = 0.11 m |

   **Worst error: 0.48 px (0.29 m). PASS** (threshold 1 px = 0.61 m).
   - The remaining error is under one pixel. It comes from the cube edges snapping to whole pixels, not from the projection.
   - The cubes were deleted after the test and never saved.
3. **Real objects.** The rendered roofs of the Goju pagoda (world 1066, 1430) and the Sanju pagoda (1572, 602) sit at clean-map pixels (1746, 1753) and (2575, 3109). That is exactly where the formula puts them.
4. **Round trip.** World → pixel → world over 2000 points: max error 0.00012 m.
5. **Corners.** The NW terrain corner (X 0, Z 2500) maps to px (0, 0); the SE corner (2500, 0) maps to px (4096, 4096).
6. **Image sizes.** A unit test confirms each PNG's real pixel size matches the size stored in `IslandMapData` and `island_map.json`.
7. **Stored heights.** The 513 × 513 height grid in `IslandMapData` was compared with the live terrain at 2000 random points. Mean error 0.04 m, max 0.60 m (on cliffs). That is fine for planning.
   - The Developer Map window uses the live terrain when it is open, so it has no such error.

### A test bug found and fixed during the run

On the first generation, two of the six markers reported large errors (5 px and 177 px).

* **Cause:** all six cubes were visible at the same time. The centre cube sits on the corner shared by four render tiles, so it showed up as a partial cube in the tile used to measure another marker and pulled that centroid off.
* **The map itself was not misaligned.** The other four markers were within 0.5 px.
* **Fix:** the check now shows one marker at a time and measures across every tile it touches. The tile-corner case is kept on purpose, as a seam test.

## Unit tests (EditMode, 9/9 passed)

* `Corners_MapToImageCorners_NorthUp`: north = +Z is up and east = +X is right.
* `RoundTrip_WithMargins`: conversion is correct with the developer map's margins; margins are not map.
* `PixelCentre_Formula_MatchesDocumentation`: the README formulas match the code.
* `Grid_NamesCells_FromNorthWest`: A1 is the NW cell, J10 the SE cell; C4 = X 500–750, Z 1500–1750; cell names parse.
* `Grid_LinesSitOnRoundWorldCoordinates`: grid lines land on multiples of the cell size even for odd terrain origins.
* `TopDownCamera_AgreesWithProjection`: the camera set-up used for rendering matches the formula.
* `HeightGrid_EncodesAndInterpolates`: height storage and lookup.
* `Markers_JsonRoundTrip_AndValidation`: the marker file format, zone containment, and detection of duplicate ids, bad statuses and out-of-map points.
* `Generated_ImagesMatchStoredProjections`: the exported PNGs match their stored projections, and every existing feature lies inside the map.

## Not tested here (needs a quick check in the Unity editor)

* **Developer Map window** (`Tools > Island > Map > Developer Map`): it compiles, but its interactive features were not clicked through, because batch mode has no editor UI. These need a manual check:
  - hover readout
  - pin
  - copy
  - Frame in Scene
  - Scene-view pins
  - view switching
  - zoom and pan
* **Menu-driven generation** (`Tools > Island > Map > Generate Maps (Scene 1)`): it runs the same `Generate` code as the batch run, which was tested, but it was not started from the menu. It asks before leaving an unsaved scene and reopens your scene afterwards.
* **Spot-check by hand:** pick any grid intersection on `island_devmap.png`, for example X 1000 / Z 1500. In Scene 1, place an object at that position and confirm it is in the same spot relative to the terrain.

## Safety

* Scene 1 was opened read-only by the tools and **never saved**. Its file is unchanged.
* No terrain, scene, prefab, material, vegetation or gameplay code was modified. That includes the controller and the sword and combat systems, which are not on this branch.
* Render settings were changed in memory only and restored, or discarded with the unsaved scene:
  - LOD bias and maximum LOD
  - fog off
  - terrain draw distances
* No packages were imported. The tools use only Unity built-ins (`LegacyRuntime.ttf` for map text).
* The new files are:
  - `Assets/_IslandMap/` (runtime data and coordinate code, editor tools, tests)
  - `Assets/_IslandMap/Data/IslandMapData.asset`
  - `Docs/IslandMap/`
* The developer map is editor-only. `IslandMapData` is plain data that no scene references, so it is not included in builds.
