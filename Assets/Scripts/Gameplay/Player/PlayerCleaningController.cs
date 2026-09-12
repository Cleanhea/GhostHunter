using GhostHunter.Core;
using GhostHunter.Gameplay.Cleaning;
using GhostHunter.Gameplay.Interaction;
using GhostHunter.Gameplay.Sanity;
using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Gameplay.Player
{
    /// <summary>대걸레 장착·조준 입력을 서버에 요청하고 소유자에게 대걸레를 표시한다.</summary>
    [DefaultExecutionOrder(90)]
    [DisallowMultipleComponent]
    public sealed class PlayerCleaningController : NetworkBehaviour
    {
        private const float MaximumOriginOffset = 2.5f;
        [SerializeField] private PlayerInputReader _input;
        [SerializeField] private Camera _camera;
        [SerializeField] private QuickSlotLoadout _loadout;
        [SerializeField] private CleaningSettings _settings;
        [SerializeField] private Transform _mopView;
        private GrabController _grab;
        private SanityNetworkState _sanity;
        private MoleBurrowController _burrow;
        private ILocalPlayerContext _localPlayer;
        private ICleaningService _cleaning;
        private int _equippedSlot = -1;
        private int _serverSlot = -1;
        private float _nextClickAt;
        private float _nextServerClickAt;
        private float _strokeElapsed;
        private Quaternion _restRotation;

        public int EquippedSlot => _equippedSlot;
        public bool IsMopEquipped => IsMopSlot(_equippedSlot);
        public bool ServerHasMop => IsServer && IsMopSlot(_serverSlot);

        private void Awake()
        {
            _grab = GetComponent<GrabController>();
            _sanity = GetComponent<SanityNetworkState>();
            _burrow = GetComponent<MoleBurrowController>();
            if (_mopView != null)
            {
                _restRotation = _mopView.localRotation;
                _mopView.gameObject.SetActive(false);
            }
        }

        public override void OnNetworkSpawn()
        {
            Services.TryGet(out _cleaning);
            if (IsOwner)
            {
                Services.TryGet(out _localPlayer);
                _localPlayer?.Register(this);
            }
            _equippedSlot = -1;
            _serverSlot = -1;
        }

        public override void OnNetworkDespawn()
        {
            _localPlayer?.Unregister(this);
            _localPlayer = null;
            _cleaning = null;
            _equippedSlot = -1;
            _serverSlot = -1;
            if (_mopView != null)
                _mopView.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (!IsSpawned || !IsOwner || _input == null || _settings == null)
                return;
            bool usable = IsMopEquipped && CanUseLocally();
            if (_mopView != null)
            {
                _mopView.gameObject.SetActive(usable);
                _strokeElapsed += Time.deltaTime;
                float phase = Mathf.Clamp01(_strokeElapsed / _settings.WipeSeconds);
                _mopView.localRotation = _restRotation * Quaternion.Euler(
                    0f, Mathf.Sin(phase * Mathf.PI) * -22f, 0f);
            }
            if (!usable || !_input.AttackPressedThisFrame || Time.unscaledTime < _nextClickAt)
                return;
            _nextClickAt = Time.unscaledTime + _settings.RequestInterval;
            _strokeElapsed = 0f;
            if (_camera != null && _cleaning != null && _cleaning.TryRaycast(
                _camera.transform.position, _camera.transform.forward,
                _settings.CleanDistance, out CleaningStain stain, transform))
            {
                RequestCleanRpc(stain.NetworkObjectId, stain.Revision,
                    _camera.transform.position, _camera.transform.forward);
            }
        }

        /// <summary>퀵슬롯 선택을 로컬에 표시하고 서버에서 장착 가능 여부를 확인한다.</summary>
        public bool EquipSlot(int index)
        {
            if (!IsSpawned || !IsOwner || _loadout == null || _loadout.GetSlot(index) == null
                || _input == null || _input.IsGameplayInputLocked || _input.IsDeathInputLocked
                || _input.IsSkillInputLocked || !CanUseOnServer())
                return false;
            _equippedSlot = index;
            RequestEquipRpc(index);
            return true;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestEquipRpc(int index, RpcParams rpcParams = default)
        {
            if (!IsServer || rpcParams.Receive.SenderClientId != OwnerClientId)
                return;
            if (_loadout != null && _loadout.GetSlot(index) != null && CanUseOnServer())
                _serverSlot = index;
            ConfirmEquipRpc(_serverSlot);
        }

        [Rpc(SendTo.Owner)]
        private void ConfirmEquipRpc(int index) => _equippedSlot = index;

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestCleanRpc(ulong objectId, uint revision, Vector3 origin, Vector3 direction,
            RpcParams rpcParams = default)
        {
            if (!IsServer || rpcParams.Receive.SenderClientId != OwnerClientId || !ServerHasMop
                || _settings == null || _cleaning == null || !CanUseOnServer()
                || Time.unscaledTime < _nextServerClickAt
                || !IsValidAim(transform.position, origin, direction))
                return;
            _nextServerClickAt = Time.unscaledTime + _settings.RequestInterval;
            // 이동은 Owner 권위지만, 벽 너머로 조준 원점을 보내는 요청은 허용하지 않는다.
            Vector3 bodyCenter = transform.position + Vector3.up * 0.5f;
            if (_cleaning.IsObstructed(bodyCenter, origin, transform))
                return;
            if (_cleaning.TryRaycast(origin, direction.normalized, _settings.CleanDistance,
                out CleaningStain stain, transform) && stain.NetworkObjectId == objectId)
                stain.ServerClean(revision);
        }

        /// <summary>비정상 벡터 및 플레이어에서 벗어난 조준 원점을 차단한다.</summary>
        public static bool IsValidAim(Vector3 playerPosition, Vector3 origin, Vector3 direction)
        {
            return IsFinite(playerPosition) && IsFinite(origin) && IsFinite(direction)
                && (origin - playerPosition).sqrMagnitude <= MaximumOriginOffset * MaximumOriginOffset
                && direction.sqrMagnitude > 0.99f && direction.sqrMagnitude < 1.01f;
        }

        private bool CanUseLocally() => !_input.IsGameplayInputLocked && !_input.IsDeathInputLocked
            && !_input.IsSkillInputLocked && !_input.IsWheelInputLocked && CanUseOnServer();

        private bool CanUseOnServer() => (_sanity == null || _sanity.HasSanity)
            && (_grab == null || !_grab.IsHolding) && (_burrow == null || !_burrow.IsBurrowed);

        private bool IsMopSlot(int index)
        {
            QuickSlotItemDefinition item = _loadout != null ? _loadout.GetSlot(index) : null;
            return item != null && item.IsMop;
        }

        private static bool IsFinite(Vector3 value) => float.IsFinite(value.x)
            && float.IsFinite(value.y) && float.IsFinite(value.z);
    }
}
