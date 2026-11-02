using GhostHunter.Core;
using GhostHunter.Gameplay.Player;
using UnityEngine;
using UnityEngine.UI;

namespace GhostHunter.UI
{
    /// <summary>
    /// 화면 우측 상단에 탐지·굴착 공통 원형 게이지를 표시한다. 실제 스킬 상태는
    /// <see cref="IMoleSkillStatus"/> 읽기 전용 경로로만 읽고, UI 자체는 로컬 Canvas에서 만든다.
    /// 탐지 시전 중에는 <see cref="DetectionSkillSettings.CastScreenColor"/>를 짧게 겹쳐
    /// 파란빛 화면 연출을 재생한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MoleSkillHud : MonoBehaviour
    {
        private const int CanvasSortingOrder = 200;

        [Header("설정")]
        [SerializeField] private MoleSkillUiSettings _uiSettings;

        [Header("아이콘")]
        [SerializeField] private Sprite _detectionOnIcon;
        [SerializeField] private Sprite _detectionOffIcon;
        [SerializeField] private Sprite _excavationOnIcon;
        [SerializeField] private Sprite _excavationOffIcon;

        private ILocalPlayerContext _localPlayer;
        private readonly HudUiFactory _ui = new();
        private Canvas _canvas;
        private RadialGaugeView _detectionIndicator;
        private RadialGaugeView _excavationIndicator;
        private Sprite _circleSprite;
        private Sprite _ringSprite;
        private Sprite _whiteSprite;
        private Image _detectionCastOverlay;

        private void Awake()
        {
            if (_uiSettings == null)
            {
                Debug.LogError(
                    $"{nameof(MoleSkillHud)}: MoleSkillUiSettings 에셋이 배선되지 않았습니다.",
                    this);
                enabled = false;
                return;
            }

            if (_detectionOnIcon == null || _detectionOffIcon == null
                || _excavationOnIcon == null || _excavationOffIcon == null)
            {
                Debug.LogError(
                    $"{nameof(MoleSkillHud)}: 탐지·굴착 on/off 아이콘 4장이 모두 필요합니다.",
                    this);
            }

            Services.TryGet(out _localPlayer);
            BuildCanvas();
        }

        private void OnDestroy() => _ui.Dispose();

        private void Update()
        {
            if (_localPlayer == null)
                Services.TryGet(out _localPlayer);

            DetectionSkillController detection = _localPlayer != null
                ? _localPlayer.DetectionController
                : null;
            MoleBurrowController excavation = _localPlayer != null
                ? _localPlayer.BurrowController
                : null;

            UpdateIndicator(_detectionIndicator, detection);
            UpdateIndicator(_excavationIndicator, excavation);
            UpdateDetectionCastOverlay(detection);
        }

        private void BuildCanvas()
        {
            _canvas = _ui.CreateCanvas("MoleSkillCanvas", transform, CanvasSortingOrder, _uiSettings.ReferenceResolution);
            int resolution = _uiSettings.TextureResolution;
            _circleSprite = _ui.CreateCircleSprite(resolution, resolution * 0.5f - 1f, "MoleSkillCircleRuntime");
            _ringSprite = _ui.CreateRingSprite(resolution, resolution * 0.5f - 1f,
                resolution * _uiSettings.RingThicknessRatio, "MoleSkillRingRuntime");
            _whiteSprite = _ui.CreateWhiteSprite("MoleSkillWhiteRuntime");
            _detectionCastOverlay = BuildDetectionCastOverlay();
            _detectionIndicator = BuildIndicator("DetectionIndicator", _detectionOnIcon, _detectionOffIcon, 0);
            _excavationIndicator = BuildIndicator("ExcavationIndicator", _excavationOnIcon, _excavationOffIcon, 1);
        }

        private Image BuildDetectionCastOverlay()
        {
            GameObject overlayObject = new("DetectionCastOverlay");
            overlayObject.transform.SetParent(_canvas.transform, false);

            RectTransform overlayRect = overlayObject.AddComponent<RectTransform>();
            HudUiFactory.Stretch(overlayRect);

            Image overlay = overlayObject.AddComponent<Image>();
            overlay.sprite = _whiteSprite;
            overlay.color = Color.clear;
            overlay.raycastTarget = false;
            overlayObject.transform.SetAsFirstSibling();
            return overlay;
        }

        private void UpdateDetectionCastOverlay(DetectionSkillController detection)
        {
            if (_detectionCastOverlay == null)
                return;

            if (detection == null
                || detection.Phase != MoleSkillPhase.Casting
                || detection.Settings == null
                || detection.Settings.CastDurationSeconds <= 0f)
            {
                _detectionCastOverlay.color = Color.clear;
                return;
            }

            // 시전 구간 중간에서 가장 강하고, 카운트 시작 직전에 다시 사라지는 펄스다.
            float progress = 1f - Mathf.Clamp01(
                detection.PhaseRemainingSeconds / detection.Settings.CastDurationSeconds);
            float pulse = Mathf.Sin(progress * Mathf.PI);
            Color overlayColor = detection.Settings.CastScreenColor;
            overlayColor.a = pulse * detection.Settings.CastScreenOpacity;
            _detectionCastOverlay.color = overlayColor;
        }

        private RadialGaugeView BuildIndicator(string objectName, Sprite onIcon, Sprite offIcon, int order)
        {
            var indicator = new RadialGaugeView(_canvas.transform, objectName, _uiSettings.IndicatorSize,
                _circleSprite, _ringSprite, "ProgressRing", clockwise: false);
            indicator.PlaceTopRight(new Vector2(-_uiSettings.RightMargin,
                -_uiSettings.TopMargin - order * (_uiSettings.IndicatorSize + _uiSettings.VerticalSpacing)));
            indicator.Background.color = _uiSettings.BackgroundColor;
            indicator.Ring.fillAmount = 0f;
            Image icon = indicator.CreateIcon(onIcon, offIcon);
            float inset = _uiSettings.IndicatorSize * _uiSettings.IconInsetRatio;
            icon.rectTransform.offsetMin = new Vector2(inset, inset);
            icon.rectTransform.offsetMax = new Vector2(-inset, -inset);
            indicator.Root.SetActive(false);
            return indicator;
        }

        private void UpdateIndicator(RadialGaugeView indicator, IMoleSkillStatus status)
        {
            if (indicator == null)
                return;

            bool hasStatus = status != null;
            if (status is Object unityObject && unityObject == null)
                hasStatus = false;

            MoleSkillPhase phase = hasStatus ? status.Phase : MoleSkillPhase.Idle;
            bool visible = phase != MoleSkillPhase.Idle;
            indicator.Root.SetActive(visible);
            if (!visible)
                return;

            switch (phase)
            {
                case MoleSkillPhase.Casting:
                    // 탐지 시전 연출 동안에는 5초 카운트가 아직 시작되지 않으므로 선을 줄이지 않는다.
                    indicator.Ring.color = _uiSettings.CastingColor;
                    indicator.Ring.fillAmount = 1f;
                    indicator.Icon.sprite = indicator.OnIcon;
                    break;

                case MoleSkillPhase.Active:
                    indicator.Ring.color = _uiSettings.CastingColor;
                    indicator.Ring.fillAmount = NormalizeRemaining(
                        status.PhaseRemainingSeconds,
                        status.ActiveDurationSeconds);
                    indicator.Icon.sprite = indicator.OnIcon;
                    break;

                case MoleSkillPhase.Cooldown:
                    indicator.Ring.color = _uiSettings.CooldownColor;
                    indicator.Ring.fillAmount = NormalizeRemaining(
                        status.CooldownRemainingSeconds,
                        status.CooldownDurationSeconds);
                    indicator.Icon.sprite = indicator.OffIcon;
                    break;
            }
        }

        private static float NormalizeRemaining(float remaining, float duration)
        {
            if (duration <= 0f)
                return 0f;

            return Mathf.Clamp01(remaining / duration);
        }
    }
}
