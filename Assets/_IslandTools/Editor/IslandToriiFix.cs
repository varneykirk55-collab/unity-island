// Editor-only. Phase 3 torii repair for Scene 1.
//  Goju approach: keeps the existing route but (a) reduces 32 gates to ~10 at ~16 m spacing, (b) grades a 5 m shrine
//  path into the slope along a smoothed profile (max 25 % grade) so terrain no longer rises into the openings,
//  (c) re-seats gates/lanterns/platforms on the graded path. Removed gates/lanterns are deleted (backup holds them).
//  Sanju + garden torii: a small level patch under the opening at the gate's base height.
// Batchmode: ... -executeMethod IslandTools.IslandToriiFix.Run -surveyOut <dir>
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace IslandTools
{
    public static class IslandToriiFix
    {
        const string Scene1Path = "Assets/Free Island Collection/Scenes/Scene 1.unity";
        const float GateSpacing = 16f, PathHalf = 2.6f, Blend = 5f, MaxGrade = 0.25f;
        static Terrain T; static TerrainData TD;
        static readonly StringBuilder R = new StringBuilder();
        static void Note(string s) { R.AppendLine(s); Debug.Log("[ToriiFix] " + s); }

        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            int ai = Array.IndexOf(args, "-surveyOut");
            var outDir = ai >= 0 && ai + 1 < args.Length ? args[ai + 1] : "_Backups/toriifix";
            Directory.CreateDirectory(outDir);
            ShaderUtil.allowAsyncCompilation = false;
            try
            {
                var scene = EditorSceneManager.OpenScene(Scene1Path, OpenSceneMode.Single);
                T = UnityEngine.Object.FindObjectOfType<Terrain>(); TD = T.terrainData;
                var approach = GameObject.Find("Island_Environment/Goju_Approach").transform;
                var goju = GameObject.Find("Island_Environment/Landmarks/Pagoda_Goju_Main (site 2)").transform;

                var gates = approach.Cast<Transform>().Where(t => t.name.StartsWith("Torii")).ToList();
                var lanterns = approach.Cast<Transform>().Where(t => t.name.StartsWith("Lantern_Stone_A")).ToList();
                var bigLanterns = approach.Cast<Transform>().Where(t => t.name.StartsWith("Lantern_Stone_C")).ToList();
                var platforms = approach.Cast<Transform>().Where(t => t.name.StartsWith("Platform_Stone")).ToList();
                Note(string.Format("before: {0} gates, {1} path lanterns, {2} base lanterns, {3} platforms", gates.Count, lanterns.Count, bigLanterns.Count, platforms.Count));

                // route: existing direction from pagoda through the furthest gate
                var gp = Flat(goju.position);
                var far = gates.OrderByDescending(g => (Flat(g.position) - gp).sqrMagnitude).First();
                var dir = (Flat(far.position) - gp).normalized;
                float startD = 14f, endD = (Flat(far.position) - gp).magnitude + 6f;

                // smoothed, grade-limited profile along the centre line
                var ds = new List<float>(); for (float d = 0; d <= endD + 0.01f; d += 1f) ds.Add(d);
                var raw = ds.Select(d => H(gp + dir * d)).ToArray();
                var prof = new float[raw.Length];
                for (int i = 0; i < raw.Length; i++)
                {   // 15 m moving average
                    int a = Math.Max(0, i - 7), b = Math.Min(raw.Length - 1, i + 7); prof[i] = raw.Skip(a).Take(b - a + 1).Average();
                }
                prof[0] = H(gp + dir * startD); // join the pagoda apron level
                for (int i = 1; i < prof.Length; i++) prof[i] = Mathf.Clamp(prof[i], prof[i - 1] - MaxGrade, prof[i - 1] + MaxGrade);
                for (int i = 0; i < (int)startD; i++) prof[i] = prof[(int)startD];

                // grade the path corridor
                GradeCorridor(gp, dir, startD, endD, prof);

                // gates: keep ~endD/16, reuse existing objects in order of distance, delete the rest
                var ordered = gates.OrderBy(g => (Flat(g.position) - gp).sqrMagnitude).ToList();
                int keep = Mathf.Max(6, Mathf.FloorToInt((endD - 24f) / GateSpacing) + 1);
                var kept = new List<Transform>();
                for (int i = 0; i < ordered.Count; i++)
                {
                    if (i < keep)
                    {
                        float d = 24f + i * GateSpacing; if (d > endD - 4f) d = endD - 4f;
                        Seat(ordered[i], gp + dir * d, Bearing(dir), Prof(prof, d));
                        kept.Add(ordered[i]);
                    }
                    else UnityEngine.Object.DestroyImmediate(ordered[i].gameObject);
                }
                // lantern pairs midway between every other gate, just outside the pillars on the graded edge
                var side = new Vector2(dir.y, -dir.x);
                var lq = new Queue<Transform>(lanterns.OrderBy(l => (Flat(l.position) - gp).sqrMagnitude));
                int pairs = 0;
                for (int i = 0; i < kept.Count - 1 && lq.Count >= 2; i += 2)
                {
                    float d = 24f + i * GateSpacing + GateSpacing * 0.5f;
                    foreach (var s in new[] { 1f, -1f })
                    {
                        var p = gp + dir * d + side * s * 2.2f;
                        var l = lq.Dequeue(); Seat(l, p, Bearing(dir), H(p));
                    }
                    pairs++;
                }
                while (lq.Count > 0) UnityEngine.Object.DestroyImmediate(lq.Dequeue().gameObject);
                // base lanterns on platforms flanking the path start
                for (int i = 0; i < platforms.Count && i < 2; i++)
                {
                    float s = i == 0 ? 1f : -1f;
                    var p = gp + dir * 14f + side * s * 4.2f;
                    Seat(platforms[i], p, Bearing(dir), H(p));
                    var top = Bounds(platforms[i].gameObject).max.y;
                    if (i < bigLanterns.Count) { Seat(bigLanterns[i], p, Bearing(dir), top); }
                }
                Note(string.Format("after: {0} gates at {1} m spacing along {2:0} m graded path (max grade {3:0}%), {4} lantern pairs", kept.Count, GateSpacing, endD - startD, MaxGrade * 100, pairs));

                // Sanju + garden torii: level patch under the opening
                foreach (var path in new[] { "Island_Environment/Sanju_Shrine", "Island_Environment/Garden" })
                {
                    var grp = GameObject.Find(path); if (grp == null) continue;
                    foreach (Transform t in grp.transform)
                    {
                        if (!t.name.StartsWith("Torii") || t.name.Contains("Votive")) continue;
                        var b = Bounds(t.gameObject);
                        float baseY = b.min.y + 0.12f;
                        LevelPatch(Flat(t.position), t.forward, t.right, Mathf.Max(b.extents.x, b.extents.z) * 0.75f, 2.5f, baseY);
                        Note("  levelled opening of " + path.Split('/').Last() + "/" + t.name + " to " + baseY.ToString("0.00"));
                    }
                }

                TD.SetTreeInstances(TD.treeInstances, true);
                EditorUtility.SetDirty(TD); AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
                Note("Scene 1 + terrain saved.");

                // verification: terrain intrusion inside every torii opening
                foreach (var g in UnityEngine.Object.FindObjectsOfType<Transform>().Where(x => x.name.StartsWith("Torii") && !x.name.Contains("Votive") && x.parent != null && x.parent.parent != null && x.parent.parent.name == "Island_Environment"))
                {
                    var b = Bounds(g.gameObject); float half = Mathf.Max(b.extents.x, b.extents.z) * 0.62f, worst = -9;
                    var fw = Flat3(g.forward); var rt = Flat3(g.right);
                    for (float u = -half; u <= half + 0.01f; u += half / 4) for (float v = -1.5f; v <= 1.51f; v += 0.75f)
                        worst = Mathf.Max(worst, H(Flat(g.position + rt * u + fw * v)) - b.min.y);
                    Note(string.Format("  check {0}/{1}: terrain inside opening max {2:+0.00;-0.00} m relative to gate base", g.parent.name, g.name, worst));
                }
            }
            catch (Exception e) { Note("EXCEPTION: " + e); throw; }
            finally { File.WriteAllText(Path.Combine(outDir, "toriifix.txt"), R.ToString()); }
        }

        static float Prof(float[] p, float d) { int i = Mathf.Clamp(Mathf.FloorToInt(d), 0, p.Length - 2); return Mathf.Lerp(p[i], p[i + 1], d - i); }

        static void GradeCorridor(Vector2 origin, Vector2 dir, float d0, float d1, float[] prof)
        {
            int res = TD.heightmapResolution; var tp = T.transform.position;
            var side = new Vector2(dir.y, -dir.x);
            var pts = new[] { origin + dir * (d0 - Blend) + side * (PathHalf + Blend), origin + dir * (d0 - Blend) - side * (PathHalf + Blend), origin + dir * (d1 + Blend) + side * (PathHalf + Blend), origin + dir * (d1 + Blend) - side * (PathHalf + Blend) };
            int x0 = Idx(pts.Min(p => p.x) - tp.x, TD.size.x, res), x1 = Idx(pts.Max(p => p.x) - tp.x, TD.size.x, res);
            int z0 = Idx(pts.Min(p => p.y) - tp.z, TD.size.z, res), z1 = Idx(pts.Max(p => p.y) - tp.z, TD.size.z, res);
            var hs = TD.GetHeights(x0, z0, x1 - x0 + 1, z1 - z0 + 1);
            float maxCut = 0, maxFill = 0;
            for (int z = 0; z <= z1 - z0; z++)
                for (int x = 0; x <= x1 - x0; x++)
                {
                    var w = new Vector2(tp.x + (x0 + x) / (float)(res - 1) * TD.size.x, tp.z + (z0 + z) / (float)(res - 1) * TD.size.z);
                    var rel = w - origin; float along = Vector2.Dot(rel, dir), across = Mathf.Abs(Vector2.Dot(rel, side));
                    if (along < d0 - Blend || along > d1 + Blend) continue;
                    float dAcross = Mathf.Max(0, across - PathHalf), dAlong = Mathf.Max(0, Mathf.Max(d0 - along, along - d1));
                    float d = Mathf.Sqrt(dAcross * dAcross + dAlong * dAlong);
                    if (d >= Blend) continue;
                    float k = d <= 0 ? 1f : 1f - Mathf.SmoothStep(0, 1, d / Blend);
                    float target = (Prof(prof, Mathf.Clamp(along, d0, d1)) - tp.y) / TD.size.y;
                    float orig = hs[z, x], nv = Mathf.Lerp(orig, target, k);
                    float delta = (nv - orig) * TD.size.y; if (delta < 0) maxCut = Mathf.Max(maxCut, -delta); else maxFill = Mathf.Max(maxFill, delta);
                    hs[z, x] = nv;
                }
            TD.SetHeights(x0, z0, hs);
            Note(string.Format("  graded path corridor {0:0} m long, {1:0.0} m wide (+{2} m blend); max cut {3:0.0} m, max fill {4:0.0} m", d1 - d0, PathHalf * 2, Blend, maxCut, maxFill));
        }

        static void LevelPatch(Vector2 c, Vector3 fwd, Vector3 right, float halfW, float halfD, float y)
        {
            int res = TD.heightmapResolution; var tp = T.transform.position;
            var f = new Vector2(fwd.x, fwd.z).normalized; var r = new Vector2(right.x, right.z).normalized;
            float reach = Mathf.Sqrt(halfW * halfW + halfD * halfD) + 4f;
            int x0 = Idx(c.x - reach - tp.x, TD.size.x, res), x1 = Idx(c.x + reach - tp.x, TD.size.x, res);
            int z0 = Idx(c.y - reach - tp.z, TD.size.z, res), z1 = Idx(c.y + reach - tp.z, TD.size.z, res);
            var hs = TD.GetHeights(x0, z0, x1 - x0 + 1, z1 - z0 + 1);
            for (int z = 0; z <= z1 - z0; z++)
                for (int x = 0; x <= x1 - x0; x++)
                {
                    var w = new Vector2(tp.x + (x0 + x) / (float)(res - 1) * TD.size.x, tp.z + (z0 + z) / (float)(res - 1) * TD.size.z);
                    var rel = w - c; float du = Mathf.Max(0, Mathf.Abs(Vector2.Dot(rel, r)) - halfW), dv = Mathf.Max(0, Mathf.Abs(Vector2.Dot(rel, f)) - halfD);
                    float d = Mathf.Sqrt(du * du + dv * dv); if (d >= 4f) continue;
                    float k = d <= 0 ? 1f : 1f - Mathf.SmoothStep(0, 1, d / 4f);
                    float orig = hs[z, x], target = (y - tp.y) / TD.size.y;
                    if (orig > target) hs[z, x] = Mathf.Lerp(orig, target, k);   // only cut what intrudes into the opening
                }
            TD.SetHeights(x0, z0, hs);
        }

        static void Seat(Transform t, Vector2 xz, float yaw, float groundY)
        {
            t.rotation = Quaternion.Euler(0, yaw, 0);
            t.position = new Vector3(xz.x, 0, xz.y);
            var b = Bounds(t.gameObject); float off = b.min.y - t.position.y;
            t.position = new Vector3(xz.x, groundY - off - 0.08f, xz.y);
        }

        static Bounds Bounds(GameObject g)
        {
            var lod = g.GetComponentInChildren<LODGroup>();
            IEnumerable<Renderer> rs = g.GetComponentsInChildren<Renderer>(true);
            if (lod != null && lod.GetLODs().Length > 0) rs = lod.GetLODs()[0].renderers.Where(r => r);
            var b = rs.First().bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
        }

        static int Idx(float v, float size, int res) => Mathf.Clamp(Mathf.RoundToInt(v / size * (res - 1)), 0, res - 1);
        static float H(Vector2 p) => T.SampleHeight(new Vector3(p.x, 0, p.y)) + T.transform.position.y;
        static Vector2 Flat(Vector3 v) => new Vector2(v.x, v.z);
        static Vector3 Flat3(Vector3 v) { v.y = 0; return v.normalized; }
        static float Bearing(Vector2 d) => Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
    }
}
