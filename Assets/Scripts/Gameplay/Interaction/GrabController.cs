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
        private FurnitureRotateMode _rotateMode = FurnitureRotateMode.Rotate;
        private ulong _corpseInteractionId = NoObjectId;
        private ulong _serverHeldCorpseId = NoObjectId;
        private float _corpsePressAt;
        private float _corpseHoldThreshold;
        private Vector3 _corpsePressOrigin;
        private Vector3 _corpsePressDirection;
        private bool _corpseCarryRequested;

        public ulong HeldObjectId => _heldObjectId.Value;
        public bool IsHolding => _heldObjectId.Value != NoObjectId;
        public bool IsTestHoldLatched => _testHoldLatched;

        /// <summary>로컬 소유자의 휠 조작 모드. 휠 클릭으로 바뀌고, 잡기가 끝나면 회전으로 돌아간다.</summary>
        public FurnitureRotateMode RotateMode => _rotateMode;

        private ILocalPlayerContext _localPlayer;
        private SanityNetworkState _sanity;
        private PlayerCleaningController _cleaning;
        private PlayerFurnitureDriverController _driver;

        public override void OnNetworkSpawn()
        {
            _heldObjectId.OnValueChanged += HandleHeldObjectChanged;

            // 서버 인스턴스에서도 필요하다(원격 플레이어의 사망 시 강제 해제) — Owner 분기 밖에서 구한다.
            _sanity = GetComponent<SanityNetworkState>();
            _cleaning = GetComponent<PlayerCleaningController>();
            _driver = GetComponent<PlayerFurnitureDriverController>();
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
            CancelCorpseInteraction();
            ServerReleaseCorpseCarry();

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
            if (alive)
                return;

            if (IsOwner)
                CancelCorpseInteraction();
            if (!IsServer)
                return;

            ServerReleaseCorpseCarry();

            if (IsHolding && TryResolveTarget(_heldObjectId.Value, out FurnitureGrabTarget target))
                target.ServerForceRelease(OwnerClientId);
        }

        private void Update()
        {
            if (!IsOwner || _input == null || _camera == null)
                return;

            if ((_cleaning != null && _cleaning.IsMopEquipped)
                || (_driver != null && _driver.IsDriverEquipped))
            {
                CancelCorpseInteraction();
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard[_testHoldToggleKey].wasPressedThisFrame)
                ToggleTestHold();

            if (_input.AttackPressedThisFrame)
            {
                if (!TryBeginCorpseInteraction())
                    BeginGrab();
            }

            if (_corpseInteractionId != NoObjectId)
                TickCorpseInteraction();

            if (_input.AttackReleasedThisFrame && !_testHoldLatched
                && _corpseInteractionId == NoObjectId)
                ReleaseGrab();

            if (IsHolding && Time.unscaledTime >= _nextAimSendAt)
            {
                _nextAimSendAt = Time.unscaledTime + _aimSendInterval;
                UpdateAimRpc(
                    _heldObjectId.Value,
                    _camera.transform.position,
                    _camera.transform.forward);
            }

            if (IsHolding)
                HandleHeldRotationInput();

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

        private void HandleHeldRotationInput()
        {
            if (_input.FurnitureRotateModePressedThisFrame)
            {
                _rotateMode = _rotateMode == FurnitureRotateMode.Rotate
                    ? FurnitureRotateMode.Tilt
                    : FurnitureRotateMode.Rotate;
            }

            float scroll = _input.FurnitureRotateScroll;
            if (Mathf.Approximately(scroll, 0f)
                || !TryResolveTarget(_heldObjectId.Value, out FurnitureGrabTarget target)
                || target.State != FurnitureState.Held)
            {
                return;
            }

            // 휠 값의 크기는 플랫폼·설정마다 달라 부호만 쓴다 — 한 프레임에 최대 한 칸.
            RequestRotateHeldRpc(_heldObjectId.Value, scroll > 0f ? 1 : -1, _rotateMode);
        }

        public bool TryGetHeldTarget(out FurnitureGrabTarget target)
        {
            return TryResolveTarget(_heldObjectId.Value, out target);
        }

        private void BeginGrab()
        {
            if (IsHolding || _requestedObjectId != NoObjectId
                || _corpseInteractionId != NoObjectId || _targeter == null)
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

        private bool TryBeginCorpseInteraction()
        {
            if (IsHolding || _requestedObjectId != NoObjectId || _settings == null
                || !Physics.Raycast(
                    _camera.transform.position, _camera.transform.forward,
                    out RaycastHit hit, _settings.MaxTargetDistance,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return false;

            Debug.Log($"[CorpseDbg] 좌클릭 조준 대상 {hit.collider.name} (거리 {hit.distance:F2}m, 시체 {PlayerVisuals.TryGetCorpseOwner(hit.collider, out _)})", this);
            if (!PlayerVisuals.TryGetCorpseOwner(hit.collider, out PlayerVisuals corpse))
                return false;

            _corpseInteractionId = corpse.NetworkObjectId;
            _corpsePressAt = Time.unscaledTime;
            _corpseHoldThreshold = corpse.CarryHoldSeconds;
            _corpsePressOrigin = _camera.transform.position;
            _corpsePressDirection = _camera.transform.forward;
            _corpseCarryRequested = false;
            return true;
        }

        private void TickCorpseInteraction()
        {
            ulong corpseId = _corpseInteractionId;
            if (_input.AttackReleasedThisFrame)
            {
                if (_corpseCarryRequested)
                    RequestCorpseReleaseRpc(corpseId);
                else if (Time.unscaledTime - _corpsePressAt < _corpseHoldThreshold)
                {
                    Debug.Log($"[CorpseDbg] 밀기 요청 전송 (누른 시간 {Time.unscaledTime - _corpsePressAt:F2}s)", this);
                    RequestCorpsePushRpc(corpseId, _corpsePressOrigin, _corpsePressDirection);
                }

                ClearCorpseInteraction();
                return;
            }

            if (!_corpseCarryRequested
                && Time.unscaledTime - _corpsePressAt >= _corpseHoldThreshold)
            {
                _corpseCarryRequested = true;
                RequestCorpseCarryRpc(corpseId,
                    _camera.transform.position, _camera.transform.forward);
            }

            if (_corpseCarryRequested && Time.unscaledTime >= _nextAimSendAt)
            {
                _nextAimSendAt = Time.unscaledTime + _aimSendInterval;
                RequestCorpseAimRpc(corpseId,
                    _camera.transform.position, _camera.transform.forward);
            }
        }

        private void CancelCorpseInteraction()
        {
            if (_corpseInteractionId != NoObjectId && _corpseCarryRequested && IsSpawned)
                RequestCorpseReleaseRpc(_corpseInteractionId);
            ClearCorpseInteraction();
        }

        private void ClearCorpseInteraction()
        {
            _corpseInteractionId = NoObjectId;
            _corpseCarryRequested = false;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestCorpsePushRpc(
            ulong corpseNetworkId, Vector3 origin, Vector3 direction,
            RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId
                || _sanity == null || !_sanity.HasSanity
                || _settings == null || !IsValidAim(origin, direction)
                || Vector3.Distance(transform.position, origin) > 3f
                || !TryResolveCorpse(corpseNetworkId, out PlayerVisuals corpse))
                return;

            if (Vector3.Distance(transform.position, corpse.CorpsePosition)
                    > _settings.MaxTargetDistance * 1.2f)
                return;

            if (Physics.Raycast(origin, direction, out RaycastHit hit,
                    _settings.MaxTargetDistance * 1.2f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && PlayerVisuals.TryGetCorpseOwner(hit.collider, out PlayerVisuals owner)
                && owner == corpse)
            {
                Debug.Log($"[CorpseDbg] 서버 밀기 결과 {corpse.ServerTryPush(hit.point, direction.normalized)}", this);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestCorpseCarryRpc(
            ulong corpseNetworkId, Vector3 origin, Vector3 direction,
            RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId
                || _sanity == null || !_sanity.HasSanity
                || _settings == null || !IsValidAim(origin, direction)
                || Vector3.Distance(transform.position, origin) > 3f
                || IsHolding || _serverHeldCorpseId != NoObjectId
                || !TryResolveCorpse(corpseNetworkId, out PlayerVisuals corpse)
                || Vector3.Distance(transform.position, corpse.CorpsePosition)
                    > _settings.MaxTargetDistance * 1.2f)
                return;

            if (!Physics.Raycast(origin, direction, out RaycastHit hit,
                    _settings.MaxTargetDistance * 1.2f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                || !PlayerVisuals.TryGetCorpseOwner(hit.collider, out PlayerVisuals owner)
                || owner != corpse)
                return;

            Vector3 target = origin + direction.normalized * corpse.CarryDistance;
            if (corpse.ServerAddCarrier(OwnerClientId, target))
                _serverHeldCorpseId = corpseNetworkId;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestCorpseAimRpc(
            ulong corpseNetworkId, Vector3 origin, Vector3 direction,
            RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId
                || _serverHeldCorpseId != corpseNetworkId
                || _sanity == null || !_sanity.HasSanity
                || !IsValidAim(origin, direction)
                || Vector3.Distance(transform.position, origin) > 3f
                || !TryResolveCorpse(corpseNetworkId, out PlayerVisuals corpse))
                return;

            corpse.ServerUpdateCarrier(OwnerClientId,
                origin + direction.normalized * corpse.CarryDistance);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestCorpseReleaseRpc(
            ulong corpseNetworkId, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId == OwnerClientId
                && _serverHeldCorpseId == corpseNetworkId)
                ServerReleaseCorpseCarry();
        }

        private void ServerReleaseCorpseCarry()
        {
            if (!IsServer || _serverHeldCorpseId == NoObjectId)
                return;

            if (TryResolveCorpse(_serverHeldCorpseId, out PlayerVisuals corpse))
                corpse.ServerRemoveCarrier(OwnerClientId);
            _serverHeldCorpseId = NoObjectId;
        }

        private bool TryResolveCorpse(ulong corpseNetworkId, out PlayerVisuals corpse)
        {
            corpse = null;
            return NetworkManager != null
                && NetworkManager.SpawnManager != null
                && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(
                    corpseNetworkId, out NetworkObject networkObject)
                && networkObject.TryGetComponent(out corpse)
                && corpse.HasCorpse;
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
            CancelCorpseInteraction();
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
                || (_cleaning != null && _cleaning.ServerHasMop)
                || (_driver != null && _driver.ServerHasDriver)
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

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestRotateHeldRpc(
            ulong objectId,
            int steps,
            FurnitureRotateMode mode,
            RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (sender != OwnerClientId
                || (_sanity != null && !_sanity.HasSanity)
                || objectId != _heldObjectId.Value
                || !TryResolveTarget(objectId, out FurnitureGrabTarget target))
            {
                return;
            }

            // 홀더 여부·Held 상태·한 칸 범위·모드 값은 가구 쪽에서 다시 검증한다.
            target.ServerRotateHeld(sender, steps, mode);
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
            if (current == NoObjectId)
                _rotateMode = FurnitureRotateMode.Rotate;

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
