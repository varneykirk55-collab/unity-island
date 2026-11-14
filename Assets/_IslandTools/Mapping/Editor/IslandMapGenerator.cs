// Editor-only, repeatable terrain -> map generator. READS the scene; never modifies or saves it.
//
// Menu:  Tools > Island > Mapping > Generate Maps (open scene)
// Batch: Unity -batchmode -quit -projectPath <proj> -executeMethod IslandTools.Mapping.Editor.IslandMapGenerator.BatchGenerate
//          -mapScene "<scene path>" -mapOut "<dir>" [-mapPx 4096] [-mapGrid 100] [-mapSea 45] [-mapContour 10]
//
// Outputs (in the output directory):
//   island_overhead.png      clean overhead map (no grid/labels)
//   island_devmap.png        developer map: grid, X/Z labels, north arrow, scale bar, contours, region ids, boundary
//   island_map_meta.json     IslandMapMetadata (space, elevation range, layers, sea level, notes)
//   island_height16.raw      16-bit little-endian heightmap normalised to [terrainMinY, terrainMaxY], row 0 = south
//   island_cells.csv         coordinate readout: one row per grid cell centre (world X/Z, ground Y, slope, layer, water)
//   island_structures.json   existing placed structures (renderer bounds) found under the island root
//
// Nothing is invented: every pixel comes from terrain height/splat/tree data or placed scene objects.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IslandTools.Mapping.Editor
{
    public class IslandMapSettings
    {
        public int pixels = 4096;                  // longest side of the output image
        public float gridMetres = 0;               // 0 = auto (about 10-20 cells across)
        public float seaLevel = float.NaN;         // NaN = detect from an object named "Ocean"/"Water"; else no water mask
        public float contourMetres = 10f;
        public string[] roadLayerKeywords = { "road", "path", "trail", "street" };
        public string structuresRootName = "Island_Environment";
        public string outDir = "MapOutput";
    }

    public static class IslandMapGenerator
    {
        [Serializable] class StructureRecord { public string name, prefab; public Vector3 center, size; }
        [Serializable] class StructureList { public List<StructureRecord> structures = new List<StructureRecord>(); }

        [MenuItem("Tools/Island/Mapping/Generate Maps (open scene)")]
        public static void GenerateFromMenu()
        {
            string dir = EditorUtility.OpenFolderPanel("Map output folder", "MapOutput", "");
            if (string.IsNullOrEmpty(dir)) return;
            Generate(new IslandMapSettings { outDir = dir });
        }

        public static void BatchGenerate()
        {
            var args = Environment.GetCommandLineArgs();
            string Arg(string n, string d) { int i = Array.IndexOf(args, n); return i >= 0 && i + 1 < args.Length ? args[i + 1] : d; }
            var s = new IslandMapSettings
            {
                outDir = Arg("-mapOut", "MapOutput"),
                pixels = int.Parse(Arg("-mapPx", "4096")),
                gridMetres = float.Parse(Arg("-mapGrid", "0")),
                contourMetres = float.Parse(Arg("-mapContour", "10")),
            };
            string sea = Arg("-mapSea", null); if (sea != null) s.seaLevel = float.Parse(sea);
            string scene = Arg("-mapScene", null);
            try
            {
                if (!string.IsNullOrEmpty(scene)) EditorSceneManager.OpenScene(scene, OpenSceneMode.Single); // opened, never saved
                Generate(s);
                EditorApplication.Exit(0);
            }
            catch (Exception e) { Debug.LogError("[IslandMap] FAILED: " + e); EditorApplication.Exit(1); }
        }

        public static IslandMapMetadata Generate(IslandMapSettings s)
        {
            var terrains = Terrain.activeTerrains;
            if (terrains == null || terrains.Length == 0) throw new InvalidOperationException("No active Terrain in the open scene. Open an island terrain scene first.");
            Directory.CreateDirectory(s.outDir);

            // ---- extent (union of all terrains, world space) ----
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            foreach (var t in terrains)
            {
                var p = t.transform.position; var sz = t.terrainData.size;
                minX = Mathf.Min(minX, p.x); minZ = Mathf.Min(minZ, p.z);
                maxX = Mathf.Max(maxX, p.x + sz.x); maxZ = Mathf.Max(maxZ, p.z + sz.z);
            }
            float sizeX = maxX - minX, sizeZ = maxZ - minZ;
            float ppm = Mathf.Min(s.pixels / Mathf.Max(sizeX, sizeZ), 8192f / Mathf.Max(sizeX, sizeZ));
            int W = Mathf.Max(16, Mathf.RoundToInt(sizeX * ppm)), H = Mathf.Max(16, Mathf.RoundToInt(sizeZ * ppm));
            var space = new IslandMapSpace(minX, minZ, sizeX, sizeZ, W, H);

            float grid = s.gridMetres > 0 ? s.gridMetres : AutoGrid(Mathf.Max(sizeX, sizeZ));
            var scene = SceneManager.GetActiveScene();
            var meta = new IslandMapMetadata
            {
                scenePath = scene.path, terrainObjectName = terrains[0].name, space = space, gridMetres = grid,
                generatedUtc = DateTime.UtcNow.ToString("o"),
                heightmapResolution = terrains[0].terrainData.heightmapResolution,
                alphamapResolution = terrains[0].terrainData.alphamapResolution,
            };
            if (terrains.Length > 1) meta.sourceNotes.Add("Multiple terrains merged: " + terrains.Length);

            // ---- sea level ----
            float sea = s.seaLevel;
            if (float.IsNaN(sea))
                foreach (var n in new[] { "Ocean", "Water", "Sea" })
                {
                    var go = GameObject.Find(n);
                    if (go != null) { sea = go.transform.position.y; meta.seaLevelDetected = true; meta.sourceNotes.Add("Sea level taken from scene object '" + n + "'"); break; }
                }
            bool hasSea = !float.IsNaN(sea);
            meta.hasSeaLevel = hasSea; meta.seaLevelY = hasSea ? sea : 0f;
            if (!hasSea) meta.sourceNotes.Add("No sea level supplied or detected: water mask omitted.");

            // ---- per-terrain cached data ----
            var caches = new List<TerrainCache>();
            foreach (var t in terrains) caches.Add(new TerrainCache(t));
            foreach (var l in caches[0].layerNames) meta.terrainLayers.Add(l);
            var layerColors = new Color32[caches[0].layerNames.Length];
            var isRoad = new bool[layerColors.Length];
            for (int i = 0; i < layerColors.Length; i++)
            {
                layerColors[i] = LayerColor(caches[0].layerNames[i]);
                foreach (var kw in s.roadLayerKeywords) if (caches[0].layerNames[i].ToLowerInvariant().Contains(kw)) isRoad[i] = true;
            }
            bool anyRoadLayer = Array.IndexOf(isRoad, true) >= 0;
            if (!anyRoadLayer) meta.sourceNotes.Add("No terrain layer matches road/path keywords: no roads drawn (none invented).");

            // ---- sample heights to find the real elevation range ----
            float zMin = float.MaxValue, zMax = float.MinValue;
            var hgt = new float[W * H];            // index: row 0 = NORTH (image order)
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    var w = space.MapToWorld(x + 0.5f, y + 0.5f);
                    float hv = Sample(caches, w.x, w.y, out _);
                    hgt[y * W + x] = hv;
                    if (!float.IsNaN(hv)) { zMin = Mathf.Min(zMin, hv); zMax = Mathf.Max(zMax, hv); }
                }
            meta.terrainMinY = zMin; meta.terrainMaxY = zMax;

            // ---- clean overhead ----
            var overhead = new Color32[W * H];
            var lightDir = new Vector3(-0.5f, 0.7f, -0.5f).normalized;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x; float hv = hgt[i];
                    Color32 c;
                    if (float.IsNaN(hv)) c = new Color32(0, 0, 0, 0);
                    else if (hasSea && hv < sea) { float d = Mathf.Clamp01((sea - hv) / 40f); c = Color32.Lerp(new Color32(70, 140, 190, 255), new Color32(15, 50, 110, 255), d); }
                    else
                    {
                        var w = space.MapToWorld(x + 0.5f, y + 0.5f);
                        Sample(caches, w.x, w.y, out var info);
                        int li = info.dominantLayer;
                        c = li >= 0 ? layerColors[Mathf.Min(li, layerColors.Length - 1)] : new Color32(128, 128, 128, 255);
                        if (li >= 0 && isRoad[Mathf.Min(li, isRoad.Length - 1)]) c = new Color32(150, 120, 80, 255);
                        float shade = Mathf.Clamp01(Vector3.Dot(info.normal, lightDir)) * 0.9f + 0.25f;
                        c = new Color32((byte)Mathf.Clamp(c.r * shade, 0, 255), (byte)Mathf.Clamp(c.g * shade, 0, 255), (byte)Mathf.Clamp(c.b * shade, 0, 255), 255);
                    }
                    overhead[(H - 1 - y) * W + x] = c;   // texture row 0 = bottom, so flip
                }
            // trees (from TerrainData.treeInstances only)
            int trees = 0;
            foreach (var tc in caches)
                foreach (var ti in tc.terrain.terrainData.treeInstances)
                {
                    var wp = tc.terrain.transform.position + Vector3.Scale(ti.position, tc.terrain.terrainData.size);
                    if (hasSea && wp.y < sea) continue;
                    var pm = space.WorldToMap(wp.x, wp.z); int px = (int)pm.x, py = (int)pm.y;
                    if (px < 0 || py < 0 || px >= W || py >= H) continue;
                    var o = overhead[(H - 1 - py) * W + px];
                    overhead[(H - 1 - py) * W + px] = new Color32((byte)(o.r * 0.55f), (byte)(Mathf.Min(255, o.g * 0.8f)), (byte)(o.b * 0.5f), 255);
                    trees++;
                }
            meta.treeInstanceCount = trees;
            if (trees == 0) meta.sourceNotes.Add("Terrain has no tree instances: no forest overlay.");

            // existing placed structures
            var structures = CollectStructures(s.structuresRootName, meta);
            foreach (var st in structures.structures)
                DrawRect(overhead, W, H, space, st.center, st.size, new Color32(235, 60, 200, 255), 1);

            WritePng(Path.Combine(s.outDir, "island_overhead.png"), overhead, W, H);

            // ---- developer map ----
            var dev = (Color32[])overhead.Clone();
            DrawDevOverlay(dev, W, H, space, grid, s.contourMetres, hgt, hasSea ? sea : float.NaN, zMin, zMax);
            WritePng(Path.Combine(s.outDir, "island_devmap.png"), dev, W, H);

            // ---- raw heightmap + cell readout + metadata ----
            WriteHeight16(Path.Combine(s.outDir, "island_height16.raw"), hgt, W, H, zMin, zMax);
            WriteCells(Path.Combine(s.outDir, "island_cells.csv"), caches, space, grid, hasSea ? sea : float.NaN);
            File.WriteAllText(Path.Combine(s.outDir, "island_structures.json"), JsonUtility.ToJson(structures, true));
            File.WriteAllText(Path.Combine(s.outDir, "island_map_meta.json"), JsonUtility.ToJson(meta, true));
            Debug.Log(string.Format("[IslandMap] Wrote maps to {0} ({1}x{2}px, {3:0.0}x{4:0.0} m, Y {5:0.0}..{6:0.0}, grid {7} m)", s.outDir, W, H, sizeX, sizeZ, zMin, zMax, grid));
            return meta;
        }

        // ---------------- sampling ----------------
        struct SampleInfo { public Vector3 normal; public int dominantLayer; public float slopeDeg; }

        class TerrainCache
        {
            public Terrain terrain; public float[,,] alpha; public string[] layerNames;
            public TerrainCache(Terrain t)
            {
                terrain = t; var td = t.terrainData;
                layerNames = new string[td.terrainLayers.Length];
                for (int i = 0; i < layerNames.Length; i++) layerNames[i] = td.terrainLayers[i] != null ? td.terrainLayers[i].name : "layer" + i;
                alpha = layerNames.Length > 0 ? td.GetAlphamaps(0, 0, td.alphamapResolution, td.alphamapResolution) : null;
            }
        }

        static float Sample(List<TerrainCache> caches, float wx, float wz, out SampleInfo info)
        {
            info = new SampleInfo { normal = Vector3.up, dominantLayer = -1 };
            foreach (var c in caches)
            {
                var t = c.terrain; var td = t.terrainData; var p = t.transform.position;
                float u = (wx - p.x) / td.size.x, v = (wz - p.z) / td.size.z;
                if (u < 0 || v < 0 || u > 1 || v > 1) continue;
                info.normal = td.GetInterpolatedNormal(u, v);
                info.slopeDeg = Vector3.Angle(info.normal, Vector3.up);
                if (c.alpha != null)
                {
                    int r = td.alphamapResolution, ax = Mathf.Clamp((int)(u * r), 0, r - 1), az = Mathf.Clamp((int)(v * r), 0, r - 1);
                    float best = -1;
                    for (int l = 0; l < c.alpha.GetLength(2); l++) if (c.alpha[az, ax, l] > best) { best = c.alpha[az, ax, l]; info.dominantLayer = l; }
                }
                return td.GetInterpolatedHeight(u, v) + p.y;
            }
            return float.NaN;
        }

        // Palette keyed on layer NAME (deterministic, no texture-readability requirement). Unknown -> neutral grey.
        static Color32 LayerColor(string n)
        {
            n = n.ToLowerInvariant();
            if (n.Contains("sand")) return new Color32(226, 208, 150, 255);
            if (n.Contains("grass")) return new Color32(110, 160, 70, 255);
            if (n.Contains("cliff") || n.Contains("rock")) return new Color32(125, 120, 115, 255);
            if (n.Contains("moss")) return new Color32(90, 125, 80, 255);
            if (n.Contains("dirt") || n.Contains("mud") || n.Contains("soil")) return new Color32(140, 105, 70, 255);
            if (n.Contains("snow")) return new Color32(240, 245, 250, 255);
            return new Color32(150, 150, 150, 255);
        }

        static float AutoGrid(float longest)
        {
            float raw = longest / 15f; float[] steps = { 10, 20, 25, 50, 100, 200, 250, 500, 1000, 2000 };
            foreach (var st in steps) if (st >= raw) return st;
            return 5000;
        }

        // ---------------- structures ----------------
        static StructureList CollectStructures(string rootName, IslandMapMetadata meta)
        {
            var list = new StructureList();
            var root = GameObject.Find(rootName);
            if (root == null) { meta.sourceNotes.Add("No '" + rootName + "' root in scene: no structures listed."); return list; }
            foreach (Transform child in root.transform)
            {
                var rs = child.GetComponentsInChildren<Renderer>();
                if (rs.Length == 0) continue;
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                string n = child.name; string ln = n.ToLowerInvariant();
                if (ln.Contains("ocean") || ln.Contains("water") || ln.Contains("light") || ln.Contains("tree")) continue;
                string src = PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject) is GameObject pg ? AssetDatabase.GetAssetPath(pg) : "";
                list.structures.Add(new StructureRecord { name = n, prefab = src, center = b.center, size = b.size });
            }
            meta.sourceNotes.Add("Structures = renderer bounds of direct children of '" + rootName + "' (excluding ocean/water/light/tree).");
            return list;
        }

        // ---------------- drawing ----------------
        static void Put(Color32[] px, int W, int H, int x, int y, Color32 c)
        { if (x >= 0 && y >= 0 && x < W && y < H) px[(H - 1 - y) * W + x] = c; }

        static void Blend(Color32[] px, int W, int H, int x, int y, Color32 c, float a)
        {
            if (x < 0 || y < 0 || x >= W || y >= H) return;
            int i = (H - 1 - y) * W + x; var o = px[i];
            px[i] = new Color32((byte)(o.r + (c.r - o.r) * a), (byte)(o.g + (c.g - o.g) * a), (byte)(o.b + (c.b - o.b) * a), 255);
        }

        static void Line(Color32[] px, int W, int H, int x0, int y0, int x1, int y1, Color32 c, float a)
        {
            int dx = Mathf.Abs(x1 - x0), dy = -Mathf.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
            while (true)
            {
                Blend(px, W, H, x0, y0, c, a);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err; if (e2 >= dy) { err += dy; x0 += sx; } if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        static void DrawRect(Color32[] px, int W, int H, IslandMapSpace sp, Vector3 center, Vector3 size, Color32 c, int th)
        {
            var a = sp.WorldToMap(center.x - size.x / 2, center.z - size.z / 2); var b = sp.WorldToMap(center.x + size.x / 2, center.z + size.z / 2);
            int x0 = (int)Mathf.Min(a.x, b.x), x1 = (int)Mathf.Max(a.x, b.x), y0 = (int)Mathf.Min(a.y, b.y), y1 = (int)Mathf.Max(a.y, b.y);
            for (int t = 0; t < th; t++)
            {
                Line(px, W, H, x0 - t, y0 - t, x1 + t, y0 - t, c, 1); Line(px, W, H, x0 - t, y1 + t, x1 + t, y1 + t, c, 1);
                Line(px, W, H, x0 - t, y0 - t, x0 - t, y1 + t, c, 1); Line(px, W, H, x1 + t, y0 - t, x1 + t, y1 + t, c, 1);
            }
        }

        static void DrawDevOverlay(Color32[] px, int W, int H, IslandMapSpace sp, float grid, float contour, float[] hgt, float sea, float zMin, float zMax)
        {
            int fs = Mathf.Max(1, Mathf.RoundToInt(Mathf.Min(W, H) / 700f));   // font scale
            var white = new Color32(255, 255, 255, 255); var black = new Color32(0, 0, 0, 255);
            // contours
            if (contour > 0)
                for (int y = 0; y < H - 1; y++)
                    for (int x = 0; x < W - 1; x++)
                    {
                        float a = hgt[y * W + x], b = hgt[y * W + x + 1], c = hgt[(y + 1) * W + x];
                        if (float.IsNaN(a) || float.IsNaN(b) || float.IsNaN(c)) continue;
                        bool major = Mathf.FloorToInt(a / (contour * 5)) != Mathf.FloorToInt(b / (contour * 5)) || Mathf.FloorToInt(a / (contour * 5)) != Mathf.FloorToInt(c / (contour * 5));
                        if (Mathf.FloorToInt(a / contour) != Mathf.FloorToInt(b / contour) || Mathf.FloorToInt(a / contour) != Mathf.FloorToInt(c / contour))
                            Blend(px, W, H, x, y, new Color32(80, 50, 20, 255), major ? 0.85f : 0.4f);
                    }
            // coast line (sea level crossing)
            if (!float.IsNaN(sea))
                for (int y = 0; y < H - 1; y++) for (int x = 0; x < W - 1; x++)
                {
                    float a = hgt[y * W + x], b = hgt[y * W + x + 1], c = hgt[(y + 1) * W + x];
                    if (float.IsNaN(a) || float.IsNaN(b) || float.IsNaN(c)) continue;
                    if ((a < sea) != (b < sea) || (a < sea) != (c < sea)) Put(px, W, H, x, y, new Color32(255, 255, 255, 255));
                }
            // grid lines (world multiples of `grid`, anchored at world 0 so they match Unity coordinates)
            int majorEvery = 5;
            for (float wx = Mathf.Ceil(sp.OriginX / grid) * grid; wx <= sp.OriginX + sp.SizeX + 0.001f; wx += grid)
            {
                int px0 = Mathf.RoundToInt(sp.WorldToMap(wx, sp.OriginZ).x); bool maj = Mathf.RoundToInt(wx / grid) % majorEvery == 0;
                for (int y = 0; y < H; y++) Blend(px, W, H, px0, y, black, maj ? 0.8f : 0.4f);
                if (maj) for (int y = 0; y < H; y++) Blend(px, W, H, px0 + 1, y, black, 0.5f);
            }
            for (float wz = Mathf.Ceil(sp.OriginZ / grid) * grid; wz <= sp.OriginZ + sp.SizeZ + 0.001f; wz += grid)
            {
                int py0 = Mathf.RoundToInt(sp.WorldToMap(sp.OriginX, wz).y); bool maj = Mathf.RoundToInt(wz / grid) % majorEvery == 0;
                for (int x = 0; x < W; x++) Blend(px, W, H, x, py0, black, maj ? 0.8f : 0.4f);
                if (maj) for (int x = 0; x < W; x++) Blend(px, W, H, x, py0 + 1, black, 0.5f);
            }
            // axis labels (world X along top & bottom edge, world Z along left & right) at every major line; plus all lines if room
            int labelEvery = (grid * sp.PixelsPerMetreX < 14 * fs * 4) ? majorEvery : 1;
            for (float wx = Mathf.Ceil(sp.OriginX / grid) * grid; wx <= sp.OriginX + sp.SizeX + 0.001f; wx += grid)
            {
                if (Mathf.RoundToInt(wx / grid) % labelEvery != 0) continue;
                string t = Mathf.RoundToInt(wx).ToString(); int x = Mathf.RoundToInt(sp.WorldToMap(wx, 0).x) - IslandMapFont.Width(t, fs) / 2;
                IslandMapFont.Draw(px, W, H, x, 3 * fs, t, fs, white, true, black);
                IslandMapFont.Draw(px, W, H, x, H - 8 * fs, t, fs, white, true, black);
            }
            for (float wz = Mathf.Ceil(sp.OriginZ / grid) * grid; wz <= sp.OriginZ + sp.SizeZ + 0.001f; wz += grid)
            {
                if (Mathf.RoundToInt(wz / grid) % labelEvery != 0) continue;
                string t = Mathf.RoundToInt(wz).ToString(); int y = Mathf.RoundToInt(sp.WorldToMap(0, wz).y) - 2 * fs;
                IslandMapFont.Draw(px, W, H, 3 * fs, y, t, fs, white, true, black);
                IslandMapFont.Draw(px, W, H, W - IslandMapFont.Width(t, fs) - 3 * fs, y, t, fs, white, true, black);
            }
            IslandMapFont.Draw(px, W, H, W / 2 - 6 * fs, 11 * fs, "X", fs * 2, new Color32(255, 230, 80, 255), true, black);
            IslandMapFont.Draw(px, W, H, 14 * fs, H / 2 - 5 * fs, "Z", fs * 2, new Color32(255, 230, 80, 255), true, black);
            // region ids = grid cell "col-row" (column index along +X, row index along +Z, both = floor(world / grid)) at cell centres when room allows
            if (grid * sp.PixelsPerMetreX >= 18 * fs * 4)
                for (float wx = Mathf.Floor(sp.OriginX / grid) * grid; wx < sp.OriginX + sp.SizeX; wx += grid)
                    for (float wz = Mathf.Floor(sp.OriginZ / grid) * grid; wz < sp.OriginZ + sp.SizeZ; wz += grid)
                    {
                        float cx = wx + grid / 2, cz = wz + grid / 2; if (!sp.ContainsWorld(cx, cz)) continue;
                        string id = CellId(wx, wz, grid); var m = sp.WorldToMap(cx, cz);
                        IslandMapFont.Draw(px, W, H, (int)m.x - IslandMapFont.Width(id, fs) / 2, (int)m.y - 2 * fs, id, fs, new Color32(255, 255, 255, 200), true, black);
                    }
            // terrain boundary
            var bc = new Color32(255, 40, 40, 255);
            for (int t = 0; t < 2 * fs; t++)
            { Line(px, W, H, t, t, W - 1 - t, t, bc, 1); Line(px, W, H, t, H - 1 - t, W - 1 - t, H - 1 - t, bc, 1); Line(px, W, H, t, t, t, H - 1 - t, bc, 1); Line(px, W, H, W - 1 - t, t, W - 1 - t, H - 1 - t, bc, 1); }
            // north arrow (world +Z = up) bottom-left-ish
            int ax = 40 * fs + 30, ay = 60 * fs + 40, L = 22 * fs;
            for (int t = 0; t < fs; t++) { Line(px, W, H, ax + t, ay + L, ax + t, ay - L, white, 1); }
            Line(px, W, H, ax, ay - L, ax - 5 * fs, ay - L + 9 * fs, white, 1); Line(px, W, H, ax, ay - L, ax + 5 * fs, ay - L + 9 * fs, white, 1);
            IslandMapFont.Draw(px, W, H, ax - 2 * fs, ay - L - 8 * fs, "N", fs * 2, white, true, black);
            // scale bar: largest of 100/250/500/1000/... m that fits in 1/5 of the image width
            float[] bars = { 10, 20, 50, 100, 250, 500, 1000, 2000, 5000 }; float barM = bars[0];
            foreach (var b in bars) if (b * sp.PixelsPerMetreX <= W / 5f) barM = b;
            int bx = 30 * fs, by = H - 20 * fs, bl = Mathf.RoundToInt(barM * sp.PixelsPerMetreX);
            for (int t = -fs; t <= fs; t++) Line(px, W, H, bx, by + t, bx + bl, by + t, white, 1);
            Line(px, W, H, bx, by - 4 * fs, bx, by + 4 * fs, white, 1); Line(px, W, H, bx + bl, by - 4 * fs, bx + bl, by + 4 * fs, white, 1);
            IslandMapFont.Draw(px, W, H, bx, by - 10 * fs, barM + " m", fs, white, true, black);
            // elevation key (text only)
            IslandMapFont.Draw(px, W, H, W - 140 * fs, H - 20 * fs, "Z " + Mathf.RoundToInt(zMin) + ".." + Mathf.RoundToInt(zMax), fs, white, true, black);
        }

        public static string CellId(float cellOriginX, float cellOriginZ, float grid)
            => Mathf.FloorToInt(cellOriginX / grid + 0.0001f).ToString("00") + "-" + Mathf.FloorToInt(cellOriginZ / grid + 0.0001f).ToString("00");

        // ---------------- file output ----------------
        static void WritePng(string path, Color32[] px, int W, int H)
        {
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            tex.SetPixels32(px); tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        static void WriteHeight16(string path, float[] hgt, int W, int H, float zMin, float zMax)
        {
            var bytes = new byte[W * H * 2]; float range = Mathf.Max(0.0001f, zMax - zMin); int k = 0;
            for (int y = H - 1; y >= 0; y--)   // row 0 = south edge (matches Unity heightmap order)
                for (int x = 0; x < W; x++)
                {
                    float v = hgt[y * W + x]; ushort u = float.IsNaN(v) ? (ushort)0 : (ushort)Mathf.Clamp(Mathf.RoundToInt((v - zMin) / range * 65535f), 0, 65535);
                    bytes[k++] = (byte)(u & 0xFF); bytes[k++] = (byte)(u >> 8);
                }
            File.WriteAllBytes(path, bytes);
        }

        static void WriteCells(string path, List<TerrainCache> caches, IslandMapSpace sp, float grid, float sea)
        {
            var sb = new StringBuilder("cell_id,centre_x,centre_z,ground_y,slope_deg,dominant_layer,is_water\n");
            for (float wx = Mathf.Floor(sp.OriginX / grid) * grid; wx < sp.OriginX + sp.SizeX; wx += grid)
                for (float wz = Mathf.Floor(sp.OriginZ / grid) * grid; wz < sp.OriginZ + sp.SizeZ; wz += grid)
                {
                    float cx = wx + grid / 2, cz = wz + grid / 2; float h = Sample(caches, cx, cz, out var info);
                    if (float.IsNaN(h)) continue;
                    string layer = info.dominantLayer >= 0 ? caches[0].layerNames[Mathf.Min(info.dominantLayer, caches[0].layerNames.Length - 1)] : "";
                    bool water = !float.IsNaN(sea) && h < sea;
                    sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "{0},{1:0.##},{2:0.##},{3:0.##},{4:0.#},{5},{6}\n", CellId(wx, wz, grid), cx, cz, h, info.slopeDeg, layer, water ? 1 : 0);
                }
            File.WriteAllText(path, sb.ToString());
        }
    }
}
