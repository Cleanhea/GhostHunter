using System.Collections.Generic;
using GhostHunter.Gameplay.Furniture;
using GhostHunter.Gameplay.Map;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace GhostHunter.Gameplay.FurnitureDriver
{
    /// <summary>
    /// 가구용 멀티 드라이버가 다루는 씬 배치 오브젝트 하나 — 분해 가능한 큰 가구 인스턴스이거나
    /// 분해로 나오는 작은 부품 인스턴스다. 둘 다 같은 방식(활성/비활성 토글 + 재배치)으로 다룬다:
    /// <c>GhostHunter.Gameplay.Map.RandomFurnitureItem</c>이 쓰는 "런타임 스폰 없이 씬 풀을
    /// 서버가 재배치" 패턴을 그대로 따른다 → ADR-0009, docs/architecture/furniture-multidriver.md.
    ///
    /// <para><see cref="PoolKey"/>는 역할에 따라 두 가지 의미를 가진다 — 큰 가구 인스턴스면
    /// <see cref="FurnitureDisassemblyRecipe.LargeFurnitureId"/>, 부품 인스턴스면 부품 ID
    /// (<see cref="FurniturePartRequirement.PartId"/>). 어느 쪽이든 씬에 미리 배치되어 있고,
    /// 서버가 활성화(<see cref="ServerActivate"/>)·비활성화(<see cref="ServerDeactivate"/>)만
    /// 한다 — 새로 스폰하지 않는다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject), typeof(NetworkTransform), typeof(Rigidbody))]
    public sealed class FurnitureDriverPoolItem : NetworkBehaviour
    {
        private static readonly List<FurnitureDriverPoolItem> Registry = new();

        [SerializeField] private string _poolKey = "";

        [Tooltip("이미 방에 배치되어 처음부터 보이고 사용 가능해야 하는 실제 가구면 켠다. " +
            "분해로 나중에 활성화될 부품/조립 대기용 사본은 꺼둔 채로 둔다.")]
        [SerializeField] private bool _startActive;

        private readonly NetworkVariable<bool> _active = new(false,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _durability = new(100,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private NetworkTransform _networkTransform;
        private Rigidbody _body;
        private FurnitureGrabTarget _grabTarget;
        private Renderer[] _renderers;
        private Collider[] _colliders;
        private bool[] _rendererEnabled;
        private bool[] _colliderEnabled;

        public string PoolKey => _poolKey;
        public bool IsActive => IsSpawned && _active.Value;
        public int Durability => _durability.Value;

        public override void OnNetworkSpawn()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
            _colliders = GetComponentsInChildren<Collider>(true);
            _rendererEnabled = new bool[_renderers.Length];
            _colliderEnabled = new bool[_colliders.Length];
            for (int i = 0; i < _renderers.Length; i++)
                _rendererEnabled[i] = _renderers[i].enabled;
            for (int i = 0; i < _colliders.Length; i++)
                _colliderEnabled[i] = _colliders[i].enabled;

            _networkTransform = GetComponent<NetworkTransform>();
            _body = GetComponent<Rigidbody>();
            _grabTarget = GetComponent<FurnitureGrabTarget>();

            _active.OnValueChanged += HandleActiveChanged;
            if (IsServer)
                _active.Value = _startActive;
            ApplyPresentation(_active.Value);
            Registry.Add(this);
        }

        public override void OnNetworkDespawn()
        {
            _active.OnValueChanged -= HandleActiveChanged;
            Registry.Remove(this);
        }

        /// <summary>
        /// 서버 전용 — 지정한 풀 키의 비활성(대기 중) 인스턴스를 하나 찾는다. 분해가 부품을 꺼내거나
        /// 조립이 완성된 큰 가구를 꺼낼 때 쓴다. 어떤 개체인지는 규칙 2에 따라 구분하지 않으므로
        /// 먼저 찾은 것을 그대로 쓴다.
        /// </summary>
        public static bool TryFindInactive(string poolKey, out FurnitureDriverPoolItem item)
        {
            foreach (FurnitureDriverPoolItem candidate in Registry)
            {
                if (candidate != null && candidate.PoolKey == poolKey && !candidate.IsActive)
                {
                    item = candidate;
                    return true;
                }
            }
            item = null;
            return false;
        }

        /// <summary>에디터 설치 도구가 풀 키(큰 가구 종류 또는 부품 ID)를 지정한다.</summary>
        public void Configure(string poolKey) => _poolKey = poolKey;

        /// <summary>서버에서 지정한 위치에 나타나 물리적으로 낙하한다(§6.2·§6.3).</summary>
        public bool ServerActivate(Vector3 dropPosition, Quaternion rotation, int durability)
        {
            if (!IsServer || !IsSpawned || _networkTransform == null || !_networkTransform.IsSpawned)
                return false;
            _networkTransform.Teleport(dropPosition, rotation, transform.localScale);
            // 보간 바디는 트랜스폼만 옮기면 다음 물리 스텝에 이전 자세(풀 보관 위치)로 되돌아간다.
            RoomPreset.TeleportBody(_body, dropPosition, rotation);
            _durability.Value = Mathf.Clamp(durability, 0, 100);
            _active.Value = true;
            ApplyPresentation(true);
            // 대기 중에는 kinematic이므로 활성화 후 속도를 초기화한다.
            _body.linearVelocity = Vector3.zero;
            _body.angularVelocity = Vector3.zero;
            return true;
        }

        /// <summary>서버에서 이 오브젝트를 숨기고 물리 시뮬레이션을 멈춘다.</summary>
        public void ServerDeactivate()
        {
            if (!IsServer || !IsSpawned)
                return;
            if (_grabTarget != null)
                _grabTarget.ServerResetForPool();
            _active.Value = false;
            ApplyPresentation(false);
        }

        private void HandleActiveChanged(bool previous, bool current) => ApplyPresentation(current);

        private void ApplyPresentation(bool active)
        {
            if (_renderers == null)
                return;
            for (int i = 0; i < _renderers.Length; i++)
                if (_renderers[i] != null)
                    _renderers[i].enabled = active && _rendererEnabled[i];
            for (int i = 0; i < _colliders.Length; i++)
                if (_colliders[i] != null)
                    _colliders[i].enabled = active && _colliderEnabled[i];
            if (_body != null)
            {
                _body.isKinematic = !active || !IsServer;
                _body.detectCollisions = active;
                _body.useGravity = active;
            }
        }
    }
}
