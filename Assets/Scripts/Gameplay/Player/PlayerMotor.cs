using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Gameplay.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerMotor : NetworkBehaviour
    {
        [SerializeField] private PlayerMoveSettings _settings;
        [SerializeField] private PlayerInputReader _input;

        private CharacterController _controller;
        private float _verticalVelocity;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
        }

        public override void OnNetworkSpawn()
        {
            if (!IsOwner)
                enabled = false;
        }

        /// <summary>
        /// 물리 코드지만 FixedUpdate 가 아니라 Update 에서 돈다.
        ///
        /// <see cref="CharacterController"/>는 Rigidbody 가 아니라 즉시 반영되는 스윕 이동이라
        /// 물리 스텝에 묶을 이유가 없다. 반대로 FixedUpdate(기본 50Hz)에 두면 프레임마다
        /// 그려지는 카메라(플레이어의 자식)가 물리 스텝 단위로만 움직여서, 화면 주사율이
        /// 50Hz 의 배수가 아닐 때 프레임 드랍처럼 보이는 미세한 떨림이 생긴다.
        /// 가구 <see cref="Rigidbody"/> 물리는 그대로 FixedUpdate 에 남는다.
        /// </summary>
        private void Update()
        {
            if (_settings == null || _input == null || !_controller.enabled)
                return;

            float deltaTime = Time.deltaTime;
            bool grounded = _controller.isGrounded;
            if (grounded && _verticalVelocity < 0f)
                _verticalVelocity = -2f;

            if (grounded && _input.ConsumeJump())
                _verticalVelocity = Mathf.Sqrt(_settings.JumpHeight * -2f * _settings.Gravity);

            _verticalVelocity += _settings.Gravity * deltaTime;

            Vector2 input = Vector2.ClampMagnitude(_input.Move, 1f);
            Vector3 planar = transform.right * input.x + transform.forward * input.y;
            float control = grounded ? 1f : _settings.AirControl;
            Vector3 velocity = planar * (_settings.MoveSpeed * control);
            velocity.y = _verticalVelocity;

            _controller.Move(velocity * deltaTime);
        }

        public void Teleport(Vector3 position, Quaternion rotation)
        {
            bool wasEnabled = _controller.enabled;
            _controller.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            _controller.enabled = wasEnabled;
            _verticalVelocity = 0f;
        }
    }
}
