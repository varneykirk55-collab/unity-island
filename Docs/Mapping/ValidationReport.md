# Validation Report — Phase 1

## Actually done
- Cloned repo; statically inspected scenes, assets, `.gitignore`, and existing island scripts (see SceneInspectionReport).
- Verified the coordinate-conversion **formulas** with an independent Python port: corner mapping, north-up orientation, and 100,000 random round-trips world→map→world (error < 1e-6). This tests the math, **not the C# code**.

## NOT done
- **No Unity Editor is available in this cloud environment and no C# compiler**; none of the new C# was compiled. Expect possible compile errors on first import; fix before use.
- `IslandMapGenerator` has never run. No PNG, CSV, RAW, or JSON map output exists. No map was drawn or approximated.
- EditMode tests (`IslandMapSpaceTests`) are written but not executed. No PlayMode tests were run.
- Generated `.meta` files were hand-written with new GUIDs; Unity may rewrite them.
- Existing scenes, prefabs, materials, controller, animation, and combat code were not touched (additions only: `Assets/_IslandTools/Mapping/**`, `Docs/Mapping/**`).

## Requires local Unity 2022.3.44f1 verification
1. Import compiles cleanly; run Test Runner → EditMode → `IslandMapSpaceTests`.
2. Open the island scene (with Free Island Collection), run `Tools > Island > Mapping > Generate Maps` or the batch command in `README.md`.
3. Check: terrain extent in `island_map_meta.json` equals the Terrain's position/size; click a known object (e.g. a pagoda) and confirm its X/Z against the dev-map grid and `MapToWorld`; confirm the coast matches the 45 m waterline; check the `Island_Environment` structure footprints.
4. Confirm the generator leaves the scene unmodified (it only reads; batch mode never saves).
5. Judge readability of labels at the chosen resolution (`-mapPx 4096` default, 8192 max); label font is a built-in 3×5 bitmap.

## Known limitations / design notes
- Roads: drawn only from terrain layers whose names contain road/path/trail/street. The existing eight layers match none, so currently none would be drawn. Dirt is treated as natural ground.
- Rivers/lakes: shown only as terrain below the sea level; Suimono/other water objects are not read.
- Forest: from `TerrainData.treeInstances` only (not detail/grass layers or scene-placed trees).
- Structures: renderer bounds (axis-aligned rectangles) of direct children of `Island_Environment`.
- Editor tooling lives in an Editor-only assembly, so it is excluded from player builds. The small runtime assembly (coordinate math + marker schema) is data-only and has no gameplay dependencies.
