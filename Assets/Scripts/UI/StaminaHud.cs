using GhostHunter.Gameplay.Player;
using UnityEngine;
using UnityEngine.UI;

namespace GhostHunter.UI
{
    /// <summary>
    /// 스태미나를 화면 아래 가운데에 흰색 가로 바로 띄운다. 바는 왼쪽에서 채워지고, 스태미나가 가득 차 있으면
    /// 잠시 뒤 서서히 사라진다(<see cref="StaminaSettings.AlwaysShowBar"/> 로 계속 보이게 할 수 있다).
    /// Player 프리팹에 붙어 있으며 <b>로컬 소유 플레이어에서만</b> 캔버스를 만든다.
    /// 크기·위치·색은 <see cref="StaminaSettings"/> 에서 매 프레임 읽으므로 F2 튜닝 창에서 바로 바꿔 볼 수 있다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StaminaHud : MonoBehaviour
    {
        // 가구 드라이버 게이지(140) 아래 — 화면 가운데 근처를 쓰는 게이지·퀵슬롯 휠이 위에 그려진다.
        private const int CanvasSortingOrder = 130;

        // 이 비율 이상이면 가득 찬 것으로 본다(부동소수 오차 방지).
        private const float FullRatio = 0.9999f;

        [SerializeField] private PlayerStamina _stamina;

        private readonly HudUiFactory _ui = new();
        private Canvas _canvas;
        private CanvasGroup _group;
        private RectTransform _bar;
        private RectTransform _fillRect;
        private Image _background;
        private Image _fill;
        private float _alpha;
        private float _fullSeconds;

        private void Awake()
        {
            if (_stamina == null)
            {
                Debug.LogError($"{nameof(StaminaHud)}: PlayerStamina 가 Player 프리팹에 배선되지 않았습니다.", this);
                enabled = false;
            }
        }

        private void OnDestroy() => _ui.Dispose();

        private void LateUpdate()
        {
            StaminaSettings settings = _stamina.Settings;
            if (!_stamina.IsLocalOwner || settings == null)
            {
                if (_canvas != null)
                    _canvas.enabled = false;
                return;
            }

            float ratio = _stamina.Ratio;
            if (_canvas == null)
                BuildCanvas(settings, ratio);

            ApplyLayout(settings);
            _fillRect.anchorMax = new Vector2(ratio, 1f);

            float deltaTime = Time.unscaledDeltaTime;
            bool full = ratio >= FullRatio;
            _fullSeconds = full ? _fullSeconds + deltaTime : 0f;
            float target = settings.AlwaysShowBar || _fullSeconds < settings.HideDelaySeconds ? 1f : 0f;
            _alpha = Mathf.MoveTowards(_alpha, target, deltaTime / settings.FadeSeconds);

            _group.alpha = _alpha;
            // 완전히 사라졌으면 그리지도 않는다.
            _canvas.enabled = _alpha > 0.001f;
        }

        private void ApplyLayout(StaminaSettings settings)
        {
            _bar.sizeDelta = new Vector2(settings.BarWidth, settings.BarHeight);
            _bar.anchoredPosition = new Vector2(0f, settings.BottomMargin);
            _background.color = settings.BackgroundColor;
            _fill.color = settings.BarColor;
        }

        private void BuildCanvas(StaminaSettings settings, float ratio)
        {
            _canvas = _ui.CreateCanvas("StaminaCanvas", transform, CanvasSortingOrder, settings.ReferenceResolution);
            _group = _canvas.gameObject.AddComponent<CanvasGroup>();
            _group.interactable = false;
            _group.blocksRaycasts = false;

            Sprite white = _ui.CreateWhiteSprite("StaminaWhiteRuntime");

            _bar = HudUiFactory.CreateRect("StaminaBar", _canvas.transform);
            _bar.anchorMin = new Vector2(0.5f, 0f);
            _bar.anchorMax = new Vector2(0.5f, 0f);
            _bar.pivot = new Vector2(0.5f, 0f);
            _background = _bar.gameObject.AddComponent<Image>();
            _background.sprite = white;
            _background.raycastTarget = false;

            _fill = HudUiFactory.CreateImage("Fill", _bar, white);
            _fillRect = _fill.rectTransform;
            _fillRect.anchorMin = Vector2.zero;
            _fillRect.anchorMax = new Vector2(ratio, 1f);

            // 가득 찬 채 스폰하면 바가 번쩍였다 사라지지 않고 처음부터 숨어 있게 한다.
            bool startsHidden = ratio >= FullRatio && !settings.AlwaysShowBar;
            _alpha = startsHidden ? 0f : 1f;
            _group.alpha = _alpha;
            _fullSeconds = startsHidden ? settings.HideDelaySeconds : 0f;
            ApplyLayout(settings);
        }
    }
}
