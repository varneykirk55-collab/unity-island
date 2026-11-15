# Unity Island: tactical mapping foundation (Phase 1)

This folder holds the maps of the island as it exists in the project right now, and the data needed to plan on them.
Nothing here places, moves or plans anything in the world.

| File | What it is |
|---|---|
| `island_overhead.png` | **Clean overhead map.** The real Scene 1 rendered straight down (orthographic), with light hillshade. 4096 × 4096 px, no overlays. |
| `island_devmap.png` | **Developer map.** The same image with grid, world coordinates, contours, coastline, spot heights, existing structures and a legend. 5936 × 4726 px. |
| `island_terrain_analysis.png` | Elevation-tinted terrain with flat open ground, terrain trees and water, computed from the heightmap. 4096 × 4566 px. |
| `island_map.json` | Machine-readable map data: projections, grid, terrain info, existing structures, spot heights, per-cell statistics. |
| `markers.json` | **Empty** marker file for approved construction locations (see below). |
| `scene_inspection_report.md` | Which scene was mapped, how the five island scenes differ, and what is missing. |
| `validation_report.md` | What was tested, and what still needs checking by hand. |
| `generation_log.txt`, `scene_inspection_raw.txt` | Raw output of the last generator and inspector runs. |

## Coordinate system

All coordinates are **Unity world space in metres**. They are the numbers you type into a Transform's Position in the Inspector.

* **Up on every map = north = world +Z.** Right = east = world +X. The maps are never rotated.
* World Y is height. The maps show it as contours and spot heights. The Developer Map window reads it live from the terrain.
* Mapped area = terrain boundary: **X 0 to 2500, Z 0 to 2500** (the terrain sits at the world origin, unrotated, unscaled).
* Ground height ranges from about Y 17.5 (sea floor) to Y 408 (summit). **Sea level is Y 45** (the `Island_Environment/Ocean` plane).
* Scale: 2500 m across 4096 px = **0.6103515625 m per pixel** (1.6384 px per metre).

### Pixel ↔ world formulas

Pixel coordinates `(px, py)` are measured from the **top-left corner of the image**, like any image editor shows them
(Photoshop, GIMP, Paint.NET, Figma). Pixel `(i, j)` covers the square from `i` to `i+1`; its centre is `(i + 0.5, j + 0.5)`.

Let `s = 0.6103515625`, the metres per pixel.

**Clean map** `island_overhead.png` and **analysis map** `island_terrain_analysis.png`. The map starts at the image's top-left corner; the analysis legend strip sits below it.

```
world X = px × s                 px = X / s
world Z = 2500 − py × s          py = (2500 − Z) / s
```

**Developer map** `island_devmap.png`. The map area starts 300 px from the left and 330 px from the top.

```
world X = (px − 300) × s         px = 300 + X / s
world Z = 2500 − (py − 330) × s  py = 330 + (2500 − Z) / s
```

Example: the Goju pagoda is at world X 1066, Z 1430.
- Clean map: px = 1066 / 0.61035 ≈ 1746.6, py = (2500 − 1430) / 0.61035 ≈ 1753.1.
- Developer map: (2046.6, 2083.1).

These numbers are also stored in `island_map.json` (`Overhead`, `DeveloperMap`, `Analysis`). They're in the form
`WorldMin`, `WorldSize`, `ImageSize`, `MapRectMin` and `MapRectSize`, so any tool can convert without hard-coding them:

```
X = WorldMin.x + (px − MapRectMin.x) / MapRectSize.x × WorldSize.x
Z = WorldMin.y + WorldSize.y − (py − MapRectMin.y) / MapRectSize.y × WorldSize.y
```

### Grid

* **250 m cells**, named like a printed map:
  - Columns A–J run west to east, starting at X 0.
  - Rows 1–10 run north to south, starting at Z 2500.
  - A1 is the north-west corner and J10 the south-east.
* Cell `C4` = X 500–750, Z 1500–1750. In general, the cell for column index `c` (A = 0) and row number `r` covers:
  - X = `250·c` to `250·(c+1)`
  - Z = `2500 − 250·r` to `2500 − 250·(r−1)`
* Thin lines every 50 m. Every grid line is a round world coordinate, so you can read a position to about ±5 m by eye.
* The cell size can be changed: `-mapCell` in batch mode, or `Settings.CellSize`.

### In code

`Assets/_IslandMap/Runtime` holds the `IslandMap` assembly. It has no gameplay dependencies and can be reused by the future player map.

```csharp
var data = AssetDatabase.LoadAssetAtPath<IslandMap.IslandMapData>("Assets/_IslandMap/Data/IslandMapData.asset");
Vector2 px   = data.DeveloperMap.WorldToPixel(new Vector3(1066, 0, 1430)); // world -> developer-map pixel
Vector2 xz   = data.Overhead.PixelToWorldXZ(new Vector2(1746.6f, 1753.1f)); // clean-map pixel -> world X/Z
float  y     = data.SampleHeight(xz);                                      // ground height from the stored grid
string cell  = data.Grid.CellName(new Vector3(1066, 0, 1430));            // "E5"
Rect   area  = data.Grid.CellRect(new Vector2Int(2, 3));                   // C4 in world X/Z
```

## Developer Map window (in the editor)

Open it from **Tools > Island > Map > Developer Map**.

* Hover over the map to read world X, Z, ground Y and the grid cell.
  - Ground Y comes from the open terrain, or from the stored height grid if no terrain is open.
* Click to pin a point. Pins also appear in the Scene view as magenta posts.
  - **Copy X,Y,Z** copies the values as Transform numbers.
  - **Frame in Scene** moves the Scene view camera to the pin.
* Type an X/Z or a cell name (e.g. `D6`) to pin it.
* You can switch between the developer, clean overhead and analysis images.
* It only exists in the editor and is never part of a build.

## Regenerating the maps

Regenerate after the island changes. The process is repeatable and reads only the scene, never editing it.

* In the editor: **Tools > Island > Map > Generate Maps (Scene 1)**. It takes a few minutes, then reopens your scene.
* From a terminal (Unity must be closed; **do not** pass `-nographics`, the render needs the GPU):

  ```
  Unity.exe -batchmode -quit -projectPath "<project>" -executeMethod IslandMap.Editor.IslandMapGenerator.RunBatch
            [-mapScene "Assets/.../Scene.unity"] [-mapOut Docs/IslandMap] [-mapPixels 4096] [-mapCell 250]
  ```
* Read-only scene report for all five island scenes: **Tools > Island > Map > Inspect Island Scenes (read-only)**.

Requirements: the git-ignored Asset Store packs must be imported locally, because the terrain, scene and buildings live in them. A clone of the repository alone **cannot** regenerate the maps (see `scene_inspection_report.md`).

## Planned-location markers (for later)

`markers.json` is deliberately empty. When the owner approves construction locations, add entries like:

```json
{
  "Version": 1,
  "CoordinateSystem": "Unity world space, metres, +X east, +Z north, +Y up",
  "Markers": [
    {
      "Id": "example-id",
      "Name": "Location name",
      "Position": { "x": 0, "y": 0, "z": 0 },
      "Zone": [ { "x": 0, "y": 0 }, { "x": 10, "y": 0 }, { "x": 10, "y": 10 }, { "x": 0, "y": 10 } ],
      "AssetRequirements": [ "asset or prefab name" ],
      "Purpose": "why it is there",
      "AssignedBranch": "git branch doing the work",
      "Status": "Proposed",
      "Notes": ""
    }
  ]
}
```

Field notes:

* `Zone` points are world **X and Z**. The JSON field names are `x`/`y` because the points are stored as Vector2.
* `Status` is one of `Proposed, Approved, Assigned, InProgress, Built, OnHold, Rejected`.

The Developer Map window draws markers and zones, and warns about:

* duplicate ids
* unknown statuses
* points outside the map

The C# types are `IslandMap.MapMarker` and `IslandMap.MapMarkerSet`.
