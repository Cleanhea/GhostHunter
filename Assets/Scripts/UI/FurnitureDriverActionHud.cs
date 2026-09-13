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

        private Canvas _canvas;
        private Font _font;
        private Texture2D _ringTexture;
        private Sprite _ringSprite;
        private RectTransform _gaugeRoot;
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
                _isShowingFailure = false;
            }

            if (driver != null && driver.ActionCancelSerial != _observedCancelSerial)
            {
                _observedCancelSerial = driver.ActionCancelSerial;
                BeginFailure(driver.LastCancelledAction, driver.LastCancelledProgress);
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

        private void OnDestroy()
        {
            DestroyRuntimeObject(_ringSprite);
            DestroyRuntimeObject(_ringTexture);
            DestroyRuntimeObject(_canvas != null ? _canvas.gameObject : null);
        }

        private void ShowProgress(FurnitureDriverActionKind kind, float progress)
        {
            _gaugeRoot.gameObject.SetActive(true);
            _gaugeRoot.anchoredPosition = Vector2.zero;
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
            _fill.color = _uiSettings.FailColor;
            // §6.1 — 중단된 지점에서 게이지 채움을 멈춘 채로 보여 준다.
            _fill.fillAmount = progress;
            _label.text = kind == FurnitureDriverActionKind.Assemble
                ? _uiSettings.AssembleFailText
                : _uiSettings.DisassembleFailText;
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
            GameObject canvasObject = new("FurnitureDriverActionCanvas");
            canvasObject.transform.SetParent(transform, false);

            _canvas = canvasObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.overrideSorting = true;
            _canvas.sortingOrder = CanvasSortingOrder;

            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = _uiSettings.ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            int resolution = _uiSettings.TextureResolution;
            _ringSprite = CreateRingSprite(
                resolution,
                resolution * 0.5f - 1f,
                resolution * 0.5f * _uiSettings.RingThicknessRatio,
                out _ringTexture);

            GameObject rootObject = new("ActionGauge");
            rootObject.transform.SetParent(canvasObject.transform, false);
            _gaugeRoot = rootObject.AddComponent<RectTransform>();
            _gaugeRoot.anchorMin = new Vector2(0.5f, 0.5f);
            _gaugeRoot.anchorMax = new Vector2(0.5f, 0.5f);
            _gaugeRoot.pivot = new Vector2(0.5f, 0.5f);
            _gaugeRoot.sizeDelta = new Vector2(_uiSettings.RingDiameter, _uiSettings.RingDiameter);
            _gaugeRoot.anchoredPosition = Vector2.zero;

            Image track = rootObject.AddComponent<Image>();
            track.sprite = _ringSprite;
            track.color = _uiSettings.TrackColor;
            track.raycastTarget = false;

            GameObject fillObject = new("Fill");
            fillObject.transform.SetParent(rootObject.transform, false);
            Stretch(fillObject.AddComponent<RectTransform>());
            _fill = fillObject.AddComponent<Image>();
            _fill.sprite = _ringSprite;
            _fill.type = Image.Type.Filled;
            _fill.fillMethod = Image.FillMethod.Radial360;
            _fill.fillOrigin = (int)Image.Origin360.Top;
            _fill.fillClockwise = true;
            _fill.fillAmount = 0f;
            _fill.raycastTarget = false;

            GameObject labelObject = new("Label");
            labelObject.transform.SetParent(rootObject.transform, false);
            RectTransform labelRect = labelObject.AddComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0.5f, 0f);
            labelRect.anchorMax = new Vector2(0.5f, 0f);
            labelRect.pivot = new Vector2(0.5f, 1f);
            labelRect.anchoredPosition = new Vector2(0f, -_uiSettings.LabelGap);
            labelRect.sizeDelta = new Vector2(240f, _uiSettings.FontSize * 1.6f);

            _label = labelObject.AddComponent<Text>();
            _label.font = _font;
            _label.fontSize = _uiSettings.FontSize;
            _label.fontStyle = FontStyle.Bold;
            _label.alignment = TextAnchor.UpperCenter;
            _label.color = _uiSettings.TextColor;
            _label.horizontalOverflow = HorizontalWrapMode.Overflow;
            _label.verticalOverflow = VerticalWrapMode.Overflow;
            _label.raycastTarget = false;

            // 밝은 벽·가구 앞에서도 흰 문구가 읽히도록 외곽선을 둔다.
            Outline outline = labelObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.75f);

            rootObject.SetActive(false);
        }

        private static void Stretch(RectTransform rectTransform)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
        }

        private static Sprite CreateRingSprite(
            int resolution,
            float outerRadius,
            float thickness,
            out Texture2D texture)
        {
            texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false)
            {
                name = "FurnitureDriverRingRuntime",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };

            Color32[] pixels = new Color32[resolution * resolution];
            float center = (resolution - 1) * 0.5f;
            float outerSquared = outerRadius * outerRadius;
            float innerRadius = Mathf.Max(0f, outerRadius - thickness);
            float innerSquared = innerRadius * innerRadius;

            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float distanceSquared = dx * dx + dy * dy;
                    pixels[y * resolution + x] = distanceSquared <= outerSquared
                        && distanceSquared >= innerSquared
                        ? Color.white
                        : Color.clear;
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(
                texture,
                new Rect(0f, 0f, resolution, resolution),
                new Vector2(0.5f, 0.5f),
                100f);
        }

        private static void DestroyRuntimeObject(Object target)
        {
            if (target == null)
                return;

            if (Application.isPlaying)
                Destroy(target);
            else
                DestroyImmediate(target);
        }
    }
}
