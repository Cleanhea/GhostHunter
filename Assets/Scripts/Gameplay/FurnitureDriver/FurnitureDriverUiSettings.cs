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
        public int FontSize => _fontSize;
        public float LabelGap => _labelGap;
        public float FailDisplaySeconds => _failDisplaySeconds;
        public float ShakeSeconds => _shakeSeconds;
        public int ShakeCount => _shakeCount;
        public float ShakeAmplitude => _shakeAmplitude;
        public int TextureResolution => _textureResolution;
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
            _textureResolution = Mathf.Clamp(_textureResolution, 32, 512);
            _referenceResolution.x = Mathf.Max(1f, _referenceResolution.x);
            _referenceResolution.y = Mathf.Max(1f, _referenceResolution.y);
        }
    }
}
