using GhostHunter.Core;
using GhostHunter.Gameplay.Sanity;
using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Gameplay.Player
{
    /// <summary>
    /// 소유자 화면에서만 탐지 스킬을 진행한다. 서버 복제·원격 호출을 사용하지 않고,
    /// 대상 마커의 로컬 렌더러만 바꾸므로 다른 클라이언트에는 결과가 보이지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerInputReader))]
    public sealed class DetectionSkillController : NetworkBehaviour, IMoleSkillDebug, IMoleSkillStatus
    {
        [SerializeField] private DetectionSkillSettings _settings;

        private PlayerInputReader _input;
        private SanityNetworkState _sanity;
        private ILocalPlayerContext _localPlayer;
        private DetectionSkillStateMachine _stateMachine;
        private bool _ownsHighlightState;

        public DetectionSkillSettings Settings => _settings;

        public MoleSkillPhase Phase => _stateMachine != null
            ? _stateMachine.Phase
            : MoleSkillPhase.Idle;

        public float PhaseRemainingSeconds => _stateMachine != null
            ? _stateMachine.PhaseRemainingSeconds
            : 0f;

        public float ActiveDurationSeconds => _stateMachine != null
            ? _stateMachine.ActiveDurationSeconds
            : 0f;

        public float CooldownRemainingSeconds => _stateMachine != null
            ? _stateMachine.CooldownRemainingSeconds
            : 0f;

        public float CooldownDurationSeconds => _stateMachine != null
            ? _stateMachine.CooldownDurationSeconds
            : 0f;

        private void Awake()
        {
            _input = GetComponent<PlayerInputReader>();
            _sanity = GetComponent<SanityNetworkState>();

            if (_settings == null)
            {
                Debug.LogError(
                    $"{nameof(DetectionSkillController)}: 탐지 설정 에셋이 없습니다.",
                    this);
                enabled = false;
                return;
            }

            _stateMachine = new DetectionSkillStateMachine(
                _settings.CastDurationSeconds,
                _settings.DisplayDurationSeconds,
                _settings.CooldownSeconds);
        }

        public override void OnNetworkSpawn()
        {
            if (!IsOwner)
            {
                enabled = false;
                return;
            }

            if (!Services.TryGet(out _localPlayer))
            {
                Debug.LogError(
                    $"{nameof(DetectionSkillController)}: {nameof(ILocalPlayerContext)}가 등록되지 않았습니다.",
                    this);
                enabled = false;
                return;
            }

            _localPlayer.Register(this);
            _ownsHighlightState = true;
        }

        public override void OnNetworkDespawn()
        {
            if (!IsOwner)
                return;

            if (_ownsHighlightState)
            {
                DetectionTargetMarker.SetAllHighlighted(false, null);
                _ownsHighlightState = false;
            }

            _localPlayer?.Unregister(this);
            _localPlayer = null;
        }

        private void OnDisable()
        {
            // 원격 플레이어의 비활성화가 로컬 소유자의 하이라이트를 끄지 않도록 소유권을 확인한다.
            if (!_ownsHighlightState)
                return;

            DetectionTargetMarker.SetAllHighlighted(false, null);
            _ownsHighlightState = false;
        }

        private void Update()
        {
            if (!IsSpawned || !IsOwner || _stateMachine == null || _input == null)
                return;

            _stateMachine.Configure(
                _settings.CastDurationSeconds,
                _settings.DisplayDurationSeconds,
                _settings.CooldownSeconds);

            bool playerAlive = _sanity == null || _sanity.HasSanity;
            if (_input.DetectPressedThisFrame)
                _stateMachine.TryStart(inputPressed: true, playerAlive: playerAlive);

            DetectionSkillStateMachine.Transition transition = _stateMachine.Tick(
                Time.deltaTime,
                playerAlive,
                _settings.CancelOnDeath);
            ApplyTransition(transition);
        }

        private void ApplyTransition(DetectionSkillStateMachine.Transition transition)
        {
            switch (transition)
            {
                case DetectionSkillStateMachine.Transition.ActiveStarted:
                    DetectionTargetMarker.SetAllHighlighted(true, _settings);
                    break;

                case DetectionSkillStateMachine.Transition.Finished:
                case DetectionSkillStateMachine.Transition.Cancelled:
                    DetectionTargetMarker.SetAllHighlighted(false, null);
                    break;
            }
        }

        bool IMoleSkillDebug.CanControl => IsSpawned && IsOwner && _stateMachine != null;

        string IMoleSkillDebug.StatusSummary
        {
            get
            {
                if (!IsSpawned || !IsOwner)
                    return "로컬 소유자의 탐지가 아닙니다.";

                if (_stateMachine == null || _settings == null)
                    return "탐지 설정 에셋이 배선되지 않았습니다.";

                string phase = Phase switch
                {
                    MoleSkillPhase.Casting => $"시전 연출  {PhaseRemainingSeconds:0.0}s 남음",
                    MoleSkillPhase.Active => $"탐지 중  {PhaseRemainingSeconds:0.0}s 남음",
                    MoleSkillPhase.Cooldown => "탐지 종료",
                    _ => "대기(Idle)",
                };

                string cooldown = CooldownRemainingSeconds > 0f
                    ? $"쿨타임 {CooldownRemainingSeconds:0.0}s / {CooldownDurationSeconds:0.#}s"
                    : "쿨타임 준비됨";

                bool alive = _sanity == null || _sanity.HasSanity;
                return alive
                    ? $"{phase}   ·   {cooldown}"
                    : $"{phase}   ·   {cooldown}   ·   사망 — 사용 불가(§3.1)";
            }
        }

        void IMoleSkillDebug.ForceStart()
        {
            if (!IsSpawned || !IsOwner || _stateMachine == null)
                return;

            if (_sanity != null && !_sanity.HasSanity)
                return;

            _stateMachine.ResetCooldown();
            if (_stateMachine.Phase == MoleSkillPhase.Idle
                && _stateMachine.TryStart(inputPressed: true, playerAlive: true))
            {
                // 시전 연출은 다음 Tick에서 끝난다. 5초 표시가 시작될 때만 마커를 켠다.
            }
        }

        void IMoleSkillDebug.ForceEnd()
        {
            if (!IsSpawned || !IsOwner || _stateMachine == null)
                return;

            if (_stateMachine.Phase == MoleSkillPhase.Casting
                || _stateMachine.Phase == MoleSkillPhase.Active)
            {
                _stateMachine.Cancel();
                DetectionTargetMarker.SetAllHighlighted(false, null);
            }
        }

        void IMoleSkillDebug.ResetCooldown()
        {
            if (IsSpawned && IsOwner)
                _stateMachine?.ResetCooldown();
        }
    }
}
