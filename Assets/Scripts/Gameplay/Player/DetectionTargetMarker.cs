using System.Collections.Generic;
using UnityEngine;

namespace GhostHunter.Gameplay.Player
{
    /// <summary>
    /// 탐지 스킬이 표시할 작업 대상의 스캐폴드. 이 컴포넌트가 붙어 있고 활성화된 오브젝트만
    /// 로컬 시전자 화면에서 빛난다. 작업 시스템이 생기면 <see cref="SetTargetActive"/> 로
    /// 대상 여부를 교체할 수 있으며, 이 컴포넌트 자체는 판정 기준을 정의하지 않는다.
    ///
    /// <para><b>형광 덧입히기</b>(2026-10-04 사용자 요청, MS-19) — 원래 머티리얼을 바꾸지 않는다. 바꾸면 텍스처·음영이
    /// 사라져 평평한 단색이 되고, 셰이더가 모양을 그리는 얼룩(사각 메시)은 통째로 네모가 된다. 대신 두 가지로 빛을 더한다.</para>
    /// <list type="bullet">
    /// <item>일반 메시 — 같은 메시를 쓰는 자식 렌더러를 하나 더 두고 <c>GhostHunter/DetectionHighlight</c>(가산 · 윤곽이 강한 형광)로
    /// 덧그린다. 원래 렌더러의 켜짐을 따른다.</item>
    /// <item><c>_HighlightColor</c> 를 가진 셰이더(얼룩) — 그 렌더러의 MaterialPropertyBlock 에 색을 넣어 셰이더가 자기 모양대로 빛낸다.</item>
    /// </list>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DetectionTargetMarker : MonoBehaviour
    {
        private const string HighlightShaderName = "GhostHunter/DetectionHighlight";
        private const string OverlayName = "DetectionGlow";

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int HighlightColorId = Shader.PropertyToID("_HighlightColor");
        private static readonly int ShellCenterId = Shader.PropertyToID("_ShellCenter");
        private static readonly List<DetectionTargetMarker> Markers = new();

        private static bool _highlighting;
        private static DetectionSkillSettings _highlightSettings;

        [Header("작업 시스템 스캐폴드 (TEMP 배치 가능)")]
        [SerializeField] private DetectionTargetKind _kind = DetectionTargetKind.MovingFurniture;

        [Tooltip("작업 시스템이 켜고 끄는 활성 상태. 현재 임시 검증 대상은 true로 둔다.")]
        [SerializeField] private bool _targetActive = true;

        [Tooltip("대상 메시 렌더러. 비워 두면 자식 포함 렌더러를 Awake에서 한 번 수집한다.")]
        [SerializeField] private Renderer[] _renderers;

        // 덧그리기 렌더러와 그 원본(켜짐을 따라간다).
        private readonly List<(Renderer Source, MeshRenderer Overlay)> _overlays = new();
        // 셰이더가 직접 빛나는 렌더러(얼룩).
        private readonly List<Renderer> _nativeRenderers = new();
        private MaterialPropertyBlock _block;
        private Material _highlightMaterial;
        private bool _isHighlighted;

        public DetectionTargetKind Kind => _kind;
        public bool IsTargetActive => _targetActive;
        public bool IsHighlighted => _isHighlighted;

        private void Awake()
        {
            CreateHighlightMaterial();
            CacheRenderers();
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
            ClearHighlight();
        }

        private void LateUpdate()
        {
            if (!_isHighlighted)
                return;

            // 원래 렌더러가 숨겨지면(반출·풀 보관 등) 덧그리기도 숨긴다.
            foreach ((Renderer source, MeshRenderer overlay) in _overlays)
            {
                if (overlay == null)
                    continue;

                bool visible = IsShown(source);
                if (overlay.enabled != visible)
                    overlay.enabled = visible;
            }
        }

        private void OnDestroy()
        {
            ClearHighlight();
            DestroyObject(_highlightMaterial);
        }

        /// <summary>작업 시스템이 이 대상의 활성 여부를 바꾼다.</summary>
        public void SetTargetActive(bool active)
        {
            _targetActive = active;

            if (!active)
            {
                ClearHighlight();
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
                    marker.ClearHighlight();
            }
        }

        private void CreateHighlightMaterial()
        {
            Shader shader = Shader.Find(HighlightShaderName);
            if (shader == null)
            {
                Debug.LogError(
                    $"{nameof(DetectionTargetMarker)}: {HighlightShaderName} 셰이더를 찾지 못했습니다.",
                    this);
                return;
            }

            _highlightMaterial = new Material(shader)
            {
                name = $"{nameof(DetectionTargetMarker)}_RuntimeMaterial",
            };
        }

        private void CacheRenderers()
        {
            if (_renderers == null || _renderers.Length == 0)
                _renderers = GetComponentsInChildren<Renderer>(true);

            foreach (Renderer renderer in _renderers)
            {
                if (renderer == null || renderer.name == OverlayName)
                    continue;

                if (SupportsNativeHighlight(renderer))
                {
                    _nativeRenderers.Add(renderer);
                    continue;
                }

                MeshRenderer overlay = CreateOverlay(renderer);
                if (overlay != null)
                    _overlays.Add((renderer, overlay));
            }
        }

        private static bool SupportsNativeHighlight(Renderer renderer)
        {
            foreach (Material material in renderer.sharedMaterials)
            {
                if (material != null && material.HasProperty(HighlightColorId))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 원래 메시를 그대로 쓰는 덧그리기 렌더러를 자식으로 만든다(같은 위치·회전, 스케일 1). 서브메시마다 하이라이트
        /// 머티리얼을 하나씩 둔다. 메시 필터가 없는 렌더러(스킨드 메시 등)는 덧그리지 않는다.
        /// </summary>
        private MeshRenderer CreateOverlay(Renderer source)
        {
            if (_highlightMaterial == null || source is not MeshRenderer
                || !source.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null)
                return null;

            var overlayObject = new GameObject(OverlayName) { layer = source.gameObject.layer };
            overlayObject.transform.SetParent(source.transform, false);
            overlayObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;

            MeshRenderer overlay = overlayObject.AddComponent<MeshRenderer>();
            var materials = new Material[Mathf.Max(1, filter.sharedMesh.subMeshCount)];
            for (int i = 0; i < materials.Length; i++)
                materials[i] = _highlightMaterial;
            overlay.sharedMaterials = materials;
            overlay.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            overlay.receiveShadows = false;
            overlay.enabled = false;

            // 바깥 테두리 껍질을 메시 중심에서 부풀린다 — 렌더러마다 메시가 달라 블록으로 넣는다.
            var shell = new MaterialPropertyBlock();
            shell.SetVector(ShellCenterId, filter.sharedMesh.bounds.center);
            overlay.SetPropertyBlock(shell);
            return overlay;
        }

        private void ApplyHighlightFromSettings()
        {
            if (!_targetActive || _highlightSettings == null)
            {
                ClearHighlight();
                return;
            }

            Color color = ResolveColor(_kind, _highlightSettings);
            if (_highlightMaterial != null)
                _highlightMaterial.SetColor(BaseColorId, color);

            foreach ((Renderer source, MeshRenderer overlay) in _overlays)
            {
                if (overlay != null)
                    overlay.enabled = IsShown(source);
            }

            SetNativeHighlight(color);
            _isHighlighted = true;
        }

        private void ClearHighlight()
        {
            foreach ((Renderer _, MeshRenderer overlay) in _overlays)
            {
                if (overlay != null)
                    overlay.enabled = false;
            }

            if (_isHighlighted)
                SetNativeHighlight(Color.clear);
            _isHighlighted = false;
        }

        /// <summary>얼룩처럼 셰이더가 직접 빛나는 렌더러에 색을 넣는다. 블록을 읽어 덮어써 다른 값(닦기 진행)을 지우지 않는다.</summary>
        private void SetNativeHighlight(Color color)
        {
            if (_nativeRenderers.Count == 0)
                return;

            _block ??= new MaterialPropertyBlock();
            foreach (Renderer renderer in _nativeRenderers)
            {
                if (renderer == null)
                    continue;

                renderer.GetPropertyBlock(_block);
                _block.SetColor(HighlightColorId, color);
                renderer.SetPropertyBlock(_block);
            }
        }

        // 보관(FurnitureNetworkPhysics)은 renderer.forceRenderingOff 로, 반출·풀은 renderer.enabled 로 숨긴다.
        private static bool IsShown(Renderer source)
        {
            return source != null && source.enabled && !source.forceRenderingOff;
        }

        private static Color ResolveColor(
            DetectionTargetKind kind,
            DetectionSkillSettings settings)
        {
            return kind == DetectionTargetKind.Stain
                ? settings.StainColor
                : settings.MovingFurnitureColor;
        }

        private static void DestroyObject(Object target)
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
