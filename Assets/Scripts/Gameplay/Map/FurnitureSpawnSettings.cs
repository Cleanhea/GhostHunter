using System;
using UnityEngine;

namespace GhostHunter.Gameplay.Map
{
    /// <summary>맵별 랜덤 가구 풀의 선택 수량과 생성 검증 한도를 정의한다.</summary>
    [CreateAssetMenu(fileName = "FurnitureSpawnSettings", menuName = "GhostHunter/Map/Furniture Spawn Settings")]
    public sealed class FurnitureSpawnSettings : ScriptableObject
    {
        [Serializable]
        public sealed class PoolRule
        {
            [SerializeField] private string _poolId = "Box";
            [SerializeField] private FurnitureSpawnType _spawnType = FurnitureSpawnType.Floor;
            [SerializeField, Min(0)] private int _minimumCount = 1;
            [SerializeField, Min(0)] private int _maximumCount = 1;
            [Tooltip("true면 방마다 수량을 선택한다. false면 House 전체 수량이다.")]
            [SerializeField] private bool _perRoom;
            [SerializeField] private bool _isWorkTarget = true;

            public string PoolId => _poolId;
            public FurnitureSpawnType SpawnType => _spawnType;
            public int MinimumCount => _minimumCount;
            public int MaximumCount => _maximumCount;
            public bool PerRoom => _perRoom;
            public bool IsWorkTarget => _isWorkTarget;

            /// <summary>풀의 수량과 대상 여부를 구성한다.</summary>
            public PoolRule(string poolId, FurnitureSpawnType spawnType, int minimumCount,
                int maximumCount, bool perRoom = false, bool isWorkTarget = true)
            {
                _poolId = poolId;
                _spawnType = spawnType;
                _minimumCount = minimumCount;
                _maximumCount = maximumCount;
                _perRoom = perRoom;
                _isWorkTarget = isWorkTarget;
            }
        }

        [Header("맵별 가구 풀")]
        [SerializeField] private PoolRule[] _pools = Array.Empty<PoolRule>();
        [SerializeField, Min(0)] private int _minimumTargetTypes = 4;
        [SerializeField, Min(0)] private int _maximumTargetTypes = 4;
        [Tooltip("0이면 방별 상한을 적용하지 않는다. 최종 집중 제한 수치는 MG-10에서 결정한다.")]
        [SerializeField, Min(0)] private int _maximumTargetsPerRoom;

        [Header("생성·재현")]
        [SerializeField] private bool _useFixedSeed;
        [SerializeField] private int _seed = 1;
        [Tooltip("배치 탐색 연산의 상한이다. 초과 시 부분 배치 없이 실패한다.")]
        [SerializeField, Min(1)] private int _searchBudget = 20000;
        [SerializeField] private LayerMask _obstacleMask = ~0;

        [Header("B안 후보 지점 설치용 [TEMP]")]
        [SerializeField, Min(1)] private int _floorCandidatesPerRoom = 7;
        [SerializeField, Min(0.1f)] private float _candidateSpacing = 1.5f;
        [SerializeField, Min(0.1f)] private float _wallInset = 1.5f;
        [Tooltip("방 중앙을 십자로 비워 문으로 가는 통로를 보존한다.")]
        [SerializeField, Min(0.1f)] private float _clearPathWidth = 1.2f;

        public PoolRule[] Pools => _pools;
        public int MinimumTargetTypes => _minimumTargetTypes;
        public int MaximumTargetTypes => _maximumTargetTypes;
        public int MaximumTargetsPerRoom => _maximumTargetsPerRoom;
        public bool UseFixedSeed => _useFixedSeed;
        public int Seed => _seed;
        public int SearchBudget => _searchBudget;
        public int ObstacleMask => _obstacleMask.value;
        public int FloorCandidatesPerRoom => _floorCandidatesPerRoom;
        public float CandidateSpacing => _candidateSpacing;
        public float WallInset => _wallInset;
        public float ClearPathWidth => _clearPathWidth;

        /// <summary>기획서의 임시 예시를 설정 에셋에 기록한다.</summary>
        public void ConfigurePrototypeTargets()
        {
            _pools = new[]
            {
                new PoolRule("Sofa", FurnitureSpawnType.Floor, 2, 2),
                new PoolRule("Drawer", FurnitureSpawnType.Floor, 3, 3),
                new PoolRule("Chair", FurnitureSpawnType.Floor, 5, 5),
                new PoolRule("Box", FurnitureSpawnType.Floor, 6, 6),
            };
            _minimumTargetTypes = _maximumTargetTypes = 4;
        }
    }
}
