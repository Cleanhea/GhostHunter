using System;
using GhostHunter.Gameplay.Sanity;
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

        /// <summary>
        /// 귀신 연출이 문을 잡고 있는 상태(인커 '문틈으로 지켜보는 귀신', project/ghost-inker.md §4). 잡혀 있는 동안
        /// 플레이어·귀신 AI 가 여닫지 못한다. <see cref="Degrees"/> 가 0 보다 크면 닫힌 각도에서 그만큼만, 정해진 속도로 연다.
        /// </summary>
        private struct GhostHold : INetworkSerializable, IEquatable<GhostHold>
        {
            public bool Locked;
            public float Degrees;
            public float Speed;

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref Locked);
                serializer.SerializeValue(ref Degrees);
                serializer.SerializeValue(ref Speed);
            }

            public bool Equals(GhostHold other) => Locked == other.Locked
                && Degrees == other.Degrees && Speed == other.Speed;
        }

        private readonly NetworkVariable<bool> _isOpen = new();
        private readonly NetworkVariable<GhostHold> _ghostHold = new();
        private float _yaw;

        public bool IsOpen => _isOpen.Value;

        /// <summary>귀신 연출이 문을 잡고 있다 — 여닫기 상호작용 불가.</summary>
        public bool IsGhostHeld => _ghostHold.Value.Locked;

        /// <summary>닫힌 상태의 월드 회전(부모 기준 닫힌 각도). 인커가 문짝 모양을 계산할 때 쓴다.</summary>
        public Quaternion ClosedWorldRotation =>
            (transform.parent != null ? transform.parent.rotation : Quaternion.identity)
            * Quaternion.Euler(0f, _closedYaw, 0f);

        /// <summary>서버 전용 — 문을 닫고 잡은 뒤 <paramref name="degrees"/> 만큼 <paramref name="seconds"/> 동안 천천히 연다.</summary>
        public void ServerBeginGhostPeek(float degrees, float seconds)
        {
            if (!IsSpawned || !IsServer)
                return;
            _isOpen.Value = false;
            _ghostHold.Value = new GhostHold
            {
                Locked = true,
                Degrees = Mathf.Max(0f, degrees),
                Speed = Mathf.Max(1f, degrees / Mathf.Max(0.05f, seconds)),
            };
        }

        /// <summary>서버 전용 — 잡은 채로 닫는다(원래 여닫는 속도라 세게 닫힌다). 닫는 동안에도 상호작용은 막는다.</summary>
        public void ServerGhostSlam()
        {
            if (!IsSpawned || !IsServer)
                return;
            _isOpen.Value = false;
            _ghostHold.Value = new GhostHold { Locked = true };
        }

        /// <summary>서버 전용 — 연출을 끝내고 문을 놓는다. 문은 닫힌 상태로 남는다.</summary>
        public void ServerReleaseGhostHold()
        {
            if (!IsSpawned || !IsServer)
                return;
            _ghostHold.Value = default;
        }

        /// <summary>
        /// 서버 전용 — 귀신이 닫힌 문을 열었다(<see cref="ServerForceOpen"/>·<see cref="ServerForceToggle"/> 는 귀신만 부른다).
        /// 부활 의식이 소환진 방의 문을 보고 진행도를 초기화한다(revival-system.md §7).
        /// </summary>
        public static event Action<DoorInteractable> ServerOpenedByGhost;

        /// <summary>
        /// 서버 전용 — 플레이어(client id)가 닫힌 문을 열었다. 인커 명령 'DON'T OPEN THE DOOR' 위반 판정이 듣는다
        /// (사용자 요청 2026-11-15). 닫는 것은 알리지 않는다.
        /// </summary>
        public static event Action<DoorInteractable, ulong> ServerOpenedByPlayer;

        /// <summary>테스트·내부 — 플레이어가 문을 연 것으로 알린다.</summary>
        internal static void ServerNotifyOpenedByPlayer(DoorInteractable door, ulong clientId)
            => ServerOpenedByPlayer?.Invoke(door, clientId);

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

            GhostHold hold = _ghostHold.Value;
            bool peeking = hold.Locked && hold.Degrees > 0f;
            float target = peeking
                ? _closedYaw + Mathf.Sign(Mathf.DeltaAngle(_closedYaw, _openYaw)) * hold.Degrees
                : _isOpen.Value ? _openYaw : _closedYaw;
            if (Mathf.Approximately(_yaw, target))
                return;

            float speed = peeking ? hold.Speed : _swingSpeed;
            _yaw = Mathf.MoveTowardsAngle(_yaw, target, speed * Time.deltaTime);
            ApplyYaw();
        }

        /// <summary>로컬 플레이어의 상호작용 입력. 상태를 바꾸는 건 서버뿐이다.</summary>
        public void RequestToggle()
        {
            if (!IsSpawned)
                return;

            RequestToggleServerRpc();
        }

        /// <summary>
        /// 서버가 문을 직접 연다. 귀신이 추격·수색 중 앞을 막은 방문을 여는 용도(§9.4).
        /// 플레이어 상호작용이 아니라 서버 AI가 부르는 경로라 거리·소유권 검증이 없다.
        /// </summary>
        public void ServerForceOpen()
        {
            if (!IsSpawned || !IsServer || _isOpen.Value || IsGhostHeld)
                return;
            _isOpen.Value = true;
            ServerOpenedByGhost?.Invoke(this);
        }

        /// <summary>서버가 문을 직접 닫는다. 귀신 초자연현상 '문 열고 닫기'(§6.5 #4) 용도.</summary>
        public void ServerForceClose()
        {
            if (IsSpawned && IsServer)
                _isOpen.Value = false;
        }

        public void ServerRestoreStageState(bool open)
        {
            if (IsSpawned && IsServer)
                _isOpen.Value = open;
        }

        /// <summary>서버가 문 상태를 뒤집는다. 귀신 초자연현상 '문 열고 닫기'(§6.5 #4) 용도.</summary>
        public void ServerForceToggle()
        {
            if (!IsSpawned || !IsServer || IsGhostHeld)
                return;
            _isOpen.Value = !_isOpen.Value;
            if (_isOpen.Value)
                ServerOpenedByGhost?.Invoke(this);
        }

        /// <summary>
        /// 문은 아무도 소유하지 않으므로 소유권 대신 거리로 검증한다. 사망한 플레이어의 요청은
        /// 거절한다(관전 기획서 SP-2, 사용자 확정 2026-09-12) — 귀신의 서버 전용
        /// <see cref="ServerForceOpen"/>/<see cref="ServerForceClose"/>/<see cref="ServerForceToggle"/>
        /// 는 이 RPC를 거치지 않으므로 영향을 받지 않는다.
        /// </summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestToggleServerRpc(RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (IsGhostHeld || !ServerIsSenderInRange(sender) || !ServerIsSenderAlive(sender))
                return;

            _isOpen.Value = !_isOpen.Value;
            if (_isOpen.Value)
                ServerNotifyOpenedByPlayer(this, sender);
        }

        private bool ServerIsSenderInRange(ulong senderClientId)
        {
            if (!TryGetSenderPlayerObject(senderClientId, out NetworkObject playerObject))
                return false;

            return Vector3.Distance(playerObject.transform.position, transform.position)
                <= _maxInteractDistance;
        }

        private bool ServerIsSenderAlive(ulong senderClientId)
        {
            if (!TryGetSenderPlayerObject(senderClientId, out NetworkObject playerObject))
                return false;

            SanityNetworkState sanity = playerObject.GetComponent<SanityNetworkState>();
            return sanity == null || sanity.HasSanity;
        }

        private bool TryGetSenderPlayerObject(ulong senderClientId, out NetworkObject playerObject)
        {
            playerObject = null;
            if (NetworkManager == null
                || !NetworkManager.ConnectedClients.TryGetValue(senderClientId, out NetworkClient client)
                || client.PlayerObject == null)
            {
                return false;
            }

            playerObject = client.PlayerObject;
            return true;
        }

        private void ApplyYaw()
        {
            transform.localRotation = Quaternion.Euler(0f, _yaw, 0f);
        }
    }
}
