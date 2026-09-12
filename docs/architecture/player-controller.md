# 04. 플레이어 컨트롤러 (1인칭 + 동기화)

## 구성

| 컴포넌트 | 실행 위치 | 역할 |
|---|---|---|
| `PlayerInputReader` | 로컬 소유자만 | Input System 액션 → 값 노출. 다른 로직은 이걸 읽기만 함 |
| `PlayerLook` | 로컬 소유자만 | 마우스 델타 → 요(몸통 Y 회전) / 피치(카메라 X 회전) |
| `PlayerMotor` | 로컬 소유자만 | `CharacterController`로 이동·중력·점프 |
| `PlayerInteractor` | 로컬 소유자만 | 조준선 끝의 문을 찾아 E 입력을 넘김 |
| `ClientNetworkTransform` | 전원 | 소유자가 쓰고 나머지가 읽는 트랜스폼 복제 |
| `PlayerVisuals` | 전원 | 원격 플레이어 몸통 표시, 로컬은 숨김 |

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
  `_startsOpen`(기본 켬)이 정한다. `HousePrototypeBuilder`의 통로/연결성 검증은 항상 문을 연
  상태로 돌린 뒤 원래 각도로 되돌리므로, 초기 상태를 닫힘으로 바꿔도 생성 검증은 깨지지 않는다.
- 문짝은 런타임에 회전하므로 **정적 배칭에서 빼고**(배칭된 메시는 정점이 월드 좌표로 구워진다)
  경첩에 **키네마틱 `Rigidbody`**를 달아 PhysX가 정적 콜라이더 트리를 매 프레임 다시 만들지 않게 한다.

## 시점 처리

```
Player (root)          ← 요(Y) 회전. ClientNetworkTransform이 복제
└─ CameraPivot         ← 피치(X) 회전. 로컬 전용, 복제 안 함
   └─ Main Camera      ← 로컬 소유자만 enabled = true
```

- 피치는 `[-89°, +89°]`로 클램프.
- 피치는 네트워크로 복제하지 않는다 — 단, **던지기 방향은 피치에 의존**하므로 발사 시 조준 방향 벡터를 RPC 파라미터로 함께 보낸다. 원격 플레이어의 머리 각도를 시각적으로 보여줄 필요가 생기면 그때 `NetworkVariable<float> pitch`를 추가한다.
- 커서: 플레이 중 `Cursor.lockState = CursorLockMode.Locked`. `PlayerLook`은 스폰·디스폰 시에만
  초기 잠금 상태를 관리한다. 플레이 중 ESC를 누르면 일시정지 메뉴가 커서를 해제·표시하고,
  닫으면 다시 잠근다. 커서가 잠기지 않은 동안 `PlayerLook`은 시점 처리를 건너뛴다.

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
- Rotation: **Y만** 동기화 (피치는 로컬 카메라 전용)
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
| 캡슐 높이 / 반지름 (서있음 / 웅크림 / 엎드림) | 1.8 / 1.2 / 0.5 m · 반지름 0.35 m |
| 카메라 높이 (서있음 / 웅크림 / 엎드림) | 1.65 / 1.05 / 0.35 m |
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

---

최종 갱신: 2026-09-12 (퀵슬롯 휠 `QuickSlot`(Tab) 액션과 세 번째 입력 잠금 `SetWheelInputLocked`
추가 — 우선순위 메뉴 > 굴착 > 휠 → [quick-slot.md](quick-slot.md). 이전: 2026-09-04 ESC 커서
토글을 제거하고 일시정지 메뉴가 커서를 관리하도록 **해소안 A를 구현**
— `Player/Pause` 신설·입력 잠금 포함. 이전: 엎드리기(Z 토글)
3번째 자세 추가 — `PlayerStance`/`PlayerPosture` 분리, 침대 밑 은신 연동. `Prototype` → `Game` 씬 개명 등
나머지 낡은 서술은 미정리)
