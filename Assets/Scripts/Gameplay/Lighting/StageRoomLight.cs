using UnityEngine;

namespace GhostHunter.Gameplay.Lighting
{
    /// <summary>
    /// 방 하나의 천장등. 기준 반경·밝기 가중치와 켜짐 스위치를 들고, 실제 값은
    /// <see cref="StageLightingController"/> 가 설정 배율을 곱해 넣는다. 천장 패널의 발광은
    /// 조명의 현재 밝기를 따라가므로 귀신 깜빡임(§6.5 #5)도 패널에 그대로 보인다.
    /// 네트워크 동기화 없음 — 피어마다 로컬 연출이다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Light))]
    public sealed class StageRoomLight : MonoBehaviour
    {
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [Tooltip("0 = 1층, 1 = 2층, 2 = 다락.")]
        [SerializeField, Min(0)] private int _floor;
        [Tooltip("HUD 에 보일 방 이름.")]
        [SerializeField] private string _label;
        [Tooltip("배율 1 일 때 반경(m). 방 크기에서 정했다.")]
        [SerializeField, Min(0.5f)] private float _baseRange = 6f;
        [Tooltip("설정 밝기에 곱하는 방별 가중치.")]
        [SerializeField, Min(0f)] private float _intensityWeight = 1f;
        [SerializeField] private Renderer _fixture;

        private Light _light;
        private MaterialPropertyBlock _block;
        private Color _fixtureColor;
        private float _fixtureEmission;
        private float _appliedIntensity;
        private float _lastFixtureFactor = -1f;
        private Color _lastFixtureTint;
        private bool _switchOn = true;

        public int Floor => _floor;
        public string Label => string.IsNullOrEmpty(_label) ? name : _label;
        public Light Light => _light != null ? _light : _light = GetComponent<Light>();

        /// <summary>층·방 스위치(HUD). 설정의 전체 스위치와 AND 로 합쳐진다.</summary>
        public bool SwitchOn => _switchOn;

        public void SetSwitch(bool on) => _switchOn = on;

        /// <summary>설정 배율을 적용한다. 밝기는 귀신 연출의 기준값(<c>GhostAmbientLight</c>)도 된다.</summary>
        public void Apply(StageLightingSettings settings, bool masterOn, out float intensity, out Color color)
        {
            Light light = Light;
            intensity = settings.RoomIntensity * _intensityWeight;
            color = settings.RoomColor;

            light.enabled = masterOn && _switchOn;
            light.range = _baseRange * settings.RoomRangeScale;
            light.color = color;
            light.intensity = intensity;
            light.shadows = settings.RoomShadows;
            light.shadowStrength = settings.RoomShadowStrength;

            _appliedIntensity = intensity;
            _fixtureColor = color;
            _fixtureEmission = settings.FixtureEmission;
            _lastFixtureFactor = -1f;
        }

        private void LateUpdate()
        {
            if (_fixture == null)
                return;

            // 귀신 깜빡임은 intensity 만 바꾼다 — 패널도 같은 비율로 따라간다.
            Light light = Light;
            float factor = !light.enabled || _appliedIntensity <= 0f
                ? 0f
                : Mathf.Clamp01(light.intensity / _appliedIntensity);
            // 귀신 어택의 붉은 조명도 패널 색에 반영한다.
            Color tint = light.enabled ? light.color : _fixtureColor;
            if (Mathf.Approximately(factor, _lastFixtureFactor) && tint == _lastFixtureTint)
                return;

            _lastFixtureFactor = factor;
            _lastFixtureTint = tint;
            _block ??= new MaterialPropertyBlock();
            _fixture.GetPropertyBlock(_block);
            _block.SetColor(EmissionColorId, tint * (_fixtureEmission * factor));
            _fixture.SetPropertyBlock(_block);
        }
    }
}
