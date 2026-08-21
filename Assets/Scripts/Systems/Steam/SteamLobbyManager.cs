using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using GhostHunter.Core.Steam;
using GhostHunter.Networking;
using Netcode.Transports.Facepunch;
using Steamworks;
using Steamworks.Data;
using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Systems.Steam
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
    public class SteamLobbyManager : MonoBehaviour, ISteamLobbyService
    {
        /// <summary>Valve의 공개 테스트 앱(Spacewar). 자체 App ID를 받기 전까지 사용한다.</summary>
        public const uint SpacewarAppId = 480;

        /// <summary>로비 데이터에 호스트 SteamId를 담는 키.</summary>
        public const string HostSteamIdKey = "gh_host_steam_id";

        /// <summary>로비 데이터에 방 코드를 담는 키. LobbyList 검색 필터로도 쓴다.</summary>
        public const string RoomCodeKey = "gh_room_code";

        /// <summary>호스트가 게임을 시작했음을 알리는 로비 데이터 키. 값 "1"이면 시작됨.</summary>
        public const string GameStartedKey = "gh_game_started";

        /// <summary>멤버별 준비 상태를 담는 멤버 데이터 키. 값 "1"이면 준비 완료.</summary>
        public const string ReadyMemberKey = "gh_ready";

        /// <summary>호스트의 네트워크 호환성 지문을 담는 로비 데이터 키.</summary>
        public const string NetFingerprintKey = "gh_net_fingerprint";

        /// <summary>
        /// 네트워크 직렬화에 영향을 주는 변경(NGO 업그레이드, 토폴로지 변경, 트랜스포트 패치 등)을
        /// 할 때 수동으로 올린다. 호스트와 값이 다르면 로비 참가 단계에서 걸러진다.
        /// </summary>
        public const int NetProtocolVersion = 1;

        /// <summary>0/O, 1/I 처럼 눈으로 헷갈리는 글자를 뺀 방 코드 문자셋.</summary>
        private const string RoomCodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        public const int RoomCodeLength = 6;

        [Header("Steam")]
        [SerializeField] private uint _appId = SpacewarAppId;

        [Header("Lobby")]
        [SerializeField] private int _maxLobbyMembers = 4;
        [Tooltip("켜면 초대/친구 목록으로만 참가 가능. 방 코드 참가는 LobbyList 검색을 쓰므로 " +
                 "공개 로비(꺼짐)에서만 동작한다.")]
        [SerializeField] private bool _friendsOnly;

        public bool IsSteamReady => SteamClient.IsValid;
        public ulong LocalSteamId => SteamClient.IsValid ? SteamClient.SteamId.Value : 0UL;
        public string LocalName => SteamClient.IsValid ? SteamClient.Name : "(Steam 미연결)";
        public Lobby? CurrentLobby { get; private set; }
        public bool IsInLobby => CurrentLobby.HasValue;

        /// <summary>참가자에게 공유하는 사람이 읽는 방 코드. 로비에 없으면 빈 문자열.</summary>
        public string CurrentRoomCode { get; private set; } = string.Empty;

        public bool IsLobbyOwner =>
            CurrentLobby.HasValue
            && SteamClient.IsValid
            && CurrentLobby.Value.Owner.Id.Value == SteamClient.SteamId.Value;

        /// <summary>호스트가 게임 씬으로 넘어가며 <see cref="MarkGameStarted"/>를 불렀는가.</summary>
        public bool IsGameStarted =>
            CurrentLobby.HasValue && CurrentLobby.Value.GetData(GameStartedKey) == "1";

        /// <summary>접속 대상 호스트 SteamId. 로비에 없으면 0.</summary>
        public ulong CurrentHostSteamId =>
            CurrentLobby.HasValue ? ResolveHostSteamId(CurrentLobby.Value).Value : 0UL;

        /// <summary>사람이 읽는 진행 상황. 개발용 HUD가 그대로 표시한다.</summary>
        public event Action<string> StatusChanged;

        /// <summary>멤버 입퇴장·로비/멤버 데이터 변경 등 로비 UI를 다시 그려야 할 때.</summary>
        public event Action LobbyUpdated;

        /// <summary>호스트로서 로비 준비 완료. 이제 StartHost 해도 된다.</summary>
        public event Action HostLobbyReady;

        /// <summary>참가자로서 접속할 호스트 SteamId 확보. 이제 StartClient 해도 된다.</summary>
        public event Action<ulong> JoinTargetResolved;

        public event Action LobbyLeft;

        // 아바타는 세션 내내 안 바뀌므로 SteamId별로 캐시한다. 몇 장 수준이라 해제하지 않는다.
        private static readonly Dictionary<ulong, Texture2D> AvatarCache = new();

        private bool _ownsSteamClient;
        private bool _callbacksSubscribed;
        private NetworkManager _networkManager;
        private FacepunchTransport _steamTransport;

        /// <summary>
        /// 호스트·게스트가 서로 호환되는 빌드인지 로비 단계에서 판별하는 지문.
        /// NGO의 접속 승인 검사(config 해시)에는 NetworkTopology가 포함되지 않아서,
        /// 서로 다른 프로젝트 상태의 두 빌드가 그대로 연결되면 씬 동기화 페이로드 파싱이
        /// 어긋나 클라이언트가 OutOfMemoryException 같은 형태로 조용히 죽는다.
        /// 접속 전에 여기서 명확한 에러로 걸러낸다.
        /// </summary>
        public string LocalNetFingerprint
        {
            get
            {
                int topology = _networkManager != null
                    ? (int)_networkManager.NetworkConfig.NetworkTopology
                    : -1;
                return $"{NetProtocolVersion}|{topology}|{Application.unityVersion}|{Application.version}";
            }
        }

        private void Awake()
        {
            // Bootstrap 씬의 NetworkRig에서 NetworkManager와 같은 오브젝트에 붙는다.
            // Singleton 초기화 순서에 기대지 않고 같은 오브젝트에서 직접 잡는다.
            _networkManager = GetComponent<NetworkManager>();
            _steamTransport = GetComponent<FacepunchTransport>();

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
            UnsubscribeCallbacks();
            LeaveLobby();

            if (_ownsSteamClient)
            {
                SteamClient.Shutdown();
                _ownsSteamClient = false;
            }
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
                    $"[SteamLobbyManager] Steam 초기화 실패 (AppId {_appId}, {e.GetType().Name}): {e.Message}\n" +
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
            SteamMatchmaking.OnLobbyDataChanged += HandleLobbyDataChanged;
            SteamMatchmaking.OnLobbyMemberDataChanged += HandleLobbyMemberDataChanged;
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
            SteamMatchmaking.OnLobbyDataChanged -= HandleLobbyDataChanged;
            SteamMatchmaking.OnLobbyMemberDataChanged -= HandleLobbyMemberDataChanged;
            SteamFriends.OnGameLobbyJoinRequested -= HandleGameLobbyJoinRequested;

            _callbacksSubscribed = false;
        }

        #endregion

        #region 로비 조작

        /// <summary>로비를 만든다. 성공하면 <see cref="HostLobbyReady"/>가 발생한다.</summary>
        public async UniTask CreateLobbyAsync()
        {
            if (!RequireSteam())
                return;

            if (IsInLobby)
            {
                SetStatus("이미 로비에 있습니다. 먼저 나가세요.");
                return;
            }

            SetStatus("로비 생성 중...");

            Lobby? lobby = await SteamMatchmaking.CreateLobbyAsync(_maxLobbyMembers)
                .AsUniTask()
                .AttachExternalCancellation(destroyCancellationToken);

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
        public async UniTask JoinLobbyAsync(SteamId lobbyId)
        {
            if (!RequireSteam())
                return;

            SetStatus($"로비 참가 중... ({lobbyId})");

            var lobby = new Lobby(lobbyId);
            RoomEnter result = await lobby.Join()
                .AsUniTask()
                .AttachExternalCancellation(destroyCancellationToken);

            if (result != RoomEnter.Success)
            {
                SetStatus($"로비 참가 실패: {result}");
                return;
            }

            // 성공 시 OnLobbyEntered 콜백이 이어서 처리한다.
        }

        /// <summary>
        /// 방 코드로 공개 로비를 검색해 참가한다. 코드는 로비 생성 시
        /// <see cref="RoomCodeKey"/> 데이터로 심어둔 값이다.
        /// </summary>
        public async UniTask JoinLobbyByCodeAsync(string rawCode)
        {
            if (!RequireSteam())
                return;

            if (IsInLobby)
            {
                SetStatus("이미 로비에 있습니다. 먼저 나가세요.");
                return;
            }

            string code = NormalizeRoomCode(rawCode);
            if (code.Length != RoomCodeLength)
            {
                SetStatus($"방 코드는 {RoomCodeLength}자리입니다. (예: AB3CD9)");
                return;
            }

            SetStatus($"방 코드 {code} 검색 중...");

            Lobby[] lobbies = await SteamMatchmaking.LobbyList
                .WithMaxResults(1)
                .WithKeyValue(RoomCodeKey, code)
                .WithSlotsAvailable(1)
                .FilterDistanceWorldwide()
                .RequestAsync()
                .AsUniTask()
                .AttachExternalCancellation(destroyCancellationToken);

            if (lobbies == null || lobbies.Length == 0)
            {
                SetStatus($"코드 {code} 에 해당하는 방을 찾지 못했습니다.");
                return;
            }

            await JoinLobbyAsync(lobbies[0].Id);
        }

        /// <summary>입력값에서 공백 제거·대문자화. 검증은 호출자가 길이로 한다.</summary>
        public static string NormalizeRoomCode(string rawCode)
        {
            return string.IsNullOrEmpty(rawCode)
                ? string.Empty
                : rawCode.Trim().ToUpperInvariant();
        }

        /// <summary>내 준비 상태를 로비 멤버 데이터로 알린다. 모든 멤버에게 콜백이 간다.</summary>
        public void SetLocalReady(bool ready)
        {
            if (!CurrentLobby.HasValue)
                return;

            CurrentLobby.Value.SetMemberData(ReadyMemberKey, ready ? "1" : "0");
        }

        public bool IsMemberReady(Friend member)
        {
            return CurrentLobby.HasValue
                   && CurrentLobby.Value.GetMemberData(member, ReadyMemberKey) == "1";
        }

        /// <summary>
        /// 현재 로비 멤버 스냅샷. 방장이 첫 줄, 그다음은 이름 순.
        /// UI가 <c>Friend</c>를 다루지 않도록 여기서 DTO로 바꿔 넘긴다.
        /// </summary>
        public IReadOnlyList<LobbyMemberInfo> GetMembers()
        {
            if (!CurrentLobby.HasValue)
                return Array.Empty<LobbyMemberInfo>();

            Lobby lobby = CurrentLobby.Value;
            ulong ownerId = lobby.Owner.Id.Value;

            var members = new List<Friend>(lobby.Members);
            members.Sort((a, b) =>
            {
                bool aOwner = a.Id.Value == ownerId;
                bool bOwner = b.Id.Value == ownerId;
                if (aOwner != bOwner)
                    return aOwner ? -1 : 1;

                return string.CompareOrdinal(a.Name, b.Name);
            });

            var result = new List<LobbyMemberInfo>(members.Count);
            foreach (Friend member in members)
            {
                result.Add(new LobbyMemberInfo(
                    member.Id.Value,
                    member.Name,
                    member.Id.Value == ownerId,
                    IsMemberReady(member)));
            }

            return result;
        }

        /// <summary>
        /// 멤버 아바타. 실패하면 null. 아바타는 세션 내내 안 바뀌므로 SteamId별로 캐시한다
        /// (몇 장 수준이라 해제하지 않는다).
        /// </summary>
        public async UniTask<Texture2D> GetAvatarAsync(ulong steamId)
        {
            if (AvatarCache.TryGetValue(steamId, out Texture2D cached))
                return cached;

            if (!SteamClient.IsValid)
                return null;

            try
            {
                Steamworks.Data.Image? image = await SteamFriends.GetMediumAvatarAsync(steamId)
                    .AsUniTask()
                    .AttachExternalCancellation(destroyCancellationToken);

                if (!image.HasValue)
                    return null;

                Texture2D texture = CreateAvatarTexture(image.Value);
                AvatarCache[steamId] = texture;
                return texture;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SteamLobbyManager] 아바타 로드 실패 ({steamId}): {e.Message}", this);
                return null;
            }
        }

        private static Texture2D CreateAvatarTexture(Steamworks.Data.Image image)
        {
            int width = (int)image.Width;
            int height = (int)image.Height;

            // Steam 아바타는 위→아래 순서의 RGBA, Texture2D는 아래→위라 행을 뒤집는다.
            var flipped = new byte[image.Data.Length];
            int stride = width * 4;
            for (int y = 0; y < height; y++)
                Buffer.BlockCopy(image.Data, y * stride, flipped, (height - 1 - y) * stride, stride);

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.LoadRawTextureData(flipped);
            texture.Apply();
            return texture;
        }

        /// <summary>호스트를 제외한 전원이 준비 완료인가. 게스트가 없으면 true(솔로 테스트).</summary>
        public bool AllGuestsReady()
        {
            if (!CurrentLobby.HasValue)
                return false;

            Lobby lobby = CurrentLobby.Value;
            ulong ownerId = lobby.Owner.Id.Value;

            foreach (Friend member in lobby.Members)
            {
                if (member.Id.Value == ownerId)
                    continue;

                if (lobby.GetMemberData(member, ReadyMemberKey) != "1")
                    return false;
            }

            return true;
        }

        /// <summary>Steam 구현체만 FacepunchTransport의 구체 API를 다룬다.</summary>
        public bool TrySetConnectionTarget(ulong hostSteamId)
        {
            if (_steamTransport == null || hostSteamId == 0)
                return false;

            _steamTransport.targetSteamId = hostSteamId;
            return true;
        }

        /// <summary>
        /// 호스트가 세션을 실제로 띄운 뒤에 부른다. 게스트는 이 신호(로비 데이터 변경)를 받고
        /// StartClient 한다 — 세션이 없는 호스트에게 미리 접속하는 것을 막기 위한 순서다.
        /// </summary>
        public void MarkGameStarted()
        {
            if (!CurrentLobby.HasValue || !IsLobbyOwner)
                return;

            CurrentLobby.Value.SetData(GameStartedKey, "1");
            SetStatus("게임 시작을 로비에 알렸습니다.");
        }

        public void LeaveLobby()
        {
            if (!CurrentLobby.HasValue)
                return;

            CurrentLobby.Value.Leave();
            CurrentLobby = null;
            CurrentRoomCode = string.Empty;

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

            // 참가자가 검색으로 이 로비를 찾을 수 있게 방 코드를 심는다.
            CurrentRoomCode = GenerateRoomCode();
            lobby.SetData(RoomCodeKey, CurrentRoomCode);

            // 참가자가 접속 전에 빌드 호환성을 검사할 수 있게 지문을 심는다.
            lobby.SetData(NetFingerprintKey, LocalNetFingerprint);

            CurrentLobby = lobby;

            SetStatus($"로비 생성 완료. 방 코드: {CurrentRoomCode}");
            HostLobbyReady?.Invoke();
            LobbyUpdated?.Invoke();
        }

        private static string GenerateRoomCode()
        {
            var buffer = new char[RoomCodeLength];
            for (int i = 0; i < buffer.Length; i++)
                buffer[i] = RoomCodeAlphabet[UnityEngine.Random.Range(0, RoomCodeAlphabet.Length)];

            return new string(buffer);
        }

        private void HandleLobbyEntered(Lobby lobby)
        {
            CurrentLobby = lobby;
            CurrentRoomCode = lobby.GetData(RoomCodeKey) ?? string.Empty;
            LobbyUpdated?.Invoke();

            // 호스트 자신도 자기 로비에 들어오면서 이 콜백을 받는다. 그 경우는 무시한다.
            // (SteamId 끼리 == 비교는 ulong 암시적 변환에 의존하므로 Value 로 명시 비교한다.)
            if (lobby.Owner.Id.Value == SteamClient.SteamId.Value)
                return;

            // 호스트와 빌드가 다르면 접속해 봐야 씬 동기화 단계에서 알 수 없는 형태로 깨진다.
            // 지문이 비어 있으면 지문 기능이 없는 옛 빌드의 로비이므로 경고만 남기고 진행한다.
            string hostFingerprint = lobby.GetData(NetFingerprintKey);
            if (string.IsNullOrEmpty(hostFingerprint))
            {
                Debug.LogWarning(
                    "[SteamLobbyManager] 호스트 로비에 네트워크 지문이 없습니다. " +
                    "호스트가 옛 빌드일 수 있습니다. 접속은 계속하지만 씬 동기화가 깨질 수 있습니다.");
            }
            else if (hostFingerprint != LocalNetFingerprint)
            {
                SetStatus(
                    "호스트와 게임 빌드가 달라 참가할 수 없습니다. 두 쪽 모두 같은 커밋으로 맞춘 뒤 " +
                    $"다시 시도하세요. (호스트: {hostFingerprint} / 나: {LocalNetFingerprint})");
                LeaveLobby();
                return;
            }

            SteamId hostId = ResolveHostSteamId(lobby);

            if (hostId.Value == 0)
            {
                SetStatus("로비에 들어갔지만 호스트 SteamId를 알아내지 못했습니다.");
                return;
            }

            SetStatus($"로비 입장 ({lobby.Id}). 호스트 {hostId} 에 접속합니다.");
            JoinTargetResolved?.Invoke(hostId.Value);
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
            LobbyUpdated?.Invoke();
        }

        private void HandleLobbyMemberLeave(Lobby lobby, Friend friend)
        {
            SetStatus($"{friend.Name} 님이 로비를 떠났습니다. ({lobby.MemberCount}/{_maxLobbyMembers})");
            LobbyUpdated?.Invoke();
        }

        private void HandleLobbyDataChanged(Lobby lobby)
        {
            if (!CurrentLobby.HasValue || CurrentLobby.Value.Id.Value != lobby.Id.Value)
                return;

            LobbyUpdated?.Invoke();
        }

        private void HandleLobbyMemberDataChanged(Lobby lobby, Friend friend)
        {
            if (!CurrentLobby.HasValue || CurrentLobby.Value.Id.Value != lobby.Id.Value)
                return;

            LobbyUpdated?.Invoke();
        }

        /// <summary>
        /// 친구 목록/오버레이에서 "게임 참가"를 눌렀을 때.
        /// Steam 콜백 델리게이트가 <c>void</c> 시그니처라 여기서 UniTask 로 넘긴다.
        /// </summary>
        private void HandleGameLobbyJoinRequested(Lobby lobby, SteamId invitedBy)
            => AcceptInviteAsync(lobby, invitedBy).Forget();

        private async UniTaskVoid AcceptInviteAsync(Lobby lobby, SteamId invitedBy)
        {
            SetStatus($"{invitedBy} 의 초대를 수락합니다...");

            try
            {
                RoomEnter result = await lobby.Join()
                    .AsUniTask()
                    .AttachExternalCancellation(destroyCancellationToken);

                if (result != RoomEnter.Success)
                    SetStatus($"초대 수락 실패: {result}");
            }
            catch (OperationCanceledException)
            {
                // 대기 도중 오브젝트가 파괴됐다. 정상 종료.
            }
            catch (Exception e)
            {
                Debug.LogError($"[SteamLobbyManager] 초대 수락 중 예외: {e}", this);
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
