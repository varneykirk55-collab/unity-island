// Editor-only, READ-ONLY survey tooling. Never saves any scene or asset.
//  1) Renders key imported assets in a temporary unsaved scene (URP material check).
//  2) Analyses Scene 1 terrain (heights, slopes, layers) and scores candidate sites.
//  3) Renders aerial views of Scene 1 and candidate sites.
// Batchmode: Unity.exe -batchmode -quit -projectPath <proj> -executeMethod IslandTools.IslandSurvey.Run -surveyOut <dir>
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace IslandTools
{
    public static class IslandSurvey
    {
        const string Scene1Path = "Assets/Free Island Collection/Scenes/Scene 1.unity";
        static string outDir;
        static readonly StringBuilder R = new StringBuilder();
        static void Note(string s) { R.AppendLine(s); Debug.Log("[IslandSurvey] " + s); }

        static readonly string[] TestAssets =
        {
            "Pagoda_Goju_36m_LOD", "Pagoda_Sanju_13m_LOD", "Pagoda_Tahoto_14m_LOD",
            "Torii_Inari_Med1_LOD", "Torii_Myoujin_Med1_LOD",
            "Lantern_Stone_A1", "Lantern_Stone_C1", "Platform_Stone_A1",
            "Torii (Double Color)", "Small Rusted House",
            "Tree_BlackPine_Mid_Green_01", "Tree_Sakura_Mid_Spring_01", "Tree_Bamboo_Mid_Green_01",
            "Plant_Boxwood_Spring_01", "JG_Rock_A_01",
        };

        static readonly Dictionary<string, Vector3> Footprints = new Dictionary<string, Vector3>();

        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-surveyOut");
            outDir = i >= 0 && i + 1 < args.Length ? args[i + 1] : "_Backups/survey";
            Directory.CreateDirectory(outDir);
            try
            {
                AssetRenderTest();
                TerrainSurvey();
            }
            catch (Exception e) { Note("EXCEPTION: " + e); throw; }
            finally
            {
                File.WriteAllText(Path.Combine(outDir, "survey_report.txt"), R.ToString());
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); // discard, never save
            }
        }

        // ------------------------------------------------------------------ asset render test
        static void AssetRenderTest()
        {
            Note("=== ASSET RENDER TEST (temporary unsaved scene) ===");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var sky = AssetDatabase.LoadAssetAtPath<Material>("Assets/Free Island Collection/Environment/Skies/Sky Material.mat");
            if (sky != null) RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            DynamicGI.UpdateEnvironment();
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.2f; sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(45, -35, 0);

            var cam = NewCam();
            foreach (var name in TestAssets)
            {
                var path = FindPrefab(name);
                if (path == null) { Note("  NOT FOUND: " + name); continue; }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                go.transform.position = Vector3.zero;
                var b = Bounds(go);
                Footprints[name] = b.size;

                var shaders = go.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials)
                    .Where(m => m != null).Select(m => m.shader != null ? m.shader.name : "<null>").Distinct().ToList();
                bool bad = shaders.Any(s => !s.StartsWith("Universal Render Pipeline") && !s.StartsWith("Shader Graphs"));
                Note(string.Format("  {0}: size {1:0.0} x {2:0.0} x {3:0.0} m (WxHxD)  shaders: {4}{5}",
                    name, b.size.x, b.size.y, b.size.z, string.Join(", ", shaders), bad ? "   <-- NON-URP SHADER" : ""));

                float radius = b.extents.magnitude;
                var center = b.center;
                cam.transform.position = center + new Vector3(-0.8f, 0.35f, -1.0f).normalized * radius * 2.4f;
                cam.transform.LookAt(center);
                cam.nearClipPlane = Mathf.Max(0.05f, radius * 0.01f); cam.farClipPlane = radius * 20f;
                Shoot(cam, "asset_" + Safe(name) + ".png");

                if (name.StartsWith("Pagoda"))
                {
                    // close-up of the base for decal / z-fighting check
                    var basePt = new Vector3(center.x, b.min.y + b.size.y * 0.12f, center.z);
                    cam.transform.position = basePt + new Vector3(-0.6f, 0.15f, -1f).normalized * Mathf.Max(b.size.x, b.size.z) * 0.9f;
                    cam.transform.LookAt(basePt);
                    Shoot(cam, "asset_" + Safe(name) + "_closeup.png");
                }
                UnityEngine.Object.DestroyImmediate(go);
            }
            UnityEngine.Object.DestroyImmediate(cam.gameObject);
        }

        // ------------------------------------------------------------------ terrain survey
        struct Site { public Vector3 p; public float score, flat, slope, coastDist; public string layer; }

        static void TerrainSurvey()
        {
            Note("");
            Note("=== SCENE 1 TERRAIN SURVEY (read-only; scene is not saved) ===");
            var scene = EditorSceneManager.OpenScene(Scene1Path, OpenSceneMode.Single);
            var terrain = UnityEngine.Object.FindObjectOfType<Terrain>();
            var td = terrain.terrainData;
            var tpos = terrain.transform.position;
            int res = td.heightmapResolution;
            var size = td.size;
            Note(string.Format("Terrain '{0}' pos {1} size {2:0} x {3:0} x {4:0} m, heightmap {5}, alphamap {6}",
                terrain.name, tpos, size.x, size.y, size.z, res, td.alphamapResolution));
            Note("Layers: " + string.Join(", ", td.terrainLayers.Select((l, k) => k + ":" + (l ? l.name : "null"))));
            Note("Tree prototypes: " + td.treePrototypes.Length + "  tree instances: " + td.treeInstanceCount +
                 (td.treePrototypes.Length > 0 ? "  [" + string.Join(", ", td.treePrototypes.Select(t => t.prefab ? t.prefab.name : "null")) + "]" : ""));
            Note("Detail prototypes: " + td.detailPrototypes.Length +
                 (td.detailPrototypes.Length > 0 ? "  [" + string.Join(", ", td.detailPrototypes.Select(d => d.prototypeTexture ? d.prototypeTexture.name : (d.prototype ? d.prototype.name : "null"))) + "]" : ""));
            foreach (var go in scene.GetRootGameObjects())
                Note("Scene object: " + go.name + " @ " + go.transform.position);

            float[,] h = td.GetHeights(0, 0, res, res); // [z, x], 0..1
            float cell = size.x / (res - 1);

            // sea level estimate: edge heights
            var edge = new List<float>();
            for (int k = 0; k < res; k += 4) { edge.Add(h[0, k]); edge.Add(h[res - 1, k]); edge.Add(h[k, 0]); edge.Add(h[k, res - 1]); }
            edge.Sort();
            float edgeMed = edge[edge.Count / 2] * size.y + tpos.y;
            float edgeMax = edge[edge.Count - 1] * size.y + tpos.y;
            float maxH = 0; for (int z = 0; z < res; z++) for (int x = 0; x < res; x++) maxH = Mathf.Max(maxH, h[z, x]);
            maxH = maxH * size.y + tpos.y;

            // Sand layer heights give a better shoreline estimate
            int sandIdx = Array.FindIndex(td.terrainLayers, l => l && l.name.ToLower().Contains("sand"));
            var am = td.GetAlphamaps(0, 0, td.alphamapResolution, td.alphamapResolution);
            int ar = td.alphamapResolution;
            float seaLevel = edgeMed + 1f;
            if (sandIdx >= 0)
            {
                var sandH = new List<float>();
                for (int z = 0; z < ar; z += 2) for (int x = 0; x < ar; x += 2)
                    if (am[z, x, sandIdx] > 0.6f) sandH.Add(HeightAt(h, res, (float)x / (ar - 1), (float)z / (ar - 1)) * size.y + tpos.y);
                if (sandH.Count > 50)
                {
                    sandH.Sort();
                    Note(string.Format("Sand-dominant heights: p5={0:0.0} p25={1:0.0} p50={2:0.0} p95={3:0.0} m", P(sandH, .05f), P(sandH, .25f), P(sandH, .5f), P(sandH, .95f)));
                    seaLevel = Mathf.Max(seaLevel, P(sandH, .05f));
                }
            }
            Note(string.Format("Edge height median {0:0.0} m, edge max {1:0.0} m, terrain max {2:0.0} m. Estimated sea level ~{3:0.0} m (no water object in scene)", edgeMed, edgeMax, maxH, seaLevel));

            // grid sample
            float step = 8f;
            int nx = Mathf.FloorToInt(size.x / step), nz = Mathf.FloorToInt(size.z / step);
            var land = new bool[nx, nz];
            var hgt = new float[nx, nz];
            int landCount = 0;
            for (int gx = 0; gx < nx; gx++) for (int gz = 0; gz < nz; gz++)
            {
                float u = (gx * step) / size.x, v = (gz * step) / size.z;
                hgt[gx, gz] = HeightAt(h, res, u, v) * size.y + tpos.y;
                land[gx, gz] = hgt[gx, gz] > seaLevel + 0.5f;
                if (land[gx, gz]) landCount++;
            }
            Note(string.Format("Land area above sea level: ~{0:0.00} km2", landCount * step * step / 1e6f));
            var coast = new List<Vector2Int>();
            for (int gx = 1; gx < nx - 1; gx++) for (int gz = 1; gz < nz - 1; gz++)
                if (land[gx, gz] && (!land[gx + 1, gz] || !land[gx - 1, gz] || !land[gx, gz + 1] || !land[gx, gz - 1])) coast.Add(new Vector2Int(gx, gz));
            Note("Coastline cells: " + coast.Count);

            // land elevation percentiles
            var landH = new List<float>();
            for (int gx = 0; gx < nx; gx++) for (int gz = 0; gz < nz; gz++) if (land[gx, gz]) landH.Add(hgt[gx, gz]);
            landH.Sort();
            if (landH.Count == 0) { Note("No land found above estimated sea level."); return; }
            Note(string.Format("Land elevation: p25={0:0} p50={1:0} p75={2:0} p90={3:0} max={4:0} m", P(landH, .25f), P(landH, .5f), P(landH, .75f), P(landH, .9f), landH.Last()));

            Func<string, float> fpRadius = n => Footprints.TryGetValue(n, out var s) ? 0.5f * Mathf.Sqrt(s.x * s.x + s.z * s.z) : 12f;
            float rGoju = fpRadius("Pagoda_Goju_36m_LOD") + 6f;
            float rSanju = fpRadius("Pagoda_Sanju_13m_LOD") + 5f;
            Note(string.Format("Required flat radius: Goju (5-storey) {0:0} m, Sanju (3-storey) {1:0} m, garden 25 m", rGoju, rSanju));

            var pag = FindSites(td, tpos, size, h, res, am, ar, hgt, land, coast, nx, nz, step, rGoju, P(landH, .6f), 1.8f, 250f, true);
            var small = FindSites(td, tpos, size, h, res, am, ar, hgt, land, coast, nx, nz, step, rSanju, P(landH, .3f), 1.2f, 200f, true);
            var garden = FindSites(td, tpos, size, h, res, am, ar, hgt, land, coast, nx, nz, step, 25f, P(landH, .15f), 2.0f, 200f, false);

            ReportSites("PAGODA (5-storey Goju) candidates - high, flat, visible", pag, seaLevel);
            ReportSites("SECONDARY SHRINE (3-storey Sanju / Tahoto) candidates", small, seaLevel);
            ReportSites("GARDEN CLEARING candidates (flat, gentle, 25 m radius)", garden, seaLevel);

            // approach path profile from nearest coast to each top pagoda site
            foreach (var s in pag.Take(3))
            {
                var c = NearestCoast(coast, s.p, tpos, step);
                float maxGrade = 0, len = Vector2.Distance(new Vector2(c.x, c.z), new Vector2(s.p.x, s.p.z));
                int n = Mathf.Max(2, Mathf.CeilToInt(len / 4f));
                float prev = Sample(terrain, c);
                for (int k = 1; k <= n; k++)
                {
                    var q = Vector3.Lerp(c, s.p, k / (float)n);
                    float hh = Sample(terrain, q);
                    maxGrade = Mathf.Max(maxGrade, Mathf.Abs(hh - prev) / (len / n));
                    prev = hh;
                }
                Note(string.Format("  Approach to ({0:0},{1:0}) from coast ({2:0},{3:0}): {4:0} m long, climb {5:0} m, steepest grade {6:0}%",
                    s.p.x, s.p.z, c.x, c.z, len, s.p.y - Sample(terrain, c), maxGrade * 100f));
            }

            // map image
            WriteMap(h, res, size, tpos, seaLevel, am, ar, td, pag, small, garden);

            // aerial renders
            var cam = NewCam();
            cam.farClipPlane = 8000f;
            var center = tpos + new Vector3(size.x / 2, 0, size.z / 2);
            cam.orthographic = true; cam.orthographicSize = size.z / 2;
            cam.transform.position = center + Vector3.up * (maxH + 500f);
            cam.transform.rotation = Quaternion.Euler(90, 0, 0);
            Shoot(cam, "scene1_topdown.png", 1600, 1600);
            cam.orthographic = false; cam.fieldOfView = 50;
            string[] dirs = { "south", "west", "north", "east" };
            for (int k = 0; k < 4; k++)
            {
                var dir = Quaternion.Euler(0, k * 90, 0) * Vector3.back;
                cam.transform.position = center + dir * size.x * 0.75f + Vector3.up * (maxH * 1.2f + 150f);
                cam.transform.LookAt(center + Vector3.up * P(landH, .5f));
                Shoot(cam, "scene1_oblique_from_" + dirs[k] + ".png");
            }
            int idx = 1;
            foreach (var s in pag.Take(3))
            {
                cam.transform.position = s.p + new Vector3(-1, 0, -1).normalized * 160f + Vector3.up * 70f;
                cam.transform.LookAt(s.p + Vector3.up * 10f);
                Shoot(cam, "site_pagoda_" + idx++ + ".png");
            }
            idx = 1;
            foreach (var s in garden.Take(2))
            {
                cam.transform.position = s.p + new Vector3(1, 0, -1).normalized * 120f + Vector3.up * 50f;
                cam.transform.LookAt(s.p);
                Shoot(cam, "site_garden_" + idx++ + ".png");
            }
            UnityEngine.Object.DestroyImmediate(cam.gameObject);
        }

        static List<Site> FindSites(TerrainData td, Vector3 tpos, Vector3 size, float[,] h, int res, float[,,] am, int ar,
            float[,] hgt, bool[,] land, List<Vector2Int> coast, int nx, int nz, float step,
            float radius, float minElev, float maxDelta, float separation, bool preferHigh)
        {
            var list = new List<Site>();
            int ring = Mathf.CeilToInt(radius / step);
            for (int gx = ring; gx < nx - ring; gx++) for (int gz = ring; gz < nz - ring; gz++)
            {
                if (!land[gx, gz] || hgt[gx, gz] < minElev) continue;
                float h0 = hgt[gx, gz], dmax = 0; bool ok = true;
                for (int dx = -ring; dx <= ring && ok; dx++) for (int dz = -ring; dz <= ring; dz++)
                {
                    if (dx * dx + dz * dz > ring * ring) continue;
                    if (!land[gx + dx, gz + dz]) { ok = false; break; }
                    dmax = Mathf.Max(dmax, Mathf.Abs(hgt[gx + dx, gz + dz] - h0));
                }
                if (!ok || dmax > maxDelta) continue;
                float u = gx * step / size.x, v = gz * step / size.z;
                var p = new Vector3(tpos.x + gx * step, h0, tpos.z + gz * step);
                float cd = coast.Count == 0 ? 0 : coast.Min(c => (new Vector2(c.x - gx, c.y - gz)).sqrMagnitude);
                cd = Mathf.Sqrt(cd) * step;
                float slope = td.GetSteepness(u, v);
                float score = preferHigh ? h0 - dmax * 8f - Mathf.Max(0, cd - 600f) * 0.05f : -dmax * 10f - slope - Mathf.Abs(cd - 250f) * 0.02f;
                list.Add(new Site { p = p, score = score, flat = dmax, slope = slope, coastDist = cd, layer = Dominant(td, am, ar, u, v) });
            }
            var picked = new List<Site>();
            foreach (var s in list.OrderByDescending(s => s.score))
            {
                if (picked.Any(o => Vector2.Distance(new Vector2(o.p.x, o.p.z), new Vector2(s.p.x, s.p.z)) < separation)) continue;
                picked.Add(s);
                if (picked.Count >= 6) break;
            }
            return picked;
        }

        static void ReportSites(string title, List<Site> sites, float sea)
        {
            Note("");
            Note(title + ":");
            if (sites.Count == 0) { Note("  none found with current thresholds"); return; }
            int i = 1;
            foreach (var s in sites)
                Note(string.Format("  #{0} pos ({1:0}, {2:0}, {3:0})  elev above sea {4:0} m  height variation {5:0.0} m  slope {6:0} deg  {7:0} m from coast  ground: {8}",
                    i++, s.p.x, s.p.y, s.p.z, s.p.y - sea, s.flat, s.slope, s.coastDist, s.layer));
        }

        static Vector3 NearestCoast(List<Vector2Int> coast, Vector3 p, Vector3 tpos, float step)
        {
            var best = coast.OrderBy(c => (new Vector2(tpos.x + c.x * step - p.x, tpos.z + c.y * step - p.z)).sqrMagnitude).First();
            return new Vector3(tpos.x + best.x * step, 0, tpos.z + best.y * step);
        }

        static float Sample(Terrain t, Vector3 p) => t.SampleHeight(p) + t.transform.position.y;

        static string Dominant(TerrainData td, float[,,] am, int ar, float u, float v)
        {
            int x = Mathf.Clamp(Mathf.RoundToInt(u * (ar - 1)), 0, ar - 1), z = Mathf.Clamp(Mathf.RoundToInt(v * (ar - 1)), 0, ar - 1);
            int best = 0; float bw = -1;
            for (int l = 0; l < am.GetLength(2); l++) if (am[z, x, l] > bw) { bw = am[z, x, l]; best = l; }
            var n = td.terrainLayers[best] ? td.terrainLayers[best].name : "?";
            int cut = n.IndexOf("_Diffuse"); if (cut > 0) n = n.Substring(0, cut);
            return n.Replace("layer_", "");
        }

        static float HeightAt(float[,] h, int res, float u, float v)
        {
            float fx = Mathf.Clamp01(u) * (res - 1), fz = Mathf.Clamp01(v) * (res - 1);
            int x0 = Mathf.FloorToInt(fx), z0 = Mathf.FloorToInt(fz);
            int x1 = Mathf.Min(x0 + 1, res - 1), z1 = Mathf.Min(z0 + 1, res - 1);
            float tx = fx - x0, tz = fz - z0;
            return Mathf.Lerp(Mathf.Lerp(h[z0, x0], h[z0, x1], tx), Mathf.Lerp(h[z1, x0], h[z1, x1], tx), tz);
        }

        static float P(List<float> sorted, float p) => sorted[Mathf.Clamp(Mathf.RoundToInt(p * (sorted.Count - 1)), 0, sorted.Count - 1)];

        static void WriteMap(float[,] h, int res, Vector3 size, Vector3 tpos, float sea, float[,,] am, int ar, TerrainData td,
            List<Site> pag, List<Site> small, List<Site> garden)
        {
            int W = 1024;
            var tex = new Texture2D(W, W, TextureFormat.RGB24, false);
            var px = new Color[W * W];
            float maxH = size.y;
            for (int y = 0; y < W; y++) for (int x = 0; x < W; x++)
            {
                float u = x / (float)(W - 1), v = y / (float)(W - 1);
                float hv = HeightAt(h, res, u, v) * size.y + tpos.y;
                float hx = HeightAt(h, res, u + 1f / W, v) * size.y - HeightAt(h, res, u - 1f / W, v) * size.y;
                float hz = HeightAt(h, res, u, v + 1f / W) * size.y - HeightAt(h, res, u, v - 1f / W) * size.y;
                var n = new Vector3(-hx, 2f * size.x / W * 2f, -hz).normalized;
                float shade = Mathf.Clamp01(Vector3.Dot(n, new Vector3(-0.5f, 0.7f, -0.5f).normalized)) * 0.7f + 0.3f;
                Color c;
                if (hv <= sea) c = Color.Lerp(new Color(0.05f, 0.15f, 0.35f), new Color(0.2f, 0.45f, 0.6f), Mathf.Clamp01((hv - (sea - 30)) / 30f));
                else
                {
                    float t = Mathf.Clamp01((hv - sea) / Mathf.Max(1, maxH * 0.5f));
                    c = Color.Lerp(new Color(0.85f, 0.8f, 0.6f), new Color(0.3f, 0.55f, 0.25f), Mathf.Clamp01(t * 6f));
                    c = Color.Lerp(c, new Color(0.55f, 0.5f, 0.45f), Mathf.Clamp01((t - 0.4f) * 2f));
                    c *= shade;
                }
                px[y * W + x] = c;
            }
            // grid lines every 250 m
            for (int k = 0; k <= 10; k++)
            {
                int g = Mathf.Clamp(Mathf.RoundToInt(k * 250f / size.x * (W - 1)), 0, W - 1);
                for (int t = 0; t < W; t++) { px[g * W + t] = Color.Lerp(px[g * W + t], Color.white, 0.35f); px[t * W + g] = Color.Lerp(px[t * W + g], Color.white, 0.35f); }
            }
            Mark(px, W, size, tpos, pag, Color.red, 9);
            Mark(px, W, size, tpos, small, new Color(1f, 0.6f, 0f), 7);
            Mark(px, W, size, tpos, garden, Color.magenta, 7);
            tex.SetPixels(px); tex.Apply();
            File.WriteAllBytes(Path.Combine(outDir, "scene1_sitemap.png"), tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            Note("");
            Note("Site map: scene1_sitemap.png (north = up, x right). Grid every 250 m. RED = pagoda, ORANGE = secondary shrine, MAGENTA = garden. Marker size shrinks with rank (#1 largest).");
        }

        static void Mark(Color[] px, int W, Vector3 size, Vector3 tpos, List<Site> sites, Color col, int r0)
        {
            int rank = 0;
            foreach (var s in sites)
            {
                int r = Mathf.Max(3, r0 - rank * 1); rank++;
                int cx = Mathf.RoundToInt((s.p.x - tpos.x) / size.x * (W - 1)), cy = Mathf.RoundToInt((s.p.z - tpos.z) / size.z * (W - 1));
                for (int dy = -r - 1; dy <= r + 1; dy++) for (int dx = -r - 1; dx <= r + 1; dx++)
                {
                    int x = cx + dx, y = cy + dy; if (x < 0 || y < 0 || x >= W || y >= W) continue;
                    int d2 = dx * dx + dy * dy;
                    if (d2 <= r * r) px[y * W + x] = col;
                    else if (d2 <= (r + 1) * (r + 1)) px[y * W + x] = Color.black;
                }
            }
        }

        // ------------------------------------------------------------------ helpers
        static Camera NewCam()
        {
            var cam = new GameObject("SurveyCam").AddComponent<Camera>();
            cam.allowHDR = false; cam.allowMSAA = true; cam.fieldOfView = 40;
            cam.clearFlags = CameraClearFlags.Skybox;
            return cam;
        }

        static void Shoot(Camera cam, string file, int w = 1280, int h = 720)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = prev; cam.targetTexture = null;
            File.WriteAllBytes(Path.Combine(outDir, file), tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex); rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
        }

        static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one);
            var lod = go.GetComponentInChildren<LODGroup>();
            IEnumerable<Renderer> use = rs;
            if (lod != null) { var l0 = lod.GetLODs(); if (l0.Length > 0 && l0[0].renderers.Length > 0) use = l0[0].renderers.Where(r => r != null); }
            var b = use.First().bounds; foreach (var r in use) b.Encapsulate(r.bounds);
            return b;
        }

        static string FindPrefab(string name)
        {
            foreach (var g in AssetDatabase.FindAssets("t:Prefab " + name))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (Path.GetFileNameWithoutExtension(p) == name) return p;
            }
            return null;
        }

        public static void PineDiagnostic()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-surveyOut");
            outDir = i >= 0 && i + 1 < args.Length ? args[i + 1] : "_Backups/survey";
            Directory.CreateDirectory(outDir);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var sky = AssetDatabase.LoadAssetAtPath<Material>("Assets/Free Island Collection/Environment/Skies/Sky Material.mat");
            if (sky != null) RenderSettings.skybox = sky;
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional; sun.transform.rotation = Quaternion.Euler(45, -35, 0);
            var cam = NewCam();
            string[] names = { "Tree_BlackPine_Mid_Green_01", "Tree_BlackPine_Mid_Green_01", "Tree_Sakura_Mid_Spring_01", "Tree_BlackPine_Mid_Green_01", "Tree_BlackPine_Big_Green_01", "Tree_BlackPine_Small_Dry_01" };
            for (int k = 0; k < names.Length; k++)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(FindPrefab(names[k])));
                var b = Bounds(go); float r = b.extents.magnitude;
                cam.transform.position = b.center + new Vector3(-0.8f, 0.35f, -1f).normalized * r * 2.4f;
                cam.transform.LookAt(b.center); cam.nearClipPlane = 0.05f; cam.farClipPlane = r * 20;
                ShaderUtil.allowAsyncCompilation = false;
                Shoot(cam, "pine_diag_" + k + "_" + names[k] + ".png", 640, 360);
                UnityEngine.Object.DestroyImmediate(go);
            }
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        public static void DumpTextures()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-surveyOut");
            outDir = i >= 0 && i + 1 < args.Length ? args[i + 1] : "_Backups/survey";
            Directory.CreateDirectory(outDir);
            var sb = new StringBuilder();
            foreach (var g in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Waldemarst/FreeJapaneseGarden/Textures/Trees" }))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                var t = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                var imp = AssetImporter.GetAtPath(p) as TextureImporter;
                string info = t == null ? "LOAD FAILED" : string.Format("{0}x{1} {2} mips={3}", t.width, t.height, t.graphicsFormat, t.mipmapCount);
                sb.AppendLine(Path.GetFileName(p) + ": " + info + (imp != null ? " importerMax=" + imp.maxTextureSize : ""));
                if (t != null && (p.Contains("BlackPine") || p.Contains("Sakura_Spring") || p.Contains("Sakura_Bark")) && p.Contains("Albedo"))
                {
                    var rt = RenderTexture.GetTemporary(256, 256, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    Graphics.Blit(t, rt);
                    var prev = RenderTexture.active; RenderTexture.active = rt;
                    var tex = new Texture2D(256, 256, TextureFormat.RGBA32, false);
                    tex.ReadPixels(new Rect(0, 0, 256, 256), 0, 0); tex.Apply();
                    RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
                    var c = tex.GetPixels32(); long r = 0, gg = 0, b = 0; foreach (var px in c) { r += px.r; gg += px.g; b += px.b; }
                    sb.AppendLine(string.Format("    avg RGB {0} {1} {2}", r / c.Length, gg / c.Length, b / c.Length));
                    File.WriteAllBytes(Path.Combine(outDir, "tex_" + Path.GetFileNameWithoutExtension(p) + ".png"), tex.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(tex);
                }
            }
            File.WriteAllText(Path.Combine(outDir, "texture_report.txt"), sb.ToString());
        }

        static string Safe(string s) => new string(s.Select(ch => char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_').ToArray());
    }
}
