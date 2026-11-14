# Island Map — Coordinate System

Applies to every map produced by `IslandMapGenerator` (Assets/_IslandTools/Mapping).

## Conventions
| Item | Definition |
|---|---|
| World plane | Unity world **X / Z** in metres. Y is elevation and is not drawn as a map axis. |
| Map "east" | World **+X** (image right) |
| Map "north" | World **+Z** (image up). Unity has no intrinsic north; this is a fixed *map* convention. If the owner wants a different compass bearing, change the label only — the numbers stay the same. |
| Map extent | Union of all active terrains: `[OriginX, OriginX+SizeX] × [OriginZ, OriginZ+SizeZ]` (the terrain's own `transform.position` is honoured, it is not assumed to be 0,0). |
| Pixels | Origin top-left, +x right, +y down. Continuous coordinates: pixel centres are at `+0.5`. |
| Grid | Lines are drawn at **multiples of the grid size in absolute world coordinates** (anchored at world 0, not at the terrain corner), so a label "X 1200" is world X = 1200. Every 5th line is heavier. Grid size defaults to ~1/15 of the longest terrain side snapped to 10/20/25/50/100/…; override with `-mapGrid`. |
| Region id | Grid cell `CC-RR` where `CC = floor(worldX / grid)`, `RR = floor(worldZ / grid)`. Purely geometric — no named regions are invented. Negative cells print with a minus sign. |

## Formulas (`IslandMapSpace`)
```
px = (wx - OriginX) / SizeX * Width          py = Height - (wz - OriginZ) / SizeZ * Height
wx = OriginX + px / Width * SizeX            wz = OriginZ + (Height - py) / Height * SizeZ
```
C#: `space.WorldToMap(x, z)`, `space.MapToWorld(px, py)`, `space.WorldToUV(x, z)`, `space.ContainsWorld(x, z)`.
The `space` for a generated map is stored in `island_map_meta.json`, so any tool (including the later player map) can reconstruct it.

## Finding a map position in Unity
1. Read X/Z off the grid labels (or convert a pixel with `MapToWorld`).
2. Look up ground height: `island_cells.csv` gives `ground_y` per cell centre, or in-editor `terrain.SampleHeight(new Vector3(x,0,z)) + terrain.transform.position.y`.
3. Place/inspect at `(x, y, z)` in the scene.

## Companion data files
`island_cells.csv` (cell centre X/Z, ground Y, slope, dominant layer, water flag) is the coordinate readout for candidate construction sites. `island_height16.raw` is a 16-bit heightmap (row 0 = south, normalised to the min/max in the metadata) for the future player map.

## Future markers
`IslandMapMarker` / `IslandMapMarkerSet` (JSON via `JsonUtility`) hold: name, world position, optional zone polygon (world X/Z), asset requirements, purpose, assigned branch, status. **No markers exist yet.**
