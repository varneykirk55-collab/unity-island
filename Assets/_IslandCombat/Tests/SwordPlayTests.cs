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
        readonly StringBuilder log = new StringBuilder(), trace = new StringBuilder();
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

        void Shot(string name, float side = 1f, Transform focus = null, float dist = 2.8f)
        {
            var p = player.transform;
            var target = focus ? focus.position : p.position + Vector3.up * 1.1f;
            shotCam.transform.position = target + (p.forward * 2.4f + p.right * 1.4f * side + Vector3.up * 0.25f).normalized * dist;
            shotCam.transform.LookAt(target);
            var rt = new RenderTexture(960, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            shotCam.targetTexture = rt; shotCam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(960, 720, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 960, 720), 0, 0); tex.Apply();
            RenderTexture.active = null; shotCam.targetTexture = null; rt.Release();
            File.WriteAllBytes(Path.Combine(OutDir, shotPrefix + name + ".png"), tex.EncodeToPNG());
        }
        string shotPrefix = "";

        float Flat(Vector3 a, Vector3 b) { a.y = 0; b.y = 0; return Vector3.Distance(a, b); }
        string Layers()
        {
            var sb = new StringBuilder();
            for (int l = 0; l < anim.layerCount; l++)
            {
                var cur = anim.GetCurrentAnimatorStateInfo(l);
                string name = "?"; foreach (var n in new[] { "Empty", "Stance", "Draw", "Sheathe", "Attack1", "Attack2", "Attack3", "Idle Walk Run Blend", "JumpStart", "InAir", "JumpLand" }) if (cur.IsName(n)) name = n;
                sb.Append($"[{anim.GetLayerName(l)} w{anim.GetLayerWeight(l):0.00} {name}@{cur.normalizedTime:0.000}{(anim.IsInTransition(l) ? " ->next@" + anim.GetNextAnimatorStateInfo(l).normalizedTime.ToString("0.000") : "")}] ");
            }
            return sb.ToString();
        }

        // On the frame the sword changes parent both sockets come from the same pose, so their difference is the visible snap.
        float HandOffGap()
        {
            var tsuka = new Vector3(0, 0, -0.13f); var tip = new Vector3(0, 0, 0.6f);
            Vector3 P(Transform s, Vector3 p) => s.TransformPoint(p);
            return Mathf.Max(Vector3.Distance(P(sword.HandSocket, tsuka), P(sword.SheathSocket, tsuka)), 0.5f * Vector3.Distance(P(sword.HandSocket, tip), P(sword.SheathSocket, tip)));
        }

        void LogHandOff(float t)
        {
            var tsuka = new Vector3(0, 0, -0.13f);
            log.AppendLine($"   hand-off to {sword.Sword.parent.name} at t={t:0.000}s: hilt gap {Vector3.Distance(sword.HandSocket.TransformPoint(tsuka), sword.SheathSocket.TransformPoint(tsuka)):0.000} m, " +
                $"angle {Quaternion.Angle(sword.HandSocket.rotation, sword.SheathSocket.rotation):0}° | {Layers()}");
        }

        // middle of the sword's handle, in player space
        Vector3 Tsuka() => player.transform.InverseTransformPoint(sword.Sword.TransformPoint(new Vector3(0, 0, -0.13f)));

        /// <summary>Fast, low-memory run in a flat scene with the identical player + sword setup.</summary>
        [UnityTest, Timeout(600000)]
        public IEnumerator SwordIntegration_TestScene() => Run("Assets/_IslandCombat/Tests/SwordTestScene.unity", "test");

        /// <summary>Full island scene (~8 GB of assets; needs plenty of free RAM). Run explicitly.</summary>
        [UnityTest, Explicit, Timeout(1800000)]
        public IEnumerator SwordIntegration_Island() => Run(ScenePath, "island");

        IEnumerator Run(string scenePath, string tag)
        {
            Directory.CreateDirectory(OutDir);
            foreach (var f in Directory.GetFiles(OutDir, tag + "_*.png")) File.Delete(f);
            shotPrefix = tag + "_";
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(scenePath, new LoadSceneParameters(LoadSceneMode.Single));
            for (int i = 0; i < 10; i++) yield return null;
            Application.logMessageReceived += OnLog;
            try
            {
                log.AppendLine("scene: " + scenePath);
                player = GameObject.Find("Player_Setup/PlayerArmature");
                Check(player != null, "1. character spawns (Player_Setup/PlayerArmature)");
                if (player == null) yield break;
                cc = player.GetComponent<CharacterController>(); input = player.GetComponent<StarterAssetsInputs>();
                tpc = player.GetComponent<ThirdPersonController>(); sword = player.GetComponent<SwordCombat>(); anim = player.GetComponent<Animator>();
                // Batchmode renders no camera, so 'Cull Update Transforms' would freeze the bones between screenshots and
                // async shader compilation would leave fresh shaders undrawn. Test-only overrides; the game keeps its settings.
                anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                UnityEditor.ShaderUtil.allowAsyncCompilation = false;

                // diagnostics: are frames / scene scripts / animator actually advancing?
                int f0 = Time.frameCount; float rt0 = Time.realtimeSinceStartup, gt0 = Time.time; var pos0 = player.transform.position;
                var hipsT = anim.GetBoneTransform(HumanBodyBones.Hips); var hip0 = hipsT.localRotation;
                yield return new WaitForSeconds(1f);
                log.AppendLine($"diag: {Time.frameCount - f0} frames in {Time.time - gt0:0.00}s game / {Time.realtimeSinceStartup - rt0:0.00}s real, timeScale {Time.timeScale}, " +
                    $"player active {player.activeInHierarchy}, tpc {tpc.enabled}, cc {cc.enabled}, animator {anim.enabled} ctrl '{anim.runtimeAnimatorController?.name}' cull {anim.cullingMode}, " +
                    $"fell {pos0.y - player.transform.position.y:0.000} m, hips rotated {Quaternion.Angle(hip0, hipsT.localRotation):0.0} deg, grounded {tpc.Grounded}");
                var pi = player.GetComponent("PlayerInput") as Behaviour; if (pi) pi.enabled = false;
                cam = Camera.main; var brain = cam.GetComponent("CinemachineBrain") as Behaviour; if (brain) brain.enabled = false;
                shotCam = new GameObject("ShotCam").AddComponent<Camera>(); shotCam.enabled = false; shotCam.nearClipPlane = 0.05f; shotCam.fieldOfView = 40;
                hand = anim.GetBoneTransform(HumanBodyBones.RightHand); hips = anim.GetBoneTransform(HumanBodyBones.Hips);
                heading = Vector3.ProjectOnPlane(player.transform.forward, Vector3.up).normalized;
                Check(sword != null && sword.enabled, "SwordCombat present and enabled (animator layers found)");

                yield return Frames(1.5f);
                Check(tpc.Grounded, "1. grounded after spawn");
                Check(sword.State == SwordCombat.SwordState.Sheathed && sword.Sword.parent == sword.SheathSocket, "sword starts sheathed in the saya");
                Shot("01_sheathed_idle"); Shot("01b_sheathed_back", -2.5f);

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
                int spamAccepted = 0; float t = 0; float maxSwordJump = 0; var lastParent = sword.Sword.parent; var drawShots = new HashSet<int>();
                while (sword.State == SwordCombat.SwordState.Drawing && t < 6f)
                {
                    if (sword.ToggleSword()) spamAccepted++; if (sword.Attack()) spamAccepted++;
                    yield return null; t += Time.deltaTime;
                    if (sword.Sword.parent != lastParent) { maxSwordJump = Mathf.Max(maxSwordJump, HandOffGap()); lastParent = sword.Sword.parent; LogHandOff(t); }
                    for (int k = 0; k < 3; k++) { float at = sword.DrawAttachTime + (k - 1) * 0.25f; if (!drawShots.Contains(k) && t >= at) { drawShots.Add(k); Shot("02e_draw_" + k); } }
                }
                Check(spamAccepted == 0, $"draw cannot be interrupted by spammed draw/sheathe/attack ({spamAccepted} accepted)");
                Check(maxSwordJump < 0.12f, $"sword does not visibly jump when it leaves the saya (hand-off gap {maxSwordJump:0.000} m)");
                Check(sword.State == SwordCombat.SwordState.Equipped, $"4. draw completes into equipped stance ({t:0.00}s)");
                Check(sword.Sword.parent == sword.HandSocket, "4. sword moved to the right-hand socket");
                yield return Frames(0.6f);
                Shot("02_equipped_idle"); Shot("02b_equipped_idle_other_side", -1f);
                Shot("02c_hand_closeup", 1f, hand, 0.7f); Shot("02d_hand_closeup_other_side", -1f, hand, 0.7f);
                float handGap = Vector3.Distance(sword.Sword.TransformPoint(new Vector3(0, 0, -0.13f)), hand.position);
                Check(handGap < 0.15f, $"8. sword handle is in the hand (tsuka centre {handGap:0.000} m from wrist bone)");

                // ---- 6/7/9 attacks with spammed input, no root drift
                var rootBefore = player.transform.position; float maxHipJump = 0; Vector3 lastHip = player.transform.InverseTransformPoint(hips.position);
                Check(sword.Attack(), "6. attack accepted when equipped");
                int maxIndex = 0, stepsSeen = 0, lastIdx = -1; t = 0; var shots = new HashSet<int>(); float prevDt = 1f;
                while (sword.State == SwordCombat.SwordState.Attacking && t < 6f)
                {
                    for (int k = 0; k < 5; k++) sword.Attack();      // hammer the button
                    if (sword.AttackIndex != lastIdx) { stepsSeen++; Check(sword.AttackIndex == lastIdx + 1 || lastIdx == -1, $"combo advances one step at a time (now {sword.AttackIndex})"); lastIdx = sword.AttackIndex; }
                    maxIndex = Mathf.Max(maxIndex, sword.AttackIndex);
                    yield return null; t += Time.deltaTime;
                    var hp = player.transform.InverseTransformPoint(hips.position); float jump = (hp - lastHip).magnitude;
                    if (sword.AttackIndex == 0 && t < 0.4f && (Time.frameCount % 15 == 0 || jump > 0.05f))
                    {
                        var li = anim.GetLayerIndex("Sword Lower"); var cs = anim.GetCurrentAnimatorStateInfo(li);
                        trace.AppendLine($"     trace t={t:0.000} hipY={hp.y:0.000} lower {(cs.IsName("Attack1") ? "Attack1" : cs.IsName("Stance") ? "Stance" : "?")}@{cs.normalizedTime * cs.length:0.000}s len {cs.length:0.000} trans {anim.IsInTransition(li)} w {anim.GetLayerWeight(li):0.00}");
                    }
                    if (jump > 0.08f) log.AppendLine($"   hip jump {jump:0.000} m at t={t:0.000}s dt={Time.deltaTime:0.0000} frame {Time.frameCount} attack {sword.AttackIndex} state {sword.State} from {lastHip:F3} to {hp:F3} | {Layers()}");
                    // a screenshot stalls one frame (~0.3 s of animation) and the pose lands a frame later: skip both frames
                    if (Time.deltaTime < 0.02f && prevDt < 0.02f) maxHipJump = Mathf.Max(maxHipJump, jump);
                    prevDt = Time.deltaTime; lastHip = hp;
                    if (t > 0.3f + 0.6f * shots.Count && shots.Count < 3) { Shot("03_attack_" + shots.Count); shots.Add(shots.Count); }
                }
                Check(stepsSeen == sword.Attacks.Length, $"6. buffered combo plays all {sword.Attacks.Length} steps once under spam (saw {stepsSeen})");
                Check(sword.State == SwordCombat.SwordState.Equipped, $"7. attacks return to equipped stance ({t:0.00}s total)");
                float drift = Flat(rootBefore, player.transform.position);
                Check(drift < 0.05f, $"9. no root drift/teleport during attacks ({drift:0.000} m)");
                Check(maxHipJump < 0.12f, $"9. no severe pose snapping (max hip jump per frame {maxHipJump:0.000} m)");
                log.Append(trace);
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
                spamAccepted = 0; t = 0; maxSwordJump = 0; lastParent = sword.Sword.parent; var sheatheShots = new HashSet<int>();
                while (sword.State == SwordCombat.SwordState.Sheathing && t < 6f)
                {
                    if (sword.ToggleSword()) spamAccepted++; if (sword.Attack()) spamAccepted++;
                    yield return null; t += Time.deltaTime;
                    if (sword.Sword.parent != lastParent) { maxSwordJump = Mathf.Max(maxSwordJump, HandOffGap()); lastParent = sword.Sword.parent; LogHandOff(t); }
                    for (int k = 0; k < 3; k++) { float at = sword.SheatheAttachTime + (k - 1) * 0.3f; if (!sheatheShots.Contains(k) && t >= at) { sheatheShots.Add(k); Shot("07e_sheathe_" + k); } }
                }
                Check(spamAccepted == 0, $"sheathe cannot be interrupted ({spamAccepted} accepted)");
                Check(maxSwordJump < 0.12f, $"sword does not visibly jump when it returns to the saya (hand-off gap {maxSwordJump:0.000} m)");
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
                File.WriteAllText(Path.Combine(OutDir, "playmode_results_" + tag + ".txt"), log.ToString());
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
