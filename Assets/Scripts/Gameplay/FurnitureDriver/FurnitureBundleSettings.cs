using UnityEngine;

namespace GhostHunter.Gameplay.FurnitureDriver
{
    /// <summary>
    /// 짐더미(분해된 큰 가구 하나를 포장한 모양)의 크기·모양·재질 수치(기획서 §4.1).
    /// 무게·명칭·내구도는 여기서 정하지 않는다 — 원본 가구의 값을 그대로 쓰며, 줄이는 것은 크기뿐이다.
    /// </summary>
    [CreateAssetMenu(fileName = "FurnitureBundleSettings_", menuName = "GhostHunter/Furniture Driver/Bundle Settings")]
    public sealed class FurnitureBundleSettings : ScriptableObject
    {
        [Header("크기 (§4.1 — 크기만 축소)")]
        [Tooltip("원본 가구의 각 변에 곱하는 비율.")]
        [SerializeField, Range(0.1f, 1f)] private float _sizeScale = 0.4f;
        [Tooltip("줄인 뒤 한 변의 최소 길이(m). 얇은 가구가 종이처럼 되는 것을 막는다.")]
        [SerializeField, Min(0.1f)] private float _minimumDimension = 0.3f;

        [Header("모양 — 가장 짧은 변 기준 비율")]
        [SerializeField, Range(0.2f, 0.9f)] private float _cornerRadiusRatio = 0.6f;
        [SerializeField, Range(0f, 0.3f)] private float _pinchDepthRatio = 0.06f;
        [SerializeField, Range(0.02f, 0.3f)] private float _strapWidthRatio = 0.12f;
        [Tooltip("끈이 홈 바닥 위로 솟은 두께. 끈 가장자리 자리의 천 높이를 넘으면 그 높이로 잘린다.")]
        [SerializeField, Range(0f, 0.1f)] private float _strapThicknessRatio = 0.04f;

        [Header("재질")]
        [SerializeField] private Material _bodyMaterial;
        [SerializeField] private Material _strapMaterial;
        [Tooltip("조준 윤곽선 셸 재질 — 다른 가구와 같은 Furniture_Outline.")]
        [SerializeField] private Material _outlineMaterial;

        public float SizeScale => _sizeScale;
        public float MinimumDimension => _minimumDimension;
        public Material BodyMaterial => _bodyMaterial;
        public Material StrapMaterial => _strapMaterial;
        public Material OutlineMaterial => _outlineMaterial;

        public FurnitureBundleShape Shape => new(
            _cornerRadiusRatio, _pinchDepthRatio, _strapWidthRatio, _strapThicknessRatio);

        public Vector3 SizeFor(Vector3 sourceSize) =>
            FurnitureBundleMesh.SizeFor(sourceSize, _sizeScale, _minimumDimension);
    }
}
