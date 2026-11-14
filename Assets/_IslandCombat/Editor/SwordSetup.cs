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
        // Owner-supplied Mixamo clips (copied from OneDrive/Documents/Kengeki/assets; git-ignored)
        const string MixamoDir = Dir + "Mixamo/";
        const string DrawFbx = MixamoDir + "Withdrawing Sword.fbx";
        const string SheatheFbx = MixamoDir + "Sheathing Sword.fbx";

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

        public const string TestScene = "Assets/_IslandCombat/Tests/SwordTestScene.unity";

        [MenuItem("Tools/Island/Sword/Setup")]
        public static void Run()
        {
            var ctrl = BuildController();
            SetupScene(ctrl);
            BuildTestScene(ctrl);
        }

        /// <summary>Small flat scene with the same player + sword setup, for fast, low-memory Play Mode validation.</summary>
        [MenuItem("Tools/Island/Sword/Build Test Scene")]
        public static void BuildTestSceneOnly() => BuildTestScene(AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) ?? throw new Exception("Run Setup first"));

        public static void BuildTestScene(AnimatorController ctrl)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground"; ground.transform.localScale = new Vector3(10, 1, 10);
            var light = new GameObject("Sun").AddComponent<Light>();
            light.type = LightType.Directional; light.transform.rotation = Quaternion.Euler(50, -30, 0);
            var cam = new GameObject("Main Camera").AddComponent<Camera>();
            cam.tag = "MainCamera"; cam.transform.SetPositionAndRotation(new Vector3(0, 1.6f, -4f), Quaternion.identity);
            var root = new GameObject("Player_Setup");
            var player = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ArmaturePrefab), scene);
            player.transform.SetParent(root.transform, false);
            player.transform.position = new Vector3(0, 0.05f, 0);
            SetupPlayer(player, ctrl);
            if (!EditorSceneManager.SaveScene(scene, TestScene)) throw new Exception("test scene save failed");
            Debug.Log("[SwordSetup] " + TestScene + " saved");
        }

        // ------------------------------------------------------------------ animator

        // Derived copies of the vendor clips with corrected clip settings (the originals stay untouched).
        // Git-ignored: they are Asset Store content and are regenerated by this tool.
        const string GenClipDir = Dir + "Clips/";

        static AnimationClip Clip(string name) => Derived(name, name == StanceClip);

        static AnimationClip Derived(string name, bool loop)
        {
            var src = ClipDir + name + ".anim";
            if (AssetDatabase.LoadAssetAtPath<AnimationClip>(src) == null) throw new Exception("Missing sword clip: " + src);
            Directory.CreateDirectory(GenClipDir);
            var dst = GenClipDir + name + ".anim";
            if (AssetDatabase.LoadAssetAtPath<AnimationClip>(dst) == null && !AssetDatabase.CopyAsset(src, dst)) throw new Exception("copy clip failed: " + src);
            var c = AssetDatabase.LoadAssetAtPath<AnimationClip>(dst);
            var s = AnimationUtility.GetAnimationClipSettings(c);
            s.loopTime = loop;                 // slashes play once (vendor clips are all set to loop)
            s.loopBlendPositionXZ = false;     // XZ travel becomes root motion -> discarded (applyRootMotion is off): no lunge-and-snap-back
            s.keepOriginalPositionXZ = false;
            s.loopBlendPositionY = true;       // keep the clip's own crouch (RootT.y ~0.73-0.76) in the pose,
            s.keepOriginalPositionY = true;    // based on the original height ('Feet' sank the body ~1 m below the ground)
            s.heightFromFeet = false;
            s.loopBlendOrientation = true;     // body twist stays in the pose; recovery fade returns it
            s.keepOriginalOrientation = true;
            AnimationUtility.SetAnimationClipSettings(c, s);
            EditorUtility.SetDirty(c);
            return c;
        }

        static AvatarMask Mask(string path, params AvatarMaskBodyPart[] on)
        {
            var m = AssetDatabase.LoadAssetAtPath<AvatarMask>(path);
            bool created = m == null;
            if (created) m = new AvatarMask();
            for (var p = AvatarMaskBodyPart.Root; p < AvatarMaskBodyPart.LastBodyPart; p++) m.SetHumanoidBodyPartActive(p, on.Contains(p));
            if (created) AssetDatabase.CreateAsset(m, path); else EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Rebuilds only the sword layers/clips; the controller asset (and its GUID used by the scenes) is kept.</summary>
        [MenuItem("Tools/Island/Sword/Rebuild Animator")]
        public static void RebuildAnimator() { BuildController(); AssetDatabase.SaveAssets(); }

        public static AnimatorController BuildController()
        {
            Directory.CreateDirectory(Dir);
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (ctrl == null)
            {
                if (!AssetDatabase.CopyAsset(StarterController, ControllerPath)) throw new Exception("copy controller failed");
                ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            }
            for (int i = ctrl.layers.Length - 1; i >= 0; i--)
                if (ctrl.layers[i].name.StartsWith("Sword ")) ctrl.RemoveLayer(i);

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
            sm.AddState("Draw", new Vector3(0, 80)).motion = MixamoClip(DrawFbx, "Draw");
            sm.AddState("Sheathe", new Vector3(0, 160)).motion = MixamoClip(SheatheFbx, "Sheathe");
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
            var combat = SetupPlayer(player, ctrl);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new Exception("save failed");
            Debug.Log("[SwordSetup] " + TargetScene + " saved; attacks: " + string.Join(", ", combat.Attacks.Select(a => a.State + "=" + a.Length.ToString("0.00") + "s")));
        }

        static SwordCombat SetupPlayer(GameObject player, AnimatorController ctrl)
        {
            var anim = player.GetComponent<Animator>();
            anim.runtimeAnimatorController = ctrl;     // instance override; the prefab keeps the Starter controller

            var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
            var hips = anim.GetBoneTransform(HumanBodyBones.Hips);

            // remove any previous run of this tool
            foreach (var n in new[] { "SwordHandSocket", "SwordSayaMount" })
                foreach (var t in player.GetComponentsInChildren<Transform>(true).Where(t => t.name == n).ToArray())
                    UnityEngine.Object.DestroyImmediate(t.gameObject);

            var fit = GetFit();
            var handSocket = Child(hand, "SwordHandSocket", GripPos, GripEuler);
            var sayaMount = Child(hips, "SwordSayaMount", fit.MountPos, fit.MountRot.eulerAngles);
            var saya = Place(SayaPrefab, sayaMount, "Saya", LitMaterial("M_Saya_Generic01_URP", "tex_saya_generic01", 0.55f));
            var sheathSocket = Child(sayaMount, "SwordSheathSocket", SheathedSwordPos, SheathedSwordEuler);
            var sword = Place(KatanaPrefab, sheathSocket, "Katana", LitMaterial("M_Katana_Generic01_URP", "tex_sword_generic01", 0.85f));

            var audio = player.GetComponent<SwordAudio>(); if (audio == null) audio = player.AddComponent<SwordAudio>();
            var combat = player.GetComponent<SwordCombat>(); if (combat == null) combat = player.AddComponent<SwordCombat>();
            combat.Sword = sword.transform;
            combat.HandSocket = handSocket;
            combat.SheathSocket = sheathSocket;
            combat.Attacks = AttackClips.Select((c, i) => new SwordCombat.AttackStep { State = "Attack" + (i + 1), Length = Clip(c).length }).ToArray();
            combat.DrawState = "Draw"; combat.SheatheState = "Sheathe";
            combat.DrawAttachTime = fit.Grab; combat.DrawDuration = fit.DrawLength * 0.9f;
            combat.SheatheAttachTime = fit.Release; combat.SheatheDuration = fit.SheatheLength;
            EditorUtility.SetDirty(combat); EditorUtility.SetDirty(audio);
            return combat;
        }

        // ------------------------------------------------------------------ Mixamo draw / sheathe

        static AnimationClip MixamoClip(string path, string clipName)
        {
            var mi = AssetImporter.GetAtPath(path) as ModelImporter ?? throw new Exception("Missing " + path + " (copy it from Kengeki/assets)");
            if (mi.animationType != ModelImporterAnimationType.Human || mi.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
            {
                mi.animationType = ModelImporterAnimationType.Human;
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                mi.importAnimation = true;
                mi.SaveAndReimport();
            }
            var current = mi.clipAnimations.Length > 0 ? mi.clipAnimations : mi.defaultClipAnimations;
            var c = current[0];
            bool same = mi.clipAnimations.Length == 1 && c.name == clipName && !c.loopTime && c.lockRootRotation && c.lockRootHeightY && !c.lockRootPositionXZ && c.keepOriginalPositionY && !c.heightFromFeet;
            if (!same)
            {
                c.name = clipName;
                c.loopTime = false;                                        // one-shot
                c.lockRootRotation = true; c.keepOriginalOrientation = true;  // body rotation stays in the pose
                c.lockRootHeightY = true; c.keepOriginalPositionY = true; c.heightFromFeet = false;
                c.lockRootPositionXZ = false; c.keepOriginalPositionXZ = false; // XZ travel -> root motion -> discarded (no drift)
                mi.clipAnimations = new[] { c };
                mi.SaveAndReimport();
            }
            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(x => !x.name.StartsWith("__preview__"));
        }

        public struct Fit { public float Grab, Release, DrawLength, SheatheLength, ReleaseGap, ReleaseAngle; public Vector3 MountPos; public Quaternion MountRot; public string Note; }
        static Fit? _fit;
        static Fit GetFit() => _fit ?? (_fit = AnalyzeDrawSheathe()).Value;

        /// <summary>
        /// Samples the draw / sheathe clips on the robot: finds when the right hand is at the hilt (closest to the left
        /// hip joint), then places the scabbard so the sheathed sword coincides with the hand-held sword at that frame.
        /// The sword therefore changes parent without a visible jump.
        /// </summary>
        static Fit AnalyzeDrawSheathe()
        {
            var draw = MixamoClip(DrawFbx, "Draw");
            var sheathe = MixamoClip(SheatheFbx, "Sheathe");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ArmaturePrefab));
            var fit = new Fit { DrawLength = draw.length, SheatheLength = sheathe.length };
            try
            {
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var an = go.GetComponent<Animator>();
                Transform hips = an.GetBoneTransform(HumanBodyBones.Hips), hand = an.GetBoneTransform(HumanBodyBones.RightHand), lhip = an.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                var grip = Matrix4x4.TRS(GripPos, Quaternion.Euler(GripEuler), Vector3.one);
                var tsuka = new Vector3(0, 0, -0.13f);
                AnimationMode.StartAnimationMode();
                void Sample(AnimationClip c, float t) { AnimationMode.BeginSampling(); AnimationMode.SampleAnimationClip(go, c, t); AnimationMode.EndSampling(); }
                Matrix4x4 Socket() => hand.localToWorldMatrix * grip;

                // draw: hand at the hilt = right-hand tsuka point closest to the left hip joint in the first 60 %
                float best = float.MaxValue;
                for (float t = 0; t <= draw.length * 0.6f; t += 1f / 60f)
                {
                    Sample(draw, t);
                    float d = Vector3.Distance(Socket().MultiplyPoint3x4(tsuka), lhip.position);
                    if (d < best) { best = d; fit.Grab = t; }
                }
                Sample(draw, fit.Grab);
                var s = Socket();
                var sockRot = s.rotation; var sockPos = (Vector3)s.GetColumn(3);
                var pos = hips.InverseTransformPoint(sockPos);
                var rot = Quaternion.Inverse(hips.rotation) * sockRot;
                var fwd = rot * Vector3.forward;   // blade direction in hips space
                bool plausible = pos.x < 0.05f && pos.magnitude < 0.5f && fwd.z < -0.2f && fwd.y < 0.5f;
                if (plausible) { fit.MountPos = pos; fit.MountRot = rot; fit.Note = "scabbard aligned to the hand pose at the grab frame"; }
                else
                {   // keep the worn-katana orientation, but put the hilt where the hand grabs
                    fit.MountRot = Quaternion.Euler(SayaMountEuler);
                    fit.MountPos = hips.InverseTransformPoint(s.MultiplyPoint3x4(tsuka)) - fit.MountRot * tsuka;
                    fit.Note = $"grab pose implausible for a hip scabbard (blade dir {fwd:F2}, pos {pos:F2}); default orientation, hilt at grab point";
                }

                // sheathe: release when the hand-held sword best matches the sheathed sword, in the last 70 %
                best = float.MaxValue;
                for (float t = sheathe.length * 0.3f; t <= sheathe.length; t += 1f / 60f)
                {
                    Sample(sheathe, t);
                    var mount = hips.localToWorldMatrix * Matrix4x4.TRS(fit.MountPos, fit.MountRot, Vector3.one);
                    var h = Socket();
                    float d = Vector3.Distance(h.MultiplyPoint3x4(tsuka), mount.MultiplyPoint3x4(tsuka))
                            + 0.3f * Vector3.Distance(h.MultiplyPoint3x4(new Vector3(0, 0, 0.6f)), mount.MultiplyPoint3x4(new Vector3(0, 0, 0.6f)));
                    if (d < best) { best = d; fit.Release = t; fit.ReleaseGap = Vector3.Distance(h.MultiplyPoint3x4(tsuka), mount.MultiplyPoint3x4(tsuka)); fit.ReleaseAngle = Quaternion.Angle(h.rotation, mount.rotation); }
                }
            }
            finally { AnimationMode.StopAnimationMode(); UnityEngine.Object.DestroyImmediate(go); }
            var msg = $"[SwordSetup] draw {fit.DrawLength:0.00}s grab@{fit.Grab:0.00}s; sheathe {fit.SheatheLength:0.00}s release@{fit.Release:0.00}s (hilt gap {fit.ReleaseGap:0.000} m, {fit.ReleaseAngle:0}°); mount {fit.MountPos:F3} {fit.MountRot.eulerAngles:F0}: {fit.Note}";
            Debug.Log(msg);
            Directory.CreateDirectory("_Backups/sword"); File.WriteAllText("_Backups/sword/draw_sheathe_fit.txt", msg);
            return fit;
        }

        static Transform Child(Transform parent, string name, Vector3 pos, Vector3 euler)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = pos; t.localEulerAngles = euler; t.localScale = Vector3.one;
            return t;
        }

        // The pack's URP shader graph (made for LWRP 6.9) does not upgrade to URP 14: it compiles to a single unnamed
        // pass and draws nothing. Our own URP/Lit materials reuse the pack's textures; the vendor materials stay untouched.
        // Vendor mask: R = metallic, G = smoothness (URP Lit reads smoothness from alpha, so a constant is used instead).
        const string MatDir = "Assets/_IslandCombat/Materials/";
        const string TexDir = "Assets/JTS_TheSamuraiSword/Textures/";

        static Material LitMaterial(string name, string texPrefix, float smoothness)
        {
            Directory.CreateDirectory(MatDir);
            var path = MatDir + name + ".mat";
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? throw new Exception("URP Lit shader not found");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            Texture T(string suffix) => AssetDatabase.LoadAssetAtPath<Texture>(TexDir + texPrefix + suffix + ".psd") ?? throw new Exception("Missing texture " + texPrefix + suffix);
            m.SetTexture("_BaseMap", T("_dif")); m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_MetallicGlossMap", T("_msk")); m.EnableKeyword("_METALLICSPECGLOSSMAP");
            m.SetFloat("_Smoothness", smoothness);
            m.SetTexture("_BumpMap", T("_nrm")); m.EnableKeyword("_NORMALMAP"); m.SetFloat("_BumpScale", 1f);
            EditorUtility.SetDirty(m);
            return m;
        }

        // Prop instance with physics stripped (a Rigidbody/collider on the player's sword would fight the CharacterController).
        static GameObject Place(string prefabPath, Transform parent, string name, Material material)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) ?? throw new Exception("Missing prefab " + prefabPath);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src, parent);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
            go.name = name;
            go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity;
            foreach (var j in go.GetComponentsInChildren<Joint>(true)) UnityEngine.Object.DestroyImmediate(j);
            foreach (var r in go.GetComponentsInChildren<Rigidbody>(true)) UnityEngine.Object.DestroyImmediate(r);
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(c);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                var mats = r.sharedMaterials; for (int i = 0; i < mats.Length; i++) mats[i] = material; r.sharedMaterials = mats;
            }
            return go;
        }
    }
}
