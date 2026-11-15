// Editor-only map generator. Opens the island scene READ-ONLY (never saved), then:
//  1. renders the real scene straight down with an orthographic camera (tiled, supersampled) -> island_overhead.png
//  2. samples the terrain heightmap for relief, contours, coastline, spot heights and flat-ground statistics
//  3. composes the developer map (grid, world coordinates, legend, existing structures) -> island_devmap.png
//  4. composes a terrain analysis map (elevation, slope-based flat ground, trees, water) -> island_terrain_analysis.png
//  5. writes island_map.json (projection, grid, features, per-cell statistics) and IslandMapData.asset
//  6. validates alignment by rendering marker cubes at known world positions and locating them in the image
// Menu:      Tools > Island > Map > Generate Maps
// Batchmode: Unity.exe -batchmode -quit -projectPath <proj> -executeMethod IslandMap.Editor.IslandMapGenerator.RunBatch
//            optional: -mapScene <path> -mapOut <dir> -mapPixels 4096 -mapCell 250
//            (needs a GPU: do NOT pass -nographics)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IslandMap.Editor
{
    public static class IslandMapGenerator
    {
        public const int Version = 1;
        public const string DefaultScene = "Assets/Free Island Collection/Scenes/Scene 1.unity";
        public const string DefaultOut = "Docs/IslandMap";
        public const string DataAssetPath = "Assets/_IslandMap/Data/IslandMapData.asset";
        public const string OverheadFile = "island_overhead.png";
        public const string DevMapFile = "island_devmap.png";
        public const string AnalysisFile = "island_terrain_analysis.png";
        public const string JsonFile = "island_map.json";
        public const string MarkersFile = "markers.json";
        public const string LogFile = "generation_log.txt";

        [Serializable]
        public class Settings
        {
            public string ScenePath = DefaultScene;
            public string OutDir = DefaultOut;
            public int MapPixels = 4096;          // pixels across the mapped area (X)
            public int Tiles = 4;                 // render tiles per axis
            public int Supersample = 2;           // render at N x, box-filter down
            public float CellSize = 250f;         // labelled grid (metres)
            public float SubCellSize = 50f;       // fine grid (metres)
            public float ContourInterval = 10f;
            public float IndexContourInterval = 50f;
            public float HillshadeStrength = 0.3f;
            public int HeightGridResolution = 513;
            public float FlatSlopeDegrees = 12f;
            public float StructureClearance = 15f;
            public bool WriteDataAsset = true;
        }

        // ------------------------------------------------------------------ entry points
        [MenuItem("Tools/Island/Map/Generate Maps (Scene 1)")]
        static void Menu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var open = Enumerable.Range(0, SceneManager.sceneCount).Select(i => SceneManager.GetSceneAt(i).path).Where(p => !string.IsNullOrEmpty(p)).ToList();
            try
            {
                EditorUtility.DisplayProgressBar("Island map", "Generating maps (this takes a few minutes)…", 0.5f);
                Generate(new Settings());
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (open.Count > 0)
                {
                    EditorSceneManager.OpenScene(open[0], OpenSceneMode.Single);
                    for (int i = 1; i < open.Count; i++) EditorSceneManager.OpenScene(open[i], OpenSceneMode.Additive);
                }
            }
            EditorUtility.RevealInFinder(Path.Combine(DefaultOut, DevMapFile));
        }

        public static void RunBatch()
        {
            var s = new Settings();
            var a = Environment.GetCommandLineArgs();
            string Arg(string k) { int i = Array.IndexOf(a, k); return i >= 0 && i + 1 < a.Length ? a[i + 1] : null; }
            s.ScenePath = Arg("-mapScene") ?? s.ScenePath;
            s.OutDir = Arg("-mapOut") ?? s.OutDir;
            if (int.TryParse(Arg("-mapPixels"), out int px)) s.MapPixels = px;
            if (float.TryParse(Arg("-mapCell"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float cell)) s.CellSize = cell;
            try { Generate(s); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        // ------------------------------------------------------------------ state for one run
        sealed class Run
        {
            public Settings S;
            public StringBuilder Log = new StringBuilder();
            public Terrain[] Terrains;
            public Rect World;
            public int Nx, Nz;
            public MapProjection Proj;
            public float[] H;          // world Y per map pixel, top-left origin
            public float[] Slope;      // degrees
            public float[] Shade;      // hillshade factor, 1 = flat
            public float MinH, MaxH;
            public float Sea = float.NaN;
            public string SeaSource = "none";
            public bool[] OceanWater, InlandWater;
            public List<WaterBody> Lakes = new List<WaterBody>();
            public List<IslandMapData.MapFeature> Features = new List<IslandMapData.MapFeature>();
            public List<Vector2> Trees = new List<Vector2>();
            public int[] TreeCells; public int TreeGrid; public float TreeCellSize = 10f;
            public bool[] Flat;
            public List<Peak> Peaks = new List<Peak>();
            public MapGrid Grid;
            public Color32[] Render;
            public List<string> Validation = new List<string>();
            public List<string> Notes = new List<string>();
            public bool HasSea => !float.IsNaN(Sea);
            public void Note(string m) { Log.AppendLine(m); Debug.Log("[IslandMap] " + m); }
            public int Idx(int x, int y) => y * Nx + x;
        }

        [Serializable] public class Peak { public string Label; public Vector3 Position; public string Cell; }
        [Serializable] public class WaterBody { public Vector2 CentreXZ; public float AreaM2; public float MinBedY; public string Cell; }
        [Serializable]
        public class CellStats
        {
            public string Cell; public Rect WorldXZ; public float MinY, MaxY, MeanY;
            public float LandFraction, FlatOpenFraction; public int Trees; public string[] Features;
        }

        // ------------------------------------------------------------------ main
        public static IslandMapData Generate(Settings s)
        {
            var r = new Run { S = s };
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Directory.CreateDirectory(s.OutDir);
            r.Note($"Island map generator v{Version}  scene {s.ScenePath}  started {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            if (!File.Exists(s.ScenePath)) throw new FileNotFoundException("Scene not found (is the Free Island Collection pack imported?)", s.ScenePath);
            EditorSceneManager.OpenScene(s.ScenePath, OpenSceneMode.Single);
            r.Note($"Scene opened read-only ({clock.Elapsed.TotalSeconds:F0}s). It is never saved.");
            try
            {
                CollectTerrain(r);
                CollectWater(r);
                CollectFeatures(r);
                CollectTrees(r);
                Analyse(r);
                RenderOverhead(r);
                r.Note($"Render done ({clock.Elapsed.TotalSeconds:F0}s)");
                var overhead = ComposeOverhead(r);
                overhead.SavePng(Path.Combine(s.OutDir, OverheadFile));
                var dev = ComposeDeveloperMap(r, overhead, out var devProj);
                dev.SavePng(Path.Combine(s.OutDir, DevMapFile));
                var analysis = ComposeAnalysis(r, out var anaProj);
                analysis.SavePng(Path.Combine(s.OutDir, AnalysisFile));
                r.Note($"Images written ({clock.Elapsed.TotalSeconds:F0}s)");
                ValidateData(r);
                var cells = CellStatistics(r);
                WriteJson(r, devProj, anaProj, cells);
                EnsureMarkersFile(s.OutDir);
                IslandMapData data = null;
                if (s.WriteDataAsset) data = WriteDataAsset(r, devProj, anaProj);
                r.Note($"Finished in {clock.Elapsed.TotalSeconds:F0}s");
                return data;
            }
            finally
            {
                File.WriteAllText(Path.Combine(s.OutDir, LogFile), r.Log + "\nVALIDATION\n" + string.Join("\n", r.Validation) + "\n");
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); // discard everything; nothing saved
            }
        }

        // ------------------------------------------------------------------ data collection
        static void CollectTerrain(Run r)
        {
            r.Terrains = UnityEngine.Object.FindObjectsOfType<Terrain>();
            if (r.Terrains.Length == 0) throw new Exception("No Terrain found in " + r.S.ScenePath);
            Rect w = default;
            for (int i = 0; i < r.Terrains.Length; i++)
            {
                var t = r.Terrains[i]; var p = t.transform.position; var sz = t.terrainData.size;
                var tr = new Rect(p.x, p.z, sz.x, sz.z);
                w = i == 0 ? tr : Rect.MinMaxRect(Mathf.Min(w.xMin, tr.xMin), Mathf.Min(w.yMin, tr.yMin), Mathf.Max(w.xMax, tr.xMax), Mathf.Max(w.yMax, tr.yMax));
                r.Note($"Terrain '{t.name}' data '{t.terrainData.name}' at {p} size {sz} heightmap {t.terrainData.heightmapResolution} layers [{string.Join(", ", t.terrainData.terrainLayers.Select(l => l ? LayerName(l) : "null"))}] trees {t.terrainData.treeInstanceCount}");
                if (t.transform.rotation != Quaternion.identity || t.transform.lossyScale != Vector3.one) r.Notes.Add($"Terrain '{t.name}' has a non-identity rotation/scale; Unity ignores these for terrains.");
            }
            r.World = w;
            r.Nx = r.S.MapPixels;
            r.Nz = Mathf.RoundToInt(r.S.MapPixels * w.height / w.width);
            r.Proj = MapProjection.Create(w, new Vector2Int(r.Nx, r.Nz));
            r.Grid = MapGrid.Create(w, r.S.CellSize);
            r.Note($"Mapped area = terrain bounds: X {w.xMin}..{w.xMax}, Z {w.yMin}..{w.yMax} ({w.width} x {w.height} m) -> {r.Nx} x {r.Nz} px, {r.Proj.MetresPerPixel:F4} m/px");

            r.H = new float[r.Nx * r.Nz];
            r.MinH = float.MaxValue; r.MaxH = float.MinValue;
            for (int y = 0; y < r.Nz; y++)
                for (int x = 0; x < r.Nx; x++)
                {
                    float h = SampleTerrain(r.Terrains, r.Proj.PixelToWorldXZ(new Vector2(x + 0.5f, y + 0.5f)));
                    r.H[r.Idx(x, y)] = h;
                    if (!float.IsNaN(h)) { r.MinH = Mathf.Min(r.MinH, h); r.MaxH = Mathf.Max(r.MaxH, h); }
                }
            for (int i = 0; i < r.H.Length; i++) if (float.IsNaN(r.H[i])) r.H[i] = r.MinH; // gaps between terrains (none for a single terrain)
            r.Note($"Height (world Y): {r.MinH:F2} .. {r.MaxH:F2} m");
        }

        static string LayerName(TerrainLayer l) => l.diffuseTexture ? l.diffuseTexture.name.Replace("_Diffuse", "") : l.name;

        public static float SampleTerrain(Terrain[] terrains, Vector2 xz)
        {
            foreach (var t in terrains)
            {
                var p = t.transform.position; var sz = t.terrainData.size;
                float u = (xz.x - p.x) / sz.x, v = (xz.y - p.z) / sz.z;
                if (u < 0 || v < 0 || u > 1 || v > 1) continue;
                return t.terrainData.GetInterpolatedHeight(u, v) + p.y;
            }
            return float.NaN;
        }

        static void CollectWater(Run r)
        {
            foreach (var rend in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                string n = rend.gameObject.name.ToLowerInvariant();
                if (!(n.Contains("ocean") || n.Contains("water") || n.Contains("sea") || n.Contains("lake") || n.Contains("river"))) continue;
                var b = rend.bounds;
                bool covers = b.min.x <= r.World.xMin && b.min.z <= r.World.yMin && b.max.x >= r.World.xMax && b.max.z >= r.World.yMax;
                r.Note($"Water object '{HierarchyPath(rend.transform)}' y={rend.transform.position.y:F2} bounds X {b.min.x:F0}..{b.max.x:F0} Z {b.min.z:F0}..{b.max.z:F0}{(covers ? " (covers whole map)" : "")}");
                if (covers && float.IsNaN(r.Sea)) { r.Sea = rend.transform.position.y; r.SeaSource = HierarchyPath(rend.transform); }
                else if (!covers) r.Notes.Add($"Local water object '{HierarchyPath(rend.transform)}' at {rend.transform.position} (shown by the render only)");
            }
            if (!r.HasSea) r.Notes.Add("No ocean/water plane covering the map: coastline and water statistics are not computed.");
            else r.Note($"Sea level Y {r.Sea:F2} from '{r.SeaSource}'");
        }

        static string HierarchyPath(Transform t) => t.parent ? HierarchyPath(t.parent) + "/" + t.name : t.name;

        static void CollectFeatures(Run r)
        {
            var list = new List<IslandMapData.MapFeature>();
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root.GetComponent<Terrain>()) continue;
                if (root.GetComponentInChildren<CharacterController>(true) is CharacterController cc)
                {
                    list.Add(Feature(cc.gameObject, "Player start (spawn)", cc.gameObject.name));
                    continue;
                }
                var rends = root.GetComponentsInChildren<Renderer>(true);
                if (rends.Length == 0) continue;
                bool isContainer = root.GetComponents<Component>().Length == 1 && root.transform.childCount > 0; // empty organiser object
                var groups = isContainer ? root.transform.Cast<Transform>().ToList() : new List<Transform> { root.transform };
                foreach (var g in groups)
                {
                    var gr = g.GetComponentsInChildren<Renderer>(true);
                    if (gr.Length == 0) continue;
                    var gb = Bounds(gr);
                    if (gb.size.x >= r.World.width * 0.9f && gb.size.z >= r.World.height * 0.9f) continue; // ocean plane etc.
                    var kids = g.Cast<Transform>().Where(k => k.GetComponentsInChildren<Renderer>(true).Length > 0).ToList();
                    bool split = kids.Count > 0 && kids.Count <= 12 && kids.All(k => { var kb = Bounds(k.GetComponentsInChildren<Renderer>(true)); return Mathf.Max(kb.size.x, kb.size.z) >= 6f; });
                    if (split) foreach (var k in kids) list.Add(Feature(k.gameObject, g.name, k.name));
                    else list.Add(Feature(g.gameObject, g.name, g.name + (kids.Count > 1 ? $" ({kids.Count} objects)" : "")));
                }
            }
            int n = 1;
            foreach (var f in list.OrderBy(f => f.Category == "Player start (spawn)" ? 1 : 0).ThenBy(f => f.Category).ThenBy(f => f.Name)) { f.Number = n++; r.Features.Add(f); }
            foreach (var f in r.Features) r.Note($"Feature {f.Number}: {f.Name} [{f.Category}] centre X {f.Position.x:F1} Z {f.Position.z:F1} Y {f.Position.y:F1}, footprint X {f.Footprint.xMin:F0}..{f.Footprint.xMax:F0} Z {f.Footprint.yMin:F0}..{f.Footprint.yMax:F0}, top Y {f.TopY:F0}, cell {r.Grid.CellName(f.Position)}");
        }

        static IslandMapData.MapFeature Feature(GameObject go, string category, string name)
        {
            var rends = go.GetComponentsInChildren<Renderer>(true);
            var b = rends.Length > 0 ? Bounds(rends) : new Bounds(go.transform.position, Vector3.zero);
            bool isGroup = go.GetComponents<Component>().Length == 1 && go.transform.childCount > 1 && go.transform.position == Vector3.zero;
            var pos = isGroup ? new Vector3(b.center.x, b.min.y, b.center.z) : go.transform.position;
            return new IslandMapData.MapFeature
            {
                Name = name, Category = category, Path = HierarchyPath(go.transform), Position = pos,
                Footprint = Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z), TopY = b.max.y,
            };
        }

        static Bounds Bounds(Renderer[] rs) { var b = rs[0].bounds; foreach (var x in rs) b.Encapsulate(x.bounds); return b; }

        static void CollectTrees(Run r)
        {
            foreach (var t in r.Terrains)
            {
                var td = t.terrainData; var p = t.transform.position;
                foreach (var ti in td.treeInstances) r.Trees.Add(new Vector2(p.x + ti.position.x * td.size.x, p.z + ti.position.z * td.size.z));
            }
            r.TreeGrid = Mathf.CeilToInt(Mathf.Max(r.World.width, r.World.height) / r.TreeCellSize);
            r.TreeCells = new int[r.TreeGrid * r.TreeGrid];
            foreach (var t in r.Trees)
            {
                int cx = Mathf.Clamp((int)((t.x - r.World.xMin) / r.TreeCellSize), 0, r.TreeGrid - 1);
                int cz = Mathf.Clamp((int)((t.y - r.World.yMin) / r.TreeCellSize), 0, r.TreeGrid - 1);
                r.TreeCells[cz * r.TreeGrid + cx]++;
            }
            r.Note($"Terrain trees: {r.Trees.Count}");
        }

        // ------------------------------------------------------------------ analysis
        static void Analyse(Run r)
        {
            int nx = r.Nx, nz = r.Nz;
            float mx = r.World.width / nx, mz = r.World.height / nz;
            r.Slope = new float[r.H.Length];
            r.Shade = new float[r.H.Length];
            var L = new Vector3(-0.5f, 0.70710678f, 0.5f); // light from the north-west, 45 degrees up (cartographic convention)
            for (int y = 0; y < nz; y++)
                for (int x = 0; x < nx; x++)
                {
                    float hl = r.H[r.Idx(Mathf.Max(0, x - 1), y)], hr = r.H[r.Idx(Mathf.Min(nx - 1, x + 1), y)];
                    float hn = r.H[r.Idx(x, Mathf.Max(0, y - 1))], hs = r.H[r.Idx(x, Mathf.Min(nz - 1, y + 1))];
                    float dx = (hr - hl) / (mx * ((x > 0 ? 1 : 0) + (x < nx - 1 ? 1 : 0)));
                    float dz = (hn - hs) / (mz * ((y > 0 ? 1 : 0) + (y < nz - 1 ? 1 : 0))); // +Z is north = up the image
                    var n = new Vector3(-dx, 1f, -dz).normalized;
                    r.Slope[r.Idx(x, y)] = Mathf.Acos(n.y) * Mathf.Rad2Deg;
                    r.Shade[r.Idx(x, y)] = Mathf.Max(0f, Vector3.Dot(n, L)) / 0.70710678f;
                }

            // Water: below sea level and connected to the map edge = ocean; enclosed basins = inland water (the ocean plane shows through).
            r.OceanWater = new bool[r.H.Length];
            r.InlandWater = new bool[r.H.Length];
            if (r.HasSea)
            {
                var q = new Queue<int>();
                void Seed(int x, int y) { int i = r.Idx(x, y); if (!r.OceanWater[i] && r.H[i] < r.Sea) { r.OceanWater[i] = true; q.Enqueue(i); } }
                for (int x = 0; x < nx; x++) { Seed(x, 0); Seed(x, nz - 1); }
                for (int y = 0; y < nz; y++) { Seed(0, y); Seed(nx - 1, y); }
                while (q.Count > 0)
                {
                    int i = q.Dequeue(), x = i % nx, y = i / nx;
                    if (x > 0) Seed(x - 1, y); if (x < nx - 1) Seed(x + 1, y); if (y > 0) Seed(x, y - 1); if (y < nz - 1) Seed(x, y + 1);
                }
                var seen = new bool[r.H.Length];
                for (int i = 0; i < r.H.Length; i++)
                {
                    if (seen[i] || r.OceanWater[i] || r.H[i] >= r.Sea) continue;
                    var comp = new List<int>(); q.Enqueue(i); seen[i] = true;
                    while (q.Count > 0)
                    {
                        int j = q.Dequeue(); comp.Add(j); int x = j % nx, y = j / nx;
                        void Try(int k) { if (!seen[k] && !r.OceanWater[k] && r.H[k] < r.Sea) { seen[k] = true; q.Enqueue(k); } }
                        if (x > 0) Try(j - 1); if (x < nx - 1) Try(j + 1); if (y > 0) Try(j - nx); if (y < nz - 1) Try(j + nx);
                    }
                    foreach (int j in comp) r.InlandWater[j] = true;
                    var c = Vector2.zero; float bed = float.MaxValue;
                    foreach (int j in comp) { c += r.Proj.PixelToWorldXZ(new Vector2(j % nx + 0.5f, j / nx + 0.5f)); bed = Mathf.Min(bed, r.H[j]); }
                    c /= comp.Count;
                    r.Lakes.Add(new WaterBody { CentreXZ = c, AreaM2 = comp.Count * mx * mz, MinBedY = bed, Cell = r.Grid.CellName(new Vector3(c.x, 0, c.y)) });
                }
                int ocean = r.OceanWater.Count(b => b), inland = r.InlandWater.Count(b => b);
                r.Note($"Water: ocean {ocean * mx * mz / 1e6f:F3} km², land {(r.H.Length - ocean - inland) * mx * mz / 1e6f:F3} km², inland basins below sea level: {r.Lakes.Count} ({inland * mx * mz:F0} m²)");
                foreach (var l in r.Lakes.OrderByDescending(l => l.AreaM2).Take(20)) r.Note($"  inland basin at X {l.CentreXZ.x:F0} Z {l.CentreXZ.y:F0} ({l.Cell}) area {l.AreaM2:F0} m², bed Y {l.MinBedY:F1}");
            }

            // Flat open ground: purely geometric (slope, above water, no terrain trees nearby, clear of existing structures).
            r.Flat = new bool[r.H.Length];
            var blocked = r.Features.Select(f => new Rect(f.Footprint.x - r.S.StructureClearance, f.Footprint.y - r.S.StructureClearance, f.Footprint.width + 2 * r.S.StructureClearance, f.Footprint.height + 2 * r.S.StructureClearance)).ToArray();
            for (int y = 0; y < nz; y++)
                for (int x = 0; x < nx; x++)
                {
                    int i = r.Idx(x, y);
                    if (r.Slope[i] > r.S.FlatSlopeDegrees) continue;
                    if (r.HasSea && (r.H[i] < r.Sea + 2f || r.InlandWater[i])) continue;
                    var w = r.Proj.PixelToWorldXZ(new Vector2(x + 0.5f, y + 0.5f));
                    if (TreesNear(r, w) > 0) continue;
                    bool hit = false; foreach (var b in blocked) if (b.Contains(w)) { hit = true; break; }
                    if (!hit) r.Flat[i] = true;
                }

            // Spot heights: highest local maxima, at least 300 m apart.
            int step = Mathf.Max(1, Mathf.RoundToInt(10f / mx)), rad = Mathf.RoundToInt(150f / (mx * step));
            var cands = new List<(float h, int x, int y)>();
            float minPeak = (r.HasSea ? r.Sea : r.MinH) + 40f;
            for (int y = 0; y < nz; y += step)
                for (int x = 0; x < nx; x += step)
                {
                    float h = r.H[r.Idx(x, y)];
                    if (h < minPeak) continue;
                    bool max = true;
                    for (int dy = -rad; dy <= rad && max; dy++)
                        for (int dx = -rad; dx <= rad; dx++)
                        {
                            int sx = x + dx * step, sy = y + dy * step;
                            if ((uint)sx >= (uint)nx || (uint)sy >= (uint)nz || (dx == 0 && dy == 0)) continue;
                            if (r.H[r.Idx(sx, sy)] > h) { max = false; break; }
                        }
                    if (max) cands.Add((h, x, y));
                }
            foreach (var c in cands.OrderByDescending(c => c.h))
            {
                var w = r.Proj.PixelToWorldXZ(new Vector2(c.x + 0.5f, c.y + 0.5f));
                var p = new Vector3(w.x, c.h, w.y);
                if (r.Peaks.Any(o => Vector2.Distance(new Vector2(o.Position.x, o.Position.z), w) < 300f)) continue;
                r.Peaks.Add(new Peak { Position = p, Label = $"{c.h:F0} m", Cell = r.Grid.CellName(p) });
                if (r.Peaks.Count >= 14) break;
            }
            r.Note("Spot heights: " + string.Join("; ", r.Peaks.Select(p => $"Y {p.Position.y:F1} at X {p.Position.x:F0} Z {p.Position.z:F0} ({p.Cell})")));
        }

        static int TreesNear(Run r, Vector2 w)
        {
            int cx = (int)((w.x - r.World.xMin) / r.TreeCellSize), cz = (int)((w.y - r.World.yMin) / r.TreeCellSize), n = 0;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = cx + dx, z = cz + dz;
                    if ((uint)x < (uint)r.TreeGrid && (uint)z < (uint)r.TreeGrid) n += r.TreeCells[z * r.TreeGrid + x];
                }
            return n;
        }

        // ------------------------------------------------------------------ rendering
        /// <summary>Puts a camera straight above a world X/Z rectangle, looking down, image up = +Z (north).</summary>
        public static void ConfigureTopDownCamera(Camera cam, Rect worldXZ, float topY, float bottomY)
        {
            cam.orthographic = true;
            cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            cam.transform.position = new Vector3(worldXZ.center.x, topY + 200f, worldXZ.center.y);
            cam.orthographicSize = worldXZ.height * 0.5f;
            cam.aspect = worldXZ.width / worldXZ.height;
            cam.nearClipPlane = 1f;
            cam.farClipPlane = (topY + 200f) - (bottomY - 200f);
        }

        static void RenderOverhead(Run r)
        {
            int nx = r.Nx, nz = r.Nz, T = Mathf.Max(1, r.S.Tiles), SS = Mathf.Max(1, r.S.Supersample);
            r.Render = new Color32[nx * nz];
            float lodBias = QualitySettings.lodBias; int maxLod = QualitySettings.maximumLODLevel;
            bool fog = RenderSettings.fog;
            var go = new GameObject("__IslandMapCamera") { hideFlags = HideFlags.HideAndDontSave };
            var markers = new List<GameObject>();
            try
            {
                QualitySettings.lodBias = 50f;           // ortho cameras pick LODs by ortho size; keep full detail
                QualitySettings.maximumLODLevel = 0;
                RenderSettings.fog = false;
                foreach (var t in r.Terrains)
                {
                    t.heightmapPixelError = 1f;
                    t.basemapDistance = 20000f;
                    t.treeDistance = 5000f;
                    t.treeBillboardDistance = 2000f;
                    t.treeMaximumFullLODCount = 100000;
                    t.detailObjectDistance = 0f;          // grass is sub-pixel at map scale
                }
                var cam = go.AddComponent<Camera>();
                cam.enabled = false;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.16f, 0.32f, 0.42f);
                cam.allowHDR = false; cam.allowMSAA = false;

                float camErr = 0;
                var rng = new System.Random(12345);
                for (int ty = 0; ty < T; ty++)
                    for (int tx = 0; tx < T; tx++)
                    {
                        int px0 = tx * nx / T, px1 = (tx + 1) * nx / T, py0 = ty * nz / T, py1 = (ty + 1) * nz / T;
                        var tile = TileWorldRect(r.Proj, px0, py0, px1, py1);
                        ConfigureTopDownCamera(cam, tile, r.MaxH, r.MinH);
                        var img = RenderTile(cam, (px1 - px0) * SS, (py1 - py0) * SS);
                        // box filter down, flip to top-left origin
                        int tw = (px1 - px0) * SS, th = (py1 - py0) * SS;
                        for (int y = 0; y < py1 - py0; y++)
                            for (int x = 0; x < px1 - px0; x++)
                            {
                                int R = 0, G = 0, B = 0;
                                for (int sy = 0; sy < SS; sy++)
                                    for (int sx = 0; sx < SS; sx++)
                                    {
                                        var c = img[(th - 1 - (y * SS + sy)) * tw + x * SS + sx];
                                        R += c.r; G += c.g; B += c.b;
                                    }
                                int n = SS * SS;
                                r.Render[r.Idx(px0 + x, py0 + y)] = new Color32((byte)(R / n), (byte)(G / n), (byte)(B / n), 255);
                            }
                        // projection check: the camera must map world points exactly where MapProjection says
                        for (int k = 0; k < 64; k++)
                        {
                            var w = new Vector3(tile.xMin + (float)rng.NextDouble() * tile.width, r.MinH + (float)rng.NextDouble() * (r.MaxH - r.MinH), tile.yMin + (float)rng.NextDouble() * tile.height);
                            var vp = cam.WorldToViewportPoint(w);
                            var fromCam = new Vector2(px0 + vp.x * (px1 - px0), py0 + (1f - vp.y) * (py1 - py0));
                            camErr = Mathf.Max(camErr, (fromCam - r.Proj.WorldToPixel(w)).magnitude);
                        }
                    }
                r.Validation.Add($"PASS? camera-vs-formula: max difference between Camera.WorldToViewportPoint and MapProjection.WorldToPixel over {T * T * 64} random points = {camErr:F4} px ({(camErr < 0.05f ? "PASS" : "FAIL")})");

                // End-to-end alignment: render marker cubes at known world positions with only their layer visible,
                // find them in the image, compare with the formula.
                int layer = FreeLayer();
                var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color")) { color = Color.white };
                var spots = new[] { new Vector2(0.137f, 0.861f), new Vector2(0.5f, 0.5f), new Vector2(0.873f, 0.129f), new Vector2(0.912f, 0.903f), new Vector2(0.081f, 0.074f), new Vector2(0.333f, 0.618f) };
                foreach (var sp in spots)
                {
                    var xz = r.Proj.NormalizedToWorldXZ(sp);
                    var m = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    m.hideFlags = HideFlags.HideAndDontSave; m.layer = layer;
                    UnityEngine.Object.DestroyImmediate(m.GetComponent<Collider>());
                    m.GetComponent<Renderer>().sharedMaterial = mat;
                    m.transform.position = new Vector3(xz.x, SampleTerrain(r.Terrains, xz) + 3f, xz.y);
                    m.transform.localScale = new Vector3(8f, 4f, 8f);
                    markers.Add(m);
                }
                cam.cullingMask = 1 << layer;
                cam.backgroundColor = Color.black;
                float worst = 0;
                // One marker visible at a time; every tile the marker touches is rendered with the same tile cameras
                // as the map, and the marker's centroid is measured across them (this also checks the tile seams:
                // the centre marker sits exactly on the corner shared by four tiles).
                foreach (var m in markers) m.SetActive(false);
                float expectedArea = 8f * 8f * r.Proj.PixelsPerMetre.x * r.Proj.PixelsPerMetre.y;
                foreach (var m in markers)
                {
                    m.SetActive(true);
                    var exp = r.Proj.WorldToPixel(m.transform.position);
                    double sx = 0, sy = 0, sw = 0; int count = 0, tilesUsed = 0;
                    for (int ty = 0; ty < T; ty++)
                        for (int tx = 0; tx < T; tx++)
                        {
                            int px0 = tx * nx / T, px1 = (tx + 1) * nx / T, py0 = ty * nz / T, py1 = (ty + 1) * nz / T;
                            if (exp.x + 20 < px0 || exp.x - 20 > px1 || exp.y + 20 < py0 || exp.y - 20 > py1) continue;
                            tilesUsed++;
                            ConfigureTopDownCamera(cam, TileWorldRect(r.Proj, px0, py0, px1, py1), r.MaxH, r.MinH);
                            int tw = px1 - px0, th = py1 - py0;
                            var img = RenderTile(cam, tw, th);
                            for (int y = 0; y < th; y++)
                                for (int x = 0; x < tw; x++)
                                {
                                    var c = img[y * tw + x];
                                    float v = (c.r + c.g + c.b) / 765f;
                                    if (v < 0.5f) continue;
                                    count++;
                                    sx += (px0 + x + 0.5) * v; sy += (py0 + th - y - 0.5) * v; sw += v; // flip bottom-left -> top-left
                                }
                        }
                    m.SetActive(false);
                    if (sw <= 0) { r.Validation.Add($"FAIL marker at {m.transform.position}: not found in render"); worst = float.MaxValue; continue; }
                    var found = new Vector2((float)(sx / sw), (float)(sy / sw));
                    float e = (found - exp).magnitude; worst = Mathf.Max(worst, e);
                    r.Validation.Add($"marker world X {m.transform.position.x:F1} Z {m.transform.position.z:F1}: expected px ({exp.x:F1}, {exp.y:F1}) found ({found.x:F1}, {found.y:F1}) error {e:F2} px = {e * r.Proj.MetresPerPixel:F2} m  [{tilesUsed} tile(s), {count} px, expected ~{expectedArea:F0} px]");
                }
                r.Validation.Add($"{(worst <= 1.0f ? "PASS" : "FAIL")} end-to-end alignment: worst marker error {worst:F2} px ({worst * r.Proj.MetresPerPixel:F2} m), threshold 1 px");
                UnityEngine.Object.DestroyImmediate(mat);
            }
            finally
            {
                foreach (var m in markers) UnityEngine.Object.DestroyImmediate(m);
                UnityEngine.Object.DestroyImmediate(go);
                QualitySettings.lodBias = lodBias; QualitySettings.maximumLODLevel = maxLod; RenderSettings.fog = fog;
            }
            for (int i = 0; i < r.Validation.Count; i++) r.Validation[i] = r.Validation[i].Replace("PASS? ", "");
        }

        static Rect TileWorldRect(MapProjection p, int px0, int py0, int px1, int py1)
        {
            var sw = p.PixelToWorldXZ(new Vector2(px0, py1));
            var ne = p.PixelToWorldXZ(new Vector2(px1, py0));
            return Rect.MinMaxRect(sw.x, sw.y, ne.x, ne.y);
        }

        static Color32[] RenderTile(Camera cam, int w, int h)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            cam.targetTexture = rt;
            cam.aspect = (float)w / h;
            cam.Render();
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = prev; cam.targetTexture = null;
            var px = tex.GetPixels32();
            UnityEngine.Object.DestroyImmediate(tex); rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
            return px; // bottom-left origin
        }

        static int FreeLayer()
        {
            var used = new HashSet<int>(UnityEngine.Object.FindObjectsOfType<Renderer>().Select(x => x.gameObject.layer));
            for (int l = 31; l >= 8; l--) if (!used.Contains(l) && string.IsNullOrEmpty(LayerMask.LayerToName(l))) return l;
            throw new Exception("No free layer for alignment markers");
        }

        // ------------------------------------------------------------------ composition
        static readonly Color Ink = new Color(0.10f, 0.11f, 0.13f);
        static readonly Color Paper = new Color(0.93f, 0.94f, 0.95f);
        static readonly Color Panel = new Color(0.115f, 0.125f, 0.145f);
        static readonly Color Boundary = new Color(0.92f, 0.20f, 0.14f);
        static readonly Color Coast = new Color(0.62f, 0.90f, 1.00f);
        static readonly Color ContourCol = new Color(0.27f, 0.17f, 0.07f);
        static readonly Color FeatureCol = new Color(1.00f, 0.82f, 0.18f);
        static readonly Color FlatCol = new Color(0.20f, 0.85f, 0.95f);

        static MapCanvas ComposeOverhead(Run r)
        {
            var c = new MapCanvas(r.Nx, r.Nz, Color.black);
            float k = r.S.HillshadeStrength;
            for (int i = 0; i < r.Render.Length; i++)
            {
                var p = r.Render[i];
                float f = Mathf.Lerp(1f, Mathf.Clamp(r.Shade[i], 0.35f, 1.35f), k);
                c.Px[i] = new Color32((byte)Mathf.Min(255, p.r * f), (byte)Mathf.Min(255, p.g * f), (byte)Mathf.Min(255, p.b * f), 255);
            }
            return c;
        }

        static void DrawContours(Run r, MapCanvas c, int ox, int oy, float thinAlpha, float indexAlpha, bool includeUnderwater)
        {
            float ci = r.S.ContourInterval, ii = r.S.IndexContourInterval;
            for (int y = 0; y < r.Nz - 1; y++)
                for (int x = 0; x < r.Nx - 1; x++)
                {
                    float h = r.H[r.Idx(x, y)], hr = r.H[r.Idx(x + 1, y)], hd = r.H[r.Idx(x, y + 1)];
                    if (!includeUnderwater && r.HasSea && h < r.Sea && hr < r.Sea && hd < r.Sea) continue;
                    bool idx = Mathf.Floor(h / ii) != Mathf.Floor(hr / ii) || Mathf.Floor(h / ii) != Mathf.Floor(hd / ii);
                    bool thin = Mathf.Floor(h / ci) != Mathf.Floor(hr / ci) || Mathf.Floor(h / ci) != Mathf.Floor(hd / ci);
                    if (idx) { c.Blend(ox + x, oy + y, ContourCol, indexAlpha); c.Blend(ox + x + 1, oy + y, ContourCol, indexAlpha * 0.6f); c.Blend(ox + x, oy + y + 1, ContourCol, indexAlpha * 0.6f); }
                    else if (thin) c.Blend(ox + x, oy + y, ContourCol, thinAlpha);
                }
        }

        static void DrawCoast(Run r, MapCanvas c, int ox, int oy, Color col)
        {
            if (!r.HasSea) return;
            for (int y = 0; y < r.Nz - 1; y++)
                for (int x = 0; x < r.Nx - 1; x++)
                {
                    bool a = r.H[r.Idx(x, y)] >= r.Sea;
                    if (a != (r.H[r.Idx(x + 1, y)] >= r.Sea) || a != (r.H[r.Idx(x, y + 1)] >= r.Sea))
                        for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) c.Blend(ox + x + dx, oy + y + dy, col, dx == 0 && dy == 0 ? 1f : 0.55f);
                }
        }

        static void DrawFeatures(Run r, MapCanvas c, MapFont font, MapProjection p, bool badges)
        {
            foreach (var f in r.Features)
            {
                var a = p.WorldToPixel(new Vector2(f.Footprint.xMin, f.Footprint.yMax));
                var b = p.WorldToPixel(new Vector2(f.Footprint.xMax, f.Footprint.yMin));
                if (b.x - a.x < 6) { var m = (a + b) * 0.5f; a = m - new Vector2(3, 3); b = m + new Vector2(3, 3); }
                c.RectOutline(a, b, Ink, 6f);
                c.RectOutline(a, b, FeatureCol, 3f);
            }
            if (!badges) return;
            var placed = new List<Rect>();
            foreach (var f in r.Features)
            {
                var a = p.WorldToPixel(new Vector2(f.Footprint.xMin, f.Footprint.yMax));
                var b = p.WorldToPixel(new Vector2(f.Footprint.xMax, f.Footprint.yMin));
                var centre = new Vector2(b.x + 26, a.y - 26); // north-east of the footprint
                for (int k = 0; k < 8 && placed.Any(q => q.Overlaps(new Rect(centre.x - 26, centre.y - 26, 52, 52))); k++) centre += new Vector2(0, 54);
                placed.Add(new Rect(centre.x - 26, centre.y - 26, 52, 52));
                c.Line(centre, new Vector2(b.x, a.y), Ink, 3f);
                c.Circle(centre, 25f, Ink);
                c.Circle(centre, 22f, FeatureCol);
                font.Draw(c, f.Number.ToString(), centre, 30, Ink, 0.5f, 0.5f, true);
            }
        }

        static void DrawPeaks(Run r, MapCanvas c, MapFont font, MapProjection p)
        {
            foreach (var pk in r.Peaks)
            {
                var q = p.WorldToPixel(pk.Position);
                var tri = new[] { q + new Vector2(0, -15), q + new Vector2(13, 9), q + new Vector2(-13, 9) };
                var big = new[] { q + new Vector2(0, -20), q + new Vector2(17, 12), q + new Vector2(-17, 12) };
                c.Polygon(big, Paper);
                c.Polygon(tri, Ink);
                // label to the right unless that would cover a cell name (drawn in each cell's north-west corner)
                float w = font.Measure(pk.Label, 30, true);
                var right = new Rect(q.x + 22, q.y - 20, w, 40);
                bool clash = false;
                var g = r.Grid;
                for (int j = 0; j <= g.Rows && !clash; j++)
                    for (int i = 0; i <= g.Columns; i++)
                    {
                        var nw = p.WorldToPixel(new Vector2(g.NorthWestCorner.x + i * g.CellSize, g.NorthWestCorner.y - j * g.CellSize));
                        if (right.Overlaps(new Rect(nw.x, nw.y, 95, 60))) { clash = true; break; }
                    }
                if (clash) font.Draw(c, pk.Label, q + new Vector2(-22, 0), 30, Ink, 1f, 0.5f, true, Paper, 3.5f);
                else font.Draw(c, pk.Label, q + new Vector2(22, 0), 30, Ink, 0f, 0.5f, true, Paper, 3.5f);
            }
        }

        static MapCanvas ComposeDeveloperMap(Run r, MapCanvas overhead, out MapProjection proj)
        {
            const int L = 300, R = 300, T = 330, B = 300, Legend = 1240;
            int W = L + r.Nx + R + Legend, Hh = T + r.Nz + B;
            proj = MapProjection.Create(r.World, new Vector2Int(r.Nx, r.Nz), new Vector2Int(L, T), new Vector2Int(W, Hh));
            var p = proj;
            var font = new MapFont(26, 30, 34, 40, 46, 56, 76);
            var c = new MapCanvas(W, Hh, Panel);

            // base: rendered island, lightly muted so overlays read clearly
            var gray = new Color32(128, 128, 128, 255);
            for (int y = 0; y < r.Nz; y++)
                for (int x = 0; x < r.Nx; x++)
                {
                    var s = overhead.Px[r.Idx(x, y)];
                    c.Px[(T + y) * W + L + x] = Color32.Lerp(s, gray, 0.12f);
                }
            // fine grid
            for (float gx = Mathf.Ceil(r.World.xMin / r.S.SubCellSize) * r.S.SubCellSize; gx <= r.World.xMax; gx += r.S.SubCellSize)
                c.Line(p.WorldToPixel(new Vector2(gx, r.World.yMin)), p.WorldToPixel(new Vector2(gx, r.World.yMax)), new Color(1, 1, 1, 0.22f), 1f);
            for (float gz = Mathf.Ceil(r.World.yMin / r.S.SubCellSize) * r.S.SubCellSize; gz <= r.World.yMax; gz += r.S.SubCellSize)
                c.Line(p.WorldToPixel(new Vector2(r.World.xMin, gz)), p.WorldToPixel(new Vector2(r.World.xMax, gz)), new Color(1, 1, 1, 0.22f), 1f);
            DrawContours(r, c, L, T, 0.28f, 0.6f, false);
            DrawCoast(r, c, L, T, Coast);
            // main grid
            var g = r.Grid;
            for (int i = 0; i <= g.Columns; i++)
            {
                float gx = g.NorthWestCorner.x + i * g.CellSize;
                if (gx < r.World.xMin - 0.01f || gx > r.World.xMax + 0.01f) continue;
                var a = p.WorldToPixel(new Vector2(gx, r.World.yMin)); var b = p.WorldToPixel(new Vector2(gx, r.World.yMax));
                c.Line(a, b, new Color(0, 0, 0, 0.5f), 4.5f); c.Line(a, b, new Color(1, 1, 1, 0.92f), 2f);
            }
            for (int j = 0; j <= g.Rows; j++)
            {
                float gz = g.NorthWestCorner.y - j * g.CellSize;
                if (gz < r.World.yMin - 0.01f || gz > r.World.yMax + 0.01f) continue;
                var a = p.WorldToPixel(new Vector2(r.World.xMin, gz)); var b = p.WorldToPixel(new Vector2(r.World.xMax, gz));
                c.Line(a, b, new Color(0, 0, 0, 0.5f), 4.5f); c.Line(a, b, new Color(1, 1, 1, 0.92f), 2f);
            }
            DrawFeatures(r, c, font, p, true);
            DrawPeaks(r, c, font, p);
            // cell names in each cell's north-west corner
            for (int j = 0; j < g.Rows; j++)
                for (int i = 0; i < g.Columns; i++)
                {
                    var cr = g.CellRect(new Vector2Int(i, j));
                    var nw = p.WorldToPixel(new Vector2(Mathf.Max(cr.xMin, r.World.xMin), Mathf.Min(cr.yMax, r.World.yMax)));
                    font.Draw(c, g.CellName(new Vector2Int(i, j)), nw + new Vector2(12, 12), 34, Color.white, 0, 0, true, new Color(0, 0, 0, 0.85f), 3.5f);
                }
            // terrain boundary
            foreach (var t in r.Terrains)
            {
                var tp = t.transform.position; var sz = t.terrainData.size;
                c.RectOutline(p.WorldToPixel(new Vector2(tp.x, tp.z + sz.z)), p.WorldToPixel(new Vector2(tp.x + sz.x, tp.z)), Boundary, 5f);
            }

            // margins: ticks, coordinates, column letters / row numbers
            var mapMin = new Vector2(L, T); var mapMax = new Vector2(L + r.Nx, T + r.Nz);
            for (float gx = Mathf.Ceil(r.World.xMin / r.S.SubCellSize) * r.S.SubCellSize; gx <= r.World.xMax + 0.01f; gx += r.S.SubCellSize)
            {
                float x = p.WorldToPixel(new Vector2(gx, 0)).x;
                bool major = Mathf.Abs(Mathf.Repeat(gx - g.NorthWestCorner.x + 0.01f, g.CellSize)) < 0.1f;
                float len = major ? 34 : 14;
                c.Line(new Vector2(x, mapMin.y - len), new Vector2(x, mapMin.y), Paper, major ? 3 : 2);
                c.Line(new Vector2(x, mapMax.y), new Vector2(x, mapMax.y + len), Paper, major ? 3 : 2);
                if (major)
                {
                    font.Draw(c, $"X {gx:0}", new Vector2(x, mapMin.y - 46), 30, Paper, 0.5f, 1f);
                    font.Draw(c, $"X {gx:0}", new Vector2(x, mapMax.y + 46), 30, Paper, 0.5f, 0f);
                }
            }
            for (float gz = Mathf.Ceil(r.World.yMin / r.S.SubCellSize) * r.S.SubCellSize; gz <= r.World.yMax + 0.01f; gz += r.S.SubCellSize)
            {
                float y = p.WorldToPixel(new Vector2(0, gz)).y;
                bool major = Mathf.Abs(Mathf.Repeat(g.NorthWestCorner.y - gz + 0.01f, g.CellSize)) < 0.1f;
                float len = major ? 34 : 14;
                c.Line(new Vector2(mapMin.x - len, y), new Vector2(mapMin.x, y), Paper, major ? 3 : 2);
                c.Line(new Vector2(mapMax.x, y), new Vector2(mapMax.x + len, y), Paper, major ? 3 : 2);
                if (major)
                {
                    font.Draw(c, $"Z {gz:0}", new Vector2(mapMin.x - 44, y), 30, Paper, 1f, 0.5f);
                    font.Draw(c, $"Z {gz:0}", new Vector2(mapMax.x + 44, y), 30, Paper, 0f, 0.5f);
                }
            }
            for (int i = 0; i < g.Columns; i++)
            {
                var cr = g.CellRect(new Vector2Int(i, 0));
                float x = p.WorldToPixel(new Vector2(Mathf.Clamp(cr.center.x, r.World.xMin, r.World.xMax), 0)).x;
                font.Draw(c, MapGrid.ColumnName(i), new Vector2(x, mapMin.y - 150), 56, Color.white, 0.5f, 1f, true);
                font.Draw(c, MapGrid.ColumnName(i), new Vector2(x, mapMax.y + 150), 56, Color.white, 0.5f, 0f, true);
            }
            for (int j = 0; j < g.Rows; j++)
            {
                var cr = g.CellRect(new Vector2Int(0, j));
                float y = p.WorldToPixel(new Vector2(0, Mathf.Clamp(cr.center.y, r.World.yMin, r.World.yMax))).y;
                font.Draw(c, (j + 1).ToString(), new Vector2(mapMin.x - 230, y), 56, Color.white, 0.5f, 0.5f, true);
                font.Draw(c, (j + 1).ToString(), new Vector2(mapMax.x + 230, y), 56, Color.white, 0.5f, 0.5f, true);
            }
            font.Draw(c, "N", new Vector2(L + r.Nx / 2f, 40), 46, Color.white, 0.5f, 0f, true);
            c.Polygon(new[] { new Vector2(L + r.Nx / 2f, 96), new Vector2(L + r.Nx / 2f + 22, 130), new Vector2(L + r.Nx / 2f - 22, 130) }, Color.white);

            DrawLegend(r, c, font, p, L + r.Nx + R, Legend);
            return c;
        }

        static void DrawLegend(Run r, MapCanvas c, MapFont f, MapProjection p, int x0, int width)
        {
            float x = x0 + 30, y = 70, colW = width - 80;
            var dim = new Color(0.72f, 0.75f, 0.80f);
            void Head(string s) { y += 34; f.Draw(c, s, new Vector2(x, y), 40, FeatureCol, 0, 0, true); y += 62; }
            void Text(string s, int size = 30, Color? col = null, bool bold = false) { f.Draw(c, s, new Vector2(x, y), size, col ?? Paper, 0, 0, bold); y += size * 1.45f; }

            f.Draw(c, "UNITY ISLAND", new Vector2(x, y), 76, Color.white, 0, 0, true); y += 100;
            Text("Developer map  -  Phase 1 world planning", 34, Paper, true);
            Text("Not the player map. Existing content only; nothing planned is drawn.", 30, dim);
            Text($"Source: {r.S.ScenePath}", 26, dim);
            Text($"Generated {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC  -  generator v{Version}", 26, dim);

            // north arrow
            var na = new Vector2(x0 + width - 150, 150);
            c.Polygon(new[] { na + new Vector2(0, -85), na + new Vector2(42, 60), na + new Vector2(0, 32) }, Color.white);
            c.Polygon(new[] { na + new Vector2(0, -85), na + new Vector2(-42, 60), na + new Vector2(0, 32) }, new Color(0.55f, 0.58f, 0.62f));
            f.Draw(c, "N", na + new Vector2(0, -100), 56, Color.white, 0.5f, 1f, true);
            f.Draw(c, "= world +Z", na + new Vector2(0, 84), 26, Paper, 0.5f, 0f);

            Head("SCALE");
            float ppm = p.PixelsPerMetre.x, seg = 100f; int segs = 5;
            for (int i = 0; i < segs; i++)
                c.FillRect(x + i * seg * ppm, y, x + (i + 1) * seg * ppm, y + 22, i % 2 == 0 ? Color.white : Ink);
            c.RectOutline(new Vector2(x, y), new Vector2(x + segs * seg * ppm, y + 22), Color.white, 2f);
            for (int i = 0; i <= segs; i++) f.Draw(c, $"{i * seg:0}", new Vector2(x + i * seg * ppm, y + 34), 26, Paper, 0.5f, 0f);
            f.Draw(c, "m", new Vector2(x + segs * seg * ppm + 30, y + 34), 26, Paper, 0f, 0f);
            y += 90;
            Text($"{p.MetresPerPixel:F4} m per pixel  ({p.PixelsPerMetre.x:F4} px per m)", 30);

            Head("MAPPED AREA (= terrain boundary, red frame)");
            Text($"World X {r.World.xMin:0} to {r.World.xMax:0},  Z {r.World.yMin:0} to {r.World.yMax:0}  ({r.World.width:0} x {r.World.height:0} m)");
            Text($"Ground height (world Y): {r.MinH:F1} to {r.MaxH:F1} m");
            Text(r.HasSea ? $"Sea level: Y {r.Sea:F1}  ({r.SeaSource})" : "Sea level: no ocean object in scene", 30);
            if (r.HasSea) Text($"Highest point is {r.MaxH - r.Sea:F0} m above sea level", 30);

            Head("COORDINATES  (px, py from the image's top-left corner)");
            float mpp = p.MetresPerPixel;
            Text("This developer map:", 30, Color.white, true);
            Text($"   X = {r.World.xMin:0} + (px - {p.MapRectMin.x}) x {mpp:F4}");
            Text($"   Z = {r.World.yMax:0} - (py - {p.MapRectMin.y}) x {mpp:F4}");
            Text($"Clean map {OverheadFile} ({r.Nx} x {r.Nz}):", 30, Color.white, true);
            Text($"   X = {r.World.xMin:0} + px x {mpp:F4}      Z = {r.World.yMax:0} - py x {mpp:F4}");
            Text("Y (height): read the contours, or hover in", 30);
            Text("Tools > Island > Map > Developer Map (live readout).", 30);

            Head("GRID");
            var g = r.Grid;
            Text($"Cells {g.CellSize:0} m: columns {MapGrid.ColumnName(0)}-{MapGrid.ColumnName(g.Columns - 1)} west to east,");
            Text($"rows 1-{g.Rows} north to south. Thin lines every {r.S.SubCellSize:0} m.");
            Text("Every grid line sits on a round world coordinate.");
            var ex = new Vector2Int(Mathf.Min(2, g.Columns - 1), Mathf.Min(3, g.Rows - 1)); var er = g.CellRect(ex);
            Text($"Example: {g.CellName(ex)} = X {er.xMin:0}-{er.xMax:0}, Z {er.yMin:0}-{er.yMax:0}");

            Head("SYMBOLS");
            void Sym(Action<Vector2, Vector2> draw, string label)
            {
                var a = new Vector2(x + 10, y + 16); var b = new Vector2(x + 110, y + 16);
                draw(a, b); f.Draw(c, label, new Vector2(x + 140, y + 16), 30, Paper, 0, 0.5f); y += 48;
            }
            Sym((a, b) => c.Line(a, b, Boundary, 5f), "Terrain boundary");
            Sym((a, b) => { c.Line(a, b, Coast, 4f); }, r.HasSea ? $"Coastline (terrain meets sea level Y {r.Sea:0})" : "Coastline (n/a)");
            Sym((a, b) => { c.FillRect(a.x - 8, a.y - 14, b.x + 8, a.y + 14, new Color(0.75f, 0.70f, 0.55f)); c.Line(a + new Vector2(0, -6), b + new Vector2(0, -6), ContourCol, 1f); c.Line(a + new Vector2(0, 7), b + new Vector2(0, 7), ContourCol, 2.5f); },
                $"Contours every {r.S.ContourInterval:0} m, bold every {r.S.IndexContourInterval:0} m (world Y)");
            Sym((a, b) => { c.RectOutline(a + new Vector2(20, -14), b + new Vector2(-20, 14), Ink, 6f); c.RectOutline(a + new Vector2(20, -14), b + new Vector2(-20, 14), FeatureCol, 3f); }, "Existing structure footprint + number");
            Sym((a, b) => { var q = (a + b) / 2; c.Polygon(new[] { q + new Vector2(0, -15), q + new Vector2(13, 9), q + new Vector2(-13, 9) }, Color.white); }, "Summit / spot height (world Y)");
            Sym((a, b) => { c.Line(a, b, new Color(0, 0, 0, 0.5f), 4.5f); c.Line(a, b, Color.white, 2f); }, $"{r.S.CellSize:0} m grid line");

            Head("EXISTING CONTENT IN THE SCENE");
            foreach (var ft in r.Features)
            {
                string name = ft.Name.Length > 34 ? ft.Name.Substring(0, 33) + "…" : ft.Name;
                var bc = new Vector2(x + 22, y + 16);
                c.Circle(bc, 20f, FeatureCol);
                f.Draw(c, ft.Number.ToString(), bc, 26, Ink, 0.5f, 0.5f, true);
                f.Draw(c, name, new Vector2(x + 56, y), 30, Paper, 0, 0, true);
                f.Draw(c, $"X {ft.Position.x:0}  Z {ft.Position.z:0}  Y {ft.Position.y:0}   {r.Grid.CellName(ft.Position)}", new Vector2(x + 56, y + 34), 26, dim);
                y += 78;
            }
            Text($"Terrain trees: {r.Trees.Count:N0} (painted on the terrain; visible in the render)", 30, dim);

            Head("NOT FOUND IN THE SCENE DATA");
            foreach (var n in MissingFeatureNotes(r)) Text(n, 30, dim);
        }

        static IEnumerable<string> MissingFeatureNotes(Run r)
        {
            var names = UnityEngine.Object.FindObjectsOfType<Transform>().Select(t => t.name.ToLowerInvariant()).ToList();
            bool roads = names.Any(n => n.Contains("road") || n.Contains("path") || n.Contains("trail") || n.Contains("street"));
            bool rivers = names.Any(n => n.Contains("river") || n.Contains("stream") || n.Contains("lake"));
            if (!roads) yield return "No road or path objects; trails exist only if painted into the terrain texture.";
            if (!rivers) yield return "No river or lake objects. Only the ocean plane at sea level.";
            if (r.Lakes.Count > 0) yield return $"{r.Lakes.Count} enclosed basin(s) lie below sea level, so the ocean plane shows inside them.";
            else if (r.HasSea) yield return "No enclosed basins below sea level (no inland water).";
        }

        static Color Hypso(float aboveSea)
        {
            var stops = new (float h, Color c)[]
            {
                (0f, new Color(0.86f, 0.82f, 0.62f)), (8f, new Color(0.66f, 0.77f, 0.48f)), (60f, new Color(0.44f, 0.63f, 0.36f)),
                (140f, new Color(0.67f, 0.62f, 0.42f)), (240f, new Color(0.60f, 0.49f, 0.40f)), (360f, new Color(0.93f, 0.93f, 0.93f)),
            };
            if (aboveSea <= stops[0].h) return stops[0].c;
            for (int i = 1; i < stops.Length; i++)
                if (aboveSea <= stops[i].h) return Color.Lerp(stops[i - 1].c, stops[i].c, (aboveSea - stops[i - 1].h) / (stops[i].h - stops[i - 1].h));
            return stops[stops.Length - 1].c;
        }

        static MapCanvas ComposeAnalysis(Run r, out MapProjection proj)
        {
            const int Strip = 470;
            int W = r.Nx, Hh = r.Nz + Strip;
            proj = MapProjection.Create(r.World, new Vector2Int(r.Nx, r.Nz), Vector2Int.zero, new Vector2Int(W, Hh));
            var p = proj;
            var font = new MapFont(26, 30, 34, 40, 56);
            var c = new MapCanvas(W, Hh, Panel);
            float sea = r.HasSea ? r.Sea : r.MinH;
            for (int y = 0; y < r.Nz; y++)
                for (int ix = 0; ix < r.Nx; ix++)
                {
                    int i = r.Idx(ix, y);
                    Color col;
                    if (r.OceanWater[i] || r.InlandWater[i]) col = Color.Lerp(new Color(0.60f, 0.80f, 0.88f), new Color(0.20f, 0.40f, 0.60f), Mathf.Clamp01((sea - r.H[i]) / 25f));
                    else col = Hypso(r.H[i] - sea) * Mathf.Lerp(1f, Mathf.Clamp(r.Shade[i], 0.3f, 1.3f), 0.6f);
                    if (r.Flat[i]) col = Color.Lerp(col, FlatCol, 0.42f);
                    c.Px[i] = col;
                }
            DrawContours(r, c, 0, 0, 0f, 0.45f, false);
            DrawCoast(r, c, 0, 0, Coast);
            var tree = new Color(0.05f, 0.24f, 0.08f, 0.85f);
            foreach (var t in r.Trees) c.Circle(p.WorldToPixel(t), 1.3f, tree);
            var g = r.Grid;
            for (int i = 0; i <= g.Columns; i++) { float gx = g.NorthWestCorner.x + i * g.CellSize; c.Line(p.WorldToPixel(new Vector2(gx, r.World.yMin)), p.WorldToPixel(new Vector2(gx, r.World.yMax)), new Color(0, 0, 0, 0.45f), 2f); }
            for (int j = 0; j <= g.Rows; j++) { float gz = g.NorthWestCorner.y - j * g.CellSize; c.Line(p.WorldToPixel(new Vector2(r.World.xMin, gz)), p.WorldToPixel(new Vector2(r.World.xMax, gz)), new Color(0, 0, 0, 0.45f), 2f); }
            for (int j = 0; j < g.Rows; j++)
                for (int i = 0; i < g.Columns; i++)
                {
                    var cr = g.CellRect(new Vector2Int(i, j));
                    font.Draw(c, g.CellName(new Vector2Int(i, j)), p.WorldToPixel(new Vector2(Mathf.Max(cr.xMin, r.World.xMin), Mathf.Min(cr.yMax, r.World.yMax))) + new Vector2(10, 10), 30, Color.white, 0, 0, true, new Color(0, 0, 0, 0.8f), 3f);
                }
            DrawFeatures(r, c, font, p, true);
            DrawPeaks(r, c, font, p);
            c.RectOutline(new Vector2(0, 0), new Vector2(r.Nx, r.Nz), Boundary, 5f);

            // legend strip
            float y0 = r.Nz + 40, x = 40;
            font.Draw(c, "TERRAIN ANALYSIS  -  derived from the heightmap, terrain trees and scene objects", new Vector2(x, y0), 40, Color.white, 0, 0, true);
            float ry = y0 + 80, rw = 1400;
            for (int i = 0; i < rw; i++)
            {
                float h = i / rw * (r.MaxH - sea);
                c.FillRect(x + i, ry, x + i + 1, ry + 40, Hypso(h));
            }
            for (int k = 0; k <= 4; k++) font.Draw(c, $"Y {sea + k * (r.MaxH - sea) / 4f:0}", new Vector2(x + k * rw / 4f, ry + 52), 26, Paper, k == 0 ? 0f : k == 4 ? 1f : 0.5f, 0f);
            font.Draw(c, "Elevation (world Y, above sea level)", new Vector2(x, ry + 100), 30, Paper);
            float lx = x + rw + 120, ly = y0 + 80;
            void Key(Color col, params string[] lines)
            {
                c.FillRect(lx, ly, lx + 60, ly + 36, col);
                for (int k = 0; k < lines.Length; k++) font.Draw(c, lines[k], new Vector2(lx + 80, ly + 18 + k * 40), 30, Paper, 0, 0.5f);
                ly += 56 + (lines.Length - 1) * 40;
            }
            Key(Color.Lerp(Hypso(30), FlatCol, 0.42f), $"Flat open ground: slope <= {r.S.FlatSlopeDegrees:0} deg, >= 2 m above sea, no terrain tree within ~{r.TreeCellSize:0}-{r.TreeCellSize * 2:0} m,",
                $">= {r.S.StructureClearance:0} m from existing structures. Geometry only - not a site recommendation.");
            Key(new Color(0.05f, 0.24f, 0.08f), $"Terrain tree ({r.Trees.Count:N0})");
            Key(new Color(0.40f, 0.62f, 0.76f), r.HasSea ? $"Below sea level Y {r.Sea:0} (ocean{(r.Lakes.Count > 0 ? " / enclosed basins" : "")})" : "No sea level found");
            Key(FeatureCol, "Existing structure (numbers match the developer map)");
            font.Draw(c, $"Grid {r.Grid.CellSize:0} m (same cells as the developer map). Bold contours every {r.S.IndexContourInterval:0} m.", new Vector2(x, ry + 160), 30, Paper);
            font.Draw(c, $"North = up = world +Z.  {p.MetresPerPixel:F4} m per pixel.", new Vector2(x, ry + 205), 30, Paper);
            font.Draw(c, $"X = px x {p.MetresPerPixel:F4}     Z = {r.World.yMax:0} - py x {p.MetresPerPixel:F4}   (px, py from the top-left corner)", new Vector2(x, ry + 250), 30, Paper);
            return c;
        }

        // ------------------------------------------------------------------ data outputs
        static void ValidateData(Run r)
        {
            // pixel <-> world round trip
            float rt = 0;
            var rng = new System.Random(7);
            for (int k = 0; k < 2000; k++)
            {
                var w = new Vector2(r.World.xMin + (float)rng.NextDouble() * r.World.width, r.World.yMin + (float)rng.NextDouble() * r.World.height);
                rt = Mathf.Max(rt, (r.Proj.PixelToWorldXZ(r.Proj.WorldToPixel(w)) - w).magnitude);
            }
            r.Validation.Add($"{(rt < 0.01f ? "PASS" : "FAIL")} world->pixel->world round trip: max error {rt:F5} m over 2000 points");
            // corners
            var c0 = r.Proj.WorldToPixel(new Vector2(r.World.xMin, r.World.yMax));
            var c1 = r.Proj.WorldToPixel(new Vector2(r.World.xMax, r.World.yMin));
            r.Validation.Add($"{(c0.magnitude < 1e-3f && (c1 - new Vector2(r.Nx, r.Nz)).magnitude < 1e-2f ? "PASS" : "FAIL")} corners: NW world corner -> px {c0}, SE world corner -> px {c1}");
            // height grid vs terrain
            var tmp = ScriptableObject.CreateInstance<IslandMapData>();
            FillHeights(r, tmp);
            float maxErr = 0, sum = 0;
            for (int k = 0; k < 2000; k++)
            {
                var w = new Vector2(r.World.xMin + (float)rng.NextDouble() * r.World.width, r.World.yMin + (float)rng.NextDouble() * r.World.height);
                float e = Mathf.Abs(tmp.SampleHeight(w) - SampleTerrain(r.Terrains, w));
                maxErr = Mathf.Max(maxErr, e); sum += e;
            }
            UnityEngine.Object.DestroyImmediate(tmp);
            r.Validation.Add($"INFO stored height grid ({r.S.HeightGridResolution}^2, {r.World.width / (r.S.HeightGridResolution - 1):F2} m spacing) vs live terrain: mean error {sum / 2000:F2} m, max {maxErr:F2} m (largest on cliffs)");
            // features sit inside the map
            int outside = r.Features.Count(f => !r.Proj.ContainsWorld(f.Position));
            r.Validation.Add($"{(outside == 0 ? "PASS" : "WARN")} {r.Features.Count - outside}/{r.Features.Count} existing features lie inside the mapped area");
        }

        static void FillHeights(Run r, IslandMapData d)
        {
            int n = r.S.HeightGridResolution;
            var g = new float[n, n];
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    var w = r.Proj.NormalizedToWorldXZ(new Vector2(x / (float)(n - 1), z / (float)(n - 1)));
                    float h = SampleTerrain(r.Terrains, w);
                    g[z, x] = float.IsNaN(h) ? r.MinH : h;
                }
            d.MinHeight = r.MinH; d.MaxHeight = r.MaxH; d.Overhead = r.Proj;
            d.SetHeightGrid(g);
        }

        static List<CellStats> CellStatistics(Run r)
        {
            var g = r.Grid; var list = new List<CellStats>();
            float px = r.Proj.MetresPerPixel;
            for (int j = 0; j < g.Rows; j++)
                for (int i = 0; i < g.Columns; i++)
                {
                    var cell = new Vector2Int(i, j); var cr = g.CellRect(cell);
                    var a = Vector2Int.FloorToInt(r.Proj.WorldToPixel(new Vector2(cr.xMin, cr.yMax)));
                    var b = Vector2Int.CeilToInt(r.Proj.WorldToPixel(new Vector2(cr.xMax, cr.yMin)));
                    float mn = float.MaxValue, mx = float.MinValue; double sum = 0; int n = 0, land = 0, flat = 0;
                    for (int y = Mathf.Max(0, a.y); y < Mathf.Min(r.Nz, b.y); y++)
                        for (int x = Mathf.Max(0, a.x); x < Mathf.Min(r.Nx, b.x); x++)
                        {
                            int k = r.Idx(x, y); float h = r.H[k];
                            mn = Mathf.Min(mn, h); mx = Mathf.Max(mx, h); sum += h; n++;
                            if (!r.OceanWater[k] && !r.InlandWater[k]) land++;
                            if (r.Flat[k]) flat++;
                        }
                    if (n == 0) continue;
                    list.Add(new CellStats
                    {
                        Cell = g.CellName(cell), WorldXZ = cr, MinY = mn, MaxY = mx, MeanY = (float)(sum / n),
                        LandFraction = (float)land / n, FlatOpenFraction = (float)flat / n,
                        Trees = r.Trees.Count(t => cr.Contains(t)),
                        Features = r.Features.Where(f => cr.Contains(new Vector2(f.Position.x, f.Position.z))).Select(f => f.Number + " " + f.Name).ToArray(),
                    });
                }
            return list;
        }

        [Serializable]
        class MapJson
        {
            public int GeneratorVersion; public string SourceScene; public string GeneratedUtc;
            public string CoordinateSystem = "Unity world space, metres. Map up = north = +Z, right = east = +X. Pixel coordinates from the image's top-left corner.";
            public string OverheadImage, DeveloperImage, AnalysisImage;
            public MapProjection Overhead, DeveloperMap, Analysis;
            public MapGrid Grid;
            public bool HasSeaLevel; public float SeaLevel; public string SeaLevelSource;
            public float MinHeight, MaxHeight;
            public List<IslandMapData.TerrainEntry> Terrains;
            public List<IslandMapData.MapFeature> ExistingFeatures;
            public List<Peak> SpotHeights;
            public List<WaterBody> EnclosedBasinsBelowSeaLevel;
            public int TerrainTreeCount;
            public string FlatOpenGroundDefinition;
            public List<CellStats> Cells;
            public List<string> Notes;
        }

        static List<IslandMapData.TerrainEntry> TerrainEntries(Run r) => r.Terrains.Select(t => new IslandMapData.TerrainEntry
        {
            Name = t.name, DataAsset = AssetDatabase.GetAssetPath(t.terrainData), Position = t.transform.position, Size = t.terrainData.size,
            HeightmapResolution = t.terrainData.heightmapResolution, Layers = t.terrainData.terrainLayers.Select(l => l ? LayerName(l) : "null").ToArray(), TreeCount = t.terrainData.treeInstanceCount,
        }).ToList();

        static void WriteJson(Run r, MapProjection dev, MapProjection ana, List<CellStats> cells)
        {
            var j = new MapJson
            {
                GeneratorVersion = Version, SourceScene = r.S.ScenePath, GeneratedUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                OverheadImage = OverheadFile, DeveloperImage = DevMapFile, AnalysisImage = AnalysisFile,
                Overhead = r.Proj, DeveloperMap = dev, Analysis = ana, Grid = r.Grid,
                HasSeaLevel = r.HasSea, SeaLevel = r.HasSea ? r.Sea : 0f, SeaLevelSource = r.SeaSource,
                MinHeight = r.MinH, MaxHeight = r.MaxH, Terrains = TerrainEntries(r), ExistingFeatures = r.Features, SpotHeights = r.Peaks,
                EnclosedBasinsBelowSeaLevel = r.Lakes, TerrainTreeCount = r.Trees.Count,
                FlatOpenGroundDefinition = $"slope <= {r.S.FlatSlopeDegrees} deg, >= 2 m above sea level, no terrain tree in the surrounding 3x3 block of {r.TreeCellSize} m cells, >= {r.S.StructureClearance} m from existing structure footprints. Geometric only; not a site recommendation.",
                Cells = cells, Notes = r.Notes.Concat(MissingFeatureNotes(r)).ToList(),
            };
            File.WriteAllText(Path.Combine(r.S.OutDir, JsonFile), JsonUtility.ToJson(j, true));
        }

        static void EnsureMarkersFile(string dir)
        {
            string path = Path.Combine(dir, MarkersFile);
            if (!File.Exists(path)) File.WriteAllText(path, new MapMarkerSet().ToJson());
        }

        static IslandMapData WriteDataAsset(Run r, MapProjection dev, MapProjection ana)
        {
            if (!AssetDatabase.IsValidFolder("Assets/_IslandMap/Data")) AssetDatabase.CreateFolder("Assets/_IslandMap", "Data");
            var d = AssetDatabase.LoadAssetAtPath<IslandMapData>(DataAssetPath);
            bool create = d == null;
            if (create) d = ScriptableObject.CreateInstance<IslandMapData>();
            d.SourceScene = r.S.ScenePath; d.GeneratedUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"); d.GeneratorVersion = Version;
            d.DeveloperMap = dev; d.Analysis = ana; d.Grid = r.Grid; d.Terrains = TerrainEntries(r);
            d.SeaLevel = r.HasSea ? r.Sea : float.NaN; d.Features = r.Features;
            FillHeights(r, d);
            if (create) AssetDatabase.CreateAsset(d, DataAssetPath); else EditorUtility.SetDirty(d);
            AssetDatabase.SaveAssetIfDirty(d);
            return d;
        }
    }
}
