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
        private readonly ulong[] _holderBuffer = new ulong[FurnitureGrabTarget.MaxHolders];

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            _target = GetComponent<FurnitureGrabTarget>();
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
                if (!TryGetPlayerPosition(clientId, out Vector3 playerPosition)
                    || Vector3.Distance(playerPosition, _rigidbody.position) > _settings.MaxHoldDistance)
                {
                    _target.ServerForceRelease(clientId);
                    continue;
                }

                int currentIndex = FindHolderIndex(clientId);
                if (!_target.TryGetHolderAim(currentIndex, out _, out Vector3 origin, out Vector3 direction))
                    continue;

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
