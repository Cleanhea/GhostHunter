using System;
using System.Collections.Generic;
using GhostHunter.Gameplay.Furniture;
using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Gameplay.FurnitureDriver
{
    /// <summary>
    /// 조립 영역 하나(기획서 §4.2·§4.2.1·§6.4). MD-2 확정(2026-09-12)에 따라 기존 임시
    /// 드릴 카 세이프 존 자리를 재사용한다. 트리거 진입/이탈로 후보를 추적하고(§3.5 하드 룰 —
    /// 매 프레임 전역 검색 금지), 서버가 주기적으로 점유 판정을 재계산해 실루엣 상태를 복제한다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class FurnitureAssemblyZone : NetworkBehaviour
    {
        private static readonly List<FurnitureAssemblyZone> Registry = new();

        /// <summary>씬에 스폰된 조립 영역 전체 — 플레이어 컨트롤러가 조준 판정에 쓴다(Find 금지 대안).</summary>
        public static IReadOnlyList<FurnitureAssemblyZone> All => Registry;

        private struct SilhouetteState : INetworkSerializable, IEquatable<SilhouetteState>
        {
            internal FurnitureAssemblyState Silhouette;
            internal int RecipeIndex;

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref Silhouette);
                serializer.SerializeValue(ref RecipeIndex);
            }

            public bool Equals(SilhouetteState other) =>
                Silhouette == other.Silhouette && RecipeIndex == other.RecipeIndex;
        }

        [SerializeField] private FurnitureDriverCatalog _catalog;
        [SerializeField] private Collider _trigger;
        [SerializeField, Min(0.01f)] private float _settledSpeed = 0.05f;
        [SerializeField, Min(0f)] private float _recomputeInterval = 0.2f;

        private readonly NetworkVariable<SilhouetteState> _state = new(default,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly HashSet<FurnitureDriverPoolItem> _candidates = new();
        private float _recomputeTimer;

        public FurnitureAssemblyState Silhouette => _state.Value.Silhouette;

        /// <summary>
        /// 조준 기준점 — 트리거 상자의 <b>월드 중심</b>.
        ///
        /// <para>오브젝트 원점은 부품이 들어와야 하는 <b>바닥</b>이다. 그 점을 조준 기준으로 쓰면
        /// 발치를 내려다봐야 하고, 부품을 내려놓느라 가까이 설수록 각도가 원뿔을 벗어나 우클릭이
        /// 아무 반응도 하지 않는다(2026-09-17). 상자 중심은 가슴 높이라 서 있는 어느 거리에서나
        /// 자연스럽게 조준된다.</para>
        /// </summary>
        public Vector3 AimPoint => _trigger != null ? _trigger.bounds.center : transform.position;

        public FurnitureDisassemblyRecipe MatchedRecipe
        {
            get
            {
                int index = _state.Value.RecipeIndex;
                return _catalog != null && index >= 0 && index < _catalog.Recipes.Count
                    ? _catalog.Recipes[index]
                    : null;
            }
        }

        public event Action<FurnitureAssemblyState, FurnitureAssemblyState> SilhouetteChanged;

        private void Awake()
        {
            if (_trigger != null)
                _trigger.isTrigger = true;
        }

        public override void OnNetworkSpawn()
        {
            _state.OnValueChanged += HandleStateChanged;
            if (IsServer)
                RecomputeState();
            Registry.Add(this);
        }

        public override void OnNetworkDespawn()
        {
            _state.OnValueChanged -= HandleStateChanged;
            _candidates.Clear();
            Registry.Remove(this);
        }

        /// <summary>에디터 설치 도구가 카탈로그·트리거 경계를 연결한다.</summary>
        public void Configure(FurnitureDriverCatalog catalog, Collider trigger)
        {
            _catalog = catalog;
            _trigger = trigger;
        }

        /// <summary>파손으로 콜라이더가 꺼질 때 이탈 콜백 없이 남을 수 있는 점유를 즉시 제거한다.</summary>
        public static void ServerRemoveBrokenItem(FurnitureDriverPoolItem item)
        {
            foreach (FurnitureAssemblyZone zone in Registry)
            {
                if (zone != null && zone.IsServer && zone.IsSpawned && zone._candidates.Remove(item))
                    zone.RecomputeState();
            }
        }

        private void FixedUpdate()
        {
            if (!IsServer)
                return;
            _recomputeTimer -= Time.fixedDeltaTime;
            if (_recomputeTimer > 0f)
                return;
            _recomputeTimer = _recomputeInterval;
            RecomputeState();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!IsServer)
                return;
            if (other.GetComponentInParent<FurnitureDriverPoolItem>() is { } item && _candidates.Add(item))
                RecomputeState();
        }

        private void OnTriggerExit(Collider other)
        {
            if (!IsServer)
                return;
            if (other.GetComponentInParent<FurnitureDriverPoolItem>() is { } item && _candidates.Remove(item))
                RecomputeState();
        }

        /// <summary>
        /// 서버 전용 — 지금 이 순간 영역 안에서 "바닥에 놓인" 부품 개수를 다시 세고 실루엣을 갱신한다.
        /// 조립 실행 직전에도 한 번 더 불러 클라이언트가 보던 상태와의 시차(레이턴시)를 없앤다.
        /// </summary>
        private FurnitureAssemblyEvaluation RecomputeState()
        {
            var counts = new Dictionary<string, int>();
            _candidates.RemoveWhere(item => item == null);
            foreach (FurnitureDriverPoolItem item in _candidates)
            {
                if (!item.IsActive || !IsGrounded(item))
                    continue;
                counts.TryGetValue(item.PoolKey, out int current);
                counts[item.PoolKey] = current + 1;
            }

            IReadOnlyList<FurnitureDisassemblyRecipe> recipes = _catalog != null
                ? _catalog.Recipes
                : Array.Empty<FurnitureDisassemblyRecipe>();
            FurnitureAssemblyEvaluation evaluation = FurnitureAssemblyRules.Evaluate(counts, recipes);
            int recipeIndex = -1;
            if (evaluation.MatchedRecipe != null)
            {
                for (int i = 0; i < recipes.Count; i++)
                {
                    if (recipes[i] == evaluation.MatchedRecipe)
                    {
                        recipeIndex = i;
                        break;
                    }
                }
            }
            _state.Value = new SilhouetteState { Silhouette = evaluation.State, RecipeIndex = recipeIndex };
            return evaluation;
        }

        private bool IsGrounded(FurnitureDriverPoolItem item)
        {
            if (item.TryGetComponent(out FurnitureGrabTarget grab) && grab.State != FurnitureState.Idle)
                return false;
            return !item.TryGetComponent(out Rigidbody body)
                || body.linearVelocity.sqrMagnitude <= _settledSpeed * _settledSpeed;
        }

        /// <summary>
        /// 서버 전용 — 지금 상태를 다시 확인하고(레이턴시로 어긋난 클라이언트 판단을 신뢰하지 않는다)
        /// 초록 실루엣이면 부품을 소비하고 큰 가구를 활성화한다. 실패하면 아무것도 바꾸지 않는다.
        /// </summary>
        public bool ServerTryAssemble(float dropHeight, out FurnitureDisassemblyRecipe recipe,
            out int completedDurability)
        {
            recipe = null;
            completedDurability = 0;
            if (!IsServer)
                return false;

            FurnitureAssemblyEvaluation evaluation = RecomputeState();
            if (evaluation.State != FurnitureAssemblyState.Ready || evaluation.MatchedRecipe == null)
                return false;
            recipe = evaluation.MatchedRecipe;

            var toConsume = new List<FurnitureDriverPoolItem>();
            var durabilities = new List<int>();
            foreach (FurniturePartRequirement requirement in recipe.Parts)
            {
                int needed = requirement.Count;
                foreach (FurnitureDriverPoolItem item in _candidates)
                {
                    if (needed <= 0)
                        break;
                    if (item == null || item.PoolKey != requirement.PartId || !item.IsActive || !IsGrounded(item))
                        continue;
                    toConsume.Add(item);
                    durabilities.Add(item.Durability);
                    needed--;
                }
                if (needed > 0)
                {
                    // 재계산과 소비 사이에 상태가 바뀌었다(동시 시도 등, MD-9) — 아무것도 하지 않는다.
                    return false;
                }
            }

            if (!FurnitureDriverPoolItem.TryFindInactive(recipe.LargeFurnitureId, out FurnitureDriverPoolItem target))
                return false;

            foreach (FurnitureDriverPoolItem part in toConsume)
                part.ServerDeactivate();
            _candidates.ExceptWith(toConsume);

            completedDurability = FurnitureDurability.AverageOnAssemble(durabilities);
            Vector3 dropPosition = transform.position + Vector3.up * dropHeight;
            target.ServerActivate(dropPosition, transform.rotation, completedDurability);

            RecomputeState();
            return true;
        }

        private void HandleStateChanged(SilhouetteState previous, SilhouetteState current) =>
            SilhouetteChanged?.Invoke(previous.Silhouette, current.Silhouette);
    }
}
