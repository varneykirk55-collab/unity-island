// Editor-only. Finds sites for the Korean building prefabs on Scene 1's terrain and places them.
// Terrain heights are never modified; painted terrain trees inside building footprints are removed.
// Batchmode: Unity.exe -batchmode -quit -projectPath <proj> -executeMethod IslandTools.IslandKoreanPlace.Run -surveyOut <dir>
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
    public static class IslandKoreanPlace
    {
        const string Scene1Path = "Assets/Free Island Collection/Scenes/Scene 1.unity";
        const string BFolder = "Assets/_IslandEnvironment/Buildings";
        const float Sea = 45f;
        static Terrain T;
        static string outDir;
        static readonly StringBuilder R = new StringBuilder();
        static void Note(string s) { R.AppendLine(s); Debug.Log("[KoreanPlace] " + s); }

        class Spec { public string prefab; public float maxVar; public float margin; public float w, d; }
        class Site { public Vector2 c; public float yaw, var, min, score; }

        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            int ai = Array.IndexOf(args, "-surveyOut");
            outDir = ai >= 0 && ai + 1 < args.Length ? args[ai + 1] : "_Backups/koreanplace";
            Directory.CreateDirectory(outDir);
            ShaderUtil.allowAsyncCompilation = false;
            try
            {
                var scene = EditorSceneManager.OpenScene(Scene1Path, OpenSceneMode.Single);
                T = UnityEngine.Object.FindObjectOfType<Terrain>();
                var env = GameObject.Find("Island_Environment");
                var old = env.transform.Find("Korean_Buildings");
                if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
                var grp = new GameObject("Korean_Buildings"); grp.transform.SetParent(env.transform, false);

                // keep-out zones: everything placed so far
                var keep = new List<(Vector2 c, float r)>();
                foreach (Transform t in env.transform)
                {
                    if (t.name == "Ocean" || t.name == "WindZone") continue;
                    foreach (Transform ch in t) keep.Add((new Vector2(ch.position.x, ch.position.z), t.name == "Landmarks" ? 45f : 14f));
                }

                // largest first so they get the best land
                var specs = new[]
                {
                    new Spec { prefab = "KR_Geoncheonggung_Palace", maxVar = 4.0f, margin = 10f },
                    new Spec { prefab = "KR_Donggung_Court_03", maxVar = 3.5f, margin = 8f },
                    new Spec { prefab = "KR_Donggung_Court_02", maxVar = 3.5f, margin = 8f },
                    new Spec { prefab = "KR_Donggung_Court_01", maxVar = 3.5f, margin = 8f },
                    new Spec { prefab = "KR_Donggung_Court_04", maxVar = 3.0f, margin = 8f },
                    new Spec { prefab = "KR_Gyedong_House", maxVar = 2.5f, margin = 8f },
                    new Spec { prefab = "KR_Donggung_EastHall", maxVar = 2.5f, margin = 8f },
                };
                var placed = new List<(Vector2 c, float r)>();
                foreach (var s in specs)
                {
                    var path = BFolder + "/" + s.prefab + ".prefab";
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null) { Note("MISSING prefab " + path); continue; }
                    var probe = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    var b = Bnd(probe); s.w = b.size.x; s.d = b.size.z;
                    var localCentre = new Vector2(b.center.x, b.center.z) - new Vector2(probe.transform.position.x, probe.transform.position.z);
                    UnityEngine.Object.DestroyImmediate(probe);

                    var site = FindSite(s, keep, placed);
                    if (site == null) { Note(s.prefab + ": NO SITE within " + s.maxVar + " m height variation"); continue; }
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    go.transform.SetParent(grp.transform, true);
                    go.transform.rotation = Quaternion.Euler(0, site.yaw, 0);
                    var off = Quaternion.Euler(0, site.yaw, 0) * new Vector3(localCentre.x, 0, localCentre.y);
                    go.transform.position = new Vector3(site.c.x - off.x, site.min - 0.15f, site.c.y - off.z);
                    float rad = 0.5f * Mathf.Sqrt(s.w * s.w + s.d * s.d);
                    placed.Add((site.c, rad + 25f));
                    int cleared = ClearTrees(site.c, s.w / 2 + s.margin, s.d / 2 + s.margin, site.yaw);
                    Note(string.Format("{0}: {1:0} x {2:0} m at ({3:0},{4:0}) yaw {5:0}, ground {6:0.0} m ({7:0.0} m above sea), terrain variation under footprint {8:0.0} m (high side sunk up to that much), {9} terrain trees cleared",
                        s.prefab, s.w, s.d, site.c.x, site.c.y, site.yaw, site.min, site.min - Sea, site.var, cleared));
                }

                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new Exception("save failed");
                AssetDatabase.SaveAssets();
                Note("Scene 1 saved.");
                Renders(grp);
            }
            catch (Exception e) { Note("EXCEPTION: " + e); throw; }
            finally { File.WriteAllText(Path.Combine(outDir, "korean_place.txt"), R.ToString()); }
        }

        static Site FindSite(Spec s, List<(Vector2 c, float r)> keep, List<(Vector2 c, float r)> placed)
        {
            var td = T.terrainData; float rad = 0.5f * Mathf.Sqrt(s.w * s.w + s.d * s.d);
            Site best = null;
            for (float x = rad + 20; x < td.size.x - rad - 20; x += 12)
                for (float z = rad + 20; z < td.size.z - rad - 20; z += 12)
                {
                    var c = new Vector2(x, z);
                    if (keep.Any(k => (k.c - c).magnitude < k.r + rad)) continue;
                    if (placed.Any(p => (p.c - c).magnitude < p.r + rad)) continue;
                    foreach (var yaw in new[] { 0f, 30f, 60f, 90f, 120f, 150f })
                    {
                        var q = Quaternion.Euler(0, yaw, 0);
                        float mn = float.MaxValue, mx = float.MinValue; bool wet = false;
                        for (float u = -s.w / 2; u <= s.w / 2 + 0.01f; u += s.w / 8)
                            for (float v = -s.d / 2; v <= s.d / 2 + 0.01f; v += s.d / 8)
                            {
                                var o = q * new Vector3(u, 0, v);
                                float h = H(c.x + o.x, c.y + o.z);
                                if (h < Sea + 1.5f) { wet = true; break; }
                                mn = Mathf.Min(mn, h); mx = Mathf.Max(mx, h);
                                if (mx - mn > s.maxVar) break;
                            }
                        if (wet || mx - mn > s.maxVar) continue;
                        // prefer flat, then mid-elevation (views) over beach level, and spread around the island
                        float spread = placed.Count == 0 ? 0 : placed.Min(p => (p.c - c).magnitude);
                        float score = (mx - mn) * 10f - Mathf.Clamp(mn - Sea, 0, 80) * 0.25f - Mathf.Min(spread, 900) * 0.02f;
                        if (best == null || score < best.score) best = new Site { c = c, yaw = yaw, var = mx - mn, min = mn, score = score };
                    }
                }
            return best;
        }

        static int ClearTrees(Vector2 c, float hw, float hd, float yaw)
        {
            var td = T.terrainData; var inv = Quaternion.Euler(0, -yaw, 0);
            var trees = td.treeInstances; var keepList = new List<TreeInstance>(trees.Length);
            foreach (var t in trees)
            {
                var w = new Vector3(t.position.x * td.size.x, 0, t.position.z * td.size.z) + T.transform.position;
                var l = inv * new Vector3(w.x - c.x, 0, w.z - c.y);
                if (Mathf.Abs(l.x) < hw && Mathf.Abs(l.z) < hd) continue;
                keepList.Add(t);
            }
            int removed = trees.Length - keepList.Count;
            if (removed > 0) { td.SetTreeInstances(keepList.ToArray(), true); EditorUtility.SetDirty(td); }
            return removed;
        }

        static float H(float x, float z) => T.SampleHeight(new Vector3(x, 0, z)) + T.transform.position.y;

        static Bounds Bnd(GameObject g)
        {
            var rs = g.GetComponentsInChildren<Renderer>(true); var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
        }

        static void Renders(GameObject grp)
        {
            foreach (var p in UnityEngine.Object.FindObjectsOfType<ReflectionProbe>()) p.RenderProbe();
            var cam = new GameObject("KCam").AddComponent<Camera>(); cam.allowHDR = false; cam.fieldOfView = 45; cam.farClipPlane = 9000; cam.nearClipPlane = 0.3f;
            var c = new Vector3(1250, 0, 1250);
            cam.transform.position = new Vector3(1250, 2600, 1250); cam.transform.rotation = Quaternion.Euler(90, 0, 0); cam.fieldOfView = 55;
            Shoot(cam, "k_island_top.png", 1400, 1400);
            cam.fieldOfView = 45;
            foreach (Transform b in grp.transform)
            {
                var bb = Bnd(b.gameObject); float r = bb.extents.magnitude;
                cam.transform.position = bb.center + new Vector3(-0.8f, 0.55f, -1f).normalized * r * 1.6f; cam.transform.LookAt(bb.center);
                Shoot(cam, "k_" + b.name + "_wide.png");
                cam.transform.position = bb.center + new Vector3(0.9f, 0.18f, -0.6f).normalized * r * 0.9f; cam.transform.LookAt(bb.center);
                Shoot(cam, "k_" + b.name + "_low.png");
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
