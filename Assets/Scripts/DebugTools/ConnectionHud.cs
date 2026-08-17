using GhostHunter.Networking;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GhostHunter.DebugTools
{
    /// <summary>
    /// 개발용 접속 HUD. 일부러 IMGUI로 만들었다 — 씬에 Canvas/버튼을 배치하지 않아도
    /// 컴포넌트만 붙이면 바로 테스트할 수 있어야 하기 때문이다.
    /// 실제 로비 UI가 생기면 이 파일은 삭제한다.
    /// </summary>
    [DisallowMultipleComponent]
    public class ConnectionHud : MonoBehaviour
    {
        [SerializeField] private bool _visible = true;

        [Tooltip("HUD 표시를 켜고 끄는 키.")]
        [SerializeField] private Key _toggleKey = Key.F1;

        private string _lastStatus = "대기 중";
        private ConnectionManager _connection;
        private SteamLobbyManager _lobby;
        private GUIStyle _boxStyle;
        private GUIStyle _richLabelStyle;

        private void Start()
        {
            _connection = ConnectionManager.Instance;
            _lobby = SteamLobbyManager.Instance;

            if (_connection != null)
                _connection.StatusChanged += HandleStatus;

            if (_lobby != null)
                _lobby.StatusChanged += HandleStatus;
        }

        private void OnDestroy()
        {
            if (_connection != null)
                _connection.StatusChanged -= HandleStatus;

            if (_lobby != null)
                _lobby.StatusChanged -= HandleStatus;
        }

        private void Update()
        {
            // 프로젝트가 신규 Input System 전용(activeInputHandler=1)이라 레거시 Input 클래스는
            // 런타임에 예외를 던진다. Keyboard.current 로 직접 읽는다.
            Keyboard keyboard = Keyboard.current;

            if (keyboard != null && keyboard[_toggleKey].wasPressedThisFrame)
                _visible = !_visible;
        }

        private void HandleStatus(string message) => _lastStatus = message;

        /// <summary>메뉴 씬처럼 실제 UI가 있는 곳에서는 부트스트랩이 끈다. 토글 키는 계속 동작한다.</summary>
        public void SetVisible(bool visible) => _visible = visible;

        private void OnGUI()
        {
            if (!_visible)
                return;

            EnsureStyles();

            GUILayout.BeginArea(new Rect(10, 10, 340, 400), GUIContent.none, _boxStyle);

            GUILayout.Label($"<b>GhostHunter 접속 HUD</b>  ({_toggleKey} 로 토글)", _richLabelStyle);
            GUILayout.Space(6);

            DrawSteamSection();
            GUILayout.Space(6);
            DrawSessionSection();
            GUILayout.Space(6);
            DrawButtons();

            GUILayout.Space(8);
            GUILayout.Label("상태:", GUI.skin.label);
            GUILayout.Label(_lastStatus, GUI.skin.textArea, GUILayout.MinHeight(44));

            GUILayout.EndArea();
        }

        /// <summary>
        /// OnGUI 는 프레임마다 여러 번(Layout/Repaint) 불린다. 여기서 GUIStyle 을 새로 만들면
        /// 매 프레임 힙 할당이 쌓여 주기적인 GC 스파이크 = 체감 프레임 드랍이 된다. 한 번만 만든다.
        /// </summary>
        private void EnsureStyles()
        {
            if (_boxStyle != null)
                return;

            _boxStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperLeft,
                padding = new RectOffset(10, 10, 10, 10),
                wordWrap = true,
            };

            _richLabelStyle = new GUIStyle(GUI.skin.label)
            {
                richText = true,
            };
        }

        private void DrawSteamSection()
        {
            if (_lobby == null)
            {
                GUILayout.Label("Steam: SteamLobbyManager 없음");
                return;
            }

            if (!_lobby.IsSteamReady)
            {
                GUILayout.Label("Steam: <color=#ff6b6b>미초기화</color> (Steam 클라이언트 확인)",
                    _richLabelStyle);
                return;
            }

            GUILayout.Label($"Steam: {_lobby.LocalName}  ({_lobby.LocalSteamId})");
            GUILayout.Label(_lobby.IsInLobby
                ? $"로비: {_lobby.CurrentLobby.Value.Id}  인원 {_lobby.CurrentLobby.Value.MemberCount}"
                : "로비: 없음");
        }

        private void DrawSessionSection()
        {
            NetworkManager net = NetworkManager.Singleton;

            if (net == null)
            {
                GUILayout.Label("NetworkManager 없음");
                return;
            }

            string role = net.IsHost ? "Host" : net.IsServer ? "Server" : net.IsClient ? "Client" : "정지";
            GUILayout.Label($"세션: {role}");

            if (net.IsServer)
                GUILayout.Label($"접속 클라이언트: {net.ConnectedClientsIds.Count}");
            else if (net.IsClient)
                GUILayout.Label($"내 ClientId: {net.LocalClientId}");
        }

        private void DrawButtons()
        {
            if (_connection == null)
            {
                GUILayout.Label("ConnectionManager 없음");
                return;
            }

            bool running = _connection.IsRunning;

            GUILayout.BeginHorizontal();
            GUI.enabled = !running;
            if (GUILayout.Button($"모드: {_connection.Mode}"))
            {
                _connection.SetTransportMode(
                    _connection.Mode == TransportMode.Steam ? TransportMode.Local : TransportMode.Steam);
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUI.enabled = !running;
            if (GUILayout.Button("Host"))
                _connection.StartHost();

            if (GUILayout.Button("Join (로컬)"))
                _connection.StartLocalClient();
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUI.enabled = _lobby != null && _lobby.IsInLobby;
            if (GUILayout.Button("친구 초대"))
                _lobby.OpenInviteOverlay();
            GUI.enabled = running || (_lobby != null && _lobby.IsInLobby);
            if (GUILayout.Button("연결 끊기"))
                _connection.Disconnect();
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }
    }
}
