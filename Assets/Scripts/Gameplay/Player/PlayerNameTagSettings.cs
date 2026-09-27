using UnityEngine;

namespace GhostHunter.Gameplay.Player
{
    /// <summary>원격 플레이어 머리 위 닉네임의 위치·크기·색을 보관하는 설정 에셋.</summary>
    [CreateAssetMenu(
        fileName = "PlayerNameTagSettings",
        menuName = "GhostHunter/UI/Player Name Tag Settings")]
    public sealed class PlayerNameTagSettings : ScriptableObject
    {
        [Header("위치")]
        [Tooltip("눈높이(PlayerMotor.CameraLocalHeight) 위로 띄우는 높이(m). 자세·굴착에 따라 눈높이와 함께 내려간다.")]
        [SerializeField, Min(0f)] private float _heightAboveEyes = 0.45f;

        [Tooltip("이 거리(m)보다 먼 원격 플레이어의 이름은 숨긴다. 0 이면 거리 제한 없음.")]
        [SerializeField, Min(0f)] private float _maxVisibleDistance = 20f;

        [Header("글자")]
        [Tooltip("월드에서 보이는 글자 높이(m).")]
        [SerializeField, Min(0.01f)] private float _textHeight = 0.16f;

        [Tooltip("글자를 래스터화하는 폰트 크기(px). 클수록 가까이서 선명하다. 월드 크기는 Text Height 가 정한다.")]
        [SerializeField, Range(16, 128)] private int _fontSize = 64;

        [SerializeField] private Color _textColor = Color.white;

        [Tooltip("어두운 실내에서도 읽히도록 두르는 외곽선 색.")]
        [SerializeField] private Color _outlineColor = new Color(0f, 0f, 0f, 0.85f);

        [Tooltip("외곽선 두께(px, Font Size 기준).")]
        [SerializeField, Min(0f)] private float _outlineDistance = 2f;

        public float HeightAboveEyes => _heightAboveEyes;
        public float MaxVisibleDistance => _maxVisibleDistance;
        public float TextHeight => _textHeight;
        public int FontSize => _fontSize;
        public Color TextColor => _textColor;
        public Color OutlineColor => _outlineColor;
        public float OutlineDistance => _outlineDistance;

        private void OnValidate()
        {
            _heightAboveEyes = Mathf.Max(0f, _heightAboveEyes);
            _maxVisibleDistance = Mathf.Max(0f, _maxVisibleDistance);
            _textHeight = Mathf.Max(0.01f, _textHeight);
            _fontSize = Mathf.Clamp(_fontSize, 16, 128);
            _outlineDistance = Mathf.Max(0f, _outlineDistance);
        }
    }
}
