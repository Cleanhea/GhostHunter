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
    /// <item>입력: R(<c>Player/Burrow</c>) — Interact(E)와 분리된다(사용자 확정, MS-1의 굴착 부분 해소).</item>
    /// <item>굴착 가능 위치: 제한 없음(§5.5 MS-7 해소 — 어디서든 가능).</item>
    /// <item>땅속 이동: 불가(제자리 고정, MS-8 해소).</item>
    /// <item>카메라: 시전 시작과 동시에 로컬 높이를 <see cref="MoleBurrowSettings.BurrowedCameraHeight"/>
    /// (바닥보다 살짝 위)로 낮추고, 같은 구간 동안 <see cref="MoleBurrowCameraEffect"/> 가 비네트를
    /// 올린다(2026-08-31 추가, MS-8 화면 연출 부분 해소 — 여전히 임의값).</item>
    /// <item>낙하 피해: 없음(MS-9 해소).</item>
    /// <item>도약 방향: 수직 고정(MS-9 해소).</item>
    /// <item>도약 높이: 4m 고정, 속도는 <see cref="MoleBurrowSettings"/> 의 중력으로 역산(MS-9 부분 해소).</item>
    /// <item>시전 시간: 임의값(<see cref="MoleBurrowSettings.EnterCastSeconds"/>, MS-10 임시).</item>
    /// <item>추격·포착 중 진입: 허용(MS-10 해소 — 진입을 막지 않는다).</item>
    /// </list>
    /// 재사용 대기 시간(MS-3)과 상시 노출 안 되는 원문 §3.4 공통 판정 조건(MS-2)은 여전히 TBD라
    /// 쿨타임은 임의값을 넣어 두었을 뿐이다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerMotor))]
    public sealed class MoleBurrowController : NetworkBehaviour
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
    }
}
