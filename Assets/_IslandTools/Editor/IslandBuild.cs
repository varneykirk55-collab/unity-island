// Editor-only build step for Scene 1:
//   - ocean water plane at 45 m (URP Lit transparent material)
//   - 5-storey Goju pagoda at survey site #2, 3-storey Sanju pagoda at survey site #3
//   - re-survey garden clearings + torii approach directions with the real sea level
//   - verification renders
// The terrain is never modified. Objects are placed under a root "Island_Environment".
// Batchmode: Unity.exe -batchmode -quit -projectPath <proj> -executeMethod IslandTools.IslandBuild.PlaceWaterAndPagodas -surveyOut <dir>
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
    public static class IslandBuild
    {
        const string Scene1Path = "Assets/Free Island Collection/Scenes/Scene 1.unity";
        const string EnvFolder = "Assets/_IslandEnvironment";
        const float SeaLevel = 45f;

        static readonly Vector2 Site2 = new Vector2(1064, 1432); // main Goju
        static readonly Vector2 Site3 = new Vector2(1568, 608);  // Sanju

        static string outDir;
        static readonly StringBuilder R = new StringBuilder();
        static void Note(string s) { R.AppendLine(s); Debug.Log("[IslandBuild] " + s); }

        public static void PlaceWaterAndPagodas()
        {
            var args = Environment.GetCommandLineArgs();
            int ai = Array.IndexOf(args, "-surveyOut");
            outDir = ai >= 0 && ai + 1 < args.Length ? args[ai + 1] : "_Backups/build";
            Directory.CreateDirectory(outDir);
            ShaderUtil.allowAsyncCompilation = false;
            try
            {
                var scene = EditorSceneManager.OpenScene(Scene1Path, OpenSceneMode.Single);
                var terrain = UnityEngine.Object.FindObjectOfType<Terrain>();

                var root = GameObject.Find("Island_Environment") ?? new GameObject("Island_Environment");
                var landmarks = Child(root, "Landmarks");

                CreateWater(root, terrain);
                var goju = PlacePagoda(terrain, landmarks, "Pagoda_Goju_36m_LOD", "Pagoda_Goju_Main (site 2)", Site2);
                var sanju = PlacePagoda(terrain, landmarks, "Pagoda_Sanju_13m_LOD", "Pagoda_Sanju_South (site 3)", Site3);

                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new Exception("Scene 1 save failed");
                Note("Scene 1 saved.");

                ApproachDirections(terrain, goju, "Goju (main)");
                ApproachDirections(terrain, sanju, "Sanju (south)");
                var gardens = GardenSites(terrain, goju.transform.position);

                Renders(terrain, goju, sanju, gardens);
            }
            catch (Exception e) { Note("EXCEPTION: " + e); throw; }
            finally { File.WriteAllText(Path.Combine(outDir, "build_report.txt"), R.ToString()); }
        }

        // ------------------------------------------------------------------ water
        static void CreateWater(GameObject root, Terrain terrain)
        {
            if (!AssetDatabase.IsValidFolder(EnvFolder)) AssetDatabase.CreateFolder("Assets", "_IslandEnvironment");
            if (!AssetDatabase.IsValidFolder(EnvFolder + "/Materials")) AssetDatabase.CreateFolder(EnvFolder, "Materials");
            const string matPath = EnvFolder + "/Materials/M_Ocean_URP.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, matPath); }
            mat.shader = Shader.Find("Universal Render Pipeline/Lit");
            mat.SetFloat("_WorkflowMode", 1);
            mat.SetFloat("_Surface", 1);   // transparent
            mat.SetFloat("_Blend", 0);     // alpha
            mat.SetFloat("_CastShadows", 0);
            mat.SetFloat("_ReceiveShadows", 1);
            mat.SetColor("_BaseColor", new Color(0.04f, 0.19f, 0.26f, 0.86f));
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Smoothness", 0.93f);
            mat.SetFloat("_SpecularHighlights", 1f);
            mat.SetFloat("_EnvironmentReflections", 1f);

            const string nPath = "Assets/SUIMONO - WATER SYSTEM 2/TEXTURES/tex_WaveCalm_normal.psd";
            var nImp = AssetImporter.GetAtPath(nPath) as TextureImporter;
            if (nImp != null && nImp.textureType == TextureImporterType.NormalMap)
            {
                mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(nPath));
                mat.SetTextureScale("_BumpMap", new Vector2(600, 600));
                mat.SetTextureScale("_BaseMap", new Vector2(600, 600));
                mat.SetFloat("_BumpScale", 0.35f);
                Note("Water: using SUIMONO calm-wave normal map (read-only reference) for ripples.");
            }
            else Note("Water: SUIMONO normal map not a NormalMap import (" + (nImp != null ? nImp.textureType.ToString() : "missing") + "); water has no ripple normals.");
            UnityEditor.BaseShaderGUI.SetMaterialKeywords(mat, UnityEditor.Rendering.Universal.ShaderGUI.LitGUI.SetMaterialKeywords, null);
            EditorUtility.SetDirty(mat);

            var old = root.transform.Find("Ocean");
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var water = GameObject.CreatePrimitive(PrimitiveType.Plane);
            water.name = "Ocean";
            UnityEngine.Object.DestroyImmediate(water.GetComponent<Collider>());
            water.transform.SetParent(root.transform, false);
            var td = terrain.terrainData;
            var c = terrain.transform.position + new Vector3(td.size.x / 2, 0, td.size.z / 2);
            water.transform.position = new Vector3(c.x, SeaLevel, c.z);
            water.transform.localScale = new Vector3(1200, 1, 1200); // 12 km square (plane is 10 m)
            var mr = water.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            Note(string.Format("Water: 'Ocean' plane 12 x 12 km at y={0} m, centred on terrain ({1:0},{2:0}). Material {3}", SeaLevel, c.x, c.z, matPath));
        }

        public static void TuneWater()
        {
            var args = Environment.GetCommandLineArgs();
            int ai = Array.IndexOf(args, "-surveyOut");
            outDir = ai >= 0 && ai + 1 < args.Length ? args[ai + 1] : "_Backups/build";
            Directory.CreateDirectory(outDir);
            ShaderUtil.allowAsyncCompilation = false;
            try
            {
                const string nPath = EnvFolder + "/Textures/T_Ocean_Normal.psd";
                AssetDatabase.ImportAsset(nPath, ImportAssetOptions.ForceSynchronousImport);
                var imp = (TextureImporter)AssetImporter.GetAtPath(nPath);
                imp.textureType = TextureImporterType.NormalMap;
                imp.wrapMode = TextureWrapMode.Repeat;
                imp.mipmapEnabled = true;
                imp.anisoLevel = 4;
                imp.SaveAndReimport();

                var mat = AssetDatabase.LoadAssetAtPath<Material>(EnvFolder + "/Materials/M_Ocean_URP.mat");
                mat.SetColor("_BaseColor", new Color(0.07f, 0.26f, 0.34f, 0.965f));
                mat.SetFloat("_Smoothness", 0.88f);
                mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(nPath));
                mat.SetTextureScale("_BumpMap", new Vector2(900, 900));
                mat.SetFloat("_BumpScale", 0.6f);
                UnityEditor.BaseShaderGUI.SetMaterialKeywords(mat, UnityEditor.Rendering.Universal.ShaderGUI.LitGUI.SetMaterialKeywords, null);
                EditorUtility.SetDirty(mat);
                AssetDatabase.SaveAssets();
                Note("Water tuned: own normal map copy " + nPath + ", alpha 0.965, colour (0.07,0.26,0.34), smoothness 0.88");

                // renders only; scene not modified/saved here
                var scene = EditorSceneManager.OpenScene(Scene1Path, OpenSceneMode.Single);
                foreach (var p in UnityEngine.Object.FindObjectsOfType<ReflectionProbe>()) p.RenderProbe();
                var t = UnityEngine.Object.FindObjectOfType<Terrain>();
                var goju = GameObject.Find("Island_Environment/Landmarks/Pagoda_Goju_Main (site 2)");
                var cam = new GameObject("BuildCam").AddComponent<Camera>();
                cam.allowHDR = false; cam.fieldOfView = 45; cam.farClipPlane = 9000; cam.nearClipPlane = 0.3f;
                var td = t.terrainData; var c = t.transform.position + new Vector3(td.size.x / 2, 0, td.size.z / 2);
                cam.transform.position = c + Vector3.forward * 2300 + Vector3.up * 520; cam.transform.LookAt(c + Vector3.up * 80);
                Shoot(cam, "water2_island_from_north.png");
                var gp = goju.transform.position;
                var wv = NearestWater(t, new Vector2(gp.x, gp.z));
                cam.transform.position = new Vector3(wv.x, SeaLevel + 6, wv.y) + (new Vector3(wv.x, 0, wv.y) - new Vector3(gp.x, 0, gp.z)).normalized * 250f;
                cam.transform.LookAt(gp + Vector3.up * 20);
                Shoot(cam, "water2_goju_from_water.png");
                cam.transform.position = new Vector3(1250, SeaLevel + 30, -300); cam.transform.LookAt(new Vector3(1250, SeaLevel + 40, 600));
                Shoot(cam, "water2_south_coast_low.png");
                UnityEngine.Object.DestroyImmediate(cam.gameObject);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); // discard (nothing saved)
            }
            catch (Exception e) { Note("EXCEPTION: " + e); throw; }
            finally { File.AppendAllText(Path.Combine(outDir, "build_report.txt"), R.ToString()); }
        }

        // ------------------------------------------------------------------ pagodas
        static GameObject PlacePagoda(Terrain t, GameObject parent, string prefabName, string objName, Vector2 site)
        {
            var path = FindPrefab(prefabName);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var existing = parent.transform.Find(objName);
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.name = objName;
            go.transform.SetParent(parent.transform, true);
            go.transform.position = Vector3.zero; go.transform.rotation = Quaternion.identity;
            var b = Bounds(go);
            float fpR = 0.5f * Mathf.Sqrt(b.size.x * b.size.x + b.size.z * b.size.z);
            float pivotToBottom = b.min.y - go.transform.position.y;

            // search around the survey site for the flattest footprint
            Vector2 best = site; float bestVar = float.MaxValue, bestMin = 0;
            for (float dx = -24; dx <= 24; dx += 2) for (float dz = -24; dz <= 24; dz += 2)
            {
                var p = site + new Vector2(dx, dz);
                FootprintHeights(t, p, fpR, out float mn, out float mx);
                float v = mx - mn + new Vector2(dx, dz).magnitude * 0.01f;
                if (v < bestVar) { bestVar = v; best = p; bestMin = mn; }
            }
            FootprintHeights(t, best, fpR, out float min, out float max);

            // face the nearest open water
            var toWater = NearestWater(t, best);
            float yaw = Mathf.Atan2(toWater.x - best.x, toWater.y - best.y) * Mathf.Rad2Deg;
            yaw = Mathf.Round(yaw / 90f) * 90f; // keep it square to the compass

            float sink = 0.25f;
            go.transform.position = new Vector3(best.x, min - pivotToBottom - sink, best.y);
            go.transform.rotation = Quaternion.Euler(0, yaw, 0);
            Note(string.Format("{0}: prefab {1} at ({2:0.0}, {3:0.0}, {4:0.0}), yaw {5:0} deg (front faces nearest water at ({6:0},{7:0})). " +
                               "Footprint radius {8:0.0} m, ground variation under footprint {9:0.00} m (base embedded up to {10:0.0} m on the high side, never floating).",
                objName, prefabName, go.transform.position.x, go.transform.position.y, go.transform.position.z, yaw, toWater.x, toWater.y,
                fpR, max - min, max - min + sink));
            return go;
        }

        static void FootprintHeights(Terrain t, Vector2 c, float r, out float min, out float max)
        {
            min = float.MaxValue; max = float.MinValue;
            foreach (var rr in new[] { 0f, r * 0.5f, r })
                for (int k = 0; k < (rr == 0 ? 1 : 16); k++)
                {
                    float a = k * Mathf.PI * 2 / 16;
                    float h = H(t, c.x + Mathf.Cos(a) * rr, c.y + Mathf.Sin(a) * rr);
                    min = Mathf.Min(min, h); max = Mathf.Max(max, h);
                }
        }

        static Vector2 NearestWater(Terrain t, Vector2 from)
        {
            Vector2 best = from; float bd = float.MaxValue;
            var td = t.terrainData; var tp = t.transform.position;
            for (float x = tp.x; x <= tp.x + td.size.x; x += 16)
                for (float z = tp.z; z <= tp.z + td.size.z; z += 16)
                {
                    if (H(t, x, z) >= SeaLevel) continue;
                    float d = (new Vector2(x, z) - from).sqrMagnitude;
                    if (d < bd) { bd = d; best = new Vector2(x, z); }
                }
            return best;
        }

        // ------------------------------------------------------------------ analysis
        static void ApproachDirections(Terrain t, GameObject pagoda, string label)
        {
            var p = new Vector2(pagoda.transform.position.x, pagoda.transform.position.z);
            Note("");
            Note("Torii approach options for " + label + " (straight 180 m line downhill from the pagoda; avg and steepest grade):");
            var rows = new List<(float ang, float avg, float maxG, float drop)>();
            for (int k = 0; k < 16; k++)
            {
                float a = k * 22.5f;
                var dir = new Vector2(Mathf.Sin(a * Mathf.Deg2Rad), Mathf.Cos(a * Mathf.Deg2Rad));
                float prev = H(t, p.x + dir.x * 12, p.y + dir.y * 12), maxG = 0;
                for (float s = 16; s <= 192; s += 4)
                {
                    float h = H(t, p.x + dir.x * s, p.y + dir.y * s);
                    maxG = Mathf.Max(maxG, Mathf.Abs(h - prev) / 4f); prev = h;
                }
                float start = H(t, p.x + dir.x * 12, p.y + dir.y * 12), end = H(t, p.x + dir.x * 192, p.y + dir.y * 192);
                rows.Add((a, Mathf.Abs(start - end) / 180f, maxG, start - end));
            }
            foreach (var r in rows.OrderBy(r => r.maxG).Take(4))
                Note(string.Format("  bearing {0,5:0.0} deg ({1}): avg grade {2:0}%, steepest {3:0}%, drop {4:0} m",
                    r.ang, Compass(r.ang), r.avg * 100, r.maxG * 100, r.drop));
        }

        struct Garden { public Vector3 p; public float var, dist; public string layer; }

        static List<Garden> GardenSites(Terrain t, Vector3 goju)
        {
            var td = t.terrainData; var tp = t.transform.position;
            var am = td.GetAlphamaps(0, 0, td.alphamapResolution, td.alphamapResolution);
            var list = new List<Garden>();
            for (float x = tp.x + 40; x < tp.x + td.size.x - 40; x += 8)
                for (float z = tp.z + 40; z < tp.z + td.size.z - 40; z += 8)
                {
                    float h0 = H(t, x, z);
                    if (h0 < SeaLevel + 6 || h0 > SeaLevel + 120) continue;
                    float mn = h0, mx = h0; bool ok = true;
                    foreach (var rr in new[] { 12f, 25f })
                        for (int k = 0; k < 16; k++)
                        {
                            float a = k * Mathf.PI / 8;
                            float h = H(t, x + Mathf.Cos(a) * rr, z + Mathf.Sin(a) * rr);
                            if (h < SeaLevel + 2) { ok = false; break; }
                            mn = Mathf.Min(mn, h); mx = Mathf.Max(mx, h);
                        }
                    if (!ok || mx - mn > 2.5f) continue;
                    string layer = Dominant(td, am, (x - tp.x) / td.size.x, (z - tp.z) / td.size.z);
                    if (layer.ToLower().Contains("sand") || layer.ToLower().Contains("cliff")) continue;
                    list.Add(new Garden { p = new Vector3(x, h0, z), var = mx - mn, dist = Vector2.Distance(new Vector2(x, z), new Vector2(goju.x, goju.z)), layer = layer });
                }
            var picked = new List<Garden>();
            foreach (var g in list.OrderBy(g => g.var * 4 + Mathf.Abs(g.dist - 300f) * 0.01f))
            {
                if (picked.Any(o => Vector3.Distance(o.p, g.p) < 180)) continue;
                picked.Add(g); if (picked.Count >= 5) break;
            }
            Note("");
            Note("Garden clearing candidates (25 m radius, above beach, not sand/cliff; ranked by flatness and ~300 m from main pagoda):");
            int i = 1;
            foreach (var g in picked)
                Note(string.Format("  #{0} ({1:0}, {2:0}, {3:0})  {4:0} m above sea  variation {5:0.0} m  {6:0} m from main pagoda  ground {7}",
                    i++, g.p.x, g.p.y, g.p.z, g.p.y - SeaLevel, g.var, g.dist, g.layer));
            if (picked.Count == 0) Note("  none found");
            return picked;
        }

        // ------------------------------------------------------------------ renders
        static void Renders(Terrain t, GameObject goju, GameObject sanju, List<Garden> gardens)
        {
            var cam = new GameObject("BuildCam").AddComponent<Camera>();
            cam.allowHDR = false; cam.fieldOfView = 45; cam.farClipPlane = 9000; cam.nearClipPlane = 0.3f;
            var td = t.terrainData; var c = t.transform.position + new Vector3(td.size.x / 2, 0, td.size.z / 2);

            foreach (var (name, dir) in new[] { ("south", Vector3.back), ("north", Vector3.forward), ("east", Vector3.right) })
            {
                cam.transform.position = c + dir * 2300 + Vector3.up * 520;
                cam.transform.LookAt(c + Vector3.up * 80);
                Shoot(cam, "build_island_from_" + name + ".png");
            }
            ShootAt(cam, goju, 70f, 18f, "build_goju_close.png");
            ShootAt(cam, goju, 260f, 60f, "build_goju_wide.png");
            ShootAt(cam, sanju, 45f, 12f, "build_sanju_close.png");
            ShootAt(cam, sanju, 220f, 50f, "build_sanju_wide.png");

            // goju seen from the bay at sea level
            var gp = goju.transform.position;
            var wv = NearestWater(t, new Vector2(gp.x, gp.z));
            var from = new Vector3(wv.x, SeaLevel + 6, wv.y) + (new Vector3(wv.x, 0, wv.y) - new Vector3(gp.x, 0, gp.z)).normalized * 250f;
            cam.transform.position = from; cam.transform.LookAt(gp + Vector3.up * 20);
            Shoot(cam, "build_goju_from_water.png");

            int i = 1;
            foreach (var g in gardens.Take(3))
            {
                cam.transform.position = g.p + new Vector3(-90, 45, -90);
                cam.transform.LookAt(g.p);
                Shoot(cam, "build_garden_" + i++ + ".png");
            }
            UnityEngine.Object.DestroyImmediate(cam.gameObject);
        }

        static void ShootAt(Camera cam, GameObject target, float dist, float height, string file)
        {
            var b = Bounds(target);
            var dir = -target.transform.forward; // stand in front of the pagoda, looking back at it
            dir = Quaternion.Euler(0, 30, 0) * dir;
            cam.transform.position = b.center + dir * dist + Vector3.up * height;
            cam.transform.LookAt(b.center);
            Shoot(cam, file);
        }

        // ------------------------------------------------------------------ helpers
        static float H(Terrain t, float x, float z) => t.SampleHeight(new Vector3(x, 0, z)) + t.transform.position.y;

        static string Compass(float a) { string[] n = { "N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE", "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW" }; return n[Mathf.RoundToInt(a / 22.5f) % 16]; }

        static string Dominant(TerrainData td, float[,,] am, float u, float v)
        {
            int ar = td.alphamapResolution;
            int x = Mathf.Clamp(Mathf.RoundToInt(u * (ar - 1)), 0, ar - 1), z = Mathf.Clamp(Mathf.RoundToInt(v * (ar - 1)), 0, ar - 1);
            int best = 0; float bw = -1;
            for (int l = 0; l < am.GetLength(2); l++) if (am[z, x, l] > bw) { bw = am[z, x, l]; best = l; }
            var n = td.terrainLayers[best] ? td.terrainLayers[best].name : "?";
            int cut = n.IndexOf("_Diffuse"); if (cut > 0) n = n.Substring(0, cut);
            return n.Replace("layer_", "");
        }

        static GameObject Child(GameObject parent, string name)
        {
            var tr = parent.transform.Find(name);
            if (tr != null) return tr.gameObject;
            var go = new GameObject(name); go.transform.SetParent(parent.transform, false); return go;
        }

        static Bounds Bounds(GameObject go)
        {
            var lod = go.GetComponentInChildren<LODGroup>();
            IEnumerable<Renderer> use = go.GetComponentsInChildren<Renderer>(true);
            if (lod != null) { var l = lod.GetLODs(); if (l.Length > 0 && l[0].renderers.Length > 0) use = l[0].renderers.Where(r => r != null); }
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
            throw new Exception("Prefab not found: " + name);
        }

        static void Shoot(Camera cam, string file, int w = 1280, int h = 720)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render(); cam.Render(); // second pass after any first-use shader compilation
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = prev; cam.targetTexture = null;
            File.WriteAllBytes(Path.Combine(outDir, file), tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex); rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
        }
    }
}
