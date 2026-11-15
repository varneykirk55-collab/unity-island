using System;
using System.Collections.Generic;
using UnityEngine;

namespace IslandMap
{
    /// <summary>
    /// Geographic data extracted from the island scene by the map generator (Tools/Island/Map/Generate Maps).
    /// Pure data: nothing references it from gameplay, so it is not included in builds unless a future system
    /// (e.g. the player tactical map) references it.
    /// </summary>
    [CreateAssetMenu(menuName = "Island/Map Data", fileName = "IslandMapData")]
    public class IslandMapData : ScriptableObject
    {
        [Header("Source")]
        public string SourceScene;
        public string GeneratedUtc;
        public int GeneratorVersion;

        [Header("Projections (one per exported image)")]
        public MapProjection Overhead;
        public MapProjection DeveloperMap;
        public MapProjection Analysis;
        public MapGrid Grid;

        [Header("Terrain")]
        public List<TerrainEntry> Terrains = new List<TerrainEntry>();
        public float MinHeight;
        public float MaxHeight;
        [Tooltip("Y of the ocean surface found in the scene; NaN if there is no water object.")]
        public float SeaLevel = float.NaN;

        [Header("Height grid (world Y, sampled over the mapped area)")]
        public int HeightGridResolution;
        [Tooltip("Row-major from the SOUTH-west corner, 16-bit quantised between MinHeight and MaxHeight.")]
        [HideInInspector] public byte[] HeightGrid16;

        [Header("Existing content found in the scene (factual, not planned)")]
        public List<MapFeature> Features = new List<MapFeature>();

        [Serializable]
        public class TerrainEntry
        {
            public string Name;
            public string DataAsset;
            public Vector3 Position;
            public Vector3 Size;
            public int HeightmapResolution;
            public string[] Layers;
            public int TreeCount;
        }

        [Serializable]
        public class MapFeature
        {
            public int Number;
            public string Name;
            [Tooltip("Hierarchy path in the source scene.")]
            public string Path;
            public string Category;
            public Vector3 Position;
            [Tooltip("World X/Z bounds of the object's renderers.")]
            public Rect Footprint;
            public float TopY;
        }

        /// <summary>World height (Y) at a world X/Z from the stored grid (bilinear). NaN outside the map.</summary>
        public float SampleHeight(Vector3 world) => SampleHeight(new Vector2(world.x, world.z));
        public float SampleHeight(Vector2 worldXZ)
        {
            int n = HeightGridResolution;
            if (n < 2 || HeightGrid16 == null || HeightGrid16.Length < n * n * 2) return float.NaN;
            var uv = Overhead.WorldToNormalized(worldXZ);
            if (uv.x < 0 || uv.y < 0 || uv.x > 1 || uv.y > 1) return float.NaN;
            float fx = uv.x * (n - 1), fz = uv.y * (n - 1);
            int x0 = Mathf.Min((int)fx, n - 2), z0 = Mathf.Min((int)fz, n - 2);
            float tx = fx - x0, tz = fz - z0;
            float a = H(x0, z0), b = H(x0 + 1, z0), c = H(x0, z0 + 1), d = H(x0 + 1, z0 + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz);
        }

        float H(int x, int z)
        {
            int i = (z * HeightGridResolution + x) * 2;
            int q = HeightGrid16[i] | (HeightGrid16[i + 1] << 8);
            return Mathf.Lerp(MinHeight, MaxHeight, q / 65535f);
        }

        public void SetHeightGrid(float[,] heightsSouthWestRowMajor)
        {
            int n = heightsSouthWestRowMajor.GetLength(0);
            HeightGridResolution = n;
            HeightGrid16 = new byte[n * n * 2];
            float range = Mathf.Max(1e-4f, MaxHeight - MinHeight);
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    int q = Mathf.Clamp(Mathf.RoundToInt((heightsSouthWestRowMajor[z, x] - MinHeight) / range * 65535f), 0, 65535);
                    int i = (z * n + x) * 2;
                    HeightGrid16[i] = (byte)(q & 0xFF);
                    HeightGrid16[i + 1] = (byte)(q >> 8);
                }
        }

        /// <summary>Human-readable location: "X 812.0  Z 1043.5  (Y 61.2)  cell F4".</summary>
        public string Describe(Vector3 world)
        {
            float y = SampleHeight(world);
            return $"X {world.x:F1}  Z {world.z:F1}" + (float.IsNaN(y) ? "" : $"  (ground Y {y:F1})") + $"  cell {Grid.CellName(world)}";
        }
    }
}
