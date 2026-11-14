// Editor-only. Flattens a terrain pad under each building in Island_Environment/Korean_Buildings.
// The pad is the building's footprint (+ margin) set to the building's base height, blended smoothly
// back into the original slope over a falloff band. Buildings themselves are not moved.
// Terrain trees are re-snapped to the new heights; trees inside the pads were already cleared.
// Batchmode: Unity.exe -batchmode -quit -projectPath <proj> -executeMethod IslandTools.IslandPads.Run -surveyOut <dir>
using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace IslandTools
{
    public static class IslandPads
    {
        const string Scene1Path = "Assets/Free Island Collection/Scenes/Scene 1.unity";
        const float Margin = 3f;     // flat apron around the footprint
        const float Blend = 14f;     // falloff back to the original terrain
        static string outDir;
        static readonly StringBuilder R = new StringBuilder();
        static void Note(string s) { R.AppendLine(s); Debug.Log("[IslandPads] " + s); }

        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            int ai = Array.IndexOf(args, "-surveyOut");
            outDir = ai >= 0 && ai + 1 < args.Length ? args[ai + 1] : "_Backups/pads";
            Directory.CreateDirectory(outDir);
            ShaderUtil.allowAsyncCompilation = false;
            try
            {
                var scene = EditorSceneManager.OpenScene(Scene1Path, OpenSceneMode.Single);
                var t = UnityEngine.Object.FindObjectOfType<Terrain>();
                var td = t.terrainData; var tp = t.transform.position;
                int res = td.heightmapResolution;
                var grp = GameObject.Find("Island_Environment/Korean_Buildings");
                if (grp == null) throw new Exception("Korean_Buildings not found");

                foreach (Transform b in grp.transform)
                {
                    // footprint in the building's own frame (rotation-aware)
                    var inv = Quaternion.Inverse(b.rotation);
                    Vector2 mn = new Vector2(float.MaxValue, float.MaxValue), mx = new Vector2(float.MinValue, float.MinValue);
                    foreach (var r in b.GetComponentsInChildren<Renderer>(true))
                    {
                        var bb = r.bounds;
                        for (int i = 0; i < 4; i++)
                        {
                            var corner = new Vector3((i & 1) == 0 ? bb.min.x : bb.max.x, 0, (i & 2) == 0 ? bb.min.z : bb.max.z);
                            var l = inv * (corner - b.position);
                            mn = Vector2.Min(mn, new Vector2(l.x, l.z)); mx = Vector2.Max(mx, new Vector2(l.x, l.z));
                        }
                    }
                    var centreL = (mn + mx) * 0.5f; var half = (mx - mn) * 0.5f + Vector2.one * Margin;
                    float target = b.position.y + 0.10f; // just under the building's base level
                    float reach = half.magnitude + Blend;

                    // heightmap window covering the pad + blend
                    var cw = b.position + b.rotation * new Vector3(centreL.x, 0, centreL.y);
                    int x0 = Mathf.Clamp(Mathf.FloorToInt((cw.x - reach - tp.x) / td.size.x * (res - 1)), 0, res - 1);
                    int z0 = Mathf.Clamp(Mathf.FloorToInt((cw.z - reach - tp.z) / td.size.z * (res - 1)), 0, res - 1);
                    int x1 = Mathf.Clamp(Mathf.CeilToInt((cw.x + reach - tp.x) / td.size.x * (res - 1)), 0, res - 1);
                    int z1 = Mathf.Clamp(Mathf.CeilToInt((cw.z + reach - tp.z) / td.size.z * (res - 1)), 0, res - 1);
                    int w = x1 - x0 + 1, h = z1 - z0 + 1;
                    var hs = td.GetHeights(x0, z0, w, h);
                    float tn = (target - tp.y) / td.size.y;
                    float maxCut = 0, maxFill = 0; int changed = 0;
                    for (int z = 0; z < h; z++)
                        for (int x = 0; x < w; x++)
                        {
                            var world = new Vector3(tp.x + (x0 + x) / (float)(res - 1) * td.size.x, 0, tp.z + (z0 + z) / (float)(res - 1) * td.size.z);
                            var l = inv * (world - b.position);
                            float dx = Mathf.Max(Mathf.Abs(l.x - centreL.x) - half.x, 0), dz = Mathf.Max(Mathf.Abs(l.z - centreL.y) - half.y, 0);
                            float d = Mathf.Sqrt(dx * dx + dz * dz);
                            if (d >= Blend) continue;
                            float k = d <= 0 ? 1f : 1f - Mathf.SmoothStep(0f, 1f, d / Blend);
                            float orig = hs[z, x], nv = Mathf.Lerp(orig, tn, k);
                            float delta = (nv - orig) * td.size.y;
                            if (delta < 0) maxCut = Mathf.Max(maxCut, -delta); else maxFill = Mathf.Max(maxFill, delta);
                            hs[z, x] = nv; changed++;
                        }
                    td.SetHeights(x0, z0, hs);
                    Note(string.Format("{0}: pad {1:0} x {2:0} m at {3:0.0} m (+{4} m blend); {5} height samples changed; max cut {6:0.0} m, max fill {7:0.0} m",
                        b.name, half.x * 2, half.y * 2, target, Blend, changed, maxCut, maxFill));
                }

                // re-snap terrain trees to the modified surface
                td.SetTreeInstances(td.treeInstances, true);
                EditorUtility.SetDirty(td);
                AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Note("Terrain saved; trees re-snapped.");
                Renders(grp);
            }
            catch (Exception e) { Note("EXCEPTION: " + e); throw; }
            finally { File.WriteAllText(Path.Combine(outDir, "pads.txt"), R.ToString()); }
        }

        static void Renders(GameObject grp)
        {
            foreach (var p in UnityEngine.Object.FindObjectsOfType<ReflectionProbe>()) p.RenderProbe();
            var cam = new GameObject("PCam").AddComponent<Camera>(); cam.allowHDR = false; cam.fieldOfView = 45; cam.farClipPlane = 9000; cam.nearClipPlane = 0.3f;
            foreach (Transform b in grp.transform)
            {
                var rs = b.GetComponentsInChildren<Renderer>(true); var bb = rs[0].bounds; foreach (var r in rs) bb.Encapsulate(r.bounds);
                float rad = bb.extents.magnitude;
                cam.transform.position = bb.center + new Vector3(-0.8f, 0.55f, -1f).normalized * rad * 1.6f; cam.transform.LookAt(bb.center);
                Shoot(cam, "pad_" + b.name + "_wide.png");
                cam.transform.position = bb.center + new Vector3(0.9f, 0.16f, -0.6f).normalized * rad * 1.1f; cam.transform.LookAt(bb.center);
                Shoot(cam, "pad_" + b.name + "_low.png");
            }
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
