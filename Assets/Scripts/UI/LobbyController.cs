using System.Collections.Generic;
using GhostHunter.Core;
using GhostHunter.Core.Scenes;
using GhostHunter.Core.Steam;
using GhostHunter.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace GhostHunter.UI
{
    /// <summary>
    /// 로비(대기실) 화면. 여기까지는 Steam 로비만 살아 있고 NGO 세션은 없다.
    /// - 호스트: 전원 준비 확인 후 "게임 시작" → 게임 씬 로드 → StartHost → 로비에 시작 신호.
    /// - 게스트: 로비 데이터의 시작 신호를 보고 StartClient. NGO 씬 동기화가 게임 씬으로 데려간다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LobbyController : MonoBehaviour
    {
        [Header("표시")]
        [SerializeField] private Text _roomCodeText;
        [SerializeField] private Text _statusText;

        [Header("멤버 목록")]
        [SerializeField] private Transform _memberListRoot;
        [SerializeField] private LobbyMemberEntry _memberEntryTemplate;

        [Header("버튼")]
        [SerializeField] private Button _copyCodeButton;
        [SerializeField] private Button _inviteButton;
        [SerializeField] private Button _readyButton;
        [SerializeField] private Text _readyButtonLabel;
        [SerializeField] private Button _startButton;
        [SerializeField] private Button _leaveButton;

        private ISteamLobbyService _lobby;
        private ConnectionManager _connection;
        private ISceneFlow _sceneFlow;
        private readonly List<LobbyMemberEntry> _entries = new();
        private bool _localReady;
        private bool _departing; // 게임 시작 또는 접속 절차에 들어갔다.

        private void Start()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            _copyCodeButton.onClick.AddListener(HandleCopyCodeClicked);
            _inviteButton.onClick.AddListener(HandleInviteClicked);
            _readyButton.onClick.AddListener(HandleReadyClicked);
            _startButton.onClick.AddListener(HandleStartClicked);
            _leaveButton.onClick.AddListener(HandleLeaveClicked);

            _memberEntryTemplate.gameObject.SetActive(false);

            // MonoBehaviour를 인터페이스 참조로 들면 Unity의 가짜 null 연산자가 동작하지 않는다.
            // 대입 시점에 구체 타입으로 한 번 걸러 진짜 null 로 정규화한다.
            // TODO: MIG-1에서 Services.Get<ISteamLobbyService>() 로 교체한다.
            SteamLobbyManager lobbyManager = SteamLobbyManager.Instance;
            _lobby = lobbyManager != null ? lobbyManager : null;
            _connection = ConnectionManager.Instance;
            Services.TryGet(out _sceneFlow);

            if (_lobby == null || !_lobby.IsInLobby)
            {
                // 로비 씬을 단독으로 열었거나 로비가 이미 사라진 경우.
                SetStatus("로비 정보가 없습니다. 메인메뉴로 돌아가세요.");
                _copyCodeButton.interactable = false;
                _inviteButton.interactable = false;
                _readyButton.interactable = false;
                _startButton.gameObject.SetActive(false);
                _roomCodeText.text = "방 코드: -";
                return;
            }

            _lobby.StatusChanged += HandleStatus;
            _lobby.LobbyUpdated += HandleLobbyUpdated;
            _lobby.LobbyLeft += HandleLobbyLeft;

            // 준비 상태는 로비 씬에 들어올 때마다 초기화한다(재입장 시 이전 값 잔존 방지).
            _localReady = false;
            _lobby.SetLocalReady(false);

            SetStatus(_lobby.IsLobbyOwner
                ? "방 코드를 공유하거나 친구를 초대하세요."
                : "호스트가 시작할 때까지 기다리는 중입니다. 준비를 눌러주세요.");

            Refresh();
            TryConnectToStartedGame();
        }

        private void OnDestroy()
        {
            if (_lobby == null)
                return;

            _lobby.StatusChanged -= HandleStatus;
            _lobby.LobbyUpdated -= HandleLobbyUpdated;
            _lobby.LobbyLeft -= HandleLobbyLeft;
        }

        private void HandleLobbyUpdated()
        {
            Refresh();
            TryConnectToStartedGame();
        }

        /// <summary>
        /// 게스트 전용: 호스트가 이미 게임을 시작했으면(늦은 초대 수락 포함) 바로 접속한다.
        /// NGO 씬 동기화가 접속 직후 게임 씬을 로드해 준다.
        /// </summary>
        private void TryConnectToStartedGame()
        {
            if (_departing || _lobby.IsLobbyOwner || !_lobby.IsGameStarted)
                return;

            if (_connection == null || _connection.IsRunning)
                return;

            _departing = true;
            SetStatus("호스트가 게임을 시작했습니다. 접속 중...");
            _connection.ConnectToSteamHost(_lobby.CurrentHostSteamId);
        }

        private void HandleStartClicked()
        {
            if (_departing || _lobby == null || !_lobby.IsLobbyOwner || _connection == null)
                return;

            if (!_lobby.AllGuestsReady())
            {
                SetStatus("모든 참가자가 준비를 마쳐야 시작할 수 있습니다.");
                return;
            }

            _departing = true;
            _connection.SetTransportMode(TransportMode.Steam);
            _connection.StartHostInGameScene(SceneId.Game);
        }

        private void HandleReadyClicked()
        {
            if (_lobby == null || !_lobby.IsInLobby)
                return;

            _localReady = !_localReady;
            _lobby.SetLocalReady(_localReady);
            Refresh();
        }

        private void HandleInviteClicked()
        {
            _lobby?.OpenInviteOverlay();
        }

        private void HandleCopyCodeClicked()
        {
            if (_lobby == null || string.IsNullOrEmpty(_lobby.CurrentRoomCode))
                return;

            GUIUtility.systemCopyBuffer = _lobby.CurrentRoomCode;
            SetStatus($"방 코드 {_lobby.CurrentRoomCode} 를 클립보드에 복사했습니다.");
        }

        private void HandleLeaveClicked()
        {
            _lobby?.LeaveLobby();

            // LobbyLeft 이벤트로도 돌아가지만, 로비가 애초에 없던 경우를 위해 직접 이동한다.
            if (!_departing)
            {
                _departing = true;
                _sceneFlow?.Load(SceneId.Title);
            }
        }

        private void HandleLobbyLeft()
        {
            if (_departing)
                return;

            _departing = true;
            _sceneFlow?.Load(SceneId.Title);
        }

        private void Refresh()
        {
            if (_lobby == null || !_lobby.IsInLobby)
                return;

            _roomCodeText.text = string.IsNullOrEmpty(_lobby.CurrentRoomCode)
                ? "방 코드: (없음)"
                : $"방 코드: {_lobby.CurrentRoomCode}";

            bool isOwner = _lobby.IsLobbyOwner;

            // 방장은 준비 개념이 없고(시작 버튼이 그 역할), 게스트에게는 시작 버튼이 없다.
            _readyButton.gameObject.SetActive(!isOwner);
            _startButton.gameObject.SetActive(isOwner);
            _startButton.interactable = !_departing && _lobby.AllGuestsReady();
            _readyButtonLabel.text = _localReady ? "준비 해제" : "준비";

            RebuildMemberList();
        }

        private void RebuildMemberList()
        {
            foreach (LobbyMemberEntry entry in _entries)
            {
                if (entry != null)
                    Destroy(entry.gameObject);
            }

            _entries.Clear();

            // 정렬(방장 우선)은 서비스가 이미 해서 넘겨준다.
            foreach (LobbyMemberInfo member in _lobby.GetMembers())
            {
                LobbyMemberEntry entry = Instantiate(_memberEntryTemplate, _memberListRoot);
                entry.gameObject.SetActive(true);
                entry.Bind(member, _lobby);
                _entries.Add(entry);
            }
        }

        private void HandleStatus(string message) => SetStatus(message);

        private void SetStatus(string message)
        {
            if (_statusText != null)
                _statusText.text = message;
        }
    }
}
