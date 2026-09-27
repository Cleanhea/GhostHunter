using UnityEngine;

namespace GhostHunter.Gameplay.FurnitureDriver
{
    /// <summary>
    /// 가구용 멀티 드라이버 행동 시간 원형 게이지(기획서 §6.1)의 크기·색·문구·흔들림 수치를 보관하는 설정 에셋.
    /// </summary>
    [CreateAssetMenu(fileName = "FurnitureDriverUiSettings_Default", menuName = "GhostHunter/Furniture Driver/UI Settings")]
    public sealed class FurnitureDriverUiSettings : ScriptableObject
    {
        [Header("게이지 크기")]
        [Tooltip("원형 게이지 지름(px). 조준점(+)을 가리지 않도록 조준점보다 크게 둔다.")]
        [SerializeField, Min(16f)] private float _ringDiameter = 88f;

        [Tooltip("링 두께의 반지름 대비 비율.")]
        [SerializeField, Range(0.05f, 0.9f)] private float _ringThicknessRatio = 0.18f;

        [Header("색")]
        [Tooltip("채워지지 않은 링 바탕색.")]
        [SerializeField] private Color _trackColor = new Color32(0, 0, 0, 140);

        [Tooltip("진행 중 채워지는 링 색.")]
        [SerializeField] private Color _fillColor = new Color32(255, 255, 255, 235);

        [Tooltip("중단된 순간 멈춘 링 색. 조립 불가 실루엣과 같은 붉은색(#D66565)을 기본값으로 쓴다.")]
        [SerializeField] private Color _failColor = new Color32(214, 101, 101, 235);

        [SerializeField] private Color _textColor = Color.white;

        [Header("문구 (§6.1)")]
        [SerializeField] private string _disassemblingText = "분해 중";
        [SerializeField] private string _assemblingText = "조립 중";
        [SerializeField] private string _disassembleFailText = "분해 실패";
        [SerializeField] private string _assembleFailText = "조립 실패";
        [SerializeField, Min(1)] private int _fontSize = 18;

        [Header("조립을 시작할 수 없을 때 문구 (2026-09-27)")]
        [Tooltip("조준한 재료의 조립 영역에 그 가구의 재료가 다 모이지 않았을 때.")]
        [SerializeField] private string _notEnoughMaterialsText = "재료가 부족합니다";

        [Tooltip("다른 가구의 재료가 섞였거나 같은 재료가 너무 많을 때(§4.3 규칙 1).")]
        [SerializeField] private string _mismatchedMaterialsText = "재료 구성이 맞지 않습니다";

        [Tooltip("조준한 재료가 조립 영역 밖에 있을 때.")]
        [SerializeField] private string _outsideAssemblyZoneText = "조립 영역 안에 재료를 모아 주세요";

        [Tooltip("게이지 아래 가장자리와 문구 사이 간격(px).")]
        [SerializeField, Min(0f)] private float _labelGap = 10f;

        [Header("중단 연출 (§6.1)")]
        [Tooltip("중단 후 게이지·문구를 보여 주는 전체 시간(초).")]
        [SerializeField, Min(0.05f)] private float _failDisplaySeconds = 0.9f;

        [Tooltip("좌우 흔들림에 쓰는 시간(초). 표시 시간보다 길면 표시 시간에서 끊긴다.")]
        [SerializeField, Min(0.05f)] private float _shakeSeconds = 0.4f;

        [Tooltip("좌우 왕복 횟수. 기획서 §6.1은 두 번이다.")]
        [SerializeField, Min(1)] private int _shakeCount = 2;

        [Tooltip("흔들림 최대 폭(px).")]
        [SerializeField, Min(0f)] private float _shakeAmplitude = 12f;

        [Header("조립 영역 표시 (2026-09-27)")]
        [Tooltip("게임 화면에 조립 영역 판정 트리거를 바닥 채움 + 윤곽선으로 그린다. 색은 판정 상태를 따른다.")]
        [SerializeField] private bool _showAssemblyZone = true;

        [Tooltip("영역에 재료가 없을 때 — 위치만 알려 주는 옅은 색.")]
        [SerializeField] private Color _zoneEmptyColor = new Color(1f, 1f, 1f, 0.35f);

        [Tooltip("재료가 일부만 있을 때 — 기획서 §6.4 흰색 #FFFFFF 투명도 50.")]
        [SerializeField] private Color _zonePartialColor = new Color(1f, 1f, 1f, 0.5f);

        [Tooltip("조립 가능 — 기획서 §6.4 초록 #78C664.")]
        [SerializeField] private Color _zoneReadyColor = new Color32(120, 198, 100, 200);

        [Tooltip("다른 가구 재료 혼입·개수 초과 — 기획서 §6.4 빨강 #D66565.")]
        [SerializeField] private Color _zoneInvalidColor = new Color32(214, 101, 101, 200);

        [Tooltip("윤곽선 두께(m).")]
        [SerializeField, Min(0.005f)] private float _zoneLineWidth = 0.04f;

        [Tooltip("바닥 채움 투명도 배율 — 상태 색의 알파에 곱한다. 바닥이 가려지지 않게 낮게 둔다.")]
        [SerializeField, Range(0f, 1f)] private float _zoneFillAlphaScale = 0.3f;

        [Header("런타임 텍스처")]
        [Tooltip("링 텍스처 해상도. UI 모양만 만들며 게임 밸런스와 무관하다.")]
        [SerializeField, Range(32, 512)] private int _textureResolution = 256;

        [SerializeField] private Vector2 _referenceResolution = new Vector2(1920f, 1080f);

        public float RingDiameter => _ringDiameter;
        public float RingThicknessRatio => _ringThicknessRatio;
        public Color TrackColor => _trackColor;
        public Color FillColor => _fillColor;
        public Color FailColor => _failColor;
        public Color TextColor => _textColor;
        public string DisassemblingText => _disassemblingText;
        public string AssemblingText => _assemblingText;
        public string DisassembleFailText => _disassembleFailText;
        public string AssembleFailText => _assembleFailText;
        public string NotEnoughMaterialsText => _notEnoughMaterialsText;
        public string MismatchedMaterialsText => _mismatchedMaterialsText;
        public string OutsideAssemblyZoneText => _outsideAssemblyZoneText;
        public int FontSize => _fontSize;
        public float LabelGap => _labelGap;
        public float FailDisplaySeconds => _failDisplaySeconds;
        public float ShakeSeconds => _shakeSeconds;
        public int ShakeCount => _shakeCount;
        public float ShakeAmplitude => _shakeAmplitude;
        public bool ShowAssemblyZone => _showAssemblyZone;
        public float ZoneLineWidth => _zoneLineWidth;
        public float ZoneFillAlphaScale => _zoneFillAlphaScale;
        public int TextureResolution => _textureResolution;

        /// <summary>조립 영역 판정 상태별 표시 색.</summary>
        public Color ZoneColorFor(FurnitureAssemblyState state) => state switch
        {
            FurnitureAssemblyState.Partial => _zonePartialColor,
            FurnitureAssemblyState.Ready => _zoneReadyColor,
            FurnitureAssemblyState.Invalid => _zoneInvalidColor,
            _ => _zoneEmptyColor,
        };
        public Vector2 ReferenceResolution => _referenceResolution;

        private void OnValidate()
        {
            _ringDiameter = Mathf.Max(16f, _ringDiameter);
            _ringThicknessRatio = Mathf.Clamp(_ringThicknessRatio, 0.05f, 0.9f);
            _fontSize = Mathf.Max(1, _fontSize);
            _labelGap = Mathf.Max(0f, _labelGap);
            _failDisplaySeconds = Mathf.Max(0.05f, _failDisplaySeconds);
            _shakeSeconds = Mathf.Max(0.05f, _shakeSeconds);
            _shakeCount = Mathf.Max(1, _shakeCount);
            _shakeAmplitude = Mathf.Max(0f, _shakeAmplitude);
            _zoneLineWidth = Mathf.Max(0.005f, _zoneLineWidth);
            _zoneFillAlphaScale = Mathf.Clamp01(_zoneFillAlphaScale);
            _textureResolution = Mathf.Clamp(_textureResolution, 32, 512);
            _referenceResolution.x = Mathf.Max(1f, _referenceResolution.x);
            _referenceResolution.y = Mathf.Max(1f, _referenceResolution.y);
        }
    }
}
