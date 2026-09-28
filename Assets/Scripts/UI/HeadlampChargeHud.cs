using GhostHunter.Gameplay.Player;
using UnityEngine;
using UnityEngine.UI;

namespace GhostHunter.UI
{
    /// <summary>
    /// 드릴카 안에서 헤드라이트 배터리가 충전되는 동안 화면 우측 상단에 "nn%" 원형 게이지를 띄운다(기획서 §3).
    /// 크기·색은 두더지 스킬 UI 와 같다 — 같은 <see cref="MoleSkillUiSettings"/> 를 읽고, 탐지·굴착 두 칸 바로 아래
    /// 세 번째 칸에 선다. Player 프리팹에 붙어 있으며 <b>로컬 소유 플레이어에서만</b> 캔버스를 만든다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HeadlampChargeHud : MonoBehaviour
    {
        private const int CanvasSortingOrder = 201;

        /// <summary>두더지 스킬 게이지(탐지 0, 굴착 1) 아래 칸.</summary>
        private const int SlotIndex = 2;

        [SerializeField] private MoleSkillUiSettings _uiSettings;
        [SerializeField] private PlayerHeadlamp _headlamp;

        [Tooltip("원 안에 넣을 헤드라이트 아이콘(flashlights.png). 비어 있으면 원 안에 퍼센트만 쓴다.")]
        [SerializeField] private Sprite _icon;

        private Canvas _canvas;
        private GameObject _root;
        private Image _ring;
        private Text _label;
        private Texture2D _circleTexture;
        private Texture2D _ringTexture;
        private Sprite _circleSprite;
        private Sprite _ringSprite;
        private int _shownPercent = -1;

        private void Awake()
        {
            if (_uiSettings == null || _headlamp == null)
            {
                Debug.LogError(
                    $"{nameof(HeadlampChargeHud)}: UI 설정·헤드라이트가 Player 프리팹에 배선되지 않았습니다.",
                    this);
                enabled = false;
            }
        }

        private void OnDestroy()
        {
            MoleSkillHud.DestroyRuntimeObject(_circleSprite);
            MoleSkillHud.DestroyRuntimeObject(_ringSprite);
            MoleSkillHud.DestroyRuntimeObject(_circleTexture);
            MoleSkillHud.DestroyRuntimeObject(_ringTexture);
            MoleSkillHud.DestroyRuntimeObject(_canvas != null ? _canvas.gameObject : null);
        }

        private void LateUpdate()
        {
            bool local = _headlamp.IsSpawned && _headlamp.IsOwner;
            if (local && _canvas == null)
                BuildCanvas();

            if (_canvas == null)
                return;

            bool visible = local && _headlamp.IsCharging;
            if (_root.activeSelf != visible)
                _root.SetActive(visible);
            if (!visible)
                return;

            float ratio = Mathf.Clamp01(_headlamp.Battery / Mathf.Max(1f, _headlamp.MaxBattery));
            _ring.fillAmount = ratio;

            int percent = Mathf.FloorToInt(ratio * 100f + 0.0001f);
            if (percent == _shownPercent)
                return;

            _shownPercent = percent;
            _label.text = $"{percent}%";
        }

        private void BuildCanvas()
        {
            GameObject canvasObject = new("HeadlampChargeCanvas");
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
            _circleSprite = MoleSkillHud.CreateCircleSprite(
                resolution, resolution * 0.5f - 1f, out _circleTexture);
            _ringSprite = MoleSkillHud.CreateRingSprite(
                resolution,
                resolution * 0.5f - 1f,
                resolution * _uiSettings.RingThicknessRatio,
                out _ringTexture);

            float size = _uiSettings.IndicatorSize;
            _root = new GameObject("HeadlampChargeIndicator");
            _root.transform.SetParent(_canvas.transform, false);
            RectTransform rootRect = _root.AddComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(1f, 1f);
            rootRect.anchorMax = new Vector2(1f, 1f);
            rootRect.pivot = new Vector2(1f, 1f);
            rootRect.sizeDelta = new Vector2(size, size);
            rootRect.anchoredPosition = new Vector2(
                -_uiSettings.RightMargin,
                -_uiSettings.TopMargin - SlotIndex * (size + _uiSettings.VerticalSpacing));

            Image background = _root.AddComponent<Image>();
            background.sprite = _circleSprite;
            background.color = _uiSettings.BackgroundColor;
            background.raycastTarget = false;

            GameObject ringObject = new("ChargeRing");
            ringObject.transform.SetParent(_root.transform, false);
            MoleSkillHud.Stretch(ringObject.AddComponent<RectTransform>());
            _ring = ringObject.AddComponent<Image>();
            _ring.sprite = _ringSprite;
            _ring.type = Image.Type.Filled;
            _ring.fillMethod = Image.FillMethod.Radial360;
            _ring.fillOrigin = (int)Image.Origin360.Top;
            _ring.fillClockwise = true;
            _ring.color = _uiSettings.CastingColor;
            _ring.raycastTarget = false;

            if (_icon != null)
            {
                GameObject iconObject = new("Icon");
                iconObject.transform.SetParent(_root.transform, false);
                RectTransform iconRect = iconObject.AddComponent<RectTransform>();
                MoleSkillHud.Stretch(iconRect);
                float inset = size * _uiSettings.IconInsetRatio;
                iconRect.offsetMin = new Vector2(inset, inset);
                iconRect.offsetMax = new Vector2(-inset, -inset);
                Image icon = iconObject.AddComponent<Image>();
                icon.sprite = _icon;
                icon.preserveAspect = true;
                icon.raycastTarget = false;
            }

            // 아이콘이 있으면 퍼센트는 원 바로 아래에, 없으면 원 가운데에 쓴다.
            GameObject labelObject = new("Percent");
            labelObject.transform.SetParent(_root.transform, false);
            RectTransform labelRect = labelObject.AddComponent<RectTransform>();
            MoleSkillHud.Stretch(labelRect);
            if (_icon != null)
            {
                labelRect.anchorMin = new Vector2(0f, 0f);
                labelRect.anchorMax = new Vector2(1f, 0f);
                labelRect.pivot = new Vector2(0.5f, 1f);
                labelRect.sizeDelta = new Vector2(0f, size * 0.35f);
                labelRect.anchoredPosition = new Vector2(0f, -4f);
            }

            _label = labelObject.AddComponent<Text>();
            _label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _label.fontSize = Mathf.RoundToInt(size * 0.28f);
            _label.fontStyle = FontStyle.Bold;
            _label.alignment = TextAnchor.MiddleCenter;
            _label.color = _uiSettings.CooldownColor;
            _label.raycastTarget = false;

            _root.SetActive(false);
        }
    }
}
