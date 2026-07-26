# 03. 멀티플레이 세팅 (NGO + Facepunch Steamworks)

> **상태: 설치 완료.** 이 문서는 "앞으로 할 절차"가 아니라 **현재 어떻게 구성되어 있는지**와
> 새 PC에서 클론했을 때 무엇을 해야 하는지를 적는다.

## 설치된 구성

| 레이어 | 패키지 | 버전 | 출처 |
|---|---|---|---|
| 네트워크 프레임워크 | `com.unity.netcode.gameobjects` | 2.13.1 | registry |
| 로컬 테스트 트랜스포트 | `com.unity.transport` (UTP) | 2.7.3 | NGO 의존성으로 자동 설치 |
| Steam 트랜스포트 | `com.community.netcode.transport.facepunch` | 2.0.0-ghosthunter.1 | **embedded** (`Packages/`) |
| Steam 래퍼 | Facepunch.Steamworks | 트랜스포트 패키지에 번들 | — |

**Facepunch.Steamworks DLL을 따로 받을 필요가 없다.** 커뮤니티 트랜스포트 패키지가
매니지드 DLL(Win32/Win64/Linux/macOS)과 네이티브 `steam_api` 재배포 바이너리를
플랫폼 import 설정(`.meta`)까지 갖춘 채로 포함하고 있다.

```
Packages/com.community.netcode.transport.facepunch/Runtime/Facepunch/
├─ Facepunch.Steamworks.Win64.dll        (+ Win32 / Linux / MacOS)
└─ redistributable_bin/
   ├─ win64/steam_api64.dll
   ├─ linux32|linux64/libsteam_api.so
   └─ osx/libsteam_api.bundle
```

## 왜 git URL이 아니라 임베드인가

Package Manager의 git URL로 설치하면 패키지가 **읽기 전용**이 되는데, 이 패키지는
패치 없이는 쓸 수 없다. `main` 브랜치의 `FacepunchTransport.cs`에 **짝 없는 `#endregion`이
하나 있어서 그대로 설치하면 프로젝트 전체가 컴파일되지 않는다(CS1028).**

그 외 Steam 수명주기 관련 패치 2건이 더 있다. 전체 내역:
[`Packages/com.community.netcode.transport.facepunch/PATCHES.md`](../Packages/com.community.netcode.transport.facepunch/PATCHES.md)

대가: upstream 업데이트가 자동으로 오지 않는다. 갱신하려면 원본을 다시 받아 패치를 재적용한다.

> 참고: package.json의 NGO 의존성 표기는 `1.0.0-pre.4`였지만, 실제 코드는 NGO 2.x API
> (`OnEarlyUpdate`, `InvokeOnTransportEvent`)를 쓴다. 즉 **1.x에서는 오히려 컴파일되지 않는다.**
> 표기를 실제와 맞춰 2.13.1로 고쳤다.

## 코드 구성

| 파일 | 역할 |
|---|---|
| [`Networking/SteamLobbyManager.cs`](../Assets/Scripts/Networking/SteamLobbyManager.cs) | Steam 수명주기(Init/RunCallbacks/Shutdown) + 로비 생성·참가·초대 |
| [`Networking/ConnectionManager.cs`](../Assets/Scripts/Networking/ConnectionManager.cs) | StartHost/StartClient, 트랜스포트 전환, 접속 상태 |
| [`DebugTools/ConnectionHud.cs`](../Assets/Scripts/DebugTools/ConnectionHud.cs) | 개발용 IMGUI 접속 HUD (F1 토글) |
| [`Editor/NetworkRigSetup.cs`](../Assets/Scripts/Editor/NetworkRigSetup.cs) | 위 전부를 배선한 오브젝트를 메뉴 한 번으로 생성 |

### 역할 분리

`SteamLobbyManager`는 Steam만 알고, `ConnectionManager`는 Netcode만 안다.
로비가 준비되면 이벤트로 알리고, 실제 `StartHost`/`StartClient`는 `ConnectionManager`가 부른다.

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
           └ JoinTargetResolved(hostId) ─► targetSteamId 설정 → StartClient()
```

**순서가 중요하다.** 로비가 먼저 만들어져야 참가자가 "누구에게 P2P 연결할지"를 알 수 있다.
그래서 `StartHost`는 로비 생성 콜백을 받은 뒤에 일어난다.

## 씬 세팅

`Assets/Scenes/Prototype.unity`에는 아래 리그가 이미 만들어져 있다. 빈 씬에서 별도 리그가
필요하면 메뉴 **`GhostHunter > 네트워크 리그 생성`** 을 사용한다:

```
NetworkRig
├─ NetworkManager          (Transport=Facepunch, SceneManagement=on, LogLevel=Developer)
├─ FacepunchTransport
├─ UnityTransport          (로컬 테스트용, 기본 127.0.0.1:7777)
├─ SteamLobbyManager       (AppId 480, 최대 2인, 친구 전용)
├─ ConnectionManager       (위 3개 참조가 자동 연결됨)
└─ ConnectionHud           (F1 토글)
```

손으로 배선해도 되지만 `ConnectionManager`의 트랜스포트 참조를 빠뜨리기 쉽고,
빠뜨리면 런타임에 "트랜스포트가 연결되어 있지 않습니다" 로그만 남는다.

`Prototype`의 `NetworkManager`에는 Player Prefab과 light/heavy 가구 Network Prefab 목록까지
등록되어 있다. 메뉴 **`GhostHunter > 프로토타입 게임 생성`**을 다시 실행하면 이 배선을
에디터 API로 재생성하고 누락 여부도 검증한다.

## steam_appid.txt

프로젝트 루트에 `steam_appid.txt`(내용: `480`)가 **이미 있고 커밋되어 있다.**

- 480 = Valve의 공개 테스트 앱 **Spacewar**. 자체 App ID를 받기 전까지 사용한다.
- 에디터에서 플레이하려면 **Steam 클라이언트가 실행 중이고 로그인**되어 있어야 한다.
- 빌드 배포 시 exe 옆에도 같은 파일이 있어야 한다.
- 자체 App ID가 생기면 이 파일과 `SteamLobbyManager`의 `_appId` 인스펙터 값을 함께 바꾼다.

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
2. 빌드 실행 → F1 → 모드를 `Local`로 → **Host**
3. 에디터 플레이 → F1 → 모드를 `Local`로 → **Join (로컬)**

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
| 스폰이 조용히 실패 | `NetworkManager`의 Network Prefabs List에 프리팹을 등록했는가 |
| 씬 전환이 동기화 안 됨 | `NetworkManager.SceneManager.LoadScene`을 썼는가 (`SceneManager.LoadScene` 아님) |
| 접속은 되는데 아무것도 안 보임 | Player Prefab이 지정되어 있는가 |

`NetworkManager`의 LogLevel이 `Developer`로 설정되어 있어 트랜스포트가 Steam 연결 과정을
상세히 로그한다. 조용해지면 Normal로 낮춘다.
