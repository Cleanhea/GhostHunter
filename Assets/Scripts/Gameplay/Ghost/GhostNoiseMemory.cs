using UnityEngine;

namespace GhostHunter.Gameplay.Ghost
{
    /// <summary>
    /// 소음 수준(원문 0.4 §7.3 표의 일부). 걷기·달리기 발소리는 기존 시야/청각 탐지(<see cref="GhostPerception"/>)가
    /// 처리하므로 여기에는 "조사 지점"을 만드는 소리만 둔다. 값이 클수록 우선한다.
    /// </summary>
    internal enum GhostNoiseLevel : byte
    {
        None = 0,
        /// <summary>작은 물건 놓기 — "보통".</summary>
        Medium = 1,
        /// <summary>대형 가구 이동 — "높음".</summary>
        High = 2,
        /// <summary>가구 충돌·낙하 — "매우 높음", 우선 조사.</summary>
        VeryHigh = 3,
    }

    /// <summary>
    /// 조사하지 못한 가장 중요한 소리 하나를 보관한다(원문 0.4 §7: 소리 조사 최우선, 소리 > 활동량 > 배회).
    /// 높은 수준이 낮은 수준을 덮고, 같은 수준이면 최신이 이긴다. 기억 시간이 지나면 버린다.
    /// </summary>
    internal sealed class GhostNoiseMemory
    {
        public bool HasNoise { get; private set; }
        public Vector3 Position { get; private set; }
        public GhostNoiseLevel Level { get; private set; }
        public float ReportedAt { get; private set; }

        public void Clear()
        {
            HasNoise = false;
            Level = GhostNoiseLevel.None;
        }

        public void Report(Vector3 position, GhostNoiseLevel level, float time, float memorySeconds)
        {
            if (level == GhostNoiseLevel.None)
                return;
            if (HasNoise && time - ReportedAt <= memorySeconds && level < Level)
                return;

            HasNoise = true;
            Position = position;
            Level = level;
            ReportedAt = time;
        }

        /// <summary>유효한 소리가 있으면 꺼내며(소비) true. 만료됐으면 버리고 false.</summary>
        public bool TryTake(float time, float memorySeconds, out Vector3 position)
        {
            position = default;
            if (!HasNoise)
                return false;

            bool fresh = time - ReportedAt <= memorySeconds;
            position = Position;
            Clear();
            return fresh;
        }
    }
}
