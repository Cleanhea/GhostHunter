using GhostHunter.Core;
using GhostHunter.Core.Networking;
using GhostHunter.Core.Steam;
using GhostHunter.Gameplay.Ghost;
using GhostHunter.Gameplay.Player;
using GhostHunter.Gameplay.Sanity;
using Unity.Netcode;
using UnityEngine;
using GhostHunter.Gameplay.Cleaning;
using GhostHunter.Gameplay.Furniture;
using GhostHunter.Gameplay.Lighting;
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

        [Tooltip("HUD가 켜져 있을 때 가구 위에 내구도를 띄우는 최대 거리(m). 벽 뒤 가구도 표시한다.")]
        [SerializeField, Min(1f)] private float _durabilityLabelRange = 20f;

        private string _lastStatus = "대기 중";
        private IConnectionService _connection;
        private ISteamLobbyService _lobby;
        private ISanityDebug _sanityDebug;
        private IGhostDebug _ghostDebug;
        private ILocalPlayerContext _localPlayer;
        private ICleaningService _cleaning;
        private IStageLightingDebug _lighting;

        private readonly TuningHud _tuning = new();

        private Vector2 _scroll;
        private bool _showConnection = true;
        private bool _showSanity;
        private bool _showGhost = true;
        private bool _showPhenomena;
        private bool _showSkill;
        private bool _showCleaning = true;
        private bool _showFurniture = true;
        private bool _showThrow = true;
        private bool _showLighting;
        private bool _showLightingRooms;
        private bool _showLightingValues = true;
        private bool _showDurabilityLabels = true;

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

            // HUD 창보다 먼저 그려야 창이 라벨 위에 덮인다.
            if (_visible && _showDurabilityLabels)
                DrawDurabilityLabels();

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

            _showCleaning = SectionHeader(SectionTitle("청소", _cleaning != null), _showCleaning);
            if (_showCleaning)
                DrawCleaningBody();

            _showFurniture = SectionHeader("가구 내구도", _showFurniture);
            if (_showFurniture)
                DrawFurnitureBody();

            _showThrow = SectionHeader("가구 투척 힘", _showThrow);
            if (_showThrow)
                DrawThrowBody();

            _showLighting = SectionHeader(SectionTitle("조명", _lighting != null), _showLighting);
            if (_showLighting)
                DrawLightingBody();

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

        private void DrawFurnitureBody()
        {
            var targeter = _localPlayer != null ? _localPlayer.Targeter : null;
            var target = targeter != null ? targeter.CurrentTarget : null;
            if (target != null && target.TryGetComponent(out FurnitureNetworkPhysics physics))
                GUILayout.Label($"{target.name}: {physics.Durability} / {FurnitureNetworkPhysics.FullDurability}");
            else
                GUILayout.Label("가구를 조준하면 현재 내구도가 표시됩니다.");

            bool wasEnabled = GUI.enabled;
            NetworkManager network = NetworkManager.Singleton;
            GUI.enabled = wasEnabled && network != null && network.IsServer;
            if (GUILayout.Button("전체 내구도 100 복구 (파손 가구 포함)"))
                FurnitureNetworkPhysics.ServerResetAll(false);
            GUI.enabled = wasEnabled;
            GUILayout.Label("R: 배치 위치로 복귀 + 내구도 복구 · 호스트 전용");
            _showDurabilityLabels = GUILayout.Toggle(_showDurabilityLabels, " 가구 위에 내구도 표시 (HUD 켜진 동안)");
        }

        // 미리보기 줄의 누른 시간(초). 0.1초 = 살짝 눌렀다 뗀 탭.
        private static readonly float[] ThrowPreviewSeconds = { 0.1f, 0.3f, 0.6f };

        /// <summary>
        /// 날리는 힘(throw-system.md §4·§5) — 누른 시간별 실제 발사 속도 미리보기와 FurnitureThrowSettings 의
        /// "차징 / 발사" 값 줄. 값은 튜닝 창(F2)과 같은 SO 라 즉시 적용되고, 발사는 서버가 하므로 호스트의 값이 쓰인다.
        /// </summary>
        private void DrawThrowBody()
        {
            if (!_tuning.TryGetSettings("투척", out FurnitureThrowSettings settings))
            {
                GUILayout.Label("세션을 시작하면 FurnitureThrowSettings 를 읽어 옵니다.");
                return;
            }

            GUILayout.Label("누른 시간 → 힘 · 발사 속도 (1인 / 2인, m/s)", GUI.skin.label);
            foreach (float seconds in ThrowPreviewSeconds)
            {
                if (seconds < settings.ChargeTime)
                    DrawThrowPreview($"{seconds:0.0}초", seconds / settings.ChargeTime, settings);
            }

            DrawThrowPreview($"끝까지 {settings.ChargeTime:0.0#}초", 1f, settings);
            GUILayout.Label($"무거운 가구를 혼자 던지면 ×{settings.HeavySoloMultiplier:0.##}", GUI.skin.label);

            GUILayout.Space(3);
            GUILayout.Label("수치 — 즉시 적용 · 호스트 값으로 발사 · 튜닝 창(F2)과 같은 값", GUI.skin.label);
            _tuning.DrawInline("투척", group: "차징 / 발사");
        }

        private static void DrawThrowPreview(string label, float charge, FurnitureThrowSettings settings)
        {
            float ratio = settings.ForceRatio(charge);
            GUILayout.Label(
                $"  {label,-10}  힘 {ratio * 100f,3:0}%   →   {settings.OneHolderForce * ratio:0.0} / " +
                $"{settings.TwoHolderForce * ratio:0.0}",
                GUI.skin.label);
        }

        private const float DurabilityLabelWidth = 84f;
        private const float DurabilityLabelHeight = 24f;
        private const float DurabilityLabelLift = 0.2f;
        private static readonly Color DurabilityLowColor = new(1f, 0.35f, 0.3f);
        private static readonly Color DurabilityHighColor = new(0.45f, 1f, 0.45f);
        private static string[] _durabilityTexts;
        private GUIStyle _durabilityLabelStyle;

        /// <summary>
        /// HUD가 켜진 동안 반경 안 가구의 머리 위에 복제된 내구도를 그린다. 모든 접속자에서 같은 값이 보인다.
        /// OnGUI 는 프레임마다 여러 번 불리므로 Repaint 에서만 그리고, 가구 수만큼 문자열을 새로 만들지 않는다.
        /// </summary>
        private void DrawDurabilityLabels()
        {
            if (Event.current.type != EventType.Repaint || _localPlayer == null)
                return;

            var targeter = _localPlayer.Targeter;
            Camera view = targeter != null ? targeter.AimCamera : null;
            if (view == null)
                return;

            _durabilityLabelStyle ??= new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = 13,
            };

            Vector3 eye = view.transform.position;
            float rangeSqr = _durabilityLabelRange * _durabilityLabelRange;
            Color previousColor = GUI.contentColor;
            var furniture = FurnitureNetworkPhysics.All;
            for (int i = 0; i < furniture.Count; i++)
            {
                FurnitureNetworkPhysics item = furniture[i];
                if (item == null || !item.IsAvailable
                    || (item.transform.position - eye).sqrMagnitude > rangeSqr
                    || !item.TryGetTopCenter(out Vector3 top))
                    continue;

                Vector3 screen = view.WorldToScreenPoint(top + Vector3.up * DurabilityLabelLift);
                if (screen.z <= 0f)
                    continue;

                int durability = Mathf.Clamp(item.Durability, 0, FurnitureNetworkPhysics.FullDurability);
                GUI.contentColor = Color.Lerp(DurabilityLowColor, DurabilityHighColor,
                    durability / (float)FurnitureNetworkPhysics.FullDurability);
                var rect = new Rect(screen.x - DurabilityLabelWidth * 0.5f,
                    Screen.height - screen.y - DurabilityLabelHeight, DurabilityLabelWidth, DurabilityLabelHeight);
                GUI.Label(rect, DurabilityText(durability), _durabilityLabelStyle);
            }

            GUI.contentColor = previousColor;
        }

        private static string DurabilityText(int durability)
        {
            if (_durabilityTexts == null)
            {
                _durabilityTexts = new string[FurnitureNetworkPhysics.FullDurability + 1];
                for (int i = 0; i < _durabilityTexts.Length; i++)
                    _durabilityTexts[i] = "내구도 " + i;
            }

            return _durabilityTexts[durability];
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
                GUILayout.Label("스테이지 씬의 정신력 서비스가 아직 없습니다.");
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

            if (GUILayout.Button("원격 플레이어 1명 사망 처리 (봇 검증)"))
                _sanityDebug.MarkNextRemotePlayerDead();

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
                GUILayout.Label("이 씬에는 귀신 서비스가 없습니다.");
                return;
            }

            GUILayout.Label(_ghostDebug.StatusSummary, GUI.skin.textArea);
            GUILayout.Label("AI 실험 토글: F2 → 귀신 → AI experiments (Host)", GUI.skin.label);

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
            if (GUILayout.Button("활동 상태 확인"))
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

        private void DrawCleaningBody()
        {
            if (_cleaning == null)
            {
                GUILayout.Label("이 씬에는 청소 시스템이 없습니다.");
                return;
            }
            GUILayout.Label($"남은 얼룩: {_cleaning.DirtyCount}개");
            GUILayout.Label("Tab → 대걸레 선택 → 얼룩 조준 후 좌클릭");
            bool previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && _cleaning.CanReset;
            if (GUILayout.Button("얼룩 초기화 · 랜덤 재배치"))
                _cleaning.ResetStains();
            GUI.enabled = previousEnabled;
            GUILayout.Label(_cleaning.Status);
        }

        /// <summary>
        /// 층·방 천장등 스위치 + 밝기 값(튜닝 창과 같은 SO). 스위치는 로컬 전용이라
        /// 호스트가 아니어도 누를 수 있고, 다른 접속자 화면은 바뀌지 않는다.
        /// </summary>
        private void DrawLightingBody()
        {
            if (_lighting == null)
            {
                GUILayout.Label("이 씬에는 스테이지 조명이 없습니다 (Stage1 전용).");
                return;
            }

            GUILayout.Label(_lighting.StatusSummary, GUI.skin.textArea);

            DrawDarknessRows();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("전부 켜기"))
                _lighting.SetAllOn(true);
            if (GUILayout.Button("전부 끄기"))
                _lighting.SetAllOn(false);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            for (int floor = 0; floor < _lighting.FloorCount; floor++)
            {
                bool on = _lighting.IsFloorOn(floor);
                bool next = GUILayout.Toggle(on, " " + _lighting.FloorName(floor));
                if (next != on)
                    _lighting.SetFloorOn(floor, next);
            }
            GUILayout.EndHorizontal();

            _showLightingRooms = SectionHeader($"방별 스위치 ({_lighting.Lights.Count})", _showLightingRooms);
            if (_showLightingRooms)
                DrawLightingRooms();

            _showLightingValues = SectionHeader("밝기 · 그림자 · 해 · 환경광", _showLightingValues);
            if (_showLightingValues && !_tuning.DrawInline("조명"))
                GUILayout.Label("StageLightingSettings 를 찾는 중입니다.");

            GUILayout.Label("로컬 전용 · 값은 튜닝 창(F2)과 같다 · 영구 반영은 인스펙터", GUI.skin.label);
        }

        /// <summary>불 꺼진 집의 어둠(환경광·검은 안개) — 가장 자주 만지는 값이라 섹션 맨 위에 둔다.</summary>
        private void DrawDarknessRows()
        {
            float ambient = _lighting.AmbientScale;
            float nextAmbient = DrawSliderRow(
                $"환경광 ×{ambient:0.00}", ambient, StageLightingSettings.AmbientScaleMax, 0.05f, 0.1f, 1f, "1");
            if (!Mathf.Approximately(nextAmbient, ambient))
                _lighting.AmbientScale = nextAmbient;

            bool fogOn = _lighting.FogOn;
            float density = _lighting.FogDensity;
            GUILayout.BeginHorizontal();
            bool nextFogOn = GUILayout.Toggle(fogOn, " 검은 안개", GUILayout.Width(90f));
            GUI.enabled = fogOn;
            float nextDensity = DrawSliderControls(density, StageLightingSettings.FogDensityMax, 0.005f, 0.01f, 0.025f, "기본");
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.Label(fogOn
                ? $"    밀도 {density:0.000} — 8m {FogVisible(density, 8f):0%} · 16m {FogVisible(density, 16f):0%} 보임"
                : "    안개 꺼짐 — 거리와 무관하게 환경광만큼 보인다");

            if (nextFogOn != fogOn)
                _lighting.FogOn = nextFogOn;
            if (!Mathf.Approximately(nextDensity, density))
                _lighting.FogDensity = nextDensity;
        }

        // Exponential Squared: 남는 비율 = exp(-(밀도 × 거리)²).
        private static float FogVisible(float density, float distance) =>
            Mathf.Exp(-(density * distance) * (density * distance));

        private static float DrawSliderRow(
            string label, float value, float max, float snap, float step, float reset, string resetLabel)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(90f));
            float next = DrawSliderControls(value, max, snap, step, reset, resetLabel);
            GUILayout.EndHorizontal();
            return next;
        }

        private static float DrawSliderControls(
            float value, float max, float snap, float step, float reset, string resetLabel)
        {
            float next = GUILayout.HorizontalSlider(value, 0f, max, GUILayout.MinWidth(120f));
            // 끌어서 바꾼 값만 snap 단위로 맞춘다 — 그리기만 해서는 값이 바뀌지 않는다.
            if (!Mathf.Approximately(next, value))
                next = Mathf.Round(next / snap) * snap;
            if (GUILayout.Button("−", GUILayout.Width(24f)))
                next = value - step;
            if (GUILayout.Button("+", GUILayout.Width(24f)))
                next = value + step;
            if (GUILayout.Button(resetLabel, GUILayout.ExpandWidth(false)))
                next = reset;
            return Mathf.Clamp(next, 0f, max);
        }

        private void DrawLightingRooms()
        {
            var lights = _lighting.Lights;
            for (int floor = 0; floor < _lighting.FloorCount; floor++)
            {
                GUILayout.Label($"<b>{_lighting.FloorName(floor)}</b>", _richLabelStyle);
                for (int i = 0; i < lights.Count; i++)
                {
                    StageRoomLight light = lights[i];
                    if (light == null || light.Floor != floor)
                        continue;

                    bool next = GUILayout.Toggle(light.SwitchOn, " " + light.Label);
                    if (next != light.SwitchOn)
                        _lighting.SetLightOn(i, next);
                }
            }
        }

        private void HandleSceneUnloaded(Scene scene) => ResolveSceneServices();

        private void ResolveSceneServices()
        {
            Services.TryGet(out _sanityDebug);
            Services.TryGet(out _ghostDebug);
            Services.TryGet(out _localPlayer);
            Services.TryGet(out _cleaning);
            Services.TryGet(out _lighting);
        }
    }
}
