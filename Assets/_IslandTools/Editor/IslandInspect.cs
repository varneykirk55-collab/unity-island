// Editor-only, READ-ONLY inspection of Scene 1 for the repair task. Scene is never saved.
//   A) collider vs visual audit            B) doorway / gate openings (visual + capsule clearance)
//   C) torii terrain intrusion + spacing    D) grounding of placed objects
//   E) startup-cost suspects
// Batchmode: Unity.exe -batchmode -quit -projectPath <proj> -executeMethod IslandTools.IslandInspect.Run -surveyOut <dir>
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
    public static class IslandInspect
    {
        const string Scene1Path = "Assets/Free Island Collection/Scenes/Scene 1.unity";
        // Starter Assets PlayerArmature
        public const float PR = 0.28f, PH = 1.8f, PStep = 0.25f, PSkin = 0.02f;
        const int VisualLayer = 30;
        static readonly StringBuilder R = new StringBuilder();
        static void Note(string s) { R.AppendLine(s); }

        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            int ai = Array.IndexOf(args, "-surveyOut");
            var outDir = ai >= 0 && ai + 1 < args.Length ? args[ai + 1] : "_Backups/inspect";
            Directory.CreateDirectory(outDir);
            try
            {
                EditorSceneManager.OpenScene(Scene1Path, OpenSceneMode.Single);
                Physics.autoSyncTransforms = true; Physics.SyncTransforms();
                var terrain = UnityEngine.Object.FindObjectOfType<Terrain>();
                var env = GameObject.Find("Island_Environment");
                var korean = env.transform.Find("Korean_Buildings");

                ColliderAudit(env);
                Doorways(korean, env, outDir);
                Torii(env, terrain);
                Grounding(env, terrain);
                Startup(terrain);
            }
            catch (Exception e) { Note("EXCEPTION: " + e); throw; }
            finally
            {
                File.WriteAllText(System.IO.Path.Combine(outDir, "inspect.txt"), R.ToString());
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        // ---------------------------------------------------------------- A
        static void ColliderAudit(GameObject env)
        {
            Note("=== A. COLLIDER vs VISUAL AUDIT ===");
            var byType = new Dictionary<string, int>();
            int noRenderer = 0, oversized = 0, diffMesh = 0, multi = 0, nonUniformMC = 0, convex = 0, triggers = 0;
            var examples = new Dictionary<string, List<string>>();
            void Ex(string k, string v) { if (!examples.TryGetValue(k, out var l)) examples[k] = l = new List<string>(); if (l.Count < 12) l.Add(v); }
            foreach (var c in env.GetComponentsInChildren<Collider>(true))
            {
                var tn = c.GetType().Name; byType.TryGetValue(tn, out var n); byType[tn] = n + 1;
                if (c.isTrigger) triggers++;
                var r = c.GetComponent<Renderer>() ?? c.GetComponentInParent<Renderer>();
                var mf = c.GetComponent<MeshFilter>();
                if (c.GetComponents<Collider>().Length > 1) { multi++; Ex("multiple colliders on one object", TPath(c.transform)); }
                if (c is MeshCollider mc)
                {
                    if (mc.convex) convex++;
                    var s = mc.transform.lossyScale;
                    if (Mathf.Abs(s.x - s.y) > 0.001f || Mathf.Abs(s.y - s.z) > 0.001f) nonUniformMC++;
                    if (mf != null && mc.sharedMesh != null && mf.sharedMesh != mc.sharedMesh) { diffMesh++; Ex("MeshCollider uses a different mesh than rendered", TPath(c.transform) + " col=" + mc.sharedMesh.name + " vis=" + mf.sharedMesh.name); }
                }
                if (r == null) { noRenderer++; Ex("collider with no renderer (invisible blocker)", TPath(c.transform) + " " + tn + " size " + c.bounds.size.ToString("0.0")); continue; }
                var cb = c.bounds; var rb = r.bounds;
                float over = Mathf.Max(Mathf.Max(rb.min.x - cb.min.x, cb.max.x - rb.max.x), Mathf.Max(Mathf.Max(rb.min.y - cb.min.y, cb.max.y - rb.max.y), Mathf.Max(rb.min.z - cb.min.z, cb.max.z - rb.max.z)));
                if (over > 0.15f) { oversized++; Ex("collider extends > 0.15 m beyond its visual", TPath(c.transform) + " " + tn + " by " + over.ToString("0.00") + " m"); }
            }
            Note("  colliders by type: " + string.Join(", ", byType.Select(k => k.Key + "=" + k.Value)));
            Note(string.Format("  oversized={0} noRenderer={1} meshDiffersFromVisual={2} objectsWithMultipleColliders={3} nonUniformScaledMeshColliders={4} convexMeshColliders={5} triggers={6}",
                oversized, noRenderer, diffMesh, multi, nonUniformMC, convex, triggers));
            foreach (var kv in examples) { Note("  -- " + kv.Key + ":"); foreach (var e in kv.Value) Note("     " + e); }
        }

        // ---------------------------------------------------------------- B
        class Opening { public Transform piece; public string building; public Vector3 centre, through, across; public float floor, width, height, sill; public bool visOpen; public string block; }

        static void Doorways(Transform korean, GameObject env, string outDir)
        {
            Note("");
            Note("=== B. DOORWAYS / GATES ===");
            // visual proxy colliders on layer 30 = exact rendered geometry of every building piece
            var proxies = new List<GameObject>();
            foreach (var mf in korean.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || mf.GetComponent<Renderer>() == null || !mf.GetComponent<Renderer>().enabled) continue;
                var p = new GameObject("vis"); p.layer = VisualLayer; p.hideFlags = HideFlags.DontSave;
                p.transform.SetPositionAndRotation(mf.transform.position, mf.transform.rotation); p.transform.localScale = mf.transform.lossyScale;
                p.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh; proxies.Add(p);
            }
            Physics.SyncTransforms();
            int visMask = 1 << VisualLayer, colMask = ~(1 << VisualLayer);
            Note("  visual proxies: " + proxies.Count);

            var all = new List<Opening>();
            foreach (Transform b in korean)
            {
                var pieces = b.GetComponentsInChildren<MeshFilter>(true)
                    .Where(m => m.sharedMesh != null && System.Text.RegularExpressions.Regex.IsMatch(m.name, "Door|Gate|Entrance|Mun|Frame", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    .Select(m => m.transform).ToList();
                foreach (var t in pieces)
                {
                    var mesh = t.GetComponent<MeshFilter>().sharedMesh; var mb = mesh.bounds;
                    var size = Vector3.Scale(mb.size, Abs(t.lossyScale));
                    if (size.y < 1.4f) continue; // windows / small panels
                    // thin horizontal axis = through direction
                    Vector3 thinL = size.x < size.z ? Vector3.right : Vector3.forward, wideL = size.x < size.z ? Vector3.forward : Vector3.right;
                    var o = new Opening { piece = t, building = b.name };
                    o.through = (t.rotation * thinL).normalized; o.through.y = 0; o.through.Normalize();
                    o.across = (t.rotation * wideL).normalized; o.across.y = 0; o.across.Normalize();
                    var cW = t.TransformPoint(mb.center);
                    o.floor = t.TransformPoint(mb.center - Vector3.up * mb.extents.y).y;
                    // floor = highest walkable surface just in front/behind within 0.6 m below the piece bottom
                    o.centre = new Vector3(cW.x, o.floor, cW.z);
                    // visual openness: rays through the opening at 0.5/1.0/1.5 m must not hit visible geometry
                    o.visOpen = true;
                    foreach (var hgt in new[] { 0.5f, 1.0f, 1.5f })
                        if (Physics.Raycast(o.centre + Vector3.up * hgt - o.through * 1.2f, o.through, 2.4f, visMask)) { o.visOpen = false; break; }
                    if (!o.visOpen) continue;
                    // measure visual width/height/sill
                    float L = Probe(o.centre + Vector3.up * 1.0f, -o.across, 3f, visMask), Rr = Probe(o.centre + Vector3.up * 1.0f, o.across, 3f, visMask);
                    o.width = L + Rr;
                    o.height = Probe(o.centre + Vector3.up * 0.3f, Vector3.up, 4f, visMask) + 0.3f;
                    var down = Physics.Raycast(o.centre + Vector3.up * 1.0f, Vector3.down, out var dh, 3f, visMask) ? dh.point.y : o.floor - 1f;
                    var front = Physics.Raycast(o.centre - o.through * 1.0f + Vector3.up * 1.0f, Vector3.down, out var fh, 4f, colMask) ? fh.point.y : float.NaN;
                    var back = Physics.Raycast(o.centre + o.through * 1.0f + Vector3.up * 1.0f, Vector3.down, out var bh, 4f, colMask) ? bh.point.y : float.NaN;
                    o.sill = down - Mathf.Min(float.IsNaN(front) ? down : front, float.IsNaN(back) ? down : back);
                    // physical: player capsule swept through the opening (bottom raised by step offset)
                    float baseY = Mathf.Max(down, Mathf.Min(float.IsNaN(front) ? down : front, float.IsNaN(back) ? down : back) + PStep);
                    var p1 = new Vector3(o.centre.x, baseY + PR + PSkin, o.centre.z) - o.through * 1.2f;
                    var p2 = p1 + Vector3.up * (PH - 2 * PR);
                    if (Physics.CapsuleCast(p1, p2, PR + PSkin, o.through, out var hit, 2.4f, colMask, QueryTriggerInteraction.Ignore))
                        o.block = hit.collider.name + " (" + hit.collider.GetType().Name + ", hit " + (hit.point.y - down).ToString("0.00") + " m above opening floor" + (IsVisualEquivalent(hit.collider) ? ", matches visual" : ", NOT the visible mesh") + ")";
                    all.Add(o);
                }
            }
            // de-duplicate (door leaves + frames of the same opening)
            var uniq = new List<Opening>();
            foreach (var o in all.OrderBy(x => x.block == null))
                if (!uniq.Any(u => (u.centre - o.centre).sqrMagnitude < 1.2f * 1.2f)) uniq.Add(o);
            int blocked = uniq.Count(u => u.block != null);
            Note(string.Format("  visually open doorway/gate openings: {0}; capsule BLOCKED: {1}; clear: {2}", uniq.Count, blocked, uniq.Count - blocked));
            foreach (var g in uniq.GroupBy(u => u.building))
            {
                Note(string.Format("  [{0}] openings {1}, blocked {2}", g.Key, g.Count(), g.Count(u => u.block != null)));
                foreach (var o in g.Where(u => u.block != null).Take(25))
                    Note(string.Format("     BLOCKED {0} at ({1:0.0},{2:0.0},{3:0.0}) visual width {4:0.00} height {5:0.00} sill {6:0.00} -> {7}",
                        o.piece.name, o.centre.x, o.centre.y, o.centre.z, o.width, o.height, o.sill, o.block));
            }
            // dimensions summary of blocked
            var bl = uniq.Where(u => u.block != null).ToList();
            if (bl.Count > 0)
            {
                Note(string.Format("  blocked openings: width<0.62: {0}, height<1.85: {1}, sill>0.25: {2}, blocker NOT visible mesh: {3}",
                    bl.Count(o => o.width < 0.62f), bl.Count(o => o.height < 1.85f), bl.Count(o => o.sill > PStep), bl.Count(o => o.block.Contains("NOT the visible"))));
                var blockerNames = bl.Select(o => o.block.Split(' ')[0]).Select(n => System.Text.RegularExpressions.Regex.Replace(n, @"\d+$", "")).GroupBy(n => n).OrderByDescending(x => x.Count());
                Note("  most common blocking pieces: " + string.Join(", ", blockerNames.Take(15).Select(x => x.Key + "x" + x.Count())));
            }
            File.WriteAllLines(System.IO.Path.Combine(outDir, "openings.csv"), new[] { "building,piece,x,y,z,throughX,throughZ,width,height,sill,blocked" }
                .Concat(uniq.Select(o => string.Join(",", o.building, o.piece.name, o.centre.x.ToString("0.00"), o.centre.y.ToString("0.00"), o.centre.z.ToString("0.00"),
                    o.through.x.ToString("0.000"), o.through.z.ToString("0.000"), o.width.ToString("0.00"), o.height.ToString("0.00"), o.sill.ToString("0.00"), (o.block ?? "").Replace(",", ";")))));
            foreach (var p in proxies) UnityEngine.Object.DestroyImmediate(p);
        }

        static bool IsVisualEquivalent(Collider c) => c is MeshCollider mc && c.GetComponent<MeshFilter>() != null && mc.sharedMesh == c.GetComponent<MeshFilter>().sharedMesh;
        static float Probe(Vector3 from, Vector3 dir, float max, int mask) => Physics.Raycast(from, dir, out var h, max, mask) ? h.distance : max;
        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        // ---------------------------------------------------------------- C
        static void Torii(GameObject env, Terrain t)
        {
            Note("");
            Note("=== C. TORII GATES ===");
            var gates = env.GetComponentsInChildren<Transform>(true).Where(x => x.parent != null && x.parent.parent == env.transform && x.name.StartsWith("Torii")).ToList();
            Note("  torii instances: " + gates.Count);
            foreach (var g in gates.GroupBy(x => x.parent.name))
            {
                var list = g.ToList();
                var spacing = new List<float>();
                for (int i = 1; i < list.Count; i++) spacing.Add(Vector3.Distance(Flat(list[i].position), Flat(list[i - 1].position)));
                Note(string.Format("  [{0}] {1} gates, spacing {2}", g.Key, list.Count, spacing.Count > 0 ? spacing.Min().ToString("0.0") + "-" + spacing.Max().ToString("0.0") + " m" : "n/a"));
                int bad = 0;
                foreach (var gate in list)
                {
                    var rs = gate.GetComponentsInChildren<Renderer>(true); var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                    float baseY = b.min.y;
                    // inner opening: between pillars (~70% of width), through-depth +-1.5 m
                    var right = gate.right; right.y = 0; right.Normalize(); var fwd = gate.forward; fwd.y = 0; fwd.Normalize();
                    float halfW = Mathf.Max(b.extents.x, b.extents.z) * 0.62f;
                    float maxIntr = float.MinValue; Vector3 worst = gate.position;
                    for (float u = -halfW; u <= halfW + 0.01f; u += halfW / 4)
                        for (float v = -1.5f; v <= 1.51f; v += 0.75f)
                        {
                            var p = gate.position + right * u + fwd * v;
                            float h = t.SampleHeight(p) + t.transform.position.y;
                            if (h - baseY > maxIntr) { maxIntr = h - baseY; worst = p; }
                        }
                    float clear = b.size.y - 1.2f; // approximate clear height under the lower crossbeam
                    if (maxIntr > 0.3f) bad++;
                    if (maxIntr > 0.3f || g.Count() < 6)
                        Note(string.Format("     {0} at ({1:0},{2:0.0},{3:0}) terrain rises {4:0.00} m above gate base inside opening (opening height ~{5:0.0} m)",
                            gate.name, gate.position.x, gate.position.y, gate.position.z, maxIntr, clear));
                }
                Note("     gates with terrain intruding > 0.3 m: " + bad + " / " + list.Count);
            }
        }

        // ---------------------------------------------------------------- D
        static void Grounding(GameObject env, Terrain t)
        {
            Note("");
            Note("=== D. GROUNDING (placed objects vs terrain) ===");
            int ok = 0;
            foreach (Transform grp in env.transform)
            {
                if (grp.name == "Ocean" || grp.name == "WindZone") continue;
                foreach (Transform u in grp)
                {
                    var rs = u.GetComponentsInChildren<Renderer>(true); if (rs.Length == 0) continue;
                    var lod = u.GetComponentInChildren<LODGroup>();
                    IEnumerable<Renderer> use = rs; if (lod != null && lod.GetLODs().Length > 0) use = lod.GetLODs()[0].renderers.Where(r => r);
                    var b = use.First().bounds; foreach (var r in use) b.Encapsulate(r.bounds);
                    // is it standing on another object (lanterns on platforms etc.)?
                    bool onObject = Physics.Raycast(new Vector3(b.center.x, b.min.y + 0.3f, b.center.z), Vector3.down, out var hh, 0.6f) && !(hh.collider is TerrainCollider) && !hh.collider.transform.IsChildOf(u);
                    float gapMin = float.MaxValue, buryMax = float.MinValue;
                    foreach (var p in Corners(b))
                    {
                        float h = t.SampleHeight(p) + t.transform.position.y;
                        gapMin = Mathf.Min(gapMin, b.min.y - h); buryMax = Mathf.Max(buryMax, h - b.min.y);
                    }
                    bool floating = !onObject && gapMin > 0.15f;
                    bool buried = buryMax > Mathf.Max(0.6f, b.size.y * 0.25f) && grp.name != "Korean_Buildings";
                    if (floating || buried) Note(string.Format("  {0}/{1}: {2} (lowest gap {3:0.00} m, deepest burial {4:0.00} m, height {5:0.0} m)",
                        grp.name, u.name, floating ? "FLOATING" : "BURIED", gapMin, buryMax, b.size.y));
                    else ok++;
                }
            }
            Note("  objects OK: " + ok);
        }

        static IEnumerable<Vector3> Corners(Bounds b)
        {
            yield return b.center;
            yield return new Vector3(b.min.x, 0, b.min.z); yield return new Vector3(b.max.x, 0, b.min.z);
            yield return new Vector3(b.min.x, 0, b.max.z); yield return new Vector3(b.max.x, 0, b.max.z);
        }

        // ---------------------------------------------------------------- E
        static void Startup(Terrain t)
        {
            Note("");
            Note("=== E. STARTUP-COST SUSPECTS ===");
            foreach (var p in UnityEngine.Object.FindObjectsOfType<ReflectionProbe>(true))
                Note(string.Format("  ReflectionProbe '{0}' mode={1} refresh={2} timeSlicing={3} res={4} size={5}", TPath(p.transform), p.mode, p.refreshMode, p.timeSlicingMode, p.resolution, p.size.ToString("0")));
            var lights = UnityEngine.Object.FindObjectsOfType<Light>(true);
            Note("  Lights: " + lights.Length + " (" + string.Join(", ", lights.Select(l => l.type + "/" + l.lightmapBakeType + "/shadows " + l.shadows)) + ")");
            var cols = UnityEngine.Object.FindObjectsOfType<Collider>(true);
            var mcs = cols.OfType<MeshCollider>().ToList();
            Note(string.Format("  Colliders total {0}; MeshColliders {1}; unique collision meshes {2}; collision triangles (sum) {3:N0}",
                cols.Length, mcs.Count, mcs.Select(m => m.sharedMesh).Distinct().Count(), mcs.Where(m => m.sharedMesh).Sum(m => (long)m.sharedMesh.triangles.Length / 3)));
            var notReadable = mcs.Where(m => m.sharedMesh && !m.sharedMesh.isReadable).Select(m => m.sharedMesh).Distinct().Count();
            Note("  collision meshes not CPU-readable (runtime cooking needs readable or pre-cooked): " + notReadable);
            var renderers = UnityEngine.Object.FindObjectsOfType<Renderer>(true);
            Note(string.Format("  Renderers {0}; distinct materials {1}; distinct shaders {2}", renderers.Length,
                renderers.SelectMany(r => r.sharedMaterials).Where(m => m).Distinct().Count(), renderers.SelectMany(r => r.sharedMaterials).Where(m => m && m.shader).Select(m => m.shader).Distinct().Count()));
            var td = t.terrainData;
            Note(string.Format("  Terrain: trees {0} ({1} prototypes), treeDistance {2}, billboard {3}, maxFullLOD {4}, detailDistance {5}, detail density {6}, pixelError {7}, drawInstanced {8}",
                td.treeInstanceCount, td.treePrototypes.Length, t.treeDistance, t.treeBillboardDistance, t.treeMaximumFullLODCount, t.detailObjectDistance, t.detailObjectDensity, t.heightmapPixelError, t.drawInstanced));
            var monos = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>(true).GroupBy(m => m.GetType().Name).OrderByDescending(g => g.Count());
            Note("  MonoBehaviours: " + string.Join(", ", monos.Select(g => g.Key + "x" + g.Count())));
            Note("  Shadow distance " + QualitySettings.shadowDistance + " (URP asset overrides) ; vSync " + QualitySettings.vSyncCount);
            var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            if (urp) Note(string.Format("  URP: shadowDistance {0}, cascades {1}, mainLightShadowRes {2}, depthTex {3}, opaqueTex {4}, msaa {5}, renderScale {6}, HDR {7}",
                urp.shadowDistance, urp.shadowCascadeCount, urp.mainLightShadowmapResolution, urp.supportsCameraDepthTexture, urp.supportsCameraOpaqueTexture, urp.msaaSampleCount, urp.renderScale, urp.supportsHDR));
            var svc = AssetDatabase.FindAssets("t:ShaderVariantCollection");
            Note("  ShaderVariantCollections in project: " + svc.Length + "; preloaded shaders: " + new SerializedObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("ProjectSettings/GraphicsSettings.asset")).FindProperty("m_PreloadedShaders").arraySize);
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);
        static string TPath(Transform t) { var s = t.name; var p = t.parent; int d = 0; while (p != null && d < 3) { s = p.name + "/" + s; p = p.parent; d++; } return s; }
    }
}
