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

        private readonly HudUiFactory _ui = new();
        private Canvas _canvas;
        private GameObject _root;
        private Image _ring;
        private Text _label;
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

        private void OnDestroy() => _ui.Dispose();

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
            _canvas = _ui.CreateCanvas("LighterFuelCanvas", transform, CanvasSortingOrder, _uiSettings.ReferenceResolution);
            int resolution = _uiSettings.TextureResolution;
            Sprite circle = _ui.CreateCircleSprite(resolution, resolution * 0.5f - 1f, "MoleSkillCircleRuntime");
            Sprite ring = _ui.CreateRingSprite(resolution, resolution * 0.5f - 1f,
                resolution * _uiSettings.RingThicknessRatio, "MoleSkillRingRuntime");
            float size = _uiSettings.IndicatorSize;
            var gauge = new RadialGaugeView(_canvas.transform, "LighterFuelIndicator", size,
                circle, ring, "FuelRing", clockwise: true);
            gauge.PlaceTopRight(new Vector2(-_uiSettings.RightMargin,
                -_uiSettings.TopMargin - SlotIndex * (size + _uiSettings.VerticalSpacing)));
            gauge.Background.color = _uiSettings.BackgroundColor;
            _root = gauge.Root;
            _ring = gauge.Ring;
            _ring.color = _uiSettings.CastingColor;
            _label = gauge.CreateLabel("Percent", Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),
                Mathf.RoundToInt(size * 0.2f));
            _label.color = _uiSettings.CooldownColor;
            _root.SetActive(false);
        }
    }
}
