using GhostHunter.Gameplay.Player;
using GhostHunter.Gameplay.Furniture;
using GhostHunter.Gameplay.Recovery;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace GhostHunter.Gameplay.Map
{
    /// <summary>씬 가구 풀의 배치 여부와 작업 대상 여부를 서버에서 확정하고 복제한다.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject), typeof(NetworkTransform), typeof(Rigidbody))]
    public sealed class RandomFurnitureItem : NetworkBehaviour
    {
        private const byte PlacedFlag = 1;
        private const byte TargetFlag = 2;
        private const byte DeliveredFlag = 4;

        [SerializeField] private string _poolId = "Box";
        [SerializeField] private Bounds _localBounds = new(Vector3.zero, Vector3.one);
        [SerializeField] private DetectionTargetMarker _detectionMarker;

        private readonly NetworkVariable<byte> _placementState = new(0,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private NetworkTransform _networkTransform;
        private Rigidbody _body;
        private FurnitureNetworkPhysics _physics;
        private Renderer[] _renderers;
        private Collider[] _colliders;
        private bool[] _rendererEnabled;
        private bool[] _colliderEnabled;
        private Pose _parkingPose;

        public string PoolId => _poolId;
        public Bounds LocalBounds => _localBounds;
        public bool IsPlaced => IsSpawned && (_placementState.Value & PlacedFlag) != 0;
        public bool IsAssignedWorkTarget => IsPlaced && (_placementState.Value & TargetFlag) != 0;
        public bool IsDelivered => IsAssignedWorkTarget && (_placementState.Value & DeliveredFlag) != 0;
        public bool IsWorkTarget => IsPlaced && (_physics == null || !_physics.IsBroken)
            && IsAssignedWorkTarget && !IsDelivered;
        /// <summary>맵에 실제로 놓여 있는지. 반출된 목표 가구는 배치 기록만 남기고 사라진다.</summary>
        public bool IsPresent => IsPlaced && !IsDelivered;

        public StageRecoverySnapshot.FurnitureState CaptureStageState()
        {
            return new StageRecoverySnapshot.FurnitureState
            {
                Placement = _placementState.Value,
                Position = transform.position,
                Rotation = transform.rotation,
                LinearVelocity = _body != null ? _body.linearVelocity : Vector3.zero,
                AngularVelocity = _body != null ? _body.angularVelocity : Vector3.zero,
                Durability = _physics != null ? _physics.Durability : 100,
            };
        }

        public bool ServerRestoreStageState(StageRecoverySnapshot.FurnitureState snapshot)
        {
            if (!IsServer || !IsSpawned || _networkTransform == null || !_networkTransform.IsSpawned)
                return false;
            GetComponent<FurnitureGrabTarget>()?.ServerResetForPool();
            _networkTransform.Teleport(snapshot.Position, snapshot.Rotation, transform.localScale);
            RoomPreset.TeleportBody(_body, snapshot.Position, snapshot.Rotation);
            _placementState.Value = snapshot.Placement;
            if (_physics != null)
            {
                _physics.ServerRestoreStageDurability(snapshot.Durability);
                _physics.ServerProtectPlacement();
            }
            ApplyPresentation(IsPresent, IsWorkTarget);
            if (IsPresent && _body != null && !_body.isKinematic)
            {
                _body.linearVelocity = snapshot.LinearVelocity;
                _body.angularVelocity = snapshot.AngularVelocity;
            }
            return true;
        }

        /// <summary>
        /// 서버가 반출 구역에 들어온 목표 가구를 한 번만 완료 처리한다. 잡고 있던 플레이어를 놓게 하고
        /// 보관 위치로 치운다. 정산 집계를 위해 배치·목표 플래그는 남긴다.
        /// </summary>
        public bool ServerCompleteDelivery()
        {
            if (!IsServer || !IsAssignedWorkTarget || IsDelivered
                || !FurnitureDeliveryZone.Contains(transform.position))
                return false;

            GetComponent<FurnitureGrabTarget>()?.ServerResetForPool();
            _placementState.Value |= DeliveredFlag;
            ApplyPresentation(false, false);
            if (_networkTransform != null && _networkTransform.IsSpawned)
                _networkTransform.Teleport(_parkingPose.position, _parkingPose.rotation, transform.localScale);
            RoomPreset.TeleportBody(_body, _parkingPose.position, _parkingPose.rotation);
            return true;
        }

        private void Awake()
        {
            _networkTransform = GetComponent<NetworkTransform>();
            _body = GetComponent<Rigidbody>();
            _physics = GetComponent<FurnitureNetworkPhysics>();
            _renderers = GetComponentsInChildren<Renderer>(true);
            _colliders = GetComponentsInChildren<Collider>(true);
            _rendererEnabled = new bool[_renderers.Length];
            _colliderEnabled = new bool[_colliders.Length];
            for (int i = 0; i < _renderers.Length; i++)
                _rendererEnabled[i] = _renderers[i].enabled;
            for (int i = 0; i < _colliders.Length; i++)
                _colliderEnabled[i] = _colliders[i].enabled;
            _parkingPose = new Pose(transform.position, transform.rotation);
            ApplyPresentation(false, false);
        }

        public override void OnNetworkSpawn()
        {
            _placementState.OnValueChanged += HandlePlacementChanged;
            if (IsServer && !StageRecoveryGate.Restoring)
                _placementState.Value = 0;
            ApplyPresentation(IsPresent, IsWorkTarget);
        }

        public override void OnNetworkDespawn()
        {
            _placementState.OnValueChanged -= HandlePlacementChanged;
            ApplyPresentation(false, false);
            transform.SetPositionAndRotation(_parkingPose.position, _parkingPose.rotation);
            RoomPreset.TeleportBody(_body, _parkingPose.position, _parkingPose.rotation);
        }

        /// <summary>에디터 설치 도구에서 풀 식별자·충돌 경계·탐지 마커를 구성한다.</summary>
        public void Configure(string poolId, Bounds localBounds, DetectionTargetMarker marker)
        {
            _poolId = poolId;
            _localBounds = localBounds;
            _detectionMarker = marker;
        }

        /// <summary>서버에서 보관 가구를 확정된 위치로 옮긴 뒤 물리·표시를 활성화한다.</summary>
        public bool ServerPlace(Pose pose, bool isWorkTarget)
        {
            if (!IsServer || !IsSpawned || _networkTransform == null || !_networkTransform.IsSpawned)
                return false;
            if (_physics != null && _physics.IsBroken)
                return false;
            _networkTransform.Teleport(pose.position, pose.rotation, transform.localScale);
            RoomPreset.TeleportBody(_body, pose.position, pose.rotation);
            _placementState.Value = (byte)(PlacedFlag | (isWorkTarget ? TargetFlag : 0));
            ApplyPresentation(true, isWorkTarget);
            if (_physics != null)
                _physics.ServerProtectPlacement();
            return true;
        }

        /// <summary>서버에서 실패한 배치를 보관 상태로 되돌린다.</summary>
        public void ServerPark()
        {
            if (!IsServer || !IsSpawned)
                return;
            _placementState.Value = 0;
            ApplyPresentation(false, false);
            if (_networkTransform != null && _networkTransform.IsSpawned)
                _networkTransform.Teleport(_parkingPose.position, _parkingPose.rotation, transform.localScale);
            RoomPreset.TeleportBody(_body, _parkingPose.position, _parkingPose.rotation);
        }

        /// <summary>가구의 아래 면을 지지점에 맞춘 자세와 보수적인 월드 충돌 경계를 구한다.</summary>
        public void GetPlacement(FurnitureSpawnPoint point, out Pose pose, out Bounds worldBounds)
        {
            Quaternion rotation = point.transform.rotation;
            Vector3 size = _localBounds.size;
            Vector3 x = rotation * new Vector3(size.x, 0f, 0f);
            Vector3 y = rotation * new Vector3(0f, size.y, 0f);
            Vector3 z = rotation * new Vector3(0f, 0f, size.z);
            Vector3 rotatedSize = new(Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
                Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
            Vector3 center = point.transform.position + Vector3.up * rotatedSize.y * 0.5f;
            pose = new Pose(center - rotation * _localBounds.center, rotation);
            worldBounds = new Bounds(center, rotatedSize);
        }

        private void HandlePlacementChanged(byte previous, byte current)
        {
            ApplyPresentation(IsPresent, IsWorkTarget);
        }

        /// <summary>파손·복구 시 작업 대상 마커만 갱신한다.</summary>
        public void RefreshTargetVisibility()
        {
            if (_detectionMarker != null)
                _detectionMarker.SetTargetActive(IsWorkTarget);
        }

        private void ApplyPresentation(bool placed, bool target)
        {
            for (int i = 0; i < _renderers.Length; i++)
                if (_renderers[i] != null)
                    _renderers[i].enabled = placed && _rendererEnabled[i];
            for (int i = 0; i < _colliders.Length; i++)
                if (_colliders[i] != null)
                    _colliders[i].enabled = placed && _colliderEnabled[i];
            _body.isKinematic = !placed || !IsServer;
            _body.detectCollisions = placed;
            if (_detectionMarker != null)
                _detectionMarker.SetTargetActive(placed && target);
            if (_physics != null)
                _physics.RefreshPresentation();
        }
    }
}
