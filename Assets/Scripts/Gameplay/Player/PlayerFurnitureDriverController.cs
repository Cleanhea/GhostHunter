using System.Collections.Generic;
using GhostHunter.Core;
using GhostHunter.Gameplay.FurnitureDriver;
using GhostHunter.Gameplay.Interaction;
using GhostHunter.Gameplay.Sanity;
using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Gameplay.Player
{
    public enum FurnitureDriverActionKind
    {
        None,
        Disassemble,
        Assemble,
    }

    /// <summary>
    /// 가구용 멀티 드라이버 장착·분해·조립을 담당한다(기획서 furniture-multidriver-system.md).
    ///
    /// <para><b>장착</b>은 <see cref="PlayerCleaningController"/>의 EquipSlot → RequestEquipRpc →
    /// ConfirmEquipRpc 경로를 그대로 따른다(대걸레 선례).</para>
    ///
    /// <para><b>행동 시간·중단</b>은 <see cref="MoleBurrowController"/>가 확립한 "소유자가 로컬
    /// 타이머를 진행하고, 서버는 완료 시점에만 재검증 후 적용" 신뢰 모델(이동 권위 예외의 연장,
    /// ADR-0008과 같은 자리)을 따르되 방향이 반대다 — 굴착은 시전 중 이동을 <b>잠그지만</b>,
    /// 이 기능은 이동하면 <b>취소</b>한다(MD-11, 2026-09-12 사용자 확정: Move 액션 값 ≠ 0).
    /// 취소 조건 중 "귀신에게 공격당함"(MD-12, 사용자 확정: 어택이 실제로 성립했을 때)은 이
    /// 프로토타입에서 어택 성립 = 즉시 사망(<c>GhostPrototypeController.TryCatch</c>가
    /// <c>SanityNetworkState.ServerMarkDead</c>를 부른다)이므로, 별도 이벤트 없이
    /// 매 틱의 생존 게이팅(<see cref="CanUseGate"/>)이 곧 그 취소 조건이다.</para>
    ///
    /// <para>행동은 우클릭을 누른 순간 시작하고, <see cref="PlayerInputReader.UseDriverHeld"/>가 행동
    /// 시간 끝까지 유지돼야 완료된다 — 떼는 것도 이동과 같은 취소 트리거다(기획서 §3.2 1.1).
    /// 중단 기록(<see cref="ActionCancelSerial"/>)은 HUD 실패 연출(§6.1)이 읽는다.</para>
    ///
    /// <para><b>서버 재검증은 완료 시점에만 일어난다.</b> 시작 요청 RPC가 없다 — 굴착과 같은
    /// 신뢰 경계다. 완료 RPC에서 발신자·장착·생존·거리·대상 유효성을 전부 다시 확인한다.</para>
    /// </summary>
    [DefaultExecutionOrder(90)]
    [DisallowMultipleComponent]
    public sealed class PlayerFurnitureDriverController : NetworkBehaviour
    {
        [SerializeField] private PlayerInputReader _input;
        [SerializeField] private Camera _camera;
        [SerializeField] private QuickSlotLoadout _loadout;
        [SerializeField] private FurnitureDriverSettings _settings;
        [SerializeField] private FurnitureDriverCatalog _catalog;

        private FurnitureTargeter _targeter;
        private GrabController _grab;
        private SanityNetworkState _sanity;
        private MoleBurrowController _burrow;
        private ILocalPlayerContext _localPlayer;

        private readonly NetworkVariable<int> _itemDurability = new(100,
            NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);

        private int _equippedSlot = -1;
        private int _serverSlot = -1;

        private FurnitureDriverActionKind _actionKind = FurnitureDriverActionKind.None;
        private ulong _targetObjectId;
        private float _actionTimer;
        private float _actionDurationTotal;
        private int _cancelSerial;
        private FurnitureDriverActionKind _lastCancelledAction = FurnitureDriverActionKind.None;
        private float _lastCancelledProgress;

        public int EquippedSlot => _equippedSlot;
        public bool IsDriverEquipped => IsDriverSlot(_equippedSlot);
        public bool ServerHasDriver => IsServer && IsDriverSlot(_serverSlot);
        public int ItemDurability => _itemDurability.Value;
        public FurnitureDriverActionKind CurrentAction => _actionKind;
        public float ActionSecondsRemaining => _actionTimer;
        public float ActionSecondsTotal => _actionDurationTotal;

        /// <summary>진행 중인 행동의 진행도(0~1). 행동이 없으면 0.</summary>
        public float ActionProgress => _actionDurationTotal > 0f
            ? Mathf.Clamp01(1f - _actionTimer / _actionDurationTotal)
            : 0f;

        /// <summary>진행 중이던 행동이 중단될 때마다 1씩 증가한다. 완료·서버 거절에는 변하지 않는다.</summary>
        public int ActionCancelSerial => _cancelSerial;

        /// <summary>마지막으로 중단된 행동의 종류.</summary>
        public FurnitureDriverActionKind LastCancelledAction => _lastCancelledAction;

        /// <summary>마지막으로 중단된 행동이 멈춘 시점의 진행도(0~1).</summary>
        public float LastCancelledProgress => _lastCancelledProgress;

        private void Awake()
        {
            _targeter = GetComponent<FurnitureTargeter>();
            _grab = GetComponent<GrabController>();
            _sanity = GetComponent<SanityNetworkState>();
            _burrow = GetComponent<MoleBurrowController>();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
                _itemDurability.Value = _settings != null ? _settings.StartingItemDurability : 100;
            if (IsOwner)
            {
                _localPlayer = Services.Get<ILocalPlayerContext>();
                _localPlayer.Register(this);
            }
            _equippedSlot = -1;
            _serverSlot = -1;
        }

        public override void OnNetworkDespawn()
        {
            _localPlayer?.Unregister(this);
            _localPlayer = null;
            CancelAction();
            _equippedSlot = -1;
            _serverSlot = -1;
        }

        private void Update()
        {
            if (!IsSpawned || !IsOwner || _input == null || _settings == null)
                return;

            if (_actionKind != FurnitureDriverActionKind.None)
            {
                TickAction();
                return;
            }

            if (!IsDriverEquipped || !CanUseLocally() || !_input.UseDriverPressedThisFrame)
                return;

            TryBeginAction();
        }

        /// <summary>퀵슬롯 선택을 로컬에 표시하고 서버에서 장착 가능 여부를 확인한다.</summary>
        public bool EquipSlot(int index)
        {
            if (!IsSpawned || !IsOwner || _loadout == null || _loadout.GetSlot(index) == null
                || _input == null || _input.IsGameplayInputLocked || _input.IsDeathInputLocked
                || _input.IsSkillInputLocked || !CanUseGate())
                return false;
            _equippedSlot = index;
            if (!IsDriverEquipped)
                CancelAction();
            RequestEquipRpc(index);
            return true;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestEquipRpc(int index, RpcParams rpcParams = default)
        {
            if (!IsServer || rpcParams.Receive.SenderClientId != OwnerClientId)
                return;
            if (_loadout != null && _loadout.GetSlot(index) != null && CanUseGate())
                _serverSlot = index;
            ConfirmEquipRpc(_serverSlot);
        }

        [Rpc(SendTo.Owner)]
        private void ConfirmEquipRpc(int index)
        {
            _equippedSlot = index;
            if (!IsDriverEquipped)
                CancelAction();
        }

        private void TryBeginAction()
        {
            if (_targeter != null && _targeter.CurrentTarget != null
                && _targeter.CurrentTarget.TryGetComponent(out FurnitureDriverPoolItem poolItem)
                && poolItem.IsActive
                && _catalog != null && _catalog.FindByLargeFurnitureId(poolItem.PoolKey) != null)
            {
                BeginAction(FurnitureDriverActionKind.Disassemble, poolItem.NetworkObjectId);
                return;
            }

            if (_camera == null)
                return;

            IReadOnlyList<FurnitureAssemblyZone> zones = FurnitureAssemblyZone.All;
            for (int i = 0; i < zones.Count; i++)
            {
                FurnitureAssemblyZone zone = zones[i];
                if (zone == null || zone.Silhouette != FurnitureAssemblyState.Ready)
                    continue;
                // 상자 중심을 본다 — 오브젝트 원점은 바닥이라 가까이 서면 조준이 안 된다(AimPoint 주석).
                Vector3 toZone = zone.AimPoint - _camera.transform.position;
                float distance = toZone.magnitude;
                if (distance > _settings.UseDistance || distance < 0.001f)
                    continue;
                if (Vector3.Dot(_camera.transform.forward, toZone / distance) < 0.7f)
                    continue;
                BeginAction(FurnitureDriverActionKind.Assemble, zone.NetworkObjectId);
                return;
            }
        }

        private void BeginAction(FurnitureDriverActionKind kind, ulong targetObjectId)
        {
            _actionKind = kind;
            _targetObjectId = targetObjectId;
            _actionDurationTotal = _settings.ActionSecondsFor(_itemDurability.Value);
            _actionTimer = _actionDurationTotal;
        }

        private void TickAction()
        {
            if (!CanUseLocally() || !IsTargetStillValid())
            {
                CancelAction();
                return;
            }

            // MD-11(2026-09-12 확정) — 이동 입력만 취소 트리거다. 시점 회전·웅크리기·점프는 포함하지 않는다.
            // §3.2(1.1, 2026-09-13) — 우클릭을 떼도 취소한다. 행동 시간 끝까지 누르고 있어야 완료된다.
            if (_input.Move != Vector2.zero || !_input.UseDriverHeld
                || _input.BurrowPressedThisFrame || _input.DetectPressedThisFrame)
            {
                CancelAction();
                return;
            }

            _actionTimer -= Time.deltaTime;
            if (_actionTimer > 0f)
                return;

            FurnitureDriverActionKind kind = _actionKind;
            ulong targetId = _targetObjectId;
            _actionKind = FurnitureDriverActionKind.None;
            _actionTimer = 0f;
            _actionDurationTotal = 0f;

            if (kind == FurnitureDriverActionKind.Disassemble)
                RequestDisassembleRpc(targetId);
            else if (kind == FurnitureDriverActionKind.Assemble)
                RequestAssembleRpc(targetId);
        }

        private bool IsTargetStillValid()
        {
            if (_actionKind == FurnitureDriverActionKind.Disassemble)
            {
                return TryResolve(_targetObjectId, out FurnitureDriverPoolItem item) && item.IsActive;
            }

            if (_actionKind == FurnitureDriverActionKind.Assemble)
            {
                // §4.2.1 — 조립 도중 다른 가구가 들어오거나 부품이 빠지면 실루엣이 바로 바뀐다.
                return TryResolve(_targetObjectId, out FurnitureAssemblyZone zone)
                    && zone.Silhouette == FurnitureAssemblyState.Ready;
            }

            return false;
        }

        /// <summary>진행 중인 행동을 즉시 취소한다. 행동 중이었다면 HUD 실패 연출용 중단 기록을 남긴다.</summary>
        public void CancelAction()
        {
            if (_actionKind != FurnitureDriverActionKind.None)
            {
                _lastCancelledAction = _actionKind;
                _lastCancelledProgress = ActionProgress;
                _cancelSerial++;
            }

            _actionKind = FurnitureDriverActionKind.None;
            _actionTimer = 0f;
            _actionDurationTotal = 0f;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestDisassembleRpc(ulong targetObjectId, RpcParams rpcParams = default)
        {
            if (!IsServer || rpcParams.Receive.SenderClientId != OwnerClientId || !ServerHasDriver
                || _settings == null || _catalog == null || !CanUseGate()
                || !TryResolve(targetObjectId, out FurnitureDriverPoolItem large) || !large.IsActive
                || Vector3.Distance(transform.position, large.transform.position) > _settings.UseDistance * 1.5f)
            {
                return;
            }

            FurnitureDisassemblyRecipe recipe = _catalog.FindByLargeFurnitureId(large.PoolKey);
            if (recipe == null)
                return;

            int inheritedDurability = FurnitureDurability.InheritOnDisassemble(large.Durability);
            var activated = new List<FurnitureDriverPoolItem>();
            foreach (FurniturePartRequirement requirement in recipe.Parts)
            {
                for (int i = 0; i < requirement.Count; i++)
                {
                    if (!FurnitureDriverPoolItem.TryFindInactive(requirement.PartId, out FurnitureDriverPoolItem part))
                    {
                        // 부품 풀이 모자란다 — 지금까지 활성화한 부품을 되돌리고 실패한다.
                        foreach (FurnitureDriverPoolItem rollback in activated)
                            rollback.ServerDeactivate();
                        Debug.LogWarning(
                            $"[PlayerFurnitureDriverController] 부품 풀 부족: {requirement.PartId}", this);
                        return;
                    }

                    Vector3 offset = Quaternion.Euler(0f, 360f / Mathf.Max(1, requirement.Count) * i, 0f)
                        * Vector3.forward * 0.25f;
                    Vector3 dropPosition = large.transform.position + Vector3.up * _settings.DropHeight + offset;
                    part.ServerActivate(dropPosition, large.transform.rotation, inheritedDurability);
                    activated.Add(part);
                }
            }

            large.ServerDeactivate();
            _itemDurability.Value = FurnitureDurability.ApplyItemUse(
                _itemDurability.Value, _settings.DurabilityDecreasePerUse);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestAssembleRpc(ulong zoneObjectId, RpcParams rpcParams = default)
        {
            if (!IsServer || rpcParams.Receive.SenderClientId != OwnerClientId || !ServerHasDriver
                || _settings == null || !CanUseGate()
                || !TryResolve(zoneObjectId, out FurnitureAssemblyZone zone)
                || Vector3.Distance(transform.position, zone.transform.position) > _settings.UseDistance * 1.5f)
            {
                return;
            }

            if (!zone.ServerTryAssemble(_settings.DropHeight, out _, out _))
                return;

            _itemDurability.Value = FurnitureDurability.ApplyItemUse(
                _itemDurability.Value, _settings.DurabilityDecreasePerUse);
        }

        private bool CanUseLocally() => IsDriverEquipped && !_input.IsGameplayInputLocked && !_input.IsDeathInputLocked
            && !_input.IsSkillInputLocked && !_input.IsWheelInputLocked && CanUseGate();

        private bool CanUseGate() => (_sanity == null || _sanity.HasSanity)
            && (_grab == null || !_grab.IsHolding) && (_burrow == null || !_burrow.IsBurrowed);

        private bool IsDriverSlot(int index)
        {
            QuickSlotItemDefinition item = _loadout != null ? _loadout.GetSlot(index) : null;
            return item != null && item.IsDriver;
        }

        private bool TryResolve<T>(ulong objectId, out T component) where T : Component
        {
            component = null;
            if (NetworkManager == null || NetworkManager.SpawnManager == null
                || !NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(objectId, out NetworkObject networkObject))
            {
                return false;
            }
            return networkObject.TryGetComponent(out component);
        }
    }
}
