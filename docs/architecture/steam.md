# Steam 연동 (NGO + Facepunch Steamworks)

> Steam 관련 코드를 작성·수정하기 전 MUST 읽는다.
> 선택 근거는 [ADR-0001](decisions/ADR-0001-steam-p2p-facepunch-transport.md),
> 임베드·패치 근거는 [ADR-0006](decisions/ADR-0006-facepunch-transport-embed.md),
> NGO 사용 규약은 [networking.md](networking.md).
>
> **상태: 설치·구현 완료 / 2PC 실기 검증 대기.** 이 문서는 "앞으로 할 절차"가 아니라
> **현재 어떻게 구성되어 있는지**와 새 PC에서 클론했을 때 무엇을 해야 하는지를 적는다.

## 설치된 구성

| 레이어 | 패키지 | 버전 | 출처 |
|---|---|---|---|
| 네트워크 프레임워크 | `com.unity.netcode.gameobjects` | 2.13.1 | registry |
| 로컬 테스트 트랜스포트 | `com.unity.transport` (UTP) | 2.7.3 | NGO 의존성으로 자동 설치 |
| Steam 트랜스포트 | `com.community.netcode.transport.facepunch` | 2.0.0-ghosthunter.3 | **embedded** (`Packages/`) |
| Steam 래퍼 | Facepunch.Steamworks | 2.5.2 | 트랜스포트 패키지에 번들 |

**Facepunch.Steamworks DLL을 따로 받을 필요가 없다.** 커뮤니티 트랜스포트 패키지가
매니지드 DLL(Win32/Win64/Posix)과 네이티브 `steam_api` 재배포 바이너리를
플랫폼 import 설정(`.meta`)까지 갖춘 채로 포함하고 있다.

```
Packages/com.community.netcode.transport.facepunch/Runtime/Facepunch/
├─ Facepunch.Steamworks.Win64.dll        (+ Win32 / Posix)
└─ redistributable_bin/
   ├─ win64/steam_api64.dll
   ├─ linux32|linux64/libsteam_api.so
   └─ osx/libsteam_api.bundle
```

## 왜 git URL이 아니라 임베드인가

Package Manager의 git URL로 설치하면 패키지가 **읽기 전용**이 되는데, 이 패키지는
패치 없이는 쓸 수 없다. `main` 브랜치의 `FacepunchTransport.cs`에 **짝 없는 `#endregion`이
하나 있어서 그대로 설치하면 프로젝트 전체가 컴파일되지 않는다(CS1028).**

그 외 Steam 수명주기·메타데이터·macOS 네이티브·전송 실패 감지 패치 4건이 더 있다(총 5건). 전체 내역:
[`Packages/com.community.netcode.transport.facepunch/PATCHES.md`](../../Packages/com.community.netcode.transport.facepunch/PATCHES.md)

대가: upstream 업데이트가 자동으로 오지 않는다. 갱신하려면 원본을 다시 받아 패치를 재적용한다.

> 참고: package.json의 NGO 의존성 표기는 `1.0.0-pre.4`였지만, 실제 코드는 NGO 2.x API
> (`OnEarlyUpdate`, `InvokeOnTransportEvent`)를 쓴다. 즉 **1.x에서는 오히려 컴파일되지 않는다.**
> 표기를 실제와 맞춰 2.13.1로 고쳤다.

## 코드 구성

| 파일 | 역할 |
|---|---|
| [`Systems/Steam/SteamLobbyManager.cs`](../../Assets/Scripts/Systems/Steam/SteamLobbyManager.cs) | Steam 수명주기(Init/RunCallbacks/Shutdown) + 로비 생성·참가·초대 + Facepunch 접속 대상 설정 |
| [`Networking/ConnectionManager.cs`](../../Assets/Scripts/Networking/ConnectionManager.cs) | StartHost/StartClient, 트랜스포트 전환, 접속 상태 |
| [`DebugTools/ConnectionHud.cs`](../../Assets/Scripts/DebugTools/ConnectionHud.cs) | 개발용 IMGUI 접속 HUD (**Tab** 토글. 밸런스 튜닝 창은 F2) |

### 역할 분리

`SteamLobbyManager`는 Steamworks와 FacepunchTransport를 알고, `ConnectionManager`는 Netcode 기반 타입만 안다.
로비가 준비되면 이벤트로 알리고, 실제 `StartHost`/`StartClient`는 `ConnectionManager`가 부른다.

### 스테이지와 임시 공동 상점

`gh_game_started`는 대기 `0`, 로딩 `2`, 세션 진행 `1`로 구분한다. 로딩·진행에는
Steam 로비 참가를 닫고, 기존 초대 링크로 들어와도 참가 콜백에서 거절한다. 2026-09-28부터 세션은 인게임 로비와
스테이지를 오가는 동안 유지되므로(ADR-0018) **인게임 로비에 있는 동안에도 닫혀 있다** — 참가는 일반 로비에서만 된다.
세션이 끝나 일반 로비로 돌아가면 다시 연다.

임시 상점은 잔액과 Temp1·2·3 수량을 `gh_shop_state` 한 키에 기록한다. **인게임 로비에서 방장만** 이 키를
변경한다(`StageShopRules`, 2026-09-28). 예전의 게스트 구매 요청(Steam P2P 채널 8)은 받아서 버린다. 시작 자금 600,
각 가격 100·200·300은 [스테이지 시스템 기획서](../project/stage-system.md)의 임시값이다.
음성은 별도의 채널 7로 주고받는다. 실제 Steam 2PC 구매·음성 검증은 대기 중이다.
음성 수신기를 정리할 때 공유 Steam P2P 세션은 닫지 않는다. 임시 상점 채널 8도 이 세션을 사용한다.
호스트 복구 스냅샷은 Steam P2P 채널 9로 보낸다. NGO의 FacepunchTransport는 별도의 Steam Networking Sockets 연결을 사용한다.
클라이언트 접속 대상 설정은 `ISteamLobbyService.TrySetConnectionTarget`으로 Steam 레이어에 위임한다.

정상 종료의 `StageSettlementRecord`는 방장이 `gh_settlement_{순번}`에 먼저 기록하고
`gh_settlement_count`를 마지막에 증가시킨다. `SceneFlowController`는 로비 데이터 변경 시
누락된 기록을 읽어 현재 방의 이력을 보충한다. 따라서 `Result` 중 기존 호스트가 이탈해도
남은 로비 멤버는 게시된 정산을 다시 읽을 수 있다. 이는 **정산 이력** 경로이며,
진행 중 스테이지의 게임플레이 상태 이전은 [ADR-0017](decisions/ADR-0017-host-migration.md)에서 별도로 다룬다.

```
SteamLobbyManager                        ConnectionManager
─────────────────                        ─────────────────
CreateLobbyAsync()
   └ OnLobbyCreated 콜백
       ├ SetFriendsOnly / SetJoinable
       ├ SetData(호스트 SteamId)
       └ HostLobbyReady  ───────────────► StartHost()

(친구가 초대 수락)
   └ OnGameLobbyJoinRequested → Join()
       └ OnLobbyEntered
           └ JoinTargetResolved(hostId) ─► TrySetConnectionTarget(hostId)
                                           └ targetSteamId 설정 → StartClient()
```

**순서가 중요하다.** 로비가 먼저 만들어져야 참가자가 "누구에게 P2P 연결할지"를 알 수 있다.
그래서 `StartHost`는 로비 생성 콜백을 받은 뒤에 일어난다.

### 매치 중 로비를 나가는 경로

`ConnectionManager.Disconnect()` 는 `NetworkManager.Shutdown()` 뒤에 **`LeaveLobby()` 까지 부른다.**
따라서 매치 중 나가기(일시정지 메뉴의 "타이틀로"·"종료")는 `IConnectionService.Disconnect()` 하나만
부르면 되고, `ISteamLobbyService.LeaveLobby()` 를 또 부르지 않는다 — `LobbyLeft` 이벤트가 두 번 온다.

`ConnectionManager.HandleClientDisconnected()` 는 Game/Result 중 예기치 않은 자기 연결 해제에서
Steam 로비 멤버십을 유지한다. Game 중에는 `StageRecoveryCoordinator`가 마지막 완전 수신 스냅샷과
Steam 로비의 새 호스트 정보를 이용해 NGO를 다시 연다. 실패하거나 제한 시간에 도달한 경우에만
기존 연결 끊김 모달로 넘어간다. 구현 범위와 미검증 항목은 [ADR-0017](decisions/ADR-0017-host-migration.md)을 참조한다.

## 씬 세팅

`Assets/Scenes/Bootstrap.unity`가 아래 리그를 직접 소유한다. 별도 씬에 리그를 추가하지 않는다:

```
NetworkRig
├─ NetworkManager          (Transport=Facepunch, SceneManagement=on, LogLevel=Developer)
├─ FacepunchTransport
├─ UnityTransport          (로컬 테스트용, 기본 127.0.0.1:7777)
├─ SteamLobbyManager       (AppId 480, 최대 4인, 6자리 방 코드)
├─ ConnectionManager       (위 3개 참조가 자동 연결됨)
└─ ConnectionHud           (Tab 토글, 튜닝 창 F2)
```

리그를 고칠 때 `ConnectionManager`의 트랜스포트 참조를 빠뜨리기 쉽고,
빠뜨리면 런타임에 "트랜스포트가 연결되어 있지 않습니다" 로그만 남는다.

`NetworkManager`에는 Player Prefab과 Network Prefab 목록(`Assets/DefaultNetworkPrefabs.asset`)이 등록되어 있다.
이 배선을 재생성하던 `GhostHunter > 프로토타입 게임 생성` 메뉴는 삭제했다([ADR-0020](decisions/ADR-0020-remove-one-off-editor-setup-tools.md)) — 씬에서 직접 고친다.

## steam_appid.txt

프로젝트 루트에 `steam_appid.txt`(내용: `480`)가 **이미 있고 커밋되어 있다.**

- 480 = Valve의 공개 테스트 앱 **Spacewar**. 자체 App ID를 받기 전까지 사용한다.
- 에디터에서 플레이하려면 **Steam 클라이언트가 실행 중이고 로그인**되어 있어야 한다.
- 빌드 배포 시 exe 옆에도 같은 파일이 있어야 한다.
- 자체 App ID가 생기면 이 파일과 `SteamLobbyManager`의 `_appId` 인스펙터 값을 함께 바꾼다.

## macOS(맥북)에서 참여시키기

Windows 호스트 ↔ macOS 클라이언트는 Steam P2P로 문제없이 붙는다. 다만 이 저장소에는
맥을 막는 지점이 있었다.

### 1. asmdef 플랫폼 (해결됨)

Facepunch를 참조하는 `GhostHunter.Systems.asmdef`의 `includePlatforms`에 `macOSStandalone`이 없으면
**Mac 빌드에서 Steam·씬 흐름 등 횡단 시스템이 제외**된다. 현재는 Editor + WindowsStandalone64 +
macOSStandalone만 포함하고, Core·Data·Gameplay·Networking·UI에는 플랫폼 제한을 걸지 않는다.

### 2. Apple Silicon — 네이티브 Steam 바이너리 arm64 (해결됨)

번들 원본 `redistributable_bin/osx/libsteam_api.bundle`은 슬라이스가 **i386 + x86_64뿐이라
arm64가 없었다.** M시리즈 맥에서 arm64로 실행하면 `DllNotFoundException: libsteam_api` →
`SteamClient.Init` 실패(HUD: `Steam: 미초기화`)가 났다.

Facepunch.Steamworks 2.5.2의 **x86_64 + arm64 유니버설** 네이티브 파일과 그에 맞는
Posix 관리 DLL을 함께 적용했다. 네이티브 파일만 먼저 올려 생겼던 `SteamAPI_Init`
엔트리포인트 불일치도 함께 해결했다. 경위는
[PATCHES.md 패치 4](../../Packages/com.community.netcode.transport.facepunch/PATCHES.md).

네이티브 바이너리는 공식 2.5.2와 동일하지만, 파일명은 Mac Unity 에디터의 Mono P/Invoke가
`libsteam_api`를 확실히 매핑하도록 기존 `.bundle`을 유지한다. `.dylib` 이름으로 두면 이
embedded package 구성에서는 프로젝트 루트만 검색하다 `DllNotFoundException`이 발생했다.
확인:

```bash
lipo -archs Packages/com.community.netcode.transport.facepunch/Runtime/Facepunch/redistributable_bin/osx/libsteam_api.bundle
# → x86_64 arm64
```

이제 Apple silicon 에디터/빌드에서 그대로 네이티브로 돌아간다. Rosetta나 Intel 에디터
설치는 필요 없다.

### 3. Mac 빌드의 steam_appid.txt 위치

Finder에서 .app을 실행하면 **작업 디렉터리가 `/`** 라서 .app 옆에 둔 `steam_appid.txt`를
Steam이 찾지 못한다. `GhostHunter.app/Contents/MacOS/steam_appid.txt`에 넣는다.
(터미널에서 `cd` 후 실행할 때는 그 디렉터리에 있으면 된다.)

### 4. 계정

같은 Steam 계정으로 두 기기에 동시 로그인할 수 없다. **계정 2개**가 필요하고,
로비가 `친구 전용(_friendsOnly = true)`이므로 두 계정은 **서로 친구**여야 한다.

### Steam 없이 먼저 크로스 머신 확인하기 (LAN)

가구 던지기 로직만 두 기기에서 확인하고 싶으면 Steam을 건너뛸 수 있다.
`NetworkRig > UnityTransport` 인스펙터에서:

- 호스트(Windows): `Server Listen Address` = `0.0.0.0`
- 클라이언트(Mac): `Address` = 호스트의 LAN IP (예: `192.168.0.12`), Port `7777`

양쪽 HUD에서 모드를 `Local`로 두고 Host / Join (로컬). 방화벽에서 UDP 7777 허용 필요.

## 빠른 시작 — 메뉴 흐름

**Steam 방 (Steam 필요):** `Assets/Scenes/Bootstrap.unity`를 열고 플레이한다. Bootstrap 이 `Title` 을 additive 로 올린다.

1. **방 생성** → Steam 로비 생성 + 6자리 방 코드 발급 → 일반 로비 씬으로 이동
2. 상대는 **방 참가**에 방 코드를 입력하거나, 호스트의 **초대** 오버레이로 들어온다
3. 게스트가 **준비**를 누르면 호스트의 **게임 시작**이 활성화된다
4. 호스트가 시작하면 **인게임 로비** 씬을 로드한 뒤 `StartHost` → 로비에 시작 신호 → 게스트 접속(이후 참가 차단)
5. 인게임 로비의 **단말기에서 E** → 방장만 상점 구매·**스테이지 출발**. 정산 뒤·`스테이지 나가기` 뒤엔 인게임 로비로
   돌아온다(세션 유지 — `IStageSessionFlow`, [ADR-0018](decisions/ADR-0018-persistent-session-in-game-lobby.md)).

**세션 시작 순서(씬 로드 → StartHost → 로비 신호)는 MUST 지킨다.** 로비 씬에서 바로 `StartHost` 하면
플레이어가 스폰 지점 없는 씬에 스폰되고, 신호를 먼저 보내면 게스트가 세션 없는 호스트에 접속한다.

**단독 플레이 (Steam 없이):** `Bootstrap.unity`에서 플레이 → **F1** 접속 HUD → 모드 `Local` → **Host** →
인게임 로비 단말기에서 **스테이지 출발**(2026-09-28부터 Host 는 인게임 로비에서 열린다. 상점은 Steam 방에서만 쓸 수 있다).
HUD 로 바꾼 모드는 저장하지 않는다. 저장하면 릴리스 빌드가 `TransportModeBuildGuard` 에 막힌다.

혼자 음성 확인(F3)은 [voice-chat.md](voice-chat.md) "혼자 검증", 정신력 감소 확인(`Game/SanityTestbed`)은
[sanity-system.md §7](sanity-system.md)을 본다.

## 테스트 전략 — 중요

**같은 Steam 계정으로는 두 인스턴스를 P2P 연결할 수 없다.** SteamId가 같아서 자기 자신에게
연결하는 꼴이 된다. Unity의 Multiplayer Play Mode(가상 플레이어)도 Steam 계정을 공유하므로
마찬가지로 안 된다.

그래서 트랜스포트를 두 개 두고 전환한다:

| 상황 | 모드 | 방법 |
|---|---|---|
| 혼자 로직 검증 (매일) | **Local (UTP)** | 빌드 실행 → Host, 에디터 플레이 → Join |
| Steam 경로 검증 (주기적) | **Steam (Facepunch)** | PC 2대 또는 Steam 계정 2개 |

전환 방법 세 가지:

- HUD의 `모드:` 버튼 클릭 (세션 정지 중에만)
- `ConnectionManager` 인스펙터의 Transport Mode
- 커맨드라인 `-transport=local` / `-transport=steam` (빌드 실행 시)

### 로컬 2인 테스트 절차

1. Windows 빌드 1회 생성
2. 빌드 실행 → F1 → 모드를 `Local`로 → **Host** — 인게임 로비 씬을 먼저 올린 뒤 호스트가 열린다(networking.md §3.6)
3. 에디터 플레이 → F1 → 모드를 `Local`로 → **Join (로컬)** — 호스트가 있는 씬은 NGO 동기화로 받는다

### Steam 2인 테스트 절차

1. 양쪽 다 Steam 로그인, 서로 친구 상태
2. 호스트: F1 → 모드 `Steam` → **Host** → **친구 초대**
3. 참가자: Steam 오버레이에서 초대 수락 → 자동으로 로비 입장 + 접속

## 문제가 생기면

| 증상 | 확인할 것 |
|---|---|
| `Steam 초기화 실패` 로그 | Steam 클라이언트 실행 중인가 / `steam_appid.txt` 있는가 |
| 로비 콜백이 아예 안 옴 | `SteamLobbyManager`가 씬에 있는가 (`RunCallbacks`를 이 컴포넌트가 편다) |
| HUD에 `Steam: 미초기화` | 위와 동일. Local 모드로는 계속 개발 가능 |
| Mac에서 `DllNotFoundException: libsteam_api` | 네이티브 파일이 `libsteam_api.bundle` 이름인지와 Mac Editor import가 켜졌는지 확인 |
| Mac에서 `EntryPointNotFoundException: SteamAPI_Init` | 구형 관리 DLL과 신형 네이티브 파일이 섞였다. Facepunch 2.5.2 세트인지 확인 |
| Mac 빌드에서 Steam·씬 시스템이 없음 | `GhostHunter.Systems.asmdef`에 `macOSStandalone`이 있는가 |
| 스폰이 조용히 실패 | `NetworkManager`의 Network Prefabs List에 프리팹을 등록했는가 |
| 씬 전환이 동기화 안 됨 | `NetworkManager.SceneManager.LoadScene`을 썼는가 (`SceneManager.LoadScene` 아님) |
| 접속은 되는데 아무것도 안 보임 | Player Prefab이 지정되어 있는가 |

`NetworkManager`의 LogLevel이 `Developer`로 설정되어 있어 트랜스포트가 Steam 연결 과정을
상세히 로그한다. 조용해지면 Normal로 낮춘다.

---

관련: [networking.md](networking.md) · [ADR-0001](decisions/ADR-0001-steam-p2p-facepunch-transport.md) · [ADR-0006](decisions/ADR-0006-facepunch-transport-embed.md) · [ADR-0012](decisions/ADR-0012-room-code-and-lobby-visibility.md) · [../workflow/playbooks.md PB-08](../workflow/playbooks.md)

> **로비 가시성·난입 정책은 결정 대기 중이다** → [ADR-0012](decisions/ADR-0012-room-code-and-lobby-visibility.md).
> 현재 `_friendsOnly = true`로 두면 6자리 방 코드 참가가 동작하지 않는다(LobbyList 검색은 공개 로비만 반환).

최종 갱신: 2026-09-28 (씬 세팅: 리그 선택·프로토타입 생성 메뉴 삭제 — ADR-0020. 이전: 2026-09-04 매치 중 로비 이탈 경로 명시 — `Disconnect()` 가 `LeaveLobby()` 를 이미 부른다.
개발 HUD 키 표기를 실제 값 Tab/F2 로 정정. 이전: 2026-08-20)
