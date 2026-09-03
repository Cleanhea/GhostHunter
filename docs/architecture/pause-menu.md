# 일시정지 메뉴 · 연결 끊김 처리 구현

> 상태: **구현 완료 (2026-09-04). 자동 테스트 통과, 수동 검증 대기.**
> EditMode **141/142**(잔여 1건은 이 작업과 무관한 선재 실패) · PlayMode **12/12**.
> 게임 규칙의 권위는 [일시정지 메뉴 시스템 기획서](../project/pause-menu-system.md)다.
>
> **아직 하지 않은 것 — §10.1 선행 검증 D-1·D-2, §10.4~§10.6 수동 검증 전부.**
> 실제로 플레이해서 확인한 사람은 아직 없다.

## 1. 구현 범위

### 1.1 실제로 만든 것

| 대상 | 파일 |
| --- | --- |
| 메뉴 상태기계·버튼·모달·커서·입력 잠금 | `Assets/Scripts/UI/PauseMenuController.cs` (신규) |
| `Player/Pause` 액션 (ESC · 게임패드 Start) | `Assets/InputSystem_Actions.inputactions` |
| 게임플레이 입력 잠금 / 자세 동결 | `Gameplay/Player/PlayerInputReader.SetGameplayInputLocked` |
| 로컬 플레이어 입력 접근 | `Gameplay/Player/ILocalPlayerContext.Input` + `LocalPlayerContext` |
| ESC 커서 토글 제거 | `Gameplay/Player/PlayerLook` |
| 홀드 강제 해제(발사) | `Gameplay/Interaction/GrabController.ForceRelease()` |
| 세션 종료와 로비 퇴장 분리 · 끊김 통지 | `Core/Networking/IConnectionService` · `Networking/ConnectionManager` |
| 게스트의 NGO 씬 추적 | `Systems/SceneFlow/SceneFlowController.HandleSceneLoadedExternally` |
| `Title` 로비 복귀 버튼 | `UI/MainMenuController._returnToLobbyButton` |
| 씬 설치 도구 (Game 덧붙이기 + Title 버튼) | `Editor/PauseMenuSetup.cs` (신규, 메뉴 `GhostHunter > 일시정지 메뉴 설치`) |
| 회귀 테스트 6건 | `Tests/EditMode/PauseMenuTests.cs` (신규) |
| `GhostHunter.UI` → `Unity.InputSystem` 참조 추가 | `Scripts/UI/GhostHunter.UI.asmdef` |

### 1.2 범위

포함(설계 대상):

- `Game` 씬 전용 일시정지 메뉴 — ESC 진입·종료, 4개 항목, 로컬 UI
- 메뉴 중 **플레이어 입력 전부 잠금**과 커서 관리
- "타이틀로" / "종료"(확인 대화상자)의 세션 정리 순서
- 게스트의 세션 종료 감지와 **"호스트와 연결이 끊겼습니다." 모달 → 확인 → `Title` 복귀**
- `PlayerLook` 의 ESC 커서 토글 **제거**(§6)
- **`Title` 화면의 로비 복귀 진입점**(PM-14) — 이 기능이 만드는 유일한 `Game` 씬 밖 변경

포함하지 않음:

| 제외 | 이유 |
| --- | --- |
| `Time.timeScale` 조작 | 확정 — 멀티플레이라 항상 1 → [기획서 §3.2](../project/pause-menu-system.md) |
| 설정 화면의 내용 | 확정 — **이번 범위는 stub** (PM-6은 설정 시스템 기획서로) |
| 재접속·호스트 마이그레이션 | 범위 제외 → [networking.md §2.4](networking.md) |
| `Result` 씬 경유 | 확정 — 거치지 않는다(PM-11) |
| 서버가 아는 "메뉴 중" 상태 | 확정 — **복제 상태를 만들지 않는다**(PM-12) |
| 안전지대 경고 UI | 확정 — 두지 않는다(PM-2) |
| 메뉴 열기 조건 검사 | 확정 — **제한 없음**(PM-10). 사망·어택·굴착·홀드 중 전부 열린다 |

## 2. 상태 흐름도

```text
                        ┌──────────────── ESC / "계속하기" ────────────────┐
                        ▼                                                  │
[Playing]  ──── ESC ──▶ [PauseMenu] ───────────────────────────────────────┘
   ▲                       │   (플레이어 입력 전부 잠금 + 커서 표시)
   │                       │
   │                       ├─ "설정"     ─▶ (stub — 상태 전이 없음)
   │                       │
   │                       ├─ "타이틀로" ─▶ [Leaving] ─▶ ① 세션 종료
   │                       │                            ② (호스트만) 로비 퇴장
   │                       │                            ③ ISceneFlow.Load(Title)
   │                       │
   │                       └─ "종료"     ─▶ [ConfirmQuit] ─┬─ 취소 ─▶ [PauseMenu]
   │                                                       └─ 확인 ─▶ ①②③ 후 Quit
   │
[Playing / PauseMenu / ConfirmQuit]
        └── 세션 종료 감지(게스트) ──▶ [Disconnected] (모달, 뒤 조작 차단)
                                          │  "호스트와 연결이 끊겼습니다."  [확인]
                                          ▼
                                    ISceneFlow.Load(Title)
```

**상태는 로컬 하나다.** `Playing` ↔ `PauseMenu` ↔ `ConfirmQuit` → `Leaving` / `Disconnected`.

- `Disconnected` 는 **어느 상태에서든** 진입하고 **다른 상태를 덮는다.** 세션이 이미 없으므로
  "계속하기"·"취소"로 돌아갈 수 없다.
- `Disconnected` 는 **확인 버튼을 누를 때까지 씬을 옮기지 않는다.** 타이머가 없다.

## 3. 경유하는 서비스·인터페이스

전부 `Core` 인터페이스로 받는다. **구현 타입(`SceneFlowController`·`ConnectionManager`·
`SteamLobbyManager`)을 UI 가 직접 참조하지 않는다.**

| 인터페이스 | 경로 | 이 기능이 쓰는 것 |
| --- | --- | --- |
| `ISceneFlow` | `Core/Scenes/ISceneFlow.cs` | `Load(SceneId.Title)`. `IsLoading` 으로 중복 요청 방지 |
| `IConnectionService` | `Core/Networking/IConnectionService.cs` | `IsRunning`·`IsHost`·세션 종료(§3.1에서 확장) |
| `ISteamLobbyService` | `Core/Steam/ISteamLobbyService.cs` | 호스트만 `LeaveLobby()`. **게스트는 부르지 않는다**(PM-5) |
| `ILocalPlayerContext` | `Gameplay/Player/ILocalPlayerContext.cs` | 입력 잠금 대상(모터·시점·타겟터·그랩·굴착) 접근 |

획득은 `Services.TryGet<T>(out _)` 을 `Awake` 에서 한다 —
`MainMenuController`·`LobbyController` 와 같은 방식이다([code-style.md §7.1](../conventions/code-style.md)).
**서비스는 자기 `static Instance` 를 갖지 않고, 등록은 인스톨러에서만 한다.**

### 3.1 `IConnectionService` 확장이 필요하다

확정된 규칙 두 가지가 현재 인터페이스로는 표현되지 않는다.

| # | 확정 규칙 | 현재 코드 | 필요한 것 |
| --- | --- | --- | --- |
| 1 | **게스트는 매치를 떠나도 Steam 로비에 남는다**(PM-5) | `ConnectionManager.Disconnect()` 가 `NetworkManager.Shutdown()` 뒤에 **무조건** `_lobby?.LeaveLobby()` 를 부른다 | 로비 퇴장 여부를 분리한다. 예: `Disconnect(bool leaveLobby)` 또는 `LeaveSession()` / `LeaveSessionAndLobby()` |
| 2 | 끊김을 UI 가 알아야 한다 | `StatusChanged` 가 **사람이 읽는 문자열 하나**만 던진다. UI 가 문자열을 파싱해 분기하는 것은 금지 | 타입 있는 이벤트 추가. **PM-9(문구 통일) 확정으로 사유 enum 은 불필요** → `event Action SessionEnded` 하나면 충분하다 |

`ConnectionManager.HandleClientDisconnected()` 도 손봐야 한다. 지금은 **자신이 끊긴 게스트**에게
`LeaveLobby()` 를 부르는데, 호스트가 사라진 상황이라 그 자체는 타당하다. 다만 이 경로와
게스트의 자발적 이탈(#1)이 **같은 메서드를 쓰면 안 된다**는 점을 배선에서 구분한다.

> **MUST — 구독은 `ConnectionManager` 한 곳에서만.** UI 가 `NetworkManager` 콜백을 직접
> 구독하면 `Networking` 레이어를 건너뛰고 UI 가 NGO 수명주기를 알게 된다.

## 4. 네트워크 권위

| 데이터·행동 | 권위 | 복제 |
| --- | --- | --- |
| 메뉴 열림/닫힘 상태 | **각 Client 로컬** | **복제하지 않는다** [확정, PM-12] — 서버는 누가 메뉴를 열었는지 모른다 |
| 커서 잠금 상태 | 각 Client 로컬 | 복제하지 않는다 |
| **메뉴 중 입력 전부 잠금** | 각 Client 로컬 — 소유자 권위 이동의 **입력 단계에서만** | 복제하지 않는다 → [ADR-0008](decisions/ADR-0008-owner-authoritative-player-movement.md) |
| 귀신 탐지·어택·잡힘 | **Server** | 메뉴와 무관하게 계속 판정된다 |
| 정신력 증감 | **Server** | 〃 |
| 가구 물리 | **Server** | 〃 |
| **메뉴를 열 때의 가구 발사**(PM-15) | **Server** | 클라이언트는 평소와 같은 `RequestReleaseRpc` 를 보낼 뿐이다. **메뉴 전용 경로를 새로 만들지 않는다** |
| 세션 종료(`Shutdown`) | 부른 피어의 로컬 행위 | 호스트가 부르면 전원이 끊긴다(PM-4) |
| Steam 로비 퇴장 | 각 피어 자신 | **호스트만 부른다.** 게스트는 남는다(PM-5) |
| "타이틀로" 씬 전환 | **로컬 전용**(세션 종료 후) | §5 의 순서를 어기면 NGO 가 전원을 끌고 간다 |

**MUST — 메뉴는 서버 상태를 만들지 않는다.** 입력이 잠긴다는 사실을 서버에 보고하지 않고,
무적·정지·보호 시간을 요청하지 않는다. 캐릭터는 잠긴 동안에도 서버 시뮬레이션 안에 그대로 있다
— 중력·충돌·귀신에게 잡히는 것은 계속된다([기획서 §3.3](../project/pause-menu-system.md)).

## 5. 씬 전환 순서 — 코드에서 확인한 제약

> 이 절은 **추측이 아니라 `SceneFlowController.LoadAsync` 를 읽고 확인한 사실**이다.
> 규약으로 승격한 내용은 [networking.md §3.6.1](networking.md).

### 5.1 세션이 살아 있는 동안 게스트는 씬을 전환할 수 없다

```csharp
// Systems/SceneFlow/SceneFlowController.cs
if (useNgo && !networkManager.IsServer) { /* 경고 후 return */ }
```

`useNgo` 는 `NetworkManager.IsListening && EnableSceneManagement` 다. 즉 **세션이 살아 있는 동안
게스트가 `ISceneFlow.Load(Title)` 을 부르면 경고 로그만 남고 아무 일도 일어나지 않는다.**

### 5.2 호스트가 세션 중에 전환하면 전원이 끌려간다

호스트는 `useNgo == true` 경로를 타므로 `NetworkManager.SceneManager.LoadScene` 이 돈다 —
**게스트 화면까지 `Title` 로 넘어간다.** PM-4가 "전원 강제 종료"이긴 하지만, 그 수단은
**세션 종료이지 씬 전환 전파가 아니다.** 게스트는 §5.3 순서를 거쳐 끊김 모달을 봐야 한다.

### 5.3 그러므로 순서는 MUST 이렇다

```text
① 세션 종료          ← NetworkManager.Shutdown()
② 로비 퇴장          ← 호스트만. 게스트는 건너뛴다 (PM-5)
③ 한 프레임 양보
④ ISceneFlow.Load(SceneId.Title)   ← 이 시점엔 IsListening=false → 로컬 로드 경로
```

"종료"도 같은 ①~③을 밟은 뒤 앱을 닫는다(PM-13). 확인 대화상자는 ① 앞에 온다.

### 5.4 ⚠️ 게스트의 `Game` 씬은 `SceneFlowController` 가 추적하지 않는다

게스트는 `Lobby` 씬에서 `ConnectToSteamHost` 를 부르고, `Game` 씬은 **NGO 클라이언트 동기화가**
올린다. `SceneFlowController.LoadAsync` 를 거치지 않으므로 게스트 쪽에서는

- `ISceneFlow.Current` 가 **`Lobby` 로 남고**
- 내부 `_currentScene` 도 **로비 씬을 가리킨다**

따라서 게스트가 "타이틀로"를 눌러 `Load(Title)` 이 성공해도 **`Game` 씬을 내리는 코드가
실행되지 않는다**(`previousScene` 이 이미 사라진 로비 씬이다). **게스트가 매치를 떠나는 경로가
확정된 이상(PM-5) 이 문제는 반드시 처리해야 한다.**

| 안 | 내용 |
| --- | --- |
| **A (권장)** | 게스트가 NGO 동기화로 들어간 씬을 `SceneFlowController` 가 받아들이게 한다(`NetworkManager.SceneManager.OnLoadEventCompleted` 구독 → `Current`·`_currentScene` 갱신) |
| B | 세션 종료 시 남은 게임플레이 씬을 정리하는 경로를 `SceneFlowController` 에 따로 만든다 |

**A 가 구조적으로 옳다** — `ISceneFlow.Current` 가 "지금 올라와 있는 게임플레이 씬"이라는
계약을 게스트에서도 지키게 된다. `SceneFlowController` 수정이므로 별도 확인을 받는다.

### 5.5 ⚠️ 클라이언트 동기화 모드가 문서와 다르다

[networking.md §3.5](networking.md)는 "클라이언트 동기화 모드는 `LoadSceneMode.Additive`다"라고
적었지만, **저장소 어디에서도 `SetClientSynchronizationMode` 를 호출하지 않는다.**
NGO 의 `ClientSynchronizationMode` 기본값은 `LoadSceneMode.Single` 이고, Single 모드는
동기화 시 **클라이언트에 이미 로드된 씬을 전부 언로드**한다(`Bootstrap` 포함).

`Bootstrap` 이 내려가면 `NetworkRig`·`SceneFlowController`·`Services` 등록이 통째로 사라지고,
이 기능이 의존하는 모든 서비스가 없어진다. **일시정지 메뉴 구현 전에 반드시 확인해야 하는
선행 항목**이다 → §10.1 D-1. Local 2인(UTP) 구성으로 재현 가능하다.

## 6. 입력 배선

### 6.1 확정된 것 — ESC 충돌 해소안 A (PM-3)

| 항목 | 규칙 |
| --- | --- |
| `PlayerLook` | `Update()` 의 **ESC 커서 토글 블록을 제거한다.** `Keyboard.current.escapeKey` 폴링이 사라진다 |
| 커서 소유권 | **메뉴가 갖는다.** 열면 `None`+`visible`, 닫으면 `Locked`+`invisible` |
| `PlayerLook` 잔존 책임 | `OnNetworkSpawn`/`OnNetworkDespawn` 의 초기 잠금·해제는 그대로 둔다 |
| 커서 미잠금 시 시점 처리 | `PlayerLook` 의 `if (Cursor.lockState != Locked) return;` 가드는 **잠금 자체가 대체하므로 정리 대상**이다. 메뉴가 `PlayerLook.enabled = false` 로 끄면 이 가드는 불필요해진다 |

### 6.2 현재 사실

| 항목 | 사실 |
| --- | --- |
| `Player` 액션 맵 | ESC 바인딩이 **없다** |
| `UI` 액션 맵 | `Cancel` 이 `*/{Cancel}` 사용처(usage)로 바인딩되어 있다 — 키보드 ESC·게임패드 취소 버튼 |
| `PlayerInputReader` | 에셋을 `Instantiate` 로 복제하고 `_runtimeActions.Enable()` 로 **전체 맵**(`Player` + `UI`)을 켠다. 현재 `UI` 맵 액션은 아무도 읽지 않는다 |
| `Game` 씬 | `EventSystem` 이 **없다**. Screen Space UI 를 붙이려면 새로 만들어야 한다 |

### 6.3 입력 경로 — 액션 두 개를 번갈아 켠다

여는 입력과 닫는 입력이 **둘 다 ESC** 라서, 한 번에 하나만 활성화한다.
동시에 켜 두면 한 번의 입력으로 열렸다 바로 닫힌다.

```text
[Closed]        Player/Pause 활성  · UI/Cancel 비활성
[Menu]          Player/Pause 비활성 · UI/Cancel 활성      → ESC 로 닫는다
[ConfirmQuit]   〃                                        → ESC 로 메뉴로 돌아간다
[Disconnected]  둘 다 비활성                              → 확인 버튼으로만 빠져나간다
```

| 결정 | 값 | 근거 |
| --- | --- | --- |
| 여는 입력 | **`Player/Pause` 신설** — `<Keyboard>/escape` + `<Gamepad>/start` | 메뉴가 닫혀 있을 때 쓸 액션이 필요하다 |
| 닫는 입력 | **기존 `UI/Cancel`** (`*/{Cancel}` 사용처) | 게임패드 취소 버튼이 공짜로 따라온다 |
| 소유자 | **`PauseMenuController` 가 액션 에셋을 자기 몫으로 복제**해 두 액션만 켜고 끈다 | 플레이어가 스폰되기 전에도 메뉴가 동작한다 |

**MUST — 레거시 `Input.*` 를 쓰지 않는다**([code-style.md §5](../conventions/code-style.md)).
`Keyboard.current` 직접 폴링은 레거시 API 가 아니라 허용되지만(`ConnectionHud` 가 그렇게 한다),
**제품 UI 는 액션 에셋을 경유한다.**

### 6.4 잠금 대상

**액션 맵을 끄지 않고 `PlayerInputReader` 가 노출값을 0으로 만든다.**

| 대상 | 잠금 중 값 |
| --- | --- |
| `Move`·`Look` | `Vector2.zero` → `PlayerMotor` 는 안 움직이고 `PlayerLook` 은 안 돈다 |
| `Attack` 눌림/뗌 · `Interact` · `Burrow` · `Prone` · `Sprint` | `false` |
| 대기 중인 점프 | 버린다 |
| **`CrouchHeld`** | **마지막 값으로 얼린다** |
| 홀드 중인 가구 | **발사한다** [확정, PM-15] — §6.5 |
| 진행 중인 굴착 | 끊지 않는다. 종료 타이머는 계속 돈다([기획서 §3.4](../project/pause-menu-system.md)) |

> **왜 맵을 끄지 않는가.** 맵을 끄면 `CrouchHeld` 가 `false` 로 떨어져 **메뉴를 여는 것만으로
> 플레이어가 일어선다.** 자세는 owner-authoritative `NetworkVariable` 이라 그 변화가 다른
> 피어에게 그대로 복제된다. 값을 얼려야 "잠금"이지 상태 변경이 아니다.
>
> **왜 중력까지 멈추지 않는가.** `PlayerMotor.MovementLocked` 는 굴착 스킬이 이미 쓰고 있고,
> 그걸 켜면 `TickMovement` 자체가 안 돌아 중력이 멈춘다. 입력만 0으로 두면 이동은 멎되
> 중력·충돌은 계속되어 [기획서 §3.4](../project/pause-menu-system.md)의 "캐릭터는 서버
> 시뮬레이션 안에 그대로 있다" 와 맞는다.

시점은 커서가 풀리는 것만으로도 멎는다 — `PlayerLook` 이 `Cursor.lockState != Locked` 면
회전을 건너뛴다.

### 6.5 ⚠️ 잠그기 전에 가구를 먼저 놓아야 한다 (PM-15)

**입력을 잠그기만 하면 가구가 발사되지 않고 계속 떠 있는다.** 코드에서 확인한 사실이다.

```csharp
// Gameplay/Interaction/GrabController.cs — Update()
if (_input.AttackReleasedThisFrame && !_testHoldLatched)
    ReleaseGrab();          // ← RequestReleaseRpc 를 보내는 유일한 경로
```

`ReleaseGrab()` 은 **`AttackReleasedThisFrame` 이 true 인 프레임에만** 불린다.
메뉴를 열며 입력을 잠그면 그 프레임이 영영 오지 않고, 서버는 홀드가 유지된 것으로 본다
(`FurnitureThrowSettings.maxHoldDistance` 15m 를 넘겨야 강제 해제된다).

**MUST — 메뉴 진입 순서** (`PauseMenuController.LockGameplay`)

```text
① GrabController.ForceRelease()          ← 발사가 여기서 일어난다
② PlayerInputReader.SetGameplayInputLocked(true)
③ 커서 해제 · 메뉴 패널 표시
```

①의 발사 방향은 그 시점의 `_camera.transform.forward` 이므로 **메뉴를 연 순간 보고 있던 방향**이
된다 — 기획서 §3.4의 "좌클릭을 뗀 것과 동일" 과 일치한다. 2인 홀드도 평소의
`launchOnFirstRelease` 정책을 그대로 탄다.

`ForceRelease()` 는 개발용 F12 입력 고정(`_testHoldLatched`)도 함께 푼다 — 그러지 않으면
고정된 홀드가 메뉴를 열어도 남는다.

> 잡기 **요청만 보내고 아직 홀드가 성립하지 않은 상태**(`_requestedObjectId != NoObjectId`)에서도
> `ReleaseGrab()` 이 그 id 로 해제를 보낸다. 별도 처리가 필요 없다.

이 순서는 EditMode 테스트 A-8 이 소스 상의 호출 순서로 고정한다.

## 7. 연결 끊김 감지

### 7.1 현재 구현

`Networking/ConnectionManager.cs` 가 `Start()` 에서 두 콜백을 구독한다.

```csharp
net.OnClientConnectedCallback  += HandleClientConnected;
net.OnClientDisconnectCallback += HandleClientDisconnected;
```

`HandleClientDisconnected` 는 **자기 자신이 끊겼고 서버가 아닐 때** 상태 문자열
`"서버와의 연결이 끊겼습니다. {DisconnectReason}"` 을 던지고 `_lobby?.LeaveLobby()` 를 부른다.
**씬 전환도, UI 표시도, 타입 있는 이벤트도 없다.** 지금은 게스트가 끊기면 빈 `Game` 씬에 남는다.

### 7.2 쓸 수 있는 NGO 신호 (2.13.1 에서 존재 확인)

| 신호 | 시그니처 | 언제 |
| --- | --- | --- |
| `OnClientDisconnectCallback` | `Action<ulong>` | 누군가 끊김. 자기 `LocalClientId` 면 자신이 끊긴 것 |
| `OnClientStopped` | `Action<bool wasHost>` | 로컬 클라이언트가 완전히 정지한 뒤 |
| `OnServerStopped` | `Action<bool wasHost>` | 로컬 서버가 정지한 뒤 |
| `OnTransportFailure` | `Action` | 트랜스포트 자체 실패(Steam·릴레이) |
| `OnConnectionEvent` | `Action<NetworkManager, ConnectionEventData>` | 위 둘의 통합 신호. NGO 가 권장 |
| `DisconnectReason` | `string` | 서버가 `DisconnectClient(id, reason)` 로 보낸 사유 |

**PM-9(문구 통일) 확정으로 UI 는 이 중 어느 것도 구분하지 않는다.** 네 경로 모두
`SessionEnded` 하나로 모으고, `DisconnectReason` 은 **콘솔 로그에만** 남긴다.

### 7.3 호스트 자신은 이 흐름을 타지 않는다

호스트에게 `OnClientDisconnectCallback` 은 **게스트가 나갔다**는 뜻이다. 분기 기준은
`clientId == LocalClientId && !IsServer` 이며, **이미 현재 코드가 그렇게 판별한다.**

## 8. 씬 · 프리팹 · 에셋 배선

| 대상 | 경로·오브젝트 | 상태 | 메모 |
| --- | --- | --- | --- |
| 메뉴 UI 프리팹 | `Assets/Prefabs/UI_PauseMenu.prefab` (예정) | 미생성 | 네이밍은 `UI_` 접두 → [unity-assets.md §2](../conventions/unity-assets.md) |
| 메뉴 컨트롤러 | `Assets/Scripts/UI/PauseMenuController.cs` (예정) | 미생성 | `GhostHunter.UI` 어셈블리. `MainMenuController` 와 같은 구성(`sealed`·`[DisallowMultipleComponent]`·`[SerializeField] Button/Text`·`Services.TryGet` in `Awake`·구독 `Start`/해제 `OnDestroy`) |
| 메뉴 패널 | 같은 프리팹의 자식 — 버튼 4개를 **계속하기→설정→타이틀로→종료** 순서로 배치 | 미생성 | 순서는 확정 사항이다. EditMode 로 검사한다(§10.2 A-4) |
| **종료 확인 대화상자** | 같은 프리팹의 별도 패널 (문구 + 확인/취소) | 미생성 | `MainMenuController._joinPanel` 처럼 **패널 `SetActive` 토글** |
| **끊김 모달** | 같은 프리팹의 별도 패널 (딤 + 문구 + 확인 1개) | 미생성 | 뒤 조작 차단용 전체 딤 `Image` 필요(`JoinPanel` 과 같은 방식) |
| Canvas | `Game` 씬 | **없음 — 신규 필요** | Screen Space Overlay + `CanvasScaler`(ScaleWithScreenSize, 1920×1080, match 0.5) + `GraphicRaycaster`. `MenuScenesSetup.CreateCanvas` 와 동일 규격 |
| EventSystem | `Game` 씬 | **없음 — 신규 필요** | `EventSystem` + **`InputSystemUIInputModule`**. 레거시 `StandaloneInputModule` 은 `activeInputHandler: 1` 에서 예외를 던진다 |
| 기존 Game UI | `Game/PrototypeUI` (`CrosshairUI`·`ChargeGaugeUI`) | 있음 | **IMGUI(`OnGUI`)** 다. Canvas 와 공존하지만 서로 모른다 |
| World Space 정신력 모니터 | `Game` 씬 Canvas 1개 | 있음 | World Space. `EventSystem` 없이 표시만 한다 |
| 입력 액션 | `Assets/InputSystem_Actions.inputactions` | **`Player/Pause` 신규 필요**(§6.3) | 액션 추가 시 [player-controller.md](player-controller.md) 표를 같은 작업에서 갱신한다 |
| `GrabController` 해제 공개 API | `Assets/Scripts/Gameplay/Interaction/GrabController.cs` | **수정 필요**(§6.5) | `ReleaseGrab()` 이 `private` 이라 메뉴가 부를 수 없다 |
| **`Title` 로비 복귀 진입점** | `Title` 씬 + `Assets/Scripts/UI/MainMenuController.cs` | **신규 필요**(PM-14) | 버튼 1개. `_lobby.IsInLobby` 일 때만 `SetActive(true)`, 누르면 `ISceneFlow.Load(SceneId.Lobby)`. 씬 편집은 `MenuScenesSetup` 또는 Unity MCP 로 한다 |
| 튜닝 수치 | **없다** | — | PM-8이 "자동 이동 없음"으로 확정돼 지연 시간 값이 사라졌다. 나중에 수치가 생기면 **`ScriptableObject`** 에 둔다 |
| 씬 편집 | `Game` 씬에 Canvas·EventSystem 추가 | — | **Unity MCP 또는 `Assets/Scripts/Editor/` 생성 도구로 한다.** `.unity` YAML 직접 편집 금지. `GhostHunter > 프로토타입 게임 생성` 재실행 금지(씬을 새로 만든다) |

> **⚠️ `EventSystem` 중복 주의.** `SceneFlowController.SuspendSceneInput` 이 씬 전환 중 이전 씬의
> `EventSystem`·`AudioListener` 를 끈다. `Game` 씬에 `EventSystem` 을 추가하면 `Lobby → Game`
> 전환에서 이 경로가 처음으로 실제 동작하게 된다 — 전환 실패 시 복구(`RestoreSceneInput`)까지
> 함께 확인한다.

## 9. 규약 준수 체크리스트 (구현 PR 리뷰용)

- [ ] 씬 전환이 `ISceneFlow` 를 경유하는가 (`SceneManager` 직접 호출 0건)
- [ ] 세션 종료 → 씬 전환 **순서**(§5.3)를 지키는가
- [ ] **게스트가 매치를 떠날 때 `LeaveLobby()` 를 부르지 않는가**(PM-5)
- [ ] **입력을 잠그기 전에 가구 해제를 먼저 호출하는가**(§6.5, PM-15)
- [ ] `Title` 의 로비 복귀 버튼이 **로비에 속해 있을 때만** 보이는가(PM-14)
- [ ] 레거시 `Input.*` 를 쓰지 않는가. 제품 UI 입력이 액션 에셋을 경유하는가
- [ ] `async void` 가 없는가 (`async UniTaskVoid` + `.Forget()`), 새 코루틴이 없는가
- [ ] `await` 에 `destroyCancellationToken` 을 넘겼는가
- [ ] 새 RPC 가 있다면 `[Rpc(SendTo.…)]` 통합 속성이고 이름이 `Rpc` 로 끝나는가 (**기본안은 RPC 0개**)
- [ ] `Steamworks` 타입이 `UI` 에 등장하지 않는가 (`ISteamLobbyService` 만 사용)
- [ ] 서비스가 `static Instance` 를 갖지 않고 인스톨러에서만 등록되는가
- [ ] 구독을 `OnDestroy`(또는 `OnDisable`)에서 **전부** 해제하는가
- [ ] `Time.timeScale` 을 건드리지 않는가
- [ ] 입력 잠금이 **로컬에서만** 일어나고 서버에 보고되지 않는가
- [ ] 새 프리팹·씬 오브젝트에 `NetworkObject` 를 붙이지 않았는가 (메뉴는 로컬 전용)

## 10. 검증 항목

### 10.1 선행 확인 (구현 착수 전)

| # | 항목 | 방법 |
| --- | --- | --- |
| D-1 | **클라이언트 동기화 모드가 `Single` 이라 게스트의 `Bootstrap` 이 언로드되는가**(§5.5) | Local(UTP) 2인 구성으로 Host + Client 접속 후 게스트 쪽 `SceneManager.sceneCount` 와 `Bootstrap` 존재 확인 |
| D-2 | 게스트의 `ISceneFlow.Current` 가 `Game` 접속 후에도 `Lobby` 로 남는가(§5.4) | 같은 구성에서 값 확인 |

**D-1 이 참이면 일시정지 메뉴보다 그 문제가 먼저다.** 서비스가 통째로 사라진 게스트에서는
"타이틀로"도 연결 끊김 복귀도 성립하지 않는다.

### 10.2 자동 (EditMode)

`Assets/Tests/EditMode/` 에 추가한다. 현재 방식은 `ProjectWiringTests` 처럼
**에셋·배선을 읽어 검증**하는 것이다.

`Assets/Tests/EditMode/PauseMenuTests.cs` — **6건 전부 통과 (2026-09-04)**.

| # | 검증 | 상태 |
| --- | --- | --- |
| A-2 | `Player/Pause` 액션이 `<Keyboard>/escape` 에 바인딩되는가 | ✅ |
| A-2b | 메뉴를 닫는 `UI/Cancel` 액션이 존재하는가 | ✅ |
| A-4 | 메뉴 항목 순서가 **계속하기 → 설정 → 타이틀로 → 종료** 인가 | ✅ `PauseMenuSetup.MenuButtonOrder` 검사 |
| A-5 | 런타임 스크립트가 `Time.timeScale` 에 **값을 쓰지** 않는가 | ✅ 주석의 언급은 허용 |
| A-6 | `PlayerLook` 에 `escapeKey` 참조가 남아 있지 않은가 | ✅ PM-3 회귀 방지 |
| A-8 | 메뉴가 **잠그기 전에** 가구를 놓는가(`ForceRelease` → `SetGameplayInputLocked`) | ✅ PM-15 회귀 방지 |

아직 만들지 않은 자동 검증:

| # | 검증 | 비고 |
| --- | --- | --- |
| A-1 | `Game` 씬에 `EventSystem` 이 정확히 1개이고 `InputSystemUIInputModule` 을 갖는가 | 씬을 열어야 해 EditMode 로는 부담. 설치 도구의 `ValidateInstallation()` 이 대신 검사한다 |
| A-3 | 메뉴 UI 에 `NetworkObject` 가 없는가 | 〃 (설치 도구가 검사) |
| A-7 | `Title` 씬에 로비 복귀 버튼이 배선됐는가 | 〃 (설치 시 `SetObjectReference` 로 보장) |

### 10.3 자동 (PlayMode)

**아직 만들지 않았다.** 기존 PlayMode 12건은 회귀 없이 통과한다(2026-09-04).

| # | 검증 | 비고 |
| --- | --- | --- |
| P-1 | 세션 종료 후 `ISceneFlow.Load(Title)` 이 성공하는가 | `NetworkManager` 를 코드로 구동. **Steam 의존 금지** — UTP 로 구성한다([networking.md §6](networking.md)) |
| P-2 | 세션이 살아 있는 동안 게스트의 `Load(Title)` 이 거부되는가(§5.1) | 회귀 방지 |
| P-3 | 게스트가 매치를 떠나도 로비 서비스의 `LeaveLobby` 가 호출되지 않는가 | PM-5. 로비 서비스를 스텁으로 대체해 호출 횟수 검사 |
| P-4 | 가구를 홀드한 채 메뉴를 열면 **서버에서 발사가 일어나는가** | PM-15·§6.5. 홀드 상태로 메뉴 진입 → `FurnitureState` 가 `Launched` 로 바뀌는지 |

### 10.4 수동 (에디터 · Local Host) — **미수행**

| # | 절차 | 기대 |
| --- | --- | --- |
| M-1 | `Bootstrap` 플레이 → Tab HUD → `Local` → Host → ESC | 메뉴가 열리고 커서가 보인다 |
| M-2 | 메뉴가 열린 채 WASD·마우스·좌클릭·E·T | **아무 반응 없음**(전부 잠금) |
| M-3 | 메뉴를 연 채 30초 대기 | 귀신·정신력이 계속 돈다(HUD 수치 변화). **게임이 멈추지 않는다** |
| M-4 | 메뉴를 연 채 귀신이 접근 | 잡힌다. **경고 UI 는 뜨지 않는다**(PM-2) |
| M-4b | 귀신에게 추격당하는 중 / 굴착 중 / 사망 후 ESC | **전부 열린다**(PM-10) |
| M-4c | 가구를 든 채 ESC | **가구가 날아간다**(PM-15). 보고 있던 방향으로 발사되고, 허공에 남지 않는다 |
| M-5 | ESC 로 닫기 | 커서가 다시 잠기고 이동·시점이 돌아온다 |
| M-6 | 메뉴 → "설정" | stub 반응만(화면 전환 없음) |
| M-7 | 메뉴 → "종료" | **확인 대화상자가 뜬다.** "취소"는 메뉴로 돌아온다 |
| M-8 | 확인 → 종료 (에디터) | 플레이 모드가 종료된다(`ExitPlaymode`) |
| M-9 | 메뉴 → "타이틀로" | 세션 종료 → `Title` 도착. **`Game` 씬이 남아 있지 않다**. `Result` 를 거치지 않는다 |
| M-9b | Local Host 로 나간 뒤 `Title` | 로비가 없으므로 **로비 복귀 버튼이 보이지 않는다**(PM-14) |
| M-10 | 콘솔 | 오류·경고 0건. 특히 `AudioListener` 중복, `EventSystem` 중복 경고 없음 |

### 10.5 수동 (Local 2인 · UTP) — **미수행**

| # | 절차 | 기대 |
| --- | --- | --- |
| L-1 | Host + Client 접속 후 **호스트가** "타이틀로" | 게스트에게 **모달**이 뜬다. 확인을 누르기 전에는 씬이 안 바뀐다. 확인 → `Title` |
| L-2 | **게스트가** "타이틀로" | 호스트 세션 유지. 게스트만 `Title` 로 가고 **Steam 로비 멤버로는 남는다**(PM-5) |
| L-2b | L-2 직후 `Title` 의 **로비 복귀** | `Lobby` 씬으로 들어가고, 호스트가 아직 매치 중이면 **자동 재접속**된다(PM-14) |
| L-3 | 호스트 프로세스 강제 종료 | 게스트가 **같은 문구**의 모달을 본다(PM-9) |
| L-4 | 게스트가 메뉴를 연 상태에서 L-3 | **모달이 메뉴를 덮는다.** "계속하기"로 돌아갈 수 없다 |
| L-5 | 호스트가 "종료" → 확인 | 게스트는 L-1과 같은 모달을 본다(PM-13) |

### 10.6 수동 (Steam 2PC) — **미수행**

L-1~L-5 를 실제 Steam 경로에서 반복한다. **PC 2대 + Steam 계정 2개가 필요하다**
→ [playbooks.md PB-08](../workflow/playbooks.md).
SDR 릴레이 경유는 끊김 감지까지 지연이 있으므로 **모달이 뜨는 데 걸리는 시간**을 기록한다.
PM-5 검증은 여기서만 진짜로 확인된다 — Steam 로비 멤버 목록에 나간 게스트가 남아 있어야 한다.

## 11. 남은 판단과 선행 검증

**기획 규칙은 전부 확정됐다**([기획서 §9](../project/pause-menu-system.md)). 남은 것은 아래뿐이다.

| 항목 | 종류 | 상태 |
| --- | --- | --- |
| **클라이언트 동기화 모드(§5.5)** | **선행 검증** | ❌ **D-1 미수행.** `Single` 로 확인되면 `SetClientSynchronizationMode(Additive)` 를 명시하거나 [networking.md §3.5](networking.md) 서술을 고친다. **이게 참이면 게스트 경로 전체가 무너지므로 다른 무엇보다 먼저 확인한다** |
| **수동 검증(§10.4~§10.6)** | 검증 | ❌ **미수행.** 실제로 플레이해 본 사람이 없다 |
| PlayMode 테스트 P-1~P-4(§10.3) | 테스트 | ❌ 미작성 |
| ~~게스트의 `Game` 씬 추적(§5.4)~~ | 구조 변경 | ✅ **A안 구현** — `SceneFlowController` 가 `SceneManager.sceneLoaded` 로 외부 로드를 받아들인다 |
| ~~`IConnectionService` 분리(§3.1)~~ | 인터페이스 변경 | ✅ `Disconnect(bool leaveLobby)` + `SessionEnded` |
| ~~`GrabController` 해제 공개 API(§6.5)~~ | 소규모 수정 | ✅ `ForceRelease()` |
| ~~`PlayerInputReader` 입력 잠금(§6.3)~~ | 소규모 수정 | ✅ `SetGameplayInputLocked(bool)` — 맵 비활성 대신 값을 0으로 만들고 자세는 동결한다 |
| 설정 화면 내용(PM-6) | **범위 밖** | 설정 시스템 기획서(미작성). 이번 범위는 stub |

---

관련: [../project/pause-menu-system.md](../project/pause-menu-system.md) ·
[overview.md §4](overview.md) · [networking.md](networking.md) ·
[player-controller.md](player-controller.md) · [steam.md](steam.md) ·
[../conventions/code-style.md](../conventions/code-style.md) ·
[../workflow/testing.md](../workflow/testing.md)

최종 갱신: 2026-09-04 (**구현 완료** — 자동 테스트 통과, 수동 검증·선행 검증 D-1 대기. 이전: 확정 15건 반영 — 홀드 중 발사의 구현 함정(§6.5, `ReleaseGrab` 이 입력 프레임에만 불린다), `Title` 로비 복귀 진입점 배선(PM-14), 열기 제한 없음(PM-10), 메뉴 상태 미복제(PM-12). §11을 '남은 판단과 선행 검증'으로 재작성. 이전: 확정 11건 반영 · 최초 작성)
