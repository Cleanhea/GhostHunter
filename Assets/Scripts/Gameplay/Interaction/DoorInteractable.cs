using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Gameplay.Interaction
{
    /// <summary>
    /// 경첩을 축으로 도는 여닫이 문. 복제하는 상태는 서버가 가진 열림/닫힘 bool 하나뿐이고,
    /// 각 피어가 그 값을 보고 자기 쪽 문짝을 돌린다 — 트랜스폼을 매 틱 복제하지 않으므로
    /// 대역폭이 들지 않고, 늦게 들어온 클라이언트도 NetworkVariable 로 현재 상태를 그대로 받는다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class DoorInteractable : NetworkBehaviour
    {
        [Tooltip("닫힌 상태의 로컬 Y 회전(도). 문짝이 벽과 나란해지는 각도.")]
        [SerializeField] private float _closedYaw;

        [Tooltip("열린 상태의 로컬 Y 회전(도). 부호가 열리는 방향을 정한다.")]
        [SerializeField] private float _openYaw = 90f;

        [Tooltip("세션이 시작될 때의 상태. 씬에 저장된 각도와 같아야 한다.")]
        [SerializeField] private bool _startsOpen = true;

        [Tooltip("여닫는 속도(초당 도).")]
        [SerializeField, Min(30f)] private float _swingSpeed = 220f;

        [Tooltip("서버가 다시 검사하는 상호작용 최대 거리. 조준 거리 + 문짝 폭보다 넉넉해야 한다.")]
        [SerializeField, Min(1f)] private float _maxInteractDistance = 4f;

        private readonly NetworkVariable<bool> _isOpen = new();
        private float _yaw;

        public bool IsOpen => _isOpen.Value;

        public override void OnNetworkSpawn()
        {
            if (IsServer)
                _isOpen.Value = _startsOpen;

            // 늦게 접속한 클라이언트가 문이 돌아가는 연출부터 보지 않도록 현재 상태로 스냅한다.
            _yaw = _isOpen.Value ? _openYaw : _closedYaw;
            ApplyYaw();
        }

        private void Update()
        {
            // 세션 밖(플레이 직후, 호스트 시작 전)에서는 씬에 저장된 각도를 그대로 둔다.
            if (!IsSpawned)
                return;

            float target = _isOpen.Value ? _openYaw : _closedYaw;
            if (Mathf.Approximately(_yaw, target))
                return;

            _yaw = Mathf.MoveTowardsAngle(_yaw, target, _swingSpeed * Time.deltaTime);
            ApplyYaw();
        }

        /// <summary>로컬 플레이어의 상호작용 입력. 상태를 바꾸는 건 서버뿐이다.</summary>
        public void RequestToggle()
        {
            if (!IsSpawned)
                return;

            RequestToggleServerRpc();
        }

        /// <summary>문은 아무도 소유하지 않으므로 소유권 대신 거리로 검증한다.</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestToggleServerRpc(RpcParams rpcParams = default)
        {
            if (!ServerIsSenderInRange(rpcParams.Receive.SenderClientId))
                return;

            _isOpen.Value = !_isOpen.Value;
        }

        private bool ServerIsSenderInRange(ulong senderClientId)
        {
            if (NetworkManager == null
                || !NetworkManager.ConnectedClients.TryGetValue(senderClientId, out NetworkClient client)
                || client.PlayerObject == null)
            {
                return false;
            }

            return Vector3.Distance(client.PlayerObject.transform.position, transform.position)
                <= _maxInteractDistance;
        }

        private void ApplyYaw()
        {
            transform.localRotation = Quaternion.Euler(0f, _yaw, 0f);
        }
    }
}
