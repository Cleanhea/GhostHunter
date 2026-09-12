using UnityEngine;

namespace GhostHunter.Gameplay.Cleaning
{
    /// <summary>청소 프로토타입의 임시 배치 수량과 조작·연출 설정이다.</summary>
    [CreateAssetMenu(menuName = "GhostHunter/Cleaning/Settings", fileName = "CleaningSettings")]
    public sealed class CleaningSettings : ScriptableObject
    {
        [Header("TEMP — 정식 얼룩 규칙 확정 전 테스트 설정")]
        [SerializeField, Min(1)] private int _stainCount = 12;
        [SerializeField, Min(0.1f)] private float _cleanDistance = 3f;
        [SerializeField, Min(0.01f)] private float _wipeSeconds = 0.3f;
        [SerializeField, Min(0.01f)] private float _requestInterval = 0.15f;
        [SerializeField, Min(0.1f)] private float _placementRadius = 0.6f;
        [Tooltip("B안 비충돌 바닥 장식(상단 최대 0.0395m) 위에 표시하기 위한 높이 여유.")]
        [SerializeField, Min(0.001f)] private float _surfaceOffset = 0.05f;
        [SerializeField] private LayerMask _surfaceMask = Physics.DefaultRaycastLayers;

        public int StainCount => Mathf.Clamp(_stainCount, 1, 64);
        public float CleanDistance => Mathf.Max(0.1f, _cleanDistance);
        public float WipeSeconds => Mathf.Max(0.01f, _wipeSeconds);
        public float RequestInterval => Mathf.Max(0.01f, _requestInterval);
        public float PlacementRadius => Mathf.Max(0.1f, _placementRadius);
        public float SurfaceOffset => Mathf.Max(0.001f, _surfaceOffset);
        public int SurfaceMask => _surfaceMask;
    }
}
