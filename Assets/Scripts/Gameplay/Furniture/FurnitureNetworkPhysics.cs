using System.Collections.Generic;
using GhostHunter.Core;
using GhostHunter.Gameplay.FurnitureDriver;
using GhostHunter.Gameplay.Ghost;
using GhostHunter.Gameplay.Map;
using GhostHunter.Gameplay.Player;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace GhostHunter.Gameplay.Furniture
{
    /// <summary>가구의 서버 물리, 충돌 내구도와 파손 상태를 관리한다.</summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class FurnitureNetworkPhysics : NetworkBehaviour
    {
        public const int FullDurability = 100;
        private static readonly List<FurnitureNetworkPhysics> Registry = new();

        [SerializeField] private FurnitureDefinition _definition;

        private readonly NetworkVariable<int> _durability = new(FullDurability,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private Rigidbody _rigidbody;
        private FurnitureGrabTarget _grabTarget;
        private FurnitureDriverPoolItem _poolItem;
        private RandomFurnitureItem _randomItem;
        private NetworkTransform _networkTransform;
        private Renderer[] _renderers;
        private Collider[] _colliders;
        private bool[] _colliderEnabled;
        private double _protectedUntil;
        private double _windowEndsAt;
        private int _windowDamage;
        private Pose _resetPose;

        public FurnitureDefinition Definition => _definition;
        public Rigidbody Rigidbody => _rigidbody;
        public int Durability => _durability.Value;

        /// <summary>
        /// 내구도 0에서 가구를 없앨지 여부(FD-10). <see cref="FurnitureDefinition"/>가 정하며 기본값은
        /// **끔**이다(2026-09-16 사용자 요청) — 끈 동안에는 0이 되어도 사라지지 않고 잡기·던지기·조립에
        /// 계속 쓸 수 있다. 사라지는 처리는 이 한 플래그로만 켜고 끈다.
        /// </summary>
        public bool DestroysAtZeroDurability => _definition != null && _definition.DestroyAtZeroDurability;

        /// <summary>파손(사라짐) 상태. 없애지 않는 설정에서는 내구도가 0이어도 파손으로 치지 않는다.</summary>
        public bool IsBroken => DestroysAtZeroDurability && _durability.Value == 0;
        public bool IsAvailable => IsSpawned && !IsBroken
            && (_poolItem == null || _poolItem.IsActive)
            && (_randomItem == null || _randomItem.IsPlaced);

        /// <summary>스폰된 모든 가구. 개발 HUD의 내구도 라벨처럼 읽기 전용으로 순회한다.</summary>
        public static IReadOnlyList<FurnitureNetworkPhysics> All => Registry;

        /// <summary>켜진 실제 콜라이더 경계의 윗면 중앙. 표시용이며 모든 접속자에서 호출할 수 있다.</summary>
        public bool TryGetTopCenter(out Vector3 point)
        {
            bool found = false;
            Bounds bounds = default;
            foreach (Collider collider in _colliders)
            {
                if (collider == null || !collider.enabled || collider.isTrigger
                    || !collider.gameObject.activeInHierarchy)
                    continue;
                if (found)
                    bounds.Encapsulate(collider.bounds);
                else
                    bounds = collider.bounds;
                found = true;
            }
            point = found ? new Vector3(bounds.center.x, bounds.max.y, bounds.center.z) : default;
            return found;
        }

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            _grabTarget = GetComponent<FurnitureGrabTarget>();
            _poolItem = GetComponent<FurnitureDriverPoolItem>();
            _randomItem = GetComponent<RandomFurnitureItem>();
            _networkTransform = GetComponent<NetworkTransform>();
            _renderers = GetComponentsInChildren<Renderer>(true);
            _colliders = GetComponentsInChildren<Collider>(true);
            _colliderEnabled = new bool[_colliders.Length];
            // 풀 컴포넌트의 Awake가 콜라이더를 끄기 전에 원래 배선을 기억한다.
            for (int i = 0; i < _colliders.Length; i++)
                _colliderEnabled[i] = _colliders[i].enabled;
            if (_definition != null)
                _rigidbody.mass = _definition.Mass;
        }

        public override void OnNetworkSpawn()
        {
            _durability.OnValueChanged += HandleDurabilityChanged;
            Registry.Add(this);
            if (IsServer)
            {
                _durability.Value = FullDurability;
                ServerProtectPlacement();
            }
        }

        protected override void OnNetworkPostSpawn() => RefreshPresentation();

        public override void OnNetworkDespawn()
        {
            _durability.OnValueChanged -= HandleDurabilityChanged;
            Registry.Remove(this);
            _windowDamage = 0;
            _windowEndsAt = 0;
            _protectedUntil = 0;
            foreach (Renderer renderer in _renderers)
                if (renderer != null)
                    renderer.forceRenderingOff = false;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!IsServer || !IsSpawned || IsExcluded(collision.collider))
                return;

            float speed = 0f;
            for (int i = 0; i < collision.contactCount; i++)
                speed = Mathf.Max(speed, FurnitureCollisionDamage.NormalSpeed(
                    collision.relativeVelocity, collision.GetContact(i).normal));

            FurnitureNetworkPhysics other = collision.rigidbody != null
                ? collision.rigidbody.GetComponent<FurnitureNetworkPhysics>() : null;
            // 한쪽이 이 콜백에서 파손되어도 상대에게 같은 충돌 속도를 전달한다.
            // 상대 콜백의 중복은 각 가구의 판정 창에서 제거한다.
            ServerApplyCollisionSpeed(speed);
            if (other != null && other != this)
                other.ServerApplyCollisionSpeed(speed);
        }

        /// <summary>서버에서 충돌 속도를 내구도에 적용한다. 창 안에서는 최대 피해의 차액만 반영한다.</summary>
        public void ServerApplyCollisionSpeed(float speed)
        {
            if (!IsServer || !IsAvailable || _definition == null
                || Time.timeAsDouble < _protectedUntil
                || (!_definition.DamageWhileHeld && _grabTarget != null
                    && _grabTarget.State == FurnitureState.Held))
                return;

            int damage = FurnitureCollisionDamage.Calculate(speed, _definition.DamageMinimumSpeed,
                _definition.DamagePerSpeed, _definition.DamageWeightMultiplier,
                _definition.MaximumCollisionDamage);
            if (damage == 0)
                return;
            if (Time.timeAsDouble >= _windowEndsAt)
            {
                _windowDamage = 0;
                _windowEndsAt = Time.timeAsDouble + _definition.CollisionWindowSeconds;
            }
            int additionalDamage = Mathf.Max(0, damage - _windowDamage);
            _windowDamage = Mathf.Max(_windowDamage, damage);
            if (additionalDamage > 0)
                SetDurability(Mathf.Max(0, Durability - additionalDamage));
        }

        /// <summary>
        /// 분해·조립에서 상속·평균 값을 쓴다. 파손된 인스턴스의 재사용은 거절한다.
        /// 없애지 않는 설정에서는 **0도 유효한 값**이다 — 0인 부품만 모아 조립하면 완성품도 0이 된다(§5 평균).
        /// </summary>
        public bool ServerSetDurability(int durability)
        {
            int minimum = DestroysAtZeroDurability ? 1 : 0;
            if (!IsServer || !IsSpawned || IsBroken || durability < minimum)
                return false;
            SetDurability(Mathf.Clamp(durability, minimum, FullDurability));
            return true;
        }

        /// <summary>서버 배치 직후 보호 시간을 새로 시작하고 개발용 복귀 위치를 기록한다.</summary>
        public void ServerProtectPlacement()
        {
            if (!IsServer || !IsSpawned)
                return;
            _protectedUntil = Time.timeAsDouble
                + (_definition != null ? _definition.PlacementProtectionSeconds : 0f);
            _windowEndsAt = 0;
            _windowDamage = 0;
            _resetPose = new Pose(_rigidbody.position, _rigidbody.rotation);
        }

        /// <summary>개발 도구가 전체 내구도와 파손 가구를 복구한다. R 리셋은 배치 위치까지 되돌린다.</summary>
        public static void ServerResetAll(bool restorePlacement)
        {
            foreach (FurnitureNetworkPhysics furniture in Registry)
                if (furniture != null && furniture.IsServer && furniture.IsSpawned)
                    furniture.ServerResetDurability(restorePlacement);
        }

        /// <summary>개발용 복구. 분해·조립 대기 여부는 그대로 유지한다.</summary>
        public void ServerResetDurability(bool restorePlacement)
        {
            if (!IsServer || !IsSpawned)
                return;
            if (_grabTarget != null)
                _grabTarget.ServerResetForPool();
            if (restorePlacement)
            {
                if (_networkTransform != null && _networkTransform.IsSpawned)
                    _networkTransform.Teleport(_resetPose.position, _resetPose.rotation, transform.localScale);
                RoomPreset.TeleportBody(_rigidbody, _resetPose.position, _resetPose.rotation);
            }
            SetDurability(FullDurability);
            RefreshPresentation();
            if (!_rigidbody.isKinematic)
            {
                _rigidbody.linearVelocity = Vector3.zero;
                _rigidbody.angularVelocity = Vector3.zero;
            }
            // F1의 체력 복구로 R의 복귀 위치가 바뀌어서는 안 된다.
            Pose resetPose = _resetPose;
            ServerProtectPlacement();
            _resetPose = resetPose;
        }

        /// <summary>풀 배치 상태와 파손 여부를 함께 반영한다. 모든 접속자에서 호출 가능하다.</summary>
        public void RefreshPresentation()
        {
            if (_rigidbody == null || !IsSpawned)
                return;
            bool available = IsAvailable;
            foreach (Renderer renderer in _renderers)
                if (renderer != null)
                    renderer.forceRenderingOff = IsBroken;
            for (int i = 0; i < _colliders.Length; i++)
                if (_colliders[i] != null)
                    _colliders[i].enabled = available && _colliderEnabled[i];
            if (!available && !_rigidbody.isKinematic)
            {
                _rigidbody.linearVelocity = Vector3.zero;
                _rigidbody.angularVelocity = Vector3.zero;
            }
            _rigidbody.isKinematic = !IsServer || !available;
            _rigidbody.detectCollisions = available;
            _rigidbody.useGravity = available
                && (_grabTarget == null || _grabTarget.State != FurnitureState.Held);
            if (_randomItem != null)
                _randomItem.RefreshTargetVisibility();
        }

        private void SetDurability(int durability)
        {
            // 없애지 않는 설정에서는 0이 되어도 들고 있던 홀더를 떼지 않는다 — 그냥 계속 쓰는 가구다.
            if (durability == 0 && DestroysAtZeroDurability && _grabTarget != null)
                _grabTarget.ServerResetForPool();
            _durability.Value = durability;
        }

        private void HandleDurabilityChanged(int previous, int current)
        {
            RefreshPresentation();
            if (IsServer && IsBroken && _poolItem != null)
                FurnitureAssemblyZone.ServerRemoveBrokenItem(_poolItem);
        }

        private static bool IsExcluded(Collider collider)
        {
            if (collider == null)
                return true;
            int layer = collider.gameObject.layer;
            return layer == GameLayers.Player || layer == GameLayers.GhostPrototype
                || collider.GetComponentInParent<PlayerMotor>() != null
                || collider.GetComponentInParent<GhostPrototypeController>() != null;
        }
    }
}
