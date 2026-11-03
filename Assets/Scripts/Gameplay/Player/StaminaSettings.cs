using GhostHunter.Core.Audio;
using UnityEngine;

namespace GhostHunter.Gameplay.Player
{
    /// <summary>
    /// 스태미나(달리기 지구력)의 소모·회복·지침·헐떡임·HUD 설정. 구조는 docs/architecture/player-controller.md "스태미나".
    /// 수치는 전부 [TEMP] — 플레이 화면 기준으로 조정한다. 플레이 중 F2 튜닝 창에서 실시간으로 바꿀 수 있다.
    /// </summary>
    [CreateAssetMenu(
        fileName = "StaminaSettings_Default",
        menuName = "GhostHunter/Gameplay/Stamina Settings")]
    public sealed class StaminaSettings : ScriptableObject
    {
        [Header("소모·회복 [TEMP]")]
        [Tooltip("스태미나 총량.")]
        [SerializeField, Min(1f)] private float _maxStamina = 100f;

        [Tooltip("달리는 동안 초당 소모량. 총량 ÷ 이 값이 처음부터 끝까지 달릴 수 있는 시간(초)이다.")]
        [SerializeField, Min(0f)] private float _sprintDrainPerSecond = 20f;

        [Tooltip("달리지 않는 동안 초당 회복량.")]
        [SerializeField, Min(0f)] private float _regenPerSecond = 12f;

        [Tooltip("달리기를 멈추고 이만큼(초) 지나야 회복이 시작된다.")]
        [SerializeField, Min(0f)] private float _regenDelaySeconds = 1f;

        [Header("탈진 [TEMP]")]
        [Tooltip("스태미나가 0이 되면 탈진해 달릴 수 없다. 총량 대비 이 비율까지 회복해야 다시 달릴 수 있다 — 0 근처에서 달리기가 깜빡이는 것을 막는다.")]
        [SerializeField, Range(0f, 1f)] private float _exhaustedRecoverRatio = 0.25f;

        [Header("지침 — 점점 느려짐 [TEMP]")]
        [Tooltip("스태미나가 총량 대비 이 비율 아래로 내려가면 이동 속도가 줄기 시작한다. 0 이면 느려지지 않는다.")]
        [SerializeField, Range(0f, 1f)] private float _slowStartRatio = 0.4f;

        [Tooltip("스태미나가 0일 때의 이동 속도 배율. 걷기·웅크리기·엎드리기 모두에 곱해진다. 1 이면 느려지지 않는다.")]
        [SerializeField, Range(0.1f, 1f)] private float _minSpeedMultiplier = 0.6f;

        [Header("헐떡임 — 소리 [TEMP]")]
        [Tooltip("스태미나가 총량 대비 이 비율 이하이거나 탈진하면 헐떡이기 시작한다.")]
        [SerializeField, Range(0f, 1f)] private float _breathStartRatio = 0.3f;

        [Tooltip("헐떡이는 중 이 비율 이상으로 회복하면 멈춘다. 시작 비율보다 높아야 깜빡이지 않는다.")]
        [SerializeField, Range(0f, 1f)] private float _breathStopRatio = 0.6f;

        [Tooltip("헐떡임이 멈출 때 줄어드는 시간(초).")]
        [SerializeField, Min(0f)] private float _breathFadeSeconds = 0.6f;

        [Tooltip("큐 음량에 곱하는 배율.")]
        [SerializeField, Range(0f, 2f)] private float _breathVolume = 1f;

        [Tooltip("헐떡이는 소리. 반복(Loop) 큐를 권장한다. 비어 있으면 소리를 내지 않는다. 쓰는 사람에게만 들린다.")]
        [SerializeField] private SoundCue _breathCue;

        [Header("HUD — 흰색 바 [TEMP]")]
        [Tooltip("바 너비(기준 해상도 픽셀).")]
        [SerializeField, Min(1f)] private float _barWidth = 280f;

        [Tooltip("바 높이(기준 해상도 픽셀).")]
        [SerializeField, Min(1f)] private float _barHeight = 10f;

        [Tooltip("화면 아래쪽 가장자리에서 바까지의 거리(기준 해상도 픽셀). 화면 가운데 아래에 놓인다.")]
        [SerializeField] private float _bottomMargin = 90f;

        [Tooltip("스태미나가 가득 찬 뒤 바가 사라지기 시작할 때까지의 시간(초). 가득 차 있으면 숨긴다.")]
        [SerializeField, Min(0f)] private float _hideDelaySeconds = 1.2f;

        [Tooltip("바가 사라지는 데 걸리는 시간(초).")]
        [SerializeField, Min(0.01f)] private float _fadeSeconds = 0.4f;

        [Tooltip("true 면 스태미나가 가득 찬 동안에도 바를 계속 보여 준다.")]
        [SerializeField] private bool _alwaysShowBar;

        [SerializeField] private Color _barColor = Color.white;
        [SerializeField] private Color _backgroundColor = new(0f, 0f, 0f, 0.45f);
        [SerializeField] private Vector2 _referenceResolution = new(1920f, 1080f);

        public float MaxStamina => _maxStamina;
        public float SprintDrainPerSecond => _sprintDrainPerSecond;
        public float RegenPerSecond => _regenPerSecond;
        public float RegenDelaySeconds => _regenDelaySeconds;
        public float ExhaustedRecoverRatio => _exhaustedRecoverRatio;
        public float SlowStartRatio => _slowStartRatio;
        public float MinSpeedMultiplier => _minSpeedMultiplier;
        public float BreathStartRatio => _breathStartRatio;
        public float BreathStopRatio => _breathStopRatio;
        public float BreathFadeSeconds => _breathFadeSeconds;
        public float BreathVolume => _breathVolume;
        public SoundCue BreathCue => _breathCue;
        public float BarWidth => _barWidth;
        public float BarHeight => _barHeight;
        public float BottomMargin => _bottomMargin;
        public float HideDelaySeconds => _hideDelaySeconds;
        public float FadeSeconds => _fadeSeconds;
        public bool AlwaysShowBar => _alwaysShowBar;
        public Color BarColor => _barColor;
        public Color BackgroundColor => _backgroundColor;
        public Vector2 ReferenceResolution => _referenceResolution;

        private void OnValidate()
        {
            // 멈추는 비율이 시작 비율보다 낮으면 헐떡임이 시작하자마자 멈춘다.
            _breathStopRatio = Mathf.Max(_breathStopRatio, _breathStartRatio);
        }
    }
}
