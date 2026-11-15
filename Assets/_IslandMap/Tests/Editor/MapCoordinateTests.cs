using System.IO;
using IslandMap.Editor;
using NUnit.Framework;
using UnityEngine;

namespace IslandMap.Tests
{
    public class MapCoordinateTests
    {
        static readonly Rect Island = new Rect(0, 0, 2500, 2500);

        [Test]
        public void Corners_MapToImageCorners_NorthUp()
        {
            var p = MapProjection.Create(Island, new Vector2Int(4096, 4096));
            Assert.That((p.WorldToPixel(new Vector2(0, 2500)) - Vector2.zero).magnitude, Is.LessThan(1e-3f), "NW corner -> top-left");
            Assert.That((p.WorldToPixel(new Vector2(2500, 0)) - new Vector2(4096, 4096)).magnitude, Is.LessThan(1e-3f), "SE corner -> bottom-right");
            Assert.That(p.WorldToPixel(new Vector2(100, 2000)).y, Is.LessThan(p.WorldToPixel(new Vector2(100, 1000)).y), "north (+Z) is up");
            Assert.That(p.WorldToPixel(new Vector2(2000, 100)).x, Is.GreaterThan(p.WorldToPixel(new Vector2(1000, 100)).x), "east (+X) is right");
        }

        [Test]
        public void RoundTrip_WithMargins()
        {
            var p = MapProjection.Create(new Rect(-130, 70, 1000, 800), new Vector2Int(2000, 1600), new Vector2Int(300, 330), new Vector2Int(3840, 2230));
            var rng = new System.Random(1);
            for (int i = 0; i < 1000; i++)
            {
                var w = new Vector2(-130 + (float)rng.NextDouble() * 1000, 70 + (float)rng.NextDouble() * 800);
                Assert.That((p.PixelToWorldXZ(p.WorldToPixel(w)) - w).magnitude, Is.LessThan(1e-3f));
            }
            Assert.That((p.WorldToPixel(new Vector2(-130, 870)) - new Vector2(300, 330)).magnitude, Is.LessThan(1e-3f), "map rect offset");
            Assert.That(p.ContainsPixel(new Vector2(299, 400)), Is.False, "margin is outside the map");
            Assert.That(p.ContainsPixel(new Vector2(301, 400)), Is.True);
        }

        [Test]
        public void PixelCentre_Formula_MatchesDocumentation()
        {
            // README: X = xMin + px * m/px, Z = zMax - py * m/px (clean map); pixel (i, j) centre = (i + 0.5, j + 0.5)
            var p = MapProjection.Create(Island, new Vector2Int(4096, 4096));
            float mpp = 2500f / 4096f;
            var w = p.PixelToWorldXZ(new Vector2(1000.5f, 2000.5f));
            Assert.AreEqual(1000.5f * mpp, w.x, 1e-3f);
            Assert.AreEqual(2500f - 2000.5f * mpp, w.y, 1e-3f);
            Assert.AreEqual(mpp, p.MetresPerPixel, 1e-6f);
        }

        [Test]
        public void Grid_NamesCells_FromNorthWest()
        {
            var g = MapGrid.Create(Island, 250);
            Assert.AreEqual(10, g.Columns); Assert.AreEqual(10, g.Rows);
            Assert.AreEqual(new Vector2(0, 2500), g.NorthWestCorner);
            Assert.AreEqual("A1", g.CellName(new Vector3(10, 0, 2490)));
            Assert.AreEqual("J10", g.CellName(new Vector3(2490, 0, 10)));
            Assert.AreEqual("C4", g.CellName(new Vector3(600, 0, 1600)));
            Assert.AreEqual("-", g.CellName(new Vector3(-5, 0, 100)));
            Assert.AreEqual(new Rect(500, 1500, 250, 250), g.CellRect(new Vector2Int(2, 3)));
            Assert.IsTrue(g.TryParseCell("c4", out var c)); Assert.AreEqual(new Vector2Int(2, 3), c);
            Assert.IsFalse(g.TryParseCell("K1", out _));
            Assert.AreEqual("AA", MapGrid.ColumnName(26));
            Assert.AreEqual("Z", MapGrid.ColumnName(25));
        }

        [Test]
        public void Grid_LinesSitOnRoundWorldCoordinates()
        {
            var g = MapGrid.Create(new Rect(-130, 70, 1000, 1000), 250);
            Assert.AreEqual(new Vector2(-250, 1250), g.NorthWestCorner);
            Assert.AreEqual(5, g.Columns); Assert.AreEqual(5, g.Rows);
        }

        [Test]
        public void TopDownCamera_AgreesWithProjection()
        {
            var go = new GameObject("cam");
            try
            {
                var cam = go.AddComponent<Camera>();
                var tile = new Rect(625, 1250, 625, 625);
                IslandMapGenerator.ConfigureTopDownCamera(cam, tile, 410, 15);
                var p = MapProjection.Create(tile, new Vector2Int(1024, 1024));
                var rng = new System.Random(3);
                for (int i = 0; i < 200; i++)
                {
                    var w = new Vector3(625 + (float)rng.NextDouble() * 625, 15 + (float)rng.NextDouble() * 395, 1250 + (float)rng.NextDouble() * 625);
                    var vp = cam.WorldToViewportPoint(w);
                    var n = p.WorldToNormalized(w);
                    Assert.AreEqual(n.x, vp.x, 1e-4f); Assert.AreEqual(n.y, vp.y, 1e-4f);
                    Assert.That(vp.z, Is.GreaterThan(cam.nearClipPlane).And.LessThan(cam.farClipPlane), "inside clip range");
                }
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void HeightGrid_EncodesAndInterpolates()
        {
            var d = ScriptableObject.CreateInstance<IslandMapData>();
            try
            {
                d.Overhead = MapProjection.Create(Island, new Vector2Int(4096, 4096));
                d.MinHeight = 10; d.MaxHeight = 410;
                int n = 65; var g = new float[n, n];
                float Plane(float x, float z) => 10 + x * 0.1f + z * 0.06f;
                for (int z = 0; z < n; z++) for (int x = 0; x < n; x++) g[z, x] = Plane(x * 2500f / (n - 1), z * 2500f / (n - 1));
                d.SetHeightGrid(g);
                Assert.AreEqual(Plane(1234, 777), d.SampleHeight(new Vector2(1234, 777)), 0.02f);
                Assert.AreEqual(Plane(0, 0), d.SampleHeight(new Vector2(0, 0)), 0.02f);
                Assert.AreEqual(Plane(2500, 2500), d.SampleHeight(new Vector2(2500, 2500)), 0.02f);
                Assert.IsNaN(d.SampleHeight(new Vector2(-1, 5)));
            }
            finally { Object.DestroyImmediate(d); }
        }

        [Test]
        public void Markers_JsonRoundTrip_AndValidation()
        {
            var set = new MapMarkerSet();
            set.Markers.Add(new MapMarker { Id = "a", Name = "Test A", Position = new Vector3(100, 0, 200), Status = "Approved", AssignedBranch = "build/a",
                Zone = { new Vector2(90, 190), new Vector2(110, 190), new Vector2(110, 210), new Vector2(90, 210) }, AssetRequirements = { "Pagoda" } });
            set.Markers.Add(new MapMarker { Id = "a", Name = "Dup", Position = new Vector3(5000, 0, 0), Status = "Maybe" });
            var back = MapMarkerSet.FromJson(set.ToJson());
            Assert.AreEqual(2, back.Markers.Count);
            Assert.AreEqual("build/a", back.Markers[0].AssignedBranch);
            Assert.AreEqual(4, back.Markers[0].Zone.Count);
            Assert.IsTrue(back.Markers[0].ZoneContains(new Vector2(100, 200)));
            Assert.IsFalse(back.Markers[0].ZoneContains(new Vector2(120, 200)));
            var issues = back.Validate(MapProjection.Create(Island, new Vector2Int(4096, 4096)));
            Assert.That(issues, Has.Some.Contains("duplicate Id"));
            Assert.That(issues, Has.Some.Contains("unknown Status"));
            Assert.That(issues, Has.Some.Contains("outside the mapped area"));
            Assert.AreEqual(3, issues.Count);
        }

        // ---- checks against the generated outputs (skipped until the generator has been run)

        static Vector2Int PngSize(string path)
        {
            var b = File.ReadAllBytes(path);
            int Be(int o) => (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3];
            return new Vector2Int(Be(16), Be(20));
        }

        [Test]
        public void Generated_ImagesMatchStoredProjections()
        {
            var d = UnityEditor.AssetDatabase.LoadAssetAtPath<IslandMapData>(IslandMapGenerator.DataAssetPath);
            if (!d) Assert.Ignore("Map not generated yet");
            string dir = IslandMapGenerator.DefaultOut;
            Assert.AreEqual(d.Overhead.ImageSize, PngSize(Path.Combine(dir, IslandMapGenerator.OverheadFile)), "overhead");
            Assert.AreEqual(d.DeveloperMap.ImageSize, PngSize(Path.Combine(dir, IslandMapGenerator.DevMapFile)), "developer");
            Assert.AreEqual(d.Analysis.ImageSize, PngSize(Path.Combine(dir, IslandMapGenerator.AnalysisFile)), "analysis");
            Assert.AreEqual(d.Overhead.WorldRect, d.DeveloperMap.WorldRect);
            Assert.That(d.Terrains, Is.Not.Empty);
            var t = d.Terrains[0];
            Assert.AreEqual(new Rect(t.Position.x, t.Position.z, t.Size.x, t.Size.z), d.Overhead.WorldRect, "single terrain = mapped area");
            foreach (var f in d.Features) Assert.IsTrue(d.Overhead.ContainsWorld(f.Position), f.Name + " inside map");
            Assert.That(d.MaxHeight, Is.GreaterThan(d.MinHeight));
        }
    }
}
