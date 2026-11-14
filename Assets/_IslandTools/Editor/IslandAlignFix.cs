// Editor-only. Phase 2/4 repairs (reversible):
//  1) Donggung courts: deactivate split-remnant clusters (pieces > 5 m outside the compound core) in the building prefabs
//  2) Geoncheonggung: raise terrain inside its footprint to just under its ground-level surfaces (fills the sunken pits)
//  3) Gyedong: lower terrain 0.12 m under the house so plinths/steps are visible
//  4) Scene 1 player: ThirdPersonController.GroundedRadius 0.28 -> 0.20 (instance override; wall re-jump fix)
// Batchmode: ... -executeMethod IslandTools.IslandAlignFix.Run -surveyOut <dir>
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
    public static class IslandAlignFix
    {
        const string Scene1Path = "Assets/Free Island Collection/Scenes/Scene 1.unity";
        static readonly StringBuilder R = new StringBuilder();
        static void Note(string s) { R.AppendLine(s); Debug.Log("[AlignFix] " + s); }
        static Terrain T; static TerrainData TD;

        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            int ai = Array.IndexOf(args, "-surveyOut");
            var outDir = ai >= 0 && ai + 1 < args.Length ? args[ai + 1] : "_Backups/alignfix";
            Directory.CreateDirectory(outDir);
            try
            {
                var scene = EditorSceneManager.OpenScene(Scene1Path, OpenSceneMode.Single);
                T = UnityEngine.Object.FindObjectOfType<Terrain>(); TD = T.terrainData;
                var korean = GameObject.Find("Island_Environment/Korean_Buildings").transform;

                foreach (Transform b in korean) if (b.name.StartsWith("KR_Donggung_Court")) Remnants(b);
                foreach (Transform b in korean)
                    if (PrefabUtility.IsAnyPrefabInstanceRoot(b.gameObject)) PrefabUtility.ApplyPrefabInstance(b.gameObject, InteractionMode.AutomatedAction);

                var gcg = korean.Find("KR_Geoncheonggung_Palace"); if (gcg) FillUnder(gcg);
                var gy = korean.Find("KR_Gyedong_House"); if (gy) LowerUnder(gy, 0.12f);
                TD.SetTreeInstances(TD.treeInstances, true); EditorUtility.SetDirty(TD);

                var player = GameObject.Find("Player_Setup/PlayerArmature");
                var tpc = player ? player.GetComponent("ThirdPersonController") : null;
                if (tpc != null)
                {
                    var so = new SerializedObject(tpc); var p = so.FindProperty("GroundedRadius");
                    Note(string.Format("Player GroundedRadius {0:0.00} -> 0.20 (scene instance override; capsule unchanged)", p.floatValue));
                    p.floatValue = 0.20f; so.ApplyModifiedProperties();
                }

                AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
                Note("Saved.");
            }
            catch (Exception e) { Note("EXCEPTION: " + e); throw; }
            finally { File.WriteAllText(Path.Combine(outDir, "alignfix.txt"), R.ToString()); }
        }

        public static void RenderRepairs()
        {
            var args = Environment.GetCommandLineArgs();
            int ai = Array.IndexOf(args, "-surveyOut");
            var outDir = ai >= 0 && ai + 1 < args.Length ? args[ai + 1] : "_Backups/alignfix";
            Directory.CreateDirectory(outDir);
            ShaderUtil.allowAsyncCompilation = false;
            EditorSceneManager.OpenScene(Scene1Path, OpenSceneMode.Single);
            foreach (var p in UnityEngine.Object.FindObjectsOfType<ReflectionProbe>()) p.RenderProbe();
            var cam = new GameObject("Cam").AddComponent<Camera>(); cam.allowHDR = false; cam.fieldOfView = 55; cam.farClipPlane = 3000; cam.nearClipPlane = 0.1f;
            var ap = GameObject.Find("Island_Environment/Goju_Approach").transform;
            var goju = GameObject.Find("Island_Environment/Landmarks/Pagoda_Goju_Main (site 2)").transform;
            var gates = ap.Cast<Transform>().Where(t => t.name.StartsWith("Torii")).OrderByDescending(t => (t.position - goju.position).sqrMagnitude).ToList();
            var dir = (goju.position - gates[0].position); dir.y = 0; dir.Normalize();
            cam.transform.position = gates[0].position - dir * 8f + Vector3.up * 1.7f; cam.transform.LookAt(goju.position + Vector3.up * 15f);
            Shot(cam, Path.Combine(outDir, "torii_from_path_start.png"));
            var mid = gates[gates.Count / 2].position; var side = Vector3.Cross(Vector3.up, dir);
            cam.transform.position = mid + side * 45f + Vector3.up * 25f; cam.transform.LookAt(mid);
            Shot(cam, Path.Combine(outDir, "torii_side.png"));
            cam.transform.position = gates[gates.Count / 2].position - dir * 2f + Vector3.up * 1.6f; cam.transform.LookAt(gates[gates.Count / 2].position + dir * 20f + Vector3.up * 2f);
            Shot(cam, Path.Combine(outDir, "torii_walk_view.png"));
            var gcg = GameObject.Find("Island_Environment/Korean_Buildings/KR_Geoncheonggung_Palace").transform;
            var bb = PB(gcg);
            cam.transform.position = bb.center + new Vector3(-0.5f, 0.8f, -0.8f).normalized * 90f; cam.transform.LookAt(bb.center);
            Shot(cam, Path.Combine(outDir, "gcg_after_fill.png"));
            cam.transform.position = new Vector3(1213f, 57f, 2276f); cam.transform.LookAt(new Vector3(1213f, 54f, 2290f));
            Shot(cam, Path.Combine(outDir, "gcg_gate9_after.png"));
            var c3 = PB(GameObject.Find("Island_Environment/Korean_Buildings/KR_Donggung_Court_03").transform);
            cam.transform.position = c3.center + new Vector3(-0.6f, 0.9f, -0.7f).normalized * 80f; cam.transform.LookAt(c3.center);
            Shot(cam, Path.Combine(outDir, "court03_after.png"));
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        public static void RenderCourts()
        {
            var args = Environment.GetCommandLineArgs();
            int ai = Array.IndexOf(args, "-surveyOut");
            var outDir = ai >= 0 && ai + 1 < args.Length ? args[ai + 1] : "_Backups/alignfix";
            Directory.CreateDirectory(outDir);
            ShaderUtil.allowAsyncCompilation = false;
            EditorSceneManager.OpenScene(Scene1Path, OpenSceneMode.Single);
            var korean = GameObject.Find("Island_Environment/Korean_Buildings").transform;
            var cam = new GameObject("Cam").AddComponent<Camera>(); cam.allowHDR = false; cam.fieldOfView = 50; cam.farClipPlane = 2000;
            foreach (Transform b in korean)
            {
                var bb = PB(b); float r = bb.extents.magnitude;
                cam.transform.position = bb.center + new Vector3(-0.6f, 0.9f, -0.7f).normalized * r * 1.5f; cam.transform.LookAt(bb.center);
                var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                cam.targetTexture = rt; cam.Render(); cam.Render(); RenderTexture.active = rt;
                var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
                RenderTexture.active = null; cam.targetTexture = null;
                File.WriteAllBytes(Path.Combine(outDir, "court_" + b.name + ".png"), tex.EncodeToPNG());
            }
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        // Stricter remnant rule: piece CENTRE > 4 m outside the core; roof/tile pieces only if they touch such a piece.
        static List<Transform> RemnantPieces(Transform b, StringBuilder log)
        {
            var pieces = b.GetComponentsInChildren<Renderer>(true).Select(r => Piece(r.transform, b)).Distinct().Where(p => p.gameObject.activeSelf).ToList();
            var inv = Quaternion.Inverse(b.rotation);
            Vector3 L(Vector3 w) => inv * (w - b.position);
            var info = pieces.Select(p => (p, bb: PB(p))).Select(t => (t.p, t.bb, l: L(t.bb.center))).ToList();
            float Pct(IEnumerable<float> v, float q) { var s = v.OrderBy(x => x).ToList(); return s[Mathf.Clamp((int)(q * (s.Count - 1)), 0, s.Count - 1)]; }
            float x0 = Pct(info.Select(c => c.l.x), 0.03f), x1 = Pct(info.Select(c => c.l.x), 0.97f), z0 = Pct(info.Select(c => c.l.z), 0.03f), z1 = Pct(info.Select(c => c.l.z), 0.97f);
            float Out(Vector3 l) => Mathf.Max(Mathf.Max(x0 - l.x, l.x - x1), Mathf.Max(z0 - l.z, l.z - z1));
            bool IsRoof(string n) => System.Text.RegularExpressions.Regex.IsMatch(n, "Giwa|Roof", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            // protect real entrance gates (WoodframeF passed the traversal test) and anything within 4 m of them
            var gatesB = info.Where(t => t.p.name.StartsWith("SM_DG_WoodframeF")).Select(t => t.bb).ToList();
            bool NearGate(Bounds bb) => gatesB.Any(g => { var e = g; e.Expand(8f); return e.Intersects(bb); });
            var seeds = info.Where(t => Out(t.l) > 4f && !IsRoof(t.p.name) && !NearGate(t.bb)).ToList();
            var roofs = info.Where(t => Out(t.l) > 4f && IsRoof(t.p.name) && seeds.Any(s => { var e = s.bb; e.Expand(0.8f); return e.Intersects(t.bb); })).ToList();
            var all = seeds.Concat(roofs).Select(t => t.p).ToList();
            log.AppendLine(string.Format("{0}: core {1:0}x{2:0} m; remnant candidates {3} ({4} structural + {5} roof pieces): {6}", b.name, x1 - x0, z1 - z0, all.Count, seeds.Count, roofs.Count,
                string.Join(", ", all.Select(p => System.Text.RegularExpressions.Regex.Replace(p.name, @"\d+$", "")).GroupBy(n => n).Select(g => g.Key + "x" + g.Count()))));
            return all;
        }

        public static void RemnantPreview()
        {
            var args = Environment.GetCommandLineArgs();
            int ai = Array.IndexOf(args, "-surveyOut");
            var outDir = ai >= 0 && ai + 1 < args.Length ? args[ai + 1] : "_Backups/alignfix";
            bool apply = args.Contains("-apply");
            Directory.CreateDirectory(outDir);
            ShaderUtil.allowAsyncCompilation = false;
            var log = new StringBuilder();
            var scene = EditorSceneManager.OpenScene(Scene1Path, OpenSceneMode.Single);
            var korean = GameObject.Find("Island_Environment/Korean_Buildings").transform;
            var cam = new GameObject("Cam") { hideFlags = HideFlags.DontSave }.AddComponent<Camera>(); cam.allowHDR = false; cam.fieldOfView = 50; cam.farClipPlane = 2000;
            foreach (Transform b in korean)
            {
                if (b.name != "KR_Donggung_Court_02" && b.name != "KR_Donggung_Court_03") continue;   // Court_01: manual review
                var rem = RemnantPieces(b, log);
                var bb = PB(b); float r = bb.extents.magnitude;
                cam.transform.position = bb.center + new Vector3(-0.6f, 0.9f, -0.7f).normalized * r * 1.5f; cam.transform.LookAt(bb.center);
                Shot(cam, Path.Combine(outDir, "rem_" + b.name + "_before.png"));
                foreach (var p in rem) p.gameObject.SetActive(false);
                Shot(cam, Path.Combine(outDir, "rem_" + b.name + "_after.png"));
                if (!apply) foreach (var p in rem) p.gameObject.SetActive(true);
            }
            if (apply)
            {
                foreach (Transform b in korean) if (b.name == "KR_Donggung_Court_02" || b.name == "KR_Donggung_Court_03") PrefabUtility.ApplyPrefabInstance(b.gameObject, InteractionMode.AutomatedAction);
                AssetDatabase.SaveAssets(); log.AppendLine("APPLIED to building prefabs.");
            }
            File.WriteAllText(Path.Combine(outDir, apply ? "remnants_applied.txt" : "remnants_preview.txt"), log.ToString());
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        static void Shot(Camera cam, string file)
        {
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            cam.targetTexture = rt; cam.Render(); cam.Render(); RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
            RenderTexture.active = null; cam.targetTexture = null; File.WriteAllBytes(file, tex.EncodeToPNG());
        }

        // ---------------------------------------------------------------- 1
        static Transform Piece(Transform x, Transform building) { while (x.parent != null && x.parent != building && x.parent.parent != building) x = x.parent; return x; }
        static Bounds PB(Transform p) { var rs = p.GetComponentsInChildren<Renderer>(true); var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b; }

        static void Remnants(Transform b)
        {
            var pieces = b.GetComponentsInChildren<Renderer>(true).Select(r => Piece(r.transform, b)).Distinct().Where(p => p.gameObject.activeSelf).ToList();
            var inv = Quaternion.Inverse(b.rotation);
            Vector3 L(Vector3 w) => inv * (w - b.position);
            var centres = pieces.Select(p => L(PB(p).center)).ToList();
            float Pct(IEnumerable<float> v, float q) { var s = v.OrderBy(x => x).ToList(); return s[Mathf.Clamp((int)(q * (s.Count - 1)), 0, s.Count - 1)]; }
            float x0 = Pct(centres.Select(c => c.x), 0.03f), x1 = Pct(centres.Select(c => c.x), 0.97f), z0 = Pct(centres.Select(c => c.z), 0.03f), z1 = Pct(centres.Select(c => c.z), 0.97f);
            float Outside(Bounds wb)
            {   // how far the piece's furthest corner lies outside the core rectangle (local space)
                float m = 0;
                foreach (var cx in new[] { wb.min.x, wb.max.x }) foreach (var cz in new[] { wb.min.z, wb.max.z })
                {
                    var l = L(new Vector3(cx, wb.center.y, cz));
                    m = Mathf.Max(m, Mathf.Max(Mathf.Max(x0 - l.x, l.x - x1), Mathf.Max(z0 - l.z, l.z - z1)));
                }
                return m;
            }
            var outside = pieces.Select(p => (p, b: PB(p))).Where(t => Outside(t.b) > 1.5f).ToList();
            // cluster touching outside pieces
            int n = outside.Count; var parent = Enumerable.Range(0, n).ToArray();
            int Find(int i) { while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; } return i; }
            for (int i = 0; i < n; i++) for (int j = i + 1; j < n; j++) { var e = outside[i].b; e.Expand(0.6f); if (e.Intersects(outside[j].b)) parent[Find(i)] = Find(j); }
            int hidden = 0, kept = 0;
            foreach (var g in Enumerable.Range(0, n).GroupBy(Find))
            {
                float reach = g.Max(i => Outside(outside[i].b));
                var names = string.Join(",", g.Select(i => System.Text.RegularExpressions.Regex.Replace(outside[i].p.name, @"\d+$", "")).Distinct().Take(6));
                if (reach > 5f) { foreach (var i in g) outside[i].p.gameObject.SetActive(false); hidden += g.Count(); Note(string.Format("  {0}: deactivated remnant cluster of {1} pieces reaching {2:0.0} m outside the walls [{3}]", b.name, g.Count(), reach, names)); }
                else { kept += g.Count(); Note(string.Format("  {0}: kept perimeter cluster of {1} pieces ({2:0.0} m outside) [{3}]", b.name, g.Count(), reach, names)); }
            }
            Note(string.Format("{0}: {1} remnant pieces deactivated, {2} perimeter pieces kept", b.name, hidden, kept));
        }

        // ---------------------------------------------------------------- 2
        static void FillUnder(Transform b)
        {
            const int Layer = 30;
            float baseY = b.position.y;
            // proxies of low, ground-level pieces (sand, paving, steps, terrace bases, floors)
            var proxies = new List<GameObject>();
            foreach (var mf in b.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf.gameObject.activeInHierarchy || mf.sharedMesh == null) continue;
                var rb = mf.GetComponent<Renderer>().bounds;
                if (rb.size.y > 1.4f || rb.max.y > baseY + 4.5f) continue;
                var p = new GameObject("gp") { layer = Layer, hideFlags = HideFlags.DontSave };
                p.transform.SetPositionAndRotation(mf.transform.position, mf.transform.rotation); p.transform.localScale = mf.transform.lossyScale;
                p.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh; proxies.Add(p);
            }
            Physics.SyncTransforms();
            var bb = PB(b); var tp = T.transform.position; int res = TD.heightmapResolution;
            const float Blend = 10f;
            int x0 = Idx(bb.min.x - Blend - tp.x, TD.size.x, res), x1 = Idx(bb.max.x + Blend - tp.x, TD.size.x, res);
            int z0 = Idx(bb.min.z - Blend - tp.z, TD.size.z, res), z1 = Idx(bb.max.z + Blend - tp.z, TD.size.z, res);
            int w = x1 - x0 + 1, h = z1 - z0 + 1;
            var hs = TD.GetHeights(x0, z0, w, h);
            var target = new float[h, w]; var has = new bool[h, w]; var inside = new bool[h, w];
            for (int z = 0; z < h; z++) for (int x = 0; x < w; x++)
            {
                var wp = new Vector3(tp.x + (x0 + x) / (float)(res - 1) * TD.size.x, 0, tp.z + (z0 + z) / (float)(res - 1) * TD.size.z);
                inside[z, x] = wp.x >= bb.min.x && wp.x <= bb.max.x && wp.z >= bb.min.z && wp.z <= bb.max.z;
                if (!inside[z, x]) continue;
                if (Physics.Raycast(new Vector3(wp.x, baseY + 5f, wp.z), Vector3.down, out var hit, 8f, 1 << Layer)) { target[z, x] = hit.point.y - 0.06f; has[z, x] = true; }
            }
            // fill gaps inside the footprint with the LOWEST neighbouring surface (no pits, never above a courtyard)
            for (int iter = 0; iter < 200; iter++)
            {
                bool any = false; var nt = (float[,])target.Clone(); var nh = (bool[,])has.Clone();
                for (int z = 0; z < h; z++) for (int x = 0; x < w; x++)
                {
                    if (!inside[z, x] || has[z, x]) continue;
                    float m = float.MaxValue;
                    for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
                    { int zz = z + dz, xx = x + dx; if (zz < 0 || xx < 0 || zz >= h || xx >= w || !has[zz, xx]) continue; m = Mathf.Min(m, target[zz, xx]); }
                    if (m < float.MaxValue) { nt[z, x] = m; nh[z, x] = true; any = true; }
                }
                target = nt; has = nh; if (!any) break;
            }
            float maxRaise = 0; int raised = 0;
            for (int z = 0; z < h; z++) for (int x = 0; x < w; x++)
            {
                float orig = hs[z, x] * TD.size.y + tp.y, nv = orig;
                if (inside[z, x] && has[z, x]) nv = Mathf.Max(orig, target[z, x]);
                else
                {   // blend band: distance to footprint edge, use nearest edge target
                    var wp = new Vector3(tp.x + (x0 + x) / (float)(res - 1) * TD.size.x, 0, tp.z + (z0 + z) / (float)(res - 1) * TD.size.z);
                    float dx = Mathf.Max(0, Mathf.Max(bb.min.x - wp.x, wp.x - bb.max.x)), dz = Mathf.Max(0, Mathf.Max(bb.min.z - wp.z, wp.z - bb.max.z));
                    float d = Mathf.Sqrt(dx * dx + dz * dz); if (d <= 0 || d >= Blend) continue;
                    int ex = Mathf.Clamp(x + (wp.x < bb.min.x ? 1 : wp.x > bb.max.x ? -1 : 0) * Mathf.CeilToInt(dx / (TD.size.x / (res - 1))), 0, w - 1);
                    int ez = Mathf.Clamp(z + (wp.z < bb.min.z ? 1 : wp.z > bb.max.z ? -1 : 0) * Mathf.CeilToInt(dz / (TD.size.z / (res - 1))), 0, h - 1);
                    if (!has[ez, ex]) continue;
                    float k = 1f - Mathf.SmoothStep(0, 1, d / Blend);
                    nv = Mathf.Max(orig, Mathf.Lerp(orig, target[ez, ex], k));
                }
                if (nv > orig + 0.001f) { raised++; maxRaise = Mathf.Max(maxRaise, nv - orig); hs[z, x] = (nv - tp.y) / TD.size.y; }
            }
            TD.SetHeights(x0, z0, hs);
            foreach (var p in proxies) UnityEngine.Object.DestroyImmediate(p);
            Note(string.Format("{0}: terrain raised under ground-level surfaces at {1} samples (max raise {2:0.00} m, {3} m blend); pits filled", b.name, raised, maxRaise, Blend));
        }

        // ---------------------------------------------------------------- 3
        static void LowerUnder(Transform b, float by)
        {
            var bb = PB(b); var tp = T.transform.position; int res = TD.heightmapResolution; const float Blend = 4f;
            int x0 = Idx(bb.min.x - Blend - tp.x, TD.size.x, res), x1 = Idx(bb.max.x + Blend - tp.x, TD.size.x, res);
            int z0 = Idx(bb.min.z - Blend - tp.z, TD.size.z, res), z1 = Idx(bb.max.z + Blend - tp.z, TD.size.z, res);
            var hs = TD.GetHeights(x0, z0, x1 - x0 + 1, z1 - z0 + 1);
            for (int z = 0; z <= z1 - z0; z++) for (int x = 0; x <= x1 - x0; x++)
            {
                var wp = new Vector3(tp.x + (x0 + x) / (float)(res - 1) * TD.size.x, 0, tp.z + (z0 + z) / (float)(res - 1) * TD.size.z);
                float dx = Mathf.Max(0, Mathf.Max(bb.min.x - wp.x, wp.x - bb.max.x)), dz = Mathf.Max(0, Mathf.Max(bb.min.z - wp.z, wp.z - bb.max.z));
                float d = Mathf.Sqrt(dx * dx + dz * dz); if (d >= Blend) continue;
                float k = d <= 0 ? 1 : 1 - Mathf.SmoothStep(0, 1, d / Blend);
                hs[z, x] -= by * k / TD.size.y;
            }
            TD.SetHeights(x0, z0, hs);
            Note(string.Format("{0}: terrain lowered {1:0.00} m under the footprint ({2} m blend)", b.name, by, Blend));
        }

        static int Idx(float v, float size, int res) => Mathf.Clamp(Mathf.RoundToInt(v / size * (res - 1)), 0, res - 1);
    }
}
