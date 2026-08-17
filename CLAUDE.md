# CLAUDE.md

GhostHunter — 1인칭 멀티플레이 "가구 던지기" 프로토타입.

> 상세 설계는 [docs/](docs/)에 있습니다. 이 파일은 매 세션 컨텍스트에 로드되므로 요약만 유지합니다.

## 기술 스택

| 항목 | 값 | 비고 |
|---|---|---|
| Unity | **6000.3.20f1** (Unity 6.3) | 버전 변경 시 팀 전체 합의 필요 |
| 렌더 파이프라인 | URP 17.3 | PC 프리셋(`Assets/Settings/PC_RPAsset.asset`) 사용 |
| 입력 | Input System 1.19 (**신규 전용**, `activeInputHandler: 1`) | 레거시 `Input.*`는 런타임 예외 |
| 네트워킹 | Netcode for GameObjects **2.13.1** | 설치됨 |
| Steam 트랜스포트 | `com.community.netcode.transport.facepunch` | **임베드 + 패치됨** — 아래 주의 |
| 로컬 트랜스포트 | `com.unity.transport` 2.7.3 (UTP) | NGO 의존성으로 자동 설치 |
| Steam 래퍼 | Facepunch.Steamworks | 트랜스포트 패키지에 DLL 번들 |
| 물리 | PhysX (built-in 3D) | DOTS Physics 사용 안 함 |
| 에셋 직렬화 | Force Text (`m_SerializationMode: 2`) | 유지할 것 |

## 절대 규칙

- **`Packages/com.community.netcode.transport.facepunch/`는 벤더링된 서드파티 사본이다.**
  패치 없이는 컴파일되지 않는 upstream 버그가 있어 임베드했다. 손대기 전에
  [PATCHES.md](Packages/com.community.netcode.transport.facepunch/PATCHES.md)를 읽고,
  수정하면 거기에 기록한다. 이 폴더에 우리 기능을 추가하지 않는다.
- **`.meta` 파일은 항상 짝으로 커밋**한다. 파일/폴더 이동·삭제는 Unity 에디터 안에서 하고, 셸에서 `mv`/`rm` 하지 않는다.
- **`.unity` / `.prefab` / `.asset` YAML을 손으로 편집하지 않는다.** 씬·프리팹 구성은 에디터에서 하거나, 필요하면 `Assets/Scripts/Editor/`에 에디터 스크립트를 만들어 처리한다.
- **Unity 빌드/플레이는 Claude가 직접 실행할 수 없다.** 코드 변경 후에는 "에디터에서 컴파일 확인 후 알려달라"고 요청하고, 컴파일 성공을 임의로 단정하지 않는다.
- **물리 상태의 권위는 서버(호스트)에만 있다.** 클라이언트는 입력/의도만 RPC로 보낸다. 클라이언트에서 `Rigidbody`에 직접 힘을 가하지 않는다.
- **던질 수 있는 가구는 씬에 배치된 실제 가구다**(씬 배치 `NetworkObject`). 더미 큐브를 스폰하지 않는다.
  생성 도구는 **`House_01`의 방을 비운 채로** 집을 만들고, 가구는 집 북쪽 `Furniture_Library`에
  종류별로 한 개씩 일렬로 놓는다. 방 배치는 거기서 복사해 `House_01/PhysicsFurniture` 아래에
  붙여 넣는다 — 생성 도구에 방별 가구 좌표를 다시 심지 않는다(손으로 한 배치와 겹친다).
  붙박이(`Fixtures/`)는 정적 콜라이더로 남긴다.
  **예외는 도면 배율 비교용 집**(`House_01_OriginalScale_Right`, 집 동쪽 3m 옆, 배율 ×1)뿐이다.
  이 집은 "도면 치수에서 가구가 방을 얼마나 채우는가"를 보려고 세운 것이라 생성 도구가
  방마다 가구를 깔아 주고, `ValidateFurnishedHouse`가 겹침·문짝 동선·통행로를 따로 검사한다.
- **맵은 `HousePrototypeBuilder.MapScale`(현재 ×2)로 평면(X·Z)만 넓힌다.** 배율은 상수 정의부에서
  **좌표에만** 곱한다 — 트랜스폼 스케일을 쓰면 개구부 폭과 벽 높이까지 같이 늘어난다.
  **벽 높이·벽 두께·문 폭·창 크기·붙박이·계단·가구·플레이어·투척 수치는 배율을 받지 않는다.**
  씬의 어떤 오브젝트도 스케일 1이 아니면 안 된다(생성 검증이 벽 높이와 문 크기를 확인한다).
- **침실 2칸은 예외로 프리셋이 자동으로 채운다.** `Room_Presets`(집 남쪽 바깥) A·B·C 중 둘을
  세션 시작 때 서버가 중복 없이 뽑아 `House_01/RoomSlots`로 옮긴다. 프리팹을 스폰하는 게 아니라
  **이미 스폰된 가구를 옮기는** 방식이라 중첩 `NetworkObject`가 생기지 않는다.
  침실에는 손으로 가구를 놓지 않는다 — 뽑힌 프리셋과 겹친다.
  프리셋 가구는 방 남쪽 1.11m 띠를 비워야 한다(두 침실의 문 위치가 달라서, 그래야 어느 슬롯에
  뽑혀도 문이 가구를 쓸지 않는다). 생성 검증이 전 조합을 확인한다.
  Spawn Point 랜덤은 아직 없어서, 그 자리(바닥·책상 위)는 소품 큐브로 고정 배치해 두었다.
- 레거시 `Input.GetKey` / `Input.GetAxis` 금지 — Input System만 사용한다.
- `GameObject.Find`, `SendMessage`, `Camera.main`(매 프레임) 금지. 참조는 인스펙터 직렬화 또는 명시적 주입으로 해결한다.

## 폴더 구조

```
Assets/
├─ Scenes/           MainMenu → Lobby → Prototype (빌드 순서), SampleScene(템플릿)
├─ Scripts/          asmdef: GhostHunter.Runtime
│  ├─ Core/          GameLayers, GameScenes 등 상수·공통
│  ├─ Networking/    SteamLobbyManager, ConnectionManager, NetworkRigBootstrap ← 구현됨
│  ├─ Player/        이동, 시점, 플레이어 네트워크 표현            ← 구현됨
│  ├─ Interaction/   조준 타겟팅, 그랩 컨트롤러, 문 여닫기(E)      ← 구현됨
│  ├─ Furniture/     가구 오브젝트, 부양 모터, 발사, 아웃라인       ← 구현됨
│  ├─ Map/           방 프리셋, 슬롯 랜덤 배치                     ← 구현됨
│  ├─ UI/            크로스헤어, 차지 게이지, 메인메뉴/로비 화면    ← 구현됨
│  ├─ DebugTools/    ConnectionHud, 가구 리셋(R), 런타임 스모크 테스트 ← 구현됨
│  └─ Editor/        NetworkRigSetup, PrototypeSceneSetup, MenuScenesSetup ← 구현됨
├─ Prefabs/          NetworkRig, Player
└─ Settings/         URP 에셋 (건드리지 말 것)
```

- 폴더명이 `Debug`가 아니라 **`DebugTools`**인 이유: `GhostHunter.Debug` 네임스페이스는
  `UnityEngine.Debug`를 가려 그 안의 모든 `Debug.Log` 호출을 깨뜨린다.
- `GhostHunter.Runtime` asmdef는 `includePlatforms`가 **Editor + WindowsStandalone64 +
  macOSStandalone**로 제한되어 있다. Facepunch 패키지가 매니지드 DLL을 제공하는 플랫폼만
  넣은 것이다. 목록에 없는 플랫폼을 타겟하면 게임플레이 코드가 통째로 사라진 것처럼 보인다.
  Steam DLL이 없는 플랫폼(모바일/WebGL 등)이 필요해지면 Steam 코드를 별도 어셈블리로 분리한다.
- **macOS:** 네이티브 `redistributable_bin/osx/libsteam_api.bundle`을 arm64 포함 유니버설로
  교체해서 Apple Silicon에서도 네이티브로 돈다(PATCHES.md 패치 4). 대신 매니지드 DLL은
  구버전이라 Valve가 폐기한 API 49개가 맥에서만 없다 — `QuickStatus().Ping` 같은 걸 쓰면
  맥에서만 `EntryPointNotFoundException`이 난다.
- `Assets/Scripts/Temp.cs` 템플릿 잔재는 Prototype 생성 도구가 삭제했다.

## 네트워크 리그는 씬에 배치하지 않는다

`NetworkManager` + 트랜스포트 + `SteamLobbyManager` + `ConnectionManager`는
`Assets/Prefabs/NetworkRig.prefab` **하나**에 들어 있고, 각 씬에는 `NetworkBootstrap`
오브젝트만 있다. 부트스트랩은 `ConnectionManager.Instance`가 없을 때만 리그를 생성한다 —
리그는 `NetworkManager`가 스스로 `DontDestroyOnLoad` 하므로 씬을 넘어 살아남고, 씬마다
리그를 배치하면 NGO가 정리하지 않는 중복 `NetworkManager`가 생긴다.

씬별 부트스트랩 설정(`_autoStartFromLobbyEvents`)이 흐름을 가른다.
- **Prototype 단독 플레이**: 켬 — 로비가 준비되면 즉시 세션을 시작한다(HUD 흐름).
- **MainMenu / Lobby**: 끔 — 로비 입장은 대기실일 뿐이고, 세션 시작 시점은 로비 UI가 정한다.

### 네트워크 프리팹의 GlobalObjectIdHash 함정

`PrefabUtility.SaveAsPrefabAsset`을 임시 **씬 오브젝트**에 대해 부르면, `NetworkObject.OnValidate`가
에셋이 아니라 씬 기준으로 해시를 계산해서 **모든 프리팹이 같은 해시**를 갖고 `m_InScenePlaced`가
true로 박힌다. NGO가 프리팹을 구분하지 못하는데 에러 없이 엉뚱한 게 스폰되는 식으로 조용히 깨진다.

`PrototypeSceneSetup`은 저장 후 `ImportAsset(ForceUpdate)`로 재계산시키고,
`FlushNetworkPrefabIdentity()`로 디스크까지 내려보낸 뒤 `ValidateNetworkPrefabIdentity()`로
해시가 0이 아니고 서로 겹치지 않는지 검사한다. 네트워크 프리팹을 새로 추가하면
`NetworkPrefabPaths`에도 넣어야 이 검사에 걸린다.

**씬에 놓는 `NetworkObject`(문, 스포너)도 같은 함정이 있다.** `NetworkObject.OnValidate`는
씬이 저장되어 영구 ID가 생기고 그 씬이 Build Settings에 들어 있어야만(`buildIndex >= 0`)
해시를 계산한다. 생성 중인 새 씬은 둘 다 아니라 해시가 0으로 남고, 0이 여럿이면 클라이언트가
씬 오브젝트를 찾지 못한다. `RefreshScenePlacedNetworkObjects()`가 저장·빌드목록 등록 뒤
씬을 다시 열어 `OnValidate`를 돌리고 한 번 더 저장한 다음, 해시가 0/중복이 아니고
`m_InScenePlaced`가 참인지 검사한다.

## 멀티플레이 빠른 시작

**메뉴 흐름 (Steam 필요):** `Assets/Scenes/MainMenu.unity`을 열고 플레이한다.

1. **방 생성** → Steam 로비 생성 + 6자리 방 코드 발급 → 로비 씬으로 이동.
2. 상대는 **방 참가**에 방 코드를 입력하거나, 호스트의 **초대** 오버레이로 들어온다.
3. 게스트가 **준비**를 누르면 호스트의 **게임 시작**이 활성화된다.
4. 호스트가 시작하면 Prototype 씬을 로드한 뒤 `StartHost` → 로비에 시작 신호 → 게스트 접속.

세션 시작 순서(씬 로드 → StartHost → 로비 신호)는 지켜야 한다. 로비 씬에서 바로
`StartHost` 하면 플레이어가 스폰 지점 없는 씬에 스폰되고, 신호를 먼저 보내면 게스트가
세션 없는 호스트에 접속한다.

**단독 플레이 (Steam 없이 로직만):** `Prototype.unity`을 열고 플레이 → 접속 HUD에서
`Local` / **Host**. **F1**로 HUD를 토글한다.

씬/프리팹 재생성은 메뉴 **`GhostHunter > 프로토타입 게임 생성`**(리그·플레이어·가구·Prototype)과
**`GhostHunter > 메인메뉴·로비 씬 생성`**(MainMenu·Lobby)을 쓴다. 후자가 Build Settings의
씬 목록도 MainMenu 우선으로 맞춘다.

세부 사항은 [docs/03-multiplayer-setup.md](docs/03-multiplayer-setup.md).

## 코딩 컨벤션 (요약)

- 네임스페이스는 폴더와 1:1: `GhostHunter.Player`, `GhostHunter.Furniture`, …
- `private` 필드는 `_camelCase`, 인스펙터 노출은 `[SerializeField] private`로 (public 필드 금지).
- RPC 네이밍: `DoThingServerRpc` / `OnThingClientRpc`. 서버 RPC는 항상 파라미터 유효성을 검증한다.
- 튜닝 수치는 코드 상수가 아니라 `ScriptableObject` 설정 에셋(`FurnitureThrowSettings` 등)에 둔다.
- 물리 처리는 `FixedUpdate`, 입력 폴링/카메라는 `Update`/`LateUpdate`.
  단 `CharacterController`(=`PlayerMotor`)는 예외로 `Update`에서 움직인다 — 스윕 이동이라
  물리 스텝에 묶을 이유가 없고, 50Hz로 움직이면 자식인 카메라가 끊겨 보인다.

전체 규칙: [docs/07-conventions.md](docs/07-conventions.md)

## 자주 쓰는 명령

```powershell
# 컴파일 오류 확인 (에디터가 켜져 있을 때 로그 tail)
Get-Content "$env:LOCALAPPDATA\Unity\Editor\Editor.log" -Tail 80

# 스크립트 변경 후 C# 프로젝트 재생성은 Unity 에디터가 자동 처리 (.csproj는 gitignore 대상)
```

빌드/플레이 스크립트는 아직 없다. 필요해지면 `Assets/Scripts/Editor/BuildPipeline`에 추가한다.

## 문서 인덱스

| 문서 | 내용 |
|---|---|
| [docs/01-project-overview.md](docs/01-project-overview.md) | 프로토타입 목표, 범위, 검증하려는 것 |
| [docs/02-architecture.md](docs/02-architecture.md) | 씬/어셈블리/런타임 구조, 데이터 흐름 |
| [docs/03-multiplayer-setup.md](docs/03-multiplayer-setup.md) | NGO + Facepunch Steamworks 설치·설정 절차 |
| [docs/04-player-controller.md](docs/04-player-controller.md) | 1인칭 이동/시점, 입력 매핑, 네트워크 동기화 |
| [docs/05-throw-system.md](docs/05-throw-system.md) | 타겟팅 → 홀드 → 부양 → 발사, 2인 흡착 규칙 |
| [docs/06-furniture-physics.md](docs/06-furniture-physics.md) | 가구 오브젝트, 아웃라인, 물리 파라미터 |
| [docs/07-conventions.md](docs/07-conventions.md) | 코딩/네이밍/Git 컨벤션 |
| [docs/08-roadmap.md](docs/08-roadmap.md) | 작업 체크리스트와 마일스톤 |
| [docs/09-map-generation.md](docs/09-map-generation.md) | 맵 생성 시스템 기획서 — House/Room Slot/Room Preset, Spawn Point, 콘텐츠 배치 |
