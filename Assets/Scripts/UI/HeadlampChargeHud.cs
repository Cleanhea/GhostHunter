using GhostHunter.Gameplay.Player;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace GhostHunter.UI
{
    /// <summary>
    /// 드릴카 안에서 헤드라이트 배터리가 충전되는 동안 배터리를 화면 우측 상단에 "nn%" 원형 게이지로 띄운다(기획서 §3,
    /// 2026-10-29 사용자 결정 — 2026-10-04 의 "켜져 있는 동안" 표시를 원문대로 되돌렸다). 가득 차거나 드릴카를 나가면 사라진다.
    /// 크기·색은 두더지 스킬 UI 와 같다 — 같은 <see cref="MoleSkillUiSettings"/> 를 읽고, 탐지·굴착 두 칸 바로 아래
    /// 세 번째 칸에 선다. Player 프리팹에 붙어 있으며 <b>로컬 소유 플레이어에서만</b> 캔버스를 만든다.
    /// 원 안 배치는 <c>Sprite/Skill_icon/ICON_HeadLight.png</c> 목업을 따른다 — 위에 퍼센트, 아래에 손전등 아이콘.
    /// 탐지·굴착처럼 헤드라이트가 켜져 있으면 초록(on 아이콘), 꺼져 있으면 흰색(off 아이콘)이다 — 충전 중에는 둘 다 보인다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HeadlampChargeHud : MonoBehaviour
    {
        private const int CanvasSortingOrder = 201;

        /// <summary>두더지 스킬 게이지(탐지 0, 굴착 1) 아래 칸.</summary>
        private const int SlotIndex = 2;

        [SerializeField] private MoleSkillUiSettings _uiSettings;
        [SerializeField] private PlayerHeadlamp _headlamp;

        [Tooltip("헤드라이트가 켜져 있을 때의 아이콘(ICON_HeadLight_on). 비어 있으면 원 안에 퍼센트만 쓴다.")]
        [FormerlySerializedAs("_icon")]
        [SerializeField] private Sprite _onIcon;

        [Tooltip("헤드라이트가 꺼져 있을 때의 아이콘(ICON_HeadLight_off). 비어 있으면 on 아이콘을 쓴다.")]
        [SerializeField] private Sprite _offIcon;

        private readonly HudUiFactory _ui = new();
        private Canvas _canvas;
        private GameObject _root;
        private Image _ring;
        private Image _icon;
        private Text _label;
        private int _shownPercent = -1;
        private bool? _shownOn;

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

        private void OnDestroy() => _ui.Dispose();

        private void LateUpdate()
        {
            bool local = _headlamp.IsSpawned && _headlamp.IsOwner;
            if (local && _canvas == null)
                BuildCanvas();

            if (_canvas == null)
                return;

            float ratio = Mathf.Clamp01(_headlamp.Battery / Mathf.Max(1f, _headlamp.MaxBattery));
            // 드릴카 안에서 실제로 차오르는 동안만 — 가득 차면 사라진다(LighterFuelHud 와 같다).
            bool visible = local && _headlamp.IsCharging && ratio < 1f;
            if (_root.activeSelf != visible)
                _root.SetActive(visible);
            if (!visible)
                return;

            _ring.fillAmount = ratio;
            ApplyOnState(_headlamp.IsOn);

            int percent = Mathf.FloorToInt(ratio * 100f + 0.0001f);
            if (percent == _shownPercent)
                return;

            _shownPercent = percent;
            _label.text = $"{percent}%";
        }

        private void ApplyOnState(bool on)
        {
            if (_shownOn == on)
                return;

            _shownOn = on;
            Color color = on ? _uiSettings.CastingColor : _uiSettings.CooldownColor;
            _ring.color = color;
            _label.color = color;
            if (_icon != null)
                _icon.sprite = on || _offIcon == null ? _onIcon : _offIcon;
        }

        private void BuildCanvas()
        {
            _canvas = _ui.CreateCanvas("HeadlampChargeCanvas", transform, CanvasSortingOrder, _uiSettings.ReferenceResolution);
            int resolution = _uiSettings.TextureResolution;
            Sprite circle = _ui.CreateCircleSprite(resolution, resolution * 0.5f - 1f, "MoleSkillCircleRuntime");
            Sprite ring = _ui.CreateRingSprite(resolution, resolution * 0.5f - 1f,
                resolution * _uiSettings.RingThicknessRatio, "MoleSkillRingRuntime");
            float size = _uiSettings.IndicatorSize;
            var gauge = new RadialGaugeView(_canvas.transform, "HeadlampChargeIndicator", size,
                circle, ring, "ChargeRing", clockwise: true);
            gauge.PlaceTopRight(new Vector2(-_uiSettings.RightMargin,
                -_uiSettings.TopMargin - SlotIndex * (size + _uiSettings.VerticalSpacing)));
            gauge.Background.color = _uiSettings.BackgroundColor;
            _root = gauge.Root;
            _ring = gauge.Ring;
            if (_onIcon != null)
            {
                _icon = gauge.CreateIcon(_onIcon, _offIcon);
                _icon.rectTransform.anchorMin = new Vector2(0.31f, 0.16f);
                _icon.rectTransform.anchorMax = new Vector2(0.69f, 0.54f);
            }
            _label = gauge.CreateLabel("Percent", Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),
                Mathf.RoundToInt(size * (_onIcon != null ? 0.2f : 0.28f)));
            if (_onIcon != null)
            {
                _label.rectTransform.anchorMin = new Vector2(0f, 0.54f);
                _label.rectTransform.anchorMax = new Vector2(1f, 0.86f);
            }
            _label.horizontalOverflow = HorizontalWrapMode.Overflow;
            _label.verticalOverflow = VerticalWrapMode.Overflow;
            _root.SetActive(false);
        }
    }
}
