using UnityEngine;

namespace GhostHunter.Gameplay.Player
{
    [CreateAssetMenu(
        fileName = "PlayerMoveSettings_Default",
        menuName = "GhostHunter/Player Move Settings")]
    public sealed class PlayerMoveSettings : ScriptableObject
    {
        [Header("이동")]
        [SerializeField, Min(0f)] private float _moveSpeed = 5f;
        [SerializeField, Min(0f)] private float _crouchMoveSpeed = 3.5f;

        [Tooltip("달리기(Sprint) 중 기본 이동 속도에 곱하는 배수. 웅크리는 중에는 적용되지 않는다.")]
        [SerializeField, Min(1f)] private float _sprintMultiplier = 1.4f;

        [SerializeField, Range(0f, 1f)] private float _airControl = 0.4f;
        [SerializeField, Min(0f)] private float _jumpHeight = 1.2f;
        [SerializeField] private float _gravity = -20f;

        [Header("웅크리기")]
        [SerializeField, Min(0.2f)] private float _standingHeight = 1.8f;
        [SerializeField, Min(0.2f)] private float _crouchHeight = 1.2f;
        [SerializeField, Min(0f)] private float _standingCameraHeight = 1.65f;
        [SerializeField, Min(0f)] private float _crouchCameraHeight = 1.05f;

        [Tooltip("캡슐·카메라·원격 몸통이 목표 높이로 변하는 속도(m/s).")]
        [SerializeField, Min(0.01f)] private float _postureTransitionSpeed = 4f;

        [Header("엎드리기 (Z 토글)")]
        [Tooltip("엎드린 캡슐 높이(m). 침대 밑 공간보다 낮아야 기어 들어갈 수 있다.")]
        [SerializeField, Min(0.2f)] private float _proneHeight = 0.5f;

        [Tooltip("엎드린 카메라의 로컬 높이(m). 바닥에 뺨을 댄 시점.")]
        [SerializeField, Min(0f)] private float _proneCameraHeight = 0.35f;

        [Tooltip("엎드려 기어서 이동하는 속도(m/s). 웅크리기보다 느리다.")]
        [SerializeField, Min(0f)] private float _proneMoveSpeed = 1.4f;

        [Header("시점")]
        [SerializeField, Min(0f)] private float _mouseSensitivity = 0.1f;
        [SerializeField, Range(1f, 89f)] private float _pitchLimit = 89f;

        public float MoveSpeed => _moveSpeed;
        public float CrouchMoveSpeed => _crouchMoveSpeed;
        public float SprintMultiplier => _sprintMultiplier;
        public float AirControl => _airControl;
        public float JumpHeight => _jumpHeight;
        public float Gravity => _gravity;
        public float StandingHeight => _standingHeight;
        public float CrouchHeight => _crouchHeight;
        public float ProneHeight => _proneHeight;
        public float StandingCameraHeight => _standingCameraHeight;
        public float CrouchCameraHeight => _crouchCameraHeight;
        public float ProneCameraHeight => _proneCameraHeight;
        public float ProneMoveSpeed => _proneMoveSpeed;
        public float PostureTransitionSpeed => _postureTransitionSpeed;
        public float MouseSensitivity => _mouseSensitivity;
        public float PitchLimit => _pitchLimit;

        private void OnValidate()
        {
            _crouchMoveSpeed = Mathf.Min(_crouchMoveSpeed, _moveSpeed);
            _sprintMultiplier = Mathf.Max(1f, _sprintMultiplier);
            _crouchHeight = Mathf.Min(_crouchHeight, _standingHeight);
            _crouchCameraHeight = Mathf.Min(_crouchCameraHeight, _standingCameraHeight);

            // 엎드리기는 웅크리기보다 낮고 느리다 — 자세 우선순위(엎드리기 > 웅크리기 > 서기)와 맞춘다.
            _proneHeight = Mathf.Min(_proneHeight, _crouchHeight);
            _proneCameraHeight = Mathf.Min(_proneCameraHeight, _crouchCameraHeight);
            _proneMoveSpeed = Mathf.Min(_proneMoveSpeed, _crouchMoveSpeed);
        }
    }
}
