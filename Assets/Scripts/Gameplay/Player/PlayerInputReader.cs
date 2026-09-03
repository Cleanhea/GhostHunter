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
    /// 일시정지 메뉴가 열린 동안에는 <see cref="SetGameplayInputLocked"/> 로 모든 게임플레이
    /// 입력을 0으로 만든다. 자세(웅크리기)만은 마지막 값으로 얼려서 메뉴를 여는 것만으로
    /// 플레이어가 일어서지 않게 한다.
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
        private InputAction _proneAction;
        private bool _jumpQueued;
        private bool _inputLocked;

        public Vector2 Move { get; private set; }
        public Vector2 Look { get; private set; }
        public bool AttackPressedThisFrame { get; private set; }
        public bool AttackReleasedThisFrame { get; private set; }
        public bool InteractPressedThisFrame { get; private set; }
        public bool CrouchHeld { get; private set; }

        /// <summary>달리기 입력(기본 Left Shift). 유지하는 동안 참.</summary>
        public bool SprintHeld { get; private set; }

        /// <summary>일시정지 메뉴가 게임플레이 입력을 잠갔는가 → docs/project/pause-menu-system.md §3.4</summary>
        public bool IsGameplayInputLocked => _inputLocked;

        /// <summary>
        /// 굴착 스킬 토글 입력(§5.3) — 한 번 누르면 진입, 진입/유지 중 다시 누르면 즉시 종료.
        /// 키는 사용자 확정 R이며 Interact(E)와 분리된다 → 두더지 스킬 시스템 기획서(mole-skill-system.md).
        /// </summary>
        public bool BurrowPressedThisFrame { get; private set; }

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
            _proneAction = _runtimeActions.FindAction("Player/Prone", true);
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
            _inputLocked = false;
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

            Move = Vector2.zero;
            Look = Vector2.zero;
            AttackPressedThisFrame = false;
            AttackReleasedThisFrame = false;
            InteractPressedThisFrame = false;
            BurrowPressedThisFrame = false;
            PronePressedThisFrame = false;
            SprintHeld = false;
            _jumpQueued = false;
        }

        private void Update()
        {
            if (_runtimeActions == null)
                return;

            if (_inputLocked)
            {
                // 잠긴 동안에는 액션을 읽지 않는다. CrouchHeld 는 마지막 값 그대로 두어
                // 메뉴를 여는 것만으로 자세가 바뀌지 않게 한다.
                Move = Vector2.zero;
                Look = Vector2.zero;
                AttackPressedThisFrame = false;
                AttackReleasedThisFrame = false;
                InteractPressedThisFrame = false;
                BurrowPressedThisFrame = false;
                PronePressedThisFrame = false;
                SprintHeld = false;
                _jumpQueued = false;
                return;
            }

            Move = _moveAction.ReadValue<Vector2>();
            Look = _lookAction.ReadValue<Vector2>();
            AttackPressedThisFrame = _attackAction.WasPressedThisFrame();
            AttackReleasedThisFrame = _attackAction.WasReleasedThisFrame();
            CrouchHeld = _crouchAction.IsPressed();
            SprintHeld = _sprintAction.IsPressed();

            // Interact 액션에는 Hold Interaction 이 붙어 있지만, WasPressedThisFrame 은
            // Interaction 의 phase 가 아니라 컨트롤이 눌린 순간을 보므로 탭이 씹히지 않는다.
            InteractPressedThisFrame = _interactAction.WasPressedThisFrame();
            BurrowPressedThisFrame = _burrowAction.WasPressedThisFrame();
            PronePressedThisFrame = _proneAction.WasPressedThisFrame();

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
