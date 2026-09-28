using GhostHunter.Gameplay.Player;
using UnityEngine;
using UnityEngine.UI;

namespace GhostHunter.UI
{
    /// <summary>
    /// 라이터 연료 "nn%" 원형 게이지. 라이터를 들고 있는 동안과, 드릴카에서 연료가 차는 동안 보인다
    /// (docs/project/lighter-system.md). 크기·색은 헤드라이트 충전 UI(<see cref="HeadlampChargeHud"/>)와 같고,
    /// 그 바로 아래 칸에 선다. Player 프리팹에 붙어 있으며 <b>로컬 소유 플레이어에서만</b> 캔버스를 만든다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LighterFuelHud : MonoBehaviour
    {
        private const int CanvasSortingOrder = 201;

        /// <summary>두더지 스킬 게이지(탐지 0, 굴착 1)·헤드라이트 충전(2) 아래 칸.</summary>
        private const int SlotIndex = 3;

        [SerializeField] private MoleSkillUiSettings _uiSettings;
        [SerializeField] private PlayerLighter _lighter;

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
            if (_uiSettings == null || _lighter == null)
            {
                Debug.LogError(
                    $"{nameof(LighterFuelHud)}: UI 설정·라이터가 Player 프리팹에 배선되지 않았습니다.",
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
            bool local = _lighter.IsSpawned && _lighter.IsOwner;
            if (local && _canvas == null)
                BuildCanvas();

            if (_canvas == null)
                return;

            float ratio = Mathf.Clamp01(_lighter.Fuel / Mathf.Max(1f, _lighter.MaxFuel));
            bool visible = local && (_lighter.IsEquipped || (_lighter.IsCharging && ratio < 1f));
            if (_root.activeSelf != visible)
                _root.SetActive(visible);
            if (!visible)
                return;

            _ring.fillAmount = ratio;
            _ring.color = _lighter.IsLit || _lighter.IsCharging ? _uiSettings.CastingColor : _uiSettings.CooldownColor;

            int percent = Mathf.FloorToInt(ratio * 100f + 0.0001f);
            if (percent == _shownPercent)
                return;

            _shownPercent = percent;
            _label.text = $"라이터\n{percent}%";
        }

        private void BuildCanvas()
        {
            GameObject canvasObject = new("LighterFuelCanvas");
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
            _root = new GameObject("LighterFuelIndicator");
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

            GameObject ringObject = new("FuelRing");
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

            GameObject labelObject = new("Percent");
            labelObject.transform.SetParent(_root.transform, false);
            MoleSkillHud.Stretch(labelObject.AddComponent<RectTransform>());
            _label = labelObject.AddComponent<Text>();
            _label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _label.fontSize = Mathf.RoundToInt(size * 0.2f);
            _label.fontStyle = FontStyle.Bold;
            _label.alignment = TextAnchor.MiddleCenter;
            _label.color = _uiSettings.CooldownColor;
            _label.raycastTarget = false;

            _root.SetActive(false);
        }
    }
}
