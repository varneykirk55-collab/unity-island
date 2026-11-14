// Serializable description of one generated map (written next to the PNGs as JSON).
using System;
using System.Collections.Generic;

namespace IslandTools.Mapping
{
    [Serializable]
    public class IslandMapMetadata
    {
        public int schemaVersion = 1;
        public string scenePath;
        public string terrainObjectName;
        public IslandMapSpace space;
        public float terrainMinY, terrainMaxY;     // measured elevation range, world Y metres
        public bool hasSeaLevel;
        public float seaLevelY;                    // waterline used for the water mask (valid only if hasSeaLevel)
        public bool seaLevelDetected;
        public float gridMetres;
        public string northConvention = "+Z";
        public int heightmapResolution, alphamapResolution;
        public List<string> terrainLayers = new List<string>();
        public int treeInstanceCount;
        public List<string> sourceNotes = new List<string>();
        public string generatedUtc;
    }
}
