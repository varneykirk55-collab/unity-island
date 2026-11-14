// Editor-only. Phase 1 doorway collision repair for the Korean building prefabs.
//  1) finds real walk-through openings from the VISIBLE geometry (frames/doors/gates >= 0.75 m wide, >= 1.85 m tall)
//  2) swaps "_ConvexHulls" collision meshes of door/frame/gate pieces inside those openings for the visible mesh
//  3) adds invisible step ramps where a raised threshold exceeds the player's step offset (0.25 m)
//  Walls/floors/pillars keep their collision. Changes go into the _IslandEnvironment building prefabs.
// Batchmode: ... -executeMethod IslandTools.IslandDoorFix.Run -surveyOut <dir> [-dryRun]
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace IslandTools
{
    public static class IslandDoorFix
    {
        const string Scene1Path = "Assets/Free Island Collection/Scenes/Scene 1.unity";
        const float PR = IslandInspect.PR, PH = IslandInspect.PH, PStep = IslandInspect.PStep, Skin = IslandInspect.PSkin;
        const int VisLayer = 30;
        static readonly Regex OpeningPiece = new Regex("Door|Frame|Gate|Entrance|mun", RegexOptions.IgnoreCase);
        static readonly StringBuilder R = new StringBuilder();
        static void Note(string s) { R.AppendLine(s); Debug.Log("[DoorFix] " + s); }

        class Opening { public string building; public Transform piece; public Vector3 c, through, across; public float width, height, sillTop, floorA, floorB; public string before, afterSwap, afterRamp; public int swapped; public int ramps; }

        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            int ai = Array.IndexOf(args, "-surveyOut");
            var outDir = ai >= 0 && ai + 1 < args.Length ? args[ai + 1] : "_Backups/doorfix";
            bool dry = args.Contains("-dryRun");
            Directory.CreateDirectory(outDir);
            try
            {
                var scene = EditorSceneManager.OpenScene(Scene1Path, OpenSceneMode.Single);
                var korean = GameObject.Find("Island_Environment/Korean_Buildings").transform;
                var proxies = BuildProxies(korean);
                var openings = FindOpenings(korean);
                Note((dry ? "DRY RUN. " : "") + "real walk-through openings found: " + openings.Count);

                foreach (var o in openings) o.before = Test(o);

                // fix 1: visible-mesh collision for opening pieces that intrude into the passage
                foreach (var o in openings)
                {
                    foreach (var col in PassageColliders(o))
                    {
                        if (!(col is MeshCollider mc) || mc.sharedMesh == null || !mc.sharedMesh.name.EndsWith("_ConvexHulls")) continue;
                        if (!OpeningPiece.IsMatch(col.name)) continue;               // walls/floors/pillars keep hull collision
                        var mf = col.GetComponentInChildren<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
                        if (mf.transform != col.transform)
                        {   // collider on piece root, mesh on child: put the visible-mesh collider where the mesh is
                            var vis = mf.gameObject.GetComponent<MeshCollider>();
                            if (vis == null) vis = mf.gameObject.AddComponent<MeshCollider>();
                            vis.sharedMesh = mf.sharedMesh; UnityEngine.Object.DestroyImmediate(mc);
                        }
                        else mc.sharedMesh = mf.sharedMesh;
                        o.swapped++;
                    }
                }
                Physics.SyncTransforms();
                foreach (var o in openings) o.afterSwap = Test(o);

                // fix 2: invisible ramps over thresholds higher than the step offset
                foreach (var o in openings.Where(x => x.afterSwap == "sill"))
                {
                    foreach (var side in new[] { -1f, 1f })
                    {
                        float floor = side < 0 ? o.floorA : o.floorB;
                        float rise = o.sillTop - floor;
                        if (rise <= PStep - 0.02f) continue;
                        AddRamp(o, side, rise);
                        o.ramps++;
                    }
                }
                Physics.SyncTransforms();
                foreach (var o in openings) o.afterRamp = Test(o);

                foreach (var g in openings.GroupBy(x => x.building))
                {
                    Note(string.Format("[{0}] openings {1}: before clear {2}; after collider swap clear {3}; after ramps clear {4}; hull colliders swapped {5}; ramps {6}",
                        g.Key, g.Count(), g.Count(x => x.before == "clear"), g.Count(x => x.afterSwap == "clear"), g.Count(x => x.afterRamp == "clear"), g.Sum(x => x.swapped), g.Sum(x => x.ramps)));
                    foreach (var o in g)
                        Note(string.Format("   {0} ({1:0.0},{2:0.0},{3:0.0}) w {4:0.00} h {5:0.00} sill +{6:0.00}/+{7:0.00}  before={8} swap={9} ramp={10}",
                            o.piece.name, o.c.x, o.c.y, o.c.z, o.width, o.height, o.sillTop - o.floorA, o.sillTop - o.floorB, o.before, o.afterSwap, o.afterRamp));
                }
                File.WriteAllLines(Path.Combine(outDir, "doorways.csv"), new[] { "building,piece,x,y,z,throughX,throughZ,width,height,floorA,floorB,sillTop,result" }
                    .Concat(openings.Select(o => string.Join(",", o.building, o.piece.name, F(o.c.x), F(o.c.y), F(o.c.z), F(o.through.x), F(o.through.z), F(o.width), F(o.height), F(o.floorA), F(o.floorB), F(o.sillTop), o.afterRamp))));

                foreach (var p in proxies) UnityEngine.Object.DestroyImmediate(p);
                if (!dry)
                {
                    foreach (Transform b in korean)
                        if (PrefabUtility.IsAnyPrefabInstanceRoot(b.gameObject)) PrefabUtility.ApplyPrefabInstance(b.gameObject, InteractionMode.AutomatedAction);
                    AssetDatabase.SaveAssets();
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                    Note("Applied to building prefabs and saved.");
                }
            }
            catch (Exception e) { Note("EXCEPTION: " + e); throw; }
            finally
            {
                File.WriteAllText(Path.Combine(outDir, dry ? "doorfix_dryrun.txt" : "doorfix.txt"), R.ToString());
                if (dry) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        static string F(float v) => v.ToString("0.000");

        static List<GameObject> BuildProxies(Transform root)
        {
            var list = new List<GameObject>();
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var r = mf.GetComponent<Renderer>(); if (mf.sharedMesh == null || r == null || !r.enabled) continue;
                var p = new GameObject("vis") { layer = VisLayer, hideFlags = HideFlags.DontSave };
                p.transform.SetPositionAndRotation(mf.transform.position, mf.transform.rotation); p.transform.localScale = mf.transform.lossyScale;
                p.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh; list.Add(p);
            }
            Physics.SyncTransforms();
            return list;
        }

        static List<Opening> FindOpenings(Transform korean)
        {
            int vis = 1 << VisLayer;
            var found = new List<Opening>();
            foreach (Transform b in korean)
                foreach (var mf in b.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null || !OpeningPiece.IsMatch(mf.name + " " + mf.transform.parent?.name)) continue;
                    var t = mf.transform; var mb = mf.sharedMesh.bounds; var size = Vector3.Scale(mb.size, Abs(t.lossyScale));
                    if (size.y < 1.8f) continue;
                    Vector3 thin = size.x < size.z ? Vector3.right : Vector3.forward, wide = size.x < size.z ? Vector3.forward : Vector3.right;
                    var through = Flat(t.rotation * thin); var across = Flat(t.rotation * wide);
                    var cw = t.TransformPoint(mb.center); float bottom = t.TransformPoint(mb.center - Vector3.up * mb.extents.y).y;
                    // try the piece centre and offsets along its width (frames with off-centre openings)
                    foreach (var off in new[] { 0f, -0.25f, 0.25f, -0.5f, 0.5f })
                    {
                        var c = new Vector3(cw.x, bottom, cw.z) + across * (off * Mathf.Max(size.x, size.z));
                        bool open = new[] { 0.6f, 1.0f, 1.5f, 1.8f }.All(h => !Physics.Raycast(c + Vector3.up * h - through * 1.5f, through, 3f, vis));
                        if (!open) continue;
                        float l = Dist(c + Vector3.up * 1.0f, -across, 3f, vis), r = Dist(c + Vector3.up * 1.0f, across, 3f, vis);
                        c += across * ((r - l) * 0.5f);   // centre in the opening
                        float w = l + r;
                        if (w < 0.75f || w > 6f) continue;
                        float sill = Physics.Raycast(c + Vector3.up * 1.5f, Vector3.down, out var sh, 4f, vis) ? sh.point.y : bottom;
                        float top = Physics.Raycast(new Vector3(c.x, sill + 0.1f, c.z), Vector3.up, out var th, 5f, vis) ? th.point.y : sill + 5f;
                        if (top - sill < 1.85f) continue;
                        float fa = Ground(c - through * 1.2f, sill), fb = Ground(c + through * 1.2f, sill);
                        if (found.Any(f => (f.c - c).sqrMagnitude < 1.0f)) break;
                        found.Add(new Opening { building = b.name, piece = t, c = new Vector3(c.x, sill, c.z), through = through, across = across, width = w, height = top - sill, sillTop = sill, floorA = fa, floorB = fb });
                        break;
                    }
                }
            return found;
        }

        // walkable surface height near a point (any collider incl. terrain), searching down from just above the sill
        static float Ground(Vector3 p, float sill)
        {
            var from = new Vector3(p.x, sill + 0.6f, p.z);
            return Physics.Raycast(from, Vector3.down, out var h, 4f, ~(1 << VisLayer), QueryTriggerInteraction.Ignore) ? h.point.y : sill - 1f;
        }

        static IEnumerable<Collider> PassageColliders(Opening o)
        {
            var rot = Quaternion.LookRotation(o.through, Vector3.up);
            var half = new Vector3(Mathf.Max(0.2f, o.width * 0.5f - 0.05f), 0.8f, 1.2f);
            return Physics.OverlapBox(new Vector3(o.c.x, o.sillTop + 0.15f + 0.8f, o.c.z), half, rot, ~(1 << VisLayer), QueryTriggerInteraction.Ignore)
                .Where(c => !(c is TerrainCollider));
        }

        // "clear" | "sill" (only the step is too high) | "blocked:<collider>"
        static string Test(Opening o)
        {
            int mask = ~(1 << VisLayer);
            // 1) passage itself: capsule lifted onto the sill top, swept through
            var p1 = new Vector3(o.c.x, o.sillTop + 0.03f + PR + Skin, o.c.z) - o.through * 1.0f;
            var p2 = p1 + Vector3.up * (PH - 2 * PR);
            if (Physics.CapsuleCast(p1, p2, PR + Skin, o.through, out var hit, 2.0f, mask, QueryTriggerInteraction.Ignore))
                return "blocked:" + hit.collider.name + "@" + (hit.point.y - o.sillTop).ToString("0.00");
            // 2) can the player get onto the sill from both sides? (ramps count as walkable)
            float riseA = SurfaceRise(o, -1f), riseB = SurfaceRise(o, 1f);
            return (riseA > PStep || riseB > PStep) ? "sill" : "clear";
        }

        // worst single vertical step a walker meets approaching the sill from one side (samples every 0.1 m)
        static float SurfaceRise(Opening o, float side)
        {
            float prev = float.NaN, worst = 0;
            for (float d = 1.2f; d >= 0f; d -= 0.1f)
            {
                var p = new Vector3(o.c.x, 0, o.c.z) + o.through * (side * d);
                float h = Physics.Raycast(new Vector3(p.x, o.sillTop + 0.6f, p.z), Vector3.down, out var hh, 4f, ~(1 << VisLayer), QueryTriggerInteraction.Ignore) ? hh.point.y : o.sillTop - 1f;
                if (!float.IsNaN(prev)) worst = Mathf.Max(worst, h - prev);
                prev = h;
            }
            return worst;
        }

        static void AddRamp(Opening o, float side, float rise)
        {
            float run = rise / Mathf.Tan(28f * Mathf.Deg2Rad);
            var dir = o.through * side;
            var start = new Vector3(o.c.x, 0, o.c.z) + dir * (0.12f + run);           // on the floor
            var end = new Vector3(o.c.x, 0, o.c.z) + dir * 0.12f;                     // at the sill
            var a = new Vector3(start.x, o.sillTop - rise, start.z); var b = new Vector3(end.x, o.sillTop, end.z);
            var go = new GameObject(side < 0 ? "Fix_SillRamp_A" : "Fix_SillRamp_B");
            go.transform.SetParent(o.piece.parent != null ? o.piece.parent : o.piece, true);
            go.transform.position = (a + b) * 0.5f - Vector3.up * 0.05f;
            go.transform.rotation = Quaternion.LookRotation((b - a).normalized, Vector3.Cross((b - a).normalized, Vector3.Cross(Vector3.up, (b - a).normalized)).normalized * -1f);
            go.transform.rotation = Quaternion.LookRotation((b - a).normalized, Vector3.up) ;
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(Mathf.Max(0.6f, o.width - 0.1f), 0.1f, (b - a).magnitude + 0.15f);
        }

        static float Dist(Vector3 from, Vector3 dir, float max, int mask) => Physics.Raycast(from, dir, out var h, max, mask) ? h.distance : max;
        static Vector3 Flat(Vector3 v) { v.y = 0; return v.normalized; }
        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
    }
}
