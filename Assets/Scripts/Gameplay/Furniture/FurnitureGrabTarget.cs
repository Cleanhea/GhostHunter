using System;
using System.Collections.Generic;
using GhostHunter.Gameplay.Interaction;
using GhostHunter.Gameplay.Map;
using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Gameplay.Furniture
{
    public enum FurnitureState : byte
    {
        Idle,
        ThrowReady,
        Held,
        Launched,
    }

    /// <summary>
    /// 가구의 홀더 목록과 상태를 소유한다. 모든 변경은 서버에서만 일어나며, 클라이언트에는
    /// NetworkList/NetworkVariable로 표시 상태만 복제된다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class FurnitureGrabTarget : NetworkBehaviour
    {
        public const int MaxHolders = 2;

        [SerializeField] private FurnitureThrowSettings _settings;

        private readonly NetworkList<ulong> _holders = new();
        private readonly NetworkVariable<FurnitureState> _state = new(FurnitureState.Idle);
        private readonly NetworkVariable<float> _charge = new(0f);
        private readonly Dictionary<ulong, HolderAim> _aims = new();
        private readonly List<Vector3> _releasedDirections = new(MaxHolders);

        private Rigidbody _rigidbody;
        private FurnitureLauncher _launcher;
        private RandomFurnitureItem _randomItem;
        private float _heldSince;
        private float _launchedAt;

        private struct HolderAim
        {
            public Vector3 Origin;
            public Vector3 Direction;
        }

        public NetworkList<ulong> Holders => _holders;
        public FurnitureState State => _state.Value;
        public float Charge => _charge.Value;
        public int HolderCount => _holders.Count;
        public bool HasFreeSlot => _holders.Count < MaxHolders;
        public event Action<FurnitureState, FurnitureState> StateChanged;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            _launcher = GetComponent<FurnitureLauncher>();
            _randomItem = GetComponent<RandomFurnitureItem>();
        }

        public override void OnNetworkSpawn()
        {
            _state.OnValueChanged += HandleStateValueChanged;

            if (IsServer && NetworkManager != null)
                NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
        }

        public override void OnNetworkDespawn()
        {
            _state.OnValueChanged -= HandleStateValueChanged;

            if (IsServer && NetworkManager != null)
                NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;

            _aims.Clear();
            _releasedDirections.Clear();
        }

        private void FixedUpdate()
        {
            if (!IsServer || _settings == null)
                return;

            if (_state.Value is FurnitureState.ThrowReady or FurnitureState.Held)
            {
                _charge.Value = Mathf.Clamp01((Time.time - _heldSince) / _settings.ChargeTime);
                return;
            }

            if (_state.Value != FurnitureState.Launched)
                return;

            float elapsed = Time.time - _launchedAt;
            bool settled = elapsed >= 0.2f && _rigidbody.linearVelocity.sqrMagnitude
                <= _settings.SettledSpeed * _settings.SettledSpeed;

            if (settled || elapsed >= _settings.RelaunchLockDuration)
                _state.Value = FurnitureState.Idle;
        }

        public bool CanGrab(ulong clientId)
        {
            if (_randomItem != null && !_randomItem.IsPlaced)
                return false;
            if (_state.Value == FurnitureState.Launched)
                return false;

            return ContainsHolder(clientId) || _holders.Count < MaxHolders;
        }

        public bool ContainsHolder(ulong clientId)
        {
            for (int i = 0; i < _holders.Count; i++)
            {
                if (_holders[i] == clientId)
                    return true;
            }

            return false;
        }

        public bool ServerTryAddHolder(
            ulong clientId,
            Vector3 origin,
            Vector3 direction)
        {
            if (!IsServer
                || (_randomItem != null && !_randomItem.IsPlaced)
                || _state.Value == FurnitureState.Launched
                || _holders.Count >= MaxHolders
                || ContainsHolder(clientId)
                || !TrySanitizeAim(origin, direction, out HolderAim aim))
            {
                return false;
            }

            bool firstHolder = _holders.Count == 0;
            _holders.Add(clientId);
            _aims[clientId] = aim;

            if (firstHolder)
            {
                _heldSince = Time.time;
                _releasedDirections.Clear();
                _charge.Value = 0f;
                _state.Value = FurnitureState.ThrowReady;
                _rigidbody.useGravity = true;
            }
            else
            {
                _state.Value = FurnitureState.Held;
                _rigidbody.useGravity = false;
            }

            return true;
        }

        public void ServerUpdateAim(ulong clientId, Vector3 origin, Vector3 direction)
        {
            if (!IsServer
                || !ContainsHolder(clientId)
                || !TrySanitizeAim(origin, direction, out HolderAim aim))
            {
                return;
            }

            _aims[clientId] = aim;
        }

        public void ServerRelease(ulong clientId, Vector3 direction, bool forced)
        {
            if (!IsServer || !ContainsHolder(clientId))
                return;

            Vector3 releaseDirection = ResolveReleaseDirection(clientId, direction);
            int countBeforeRelease = _holders.Count;

            RemoveHolder(clientId);
            NotifyPlayerReleased(clientId);

            if (forced)
            {
                RefreshStateAfterHolderLeft();

                return;
            }

            _releasedDirections.Add(releaseDirection);

            if (countBeforeRelease > 1 && !_settings.LaunchOnFirstRelease)
            {
                _releasedDirections.Clear();
                RefreshStateAfterHolderLeft();
                return;
            }

            Vector3 combinedDirection = CalculateLaunchDirection(releaseDirection);
            int launchHolderCount = countBeforeRelease;
            float launchCharge = _charge.Value;

            ClearRemainingHolders();
            _rigidbody.useGravity = true;
            _state.Value = FurnitureState.Launched;
            _launchedAt = Time.time;
            _charge.Value = 0f;

            if (_launcher != null)
                _launcher.ServerLaunch(combinedDirection, launchCharge, launchHolderCount);
        }

        public void ServerForceRelease(ulong clientId)
        {
            ServerRelease(clientId, Vector3.forward, true);
        }

        public bool TryGetHolderAim(
            int index,
            out ulong clientId,
            out Vector3 origin,
            out Vector3 direction)
        {
            if (index < 0 || index >= _holders.Count)
            {
                clientId = default;
                origin = default;
                direction = default;
                return false;
            }

            clientId = _holders[index];
            if (!_aims.TryGetValue(clientId, out HolderAim aim))
            {
                origin = default;
                direction = default;
                return false;
            }

            origin = aim.Origin;
            direction = aim.Direction;
            return true;
        }

        private Vector3 ResolveReleaseDirection(ulong clientId, Vector3 requested)
        {
            if (IsFinite(requested) && requested.sqrMagnitude > 0.01f)
                return requested.normalized;

            return _aims.TryGetValue(clientId, out HolderAim aim)
                ? aim.Direction
                : transform.forward;
        }

        private Vector3 CalculateLaunchDirection(Vector3 fallback)
        {
            Vector3 sum = Vector3.zero;
            foreach (Vector3 direction in _releasedDirections)
                sum += direction;

            foreach (ulong holder in _holders)
            {
                if (_aims.TryGetValue(holder, out HolderAim aim))
                    sum += aim.Direction;
            }

            return sum.sqrMagnitude > 0.01f ? sum.normalized : fallback.normalized;
        }

        private void RemoveHolder(ulong clientId)
        {
            for (int i = _holders.Count - 1; i >= 0; i--)
            {
                if (_holders[i] == clientId)
                    _holders.RemoveAt(i);
            }

            _aims.Remove(clientId);
        }

        private void ClearRemainingHolders()
        {
            while (_holders.Count > 0)
            {
                ulong holder = _holders[_holders.Count - 1];
                _holders.RemoveAt(_holders.Count - 1);
                _aims.Remove(holder);
                NotifyPlayerReleased(holder);
            }
        }

        private void ServerReturnToIdle()
        {
            _rigidbody.useGravity = true;
            _state.Value = FurnitureState.Idle;
            _charge.Value = 0f;
            _releasedDirections.Clear();
        }

        private void RefreshStateAfterHolderLeft()
        {
            _rigidbody.useGravity = true;

            if (_holders.Count == 0)
            {
                ServerReturnToIdle();
                return;
            }

            _state.Value = FurnitureState.ThrowReady;
        }

        private void NotifyPlayerReleased(ulong clientId)
        {
            if (NetworkManager == null
                || !NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client)
                || client.PlayerObject == null)
            {
                return;
            }

            GrabController controller = client.PlayerObject.GetComponent<GrabController>();
            controller?.ServerClearHeld(NetworkObjectId);
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            if (ContainsHolder(clientId))
                ServerForceRelease(clientId);
        }

        private void HandleStateValueChanged(FurnitureState previous, FurnitureState current)
        {
            StateChanged?.Invoke(previous, current);
        }

        private static bool TrySanitizeAim(
            Vector3 origin,
            Vector3 direction,
            out HolderAim aim)
        {
            if (!IsFinite(origin) || !IsFinite(direction) || direction.sqrMagnitude < 0.25f)
            {
                aim = default;
                return false;
            }

            aim = new HolderAim
            {
                Origin = origin,
                Direction = direction.normalized,
            };
            return true;
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x)
                && float.IsFinite(value.y)
                && float.IsFinite(value.z);
        }
    }
}
