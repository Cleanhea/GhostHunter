using System.Collections.Generic;
using GhostHunter.Core;
using GhostHunter.Gameplay.Interaction;
using UnityEngine;
using UnityEngine.AI;

namespace GhostHunter.Gameplay.Ghost
{
    /// <summary>귀신의 NavMesh·경로 코너·이동을 소유한다. 서버 호출과 목적지 선택은 Controller가 맡는다.</summary>
    internal sealed class GhostNavigation : System.IDisposable
    {
        public const float SampleRadius = 1f;
        public const float ArrivalVerticalTolerance = 1.2f;

        private const float TurnSpeedDegrees = 540f;
        private const float MinRoamGiveUpSeconds = 4f;
        private const float NavSampleFallbackRadius = 2.5f;
        private const float StuckCheckInterval = 1f;
        private const float StuckMinProgress = 0.25f;

        private readonly CharacterController _controller;
        private readonly Transform _transform;
        private readonly GhostPrototypeSettings _settings;
        private readonly System.Func<Vector3[], int, bool> _pathCrossesClosedDoor;
        private readonly Object _context;
        private readonly NavMeshPath _navPath = new();

        private float _pathRebuildRemaining;
        private readonly Vector3[] _pathCorners = new Vector3[64];
        private readonly Vector3[] _probeCorners = new Vector3[64];
        private int _pathCornerCount;
        private int _pathCornerIndex;
        private Vector3 _pathDestination;
        private bool _hasPath;
        private bool _navigationWarningLogged;
        private float _stuckCheckRemaining;
        private Vector3 _stuckCheckPosition;
        private NavMeshData _navMeshData;
        private NavMeshDataInstance _navMeshInstance;
        private Vector3[] _navVertices;
        private int[] _navIndices;
        private float[] _navCumulativeArea;
        private float _navTotalArea;

        public float RebuildRemaining
        {
            get => _pathRebuildRemaining;
            set => _pathRebuildRemaining = value;
        }

        public GhostNavigation(CharacterController controller, GhostPrototypeSettings settings,
            System.Func<Vector3[], int, bool> pathCrossesClosedDoor, Object context)
        {
            _controller = controller;
            _transform = controller.transform;
            _settings = settings;
            _pathCrossesClosedDoor = pathCrossesClosedDoor;
            _context = context;
        }

        public Vector3 GetWaypointOr(Vector3 fallback) => _hasPath && _pathCornerIndex < _pathCornerCount
            ? _pathCorners[_pathCornerIndex] : fallback;

        public void InvalidatePath() => _hasPath = false;

        public void Dispose() => RemoveNavMesh();

        /// <summary>경로 길이를 걸어갈 시간에 여유를 더한 배회 포기 시간. 층을 건너는 긴 경로도 끝까지 간다.</summary>
        public static float RoamGiveUpSeconds(float pathLength, float speed)
        {
            if (speed <= 0.01f)
                return MinRoamGiveUpSeconds;
            return Mathf.Max(MinRoamGiveUpSeconds, pathLength / speed * 1.5f + 2f);
        }

        /// <summary>평면 거리가 도착 반경 안이고 같은 층(높이 차 <see cref="ArrivalVerticalTolerance"/> 이하)인가.</summary>
        public static bool HasArrived(Vector3 position, Vector3 destination, float reachDistance)
        {
            Vector3 flat = destination - position;
            if (Mathf.Abs(flat.y) > ArrivalVerticalTolerance)
                return false;
            flat.y = 0f;
            return flat.sqrMagnitude <= reachDistance * reachDistance;
        }

        public bool HasArrived(Vector3 destination)
        {
            return HasArrived(_transform.position, destination, _settings.ReachDistance);
        }

        public bool TryRandomNavPoint(out Vector3 point)
        {
            point = default;
            if (_navCumulativeArea == null || _navCumulativeArea.Length == 0)
                return false;

            int triangle = System.Array.BinarySearch(_navCumulativeArea, Random.value * _navTotalArea);
            if (triangle < 0)
                triangle = ~triangle;
            triangle = Mathf.Min(triangle, _navCumulativeArea.Length - 1);

            Vector3 a = _navVertices[_navIndices[triangle * 3]];
            Vector3 b = _navVertices[_navIndices[triangle * 3 + 1]];
            Vector3 c = _navVertices[_navIndices[triangle * 3 + 2]];
            float u = Random.value;
            float v = Random.value;
            if (u + v > 1f)
            {
                u = 1f - u;
                v = 1f - v;
            }

            point = a + (b - a) * u + (c - a) * v;
            return true;
        }

        /// <summary>
        /// 현재 위치에서 목적지까지 완전한 경로가 있으면 그 길이를 돌려준다. 현재 경로는 건드리지 않는다.
        /// NavMesh 는 문을 굽지 않으므로 <paramref name="avoidClosedDoors"/> 면 닫힌 문을 지나는 경로를 따로 거른다.
        /// </summary>
        public bool TryMeasurePath(Vector3 destination, out float length, bool avoidClosedDoors = false)
        {
            length = 0f;
            if (!TryCalculatePath(destination, allowPartial: false))
                return false;

            int count = _navPath.GetCornersNonAlloc(_probeCorners);
            if (avoidClosedDoors && _pathCrossesClosedDoor(_probeCorners, count))
                return false;

            for (int i = 1; i < count; i++)
                length += Vector3.Distance(_probeCorners[i - 1], _probeCorners[i]);
            return true;
        }

        public static bool TrySampleNavMesh(Vector3 position, out Vector3 onMesh)
        {
            if (NavMesh.SamplePosition(position, out NavMeshHit hit, SampleRadius, NavMesh.AllAreas)
                || NavMesh.SamplePosition(position, out hit, NavSampleFallbackRadius, NavMesh.AllAreas))
            {
                onMesh = hit.position;
                return true;
            }

            onMesh = position;
            return false;
        }

        /// <summary>
        /// NavMesh 경로의 코너를 따라 이동한다. 경로가 없거나 1초 동안 거의 못 움직였으면(닫힌 문·걸림)
        /// false 를 돌려주고 다음 틱에 경로를 다시 만든다.
        /// </summary>
        public bool MoveToward(Vector3 target, float speed, float deltaTime, Vector3 center, Vector3 extents, bool allowPartial)
        {
            if (deltaTime <= 0f)
                return true;

            target = ClampToHouseBounds(target, center, extents);
            _pathRebuildRemaining -= deltaTime;
            if (_pathRebuildRemaining <= 0f
                || (target - _pathDestination).sqrMagnitude >
                    _settings.ReachDistance * _settings.ReachDistance)
            {
                RebuildPath(target, allowPartial);
            }

            if (!_hasPath)
            {
                _controller.Move(Vector3.down * _settings.Gravity * deltaTime);
                ResetStuckCheck();
                return false;
            }

            Vector3 waypoint = _pathCornerIndex < _pathCornerCount
                ? _pathCorners[_pathCornerIndex]
                : target;
            Vector3 flat = waypoint - _transform.position;
            flat.y = 0f;
            float distance = flat.magnitude;

            while (_pathCornerIndex < _pathCornerCount - 1
                && HasArrived(waypoint))
            {
                _pathCornerIndex++;
                waypoint = _pathCorners[_pathCornerIndex];
                flat = waypoint - _transform.position;
                flat.y = 0f;
                distance = flat.magnitude;
            }

            Vector3 direction = distance > 1e-3f ? flat / distance : Vector3.zero;
            if (direction != Vector3.zero)
            {
                Quaternion look = Quaternion.LookRotation(direction, Vector3.up);
                _transform.rotation = Quaternion.RotateTowards(
                    _transform.rotation,
                    look,
                    TurnSpeedDegrees * deltaTime);
            }

            float step = Mathf.Min(speed, distance / deltaTime);
            Vector3 move = direction * step;
            move.y = -_settings.Gravity;
            _controller.Move(move * deltaTime);
            Vector3 clamped = ClampToHouseBounds(_transform.position, center, extents);
            if (clamped != _transform.position)
                _transform.position = clamped;

            return !IsStuck(target, deltaTime);
        }

        public void ResetStuckCheck()
        {
            _stuckCheckRemaining = StuckCheckInterval;
            _stuckCheckPosition = _transform.position;
        }

        public void EnsureNavMesh(Transform house, Vector3 center, Vector3 extents, bool restrictHeight)
        {
            RemoveNavMesh();
            if (house == null)
            {
                Debug.LogError("[GhostPrototype] 집 루트가 없어 경로를 만들 수 없습니다.", _context);
                return;
            }

            int includedLayers = ~0;
            if (GameLayers.Player >= 0)
                includedLayers &= ~(1 << GameLayers.Player);
            if (GameLayers.Furniture >= 0)
                includedLayers &= ~(1 << GameLayers.Furniture);
            if (GameLayers.GhostPrototype >= 0)
                includedLayers &= ~(1 << GameLayers.GhostPrototype);

            _navMeshData = BuildHouseNavMesh(
                house,
                includedLayers,
                _settings.NavAgentRadius,
                _controller.height,
                Mathf.Max(0.3f, _controller.stepOffset));
            if (_navMeshData == null)
            {
                Debug.LogError("[GhostPrototype] 집 루트에서 NavMesh 를 만들 충돌체를 찾지 못했습니다.", _context);
                return;
            }

            _navMeshInstance = NavMesh.AddNavMeshData(_navMeshData);
            CacheRoamTriangles(center, extents, restrictHeight);

            if (!NavMesh.SamplePosition(_transform.position, out _, NavSampleFallbackRadius, NavMesh.AllAreas))
                Debug.LogError("[GhostPrototype] 집 내부 NavMesh 생성 후에도 스폰 위치를 찾지 못했습니다.", _context);
        }

        /// <summary>
        /// 집 루트 자식의 물리 충돌체로 귀신 전용 NavMesh 를 굽는다. 에이전트 종류는 기본(0)을 쓰되
        /// 반경·높이·턱 높이만 귀신에 맞춘다 — 기본 Humanoid 반경 0.5 로는 1.0m 문틀이 막힌다.
        /// 트리거(은신처·세이프 존)와 문(열고 닫혀 움직임)은 장애물에서 뺀다. 닫힌 문은 이동 중에 연다.
        /// </summary>
        public static NavMeshData BuildHouseNavMesh(
            Transform root, int layerMask, float agentRadius, float agentHeight, float agentClimb)
        {
            var sources = new List<NavMeshBuildSource>();
            NavMeshBuilder.CollectSources(
                root,
                layerMask,
                NavMeshCollectGeometry.PhysicsColliders,
                0,
                new List<NavMeshBuildMarkup>(),
                sources);

            for (int i = sources.Count - 1; i >= 0; i--)
            {
                if (sources[i].component is Collider collider
                    && (collider.isTrigger || collider.GetComponentInParent<DoorInteractable>() != null))
                {
                    sources.RemoveAt(i);
                }
            }

            bool hasBounds = false;
            Bounds bounds = default;
            foreach (Collider collider in root.GetComponentsInChildren<Collider>())
            {
                if (collider.isTrigger || (layerMask & (1 << collider.gameObject.layer)) == 0)
                    continue;
                if (hasBounds)
                {
                    bounds.Encapsulate(collider.bounds);
                }
                else
                {
                    bounds = collider.bounds;
                    hasBounds = true;
                }
            }

            if (!hasBounds || sources.Count == 0)
                return null;

            bounds.Expand(1f);
            NavMeshBuildSettings settings = NavMesh.GetSettingsByID(0);
            settings.agentRadius = agentRadius;
            settings.agentHeight = agentHeight;
            settings.agentClimb = agentClimb;
            return NavMeshBuilder.BuildNavMeshData(
                settings, sources, bounds, Vector3.zero, Quaternion.identity);
        }

        /// <summary>집 내부 활동 경계는 평면(X/Z)만 제한한다. 높이는 계단·단차를 위해 보존한다.</summary>
        public static bool IsInsideHouseBounds(Vector3 position, Vector3 center, Vector3 extents, bool restrictHeight = false)
        {
            return position.x >= center.x - extents.x
                && position.x <= center.x + extents.x
                && position.z >= center.z - extents.z
                && position.z <= center.z + extents.z
                && (!restrictHeight || Mathf.Abs(position.y - center.y) <= extents.y);
        }

        /// <summary>집 내부 활동 경계로 평면 좌표만 보정하고 높이는 유지한다.</summary>
        public static Vector3 ClampToHouseBounds(Vector3 position, Vector3 center, Vector3 extents)
        {
            position.x = Mathf.Clamp(position.x, center.x - extents.x, center.x + extents.x);
            position.z = Mathf.Clamp(position.z, center.z - extents.z, center.z + extents.z);
            return position;
        }

        private bool IsStuck(Vector3 target, float deltaTime)
        {
            _stuckCheckRemaining -= deltaTime;
            if (_stuckCheckRemaining > 0f)
                return false;

            bool stuck = (_transform.position - _stuckCheckPosition).sqrMagnitude
                    < StuckMinProgress * StuckMinProgress
                && !HasArrived(target);
            ResetStuckCheck();
            if (stuck)
                _pathRebuildRemaining = 0f;
            return stuck;
        }

        /// <summary>집 X/Z 경계 안의 NavMesh 삼각형과 누적 면적을 캐시한다(배회 목적지 추첨용).</summary>
        private void CacheRoamTriangles(Vector3 center, Vector3 extents, bool restrictHeight)
        {
            NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();
            Vector3[] vertices = triangulation.vertices;
            int[] indices = triangulation.indices;
            var kept = new List<int>(indices.Length);
            var cumulative = new List<float>(indices.Length / 3);
            float total = 0f;

            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                Vector3 a = vertices[indices[i]];
                Vector3 b = vertices[indices[i + 1]];
                Vector3 c = vertices[indices[i + 2]];
                if (!IsInsideHouseBounds((a + b + c) / 3f, center, extents, restrictHeight))
                    continue;

                float area = Vector3.Cross(b - a, c - a).magnitude * 0.5f;
                if (area <= 1e-4f)
                    continue;

                total += area;
                kept.Add(indices[i]);
                kept.Add(indices[i + 1]);
                kept.Add(indices[i + 2]);
                cumulative.Add(total);
            }

            _navVertices = vertices;
            _navIndices = kept.ToArray();
            _navCumulativeArea = cumulative.ToArray();
            _navTotalArea = total;
        }

        private void RemoveNavMesh()
        {
            if (_navMeshInstance.valid)
                _navMeshInstance.Remove();
            _navMeshInstance = default;
            if (_navMeshData != null)
            {
                Object.Destroy(_navMeshData);
                _navMeshData = null;
            }

            _navVertices = null;
            _navIndices = null;
            _navCumulativeArea = null;
            _navTotalArea = 0f;
            _hasPath = false;
        }

        private bool TryCalculatePath(Vector3 target, bool allowPartial)
        {
            return TrySampleNavMesh(_transform.position, out Vector3 from)
                && TrySampleNavMesh(target, out Vector3 to)
                && NavMesh.CalculatePath(from, to, NavMesh.AllAreas, _navPath)
                && _navPath.status != NavMeshPathStatus.PathInvalid
                && (allowPartial || _navPath.status == NavMeshPathStatus.PathComplete);
        }

        private bool RebuildPath(Vector3 target, bool allowPartial)
        {
            _pathRebuildRemaining = _settings.RepathInterval;
            _pathDestination = target;
            _hasPath = false;

            if (!TryCalculatePath(target, allowPartial))
            {
                if (!_navigationWarningLogged)
                {
                    Debug.LogWarning(
                        $"[GhostPrototype] {target} 까지 NavMesh 경로가 없습니다. 다른 목적지·경로를 찾습니다.",
                        _context);
                    _navigationWarningLogged = true;
                }
                return false;
            }

            _pathCornerCount = _navPath.GetCornersNonAlloc(_pathCorners);
            _pathCornerIndex = _pathCornerCount > 1 ? 1 : 0;
            _hasPath = _pathCornerCount > 0;
            _navigationWarningLogged = false;
            return _hasPath;
        }

    }
}
