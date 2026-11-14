// Editor-only diagnostics used while fitting the sword: mesh axes/bounds of the katana + scabbard prefabs and the
// orientation of the player's hand/hip bones. Batchmode: -executeMethod IslandCombat.Editor.SwordReport.Run
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace IslandCombat.Editor
{
    public static class SwordReport
    {
        [MenuItem("Tools/Island/Sword/Report Geometry")]
        public static void Run()
        {
            var sb = new StringBuilder();
            foreach (var p in new[] { SwordSetup.KatanaPrefab, SwordSetup.SayaPrefab })
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                sb.AppendLine("== " + p + (go ? "" : "  (MISSING)"));
                if (!go) continue;
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(go);
                inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                foreach (var t in inst.GetComponentsInChildren<Transform>(true))
                {
                    var comps = string.Join(",", t.GetComponents<Component>().Where(c => !(c is Transform)).Select(c => c.GetType().Name));
                    sb.AppendLine($"  {Path(t, inst.transform)}  lp={t.localPosition:F3} le={t.localEulerAngles:F1} ls={t.localScale:F3}  [{comps}]");
                    var mf = t.GetComponent<MeshFilter>();
                    if (mf && mf.sharedMesh)
                    {
                        var r = t.GetComponent<Renderer>();
                        sb.AppendLine($"     mesh {mf.sharedMesh.name} localBounds c={mf.sharedMesh.bounds.center:F3} size={mf.sharedMesh.bounds.size:F3}  worldBounds c={r.bounds.center:F3} size={r.bounds.size:F3}");
                        // which end is the hilt? report vertex extent along the longest world axis split by thickness
                        var v = mf.sharedMesh.vertices.Select(x => t.TransformPoint(x)).ToArray();
                        var b = r.bounds; int ax = b.size.x > b.size.y ? (b.size.x > b.size.z ? 0 : 2) : (b.size.y > b.size.z ? 1 : 2);
                        float lo = b.min[ax], hi = b.max[ax], span = hi - lo;
                        string Thick(float a, float c) { var s = v.Where(x => x[ax] >= a && x[ax] <= c).ToArray(); if (s.Length == 0) return "-"; var mn = s.Aggregate(Vector3.positiveInfinity, Vector3.Min); var mx = s.Aggregate(Vector3.negativeInfinity, Vector3.Max); return (mx - mn).ToString("F3"); }
                        sb.AppendLine($"     longest axis {"XYZ"[ax]} from {lo:F3} to {hi:F3}; extent of first 10%: {Thick(lo, lo + span * 0.1f)}  middle: {Thick(lo + span * 0.45f, lo + span * 0.55f)}  last 10%: {Thick(hi - span * 0.1f, hi)}");
                    }
                }
                Object.DestroyImmediate(inst);
            }

            var arm = AssetDatabase.LoadAssetAtPath<GameObject>(SwordSetup.ArmaturePrefab);
            var pi = (GameObject)PrefabUtility.InstantiatePrefab(arm);
            pi.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var an = pi.GetComponent<Animator>();
            sb.AppendLine("== PlayerArmature bones (bind pose, character facing +Z)");
            foreach (var hb in new[] { HumanBodyBones.Hips, HumanBodyBones.RightHand, HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightIndexProximal, HumanBodyBones.RightThumbProximal, HumanBodyBones.RightLittleProximal, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftHand, HumanBodyBones.Spine })
            {
                var t = an.GetBoneTransform(hb);
                if (!t) { sb.AppendLine($"  {hb}: none"); continue; }
                sb.AppendLine($"  {hb,-20} {t.name,-22} pos={t.position:F3}  right(x)={t.right:F2} up(y)={t.up:F2} fwd(z)={t.forward:F2} lossyScale={t.lossyScale:F2}");
            }
            Object.DestroyImmediate(pi);

            Directory.CreateDirectory("_Backups/sword");
            File.WriteAllText("_Backups/sword/report.txt", sb.ToString());
            Debug.Log(sb.ToString());
        }

        static string Path(Transform t, Transform root) => t == root ? t.name : Path(t.parent, root) + "/" + t.name;
    }
}
