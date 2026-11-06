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

        /// <summary>서버가 받아들이는 조준 원점(카메라)과 플레이어 루트 사이의 최대 거리(m). 시선 원점을 속여 사거리를 늘리는 것을 막는다.</summary>
        internal const float AimOriginMaxOffset = 3f;

        /// <summary>서버 잡기 사거리의 지연 보정 여유 배수 — 클라이언트 타게팅 사거리(<c>MaxTargetDistance</c>)에 곱한다.</summary>
        internal const float GrabReachSlack = 1.2f;

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
        private ulong _serverHeldCorpseId = NoObjectId;
        private CorpseInteraction _corpseInteraction;
        private PlayerShoveInteraction _shoveInteraction;
        private ulong _serverShoveTargetId = NoObjectId;
        private float _serverShoveBeganAt;
        private float _serverNextShoveAt;

        public ulong HeldObjectId => _heldObjectId.Value;

        /// <summary>시체(PlayerVisuals)가 가구와 같은 잡기·던지기 값을 쓰도록 노출한다.</summary>
        public FurnitureThrowSettings Settings => _settings;
        public bool IsHolding => _heldObjectId.Value != NoObjectId;
        public bool IsTestHoldLatched => _testHoldLatched;

        /// <summary>로컬 소유자가 다른 플레이어를 밀치려고 차징 중인가.</summary>
        public bool IsChargingShove => _shoveInteraction != null && _shoveInteraction.IsActive;

        /// <summary>지금 놓으면 나갈 밀치기 힘 비율(0~1). 차징 중이 아니면 0. 차지 게이지 UI가 읽는다.</summary>
        public float ShovePower => _shoveInteraction != null ? _shoveInteraction.Power : 0f;

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
                _corpseInteraction = new CorpseInteraction(_camera, _settings, OwnerClientId,
                    (id, origin, direction) => RequestCorpseGrabRpc(id, origin, direction),
                    (id, origin, direction) => RequestCorpseAimRpc(id, origin, direction),
                    (id, direction, launch) => RequestCorpseReleaseRpc(id, direction, launch));
                _shoveInteraction = new PlayerShoveInteraction(transform, NetworkObject, _camera, _settings,
                    id => BeginShoveRpc(id), (id, direction) => ReleaseShoveRpc(id, direction));
                _localPlayer = Services.Get<ILocalPlayerContext>();
                _localPlayer.Register(this);
            }
        }

        public override void OnNetworkDespawn()
        {
            _heldObjectId.OnValueChanged -= HandleHeldObjectChanged;
            _testHoldLatched = false;
            _corpseInteraction?.Cancel(IsSpawned);
            _corpseInteraction?.UpdateHover(false);
            _shoveInteraction?.Cancel();
            _corpseInteraction = null;
            _shoveInteraction = null;
            _serverShoveTargetId = NoObjectId;
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
            {
                _corpseInteraction?.Cancel(IsSpawned);
                _shoveInteraction?.Cancel();
            }
            if (!IsServer)
                return;

            _serverShoveTargetId = NoObjectId;
            ServerReleaseCorpseCarry();

            if (IsHolding && TryResolveTarget(_heldObjectId.Value, out FurnitureGrabTarget target))
                target.ServerForceRelease(OwnerClientId);
        }

        private void Update()
        {
            if (!IsOwner || _input == null || _camera == null
                || _corpseInteraction == null || _shoveInteraction == null)
                return;

            if ((_cleaning != null && _cleaning.IsMopEquipped)
                || (_driver != null && _driver.IsDriverEquipped))
            {
                _corpseInteraction.Cancel(IsSpawned);
                _corpseInteraction.UpdateHover(false);
                _shoveInteraction.Cancel();
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard[_testHoldToggleKey].wasPressedThisFrame)
                ToggleTestHold();

            bool furnitureHeld = IsHolding || _requestedObjectId != NoObjectId;
            _corpseInteraction.UpdateHover(
                !furnitureHeld && !IsChargingShove && (_sanity == null || _sanity.HasSanity));

            if (_input.AttackPressedThisFrame)
            {
                bool furnitureBusy = furnitureHeld;
                if (!_corpseInteraction.TryBegin(furnitureBusy)
                    && !_shoveInteraction.TryBegin(furnitureBusy || _corpseInteraction.IsActive,
                        _sanity == null || _sanity.HasSanity))
                    BeginGrab();
            }

            if (_corpseInteraction.IsActive)
                _corpseInteraction.Tick(_input.AttackReleasedThisFrame, _aimSendInterval, ref _nextAimSendAt);

            if (_shoveInteraction.IsActive && _input.AttackReleasedThisFrame)
                _shoveInteraction.Release(IsSpawned);

            if (_input.AttackReleasedThisFrame && !_testHoldLatched
                && !_corpseInteraction.IsActive)
                ReleaseGrab();

            if (IsHolding && Time.unscaledTime >= _nextAimSendAt)
            {
                _nextAimSendAt = Time.unscaledTime + _aimSendInterval;
                UpdateAimRpc(
                    _heldObjectId.Value,
                    _camera.transform.position,
                    _camera.transform.forward);
            }

            bool corpseHeld = _corpseInteraction.IsActive
                && _corpseInteraction.Target.GrabState == FurnitureState.Held
                && _corpseInteraction.Target.ContainsHolder(OwnerClientId);
            if (IsHolding || corpseHeld)
                HandleHeldRotationInput(corpseHeld);
            else if (!_corpseInteraction.IsActive)
                _rotateMode = FurnitureRotateMode.Rotate;

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

        private void HandleHeldRotationInput(bool corpseHeld)
        {
            if (_input.FurnitureRotateModePressedThisFrame)
            {
                _rotateMode = _rotateMode == FurnitureRotateMode.Rotate
                    ? FurnitureRotateMode.Tilt
                    : FurnitureRotateMode.Rotate;
            }

            float scroll = _input.FurnitureRotateScroll;
            if (Mathf.Approximately(scroll, 0f))
                return;

            // 휠 값의 크기는 플랫폼·설정마다 달라 부호만 쓴다 — 한 프레임에 최대 한 칸.
            int steps = scroll > 0f ? 1 : -1;
            if (corpseHeld)
            {
                RequestCorpseRotateRpc(_corpseInteraction.Target.NetworkObjectId, steps, _rotateMode);
                return;
            }

            if (TryResolveTarget(_heldObjectId.Value, out FurnitureGrabTarget target)
                && target.State == FurnitureState.Held)
            {
                RequestRotateHeldRpc(_heldObjectId.Value, steps, _rotateMode);
            }
        }

        public bool TryGetHeldTarget(out FurnitureGrabTarget target)
        {
            return TryResolveTarget(_heldObjectId.Value, out target);
        }

        /// <summary>로컬 소유자가 지금 잡고 있는(서버가 홀더로 받아들인) 시체. 차지 게이지가 읽는다.</summary>
        public bool TryGetHeldCorpse(out PlayerVisuals corpse)
        {
            corpse = _corpseInteraction != null ? _corpseInteraction.Target : null;
            return corpse != null && corpse.ContainsHolder(OwnerClientId);
        }

        private void BeginGrab()
        {
            if (IsHolding || _requestedObjectId != NoObjectId
                || (_corpseInteraction != null && _corpseInteraction.IsActive) || IsChargingShove
                || _targeter == null)
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

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void BeginShoveRpc(ulong targetId, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId
                || (_sanity != null && !_sanity.HasSanity)
                || _settings == null)
                return;

            _serverShoveTargetId = targetId;
            _serverShoveBeganAt = Time.unscaledTime;
        }

        /// <summary>
        /// 서버가 밀치기를 검증하고 대상 소유자에게 넉백을 보낸다. 이동은 소유자 권위(ADR-0008)라 서버는 속도만 정해 알리고
        /// 실제 이동은 대상의 <see cref="PlayerMotor"/> 가 적용한다. 차지는 클라이언트가 보내지 않고 서버가 시작 시각으로 잰다.
        /// </summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void ReleaseShoveRpc(ulong targetId, Vector3 aimDirection, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId
                || (_sanity != null && !_sanity.HasSanity)
                || _settings == null
                || !IsFinite(aimDirection)
                || targetId == NoObjectId || targetId != _serverShoveTargetId)
                return;

            _serverShoveTargetId = NoObjectId;
            float now = Time.unscaledTime;
            if (now < _serverNextShoveAt
                || targetId == NetworkObjectId
                || NetworkManager == null || NetworkManager.SpawnManager == null
                || !NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(targetId, out NetworkObject targetObject)
                || !targetObject.TryGetComponent(out PlayerMotor targetMotor)
                || !targetObject.TryGetComponent(out SanityNetworkState targetSanity) || !targetSanity.HasSanity
                || (targetObject.TryGetComponent(out MoleBurrowController burrow) && burrow.IsBurrowed))
                return;

            Vector3 toTarget = targetMotor.transform.position - transform.position;
            Vector3 flat = new(toTarget.x, 0f, toTarget.z);
            if (flat.magnitude > PlayerShoveRules.ServerReach(_settings.ShoveMaxDistance)
                || Mathf.Abs(toTarget.y) > 2.5f)
                return;

            Vector3 direction = PlayerShoveRules.ResolveDirection(aimDirection, toTarget);
            if (direction == Vector3.zero)
                return;

            float charge = Mathf.Clamp01((now - _serverShoveBeganAt) / _settings.ChargeTime);
            _serverNextShoveAt = now + _settings.ShoveCooldown;
            targetMotor.ServerKnockback(PlayerShoveRules.KnockbackVelocity(
                direction, _settings.ShoveForceRatio(charge),
                _settings.ShoveMaxSpeed, _settings.ShoveUpwardSpeed));
        }

        /// <summary>
        /// 시체를 가구처럼 잡는다 — 첫 홀더는 투척 준비(차징), 둘째 홀더가 붙으면 2인 운반.
        /// 서버가 생존·거리·가시선·시체 상태를 다시 검사한다.
        /// </summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestCorpseGrabRpc(
            ulong corpseNetworkId, Vector3 origin, Vector3 direction,
            RpcParams rpcParams = default)
        {
            ServerForgetStaleCorpse();
            if (rpcParams.Receive.SenderClientId != OwnerClientId
                || _sanity == null || !_sanity.HasSanity
                || (_cleaning != null && _cleaning.ServerHasMop)
                || (_driver != null && _driver.ServerHasDriver)
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

            if (corpse.ServerTryAddHolder(OwnerClientId, origin, direction))
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

            corpse.ServerUpdateAim(OwnerClientId, origin, direction);
        }

        /// <summary>
        /// 시체에서 손을 뗀다. <paramref name="launch"/>면 가구처럼 차지만큼 던지고,
        /// 아니면(일시정지·장비 장착 취소) 발사 없이 그 홀더만 푼다.
        /// </summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestCorpseReleaseRpc(
            ulong corpseNetworkId, Vector3 aimDirection, bool launch,
            RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId
                || _serverHeldCorpseId != corpseNetworkId
                || !IsFinite(aimDirection))
                return;

            if (TryResolveCorpse(corpseNetworkId, out PlayerVisuals corpse))
                corpse.ServerRelease(OwnerClientId, aimDirection, !launch || _sanity == null || !_sanity.HasSanity);
            _serverHeldCorpseId = NoObjectId;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestCorpseRotateRpc(
            ulong corpseNetworkId, int steps, FurnitureRotateMode mode,
            RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId
                || _serverHeldCorpseId != corpseNetworkId
                || _sanity == null || !_sanity.HasSanity
                || !TryResolveCorpse(corpseNetworkId, out PlayerVisuals corpse))
                return;

            // 홀더 여부·Held 상태·한 칸 범위·모드 값은 시체 쪽에서 다시 검증한다.
            corpse.ServerRotateHeld(OwnerClientId, steps, mode);
        }

        /// <summary>시체가 홀더를 풀었을 때(발사·같이 내려놓기·강제 해제) 서버가 이쪽 기록을 지운다.</summary>
        public void ServerClearHeldCorpse(ulong corpseNetworkId)
        {
            if (IsServer && _serverHeldCorpseId == corpseNetworkId)
                _serverHeldCorpseId = NoObjectId;
        }

        private void ServerReleaseCorpseCarry()
        {
            if (!IsServer || _serverHeldCorpseId == NoObjectId)
                return;

            if (TryResolveCorpse(_serverHeldCorpseId, out PlayerVisuals corpse))
                corpse.ServerForceRelease(OwnerClientId);
            _serverHeldCorpseId = NoObjectId;
        }

        /// <summary>시체가 사라졌거나 더는 내가 홀더가 아니면 남은 기록을 지워 다음 잡기를 막지 않게 한다.</summary>
        private void ServerForgetStaleCorpse()
        {
            if (_serverHeldCorpseId != NoObjectId
                && (!TryResolveCorpse(_serverHeldCorpseId, out PlayerVisuals corpse)
                    || !corpse.ContainsHolder(OwnerClientId)))
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
            _corpseInteraction?.Cancel(IsSpawned);
            _shoveInteraction?.Cancel();
            ReleaseGrab();
        }

        private void ReleaseGrab()
        {
            ulong objectId = IsHolding ? _heldObjectId.Value : _requestedObjectId;
            if (objectId == NoObjectId)
                return;

            RequestReleaseRpc(objectId, _camera.transform.forward);
            _requestedObjectId = NoObjectId;

            // 서버가 잡기를 거부했다면 '잡힘' 복제가 오지 않아 HandleHeldObjectChanged 가 조준 고정을 풀어 주지 않는다.
            // 0.5초 타임아웃은 아직 요청이 남아 있을 때만 도므로, 그 전에 떼면(탭) 타게터가 거부된 가구에 계속 묶여
            // 다른 가구를 겨눠도 외곽선·잡기가 먹지 않았다. 아직 홀더가 아니면 여기서 푼다 —
            // 요청이 받아들여졌다면 이어서 오는 복제가 다시 걸었다가 해제 복제가 푼다.
            if (!IsHolding)
                _targeter?.SetHoldTarget(null);
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
                || !IsWithinGrabReach(
                    Vector3.Distance(transform.position, aimOrigin),
                    target.DistanceToSurface(aimOrigin),
                    _settings.MaxTargetDistance))
            {
                return;
            }

            if (target.ServerTryAddHolder(sender, aimOrigin, aimDirection))
                _heldObjectId.Value = objectId;
        }

        /// <summary>
        /// 서버 잡기 사거리 — 클라이언트 타게팅(<see cref="FurnitureTargeter"/>)과 같은 기준으로 잰다:
        /// 조준 원점에서 가구 <b>표면</b>까지가 <c>maxTargetDistance × 1.2</c> 이내여야 한다.
        /// 예전에는 플레이어 발~가구 피벗 거리를 쟀다 — 표면은 2m 안인데 피벗이 멀리 있는 큰 가구(침대·소파·리프트)는
        /// 서 있는 자리에 따라 서버가 조용히 거부했다. 조준 원점은 클라이언트가 보낸 값이므로 플레이어에서 멀리 떨어진
        /// 값은 거른다(<see cref="AimOriginMaxOffset"/>).
        /// </summary>
        internal static bool IsWithinGrabReach(float aimOriginToPlayer, float aimOriginToSurface, float maxTargetDistance)
        {
            return aimOriginToPlayer <= AimOriginMaxOffset
                && aimOriginToSurface <= maxTargetDistance * GrabReachSlack;
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
