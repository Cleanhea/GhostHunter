using UnityEngine;

namespace GhostHunter.Gameplay.Player
{
    /// <summary>
    /// 굴착 스킬(두더지 스킬 시스템 기획서 §5)의 튜닝 값.
    /// **프로토타입 값이다.** 시전 시간은 기획 미결정(MS-10)이라 임의로 채웠고,
    /// 재사용 대기는 사용자 확정값 10초다 — 최종 권위는 이 에셋이다.
    /// </summary>
    [CreateAssetMenu(
        fileName = "MoleBurrowSettings_Default",
        menuName = "GhostHunter/Mole Burrow Settings")]
    public sealed class MoleBurrowSettings : ScriptableObject
    {
        [Header("지속 시간")]
        [Tooltip("입력 후 실제로 땅속에 숨기까지의 시전 시간(초). 기획 미결정(MS-10) — 임의값.")]
        [SerializeField, Min(0f)] private float _enterCastSeconds = 0.3f;

        [Tooltip("땅속에 머무를 수 있는 최대 시간(초). §5.3 — 5초.")]
        [SerializeField, Min(0.1f)] private float _maxBurrowDuration = 5f;

        [Tooltip("종료 후 재사용 대기 시간(초). 사용자 확정값 10초.")]
        [SerializeField, Min(0f)] private float _cooldownSeconds = 10f;

        [Header("연출")]
        [Tooltip("굴착 중 카메라의 목표 로컬 높이(m) — 바닥(0)보다 살짝 위. 임의값.")]
        [SerializeField, Min(0f)] private float _burrowedCameraHeight = 0.15f;

        [Header("튀어오름 (§5.4)")]
        [Tooltip("목표 도약 높이(m). '2층을 바로 올라갈 수 있을 정도' 기준값 — 임의로 4m.")]
        [SerializeField, Min(0.1f)] private float _popHeight = 4f;

        [Tooltip("도약 속도를 계산할 때 쓰는 중력(m/s²). PlayerMoveSettings와 별개로 이 스킬 전용으로 둔다.")]
        [SerializeField] private float _gravity = -20f;

        public float EnterCastSeconds => _enterCastSeconds;
        public float BurrowedCameraHeight => _burrowedCameraHeight;
        public float MaxBurrowDuration => _maxBurrowDuration;
        public float CooldownSeconds => _cooldownSeconds;
        public float PopHeight => _popHeight;
        public float Gravity => _gravity;

        /// <summary>점프와 같은 공식으로 목표 높이에 도달할 초기 수직 속도를 구한다.</summary>
        public float ComputePopLaunchSpeed()
        {
            return Mathf.Sqrt(_popHeight * -2f * _gravity);
        }
    }
}
