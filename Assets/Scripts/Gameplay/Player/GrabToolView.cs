using UnityEngine;
using UnityEngine.Rendering;

namespace GhostHunter.Gameplay.Player
{
    /// <summary>흡착 장비 메시의 카메라별 표시와 발광을 제어한다.</summary>
    [DisallowMultipleComponent]
    public sealed class GrabToolView : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [SerializeField] private Renderer _emitter;
        [SerializeField] private Light _grabLight;

        private Renderer[] _renderers;
        private MaterialPropertyBlock _block;
        private GrabToolVisualSettings _settings;
        private bool _firstPerson;
        private bool _lit;

        private void Awake()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
            _block = new MaterialPropertyBlock();
            if (_emitter == null || _grabLight == null)
            {
                Debug.LogError($"{nameof(GrabToolView)}: 발광 메시·광원 참조가 없습니다.", this);
                enabled = false;
            }
            SetRendered(false);
        }

        /// <summary>같은 프리팹을 손 장비와 소유자 화면 장비로 사용한다.</summary>
        public void Configure(GrabToolVisualSettings settings, bool firstPerson)
        {
            _settings = settings;
            _firstPerson = firstPerson;
            if (_grabLight != null)
            {
                _grabLight.color = settings.GrabEmission / Mathf.Max(1f, settings.GrabEmission.maxColorComponent);
                _grabLight.intensity = settings.LightIntensity;
                _grabLight.range = settings.LightRange;
            }
            foreach (Renderer part in _renderers)
                part.shadowCastingMode = firstPerson ? ShadowCastingMode.Off : ShadowCastingMode.On;
            SetLit(false);
        }

        /// <summary>잡기 상태가 바뀔 때만 공유 머티리얼을 건드리지 않고 발광을 갱신한다.</summary>
        public void SetLit(bool lit)
        {
            _lit = lit;
            if (_emitter == null || _settings == null)
                return;
            _block.SetColor(BaseColorId, _settings.IdleColor);
            _block.SetColor(EmissionColorId, lit ? _settings.GrabEmission : Color.black);
            _emitter.SetPropertyBlock(_block);
            if (!lit && _grabLight != null)
                _grabLight.enabled = false;
        }

        /// <summary>렌더 직전에 해당 카메라에서 볼 장비만 표시한다.</summary>
        public void SetRendered(bool visible)
        {
            if (_renderers != null)
            {
                foreach (Renderer part in _renderers)
                    part.forceRenderingOff = !visible;
            }
            // 화면 장비는 발광만 한다. 실제 점광원은 월드 장비 하나라 이중으로 밝아지지 않는다.
            if (_grabLight != null)
                _grabLight.enabled = visible && _lit && !_firstPerson;
        }
    }
}
