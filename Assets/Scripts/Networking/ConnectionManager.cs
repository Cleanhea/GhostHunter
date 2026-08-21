using System;
using Cysharp.Threading.Tasks;
using GhostHunter.Core;
using GhostHunter.Core.Networking;
using GhostHunter.Core.Scenes;
using GhostHunter.Core.Steam;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GhostHunter.Networking
{
    /// <summary>
    /// Netcode 세션의 시작/종료를 담당한다. Steam 쪽 사정은 <see cref="ISteamLobbyService"/>가 알고,
    /// 이 컴포넌트는 "언제 StartHost/StartClient를 부를지"만 안다.
    ///
    /// 트랜스포트를 두 개 두는 이유: 같은 Steam 계정으로는 두 인스턴스를 P2P 연결할 수 없다
    /// (SteamId가 같아 자기 자신에게 연결하는 꼴이 된다). PC/계정이 하나뿐인 날에도 개발이
    /// 멈추지 않도록 로컬 UTP 경로를 항상 열어둔다.
    /// </summary>
    [DisallowMultipleComponent]
    public class ConnectionManager : MonoBehaviour, IConnectionService
    {
        [Header("참조")]
        [Tooltip("비워두면 NetworkManager.Singleton 을 사용한다.")]
        [SerializeField] private NetworkManager _networkManager;

        [Tooltip("Steam P2P 트랜스포트. NetworkManager 와 같은 오브젝트에 붙인다.")]
        [SerializeField] private NetworkTransport _steamTransport;

        [Tooltip("로컬 테스트용 UnityTransport. 구체 타입 의존을 피하려고 기반 타입으로 받는다.")]
        [SerializeField] private NetworkTransport _localTransport;

        [Header("설정")]
        [SerializeField] private TransportMode _transportMode = TransportMode.Steam;

        [Tooltip("커맨드라인 -transport=local / -transport=steam 으로 위 설정을 덮어쓴다.")]
        [SerializeField] private bool _allowCommandLineOverride = true;

        [Tooltip("로비 이벤트(HostLobbyReady/JoinTargetResolved)를 받으면 즉시 세션을 시작한다. " +
                 "개발 HUD 직접 접속 흐름에서만 켠다. Title→Lobby 흐름은 로비 UI가 시작 시점을 정한다.")]
        [SerializeField] private bool _autoStartFromLobbyEvents;

        /// <summary>게임 씬 로드를 기다리는 한계. 넘으면 원인을 로그로 남기고 포기한다.</summary>
        private static readonly TimeSpan SceneLoadTimeout = TimeSpan.FromSeconds(30);

        public TransportMode Mode => _transportMode;
        public bool IsRunning => Net != null && (Net.IsServer || Net.IsClient);
        public bool IsHost => Net != null && Net.IsHost;

        /// <summary>사람이 읽는 진행 상황. 개발용 HUD가 그대로 표시한다.</summary>
        public event Action<string> StatusChanged;

        private NetworkManager Net => _networkManager != null ? _networkManager : NetworkManager.Singleton;

        private ISteamLobbyService _lobby;
        private ISceneFlow _sceneFlow;

        private void Awake()
        {
            Services.TryGet(out _lobby);
            Services.TryGet(out _sceneFlow);

            if (_allowCommandLineOverride)
                ApplyCommandLineOverride();
        }

        private void Start()
        {
            if (_sceneFlow == null)
                SetStatus("ISceneFlow 가 등록되지 않았습니다. Bootstrap 씬에서 시작했는지 확인하세요.");

            if (_lobby != null)
            {
                _lobby.HostLobbyReady += HandleHostLobbyReady;
                _lobby.JoinTargetResolved += HandleJoinTargetResolved;
            }
            else if (_transportMode == TransportMode.Steam)
            {
                SetStatus("SteamLobbyManager 가 씬에 없습니다. Steam 모드를 쓸 수 없습니다.");
            }

            NetworkManager net = Net;
            if (net != null)
            {
                net.OnClientConnectedCallback += HandleClientConnected;
                net.OnClientDisconnectCallback += HandleClientDisconnected;
            }
            else
            {
                SetStatus("NetworkManager 를 찾지 못했습니다. 인스펙터에 연결하세요.");
            }
        }

        private void OnDestroy()
        {
            if (_lobby != null)
            {
                _lobby.HostLobbyReady -= HandleHostLobbyReady;
                _lobby.JoinTargetResolved -= HandleJoinTargetResolved;
            }

            // Net 프로퍼티는 종료 중 Singleton 이 이미 null 일 수 있으므로 필드를 직접 본다.
            if (_networkManager != null)
            {
                _networkManager.OnClientConnectedCallback -= HandleClientConnected;
                _networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            }
        }

        #region 공개 API

        public void SetTransportMode(TransportMode mode)
        {
            if (IsRunning)
            {
                SetStatus("세션 중에는 트랜스포트를 바꿀 수 없습니다. 먼저 연결을 끊으세요.");
                return;
            }

            _transportMode = mode;
            SetStatus($"트랜스포트: {mode}");
        }

        /// <summary>
        /// 호스트로 시작한다. Steam 모드에서는 로비를 먼저 만들고,
        /// <see cref="ISteamLobbyService.HostLobbyReady"/> 를 받은 뒤에 실제 StartHost 가 일어난다.
        /// </summary>
        public void StartHost() => StartHostAsync().Forget();

        private async UniTaskVoid StartHostAsync()
        {
            if (!EnsureReadyToStart())
                return;

            if (_transportMode == TransportMode.Local)
            {
                StartHostInternal();
                return;
            }

            if (_lobby == null)
            {
                SetStatus("SteamLobbyManager 가 없어 Steam 호스팅을 할 수 없습니다.");
                return;
            }

            // 메뉴 흐름에서 이미 로비를 만들어 둔 호스트는 로비 재생성 없이 바로 시작한다.
            if (_lobby.IsInLobby && _lobby.IsLobbyOwner)
            {
                StartHostInternal();
                return;
            }

            try
            {
                await _lobby.CreateLobbyAsync();
            }
            catch (OperationCanceledException)
            {
                // 대기 도중 오브젝트가 파괴됐다. 정상 종료.
            }
            catch (Exception e)
            {
                Debug.LogError($"[ConnectionManager] 로비 생성 중 예외: {e}", this);
                SetStatus($"로비 생성 실패: {e.Message}");
            }
        }

        /// <summary>
        /// 게임 씬을 로드한 뒤 그 씬에서 호스트를 시작한다(메뉴→로비 흐름 전용).
        /// 로비 씬에서 바로 StartHost 하면 플레이어가 스폰 지점 없는 씬에 스폰되므로,
        /// 반드시 게임 씬이 활성화된 다음 세션을 연다. 성공하면 Steam 로비에
        /// 게임 시작을 알려 게스트들이 접속하게 한다.
        /// </summary>
        public void StartHostInGameScene(SceneId scene)
        {
            if (!EnsureReadyToStart())
                return;

            if (_sceneFlow == null)
            {
                SetStatus("ISceneFlow 가 없어 게임 씬으로 넘어갈 수 없습니다.");
                return;
            }

            StartHostInGameSceneAsync(scene).Forget();
        }

        private async UniTaskVoid StartHostInGameSceneAsync(SceneId target)
        {
            SetStatus($"게임 씬 로드 중... ({target})");

            var loaded = new UniTaskCompletionSource();
            Action<SceneId> onSceneChanged = id =>
            {
                if (id == target)
                    loaded.TrySetResult();
            };

            _sceneFlow.SceneChanged += onSceneChanged;

            try
            {
                _sceneFlow.Load(target);

                // Load 는 거부될 수 있고(전환 중·미배선) 그때는 SceneChanged 가 오지 않는다.
                // 무한 대기 대신 시간 제한을 둬서 원인이 로그에 남게 한다.
                int winner = await UniTask.WhenAny(
                    loaded.Task,
                    UniTask.Delay(SceneLoadTimeout, cancellationToken: destroyCancellationToken));

                if (winner != 0)
                {
                    SetStatus($"게임 씬({target}) 로드가 시간 안에 끝나지 않았습니다.");
                    return;
                }

                // 씬 오브젝트(스폰 레지스트리 등)의 Awake 가 끝난 다음 프레임에 시작한다.
                await UniTask.NextFrame(destroyCancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            finally
            {
                _sceneFlow.SceneChanged -= onSceneChanged;
            }

            StartHostInternal();

            if (IsRunning && _transportMode == TransportMode.Steam)
                _lobby?.MarkGameStarted();
        }

        /// <summary>
        /// 알고 있는 호스트 SteamId 로 클라이언트 접속한다. 메뉴→로비 흐름에서는
        /// 로비 UI가 "게임 시작" 신호를 받은 뒤 직접 부른다.
        /// </summary>
        public void ConnectToSteamHost(ulong hostSteamId)
        {
            if (IsRunning)
            {
                SetStatus("이미 세션이 실행 중이라 접속 요청을 무시합니다.");
                return;
            }

            if (_steamTransport == null)
            {
                SetStatus("Steam 트랜스포트가 연결되어 있지 않습니다.");
                return;
            }

            if (hostSteamId == 0)
            {
                SetStatus("호스트 SteamId 가 유효하지 않습니다.");
                return;
            }

            _transportMode = TransportMode.Steam;

            if (!ApplyTransport())
                return;

            if (_lobby == null || !_lobby.TrySetConnectionTarget(hostSteamId))
            {
                SetStatus("Steam 접속 대상을 설정하지 못했습니다.");
                return;
            }

            if (Net.StartClient())
                SetStatus($"호스트 {hostSteamId} 에 접속 시도 중...");
            else
                SetStatus("StartClient 실패.");
        }

        /// <summary>
        /// 로컬(UTP) 클라이언트로 접속한다. Steam 모드에서는 이 버튼이 아니라
        /// 친구 초대 수락 흐름으로 접속한다.
        /// </summary>
        public void StartLocalClient()
        {
            if (!EnsureReadyToStart())
                return;

            if (_transportMode != TransportMode.Local)
            {
                SetStatus("Local 모드에서만 직접 접속할 수 있습니다. Steam 모드는 초대로 접속하세요.");
                return;
            }

            if (!ApplyTransport())
                return;

            if (Net.StartClient())
                SetStatus("로컬 클라이언트로 접속 시도 중 (127.0.0.1)...");
            else
                SetStatus("StartClient 실패.");
        }

        public void Disconnect()
        {
            NetworkManager net = Net;

            if (net != null && (net.IsServer || net.IsClient))
                net.Shutdown();

            _lobby?.LeaveLobby();

            SetStatus("연결을 끊었습니다.");
        }

        #endregion

        #region 내부

        private bool EnsureReadyToStart()
        {
            if (Net == null)
            {
                SetStatus("NetworkManager 가 없습니다.");
                return false;
            }

            if (IsRunning)
            {
                SetStatus("이미 세션이 실행 중입니다.");
                return false;
            }

            return true;
        }

        /// <summary>선택된 모드에 맞는 트랜스포트를 NetworkConfig 에 꽂는다.</summary>
        private bool ApplyTransport()
        {
            NetworkTransport transport = _transportMode == TransportMode.Steam
                ? _steamTransport
                : _localTransport;

            if (transport == null)
            {
                SetStatus($"{_transportMode} 트랜스포트가 인스펙터에 연결되어 있지 않습니다.");
                return false;
            }

            Net.NetworkConfig.NetworkTransport = transport;
            return true;
        }

        private void StartHostInternal()
        {
            if (!ApplyTransport())
                return;

            if (Net.StartHost())
                SetStatus($"호스트 시작됨 ({_transportMode}).");
            else
                SetStatus("StartHost 실패.");
        }

        private void HandleHostLobbyReady()
        {
            // 메뉴 흐름에서는 로비 생성 = 대기실 입장일 뿐이므로 자동 시작하지 않는다.
            if (!_autoStartFromLobbyEvents)
                return;

            // 로비가 준비된 뒤에야 호스트를 띄운다. 순서가 반대면 참가자가
            // 접속할 대상 SteamId를 알 방법이 없다.
            StartHostInternal();
        }

        private void HandleJoinTargetResolved(ulong hostSteamId)
        {
            // 메뉴 흐름에서는 로비 입장만으로 접속하지 않는다. 호스트 세션이 아직 없을 수
            // 있으므로 로비 UI가 "게임 시작" 신호를 확인하고 ConnectToSteamHost 를 부른다.
            if (!_autoStartFromLobbyEvents)
                return;

            ConnectToSteamHost(hostSteamId);
        }

        private void HandleClientConnected(ulong clientId)
        {
            SetStatus($"클라이언트 접속: {clientId}");
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            NetworkManager net = Net;

            // 내가 끊긴 경우와 남이 끊긴 경우를 구분한다.
            bool selfDisconnected = net != null && clientId == net.LocalClientId;

            if (selfDisconnected && !net.IsServer)
            {
                string reason = string.IsNullOrEmpty(net.DisconnectReason)
                    ? "(사유 없음)"
                    : net.DisconnectReason;

                SetStatus($"서버와의 연결이 끊겼습니다. {reason}");
                _lobby?.LeaveLobby();
                return;
            }

            SetStatus($"클라이언트 연결 해제: {clientId}");
        }

        private void ApplyCommandLineOverride()
        {
            foreach (string arg in Environment.GetCommandLineArgs())
            {
                if (!arg.StartsWith("-transport=", StringComparison.OrdinalIgnoreCase))
                    continue;

                string value = arg.Substring("-transport=".Length);

                if (Enum.TryParse(value, ignoreCase: true, out TransportMode parsed))
                {
                    _transportMode = parsed;
                    Debug.Log($"[ConnectionManager] 커맨드라인으로 트랜스포트 지정: {parsed}");
                }
                else
                {
                    Debug.LogWarning($"[ConnectionManager] 알 수 없는 -transport 값: '{value}'");
                }

                return;
            }
        }

        private void SetStatus(string message)
        {
            Debug.Log($"[ConnectionManager] {message}");
            StatusChanged?.Invoke(message);
        }

        #endregion
    }
}
