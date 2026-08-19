# 아키텍처 개요

> **일부는 목표 구조다.** 씬 구조(§4)와 의존성 획득(§8)은 반영이 끝났고,
> 스크립트 레이어(§2)와 asmdef(§3)는 아직 목표다. 어디까지 왔는지는 [../project/roadmap.md §2](../project/roadmap.md).
> 현재 코드가 이 문서와 다르면 **이 문서가 목표, 코드가 미이관 상태**다.

## 1. 폴더 구조 (Assets)

```
Assets/
├── Scenes/            Bootstrap, Title, Lobby, Game, Result
├── Scripts/           런타임 C# (§2)
├── Prefabs/           NetworkRig, Player, Furniture_*, UI_*
├── Settings/          URP RP Asset · Renderer, Gameplay SO, Scenes SO
├── Materials/         M_*
├── Shaders/           SH_*
├── Tests/             EditMode / PlayMode 테스트 어셈블리
└── InputSystem_Actions.inputactions
```

**`Assets/_Project/` 래퍼를 쓰지 않는다** → [ADR-0007](decisions/ADR-0007-flat-assets-layout.md).
폴더는 **에셋 종류**로 1차 분류하고, 그 아래를 기능으로 2차 분류한다.
네이밍은 [../conventions/unity-assets.md](../conventions/unity-assets.md).

## 2. 스크립트 레이어

```
Assets/Scripts/
├── Core/          엔진 비의존에 가까운 기반 (서비스 로케이터, 서비스 인터페이스, 상수, 상태머신)
│   └── Steam/     ISteamLobbyService · 로비 DTO (Steamworks 타입을 노출하지 않는다)
├── Data/          ScriptableObject 정의 + SceneReference / SceneNameSO (런타임 로직 없음)
├── Gameplay/      실제 게임 로직
│   ├── Player/        이동·시점·스폰
│   ├── Interaction/   타겟팅·그랩·문
│   ├── Furniture/     가구 오브젝트·부양·발사·아웃라인
│   └── Map/           방 슬롯 배정·프리셋
├── Networking/    연결·세션 관리 (트랜스포트 구현은 모른다)
├── UI/            뷰·프리젠터. Gameplay를 참조하되 그 반대는 금지
├── Systems/       매니저·부트스트랩·씬 로딩 등 횡단 시스템
│   ├── SceneFlow/     SceneFlowController
│   ├── Installers/    BootstrapInstaller, SceneInstaller 파생
│   └── Steam/         SteamLobbyManager. **Steamworks 참조는 여기에만 존재**
├── DebugTools/    개발 전용 HUD·스모크 테스트 (릴리스 빌드 대상 아님)
└── Editor/        에디터 전용 도구 (별도 asmdef)
```

> 폴더명이 `Debug`가 아니라 **`DebugTools`**인 이유: `GhostHunter.Debug` 네임스페이스는
> `UnityEngine.Debug`를 가려 그 안의 모든 `Debug.Log` 호출을 깨뜨린다.

**의존 방향 (MUST 지킬 것)**

```
UI ─────────┐
            ├──▶ Gameplay ──▶ Core
Systems ────┘           └──▶ Data
Networking ──▶ Gameplay, Data, Core
DebugTools ──▶ 전부 (개발 전용, 아무도 DebugTools를 참조하지 않는다)
```

- `Core`는 **UnityEngine 과 UniTask 외에는 아무것도 참조하지 않는다.**
  UniTask 예외의 근거는 §3.1.
- `Data`는 로직을 갖지 않는다. 값과 참조만 보유한다.
- **역방향 참조 금지.** 필요하면 이벤트/인터페이스로 역전시킨다.
- 순환 참조가 생기면 asmdef 컴파일이 실패한다 — 그게 정상 동작이며, 우회하지 말고 설계를 고친다.
- **`Steamworks` 네임스페이스는 `Systems/Steam`에만 등장한다.** `Gameplay`·`UI`·`Networking`이
  플레이어 이름·아바타 같은 Steam 정보를 필요로 하면 `Core/Steam`의 `ISteamLobbyService`로 받는다.

> **AlienGhost 규약과 다른 점 하나** — `Gameplay`가 `Unity.Netcode.Runtime`을 참조한다.
> 가구·문·그랩이 전부 `NetworkBehaviour`라 이걸 `Networking` 레이어로 빼면 게임 로직이 통째로 이동한다.
> **NGO는 프레임워크지 트랜스포트가 아니므로** 참조를 허용하고, 격리 대상은 트랜스포트 구현
> (`FacepunchTransport`)과 `Steamworks`로 한정한다.

## 3. 어셈블리 정의 (asmdef)

| asmdef | 경로 | 참조 |
| --- | --- | --- |
| `GhostHunter.Core` | `Scripts/Core` | UniTask (§3.1) |
| `GhostHunter.Data` | `Scripts/Data` | Core |
| `GhostHunter.Gameplay` | `Scripts/Gameplay` | Core, Data, Unity.Netcode.Runtime, Unity.InputSystem, UniTask |
| `GhostHunter.Networking` | `Scripts/Networking` | Core, Data, Gameplay, Unity.Netcode.Runtime, UniTask |
| `GhostHunter.UI` | `Scripts/UI` | Core, Data, Gameplay, UniTask |
| `GhostHunter.Systems` | `Scripts/Systems` | 전부 + Facepunch.Steamworks, FacepunchTransport, UniTask |
| `GhostHunter.DebugTools` | `Scripts/DebugTools` | 전부 |
| `GhostHunter.Editor` | `Scripts/Editor` | 전부 (Editor 플랫폼 한정) |
| `GhostHunter.Tests.EditMode` | `Tests/EditMode` | 전부 + Test Framework |
| `GhostHunter.Tests.PlayMode` | `Tests/PlayMode` | 전부 + Test Framework |

**규칙**
- asmdef를 추가/변경하면 MUST 이 표를 갱신한다.
- 새 참조를 추가하기 전에 §2 의존 방향을 위반하지 않는지 확인한다.
- `Auto Referenced`는 런타임 어셈블리에서 끄지 않는다(기본값 유지).

### 3.1 `Core`가 UniTask를 참조하는 이유

`Core`는 "아무것도 참조하지 않는다"가 원칙이지만 **UniTask 하나만 예외로 둔다.**

`Core`에 있는 서비스 인터페이스 중 일부는 본질적으로 awaitable 이어야 한다.
`ISteamLobbyService.CreateLobbyAsync()` 는 소비자(UI)가 결과를 기다렸다가 실패를 표시해야 하고,
이것을 void + 이벤트로 바꾸면 호출부가 "요청했다"와 "끝났다"를 따로 배선하게 된다.

**UniTask는 도메인 의존이 아니라 언어 수준의 비동기 원시 타입이다.** BCL의 `Task`가 인터페이스
시그니처에 등장하는 것을 의존성이라 부르지 않는 것과 같은 이유로, 이 예외는 레이어 규칙을 무너뜨리지 않는다.

**다른 어떤 것도 `Core` 참조에 추가하지 않는다.** NGO·Steamworks·UI·Gameplay 타입이 `Core`에
등장하면 그것은 설계가 틀린 것이다.

> `ISceneFlow`는 반대로 **void + 이벤트**로 두었다. 씬 전환은 UI 버튼이 던지는 "요청"이고
> 완료를 기다릴 호출부가 없어서, awaitable 로 만들 이유가 없다.

### 3.2 플랫폼 제한은 `Systems`에만 건다

Facepunch 패키지는 **Editor / Windows32·64 / Linux64 / macOS**에만 매니지드 DLL을 제공한다.
목록에 없는 플랫폼을 타겟하면 그 DLL을 참조하는 어셈블리가 통째로 빠진다.

- **`GhostHunter.Systems`에만** `includePlatforms`(Editor + WindowsStandalone64 + macOSStandalone)를 건다.
- 나머지 런타임 어셈블리는 플랫폼 제한 없이 둔다.

단일 `GhostHunter.Runtime` 시절에는 게임플레이 코드 전체가 이 제한을 뒤집어썼다
(Mac 빌드에서 게임이 통째로 사라지는 증상). **레이어 분리의 실질적 이득 중 하나가 이것이다.**

## 4. 씬 구성

**멀티씬 아키텍처를 쓴다.** `Bootstrap`은 앱 실행 내내 로드된 채로 남고, 나머지 씬을 그 위에
additive로 얹었다 내린다 → [ADR-0004](decisions/ADR-0004-multi-scene-additive.md)

| 씬 | 역할 | 로드 방식 |
| --- | --- | --- |
| `Bootstrap` | 최초 진입. 전역 서비스 등록 후 Title로 전환 | **빌드 인덱스 0. 언로드하지 않는다** |
| `Title` | 타이틀/메뉴. 방 생성·방 코드 참가·설정·종료 | Additive (로컬) |
| `Lobby` | 방 코드 표시·멤버 목록·준비·시작 | Additive (로컬) |
| `Game` | 실제 매치. House_01 맵 | Additive — `NetworkManager.SceneManager` |
| `Result` | 결과 정산 | Additive |

**규칙**
- `Bootstrap`은 MUST 언로드하지 않는다. 영속 시스템은 `Bootstrap`에 두면 되고, **`DontDestroyOnLoad`를 쓰지 않는다.**
- 씬을 올릴 때 MUST `SceneManager.SetActiveScene()`으로 활성 씬을 새 씬으로 지정한다.
  누락하면 라이팅·스카이박스가 `Bootstrap` 기준이 되고 런타임 생성 오브젝트가 `Bootstrap`에 쌓인다.
- 전환 순서는 MUST **① 새 씬 additive 로드 → ② `SetActiveScene` → ③ 이전 씬 언로드**.
  순서를 뒤집으면 아무 씬도 없는 프레임이 노출된다.
- 전환 중 씬이 겹치는 구간에는 `SceneFlowController`가 이전 씬의 `EventSystem`·`AudioListener`를
  먼저 비활성화한다. 로드 시작 실패 시 다시 활성화한다.
- 씬 전환은 MUST `SceneFlowController`가 수행한다. **다른 코드는 `SceneManager`를 직접 호출하지 않는다.**
- 다른 레이어에서 씬 전환이 필요하면 MUST `Services.Get<ISceneFlow>()`(`Core`)로 받아 쓴다.
- 네트워크 세션 중 씬 전환은 MUST `NetworkManager.SceneManager.LoadScene`을 쓴다.
- `NetworkManager` GameObject는 NGO 제약상 다른 GameObject의 자식으로 둘 수 없으므로 씬 root에 둔다.
- 씬 단위 서비스는 그 씬의 `SceneInstaller` 파생 컴포넌트에서 `Bind`로 등록한다. 씬이 내려가면 자동 해제된다.
- 씬 이름을 코드에서 문자열로 쓰지 않는다 → [../conventions/unity-assets.md](../conventions/unity-assets.md)

### 4.1 Bootstrap 구성

```
Bootstrap                     (root 3개)
├── --- Systems ---
│   ├── SceneFlowController   씬 전환 단일 진입점 (SceneNameSO 참조, 첫 씬 = Title)
│   └── BootstrapInstaller    전역 서비스 등록 (ISceneFlow, ISteamLobbyService)
├── NetworkRig                (씬 root — NetworkManager 는 중첩할 수 없다)
│   ├── Unity.Netcode.NetworkManager
│   ├── FacepunchTransport
│   ├── UnityTransport        개발 전용. 기본값은 항상 Steam → ADR-0011
│   ├── SteamLobbyManager
│   ├── ConnectionManager     autoStartFromLobbyEvents = false (메뉴 흐름이 직접 몬다)
│   ├── ConnectionHud         개발용 IMGUI HUD (F1)
│   └── PrototypeRuntimeSmoke -smoke-test 인자가 있을 때만 동작
└── --- UI ---                (로딩 화면 자리)
```

> `NetworkRig` 는 아직 프리팹 인스턴스다. 프리팹을 풀고 `static Instance` 를 걷어내는 것은 MIG-3.
> 그때까지 `Title`/`Lobby`/`Game` 의 `NetworkRigBootstrap` 은 남아 있지만,
> `ConnectionManager.Instance` 가 이미 있어 아무것도 하지 않는다.

- **카메라와 라이트를 두지 않는다.** 멀티씬에서 `Bootstrap`은 내려가지 않으므로 게임플레이 씬과
  `AudioListener`·카메라가 중복된다. 카메라는 각 게임플레이 씬이 갖는다.
- `NetworkManager.NetworkConfig.NetworkTransport`는 같은 GameObject의 트랜스포트를 참조한다.
- NGO Scene Management는 활성화하고 `Assets/DefaultNetworkPrefabs.asset`을 등록한다.

> 이전 구조(`NetworkRig.prefab` + 씬별 `NetworkBootstrap`)는 `Bootstrap` 씬으로 대체된다.
> 리그 프리팹은 마이그레이션 완료 시 제거한다 → roadmap MIG-3.

### 4.2 에디터에서 플레이하기

서비스는 `Bootstrap`에서 등록되므로, `Bootstrap` 없이 다른 씬만 단독 실행하면 `Services.Get`이 실패한다.
메뉴 **`GhostHunter/Play From Bootstrap`** 으로 두 방식을 전환한다(기본 켜짐).

| 메뉴 | 동작 | 쓰는 때 |
| --- | --- | --- |
| **켜짐** (기본) | 어느 씬을 열고 있든 플레이하면 빌드 목록 첫 씬(`Bootstrap`)에서 시작한다 | 실제 부팅 흐름 그대로 확인할 때 |
| **꺼짐** | 지금 열려 있는 씬 구성 그대로 플레이한다 | 특정 씬을 반복 수정하며 볼 때 |

- 메뉴를 **끄고** 작업할 때는 `Bootstrap`과 작업 중인 씬을 **함께 열어둔다.**
- 강제 시작 씬은 경로가 아니라 **빌드 목록의 첫 활성 씬**을 기준으로 잡는다.

## 5. 런타임 컴포넌트 맵

```
Player 프리팹 (NetworkObject, 플레이어당 1개 스폰)
├─ CharacterController
├─ PlayerInputReader        Input System → 입력 값 노출 (로컬 소유자만 활성)
├─ PlayerMotor              이동/중력/점프. Update (스윕 이동)
├─ PlayerLook               피치/요. 카메라는 로컬 소유자만 활성화
├─ PlayerInteractor         조준선 끝의 문을 찾아 E 입력을 넘김
├─ ClientNetworkTransform   소유자 권위 위치/회전 복제
├─ PlayerVisuals            원격 플레이어 몸통 표시, 로컬은 숨김
├─ FurnitureTargeter        카메라 레이캐스트 → 현재 조준 대상 (로컬 전용)
└─ GrabController           투척 준비/2인 잡기 입력, Grab/Release RPC 송신

Furniture (씬 배치 NetworkObject, 프리팹 인스턴스)
├─ Rigidbody (서버만 non-kinematic)
├─ Collider
├─ NetworkTransform         서버 권위 복제
├─ FurnitureGrabTarget      홀더 슬롯(NetworkList) 관리, 잡기 가능 여부 판정
├─ FurnitureHoverMotor      서버 전용. 2인 잡기 중에만 스프링 힘 적용
├─ FurnitureLauncher        서버 전용. 발사 속도와 보정 각도 계산·적용
└─ FurnitureOutline         클라이언트 전용. 조준/홀드 상태에 따라 윤곽선 표시

Door (씬 배치 NetworkObject)
├─ 키네마틱 Rigidbody (경첩)
└─ DoorInteractable         열림/닫힘 bool 하나만 복제. 서버 권위, 거리 검증

UI (씬별, 로컬 전용)
├─ CrosshairUI              조준 대상 유무에 따른 상태 변화
├─ ChargeGaugeUI            투척 준비 게이지, 1인 준비/2인 잡기 표시
├─ TitleMenuController      방 생성 / 방 코드 참가 / 설정 / 종료
└─ LobbyScreenController    방 코드·멤버 목록·준비·시작
```

데이터 흐름(잡기 → 던지기)은 [throw-system.md](throw-system.md).

## 6. 레이어 / 태그

| 레이어 | 용도 |
| --- | --- |
| `Default` | 지형, 벽, 붙박이 |
| `Player` | 플레이어 캐릭터 콜라이더 |
| `Furniture` | 던질 수 있는 가구. **타겟팅 레이캐스트의 마스크** |
| `Ignore Raycast` | 조준을 막으면 안 되는 것 |

레이어 인덱스를 코드에 하드코딩하지 않는다. `Core/GameLayers.cs`에 `LayerMask.NameToLayer` 결과를 캐싱해 노출한다.

## 7. 설정 에셋 (ScriptableObject)

튜닝 수치는 전부 여기에 모은다. 코드 재컴파일 없이 플레이 중 조정하기 위해서다.

| 에셋 | 담는 값 |
| --- | --- |
| `PlayerMoveSettings` | 이동 속도, 가속, 점프 높이, 중력 배수, 마우스 감도 |
| `FurnitureThrowSettings` | 부양 거리/강성/댐핑, 차지 시간, 1인/2인 발사 속도, 최대 사거리 |
| `FurnitureDefinition` | 가구 종류별 질량, 무게 등급(1인/2인), 기본 프리팹 참조 |
| `SceneNameSO` | 씬 참조 목록 (문자열 대신) |

경로: `Assets/Settings/Gameplay/`, `Assets/Settings/Scenes/`

## 8. 런타임 구조 원칙

- **매니저는 최소화.** 전역 서비스는 씬 로딩·네트워크·Steam·오디오·입력 정도로 제한한다.
- **데이터는 SO, 상태는 컴포넌트.** 밸런스 수치를 `MonoBehaviour` 필드에 하드코딩하지 않는다.
- **결합은 인터페이스로.** 서로 다른 레이어를 직접 참조해야 할 것 같으면 `Core` 인터페이스를 검토한다.
- **초기화 순서에 의존하지 않는다.** `Awake`에서 자기 자신, `Start`에서 타인을 참조한다.
  단 `Services.Get<T>()`는 예외다 → [../conventions/code-style.md §7.1](../conventions/code-style.md)

## 9. 렌더링

- URP 17.3. PC 프리셋(`Assets/Settings/PC_RPAsset.asset`)을 쓴다.
- 품질 설정 변경은 MUST 사용자 승인 후 진행한다.
- 포스트 프로세싱은 Volume Profile로 관리한다. 카메라에 직접 설정을 박지 않는다.

## 10. 미결 사항

| 항목 | 상태 |
| --- | --- |
| asmdef 실제 분리 | 대기 (roadmap MIG-5). 현재는 `GhostHunter.Runtime` 1개 + `GhostHunter.Editor` |
| `Result` 씬 내용 | TBD — 승패 조건 확정 후 |
| 세이브/영속 데이터 방식 | TBD |
| 오디오 시스템 | TBD |

---

관련: [networking.md](networking.md) · [steam.md](steam.md) · [decisions/](decisions/README.md)

최종 갱신: 2026-08-20
