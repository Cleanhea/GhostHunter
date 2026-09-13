using UnityEngine;

namespace GhostHunter.Gameplay.FurnitureDriver
{
    /// <summary>
    /// 가구용 멀티 드라이버의 전역 수치(기획서 §3.2·§3.3·§5). 값은 전부 임시이며
    /// 사용자가 확정한 규칙(3초/9초, 5 감소, 임계 15)만 기본값으로 반영했다.
    /// </summary>
    [CreateAssetMenu(fileName = "FurnitureDriverSettings_", menuName = "GhostHunter/Furniture Driver/Settings")]
    public sealed class FurnitureDriverSettings : ScriptableObject
    {
        [Header("행동 시간 (§3.2)")]
        [SerializeField, Min(0.1f)] private float _actionSecondsHighDurability = 3f;
        [SerializeField, Min(0.1f)] private float _actionSecondsLowDurability = 9f;
        [SerializeField, Range(0, 100)] private int _durabilityThreshold = 15;

        [Header("아이템 내구도 (§3.3)")]
        [SerializeField, Range(0, 100)] private int _startingItemDurability = 100;
        [SerializeField, Min(0)] private int _durabilityDecreasePerUse = 5;

        [Header("조준·연출")]
        [SerializeField, Min(0.1f)] private float _useDistance = 3f;
        [SerializeField, Min(0.02f)] private float _requestInterval = 0.2f;
        [Tooltip("§6.2·§6.3 — 생성된 가구가 떨어지기 시작하는 지면 위 높이(m).")]
        [SerializeField, Min(0f)] private float _dropHeight = 1f;

        public float ActionSecondsHighDurability => _actionSecondsHighDurability;
        public float ActionSecondsLowDurability => _actionSecondsLowDurability;
        public int DurabilityThreshold => _durabilityThreshold;
        public int StartingItemDurability => _startingItemDurability;
        public int DurabilityDecreasePerUse => _durabilityDecreasePerUse;
        public float UseDistance => _useDistance;
        public float RequestInterval => _requestInterval;
        public float DropHeight => _dropHeight;

        /// <summary>시작 시점 내구도로 판정하는 행동 시간(§3.2 — 완료까지 값이 바뀌어도 고정).</summary>
        public float ActionSecondsFor(int durabilityAtStart) =>
            durabilityAtStart > _durabilityThreshold ? _actionSecondsHighDurability : _actionSecondsLowDurability;

        /// <summary>테스트·에디터 설치 도구가 수치를 지정한다.</summary>
        public void Configure(float actionSecondsHighDurability, float actionSecondsLowDurability,
            int durabilityThreshold, int startingItemDurability, int durabilityDecreasePerUse,
            float useDistance, float requestInterval, float dropHeight)
        {
            _actionSecondsHighDurability = actionSecondsHighDurability;
            _actionSecondsLowDurability = actionSecondsLowDurability;
            _durabilityThreshold = durabilityThreshold;
            _startingItemDurability = startingItemDurability;
            _durabilityDecreasePerUse = durabilityDecreasePerUse;
            _useDistance = useDistance;
            _requestInterval = requestInterval;
            _dropHeight = dropHeight;
        }
    }
}
