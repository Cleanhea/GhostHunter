# 04. 플레이어 컨트롤러 (1인칭 + 동기화)

> **2026-09-28:** 이 문서의 `GhostHunter > …` 설치·생성·검증 메뉴와 `Editor/…Setup.cs` 도구는 [ADR-0020](decisions/ADR-0020-remove-one-off-editor-setup-tools.md)으로 삭제됐다.
> 도구 실행 절차·결과는 구현 당시 기록이다. 지금은 저장된 씬·프리팹이 원본이고 직접 고친다.

## 구성

| 컴포넌트 | 실행 위치 | 역할 |
|---|---|---|
| `PlayerInputReader` | 로컬 소유자만 | Input System 액션 → 값 노출. 다른 로직은 이걸 읽기만 함 |
| `PlayerLook` | 로컬 소유자만(피치 복제는 전원) | 마우스 델타 → 요(몸통 Y 회전) / 피치(카메라 X 회전) |
| `PlayerMotor` | 로컬 소유자만(카메라 높이 복제는 전원) | `CharacterController`로 이동·중력·점프 |
| `PlayerInteractor` | 로컬 소유자만 | 조준선 끝의 문을 찾아 E 입력을 넘김 |
| `ClientNetworkTransform` | 전원 | 소유자가 쓰고 나머지가 읽는 트랜스폼 복제 |
| `PlayerVisuals` | 전원 | 생존 몸(두더지 모델) 표시. 로컬 몸은 자기 Game 카메라에서만 가리고 Scene 뷰에는 표시한다. 사망 시 모델째 복제해 시체를 만든다 |
| `PlayerCharacterAnimator` | 전원 | 루트 이동 속도로 몸 모델의 대기·걷기를 고른다 — 아래 "캐릭터 모델·애니메이션" |
| `PlayerNameTag` | 전원(요청은 소유자, 확정은 서버) | 닉네임 복제 — 아래 "머리 위 닉네임" |
| `PlayerNameTagView` | 전원(표시는 원격 플레이어만) | 머리 위 World Space 닉네임. `GhostHunter.UI` 어셈블리 |
| `SpectatorController` | 로컬 소유자만 | 사망 후 관전(자유시점·생존자 추종) — `docs/project/spectator-system.md` |

## 이동 방식: CharacterController

`Rigidbody`가 아니라 `CharacterController`를 쓴다.

- 프로토타입 이동에 필요한 건 예측 가능한 캡슐 이동이지 물리 상호작용이 아니다.
- 플레이어가 가구를 몸으로 밀어 물리를 흔드는 건 지금 원하는 게 아니다 (던지기만 물리로 다룬다).
- 나중에 "가구에 부딪히면 밀린다"가 필요해지면 `OnControllerColliderHit`에서 명시적으로 처리한다.

### 이동은 `Update`에서 돈다 (`FixedUpdate` 아님)

`CharacterController.Move`는 `Rigidbody`가 아니라 즉시 반영되는 스윕 이동이라 물리 스텝에
묶을 이유가 없다. 반대로 `FixedUpdate`(기본 50Hz)에 두면 **프레임마다 그려지는 카메라가
물리 스텝 단위로만 움직여서** 화면 주사율이 50Hz의 배수가 아닐 때 프레임 드랍처럼 보이는
떨림이 생긴다. 그래서 `PlayerMotor`만 `Update` + `Time.deltaTime`으로 돌고,
가구 `Rigidbody` 물리는 그대로 `FixedUpdate`에 남는다.

## 입력 매핑

`Assets/InputSystem_Actions.inputactions`의 기본 액션을 재사용한다.

| 액션 | 바인딩 | 용도 |
|---|---|---|
| `Move` | WASD / 좌스틱 | 수평 이동 |
| `Look` | Mouse Delta / 우스틱 | 시점 |
| `Jump` | Space | 점프 (엎드린 중에는 무시) |
| `Attack` | 마우스 좌클릭 (**Hold 아님, press/release 둘 다 필요**) | 가구 잡기/던지기 |
| `RotateFurniture` | 마우스 휠 (`<Mouse>/scroll`, 부호만 사용) | 2인 잡기 가구 회전/기울이기 한 칸 → [throw-system.md §3.1](throw-system.md) |
| `RotateFurnitureMode` | 휠 클릭 (`<Mouse>/middleButton`) | 휠 조작 회전 ↔ 기울이기 전환 |
| `Interact` | E | 문 여닫기 (조준선 2.5m 안의 문) |
| `Crouch` | C (홀드) | 웅크리기 |
| `Burrow` | **T** | 굴착 스킬 토글 — **확정(2026-09-05)**. 기획서의 E는 `Interact`(E)와 겹쳐 채택하지 않았다 → [mole-skill-system.md §3.5](../project/mole-skill-system.md) |
| `Detect` | **Q** | 탐지 스킬 — 소유자 화면에서만 5초 동안 활성 마커를 표시하고 종료 후 10초 대기 |
| `Prone` | Z (토글) | 엎드리기 — 침대 밑으로 기어 들어가는 3번째 자세 |
| **`Pause`** | **ESC** / 게임패드 Start | **일시정지 메뉴 열기.** 닫기는 기존 `UI/Cancel`(`*/{Cancel}`) 이 받는다 (아래 참조) |
| `QuickSlot` | **Tab** (홀드) | 퀵슬롯(라디얼 휠) 열기 — 사용자 확정 2026-09-12. 뗀 순간 확정한다 → [quick-slot-system.md](../project/quick-slot-system.md) |

> **`Burrow` 는 T 로 확정됐다(2026-09-05).** 커밋 `e851cff` 가 E→R(Interact 충돌 회피),
> 커밋 `0aad69e` 가 R→T 로 옮겼고, 실제 바인딩인 T 를 그대로 채택했다.
> `ProjectWiringTests`의 굴착 키 단언도 현재 확정값 T에 맞춰져 있다.
>
> **입력 잠금은 세 가지고 서로 독립이다.**
> `SetGameplayInputLocked` 는 일시정지 메뉴용으로 **시점까지 전부** 0으로 만들고,
> `SetSkillInputLocked` 는 굴착용으로 **`Look` 과 `Burrow` 만 남기며**(2026-09-05 구현),
> `SetWheelInputLocked` 는 퀵슬롯 휠용으로 **`Look` 만 0으로 만들고 `Move`·`Crouch`·`Sprint`·
> `QuickSlot` 은 남긴다**(2026-09-12 구현, QS-5). 겹치면 **메뉴 > 굴착 > 휠** 순으로 더 강한 쪽이
> 이긴다. 어느 잠금이든 `CrouchHeld` 는 마지막 값으로 얼려, 잠기는 것만으로 자세가 바뀌지 않게
> 한다. 굴착은 시전 시작에 걸고 **정상 종료·시전 취소·디스폰 세 경로 모두**에서 푼다 →
> [mole-skill-system.md §5.5.1](../project/mole-skill-system.md). 휠은 열리는 조건이 깨지는
> 즉시 스스로 닫으며 푼다 → [quick-slot.md](quick-slot.md).
>
> **탐지 스킬은 `Q`로 배선됐다**(`Player/Detect`). 판정·시각 표시·공통 UI 구현은
> [mole-skill-system.md §4·§6](../project/mole-skill-system.md)에 따른다. 탐지 결과는
> 순수 로컬이라 `NetworkVariable`·RPC를 사용하지 않는다.

> **주의:** `Attack` 액션은 홀드 방식이므로 Interaction을 `Press`(Trigger Behavior: `Press And Release`)로 두고 `started`/`canceled` 콜백을 각각 잡는다. `Hold` Interaction을 붙이면 최소 유지 시간 임계값이 생겨 짧은 탭이 씹힌다.

로컬 소유자가 아닌 플레이어 오브젝트에서는 `PlayerInput`을 **비활성화**한다. 안 그러면 원격 플레이어가 내 입력으로 움직인다.

## 문 여닫기 (E)

맵의 여닫이 문 5개(현관·침실1·침실2·욕실·창고)는 `DoorInteractable`을 단 씬 배치
`NetworkObject`다. 주방-거실 사이 1.4m 개구부는 문틀만 있어 대상이 아니다.

- `PlayerInteractor`가 카메라에서 **플레이어 레이어만 제외한** 마스크로 2.5m 레이캐스트를 쏜다.
  가구 레이어만 보면 벽 너머의 문이 열리므로 벽·가구가 시야를 막으면 대상이 되지 않는다.
- 복제하는 상태는 서버가 가진 **열림/닫힘 bool 하나**뿐이다. 트랜스폼을 복제하지 않고
  각 피어가 그 값을 보고 자기 쪽 문짝을 돌린다 — 대역폭이 거의 들지 않고, 늦게 접속한
  클라이언트는 `NetworkVariable`로 현재 상태를 받아 회전 연출 없이 스냅한다.
- 상태 변경은 서버만 한다. 문은 아무도 소유하지 않으므로 소유권 대신 **요청자와 문 사이 거리**로
  검증한다(`_maxInteractDistance`, 기본 4m).
- 씬에 저장된 각도가 곧 열린 상태이고 닫힘은 항상 로컬 Y 0°(벽과 나란함)다. 초기 상태는
  `_startsOpen`(기본 켬)이 정한다.
- 문짝은 런타임에 회전하므로 **정적 배칭에서 빼고**(배칭된 메시는 정점이 월드 좌표로 구워진다)
  경첩에 **키네마틱 `Rigidbody`**를 달아 PhysX가 정적 콜라이더 트리를 매 프레임 다시 만들지 않게 한다.

## 시점 처리

```
Player (root)          ← 요(Y) 회전. ClientNetworkTransform이 복제
├─ RemoteBody          ← 몸 컨테이너(발밑 원점). 자세에 따라 Y 스케일, 굴착 중 비활성
│  └─ Character        ← MainCharacter.fbx 인스턴스 + Animator(원격 피어에게만 보임)
└─ CameraPivot         ← 피치(X) 회전. 로컬이 계산, NetworkVariable<float>로 전원에 복제
   └─ Main Camera      ← 로컬 소유자만 enabled = true
```

- 피치는 `[-89°, +89°]`로 클램프.
- **피치는 네트워크로 복제한다**(`PlayerLook.Pitch`, Owner 쓰기 + Everyone 읽기 — `PlayerMotor`의
  `IsCrouching`/`IsProne`, `MoleBurrowController`의 `IsBurrowed`와 같은 ADR-0008 이동 권위 예외의
  연장). 사망 후 관전(`SpectatorController`)이 생존자의 상하 시선을 재현하려고 2026-09-12에
  추가했다 — 그 전까지는 로컬 전용이었다. 던지기 방향은 여전히 조준 벡터를 RPC 파라미터로
  직접 보낸다(피치 복제와 별개). `PlayerMotor.CameraLocalHeight`도 같은 방식으로 복제해
  자세·굴착에 따른 눈높이를 함께 재현한다.
- 커서: 플레이 중 `Cursor.lockState = CursorLockMode.Locked`. `PlayerLook`은 스폰·디스폰 시에만
  초기 잠금 상태를 관리한다. 플레이 중 ESC를 누르면 일시정지 메뉴가 커서를 해제·표시하고,
  닫으면 다시 잠근다. 커서가 잠기지 않은 동안 `PlayerLook`은 시점 처리를 건너뛴다.
  스테이지 → `Result`는 플레이어를 디스폰하지 않으므로(정산 음성) `Result` 동안은 `SceneFlowController`가
  매 프레임 커서를 풀어 정산 버튼을 누를 수 있게 한다(2026-10-02). 인게임 로비 재스폰 때 `PlayerLook`이 다시 잠근다.

### ESC 충돌 해소 — 일시정지 메뉴 (2026-09-04 구현)

**`Game` 씬의 일시정지 메뉴도 ESC로 연다**(사용자 확정) → [일시정지 메뉴 시스템 기획서](../project/pause-menu-system.md).
위 커서 토글이 같은 키를 소비하므로 **해소안 A로 확정했다**(PM-3).

| 항목 | 확정된 규칙 |
|---|---|
| `PlayerLook` | **`Update()`의 ESC 커서 토글 블록을 제거한다.** `escapeKey` 폴링이 사라진다 |
| 커서 소유권 | **일시정지 메뉴가 갖는다.** 열면 해제·표시, 닫으면 잠금·숨김 |
| `PlayerLook` 잔존 책임 | `OnNetworkSpawn`/`OnNetworkDespawn`의 초기 잠금·해제는 그대로 |
| 입력 경로 | 여는 것은 **`Player/Pause` 신설(ESC·게임패드 Start)**, 닫는 것은 기존 **`UI/Cancel`**. 둘 다 ESC 라서 한 번에 하나만 켠다 |
| 잠금 방식 | `PlayerInputReader.SetGameplayInputLocked` 가 노출값을 0으로 만든다. **액션 맵을 끄지 않는다** — 끄면 `CrouchHeld` 가 false 로 떨어져 메뉴를 여는 것만으로 웅크린 플레이어가 일어선다 |
| 대가 | 메뉴 없이 커서만 푸는 개발 편의가 사라진다. 개발 HUD(Tab)·튜닝 창(F2)은 자체 커서 처리를 그대로 쓴다 |

**구현됨 (2026-09-04).** 배선·검증 항목은 [pause-menu.md](pause-menu.md).
메뉴 중에는 이동·시점·상호작용·던지기·스킬이 **전부 잠기지만**, `timeScale`은 1이라
귀신·정신력은 계속 돈다 — 즉 **회피 불가 상태로 잡힐 수 있고 그것이 의도된 설계다.**

## 네트워크 동기화 방침

플레이어 이동만은 **소유자 권위**다:

- 소유자 클라이언트가 자기 트랜스폼을 직접 쓰고, `ClientNetworkTransform`이 그 값을 서버 → 다른 클라이언트로 중계한다.
- 이유: 서버 권위 + 예측/보정은 프로토타입에서 투자 대비 효과가 나쁘다. 지금 검증하려는 건 던지기 손맛이지 이동 정확도가 아니다.
- 대가: 이동에 관한 한 클라이언트를 신뢰한다. 프로토타입에서는 허용 가능한 리스크다.

`ClientNetworkTransform`은 NGO 샘플/커뮤니티 구현을 가져오거나, `NetworkTransform`을 상속해 `OnIsServerAuthoritative() => false`만 오버라이드하면 된다.

동기화 설정:
- Position: X/Y/Z 동기화, 임계값 0.01
- Rotation: **Y만** 동기화 (피치는 `PlayerLook`의 별도 `NetworkVariable<float>`로 복제 — 위 참고)
- Scale: 동기화 끔
- Interpolate: 켬 (원격 플레이어 끊김 방지)

## 스폰

- `NetworkManager`의 Player Prefab에 등록 → 접속 시 자동 스폰.
- 스폰 위치는 `Prototype` 씬의 `SpawnPoint` 오브젝트들에서 `OwnerClientId` 기준으로 골라, 서버가 `OnNetworkSpawn`에서 위치를 지정하고 클라이언트에 알린다.
  - 소유자 권위 트랜스폼이므로 서버가 위치를 직접 써도 다음 틱에 소유자 값으로 덮인다. `TeleportClientRpc(position)`로 소유자에게 지시하는 방식이 안전하다.

## 초기 파라미터

`PlayerMoveSettings` (ScriptableObject):

| 값 | 초기값 |
|---|---|
| 이동 속도 (걷기) | 5.0 m/s |
| 달리기 배수 (`sprintMultiplier`, `Sprint`=Left Shift) | 1.4× → 7.0 m/s. 웅크리는 중엔 무효 |
| 웅크리기 이동 속도 (`Crouch`=C) | 3.5 m/s |
| 공중 제어 계수 | 0.4 |
| 점프 높이 | 1.2 m |
| 중력 | -20 m/s² (실제 중력보다 무겁게 — 체감이 좋다) |
| 마우스 감도 | 0.1 (deg per pixel) |
| 캡슐 높이 / 반지름 (서있음 / 웅크림 / 엎드림) | 1.3 / 0.87 / 0.5 m · 반지름 0.3 m (폭 0.6 m) |
| 카메라 높이 (서있음 / 웅크림 / 엎드림) | 1.2 / 0.77 / 0.35 m |
| 엎드려 이동 속도 (`Prone`=Z 토글) | 1.4 m/s |

전부 플레이테스트로 바뀔 값이다. 코드에 박지 말 것. **플레이 중 `F2` 로 여는 밸런스 튜닝 창에서
이 값들(과 귀신·굴착·투척·정신력 설정 전부)을 `[Header]` 그룹별 슬라이더/입력칸으로 실시간
조정**할 수 있다 (`Assets/Scripts/DebugTools/TuningHud.cs` — SO의 `[SerializeField]` 필드를 리플렉션으로
자동 노출, 필터 검색 지원). 접속 HUD(Tab)와 별개 창이다.
**굴착 수치만은 접속 HUD 의 `두더지 스킬` 섹션에도 같은 줄이 펼쳐져 있다** — 상태·강제 조작 버튼과
같은 자리에서 바꿔 보라는 뜻이고, 두 곳이 같은 SO 를 만진다(`TuningHud.DrawInline`).

이동 속도 결정 순서: **엎드리기 > 웅크리기 > 달리기 > 걷기** (`PlayerMotor.ResolveMoveSpeed` →
순수 규칙은 `PlayerPosture`). 엎드리거나 웅크리는 동안에는 `Sprint` 입력을 무시한다.

## 3단 자세 (`PlayerStance` / `PlayerPosture`)

자세는 `PlayerMotor`가 소유하는 owner-authoritative `NetworkVariable<bool>` 둘(`_isCrouching`,
`_isProne`)에서 파생한다 — 우선순위 **엎드리기 > 웅크리기 > 서기**(`PlayerPosture.Resolve`).
캡슐/카메라 높이 전환은 세 자세 모두 같은 `PostureTransitionSpeed`로 부드럽게 이어진다.

- **엎드리기(Z 토글)** — 어디서나 켤 수 있는 저자세. 아주 느리게 기어서 이동. 카메라가 바닥
  가까이 내려간다. 점프 불가.
- 자세를 올릴 때는 목표 높이만큼 머리 위 공간이 있어야 한다(`PlayerMotor.CanOccupyHeight`) —
  침대 밑에서는 슬랫에 막혀 일어설 수 없고, 기어 나와야 웅크리기/서기로 돌아간다.
- Z로 엎드리기를 해제하면 `Crouch`(C)를 누르고 있으면 웅크리기로, 아니면 서기로 돌아간다.
  둘 다 공간이 없으면 엎드린 채로 남는다.
- 귀신은 엎드려 기어서 이동하는 것도 웅크리기처럼 **소리 탐지에서 제외**한다(P1 확장) →
  [ghost-prototype.md](ghost-prototype.md), [ghost-system.md §8.3](../project/ghost-system.md).
- 침대 밑에서 완전히 숨으면 귀신 탐지가 끊긴다 — 규칙과 "들어가는 걸 봤을 때"의 처리는
  [ghost-prototype.md](ghost-prototype.md)의 침대 밑 은신 절을 본다.

## 머리 위 닉네임 (2026-09-27)

멀티플레이 중 각 플레이어의 닉네임을 그 캐릭터 머리 위에 띄운다. Player 프리팹에 배선되어 있다
(배선하던 설치 메뉴 `PlayerNameTagSetup` 은 ADR-0020으로 삭제).

| 항목 | 규칙 |
|---|---|
| 이름 출처 | 소유자의 `ISteamLobbyService.LocalName`(Steam 표시 이름). Steam 이 준비되지 않았으면(Local 단독 검증) 빈 이름을 보내고 서버가 `Player {clientId}` 를 붙인다 — 음성 HUD 의 원격 화자 표기와 같다 |
| 복제 | `PlayerNameTag` 의 `NetworkVariable<FixedString128Bytes>`, **쓰기 권한 Server**. 소유자가 스폰 직후 `RequestDisplayNameRpc`(`InvokePermission = Owner`)로 **스폰당 한 번** 요청하고, 두 번째부터는 서버가 무시한다 |
| 서버 검증 | `PlayerNameRules.Resolve` — 제어·서식 문자(줄바꿈, 폭 없는 공백, 방향 뒤집기) 제거, 앞뒤 공백 제거, 32자(Steam 한도)로 자르되 서로게이트 쌍은 쪼개지 않음, 비면 대체 이름. 32자는 UTF-8 최대 96바이트라 `FixedString128Bytes`(125바이트)에 잘리지 않는다 |
| 표시 | `PlayerNameTagView` 가 런타임에 자식 `NameTag`(World Space `Canvas` + `LegacyRuntime.ttf` `Text` + `Outline`)를 만든다. 리치 텍스트는 끈다(이름의 `<color>` 가 서식으로 먹지 않게) |
| 보이는 조건 | 스폰됨 · **로컬 소유자가 아님**(1인칭이라 자기 몸처럼 자기 이름도 숨긴다) · 이름 확정 · 굴착으로 땅속에 숨지 않음(`IsBurrowed` — 몸이 숨는 조건과 같다) · 설정 거리 이내. **사망한 플레이어는 몸이 남아 있으므로 이름도 유지한다** |
| 위치 | `PlayerMotor.CameraLocalHeight + HeightAboveEyes`. 복제된 눈높이를 쓰므로 웅크리기·엎드리기에 따라 같이 내려간다. 몸통(`RemoteBody`)은 자세에 따라 스케일이 바뀌어 그 밑에 달지 않는다 |
| 방향 | **플레이어 화면 카메라**와 같은 회전(화면 정렬 빌보드) — 켜진 카메라 중 렌더 텍스처 없는 게임 카메라, 여럿이면 depth 최대. 생존 중엔 PlayerCamera, 사망 후엔 SpectatorCamera 가 잡힌다. `Canvas.willRenderCanvases`(배치 굽기 직전, 시점·관전 카메라 이동이 끝난 뒤)에 맞춰 시선을 돌린 그 프레임에 따라온다. `Camera.main` 을 찾지 않는다. **카메라마다 렌더 직전에 돌리면 안 된다** — UGUI 캔버스는 프레임당 한 번 구운 회전을 모든 카메라가 같이 써서, 직전 프레임에 마지막으로 그린 카메라 쪽을 본다. 처음 구현이 그랬고 에디터 Game 뷰에서 Scene 뷰 카메라 쪽을 봐 종이처럼 옆면이 보였다(2026-09-27 수정). Scene 뷰에서는 Game 카메라 쪽을 본다 |
| 가림 | World Space 캔버스는 깊이 테스트를 하므로 **벽·가구 너머의 이름은 보이지 않는다** |
| 수치 | `Assets/Settings/Gameplay/PlayerNameTagSettings.asset` — 눈 위 0.45m, 최대 거리 20m(0=무제한), 글자 높이 0.16m, 래스터 64px. F2 튜닝 창에 자동 노출된다. 글자 크기·색은 스폰 시점에 한 번 적용한다 |

자동 검증(2026-09-27, 검증용 복제 프로젝트 batchmode — 설치 메뉴 실행 후): EditMode **314/314**
(`PlayerNameTagTests` 7건), PlayMode **64/64**(`PlayerNameTagFlowTests` 6건 — 서버 정리·확정, Steam 미연결 대체 이름,
두 번째 요청 무시, 원격 소유 미요청, 원격 표시·크기·높이, 자기 이름 숨김). 설치 전후 Player `GlobalObjectIdHash` 동일.

방향 수정 검증(2026-09-27, 검증용 복제 프로젝트): `PlayerNameTagFlowTests` 에 2건 추가 — 화면 카메라(40° 비스듬히) 뒤에
옆 카메라가 같은 프레임에 그려도 화면 카메라를 본다(같은 자리 렌더 텍스처에서 이름표 폭 40px 초과), 시선을 돌린 프레임에
바로 따라온다. **수정 전 코드에서는 두 건 모두 실패**했다(옆 카메라 쪽으로 정확히 90°). PlayMode **74/74**.
batchmode 에서는 화면(렌더 텍스처 없음) 카메라가 렌더되지 않으므로, 시선 카메라는 렌더 이벤트가 아니라 켜진 카메라 목록에서 고른다.

미검증: 실제 Host/Client Play 에서의 표시·가림·한글 글꼴 폴백은 아직 눈으로 확인하지 않았다.

## 캐릭터 모델·애니메이션 (2026-09-27)

원격 플레이어의 몸을 캡슐에서 두더지 모델로 바꾸고 대기·걷기 애니메이션을 붙였다. 모델·Animator Controller·
Player 프리팹 배선은 저장된 에셋이 원본이다(설치 메뉴 `PlayerCharacterSetup` 은 ADR-0020으로 삭제 — 모델을 바꾸면 직접 다시 배선한다).
자기 몸은 1인칭 Game 카메라에서만 숨기고, 에디터 Scene 뷰에는 표시한다.

| 항목 | 규칙 |
|---|---|
| 에셋 | 모델 `Assets/Mesh/MainCharacter.fbx`(Blender), 애니메이션 `Idle.fbx`·`Walking.fbx`(Mixamo, 스킨 없음). 클립 이름 `A_Player_Idle`·`A_Player_Walk`, 컨트롤러 `Assets/Animations/PlayerCharacter.controller` |
| 리그 | **세 파일 모두 Humanoid, 아바타는 각자 생성.** 모델은 본이 `Armature` 아래 43개, 애니메이션은 Mixamo 원본이라 루트 `Hips` 아래 57개다. 본 경로가 달라 Generic 으로는 바인딩되지 않는다 |
| 크기 | 모델 높이를 서 있는 캡슐(`PlayerMoveSettings.StandingHeight` 1.3m)에 맞춘다. 원본 높이가 1.30m 라 **임포트 배율**(`globalScale`)은 1이다(2026-09-29, 1.8m → 1.3m 사용자 지정). 트랜스폼 스케일은 1로 둔다 |
| 루트 모션 | `Animator.applyRootMotion` 끔 — 이동은 `PlayerMotor` 가 한다. 걷기 클립은 XZ 를 포즈에 굽지 않아 전진량이 루트 모션으로 빠졌다가 버려진다(제자리 걸음). 회전·높이는 포즈에 굽는다(원본 기준) |
| 상태 | `Idle`(기본) ↔ `Walk`. 파라미터 `IsMoving`(Bool, 전환 0.15초·Exit Time 없음), `WalkSpeed`(Float, Walk 상태 재생 배속) |
| 속도 입력 | `PlayerCharacterAnimator` 가 **자기 화면의 루트 변위**로 수평 속도를 잰다. 소유자는 CharacterController, 원격은 ClientNetworkTransform 보간이 루트를 옮기므로 **새 NetworkVariable·RPC 가 없다.** 순간이동(스폰·텔레포트) 프레임은 무시한다 |
| 컬링 | 기본 Animator는 `CullCompletely`. 에디터에서 로컬 소유자의 Animator만 `AlwaysAnimate`로 바꿔 Scene 뷰에서 대기·걷기를 확인한다. Game 카메라 렌더 직전에 로컬 몸의 `forceRenderingOff`를 켰다가 렌더 뒤 해제한다. 굴착 중(`RemoteBody` 비활성)에는 파라미터를 쓰지 않는다 |
| 자세 | 웅크리기·엎드리기는 아직 애니메이션이 없어 **기존처럼 `RemoteBody` 를 Y 로 눌러** 표현한다 |
| 시체 | `PlayerVisuals._corpseModel`(모델 루트)을 **본까지** 복제해 캡슐 중심 루트 `Corpse_{id}` 아래 절반 높이만큼 내려 둔다. 모든 피어가 같은 자세로 눕도록 Idle 첫 프레임으로 되감아 Animator 를 멈춘다. 콜라이더는 `SpectatorSettings.CorpseHeight`(1.3m)·`CorpseRadius`(0.3m) |

`Assets/Settings/Gameplay/PlayerCharacterAnimationSettings.asset`(F2 튜닝 창 자동 노출, 구현자 임시값):

| 값 | 초기값 | 비고 |
|---|---|---|
| `MoveThreshold` | 0.2 m/s | 넘으면 걷기 |
| `SpeedSmoothTime` | 0.1 s | 원격 보간 흔들림이 대기↔걷기를 깜빡이지 않게 |
| `TeleportSpeed` | 30 m/s | 한 프레임 변위가 이보다 빠르면 무시 |
| `WalkClipSpeed` | 0.76 m/s | 1배속에서 발이 미끄러지지 않는 속도. 1.8m 두더지 실측(디딤발 0.98~1.01, 루트 모션 평균 1.08 → 1.05)을 1.3m 로 줄인 비례값이다 — 1.3m 실측은 아직 없다 |
| `MinWalkPlaybackSpeed` / `MaxWalkPlaybackSpeed` | 0.6 / 2.0 | 걷기 5m/s 는 발 속도대로면 4.8배속이라 상한 2배속에서 자른다 — **그 이상은 발이 미끄러진다** |

자동 검증(2026-09-27, 검증용 복제 프로젝트 batchmode — 설치 메뉴 실행 후): 설치 2회 실행 시 Player 프리팹 동일(멱등),
Player `GlobalObjectIdHash` 유지. EditMode **315/316** — `PlayerCharacterAnimationTests` 6건 통과, 유일한 실패
`MoleSkillWiringTests.귀신이_탐지와_포획_양쪽에서_굴착_노출을_본다` 는 작업 트리의 커밋되지 않은
`GhostPrototypeController.cs` 변경 때문으로 이 작업과 무관하다. PlayMode **72/72**(`PlayerCharacterAnimatorTests` 2건,
`DeathSystemFlowTests` 스킨 모델 시체 1건 추가). 사본에서 실제 프리팹에 Idle·Walk 를 재생해 렌더링으로 자세·방향(+Z)·
지면 접지를 확인했다.

미검증: 실제 Host/Client Play 에서 원격 두더지의 걷기 전환·재생 배속 체감, 시체 눕는 모습, 웅크리기·엎드리기 때
눌린 모습은 아직 눈으로 확인하지 않았다.

---

최종 갱신: 2026-09-28 (닉네임·캐릭터 모델 설치 메뉴 삭제 — ADR-0020. 이전: 2026-09-27 로컬 몸을 Scene 뷰에 표시하고 자기 Game 카메라에서만 숨김. 이전: 캐릭터 모델·애니메이션 — 캡슐 몸을 두더지 모델로 교체, `PlayerCharacterAnimator` 추가. 이전: 머리 위 닉네임 `PlayerNameTag`·`PlayerNameTagView` 추가. 이전: 2026-09-12 퀵슬롯 휠 `QuickSlot`(Tab) 액션과 세 번째 입력 잠금 `SetWheelInputLocked`
추가 — 우선순위 메뉴 > 굴착 > 휠 → [quick-slot.md](quick-slot.md). 이전: 2026-09-04 ESC 커서
토글을 제거하고 일시정지 메뉴가 커서를 관리하도록 **해소안 A를 구현**
— `Player/Pause` 신설·입력 잠금 포함. 이전: 엎드리기(Z 토글)
3번째 자세 추가 — `PlayerStance`/`PlayerPosture` 분리, 침대 밑 은신 연동. `Prototype` → `Game` 씬 개명 등
나머지 낡은 서술은 미정리)

### 음성 입력 (2026-09-17)

`Player/Voice`=V(PTT), `Player/VoiceMute`=M(캡처 뮤트). 메뉴·사망·굴착·휠 잠금과 독립적으로 읽는다.
PTT와 겹치던 `SpectateToggleMode`는 C로 분리했다. C 웅크리기는 생존, C 관전 전환은 사망 상태에서만 처리한다.
키 선호도 질문에 답변이 없어 제시한 기본안을 적용했다. 음성 구현은 [voice-chat.md](voice-chat.md).
