using GhostHunter.Core;
using GhostHunter.Gameplay.Player;
using GhostHunter.Gameplay.Ghost;
using GhostHunter.Gameplay.Sanity;
using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Gameplay.Interaction
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
        private StageExitInteractable _currentStageExit;
        private StageLobbyTerminal _currentLobbyTerminal;
        private bool _confirmStageExit;
        private CursorLockMode _previousCursorLock;
        private bool _previousCursorVisible;

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
            CloseStageExitConfirmation();
            _localPlayer?.Unregister(this);
            _localPlayer = null;

            CurrentDoor = null;
        }

        private void Update()
        {
            if (_input == null || _camera == null)
                return;

            if (_confirmStageExit)
            {
                if (_input.IsDeathInputLocked || _input.IsGameplayInputLocked)
                    CloseStageExitConfirmation();
                return;
            }

            FindTargets();

            if (CurrentDoor != null && _input.InteractPressedThisFrame)
                CurrentDoor.RequestToggle();

            if (_currentStageExit != null && _input.InteractPressedThisFrame
                && !_input.IsDeathInputLocked && !_input.IsGameplayInputLocked)
                OpenStageExitConfirmation();

            // 인게임 로비 단말기(ADR-0018) — 창은 UI 계층이 열고 입력·커서를 맡는다.
            if (_currentLobbyTerminal != null && _input.InteractPressedThisFrame
                && !_input.IsDeathInputLocked && !_input.IsGameplayInputLocked)
                _currentLobbyTerminal.RequestOpen();
        }

        private void FindTargets()
        {
            Ray ray = new(_camera.transform.position, _camera.transform.forward);
            if (!Physics.Raycast(ray, out RaycastHit hit, _maxDistance, _blockingMask,
                QueryTriggerInteraction.Ignore))
            {
                CurrentDoor = null;
                _currentStageExit = null;
                _currentLobbyTerminal = null;
                return;
            }

            CurrentDoor = hit.collider.GetComponentInParent<DoorInteractable>();
            _currentStageExit = hit.collider.GetComponentInParent<StageExitInteractable>();
            _currentLobbyTerminal = hit.collider.GetComponentInParent<StageLobbyTerminal>();
        }

        private void OpenStageExitConfirmation()
        {
            _confirmStageExit = true;
            _previousCursorLock = Cursor.lockState;
            _previousCursorVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void CloseStageExitConfirmation()
        {
            if (!_confirmStageExit)
                return;
            _confirmStageExit = false;
            if (_input == null || !_input.IsGameplayInputLocked)
            {
                Cursor.lockState = _previousCursorLock;
                Cursor.visible = _previousCursorVisible;
            }
        }

        private void OnGUI()
        {
            if (!IsOwner)
                return;

            if (!_confirmStageExit)
            {
                if (_currentStageExit != null && _input != null
                    && !_input.IsDeathInputLocked && !_input.IsGameplayInputLocked)
                    GUI.Label(new Rect((Screen.width - 220f) * 0.5f,
                        Screen.height * 0.62f, 220f, 28f), "E: 스테이지 종료");
                else if (_currentLobbyTerminal != null && _input != null && !_input.IsGameplayInputLocked)
                    GUI.Label(new Rect((Screen.width - 220f) * 0.5f,
                        Screen.height * 0.62f, 220f, 28f), "E: 상점 · 스테이지 출발");
                return;
            }

            Rect area = new((Screen.width - 320f) * 0.5f,
                (Screen.height - 150f) * 0.5f, 320f, 150f);
            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.Label("스테이지를 종료하시겠습니까?");
            GUILayout.Label("드릴카 밖의 생존자는 실종 처리됩니다.");
            if (GUILayout.Button("종료", GUILayout.Height(36f)))
            {
                CloseStageExitConfirmation();
                RequestStageEndRpc();
            }
            if (GUILayout.Button("취소", GUILayout.Height(30f)))
                CloseStageExitConfirmation();
            GUILayout.EndArea();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestStageEndRpc()
        {
            StageExitInteractable terminal = FindFirstObjectByType<StageExitInteractable>();
            SanityNetworkState sanity = GetComponent<SanityNetworkState>();
            if (terminal == null || sanity == null || !sanity.HasSanity
                || Vector3.Distance(transform.position, terminal.transform.position) > 4f
                || !DrillCarSafeZone.Contains(transform.position))
                return;

            if (Services.TryGet(out ISanityTeamService team))
                team.ServerEndStageByExit();
        }

    }
}
