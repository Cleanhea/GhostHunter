using UnityEngine;

namespace GhostHunter.Gameplay.Revival
{
    /// <summary>
    /// 부활 의식 튜닝 값(revival-system.md). 기획서에 수치가 없는 값은 [TEMP] 이다 — 3초 보호·1초 소등·2% 저주만 원문 값.
    /// 의식 방은 스테이지마다 무작위로 골라 가구·얼룩을 비우고, 가운데 마법진 + 둘레 촛대 5개로 꾸민다(2026-09-30 사용자 요청).
    /// </summary>
    [CreateAssetMenu(menuName = "GhostHunter/Gameplay/Revival Ritual Settings", fileName = "RevivalRitualSettings")]
    public sealed class RevivalRitualSettings : ScriptableObject
    {
        [Header("소환진 [TEMP]")]
        [Tooltip("시체가 이 반경(m, 수평) 안에 있으면 '소환진 중앙에 배치'로 친다.")]
        [SerializeField, Min(0.2f)] private float _corpseRadius = 0.8f;

        [Tooltip("촛대 5개가 서는 원 반경(m) — 마법진 오망성 꼭짓점이 여기 닿도록 마법진 크기가 이 값에서 정해진다.")]
        [SerializeField, Min(0.3f)] private float _candleRingRadius = 1.45f;

        [Tooltip("의식 방 후보의 최소 가로·세로(m). 작은 욕실·창고·현관은 마법진과 촛대가 들어가지 않는다.")]
        [SerializeField, Min(1f)] private float _minRoomSize = 4.1f;

        [Tooltip("촛불 자리와 상호작용하는 최대 거리(m). 서버가 다시 검사한다.")]
        [SerializeField, Min(1f)] private float _interactDistance = 3f;

        [Header("타이밍 미니게임 [TEMP] (2026-09-30 사용자 확정: 이동 커서 + 랜덤 초록 구간)")]
        [Tooltip("커서가 바를 한 번 지나가는 시간(초).")]
        [SerializeField, Min(0.3f)] private float _barSeconds = 1.5f;

        [Tooltip("초록 구간 폭(바 전체 대비 0~1).")]
        [SerializeField, Range(0.05f, 0.8f)] private float _zoneWidth = 0.2f;

        [Tooltip("초록 구간 시작 위치 범위 — 너무 앞이면 바로 눌러 버린다.")]
        [SerializeField, Range(0f, 1f)] private float _zoneMinStart = 0.3f;

        [Tooltip("클라이언트가 보고한 누름 시점을 서버 시간과 비교할 때 봐주는 지연(초).")]
        [SerializeField, Min(0f)] private float _pressToleranceSeconds = 0.4f;

        [Header("판정·부활 (원문 수치)")]
        [Tooltip("실패한 촛불이 켜진 뒤 꺼지기까지(초) — §6 '1초 후 꺼진다'.")]
        [SerializeField, Min(0f)] private float _failedCandleOutSeconds = 1f;

        [Tooltip("다섯 개가 다 켜진 뒤 부활하기까지(초) [TEMP]. 실패 촛불이 꺼지는 걸 볼 시간을 준다.")]
        [SerializeField, Min(0f)] private float _reviveDelaySeconds = 2f;

        [Tooltip("부활 직후 보호 시간(초) — §8 '3초'.")]
        [SerializeField, Min(0f)] private float _protectionSeconds = 3f;

        [Header("개발 [TEMP]")]
        [Tooltip("상점 서비스가 없는 경우(부트스트랩 없이 도는 테스트)에만 쓰는 촛불 재고. 로컬 세션도 상점 재고를 쓴다.")]
        [SerializeField, Min(0)] private int _devCandleStock = 10;

        public float CorpseRadius => _corpseRadius;
        /// <summary>바닥 마법진 반경(m) — 오망성 꼭짓점이 촛대 위에 오도록 촛대 원에서 구한다.</summary>
        public float CircleRadius => _candleRingRadius / RevivalCircleTexture.StarRadius;
        public float CandleRingRadius => _candleRingRadius;
        public float MinRoomSize => _minRoomSize;
        public float InteractDistance => _interactDistance;
        public float BarSeconds => _barSeconds;
        public float ZoneWidth => _zoneWidth;
        public float ZoneMinStart => _zoneMinStart;
        public float PressToleranceSeconds => _pressToleranceSeconds;
        public float FailedCandleOutSeconds => _failedCandleOutSeconds;
        public float ReviveDelaySeconds => Mathf.Max(_reviveDelaySeconds, _failedCandleOutSeconds);
        public float ProtectionSeconds => _protectionSeconds;
        public int DevCandleStock => _devCandleStock;
    }
}
