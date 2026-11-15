using System;
using UnityEngine;

namespace IslandMap
{
    /// <summary>
    /// Maps Unity world space (X/Z, metres) to pixels of a top-down map image and back.
    /// Conventions (see Docs/IslandMap/README.md):
    ///  - Map north = world +Z, map east = world +X. The image is not rotated.
    ///  - Pixel coordinates use a TOP-LEFT origin (as image editors report them): px.x grows east, px.y grows south.
    ///  - Integer pixel (i, j) covers [i, i+1) x [j, j+1); its centre is (i + 0.5, j + 0.5).
    ///  - The mapped world rectangle may sit inside a larger image (margins on the developer map): MapRect.
    /// </summary>
    [Serializable]
    public struct MapProjection
    {
        [Tooltip("World X/Z of the south-west corner of the mapped area.")]
        public Vector2 WorldMin;
        [Tooltip("World extent (X, Z) of the mapped area in metres.")]
        public Vector2 WorldSize;
        [Tooltip("Full image size in pixels.")]
        public Vector2Int ImageSize;
        [Tooltip("Top-left pixel of the mapped area inside the image (0,0 when the image is the map only).")]
        public Vector2Int MapRectMin;
        [Tooltip("Pixel size of the mapped area inside the image.")]
        public Vector2Int MapRectSize;

        public static MapProjection Create(Rect worldXZ, Vector2Int mapPixels, Vector2Int mapOffset = default, Vector2Int? imageSize = null)
        {
            return new MapProjection
            {
                WorldMin = worldXZ.min,
                WorldSize = worldXZ.size,
                MapRectMin = mapOffset,
                MapRectSize = mapPixels,
                ImageSize = imageSize ?? mapPixels + mapOffset,
            };
        }

        public Rect WorldRect => new Rect(WorldMin, WorldSize);
        public Vector2 WorldMax => WorldMin + WorldSize;
        /// <summary>Pixels per metre along X and Z.</summary>
        public Vector2 PixelsPerMetre => new Vector2(MapRectSize.x / WorldSize.x, MapRectSize.y / WorldSize.y);
        public float MetresPerPixel => WorldSize.x / MapRectSize.x;

        /// <summary>World position to normalized map coordinates: (0,0) = south-west corner, (1,1) = north-east.</summary>
        public Vector2 WorldToNormalized(Vector3 world) => WorldToNormalized(new Vector2(world.x, world.z));
        public Vector2 WorldToNormalized(Vector2 worldXZ) => new Vector2((worldXZ.x - WorldMin.x) / WorldSize.x, (worldXZ.y - WorldMin.y) / WorldSize.y);
        public Vector2 NormalizedToWorldXZ(Vector2 n) => new Vector2(WorldMin.x + n.x * WorldSize.x, WorldMin.y + n.y * WorldSize.y);

        /// <summary>World position to image pixel coordinates (top-left origin, fractional).</summary>
        public Vector2 WorldToPixel(Vector3 world) => WorldToPixel(new Vector2(world.x, world.z));
        public Vector2 WorldToPixel(Vector2 worldXZ)
        {
            var n = WorldToNormalized(worldXZ);
            return new Vector2(MapRectMin.x + n.x * MapRectSize.x, MapRectMin.y + (1f - n.y) * MapRectSize.y);
        }

        /// <summary>Image pixel (top-left origin) to world X/Z. Use (i + 0.5, j + 0.5) for the centre of pixel (i, j).</summary>
        public Vector2 PixelToWorldXZ(Vector2 pixel)
        {
            var n = new Vector2((pixel.x - MapRectMin.x) / MapRectSize.x, 1f - (pixel.y - MapRectMin.y) / MapRectSize.y);
            return NormalizedToWorldXZ(n);
        }

        /// <summary>Image pixel to a world position; Y is supplied by the caller (e.g. from IslandMapData.SampleHeight).</summary>
        public Vector3 PixelToWorld(Vector2 pixel, float y = 0f)
        {
            var xz = PixelToWorldXZ(pixel);
            return new Vector3(xz.x, y, xz.y);
        }

        public bool ContainsWorld(Vector3 world) => WorldRect.Contains(new Vector2(world.x, world.z));
        public bool ContainsPixel(Vector2 pixel) => new RectInt(MapRectMin, MapRectSize).Contains(Vector2Int.FloorToInt(pixel));
    }

    /// <summary>
    /// Square grid aligned to absolute world coordinates: grid lines lie at multiples of CellSize (…, -250, 0, 250, …),
    /// so every line is a round Unity coordinate. Cells are named column letter(s) + row number, counted from the
    /// north-west corner of the mapped area (A1 = north-west), like a printed map.
    /// </summary>
    [Serializable]
    public struct MapGrid
    {
        public float CellSize;
        [Tooltip("World X/Z of the north-west grid corner (first column's west edge, first row's north edge).")]
        public Vector2 NorthWestCorner;
        public int Columns;
        public int Rows;

        public static MapGrid Create(Rect worldXZ, float cellSize)
        {
            float x0 = Mathf.Floor(worldXZ.xMin / cellSize + 1e-4f) * cellSize;
            float x1 = Mathf.Ceil(worldXZ.xMax / cellSize - 1e-4f) * cellSize;
            float z0 = Mathf.Floor(worldXZ.yMin / cellSize + 1e-4f) * cellSize;
            float z1 = Mathf.Ceil(worldXZ.yMax / cellSize - 1e-4f) * cellSize;
            return new MapGrid
            {
                CellSize = cellSize,
                NorthWestCorner = new Vector2(x0, z1),
                Columns = Mathf.Max(1, Mathf.RoundToInt((x1 - x0) / cellSize)),
                Rows = Mathf.Max(1, Mathf.RoundToInt((z1 - z0) / cellSize)),
            };
        }

        /// <summary>Zero-based (column, row); row 0 is the northernmost. May be outside the grid.</summary>
        public Vector2Int CellIndex(Vector3 world) => new Vector2Int(
            Mathf.FloorToInt((world.x - NorthWestCorner.x) / CellSize),
            Mathf.FloorToInt((NorthWestCorner.y - world.z) / CellSize));

        public bool IsInside(Vector2Int cell) => cell.x >= 0 && cell.y >= 0 && cell.x < Columns && cell.y < Rows;

        public string CellName(Vector3 world) => CellName(CellIndex(world));
        public string CellName(Vector2Int cell) => IsInside(cell) ? ColumnName(cell.x) + (cell.y + 1) : "-";

        /// <summary>World X/Z rectangle covered by a cell.</summary>
        public Rect CellRect(Vector2Int cell) => new Rect(
            NorthWestCorner.x + cell.x * CellSize, NorthWestCorner.y - (cell.y + 1) * CellSize, CellSize, CellSize);

        public bool TryParseCell(string name, out Vector2Int cell)
        {
            cell = default;
            if (string.IsNullOrEmpty(name)) return false;
            name = name.Trim().ToUpperInvariant();
            int i = 0, col = 0;
            while (i < name.Length && name[i] >= 'A' && name[i] <= 'Z') { col = col * 26 + (name[i] - 'A' + 1); i++; }
            if (i == 0 || i == name.Length || !int.TryParse(name.Substring(i), out int row)) return false;
            cell = new Vector2Int(col - 1, row - 1);
            return IsInside(cell);
        }

        /// <summary>0 → A, 25 → Z, 26 → AA …</summary>
        public static string ColumnName(int index)
        {
            string s = "";
            index++;
            while (index > 0) { int r = (index - 1) % 26; s = (char)('A' + r) + s; index = (index - 1) / 26; }
            return s;
        }
    }
}
