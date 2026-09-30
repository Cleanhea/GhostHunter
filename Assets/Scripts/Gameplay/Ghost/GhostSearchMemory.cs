using UnityEngine;

namespace GhostHunter.Gameplay.Ghost
{
    /// <summary>수색 중 확인한 격자 칸을 고정 크기로 기억한다.</summary>
    internal sealed class GhostSearchMemory
    {
        private const int Capacity = 32;
        private readonly Vector3Int[] _cells = new Vector3Int[Capacity];
        private readonly float[] _times = new float[Capacity];
        private readonly float _cellSize;
        private readonly float _memorySeconds;
        private int _count;
        private int _next;

        public GhostSearchMemory(float cellSize, float memorySeconds)
        {
            _cellSize = Mathf.Max(0.5f, cellSize);
            _memorySeconds = Mathf.Max(0.1f, memorySeconds);
        }

        public int Count => _count;

        public void Clear()
        {
            _count = 0;
            _next = 0;
        }

        public void MarkVisited(Vector3 position, float time)
        {
            Vector3Int cell = CellOf(position);
            for (int i = 0; i < _count; i++)
            {
                if (_cells[i] != cell)
                    continue;
                _times[i] = time;
                return;
            }
            int index = _count < Capacity ? _count++ : _next;
            _cells[index] = cell;
            _times[index] = time;
            _next = (index + 1) % Capacity;
        }

        public float Staleness(Vector3 position, float time)
        {
            Vector3Int cell = CellOf(position);
            for (int i = 0; i < _count; i++)
                if (_cells[i] == cell)
                    return Mathf.Clamp(time - _times[i], 0f, _memorySeconds);
            return _memorySeconds;
        }

        private Vector3Int CellOf(Vector3 position) => new(
            Mathf.FloorToInt(position.x / _cellSize),
            Mathf.FloorToInt((position.y + GhostRoamMemory.FloorHeight * 0.5f) / GhostRoamMemory.FloorHeight),
            Mathf.FloorToInt(position.z / _cellSize));
    }
}
