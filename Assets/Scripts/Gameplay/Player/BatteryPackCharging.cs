using UnityEngine;

namespace GhostHunter.Gameplay.Player
{
    /// <summary>
    /// 배터리 팩 충전 속도 규칙(기획서 headlamp-system.md §8.2). E를 연속으로 홀드하면 일정 시간마다 초당 충전량이 늘어나
    /// 최대치에서 멈추고, 떼면 처음부터 다시 시작한다. <c>MonoBehaviour</c> 가 아닌 순수 클래스라 EditMode 에서 검증한다.
    /// </summary>
    public sealed class BatteryPackCharging
    {
        private readonly float _baseRate;
        private readonly float _stepRate;
        private readonly float _stepSeconds;
        private readonly float _maxRate;

        public BatteryPackCharging(float baseRate, float stepRate, float stepSeconds, float maxRate)
        {
            _baseRate = Mathf.Max(0f, baseRate);
            _stepRate = Mathf.Max(0f, stepRate);
            _stepSeconds = Mathf.Max(0.01f, stepSeconds);
            _maxRate = Mathf.Max(_baseRate, maxRate);
        }

        /// <summary>연속으로 홀드한 시간(초).</summary>
        public float HeldSeconds { get; private set; }

        /// <summary>지금 초당 충전량(%p). 연속 홀드 시간으로 정해진다.</summary>
        public float CurrentRate => RateAt(HeldSeconds);

        /// <summary>연속 홀드 <paramref name="heldSeconds"/> 시점의 초당 충전량 — 기본 + 단계 × 지난 단계 수, 최대치로 자른다.</summary>
        public float RateAt(float heldSeconds) =>
            Mathf.Min(_maxRate, _baseRate + _stepRate * Mathf.Floor(Mathf.Max(0f, heldSeconds) / _stepSeconds));

        /// <summary>
        /// 한 프레임 진행. <paramref name="charging"/> 이 거짓이면(홀드 해제·가득 참·잔량 0·조준 이탈 등) 속도를 처음으로 되돌리고 0을 돌려준다.
        /// 참이면 이번 프레임에 충전할 양(%p)을 돌려주고 홀드 시간을 늘린다 — 단계는 그 프레임 시작 시점 속도로 계산한다.
        /// </summary>
        public float Tick(float deltaTime, bool charging)
        {
            if (!charging)
            {
                HeldSeconds = 0f;
                return 0f;
            }

            if (deltaTime <= 0f)
                return 0f;

            float amount = CurrentRate * deltaTime;
            HeldSeconds += deltaTime;
            return amount;
        }

        public void Reset() => HeldSeconds = 0f;
    }
}
