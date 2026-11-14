using GhostHunter.Gameplay.Interaction;
using GhostHunter.Gameplay.Theft;
using UnityEngine;
using UnityEngine.UI;

namespace GhostHunter.UI
{
    /// <summary>
    /// 도둑질 UI(theft-system.md §4). Player 프리팹에 붙어 있고 <b>로컬 소유 플레이어에서만</b> 캔버스를 만든다.
    /// <list type="bullet">
    /// <item>귀중품을 조준하면 우측 하단에 "E 훔치기"(1920×1080 기준 132×50, 오른쪽 378·아래 161 여백).</item>
    /// <item>서버가 도둑질을 확정하면 화면 가운데 아래에 "+ n$"(#54d6ad).</item>
    /// <item>동시에 화면 테두리 2초 빛(#c2fcf3) — 전역 셰이더 값을 넣으면 <see cref="TheftRecoveryGlowFeature"/> 가 그린다.</item>
    /// </list>
    /// 폰트는 원문 Noto Sans KR Bold 지만 저장소에 폰트 에셋이 없어 [TEMP] 기본 폰트 굵게로 쓴다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TheftHud : MonoBehaviour
    {
        private const int CanvasSortingOrder = 205;

        [SerializeField] private PlayerTheft _theft;
        [SerializeField] private PlayerInteractor _interactor;

        private readonly HudUiFactory _ui = new();
        private TheftSettings _settings;
        private Canvas _canvas;
        private GameObject _prompt;
        private Text _price;
        private RectTransform _priceRect;
        private float _stolenAt = float.NegativeInfinity;
        private bool _subscribed;
        private bool _glowActive;

        /// <summary>테스트·디버그용 — 지금 "E 훔치기" 안내가 보이는가.</summary>
        public bool IsPromptVisible => _prompt != null && _prompt.activeSelf;

        /// <summary>테스트·디버그용 — 마지막으로 띄운 가격 문구.</summary>
        public string PriceText => _price != null ? _price.text : string.Empty;

        private void Awake()
        {
            if (_theft == null || _interactor == null || _theft.Settings == null)
            {
                Debug.LogError($"{nameof(TheftHud)}: 도둑질·상호작용 컴포넌트가 Player 프리팹에 배선되지 않았습니다.", this);
                enabled = false;
                return;
            }
            _settings = _theft.Settings;
        }

        private void OnDisable()
        {
            Unsubscribe();
            ClearGlow();
        }

        private void OnDestroy()
        {
            ClearGlow();
            _ui.Dispose();
        }

        private void LateUpdate()
        {
            bool local = _theft.IsSpawned && _theft.IsOwner;
            if (!local)
            {
                Unsubscribe();
                ClearGlow();
                if (_canvas != null && _canvas.gameObject.activeSelf)
                    _canvas.gameObject.SetActive(false);
                return;
            }

            if (_canvas == null)
                BuildCanvas();
            if (!_canvas.gameObject.activeSelf)
                _canvas.gameObject.SetActive(true);
            if (!_subscribed)
            {
                _theft.LocalStealConfirmed += OnStealConfirmed;
                _subscribed = true;
            }

            bool showPrompt = _interactor.CurrentValuable != null;
            if (_prompt.activeSelf != showPrompt)
                _prompt.SetActive(showPrompt);

            float elapsed = Time.time - _stolenAt;
            float alpha = _settings.PriceAlphaAt(elapsed);
            bool showPrice = alpha > 0f;
            if (_price.gameObject.activeSelf != showPrice)
                _price.gameObject.SetActive(showPrice);
            if (showPrice)
            {
                Color color = _settings.PriceColor;
                color.a *= alpha;
                _price.color = color;
                // 나타날 때 살짝 커졌다가 돌아오고, 보이는 동안 위로 떠오른다(모두 선형).
                _priceRect.localScale = Vector3.one * _settings.PriceScaleAt(elapsed);
                _priceRect.anchoredPosition = new Vector2(0f,
                    -_settings.PriceOffsetBelowCenter + _settings.PriceRiseAt(elapsed));
            }

            // 가장자리에서 안쪽으로 빛의 폭과 밝기가 선형으로 차오르고, 폭을 유지한 채 선형으로 빠진다.
            float glow = _settings.GlowIntensityAt(elapsed) * _settings.GlowStrength;
            if (glow > 0f)
            {
                Shader.SetGlobalFloat(TheftRecoveryGlowFeature.IntensityId, glow);
                Shader.SetGlobalColor(TheftRecoveryGlowFeature.ColorId, _settings.GlowColor);
                Shader.SetGlobalFloat(TheftRecoveryGlowFeature.EdgeId, _settings.GlowWidthAt(elapsed));
                _glowActive = true;
            }
            else
                ClearGlow();
        }

        private void OnStealConfirmed(int price)
        {
            _stolenAt = Time.time;
            if (_price != null)
                _price.text = $"+ {price}$";
        }

        private void Unsubscribe()
        {
            if (!_subscribed || _theft == null)
                return;
            _theft.LocalStealConfirmed -= OnStealConfirmed;
            _subscribed = false;
        }

        private void ClearGlow()
        {
            if (!_glowActive)
                return;
            Shader.SetGlobalFloat(TheftRecoveryGlowFeature.IntensityId, 0f);
            _glowActive = false;
        }

        private void BuildCanvas()
        {
            _canvas = _ui.CreateCanvas("TheftCanvas", transform, CanvasSortingOrder, _settings.ReferenceResolution);
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Sprite white = _ui.CreateWhiteSprite("TheftHudWhite");

            // "E 훔치기" — 우측 하단. 원문 패널 외곽 132×50, 오른쪽 378·아래 161(1080p).
            RectTransform prompt = HudUiFactory.CreateRect("StealPrompt", _canvas.transform);
            prompt.anchorMin = prompt.anchorMax = new Vector2(1f, 0f);
            prompt.pivot = new Vector2(1f, 0f);
            prompt.sizeDelta = _settings.PromptSize;
            prompt.anchoredPosition = new Vector2(-_settings.PromptRightMargin, _settings.PromptBottomMargin);
            Image panel = prompt.gameObject.AddComponent<Image>();
            panel.sprite = white;
            panel.color = new Color(0f, 0f, 0f, 0.55f);
            panel.raycastTarget = false;

            float height = _settings.PromptSize.y;
            float keySize = Mathf.Round(height * 0.56f);
            float padding = Mathf.Round((height - keySize) * 0.5f);
            RectTransform key = HudUiFactory.CreateRect("KeyCap", prompt);
            key.anchorMin = key.anchorMax = new Vector2(0f, 0.5f);
            key.pivot = new Vector2(0f, 0.5f);
            key.sizeDelta = new Vector2(keySize, keySize);
            key.anchoredPosition = new Vector2(padding, 0f);
            Image keyImage = key.gameObject.AddComponent<Image>();
            keyImage.sprite = white;
            keyImage.color = new Color(1f, 1f, 1f, 0.08f);
            keyImage.raycastTarget = false;
            Outline keyOutline = key.gameObject.AddComponent<Outline>();
            keyOutline.effectColor = new Color(1f, 1f, 1f, 0.9f);
            keyOutline.effectDistance = new Vector2(1.5f, -1.5f);
            CreateText("Key", key, font, "E", _settings.PromptFontSize, TextAnchor.MiddleCenter, Color.white, stretch: true);

            RectTransform labelRect = HudUiFactory.CreateRect("LabelArea", prompt);
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.offsetMin = new Vector2(padding * 2f + keySize, 0f);
            labelRect.offsetMax = new Vector2(-padding, 0f);
            CreateText("Label", labelRect, font, "훔치기", _settings.PromptFontSize, TextAnchor.MiddleLeft, Color.white, stretch: true);
            _prompt = prompt.gameObject;
            _prompt.SetActive(false);

            // "+ n$" — 화면 가로 가운데, 세로 중앙선에서 원문 61px 아래가 글자 위쪽.
            RectTransform priceRect = HudUiFactory.CreateRect("StolenPrice", _canvas.transform);
            priceRect.anchorMin = priceRect.anchorMax = new Vector2(0.5f, 0.5f);
            priceRect.pivot = new Vector2(0.5f, 1f);
            priceRect.sizeDelta = new Vector2(480f, _settings.PriceFontSize * 1.5f);
            priceRect.anchoredPosition = new Vector2(0f, -_settings.PriceOffsetBelowCenter);
            _priceRect = priceRect;
            _price = CreateText("Price", priceRect, font, string.Empty, _settings.PriceFontSize,
                TextAnchor.UpperCenter, _settings.PriceColor, stretch: true);
            Shadow shadow = _price.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
            shadow.effectDistance = new Vector2(2f, -2f);
            _price.gameObject.SetActive(false);
        }

        private static Text CreateText(string name, RectTransform parent, Font font, string value, int size,
            TextAnchor anchor, Color color, bool stretch)
        {
            RectTransform rect = HudUiFactory.CreateRect(name, parent);
            if (stretch)
                HudUiFactory.Stretch(rect);
            Text text = rect.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontStyle = FontStyle.Bold;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = color;
            text.text = value;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }
    }
}
