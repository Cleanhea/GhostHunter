using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GhostHunter.Player
{
    /// <summary>
    /// 프로젝트의 InputActionAsset을 로컬 플레이어별로 복제한다. 원격 플레이어가 공유 에셋을
    /// 활성화해 내 입력을 읽는 일을 막기 위해 소유자에서만 런타임 복제본을 켠다.
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
        private bool _jumpQueued;

        public Vector2 Move { get; private set; }
        public Vector2 Look { get; private set; }
        public bool AttackPressedThisFrame { get; private set; }
        public bool AttackReleasedThisFrame { get; private set; }

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
            _runtimeActions.Enable();
        }

        public override void OnNetworkDespawn()
        {
            if (_runtimeActions == null)
                return;

            _runtimeActions.Disable();
            Destroy(_runtimeActions);
            _runtimeActions = null;
        }

        private void Update()
        {
            if (_runtimeActions == null)
                return;

            Move = _moveAction.ReadValue<Vector2>();
            Look = _lookAction.ReadValue<Vector2>();
            AttackPressedThisFrame = _attackAction.WasPressedThisFrame();
            AttackReleasedThisFrame = _attackAction.WasReleasedThisFrame();

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
