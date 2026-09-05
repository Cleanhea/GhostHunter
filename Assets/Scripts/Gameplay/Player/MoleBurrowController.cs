using Unity.Netcode;
using UnityEngine;
using GhostHunter.Core;
using GhostHunter.Gameplay.Sanity;

namespace GhostHunter.Gameplay.Player
{
    /// <summary>
    /// 굴착 스킬 프로토타입(두더지 스킬 시스템 기획서 §5). 소유자가 입력을 읽고 상태를 진행하는
    /// 것은 <see cref="PlayerMotor"/> 의 이동 권위 예외(ADR-0008)와 같은 자리에 얹은 것이다 —
    /// 물리 상태를 서버가 아니라 소유자가 결정한다는 점에서 크게 벗어나지 않는다.
    ///
    /// <para>
    /// 다만 <see cref="IsBurrowed"/> 는 귀신의 탐지 판정(서버)이 읽어야 하므로
    /// <see cref="PlayerMotor.IsCrouching"/> 과 같은 방식(Owner 쓰기 + Everyone 읽기)의
    /// <see cref="NetworkVariable{T}"/> 로 복제한다. 서버는 Owner 가 보낸 값을 그대로 받아
    /// 자기 사본에도 반영하므로, 귀신 쪽에서 이 값을 신뢰하고 탐지를 완전히 건너뛸 수 있다
    /// (<see cref="SanityNetworkState.IsCrouching"/> 이 이미 같은 패턴으로 검증돼 있다).
    /// </para>
    ///
    /// <para><b>이 프로토타입의 명시적 범위(사용자 확정, 2026-08-31):</b></para>
    /// <list type="bullet">
    /// <item>입력: T(<c>Player/Burrow</c>) — Interact(E)와 분리된다(사용자 확정 2026-09-05, MS-16 해소).</item>
    /// <item>굴착 가능 위치: 제한 없음(§5.5 MS-7 해소 — 어디서든 가능).</item>
    /// <item>땅속 이동: 불가(제자리 고정, MS-8 해소).</item>
    /// <item>카메라: 시전 시작과 동시에 로컬 높이를 <see cref="MoleBurrowSettings.BurrowedCameraHeight"/>
    /// (바닥보다 살짝 위)로 낮추고, 같은 구간 동안 <see cref="MoleBurrowCameraEffect"/> 가 비네트를
    /// 올린다(2026-08-31 추가, MS-8 화면 연출 부분 해소 — 여전히 임의값).</item>
    /// <item>낙하 피해: 없음(MS-9 해소).</item>
    /// <item>도약 방향: 수직 고정(MS-9 해소).</item>
    /// <item>도약 높이: 4m 고정, 속도는 <see cref="MoleBurrowSettings"/> 의 중력으로 역산(MS-9 부분 해소).</item>
    /// <item>시전 시간: 임의값(<see cref="MoleBurrowSettings.EnterCastSeconds"/>, MS-10 임시).</item>
    /// <item>재사용 대기: 10초(사용자 확정 2026-09-05, MS-3 수치 부분 해소).</item>
    /// <item>추격·포착 중 진입: 진입 자체는 허용.</item>
    /// </list>
    ///
    /// <para><b>조작 제한(§5.5.1, 2026-09-05 구현).</b> 시전을 시작하는 순간
    /// <see cref="PlayerInputReader.SetSkillInputLocked"/> 로 <b>시야 회전과 굴착 키를 뺀 모든
    /// 입력</b>을 막는다. <see cref="PlayerMotor.MovementLocked"/> 도 함께 세워 둔다 — 입력이
    /// 0이어도 모터 쪽에서 한 번 더 막아야 잔여 속도나 다른 경로의 이동이 새지 않는다.</para>
    ///
    /// <para><b>은신이 깨지는 경우(§5.2.1, 2026-09-05 구현).</b> 땅속은 원래 완전 은신이지만
    /// <b>귀신에게 이미 감지된 상태에서 매몰되면 땅속에서도 계속 감지·포획된다.</b> 판정은
    /// 서버(귀신)가 <see cref="Ghost.BurrowExposureTracker"/> 로 하며, 이 컴포넌트는
    /// <see cref="IsBurrowed"/> 만 정직하게 복제한다. 손전등 예외는 손전등 시스템이 없어 미구현이다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerMotor))]
    public sealed class MoleBurrowController : NetworkBehaviour, IMoleSkillDebug, IMoleSkillStatus
    {
        private enum State
        {
            Idle,
            Entering,
            Buried,
        }

        [SerializeField] private MoleBurrowSettings _settings;

        private PlayerInputReader _input;

        private readonly NetworkVariable<bool> _isBurrowed = new(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        private PlayerMotor _motor;
        private SanityNetworkState _sanity;
        private ILocalPlayerContext _localPlayer;
        private State _state;
        private float _timer;
        private float _cooldownRemaining;

        /// <summary>귀신의 탐지 판정(§9 GhostPrototypeController)이 읽는 은신 여부.</summary>
        public bool IsBurrowed => _isBurrowed.Value;

        /// <summary>
        /// 시전(Entering) ~ 매몰(Buried) 전체 구간 — 네트워크 복제가 필요 없는 로컬 전용 상태다.
        /// <see cref="IsBurrowed"/>(안전하게 숨었는지, 귀신 판정용)와는 다르다: 카메라 낮추기·
        /// 비네트 같은 화면 연출은 시전을 시작한 순간부터 켜야 자연스러워서 이 값을 쓴다
        /// (<see cref="MoleBurrowCameraEffect"/>가 <see cref="ILocalPlayerContext"/> 로 읽는다).
        /// </summary>
        public bool IsActive => _state != State.Idle;

        /// <summary>공통 스킬 UI가 읽는 굴착 단계.</summary>
        public MoleSkillPhase Phase => _state switch
        {
            State.Entering => MoleSkillPhase.Casting,
            State.Buried => MoleSkillPhase.Active,
            _ => _cooldownRemaining > 0f ? MoleSkillPhase.Cooldown : MoleSkillPhase.Idle,
        };

        /// <summary>현재 시전·매몰·쿨타임 단계의 남은 시간.</summary>
        public float PhaseRemainingSeconds => _state switch
        {
            State.Entering => _timer,
            State.Buried => _timer,
            _ => _cooldownRemaining,
        };

        public float ActiveDurationSeconds => _settings != null ? _settings.MaxBurrowDuration : 0f;
        public float CooldownRemainingSeconds => _cooldownRemaining;
        public float CooldownDurationSeconds => _settings != null ? _settings.CooldownSeconds : 0f;

        private void Awake()
        {
            _motor = GetComponent<PlayerMotor>();
            _sanity = GetComponent<SanityNetworkState>();
            _input = GetComponent<PlayerInputReader>();

            if (_settings == null)
                Debug.LogError($"{nameof(MoleBurrowController)}: 설정 에셋이 없습니다.", this);
        }

        public override void OnNetworkSpawn()
        {
            _isBurrowed.OnValueChanged += HandleBurrowedChanged;
            ApplyVisual(_isBurrowed.Value);

            if (!IsOwner)
                return;

            _isBurrowed.Value = false;
            _localPlayer = Services.Get<ILocalPlayerContext>();
            _localPlayer.Register(this);
        }

        public override void OnNetworkDespawn()
        {
            _isBurrowed.OnValueChanged -= HandleBurrowedChanged;

            if (!IsOwner)
                return;

            if (_motor != null)
                _motor.MovementLocked = false;

            // 굴착 중 디스폰되면 잠금이 남는다 — 입력 리더가 먼저 정리됐을 수 있으니 null 검사.
            if (_input != null)
                _input.SetSkillInputLocked(false);

            _localPlayer?.Unregister(this);
            _localPlayer = null;
        }

        private void Update()
        {
            if (!IsSpawned || !IsOwner || _settings == null || _input == null)
                return;

            OwnerTick(Time.deltaTime);
        }

        private void OwnerTick(float deltaTime)
        {
            if (_cooldownRemaining > 0f)
                _cooldownRemaining = Mathf.Max(0f, _cooldownRemaining - deltaTime);

            bool alive = _sanity == null || _sanity.HasSanity;

            switch (_state)
            {
                case State.Idle:
                    if (alive && _cooldownRemaining <= 0f && _input.BurrowPressedThisFrame)
                        StartEntering();
                    break;

                case State.Entering:
                    if (!alive)
                    {
                        Cancel();
                        break;
                    }

                    _timer -= deltaTime;
                    if (_timer <= 0f)
                        EnterBuried();
                    break;

                case State.Buried:
                    if (!alive)
                    {
                        // 사망 시 튀어오르지 않고 조용히 종료한다 — §3.1, 죽은 채로 숨어 있게 두지 않는다.
                        Pop(applyLaunch: false);
                        break;
                    }

                    _timer -= deltaTime;
                    if (_input.BurrowPressedThisFrame || _timer <= 0f)
                        Pop(applyLaunch: true);
                    break;
            }
        }

        private void StartEntering()
        {
            _state = State.Entering;
            _timer = _settings.EnterCastSeconds;
            _motor.MovementLocked = true;
            // §5.5.1 — 시야 회전과 굴착 키만 남기고 나머지 조작을 막는다. 시전 시작부터 건다:
            // 0.3초 동안 던지거나 문을 열 수 있으면 "땅을 파는 중"이 아니다.
            _input.SetSkillInputLocked(true);
            // 시전 시작과 동시에 낮아지기 시작한다 — 매몰 완료를 기다리지 않는다(연출 요청).
            _motor.CameraHeightOverride = _settings.BurrowedCameraHeight;
        }

        private void EnterBuried()
        {
            _state = State.Buried;
            _timer = _settings.MaxBurrowDuration;
            _isBurrowed.Value = true;
        }

        /// <summary>
        /// 종료 — 스킬 키 재입력(즉시 종료) / 5초 경과(자동 종료) 모두 여기로 온다(§5.3).
        /// 두 경우 다 튀어오른다. 사망으로 인한 강제 종료만 <paramref name="applyLaunch"/> = false.
        /// </summary>
        private void Pop(bool applyLaunch)
        {
            _isBurrowed.Value = false;
            _motor.MovementLocked = false;
            _motor.CameraHeightOverride = null;
            _input.SetSkillInputLocked(false);

            if (applyLaunch)
                _motor.ApplyVerticalLaunch(_settings.ComputePopLaunchSpeed());

            _cooldownRemaining = _settings.CooldownSeconds;
            _state = State.Idle;
        }

        /// <summary>시전(Entering) 중 사망 — 아직 숨지 못했으니 튀어오름 없이 그냥 취소한다.</summary>
        private void Cancel()
        {
            _motor.MovementLocked = false;
            _motor.CameraHeightOverride = null;
            _input.SetSkillInputLocked(false);
            _state = State.Idle;
        }

        private void HandleBurrowedChanged(bool previous, bool current) => ApplyVisual(current);

        /// <summary>
        /// 땅속인 동안 다른 클라이언트에게 몸(RemoteBody)을 숨긴다. 소유자 자신은 원래도
        /// <see cref="PlayerVisuals"/> 가 자기 몸 렌더러를 꺼 둔 상태라 영향이 없다.
        /// </summary>
        private void ApplyVisual(bool hidden)
        {
            Transform body = _motor != null ? _motor.VisualBody : null;
            if (body != null)
                body.gameObject.SetActive(!hidden);
        }

        // ── 개발 HUD(Tab) 전용 ─────────────────────────────────────────────
        // 상태를 강제로 바꾸기만 한다. 수치는 MoleBurrowSettings 에셋이 권위이고 HUD 가 직접 만진다.

        bool IMoleSkillDebug.CanControl =>
            IsSpawned && IsOwner && _settings != null && _input != null && _motor != null;

        string IMoleSkillDebug.StatusSummary
        {
            get
            {
                if (!IsSpawned || !IsOwner)
                    return "로컬 소유자의 굴착이 아닙니다.";

                if (_settings == null)
                    return "설정 에셋이 배선되지 않았습니다.";

                string phase = _state switch
                {
                    State.Entering => $"시전 중  {_timer:0.0}s 남음",
                    State.Buried => $"매몰 중  {_timer:0.0}s 남음",
                    _ => "대기(Idle)",
                };

                string cooldown = _cooldownRemaining > 0f
                    ? $"쿨타임 {_cooldownRemaining:0.0}s / {_settings.CooldownSeconds:0.#}s"
                    : "쿨타임 준비됨";

                bool alive = _sanity == null || _sanity.HasSanity;
                return alive
                    ? $"{phase}   ·   {cooldown}"
                    : $"{phase}   ·   {cooldown}   ·   사망 — 사용 불가(§3.1)";
            }
        }

        void IMoleSkillDebug.ForceStart()
        {
            if (!IsSpawned || !IsOwner || _settings == null || _input == null || _motor == null
                || _state != State.Idle)
                return;

            // 쿨타임만 건너뛴다. 사망 게이팅(§3.1)은 그대로 둬야 규칙을 확인할 수 있다.
            if (_sanity != null && !_sanity.HasSanity)
                return;

            _cooldownRemaining = 0f;
            StartEntering();
        }

        void IMoleSkillDebug.ForceEnd()
        {
            if (!IsSpawned || !IsOwner)
                return;

            switch (_state)
            {
                case State.Buried:
                    Pop(applyLaunch: true);
                    break;
                case State.Entering:
                    Cancel();
                    break;
            }
        }

        void IMoleSkillDebug.ResetCooldown()
        {
            if (IsSpawned && IsOwner)
                _cooldownRemaining = 0f;
        }
    }
}
