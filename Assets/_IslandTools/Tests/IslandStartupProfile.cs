// Play Mode startup profiling for Scene 1 (run WITH graphics). Measures scene load time and the first 600 frames,
// plus selected profiler markers, for several experiment variants. Nothing is saved to the scene.
// Results -> _Backups/tests/startup.txt
#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace IslandTools.Tests
{
    public class IslandStartupProfile
    {
        const string Scene1Path = "Assets/Free Island Collection/Scenes/Scene 1.unity";
        static readonly string[] Markers =
        {
            "Shader.CreateGPUProgram", "Mesh.Bake Scaled Mesh PhysX CollisionData", "Mesh.Bake PhysX CollisionData",
            "ReflectionProbes.Render", "Terrain.Trees.Render", "Physics.Simulate", "Camera.Render", "Loading.ReadObject", "Application.Integrate Assets in Background",
        };

        class Variant { public string name; public Action<Scene> apply; }

        [UnityTest, Timeout(3600000)]
        public IEnumerator Startup_FrameTimes()
        {
            Directory.CreateDirectory("_Backups/tests");
            var variants = new List<Variant>
            {
                new Variant { name = "A baseline (cold)", apply = s => { } },
                new Variant { name = "A baseline (warm)", apply = s => { } },
                new Variant { name = "B realtime reflection probe disabled", apply = s => { foreach (var p in UnityEngine.Object.FindObjectsOfType<ReflectionProbe>()) if (p.mode == UnityEngine.Rendering.ReflectionProbeMode.Realtime) p.enabled = false; } },
                new Variant { name = "C tree distance 1500 m", apply = s => { var t = UnityEngine.Object.FindObjectOfType<Terrain>(); if (t) t.treeDistance = 1500; } },
                new Variant { name = "A baseline (warm, repeat)", apply = s => { } },
            };
            var sb = new StringBuilder();
            sb.AppendLine("Frame-time measurement in Editor Play Mode (editor overhead included; compare variants, not absolute numbers).");
            foreach (var v in variants)
            {
                var handles = new List<ProfilerRecorderHandle>(); ProfilerRecorderHandle.GetAvailable(handles);
                var recs = new List<ProfilerRecorder>();
                foreach (var m in Markers)
                {
                    var h = handles.FirstOrDefault(x => ProfilerRecorderHandle.GetDescription(x).Name == m);
                    var r = h.Valid ? new ProfilerRecorder(h, 700) : default;
                    if (r.Valid) r.Start();
                    recs.Add(r);
                }
                UnityEngine.Events.UnityAction<Scene, LoadSceneMode> hook = (s, m) => v.apply(s);
                SceneManager.sceneLoaded += hook;
                var t0 = Time.realtimeSinceStartupAsDouble;
                yield return EditorSceneManager.LoadSceneAsyncInPlayMode(Scene1Path, new LoadSceneParameters(LoadSceneMode.Single));
                var loadSec = Time.realtimeSinceStartupAsDouble - t0;
                SceneManager.sceneLoaded -= hook;
                var frames = new List<float>(); var markerTotals = new double[Markers.Length]; var markerMax = new double[Markers.Length];
                for (int i = 0; i < 600; i++)
                {
                    var f0 = Time.realtimeSinceStartupAsDouble;
                    yield return null;
                    frames.Add((float)((Time.realtimeSinceStartupAsDouble - f0) * 1000.0));
                    for (int k = 0; k < recs.Count; k++)
                        if (recs[k].Valid && recs[k].Count > 0) { double ms = recs[k].LastValue / 1e6; markerTotals[k] += ms; markerMax[k] = Math.Max(markerMax[k], ms); }
                }
                foreach (var r in recs) r.Dispose();
                var first60 = frames.Take(60).Sum(); var steady = frames.Skip(300).ToList();
                sb.AppendLine(string.Format("[{0}] scene load {1:0.00} s | first frame {2:0} ms | first 60 frames {3:0} ms | frames >100 ms: {4} | >50 ms: {5} | worst {6:0} ms | steady (frames 300-600) median {7:0.0} ms",
                    v.name, loadSec, frames[0], first60, frames.Count(f => f > 100), frames.Count(f => f > 50), frames.Max(), Median(steady)));
                sb.AppendLine("     spikes at frames: " + string.Join(", ", frames.Select((f, i) => (f, i)).Where(x => x.f > 100).Take(15).Select(x => x.i + ":" + x.f.ToString("0"))));
                for (int k = 0; k < Markers.Length; k++)
                    if (markerTotals[k] > 0.5) sb.AppendLine(string.Format("     marker {0}: total {1:0} ms over 600 frames, worst frame {2:0.0} ms", Markers[k], markerTotals[k], markerMax[k]));
                yield return null;
            }
            File.WriteAllText("_Backups/tests/startup.txt", sb.ToString());
            Debug.Log(sb.ToString());
        }

        static float Median(List<float> l) { var s = l.OrderBy(x => x).ToList(); return s.Count == 0 ? 0 : s[s.Count / 2]; }
    }
}
#endif
