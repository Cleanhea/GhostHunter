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
        private Canvas _canvas;
        private SkillIndicator _detectionIndicator;
        private SkillIndicator _excavationIndicator;
        private Texture2D _circleTexture;
        private Texture2D _ringTexture;
        private Sprite _circleSprite;
        private Sprite _ringSprite;
        private Texture2D _whiteTexture;
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

        private void OnDestroy()
        {
            DestroyRuntimeObject(_circleSprite);
            DestroyRuntimeObject(_ringSprite);
            DestroyRuntimeObject(_whiteSprite);
            DestroyRuntimeObject(_circleTexture);
            DestroyRuntimeObject(_ringTexture);
            DestroyRuntimeObject(_whiteTexture);
            DestroyRuntimeObject(_canvas != null ? _canvas.gameObject : null);
        }

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
            GameObject canvasObject = new("MoleSkillCanvas");
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
            _circleSprite = CreateCircleSprite(resolution, resolution * 0.5f - 1f, out _circleTexture);
            _ringSprite = CreateRingSprite(
                resolution,
                resolution * 0.5f - 1f,
                resolution * _uiSettings.RingThicknessRatio,
                out _ringTexture);
            _whiteSprite = CreateWhiteSprite(out _whiteTexture);

            _detectionCastOverlay = BuildDetectionCastOverlay();

            _detectionIndicator = BuildIndicator(
                "DetectionIndicator",
                _detectionOnIcon,
                _detectionOffIcon,
                0);
            _excavationIndicator = BuildIndicator(
                "ExcavationIndicator",
                _excavationOnIcon,
                _excavationOffIcon,
                1);
        }

        private Image BuildDetectionCastOverlay()
        {
            GameObject overlayObject = new("DetectionCastOverlay");
            overlayObject.transform.SetParent(_canvas.transform, false);

            RectTransform overlayRect = overlayObject.AddComponent<RectTransform>();
            Stretch(overlayRect);

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

        private SkillIndicator BuildIndicator(
            string objectName,
            Sprite onIcon,
            Sprite offIcon,
            int order)
        {
            GameObject root = new(objectName);
            root.transform.SetParent(_canvas.transform, false);

            RectTransform rootRect = root.AddComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(1f, 1f);
            rootRect.anchorMax = new Vector2(1f, 1f);
            rootRect.pivot = new Vector2(1f, 1f);
            rootRect.sizeDelta = new Vector2(_uiSettings.IndicatorSize, _uiSettings.IndicatorSize);
            rootRect.anchoredPosition = new Vector2(
                -_uiSettings.RightMargin,
                -_uiSettings.TopMargin - order * (_uiSettings.IndicatorSize + _uiSettings.VerticalSpacing));

            Image background = root.AddComponent<Image>();
            background.sprite = _circleSprite;
            background.color = _uiSettings.BackgroundColor;
            background.raycastTarget = false;

            GameObject ringObject = new("ProgressRing");
            ringObject.transform.SetParent(root.transform, false);
            RectTransform ringRect = ringObject.AddComponent<RectTransform>();
            Stretch(ringRect);
            Image ring = ringObject.AddComponent<Image>();
            ring.sprite = _ringSprite;
            ring.type = Image.Type.Filled;
            ring.fillMethod = Image.FillMethod.Radial360;
            ring.fillOrigin = (int)Image.Origin360.Top;
            ring.fillClockwise = false;
            ring.fillAmount = 0f;
            ring.raycastTarget = false;

            GameObject iconObject = new("Icon");
            iconObject.transform.SetParent(root.transform, false);
            RectTransform iconRect = iconObject.AddComponent<RectTransform>();
            Stretch(iconRect);
            float inset = _uiSettings.IndicatorSize * _uiSettings.IconInsetRatio;
            iconRect.offsetMin = new Vector2(inset, inset);
            iconRect.offsetMax = new Vector2(-inset, -inset);
            Image icon = iconObject.AddComponent<Image>();
            icon.sprite = onIcon;
            icon.preserveAspect = true;
            icon.raycastTarget = false;

            root.SetActive(false);
            return new SkillIndicator(root, ring, icon, onIcon, offIcon);
        }

        private void UpdateIndicator(SkillIndicator indicator, IMoleSkillStatus status)
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

        private static void Stretch(RectTransform rectTransform)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
        }

        private static Sprite CreateCircleSprite(int resolution, float radius, out Texture2D texture)
        {
            texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false)
            {
                name = "MoleSkillCircleRuntime",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };

            Color32[] pixels = new Color32[resolution * resolution];
            float center = (resolution - 1) * 0.5f;
            float radiusSquared = radius * radius;
            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    pixels[y * resolution + x] = dx * dx + dy * dy <= radiusSquared
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

        private static Sprite CreateRingSprite(
            int resolution,
            float outerRadius,
            float thickness,
            out Texture2D texture)
        {
            texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false)
            {
                name = "MoleSkillRingRuntime",
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

        private static Sprite CreateWhiteSprite(out Texture2D texture)
        {
            texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                name = "MoleSkillWhiteRuntime",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            texture.SetPixel(0, 0, Color.white);
            texture.Apply(false, true);
            return Sprite.Create(
                texture,
                new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f),
                1f);
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

        private sealed class SkillIndicator
        {
            public SkillIndicator(
                GameObject root,
                Image ring,
                Image icon,
                Sprite onIcon,
                Sprite offIcon)
            {
                Root = root;
                Ring = ring;
                Icon = icon;
                OnIcon = onIcon;
                OffIcon = offIcon;
            }

            public GameObject Root { get; }
            public Image Ring { get; }
            public Image Icon { get; }
            public Sprite OnIcon { get; }
            public Sprite OffIcon { get; }
        }
    }
}
