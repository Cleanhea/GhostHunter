using System;
using Cysharp.Threading.Tasks;
using GhostHunter.Core;
using GhostHunter.Core.Scenes;
using GhostHunter.Core.Steam;
using GhostHunter.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace GhostHunter.UI
{
    /// <summary>
    /// 메인메뉴 화면. 방 생성/참가는 Steam 로비까지만 처리하고(네트워크 세션은 시작하지 않음),
    /// 로비에 들어가면 Lobby 씬으로 넘어간다. 실제 StartHost/StartClient 는 로비 씬에서
    /// "게임 시작" 시점에 일어난다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MainMenuController : MonoBehaviour
    {
        [Header("메인 버튼")]
        [SerializeField] private Button _createRoomButton;
        [SerializeField] private Button _joinRoomButton;
        [SerializeField] private Button _settingsButton;
        [SerializeField] private Button _quitButton;

        [Header("방 참가 패널")]
        [SerializeField] private GameObject _joinPanel;
        [SerializeField] private InputField _roomCodeInput;
        [SerializeField] private Button _joinConfirmButton;
        [SerializeField] private Button _joinCancelButton;

        [Header("상태 표시")]
        [SerializeField] private Text _statusText;

        private ISteamLobbyService _lobby;
        private ISceneFlow _sceneFlow;
        private bool _navigating;

        private void Start()
        {
            // 게임 씬에서 잠긴 커서가 남아 있을 수 있다.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            _createRoomButton.onClick.AddListener(HandleCreateRoomClicked);
            _joinRoomButton.onClick.AddListener(HandleJoinRoomClicked);
            _settingsButton.onClick.AddListener(HandleSettingsClicked);
            _quitButton.onClick.AddListener(HandleQuitClicked);
            _joinConfirmButton.onClick.AddListener(HandleJoinConfirmClicked);
            _joinCancelButton.onClick.AddListener(HandleJoinCancelClicked);
            _roomCodeInput.onValueChanged.AddListener(HandleRoomCodeChanged);

            _joinPanel.SetActive(false);

            // MonoBehaviour를 인터페이스 참조로 들면 Unity의 가짜 null 연산자가 동작하지 않는다.
            // 대입 시점에 구체 타입으로 한 번 걸러 진짜 null 로 정규화한다.
            // TODO: MIG-1에서 Services.Get<ISteamLobbyService>() 로 교체한다.
            SteamLobbyManager lobbyManager = SteamLobbyManager.Instance;
            _lobby = lobbyManager != null ? lobbyManager : null;
            Services.TryGet(out _sceneFlow);

            if (_lobby == null)
            {
                SetStatus("네트워크 리그가 없습니다. 씬 부트스트랩 설정을 확인하세요.");
                SetMenuInteractable(false);
                _quitButton.interactable = true;
                return;
            }

            _lobby.StatusChanged += HandleStatus;
            _lobby.HostLobbyReady += HandleEnteredLobby;
            _lobby.JoinTargetResolved += HandleJoinTargetResolved;

            SetStatus(_lobby.IsSteamReady
                ? $"Steam: {_lobby.LocalName}"
                : "Steam 미초기화 — Steam 클라이언트 실행 후 게임을 다시 시작하세요.");
        }

        private void OnDestroy()
        {
            if (_lobby == null)
                return;

            _lobby.StatusChanged -= HandleStatus;
            _lobby.HostLobbyReady -= HandleEnteredLobby;
            _lobby.JoinTargetResolved -= HandleJoinTargetResolved;
        }

        private void HandleCreateRoomClicked() => CreateRoomAsync().Forget();

        private async UniTaskVoid CreateRoomAsync()
        {
            if (_lobby == null || _navigating)
                return;

            SetMenuInteractable(false);

            try
            {
                await _lobby.CreateLobbyAsync();
            }
            catch (OperationCanceledException)
            {
                // 대기 도중 씬이 바뀌었다. 정상 종료.
                return;
            }
            catch (Exception e)
            {
                Debug.LogError($"[MainMenuController] 방 생성 중 예외: {e}", this);
                SetStatus($"방 생성 실패: {e.Message}");
            }
            finally
            {
                if (!_navigating)
                    SetMenuInteractable(true);
            }
        }

        private void HandleJoinRoomClicked()
        {
            _roomCodeInput.text = string.Empty;
            _joinPanel.SetActive(true);
            _roomCodeInput.ActivateInputField();
        }

        private void HandleJoinConfirmClicked() => JoinRoomAsync().Forget();

        private async UniTaskVoid JoinRoomAsync()
        {
            if (_lobby == null || _navigating)
                return;

            SetMenuInteractable(false);
            _joinConfirmButton.interactable = false;

            try
            {
                await _lobby.JoinLobbyByCodeAsync(_roomCodeInput.text);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e)
            {
                Debug.LogError($"[MainMenuController] 방 참가 중 예외: {e}", this);
                SetStatus($"방 참가 실패: {e.Message}");
            }
            finally
            {
                if (!_navigating)
                {
                    SetMenuInteractable(true);
                    _joinConfirmButton.interactable = true;
                }
            }
        }

        private void HandleJoinCancelClicked()
        {
            _joinPanel.SetActive(false);
        }

        private void HandleRoomCodeChanged(string value)
        {
            // 방 코드는 대문자로만 다룬다. SetTextWithoutNotify 로 재귀 호출을 막는다.
            string upper = value.ToUpperInvariant();
            if (upper != value)
                _roomCodeInput.SetTextWithoutNotify(upper);
        }

        private void HandleSettingsClicked()
        {
            SetStatus("설정은 아직 미구현입니다.");
        }

        private void HandleQuitClicked()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.ExitPlaymode();
#else
            Application.Quit();
#endif
        }

        /// <summary>호스트로 로비를 만들었을 때. 로비 씬으로 이동한다.</summary>
        private void HandleEnteredLobby()
        {
            NavigateToLobby();
        }

        /// <summary>게스트로 로비에 들어갔을 때(코드 참가 또는 초대 수락). 로비 씬으로 이동한다.</summary>
        private void HandleJoinTargetResolved(ulong hostSteamId)
        {
            NavigateToLobby();
        }

        private void NavigateToLobby()
        {
            if (_navigating)
                return;

            _navigating = true;
            _sceneFlow?.Load(SceneId.Lobby);
        }

        private void SetMenuInteractable(bool interactable)
        {
            _createRoomButton.interactable = interactable;
            _joinRoomButton.interactable = interactable;
            _settingsButton.interactable = interactable;
            _quitButton.interactable = interactable;
        }

        private void HandleStatus(string message) => SetStatus(message);

        private void SetStatus(string message)
        {
            if (_statusText != null)
                _statusText.text = message;
        }
    }
}
