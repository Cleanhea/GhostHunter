using GhostHunter.Core;
using GhostHunter.Core.Networking;
using GhostHunter.Core.Steam;
using GhostHunter.Gameplay.Ghost;
using GhostHunter.Gameplay.Sanity;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace GhostHunter.DebugTools
{
    /// <summary>
    /// 개발용 접속 HUD. 일부러 IMGUI로 만들었다 — 씬에 Canvas/버튼을 배치하지 않아도
    /// 컴포넌트만 붙이면 바로 테스트할 수 있어야 하기 때문이다.
    /// 실제 로비 UI가 생기면 이 파일은 삭제한다.
    ///
    /// 섹션은 접이식이다. 지금 시험하는 것만 펼쳐서 화면을 어지럽히지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public class ConnectionHud : MonoBehaviour
    {
        [SerializeField] private bool _visible = true;

        [Tooltip("HUD 표시를 켜고 끄는 키.")]
        [SerializeField] private Key _toggleKey = Key.F1;

        private string _lastStatus = "대기 중";
        private IConnectionService _connection;
        private ISteamLobbyService _lobby;
        private ISanityDebug _sanityDebug;
        private IGhostDebug _ghostDebug;

        private Vector2 _scroll;
        private bool _showConnection = true;
        private bool _showSanity;
        private bool _showGhost = true;
        private bool _showPhenomena;

        private GUIStyle _boxStyle;
        private GUIStyle _richLabelStyle;
        private GUIStyle _headerStyle;

        private void Awake()
        {
            Services.TryGet(out _connection);
            Services.TryGet(out _lobby);
        }

        private void Start()
        {
            if (_connection != null)
                _connection.StatusChanged += HandleStatus;

            if (_lobby != null)
                _lobby.StatusChanged += HandleStatus;

            SceneManager.sceneLoaded += HandleSceneLoaded;
            SceneManager.sceneUnloaded += HandleSceneUnloaded;
            ResolveSceneServices();
        }

        private void OnDestroy()
        {
            if (_connection != null)
                _connection.StatusChanged -= HandleStatus;

            if (_lobby != null)
                _lobby.StatusChanged -= HandleStatus;

            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneUnloaded -= HandleSceneUnloaded;
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

            float height = Mathf.Min(760f, Screen.height - 20f);
            GUILayout.BeginArea(new Rect(10, 10, 380, height), GUIContent.none, _boxStyle);

            GUILayout.Label($"<b>GhostHunter 접속 HUD</b>   ({_toggleKey})", _richLabelStyle);
            DrawStatusLines();

            GUILayout.Space(4);
            _scroll = GUILayout.BeginScrollView(_scroll);

            _showConnection = SectionHeader("연결 · 세션", _showConnection);
            if (_showConnection)
                DrawConnectionBody();

            _showSanity = SectionHeader(SectionTitle("정신력", _sanityDebug != null), _showSanity);
            if (_showSanity)
                DrawSanityBody();

            _showGhost = SectionHeader(SectionTitle("귀신 프로토타입", _ghostDebug != null), _showGhost);
            if (_showGhost)
                DrawGhostBody();

            GUILayout.EndScrollView();

            GUILayout.Space(4);
            GUILayout.Label(_lastStatus, GUI.skin.textArea, GUILayout.MinHeight(38));

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

            _headerStyle = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleLeft,
                fontStyle = FontStyle.Bold,
                richText = true,
                margin = new RectOffset(0, 0, 3, 1),
            };
        }

        private static string SectionTitle(string name, bool ready) => ready ? name : name + "  (씬 없음)";

        private bool SectionHeader(string title, bool open)
        {
            if (GUILayout.Button((open ? "▼  " : "▶  ") + title, _headerStyle))
                open = !open;

            return open;
        }

        /// <summary>항상 보이는 Steam·세션 요약 두어 줄.</summary>
        private void DrawStatusLines()
        {
            if (_lobby == null || !_lobby.IsSteamReady)
            {
                GUILayout.Label(
                    _lobby == null
                        ? "Steam: 매니저 없음"
                        : "Steam: <color=#ff6b6b>미초기화</color>",
                    _richLabelStyle);
            }
            else
            {
                GUILayout.Label(_lobby.IsInLobby
                    ? $"Steam: {_lobby.LocalName} · 로비 {_lobby.CurrentRoomCode} ({_lobby.GetMembers().Count})"
                    : $"Steam: {_lobby.LocalName} · 로비 없음");
            }

            NetworkManager net = NetworkManager.Singleton;
            if (net == null)
            {
                GUILayout.Label("세션: NetworkManager 없음");
                return;
            }

            string role = net.IsHost ? "Host" : net.IsServer ? "Server" : net.IsClient ? "Client" : "정지";
            string detail = net.IsServer
                ? $"접속 {net.ConnectedClientsIds.Count}"
                : net.IsClient ? $"ClientId {net.LocalClientId}" : "—";
            GUILayout.Label($"세션: {role} · {detail}");
        }

        private void DrawConnectionBody()
        {
            if (_connection == null)
            {
                GUILayout.Label("ConnectionManager 없음");
                return;
            }

            bool running = _connection.IsRunning;

            GUI.enabled = !running;
            if (GUILayout.Button($"모드 전환 (현재: {_connection.Mode})"))
            {
                _connection.SetTransportMode(
                    _connection.Mode == TransportMode.Steam ? TransportMode.Local : TransportMode.Steam);
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Host"))
                _connection.StartHost();
            if (GUILayout.Button("Join (로컬)"))
                _connection.StartLocalClient();
            GUILayout.EndHorizontal();
            GUI.enabled = true;

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

        private void DrawSanityBody()
        {
            if (_sanityDebug == null)
            {
                GUILayout.Label("Game 씬의 정신력 서비스가 아직 없습니다.");
                return;
            }

            GUILayout.Label(_sanityDebug.StatusSummary, GUI.skin.textArea);

            GUI.enabled = _sanityDebug.CanControl;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("어둠 ON/OFF"))
                _sanityDebug.ToggleLocalDarkness();
            if (GUILayout.Button("귀신 이벤트 -10"))
                _sanityDebug.ApplyLocalGhostEvent();
            if (GUILayout.Button("아이템 +10"))
                _sanityDebug.RestoreLocalSanity();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("새 시체 -20"))
                _sanityDebug.WitnessNewCorpse();
            if (GUILayout.Button("같은 시체 재목격"))
                _sanityDebug.WitnessSameCorpse();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("사망 처리"))
                _sanityDebug.MarkLocalPlayerDead();
            if (GUILayout.Button("스테이지 리셋"))
                _sanityDebug.ResetLocalPlayerForStage();
            GUILayout.EndHorizontal();
            GUI.enabled = true;

            GUILayout.Label(_sanityDebug.LastStatus, GUI.skin.label);
        }

        private void DrawGhostBody()
        {
            if (_ghostDebug == null)
            {
                GUILayout.Label("Game 씬의 귀신 서비스가 아직 없습니다.");
                return;
            }

            GUILayout.Label(_ghostDebug.StatusSummary, GUI.skin.textArea);

            GUI.enabled = _ghostDebug.CanControl;

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(_ghostDebug.HasGhost ? "귀신 제거" : "귀신 스폰"))
            {
                if (_ghostDebug.HasGhost)
                    _ghostDebug.DespawnGhost();
                else
                    _ghostDebug.SpawnGhost();
            }

            bool noGhost = !_ghostDebug.HasGhost;
            GUI.enabled = _ghostDebug.CanControl && noGhost;
            if (GUILayout.Button("내 위치에 스폰"))
                _ghostDebug.SpawnGhostAtPlayer();
            GUI.enabled = _ghostDebug.CanControl;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("활동 강제"))
                _ghostDebug.ToggleForceActive();
            if (GUILayout.Button(_ghostDebug.IsGhostForcedVisible ? "본체 숨기기" : "본체 보이기"))
                _ghostDebug.ToggleGhostVisible();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("어택 강제"))
                _ghostDebug.ForceSpecialAttack();
            if (GUILayout.Button("강제 진정"))
                _ghostDebug.ForceSuppression();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("청소 +10%"))
                _ghostDebug.AddCleaningProgress(10);
            if (GUILayout.Button("청소 리셋"))
                _ghostDebug.ResetCleaningProgress();
            GUILayout.EndHorizontal();

            _showPhenomena = SectionHeader("초자연현상 (§6)", _showPhenomena);
            if (_showPhenomena)
                DrawPhenomenonButtons();

            GUI.enabled = true;

            GUILayout.Label(_ghostDebug.LastStatus, GUI.skin.label);
        }

        private static readonly (GhostPhenomenonKind Kind, string Label)[] PhenomenonButtons =
        {
            (GhostPhenomenonKind.ObjectShake, "1 흔들기"),
            (GhostPhenomenonKind.SmallObjectDrop, "2 떨어뜨림"),
            (GhostPhenomenonKind.DoorMove, "3 문"),
            (GhostPhenomenonKind.DrawerOpen, "4 서랍"),
            (GhostPhenomenonKind.LightFlicker, "5 조명"),
            (GhostPhenomenonKind.WallKnock, "6 두드림"),
            (GhostPhenomenonKind.Footsteps, "7 발소리"),
            (GhostPhenomenonKind.Apparition, "8 출현"),
        };

        private void DrawPhenomenonButtons()
        {
            GUILayout.Label("상태·주기 무관 즉시 실행 · 귀신 반경 6m · 6·7 무음(오디오 대기)",
                GUI.skin.label);

            if (GUILayout.Button("랜덤 (직전 제외 Pool)"))
                _ghostDebug.ForcePhenomenon();

            const int perRow = 4;
            for (int i = 0; i < PhenomenonButtons.Length; i++)
            {
                if (i % perRow == 0)
                    GUILayout.BeginHorizontal();

                (GhostPhenomenonKind kind, string label) = PhenomenonButtons[i];
                if (GUILayout.Button(label))
                    _ghostDebug.ForcePhenomenon(kind);

                if (i % perRow == perRow - 1 || i == PhenomenonButtons.Length - 1)
                    GUILayout.EndHorizontal();
            }
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode) => ResolveSceneServices();

        private void HandleSceneUnloaded(Scene scene) => ResolveSceneServices();

        private void ResolveSceneServices()
        {
            Services.TryGet(out _sanityDebug);
            Services.TryGet(out _ghostDebug);
        }
    }
}
