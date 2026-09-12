using GhostHunter.Core;
using GhostHunter.Gameplay.Player;
using UnityEngine;
using UnityEngine.UI;

namespace GhostHunter.UI
{
    /// <summary>
    /// Tab 홀드형 라디얼 퀵슬롯 휠. <see cref="PlayerInputReader.QuickSlotHeld"/> 동안 열리고,
    /// 마우스 델타 누적으로 슬롯을 고른 뒤 떼는 순간 확정한다. 아이템/인벤토리 시스템이 아직
    /// 없어(사용자 확정 2026-09-12) <see cref="_loadout"/>는 더미 데이터이고, 확정은 로컬 상태
    /// 변경(장착 표시)까지만 한다 — 서버 RPC는 실제 인벤토리가 붙을 때 추가한다.
    ///
    /// <para><b>열림 조건</b> — 일시정지 메뉴·굴착 잠금이 없고, 사망하지 않았고, 가구를 잡고
    /// 있지 않을 때만 연다(QS-9, 사용자 확정 + 관전 기획서 SP-2 QS-사망 게이팅, 2026-09-12).
    /// 열려 있는 동안 이 조건이 깨지면 선택을 버리고 즉시 닫는다 — 사망 시에도 같은 경로로
    /// 닫히므로 별도의 사망 처리 분기가 없다 → docs/architecture/quick-slot.md §5.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class QuickSlotWheelUi : MonoBehaviour
    {
        private const int CanvasSortingOrder = 150;

        [Header("설정")]
        [SerializeField] private QuickSlotUiSettings _uiSettings;
        [SerializeField] private QuickSlotLoadout _loadout;

        private ILocalPlayerContext _localPlayer;
        private Canvas _canvas;
        private Font _font;

        private Texture2D _circleTexture;
        private Texture2D _ringTexture;
        private Texture2D _whiteTexture;
        private Texture2D _arrowTexture;
        private Sprite _circleSprite;
        private Sprite _ringSprite;
        private Sprite _whiteSprite;
        private Sprite _arrowSprite;

        private GameObject _wheelRoot;
        private Image _dimOverlay;
        private Image[] _slotMarkers;
        private Image[] _slotIcons;
        private Text _titleText;
        private Text _descriptionText;
        private RectTransform _arrowRect;
        private Image _arrowImage;

        private Image _equippedIcon;
        private Text _equippedHintText;

        private bool _isOpen;
        private Vector2 _pointer;
        private int _selectedIndex = -1;
        private int _equippedIndex = -1;
        private QuickSlotItemDefinition _equippedItem;

        private void Awake()
        {
            if (_uiSettings == null || _loadout == null)
            {
                Debug.LogError(
                    $"{nameof(QuickSlotWheelUi)}: UI 설정 또는 로드아웃 에셋이 배선되지 않았습니다.",
                    this);
                enabled = false;
                return;
            }

            Services.TryGet(out _localPlayer);
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildCanvas();
        }

        private void OnDestroy()
        {
            DestroyRuntimeObject(_circleSprite);
            DestroyRuntimeObject(_ringSprite);
            DestroyRuntimeObject(_whiteSprite);
            DestroyRuntimeObject(_arrowSprite);
            DestroyRuntimeObject(_circleTexture);
            DestroyRuntimeObject(_ringTexture);
            DestroyRuntimeObject(_whiteTexture);
            DestroyRuntimeObject(_arrowTexture);
            DestroyRuntimeObject(_canvas != null ? _canvas.gameObject : null);
        }

        private void Update()
        {
            if (_localPlayer == null)
                Services.TryGet(out _localPlayer);

            PlayerInputReader input = _localPlayer != null ? _localPlayer.Input : null;

            if (!_isOpen)
            {
                if (input != null && input.QuickSlotPressedThisFrame && CanOpen(input))
                    Open(input);
                return;
            }

            if (input == null || !CanRemainOpen(input))
            {
                Close(input);
                return;
            }

            _pointer += input.RawLookDelta * _uiSettings.Sensitivity;
            _pointer = Vector2.ClampMagnitude(_pointer, _uiSettings.OuterRadius);

            _selectedIndex = QuickSlotSelection.Resolve(
                _pointer, _loadout.SlotCount, _uiSettings.DeadZoneRadius, _equippedIndex);

            UpdateWheelVisual();

            if (input.QuickSlotReleasedThisFrame)
            {
                Confirm(_selectedIndex);
                Close(input);
            }
        }

        private bool CanOpen(PlayerInputReader input)
        {
            return !input.IsGameplayInputLocked
                && !input.IsSkillInputLocked
                && (_localPlayer.Sanity == null || _localPlayer.Sanity.HasSanity)
                && (_localPlayer.GrabController == null || !_localPlayer.GrabController.IsHolding);
        }

        private bool CanRemainOpen(PlayerInputReader input)
        {
            return CanOpen(input);
        }

        private void Open(PlayerInputReader input)
        {
            _isOpen = true;
            _pointer = Vector2.zero;
            _selectedIndex = -1;
            input.SetWheelInputLocked(true);
            _wheelRoot.SetActive(true);
            UpdateWheelVisual();
        }

        private void Close(PlayerInputReader input)
        {
            _isOpen = false;
            input?.SetWheelInputLocked(false);
            _wheelRoot.SetActive(false);
        }

        /// <summary>확정 — 데드존(-1)이면 변경 없이 무시하고, 빈 슬롯이면 확정하지 않는다(QS-2).</summary>
        private void Confirm(int index)
        {
            if (index < 0)
                return;

            QuickSlotItemDefinition item = _loadout.GetSlot(index);
            if (item == null)
                return;

            _equippedIndex = index;
            _equippedItem = item;
            UpdateEquippedDisplay();
        }

        private void UpdateWheelVisual()
        {
            int slotCount = _loadout.SlotCount;

            for (int i = 0; i < slotCount; i++)
            {
                QuickSlotItemDefinition item = _loadout.GetSlot(i);
                bool selected = i == _selectedIndex;

                Image marker = _slotMarkers[i];
                marker.color = selected
                    ? _uiSettings.SlotHighlightColor
                    : (item != null ? _uiSettings.SlotBaseColor : _uiSettings.EmptySlotColor);

                Image icon = _slotIcons[i];
                icon.enabled = item != null;
                if (item != null)
                {
                    icon.sprite = item.Icon != null ? item.Icon : _circleSprite;
                    icon.color = item.Icon != null ? Color.white : item.PlaceholderColor;
                }
            }

            QuickSlotItemDefinition hovered = _selectedIndex >= 0 ? _loadout.GetSlot(_selectedIndex) : null;
            _titleText.text = hovered != null ? hovered.DisplayName : string.Empty;
            _descriptionText.text = hovered != null ? hovered.Description : string.Empty;

            bool hasDirection = _selectedIndex >= 0;
            _arrowImage.enabled = hasDirection;
            if (hasDirection)
            {
                // 화살표는 고정 위치에서 회전만 하지 않고, 링을 따라 마우스 각도로 궤도를 돈다.
                float pointerAngle = Mathf.Atan2(_pointer.x, _pointer.y) * Mathf.Rad2Deg;
                float radians = pointerAngle * Mathf.Deg2Rad;
                float orbitRadius = _uiSettings.OuterRadius * (1f - _uiSettings.RingThicknessRatio * 0.5f);
                _arrowRect.anchoredPosition = new Vector2(
                    Mathf.Sin(radians) * orbitRadius,
                    Mathf.Cos(radians) * orbitRadius);
                _arrowRect.localRotation = Quaternion.Euler(0f, 0f, -pointerAngle);
            }
        }

        private void UpdateEquippedDisplay()
        {
            if (_equippedItem == null)
            {
                _equippedIcon.enabled = false;
                return;
            }

            _equippedIcon.enabled = true;
            _equippedIcon.sprite = _equippedItem.Icon != null ? _equippedItem.Icon : _circleSprite;
            _equippedIcon.color = _equippedItem.Icon != null ? Color.white : _equippedItem.PlaceholderColor;
        }

        private void BuildCanvas()
        {
            GameObject canvasObject = new("QuickSlotCanvas");
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
            _arrowSprite = CreateArrowSprite(32, out _arrowTexture);

            BuildWheel(canvasObject.transform);
            BuildEquippedDisplay(canvasObject.transform);

            _wheelRoot.SetActive(false);
        }

        /// <summary>
        /// 화면 전체를 덮는 딤 오버레이 — <paramref name="parent"/>가 <see cref="_wheelRoot"/>라
        /// 휠이 열려 있을 때만 <c>SetActive</c>로 함께 켜지고 꺼진다(QS-8, 로컬 디밍만).
        /// </summary>
        private void BuildDimOverlay(Transform parent)
        {
            GameObject overlayObject = new("DimOverlay");
            overlayObject.transform.SetParent(parent, false);
            Stretch(overlayObject.AddComponent<RectTransform>());

            _dimOverlay = overlayObject.AddComponent<Image>();
            _dimOverlay.sprite = _whiteSprite;
            _dimOverlay.color = new Color(0f, 0f, 0f, _uiSettings.BackgroundDimAlpha);
            _dimOverlay.raycastTarget = false;
        }

        private void BuildWheel(Transform parent)
        {
            _wheelRoot = new GameObject("Wheel");
            _wheelRoot.transform.SetParent(parent, false);
            RectTransform wheelRect = _wheelRoot.AddComponent<RectTransform>();
            wheelRect.anchorMin = Vector2.zero;
            wheelRect.anchorMax = Vector2.one;
            wheelRect.offsetMin = Vector2.zero;
            wheelRect.offsetMax = Vector2.zero;

            // 딤 오버레이는 화면을 덮어야 하므로 wheelRect 스트레치 전체를 쓰고, 링·슬롯은
            // 그 위에 중앙 고정 크기로 따로 얹는다.
            BuildDimOverlay(_wheelRoot.transform);

            GameObject ringContainer = new("RingContainer");
            ringContainer.transform.SetParent(_wheelRoot.transform, false);
            RectTransform ringContainerRect = ringContainer.AddComponent<RectTransform>();
            ringContainerRect.anchorMin = new Vector2(0.5f, 0.5f);
            ringContainerRect.anchorMax = new Vector2(0.5f, 0.5f);
            ringContainerRect.pivot = new Vector2(0.5f, 0.5f);
            ringContainerRect.anchoredPosition = Vector2.zero;
            ringContainerRect.sizeDelta = new Vector2(_uiSettings.OuterRadius, _uiSettings.OuterRadius) * 2f;

            GameObject ringObject = new("Ring");
            ringObject.transform.SetParent(ringContainer.transform, false);
            Stretch(ringObject.AddComponent<RectTransform>());
            Image ring = ringObject.AddComponent<Image>();
            ring.sprite = _ringSprite;
            ring.color = _uiSettings.SlotBaseColor;
            ring.raycastTarget = false;

            int slotCount = _loadout.SlotCount;
            _slotMarkers = new Image[slotCount];
            _slotIcons = new Image[slotCount];
            float markerDiameter = _uiSettings.OuterRadius * _uiSettings.RingThicknessRatio * 0.9f;
            float markerRadius = _uiSettings.OuterRadius * (1f - _uiSettings.RingThicknessRatio * 0.5f);
            float slotAngleStep = 360f / slotCount;

            for (int i = 0; i < slotCount; i++)
            {
                GameObject markerObject = new($"Slot_{i}");
                markerObject.transform.SetParent(ringContainer.transform, false);
                RectTransform markerRect = markerObject.AddComponent<RectTransform>();
                markerRect.anchorMin = new Vector2(0.5f, 0.5f);
                markerRect.anchorMax = new Vector2(0.5f, 0.5f);
                markerRect.pivot = new Vector2(0.5f, 0.5f);
                markerRect.sizeDelta = new Vector2(markerDiameter, markerDiameter);

                // 12시가 0번, 시계 방향 증가 — QuickSlotSelection과 같은 각도 규약.
                float angle = i * slotAngleStep * Mathf.Deg2Rad;
                markerRect.anchoredPosition = new Vector2(
                    Mathf.Sin(angle) * markerRadius,
                    Mathf.Cos(angle) * markerRadius);

                Image marker = markerObject.AddComponent<Image>();
                marker.sprite = _circleSprite;
                marker.raycastTarget = false;
                _slotMarkers[i] = marker;

                GameObject iconObject = new("Icon");
                iconObject.transform.SetParent(markerObject.transform, false);
                RectTransform iconRect = iconObject.AddComponent<RectTransform>();
                float inset = markerDiameter * 0.2f;
                iconRect.anchorMin = Vector2.zero;
                iconRect.anchorMax = Vector2.one;
                iconRect.offsetMin = new Vector2(inset, inset);
                iconRect.offsetMax = new Vector2(-inset, -inset);
                Image icon = iconObject.AddComponent<Image>();
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                icon.enabled = false;
                _slotIcons[i] = icon;
            }

            BuildCenterPanel(ringContainer.transform);
            BuildArrow(ringContainer.transform);
        }

        private void BuildCenterPanel(Transform parent)
        {
            float centerDiameter = _uiSettings.OuterRadius * (1f - _uiSettings.RingThicknessRatio) * 1.8f;

            GameObject centerObject = new("CenterPanel");
            centerObject.transform.SetParent(parent, false);
            RectTransform centerRect = centerObject.AddComponent<RectTransform>();
            centerRect.anchorMin = new Vector2(0.5f, 0.5f);
            centerRect.anchorMax = new Vector2(0.5f, 0.5f);
            centerRect.pivot = new Vector2(0.5f, 0.5f);
            centerRect.sizeDelta = new Vector2(centerDiameter, centerDiameter);

            Image background = centerObject.AddComponent<Image>();
            background.sprite = _circleSprite;
            background.color = new Color(0.1f, 0.1f, 0.1f, 0.85f);
            background.raycastTarget = false;

            _titleText = CreateText(centerObject.transform, "Title", _uiSettings.TitleFontSize, TextAnchor.MiddleCenter);
            RectTransform titleRect = (RectTransform)_titleText.transform;
            titleRect.anchorMin = new Vector2(0.1f, 0.55f);
            titleRect.anchorMax = new Vector2(0.9f, 0.85f);
            titleRect.offsetMin = Vector2.zero;
            titleRect.offsetMax = Vector2.zero;

            _descriptionText = CreateText(
                centerObject.transform, "Description", _uiSettings.DescriptionFontSize, TextAnchor.UpperCenter);
            RectTransform descriptionRect = (RectTransform)_descriptionText.transform;
            descriptionRect.anchorMin = new Vector2(0.1f, 0.15f);
            descriptionRect.anchorMax = new Vector2(0.9f, 0.55f);
            descriptionRect.offsetMin = Vector2.zero;
            descriptionRect.offsetMax = Vector2.zero;
        }

        private void BuildArrow(Transform parent)
        {
            GameObject arrowObject = new("PointerArrow");
            arrowObject.transform.SetParent(parent, false);
            _arrowRect = arrowObject.AddComponent<RectTransform>();
            _arrowRect.anchorMin = new Vector2(0.5f, 0.5f);
            _arrowRect.anchorMax = new Vector2(0.5f, 0.5f);
            _arrowRect.pivot = new Vector2(0.5f, 0.5f);
            _arrowRect.sizeDelta = new Vector2(28f, 28f);
            // 실제 위치는 UpdateWheelVisual()이 매 프레임 포인터 각도로 다시 계산한다(링을 따라 도는 궤도).
            _arrowRect.anchoredPosition = new Vector2(
                0f, -_uiSettings.OuterRadius * (1f - _uiSettings.RingThicknessRatio * 0.5f));

            _arrowImage = arrowObject.AddComponent<Image>();
            _arrowImage.sprite = _arrowSprite;
            _arrowImage.color = _uiSettings.TextColor;
            _arrowImage.raycastTarget = false;
            _arrowImage.enabled = false;
        }

        private void BuildEquippedDisplay(Transform parent)
        {
            GameObject rootObject = new("EquippedDisplay");
            rootObject.transform.SetParent(parent, false);
            RectTransform rootRect = rootObject.AddComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(1f, 0f);
            rootRect.anchorMax = new Vector2(1f, 0f);
            rootRect.pivot = new Vector2(1f, 0f);
            rootRect.sizeDelta = new Vector2(72f, 72f);
            rootRect.anchoredPosition = new Vector2(-24f, 24f);

            Image background = rootObject.AddComponent<Image>();
            background.sprite = _circleSprite;
            background.color = _uiSettings.SlotBaseColor;
            background.raycastTarget = false;

            GameObject iconObject = new("Icon");
            iconObject.transform.SetParent(rootObject.transform, false);
            RectTransform iconRect = iconObject.AddComponent<RectTransform>();
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            iconRect.offsetMin = new Vector2(10f, 10f);
            iconRect.offsetMax = new Vector2(-10f, -10f);
            _equippedIcon = iconObject.AddComponent<Image>();
            _equippedIcon.preserveAspect = true;
            _equippedIcon.raycastTarget = false;
            _equippedIcon.enabled = false;

            _equippedHintText = CreateText(rootObject.transform, "KeyHint", 14, TextAnchor.LowerCenter);
            RectTransform hintRect = (RectTransform)_equippedHintText.transform;
            hintRect.anchorMin = new Vector2(0f, 0f);
            hintRect.anchorMax = new Vector2(1f, 0f);
            hintRect.pivot = new Vector2(0.5f, 1f);
            hintRect.anchoredPosition = new Vector2(0f, -4f);
            hintRect.sizeDelta = new Vector2(0f, 20f);
            _equippedHintText.text = "Tab";
        }

        private Text CreateText(Transform parent, string objectName, int fontSize, TextAnchor alignment)
        {
            GameObject textObject = new(objectName);
            textObject.transform.SetParent(parent, false);
            textObject.AddComponent<RectTransform>();

            Text text = textObject.AddComponent<Text>();
            text.font = _font;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = _uiSettings.TextColor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
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
                name = "QuickSlotCircleRuntime",
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
                name = "QuickSlotRingRuntime",
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
                name = "QuickSlotWhiteRuntime",
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

        /// <summary>위(12시)를 가리키는 삼각형. 화살표는 이 그래픽을 회전시켜 방향을 나타낸다.</summary>
        private static Sprite CreateArrowSprite(int resolution, out Texture2D texture)
        {
            texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false)
            {
                name = "QuickSlotArrowRuntime",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };

            Color32[] pixels = new Color32[resolution * resolution];
            for (int y = 0; y < resolution; y++)
            {
                // 위쪽(y가 클수록)일수록 폭이 좁아지는 삼각형 — 꼭짓점이 위(12시)를 가리킨다.
                float t = (float)y / (resolution - 1);
                float halfWidth = (1f - t) * resolution * 0.5f;
                float center = (resolution - 1) * 0.5f;
                for (int x = 0; x < resolution; x++)
                {
                    float dx = Mathf.Abs(x - center);
                    pixels[y * resolution + x] = dx <= halfWidth ? Color.white : Color.clear;
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
