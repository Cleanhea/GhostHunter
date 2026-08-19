using GhostHunter.Core;
using GhostHunter.Player;
using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Interaction
{
    /// <summary>
    /// 조준선 끝의 상호작용 대상(현재는 문)을 찾아 E 입력을 넘긴다.
    /// 레이캐스트가 플레이어 레이어만 빼고 전부 맞으므로 벽이나 가구 뒤의 문은 잡히지 않는다 —
    /// <see cref="FurnitureTargeter"/> 처럼 가구 레이어만 보면 벽을 뚫고 문이 열린다.
    /// </summary>
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class PlayerInteractor : NetworkBehaviour
    {
        [SerializeField] private PlayerInputReader _input;
        [SerializeField] private Camera _camera;

        [Tooltip("조준선으로 문에 닿을 수 있는 최대 거리(m).")]
        [SerializeField, Min(0.5f)] private float _maxDistance = 2.5f;

        private int _blockingMask;

        /// <summary>지금 조준 중인 문. HUD 프롬프트가 읽는다.</summary>
        public DoorInteractable CurrentDoor { get; private set; }

        private ILocalPlayerContext _localPlayer;

        private void Awake()
        {
            _blockingMask = GameLayers.Player >= 0 ? ~(1 << GameLayers.Player) : ~0;
        }

        public override void OnNetworkSpawn()
        {
            if (!IsOwner)
            {
                enabled = false;
                return;
            }

            _localPlayer = Services.Get<ILocalPlayerContext>();
            _localPlayer.Register(this);
        }

        public override void OnNetworkDespawn()
        {
            _localPlayer?.Unregister(this);
            _localPlayer = null;

            CurrentDoor = null;
        }

        private void Update()
        {
            if (_input == null || _camera == null)
                return;

            CurrentDoor = FindDoor();

            if (CurrentDoor != null && _input.InteractPressedThisFrame)
                CurrentDoor.RequestToggle();
        }

        private DoorInteractable FindDoor()
        {
            Ray ray = new(_camera.transform.position, _camera.transform.forward);

            if (!Physics.Raycast(
                    ray,
                    out RaycastHit hit,
                    _maxDistance,
                    _blockingMask,
                    QueryTriggerInteraction.Ignore))
            {
                return null;
            }

            return hit.collider.GetComponentInParent<DoorInteractable>();
        }
    }
}
