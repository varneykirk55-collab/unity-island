// Editor-only. Builds the sword Animator controller (a copy of the Starter Assets controller + two sword layers)
// and sets up the player in the tracked scene copy. Nothing in Starter Assets or the Asset Store packs is modified.
// Batchmode: Unity.exe -batchmode -quit -projectPath <proj> -executeMethod IslandCombat.Editor.SwordSetup.Run
using System;
using System.IO;
using System.Linq;
using IslandCombat;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace IslandCombat.Editor
{
    public static class SwordSetup
    {
        // existing assets (read only)
        public const string ArmaturePrefab = "Assets/Starter Assets/Runtime/ThirdPersonController/Prefabs/PlayerArmature.prefab";
        const string StarterController = "Assets/Starter Assets/Runtime/ThirdPersonController/Character/Animations/StarterAssetsThirdPerson.controller";
        const string ClipDir = "Assets/EEJANAI_Team/FreeSwordAnimations/Animations/";
        public const string KatanaPrefab = "Assets/JTS_TheSamuraiSword/Prefabs/Katana_Generic01.prefab";
        public const string SayaPrefab = "Assets/JTS_TheSamuraiSword/Prefabs/Saya_KatanaGeneric01.prefab";
        const string SourceScene = "Assets/Free Island Collection/Scenes/Scene 1.unity";

        // assets created by this tool
        const string Dir = "Assets/_IslandCombat/Animation/";
        const string ControllerPath = Dir + "PlayerArmature_Sword.controller";
        const string UpperMaskPath = Dir + "SwordUpper.mask";
        const string LowerMaskPath = Dir + "SwordLower.mask";
        public const string TargetScene = "Assets/_IslandScenes/Island_Main.unity";

        // clip choices
        const string StanceClip = "battle stance";
        static readonly string[] AttackClips = { "slash1", "slash2", "slash3" };

        // sword fit (local to the bones; tuned against the geometry report + screenshots)
        // Katana mesh: blade tip +Z, tsuka (handle) from z=-0.26 to the tsuba at z~0, blade plane = YZ.
        // Robot Right_Hand: fingers -Y, thumb side -Z, palm normal +X (from the geometry report).
        // => handle across the palm along hand Z, blade out of the thumb side, edge toward the fingers;
        //    sword origin placed so the middle of the tsuka (z=-0.13) sits in the palm.
        public static Vector3 GripPos = new Vector3(0.03f, -0.085f, -0.13f);
        public static Vector3 GripEuler = new Vector3(0f, 180f, 0f);
        // Scabbard mouth at the front of the left hip, tip pointing back/down, edge up (worn katana style).
        public static Vector3 SayaMountPos = new Vector3(-0.17f, 0.0f, 0.12f);
        public static Vector3 SayaMountEuler = Quaternion.LookRotation(new Vector3(-0.1f, -0.35f, -1f), Vector3.down).eulerAngles;
        // The saya shares the katana's origin/axis (blade 0..0.71 m sits inside the saya 0.01..0.75 m).
        public static Vector3 SheathedSwordPos = Vector3.zero;
        public static Vector3 SheathedSwordEuler = Vector3.zero;

        [MenuItem("Tools/Island/Sword/Setup")]
        public static void Run()
        {
            var ctrl = BuildController();
            SetupScene(ctrl);
        }

        // ------------------------------------------------------------------ animator

        static AnimationClip Clip(string name)
        {
            var c = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipDir + name + ".anim");
            if (c == null) throw new Exception("Missing sword clip: " + ClipDir + name + ".anim");
            return c;
        }

        static AvatarMask Mask(string path, params AvatarMaskBodyPart[] on)
        {
            AssetDatabase.DeleteAsset(path);
            var m = new AvatarMask();
            for (var p = AvatarMaskBodyPart.Root; p < AvatarMaskBodyPart.LastBodyPart; p++) m.SetHumanoidBodyPartActive(p, on.Contains(p));
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        public static AnimatorController BuildController()
        {
            Directory.CreateDirectory(Dir);
            AssetDatabase.DeleteAsset(ControllerPath);
            if (!AssetDatabase.CopyAsset(StarterController, ControllerPath)) throw new Exception("copy controller failed");
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);

            // Upper: arms + head only, so the torso keeps the run/walk motion while the arms hold the sword.
            var upper = Mask(UpperMaskPath, AvatarMaskBodyPart.Head, AvatarMaskBodyPart.LeftArm, AvatarMaskBodyPart.RightArm,
                AvatarMaskBodyPart.LeftFingers, AvatarMaskBodyPart.RightFingers, AvatarMaskBodyPart.LeftHandIK, AvatarMaskBodyPart.RightHandIK);
            // Lower: torso + legs; only weighted in while standing, giving the full-body stance and full-body attacks.
            var lower = Mask(LowerMaskPath, AvatarMaskBodyPart.Root, AvatarMaskBodyPart.Body, AvatarMaskBodyPart.LeftLeg, AvatarMaskBodyPart.RightLeg,
                AvatarMaskBodyPart.LeftFootIK, AvatarMaskBodyPart.RightFootIK);

            AddSwordLayer(ctrl, "Sword Upper", upper);
            AddSwordLayer(ctrl, "Sword Lower", lower);
            EditorUtility.SetDirty(ctrl);
            AssetDatabase.SaveAssets();
            return ctrl;
        }

        static void AddSwordLayer(AnimatorController ctrl, string name, AvatarMask mask)
        {
            var sm = new AnimatorStateMachine { name = name, hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(sm, ctrl);
            var empty = sm.AddState("Empty", new Vector3(250, 0));
            sm.defaultState = empty;
            sm.AddState("Stance", new Vector3(250, 80)).motion = Clip(StanceClip);
            for (int i = 0; i < AttackClips.Length; i++)
                sm.AddState("Attack" + (i + 1), new Vector3(500, 80 * i)).motion = Clip(AttackClips[i]);
            foreach (var s in sm.states) s.state.writeDefaultValues = true;
            ctrl.AddLayer(new AnimatorControllerLayer
            {
                name = name, stateMachine = sm, avatarMask = mask, defaultWeight = 0f, blendingMode = AnimatorLayerBlendingMode.Override,
            });
        }

        // ------------------------------------------------------------------ scene

        public static void SetupScene(AnimatorController ctrl)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(TargetScene));
            if (!File.Exists(TargetScene) && !AssetDatabase.CopyAsset(SourceScene, TargetScene)) throw new Exception("scene copy failed");
            var scene = EditorSceneManager.OpenScene(TargetScene, OpenSceneMode.Single);

            var player = GameObject.Find("Player_Setup/PlayerArmature") ?? throw new Exception("PlayerArmature not found in " + TargetScene);
            var anim = player.GetComponent<Animator>();
            anim.runtimeAnimatorController = ctrl;     // instance override; the prefab keeps the Starter controller

            var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
            var hips = anim.GetBoneTransform(HumanBodyBones.Hips);

            // remove any previous run of this tool
            foreach (var n in new[] { "SwordHandSocket", "SwordSayaMount" })
                foreach (var t in player.GetComponentsInChildren<Transform>(true).Where(t => t.name == n).ToArray())
                    UnityEngine.Object.DestroyImmediate(t.gameObject);

            var handSocket = Child(hand, "SwordHandSocket", GripPos, GripEuler);
            var sayaMount = Child(hips, "SwordSayaMount", SayaMountPos, SayaMountEuler);
            var saya = Place(SayaPrefab, sayaMount, "Saya");
            var sheathSocket = Child(sayaMount, "SwordSheathSocket", SheathedSwordPos, SheathedSwordEuler);
            var sword = Place(KatanaPrefab, sheathSocket, "Katana");

            var audio = player.GetComponent<SwordAudio>() ?? player.AddComponent<SwordAudio>();
            var combat = player.GetComponent<SwordCombat>() ?? player.AddComponent<SwordCombat>();
            combat.Sword = sword.transform;
            combat.HandSocket = handSocket;
            combat.SheathSocket = sheathSocket;
            combat.Attacks = AttackClips.Select((c, i) => new SwordCombat.AttackStep { State = "Attack" + (i + 1), Length = Clip(c).length }).ToArray();
            EditorUtility.SetDirty(combat); EditorUtility.SetDirty(audio);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new Exception("save failed");
            Debug.Log("[SwordSetup] " + TargetScene + " saved; attacks: " + string.Join(", ", combat.Attacks.Select(a => a.State + "=" + a.Length.ToString("0.00") + "s")));
        }

        static Transform Child(Transform parent, string name, Vector3 pos, Vector3 euler)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = pos; t.localEulerAngles = euler; t.localScale = Vector3.one;
            return t;
        }

        // Prop instance with physics stripped (a Rigidbody/collider on the player's sword would fight the CharacterController).
        static GameObject Place(string prefabPath, Transform parent, string name)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) ?? throw new Exception("Missing prefab " + prefabPath);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src, parent);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
            go.name = name;
            go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity;
            foreach (var j in go.GetComponentsInChildren<Joint>(true)) UnityEngine.Object.DestroyImmediate(j);
            foreach (var r in go.GetComponentsInChildren<Rigidbody>(true)) UnityEngine.Object.DestroyImmediate(r);
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(c);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return go;
        }
    }
}
