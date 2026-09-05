using GhostHunter.Core;
using GhostHunter.Core.Networking;
using GhostHunter.Core.Steam;
using GhostHunter.Gameplay.Ghost;
using GhostHunter.Gameplay.Player;
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
        [SerializeField] private Key _toggleKey = Key.Tab;

        [Tooltip("밸런스 튜닝 창(별도)을 켜고 끄는 키.")]
        [SerializeField] private Key _tuningToggleKey = Key.F2;

        private string _lastStatus = "대기 중";
        private IConnectionService _connection;
        private ISteamLobbyService _lobby;
        private ISanityDebug _sanityDebug;
        private IGhostDebug _ghostDebug;
        private ILocalPlayerContext _localPlayer;

        private readonly TuningHud _tuning = new();

        private Vector2 _scroll;
        private bool _showConnection = true;
        private bool _showSanity;
        private bool _showGhost = true;
        private bool _showPhenomena;
        private bool _showSkill;

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
            if (keyboard == null)
                return;

            if (keyboard[_toggleKey].wasPressedThisFrame)
                _visible = !_visible;

            if (keyboard[_tuningToggleKey].wasPressedThisFrame)
                _tuning.ToggleVisible();
        }

        private void HandleStatus(string message) => _lastStatus = message;

        /// <summary>메뉴 씬처럼 실제 UI가 있는 곳에서는 부트스트랩이 끈다. 토글 키는 계속 동작한다.</summary>
        public void SetVisible(bool visible) => _visible = visible;

        private void OnGUI()
        {
            EnsureStyles();

            // 튜닝 창은 접속 HUD(Tab)와 독립이다 — HUD가 꺼져 있어도 F2로 열 수 있다.
            _tuning.DrawWindow();

            if (!_visible)
                return;

            float height = Mathf.Min(760f, Screen.height - 20f);
            GUILayout.BeginArea(new Rect(10, 10, 380, height), GUIContent.none, _boxStyle);

            GUILayout.Label(
                $"<b>GhostHunter 접속 HUD</b>   ({_toggleKey})   ·   튜닝 창 {_tuningToggleKey}",
                _richLabelStyle);
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

            _showSkill = SectionHeader(
                SectionTitle("두더지 스킬", LocalBurrow != null || LocalDetection != null),
                _showSkill);
            if (_showSkill)
                DrawSkillBody();

            if (GUILayout.Button(
                    (_tuning.Visible ? "▼" : "▶") + $"  튜닝 창 (밸런스 값) — {_tuningToggleKey}",
                    _headerStyle))
            {
                _tuning.ToggleVisible();
            }

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

            DrawSanitySliders();

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
            if (GUILayout.Button("부활"))
                _sanityDebug.ReviveLocalPlayer();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("팀 전원 부활"))
                _sanityDebug.ReviveTeam();
            if (GUILayout.Button("스테이지 리셋"))
                _sanityDebug.ResetLocalPlayerForStage();
            GUILayout.EndHorizontal();
            GUI.enabled = true;

            GUILayout.Label(_sanityDebug.LastStatus, GUI.skin.label);
        }

        /// <summary>
        /// 개인 정신력과 팀 전체 정신력을 0~100 사이 임의 값으로 바로 맞추는 슬라이더 두 줄.
        /// 팀 줄은 호스트(서버)에서 연결된 모든 플레이어에 일괄 적용한다.
        /// </summary>
        private void DrawSanitySliders()
        {
            int min = _sanityDebug.SanityMinimum;
            int max = _sanityDebug.SanityMaximum;

            float mine = _sanityDebug.TryGetLocalSanity(out int localSanity) ? localSanity : min;
            if (SanitySliderRow("내 정신력", mine, min, max, out int myTarget))
                _sanityDebug.SetLocalSanity(myTarget);

            float team = _sanityDebug.TryGetTeamSanity(out int teamAverage) ? teamAverage : min;
            if (SanitySliderRow("팀 전체", team, min, max, out int teamTarget))
                _sanityDebug.SetTeamSanity(teamTarget);
        }

        /// <summary>슬라이더 한 줄. 이번 프레임에 드래그로 값이 바뀌었으면 true 와 정수 목표값을 돌려준다.</summary>
        private static bool SanitySliderRow(string label, float current, int min, int max, out int target)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(66));

            GUI.changed = false;
            float slid = GUILayout.HorizontalSlider(current, min, max, GUILayout.MinWidth(150));
            bool draggedThisFrame = GUI.changed;

            GUILayout.Label($"{Mathf.RoundToInt(slid)}%", GUILayout.Width(38));
            GUILayout.EndHorizontal();

            target = Mathf.RoundToInt(slid);
            return draggedThisFrame && target != Mathf.RoundToInt(current);
        }

        /// <summary>로컬 소유자의 굴착 컨트롤러. 세션 시작 전이거나 스폰 전이면 null 이다.</summary>
        private IMoleSkillDebug LocalBurrow
        {
            get
            {
                MoleBurrowController controller = _localPlayer?.BurrowController;

                // Unity 의 == 오버로드(파괴된 객체를 null 로 보는 것)는 인터페이스로 올리는 순간
                // 사라진다. 구체 타입인 채로 먼저 걸러야 씬을 내린 뒤 죽은 참조를 붙잡지 않는다.
                return controller != null ? controller : null;
            }
        }

        /// <summary>로컬 소유자의 탐지 디버그 창구. 세션 시작 전에는 null 이다.</summary>
        private IMoleSkillDebug LocalDetection
        {
            get
            {
                DetectionSkillController controller = _localPlayer?.DetectionController;
                return controller != null ? controller : null;
            }
        }

        /// <summary>
        /// 굴착 상태·강제 조작 + <b>수치 조절</b>. 수치 줄은 튜닝 창(F2)의 렌더러를 그대로 불러
        /// 같은 SO 를 만진다 — 5초 유지·10초 쿨타임을 매번 기다리지 않고 값을 굴려 볼 수 있도록
        /// 상태 버튼과 한자리에 뒀다.
        /// </summary>
        private void DrawSkillBody()
        {
            IMoleSkillDebug burrow = LocalBurrow;

            GUILayout.Label("<b>굴착</b>   ·   T   ·   기획서 §5", _richLabelStyle);

            if (burrow == null)
            {
                GUILayout.Label("로컬 플레이어가 아직 스폰되지 않았습니다. Host 로 세션을 시작하세요.");
            }
            else
            {
                GUILayout.Label(burrow.StatusSummary, GUI.skin.textArea);

                GUI.enabled = burrow.CanControl;
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("굴착 시작"))
                    burrow.ForceStart();
                if (GUILayout.Button("즉시 종료"))
                    burrow.ForceEnd();
                if (GUILayout.Button("쿨타임 리셋"))
                    burrow.ResetCooldown();
                GUILayout.EndHorizontal();
                GUI.enabled = true;
            }

            GUILayout.Space(3);
            GUILayout.Label("수치 — 즉시 적용 · 튜닝 창(F2)과 같은 값", GUI.skin.label);

            if (!_tuning.DrawInline("굴착"))
                GUILayout.Label("세션을 시작하면 MoleBurrowSettings 를 읽어 옵니다.");

            GUILayout.Space(5);
            GUILayout.Label("<b>탐지</b>   ·   Q   ·   기획서 §4", _richLabelStyle);

            IMoleSkillDebug detection = LocalDetection;
            if (detection == null)
            {
                GUILayout.Label("로컬 플레이어가 아직 스폰되지 않았습니다. Host 로 세션을 시작하세요.");
            }
            else
            {
                GUILayout.Label(detection.StatusSummary, GUI.skin.textArea);

                GUI.enabled = detection.CanControl;
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("탐지 시작"))
                    detection.ForceStart();
                if (GUILayout.Button("즉시 종료"))
                    detection.ForceEnd();
                if (GUILayout.Button("쿨타임 리셋"))
                    detection.ResetCooldown();
                GUILayout.EndHorizontal();
                GUI.enabled = true;
            }

            GUILayout.Space(3);
            GUILayout.Label("수치 — 즉시 적용 · 튜닝 창(F2)과 같은 값", GUI.skin.label);
            if (!_tuning.DrawInline("탐지"))
                GUILayout.Label("세션을 시작하면 DetectionSkillSettings 를 읽어 옵니다.");

            GUILayout.Space(3);
            GUILayout.Label("대상은 DetectionTargetMarker 가 붙고 활성화된 오브젝트만 표시합니다. " +
                "작업 시스템의 판정 기준은 MS-5로 남아 있습니다.", GUI.skin.label);
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
            Services.TryGet(out _localPlayer);
        }
    }
}
