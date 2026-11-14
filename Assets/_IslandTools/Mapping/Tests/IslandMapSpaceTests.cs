// EditMode tests for the coordinate conversion. NOT yet run in a Unity Editor (see Docs/Mapping/ValidationReport.md).
using NUnit.Framework;
using UnityEngine;

namespace IslandTools.Mapping.Tests
{
    public class IslandMapSpaceTests
    {
        static IslandMapSpace Space() => new IslandMapSpace(-100f, 50f, 2000f, 1000f, 4000, 2000);

        [Test] public void SouthWestCorner_IsBottomLeftPixel()
        { var p = Space().WorldToMap(-100f, 50f); Assert.AreEqual(0f, p.x, 1e-4f); Assert.AreEqual(2000f, p.y, 1e-4f); }

        [Test] public void NorthEastCorner_IsTopRightPixel()
        { var p = Space().WorldToMap(1900f, 1050f); Assert.AreEqual(4000f, p.x, 1e-4f); Assert.AreEqual(0f, p.y, 1e-4f); }

        [Test] public void NorthIsUp_PlusZMovesTowardSmallerPixelY()
        { var s = Space(); Assert.Less(s.WorldToMap(0, 600).y, s.WorldToMap(0, 100).y); }

        [Test] public void RoundTrip_WorldMapWorld()
        {
            var s = Space();
            foreach (var w in new[] { new Vector2(0, 0), new Vector2(1064, 1032), new Vector2(-99.5f, 1049.9f), new Vector2(1568, 608) })
            { var m = s.WorldToMap(w.x, w.y); var r = s.MapToWorld(m); Assert.AreEqual(w.x, r.x, 1e-3f); Assert.AreEqual(w.y, r.y, 1e-3f); }
        }

        [Test] public void RoundTrip_MapWorldMap()
        { var s = Space(); var w = s.MapToWorld(1234.5f, 678.25f); var m = s.WorldToMap(w.x, w.y); Assert.AreEqual(1234.5f, m.x, 1e-3f); Assert.AreEqual(678.25f, m.y, 1e-3f); }

        [Test] public void Contains_RespectsBounds()
        { var s = Space(); Assert.IsTrue(s.ContainsWorld(0, 100)); Assert.IsFalse(s.ContainsWorld(-101, 100)); Assert.IsFalse(s.ContainsWorld(0, 1051)); }

        [Test] public void MarkerSet_JsonRoundTrip()
        {
            var set = new IslandMapMarkerSet(); set.markers.Add(new IslandMapMarker { id = "t1", locationName = "test", worldPosition = new Vector3(1, 2, 3), status = ConstructionStatus.Approved });
            var back = IslandMapMarkerSet.FromJson(set.ToJson());
            Assert.AreEqual(1, back.markers.Count); Assert.AreEqual("t1", back.markers[0].id); Assert.AreEqual(ConstructionStatus.Approved, back.markers[0].status);
        }
    }
}
