using System.Collections.Generic;
using UnityEngine;

namespace GhostHunter.Gameplay.Ghost
{
    /// <summary>
    /// 배회용 방문 기록. 집을 층(3m)·평면 격자 칸으로 나눠 귀신이 마지막으로 지나간 시각을 적는다.
    /// 오래 안 간 칸일수록 다음 목적지 점수가 높아서, 한 방·한 층만 맴돌지 않고 다른 층까지 집 전체를 돈다.
    /// </summary>
    internal sealed class GhostRoamMemory
    {
        public const float FloorHeight = 3f;

        private readonly Dictionary<Vector3Int, float> _lastVisit = new();
        private readonly float _cellSize;
        private readonly float _memorySeconds;

        public GhostRoamMemory(float cellSize, float memorySeconds)
        {
            _cellSize = Mathf.Max(0.5f, cellSize);
            _memorySeconds = Mathf.Max(1f, memorySeconds);
        }

        public int VisitedCellCount => _lastVisit.Count;

        public void Clear() => _lastVisit.Clear();

        public void MarkVisited(Vector3 position, float time)
        {
            _lastVisit[CellOf(position)] = time;
        }

        /// <summary>그 칸에 안 간 지 몇 초인가. 처음 가는 칸과 기억 시간을 넘긴 칸은 기억 시간으로 같게 친다.</summary>
        public float Staleness(Vector3 position, float time)
        {
            return _lastVisit.TryGetValue(CellOf(position), out float last)
                ? Mathf.Clamp(time - last, 0f, _memorySeconds)
                : _memorySeconds;
        }

        /// <summary>
        /// 목적지 점수 = 안 간 시간 − 걸어가는 시간. 둘 다 초라서 단위가 맞고, 똑같이 오래 안 간 곳이면
        /// 가까운 곳부터 쓸고 지나간다(집 끝에서 끝으로 매번 가로지르지 않는다).
        /// </summary>
        public float Score(Vector3 position, float pathLength, float speed, float time)
        {
            float travel = speed > 0.01f ? pathLength / speed : pathLength;
            return Staleness(position, time) - travel;
        }

        internal Vector3Int CellOf(Vector3 position)
        {
            return new Vector3Int(
                Mathf.FloorToInt(position.x / _cellSize),
                Mathf.FloorToInt((position.y + FloorHeight * 0.5f) / FloorHeight),
                Mathf.FloorToInt(position.z / _cellSize));
        }
    }
}
