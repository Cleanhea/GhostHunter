using System.Collections.Generic;
using UnityEngine;

namespace GhostHunter.Gameplay.Player
{
    /// <summary>
    /// 탐지 스킬이 표시할 작업 대상의 스캐폴드. 이 컴포넌트가 붙어 있고 활성화된 오브젝트만
    /// 로컬 시전자 화면에서 빛난다. 작업 시스템이 생기면 <see cref="SetTargetActive"/> 로
    /// 대상 여부를 교체할 수 있으며, 이 컴포넌트 자체는 판정 기준을 정의하지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DetectionTargetMarker : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly List<DetectionTargetMarker> Markers = new();

        private static bool _highlighting;
        private static DetectionSkillSettings _highlightSettings;

        [Header("작업 시스템 스캐폴드 (TEMP 배치 가능)")]
        [SerializeField] private DetectionTargetKind _kind = DetectionTargetKind.MovingFurniture;

        [Tooltip("작업 시스템이 켜고 끄는 활성 상태. 현재 임시 검증 대상은 true로 둔다.")]
        [SerializeField] private bool _targetActive = true;

        [Tooltip("대상 메시 렌더러. 비워 두면 자식 포함 렌더러를 Awake에서 한 번 수집한다.")]
        [SerializeField] private Renderer[] _renderers;

        private Material[][] _originalMaterials;
        private Material[][] _highlightMaterials;
        private Material _highlightMaterial;
        private bool _isHighlighted;

        public DetectionTargetKind Kind => _kind;
        public bool IsTargetActive => _targetActive;
        public bool IsHighlighted => _isHighlighted;

        private void Awake()
        {
            CacheRenderers();
            CreateHighlightMaterial();
        }

        private void OnEnable()
        {
            if (!Markers.Contains(this))
                Markers.Add(this);

            if (_highlighting && _highlightSettings != null)
                ApplyHighlightFromSettings();
        }

        private void OnDisable()
        {
            Markers.Remove(this);
            RestoreOriginalMaterials();
        }

        private void OnDestroy()
        {
            RestoreOriginalMaterials();

            if (_highlightMaterial == null)
                return;

            if (Application.isPlaying)
                Destroy(_highlightMaterial);
            else
                DestroyImmediate(_highlightMaterial);
        }

        /// <summary>작업 시스템이 이 대상의 활성 여부를 바꾼다.</summary>
        public void SetTargetActive(bool active)
        {
            _targetActive = active;

            if (!active)
            {
                RestoreOriginalMaterials();
                return;
            }

            if (_highlighting && _highlightSettings != null)
                ApplyHighlightFromSettings();
        }

        /// <summary>
        /// 로컬 시전자 화면의 모든 활성 마커에 하이라이트를 켜거나 끈다.
        /// 다른 클라이언트에는 별도 호출이 없으므로 상태가 복제되지 않는다.
        /// </summary>
        public static void SetAllHighlighted(bool highlighted, DetectionSkillSettings settings)
        {
            _highlighting = highlighted;
            _highlightSettings = highlighted ? settings : null;

            for (int i = Markers.Count - 1; i >= 0; i--)
            {
                DetectionTargetMarker marker = Markers[i];
                if (marker == null)
                {
                    Markers.RemoveAt(i);
                    continue;
                }

                if (highlighted && settings != null)
                    marker.ApplyHighlightFromSettings();
                else
                    marker.RestoreOriginalMaterials();
            }
        }

        private void CacheRenderers()
        {
            if (_renderers == null || _renderers.Length == 0)
                _renderers = GetComponentsInChildren<Renderer>(true);

            _originalMaterials = new Material[_renderers.Length][];
            _highlightMaterials = new Material[_renderers.Length][];

            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer renderer = _renderers[i];
                if (renderer == null)
                    continue;

                Material[] originals = renderer.sharedMaterials;
                _originalMaterials[i] = originals;
                if (originals == null || originals.Length == 0)
                    continue;

                var highlights = new Material[originals.Length];
                for (int slot = 0; slot < highlights.Length; slot++)
                    highlights[slot] = _highlightMaterial;

                _highlightMaterials[i] = highlights;
            }
        }

        private void CreateHighlightMaterial()
        {
            Shader shader = Shader.Find("GhostHunter/DetectionHighlight");
            if (shader == null)
            {
                Debug.LogError(
                    $"{nameof(DetectionTargetMarker)}: GhostHunter/DetectionHighlight 셰이더를 찾지 못했습니다.",
                    this);
                return;
            }

            _highlightMaterial = new Material(shader)
            {
                name = $"{nameof(DetectionTargetMarker)}_RuntimeMaterial",
            };

            if (_highlightMaterials == null)
                return;

            for (int i = 0; i < _highlightMaterials.Length; i++)
            {
                Material[] slots = _highlightMaterials[i];
                if (slots == null)
                    continue;

                for (int slot = 0; slot < slots.Length; slot++)
                    slots[slot] = _highlightMaterial;
            }
        }

        private void ApplyHighlightFromSettings()
        {
            if (!_targetActive || _highlightSettings == null)
            {
                RestoreOriginalMaterials();
                return;
            }

            if (_highlightMaterial == null)
                return;

            // Outline/Silhouette은 MS-19에서 다시 정한다. 현재 선택값은 전신 발광이며,
            // enum을 통해 작업 시스템이 바뀌어도 마커의 배선 지점은 유지한다.
            _highlightMaterial.SetColor(
                BaseColorId,
                ResolveColor(_kind, _highlightSettings));

            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer renderer = _renderers[i];
                Material[] highlights = _highlightMaterials[i];
                if (renderer == null || highlights == null)
                    continue;

                renderer.sharedMaterials = highlights;
            }

            _isHighlighted = true;
        }

        private void RestoreOriginalMaterials()
        {
            if (_renderers == null || _originalMaterials == null)
            {
                _isHighlighted = false;
                return;
            }

            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer renderer = _renderers[i];
                Material[] originals = _originalMaterials[i];
                if (renderer == null || originals == null)
                    continue;

                renderer.sharedMaterials = originals;
            }

            _isHighlighted = false;
        }

        private static Color ResolveColor(
            DetectionTargetKind kind,
            DetectionSkillSettings settings)
        {
            return kind == DetectionTargetKind.Stain
                ? settings.StainColor
                : settings.MovingFurnitureColor;
        }
    }
}
