// Editor-only, READ-ONLY. Opens each Free Island Collection scene, reports its terrain(s), water, structures and
// vegetation, and writes Docs/IslandMap/scene_inspection_raw.txt. Never saves a scene or asset.
// Batchmode: Unity.exe -batchmode -quit -projectPath <proj> -executeMethod IslandMap.Editor.IslandMapInspector.Run
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace IslandMap.Editor
{
    public static class IslandMapInspector
    {
        public const string SceneFolder = "Assets/Free Island Collection/Scenes/";

        [MenuItem("Tools/Island/Map/Inspect Island Scenes (read-only)")]
        public static void Run()
        {
            var sb = new StringBuilder();
            var hashes = new Dictionary<int, string>();
            for (int s = 1; s <= 5; s++)
            {
                string path = SceneFolder + "Scene " + s + ".unity";
                sb.AppendLine("==================== " + path);
                if (!File.Exists(path)) { sb.AppendLine("  MISSING"); continue; }
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                var roots = scene.GetRootGameObjects();
                sb.AppendLine("  roots: " + string.Join(", ", roots.Select(r => r.name)));

                foreach (var t in UnityEngine.Object.FindObjectsOfType<Terrain>())
                {
                    var td = t.terrainData;
                    var p = t.transform.position;
                    int res = td.heightmapResolution;
                    var h = td.GetHeights(0, 0, res, res);
                    float mn = float.MaxValue, mx = float.MinValue; double sum = 0;
                    foreach (var v in h) { mn = Mathf.Min(mn, v); mx = Mathf.Max(mx, v); sum += v; }
                    int hash = 17; for (int z = 0; z < res; z += 37) for (int x = 0; x < res; x += 37) hash = hash * 31 + Mathf.RoundToInt(h[z, x] * 100000);
                    string dup = hashes.TryGetValue(hash, out var other) ? "  (IDENTICAL heightmap to " + other + ")" : "";
                    hashes[hash] = "Scene " + s;
                    sb.AppendLine($"  TERRAIN '{t.name}' data '{td.name}' ({AssetDatabase.GetAssetPath(td)})");
                    sb.AppendLine($"    position {p:F2}  rotation {t.transform.eulerAngles:F1}  scale {t.transform.lossyScale:F2}");
                    sb.AppendLine($"    size {td.size:F1} (X x heightScale x Z)  heightmap {res}  alphamap {td.alphamapResolution}  basemap {td.baseMapResolution}  detail {td.detailResolution}");
                    sb.AppendLine($"    world X {p.x:F1}..{p.x + td.size.x:F1}  Z {p.z:F1}..{p.z + td.size.z:F1}  height {p.y + mn * td.size.y:F2}..{p.y + mx * td.size.y:F2} m (mean {p.y + sum / h.Length * td.size.y:F2}){dup}");
                    foreach (float sea in new[] { 0f, 45f })
                    {
                        int above = 0; foreach (var v in h) if (p.y + v * td.size.y > sea) above++;
                        sb.AppendLine($"    fraction above y={sea}: {(float)above / h.Length:P1}");
                    }
                    sb.AppendLine("    layers: " + string.Join(", ", td.terrainLayers.Select((l, k) => k + ":" + (l ? l.name + "[" + (l.diffuseTexture ? l.diffuseTexture.name : "-") + " tile " + l.tileSize.x + "]" : "null"))));
                    var aw = td.GetAlphamaps(0, 0, td.alphamapWidth, td.alphamapHeight);
                    var cover = new double[td.alphamapLayers];
                    for (int z = 0; z < td.alphamapHeight; z++) for (int x = 0; x < td.alphamapWidth; x++) for (int l = 0; l < td.alphamapLayers; l++) cover[l] += aw[z, x, l];
                    double tot = cover.Sum();
                    sb.AppendLine("    layer coverage: " + string.Join(", ", cover.Select((c, k) => k + ":" + (c / tot).ToString("P1"))));
                    sb.AppendLine($"    trees: {td.treeInstanceCount} instances, prototypes: " + string.Join(", ", td.treePrototypes.Select((tp, k) => k + ":" + (tp.prefab ? tp.prefab.name : "null") + "=" + td.treeInstances.Count(ti => ti.prototypeIndex == k))));
                    sb.AppendLine("    detail prototypes: " + string.Join(", ", td.detailPrototypes.Select((dp, k) => k + ":" + (dp.prototypeTexture ? dp.prototypeTexture.name : dp.prototype ? dp.prototype.name : "null"))));
                    for (int l = 0; l < td.detailPrototypes.Length; l++)
                    {
                        var d = td.GetDetailLayer(0, 0, td.detailWidth, td.detailHeight, l); long n = 0; foreach (var v in d) n += v;
                        sb.AppendLine($"      detail {l}: total density {n}");
                    }
                    sb.AppendLine($"    drawTreesAndFoliage {t.drawTreesAndFoliage} treeDistance {t.treeDistance} basemapDistance {t.basemapDistance} material {(t.materialTemplate ? t.materialTemplate.name + "/" + t.materialTemplate.shader.name : "default")}");
                }

                // Structures / non-terrain content with world bounds
                foreach (var r in roots)
                {
                    if (r.GetComponent<Terrain>()) continue;
                    var rends = r.GetComponentsInChildren<Renderer>(true);
                    string b = rends.Length > 0 ? Bounds(rends) : "no renderers";
                    sb.AppendLine($"  ROOT {r.name} pos {r.transform.position:F1} children {r.transform.childCount} renderers {rends.Length}  {b}  comps [{Comps(r)}]");
                    if (r.name == "Island_Environment" || r.name == "Player_Setup")
                        foreach (Transform g in r.transform)
                        {
                            var gr = g.GetComponentsInChildren<Renderer>(true);
                            sb.AppendLine($"    GROUP {g.name} pos {g.position:F1} children {g.childCount} renderers {gr.Length}  {(gr.Length > 0 ? Bounds(gr) : "")}  [{Comps(g.gameObject)}]");
                            if (g.name == "Ocean" || g.name == "WindZone") continue;
                            foreach (Transform c in g)
                            {
                                var cr = c.GetComponentsInChildren<Renderer>(true);
                                string src = PrefabUtility.GetCorrespondingObjectFromSource(c.gameObject) is GameObject src0 ? AssetDatabase.GetAssetPath(src0) : "-";
                                sb.AppendLine($"      {c.name}  pos {c.position:F1} rotY {c.eulerAngles.y:F0}  {(cr.Length > 0 ? Bounds(cr) : "")}  src {src}");
                            }
                        }
                }
                sb.AppendLine("  RenderSettings: fog " + RenderSettings.fog + " sun " + (RenderSettings.sun ? RenderSettings.sun.transform.eulerAngles.ToString("F1") : "none") + " skybox " + (RenderSettings.skybox ? RenderSettings.skybox.name : "none"));
                foreach (var l in UnityEngine.Object.FindObjectsOfType<Light>()) sb.AppendLine($"  LIGHT {l.name} {l.type} rot {l.transform.eulerAngles:F1}");
            }
            sb.AppendLine("Build scenes: " + string.Join(", ", EditorBuildSettings.scenes.Select(x => x.path + (x.enabled ? "" : "(off)"))));
            Directory.CreateDirectory("Docs/IslandMap");
            File.WriteAllText("Docs/IslandMap/scene_inspection_raw.txt", sb.ToString());
            Debug.Log(sb.ToString());
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); // discard, never save
        }

        static string Comps(GameObject g) => string.Join(",", g.GetComponents<Component>().Where(c => c && !(c is Transform)).Select(c => c.GetType().Name));

        static string Bounds(Renderer[] rs)
        {
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            return $"bounds X {b.min.x:F0}..{b.max.x:F0} Y {b.min.y:F0}..{b.max.y:F0} Z {b.min.z:F0}..{b.max.z:F0}";
        }
    }
}
