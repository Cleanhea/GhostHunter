using UnityEngine;

namespace GhostHunter.Gameplay.Theft
{
    /// <summary>
    /// 도둑질 수치·UI 규격(theft-system.md). 색·치수·테두리 2초는 기획 원문 값이고, <b>[TEMP]</b> 표시는 원문에 없어 구현에서
    /// 정한 임시값이다 — 기획 확인 전까지 확정이 아니다(TH-Q5~7).
    /// </summary>
    [CreateAssetMenu(fileName = "TheftSettings_", menuName = "GhostHunter/Gameplay/Theft Settings")]
    public sealed class TheftSettings : ScriptableObject
    {
        [Header("판정 [TEMP]")]
        [Tooltip("서버가 받아 주는 눈~귀중품 콜라이더 거리(m). 조준 거리(PlayerInteractor 2.5m)에 지연 여유를 더했다.")]
        [SerializeField, Min(0.5f)] private float _serverReachDistance = 3.5f;

        [Tooltip("서버 거리 검사에 쓰는 발 → 눈 높이(m). 플레이어 카메라 높이와 맞춘다.")]
        [SerializeField, Min(0f)] private float _eyeHeight = 1.2f;

        [Header("UI 기준 해상도 (원문 1920×1080)")]
        [SerializeField] private Vector2 _referenceResolution = new(1920f, 1080f);

        [Header("\"E 훔치기\" 안내 (원문 §3-1 — 이미지 실측 근사값)")]
        [Tooltip("오른쪽 여백(px, 1080p). 원문: 패널 왼쪽 위 (1410, 869), 크기 132×50 → 오른쪽 378.")]
        [SerializeField, Min(0f)] private float _promptRightMargin = 378f;

        [Tooltip("아래 여백(px, 1080p). 원문 161.")]
        [SerializeField, Min(0f)] private float _promptBottomMargin = 161f;

        [SerializeField] private Vector2 _promptSize = new(132f, 50f);

        [Tooltip("[TEMP] 안내 글자 크기(px, 1080p). 원문에 없다.")]
        [SerializeField, Min(8)] private int _promptFontSize = 20;

        [Header("\"+ n$\" 가격 표시 (원문 §3-2)")]
        [Tooltip("원문 #54d6ad.")]
        [SerializeField] private Color _priceColor = new(0x54 / 255f, 0xd6 / 255f, 0xad / 255f, 1f);

        [Tooltip("화면 세로 중앙선에서 글자 위쪽까지(px, 1080p). 원문 주석 61px — 기준 해상도 미기재(TH-Q7).")]
        [SerializeField, Min(0f)] private float _priceOffsetBelowCenter = 61f;

        [Tooltip("[TEMP] 가격 글자 크기(px, 1080p).")]
        [SerializeField, Min(8)] private int _priceFontSize = 40;

        [Tooltip("[TEMP] 가격을 또렷하게 보여 주는 시간(초). 원문은 \"도둑질 애니메이션이 진행되는 동안\"(TH-Q5).")]
        [SerializeField, Min(0.1f)] private float _priceShowSeconds = 1.2f;

        [Tooltip("[TEMP] 보여 준 뒤 선형으로 사라지는 시간(초).")]
        [SerializeField, Min(0f)] private float _priceFadeSeconds = 0.6f;

        [Tooltip("[TEMP] 처음 커졌다가 원래 크기로 선형으로 돌아오는 시간(초).")]
        [SerializeField, Min(0f)] private float _pricePopSeconds = 0.18f;

        [Tooltip("[TEMP] 나타날 때의 시작 배율.")]
        [SerializeField, Min(1f)] private float _pricePopScale = 1.3f;

        [Tooltip("[TEMP] 보이는 동안 위로 선형으로 떠오르는 거리(px, 1080p).")]
        [SerializeField, Min(0f)] private float _priceRisePixels = 28f;

        [Header("정신력 회복 테두리 (원문 §3-3 — 포스트프로세싱)")]
        [Tooltip("원문 #c2fcf3.")]
        [SerializeField] private Color _glowColor = new(0xc2 / 255f, 0xfc / 255f, 0xf3 / 255f, 1f);

        [Tooltip("원문 2초.")]
        [SerializeField, Min(0.1f)] private float _glowSeconds = 2f;

        [Tooltip("[TEMP] 가장자리에서 안쪽으로 빛이 선형으로 차오르는 시간(초). 나머지 시간 동안 선형으로 빠진다(TH-Q6).")]
        [SerializeField, Min(0.01f)] private float _glowFillSeconds = 0.5f;

        [Tooltip("[TEMP] 다 차올랐을 때 빛이 화면 모서리(타원 거리 1.414)에서 안쪽으로 번지는 폭(타원 거리 — 변 가운데 1, 가운데 0). 0.62 면 변 가운데에는 아주 은은하게만 닿는다.")]
        [SerializeField, Range(0.05f, 1.414f)] private float _glowEdgeWidth = 0.62f;

        [Tooltip("[TEMP] 가장 밝을 때 화면 맨 가장자리에 더하는 테두리 색의 양. 장면을 덮지 않고 더한다.")]
        [SerializeField, Range(0f, 1f)] private float _glowStrength = 0.75f;

        public float ServerReachDistance => _serverReachDistance;
        public float EyeHeight => _eyeHeight;
        public Vector2 ReferenceResolution => _referenceResolution;
        public float PromptRightMargin => _promptRightMargin;
        public float PromptBottomMargin => _promptBottomMargin;
        public Vector2 PromptSize => _promptSize;
        public int PromptFontSize => _promptFontSize;
        public Color PriceColor => _priceColor;
        public float PriceOffsetBelowCenter => _priceOffsetBelowCenter;
        public int PriceFontSize => _priceFontSize;
        public float PriceShowSeconds => _priceShowSeconds;
        public float PriceFadeSeconds => _priceFadeSeconds;
        public Color GlowColor => _glowColor;
        public float GlowSeconds => _glowSeconds;
        /// <summary>차오르기 시작할 때의 빛 폭(최대 폭 대비).</summary>
        public const float GlowStartWidthRatio = 0.35f;

        public float GlowFillSeconds => Mathf.Min(_glowFillSeconds, _glowSeconds);
        public float GlowEdgeWidth => _glowEdgeWidth;
        public float GlowStrength => _glowStrength;

        /// <summary>
        /// 훔친 뒤 <paramref name="elapsed"/>초 시점의 테두리 진행도(0~1, 선형). <see cref="GlowFillSeconds"/> 동안 0 → 1 로 차오르고,
        /// 남은 시간 동안 1 → 0 으로 빠진다(총 <see cref="GlowSeconds"/>).
        /// </summary>
        public float GlowIntensityAt(float elapsed)
        {
            if (elapsed <= 0f || elapsed >= _glowSeconds)
                return 0f;
            float fill = GlowFillSeconds;
            if (elapsed < fill)
                return elapsed / fill;
            float drain = _glowSeconds - fill;
            return drain <= 0f ? 0f : 1f - (elapsed - fill) / drain;
        }

        /// <summary>
        /// 훔친 뒤 <paramref name="elapsed"/>초 시점에 빛이 들어와 있는 폭(화면 반폭 대비, 선형). 차오르는 동안 0 → <see cref="GlowEdgeWidth"/>
        /// 로 넓어지고, 빠지는 동안에는 그 폭을 유지한 채 밝기만 줄어든다.
        /// </summary>
        public float GlowWidthAt(float elapsed)
        {
            if (elapsed <= 0f || elapsed >= _glowSeconds)
                return 0f;
            float fill = GlowFillSeconds;
            // 얇게(35%) 시작해 차오르는 동안 선형으로 다 넓어진다.
            return _glowEdgeWidth * Mathf.Lerp(GlowStartWidthRatio, 1f, Mathf.Clamp01(elapsed / fill));
        }

        /// <summary>훔친 뒤 <paramref name="elapsed"/>초 시점의 가격 글자 불투명도(0~1). 보이다가 선형으로 사라진다.</summary>
        public float PriceAlphaAt(float elapsed)
        {
            if (elapsed < 0f)
                return 0f;
            if (elapsed <= _priceShowSeconds)
                return 1f;
            if (_priceFadeSeconds <= 0f)
                return 0f;
            return Mathf.Clamp01(1f - (elapsed - _priceShowSeconds) / _priceFadeSeconds);
        }

        /// <summary>가격 글자 배율 — 나타날 때 <c>_pricePopScale</c> 에서 1 로 선형으로 돌아온다.</summary>
        public float PriceScaleAt(float elapsed)
        {
            if (elapsed < 0f || _pricePopSeconds <= 0f || elapsed >= _pricePopSeconds)
                return 1f;
            return Mathf.Lerp(_pricePopScale, 1f, elapsed / _pricePopSeconds);
        }

        /// <summary>가격 글자가 원래 자리에서 위로 떠오른 거리(px, 1080p) — 보이는 동안 선형으로.</summary>
        public float PriceRiseAt(float elapsed)
        {
            float total = _priceShowSeconds + _priceFadeSeconds;
            if (elapsed <= 0f || total <= 0f)
                return 0f;
            return _priceRisePixels * Mathf.Clamp01(elapsed / total);
        }
    }
}
