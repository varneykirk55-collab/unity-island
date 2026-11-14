// Play Mode validation of the sword integration in the real scene with the real ThirdPersonController.
// Movement input is simulated through StarterAssetsInputs (as IslandTraversalTests does); sword input goes through
// the same public methods the InputActions call. Results + screenshots -> _Backups/sword/
#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using IslandCombat;
using NUnit.Framework;
using StarterAssets;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace IslandCombat.Tests
{
    public class SwordPlayTests
    {
        const string ScenePath = "Assets/_IslandScenes/Island_Main.unity";
        const string OutDir = "_Backups/sword";

        GameObject player; CharacterController cc; StarterAssetsInputs input; ThirdPersonController tpc;
        SwordCombat sword; Animator anim; Camera cam, shotCam; Transform hand, hips;
        readonly StringBuilder log = new StringBuilder();
        readonly List<string> errors = new List<string>();
        int fails;

        void Check(bool ok, string what) { if (!ok) fails++; log.AppendLine((ok ? "PASS  " : "FAIL  ") + what); }
        void OnLog(string msg, string stack, LogType t) { if (t == LogType.Error || t == LogType.Exception || t == LogType.Assert) errors.Add(t + ": " + msg); }

        IEnumerator Frames(float seconds, Vector2 move = default, bool sprint = false)
        {
            float t = 0;
            while (t < seconds) { input.move = move; input.sprint = sprint; Steer(); yield return null; t += Time.deltaTime; }
            input.move = Vector2.zero; input.sprint = false;
        }
        Vector3 heading;
        void Steer() { cam.transform.rotation = Quaternion.LookRotation(heading); }

        void Shot(string name, float side = 1f)
        {
            var p = player.transform;
            var target = p.position + Vector3.up * 1.1f;
            shotCam.transform.position = target + p.forward * 2.4f + p.right * 1.4f * side + Vector3.up * 0.25f;
            shotCam.transform.LookAt(target);
            var rt = new RenderTexture(960, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            shotCam.targetTexture = rt; shotCam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(960, 720, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 960, 720), 0, 0); tex.Apply();
            RenderTexture.active = null; shotCam.targetTexture = null; rt.Release();
            File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), tex.EncodeToPNG());
        }

        float Flat(Vector3 a, Vector3 b) { a.y = 0; b.y = 0; return Vector3.Distance(a, b); }

        [UnityTest, Timeout(600000)]
        public IEnumerator SwordIntegration_Playable()
        {
            Directory.CreateDirectory(OutDir);
            foreach (var f in Directory.GetFiles(OutDir, "*.png")) File.Delete(f);
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            for (int i = 0; i < 10; i++) yield return null;
            Application.logMessageReceived += OnLog;
            try
            {
                player = GameObject.Find("Player_Setup/PlayerArmature");
                Check(player != null, "1. character spawns (Player_Setup/PlayerArmature)");
                if (player == null) yield break;
                cc = player.GetComponent<CharacterController>(); input = player.GetComponent<StarterAssetsInputs>();
                tpc = player.GetComponent<ThirdPersonController>(); sword = player.GetComponent<SwordCombat>(); anim = player.GetComponent<Animator>();
                var pi = player.GetComponent("PlayerInput") as Behaviour; if (pi) pi.enabled = false;
                cam = Camera.main; var brain = cam.GetComponent("CinemachineBrain") as Behaviour; if (brain) brain.enabled = false;
                shotCam = new GameObject("ShotCam").AddComponent<Camera>(); shotCam.enabled = false; shotCam.nearClipPlane = 0.05f; shotCam.fieldOfView = 40;
                hand = anim.GetBoneTransform(HumanBodyBones.RightHand); hips = anim.GetBoneTransform(HumanBodyBones.Hips);
                heading = Vector3.ProjectOnPlane(player.transform.forward, Vector3.up).normalized;
                Check(sword != null && sword.enabled, "SwordCombat present and enabled (animator layers found)");

                yield return Frames(1.5f);
                Check(tpc.Grounded, "1. grounded after spawn");
                Check(sword.State == SwordCombat.SwordState.Sheathed && sword.Sword.parent == sword.SheathSocket, "sword starts sheathed in the saya");
                Shot("01_sheathed_idle");

                // ---- 2/3 baseline locomotion
                var p0 = player.transform.position; yield return Frames(2f, Vector2.up);
                float walk = Flat(p0, player.transform.position) / 2f;
                Check(walk > 1.2f, $"2. walking works (avg {walk:0.00} m/s, MoveSpeed {tpc.MoveSpeed})");
                p0 = player.transform.position; yield return Frames(2f, Vector2.up, true);
                float run = Flat(p0, player.transform.position) / 2f;
                Check(run > 3.5f, $"2. running works (avg {run:0.00} m/s)");
                yield return Frames(0.5f);
                yield return Jump("3. jumping works (sheathed)");

                // ---- 4 draw (+ spam)
                Check(sword.ToggleSword(), "4. draw accepted");
                int spamAccepted = 0; float t = 0;
                while (sword.State == SwordCombat.SwordState.Drawing && t < 2f) { if (sword.ToggleSword()) spamAccepted++; if (sword.Attack()) spamAccepted++; yield return null; t += Time.deltaTime; }
                Check(spamAccepted == 0, $"draw cannot be interrupted by spammed draw/sheathe/attack ({spamAccepted} accepted)");
                Check(sword.State == SwordCombat.SwordState.Equipped, $"4. draw completes into equipped stance ({t:0.00}s)");
                Check(sword.Sword.parent == sword.HandSocket, "4. sword moved to the right-hand socket");
                yield return Frames(0.6f);
                Shot("02_equipped_idle"); Shot("02b_equipped_idle_other_side", -1f);
                float handGap = Vector3.Distance(sword.Sword.TransformPoint(new Vector3(0, 0, -0.13f)), hand.position);
                Check(handGap < 0.15f, $"8. sword handle is in the hand (tsuka centre {handGap:0.000} m from wrist bone)");

                // ---- 6/7/9 attacks with spammed input, no root drift
                var rootBefore = player.transform.position; float maxHipJump = 0; Vector3 lastHip = player.transform.InverseTransformPoint(hips.position);
                Check(sword.Attack(), "6. attack accepted when equipped");
                int maxIndex = 0, stepsSeen = 0, lastIdx = -1; t = 0; var shots = new HashSet<int>();
                while (sword.State == SwordCombat.SwordState.Attacking && t < 6f)
                {
                    for (int k = 0; k < 5; k++) sword.Attack();      // hammer the button
                    if (sword.AttackIndex != lastIdx) { stepsSeen++; Check(sword.AttackIndex == lastIdx + 1 || lastIdx == -1, $"combo advances one step at a time (now {sword.AttackIndex})"); lastIdx = sword.AttackIndex; }
                    maxIndex = Mathf.Max(maxIndex, sword.AttackIndex);
                    yield return null; t += Time.deltaTime;
                    var hp = player.transform.InverseTransformPoint(hips.position); maxHipJump = Mathf.Max(maxHipJump, (hp - lastHip).magnitude); lastHip = hp;
                    if (t > 0.3f + 0.6f * shots.Count && shots.Count < 3) { Shot("03_attack_" + shots.Count); shots.Add(shots.Count); }
                }
                Check(stepsSeen == sword.Attacks.Length, $"6. buffered combo plays all {sword.Attacks.Length} steps once under spam (saw {stepsSeen})");
                Check(sword.State == SwordCombat.SwordState.Equipped, $"7. attacks return to equipped stance ({t:0.00}s total)");
                float drift = Flat(rootBefore, player.transform.position);
                Check(drift < 0.05f, $"9. no root drift/teleport during attacks ({drift:0.000} m)");
                Check(maxHipJump < 0.12f, $"9. no severe pose snapping (max hip jump per frame {maxHipJump:0.000} m)");
                yield return Frames(0.4f);
                Shot("04_recovered_stance");

                // ---- equipped locomotion, attack while moving, jump while equipped
                p0 = player.transform.position; yield return Frames(1.5f, Vector2.up);
                Check(Flat(p0, player.transform.position) / 1.5f > 1.2f, "2. walking still works with sword drawn");
                Shot("05_walk_equipped");
                yield return Frames(0.8f, Vector2.up, true);
                Shot("06_run_equipped");
                p0 = player.transform.position; yield return Frames(1.2f, Vector2.up, true);
                Check(Flat(p0, player.transform.position) / 1.2f > 3.5f, "2. running still works with sword drawn");
                Check(anim.GetLayerWeight(anim.GetLayerIndex("Sword Lower")) < 0.05f, "legs use locomotion while moving (Sword Lower weight 0)");
                sword.Attack(); p0 = player.transform.position; yield return Frames(0.4f, Vector2.up);
                float atkMove = Flat(p0, player.transform.position) / 0.4f;
                Check(atkMove > 0.2f && atkMove < 2.0f, $"movement stays responsive during an attack, slowed ({atkMove:0.00} m/s)");
                Shot("07_attack_while_moving");
                t = 0; while (sword.State != SwordCombat.SwordState.Equipped && t < 4f) { yield return null; t += Time.deltaTime; }
                Check(Mathf.Approximately(tpc.MoveSpeed, 2.0f) || tpc.MoveSpeed > 1.9f, $"move speed restored after attack ({tpc.MoveSpeed})");
                yield return Frames(0.5f);
                yield return Jump("3. jumping works with sword drawn");
                yield return Frames(0.8f);

                // ---- 5 sheathe (+ spam)
                Check(sword.ToggleSword(), "5. sheathe accepted");
                spamAccepted = 0; t = 0;
                while (sword.State == SwordCombat.SwordState.Sheathing && t < 2f) { if (sword.ToggleSword()) spamAccepted++; if (sword.Attack()) spamAccepted++; yield return null; t += Time.deltaTime; }
                Check(spamAccepted == 0, $"sheathe cannot be interrupted ({spamAccepted} accepted)");
                Check(sword.State == SwordCombat.SwordState.Sheathed && sword.Sword.parent == sword.SheathSocket, $"5. sheathe completes, sword back in saya ({t:0.00}s)");
                Check(!sword.Attack(), "attack ignored while sheathed");
                yield return Frames(0.5f);
                Check(anim.GetLayerWeight(anim.GetLayerIndex("Sword Upper")) == 0f, "sword layers fully off after sheathe");
                Shot("08_sheathed_again");
                p0 = player.transform.position; yield return Frames(1.5f, Vector2.up);
                Check(Flat(p0, player.transform.position) / 1.5f > 1.2f, "2. walking works after sheathe");

                Check(errors.Count == 0, "10. no Console errors/exceptions during play (" + errors.Count + ")");
                foreach (var e in errors) log.AppendLine("   " + e);
            }
            finally
            {
                Application.logMessageReceived -= OnLog;
                File.WriteAllText(Path.Combine(OutDir, "playmode_results.txt"), log.ToString());
                Debug.Log("[SwordPlayTests]\n" + log);
            }
            Assert.That(fails, Is.EqualTo(0), log.ToString());
        }

        IEnumerator Jump(string label)
        {
            float y0 = player.transform.position.y, peak = y0; bool left = false;
            input.jump = true; float t = 0;
            while (t < 2.5f) { yield return null; t += Time.deltaTime; peak = Mathf.Max(peak, player.transform.position.y); if (!tpc.Grounded) left = true; if (left && tpc.Grounded && t > 0.3f) break; }
            input.jump = false;
            Check(left && peak - y0 > 0.8f && tpc.Grounded, $"{label} (rose {peak - y0:0.00} m, landed {tpc.Grounded})");
        }
    }
}
#endif
