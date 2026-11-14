// Editor-only. Builds placeable building prefabs from the Korean heritage demo scenes.
// Demo scenes are opened read-only and never saved; prefabs keep links to the original piece prefabs.
// Batchmode: Unity.exe -batchmode -quit -projectPath <proj> -executeMethod IslandTools.IslandKoreanPrefabs.Run -surveyOut <dir>
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
    public static class IslandKoreanPrefabs
    {
        const string OutFolder = "Assets/_IslandEnvironment/Buildings";
        static string outDir;
        static readonly StringBuilder R = new StringBuilder();
        static void Note(string s) { R.AppendLine(s); Debug.Log("[KoreanPrefabs] " + s); }

        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            int ai = Array.IndexOf(args, "-surveyOut");
            outDir = ai >= 0 && ai + 1 < args.Length ? args[ai + 1] : "_Backups/koreanprefabs";
            Directory.CreateDirectory(outDir);
            ShaderUtil.allowAsyncCompilation = false;
            if (!AssetDatabase.IsValidFolder(OutFolder)) AssetDatabase.CreateFolder("Assets/_IslandEnvironment", "Buildings");
            var made = new List<string>();
            try
            {
                made.Add(FromRoot("Assets/GeoncheonggungComplex/Scene/Demo.unity", "GCG", "KR_Geoncheonggung_Palace"));
                made.Add(FromRoot("Assets/Bugchonmunhwa/Scenes/Demo.unity", "Model", "KR_Gyedong_House"));
                made.AddRange(DonggungClusters());
                AssetDatabase.SaveAssets();
                RenderPrefabs(made.Where(m => m != null).ToList());
            }
            catch (Exception e) { Note("EXCEPTION: " + e); throw; }
            finally
            {
                File.WriteAllText(Path.Combine(outDir, "korean_prefabs.txt"), R.ToString());
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        // Wrap a demo-scene root in a pivot object at footprint centre / ground level and save as prefab.
        static string FromRoot(string scenePath, string rootName, string prefabName)
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var root = scene.GetRootGameObjects().First(g => g.name == rootName);
            return SaveGroup(new[] { root.transform }, prefabName);
        }

        static string SaveGroup(IList<Transform> members, string prefabName)
        {
            var rs = members.SelectMany(m => m.GetComponentsInChildren<Renderer>(true)).Where(r => r && !(r is ParticleSystemRenderer)).ToList();
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            // ground level = 2nd-percentile of renderer bottoms (ignores a few sunken foundation stones)
            var bottoms = rs.Select(r => r.bounds.min.y).OrderBy(v => v).ToList();
            float ground = bottoms[Mathf.Clamp((int)(bottoms.Count * 0.02f), 0, bottoms.Count - 1)];
            var pivot = new GameObject(prefabName);
            pivot.transform.position = new Vector3(b.center.x, ground, b.center.z);
            foreach (var m in members) m.SetParent(pivot.transform, true);
            pivot.transform.position = Vector3.zero;
            var path = OutFolder + "/" + prefabName + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(pivot, path, out bool ok);
            Note(string.Format("{0}: {1} renderers, footprint {2:0.0} x {3:0.0} m, height {4:0.0} m (ground offset {5:0.00}) -> {6} {7}",
                prefabName, rs.Count, b.size.x, b.size.z, b.max.y - ground, ground - b.min.y, path, ok ? "" : "SAVE FAILED"));
            return ok ? path : null;
        }

        // Donggung: thousands of loose roots. Remove the demo ground slab, then union-find pieces whose
        // bounds touch (0.6 m tolerance) into building compounds.
        static IEnumerable<string> DonggungClusters()
        {
            var scene = EditorSceneManager.OpenScene("Assets/DongGungComplex/Demo_DongGung.unity", OpenSceneMode.Single);
            var roots = scene.GetRootGameObjects().Where(g => g.GetComponentInChildren<Renderer>(true) != null).ToList();
            var items = roots.Select(g => (g: g, b: Bnd(g))).ToList();
            // ground slab(s): very large, very thin
            var slabs = items.Where(i => i.b.size.x * i.b.size.z > 3000 && i.b.size.y < 3).ToList();
            foreach (var s in slabs) Note("  Donggung: excluding demo ground '" + s.g.name + "' " + s.b.size.ToString("0"));
            items = items.Except(slabs).ToList();

            int n = items.Count; var parent = Enumerable.Range(0, n).ToArray();
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            var exp = items.Select(i => { var e = i.b; e.Expand(1.2f); return e; }).ToArray();
            // spatial hash to keep this fast
            var cell = 6f; var grid = new Dictionary<(int, int), List<int>>();
            for (int i = 0; i < n; i++)
            {
                var e = exp[i];
                for (int x = Mathf.FloorToInt(e.min.x / cell); x <= Mathf.FloorToInt(e.max.x / cell); x++)
                    for (int z = Mathf.FloorToInt(e.min.z / cell); z <= Mathf.FloorToInt(e.max.z / cell); z++)
                    {
                        if (!grid.TryGetValue((x, z), out var l)) grid[(x, z)] = l = new List<int>();
                        foreach (var j in l) if (exp[j].Intersects(e)) parent[Find(i)] = Find(j);
                        l.Add(i);
                    }
            }
            var clusters = Enumerable.Range(0, n).GroupBy(Find).Select(g => g.ToList()).OrderByDescending(g => g.Count).ToList();
            Note("  Donggung: " + n + " pieces -> " + clusters.Count + " connected groups; sizes: " + string.Join(",", clusters.Take(15).Select(c => c.Count)));

            // big groups become buildings; small leftovers join the nearest big group
            var big = clusters.Where(c => c.Count >= 60).ToList();
            var small = clusters.Where(c => c.Count < 60).ToList();
            Bounds CB(List<int> c) { var b = items[c[0]].b; foreach (var i in c) b.Encapsulate(items[i].b); return b; }
            var bigB = big.Select(CB).ToList();
            foreach (var s in small)
            {
                var sc = CB(s).center; int best = 0; float bd = float.MaxValue;
                for (int k = 0; k < big.Count; k++) { float d = bigB[k].SqrDistance(sc); if (d < bd) { bd = d; best = k; } }
                big[best].AddRange(s);
            }
            // halls stand free inside their courtyard walls: merge any group whose centre lies inside a bigger group's footprint
            var merged = true;
            while (merged)
            {
                merged = false;
                var bs = big.Select(CB).ToList();
                for (int a = 0; a < big.Count && !merged; a++)
                    for (int o = 0; o < big.Count && !merged; o++)
                    {
                        if (a == o || big[o].Count >= big[a].Count) continue;
                        var c = bs[o].center;
                        if (c.x > bs[a].min.x && c.x < bs[a].max.x && c.z > bs[a].min.z && c.z < bs[a].max.z)
                        { big[a].AddRange(big[o]); big.RemoveAt(o); merged = true; }
                    }
            }
            foreach (var old in AssetDatabase.FindAssets("KR_Donggung_Compound_", new[] { OutFolder }))
                AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(old));
            var ordered = big.Select(c => (c, b: CB(c))).OrderByDescending(t => t.c.Count).ToList();
            string[] names = { "KR_Donggung_MainPalace", "KR_Donggung_EastHall", "KR_Donggung_Extra_A", "KR_Donggung_Extra_B" };
            int idx = 0;
            foreach (var (c, b) in ordered)
                yield return SaveGroup(c.Select(i => items[i].g.transform).ToList(), names[Mathf.Min(idx++, names.Length - 1)] + (idx > names.Length ? "_" + idx : ""));
        }

        // Split KR_Donggung_MainPalace into compounds by cutting along the line (x or z) that crosses the fewest pieces,
        // recursively, until every part is at most MaxSide long. Pieces are never cut; nested piece-prefab links are kept.
        const float MaxSide = 72f;
        public static void SplitDonggungMain()
        {
            var args = Environment.GetCommandLineArgs();
            int ai = Array.IndexOf(args, "-surveyOut");
            outDir = ai >= 0 && ai + 1 < args.Length ? args[ai + 1] : "_Backups/koreanprefabs";
            Directory.CreateDirectory(outDir);
            ShaderUtil.allowAsyncCompilation = false;
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var src = AssetDatabase.LoadAssetAtPath<GameObject>(OutFolder + "/KR_Donggung_MainPalace.prefab");
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
                PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
                var pieces = inst.transform.Cast<Transform>().ToList();
                var parts = new List<List<Transform>>();
                Split(pieces, parts, 0);
                foreach (var old in AssetDatabase.FindAssets("KR_Donggung_Court_", new[] { OutFolder }))
                    AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(old));
                var made = new List<string>();
                int i = 1;
                foreach (var p in parts.OrderBy(p => PB(p).center.x).ThenBy(p => PB(p).center.z))
                    made.Add(SaveGroup(p, "KR_Donggung_Court_" + (i++).ToString("00")));
                AssetDatabase.SaveAssets();
                RenderPrefabs(made.Where(m => m != null).ToList());
            }
            catch (Exception e) { Note("EXCEPTION: " + e); throw; }
            finally
            {
                File.WriteAllText(Path.Combine(outDir, "donggung_split.txt"), R.ToString());
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        static Bounds PB(List<Transform> ts)
        {
            var rs = ts.SelectMany(t => t.GetComponentsInChildren<Renderer>(true)).ToList();
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
        }

        static void Split(List<Transform> pieces, List<List<Transform>> parts, int depth)
        {
            var b = PB(pieces);
            if ((b.size.x <= MaxSide && b.size.z <= MaxSide) || depth > 6 || pieces.Count < 50) { parts.Add(pieces); return; }
            var pb = pieces.Select(p => (t: p, b: PB(new List<Transform> { p }))).ToList();
            bool alongX = b.size.x >= b.size.z;
            float lo = alongX ? b.min.x : b.min.z, len = alongX ? b.size.x : b.size.z;
            float bestCut = lo + len / 2; int bestCross = int.MaxValue; float bestBal = float.MaxValue;
            for (float f = 0.3f; f <= 0.7f; f += 0.01f)
            {
                float cut = lo + len * f;
                int cross = pb.Count(p => (alongX ? p.b.min.x : p.b.min.z) < cut - 0.05f && (alongX ? p.b.max.x : p.b.max.z) > cut + 0.05f);
                float bal = Mathf.Abs(f - 0.5f);
                if (cross < bestCross || (cross == bestCross && bal < bestBal)) { bestCross = cross; bestCut = cut; bestBal = bal; }
            }
            var a = pb.Where(p => (alongX ? p.b.center.x : p.b.center.z) < bestCut).Select(p => p.t).ToList();
            var c = pb.Where(p => (alongX ? p.b.center.x : p.b.center.z) >= bestCut).Select(p => p.t).ToList();
            Note(string.Format("  split depth {0}: {1} pieces {2:0}x{3:0} m cut along {4} at {5:0.0} crossing {6} pieces -> {7} + {8}",
                depth, pieces.Count, b.size.x, b.size.z, alongX ? "x" : "z", bestCut, bestCross, a.Count, c.Count));
            Split(a, parts, depth + 1); Split(c, parts, depth + 1);
        }

        static Bounds Bnd(GameObject g)
        {
            var rs = g.GetComponentsInChildren<Renderer>(true);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
        }

        static void RenderPrefabs(List<string> paths)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var sky = AssetDatabase.LoadAssetAtPath<Material>("Assets/Free Island Collection/Environment/Skies/Sky Material.mat");
            if (sky) RenderSettings.skybox = sky;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox; DynamicGI.UpdateEnvironment();
            var sun = new GameObject("Sun").AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 1.2f; sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(42, -40, 0);
            var cam = new GameObject("Cam").AddComponent<Camera>(); cam.allowHDR = false; cam.fieldOfView = 40; cam.farClipPlane = 5000;
            foreach (var p in paths)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(p));
                var rs = go.GetComponentsInChildren<Renderer>(true); var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                float rad = b.extents.magnitude;
                cam.transform.position = b.center + new Vector3(-0.7f, 0.6f, -1f).normalized * rad * 1.7f; cam.transform.LookAt(b.center);
                cam.nearClipPlane = 0.3f;
                Shoot(cam, Path.GetFileNameWithoutExtension(p) + ".png");
                UnityEngine.Object.DestroyImmediate(go);
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
