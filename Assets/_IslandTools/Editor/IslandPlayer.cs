// Editor-only. Adds the Starter Assets third-person placeholder character (gamepad + keyboard via Input System)
// and a Cinemachine follow camera to Scene 1. Re-running replaces the previous setup.
// Batchmode: Unity.exe -batchmode -quit -projectPath <proj> -executeMethod IslandTools.IslandPlayer.Setup -surveyOut <dir>
using System;
using System.IO;
using System.Linq;
using System.Text;
using Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace IslandTools
{
    public static class IslandPlayer
    {
        const string Scene1Path = "Assets/Free Island Collection/Scenes/Scene 1.unity";
        const string ArmaturePath = "Assets/Starter Assets/Runtime/ThirdPersonController/Prefabs/PlayerArmature.prefab";
        const string FollowCamPath = "Assets/Starter Assets/Runtime/ThirdPersonController/Prefabs/PlayerFollowCamera.prefab";
        static readonly StringBuilder R = new StringBuilder();
        static void Note(string s) { R.AppendLine(s); Debug.Log("[IslandPlayer] " + s); }

        [MenuItem("Tools/Island/Setup Player")]
        public static void Setup()
        {
            var args = Environment.GetCommandLineArgs();
            int ai = Array.IndexOf(args, "-surveyOut");
            var outDir = ai >= 0 && ai + 1 < args.Length ? args[ai + 1] : "_Backups/player";
            Directory.CreateDirectory(outDir);
            try
            {
                var scene = EditorSceneManager.OpenScene(Scene1Path, OpenSceneMode.Single);
                var terrain = UnityEngine.Object.FindObjectOfType<Terrain>();

                var old = GameObject.Find("Player_Setup");
                if (old != null) UnityEngine.Object.DestroyImmediate(old);
                var root = new GameObject("Player_Setup");

                // spawn: just beyond the lowest torii gate of the Goju approach, facing up the path
                var approach = GameObject.Find("Island_Environment/Goju_Approach");
                var goju = GameObject.Find("Island_Environment/Landmarks/Pagoda_Goju_Main (site 2)");
                Vector3 spawn, look;
                if (approach != null && goju != null)
                {
                    var gp = goju.transform.position;
                    var far = approach.GetComponentsInChildren<Transform>().Where(t => t.parent == approach.transform && t.name.StartsWith("Torii"))
                        .OrderByDescending(t => (t.position - gp).sqrMagnitude).First().position;
                    var dir = new Vector3(far.x - gp.x, 0, far.z - gp.z).normalized;
                    spawn = far + dir * 6f;
                    look = gp;
                }
                else { spawn = new Vector3(1370, 0, 1616); look = spawn + Vector3.forward; Note("approach not found; spawning at bay landing"); }
                spawn.y = terrain.SampleHeight(spawn) + terrain.transform.position.y + 0.15f;

                var player = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ArmaturePath), scene);
                player.transform.SetParent(root.transform, false);
                player.transform.position = spawn;
                var flat = new Vector3(look.x - spawn.x, 0, look.z - spawn.z);
                player.transform.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
                var camRoot = player.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "PlayerCameraRoot");
                Note(string.Format("PlayerArmature at ({0:0.0},{1:0.0},{2:0.0}) facing the main pagoda; camera target: {3}", spawn.x, spawn.y, spawn.z, camRoot ? camRoot.name : "MISSING"));

                var follow = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(FollowCamPath), scene);
                follow.transform.SetParent(root.transform, false);
                var vcam = follow.GetComponent<CinemachineVirtualCamera>();
                vcam.Follow = camRoot;
                follow.transform.position = camRoot.position - player.transform.forward * 4f + Vector3.up * 0.5f;
                follow.transform.rotation = player.transform.rotation;
                Note("PlayerFollowCamera following PlayerCameraRoot");

                var main = Camera.main != null ? Camera.main : UnityEngine.Object.FindObjectsOfType<Camera>().FirstOrDefault(c => c.CompareTag("MainCamera"));
                if (main == null) throw new Exception("No MainCamera in Scene 1");
                if (main.GetComponent<CinemachineBrain>() == null) main.gameObject.AddComponent<CinemachineBrain>();
                main.nearClipPlane = 0.1f;
                main.transform.SetPositionAndRotation(follow.transform.position, follow.transform.rotation);
                Note(string.Format("Main Camera: CinemachineBrain added, near {0}, far {1}", main.nearClipPlane, main.farClipPlane));

                var ctrl = player.GetComponent("ThirdPersonController");
                var pin = player.GetComponent("PlayerInput");
                Note("Components on player: ThirdPersonController=" + (ctrl != null) + " PlayerInput=" + (pin != null));

                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new Exception("save failed");
                Note("Scene 1 saved.");

                // preview from behind the player
                ShaderUtil.allowAsyncCompilation = false;
                foreach (var p in UnityEngine.Object.FindObjectsOfType<ReflectionProbe>()) p.RenderProbe();
                var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
                main.targetTexture = rt; main.Render(); main.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
                RenderTexture.active = null; main.targetTexture = null;
                File.WriteAllBytes(Path.Combine(outDir, "player_view.png"), tex.EncodeToPNG());
            }
            catch (Exception e) { Note("EXCEPTION: " + e); throw; }
            finally { File.WriteAllText(Path.Combine(outDir, "player.txt"), R.ToString()); }
        }
    }
}
