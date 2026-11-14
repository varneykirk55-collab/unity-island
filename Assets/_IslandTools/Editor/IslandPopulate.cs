// Editor-only: populates Scene 1 with real Asset Store prefabs (no primitives).
//   1) Torii approach + lanterns to the main Goju pagoda
//   2) Shrine dressing at the Sanju pagoda
//   3) Japanese garden clearing
//   4) Forests painted as terrain trees (terrain heights are never changed)
//   5) North bay shore dressing
// Deterministic (fixed seeds). Re-running replaces what it placed before.
// Batchmode: Unity.exe -batchmode -quit -projectPath <proj> -executeMethod IslandTools.IslandPopulate.Run -surveyOut <dir>
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Random = System.Random;

namespace IslandTools
{
    public static class IslandPopulate
    {
        const string Scene1Path = "Assets/Free Island Collection/Scenes/Scene 1.unity";
        const float Sea = 45f;
        static readonly Vector3 GardenCenter = new Vector3(456, 0, 1888);

        static Terrain T;
        static string outDir;
        static readonly StringBuilder R = new StringBuilder();
        static readonly List<(Vector2 c, float r)> Exclusions = new List<(Vector2, float)>();
        static readonly List<Vector2> PathPoints = new List<Vector2>();
        static void Note(string s) { R.AppendLine(s); Debug.Log("[IslandPopulate] " + s); }

        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            int ai = Array.IndexOf(args, "-surveyOut");
            outDir = ai >= 0 && ai + 1 < args.Length ? args[ai + 1] : "_Backups/populate";
            Directory.CreateDirectory(outDir);
            ShaderUtil.allowAsyncCompilation = false;
            try
            {
                var scene = EditorSceneManager.OpenScene(Scene1Path, OpenSceneMode.Single);
                T = UnityEngine.Object.FindObjectOfType<Terrain>();
                var root = GameObject.Find("Island_Environment");
                var goju = GameObject.Find("Island_Environment/Landmarks/Pagoda_Goju_Main (site 2)");
                var sanju = GameObject.Find("Island_Environment/Landmarks/Pagoda_Sanju_South (site 3)");
                if (root == null || goju == null || sanju == null) throw new Exception("Expected Island_Environment / pagodas not found");

                Exclusions.Add((XZ(goju.transform.position), 22f));
                Exclusions.Add((XZ(sanju.transform.position), 14f));

                Step1_GojuApproach(root, goju);
                Step2_SanjuShrine(root, sanju);
                Step3_Garden(root, goju);
                Step5_BayShore(root, goju);   // before trees so its area is excluded
                Step4_Forests();

                EditorSceneManager.MarkSceneDirty(scene);
                AssetDatabase.SaveAssets();
                if (!EditorSceneManager.SaveScene(scene)) throw new Exception("Scene save failed");
                Note("Scene 1 saved.");
                Renders(goju, sanju);
            }
            catch (Exception e) { Note("EXCEPTION: " + e); throw; }
            finally { File.WriteAllText(Path.Combine(outDir, "populate_report.txt"), R.ToString()); }
        }

        // ---------------------------------------------------------------- 1
        static void Step1_GojuApproach(GameObject root, GameObject goju)
        {
            Note("=== 1. Torii approach to Goju ===");
            var grp = Group(root, "Goju_Approach");
            var gp = XZ(goju.transform.position);

            // choose the gentlest bearing within +-40 deg of WNW (292.5) by steepest-step
            float bestBearing = 292.5f, bestMax = float.MaxValue;
            for (float b = 252.5f; b <= 332.5f; b += 5f)
            {
                var d = Dir(b); float prev = H(gp + d * 14), mx = 0; bool wet = false;
                for (float s = 18; s <= 190; s += 4) { float h = H(gp + d * s); if (h < Sea + 1) wet = true; mx = Mathf.Max(mx, Mathf.Abs(h - prev) / 4f); prev = h; }
                if (!wet && mx < bestMax) { bestMax = mx; bestBearing = b; }
            }
            var dir = Dir(bestBearing);
            Note(string.Format("  path bearing {0:0.0} deg, steepest step {1:0}%", bestBearing, bestMax * 100));

            // senbon-torii style tunnel: a gate every 5 m from 26 m to 185 m; gate faces along the path.
            // lantern pairs stand just outside the pillars every 20 m.
            int gates = 0, lanterns = 0;
            var gateYaw = bestBearing;
            var side = new Vector2(dir.y, -dir.x);
            for (float s = 26; s <= 185; s += 5)
            {
                var p = gp + dir * s;
                PathPoints.Add(p);
                Place(grp, "Torii_Inari_Med1_LOD", p, gateYaw, wideAxisX: 2.3f); gates++;
                if (((int)(s - 26)) % 20 == 10)
                {
                    var m = gp + dir * (s + 2.5f);
                    string ln = "Lantern_Stone_A" + (1 + (gates % 3));
                    Place(grp, ln, m + side * 3.8f, gateYaw); Place(grp, ln, m - side * 3.8f, gateYaw); lanterns += 2;
                }
            }
            for (float s = 0; s <= 190; s += 6) PathPoints.Add(gp + dir * s);

            // two large lanterns on stone platforms flanking the path at the pagoda base
            var sideB = new Vector2(dir.y, -dir.x);
            foreach (var sgn in new[] { 1f, -1f })
            {
                var pp = gp + dir * 14f + sideB * 5.5f * sgn;
                var plat = Place(grp, "Platform_Stone_A1", pp, gateYaw);
                Place(grp, "Lantern_Stone_C1", pp, gateYaw, onTopOf: plat); lanterns++;
            }
            Note(string.Format("  {0} Inari torii, {1} stone lanterns (+2 stone platforms)", gates, lanterns));
        }

        // ---------------------------------------------------------------- 2
        static void Step2_SanjuShrine(GameObject root, GameObject sanju)
        {
            Note("=== 2. Sanju shrine ===");
            var grp = Group(root, "Sanju_Shrine");
            var sp = XZ(sanju.transform.position);
            var dir = Dir(135f); // SE, gentlest approach from survey
            var side = new Vector2(dir.y, -dir.x);
            Place(grp, "Torii_Myoujin_Large1_LOD", sp + dir * 24f, 135f, wideAxisX: 3f);
            for (float s = 0; s <= 30; s += 6) PathPoints.Add(sp + dir * s);
            int n = 0;
            foreach (var s in new[] { 11f, 17f })
                foreach (var sg in new[] { 1f, -1f }) { Place(grp, "Lantern_Stone_B" + (s < 12 ? 1 : 2), sp + dir * s + side * 3f * sg, 135f); n++; }
            Place(grp, "Torii_Votive_Stone1_LOD", sp + dir * 8f + side * 6.5f, 135f + 90f);
            Note(string.Format("  1 Myoujin torii (large) at 24 m SE, {0} lanterns, 1 stone votive stand", n));
            Exclusions.Add((sp + dir * 15f, 14f));
        }

        // ---------------------------------------------------------------- 3
        static void Step3_Garden(GameObject root, GameObject goju)
        {
            Note("=== 3. Garden clearing ===");
            var grp = Group(root, "Garden");
            var c = new Vector2(GardenCenter.x, GardenCenter.z);
            Exclusions.Add((c, 34f));
            var rnd = new Random(1888);
            var used = new List<(Vector2 p, float r)>();

            // entrance faces the main pagoda
            var toGoju = (XZ(goju.transform.position) - c).normalized;
            float entranceYaw = Bearing(toGoju);
            var side = new Vector2(toGoju.y, -toGoju.x);

            // small shrine (hokora) at the back on a stone platform, flanked by lanterns
            var back = c - toGoju * 12f;
            var plat = Place(grp, "Platform_Stone_B1", back, entranceYaw);
            Place(grp, "Small Rusted House", back, entranceYaw, onTopOf: plat, scale: 1.6f);
            Place(grp, "Lantern_Stone_D1", back + side * 4f, entranceYaw);
            Place(grp, "Lantern_Stone_D1", back - side * 4f, entranceYaw);
            Place(grp, "Rock Bowl 1", back + toGoju * 4f + side * 2.2f, entranceYaw);
            used.Add((back, 6f));

            // entrance torii + stepping-stone path from the entrance to the shrine
            var entrance = c + toGoju * 22f;
            Place(grp, "Torii (Double Color)", entrance, entranceYaw, wideAxisX: 3.4f);
            used.Add((entrance, 4.5f));
            int stones = 0;
            for (float s = 20f; s >= -7f; s -= 1.35f)
            {
                var p = c + toGoju * s + side * Mathf.Sin(s * 0.18f) * 1.6f;
                Place(grp, stones % 2 == 0 ? "JG_TobiIshi_A_01" : "JG_TobiIshi_A_02", p, (float)rnd.NextDouble() * 360f, sink: 0.05f);
                used.Add((p, 0.9f)); stones++;
            }

            // Sakura (spring) ring
            string[] sakura = { "Tree_Sakura_Big_Spring_01", "Tree_Sakura_Big_Spring_05", "Tree_Sakura_Mid_Spring_01", "Tree_Sakura_Mid_Spring_05", "Tree_Sakura_Small_Spring_01", "Tree_Sakura_Small_Spring_05" };
            int trees = Scatter(grp, rnd, used, c, 8f, 27f, 14, 5.0f, k => sakura[k % sakura.Length]);
            // boxwood + fern under-planting, rocks
            int shrubs = Scatter(grp, rnd, used, c, 4f, 27f, 30, 1.4f, k => "Plant_Boxwood_" + (k % 2 == 0 ? "Spring_0" + (1 + k % 3) : "Single_Spring_0" + (1 + k % 3)));
            int ferns = Scatter(grp, rnd, used, c, 5f, 25f, 12, 1.2f, k => "Plant_PaintedFern_" + (k % 2 == 0 ? "Spring_0" + (1 + k % 2) : "Single_Spring_0" + (1 + k % 2)));
            int rocks = Scatter(grp, rnd, used, c, 6f, 22f, 7, 2.0f, k => "JG_Rock_A_0" + (1 + k % 3), scaleMin: 1.2f, scaleMax: 2.2f);
            Note(string.Format("  shrine + platform, 2 lanterns, rock bowl, Zen torii, {0} stepping stones, {1} sakura, {2} boxwood, {3} ferns, {4} rocks", stones, trees, shrubs, ferns, rocks));
        }

        // ---------------------------------------------------------------- 5
        static void Step5_BayShore(GameObject root, GameObject goju)
        {
            Note("=== 5. North bay shore ===");
            var grp = Group(root, "Bay_Shore");
            var gp = XZ(goju.transform.position);
            // shoreline points (just above sea) within 650 m of the pagoda, on the bay side (north/east of it)
            var shore = new List<Vector2>();
            for (float x = gp.x - 650; x <= gp.x + 650; x += 6)
                for (float z = gp.y; z <= gp.y + 750; z += 6)
                {
                    var p = new Vector2(x, z); float h = H(p);
                    if (h > Sea + 0.4f && h < Sea + 1.6f && (p - gp).magnitude < 750) shore.Add(p);
                }
            Note("  shoreline sample points in bay: " + shore.Count);
            if (shore.Count == 0) return;
            var rnd = new Random(45);
            var landing = shore.OrderBy(p => (p - gp).sqrMagnitude).First();
            Exclusions.Add((landing, 18f));

            // landing: two lanterns, baskets, rake, fence pieces, rock bowl
            var toLand = (gp - landing).normalized; float yaw = Bearing(toLand);
            var side = new Vector2(toLand.y, -toLand.x);
            Place(grp, "Lantern_Stone_A2", landing + toLand * 6f + side * 3f, yaw);
            Place(grp, "Lantern_Stone_A2", landing + toLand * 6f - side * 3f, yaw);
            Place(grp, "Basket 1", landing + side * 5f + toLand * 2f, (float)rnd.NextDouble() * 360);
            Place(grp, "Basket 2", landing + side * 5.8f + toLand * 3f, (float)rnd.NextDouble() * 360);
            Place(grp, "Rake", landing + side * 6.5f + toLand * 1.5f, yaw + 70);
            Place(grp, "Rock Bowl 2", landing - side * 5f + toLand * 3f, yaw);
            for (int k = 0; k < 4; k++) Place(grp, "Fence With Post", landing - side * (7f + k * 2.2f) + toLand * 8f, yaw + 90);

            // boulders along the bay shoreline (scaled garden rocks), spaced out
            int boulders = 0; var used = new List<Vector2>();
            foreach (var p in shore.OrderBy(_ => rnd.Next()))
            {
                if (boulders >= 28) break;
                if ((p - landing).magnitude < 14 || used.Any(u => (u - p).magnitude < 22)) continue;
                int cluster = 1 + rnd.Next(3);
                for (int k = 0; k < cluster; k++)
                {
                    var q = p + new Vector2((float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f) * 5f;
                    Place(grp, "JG_Rock_A_0" + (1 + rnd.Next(3)), q, (float)rnd.NextDouble() * 360, scale: 2.5f + (float)rnd.NextDouble() * 3.5f, sink: 0.4f);
                    boulders++;
                }
                used.Add(p);
            }
            Note(string.Format("  landing at ({0:0},{1:0}): 2 lanterns, 2 baskets, rake, rock bowl, 4 fence sections; {2} shoreline boulders", landing.x, landing.y, boulders));
        }

        // ---------------------------------------------------------------- 4
        static void Step4_Forests()
        {
            Note("=== 4. Forests (terrain trees) ===");
            var td = T.terrainData;
            string[] protoNames =
            {
                "Tree_BlackPine_Big_Green_01", "Tree_BlackPine_Big_Green_02", "Tree_BlackPine_Big_Green_03",
                "Tree_BlackPine_Mid_Green_01", "Tree_BlackPine_Mid_Green_02", "Tree_BlackPine_Mid_Green_03",
                "Tree_BlackPine_Small_Green_01", "Tree_BlackPine_Small_Green_02",
                "Tree_BlackPine_Big_Dry_01", "Tree_BlackPine_Mid_Dry_02", "Tree_BlackPine_Small_Dry_03",
                "Tree_Bamboo_Big_Green_01", "Tree_Bamboo_Big_Green_02", "Tree_Bamboo_Mid_Green_01", "Tree_Bamboo_Mid_Green_02", "Tree_Bamboo_Mid_Dry_01",
            };
            var protos = protoNames.Select(n => new TreePrototype { prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FindPrefab(n)) }).ToArray();
            td.treePrototypes = protos;
            td.RefreshPrototypes();
            int Pidx(string n) => Array.IndexOf(protoNames, n);
            int[] pineGreen = { 0, 1, 2, 3, 4, 5, 3, 4, 5, 6, 7 };   // weighted toward mid
            int[] pineDry = { 8, 9, 10 };
            int[] bamboo = { 11, 12, 13, 14, 13, 15 };

            var am = td.GetAlphamaps(0, 0, td.alphamapResolution, td.alphamapResolution);
            int ar = td.alphamapResolution;
            var names = td.terrainLayers.Select(l => l ? l.name.ToLower() : "").ToArray();
            int sand = Array.FindIndex(names, n => n.Contains("sand"));
            int[] cliff = names.Select((n, i) => (n, i)).Where(t => t.n.Contains("cliff")).Select(t => t.i).ToArray();
            int moss = Array.FindIndex(names, n => n.Contains("mossy"));

            var rnd = new Random(2500);
            var trees = new List<TreeInstance>();
            int cPine = 0, cDry = 0, cBam = 0;
            float step = 5f;
            for (float x = 6; x < td.size.x - 6; x += step)
                for (float z = 6; z < td.size.z - 6; z += step)
                {
                    float jx = x + ((float)rnd.NextDouble() - 0.5f) * step * 0.9f, jz = z + ((float)rnd.NextDouble() - 0.5f) * step * 0.9f;
                    var p = new Vector2(jx, jz);
                    float h = H(p);
                    if (h < Sea + 4f || h > Sea + 290f) continue;
                    if (Excluded(p)) continue;
                    float u = jx / td.size.x, v = jz / td.size.z;
                    float steep = td.GetSteepness(u, v);
                    int ax = Mathf.Clamp(Mathf.RoundToInt(u * (ar - 1)), 0, ar - 1), az = Mathf.Clamp(Mathf.RoundToInt(v * (ar - 1)), 0, ar - 1);
                    float wSand = sand >= 0 ? am[az, ax, sand] : 0, wCliff = cliff.Sum(i => am[az, ax, i]), wMoss = moss >= 0 ? am[az, ax, moss] : 0;
                    if (wSand > 0.3f) continue;

                    float forest = Mathf.PerlinNoise(jx * 0.0045f + 11.3f, jz * 0.0045f + 7.1f);      // large forest/clearing pattern
                    float clump = Mathf.PerlinNoise(jx * 0.03f + 3.7f, jz * 0.03f + 9.2f);           // small-scale clumping
                    float alt = Mathf.InverseLerp(Sea + 4f, Sea + 290f, h);
                    float valley = H(p) - (H(p + new Vector2(28, 0)) + H(p - new Vector2(28, 0)) + H(p + new Vector2(0, 28)) + H(p - new Vector2(0, 28))) * 0.25f;

                    int proto; float roll = (float)rnd.NextDouble();
                    if (valley < -3.5f && h < Sea + 140f && steep < 30f && forest > 0.35f)
                    {   // bamboo groves in gullies
                        if (roll > 0.75f + clump * 0.1f) continue;
                        proto = bamboo[rnd.Next(bamboo.Length)]; cBam++;
                        float bs = 1.3f + (float)rnd.NextDouble() * 0.5f;
                        trees.Add(new TreeInstance { prototypeIndex = proto, position = new Vector3(u, 0, v), widthScale = bs, heightScale = bs,
                            rotation = (float)rnd.NextDouble() * Mathf.PI * 2f, color = Color.white, lightmapColor = Color.white });
                        continue;
                    }
                    else if (steep > 33f || wCliff > 0.5f || alt > 0.75f)
                    {   // sparse wind-blasted dry pines on cliffs / high ground
                        if (steep > 48f || roll > 0.035f) continue;
                        proto = pineDry[rnd.Next(pineDry.Length)]; cDry++;
                    }
                    else
                    {   // black-pine forest with clearings; thins with altitude
                        float density = Mathf.Clamp01((forest - 0.30f) * 3.2f) * (1f - alt * 0.7f) * (0.6f + clump * 0.6f);
                        if (wMoss > 0.5f) density *= 0.5f;
                        if (roll > density * 0.85f) continue;
                        proto = pineGreen[rnd.Next(pineGreen.Length)]; cPine++;
                    }
                    // island is ~2.5 km across; the pack's "mid" pines are only ~5 m, so forest trees are scaled up
                    float sc = 1.4f + (float)rnd.NextDouble() * 0.8f;
                    trees.Add(new TreeInstance
                    {
                        prototypeIndex = proto,
                        position = new Vector3(u, 0, v),       // y is resolved from the heightmap (Terrain snaps trees)
                        widthScale = sc, heightScale = sc * (0.9f + (float)rnd.NextDouble() * 0.2f),
                        rotation = (float)rnd.NextDouble() * Mathf.PI * 2f,
                        color = Color.white, lightmapColor = Color.white,
                    });
                }
            td.SetTreeInstances(trees.ToArray(), true);
            EditorUtility.SetDirty(td);
            Note(string.Format("  {0} prototypes, {1} trees: {2} green black pine, {3} dry pine (cliffs/high), {4} bamboo (gullies)", protos.Length, trees.Count, cPine, cDry, cBam));

            T.treeDistance = 5000f; T.treeBillboardDistance = 300f; T.treeMaximumFullLODCount = 300; T.treeCrossFadeLength = 25f;
            EditorUtility.SetDirty(T);

            // wind as designed by the asset author: directional WindZone + Broccoli terrain controller
            var wz = UnityEngine.Object.FindObjectsOfType<WindZone>().FirstOrDefault(w => w.mode == WindZoneMode.Directional);
            if (wz == null)
            {
                wz = new GameObject("WindZone").AddComponent<WindZone>();
                wz.transform.SetParent(GameObject.Find("Island_Environment").transform, false);
                wz.mode = WindZoneMode.Directional; wz.windMain = 0.6f; wz.windTurbulence = 0.4f; wz.windPulseMagnitude = 0.4f; wz.windPulseFrequency = 0.08f;
                wz.transform.rotation = Quaternion.Euler(0, 210, 0);
            }
            var ctrlType = Type.GetType("Broccoli.Controller.BroccoTerrainController_FJG_1_10_3, Assembly-CSharp");
            if (ctrlType != null && T.GetComponent(ctrlType) == null) T.gameObject.AddComponent(ctrlType);
            Note("  terrain tree distance 5000 m, billboards from 300 m; WindZone + BroccoTerrainController added for tree wind");
        }

        // ---------------------------------------------------------------- placement helpers
        static GameObject Place(GameObject parent, string prefabName, Vector2 xz, float yaw, float wideAxisX = 0f, GameObject onTopOf = null, float scale = 1f, float sink = 0.12f)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FindPrefab(prefabName));
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.scene);
            go.transform.SetParent(parent.transform, true);
            go.transform.localScale = Vector3.one * scale;
            go.transform.rotation = Quaternion.Euler(0, yaw, 0);
            go.transform.position = new Vector3(xz.x, 0, xz.y);
            var b = Bounds(go);
            float pivotToBottom = b.min.y - go.transform.position.y;
            float ground;
            if (onTopOf != null) { ground = Bounds(onTopOf).max.y; sink = 0.02f; }
            else
            {
                // lowest ground under the footprint so nothing floats; for wide gates sample both pillars
                ground = float.MaxValue;
                var right = new Vector2(go.transform.right.x, go.transform.right.z);
                var fwd = new Vector2(go.transform.forward.x, go.transform.forward.z);
                float hx = wideAxisX > 0 ? wideAxisX : b.extents.x * 0.8f, hz = b.extents.z * 0.8f;
                foreach (var o in new[] { Vector2.zero, right * hx, -right * hx, fwd * hz, -fwd * hz })
                    ground = Mathf.Min(ground, H(xz + o));
            }
            go.transform.position = new Vector3(xz.x, ground - pivotToBottom - sink, xz.y);
            if (ground < Sea + 0.2f && onTopOf == null) Note("  WARNING: " + prefabName + " at (" + xz.x.ToString("0") + "," + xz.y.ToString("0") + ") is at/below sea level");
            return go;
        }

        static int Scatter(GameObject grp, Random rnd, List<(Vector2 p, float r)> used, Vector2 c, float rMin, float rMax, int count, float spacing, Func<int, string> pick, float scaleMin = 0.9f, float scaleMax = 1.15f)
        {
            int placed = 0, tries = 0;
            while (placed < count && tries++ < count * 60)
            {
                float a = (float)rnd.NextDouble() * Mathf.PI * 2f, r = Mathf.Lerp(rMin, rMax, Mathf.Sqrt((float)rnd.NextDouble()));
                var p = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                if (used.Any(u => (u.p - p).magnitude < u.r + spacing * 0.5f)) continue;
                Place(grp, pick(placed), p, (float)rnd.NextDouble() * 360f, scale: Mathf.Lerp(scaleMin, scaleMax, (float)rnd.NextDouble()));
                used.Add((p, spacing * 0.5f)); placed++;
            }
            return placed;
        }

        static bool Excluded(Vector2 p)
        {
            foreach (var e in Exclusions) if ((p - e.c).magnitude < e.r) return true;
            foreach (var q in PathPoints) if ((p - q).sqrMagnitude < 7f * 7f) return true;
            return false;
        }

        static GameObject Group(GameObject root, string name)
        {
            var old = root.transform.Find(name);
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var g = new GameObject(name); g.transform.SetParent(root.transform, false); return g;
        }

        static float H(Vector2 p) => T.SampleHeight(new Vector3(p.x, 0, p.y)) + T.transform.position.y;
        static Vector2 XZ(Vector3 v) => new Vector2(v.x, v.z);
        static Vector2 Dir(float bearing) => new Vector2(Mathf.Sin(bearing * Mathf.Deg2Rad), Mathf.Cos(bearing * Mathf.Deg2Rad));
        static float Bearing(Vector2 d) => Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;

        static Bounds Bounds(GameObject go)
        {
            var lod = go.GetComponentInChildren<LODGroup>();
            IEnumerable<Renderer> use = go.GetComponentsInChildren<Renderer>(true);
            if (lod != null) { var l = lod.GetLODs(); if (l.Length > 0 && l[0].renderers.Length > 0) use = l[0].renderers.Where(r => r != null); }
            if (!use.Any()) return new Bounds(go.transform.position, Vector3.one * 0.1f);
            var b = use.First().bounds; foreach (var r in use) b.Encapsulate(r.bounds);
            return b;
        }

        static readonly Dictionary<string, string> PrefabCache = new Dictionary<string, string>();
        static string FindPrefab(string name)
        {
            if (PrefabCache.TryGetValue(name, out var cached)) return cached;
            foreach (var g in AssetDatabase.FindAssets("t:Prefab " + name))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (Path.GetFileNameWithoutExtension(p) == name) { PrefabCache[name] = p; return p; }
            }
            throw new Exception("Prefab not found: " + name);
        }

        // ---------------------------------------------------------------- verification renders
        static void Renders(GameObject goju, GameObject sanju)
        {
            foreach (var p in UnityEngine.Object.FindObjectsOfType<ReflectionProbe>()) p.RenderProbe();
            var cam = new GameObject("PopCam").AddComponent<Camera>();
            cam.allowHDR = false; cam.fieldOfView = 45; cam.farClipPlane = 9000; cam.nearClipPlane = 0.3f;
            var c = new Vector3(1250, 0, 1250);

            void At(Vector3 pos, Vector3 look, string f) { cam.transform.position = pos; cam.transform.LookAt(look); Shoot(cam, f); }
            var gp = goju.transform.position; var sp = sanju.transform.position;
            var gate = GameObject.Find("Island_Environment/Goju_Approach");
            var far = gate.transform.GetChild(gate.transform.childCount - 3).position;
            At(c + new Vector3(0, 650, 2400), c + Vector3.up * 80, "pop_island_north.png");
            At(c + new Vector3(-2200, 520, -900), c + Vector3.up * 80, "pop_island_southwest.png");
            At(far + (far - gp).normalized * 25f + Vector3.up * 8f, gp + Vector3.up * 12f, "pop_goju_approach_up.png");
            At(gp + (far - gp).normalized * 60f + new Vector3(0, 55, 0) + Vector3.Cross((far - gp).normalized, Vector3.up) * 60f, (gp + far) * 0.5f, "pop_goju_approach_side.png");
            At(sp + new Vector3(30, 14, -30), sp + Vector3.up * 6f, "pop_sanju_shrine.png");
            var g = new Vector3(GardenCenter.x, T.SampleHeight(GardenCenter), GardenCenter.z);
            At(g + new Vector3(-30, 16, -30), g + Vector3.up * 2f, "pop_garden_a.png");
            At(g + new Vector3(18, 6, 22), g, "pop_garden_b.png");
            var bay = GameObject.Find("Island_Environment/Bay_Shore");
            if (bay != null && bay.transform.childCount > 0)
            {
                var l = bay.transform.GetChild(0).position;
                var gp2 = goju.transform.position;
                var outward = (new Vector3(l.x - gp2.x, 0, l.z - gp2.z)).normalized;
                At(l + outward * 45f + Vector3.up * 14f, l + Vector3.up * 2f, "pop_bay_landing.png");
                At(l + outward * 160f + Vector3.up * 60f, gp2, "pop_bay_to_pagoda.png");
            }
            At(new Vector3(800, 260, 900), new Vector3(1100, 150, 1300), "pop_forest_mid.png");
            UnityEngine.Object.DestroyImmediate(cam.gameObject);
        }

        static void Shoot(Camera cam, string file, int w = 1280, int h = 720)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            cam.targetTexture = rt; cam.Render(); cam.Render();
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = prev; cam.targetTexture = null;
            File.WriteAllBytes(Path.Combine(outDir, file), tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex); rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
        }
    }
}
