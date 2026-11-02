using GhostHunter.Core;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GhostHunter.Gameplay.Player
{
    /// <summary>
    /// 소유자의 InputActionAsset 복제본을 읽어 입력값을 노출한다. 원격 플레이어는 액션을 켜지 않는다.
    /// 메뉴·복구 > 사망·관전 > 굴착 > 휠 우선순위는 PlayerInputPolicy가 정한다.
    /// 잠금별 허용 입력·자세 유지 규칙은 docs/architecture/player-controller.md의 입력 매핑을 따른다.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    public sealed class PlayerInputReader : NetworkBehaviour
    {
        [SerializeField] private InputActionAsset _inputActions;

        private InputActionAsset _runtimeActions;
        private InputAction _voiceAction;
        private InputAction _voiceMuteAction;
        public bool VoiceHeld { get; private set; }
        public bool VoiceMutePressedThisFrame { get; private set; }
        private InputAction _moveAction;
        private InputAction _lookAction;
        private InputAction _jumpAction;
        private InputAction _attackAction;
        private InputAction _interactAction;
        private InputAction _crouchAction;
        private InputAction _sprintAction;
        private InputAction _burrowAction;
        private InputAction _detectAction;
        private InputAction _proneAction;
        private InputAction _quickSlotAction;
        private InputAction _spectateDescendAction;
        private InputAction _spectateToggleAction;
        private InputAction _spectateNextAction;
        private InputAction _useDriverAction;
        private InputAction _rotateFurnitureAction;
        private InputAction _rotateFurnitureModeAction;
        private InputAction _headlampAction;
        private bool _jumpQueued;
        private bool _inputLocked;
        private bool _deathLocked;
        private SkillInputMode _skillMode;
        private bool _wheelLocked;

        public Vector2 Move { get; private set; }

        // 폐급 두더지 효과(PlayerDefectState) — 이동 키 반대, 또는 저주로 이동 입력 덮어쓰기.
        private bool _moveInverted;
        private Vector2? _moveOverride;
        public Vector2 Look { get; private set; }
        public bool AttackPressedThisFrame { get; private set; }
        public bool AttackReleasedThisFrame { get; private set; }
        public bool InteractPressedThisFrame { get; private set; }

        /// <summary>E를 누르고 있는 동안 참. 배터리 팩 충전처럼 홀드로 쓰는 상호작용이 읽는다 — 잠금 중에는 거짓이다.</summary>
        public bool InteractHeld { get; private set; }
        public bool CrouchHeld { get; private set; }

        /// <summary>달리기 입력(기본 Left Shift). 유지하는 동안 참.</summary>
        public bool SprintHeld { get; private set; }

        /// <summary>
        /// Look 액션의 가공되지 않은 델타값. <see cref="Look"/>과 달리 어떤 잠금 중에도 0으로
        /// 바뀌지 않는다 — 퀵슬롯 휠이 자신의 포인터 누적을 계산할 때 이 값을 쓴다(휠이 열린
        /// 동안 <see cref="Look"/> 자체는 카메라가 돌지 않도록 0이 된다).
        /// </summary>
        public Vector2 RawLookDelta { get; private set; }

        /// <summary>일시정지 메뉴가 게임플레이 입력을 잠갔는가 → docs/project/pause-menu-system.md §3.4</summary>
        public bool IsGameplayInputLocked => _inputLocked;

        /// <summary>사망해 생존 조작이 잠기고 관전 입력으로 바뀌었는가 → docs/project/spectator-system.md</summary>
        public bool IsDeathInputLocked => _deathLocked;

        /// <summary>굴착 중이라 조작이 제한됐는가(시전·매몰 모두) → docs/project/mole-skill-system.md §5.5.1</summary>
        public bool IsSkillInputLocked => _skillMode != SkillInputMode.None;

        /// <summary>퀵슬롯 휠 잠금이 활성화됐는가 → docs/architecture/quick-slot.md</summary>
        public bool IsWheelInputLocked => _wheelLocked;

        /// <summary>
        /// 관전 자유비행 이동 입력(WASD, Move 액션 재사용) — <see cref="IsDeathInputLocked"/> 동안만 채워진다.
        /// </summary>
        public Vector2 SpectatorMove { get; private set; }

        /// <summary>관전 자유비행 상승 입력(Space, Jump 액션 재사용) — 누르는 동안 참.</summary>
        public bool SpectatorAscendHeld { get; private set; }

        /// <summary>관전 자유비행 하강 입력(Left Ctrl) — 누르는 동안 참.</summary>
        public bool SpectatorDescendHeld { get; private set; }

        /// <summary>관전 모드 전환 입력(C) — 자유시점 ↔ 플레이어 관전.</summary>
        public bool SpectatorToggleModePressedThisFrame { get; private set; }

        /// <summary>관전 대상 이전 전환 입력(좌클릭, Attack 액션 재사용).</summary>
        public bool SpectatorPreviousPressedThisFrame { get; private set; }

        /// <summary>관전 대상 다음 전환 입력(우클릭).</summary>
        public bool SpectatorNextPressedThisFrame { get; private set; }

        /// <summary>
        /// 굴착 스킬 토글 입력(§5.3) — 한 번 누르면 진입, 진입/유지 중 다시 누르면 즉시 종료.
        /// 키는 사용자 확정 T이며 Interact(E)와 분리된다 → 두더지 스킬 시스템 기획서(mole-skill-system.md).
        /// <b>굴착 잠금 중에도 읽는다</b> — 그래야 스스로 나올 수 있다.
        /// </summary>
        public bool BurrowPressedThisFrame { get; private set; }

        /// <summary>탐지 스킬 입력(Q). 실패한 입력은 컨트롤러가 즉시 버린다.</summary>
        public bool DetectPressedThisFrame { get; private set; }

        /// <summary>퀵슬롯 휠 입력(Tab)을 누르고 있는 동안 참.</summary>
        public bool QuickSlotHeld { get; private set; }

        /// <summary>퀵슬롯 휠 입력을 누른 프레임.</summary>
        public bool QuickSlotPressedThisFrame { get; private set; }

        /// <summary>퀵슬롯 휠 입력을 뗀 프레임 — 휠이 이 프레임에 선택을 확정한다.</summary>
        public bool QuickSlotReleasedThisFrame { get; private set; }

        /// <summary>
        /// 엎드리기 토글 입력(Z) — 한 번 누르면 엎드리고, 다시 누르면 머리 위 공간이 있을 때 일어선다.
        /// 침대 밑으로 기어 들어가는 3번째 자세다 → player-controller.md.
        /// </summary>
        public bool PronePressedThisFrame { get; private set; }

        /// <summary>
        /// 생존 중 우클릭 입력(Player/UseDriver) — 가구용 멀티 드라이버의 분해·조립 트리거.
        /// 사망 중 우클릭(<see cref="SpectatorNextPressedThisFrame"/>)과는 별개 액션이라 상태가
        /// 갈려 직접 충돌하지 않는다 → docs/project/furniture-multidriver-system.md §7.
        /// </summary>
        public bool UseDriverPressedThisFrame { get; private set; }

        /// <summary>
        /// 생존 중 우클릭(Player/UseDriver)을 누르고 있는 동안 참 — 분해·조립은 행동 시간 끝까지 이 값이
        /// 유지돼야 완료된다 → docs/project/furniture-multidriver-system.md §3.2.
        /// </summary>
        public bool UseDriverHeld { get; private set; }

        /// <summary>
        /// 이 프레임 마우스 휠 세로 이동량(Player/RotateFurniture) — 2인 잡기 가구 회전·기울이기에 쓴다.
        /// 크기는 플랫폼·설정마다 달라 사용처는 부호만 본다 → docs/architecture/throw-system.md §3.1.
        /// </summary>
        public float FurnitureRotateScroll { get; private set; }

        /// <summary>휠 클릭(Player/RotateFurnitureMode) — 가구 휠 조작의 회전 ↔ 기울이기 전환.</summary>
        public bool FurnitureRotateModePressedThisFrame { get; private set; }

        /// <summary>
        /// 헤드라이트 전원 토글(F, Player/Headlamp) → docs/project/headlamp-system.md.
        /// <b>굴착·휠 잠금 중에도 살아 있다</b> — 땅굴 안에서 켜는 것 자체가 규칙에 있는 행동이다(두더지 스킬
        /// 기획서 §5.2.1 ②). 메뉴·사망 잠금에서는 막힌다.
        /// </summary>
        public bool HeadlampPressedThisFrame { get; private set; }

        private ILocalPlayerContext _localPlayer;

        public override void OnNetworkSpawn()
        {
            if (!IsOwner)
            {
                enabled = false;
                return;
            }

            if (_inputActions == null)
            {
                Debug.LogError("[PlayerInputReader] InputActionAsset이 연결되지 않았습니다.", this);
                enabled = false;
                return;
            }

            _runtimeActions = Instantiate(_inputActions);
            _moveAction = _runtimeActions.FindAction("Player/Move", true);
            _lookAction = _runtimeActions.FindAction("Player/Look", true);
            _jumpAction = _runtimeActions.FindAction("Player/Jump", true);
            _attackAction = _runtimeActions.FindAction("Player/Attack", true);
            _interactAction = _runtimeActions.FindAction("Player/Interact", true);
            _crouchAction = _runtimeActions.FindAction("Player/Crouch", true);
            _sprintAction = _runtimeActions.FindAction("Player/Sprint", true);
            _burrowAction = _runtimeActions.FindAction("Player/Burrow", true);
            _detectAction = _runtimeActions.FindAction("Player/Detect", true);
            _proneAction = _runtimeActions.FindAction("Player/Prone", true);
            _quickSlotAction = _runtimeActions.FindAction("Player/QuickSlot", true);
            _spectateDescendAction = _runtimeActions.FindAction("Player/SpectateDescend", true);
            _spectateToggleAction = _runtimeActions.FindAction("Player/SpectateToggleMode", true);
            _spectateNextAction = _runtimeActions.FindAction("Player/SpectateNext", true);
            _useDriverAction = _runtimeActions.FindAction("Player/UseDriver", true);
            _rotateFurnitureAction = _runtimeActions.FindAction("Player/RotateFurniture", true);
            _rotateFurnitureModeAction = _runtimeActions.FindAction("Player/RotateFurnitureMode", true);
            _headlampAction = _runtimeActions.FindAction("Player/Headlamp", true);
            _voiceAction = _runtimeActions.FindAction("Player/Voice", true);
            _voiceMuteAction = _runtimeActions.FindAction("Player/VoiceMute", true);
            _runtimeActions.Enable();

            // 일시정지 메뉴가 로컬 플레이어의 입력을 잠글 수 있도록 자신을 알린다.
            _localPlayer = Services.Get<ILocalPlayerContext>();
            _localPlayer.Register(this);
        }

        public override void OnNetworkDespawn()
        {
            _localPlayer?.Unregister(this);
            _localPlayer = null;

            if (_runtimeActions == null)
                return;

            VoiceHeld = false;
            VoiceMutePressedThisFrame = false;
            _runtimeActions.Disable();
            Destroy(_runtimeActions);
            _runtimeActions = null;
            CrouchHeld = false;
            SprintHeld = false;
            BurrowPressedThisFrame = false;
            DetectPressedThisFrame = false;
            QuickSlotHeld = false;
            QuickSlotPressedThisFrame = false;
            QuickSlotReleasedThisFrame = false;
            UseDriverPressedThisFrame = false;
            UseDriverHeld = false;
            InteractHeld = false;
            FurnitureRotateScroll = 0f;
            FurnitureRotateModePressedThisFrame = false;
            HeadlampPressedThisFrame = false;
            _inputLocked = false;
            _deathLocked = false;
            _skillMode = SkillInputMode.None;
            _wheelLocked = false;
            ClearSpectatorInputs();
        }

        /// <summary>
        /// 굴착 시전은 시점·이동도 막고, 매몰은 둘을 허용한다. 굴착 키·헤드라이트는 두 모드에서 읽는다.
        /// 시전 진입은 시점까지 즉시 비워 이미 읽은 입력이 적용되지 않게 한다.
        /// </summary>
        public void SetSkillInputMode(SkillInputMode mode)
        {
            if (_skillMode == mode)
                return;

            _skillMode = mode;

            if (mode == SkillInputMode.None)
                return;

            ClearBlockedInputs();
            if (mode == SkillInputMode.Frozen)
                Look = Vector2.zero;
        }

        /// <summary>
        /// 생존 조작을 즉시 비우고 다음 Update에서 관전 입력을 읽는다. 해제 시 관전 입력도 즉시 비운다.
        /// 굴착·휠 해제가 사망 잠금을 풀지 않으며, 메뉴·복구 잠금은 관전도 막는다.
        /// </summary>
        public void SetDeathInputLocked(bool locked)
        {
            if (_deathLocked == locked)
                return;

            _deathLocked = locked;

            if (locked)
            {
                ClearBlockedInputs();
                Look = Vector2.zero;
                BurrowPressedThisFrame = false;
            }
            else
            {
                ClearSpectatorInputs();
            }
        }

        /// <summary>
        /// 휠 중에도 이동·자세·휠·헤드라이트를 읽는다(QS-5). 다른 행동은 즉시, 시점은 다음 Update에서 막는다.
        /// </summary>
        public void SetWheelInputLocked(bool locked)
        {
            if (_wheelLocked == locked)
                return;

            _wheelLocked = locked;

            if (!locked)
                return;

            ClearWheelBlockedInputs();
        }

        /// <summary>세 잠금이 공통으로 버리는 입력. 시점과 자세 유지 값은 여기서 건드리지 않는다.</summary>
        private void ClearBlockedInputs()
        {
            ClearWheelBlockedInputs();
            Move = Vector2.zero;
            SprintHeld = false;
            QuickSlotHeld = false;
            QuickSlotPressedThisFrame = false;
            QuickSlotReleasedThisFrame = false;
            HeadlampPressedThisFrame = false;
        }

        /// <summary>
        /// 휠 잠금 전용 — <see cref="ClearBlockedInputs"/>와 달리 Move·Crouch·Sprint·QuickSlot은
        /// 건드리지 않는다(QS-5, 휠 자신은 살아 있어야 함).
        /// </summary>
        private void ClearWheelBlockedInputs()
        {
            AttackPressedThisFrame = false;
            AttackReleasedThisFrame = false;
            InteractPressedThisFrame = false;
            InteractHeld = false;
            PronePressedThisFrame = false;
            BurrowPressedThisFrame = false;
            DetectPressedThisFrame = false;
            UseDriverPressedThisFrame = false;
            UseDriverHeld = false;
            FurnitureRotateScroll = 0f;
            FurnitureRotateModePressedThisFrame = false;
            _jumpQueued = false;
        }

        /// <summary>관전 입력을 전부 0/false로 되돌린다 — 사망 잠금이 아닌 모든 분기에서 부른다.</summary>
        private void ClearSpectatorInputs()
        {
            SpectatorMove = Vector2.zero;
            SpectatorAscendHeld = false;
            SpectatorDescendHeld = false;
            SpectatorToggleModePressedThisFrame = false;
            SpectatorPreviousPressedThisFrame = false;
            SpectatorNextPressedThisFrame = false;
        }

        /// <summary>
        /// 사망 잠금 중에만 채운다. Move/Jump/Attack 액션을 자유비행·대상 전환 용도로 재사용하고,
        /// 시야는 <see cref="RawLookDelta"/>(항상 갱신됨)를 관전 컨트롤러가 직접 읽는다.
        /// </summary>
        private void PopulateSpectatorInputs()
        {
            SpectatorMove = _moveAction.ReadValue<Vector2>();
            SpectatorAscendHeld = _jumpAction.IsPressed();
            SpectatorDescendHeld = _spectateDescendAction.IsPressed();
            SpectatorToggleModePressedThisFrame = _spectateToggleAction.WasPressedThisFrame();
            SpectatorPreviousPressedThisFrame = _attackAction.WasPressedThisFrame();
            SpectatorNextPressedThisFrame = _spectateNextAction.WasPressedThisFrame();
        }

        /// <summary>
        /// 게임플레이 입력을 잠그거나 푼다. 잠긴 동안 이동·시점·상호작용·던지기·스킬 입력은
        /// 전부 0이 되고, 대기 중이던 점프도 버린다. 자세는 마지막 값으로 얼린다.
        ///
        /// 잠금은 로컬 입력 단계에서만 일어나며 서버에 보고되지 않는다 — 캐릭터는 잠긴 동안에도
        /// 중력·충돌·귀신 판정을 그대로 받는다.
        /// </summary>
        public void SetGameplayInputLocked(bool locked)
        {
            if (_inputLocked == locked)
                return;

            _inputLocked = locked;

            if (!locked)
                return;

            ClearBlockedInputs();
            Look = Vector2.zero;
            BurrowPressedThisFrame = false;
        }

        private void Update()
        {
            if (_runtimeActions == null)
                return;

            RawLookDelta = _lookAction.ReadValue<Vector2>();
            VoiceHeld = _voiceAction.IsPressed();
            VoiceMutePressedThisFrame = _voiceMuteAction.WasPressedThisFrame();

            PlayerInputPolicy policy = PlayerInputPolicy.Resolve(
                _inputLocked, GhostHunter.Gameplay.Recovery.StageRecoveryGate.Restoring,
                _deathLocked, _skillMode, _wheelLocked);
            // 잠금 중에도 마지막 웅크린 자세를 유지한다. 이동 보정은 허용된 이동에만 적용한다.
            Move = policy.CanMove ? FilterMove(_moveAction.ReadValue<Vector2>()) : Vector2.zero;
            Look = policy.CanLook ? RawLookDelta : Vector2.zero;
            if (policy.CanReadStance)
                CrouchHeld = _crouchAction.IsPressed();
            SprintHeld = policy.CanReadStance && _sprintAction.IsPressed();

            AttackPressedThisFrame = policy.CanReadGameplay && _attackAction.WasPressedThisFrame();
            AttackReleasedThisFrame = policy.CanReadGameplay && _attackAction.WasReleasedThisFrame();
            // Hold Interaction의 phase와 별개로, 누른 프레임부터 탭을 읽어야 한다.
            InteractPressedThisFrame = policy.CanReadGameplay && _interactAction.WasPressedThisFrame();
            InteractHeld = policy.CanReadGameplay && _interactAction.IsPressed();
            DetectPressedThisFrame = policy.CanReadGameplay && _detectAction.WasPressedThisFrame();
            PronePressedThisFrame = policy.CanReadGameplay && _proneAction.WasPressedThisFrame();
            UseDriverPressedThisFrame = policy.CanReadGameplay && _useDriverAction.WasPressedThisFrame();
            UseDriverHeld = policy.CanReadGameplay && _useDriverAction.IsPressed();
            FurnitureRotateScroll = policy.CanReadGameplay ? _rotateFurnitureAction.ReadValue<Vector2>().y : 0f;
            FurnitureRotateModePressedThisFrame = policy.CanReadGameplay && _rotateFurnitureModeAction.WasPressedThisFrame();
            QuickSlotHeld = policy.CanReadWheel && _quickSlotAction.IsPressed();
            QuickSlotPressedThisFrame = policy.CanReadWheel && _quickSlotAction.WasPressedThisFrame();
            QuickSlotReleasedThisFrame = policy.CanReadWheel && _quickSlotAction.WasReleasedThisFrame();
            BurrowPressedThisFrame = policy.CanBurrow && _burrowAction.WasPressedThisFrame();
            HeadlampPressedThisFrame = policy.CanUseHeadlamp && _headlampAction.WasPressedThisFrame();

            if (!policy.CanReadGameplay)
                _jumpQueued = false;
            else if (_jumpAction.WasPressedThisFrame())
                _jumpQueued = true;

            if (policy.CanSpectate)
                PopulateSpectatorInputs();
            else
                ClearSpectatorInputs();
        }

        /// <summary>
        /// 폐급 두더지 효과를 건다(revival-system.md §9). <paramref name="inverted"/> 면 이동 키가 반대로,
        /// <paramref name="forced"/> 가 있으면 사람 입력 대신 그 방향으로 움직인다. 잠금(사망·메뉴)은 그대로 이긴다.
        /// </summary>
        public void SetMoveModifiers(bool inverted, Vector2? forced)
        {
            _moveInverted = inverted;
            _moveOverride = forced;
        }

        private Vector2 FilterMove(Vector2 raw)
        {
            if (_moveOverride.HasValue)
                return _moveOverride.Value;
            return _moveInverted ? -raw : raw;
        }

        public bool ConsumeJump()
        {
            if (!_jumpQueued)
                return false;

            _jumpQueued = false;
            return true;
        }
    }
}
