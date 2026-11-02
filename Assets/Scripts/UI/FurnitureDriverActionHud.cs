using GhostHunter.Core;
using GhostHunter.Gameplay.FurnitureDriver;
using GhostHunter.Gameplay.Player;
using UnityEngine;
using UnityEngine.UI;

namespace GhostHunter.UI
{
    /// <summary>
    /// 가구용 멀티 드라이버 행동 시간을 화면 중앙의 원형 게이지로 표시한다. 우클릭을 누르고 있는 동안
    /// 게이지가 채워지고, 행동이 중단되면 멈춘 게이지가 좌우로 흔들리며 실패 문구를 띄운 뒤 사라진다
    /// → docs/project/furniture-multidriver-system.md §6.1.
    ///
    /// <para>조립을 시작조차 못 한 경우(재료 부족·재료 구성 불일치·영역 밖)는 게이지 없이 같은 자리에 문구만
    /// 같은 방식으로 흔들어 띄운다(<see cref="PlayerFurnitureDriverController.FeedbackSerial"/>).</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FurnitureDriverActionHud : MonoBehaviour
    {
        private const int CanvasSortingOrder = 140;

        [Header("설정")]
        [SerializeField] private FurnitureDriverUiSettings _uiSettings;

        private ILocalPlayerContext _localPlayer;
        private PlayerFurnitureDriverController _observedDriver;
        private int _observedCancelSerial;
        private int _observedFeedbackSerial;

        private readonly HudUiFactory _ui = new();
        private Canvas _canvas;
        private Font _font;
        private RectTransform _gaugeRoot;
        private Image _track;
        private Image _fill;
        private Text _label;

        private bool _isShowingFailure;
        private float _failureStartedAt;

        private void Awake()
        {
            if (_uiSettings == null)
            {
                Debug.LogError(
                    $"{nameof(FurnitureDriverActionHud)}: FurnitureDriverUiSettings 에셋이 배선되지 않았습니다.",
                    this);
                enabled = false;
                return;
            }

            Services.TryGet(out _localPlayer);
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildCanvas();
        }

        private void Update()
        {
            if (_localPlayer == null)
                Services.TryGet(out _localPlayer);

            PlayerFurnitureDriverController driver = _localPlayer != null
                ? _localPlayer.FurnitureDriverController
                : null;

            if (driver != _observedDriver)
            {
                // 로컬 플레이어가 바뀌면 이전 기록을 새 중단으로 오인하지 않도록 기준을 다시 잡는다.
                _observedDriver = driver;
                _observedCancelSerial = driver != null ? driver.ActionCancelSerial : 0;
                _observedFeedbackSerial = driver != null ? driver.FeedbackSerial : 0;
                _isShowingFailure = false;
            }

            if (driver != null && driver.ActionCancelSerial != _observedCancelSerial)
            {
                _observedCancelSerial = driver.ActionCancelSerial;
                BeginFailure(driver.LastCancelledAction, driver.LastCancelledProgress);
            }

            if (driver != null && driver.FeedbackSerial != _observedFeedbackSerial)
            {
                _observedFeedbackSerial = driver.FeedbackSerial;
                BeginFeedback(driver.LastFeedback);
            }

            if (driver != null && driver.CurrentAction != FurnitureDriverActionKind.None)
            {
                _isShowingFailure = false;
                ShowProgress(driver.CurrentAction, driver.ActionProgress);
                return;
            }

            if (_isShowingFailure)
            {
                UpdateFailure();
                return;
            }

            _gaugeRoot.gameObject.SetActive(false);
        }

        private void OnDestroy() => _ui.Dispose();

        private void ShowProgress(FurnitureDriverActionKind kind, float progress)
        {
            _gaugeRoot.gameObject.SetActive(true);
            _gaugeRoot.anchoredPosition = Vector2.zero;
            SetRingVisible(true);
            _label.color = _uiSettings.TextColor;
            _fill.color = _uiSettings.FillColor;
            _fill.fillAmount = progress;
            _label.text = kind == FurnitureDriverActionKind.Assemble
                ? _uiSettings.AssemblingText
                : _uiSettings.DisassemblingText;
        }

        private void BeginFailure(FurnitureDriverActionKind kind, float progress)
        {
            _isShowingFailure = true;
            _failureStartedAt = Time.unscaledTime;
            _gaugeRoot.gameObject.SetActive(true);
            SetRingVisible(true);
            _label.color = _uiSettings.TextColor;
            _fill.color = _uiSettings.FailColor;
            // §6.1 — 중단된 지점에서 게이지 채움을 멈춘 채로 보여 준다.
            _fill.fillAmount = progress;
            _label.text = kind == FurnitureDriverActionKind.Assemble
                ? _uiSettings.AssembleFailText
                : _uiSettings.DisassembleFailText;
        }

        /// <summary>행동을 시작하지 못한 이유 — 게이지 없이 문구만 실패색으로 흔들어 보여 준다.</summary>
        private void BeginFeedback(FurnitureDriverFeedback feedback)
        {
            string text = feedback switch
            {
                FurnitureDriverFeedback.NotEnoughMaterials => _uiSettings.NotEnoughMaterialsText,
                FurnitureDriverFeedback.MismatchedMaterials => _uiSettings.MismatchedMaterialsText,
                FurnitureDriverFeedback.OutsideAssemblyZone => _uiSettings.OutsideAssemblyZoneText,
                _ => null,
            };
            if (string.IsNullOrEmpty(text))
                return;

            _isShowingFailure = true;
            _failureStartedAt = Time.unscaledTime;
            _gaugeRoot.gameObject.SetActive(true);
            SetRingVisible(false);
            _label.color = _uiSettings.FailColor;
            _label.text = text;
        }

        private void SetRingVisible(bool visible)
        {
            _track.enabled = visible;
            _fill.enabled = visible;
        }

        private void UpdateFailure()
        {
            float elapsed = Time.unscaledTime - _failureStartedAt;
            if (elapsed >= _uiSettings.FailDisplaySeconds)
            {
                _isShowingFailure = false;
                _gaugeRoot.gameObject.SetActive(false);
                return;
            }

            _gaugeRoot.anchoredPosition = new Vector2(ShakeOffset(elapsed), 0f);
        }

        private float ShakeOffset(float elapsed)
        {
            float t = elapsed / _uiSettings.ShakeSeconds;
            if (t >= 1f)
                return 0f;

            // 왕복 ShakeCount회를 한 번에 그리고, 끝으로 갈수록 폭을 줄여 제자리에 멈추게 한다.
            return Mathf.Sin(t * _uiSettings.ShakeCount * 2f * Mathf.PI) * _uiSettings.ShakeAmplitude * (1f - t);
        }

        private void BuildCanvas()
        {
            _canvas = _ui.CreateCanvas("FurnitureDriverActionCanvas", transform, CanvasSortingOrder, _uiSettings.ReferenceResolution);
            int resolution = _uiSettings.TextureResolution;
            Sprite ring = _ui.CreateRingSprite(resolution, resolution * 0.5f - 1f,
                resolution * 0.5f * _uiSettings.RingThicknessRatio, "FurnitureDriverRingRuntime");
            var gauge = new RadialGaugeView(_canvas.transform, "ActionGauge", _uiSettings.RingDiameter,
                ring, ring, "Fill", clockwise: true);
            _gaugeRoot = gauge.Rect;
            _track = gauge.Background;
            _track.color = _uiSettings.TrackColor;
            _fill = gauge.Ring;
            _fill.fillAmount = 0f;
            _label = gauge.CreateLabel("Label", _font, _uiSettings.FontSize);
            RectTransform labelRect = _label.rectTransform;
            labelRect.anchorMin = new Vector2(0.5f, 0f);
            labelRect.anchorMax = new Vector2(0.5f, 0f);
            labelRect.pivot = new Vector2(0.5f, 1f);
            labelRect.anchoredPosition = new Vector2(0f, -_uiSettings.LabelGap);
            labelRect.sizeDelta = new Vector2(240f, _uiSettings.FontSize * 1.6f);
            _label.alignment = TextAnchor.UpperCenter;
            _label.color = _uiSettings.TextColor;
            _label.horizontalOverflow = HorizontalWrapMode.Overflow;
            _label.verticalOverflow = VerticalWrapMode.Overflow;
            Outline outline = _label.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.75f);
            gauge.Root.SetActive(false);
        }
    }
}
