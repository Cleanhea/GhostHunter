using UnityEngine;

namespace GhostHunter.Gameplay.Player
{
    /// <summary>두더지 스킬 공통 UI의 위치·크기·색을 보관하는 설정 에셋.</summary>
    [CreateAssetMenu(
        fileName = "MoleSkillUiSettings_Default",
        menuName = "GhostHunter/UI/Mole Skill UI Settings")]
    public sealed class MoleSkillUiSettings : ScriptableObject
    {
        [Header("위치·크기")]
        [Tooltip("원형 게이지 한 변의 크기(px). 실제 최종 크기는 [TBD: MS-21].")]
        [SerializeField, Min(32f)] private float _indicatorSize = 80f;

        [Tooltip("화면 오른쪽·위쪽 여백(px). 실제 최종 여백은 [TBD: MS-21].")]
        [SerializeField, Min(0f)] private float _rightMargin = 24f;

        [SerializeField, Min(0f)] private float _topMargin = 24f;

        [Tooltip("두 스킬 게이지 사이 세로 간격(px). 겹침 방지를 위한 초기값이다.")]
        [SerializeField, Min(0f)] private float _verticalSpacing = 12f;

        [Header("색")]
        [Tooltip("시전·쿨타임 공통 원형 배경. #595959, 투명도 70%.")]
        [SerializeField] private Color _backgroundColor = new Color32(89, 89, 89, 179);

        [Tooltip("시전 중 테두리 색. #78c664.")]
        [SerializeField] private Color _castingColor = new Color32(120, 198, 100, 255);

        [Tooltip("재사용 대기 중 테두리 색. #FFFFFF.")]
        [SerializeField] private Color _cooldownColor = Color.white;

        [Header("런타임 텍스처")]
        [Tooltip("원형 UI 텍스처 해상도. UI 모양만 만들며 게임 밸런스와 무관하다.")]
        [SerializeField, Range(32, 256)] private int _textureResolution = 128;

        [Tooltip("진행 링 두께의 원 지름 대비 비율. 레퍼런스 기반 초기값 12%.")]
        [SerializeField, Range(0.02f, 0.45f)] private float _ringThicknessRatio = 0.12f;

        [Tooltip("아이콘을 원형 배경 안쪽으로 넣는 여백의 비율. 레퍼런스 기반 초기값 20%.")]
        [SerializeField, Range(0f, 0.45f)] private float _iconInsetRatio = 0.2f;

        [Tooltip("CanvasScaler가 화면 비율을 맞출 기준 해상도.")]
        [SerializeField] private Vector2 _referenceResolution = new Vector2(1920f, 1080f);

        public float IndicatorSize => _indicatorSize;
        public float RightMargin => _rightMargin;
        public float TopMargin => _topMargin;
        public float VerticalSpacing => _verticalSpacing;
        public Color BackgroundColor => _backgroundColor;
        public Color CastingColor => _castingColor;
        public Color CooldownColor => _cooldownColor;
        public int TextureResolution => _textureResolution;
        public float RingThicknessRatio => _ringThicknessRatio;
        public float IconInsetRatio => _iconInsetRatio;
        public Vector2 ReferenceResolution => _referenceResolution;

        private void OnValidate()
        {
            _indicatorSize = Mathf.Max(32f, _indicatorSize);
            _rightMargin = Mathf.Max(0f, _rightMargin);
            _topMargin = Mathf.Max(0f, _topMargin);
            _verticalSpacing = Mathf.Max(0f, _verticalSpacing);
            _textureResolution = Mathf.Clamp(_textureResolution, 32, 256);
            _ringThicknessRatio = Mathf.Clamp(_ringThicknessRatio, 0.02f, 0.45f);
            _iconInsetRatio = Mathf.Clamp(_iconInsetRatio, 0f, 0.45f);
            _referenceResolution.x = Mathf.Max(1f, _referenceResolution.x);
            _referenceResolution.y = Mathf.Max(1f, _referenceResolution.y);
        }
    }
}
