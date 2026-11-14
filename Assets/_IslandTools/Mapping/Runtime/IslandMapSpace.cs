// Pure coordinate math linking Unity world space (X/Z, metres) to map-image pixels.
// Runtime-safe and data-only so the future player map can reuse it. No gameplay dependencies.
//
// Conventions (see Docs/Mapping/CoordinateSystem.md):
//   * World +X = map right (East by convention), world +Z = map up (North by convention).
//   * Pixel (0,0) is the TOP-LEFT of the image; pixel Y grows downward.
//   * The map covers the rectangle [OriginX, OriginX+SizeX] x [OriginZ, OriginZ+SizeZ].
//   * Pixel coordinates refer to the continuous image plane: x in [0,Width], y in [0,Height].
//     Pixel centres are at +0.5.
using System;
using UnityEngine;

namespace IslandTools.Mapping
{
    [Serializable]
    public struct IslandMapSpace
    {
        public float OriginX, OriginZ;   // world position of the map's south-west corner
        public float SizeX, SizeZ;       // world extent in metres
        public int Width, Height;        // image size in pixels

        public IslandMapSpace(float originX, float originZ, float sizeX, float sizeZ, int width, int height)
        {
            OriginX = originX; OriginZ = originZ; SizeX = sizeX; SizeZ = sizeZ; Width = width; Height = height;
        }

        public float PixelsPerMetreX => Width / SizeX;
        public float PixelsPerMetreZ => Height / SizeZ;

        /// <summary>World (x,z) metres -> continuous pixel position (origin top-left).</summary>
        public Vector2 WorldToMap(float worldX, float worldZ)
        {
            float px = (worldX - OriginX) / SizeX * Width;
            float py = Height - (worldZ - OriginZ) / SizeZ * Height;
            return new Vector2(px, py);
        }

        public Vector2 WorldToMap(Vector3 world) => WorldToMap(world.x, world.z);

        /// <summary>Continuous pixel position -> world (x,z) metres, returned as Vector2(x,z).</summary>
        public Vector2 MapToWorld(float px, float py)
        {
            float wx = OriginX + px / Width * SizeX;
            float wz = OriginZ + (Height - py) / Height * SizeZ;
            return new Vector2(wx, wz);
        }

        public Vector2 MapToWorld(Vector2 pixel) => MapToWorld(pixel.x, pixel.y);

        /// <summary>World (x,z) -> normalised 0..1 UV (u right, v up), as a future map shader would want.</summary>
        public Vector2 WorldToUV(float worldX, float worldZ)
            => new Vector2((worldX - OriginX) / SizeX, (worldZ - OriginZ) / SizeZ);

        public bool ContainsWorld(float worldX, float worldZ)
            => worldX >= OriginX && worldX <= OriginX + SizeX && worldZ >= OriginZ && worldZ <= OriginZ + SizeZ;
    }
}
