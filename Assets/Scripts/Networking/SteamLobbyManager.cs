using System;
using System.Threading.Tasks;
using Steamworks;
using Steamworks.Data;
using UnityEngine;

namespace GhostHunter.Networking
{
    /// <summary>
    /// Steam 수명주기(Init / RunCallbacks / Shutdown)와 로비를 소유한다.
    ///
    /// 이 컴포넌트가 Steam을 초기화하는 이유: 로비 생성/참가는 <see cref="ConnectionManager"/>가
    /// StartHost/StartClient를 부르기 <b>전에</b> 끝나 있어야 하는데, FacepunchTransport는
    /// StartHost 시점에야 Steam을 초기화하기 때문이다. 트랜스포트 쪽은 "이미 유효하면 건너뛰도록"
    /// 패치되어 있다 (Packages/com.community.netcode.transport.facepunch/PATCHES.md 참조).
    ///
    /// 네트워크 시작은 여기서 하지 않는다. 이벤트로 알리기만 하고 실제 StartHost/StartClient는
    /// ConnectionManager가 한다 — Steam 관심사와 Netcode 관심사를 섞지 않기 위해서다.
    /// </summary>
    [DisallowMultipleComponent]
    public class SteamLobbyManager : MonoBehaviour
    {
        /// <summary>Valve의 공개 테스트 앱(Spacewar). 자체 App ID를 받기 전까지 사용한다.</summary>
        public const uint SpacewarAppId = 480;

        /// <summary>로비 데이터에 호스트 SteamId를 담는 키.</summary>
        public const string HostSteamIdKey = "gh_host_steam_id";

        [Header("Steam")]
        [SerializeField] private uint _appId = SpacewarAppId;

        [Header("Lobby")]
        [SerializeField] private int _maxLobbyMembers = 2;
        [Tooltip("켜면 친구 목록에 노출되는 공개 로비, 끄면 초대로만 참가 가능.")]
        [SerializeField] private bool _friendsOnly = true;

        public static SteamLobbyManager Instance { get; private set; }

        public bool IsSteamReady => SteamClient.IsValid;
        public SteamId LocalSteamId => SteamClient.IsValid ? SteamClient.SteamId : default;
        public string LocalName => SteamClient.IsValid ? SteamClient.Name : "(Steam 미연결)";
        public Lobby? CurrentLobby { get; private set; }
        public bool IsInLobby => CurrentLobby.HasValue;

        /// <summary>사람이 읽는 진행 상황. 개발용 HUD가 그대로 표시한다.</summary>
        public event Action<string> StatusChanged;

        /// <summary>호스트로서 로비 준비 완료. 이제 StartHost 해도 된다.</summary>
        public event Action HostLobbyReady;

        /// <summary>참가자로서 접속할 호스트 SteamId 확보. 이제 StartClient 해도 된다.</summary>
        public event Action<SteamId> JoinTargetResolved;

        public event Action LobbyLeft;

        private bool _ownsSteamClient;
        private bool _callbacksSubscribed;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            InitializeSteam();
        }

        private void Update()
        {
            // asyncCallbacks: false 로 초기화했으므로 콜백 펌핑은 우리 몫이다.
            // 이걸 빠뜨리면 로비 콜백이 영원히 오지 않는다 - 가장 흔한 함정.
            if (SteamClient.IsValid)
                SteamClient.RunCallbacks();
        }

        private void OnDestroy()
        {
            if (Instance != this)
                return;

            UnsubscribeCallbacks();
            LeaveLobby();

            if (_ownsSteamClient)
            {
                SteamClient.Shutdown();
                _ownsSteamClient = false;
            }

            Instance = null;
        }

        #region Steam 초기화

        private void InitializeSteam()
        {
            if (SteamClient.IsValid)
            {
                // 다른 무언가(예: 트랜스포트)가 먼저 초기화한 경우. 종료 책임도 그쪽에 있다.
                _ownsSteamClient = false;
                SubscribeCallbacks();
                SetStatus($"Steam 이미 초기화됨 - {SteamClient.Name}");
                return;
            }

            try
            {
                SteamClient.Init(_appId, false);
                _ownsSteamClient = true;
            }
            catch (Exception e)
            {
                // 거의 항상 원인은 셋 중 하나다. 추측하게 두지 말고 다 적어준다.
                Debug.LogError(
                    $"[SteamLobbyManager] Steam 초기화 실패 (AppId {_appId}): {e.Message}\n" +
                    "확인할 것:\n" +
                    "  1. Steam 클라이언트가 실행 중이고 로그인되어 있는가\n" +
                    "  2. 프로젝트 루트(빌드에서는 exe 옆)에 steam_appid.txt 가 있는가\n" +
                    "  3. 해당 AppId를 이 계정이 소유하고 있는가 (480은 누구나 가능)\n" +
                    "Steam 없이 로직만 테스트하려면 ConnectionManager의 Local(UTP) 모드를 쓰세요.");
                return;
            }

            SubscribeCallbacks();
            SetStatus($"Steam 준비 완료 - {SteamClient.Name} ({SteamClient.SteamId})");
        }

        private void SubscribeCallbacks()
        {
            if (_callbacksSubscribed)
                return;

            SteamMatchmaking.OnLobbyCreated += HandleLobbyCreated;
            SteamMatchmaking.OnLobbyEntered += HandleLobbyEntered;
            SteamMatchmaking.OnLobbyMemberJoined += HandleLobbyMemberJoined;
            SteamMatchmaking.OnLobbyMemberLeave += HandleLobbyMemberLeave;
            SteamFriends.OnGameLobbyJoinRequested += HandleGameLobbyJoinRequested;

            _callbacksSubscribed = true;
        }

        private void UnsubscribeCallbacks()
        {
            if (!_callbacksSubscribed)
                return;

            SteamMatchmaking.OnLobbyCreated -= HandleLobbyCreated;
            SteamMatchmaking.OnLobbyEntered -= HandleLobbyEntered;
            SteamMatchmaking.OnLobbyMemberJoined -= HandleLobbyMemberJoined;
            SteamMatchmaking.OnLobbyMemberLeave -= HandleLobbyMemberLeave;
            SteamFriends.OnGameLobbyJoinRequested -= HandleGameLobbyJoinRequested;

            _callbacksSubscribed = false;
        }

        #endregion

        #region 로비 조작

        /// <summary>로비를 만든다. 성공하면 <see cref="HostLobbyReady"/>가 발생한다.</summary>
        public async Task CreateLobbyAsync()
        {
            if (!RequireSteam())
                return;

            if (IsInLobby)
            {
                SetStatus("이미 로비에 있습니다. 먼저 나가세요.");
                return;
            }

            SetStatus("로비 생성 중...");

            Lobby? lobby = await SteamMatchmaking.CreateLobbyAsync(_maxLobbyMembers);

            // 실제 설정과 HostLobbyReady 발생은 OnLobbyCreated 콜백에서 처리한다.
            // 여기서는 즉시 실패만 잡는다.
            if (!lobby.HasValue)
                SetStatus("로비 생성 실패 (Steam이 null 반환)");
        }

        /// <summary>친구 초대 오버레이를 연다. 로비에 있어야 한다.</summary>
        public void OpenInviteOverlay()
        {
            if (!RequireSteam())
                return;

            if (!CurrentLobby.HasValue)
            {
                SetStatus("로비가 없습니다. 먼저 Host를 누르세요.");
                return;
            }

            SteamFriends.OpenGameInviteOverlay(CurrentLobby.Value.Id);
            SetStatus("초대 오버레이를 열었습니다.");
        }

        /// <summary>로비 ID로 직접 참가. 성공하면 <see cref="JoinTargetResolved"/>가 발생한다.</summary>
        public async Task JoinLobbyAsync(SteamId lobbyId)
        {
            if (!RequireSteam())
                return;

            SetStatus($"로비 참가 중... ({lobbyId})");

            var lobby = new Lobby(lobbyId);
            RoomEnter result = await lobby.Join();

            if (result != RoomEnter.Success)
            {
                SetStatus($"로비 참가 실패: {result}");
                return;
            }

            // 성공 시 OnLobbyEntered 콜백이 이어서 처리한다.
        }

        public void LeaveLobby()
        {
            if (!CurrentLobby.HasValue)
                return;

            CurrentLobby.Value.Leave();
            CurrentLobby = null;

            SetStatus("로비에서 나갔습니다.");
            LobbyLeft?.Invoke();
        }

        #endregion

        #region Steam 콜백

        private void HandleLobbyCreated(Result result, Lobby lobby)
        {
            if (result != Result.OK)
            {
                SetStatus($"로비 생성 실패: {result}");
                return;
            }

            if (_friendsOnly)
                lobby.SetFriendsOnly();
            else
                lobby.SetPublic();

            lobby.SetJoinable(true);

            // 참가자가 "누구에게 P2P 연결할지" 알아내는 경로. Lobby.Owner로도 알 수 있지만
            // 오너 정보가 아직 복제되지 않은 타이밍이 있어 명시적으로 심어둔다.
            lobby.SetData(HostSteamIdKey, SteamClient.SteamId.Value.ToString());

            CurrentLobby = lobby;

            SetStatus($"로비 생성 완료 ({lobby.Id}). 친구를 초대하세요.");
            HostLobbyReady?.Invoke();
        }

        private void HandleLobbyEntered(Lobby lobby)
        {
            CurrentLobby = lobby;

            // 호스트 자신도 자기 로비에 들어오면서 이 콜백을 받는다. 그 경우는 무시한다.
            // (SteamId 끼리 == 비교는 ulong 암시적 변환에 의존하므로 Value 로 명시 비교한다.)
            if (lobby.Owner.Id.Value == SteamClient.SteamId.Value)
                return;

            SteamId hostId = ResolveHostSteamId(lobby);

            if (hostId.Value == 0)
            {
                SetStatus("로비에 들어갔지만 호스트 SteamId를 알아내지 못했습니다.");
                return;
            }

            SetStatus($"로비 입장 ({lobby.Id}). 호스트 {hostId} 에 접속합니다.");
            JoinTargetResolved?.Invoke(hostId);
        }

        /// <summary>로비 데이터를 우선 쓰고, 없으면 로비 오너로 폴백한다.</summary>
        private static SteamId ResolveHostSteamId(Lobby lobby)
        {
            string raw = lobby.GetData(HostSteamIdKey);

            if (!string.IsNullOrEmpty(raw) && ulong.TryParse(raw, out ulong parsed) && parsed != 0)
                return parsed;

            return lobby.Owner.Id;
        }

        private void HandleLobbyMemberJoined(Lobby lobby, Friend friend)
        {
            SetStatus($"{friend.Name} 님이 로비에 참가했습니다. ({lobby.MemberCount}/{_maxLobbyMembers})");
        }

        private void HandleLobbyMemberLeave(Lobby lobby, Friend friend)
        {
            SetStatus($"{friend.Name} 님이 로비를 떠났습니다. ({lobby.MemberCount}/{_maxLobbyMembers})");
        }

        /// <summary>친구 목록/오버레이에서 "게임 참가"를 눌렀을 때.</summary>
        private async void HandleGameLobbyJoinRequested(Lobby lobby, SteamId invitedBy)
        {
            SetStatus($"{invitedBy} 의 초대를 수락합니다...");

            try
            {
                RoomEnter result = await lobby.Join();

                if (result != RoomEnter.Success)
                    SetStatus($"초대 수락 실패: {result}");
            }
            catch (Exception e)
            {
                // async void라 예외를 여기서 안 잡으면 조용히 사라진다.
                Debug.LogError($"[SteamLobbyManager] 초대 수락 중 예외: {e}");
            }
        }

        #endregion

        private bool RequireSteam()
        {
            if (SteamClient.IsValid)
                return true;

            SetStatus("Steam이 초기화되지 않았습니다. Steam 클라이언트를 켜고 다시 실행하세요.");
            return false;
        }

        private void SetStatus(string message)
        {
            Debug.Log($"[SteamLobbyManager] {message}");
            StatusChanged?.Invoke(message);
        }
    }
}
