// Editor-only. Adds missing colliders so the player can't walk through trees and buildings.
//  - Terrain tree prototypes: prefab variant with a trunk CapsuleCollider on the root (originals untouched)
//  - Korean building prefabs (_IslandEnvironment/Buildings): MeshCollider on every piece lacking a collider
//  - Scene objects under Island_Environment: MeshColliders (LOD0) / trunk capsules where missing
// Small plants (boxwood, ferns) are intentionally left without colliders.
// Batchmode: Unity.exe -batchmode -quit -projectPath <proj> -executeMethod IslandTools.IslandColliders.Run -surveyOut <dir>
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
    public static class IslandColliders
    {
        const string Scene1Path = "Assets/Free Island Collection/Scenes/Scene 1.unity";
        const string VariantFolder = "Assets/_IslandEnvironment/TreeVariants";
        static readonly StringBuilder R = new StringBuilder();
        static void Note(string s) { R.AppendLine(s); Debug.Log("[IslandColliders] " + s); }

        [MenuItem("Tools/Island/Add Missing Colliders")]
        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            int ai = Array.IndexOf(args, "-surveyOut");
            var outDir = ai >= 0 && ai + 1 < args.Length ? args[ai + 1] : "_Backups/colliders";
            Directory.CreateDirectory(outDir);
            try
            {
                BuildingPrefabs();
                AssetDatabase.SaveAssets();

                var scene = EditorSceneManager.OpenScene(Scene1Path, OpenSceneMode.Single);
                TerrainTrees(UnityEngine.Object.FindObjectOfType<Terrain>());
                SceneObjects();
                EditorSceneManager.MarkSceneDirty(scene);
                AssetDatabase.SaveAssets();
                if (!EditorSceneManager.SaveScene(scene)) throw new Exception("save failed");
                Note("Scene 1 saved.");
            }
            catch (Exception e) { Note("EXCEPTION: " + e); throw; }
            finally { File.WriteAllText(Path.Combine(outDir, "colliders.txt"), R.ToString()); }
        }

        // ---------------- Korean building prefabs
        static void BuildingPrefabs()
        {
            Note("=== Building prefabs ===");
            foreach (var g in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_IslandEnvironment/Buildings" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                var root = PrefabUtility.LoadPrefabContents(path);
                int added = 0, already = 0;
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    // a piece is "covered" if its own piece-root (nearest prefab instance root) already has any collider
                    var pieceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(mf.gameObject) ?? mf.gameObject;
                    if (pieceRoot == root) pieceRoot = mf.gameObject;
                    if (pieceRoot.GetComponentInChildren<Collider>(true) != null) { already++; continue; }
                    var mc = mf.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; added++;
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
                PrefabUtility.UnloadPrefabContents(root);
                Note(string.Format("  {0}: {1} pieces already had colliders, {2} MeshColliders added", Path.GetFileNameWithoutExtension(path), already, added));
            }
        }

        // ---------------- terrain trees
        static void TerrainTrees(Terrain t)
        {
            Note("=== Terrain tree prototypes ===");
            if (!AssetDatabase.IsValidFolder(VariantFolder)) AssetDatabase.CreateFolder("Assets/_IslandEnvironment", "TreeVariants");
            var td = t.terrainData;
            var protos = td.treePrototypes;
            for (int i = 0; i < protos.Length; i++)
            {
                var src = protos[i].prefab;
                if (src == null) continue;
                if (src.GetComponent<Collider>() != null) { Note("  " + src.name + ": root collider already present (" + src.GetComponent<Collider>().GetType().Name + ")"); continue; }
                var vPath = VariantFolder + "/" + src.name + "_Collider.prefab";
                var existing = AssetDatabase.LoadAssetAtPath<GameObject>(vPath);
                if (existing == null)
                {
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
                    var cap = inst.AddComponent<CapsuleCollider>();
                    SizeTrunk(inst, cap);
                    existing = PrefabUtility.SaveAsPrefabAsset(inst, vPath);
                    UnityEngine.Object.DestroyImmediate(inst);
                }
                var c = existing.GetComponent<CapsuleCollider>();
                protos[i].prefab = existing;
                Note(string.Format("  {0}: variant with trunk capsule r={1:0.00} h={2:0.0} -> {3}", src.name, c.radius, c.height, vPath));
            }
            td.treePrototypes = protos;
            td.RefreshPrototypes();
            EditorUtility.SetDirty(td);
            var tc = t.GetComponent<TerrainCollider>();
            Note("  TerrainCollider present: " + (tc != null) + "; tree instances: " + td.treeInstanceCount + " (instance scale is applied to colliders)");
        }

        static void SizeTrunk(GameObject tree, CapsuleCollider cap)
        {
            var b = LOD0Bounds(tree);
            bool bamboo = tree.name.Contains("Bamboo");
            float h = Mathf.Max(2f, b.size.y * (bamboo ? 0.9f : 0.55f));     // trunk zone, not the canopy
            float r = bamboo ? Mathf.Clamp(Mathf.Min(b.size.x, b.size.z) * 0.22f, 0.3f, 0.8f)
                             : Mathf.Clamp(Mathf.Min(b.size.x, b.size.z) * 0.06f, 0.15f, 0.45f);
            cap.direction = 1; cap.radius = r; cap.height = h;
            cap.center = new Vector3(0, b.min.y - tree.transform.position.y + h / 2f, 0);
        }

        // ---------------- scene objects
        static void SceneObjects()
        {
            Note("=== Scene objects under Island_Environment ===");
            var env = GameObject.Find("Island_Environment");
            var counts = new Dictionary<string, int>();
            void Bump(string k) { counts.TryGetValue(k, out var v); counts[k] = v + 1; }
            foreach (Transform grp in env.transform)
            {
                if (grp.name == "Ocean" || grp.name == "WindZone" || grp.name == "Korean_Buildings") continue;
                foreach (Transform unit in grp)
                {
                    var go = unit.gameObject;
                    if (go.GetComponentInChildren<Collider>(true) != null) { Bump("already had collider"); continue; }
                    if (go.name.StartsWith("Plant_")) { Bump("small plant (left walk-through)"); continue; }
                    if (go.name.StartsWith("Tree_"))
                    {
                        var cap = go.AddComponent<CapsuleCollider>(); SizeTrunk(go, cap); Bump("tree trunk capsule added");
                        continue;
                    }
                    int n = 0;
                    foreach (var r in LOD0Renderers(go))
                    {
                        var mf = r.GetComponent<MeshFilter>();
                        if (mf == null || mf.sharedMesh == null) continue;
                        var mc = r.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; n++;
                    }
                    Bump(n > 0 ? "mesh collider(s) added: " + Clean(go.name) : "no mesh found: " + Clean(go.name));
                }
            }
            foreach (var kv in counts.OrderBy(k => k.Key)) Note("  " + kv.Value + " x " + kv.Key);
        }

        static string Clean(string n) { int i = n.IndexOf(" ("); var s = i > 0 ? n.Substring(0, i) : n; return System.Text.RegularExpressions.Regex.Replace(s, @"_?\d+$", ""); }

        static IEnumerable<Renderer> LOD0Renderers(GameObject go)
        {
            var lod = go.GetComponentInChildren<LODGroup>();
            if (lod != null) { var l = lod.GetLODs(); if (l.Length > 0) return l[0].renderers.Where(r => r != null); }
            return go.GetComponentsInChildren<MeshRenderer>(true);
        }

        static Bounds LOD0Bounds(GameObject go)
        {
            var rs = LOD0Renderers(go).ToList();
            if (rs.Count == 0) rs = go.GetComponentsInChildren<Renderer>(true).ToList();
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
        }
    }
}
