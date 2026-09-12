using UnityEngine;

namespace GhostHunter.Gameplay.Player
{
    /// <summary>퀵슬롯 휠의 반경·감도·데드존·색을 보관하는 설정 에셋.</summary>
    [CreateAssetMenu(fileName = "QuickSlotUiSettings_Default", menuName = "GhostHunter/QuickSlot/UI Settings")]
    public sealed class QuickSlotUiSettings : ScriptableObject
    {
        [Header("링 크기")]
        [Tooltip("휠 바깥 반지름(px).")]
        [SerializeField, Min(32f)] private float _outerRadius = 220f;

        [Tooltip("링 두께의 바깥 반지름 대비 비율.")]
        [SerializeField, Range(0.05f, 0.9f)] private float _ringThicknessRatio = 0.35f;

        [Header("입력")]
        [Tooltip("마우스 델타(px)에 곱하는 감도. 1이면 원시 델타를 그대로 누적한다.")]
        [SerializeField, Min(0.01f)] private float _sensitivity = 1.5f;

        [Tooltip("이 길이(px) 미만으로 누적되면 선택 없음으로 본다.")]
        [SerializeField, Min(0f)] private float _deadZoneRadius = 24f;

        [Header("색")]
        [Tooltip("기본 슬롯 배경색.")]
        [SerializeField] private Color _slotBaseColor = new Color32(40, 40, 40, 200);

        [Tooltip("가리키고 있는 슬롯의 강조색.")]
        [SerializeField] private Color _slotHighlightColor = new Color32(198, 60, 60, 230);

        [Tooltip("빈 슬롯(확정 불가)의 배경색.")]
        [SerializeField] private Color _emptySlotColor = new Color32(20, 20, 20, 140);

        [Tooltip("휠이 열린 동안 배경에 까는 로컬 디밍 알파(QS-8, 시간 감속 연출 없음).")]
        [SerializeField, Range(0f, 1f)] private float _backgroundDimAlpha = 0.45f;

        [Header("텍스트")]
        [SerializeField, Min(1)] private int _titleFontSize = 24;
        [SerializeField, Min(1)] private int _descriptionFontSize = 16;
        [SerializeField] private Color _textColor = Color.white;

        [Header("런타임 텍스처")]
        [Tooltip("원형 UI 텍스처 해상도. UI 모양만 만들며 게임 밸런스와 무관하다.")]
        [SerializeField, Range(32, 512)] private int _textureResolution = 256;

        [SerializeField] private Vector2 _referenceResolution = new Vector2(1920f, 1080f);

        public float OuterRadius => _outerRadius;
        public float RingThicknessRatio => _ringThicknessRatio;
        public float Sensitivity => _sensitivity;
        public float DeadZoneRadius => _deadZoneRadius;
        public Color SlotBaseColor => _slotBaseColor;
        public Color SlotHighlightColor => _slotHighlightColor;
        public Color EmptySlotColor => _emptySlotColor;
        public float BackgroundDimAlpha => _backgroundDimAlpha;
        public int TitleFontSize => _titleFontSize;
        public int DescriptionFontSize => _descriptionFontSize;
        public Color TextColor => _textColor;
        public int TextureResolution => _textureResolution;
        public Vector2 ReferenceResolution => _referenceResolution;

        private void OnValidate()
        {
            _outerRadius = Mathf.Max(32f, _outerRadius);
            _ringThicknessRatio = Mathf.Clamp(_ringThicknessRatio, 0.05f, 0.9f);
            _sensitivity = Mathf.Max(0.01f, _sensitivity);
            _deadZoneRadius = Mathf.Max(0f, _deadZoneRadius);
            _backgroundDimAlpha = Mathf.Clamp01(_backgroundDimAlpha);
            _titleFontSize = Mathf.Max(1, _titleFontSize);
            _descriptionFontSize = Mathf.Max(1, _descriptionFontSize);
            _textureResolution = Mathf.Clamp(_textureResolution, 32, 512);
            _referenceResolution.x = Mathf.Max(1f, _referenceResolution.x);
            _referenceResolution.y = Mathf.Max(1f, _referenceResolution.y);
        }
    }
}
