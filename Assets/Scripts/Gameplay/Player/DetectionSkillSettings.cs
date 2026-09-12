using UnityEngine;

namespace GhostHunter.Gameplay.Player
{
    /// <summary>
    /// 탐지 스킬의 지속 시간·색·표시 방식을 보관하는 설정 에셋.
    /// 수치와 미결정 선택지는 코드가 아니라 이 에셋에서 조정한다.
    /// </summary>
    [CreateAssetMenu(
        fileName = "DetectionSkillSettings_Default",
        menuName = "GhostHunter/Gameplay/Detection Skill Settings")]
    public sealed class DetectionSkillSettings : ScriptableObject
    {
        [Header("지속 시간")]
        [Tooltip("Q 입력 뒤 표시가 시작되기 전 레이저 시전 연출 시간(초). [TBD: MS-14] 기본 0.5초.")]
        [SerializeField, Min(0f)] private float _castDurationSeconds = 0.5f;

        [Tooltip("탐지 표시가 유지되는 시간(초). 기획서 §4.3 확정값 5초.")]
        [SerializeField, Min(0.1f)] private float _displayDurationSeconds = 5f;

        [Tooltip("표시 종료 후 재사용 대기 시간(초). 기획서 플로우차트 확정값 10초.")]
        [SerializeField, Min(0f)] private float _cooldownSeconds = 10f;

        [Header("시전 연출")]
        [Tooltip("[TBD: MS-14] 시전 중 화면에 겹칠 파란빛 색. 레이저 포인터 애니메이션이 연결되면 교체한다.")]
        [SerializeField] private Color _castScreenColor = new Color32(64, 160, 255, 255);

        [Tooltip("[TBD: MS-14] 시전 중 파란빛의 최대 불투명도. 0이면 화면 오버레이를 끈다.")]
        [SerializeField, Range(0f, 1f)] private float _castScreenOpacity = 0.35f;

        [Header("대상 색")]
        [Tooltip("이동해야 하는 가구의 표시 색. 기획서 §4.5 확정값 #f9f871.")]
        [SerializeField] private Color _movingFurnitureColor = new Color32(249, 248, 113, 255);

        [Tooltip("닦아야 하는 얼룩의 표시 색. 기획서 §4.5 확정값 #fc84b8.")]
        [SerializeField] private Color _stainColor = new Color32(252, 132, 184, 255);

        [Header("렌더링")]
        [Tooltip("[TBD: MS-19] 현재 FullBodyEmission을 선택했다. 벽 투시는 없다 — ZTest LEqual 로 시야에 보이는 표면만 표시한다.")]
        [SerializeField] private DetectionHighlightMode _highlightMode = DetectionHighlightMode.FullBodyEmission;

        [Tooltip("시전·표시 중 사망하면 탐지를 즉시 취소한다(MS-20, 관전 기획서 SP-2로 확정 2026-09-12).")]
        [SerializeField] private bool _cancelOnDeath = true;

        public float CastDurationSeconds => _castDurationSeconds;
        public float DisplayDurationSeconds => _displayDurationSeconds;
        public float CooldownSeconds => _cooldownSeconds;
        public Color CastScreenColor => _castScreenColor;
        public float CastScreenOpacity => _castScreenOpacity;
        public Color MovingFurnitureColor => _movingFurnitureColor;
        public Color StainColor => _stainColor;
        public DetectionHighlightMode HighlightMode => _highlightMode;
        public bool CancelOnDeath => _cancelOnDeath;

        private void OnValidate()
        {
            _castDurationSeconds = Mathf.Max(0f, _castDurationSeconds);
            _displayDurationSeconds = Mathf.Max(0.1f, _displayDurationSeconds);
            _cooldownSeconds = Mathf.Max(0f, _cooldownSeconds);
            _castScreenOpacity = Mathf.Clamp01(_castScreenOpacity);
        }
    }
}
