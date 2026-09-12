using System;
using System.Collections.Generic;

namespace GhostHunter.Gameplay.Map
{
    /// <summary>가구·후보 중복과 공간 겹침을 피하는 유한 탐색으로 전체 배치 계획을 구한다.</summary>
    public static class FurniturePlacementPlanner
    {
        public readonly struct Box
        {
            public readonly float MinX, MinY, MinZ, MaxX, MaxY, MaxZ;

            public Box(float minX, float minY, float minZ, float maxX, float maxY, float maxZ)
            {
                MinX = minX; MinY = minY; MinZ = minZ;
                MaxX = maxX; MaxY = maxY; MaxZ = maxZ;
            }

            public bool IsValid => IsFinite(MinX) && IsFinite(MinY) && IsFinite(MinZ)
                && IsFinite(MaxX) && IsFinite(MaxY) && IsFinite(MaxZ)
                && MinX < MaxX && MinY < MaxY && MinZ < MaxZ;

            public bool Overlaps(Box other) => MinX < other.MaxX && MaxX > other.MinX
                && MinY < other.MaxY && MaxY > other.MinY && MinZ < other.MaxZ && MaxZ > other.MinZ;

            private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public readonly struct Request
        {
            public readonly int Pool;
            public readonly int Room;
            public readonly bool IsTarget;

            public Request(int pool, int room = -1, bool isTarget = true)
            {
                Pool = pool; Room = room; IsTarget = isTarget;
            }
        }

        public readonly struct Candidate
        {
            public readonly int Pool, Item, Point, Room;
            public readonly Box Bounds;

            public Candidate(int pool, int item, int point, int room, Box bounds)
            {
                Pool = pool; Item = item; Point = point; Room = room; Bounds = bounds;
            }
        }

        /// <summary>성공 시 요청 순서의 전체 계획을 반환하며 실패 시 빈 배열만 반환한다.</summary>
        public static bool TryPlan(int itemCount, int pointCount, Request[] requests,
            Candidate[] candidates, int maximumTargetsPerRoom, int searchBudget, Random random,
            out Candidate[] plan, out string error)
        {
            plan = Array.Empty<Candidate>();
            error = null;
            if (itemCount < 0 || pointCount < 0 || requests == null || candidates == null
                || random == null || searchBudget < 1 || maximumTargetsPerRoom < 0)
            {
                error = "배치 입력 또는 탐색 한도가 유효하지 않습니다.";
                return false;
            }

            foreach (Candidate candidate in candidates)
            {
                if (candidate.Pool < 0 || candidate.Item < 0 || candidate.Item >= itemCount
                    || candidate.Point < 0 || candidate.Point >= pointCount || candidate.Room < 0
                    || !candidate.Bounds.IsValid)
                {
                    error = "배치 후보의 식별자 또는 경계가 유효하지 않습니다.";
                    return false;
                }
            }

            if (requests.Length > itemCount || requests.Length > pointCount)
            {
                error = "요청 수량보다 씬 가구 풀 또는 후보 지점이 적습니다.";
                return false;
            }

            var choices = new List<int>[requests.Length];
            var order = new int[requests.Length];
            for (int requestIndex = 0; requestIndex < requests.Length; requestIndex++)
            {
                Request request = requests[requestIndex];
                if (request.Pool < 0 || request.Room < -1)
                {
                    error = "요청의 풀 또는 방 식별자가 유효하지 않습니다.";
                    return false;
                }
                var list = new List<int>();
                for (int candidateIndex = 0; candidateIndex < candidates.Length; candidateIndex++)
                {
                    Candidate candidate = candidates[candidateIndex];
                    if (candidate.Pool == request.Pool && (request.Room < 0 || candidate.Room == request.Room))
                        list.Add(candidateIndex);
                }
                if (list.Count == 0)
                {
                    error = $"풀 {request.Pool}, 방 {request.Room}의 유효한 후보가 없습니다.";
                    return false;
                }
                Shuffle(list, random);
                choices[requestIndex] = list;
                order[requestIndex] = requestIndex;
            }
            // 후보가 적은 요청부터 풀어 공유 후보를 먼저 소비하는 실패를 줄인다.
            Array.Sort(order, (left, right) =>
            {
                int comparison = choices[left].Count.CompareTo(choices[right].Count);
                return comparison != 0 ? comparison : left.CompareTo(right);
            });

            var selected = new Candidate[requests.Length];
            var usedItems = new bool[itemCount];
            var usedPoints = new bool[pointCount];
            var roomTargets = new Dictionary<int, int>();
            int remaining = searchBudget;
            if (!Search(0))
            {
                error = remaining <= 0 ? "배치 탐색 한도를 초과했습니다. 후보·풀·설정을 확인하세요."
                    : "수량·방 분포·겹침 조건을 함께 충족하는 배치가 없습니다.";
                return false;
            }
            plan = selected;
            return true;

            bool Search(int depth)
            {
                if (depth == order.Length)
                    return true;
                int requestIndex = order[depth];
                Request request = requests[requestIndex];
                // 동일 수량일 때에도 방마다 흩어 놓도록 덜 채워진 방을 우선한다.
                var ranked = new List<int>(choices[requestIndex]);
                var rank = new Dictionary<int, int>();
                for (int i = 0; i < ranked.Count; i++)
                    rank[ranked[i]] = i;
                ranked.Sort((left, right) =>
                {
                    int comparison = Count(candidates[left].Room).CompareTo(Count(candidates[right].Room));
                    return comparison != 0 ? comparison : rank[left].CompareTo(rank[right]);
                });
                foreach (int candidateIndex in ranked)
                {
                    if (--remaining < 0)
                        return false;
                    Candidate candidate = candidates[candidateIndex];
                    if (usedItems[candidate.Item] || usedPoints[candidate.Point])
                        continue;
                    int count = Count(candidate.Room);
                    if (request.IsTarget && maximumTargetsPerRoom > 0 && count >= maximumTargetsPerRoom)
                        continue;
                    bool overlaps = false;
                    for (int i = 0; i < depth; i++)
                    {
                        if (candidate.Bounds.Overlaps(selected[order[i]].Bounds))
                        {
                            overlaps = true;
                            break;
                        }
                    }
                    if (overlaps)
                        continue;
                    selected[requestIndex] = candidate;
                    usedItems[candidate.Item] = true;
                    usedPoints[candidate.Point] = true;
                    if (request.IsTarget)
                        roomTargets[candidate.Room] = count + 1;
                    if (Search(depth + 1))
                        return true;
                    usedItems[candidate.Item] = false;
                    usedPoints[candidate.Point] = false;
                    if (request.IsTarget)
                        roomTargets[candidate.Room] = count;
                }
                return false;
            }

            int Count(int room) => roomTargets.TryGetValue(room, out int count) ? count : 0;
        }

        private static void Shuffle(List<int> list, Random random)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int other = random.Next(i + 1);
                (list[i], list[other]) = (list[other], list[i]);
            }
        }
    }
}
