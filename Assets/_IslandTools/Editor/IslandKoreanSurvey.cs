// Editor-only. (a) Converts House of Gyedong materials (built-in Standard) to URP Lit with Unity's upgrader.
// (b) READ-ONLY survey of the Korean demo scenes: hierarchy, bounds, renders. Demo scenes are never saved.
// Batchmode: Unity.exe -batchmode -quit -projectPath <proj> -executeMethod IslandTools.IslandKoreanSurvey.Run -surveyOut <dir>
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace IslandTools
{
    public static class IslandKoreanSurvey
    {
        static string outDir;
        static readonly StringBuilder R = new StringBuilder();
        static void Note(string s) { R.AppendLine(s); Debug.Log("[KoreanSurvey] " + s); }

        static readonly string[] DemoScenes =
        {
            "Assets/DongGungComplex/Demo_DongGung.unity",
            "Assets/GeoncheonggungComplex/Scene/Demo.unity",
            "Assets/Bugchonmunhwa/Scenes/Demo.unity",
            "Assets/Bugchonmunhwa/Scenes/Overview.unity",
        };

        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            int ai = Array.IndexOf(args, "-surveyOut");
            outDir = ai >= 0 && ai + 1 < args.Length ? args[ai + 1] : "_Backups/korean";
            Directory.CreateDirectory(outDir);
            ShaderUtil.allowAsyncCompilation = false;
            try
            {
                ConvertGyedong();
                AssetDatabase.SaveAssets();
                foreach (var s in DemoScenes) SurveyScene(s);
            }
            catch (Exception e) { Note("EXCEPTION: " + e); throw; }
            finally
            {
                File.WriteAllText(Path.Combine(outDir, "korean_survey.txt"), R.ToString());
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        static void ConvertGyedong()
        {
            Note("=== Gyedong material conversion ===");
            var t = Type.GetType("UnityEditor.Rendering.Universal.UniversalRenderPipelineMaterialUpgrader, Unity.RenderPipelines.Universal.Editor");
            var m = t.GetMethod("GetUpgraders", BindingFlags.NonPublic | BindingFlags.Static);
            var a = new object[] { new List<MaterialUpgrader>() }; m.Invoke(null, a);
            var ups = (List<MaterialUpgrader>)a[0];
            var counts = new Dictionary<string, int>();
            foreach (var g in AssetDatabase.FindAssets("t:Material", new[] { "Assets/Bugchonmunhwa", "Assets/Bitgem" }))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(p);
                if (mat == null || mat.shader == null || !AssetDatabase.IsMainAsset(mat)) continue;
                var before = mat.shader.name;
                if (before.StartsWith("Universal Render Pipeline") || before.StartsWith("Shader Graphs") || before.StartsWith("Skybox")) continue;
                string msg = "";
                bool ok = MaterialUpgrader.Upgrade(mat, ups, MaterialUpgrader.UpgradeFlags.None, ref msg);
                var k = ok && mat.shader.name != before ? before + " -> " + mat.shader.name : "NOT CONVERTED " + before + " (" + p + ")";
                counts.TryGetValue(k, out var c); counts[k] = c + 1;
                if (ok) EditorUtility.SetDirty(mat);
            }
            foreach (var kv in counts) Note("  " + kv.Value + " x " + kv.Key);
        }

        static void SurveyScene(string path)
        {
            Note("");
            Note("=== " + path + " ===");
            if (!File.Exists(path)) { Note("  missing"); return; }
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var roots = scene.GetRootGameObjects();
            var all = new List<Renderer>();
            foreach (var r in roots)
            {
                var rs = r.GetComponentsInChildren<Renderer>(true);
                all.AddRange(rs);
                var b = B(rs);
                Note(string.Format("ROOT '{0}' active={1} children={2} renderers={3} prefabInstance={4} size {5:0.0} x {6:0.0} x {7:0.0} centre ({8:0.0},{9:0.0},{10:0.0})  comps: {11}",
                    r.name, r.activeSelf, r.transform.childCount, rs.Length, PrefabUtility.IsAnyPrefabInstanceRoot(r),
                    b.size.x, b.size.y, b.size.z, b.center.x, b.center.y, b.center.z,
                    string.Join(",", r.GetComponents<Component>().Select(c => c ? c.GetType().Name : "MISSING").Where(n => n != "Transform"))));
                // second level: only meaningful groups
                foreach (Transform ch in r.transform)
                {
                    var crs = ch.GetComponentsInChildren<Renderer>(true);
                    if (crs.Length < 8 && ch.childCount < 3) continue;
                    var cb = B(crs);
                    Note(string.Format("   - '{0}' children={1} renderers={2} size {3:0.0} x {4:0.0} x {5:0.0} centre ({6:0.0},{7:0.0},{8:0.0})",
                        ch.name, ch.childCount, crs.Length, cb.size.x, cb.size.y, cb.size.z, cb.center.x, cb.center.y, cb.center.z));
                    foreach (Transform gch in ch)
                    {
                        var grs = gch.GetComponentsInChildren<Renderer>(true);
                        if (grs.Length < 20) continue;
                        var gb = B(grs);
                        Note(string.Format("        . '{0}' children={1} renderers={2} size {3:0.0} x {4:0.0} x {5:0.0} centre ({6:0.0},{7:0.0},{8:0.0})",
                            gch.name, gch.childCount, grs.Length, gb.size.x, gb.size.y, gb.size.z, gb.center.x, gb.center.y, gb.center.z));
                    }
                }
            }
            var shaders = all.SelectMany(r => r.sharedMaterials).Where(m => m).Select(m => m.shader ? m.shader.name : "null").Distinct().ToList();
            var bad = all.SelectMany(r => r.sharedMaterials).Where(m => m && m.shader && (m.shader.name == "Hidden/InternalErrorShader" || !m.shader.isSupported)).Select(m => m.name).Distinct().ToList();
            Note(string.Format("  total renderers {0}, distinct shaders {1}, unsupported/error shaders: {2}", all.Count, shaders.Count, bad.Count == 0 ? "none" : string.Join(", ", bad.Take(10))));
            int missingMesh = all.OfType<MeshRenderer>().Count(r => { var f = r.GetComponent<MeshFilter>(); return f == null || f.sharedMesh == null; });
            int missingMat = all.Count(r => r.sharedMaterials.Any(m => m == null));
            Note("  renderers with missing mesh: " + missingMesh + ", with missing material: " + missingMat);

            // renders
            var ab = B(all.Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray());
            var cam = new GameObject("SurveyCam").AddComponent<Camera>();
            cam.allowHDR = false; cam.farClipPlane = 20000; cam.nearClipPlane = 0.3f;
            var tag = Path.GetFileNameWithoutExtension(Path.GetDirectoryName(path).Replace("/Scenes", "").Replace("/Scene", "")) + "_" + Path.GetFileNameWithoutExtension(path);
            cam.orthographic = true; cam.orthographicSize = Mathf.Max(ab.extents.x, ab.extents.z) * 1.05f;
            cam.transform.position = ab.center + Vector3.up * (ab.extents.y + 200); cam.transform.rotation = Quaternion.Euler(90, 0, 0);
            Shoot(cam, tag + "_top.png", 1400, 1400);
            cam.orthographic = false; cam.fieldOfView = 40;
            float r0 = ab.extents.magnitude;
            cam.transform.position = ab.center + new Vector3(-0.7f, 0.55f, -1f).normalized * r0 * 1.9f; cam.transform.LookAt(ab.center);
            Shoot(cam, tag + "_oblique.png");
            UnityEngine.Object.DestroyImmediate(cam.gameObject);
        }

        static Bounds B(IEnumerable<Renderer> rs)
        {
            var list = rs.Where(r => r != null && !(r is ParticleSystemRenderer)).ToList();
            if (list.Count == 0) return new Bounds();
            var b = list[0].bounds; foreach (var r in list) b.Encapsulate(r.bounds); return b;
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
