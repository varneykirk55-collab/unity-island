# Island Mapping Tools

Code: `Assets/_IslandTools/Mapping/` — `Runtime/` (IslandMapSpace, IslandMapMarker, IslandMapMetadata), `Editor/` (IslandMapGenerator, font; Editor-only), `Tests/` (EditMode).

## Generate maps (needs the island scene + Free Island Collection locally)
Editor: open the island scene → `Tools > Island > Mapping > Generate Maps (open scene)`.

Batch:
```
Unity -batchmode -quit -projectPath <project> -executeMethod IslandTools.Mapping.Editor.IslandMapGenerator.BatchGenerate ^
  -mapScene "Assets/Free Island Collection/Scenes/Scene 1.unity" -mapOut MapOutput -mapPx 4096 -mapGrid 100 -mapSea 45 -mapContour 10
```
Options: `-mapPx` longest side (≤8192), `-mapGrid` metres (0 = auto), `-mapSea` waterline Y (omit to auto-detect an object named Ocean/Water/Sea), `-mapContour` metres.
Outputs: `island_overhead.png`, `island_devmap.png`, `island_map_meta.json`, `island_height16.raw`, `island_cells.csv`, `island_structures.json`. Re-run any time after the island changes. See `CoordinateSystem.md`.
Tip: add `MapOutput/` to review commits deliberately; generated output is not committed by default.
