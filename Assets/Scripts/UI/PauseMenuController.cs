using Cysharp.Threading.Tasks;
using GhostHunter.Core;
using GhostHunter.Core.Networking;
using GhostHunter.Core.Scenes;
using GhostHunter.Gameplay.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GhostHunter.UI
{
    /// <summary>
    /// 매치 중 ESC로 여는 메뉴. 계속하기 / 설정(stub) / 타이틀로 / 종료 4개를 제공하고,
    /// 세션이 끊기면 그 위에 안내 모달을 띄운다.
    ///
    /// 시간을 멈추지 않는다 — <c>Time.timeScale</c> 은 항상 1이다. 규칙은
    /// docs/project/pause-menu-system.md, 배선은 docs/architecture/pause-menu.md.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PauseMenuController : MonoBehaviour
    {
        private enum State
        {
            Closed,
            Menu,
            ConfirmQuit,
            Disconnected,
        }

        [Header("입력")]
        [Tooltip("프로젝트 액션 에셋. Player/Pause 로 열고 UI/Cancel 로 닫는다.")]
        [SerializeField] private InputActionAsset _inputActions;

        [Header("패널")]
        [SerializeField] private GameObject _menuPanel;
        [SerializeField] private GameObject _confirmQuitPanel;
        [SerializeField] private GameObject _disconnectedPanel;

        [Header("메뉴 버튼 (순서 고정: 계속하기 / 설정 / 타이틀로 / 종료)")]
        [SerializeField] private Button _resumeButton;
        [SerializeField] private Button _settingsButton;
        [SerializeField] private Button _titleButton;
        [SerializeField] private Button _quitButton;

        [Header("종료 확인")]
        [SerializeField] private Button _quitConfirmButton;
        [SerializeField] private Button _quitCancelButton;

        [Header("연결 끊김")]
        [SerializeField] private Button _disconnectedConfirmButton;

        [Header("상태 표시")]
        [SerializeField] private Text _statusText;

        private ISceneFlow _sceneFlow;
        private IConnectionService _connection;
        private ILocalPlayerContext _localPlayer;

        private InputActionAsset _runtimeActions;
        private InputAction _openAction;
        private InputAction _closeAction;

        private State _state = State.Closed;
        private bool _leaving;

        public bool IsOpen => _state != State.Closed;

        private void Awake()
        {
            Services.TryGet(out _sceneFlow);
            Services.TryGet(out _connection);
            Services.TryGet(out _localPlayer);
        }

        private void Start()
        {
            _resumeButton.onClick.AddListener(HandleResumeClicked);
            _settingsButton.onClick.AddListener(HandleSettingsClicked);
            _titleButton.onClick.AddListener(HandleTitleClicked);
            _quitButton.onClick.AddListener(HandleQuitClicked);
            _quitConfirmButton.onClick.AddListener(HandleQuitConfirmClicked);
            _quitCancelButton.onClick.AddListener(HandleQuitCancelClicked);
            _disconnectedConfirmButton.onClick.AddListener(HandleDisconnectedConfirmClicked);

            if (_connection != null)
                _connection.SessionEnded += HandleSessionEnded;

            if (_inputActions == null)
            {
                Debug.LogError($"{nameof(PauseMenuController)}: InputActionAsset 미할당", this);
                enabled = false;
                return;
            }

            // 열기와 닫기를 서로 다른 액션으로 나눈다. 둘 다 ESC 라서 동시에 켜 두면
            // 한 번의 입력으로 열렸다 바로 닫힌다.
            _runtimeActions = Instantiate(_inputActions);
            _openAction = _runtimeActions.FindAction("Player/Pause", true);
            _closeAction = _runtimeActions.FindAction("UI/Cancel", true);

            ApplyState(State.Closed);
        }

        private void OnDestroy()
        {
            if (_connection != null)
                _connection.SessionEnded -= HandleSessionEnded;

            if (_runtimeActions == null)
                return;

            _runtimeActions.Disable();
            Destroy(_runtimeActions);
            _runtimeActions = null;
        }

        private void Update()
        {
            if (_runtimeActions == null || _leaving)
                return;

            if (_state == State.Closed)
            {
                if (_openAction.WasPressedThisFrame())
                    ApplyState(State.Menu);

                return;
            }

            if (_state == State.Menu && _closeAction.WasPressedThisFrame())
                ApplyState(State.Closed);
            else if (_state == State.ConfirmQuit && _closeAction.WasPressedThisFrame())
                ApplyState(State.Menu);
        }

        #region 상태 전환

        private void ApplyState(State next)
        {
            // 세션이 끊긴 뒤에는 메뉴로 돌아갈 수 없다. 이어서 할 게임이 없다.
            if (_state == State.Disconnected && next != State.Disconnected)
                return;

            bool wasOpen = IsOpen;
            _state = next;
            bool isOpen = IsOpen;

            _menuPanel.SetActive(next == State.Menu);
            _confirmQuitPanel.SetActive(next == State.ConfirmQuit);
            _disconnectedPanel.SetActive(next == State.Disconnected);

            ApplyInputActionState(next);

            if (isOpen == wasOpen)
                return;

            if (isOpen)
                LockGameplay();
            else
                UnlockGameplay();

            SetCursorVisible(isOpen);
        }

        /// <summary>
        /// 여는 입력과 닫는 입력이 둘 다 ESC 라서, 한 번에 하나만 켠다.
        /// 끊김 모달에서는 둘 다 꺼서 확인 버튼으로만 빠져나가게 한다.
        /// </summary>
        private void ApplyInputActionState(State state)
        {
            if (_openAction == null || _closeAction == null)
                return;

            if (state == State.Closed)
            {
                _openAction.Enable();
                _closeAction.Disable();
                return;
            }

            _openAction.Disable();

            if (state == State.Disconnected)
                _closeAction.Disable();
            else
                _closeAction.Enable();
        }

        /// <summary>
        /// 잠그기 <b>전에</b> 들고 있던 가구를 놓는다 — 순서를 바꾸면 해제 입력 프레임이
        /// 오지 않아 가구가 계속 떠 있는다 → docs/architecture/pause-menu.md §6.5
        /// </summary>
        private void LockGameplay()
        {
            if (_localPlayer == null)
                return;

            if (_localPlayer.GrabController != null)
                _localPlayer.GrabController.ForceRelease();

            if (_localPlayer.Input != null)
                _localPlayer.Input.SetGameplayInputLocked(true);
        }

        private void UnlockGameplay()
        {
            if (_localPlayer == null || _localPlayer.Input == null)
                return;

            _localPlayer.Input.SetGameplayInputLocked(false);
        }

        private static void SetCursorVisible(bool visible)
        {
            Cursor.lockState = visible ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = visible;
        }

        #endregion

        #region 버튼

        private void HandleResumeClicked() => ApplyState(State.Closed);

        private void HandleSettingsClicked()
        {
            // 설정 시스템이 아직 없다. 이번 범위는 stub 이다 → pause-menu-system.md §4.3 (PM-6)
            SetStatus("설정은 아직 미구현입니다.");
        }

        private void HandleTitleClicked() => LeaveToTitleAsync().Forget();

        private void HandleQuitClicked() => ApplyState(State.ConfirmQuit);

        private void HandleQuitCancelClicked() => ApplyState(State.Menu);

        private void HandleQuitConfirmClicked() => QuitAsync().Forget();

        private void HandleDisconnectedConfirmClicked() => LeaveToTitleAsync().Forget();

        #endregion

        #region 나가기

        private void HandleSessionEnded()
        {
            if (_leaving)
                return;

            SetStatus("호스트와 연결이 끊겼습니다.");
            ApplyState(State.Disconnected);
        }

        /// <summary>
        /// 세션을 먼저 끊고 그 다음에 씬을 옮긴다. 순서를 뒤집으면 게스트는 전환이 거부되고,
        /// 호스트는 NGO 가 게스트까지 타이틀로 끌고 간다 → docs/architecture/networking.md §3.6.1
        /// </summary>
        private async UniTaskVoid LeaveToTitleAsync()
        {
            if (_leaving)
                return;

            _leaving = true;
            SetButtonsInteractable(false);

            await EndSessionAsync();

            if (_sceneFlow == null)
            {
                Debug.LogError($"{nameof(PauseMenuController)}: ISceneFlow 가 없어 타이틀로 갈 수 없다.", this);
                return;
            }

            _sceneFlow.Load(SceneId.Title);
        }

        private async UniTaskVoid QuitAsync()
        {
            if (_leaving)
                return;

            _leaving = true;
            SetButtonsInteractable(false);

            // "종료"도 "타이틀로"와 같은 세션 정리를 밟는다 → pause-menu-system.md §4.5 (PM-13)
            await EndSessionAsync();

#if UNITY_EDITOR
            UnityEditor.EditorApplication.ExitPlaymode();
#else
            Application.Quit();
#endif
        }

        /// <summary>
        /// 세션이 멈출 때까지 프레임을 넘긴다. 끝내 멈추지 않으면 기다리기를 포기하고 진행한다 —
        /// 여기서 영원히 대기하면 메뉴가 아무 반응 없는 상태로 남는다.
        /// </summary>
        private async UniTask WaitForSessionStoppedAsync()
        {
            const int maxFrames = 300;

            for (int i = 0; i < maxFrames && _connection.IsRunning; i++)
                await UniTask.NextFrame(destroyCancellationToken);

            if (_connection.IsRunning)
            {
                Debug.LogError(
                    $"{nameof(PauseMenuController)}: 세션이 {maxFrames} 프레임 안에 종료되지 않았다. " +
                    "그대로 씬을 전환한다.", this);
            }
        }

        /// <summary>
        /// 세션을 끊는다. 게스트는 Steam 로비에 남아 타이틀에서 다시 들어갈 수 있고,
        /// 호스트는 로비까지 나간다 → pause-menu-system.md §4.4 (PM-4·PM-5)
        /// </summary>
        private async UniTask EndSessionAsync()
        {
            if (_connection != null && _connection.IsRunning)
            {
                _connection.Disconnect(leaveLobby: _connection.IsHost);

                // 세션이 완전히 내려간 뒤에 씬을 만진다. 게스트가 붙어 있으면 NGO 는 클라이언트를
                // 먼저 끊느라 한 프레임으로 끝나지 않는데, 그 사이에 씬을 전환하면 NGO 경로로 빠져
                // 완료 콜백이 오지 않는 전환에 갇힌다 → docs/architecture/pause-menu.md §5.3
                await WaitForSessionStoppedAsync();
            }

            // 세션이 끝났으니 커서와 입력 잠금을 원래대로 돌린다.
            UnlockGameplay();
            SetCursorVisible(true);
        }

        #endregion

        private void SetButtonsInteractable(bool interactable)
        {
            _resumeButton.interactable = interactable;
            _settingsButton.interactable = interactable;
            _titleButton.interactable = interactable;
            _quitButton.interactable = interactable;
            _quitConfirmButton.interactable = interactable;
            _quitCancelButton.interactable = interactable;
        }

        private void SetStatus(string message)
        {
            if (_statusText != null)
                _statusText.text = message;
        }
    }
}
