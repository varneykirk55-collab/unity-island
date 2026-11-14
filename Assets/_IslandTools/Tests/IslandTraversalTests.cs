// Play Mode tests driving the real Starter Assets ThirdPersonController through Scene 1.
// Input is simulated by writing StarterAssetsInputs (what a gamepad stick would set); the controller,
// its CharacterController capsule and physics are untouched. Results -> _Backups/tests/*.txt
#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using StarterAssets;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace IslandTools.Tests
{
    public class IslandTraversalTests
    {
        const string Scene1Path = "Assets/Free Island Collection/Scenes/Scene 1.unity";
        const string OutDir = "_Backups/tests";
        GameObject player; CharacterController cc; StarterAssetsInputs input; ThirdPersonController tpc; Camera cam;

        IEnumerator Load()
        {
            Directory.CreateDirectory(OutDir);
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(Scene1Path, new LoadSceneParameters(LoadSceneMode.Single));
            for (int i = 0; i < 10; i++) yield return null;
            player = GameObject.Find("Player_Setup/PlayerArmature");
            cc = player.GetComponent<CharacterController>();
            input = player.GetComponent<StarterAssetsInputs>();
            tpc = player.GetComponent<ThirdPersonController>();
            var pi = player.GetComponent("PlayerInput") as Behaviour; if (pi) pi.enabled = false;     // no device events overwrite our stick values
            cam = Camera.main;
            var brain = cam.GetComponent("CinemachineBrain") as Behaviour; if (brain) brain.enabled = false; // camera yaw = desired heading
        }

        IEnumerator Teleport(Vector3 pos, Vector3 heading)
        {
            cc.enabled = false; player.transform.position = pos; player.transform.rotation = Quaternion.LookRotation(heading); cc.enabled = true;
            input.move = Vector2.zero; input.jump = false; input.sprint = false;
            for (int i = 0; i < 20; i++) { SetHeading(heading); yield return new WaitForFixedUpdate(); }
        }
        void SetHeading(Vector3 h) { cam.transform.rotation = Quaternion.LookRotation(new Vector3(h.x, 0, h.z)); }

        // ------------------------------------------------------------------ doorways
        [UnityTest, Timeout(1800000)]
        public IEnumerator Doorways_TraversableBothWays()
        {
            yield return Load();
            var csv = "_Backups/doorfix_last/doorways.csv";
            Assume.That(File.Exists(csv), "doorways.csv missing (run IslandDoorFix first)");
            var sb = new StringBuilder(); int pass = 0, total = 0;
            foreach (var line in File.ReadAllLines(csv).Skip(1))
            {
                var c = line.Split(',');
                var centre = new Vector3(float.Parse(c[2]), float.Parse(c[3]), float.Parse(c[4]));
                var through = new Vector3(float.Parse(c[5]), 0, float.Parse(c[6])).normalized;
                foreach (var dir in new[] { 1f, -1f })
                {
                    var d = through * dir;
                    var start = centre - d * 2.5f;
                    // stand on whatever is below the start point
                    // cast from just above the opening's floor level so we never land on a roof or eave
                    if (Physics.Raycast(new Vector3(start.x, centre.y + 1.2f, start.z), Vector3.down, out var g, 6f, ~0, QueryTriggerInteraction.Ignore)) start.y = g.point.y + 0.05f;
                    yield return Teleport(start, d);
                    float t = 0, best = -9; bool stuckInDoor = false;
                    while (t < 6f)
                    {
                        SetHeading(d); input.move = new Vector2(0, 1);
                        yield return null; t += Time.deltaTime;
                        best = Mathf.Max(best, Vector3.Dot(player.transform.position - centre, d));
                        if (best > 1.5f) break;
                    }
                    input.move = Vector2.zero;
                    float along = Vector3.Dot(player.transform.position - centre, d);
                    stuckInDoor = along > -0.6f && along < 0.6f;
                    bool ok = best > 1.5f; total++; if (ok) pass++;
                    sb.AppendLine(string.Format("{0},{1},dir{2},{3},reached {4:0.00} m past opening{5}", c[0], c[1], dir > 0 ? "+" : "-", ok ? "PASS" : "FAIL", best, ok ? "" : (stuckInDoor ? " (STUCK inside doorway)" : " (stopped before doorway)")));
                }
            }
            sb.Insert(0, "doorway traversals passed " + pass + " / " + total + "\n");
            File.WriteAllText(Path.Combine(OutDir, "doorways.txt"), sb.ToString());
            Debug.Log(sb.ToString());
        }

        // ------------------------------------------------------------------ walls: repeated jumping against solid walls
        [UnityTest, Timeout(1800000)]
        public IEnumerator Walls_CannotBeJumpClimbed()
        {
            yield return Load();
            var walls = Object.FindObjectsOfType<Collider>()
                .Where(c => c.transform.IsChildOf(GameObject.Find("Island_Environment/Korean_Buildings").transform))
                .Where(c => System.Text.RegularExpressions.Regex.IsMatch(c.name, "StoneWall|Stonewall|StoneFence|_Wall"))
                .Where(c => c.bounds.size.y > 1.6f && c.bounds.size.y < 3.5f && Mathf.Max(c.bounds.size.x, c.bounds.size.z) > 2f && Mathf.Min(c.bounds.size.x, c.bounds.size.z) < 0.9f)
                .ToList();
            // realistic pacing: 60 fps, like a real game session
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = 60;
            var rnd = new System.Random(7);
            var shuffled = walls.OrderBy(_ => rnd.Next()).ToList();
            var sb = new StringBuilder();
            float originalRadius = tpc.GroundedRadius;
            var results = new Dictionary<float, int>(); var tested = 0;
            int validWalls = 0;
            foreach (var w in shuffled)
            {
                if (validWalls >= 12) break;
                var b = w.bounds; bool alongX = b.size.x > b.size.z;
                var normal = alongX ? Vector3.forward : Vector3.right;
                // a start spot 1 m from the wall face with ground near the wall base and nothing overhead
                Vector3 start = Vector3.zero; bool found = false;
                foreach (var s in new[] { 1f, -1f })
                {
                    var p = b.center + normal * s * (Mathf.Min(b.extents.x, b.extents.z) + 1.0f);
                    if (Physics.Raycast(new Vector3(p.x, b.min.y + 1.5f, p.z), Vector3.down, out var gh, 3f, ~0, QueryTriggerInteraction.Ignore)
                        && gh.point.y < b.min.y + 0.4f && gh.point.y > b.min.y - 0.6f
                        && !Physics.CheckCapsule(gh.point + Vector3.up * 0.35f, gh.point + Vector3.up * 1.6f, 0.3f, ~0, QueryTriggerInteraction.Ignore))
                    { start = gh.point + Vector3.up * 0.05f; normal *= -s; found = true; break; }
                }
                if (!found) continue;
                validWalls++;
                var line = string.Format("{0}/{1} (height {2:0.00} m):", GetBuilding(w.transform), w.name, b.size.y);
                foreach (var radius in new[] { originalRadius, 0.20f })
                {
                    tpc.GroundedRadius = radius;
                    yield return Teleport(start, normal);
                    float startY = player.transform.position.y, maxY = startY, t = 0; int takeoffs = 0; bool wasGrounded = true;
                    while (t < 8f)
                    {
                        SetHeading(normal); input.move = new Vector2(0, 1); input.jump = true;   // hold jump, push into the wall
                        yield return null; t += Time.deltaTime;
                        if (wasGrounded && !tpc.Grounded) takeoffs++;
                        wasGrounded = tpc.Grounded;
                        maxY = Mathf.Max(maxY, player.transform.position.y);
                    }
                    input.move = Vector2.zero; input.jump = false;
                    float crossed = Vector3.Dot(player.transform.position - b.center, normal);
                    bool over = maxY - startY > b.size.y - 0.15f || crossed > 0.3f;
                    results.TryGetValue(radius, out var n); results[radius] = n + (over ? 1 : 0);
                    line += string.Format("  r={0:0.00}: takeoffs {1}, max rise {2:0.00} m, {3}", radius, takeoffs, maxY - startY, over ? "GOT OVER/ONTO WALL" : "held");
                }
                tested++;
                sb.AppendLine(line);
            }
            tpc.GroundedRadius = originalRadius;
            sb.Insert(0, string.Format("walls tested {0} (normal jump apex 1.20 m)\n  climbed with GroundedRadius {1:0.00} (current): {2}\n  climbed with GroundedRadius 0.20: {3}\n",
                tested, originalRadius, results.TryGetValue(originalRadius, out var a) ? a : 0, results.TryGetValue(0.20f, out var bb) ? bb : 0));
            File.WriteAllText(Path.Combine(OutDir, "walls.txt"), sb.ToString());
            Debug.Log(sb.ToString());
        }

        static string GetBuilding(Transform t) { while (t.parent != null && t.parent.name != "Korean_Buildings") t = t.parent; return t.name; }
    }
}
#endif
