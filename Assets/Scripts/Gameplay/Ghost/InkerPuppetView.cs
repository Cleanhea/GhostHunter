using System.Collections.Generic;
using UnityEngine;

namespace GhostHunter.Gameplay.Ghost
{
    /// <summary>
    /// 인커 현상 연출용 귀신 인형(project/ghost-inker.md IK-14 임시 해석). 실제 귀신 본체는 활동 중 숨긴 채 두고,
    /// 각 피어가 귀신 몸(<c>Body</c>) 모델을 복제해 문틈·벽 앞에 세운다. 충돌·판정은 없다 — 서버는 같은 자리를
    /// 계산해 눈 맞춤·접근만 판정한다. 목 회전은 모델에서 이름에 "head" 가 들어간 뼈를 돌리고, 없으면 모델 전체를 돌린다.
    /// 갑툭튀 인형은 <see cref="EnableGlow"/> 로 스스로 빛나고 얼굴 앞 점광원을 달아 어두운 곳에서도 보인다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InkerPuppetView : MonoBehaviour
    {
        private const string EmissionKeyword = "_EMISSION";
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private Transform _turnTarget;
        private Quaternion _turnRestLocal;
        private Renderer[] _renderers;
        private Color[] _baseColors;
        private MaterialPropertyBlock _block;
        private float _appliedAlpha = -1f;
        private Light _glowLight;
        private float _glowLightIntensity;
        private float _glowFlicker = 1f;
        private readonly List<Material> _ownedMaterials = new();

        /// <summary>선명도 1 일 때의 불투명도. 0 이면 재질 값(최소 0.6). 갑툭튀는 더 진하게 쓴다.</summary>
        public float MaxOpacity { get; set; }

        /// <summary>발광·점광원을 켰는가(<see cref="EnableGlow"/>).</summary>
        public bool IsGlowing { get; private set; }

        /// <summary>얼굴 앞 점광원(없으면 null). 테스트·디버그용.</summary>
        public Light GlowLight => _glowLight;

        /// <summary>몸 템플릿(귀신의 Body)을 복제해 인형을 만든다. 처음에는 숨겨 둔다.</summary>
        public static InkerPuppetView Create(GameObject bodyTemplate, string name)
        {
            if (bodyTemplate == null)
                return null;

            GameObject copy = Instantiate(bodyTemplate);
            copy.name = name;
            copy.transform.SetParent(null, false);
            copy.SetActive(true);
            InkerPuppetView view = copy.AddComponent<InkerPuppetView>();
            view.Initialize();
            view.SetAlpha(0f);
            return view;
        }

        private void Initialize()
        {
            _block = new MaterialPropertyBlock();
            _renderers = GetComponentsInChildren<Renderer>(true);
            _baseColors = new Color[_renderers.Length];
            for (int i = 0; i < _renderers.Length; i++)
            {
                Material material = _renderers[i].sharedMaterial;
                _baseColors[i] = material != null && material.HasProperty(BaseColorId)
                    ? material.GetColor(BaseColorId)
                    : new Color(1f, 1f, 1f, 0.6f);
                _renderers[i].enabled = true;
            }

            _turnTarget = FindHead(transform);
            if (_turnTarget == null)
                _turnTarget = transform.childCount > 0 ? transform.GetChild(0) : transform;
            _turnRestLocal = _turnTarget == transform ? Quaternion.identity : _turnTarget.localRotation;
        }

        /// <summary>
        /// 인형 자세. <paramref name="bodyYaw"/>: 몸 방향(도). <paramref name="headYawOffset"/>: 몸 기준 목 회전(도).
        /// <paramref name="rollDegrees"/>: 고개 기울기(도). <paramref name="alpha"/>: 0~1 선명도(0 이면 숨김).
        /// </summary>
        public void Pose(Vector3 position, float bodyYaw, float headYawOffset, float rollDegrees, float alpha)
        {
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, bodyYaw, 0f));
            if (_turnTarget != transform)
            {
                // 쉬는 자세(몸 기준)를 먼저 세운 뒤 월드 위축 회전(목)·앞축 회전(기울기)을 얹는다 — 뼈 축 방향과 무관하다.
                _turnTarget.localRotation = _turnRestLocal;
                Quaternion rest = _turnTarget.rotation;
                Vector3 forward = Quaternion.Euler(0f, bodyYaw + headYawOffset, 0f) * Vector3.forward;
                _turnTarget.rotation = Quaternion.AngleAxis(rollDegrees, forward)
                    * Quaternion.AngleAxis(headYawOffset, Vector3.up) * rest;
            }
            else
            {
                transform.rotation = Quaternion.AngleAxis(rollDegrees, Quaternion.Euler(0f, bodyYaw, 0f) * Vector3.forward)
                    * Quaternion.Euler(0f, bodyYaw + headYawOffset, 0f);
            }

            SetAlpha(alpha);
        }

        public void Hide() => SetAlpha(0f);

        /// <summary>
        /// 조명과 무관하게 보이도록 몸을 스스로 빛나게 하고(<c>_EmissionColor</c>), 얼굴 앞 아래쪽에 그림자 없는 점광원을 단다.
        /// 어두운 집에서도 갑툭튀가 보여야 한다(사용자 요청 2026-11-16). 재질에 발광 키워드가 꺼져 있으면 이 인형 전용 사본에서 켠다.
        /// </summary>
        public void EnableGlow(Color emission, Color lightColor, float lightIntensity, float lightRange, float faceHeight)
        {
            if (_renderers == null)
                return;

            bool emissive = emission.maxColorComponent > 0f;
            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer renderer = _renderers[i];
                if (renderer == null)
                    continue;
                if (emissive)
                    EnsureEmissionKeyword(renderer);
                renderer.GetPropertyBlock(_block);
                _block.SetColor(EmissionColorId, emissive ? emission : Color.black);
                renderer.SetPropertyBlock(_block);
            }

            if (lightIntensity > 0f && lightRange > 0f)
            {
                if (_glowLight == null)
                {
                    var lightObject = new GameObject("GlowLight");
                    lightObject.layer = gameObject.layer;
                    lightObject.transform.SetParent(transform, false);
                    _glowLight = lightObject.AddComponent<Light>();
                    _glowLight.type = LightType.Point;
                    _glowLight.shadows = LightShadows.None;
                    _glowLight.renderMode = LightRenderMode.ForcePixel;
                }

                // 턱 아래 앞쪽에서 비춘다 — 얼굴에 아래에서 올려 비춘 그늘이 진다.
                _glowLight.transform.localPosition = new Vector3(0f, Mathf.Max(0.2f, faceHeight - 0.45f), 0.35f);
                _glowLight.color = lightColor;
                _glowLight.range = lightRange;
                _glowLightIntensity = lightIntensity;
            }
            else if (_glowLight != null)
            {
                Destroy(_glowLight.gameObject);
                _glowLight = null;
            }

            IsGlowing = emissive || _glowLight != null;
            UpdateGlowLight(_appliedAlpha < 0f ? 0f : _appliedAlpha);
        }

        /// <summary>점광원 깜빡임 배율(1 = 기준 세기). 다음 <see cref="Pose"/>·<see cref="SetAlpha"/> 에 반영된다.</summary>
        public void SetGlowFlicker(float flicker) => _glowFlicker = Mathf.Max(0f, flicker);

        public void SetAlpha(float alpha)
        {
            alpha = Mathf.Clamp01(alpha);
            UpdateGlowLight(alpha);
            if (Mathf.Approximately(alpha, _appliedAlpha) || _renderers == null)
                return;

            _appliedAlpha = alpha;
            bool visible = alpha > 0.01f;
            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer renderer = _renderers[i];
                if (renderer == null)
                    continue;
                renderer.enabled = visible;
                if (!visible)
                    continue;
                renderer.GetPropertyBlock(_block);
                Color color = _baseColors[i];
                color.a = Mathf.Clamp01((MaxOpacity > 0f ? MaxOpacity : Mathf.Max(color.a, 0.6f)) * alpha);
                _block.SetColor(BaseColorId, color);
                renderer.SetPropertyBlock(_block);
            }
        }

        private void UpdateGlowLight(float alpha)
        {
            if (_glowLight == null)
                return;
            bool on = alpha > 0.01f;
            _glowLight.enabled = on;
            if (on)
                _glowLight.intensity = _glowLightIntensity * alpha * _glowFlicker;
        }

        private void EnsureEmissionKeyword(Renderer renderer)
        {
            Material[] shared = renderer.sharedMaterials;
            bool missing = false;
            foreach (Material material in shared)
            {
                if (material != null && material.HasProperty(EmissionColorId) && !material.IsKeywordEnabled(EmissionKeyword))
                    missing = true;
            }

            if (!missing)
                return;

            // renderer.materials 는 이 렌더러 전용 사본을 만든다 — 원본 재질(M_Ghost)은 건드리지 않는다.
            Material[] owned = renderer.materials;
            foreach (Material material in owned)
            {
                if (material == null)
                    continue;
                _ownedMaterials.Add(material);
                if (material.HasProperty(EmissionColorId))
                    material.EnableKeyword(EmissionKeyword);
            }
        }

        private void OnDestroy()
        {
            foreach (Material material in _ownedMaterials)
            {
                if (material != null)
                    Destroy(material);
            }

            _ownedMaterials.Clear();
        }

        private static Transform FindHead(Transform root)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child != root && child.name.IndexOf("head", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return child;
            }

            return null;
        }
    }
}
