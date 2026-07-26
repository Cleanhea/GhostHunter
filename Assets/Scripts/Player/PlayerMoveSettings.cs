using UnityEngine;

namespace GhostHunter.Player
{
    [CreateAssetMenu(
        fileName = "PlayerMoveSettings_Default",
        menuName = "GhostHunter/Player Move Settings")]
    public sealed class PlayerMoveSettings : ScriptableObject
    {
        [Header("이동")]
        [SerializeField, Min(0f)] private float _moveSpeed = 5f;
        [SerializeField, Range(0f, 1f)] private float _airControl = 0.4f;
        [SerializeField, Min(0f)] private float _jumpHeight = 1.2f;
        [SerializeField] private float _gravity = -20f;

        [Header("시점")]
        [SerializeField, Min(0f)] private float _mouseSensitivity = 0.1f;
        [SerializeField, Range(1f, 89f)] private float _pitchLimit = 89f;

        public float MoveSpeed => _moveSpeed;
        public float AirControl => _airControl;
        public float JumpHeight => _jumpHeight;
        public float Gravity => _gravity;
        public float MouseSensitivity => _mouseSensitivity;
        public float PitchLimit => _pitchLimit;
    }
}
