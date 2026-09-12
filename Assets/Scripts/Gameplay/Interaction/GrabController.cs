using GhostHunter.Core;
using GhostHunter.Gameplay.Furniture;
using GhostHunter.Gameplay.Player;
using GhostHunter.Gameplay.Sanity;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GhostHunter.Gameplay.Interaction
{
    /// <summary>
    /// 로컬 입력을 서버 의도로 변환한다. 서버는 거리, 소유권, 오브젝트 상태와 벡터 유효성을
    /// 다시 검사하고 가구 강체에는 서버 컴포넌트만 힘을 가한다.
    /// </summary>
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class GrabController : NetworkBehaviour
    {
        public const ulong NoObjectId = ulong.MaxValue;

        [SerializeField] private PlayerInputReader _input;
        [SerializeField] private FurnitureTargeter _targeter;
        [SerializeField] private Camera _camera;
        [SerializeField] private FurnitureThrowSettings _settings;
        [SerializeField, Min(0.02f)] private float _aimSendInterval = 0.05f;
        [SerializeField] private Key _testHoldToggleKey = Key.F12;

        private readonly NetworkVariable<ulong> _heldObjectId = new(NoObjectId);
        private ulong _requestedObjectId = NoObjectId;
        private float _requestSentAt;
        private float _nextAimSendAt;
        private bool _testHoldLatched;

        public ulong HeldObjectId => _heldObjectId.Value;
        public bool IsHolding => _heldObjectId.Value != NoObjectId;
        public bool IsTestHoldLatched => _testHoldLatched;

        private ILocalPlayerContext _localPlayer;
        private SanityNetworkState _sanity;

        public override void OnNetworkSpawn()
        {
            _heldObjectId.OnValueChanged += HandleHeldObjectChanged;

            // 서버 인스턴스에서도 필요하다(원격 플레이어의 사망 시 강제 해제) — Owner 분기 밖에서 구한다.
            _sanity = GetComponent<SanityNetworkState>();
            if (_sanity != null)
                _sanity.AliveStateChanged += HandleAliveStateChanged;

            if (IsOwner)
            {
                _localPlayer = Services.Get<ILocalPlayerContext>();
                _localPlayer.Register(this);
            }
        }

        public override void OnNetworkDespawn()
        {
            _heldObjectId.OnValueChanged -= HandleHeldObjectChanged;
            _testHoldLatched = false;

            if (_sanity != null)
                _sanity.AliveStateChanged -= HandleAliveStateChanged;
            _sanity = null;

            _localPlayer?.Unregister(this);
            _localPlayer = null;
        }

        /// <summary>
        /// 사망 시 들고 있던 가구를 발사 없이 놓는다(관전 기획서 SP-2, 사용자 확정 2026-09-12).
        /// 서버에서만 실행하며, <see cref="FurnitureGrabTarget.ServerForceRelease"/>가 이미
        /// <see cref="FurnitureGrabTarget.NotifyPlayerReleased"/>로 <see cref="_heldObjectId"/>를
        /// 정리해 준다.
        /// </summary>
        private void HandleAliveStateChanged(bool alive)
        {
            if (alive || !IsServer || !IsHolding)
                return;

            if (TryResolveTarget(_heldObjectId.Value, out FurnitureGrabTarget target))
                target.ServerForceRelease(OwnerClientId);
        }

        private void Update()
        {
            if (!IsOwner || _input == null || _camera == null)
                return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard[_testHoldToggleKey].wasPressedThisFrame)
                ToggleTestHold();

            if (_input.AttackPressedThisFrame)
                BeginGrab();

            if (_input.AttackReleasedThisFrame && !_testHoldLatched)
                ReleaseGrab();

            if (IsHolding && Time.unscaledTime >= _nextAimSendAt)
            {
                _nextAimSendAt = Time.unscaledTime + _aimSendInterval;
                UpdateAimRpc(
                    _heldObjectId.Value,
                    _camera.transform.position,
                    _camera.transform.forward);
            }

            if (!IsHolding
                && _requestedObjectId != NoObjectId
                && Time.unscaledTime - _requestSentAt > 0.5f)
            {
                _requestedObjectId = NoObjectId;
                _targeter?.SetHoldTarget(null);
            }
        }

        private void ToggleTestHold()
        {
            _testHoldLatched = !_testHoldLatched;

            if (_testHoldLatched)
            {
                if (!IsHolding && _requestedObjectId == NoObjectId)
                    BeginGrab();
            }
            else
            {
                ReleaseGrab();
            }

            Debug.Log(
                $"[GrabController] F12 입력 고정: {(_testHoldLatched ? "ON" : "OFF")}",
                this);
        }

        public bool TryGetHeldTarget(out FurnitureGrabTarget target)
        {
            return TryResolveTarget(_heldObjectId.Value, out target);
        }

        private void BeginGrab()
        {
            if (IsHolding || _requestedObjectId != NoObjectId || _targeter == null)
                return;

            FurnitureGrabTarget target = _targeter.CurrentTarget;
            if (target == null || !target.CanGrab(OwnerClientId))
                return;

            _requestedObjectId = target.NetworkObjectId;
            _requestSentAt = Time.unscaledTime;
            _targeter.SetHoldTarget(target);

            RequestGrabRpc(
                target.NetworkObjectId,
                _camera.transform.position,
                _camera.transform.forward);
        }

        /// <summary>
        /// 입력과 무관하게 지금 잡고 있는(또는 요청 중인) 가구를 놓는다.
        /// 일시정지 메뉴가 입력을 잠그기 <b>전에</b> 부른다 — 잠근 뒤에는
        /// <c>AttackReleasedThisFrame</c> 이 영영 오지 않아 가구가 계속 떠 있는다
        /// → docs/architecture/pause-menu.md §6.5 (PM-15).
        /// </summary>
        public void ForceRelease()
        {
            _testHoldLatched = false;
            ReleaseGrab();
        }

        private void ReleaseGrab()
        {
            ulong objectId = IsHolding ? _heldObjectId.Value : _requestedObjectId;
            if (objectId == NoObjectId)
                return;

            RequestReleaseRpc(objectId, _camera.transform.forward);
            _requestedObjectId = NoObjectId;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestGrabRpc(
            ulong objectId,
            Vector3 aimOrigin,
            Vector3 aimDirection,
            RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (sender != OwnerClientId
                || (_sanity != null && !_sanity.HasSanity)
                || _heldObjectId.Value != NoObjectId
                || _settings == null
                || !TryResolveTarget(objectId, out FurnitureGrabTarget target)
                || !IsValidAim(aimOrigin, aimDirection)
                || Vector3.Distance(transform.position, target.transform.position)
                    > _settings.MaxTargetDistance * 1.2f)
            {
                return;
            }

            if (target.ServerTryAddHolder(sender, aimOrigin, aimDirection))
                _heldObjectId.Value = objectId;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void UpdateAimRpc(
            ulong objectId,
            Vector3 aimOrigin,
            Vector3 aimDirection,
            RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (sender != OwnerClientId
                || (_sanity != null && !_sanity.HasSanity)
                || objectId != _heldObjectId.Value
                || !IsValidAim(aimOrigin, aimDirection)
                || Vector3.Distance(transform.position, aimOrigin) > 3f
                || !TryResolveTarget(objectId, out FurnitureGrabTarget target)
                || !target.ContainsHolder(sender))
            {
                return;
            }

            target.ServerUpdateAim(sender, aimOrigin, aimDirection);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestReleaseRpc(
            ulong objectId,
            Vector3 aimDirection,
            RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (sender != OwnerClientId
                || (_sanity != null && !_sanity.HasSanity)
                || !IsFinite(aimDirection)
                || !TryResolveTarget(objectId, out FurnitureGrabTarget target)
                || !target.ContainsHolder(sender))
            {
                return;
            }

            target.ServerRelease(sender, aimDirection.normalized, false);
        }

        public void ServerClearHeld(ulong objectId)
        {
            if (IsServer && _heldObjectId.Value == objectId)
                _heldObjectId.Value = NoObjectId;
        }

        private void HandleHeldObjectChanged(ulong previous, ulong current)
        {
            if (!IsOwner || _targeter == null)
                return;

            _requestedObjectId = NoObjectId;

            if (current != NoObjectId && TryResolveTarget(current, out FurnitureGrabTarget target))
                _targeter.SetHoldTarget(target);
            else
                _targeter.SetHoldTarget(null);
        }

        private bool TryResolveTarget(ulong objectId, out FurnitureGrabTarget target)
        {
            target = null;
            if (objectId == NoObjectId
                || NetworkManager == null
                || NetworkManager.SpawnManager == null
                || !NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(
                    objectId,
                    out NetworkObject networkObject))
            {
                return false;
            }

            return networkObject.TryGetComponent(out target);
        }

        private static bool IsValidAim(Vector3 origin, Vector3 direction)
        {
            return IsFinite(origin) && IsFinite(direction) && direction.sqrMagnitude > 0.25f;
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x)
                && float.IsFinite(value.y)
                && float.IsFinite(value.z);
        }
    }
}
