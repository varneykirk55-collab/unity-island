// Editor-only, READ-ONLY. Close-up renders + collider listing for specific doorway openings. Scene never saved.
using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace IslandTools
{
    public static class IslandDoorDiag
    {
        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            int ai = Array.IndexOf(args, "-surveyOut");
            var outDir = ai >= 0 && ai + 1 < args.Length ? args[ai + 1] : "_Backups/doordiag";
            Directory.CreateDirectory(outDir);
            ShaderUtil.allowAsyncCompilation = false;
            var sb = new StringBuilder();
            EditorSceneManager.OpenScene("Assets/Free Island Collection/Scenes/Scene 1.unity", OpenSceneMode.Single);
            Physics.SyncTransforms();
            var lines = File.ReadAllLines(Path.Combine(outDir, "..", "inspect", "openings.csv")).Skip(1)
                .Select(l => l.Split(',')).Where(c => float.Parse(c[7]) >= 0.8f && float.Parse(c[8]) >= 1.8f).ToList();
            var cam = new GameObject("DiagCam").AddComponent<Camera>(); cam.allowHDR = false; cam.fieldOfView = 60; cam.nearClipPlane = 0.05f; cam.farClipPlane = 500;
            var light = UnityEngine.Object.FindObjectOfType<Light>();
            int idx = 0;
            foreach (var c in lines)
            {
                var centre = new Vector3(float.Parse(c[2]), float.Parse(c[3]), float.Parse(c[4]));
                var through = new Vector3(float.Parse(c[5]), 0, float.Parse(c[6]));
                sb.AppendLine(string.Format("#{0} {1}/{2} centre {3} through {4} width {5} height {6} sill {7}", idx, c[0], c[1], centre.ToString("0.0"), through.ToString("0.00"), c[7], c[8], c[9]));
                // everything solid inside a 1.6 m-wide, 2 m-tall, 3 m-long box through the opening
                var rot = Quaternion.LookRotation(through, Vector3.up);
                var hits = Physics.OverlapBox(centre + Vector3.up * 1.0f, new Vector3(0.8f, 1.0f, 1.5f), rot, ~0, QueryTriggerInteraction.Ignore);
                foreach (var h in hits.OrderBy(h => h.bounds.min.y))
                {
                    var r = h.GetComponentInChildren<MeshFilter>();
                    sb.AppendLine(string.Format("     {0,-34} {1,-13} bounds y {2:0.00}..{3:0.00} (rel floor {4:0.00}..{5:0.00}) size {6} colMesh={7} visMesh={8}",
                        h.name, h.GetType().Name, h.bounds.min.y, h.bounds.max.y, h.bounds.min.y - centre.y, h.bounds.max.y - centre.y, h.bounds.size.ToString("0.00"),
                        h is MeshCollider mc && mc.sharedMesh ? mc.sharedMesh.name : "-", r && r.sharedMesh ? r.sharedMesh.name : "-"));
                }
                // renders: outside looking in, and side view
                cam.transform.position = centre - through * 3.5f + Vector3.up * 1.6f; cam.transform.LookAt(centre + Vector3.up * 1.0f);
                Shoot(cam, Path.Combine(outDir, "door_" + idx + "_front.png"));
                var side = Vector3.Cross(Vector3.up, through);
                cam.transform.position = centre + side * 3.0f + Vector3.up * 1.2f; cam.transform.LookAt(centre + Vector3.up * 0.6f);
                Shoot(cam, Path.Combine(outDir, "door_" + idx + "_side.png"));
                idx++;
            }
            File.WriteAllText(Path.Combine(outDir, "doordiag.txt"), sb.ToString());
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

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
