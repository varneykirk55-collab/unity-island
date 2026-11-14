// Editor-only. Applies the Bitgem URP Stylised Water shader to Scene 1's Ocean.
//  - enables Depth + Opaque textures on the active URP asset (the shader samples scene depth/colour)
//  - builds a world-scale ocean grid mesh (vertex waves + white vertex colours the shader expects)
//  - creates M_Ocean_Stylized from Bitgem's example material (Bitgem files untouched)
// The previous material (M_Ocean_URP) is kept for rollback.
// Batchmode: Unity.exe -batchmode -quit -projectPath <proj> -executeMethod IslandTools.IslandStylizedWater.Run -surveyOut <dir>
using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace IslandTools
{
    public static class IslandStylizedWater
    {
        const string Scene1Path = "Assets/Free Island Collection/Scenes/Scene 1.unity";
        const string ExampleMat = "Assets/Bitgem/StylisedWater/URP/Materials/example-water-01.mat";
        const string NewMat = "Assets/_IslandEnvironment/Materials/M_Ocean_Stylized.mat";
        const string TransparentGraph = "Assets/_IslandEnvironment/Shaders/OceanStylized-URP-Transparent.shadergraph";
        const string MeshPath ="Assets/_IslandEnvironment/Meshes/OceanGrid_12km.asset";
        const float Sea = 45f, Size = 12000f, Spacing = 25f;
        static string outDir;
        static readonly StringBuilder R = new StringBuilder();
        static void Note(string s) { R.AppendLine(s); Debug.Log("[StylizedWater] " + s); }

        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            int ai = Array.IndexOf(args, "-surveyOut");
            outDir = ai >= 0 && ai + 1 < args.Length ? args[ai + 1] : "_Backups/water";
            Directory.CreateDirectory(outDir);
            ShaderUtil.allowAsyncCompilation = false;
            try
            {
                // 1. URP depth + opaque textures
                var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                if (urp == null) throw new Exception("Active pipeline is not URP");
                urp.supportsCameraDepthTexture = true;
                urp.supportsCameraOpaqueTexture = true;
                EditorUtility.SetDirty(urp);
                Note("URP asset '" + urp.name + "': Depth Texture ON, Opaque Texture ON");

                // 2. mesh
                if (!AssetDatabase.IsValidFolder("Assets/_IslandEnvironment/Meshes")) AssetDatabase.CreateFolder("Assets/_IslandEnvironment", "Meshes");
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
                if (mesh == null) { mesh = BuildGrid(); AssetDatabase.CreateAsset(mesh, MeshPath); }
                // Bitgem's volume meshes encode edge foam in vertex colour; open ocean = no edge foam (black)
                var ex = UnityEngine.Object.FindObjectOfType<MeshFilter>();
                mesh.colors = Enumerable.Repeat(Color.black, mesh.vertexCount).ToArray();
                EditorUtility.SetDirty(mesh);
                Note(string.Format("Ocean mesh: {0} vertices, {1:0} m spacing, {2:0} km square -> {3}", mesh.vertexCount, Spacing, Size / 1000f, MeshPath));

                // 3. material
                var example = AssetDatabase.LoadAssetAtPath<Material>(ExampleMat);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(NewMat);
                if (mat == null) { mat = new Material(example) { name = "M_Ocean_Stylized" }; AssetDatabase.CreateAsset(mat, NewMat); }
                else mat.CopyPropertiesFromMaterial(example);
                // transparent copy of the graph (the original legacy graph is Opaque, which breaks its own depth/colour sampling)
                AssetDatabase.ImportAsset(TransparentGraph, ImportAssetOptions.ForceSynchronousImport);
                var tShader = AssetDatabase.LoadAssetAtPath<Shader>(TransparentGraph);
                if (tShader != null) { mat.shader = tShader; Note("Using transparent graph copy: " + tShader.name + " (supported=" + tShader.isSupported + ")"); }
                else Note("WARNING: transparent graph copy not found; staying on original graph");
                Tune(mat);
                EditorUtility.SetDirty(mat);
                AssetDatabase.SaveAssets();

                // 4. scene
                var scene = EditorSceneManager.OpenScene(Scene1Path, OpenSceneMode.Single);
                var ocean = GameObject.Find("Island_Environment/Ocean");
                if (ocean == null) throw new Exception("Ocean not found");
                var t = UnityEngine.Object.FindObjectOfType<Terrain>();
                var c = t.transform.position + new Vector3(t.terrainData.size.x / 2, 0, t.terrainData.size.z / 2);
                ocean.transform.position = new Vector3(c.x, Sea, c.z);
                ocean.transform.localScale = Vector3.one;
                ocean.GetComponent<MeshFilter>().sharedMesh = mesh;
                var mr = ocean.GetComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new Exception("save failed");
                Note("Ocean: grid mesh + M_Ocean_Stylized applied, scale 1, y=" + Sea + ". Scene 1 saved.");

                Renders(t);
            }
            catch (Exception e) { Note("EXCEPTION: " + e); throw; }
            finally { File.WriteAllText(Path.Combine(outDir, "water.txt"), R.ToString()); }
        }

        // shader property names are the graph's reference names
        static void Tune(Material m)
        {
            void C(string refName, Color v) { if (m.HasProperty(refName)) m.SetColor(refName, v); else Note("  missing " + refName); }
            void F(string refName, float v) { if (m.HasProperty(refName)) m.SetFloat(refName, v); else Note("  missing " + refName); }
            void V(string refName, Vector4 v) { if (m.HasProperty(refName)) m.SetVector(refName, v); else Note("  missing " + refName); }
            Note("Example values: shallow " + Get(m, "Color_F01C36BF") + " deep " + Get(m, "Color_7D9A58EC") + " UVScale " + GetV(m, "Vector2_37B21477")
                 + " depthScale " + GetF(m, "Vector1_17E53C12") + " foamWidth " + GetF(m, "Vector1_36E8227") + " waveScale " + GetF(m, "_WaveScale"));
            C("Color_F01C36BF", new Color(0.30f, 0.78f, 0.80f, 0.75f));  // _ShallowColor: turquoise shallows
            C("Color_7D9A58EC", new Color(0.03f, 0.20f, 0.33f, 1.00f));  // _DeepColor: deep sea blue
            F("_WaveScale", 0.25f);      // metres of vertex swell
            F("_WaveFrequency", 0.08f);  // long swells
            F("_WaveSpeed", 0.6f);
            V("Vector2_37B21477", new Vector4(0.05f, 0.05f, 0, 0)); // _UVScale: normal tiling for open-sea scale
            F("Vector1_17E53C12", 0.45f); // _DepthScale: fully deep by ~2-3 m, hides the submerged terrain slab edge
        }
        static string Get(Material m, string p) => m.HasProperty(p) ? m.GetColor(p).ToString() : "n/a";
        static string GetV(Material m, string p) => m.HasProperty(p) ? m.GetVector(p).ToString() : "n/a";
        static string GetF(Material m, string p) => m.HasProperty(p) ? m.GetFloat(p).ToString("0.###") : "n/a";

        static Mesh BuildGrid()
        {
            int n = Mathf.RoundToInt(Size / Spacing) + 1;
            var v = new Vector3[n * n]; var uv = new Vector2[n * n]; var col = new Color[n * n]; var nor = new Vector3[n * n];
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    int i = z * n + x;
                    float px = -Size / 2 + x * Spacing, pz = -Size / 2 + z * Spacing;
                    v[i] = new Vector3(px, 0, pz); uv[i] = new Vector2(px, pz); col[i] = Color.white; nor[i] = Vector3.up;
                }
            var tri = new int[(n - 1) * (n - 1) * 6]; int k = 0;
            for (int z = 0; z < n - 1; z++)
                for (int x = 0; x < n - 1; x++)
                {
                    int a = z * n + x, b = a + 1, c = a + n, d = c + 1;
                    tri[k++] = a; tri[k++] = c; tri[k++] = b; tri[k++] = b; tri[k++] = c; tri[k++] = d;
                }
            var m = new Mesh { name = "OceanGrid_12km", indexFormat = IndexFormat.UInt32 };
            m.vertices = v; m.uv = uv; m.colors = col; m.normals = nor; m.triangles = tri;
            m.RecalculateTangents(); m.RecalculateBounds();
            m.bounds = new Bounds(Vector3.zero, new Vector3(Size, 20, Size));
            return m;
        }

        // Diagnostic: render Bitgem's own example scene untouched (scene is never saved)
        public static void RenderExample()
        {
            var args = Environment.GetCommandLineArgs();
            int ai = Array.IndexOf(args, "-surveyOut");
            outDir = ai >= 0 && ai + 1 < args.Length ? args[ai + 1] : "_Backups/water";
            Directory.CreateDirectory(outDir);
            ShaderUtil.allowAsyncCompilation = false;
            var scene = EditorSceneManager.OpenScene("Assets/Bitgem/StylisedWater/URP/Examples/Example-Scene-01.unity", OpenSceneMode.Single);
            var sb = new StringBuilder();
            foreach (var r in UnityEngine.Object.FindObjectsOfType<Renderer>())
                foreach (var m in r.sharedMaterials) if (m && m.shader.name.Contains("Water")) sb.AppendLine(r.name + " -> " + m.name + " / " + m.shader.name + " queue " + m.renderQueue + " mesh " + (r.GetComponent<MeshFilter>() ? r.GetComponent<MeshFilter>().sharedMesh?.name : "?"));
            var cam = Camera.main ?? UnityEngine.Object.FindObjectOfType<Camera>();
            sb.AppendLine("camera: " + (cam ? cam.name : "none"));
            var data = cam.GetUniversalAdditionalCameraData(); data.requiresDepthTexture = true; data.requiresColorTexture = true;
            Shoot(cam, "bitgem_example_camera.png");
            // also our ocean material on their scene's water renderers, same camera
            var ours = AssetDatabase.LoadAssetAtPath<Material>(NewMat);
            foreach (var r in UnityEngine.Object.FindObjectsOfType<Renderer>())
                if (r.sharedMaterial && r.sharedMaterial.shader.name.Contains("WaterVolume")) r.sharedMaterial = ours;
            Shoot(cam, "bitgem_example_with_our_material.png");
            File.WriteAllText(Path.Combine(outDir, "example.txt"), sb.ToString());
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        static void Renders(Terrain t)
        {
            foreach (var p in UnityEngine.Object.FindObjectsOfType<ReflectionProbe>()) p.RenderProbe();
            var cam = Camera.main;
            if (cam == null) cam = new GameObject("WCam").AddComponent<Camera>();
            var brain = cam.GetComponent("CinemachineBrain") as Behaviour; if (brain) brain.enabled = false;
            var data = cam.GetUniversalAdditionalCameraData(); data.requiresDepthTexture = true; data.requiresColorTexture = true;
            cam.allowHDR = false; cam.fieldOfView = 50; cam.farClipPlane = 10000; cam.nearClipPlane = 0.3f;
            void At(Vector3 pos, Vector3 look, string f) { cam.transform.position = pos; cam.transform.LookAt(look); Shoot(cam, f); }
            At(new Vector3(1250, 650, 3700), new Vector3(1250, 80, 1250), "water_island_north.png");
            At(new Vector3(1250, 52, -60), new Vector3(1250, 47, 500), "water_south_shore_low.png");
            At(new Vector3(1600, 75, 2050), new Vector3(1450, 45, 1800), "water_bay.png");
            At(new Vector3(1789, 70, 2120), new Vector3(1789, 45, 1950), "water_court03_shore.png");
        }

        static void Shoot(Camera cam, string file, int w = 1280, int h = 720)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1 };
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
