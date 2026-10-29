using UnityEngine;

namespace GhostHunter.Gameplay.Player
{
    /// <summary>
    /// 드릴카 배터리 팩(헤드라이트 충전 오브젝트)의 수치 — 기획서 headlamp-system.md §8. 충전 속도 규칙의 숫자는 기획 확정값,
    /// 총량·개수는 구현에서 정한 임시값이다(BP-1).
    /// </summary>
    [CreateAssetMenu(fileName = "BatteryPackSettings_", menuName = "GhostHunter/Gameplay/Battery Pack Settings")]
    public sealed class BatteryPackSettings : ScriptableObject
    {
        [Header("총량 (임시 — BP-1)")]
        [Tooltip("스테이지 시작 시 배터리 팩 잔량(%). 모든 플레이어가 공유한다. 헤드라이트 배터리 100%가 이 값에서 빠져나간다.")]
        [SerializeField, Min(1f)] private float _capacity = 200f;

        [Header("충전 속도 (§8.2 확정)")]
        [Tooltip("E를 처음 홀드했을 때 초당 충전량(%p).")]
        [SerializeField, Min(0f)] private float _baseRatePerSecond = 10f;

        [Tooltip("연속 홀드 단계마다 늘어나는 초당 충전량(%p).")]
        [SerializeField, Min(0f)] private float _rateStep = 5f;

        [Tooltip("초당 충전량이 한 단계 늘어나는 연속 홀드 시간(초).")]
        [SerializeField, Min(0.1f)] private float _stepSeconds = 1f;

        [Tooltip("초당 충전량 상한(%p).")]
        [SerializeField, Min(0f)] private float _maxRatePerSecond = 30f;

        [Header("조작·네트워크")]
        [Tooltip("충전을 시작할 수 있는 최대 거리(m). 서버는 이 값의 1.5배까지 받아 준다.")]
        [SerializeField, Min(0.5f)] private float _useDistance = 2.5f;

        [Tooltip("소유자가 충전량을 서버에 청구하는 간격(초).")]
        [SerializeField, Min(0.02f)] private float _requestInterval = 0.1f;

        [Tooltip("서버가 받아 주는 충전량 여유 배율 — 지연으로 청구가 몰려도 거절하지 않도록.")]
        [SerializeField, Min(1f)] private float _serverRateTolerance = 1.5f;

        [Header("표시")]
        [SerializeField] private Color _displayColor = new(1f, 0.78f, 0.1f, 1f);
        [SerializeField] private Color _emptyDisplayColor = new(0.5f, 0.5f, 0.5f, 1f);

        public float Capacity => _capacity;
        public float BaseRatePerSecond => _baseRatePerSecond;
        public float RateStep => _rateStep;
        public float StepSeconds => _stepSeconds;
        public float MaxRatePerSecond => Mathf.Max(_baseRatePerSecond, _maxRatePerSecond);
        public float UseDistance => _useDistance;
        public float RequestInterval => _requestInterval;
        public float ServerRateTolerance => _serverRateTolerance;
        public Color DisplayColor => _displayColor;
        public Color EmptyDisplayColor => _emptyDisplayColor;

        public BatteryPackCharging CreateCharging() =>
            new(_baseRatePerSecond, _rateStep, _stepSeconds, MaxRatePerSecond);
    }
}
