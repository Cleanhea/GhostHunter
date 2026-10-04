using System;
using System.Collections.Generic;
using GhostHunter.Gameplay.FurnitureDriver;
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

        private readonly List<Collider> _holderColliderScratch = new();
        private readonly List<Collider> _playerColliderScratch = new();

        private Rigidbody _rigidbody;
        private FurnitureCollisionIgnoreSet _holderIgnores;
        private FurnitureLauncher _launcher;
        private RandomFurnitureItem _randomItem;
        private FurnitureDriverPoolItem _driverPoolItem;
        private FurnitureNetworkPhysics _physics;
        private float _heldSince;
        private float _launchedAt;
        // 2인 잡기에서 한 명이 먼저 놓은 순간부터 남은 한 명이 놓아도 '같이 내려놓기'로 치는 마감 시각.
        private float _jointPutDownUntil = float.NegativeInfinity;
        private Quaternion _heldRotation = Quaternion.identity;

        private struct HolderAim
        {
            public Vector3 Origin;
            public Vector3 Direction;
        }

        public NetworkList<ulong> Holders => _holders;
        public FurnitureState State => _state.Value;
        public float Charge => _charge.Value;

        /// <summary>지금 놓으면 나갈 힘의 비율(최대 힘 대비, 차지 곡선 적용). 차지 게이지가 표시한다.</summary>
        public float LaunchPower => _settings != null ? _settings.ForceRatio(_charge.Value) : _charge.Value;
        public int HolderCount => _holders.Count;
        public bool HasFreeSlot => _holders.Count < MaxHolders;

        /// <summary>서버 전용 — 2인 잡기 중 가구가 맞출 목표 자세. Held 진입 시 현재 자세로 시작해 휠로만 바뀐다.</summary>
        public Quaternion HeldRotation => _heldRotation;
        public event Action<FurnitureState, FurnitureState> StateChanged;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            _holderIgnores = new FurnitureCollisionIgnoreSet(GetComponentsInChildren<Collider>(true));
            _launcher = GetComponent<FurnitureLauncher>();
            _randomItem = GetComponent<RandomFurnitureItem>();
            _driverPoolItem = GetComponent<FurnitureDriverPoolItem>();
            _physics = GetComponent<FurnitureNetworkPhysics>();
        }

        /// <summary>
        /// 씬 풀 재배치 컴포넌트(랜덤 가구 또는 가구용 멀티 드라이버 부품/큰 가구)가 있다면
        /// 배치·활성화된 상태인지 확인한다. 둘 다 없으면 원래부터 항상 배치된 일반 가구다.
        /// </summary>
        private bool IsPlacementReady =>
            (_physics == null || (!_physics.IsBroken && !_physics.IsStowed))
            && (_randomItem == null || _randomItem.IsPresent)
            && (_driverPoolItem == null || _driverPoolItem.IsActive);

        public override void OnNetworkSpawn()
        {
            _state.OnValueChanged += HandleStateValueChanged;
            _holders.OnListChanged += HandleHoldersChanged;

            if (IsServer && NetworkManager != null)
                NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;

            RefreshHolderCollisionIgnore();
        }

        public override void OnNetworkDespawn()
        {
            _state.OnValueChanged -= HandleStateValueChanged;
            _holders.OnListChanged -= HandleHoldersChanged;

            if (IsServer && NetworkManager != null)
                NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;

            _aims.Clear();
            _releasedDirections.Clear();
            _holderIgnores.ClearImmediately();
        }

        public override void OnDestroy()
        {
            _holderIgnores?.ClearImmediately();
            base.OnDestroy();
        }

        private void FixedUpdate()
        {
            // 모든 피어 — 운반이 끝난 홀더와의 충돌을 떨어지는 대로 다시 켠다.
            if (!_holderIgnores.IsEmpty)
                _holderIgnores.Tick();

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
            if (!IsPlacementReady)
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
                || !IsPlacementReady
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
            // 새로 잡거나 다시 둘이 되면 이전 '같이 내려놓기' 대기는 끝난다.
            _jointPutDownUntil = float.NegativeInfinity;

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
                _heldRotation = _rigidbody.rotation;
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

        /// <summary>
        /// 2인 잡기 중인 홀더의 휠 한 칸으로 목표 자세를 바꾼다. 두 홀더 입력은 순서대로 합산된다.
        /// 기울이기 축은 그 홀더의 마지막 조준 방향에서 구한다.
        /// </summary>
        public bool ServerRotateHeld(ulong clientId, int steps, FurnitureRotateMode mode)
        {
            if (!IsServer
                || _settings == null
                || _state.Value != FurnitureState.Held
                || (steps != 1 && steps != -1)
                || mode is not (FurnitureRotateMode.Rotate or FurnitureRotateMode.Tilt)
                || !ContainsHolder(clientId)
                || !_aims.TryGetValue(clientId, out HolderAim aim))
            {
                return false;
            }

            _heldRotation = FurnitureHeldControl.ApplyWheel(
                _heldRotation, steps, mode, aim.Direction, _settings.WheelStepDegrees);
            return true;
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
                _jointPutDownUntil = _settings.JointPutDownWindow > 0f
                    ? Time.time + _settings.JointPutDownWindow
                    : float.NegativeInfinity;
                RefreshStateAfterHolderLeft();
                return;
            }

            // 둘이 들다가 먼저 놓은 사람과 거의 같이 놓았다 — 던지지 않고 그 자리에 내려놓는다.
            if (IsJointPutDown(countBeforeRelease, Time.time, _jointPutDownUntil))
            {
                ServerReturnToIdle();
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

        /// <summary>
        /// 마지막 한 명의 해제가 '같이 내려놓기'인가 — 2인 잡기에서 먼저 놓은 사람 뒤로
        /// <see cref="FurnitureThrowSettings.JointPutDownWindow"/> 안에 놓았으면 발사하지 않는다.
        /// </summary>
        internal static bool IsJointPutDown(int countBeforeRelease, float now, float putDownUntil)
        {
            return countBeforeRelease == 1 && now <= putDownUntil;
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

        /// <summary>서버에서 풀로 돌려보내기 전에 홀더를 알리고 잡기·투척 상태를 초기화한다.</summary>
        public void ServerResetForPool()
        {
            if (!IsServer || !IsSpawned)
                return;

            ClearRemainingHolders();
            ServerReturnToIdle();
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
            _jointPutDownUntil = float.NegativeInfinity;
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
            RefreshHolderCollisionIgnore();
            StateChanged?.Invoke(previous, current);
        }

        private void HandleHoldersChanged(NetworkListEvent<ulong> change)
        {
            RefreshHolderCollisionIgnore();
        }

        /// <summary>2인 운반 중 충돌을 끈(또는 다시 켜기를 기다리는) 홀더 플레이어 콜라이더인가 — 끼임 보조가 겹침 검사에서 뺀다.</summary>
        internal bool IsIgnoringHolderCollider(Collider other)
        {
            return _holderIgnores != null && _holderIgnores.Contains(other);
        }

        /// <summary>
        /// 2인 운반(Held) 중에는 두 홀더의 플레이어 콜라이더와 가구의 충돌을 끈다 — 서버에서는 가구가 들고 있는 사람
        /// 몸에 막히지 않고, 각 클라이언트에서는 자기 캐릭터가 키네마틱 가구 사본에 막히지 않는다(throw-system.md §3.3).
        /// 상태와 홀더 목록이 모든 피어에 복제되므로 각 피어가 같은 판단을 한다.
        /// </summary>
        private void RefreshHolderCollisionIgnore()
        {
            if (_holderIgnores == null)
                return;

            _holderColliderScratch.Clear();
            if (_settings != null && _settings.CarryIgnoresHolders && _state.Value == FurnitureState.Held)
            {
                for (int i = 0; i < _holders.Count; i++)
                {
                    NetworkObject player = FindPlayerObject(_holders[i]);
                    if (player == null)
                        continue;

                    _playerColliderScratch.Clear();
                    player.GetComponentsInChildren(_playerColliderScratch);
                    foreach (Collider part in _playerColliderScratch)
                    {
                        if (!part.isTrigger)
                            _holderColliderScratch.Add(part);
                    }
                }
            }

            for (int i = _holderIgnores.Ignored.Count - 1; i >= 0; i--)
            {
                Collider ignored = _holderIgnores.Ignored[i];
                if (!_holderColliderScratch.Contains(ignored))
                    _holderIgnores.Release(ignored);
            }

            foreach (Collider part in _holderColliderScratch)
                _holderIgnores.Ignore(part);
        }

        /// <summary>
        /// 홀더의 플레이어 오브젝트. 클라이언트-서버 구조에서 <c>GetPlayerNetworkObject</c>는 서버만 남의 것을 주므로
        /// 클라이언트는 스폰 목록에서 찾는다(홀더가 바뀔 때만 부른다).
        /// </summary>
        private NetworkObject FindPlayerObject(ulong clientId)
        {
            if (NetworkManager == null)
                return null;

            if (IsServer)
            {
                return NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client)
                    ? client.PlayerObject
                    : null;
            }

            if (NetworkManager.SpawnManager == null)
                return null;

            foreach (NetworkObject spawned in NetworkManager.SpawnManager.SpawnedObjectsList)
            {
                if (spawned != null && spawned.IsPlayerObject && spawned.OwnerClientId == clientId)
                    return spawned;
            }

            return null;
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
