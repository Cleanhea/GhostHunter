using System;
using UnityEngine;

namespace GhostHunter.Gameplay.Map
{
    /// <summary>지지면 위치와 허용 크기·풀을 지정하는 랜덤 가구 배치 후보 지점이다.</summary>
    [DisallowMultipleComponent]
    public sealed class FurnitureSpawnPoint : MonoBehaviour
    {
        [SerializeField] private int _roomId;
        [SerializeField] private FurnitureSpawnType _spawnType = FurnitureSpawnType.Floor;
        [SerializeField] private Vector3 _maximumSize = new(2.4f, 2.5f, 2.4f);
        [Tooltip("비어 있으면 같은 Spawn Type의 모든 풀을 허용한다.")]
        [SerializeField] private string[] _allowedPoolIds = Array.Empty<string>();

        public int RoomId => _roomId;
        public FurnitureSpawnType SpawnType => _spawnType;
        public Vector3 MaximumSize => _maximumSize;

        /// <summary>에디터 설치 도구에서 지점의 방·지지면·여유 공간을 구성한다.</summary>
        public void Configure(int roomId, FurnitureSpawnType spawnType, Vector3 maximumSize)
        {
            _roomId = roomId;
            _spawnType = spawnType;
            _maximumSize = maximumSize;
        }

        /// <summary>풀과 가구 치수가 이 후보에 들어가는지 검사한다.</summary>
        public bool Accepts(string poolId, FurnitureSpawnType type, Vector3 size)
        {
            if (type != _spawnType || size.x > _maximumSize.x
                || size.y > _maximumSize.y || size.z > _maximumSize.z)
                return false;

            if (_allowedPoolIds == null || _allowedPoolIds.Length == 0)
                return true;

            return Array.IndexOf(_allowedPoolIds, poolId) >= 0;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.up * _maximumSize.y * 0.5f, _maximumSize);
        }
    }
}
