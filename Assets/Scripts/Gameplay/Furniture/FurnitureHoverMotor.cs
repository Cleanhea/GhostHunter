using GhostHunter.Core;
using GhostHunter.Gameplay.Interaction;
using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Gameplay.Furniture
{
    /// <summary>
    /// 서버 전용 — 잡힌 가구의 홀드 거리 해제와 2인 운반(Held) → docs/architecture/throw-system.md §3.
    /// 추종 계산(두 손잡이·속도 서보·끼임 보조)은 <see cref="FurnitureCarrySession"/>이 하고, 여기서는 홀더 확인,
    /// 손잡이 찾기, 운반 중 마찰 낮추기, 열린 문짝과 충돌 끄기, 옆으로 막는 접촉 기록을 맡는다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody), typeof(FurnitureGrabTarget))]
    public sealed class FurnitureHoverMotor : NetworkBehaviour
    {
        private const float DoorScanSeconds = 0.2f;
        private const float DoorScanMargin = 0.6f;
        private const float WallNormalLimit = 0.7f;

        [SerializeField] private FurnitureThrowSettings _settings;

        private Rigidbody _rigidbody;
        private FurnitureGrabTarget _target;
        private Collider[] _colliders;
        private readonly ulong[] _holderBuffer = new ulong[FurnitureGrabTarget.MaxHolders];
        private readonly ulong[] _carryHolders = new ulong[FurnitureGrabTarget.MaxHolders];
        private readonly Collider[] _doorScanBuffer = new Collider[32];

        private bool _carrying;
        private bool _touchingWall;
        private float _nextDoorScanAt;
        private FurnitureCarrySession _session;
        private FurnitureClearanceProbe _probe;
        private FurnitureCollisionIgnoreSet _doorIgnores;
        private PhysicsMaterial _carryMaterial;
        private PhysicsMaterial[] _savedMaterials;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            _target = GetComponent<FurnitureGrabTarget>();
            _colliders = GetComponentsInChildren<Collider>(true);
            _savedMaterials = new PhysicsMaterial[_colliders.Length];
            _doorIgnores = new FurnitureCollisionIgnoreSet(_colliders);

            // 끼임 보조 겹침 검사에서 플레이어·귀신은 뺀다 — 비켜 갈 대상이 아니다.
            int playerMask = GameLayers.Player >= 0 ? 1 << GameLayers.Player : 0;
            _probe = new FurnitureClearanceProbe(
                _rigidbody, _colliders, GameLayers.NonGhostPrototypeRaycastMask & ~playerMask);
            _session = new FurnitureCarrySession(_rigidbody, _probe, IsIgnoredByCarry);
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer)
                enabled = false;
        }

        public override void OnNetworkDespawn()
        {
            EndCarry();
            _doorIgnores.ClearImmediately();
        }

        private void OnDisable()
        {
            EndCarry();
        }

        public override void OnDestroy()
        {
            _doorIgnores?.ClearImmediately();
            if (_carryMaterial != null)
                Destroy(_carryMaterial);
            base.OnDestroy();
        }

        private void FixedUpdate()
        {
            _doorIgnores.Tick();
            bool touchingWall = _touchingWall;
            _touchingWall = false;

            if (_settings == null
                || _target.State is not (FurnitureState.ThrowReady or FurnitureState.Held))
            {
                EndCarry();
                return;
            }

            int snapshotCount = Mathf.Min(_target.HolderCount, _holderBuffer.Length);
            for (int i = 0; i < snapshotCount; i++)
            {
                if (_target.TryGetHolderAim(i, out ulong clientId, out _, out _))
                    _holderBuffer[i] = clientId;
            }

            for (int i = 0; i < snapshotCount; i++)
            {
                ulong clientId = _holderBuffer[i];
                if (!TryGetPlayerPosition(clientId, out _))
                {
                    _target.ServerForceRelease(clientId);
                    continue;
                }

                int currentIndex = FindHolderIndex(clientId);
                if (!_target.TryGetHolderAim(currentIndex, out _, out Vector3 origin, out _))
                    continue;

                // 차징 중(1인 투척 준비·2인 잡기 모두) 눈 위치에서 가구 표면까지 멀어지면 발사 없이 푼다.
                // 중심이 아니라 표면 기준이라 큰 가구나 위를 보고 든 2인 잡기에서 헛해제되지 않는다.
                if (DistanceToSurface(origin) > _settings.HoldBreakDistance)
                    _target.ServerForceRelease(clientId);
            }

            if (_target.State != FurnitureState.Held || _target.HolderCount < FurnitureGrabTarget.MaxHolders)
            {
                EndCarry();
                return;
            }

            if ((!_carrying || !SameHolders()) && !BeginCarry())
                return;

            if (!_target.TryGetHolderAim(0, out _, out Vector3 originA, out Vector3 directionA)
                || !_target.TryGetHolderAim(1, out _, out Vector3 originB, out Vector3 directionB))
                return;

            _session.Step(Time.fixedDeltaTime, Time.time, originA, directionA, originB, directionB,
                _target.HeldRotation, touchingWall);
            ScanDoors();
        }

        private void OnCollisionEnter(Collision collision) => NoteContact(collision);

        private void OnCollisionStay(Collision collision) => NoteContact(collision);

        /// <summary>운반 중 옆으로 막는 접촉(벽·문틀·다른 가구)이 있었는지 기록한다. 바닥·천장·플레이어 접촉은 뺀다.</summary>
        private void NoteContact(Collision collision)
        {
            if (!_carrying || _touchingWall || collision.collider == null
                || collision.collider.gameObject.layer == GameLayers.Player)
                return;

            for (int i = 0; i < collision.contactCount; i++)
            {
                if (Mathf.Abs(collision.GetContact(i).normal.y) < WallNormalLimit)
                {
                    _touchingWall = true;
                    return;
                }
            }
        }

        private bool BeginCarry()
        {
            EndCarry();

            if (!_target.TryGetHolderAim(0, out ulong holderA, out Vector3 originA, out Vector3 directionA)
                || !_target.TryGetHolderAim(1, out ulong holderB, out Vector3 originB, out Vector3 directionB))
                return false;

            _carryHolders[0] = holderA;
            _carryHolders[1] = holderB;
            _session.Begin(
                _settings,
                originA, directionA, FindGripPoint(originA, directionA),
                originB, directionB, FindGripPoint(originB, directionB));
            _nextDoorScanAt = 0f;
            ApplyCarryMaterial();
            _carrying = true;
            return true;
        }

        private void EndCarry()
        {
            if (!_carrying)
                return;

            _carrying = false;
            RestoreMaterials();
            _doorIgnores.ReleaseAll();
        }

        /// <summary>근처의 열린 문짝과는 충돌을 끄고, 닫힌 문짝은 떨어지는 대로 다시 켠다.</summary>
        private void ScanDoors()
        {
            if (!_settings.CarryPassesOpenDoors || Time.time < _nextDoorScanAt)
                return;

            _nextDoorScanAt = Time.time + DoorScanSeconds;
            for (int i = _doorIgnores.Ignored.Count - 1; i >= 0; i--)
            {
                Collider ignored = _doorIgnores.Ignored[i];
                DoorInteractable door = ignored != null ? ignored.GetComponentInParent<DoorInteractable>() : null;
                if (door == null || !door.IsOpen)
                    _doorIgnores.Release(ignored);
            }

            if (!_probe.TryGetLocalBounds(transform, out Vector3 centerLocal, out float radius))
                return;

            int count = Physics.OverlapSphereNonAlloc(
                transform.position + transform.rotation * centerLocal,
                radius + DoorScanMargin,
                _doorScanBuffer,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider other = _doorScanBuffer[i];
                if (other == null || other.attachedRigidbody == _rigidbody)
                    continue;

                DoorInteractable door = other.GetComponentInParent<DoorInteractable>();
                if (door != null && door.IsOpen)
                    _doorIgnores.Ignore(other);
            }
        }

        private bool IsIgnoredByCarry(Collider other)
        {
            return _doorIgnores.Contains(other) || _target.IsIgnoringHolderCollider(other);
        }

        /// <summary>운반 중에는 마찰을 낮춰 문틀·벽을 긁으며 미끄러져 지나가게 한다. 놓으면 원래 재질로 돌린다.</summary>
        private void ApplyCarryMaterial()
        {
            if (_carryMaterial == null)
            {
                _carryMaterial = new PhysicsMaterial("CarriedFurniture")
                {
                    bounciness = 0f,
                    bounceCombine = PhysicsMaterialCombine.Minimum,
                    frictionCombine = PhysicsMaterialCombine.Minimum,
                };
            }

            _carryMaterial.dynamicFriction = _settings.CarryFriction;
            _carryMaterial.staticFriction = _settings.CarryFriction;
            for (int i = 0; i < _colliders.Length; i++)
            {
                Collider part = _colliders[i];
                if (part == null)
                    continue;

                _savedMaterials[i] = part.sharedMaterial;
                part.sharedMaterial = _carryMaterial;
            }
        }

        private void RestoreMaterials()
        {
            for (int i = 0; i < _colliders.Length; i++)
            {
                if (_colliders[i] != null)
                    _colliders[i].sharedMaterial = _savedMaterials[i];
                _savedMaterials[i] = null;
            }
        }

        private bool SameHolders()
        {
            for (int i = 0; i < FurnitureGrabTarget.MaxHolders; i++)
            {
                if (_target.Holders[i] != _carryHolders[i])
                    return false;
            }

            return true;
        }

        /// <summary>조준 광선이 가구에 닿은 점. 빗나가면 눈에서 가장 가까운 가구 표면 점.</summary>
        private Vector3 FindGripPoint(Vector3 origin, Vector3 direction)
        {
            var ray = new Ray(origin, direction);
            float best = float.PositiveInfinity;
            Vector3 point = default;
            foreach (Collider part in _colliders)
            {
                if (part == null || !part.enabled || part.isTrigger)
                    continue;

                if (part.Raycast(ray, out RaycastHit hit, _settings.HoldBreakDistance + 1f) && hit.distance < best)
                {
                    best = hit.distance;
                    point = hit.point;
                }
            }

            return float.IsPositiveInfinity(best) ? ClosestSurfacePoint(origin) : point;
        }

        private Vector3 ClosestSurfacePoint(Vector3 point)
        {
            float bestSqr = float.PositiveInfinity;
            Vector3 best = _rigidbody.position;
            foreach (Collider part in _colliders)
            {
                if (part == null || !part.enabled || part.isTrigger)
                    continue;

                Vector3 candidate = part.ClosestPoint(point);
                float sqr = (candidate - point).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = candidate;
                }
            }

            return best;
        }

        /// <summary>점에서 가구 콜라이더 표면까지의 최단 거리. 켜진 콜라이더가 없으면 바디 위치까지 잰다.</summary>
        private float DistanceToSurface(Vector3 point)
        {
            float bestSqr = float.PositiveInfinity;
            foreach (Collider part in _colliders)
            {
                if (part == null || !part.enabled || part.isTrigger)
                    continue;

                float sqr = (part.ClosestPoint(point) - point).sqrMagnitude;
                if (sqr < bestSqr)
                    bestSqr = sqr;
            }

            return float.IsPositiveInfinity(bestSqr)
                ? Vector3.Distance(point, _rigidbody.position)
                : Mathf.Sqrt(bestSqr);
        }

        private int FindHolderIndex(ulong clientId)
        {
            for (int i = 0; i < _target.HolderCount; i++)
            {
                if (_target.Holders[i] == clientId)
                    return i;
            }

            return -1;
        }

        private bool TryGetPlayerPosition(ulong clientId, out Vector3 position)
        {
            if (NetworkManager != null
                && NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client)
                && client.PlayerObject != null)
            {
                position = client.PlayerObject.transform.position;
                return true;
            }

            position = default;
            return false;
        }
    }
}
