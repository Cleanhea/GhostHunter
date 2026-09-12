using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using Random = System.Random;

namespace GhostHunter.Gameplay.Map
{
    /// <summary>후보·수량·충돌을 검증한 전체 계획을 세션 시작 시 서버에서 한 번 배치한다.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class FurnitureSpawnController : NetworkBehaviour
    {
        private const float ContactTolerance = 0.005f;

        [SerializeField] private FurnitureSpawnSettings _settings;
        [SerializeField] private RandomFurnitureItem[] _items = Array.Empty<RandomFurnitureItem>();
        [SerializeField] private FurnitureSpawnPoint[] _points = Array.Empty<FurnitureSpawnPoint>();
        [SerializeField] private FurnitureResetter _resetter;

        private readonly NetworkVariable<bool> _isReady = new(false,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _generationSeed = new(0,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private CancellationTokenSource _spawnCancellation;

        public FurnitureSpawnSettings Settings => _settings;
        public RandomFurnitureItem[] Items => _items;
        public FurnitureSpawnPoint[] Points => _points;
        public bool IsReady => _isReady.Value;
        public int GenerationSeed => _generationSeed.Value;

        public override void OnNetworkSpawn()
        {
            if (!IsServer)
                return;
            _isReady.Value = false;
            _spawnCancellation = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            GenerateAfterSceneSpawnAsync(_spawnCancellation.Token).Forget();
        }

        public override void OnNetworkDespawn()
        {
            if (_spawnCancellation != null)
            {
                _spawnCancellation.Cancel();
                _spawnCancellation.Dispose();
                _spawnCancellation = null;
            }
        }

        /// <summary>씬 설치 도구가 명시적인 설정·풀·지점 참조를 연결한다.</summary>
        public void Configure(FurnitureSpawnSettings settings, RandomFurnitureItem[] items,
            FurnitureSpawnPoint[] points, FurnitureResetter resetter)
        {
            _settings = settings;
            _items = items;
            _points = points;
            _resetter = resetter;
        }

        /// <summary>씬이나 SO를 변경하지 않고 지정 시드의 전체 배치 가능 여부를 검증한다.</summary>
        public bool TryBuildPlan(int seed, out FurniturePlacementPlanner.Candidate[] plan,
            out FurniturePlacementPlanner.Request[] requests, out string error)
        {
            plan = Array.Empty<FurniturePlacementPlanner.Candidate>();
            requests = Array.Empty<FurniturePlacementPlanner.Request>();
            if (!ValidateReferences(out error))
                return false;

            var random = new Random(seed);
            FurnitureSpawnSettings.PoolRule[] rules = _settings.Pools;
            var selectedTargets = new List<int>();
            for (int i = 0; i < rules.Length; i++)
                if (rules[i].IsWorkTarget)
                    selectedTargets.Add(i);
            for (int i = selectedTargets.Count - 1; i > 0; i--)
            {
                int other = random.Next(i + 1);
                (selectedTargets[i], selectedTargets[other]) = (selectedTargets[other], selectedTargets[i]);
            }
            if (_settings.MinimumTargetTypes < 0
                || _settings.MaximumTargetTypes < _settings.MinimumTargetTypes
                || _settings.MaximumTargetTypes > selectedTargets.Count)
            {
                error = "선정할 Target Type 범위가 설정된 대상 풀 수를 벗어납니다.";
                return false;
            }
            int typeCount = random.Next(_settings.MinimumTargetTypes, _settings.MaximumTargetTypes + 1);
            var requestList = new List<FurniturePlacementPlanner.Request>();
            bool requestOverflow = false;
            var rooms = new SortedSet<int>();
            foreach (FurnitureSpawnPoint point in _points)
                rooms.Add(point.RoomId);

            // 일반 오브젝트 요구를 먼저 만든 뒤 작업 대상 요구를 추가한다.
            for (int phase = 0; phase < 2; phase++)
            {
                for (int ruleIndex = 0; ruleIndex < rules.Length; ruleIndex++)
                {
                    FurnitureSpawnSettings.PoolRule rule = rules[ruleIndex];
                    if (rule.IsWorkTarget != (phase == 1)
                        || (rule.IsWorkTarget && selectedTargets.IndexOf(ruleIndex) >= typeCount))
                        continue;
                    if (rule.PerRoom)
                    {
                        foreach (int room in rooms)
                        {
                            bool hasType = Array.Exists(_points,
                                point => point.RoomId == room && point.SpawnType == rule.SpawnType);
                            if (hasType)
                                AddRequests(ruleIndex, room);
                        }
                    }
                    else
                        AddRequests(ruleIndex, -1);
                }
            }
            if (requestOverflow)
                return Fail("요청 수량보다 씬 가구 풀이 적습니다. 보관 풀을 늘리거나 수량 설정을 확인하세요.", out error);
            requests = requestList.ToArray();
            var candidates = new List<FurniturePlacementPlanner.Candidate>();
            var poolColliders = new HashSet<Collider>();
            foreach (RandomFurnitureItem item in _items)
                foreach (Collider collider in item.GetComponentsInChildren<Collider>(true))
                    poolColliders.Add(collider);

            Physics.SyncTransforms();
            for (int itemIndex = 0; itemIndex < _items.Length; itemIndex++)
            {
                RandomFurnitureItem item = _items[itemIndex];
                int poolIndex = Array.FindIndex(rules, rule => rule.PoolId == item.PoolId);
                for (int pointIndex = 0; pointIndex < _points.Length; pointIndex++)
                {
                    FurnitureSpawnPoint point = _points[pointIndex];
                    if (!point.Accepts(item.PoolId, rules[poolIndex].SpawnType, item.LocalBounds.size))
                        continue;
                    item.GetPlacement(point, out _, out Bounds bounds);
                    if (HasObstacle(bounds, poolColliders, _settings.ObstacleMask,
                        point.transform.position.y))
                        continue;
                    Vector3 min = bounds.min;
                    Vector3 max = bounds.max;
                    candidates.Add(new FurniturePlacementPlanner.Candidate(poolIndex, itemIndex, pointIndex,
                        point.RoomId, new FurniturePlacementPlanner.Box(min.x, min.y, min.z, max.x, max.y, max.z)));
                }
            }
            return FurniturePlacementPlanner.TryPlan(_items.Length, _points.Length, requests,
                candidates.ToArray(), _settings.MaximumTargetsPerRoom, _settings.SearchBudget, random,
                out plan, out error);

            void AddRequests(int pool, int room)
            {
                FurnitureSpawnSettings.PoolRule rule = rules[pool];
                int count = random.Next(rule.MinimumCount, rule.MaximumCount + 1);
                if (count > _items.Length - requestList.Count)
                {
                    requestOverflow = true;
                    return;
                }
                for (int i = 0; i < count; i++)
                    requestList.Add(new FurniturePlacementPlanner.Request(pool, room, rule.IsWorkTarget));
            }
        }

        /// <summary>물리 지오메트리에 겹치는 후보인지 검사한다. 지지면 접촉은 허용한다.</summary>
        public static bool HasObstacle(Bounds bounds, HashSet<Collider> ignored, int mask,
            float supportHeight = float.NegativeInfinity)
        {
            Vector3 extents = Vector3.Max(Vector3.one * ContactTolerance,
                bounds.extents - Vector3.one * ContactTolerance);
            foreach (Collider collider in Physics.OverlapBox(bounds.center, extents,
                Quaternion.identity, mask, QueryTriggerInteraction.Ignore))
            {
                if (ignored != null && ignored.Contains(collider))
                    continue;

                // 후보점은 바닥 윗면을 지지 높이로 사용한다. PhysX의 접촉 여유 때문에
                // 바닥이 축소한 검사 상자에 다시 잡힐 수 있으므로 지지면 아래의 콜라이더는 제외한다.
                if (!float.IsNegativeInfinity(supportHeight)
                    && collider.bounds.max.y <= supportHeight + ContactTolerance)
                    continue;

                return true;
            }
            return false;
        }

        private async UniTaskVoid GenerateAfterSceneSpawnAsync(CancellationToken cancellationToken)
        {
            try
            {
                // NGO의 씬 스폰과 기존 RoomSlotAssigner 이동이 끝난 다음 검사한다.
                await UniTask.NextFrame(cancellationToken);
                await UniTask.NextFrame(cancellationToken);
                if (!IsSpawned || !IsServer || cancellationToken.IsCancellationRequested)
                    return;
                if (_settings == null)
                {
                    Debug.LogError("[FurnitureSpawnController] 설정이 연결되지 않았습니다.", this);
                    return;
                }
                int seed = _settings.UseFixedSeed ? _settings.Seed : Guid.NewGuid().GetHashCode();
                _generationSeed.Value = seed;
                if (!TryBuildPlan(seed, out FurniturePlacementPlanner.Candidate[] plan,
                    out FurniturePlacementPlanner.Request[] requests, out string error))
                {
                    Debug.LogError($"[FurnitureSpawnController] 배치 실패 (seed {seed}): {error}", this);
                    return;
                }
                foreach (RandomFurnitureItem item in _items)
                {
                    NetworkTransform networkTransform = item.GetComponent<NetworkTransform>();
                    if (!item.IsSpawned || !item.IsServer || networkTransform == null || !networkTransform.IsSpawned)
                    {
                        Debug.LogError("[FurnitureSpawnController] 씬 가구의 서버 스폰이 완료되지 않았습니다.", this);
                        return;
                    }
                }
                for (int i = 0; i < plan.Length; i++)
                {
                    RandomFurnitureItem item = _items[plan[i].Item];
                    item.GetPlacement(_points[plan[i].Point], out Pose pose, out _);
                    if (!item.ServerPlace(pose, requests[i].IsTarget))
                        throw new InvalidOperationException($"{item.name} 배치에 실패했습니다.");
                }
                if (_resetter != null)
                    _resetter.CapturePoses();
                _isReady.Value = true;
                Debug.Log($"[FurnitureSpawnController] 배치 완료: {plan.Length}개, seed {seed}.", this);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                if (IsSpawned && IsServer && _items != null)
                    foreach (RandomFurnitureItem item in _items)
                        if (item != null)
                            item.ServerPark();
                Debug.LogException(exception, this);
            }
        }

        private bool ValidateReferences(out string error)
        {
            error = null;
            if (_settings == null || _items == null || _points == null
                || _settings.Pools == null || _settings.Pools.Length == 0)
                return Fail("설정·가구 풀·후보 지점 참조가 필요합니다.", out error);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (FurnitureSpawnSettings.PoolRule rule in _settings.Pools)
            {
                if (rule == null || string.IsNullOrWhiteSpace(rule.PoolId) || !ids.Add(rule.PoolId)
                    || rule.MinimumCount < 0 || rule.MaximumCount < rule.MinimumCount
                    || rule.MaximumCount > _items.Length
                    || !Enum.IsDefined(typeof(FurnitureSpawnType), rule.SpawnType))
                    return Fail("풀 ID 중복 또는 잘못된 수량 범위입니다.", out error);
            }
            var uniqueItems = new HashSet<RandomFurnitureItem>();
            for (int i = 0; i < _items.Length; i++)
            {
                RandomFurnitureItem item = _items[i];
                if (item == null)
                    return Fail($"가구 풀 {i}번 참조가 비었습니다.", out error);
                if (!uniqueItems.Add(item))
                    return Fail($"{item.name}이 가구 풀에 중복 등록됐습니다.", out error);
                if (!ids.Contains(item.PoolId))
                    return Fail($"{item.name}의 Pool ID '{item.PoolId}'가 설정에 없습니다.", out error);
                if (!item.gameObject.activeInHierarchy)
                    return Fail($"{item.name} 또는 상위 오브젝트가 비활성 상태입니다.", out error);
                if (item.gameObject.scene != gameObject.scene)
                    return Fail($"{item.name}이 Controller와 다른 씬에 있습니다.", out error);
                if (!IsValidBounds(item.LocalBounds))
                    return Fail($"{item.name}의 배치 Bounds가 유효하지 않습니다.", out error);
                if (!IsValidScale(item.transform.lossyScale))
                    return Fail($"{item.name}의 Scale에 0 이하 또는 유효하지 않은 값이 있습니다.", out error);
            }
            var uniquePoints = new HashSet<FurnitureSpawnPoint>();
            foreach (FurnitureSpawnPoint point in _points)
                if (point == null || !uniquePoints.Add(point) || point.RoomId < 0
                    || !point.gameObject.activeInHierarchy || point.gameObject.scene != gameObject.scene
                    || !Enum.IsDefined(typeof(FurnitureSpawnType), point.SpawnType)
                    || Vector3.Dot(point.transform.up, Vector3.up) < 0.999f
                    || !IsValidBounds(new Bounds(point.transform.position, point.MaximumSize))
                    || point.transform.lossyScale != Vector3.one)
                    return Fail("후보 지점이 비었거나 중복되었거나 지지면이 기울었습니다.", out error);
            return true;
        }

        private static bool IsValidBounds(Bounds bounds)
        {
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            return new FurniturePlacementPlanner.Box(min.x, min.y, min.z, max.x, max.y, max.z).IsValid;
        }

        private static bool IsValidScale(Vector3 scale)
        {
            return scale.x > 0f && scale.y > 0f && scale.z > 0f
                && !float.IsNaN(scale.x) && !float.IsInfinity(scale.x)
                && !float.IsNaN(scale.y) && !float.IsInfinity(scale.y)
                && !float.IsNaN(scale.z) && !float.IsInfinity(scale.z);
        }

        private static bool Fail(string message, out string error)
        {
            error = message;
            return false;
        }
    }
}
