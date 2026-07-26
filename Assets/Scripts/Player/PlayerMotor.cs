using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Player
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

        private void FixedUpdate()
        {
            if (_settings == null || _input == null || !_controller.enabled)
                return;

            bool grounded = _controller.isGrounded;
            if (grounded && _verticalVelocity < 0f)
                _verticalVelocity = -2f;

            if (grounded && _input.ConsumeJump())
                _verticalVelocity = Mathf.Sqrt(_settings.JumpHeight * -2f * _settings.Gravity);

            _verticalVelocity += _settings.Gravity * Time.fixedDeltaTime;

            Vector2 input = Vector2.ClampMagnitude(_input.Move, 1f);
            Vector3 planar = transform.right * input.x + transform.forward * input.y;
            float control = grounded ? 1f : _settings.AirControl;
            Vector3 velocity = planar * (_settings.MoveSpeed * control);
            velocity.y = _verticalVelocity;

            _controller.Move(velocity * Time.fixedDeltaTime);
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
