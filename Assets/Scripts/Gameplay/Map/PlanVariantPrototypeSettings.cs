using UnityEngine;

namespace GhostHunter.Gameplay.Map
{
    /// <summary>
    /// B·C안 비교용 맵 프로토타입의 임시 치수다.
    ///
    /// 이 값은 실제 게임 밸런스가 아니라 에디터 생성기가 사용할 사람 기준 치수다.
    /// 최종 맵을 채택할 때는 이 에셋을 복제해 결정된 값으로 바꾸고, 도면 문서와 함께
    /// ADR로 확정한다.
    /// </summary>
    [CreateAssetMenu(
        fileName = "PlanVariantPrototypeSettings_Default",
        menuName = "GhostHunter/Map/Plan Variant Prototype Settings")]
    public sealed class PlanVariantPrototypeSettings : ScriptableObject
    {
        [Header("[TEMP] 구조")]
        [Tooltip("층 바닥 상면 사이의 임시 높이(m). 최종 층고는 TBD.")]
        [SerializeField, Min(2f)] private float _floorPitch = 3f;

        [Tooltip("임시 벽 높이(m).")]
        [SerializeField, Min(2f)] private float _wallHeight = 2.5f;

        [Tooltip("임시 벽 두께(m).")]
        [SerializeField, Min(0.05f)] private float _wallThickness = 0.18f;

        [Tooltip("임시 바닥 슬래브 두께(m).")]
        [SerializeField, Min(0.05f)] private float _floorThickness = 0.2f;

        [Header("[TEMP] 계단")]
        [Tooltip("도면에 표기되지 않아 임시로 정한 계단 폭(m).")]
        [SerializeField, Min(0.8f)] private float _stairWidth = 2.2f;

        [Tooltip("계단의 수평 진행 길이(m). 최종 계단참·경사는 TBD.")]
        [SerializeField, Min(2f)] private float _stairRun = 5f;

        [Tooltip("시각 계단 단 수. 실제 이동은 별도 경사 콜라이더가 담당한다.")]
        [SerializeField, Range(4, 32)] private int _stairStepCount = 12;

        [Tooltip("상·하층 바닥에서 계단을 띄우는 임시 여유(m).")]
        [SerializeField, Min(0.1f)] private float _stairLandingDepth = 1.2f;

        [Tooltip("계단 경사 콜라이더의 두께(m).")]
        [SerializeField, Min(0.05f)] private float _stairColliderThickness = 0.22f;

        [Tooltip("계단 상부 바닥 개구부의 추가 여유(m).")]
        [SerializeField, Min(0f)] private float _stairOpeningMargin = 0.18f;

        [Tooltip("두 계단을 중앙에 배치할 때 다락 폭에 곱하는 임시 오프셋 비율.")]
        [SerializeField, Range(0.05f, 0.3f)] private float _stairOffsetRatio = 0.12f;

        [Header("[TEMP] 배치")]
        [Tooltip("기존 맵·B안·C안 사이의 최소 외벽 간격(m).")]
        [SerializeField, Min(0.5f)] private float _variantGap = 4f;

        [Tooltip("실내 방 문 개구부의 임시 폭(m).")]
        [SerializeField, Min(0.6f)] private float _doorWidth = 1.1f;

        [Tooltip("도면에 표기된 앞마당 대문을 위한 임시 외벽 개구부 폭(m).")]
        [SerializeField, Min(0.8f)] private float _frontDoorWidth = 3f;

        [Tooltip("B/C prototype temporary connector floor width (m). [TEMP]")]
        [SerializeField, Min(1f)] private float _connectorWidth = 3f;

        [Tooltip("Temporary front-yard depth for the B/C prototype (m). [TEMP]")]
        [SerializeField, Min(0.5f)] private float _frontYardDepth = 4f;

        [Header("[TEMP] Lights and windows")]
        [Tooltip("Create temporary point lights for the prototype interiors.")]
        [SerializeField] private bool _placeTemporaryLights = true;

        [Tooltip("Temporary point-light range (m).")]
        [SerializeField, Min(4f)] private float _temporaryLightRange = 32f;

        [Tooltip("Temporary point-light intensity.")]
        [SerializeField, Min(0.1f)] private float _temporaryLightIntensity = 1.2f;

        [Tooltip("Temporary window opening width (m).")]
        [SerializeField, Min(0.5f)] private float _windowWidth = 2.4f;

        [Tooltip("Temporary window opening height (m).")]
        [SerializeField, Min(0.4f)] private float _windowHeight = 0.9f;

        [Tooltip("Temporary window sill height from the floor (m).")]
        [SerializeField, Min(0.4f)] private float _windowSillHeight = 1.1f;

        [Header("[TEMP] Stair railings")]
        [Tooltip("Temporary stair handrail height (m). It stays outside the clear stair width.")]
        [SerializeField, Min(0.6f)] private float _stairRailHeight = 1f;

        [Tooltip("Temporary stair handrail/post thickness (m).")]
        [SerializeField, Min(0.03f)] private float _stairRailThickness = 0.08f;

        [Tooltip("방 바닥 색 구분과 방 이름 라벨을 만든다.")]
        [SerializeField] private bool _createLabels = true;

        [Tooltip("기존 Furniture 프리팹을 각 방에 실제 인스턴스로 배치한다.")]
        [SerializeField] private bool _placeFurniture = true;

        public float FloorPitch => _floorPitch;
        public float WallHeight => _wallHeight;
        public float WallThickness => _wallThickness;
        public float FloorThickness => _floorThickness;
        public float StairWidth => _stairWidth;
        public float StairRun => _stairRun;
        public int StairStepCount => _stairStepCount;
        public float StairLandingDepth => _stairLandingDepth;
        public float StairColliderThickness => _stairColliderThickness;
        public float StairOpeningMargin => _stairOpeningMargin;
        public float StairOffsetRatio => _stairOffsetRatio;
        public float VariantGap => _variantGap;
        public float DoorWidth => _doorWidth;
        public float FrontDoorWidth => _frontDoorWidth;
        public float ConnectorWidth => _connectorWidth;
        public float FrontYardDepth => _frontYardDepth;
        public bool PlaceTemporaryLights => _placeTemporaryLights;
        public float TemporaryLightRange => _temporaryLightRange;
        public float TemporaryLightIntensity => _temporaryLightIntensity;
        public float WindowWidth => _windowWidth;
        public float WindowHeight => _windowHeight;
        public float WindowSillHeight => _windowSillHeight;
        public float StairRailHeight => _stairRailHeight;
        public float StairRailThickness => _stairRailThickness;
        public bool CreateLabels => _createLabels;
        public bool PlaceFurniture => _placeFurniture;

        private void OnValidate()
        {
            _floorPitch = Mathf.Max(2f, _floorPitch);
            _wallHeight = Mathf.Max(2f, _wallHeight);
            _wallThickness = Mathf.Max(0.05f, _wallThickness);
            _floorThickness = Mathf.Max(0.05f, _floorThickness);
            _stairWidth = Mathf.Max(0.8f, _stairWidth);
            _stairRun = Mathf.Max(2f, _stairRun);
            _stairStepCount = Mathf.Clamp(_stairStepCount, 4, 32);
            _stairLandingDepth = Mathf.Max(0.1f, _stairLandingDepth);
            _stairColliderThickness = Mathf.Max(0.05f, _stairColliderThickness);
            _stairOpeningMargin = Mathf.Max(0f, _stairOpeningMargin);
            _stairOffsetRatio = Mathf.Clamp(_stairOffsetRatio, 0.05f, 0.3f);
            _variantGap = Mathf.Max(0.5f, _variantGap);
            _doorWidth = Mathf.Max(0.6f, _doorWidth);
            _frontDoorWidth = Mathf.Max(0.8f, _frontDoorWidth);
            _connectorWidth = Mathf.Max(1f, _connectorWidth);
            _frontYardDepth = Mathf.Max(0.5f, _frontYardDepth);
            _temporaryLightRange = Mathf.Max(4f, _temporaryLightRange);
            _temporaryLightIntensity = Mathf.Max(0.1f, _temporaryLightIntensity);
            _windowWidth = Mathf.Max(0.5f, _windowWidth);
            _windowHeight = Mathf.Max(0.4f, _windowHeight);
            _windowSillHeight = Mathf.Max(0.4f, _windowSillHeight);
            _stairRailHeight = Mathf.Max(0.6f, _stairRailHeight);
            _stairRailThickness = Mathf.Max(0.03f, _stairRailThickness);
        }
    }
}
