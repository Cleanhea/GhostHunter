using GhostHunter.Core;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GhostHunter.Gameplay.Player
{
    /// <summary>
    /// 프로젝트의 InputActionAsset을 로컬 플레이어별로 복제한다. 원격 플레이어가 공유 에셋을
    /// 활성화해 내 입력을 읽는 일을 막기 위해 소유자에서만 런타임 복제본을 켠다.
    ///
    /// <para><b>잠금은 네 가지이고 서로 독립이다.</b></para>
    /// <list type="bullet">
    /// <item><see cref="SetGameplayInputLocked"/> — 일시정지 메뉴. <b>전부</b> 0으로 만든다(시점·
    /// 관전 입력 포함) → docs/project/spectator-system.md §2("메뉴가 열리면 관전 입력도 잠근다").</item>
    /// <item><see cref="SetDeathInputLocked"/> — 사망 상태(관전 기획서 SP-1~SP-2, 사용자 확정
    /// 2026-09-12). 생존 조작(이동·시점·상호작용·스킬)을 전부 막고 대신 관전 입력(자유비행·모드
    /// 전환·대상 전환)을 채운다. <b>굴착·휠 잠금보다 우선한다</b> — 사망과 같은 프레임에 굴착 취소가
    /// 자기 잠금(<see cref="SetSkillInputLocked"/>)을 먼저 풀어도 생존 조작이 되살아나지 않게
    /// 막는 안전장치다.</item>
    /// <item><see cref="SetSkillInputLocked"/> — 굴착 중(두더지 스킬 기획서 §5.5.1, 사용자 확정
    /// 2026-09-05). <b>시야 회전(Look)과 스킬 키(Burrow)만 남기고</b> 나머지를 전부 막는다 —
    /// 땅속에서 점프·던지기·문 여닫기·자세 전환이 되면 안 된다.</item>
    /// <item><see cref="SetWheelInputLocked"/> — 퀵슬롯 휠이 열린 동안(사용자 확정 2026-09-12).
    /// <b>시야 회전(Look)만 0으로</b> 만들고 Attack·Interact·Burrow·Detect·Prone·Jump를 막는다.
    /// Move·Crouch·Sprint는 살려 둔다(QS-5) — 휠을 연 채 이동할 수 있다. 휠 키(QuickSlot) 자신은
    /// 이 잠금 중에도 계속 읽혀야 스스로 닫힌다.</item>
    /// </list>
    /// 우선순위는 <b>메뉴 &gt; 사망 &gt; 굴착 &gt; 휠</b>이다 — 겹치면 더 강한 잠금이 이긴다. 어느 쪽이든
    /// 자세(웅크리기)는 마지막 값으로 얼려서, 잠기는 것만으로 플레이어가 일어서지 않게 한다.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    public sealed class PlayerInputReader : NetworkBehaviour
    {
        [SerializeField] private InputActionAsset _inputActions;

        private InputActionAsset _runtimeActions;
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
        private bool _jumpQueued;
        private bool _inputLocked;
        private bool _deathLocked;
        private bool _skillLocked;
        private bool _wheelLocked;

        public Vector2 Move { get; private set; }
        public Vector2 Look { get; private set; }
        public bool AttackPressedThisFrame { get; private set; }
        public bool AttackReleasedThisFrame { get; private set; }
        public bool InteractPressedThisFrame { get; private set; }
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

        /// <summary>굴착 중이라 시점·스킬 키를 뺀 조작이 잠겼는가 → docs/project/mole-skill-system.md §5.5.1</summary>
        public bool IsSkillInputLocked => _skillLocked;

        /// <summary>퀵슬롯 휠이 열려 있어 시점을 뺀 조작이 잠겼는가 → docs/architecture/quick-slot.md</summary>
        public bool IsWheelInputLocked => _wheelLocked;

        /// <summary>
        /// 관전 자유비행 이동 입력(WASD, Move 액션 재사용) — <see cref="IsDeathInputLocked"/> 동안만 채워진다.
        /// </summary>
        public Vector2 SpectatorMove { get; private set; }

        /// <summary>관전 자유비행 상승 입력(Space, Jump 액션 재사용) — 누르는 동안 참.</summary>
        public bool SpectatorAscendHeld { get; private set; }

        /// <summary>관전 자유비행 하강 입력(Left Ctrl) — 누르는 동안 참.</summary>
        public bool SpectatorDescendHeld { get; private set; }

        /// <summary>관전 모드 전환 입력(V) — 자유시점 ↔ 플레이어 관전.</summary>
        public bool SpectatorToggleModePressedThisFrame { get; private set; }

        /// <summary>관전 대상 이전 전환 입력(좌클릭, Attack 액션 재사용).</summary>
        public bool SpectatorPreviousPressedThisFrame { get; private set; }

        /// <summary>관전 대상 다음 전환 입력(우클릭).</summary>
        public bool SpectatorNextPressedThisFrame { get; private set; }

        /// <summary>
        /// 굴착 스킬 토글 입력(§5.3) — 한 번 누르면 진입, 진입/유지 중 다시 누르면 즉시 종료.
        /// 키는 사용자 확정 T이며 Interact(E)와 분리된다 → 두더지 스킬 시스템 기획서(mole-skill-system.md).
        /// <b>굴착 잠금 중에도 이 입력만은 살아 있다</b> — 그래야 스스로 나올 수 있다.
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
            _inputLocked = false;
            _deathLocked = false;
            _skillLocked = false;
            _wheelLocked = false;
            ClearSpectatorInputs();
        }

        /// <summary>
        /// 굴착 중 조작 제한(§5.5.1). 잠긴 동안 <b>시야 회전과 굴착 키만</b> 살아 있고
        /// 이동·점프·던지기·상호작용·자세 전환은 전부 막힌다. 자세는 마지막 값으로 얼린다.
        ///
        /// 일시정지 메뉴 잠금과 독립이며, 둘이 겹치면 메뉴 쪽이 이긴다.
        /// </summary>
        public void SetSkillInputLocked(bool locked)
        {
            if (_skillLocked == locked)
                return;

            _skillLocked = locked;

            if (!locked)
                return;

            ClearBlockedInputs();
        }

        /// <summary>
        /// 사망 상태를 반영해 생존 조작을 잠그거나 푼다(관전 기획서 SP-1·SP-2, 사용자 확정
        /// 2026-09-12). 잠긴 동안 이동·시점·상호작용·스킬 입력은 전부 0이 되고, 대신
        /// <see cref="SpectatorMove"/> 등 관전 입력이 채워진다.
        ///
        /// 굴착·휠 잠금보다 우선한다(<see cref="Update"/> 참고) — 사망과 같은 프레임에 굴착
        /// 취소가 <see cref="SetSkillInputLocked"/>(false)를 먼저 불러도 이 잠금이 살아있는 한
        /// 생존 조작이 되살아나지 않는다. 일시정지 메뉴 잠금보다는 아래다.
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
        /// 퀵슬롯 휠이 열린 동안 조작을 잠그거나 푼다. 잠긴 동안 <b>시야 회전만</b> 0이 되고
        /// Attack·Interact·Burrow·Detect·Prone·Jump가 막힌다. Move·Crouch·Sprint는 살아 있다(QS-5,
        /// 사용자 확정 2026-09-12) — 휠을 연 채로 이동할 수 있다. 휠 입력(QuickSlot) 자신은 이
        /// 잠금의 영향을 받지 않는다 — 그래야 휠이 스스로 닫힌다.
        ///
        /// 일시정지 메뉴·굴착 잠금과 독립이며, 셋이 겹치면 메뉴 &gt; 굴착 &gt; 휠 순으로 이긴다.
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
            Move = Vector2.zero;
            AttackPressedThisFrame = false;
            AttackReleasedThisFrame = false;
            InteractPressedThisFrame = false;
            PronePressedThisFrame = false;
            SprintHeld = false;
            BurrowPressedThisFrame = false;
            DetectPressedThisFrame = false;
            QuickSlotHeld = false;
            QuickSlotPressedThisFrame = false;
            QuickSlotReleasedThisFrame = false;
            _jumpQueued = false;
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
            PronePressedThisFrame = false;
            BurrowPressedThisFrame = false;
            DetectPressedThisFrame = false;
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

            // 어떤 잠금 중에도 계속 갱신된다 — 퀵슬롯 휠이 카메라와 무관하게 자기 포인터를 누적할 때 쓴다.
            RawLookDelta = _lookAction.ReadValue<Vector2>();

            if (_inputLocked)
            {
                // 잠긴 동안에는 액션을 읽지 않는다. CrouchHeld 는 마지막 값 그대로 두어
                // 메뉴를 여는 것만으로 자세가 바뀌지 않게 한다. 관전 입력도 함께 잠근다
                // → docs/project/spectator-system.md §2.
                ClearBlockedInputs();
                Look = Vector2.zero;
                BurrowPressedThisFrame = false;
                ClearSpectatorInputs();
                return;
            }

            if (_deathLocked)
            {
                // 사망(SP-1·SP-2) — 생존 조작은 전부 버리고 관전 입력만 채운다. 굴착·휠 잠금보다
                // 우선하므로 이 검사가 먼저 와야 한다(§SetDeathInputLocked 참고).
                ClearBlockedInputs();
                Look = Vector2.zero;
                BurrowPressedThisFrame = false;
                PopulateSpectatorInputs();
                return;
            }

            if (_skillLocked)
            {
                // 굴착 중(§5.5.1) — 시야 회전과 굴착 키만 살린다. 굴착 키를 살려야 스스로 나온다.
                ClearBlockedInputs();
                Look = _lookAction.ReadValue<Vector2>();
                BurrowPressedThisFrame = _burrowAction.WasPressedThisFrame();
                DetectPressedThisFrame = false;
                ClearSpectatorInputs();
                return;
            }

            if (_wheelLocked)
            {
                // 퀵슬롯 휠이 열린 동안(QS-5) — 시야 회전만 막고 이동은 살려 둔다.
                // 휠 입력 자신은 계속 읽어야 스스로 닫힌다.
                ClearWheelBlockedInputs();
                Look = Vector2.zero;
                Move = _moveAction.ReadValue<Vector2>();
                CrouchHeld = _crouchAction.IsPressed();
                SprintHeld = _sprintAction.IsPressed();
                QuickSlotHeld = _quickSlotAction.IsPressed();
                QuickSlotPressedThisFrame = _quickSlotAction.WasPressedThisFrame();
                QuickSlotReleasedThisFrame = _quickSlotAction.WasReleasedThisFrame();
                ClearSpectatorInputs();
                return;
            }

            ClearSpectatorInputs();
            Move = _moveAction.ReadValue<Vector2>();
            Look = RawLookDelta;
            AttackPressedThisFrame = _attackAction.WasPressedThisFrame();
            AttackReleasedThisFrame = _attackAction.WasReleasedThisFrame();
            CrouchHeld = _crouchAction.IsPressed();
            SprintHeld = _sprintAction.IsPressed();

            // Interact 액션에는 Hold Interaction 이 붙어 있지만, WasPressedThisFrame 은
            // Interaction 의 phase 가 아니라 컨트롤이 눌린 순간을 보므로 탭이 씹히지 않는다.
            InteractPressedThisFrame = _interactAction.WasPressedThisFrame();
            BurrowPressedThisFrame = _burrowAction.WasPressedThisFrame();
            DetectPressedThisFrame = _detectAction.WasPressedThisFrame();
            PronePressedThisFrame = _proneAction.WasPressedThisFrame();
            QuickSlotHeld = _quickSlotAction.IsPressed();
            QuickSlotPressedThisFrame = _quickSlotAction.WasPressedThisFrame();
            QuickSlotReleasedThisFrame = _quickSlotAction.WasReleasedThisFrame();

            if (_jumpAction.WasPressedThisFrame())
                _jumpQueued = true;
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
