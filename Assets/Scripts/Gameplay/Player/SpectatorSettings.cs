using UnityEngine;

namespace GhostHunter.Gameplay.Player
{
    /// <summary>
    /// 사망 후 관전(자유시점) 튜닝 값. 관전 기획서 SP-3(사용자 확정 2026-09-12: 고정 속도,
    /// 가속·빠른 이동 키 없음)의 구체적 수치는 사용자가 확정하지 않았다 — 아래 기본값은
    /// 구현자 임시값이며 밸런스 확정이 아니다 → docs/project/spectator-system.md §5.
    /// </summary>
    [CreateAssetMenu(
        fileName = "SpectatorSettings_Default",
        menuName = "GhostHunter/Gameplay/Spectator Settings")]
    public sealed class SpectatorSettings : ScriptableObject
    {
        [Header("자유비행 (임시값 — SP-3)")]
        [Tooltip("자유비행 이동 속도(m/s). 고정 속도 — 가속·빠른 이동 없음(SP-3 확정). 수치는 임시값.")]
        [SerializeField, Min(0.1f)] private float _flySpeed = 6f;

        [Tooltip("자유비행 시점 감도. 생존자 시점(PlayerMoveSettings.MouseSensitivity)과 별개다.")]
        [SerializeField, Min(0.01f)] private float _lookSensitivity = 0.1f;

        [Tooltip("자유비행 상하 시점 한계(도).")]
        [SerializeField, Range(1f, 89f)] private float _pitchLimit = 89f;

        public float FlySpeed => _flySpeed;
        public float LookSensitivity => _lookSensitivity;
        public float PitchLimit => _pitchLimit;

        private void OnValidate()
        {
            _flySpeed = Mathf.Max(0.1f, _flySpeed);
            _lookSensitivity = Mathf.Max(0.01f, _lookSensitivity);
            _pitchLimit = Mathf.Clamp(_pitchLimit, 1f, 89f);
        }
    }
}
