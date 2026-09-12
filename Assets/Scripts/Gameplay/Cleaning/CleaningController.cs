using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using GhostHunter.Gameplay.Map;
using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Gameplay.Cleaning
{
    /// <summary>씬 얼룩 풀을 빈 바닥 후보에 랜덤 배치하고 초기화한다.</summary>
    [DisallowMultipleComponent]
    public sealed class CleaningController : NetworkBehaviour, ICleaningService
    {
        [SerializeField] private CleaningSettings _settings;
        [SerializeField] private CleaningStain[] _stains = Array.Empty<CleaningStain>();
        [SerializeField] private Transform[] _points = Array.Empty<Transform>();
        [SerializeField] private FurnitureSpawnController _furniture;
        private CancellationTokenSource _spawnCancellation;
        private int[] _order;
        private uint _revision;
        private readonly RaycastHit[] _hits = new RaycastHit[64];
        private string _status = "Host 시작 후 얼룩을 배치합니다.";

        public bool CanReset => IsSpawned && IsServer && AllStainsSpawned()
            && (_furniture == null || _furniture.IsReady);
        public string Status => IsSpawned && !IsServer ? "초기화는 Host에서 할 수 있습니다." : _status;
        public int DirtyCount
        {
            get
            {
                int count = 0;
                foreach (CleaningStain stain in _stains)
                    if (stain != null && stain.IsDirty)
                        count++;
                return count;
            }
        }

        private void Awake() => _order = new int[_points.Length];

        public override void OnNetworkSpawn()
        {
            if (!IsServer)
                return;
            _status = "가구·얼룩 풀 준비 대기 중";
            _spawnCancellation = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            InitializeAsync(_spawnCancellation.Token).Forget();
        }

        public override void OnNetworkDespawn()
        {
            if (_spawnCancellation == null)
                return;
            _spawnCancellation.Cancel();
            _spawnCancellation.Dispose();
            _spawnCancellation = null;
        }

        /// <summary>가림을 포함한 첫 충돌이 현재 씬의 활성 얼룩인지 확인한다.</summary>
        public bool TryRaycast(Vector3 origin, Vector3 direction, float distance, out CleaningStain stain,
            Transform ignoredRoot = null)
        {
            stain = null;
            int count = Physics.RaycastNonAlloc(origin, direction, _hits, distance,
                _settings.SurfaceMask, QueryTriggerInteraction.Collide);
            // 버퍼가 가득 차면 가장 가까운 충돌을 보장할 수 없으므로 청소하지 않는다.
            if (count == _hits.Length)
                return false;
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _hits[i];
                if (hit.distance >= nearest || IsIgnored(hit.collider, ignoredRoot))
                    continue;
                CleaningStain candidate = FindStain(hit.collider);
                // 트리거 볼륨은 벽이 아니다. 얼룩 트리거만 조준 대상으로 취급한다.
                if (hit.collider.isTrigger && candidate == null)
                    continue;
                nearest = hit.distance;
                stain = candidate;
            }
            return stain != null;
        }

        /// <summary>요청자 자신의 충돌체와 트리거를 제외하고 고체 가림을 검사한다.</summary>
        public bool IsObstructed(Vector3 origin, Vector3 destination, Transform ignoredRoot)
        {
            Vector3 delta = destination - origin;
            int count = Physics.RaycastNonAlloc(origin, delta.normalized, _hits, delta.magnitude,
                _settings.SurfaceMask, QueryTriggerInteraction.Ignore);
            if (count == _hits.Length)
                return true;
            for (int i = 0; i < count; i++)
                if (!IsIgnored(_hits[i].collider, ignoredRoot))
                    return true;
            return false;
        }

        private CleaningStain FindStain(Collider collider)
        {
            foreach (CleaningStain candidate in _stains)
                if (candidate != null && candidate.IsDirty && candidate.HitCollider == collider)
                    return candidate;
            return null;
        }

        private static bool IsIgnored(Collider collider, Transform root) => root != null
            && collider.transform.IsChildOf(root);

        /// <summary>Host에서 모든 얼룩을 복원하고 빈 후보에 다시 배치한다.</summary>
        public void ResetStains()
        {
            if (!CanReset || _settings == null)
                return;
            _revision++;
            foreach (CleaningStain stain in _stains)
                stain.ServerReset(Vector3.zero, 0f, _revision, false);

            var random = new System.Random();
            for (int i = 0; i < _order.Length; i++)
                _order[i] = i;
            for (int i = _order.Length - 1; i > 0; i--)
            {
                int other = random.Next(i + 1);
                (_order[i], _order[other]) = (_order[other], _order[i]);
            }
            Physics.SyncTransforms();
            int placed = 0;
            int requested = Mathf.Min(_settings.StainCount, _stains.Length);
            foreach (int index in _order)
            {
                if (placed >= requested)
                    break;
                if (_points[index] == null || !TryGetSurface(_points[index].position, out Vector3 position))
                    continue;
                _stains[placed++].ServerReset(position, (float)random.NextDouble() * 360f, _revision, true);
            }
            _status = $"얼룩 {placed}/{requested}개 배치";
            if (placed < requested)
                _status += " — 빈 바닥 후보 부족";
        }

        private async UniTaskVoid InitializeAsync(CancellationToken cancellationToken)
        {
            try
            {
                await UniTask.WaitUntil(() => CanReset, cancellationToken: cancellationToken);
                ResetStains();
            }
            catch (OperationCanceledException) { }
        }

        private bool AllStainsSpawned()
        {
            if (_stains.Length == 0)
                return false;
            foreach (CleaningStain stain in _stains)
                if (stain == null || !stain.IsSpawned)
                    return false;
            return true;
        }

        private bool TryGetSurface(Vector3 candidate, out Vector3 position)
        {
            position = default;
            if (!Physics.Raycast(candidate + Vector3.up * 0.2f, Vector3.down,
                out RaycastHit hit, 0.4f, _settings.SurfaceMask, QueryTriggerInteraction.Ignore)
                || hit.normal.y < 0.99f || Mathf.Abs(hit.point.y - candidate.y) > 0.08f)
                return false;
            float radius = _settings.PlacementRadius;
            if (Physics.CheckBox(hit.point + Vector3.up * 0.12f,
                new Vector3(radius, 0.09f, radius), Quaternion.identity,
                _settings.SurfaceMask, QueryTriggerInteraction.Ignore))
                return false;
            position = hit.point + Vector3.up * _settings.SurfaceOffset;
            return true;
        }
    }
}
