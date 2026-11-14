// Editor-only, READ-ONLY. Phase 2/4 diagnostics: renders of failing openings + stray pieces left by the Donggung split
// + base-level pieces of buildings that float above or are swallowed by terrain. Scene never saved.
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
    public static class IslandAlignDiag
    {
        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            int ai = Array.IndexOf(args, "-surveyOut");
            var outDir = ai >= 0 && ai + 1 < args.Length ? args[ai + 1] : "_Backups/aligndiag";
            Directory.CreateDirectory(outDir);
            ShaderUtil.allowAsyncCompilation = false;
            var sb = new StringBuilder();
            EditorSceneManager.OpenScene("Assets/Free Island Collection/Scenes/Scene 1.unity", OpenSceneMode.Single);
            var t = UnityEngine.Object.FindObjectOfType<Terrain>();
            var korean = GameObject.Find("Island_Environment/Korean_Buildings").transform;
            var cam = new GameObject("Cam").AddComponent<Camera>(); cam.allowHDR = false; cam.fieldOfView = 60; cam.nearClipPlane = 0.05f; cam.farClipPlane = 800;

            // 1) failing openings from the doorway test
            var fails = new (string label, Vector3 c, Vector3 thr)[]
            {
                ("gcg_gate6", new Vector3(1185.4f,55.3f,2310.1f), Vector3.zero),
                ("gcg_gate9", new Vector3(1213.0f,53.8f,2284.7f), Vector3.zero),
            };
            var csv = File.ReadAllLines("_Backups/doorfix_last/doorways.csv").Skip(1).Select(l => l.Split(',')).ToList();
            var targets = new List<(string, Vector3, Vector3)>();
            foreach (var c in csv)
            {
                var p = new Vector3(float.Parse(c[2]), float.Parse(c[3]), float.Parse(c[4])); var d = new Vector3(float.Parse(c[5]), 0, float.Parse(c[6]));
                if ((p - fails[0].c).sqrMagnitude < 4 || (p - fails[1].c).sqrMagnitude < 4 || c[1].Contains("DoorRoofM") && c[0].Contains("Court_02") || c[1].Contains("WindowFrame01H") || c[1].Contains("Gyedong"))
                    targets.Add((c[0] + "_" + c[1], p, d));
            }
            int i = 0;
            foreach (var (label, p, d) in targets)
            {
                sb.AppendLine("opening " + i + ": " + label + " at " + p.ToString("0.0") + " through " + d.ToString("0.00"));
                foreach (var s in new[] { 1f, -1f })
                {
                    cam.transform.position = p - d * s * 7f + Vector3.up * 2.5f; cam.transform.LookAt(p + Vector3.up * 1.0f);
                    Shoot(cam, Path.Combine(outDir, "open_" + i + (s > 0 ? "_A" : "_B") + ".png"));
                }
                // what's under the approach on each side (stairs? drop?)
                foreach (var s in new[] { 1f, -1f })
                {
                    var prof = new List<string>();
                    for (float dist = 0.5f; dist <= 6f; dist += 0.5f)
                    {
                        var q = p - d * s * dist;
                        if (Physics.Raycast(new Vector3(q.x, p.y + 1.5f, q.z), Vector3.down, out var h, 8f, ~0, QueryTriggerInteraction.Ignore))
                            prof.Add(string.Format("{0:0.0}m:{1:+0.00;-0.00}({2})", dist, h.point.y - p.y, h.collider is TerrainCollider ? "terrain" : h.collider.name.Substring(0, Math.Min(18, h.collider.name.Length))));
                    }
                    sb.AppendLine("   side " + (s > 0 ? "A" : "B") + " ground profile: " + string.Join(" ", prof));
                }
                i++;
            }

            // 2) stray pieces in Donggung courts (outside the trimmed core footprint)
            sb.AppendLine();
            foreach (Transform b in korean)
            {
                if (!b.name.StartsWith("KR_Donggung_Court")) continue;
                var pieces = b.GetComponentsInChildren<Renderer>(true).Select(r => r.transform).Select(x => Piece(x, b)).Distinct().ToList();
                var inv = Quaternion.Inverse(b.rotation);
                var local = pieces.Select(p => (p, l: inv * (PB(p).center - b.position))).ToList();
                float Pct(IEnumerable<float> v, float q) { var s = v.OrderBy(x => x).ToList(); return s[Mathf.Clamp((int)(q * (s.Count - 1)), 0, s.Count - 1)]; }
                float x0 = Pct(local.Select(l => l.l.x), 0.03f), x1 = Pct(local.Select(l => l.l.x), 0.97f);
                float z0 = Pct(local.Select(l => l.l.z), 0.03f), z1 = Pct(local.Select(l => l.l.z), 0.97f);
                var stray = local.Where(l => l.l.x < x0 - 3f || l.l.x > x1 + 3f || l.l.z < z0 - 3f || l.l.z > z1 + 3f).ToList();
                sb.AppendLine(string.Format("{0}: {1} pieces, core {2:0}x{3:0} m, outside core by >3 m: {4}", b.name, pieces.Count, x1 - x0, z1 - z0, stray.Count));
                foreach (var g in stray.GroupBy(s => System.Text.RegularExpressions.Regex.Replace(s.p.name, @"\d+$", "")).OrderByDescending(g => g.Count()))
                    sb.AppendLine("     " + g.Key + " x" + g.Count() + "  e.g. local " + g.First().l.ToString("0"));
                if (stray.Count > 0)
                {
                    var sbnd = PB(stray[0].p); foreach (var s in stray) sbnd.Encapsulate(PB(s.p));
                    cam.transform.position = sbnd.center + new Vector3(-12, 14, -12); cam.transform.LookAt(sbnd.center);
                    Shoot(cam, Path.Combine(outDir, "stray_" + b.name + ".png"));
                }
            }

            // 3) base-level pieces vs terrain
            sb.AppendLine();
            foreach (Transform b in korean)
            {
                var pieces = b.GetComponentsInChildren<Renderer>(true).Select(r => Piece(r.transform, b)).Distinct().ToList();
                float baseY = b.position.y; int floating = 0, swallowed = 0; var ex = new List<string>();
                foreach (var p in pieces)
                {
                    var pb = PB(p); if (pb.min.y > baseY + 0.6f) continue; // only base-level pieces
                    float h = t.SampleHeight(pb.center) + t.transform.position.y;
                    bool supported = Physics.Raycast(new Vector3(pb.center.x, pb.min.y + 0.05f, pb.center.z), Vector3.down, 0.35f);
                    if (pb.min.y - h > 0.25f && !supported) { floating++; if (ex.Count < 6) ex.Add("float " + p.name + " gap " + (pb.min.y - h).ToString("0.00")); }
                    if (h - pb.max.y > -0.05f) { swallowed++; if (ex.Count < 6) ex.Add("swallowed " + p.name + " by " + (h - pb.max.y).ToString("0.00")); }
                }
                sb.AppendLine(string.Format("{0}: base-level pieces floating {1}, fully under terrain {2}  {3}", b.name, floating, swallowed, string.Join("; ", ex)));
            }
            File.WriteAllText(Path.Combine(outDir, "aligndiag.txt"), sb.ToString());
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        static Transform Piece(Transform x, Transform building)
        {   // piece = direct child of the building's group root (e.g. GCG/<piece>) or of the building itself
            while (x.parent != null && x.parent != building && x.parent.parent != building) x = x.parent;
            return x;
        }
        static Bounds PB(Transform p) { var rs = p.GetComponentsInChildren<Renderer>(true); var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b; }

        static void Shoot(Camera cam, string file, int w = 960, int h = 540)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            cam.targetTexture = rt; cam.Render(); cam.Render();
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = prev; cam.targetTexture = null;
            File.WriteAllBytes(file, tex.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(tex); rt.Release();
        }
    }
}
