// Editor-only tooling: converts imported asset materials to URP and cleans up Scene 1.
// Run from the menu (Tools > Island > ...) or via batchmode:
//   Unity.exe -batchmode -quit -projectPath <proj> -executeMethod IslandTools.IslandUrpConversion.RunAll
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace IslandTools
{
    public static class IslandUrpConversion
    {
        const string ReportPath = "_Backups/2026-11-13_urp-steps1-3/conversion_report.txt";
        const string Scene1Path = "Assets/Free Island Collection/Scenes/Scene 1.unity";
        const string PagodaRoot = "Assets/JTS_PagodaArchitecture";
        const string PagodaUrpFolder = PagodaRoot + "/Materials_URP";

        static readonly string[] StandardConvertFolders =
        {
            "Assets/Waldemarst",
            "Assets/JTS_Shrine_Torii_Gates",
            "Assets/JTS_StoneLanterns",
            "Assets/Japanese Garden Pack",
        };

        static readonly StringBuilder Log = new StringBuilder();

        static void Note(string line)
        {
            Log.AppendLine(line);
            Debug.Log("[IslandURP] " + line);
        }

        [MenuItem("Tools/Island/Run URP Steps 1-3")]
        public static void RunAll()
        {
            Log.Clear();
            Note("Run at " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            try
            {
                Step1_ConvertStandardMaterials();
                AssetDatabase.SaveAssets();
                Step2_ConvertPagodaMaterials();
                AssetDatabase.SaveAssets();
                Step3_FixScene1();
                AssetDatabase.SaveAssets();
                Note("DONE");
            }
            finally
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
                File.WriteAllText(ReportPath, Log.ToString());
            }
        }

        // ---------------- Step 1 ----------------
        static void Step1_ConvertStandardMaterials()
        {
            Note("=== STEP 1: Unity URP material upgrader on asset-pack materials ===");
            var upgraders = GetUrpUpgraders();
            Note("Loaded " + upgraders.Count + " URP upgraders.");

            var counts = new Dictionary<string, int>();
            foreach (var guid in AssetDatabase.FindAssets("t:Material", StandardConvertFolders))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null || mat.shader == null) continue;
                var before = mat.shader.name;
                if (before.StartsWith("Universal Render Pipeline")) { Bump(counts, "already URP"); continue; }

                string message = "";
                bool ok = MaterialUpgrader.Upgrade(mat, upgraders, MaterialUpgrader.UpgradeFlags.None, ref message);
                var after = mat.shader.name;
                if (ok && after != before)
                {
                    EditorUtility.SetDirty(mat);
                    Bump(counts, before + " -> " + after);
                }
                else
                {
                    Bump(counts, "NOT CONVERTED (" + before + ")");
                    Note("  not converted: " + path + " [" + before + "] " + message);
                }
            }
            foreach (var kv in counts.OrderBy(k => k.Key)) Note("  " + kv.Value + " x " + kv.Key);
        }

        static List<MaterialUpgrader> GetUrpUpgraders()
        {
            var t = Type.GetType("UnityEditor.Rendering.Universal.UniversalRenderPipelineMaterialUpgrader, Unity.RenderPipelines.Universal.Editor");
            if (t == null) throw new Exception("URP material upgrader type not found");
            var m = t.GetMethod("GetUpgraders", BindingFlags.NonPublic | BindingFlags.Static);
            if (m == null) throw new Exception("URP GetUpgraders method not found");
            var args = new object[] { new List<MaterialUpgrader>() };
            m.Invoke(null, args);
            return (List<MaterialUpgrader>)args[0];
        }

        // ---------------- Step 2 ----------------
        static void Step2_ConvertPagodaMaterials()
        {
            Note("=== STEP 2: URP Lit copies of pagoda materials (originals untouched) ===");
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) throw new Exception("URP Lit shader not found");
            if (!AssetDatabase.IsValidFolder(PagodaUrpFolder))
                AssetDatabase.CreateFolder(PagodaRoot, "Materials_URP");

            var map = new Dictionary<Material, Material>();
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { PagodaRoot + "/Materials" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var src = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (src == null) continue;
                var sname = src.shader != null ? src.shader.name : "<null>";
                var dstPath = PagodaUrpFolder + "/" + src.name + "_URP.mat";

                var dst = AssetDatabase.LoadAssetAtPath<Material>(dstPath);
                bool isNew = dst == null;
                if (isNew) dst = new Material(lit) { name = src.name + "_URP" };
                else dst.shader = lit;

                if (sname == "TanukiDigital/Standard_Packed_PBR") FillFromPacked(src, dst);
                else if (sname.StartsWith("TanukiDigital/Decal")) FillFromDecal(src, dst);
                else { Note("  skipped (unknown shader " + sname + "): " + path); continue; }

                BaseShaderGUI.SetMaterialKeywords(dst, LitGUI.SetMaterialKeywords, null);
                if (isNew) AssetDatabase.CreateAsset(dst, dstPath);
                else EditorUtility.SetDirty(dst);
                map[src] = dst;
            }
            Note("  created/updated " + map.Count + " URP materials in " + PagodaUrpFolder);

            int prefabs = 0, slots = 0;
            foreach (var folder in new[] { PagodaRoot + "/Prefabs", PagodaRoot + "/Prefabs_LOD" })
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var root = PrefabUtility.LoadPrefabContents(path);
                    int changed = 0;
                    foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                    {
                        var mats = r.sharedMaterials;
                        bool any = false;
                        for (int i = 0; i < mats.Length; i++)
                        {
                            if (mats[i] != null && map.TryGetValue(mats[i], out var rep)) { mats[i] = rep; any = true; changed++; }
                        }
                        if (any) r.sharedMaterials = mats;
                    }
                    if (changed > 0) { PrefabUtility.SaveAsPrefabAsset(root, path); prefabs++; slots += changed; }
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            Note("  remapped " + slots + " material slots across " + prefabs + " pagoda prefabs");
        }

        static void CopyTex(Material src, string srcProp, Material dst, params string[] dstProps)
        {
            if (!src.HasProperty(srcProp)) return;
            var tex = src.GetTexture(srcProp);
            var scale = src.GetTextureScale(srcProp);
            var offset = src.GetTextureOffset(srcProp);
            foreach (var p in dstProps)
            {
                dst.SetTexture(p, tex);
                dst.SetTextureScale(p, scale);
                dst.SetTextureOffset(p, offset);
            }
        }

        static float F(Material m, string p, float def = 0f) => m.HasProperty(p) ? m.GetFloat(p) : def;

        static void FillFromPacked(Material src, Material dst)
        {
            // Source shader logic (td_StandardPacked.shader):
            //   albedo = _MainTex.rgb (tinted by _Color via mask.b), smoothness = _MainTex.a * _SmoothnessFac
            //   metallic = mask.r * _MetallicFac, occlusion = lerp(1, mask.g, _OcclusionFac)
            //   normal = lerp(flat, _NTex, _NormalFac)
            var mainTex = src.GetTexture("_MainTex");
            var mask = src.GetTexture("_MaskTex");
            var ntex = src.GetTexture("_NTex");
            var color = src.HasProperty("_Color") ? src.GetColor("_Color") : Color.white;
            float nf = F(src, "_NormalFac"), sf = F(src, "_SmoothnessFac"), mf = F(src, "_MetallicFac"), of = F(src, "_OcclusionFac");

            dst.SetFloat("_WorkflowMode", 1f);
            dst.SetFloat("_Surface", 0f);
            CopyTex(src, "_MainTex", dst, "_BaseMap", "_MainTex");
            dst.SetColor("_BaseColor", Color.white);
            dst.SetColor("_Color", Color.white);

            dst.SetFloat("_SmoothnessTextureChannel", 1f); // albedo alpha
            dst.SetFloat("_Smoothness", sf);

            if (mask != null && mf > 0.001f) { CopyTex(src, "_MaskTex", dst, "_MetallicGlossMap"); dst.SetFloat("_Metallic", mf); }
            else { dst.SetTexture("_MetallicGlossMap", null); dst.SetFloat("_Metallic", 0f); }

            if (mask != null && of > 0.001f) { CopyTex(src, "_MaskTex", dst, "_OcclusionMap"); dst.SetFloat("_OcclusionStrength", of); }
            else dst.SetTexture("_OcclusionMap", null);

            if (ntex != null && nf > 0.001f) { CopyTex(src, "_NTex", dst, "_BumpMap"); dst.SetFloat("_BumpScale", nf); }
            else dst.SetTexture("_BumpMap", null);

            var notes = new List<string>();
            bool tinted = color.a > 0.001f && (Mathf.Abs(color.r - 1) > 0.01f || Mathf.Abs(color.g - 1) > 0.01f || Mathf.Abs(color.b - 1) > 0.01f);
            if (tinted) notes.Add("mask-driven tint " + color + " not reproduced");
            if (mask != null && mf > 0.001f && mf < 0.99f) notes.Add("metallic factor " + mf.ToString("0.00") + " -> URP uses mask.r at full strength");
            if (mask != null && (mf > 0.001f || of > 0.001f))
            {
                var ti = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(mask)) as TextureImporter;
                if (ti != null && ti.sRGBTexture) notes.Add("mask texture is sRGB (same as original)");
            }
            Note(string.Format("  {0}: smooth={1:0.00} metal={2:0.00} occl={3:0.00} normal={4:0.00}{5}",
                src.name, sf, mf, of, nf, notes.Count > 0 ? "  NOTE: " + string.Join("; ", notes) : ""));
        }

        static void FillFromDecal(Material src, Material dst)
        {
            dst.SetFloat("_WorkflowMode", 1f);
            dst.SetFloat("_Surface", 1f);   // transparent
            dst.SetFloat("_Blend", 0f);     // alpha blend
            dst.SetFloat("_ReceiveShadows", 1f);
            dst.SetFloat("_CastShadows", 0f);
            CopyTex(src, "_AlbedoMap", dst, "_BaseMap", "_MainTex");
            dst.SetColor("_BaseColor", Color.white);
            dst.SetFloat("_Smoothness", F(src, "_DecalSmoothnessStrength") * 0.5f);
            dst.SetFloat("_Metallic", 0f);
            BaseShaderGUI.SetupMaterialBlendMode(dst);
            Note("  " + src.name + ": decal -> URP Lit transparent (alpha blend, no shadow casting). NOTE: original used a depth offset; check for z-fighting.");
        }

        // ---------------- Step 3 ----------------
        static void Step3_FixScene1()
        {
            Note("=== STEP 3: Scene 1 terrain material + missing scripts ===");
            var scene = EditorSceneManager.OpenScene(Scene1Path, OpenSceneMode.Single);

            var rp = GraphicsSettings.currentRenderPipeline;
            var terrainMat = rp != null ? rp.defaultTerrainMaterial : null;
            if (terrainMat == null) throw new Exception("URP default terrain material not found");

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Terrain>(true))
                {
                    var old = t.materialTemplate != null ? t.materialTemplate.name : "<none>";
                    t.materialTemplate = terrainMat;
                    EditorUtility.SetDirty(t);
                    Note("  Terrain '" + t.name + "': material " + old + " -> " + terrainMat.name + " (" + terrainMat.shader.name + ")");
                }
            }

            int removed = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var tr in root.GetComponentsInChildren<Transform>(true))
                {
                    int n = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(tr.gameObject);
                    if (n > 0)
                    {
                        GameObjectUtility.RemoveMonoBehavioursWithMissingScript(tr.gameObject);
                        Note("  Removed " + n + " missing script(s) from '" + tr.name + "'");
                        removed += n;
                    }
                }
            }
            Note("  total missing scripts removed: " + removed);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new Exception("Failed to save Scene 1");
            Note("  Scene 1 saved.");
        }

        // ---------------- Step 2b: bake mask-driven tint into new albedo textures ----------------
        // Original: albedo = lerp(tex.rgb, tex.rgb * _Color.rgb, mask.b * _Color.a)  (computed in linear space)
        // URP Lit has no mask-driven tint, so we bake it into a PNG next to the URP materials.
        [MenuItem("Tools/Island/Bake Pagoda Tints")]
        public static void BakePagodaTints()
        {
            Log.Clear();
            Note("=== STEP 2b: bake pagoda tints into new albedo textures ===");
            const string bakeFolder = PagodaUrpFolder + "/BakedAlbedo";
            if (!AssetDatabase.IsValidFolder(bakeFolder)) AssetDatabase.CreateFolder(PagodaUrpFolder, "BakedAlbedo");

            try
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { PagodaRoot + "/Materials" }))
                {
                    var src = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                    if (src == null || src.shader == null || src.shader.name != "TanukiDigital/Standard_Packed_PBR") continue;
                    var c = src.GetColor("_Color");
                    bool tinted = c.a > 0.001f && (Mathf.Abs(c.r - 1) > 0.01f || Mathf.Abs(c.g - 1) > 0.01f || Mathf.Abs(c.b - 1) > 0.01f);
                    if (!tinted) continue;

                    var albedo = src.GetTexture("_MainTex") as Texture2D;
                    var mask = src.GetTexture("_MaskTex") as Texture2D;
                    var dst = AssetDatabase.LoadAssetAtPath<Material>(PagodaUrpFolder + "/" + src.name + "_URP.mat");
                    if (albedo == null || dst == null) { Note("  skip " + src.name + " (missing albedo or URP material)"); continue; }

                    int w = albedo.width, h = albedo.height;
                    var a = ReadLinear(albedo, w, h);           // rgb linear, alpha raw (smoothness)
                    var m = mask != null ? ReadLinear(mask, w, h) : null;
                    var cl = c.linear;
                    var outPx = new Color[a.Length];
                    for (int i = 0; i < a.Length; i++)
                    {
                        float k = (m != null ? m[i].b : 1f) * c.a;
                        var p = a[i];
                        var lin = new Color(
                            Mathf.Lerp(p.r, p.r * cl.r, k),
                            Mathf.Lerp(p.g, p.g * cl.g, k),
                            Mathf.Lerp(p.b, p.b * cl.b, k));
                        var g = lin.gamma;
                        outPx[i] = new Color(g.r, g.g, g.b, p.a);
                    }
                    var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
                    tex.SetPixels(outPx);
                    tex.Apply();
                    var pngPath = bakeFolder + "/" + src.name + "_albedo_tinted.png";
                    File.WriteAllBytes(pngPath, tex.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(tex);

                    AssetDatabase.ImportAsset(pngPath, ImportAssetOptions.ForceSynchronousImport);
                    var srcImp = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(albedo)) as TextureImporter;
                    var imp = (TextureImporter)AssetImporter.GetAtPath(pngPath);
                    imp.sRGBTexture = true;
                    imp.alphaSource = TextureImporterAlphaSource.FromInput;
                    imp.alphaIsTransparency = false;
                    imp.mipmapEnabled = true;
                    if (srcImp != null) { imp.maxTextureSize = srcImp.maxTextureSize; imp.wrapMode = srcImp.wrapMode; imp.anisoLevel = srcImp.anisoLevel; }
                    imp.SaveAndReimport();

                    var baked = AssetDatabase.LoadAssetAtPath<Texture2D>(pngPath);
                    dst.SetTexture("_BaseMap", baked);
                    dst.SetTexture("_MainTex", baked);
                    EditorUtility.SetDirty(dst);
                    Note(string.Format("  {0}: baked tint {1} ({2}x{3}) -> {4}", src.name, c, w, h, pngPath));
                }
                AssetDatabase.SaveAssets();
                Note("DONE");
            }
            finally
            {
                File.AppendAllText(ReportPath, Log.ToString());
            }
        }

        static Color[] ReadLinear(Texture2D t, int w, int h)
        {
            // Blit through the GPU so compressed/non-readable textures can be read without changing their import settings.
            // A linear target stores exactly what the shader would sample (sRGB textures are decoded to linear).
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var prev = RenderTexture.active;
            Graphics.Blit(t, rt);
            RenderTexture.active = rt;
            var read = new Texture2D(w, h, TextureFormat.RGBAFloat, false, true);
            read.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            read.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var px = read.GetPixels();
            UnityEngine.Object.DestroyImmediate(read);
            return px;
        }

        static void Bump(Dictionary<string, int> d, string k) { d.TryGetValue(k, out var c); d[k] = c + 1; }
    }
}
