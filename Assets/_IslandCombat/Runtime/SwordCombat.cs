using System;
using StarterAssets;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IslandCombat
{
    /// <summary>
    /// Minimal sword layer on top of the Starter Assets ThirdPersonController. The controller is not modified:
    /// locomotion, jumping, camera and ground checks keep running on the Base Layer, and this component only
    /// drives two extra Animator layers (upper body + legs) and the sword's attachment.
    ///
    /// States are played from code (CrossFade) instead of a transition graph, so input can only ever start an
    /// action that is valid for the current state (no spammed draw/sheathe, no overlapping attack transitions).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    public class SwordCombat : MonoBehaviour
    {
        public enum SwordState { Sheathed, Drawing, Equipped, Attacking, Sheathing }

        [Serializable]
        public class AttackStep
        {
            public string State = "Attack1";
            [Tooltip("Clip length in seconds (filled in by the setup tool).")]
            public float Length = 1f;
            [Tooltip("Normalized time from which a next attack press is buffered.")]
            [Range(0f, 1f)] public float ComboWindow = 0.35f;
            [Tooltip("Normalized time at which the attack hands over to the next attack or back to the stance.")]
            [Range(0.3f, 1f)] public float ExitAt = 0.85f;
            [Tooltip("Normalized time at which the swing sound plays.")]
            [Range(0f, 1f)] public float SwingAt = 0.2f;
        }

        [Header("Sword objects")]
        public Transform Sword;
        [Tooltip("Socket on the right hand the sword snaps to when drawn.")]
        public Transform HandSocket;
        [Tooltip("Socket inside the scabbard the sword snaps to when sheathed.")]
        public Transform SheathSocket;

        [Header("Animator")]
        public string UpperLayer = "Sword Upper";
        public string LowerLayer = "Sword Lower";
        public string StanceState = "Stance";
        [Tooltip("Optional dedicated draw / sheathe states. Empty = blend the stance layer in / out instead.")]
        public string DrawState = "";
        public string SheatheState = "";
        public AttackStep[] Attacks = { new AttackStep() };

        [Header("Timing (seconds)")]
        public float DrawDuration = 0.45f;
        [Tooltip("When during the draw the sword moves from the scabbard to the hand.")]
        public float DrawAttachTime = 0.15f;
        public float SheatheDuration = 0.5f;
        [Tooltip("When during the sheathe the sword moves from the hand to the scabbard.")]
        public float SheatheAttachTime = 0.4f;
        [Tooltip("With a draw clip: how fast the sword layers fade in at the start of the draw.")]
        public float LayerFadeIn = 0.12f;
        [Tooltip("With a sheathe clip: how long before the end the sword layers fade back to locomotion.")]
        public float SheatheFadeOut = 0.3f;
        public float AttackFadeIn = 0.08f;
        public float RecoveryFade = 0.25f;
        [Tooltip("How quickly the full-body (idle) sword stance fades in/out when starting or stopping movement.")]
        public float LegBlendTime = 0.2f;
        [Tooltip("Animator 'Speed' below which the legs use the sword stance instead of locomotion.")]
        public float IdleSpeedThreshold = 0.3f;

        [Header("Movement")]
        [Tooltip("Move/sprint speed multiplier while an attack is playing (keeps movement responsive but heavier).")]
        [Range(0f, 1f)] public float AttackMoveScale = 0.35f;
        public bool RequireGroundedToAttack = true;

        [Header("Input (Input System paths, separate from the Starter Assets bindings)")]
        public string[] ToggleBindings = { "<Keyboard>/f", "<Gamepad>/buttonNorth" };
        public string[] AttackBindings = { "<Mouse>/leftButton", "<Gamepad>/buttonWest" };

        public SwordState State { get; private set; } = SwordState.Sheathed;
        public int AttackIndex { get; private set; } = -1;
        bool HasDrawClip => !string.IsNullOrEmpty(DrawState);
        bool HasSheatheClip => !string.IsNullOrEmpty(SheatheState);
        public bool IsBusy => State == SwordState.Drawing || State == SwordState.Sheathing || State == SwordState.Attacking;

        public event Action Drawn, Sheathed;
        public event Action<int> AttackStarted;

        Animator _anim;
        ThirdPersonController _tpc;
        SwordAudio _audio;
        InputAction _toggle, _attack;
        int _upper = -1, _lower = -1, _speedId;
        float _upperW, _lowerW, _stateTime;
        bool _attached, _swung, _queued;
        float _baseMove, _baseSprint;
        bool _speedScaled;

        void Awake()
        {
            _anim = GetComponent<Animator>();
            _tpc = GetComponent<ThirdPersonController>();
            _audio = GetComponent<SwordAudio>();
            _speedId = Animator.StringToHash("Speed");
            _toggle = MakeAction("SwordToggle", ToggleBindings);
            _attack = MakeAction("SwordAttack", AttackBindings);
        }

        static InputAction MakeAction(string name, string[] paths)
        {
            var a = new InputAction(name, InputActionType.Button);
            foreach (var p in paths) if (!string.IsNullOrEmpty(p)) a.AddBinding(p);
            return a;
        }

        void OnEnable()
        {
            _toggle.performed += OnTogglePerformed; _toggle.Enable();
            _attack.performed += OnAttackPerformed; _attack.Enable();
        }

        void OnDisable()
        {
            _toggle.performed -= OnTogglePerformed; _toggle.Disable();
            _attack.performed -= OnAttackPerformed; _attack.Disable();
            RestoreSpeed();
        }

        void Start()
        {
            _upper = _anim.GetLayerIndex(UpperLayer);
            _lower = _anim.GetLayerIndex(LowerLayer);
            if (_upper < 0 || _lower < 0)
            {
                Debug.LogError($"[SwordCombat] Animator controller '{_anim.runtimeAnimatorController?.name}' has no '{UpperLayer}'/'{LowerLayer}' layers. Run Tools/Island/Sword/Setup.", this);
                enabled = false; return;
            }
            if (_tpc != null) { _baseMove = _tpc.MoveSpeed; _baseSprint = _tpc.SprintSpeed; }
            Attach(false);
            SetWeights(0f, 0f);
        }

        void OnTogglePerformed(InputAction.CallbackContext _) => ToggleSword();
        void OnAttackPerformed(InputAction.CallbackContext _) => Attack();

        // ------------------------------------------------------------------ public actions

        /// <summary>Draw when sheathed, sheathe when idle-equipped. Ignored while drawing, sheathing or attacking.</summary>
        public bool ToggleSword()
        {
            if (!enabled) return false;
            if (State == SwordState.Sheathed) { BeginDraw(); return true; }
            if (State == SwordState.Equipped) { BeginSheathe(); return true; }
            return false;
        }

        /// <summary>Starts an attack when equipped; during an attack, buffers the next combo step once.</summary>
        public bool Attack()
        {
            if (!enabled || Attacks == null || Attacks.Length == 0) return false;
            if (RequireGroundedToAttack && _tpc != null && !_tpc.Grounded) return false;
            if (State == SwordState.Equipped) { BeginAttack(0); return true; }
            if (State == SwordState.Attacking && !_queued && AttackIndex + 1 < Attacks.Length
                && _stateTime / Attacks[AttackIndex].Length >= Attacks[AttackIndex].ComboWindow)
            { _queued = true; return true; }
            return false;
        }

        // ------------------------------------------------------------------ state machine

        void BeginDraw()
        {
            State = SwordState.Drawing; _stateTime = 0f;
            Play(string.IsNullOrEmpty(DrawState) ? StanceState : DrawState, 0f);
            _audio?.PlayDraw();
        }

        void BeginSheathe()
        {
            State = SwordState.Sheathing; _stateTime = 0f;
            if (!string.IsNullOrEmpty(SheatheState)) Play(SheatheState, 0.15f);
            _audio?.PlaySheathe();
        }

        void BeginAttack(int i)
        {
            State = SwordState.Attacking; AttackIndex = i; _stateTime = 0f; _queued = false; _swung = false;
            Play(Attacks[i].State, AttackFadeIn);
            ScaleSpeed(true);
            AttackStarted?.Invoke(i);
        }

        void EndAttack()
        {
            State = SwordState.Equipped; AttackIndex = -1; _queued = false;
            Play(StanceState, RecoveryFade);
            ScaleSpeed(false);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            _stateTime += dt;
            float upperTarget = _upperW;

            switch (State)
            {
                case SwordState.Sheathed:
                    upperTarget = 0f;
                    break;

                case SwordState.Drawing:
                    if (!_attached && _stateTime >= DrawAttachTime) Attach(true);
                    // with a draw clip the layers come in almost at once; without one the stance blends in over the draw
                    upperTarget = HasDrawClip ? Mathf.Clamp01(_stateTime / LayerFadeIn) : Mathf.Clamp01(_stateTime / Mathf.Max(0.01f, DrawDuration * 0.7f));
                    if (_stateTime >= DrawDuration)
                    {
                        if (!_attached) Attach(true);
                        State = SwordState.Equipped; _stateTime = 0f;
                        if (HasDrawClip) Play(StanceState, RecoveryFade);
                        Drawn?.Invoke();
                    }
                    break;

                case SwordState.Equipped:
                    upperTarget = 1f;
                    break;

                case SwordState.Attacking:
                {
                    upperTarget = 1f;
                    var step = Attacks[AttackIndex];
                    float n = _stateTime / Mathf.Max(0.01f, step.Length);
                    if (!_swung && n >= step.SwingAt) { _swung = true; _audio?.PlaySwing(); }
                    if (n >= step.ExitAt)
                    {
                        if (_queued && AttackIndex + 1 < Attacks.Length) BeginAttack(AttackIndex + 1);
                        else EndAttack();
                    }
                    break;
                }

                case SwordState.Sheathing:
                    // with a sheathe clip the layers stay on until its last moments, then hand back to locomotion
                    // (full weight until the sword is back in the saya, so the hand is exactly where the clip puts it)
                    upperTarget = HasSheatheClip
                        ? (_stateTime < SheatheAttachTime ? 1f : Mathf.Clamp01((SheatheDuration - _stateTime) / Mathf.Max(0.05f, Mathf.Min(SheatheFadeOut, SheatheDuration - SheatheAttachTime))))
                        : 1f - Mathf.Clamp01(_stateTime / Mathf.Max(0.01f, SheatheDuration * 0.8f));
                    if (_attached && _stateTime >= SheatheAttachTime) Attach(false);
                    if (_stateTime >= SheatheDuration)
                    {
                        if (_attached) Attach(false);
                        State = SwordState.Sheathed; _stateTime = 0f; upperTarget = 0f;
                        Sheathed?.Invoke();
                    }
                    break;
            }

            // Legs take the sword stance only while standing on the ground; moving/jumping keeps locomotion legs.
            bool grounded = _tpc == null || _tpc.Grounded;
            bool idle = _anim.GetFloat(_speedId) < IdleSpeedThreshold;
            float lowerTarget = (grounded && idle) ? upperTarget : 0f;
            float rate = dt / Mathf.Max(0.01f, LegBlendTime);
            _lowerW = Mathf.MoveTowards(_lowerW, lowerTarget, rate);
            _upperW = upperTarget;
            SetWeights(_upperW, _lowerW);
        }

        // ------------------------------------------------------------------ helpers

        void Play(string state, float fade)
        {
            if (fade <= 0f) { _anim.Play(state, _upper, 0f); _anim.Play(state, _lower, 0f); }
            else { _anim.CrossFadeInFixedTime(state, fade, _upper); _anim.CrossFadeInFixedTime(state, fade, _lower); }
        }

        void SetWeights(float upper, float lower)
        {
            _anim.SetLayerWeight(_upper, upper);
            _anim.SetLayerWeight(_lower, Mathf.Min(lower, upper));
        }

        void Attach(bool toHand)
        {
            _attached = toHand;
            var socket = toHand ? HandSocket : SheathSocket;
            if (Sword == null || socket == null) return;
            Sword.SetParent(socket, false);
            Sword.localPosition = Vector3.zero;
            Sword.localRotation = Quaternion.identity;
        }

        void ScaleSpeed(bool on)
        {
            if (_tpc == null) return;
            if (on && !_speedScaled) { _tpc.MoveSpeed = _baseMove * AttackMoveScale; _tpc.SprintSpeed = _baseSprint * AttackMoveScale; _speedScaled = true; }
            else if (!on) RestoreSpeed();
        }

        void RestoreSpeed()
        {
            if (_tpc == null || !_speedScaled) return;
            _tpc.MoveSpeed = _baseMove; _tpc.SprintSpeed = _baseSprint; _speedScaled = false;
        }
    }
}
