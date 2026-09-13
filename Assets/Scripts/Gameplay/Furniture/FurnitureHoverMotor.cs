using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Gameplay.Furniture
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody), typeof(FurnitureGrabTarget))]
    public sealed class FurnitureHoverMotor : NetworkBehaviour
    {
        [SerializeField] private FurnitureThrowSettings _settings;

        private Rigidbody _rigidbody;
        private FurnitureGrabTarget _target;
        private Collider[] _colliders;
        private readonly ulong[] _holderBuffer = new ulong[FurnitureGrabTarget.MaxHolders];

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            _target = GetComponent<FurnitureGrabTarget>();
            _colliders = GetComponentsInChildren<Collider>(true);
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer)
                enabled = false;
        }

        private void FixedUpdate()
        {
            if (_settings == null
                || _target.State is not (FurnitureState.ThrowReady or FurnitureState.Held))
                return;

            int snapshotCount = Mathf.Min(_target.HolderCount, _holderBuffer.Length);
            for (int i = 0; i < snapshotCount; i++)
            {
                if (_target.TryGetHolderAim(i, out ulong clientId, out _, out _))
                    _holderBuffer[i] = clientId;
            }

            Vector3 targetSum = Vector3.zero;
            int validCount = 0;

            for (int i = 0; i < snapshotCount; i++)
            {
                ulong clientId = _holderBuffer[i];
                if (!TryGetPlayerPosition(clientId, out _))
                {
                    _target.ServerForceRelease(clientId);
                    continue;
                }

                int currentIndex = FindHolderIndex(clientId);
                if (!_target.TryGetHolderAim(currentIndex, out _, out Vector3 origin, out Vector3 direction))
                    continue;

                // 차징 중(1인 투척 준비·2인 잡기 모두) 눈 위치에서 가구 표면까지 멀어지면 발사 없이 푼다.
                // 중심이 아니라 표면 기준이라 큰 가구나 위를 보고 든 2인 잡기에서 헛해제되지 않는다.
                if (DistanceToSurface(origin) > _settings.HoldBreakDistance)
                {
                    _target.ServerForceRelease(clientId);
                    continue;
                }

                if (_target.State == FurnitureState.Held)
                {
                    targetSum += origin + direction * _settings.HoverDistance;
                    validCount++;
                }
            }

            if (_target.State != FurnitureState.Held || validCount < 2)
                return;

            Vector3 desiredPosition = targetSum / validCount;
            float deltaTime = Time.fixedDeltaTime;

            // 스프링 힘 대신 매 스텝 목표에 닿는 속도를 직접 준다 — 뒤처지거나 출렁이지 않는다.
            // kinematic 이 아니라 동적 바디 그대로라 벽·다른 가구에는 막힌다.
            _rigidbody.linearVelocity = FurnitureHeldControl.TrackingVelocity(
                _rigidbody.position, desiredPosition, deltaTime, _settings.HeldMaxLinearSpeed);
            _rigidbody.angularVelocity = FurnitureHeldControl.TrackingAngularVelocity(
                _rigidbody.rotation, _target.HeldRotation, deltaTime, _settings.HeldMaxAngularSpeed);
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
