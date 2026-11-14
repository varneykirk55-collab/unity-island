// Data schema for FUTURE map markers (owner-approved construction locations).
// Phase 1 ships the schema only: no markers are created, none are placed in scenes.
// Markers are stored as JSON next to the map data so they can be edited externally.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace IslandTools.Mapping
{
    public enum ConstructionStatus { Proposed, Approved, Assigned, InProgress, Built, Rejected }

    [Serializable]
    public class IslandMapMarker
    {
        public string id;                    // stable unique id
        public string locationName;
        public Vector3 worldPosition;        // Unity world metres (x,z authoritative; y optional)
        public List<Vector2> zoneBoundary = new List<Vector2>(); // polygon, world (x,z), CCW; empty = point marker
        public List<string> assetRequirements = new List<string>();
        public string purpose;
        public string assignedBranch;        // Git branch of the construction assignment
        public ConstructionStatus status = ConstructionStatus.Proposed;
    }

    [Serializable]
    public class IslandMapMarkerSet
    {
        public int schemaVersion = 1;
        public List<IslandMapMarker> markers = new List<IslandMapMarker>();

        public static IslandMapMarkerSet FromJson(string json) => JsonUtility.FromJson<IslandMapMarkerSet>(json);
        public string ToJson() => JsonUtility.ToJson(this, true);
    }
}
