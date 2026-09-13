# 가구용 멀티 드라이버 구현

기획: [가구용 멀티 드라이버](../project/furniture-multidriver-system.md). 구현 지시:
[furniture-multidriver-implementation-prompt.md](../workflow/furniture-multidriver-implementation-prompt.md).

> FM-IMPL-1(데이터·장착)·FM-IMPL-2(분해)·FM-IMPL-3(조립 판정·실행)까지 구현했다.
> **FM-IMPL-4 중 행동 시간 UI(중앙 원형 게이지·중단 흔들림·실패 문구)와 우클릭 유지 조작은
> 2026-09-13 기획서 1.1 수정과 함께 구현했다(§2.6).** 손목 애니메이션과 3색 실루엣 시각화는
> 미구현이다 — 서버는 흰/초록/빨강 상태값을 이미 계산하지만 화면에 그리는 코드가 없다.

## 1. 구성

| 파일 | 역할 |
| --- | --- |
| `Gameplay/Player/QuickSlotItemDefinition.cs` | 드라이버 여부(`IsDriver`) |
| `Gameplay/Player/PlayerFurnitureDriverController.cs` | 장착 요청·소유자 행동 타이머·취소 판정·서버 분해/조립 실행 |
| `Gameplay/FurnitureDriver/FurnitureDriverSettings.cs` | 행동 시간(3초/9초)·임계값(15)·내구도 감소량(5)·조준 사거리·낙하 높이 SO |
| `Gameplay/FurnitureDriver/FurnitureDriverUiSettings.cs` | 행동 시간 원형 게이지의 지름·두께·색·문구·중단 흔들림 수치 SO |
| `UI/FurnitureDriverActionHud.cs` | 화면 중앙 원형 게이지 — 진행도 채움, 중단 시 멈춤·좌우 흔들림·"분해 실패/조립 실패" |
| `Gameplay/FurnitureDriver/FurnitureDisassemblyRecipe.cs` | 큰 가구 1종의 부품 구성(부품 ID·개수) |
| `Gameplay/FurnitureDriver/FurniturePartRequirement.cs` | 레시피 안 부품 하나(ID·개수) |
| `Gameplay/FurnitureDriver/FurnitureDriverCatalog.cs` | 레시피 6종 묶음 SO. 서버·조립 판정이 공유 |
| `Gameplay/FurnitureDriver/FurnitureDriverPoolItem.cs` | 씬 배치 오브젝트(큰 가구 인스턴스 또는 부품 인스턴스) 하나의 활성/비활성·내구도 |
| `Gameplay/FurnitureDriver/FurnitureAssemblyZone.cs` | 조립 영역 점유 추적·순수 판정 호출·실루엣 상태 복제·조립 실행 |
| `Gameplay/FurnitureDriver/FurnitureAssemblyRules.cs` | 조립 성립 판정 순수 함수(부품 ID별 개수 → Empty/Partial/Ready/Invalid) |
| `Gameplay/FurnitureDriver/FurnitureDurability.cs` | 아이템 내구도 감소, 가구 내구도 상속·평균 순수 함수 |
| `Gameplay/Furniture/FurnitureGrabTarget.cs` | `IsPlacementReady`에 `FurnitureDriverPoolItem` 활성 여부 추가(기존 `RandomFurnitureItem` 게이트와 병렬) |
| `Gameplay/Interaction/GrabController.cs` | 드라이버 장착 중 가구 잡기 차단(대걸레와 같은 자리) |
| `Editor/FurnitureMultiDriverSetup.cs` | `GhostHunter > 가구용 멀티 드라이버 설치/검증` 메뉴, UI만 붙이는 `가구용 멀티 드라이버 행동 UI 설치` 메뉴 |
| `Assets/InputSystem_Actions.inputactions` | `Player/UseDriver`(마우스 우클릭) 액션 신설 — 사망 중 전용 `SpectateNext`와 별개. `PlayerInputReader`가 누른 프레임(`UseDriverPressedThisFrame`)과 유지 상태(`UseDriverHeld`)를 함께 읽는다 |

## 2. 흐름과 권위

### 2.1 장착

컨트롤러에는 `PlayerCleaningController`(대걸레)와 동일한 EquipSlot → `RequestEquipRpc` →
`ConfirmEquipRpc` 경로가 있다. 퀵슬롯 1번 슬롯이 드라이버 전용이다(MD-5).
**2026-09-13 장착 연결 누락 수정:** `QuickSlotWheelUi.Confirm`이 대걸레·드라이버 양쪽의
`EquipSlot(index)`을 호출한다. 다른 슬롯 선택도 양쪽에 전달해 드라이버 장착이 서버에 남지
않게 한다. 장착 해제 또는 서버의 장착 거부 회신 시 진행 중 행동을 취소하며, 행동 중에도
드라이버 장착 여부를 확인한다. 우하단 장착 안내에는 드라이버의 우클릭 조작을 표시한다.
이전에는 UI·대걸레 슬롯만 1로 바뀌고 드라이버는 -1이라 일반 우클릭 분해를 시작할 수 없었다.

### 2.2 행동 시간·취소 — 소유자 신뢰 경계

`MoleBurrowController`의 "소유자가 로컬 타이머를 진행하고, 서버는 완료 시점에만 재검증" 모델을
그대로 따른다(이동 권위 예외의 연장, ADR-0008과 같은 자리). **시작 요청 RPC가 없다** — 우클릭을
누른 프레임에 소유자가 로컬 타이머를 시작하고, 완료된 순간에만 `RequestDisassembleRpc`/`RequestAssembleRpc`를
보낸다. 매 프레임 다음을 확인해 어긋나면 그 자리에서 취소한다(처음부터 다시, 내구도 미차감):

- **우클릭 뗌**(`UseDriverHeld == false`, 기획서 1.1 §3.2) — 행동 시간 끝까지 누르고 있어야 완료된다.
  모든 입력 잠금(메뉴·사망·굴착·휠)이 이 값도 `false`로 지우므로 잠금이 걸리면 함께 취소된다.
- 이동 입력(Move 액션 값 ≠ 0, MD-11) — 시점 회전·웅크리기·점프는 취소하지 않는다.
- 스킬 입력(굴착·탐지 키 누름).
- 드라이버 장착 해제·입력 잠금(퀵슬롯 휠 열림 포함).
- 생존 상태(`SanityNetworkState.HasSanity`) — 이 프로토타입은 귀신 공격 성립 = 즉시 사망이라
  별도 이벤트 없이 이 게이팅이 곧 MD-12(공격당함) 취소 조건이다.
- 대상 유효성 — 분해 대상이 비활성화됐거나, 조립 대상 영역이 더 이상 Ready가 아니게 됨
  (§4.2.1의 이탈·잡힘·다른 가구 진입은 조립 영역 실루엣 상태 변화로 자연히 반영된다).

### 2.3 분해 — `RequestDisassembleRpc`

서버가 발신자·장착(`ServerHasDriver`)·생존·운반·굴착 상태·거리·대상 활성 여부·레시피 존재를
재검증한 뒤: 부품 풀에서 요구 개수만큼 비활성 `FurnitureDriverPoolItem`을 찾아 원본 위치
`+ DropHeight`(기본 1m)에서 활성화(중력 켜짐, 자연히 떨어진다) → 원본 큰 가구를 비활성화 →
아이템 내구도를 감소시킨다. 부품 풀이 모자라면 지금까지 활성화한 부품을 되돌리고 실패한다
(로그만 남기고 조용히 실패 — 사용자 대상 실패 UI는 FM-IMPL-4 몫).

원본 비활성화 시 `FurnitureGrabTarget.ServerResetForPool`이 기존 홀더에게 해제를 알리고
홀더 목록·차징·투척 상태를 Idle로 초기화한다. 재조립으로 같은 원본이 다시 활성화돼도 이전
잡기 상태가 남지 않는다. 부품 활성화는 Rigidbody의 kinematic을 해제한 뒤 속도를 초기화한다.

> **버그 수정(2026-09-13, 실제 씬에서 발견)**: `ServerActivate`가 `NetworkTransform.Teleport`로
> 트랜스폼만 옮기고 **Rigidbody 자세는 옮기지 않았다.** 부품 프리팹은 `Interpolate` 바디라
> 다음 물리 스텝에 이전 자세(풀 보관 위치 `Parts` 아래 y≈-50)로 되돌아가, 분해된 부품이 침대 위가
> 아니라 보관 위치에서 활성화돼 끝없이 떨어졌다(실제 Local Host에서 매트리스·헤드·다리가
> y≈-5270에서 낙하 중인 것을 확인). 트랜스폼 이동 직후 바디 `position`은 보관 위치에 그대로
> 남는 것도 직접 확인했다. `RandomFurnitureItem.ServerPlace`와 같은 `RoomPreset.TeleportBody`
> 호출을 추가해 고쳤다. 이전 PlayMode 테스트는 트랜스폼 y만 즉시 확인해 이 결함을 잡지 못했고,
> 보간 바디로 재현하려던 PlayMode 테스트는 수정 전에도 통과해(재현 실패) 넣지 않았다 —
> 회귀 확인은 실제 씬 검증(§5)으로 한다.

### 2.4 조립 — `FurnitureAssemblyZone` + `RequestAssembleRpc`

영역 트리거의 `OnTriggerEnter/Exit`로 후보 집합만 관리하고(§3.5 하드 룰 — 전역 검색 금지),
서버가 0.2초 주기 + 트리거 이벤트 시점마다 "바닥에 놓인"(잡히지 않음 + 속도 임계 이하) 부품
개수를 세어 `FurnitureAssemblyRules.Evaluate`로 상태를 계산해 `NetworkVariable`로 복제한다.
조립 실행 시 서버는 **다시 한번** 상태를 재계산해(레이턴시로 어긋난 클라이언트 판단을 신뢰하지
않음) Ready가 아니면 거부한다 — 별도 락 없이 이 재계산만으로 MD-9(동시 시도)가 해소된다.

### 2.5 씬 풀 재배치 — ADR-0009 준수

분해·조립 모두 `NetworkObject.Spawn`을 쓰지 않는다. `FurnitureDriverPoolItem`이
`RandomFurnitureItem`과 같은 패턴(씬에 미리 배치된 NetworkObject를 서버가 활성화·비활성화·
재배치)을 큰 가구 인스턴스와 부품 인스턴스 양쪽에 공용으로 적용한다. `PoolKey`가 역할에 따라
큰 가구 종류(`FurnitureDisassemblyRecipe.LargeFurnitureId`) 또는 부품 ID를 가리킨다.

> **버그 수정(2026-09-13)**: `FurnitureDriverPoolItem`이 `OnNetworkSpawn`에서 무조건
> `_active = false`로 시작해 렌더러·콜라이더를 껐다. 부품 풀·조립 대기용 사본은 맞는
> 동작이지만, **이미 방에 배치되어 있던 실제 큰 가구(예: `BedroomPreset_B`의 더블 침대)까지
> 스폰 즉시 숨고(`FurnitureGrabTarget.IsPlacementReady`가 막아 잡기·던지기도 불가) 드라이버
> 우클릭도 대상을 못 찾아 아무 반응이 없는 버그**였다. `_startActive` 직렬화 필드를 추가해
> 이미 배치된 실제 인스턴스만 시작부터 활성화하도록 고쳤다(`FurnitureMultiDriverSetup.
> MarkLiveLargeFurnitureActive`가 `Furniture_Library`·`Furniture_TEMP`·비교용 집 아래는
> 제외하고 표시). **`GhostHunter > 가구용 멀티 드라이버 설치`를 다시 실행해 씬에 반영해야
> 한다** — 코드만 고치고 재설치 전까지는 씬의 `_startActive`가 여전히 꺼져 있어 침대가 계속
> 숨어 있다.
>
> **2026-09-13 재검증:** 현재 저장된 Game 씬에는 실제 침대 1개·옷장 2개의 `_startActive=true`가
> 반영돼 있다. 실제 Local Host에서도 세 가구 모두 활성·렌더러 표시·콜라이더 활성 상태를 확인했다.
> 현재 씬에 대해 재설치가 필요하다는 뜻은 아니다.

### 2.6 행동 시간 UI — `FurnitureDriverActionHud` (기획서 §6.1, 1.1)

로컬 전용 표시다. `Game/PrototypeUI`에 붙어 `ILocalPlayerContext.FurnitureDriverController`를
매 프레임 읽고, 자체 Screen Space Overlay 캔버스(정렬 140 — 퀵슬롯 휠 150 아래)에 런타임 링
텍스처로 게이지를 그린다. 네트워크 상태를 새로 만들지 않는다.

| 컨트롤러 상태 | 표시 |
| --- | --- |
| `CurrentAction ≠ None` | 조준점 주위 원형 게이지를 12시부터 시계 방향으로 `ActionProgress`(0~1)만큼 채움 + "분해 중/조립 중" |
| `ActionCancelSerial` 증가 | `LastCancelledProgress`에서 채움을 멈추고 실패색으로 바꾼 뒤 좌우 2회 감쇠 흔들림 + "분해 실패/조립 실패", `FailDisplaySeconds` 후 숨김 |
| 행동 없음(정상 완료 포함) | 숨김 |

중단 기록은 `CancelAction()`이 **진행 중인 행동이 있었을 때만** 남긴다 — 행동 없이 부르는 장착
해제·디스폰 정리는 실패 연출을 띄우지 않는다. 완료 후 서버가 요청을 거절한 경우(부품 풀 부족 등)는
중단이 아니라서 실패 연출이 없다. HUD는 로컬 플레이어가 바뀌면 그 시점의 일련번호를 기준으로 삼아
이전 기록을 새 중단으로 오인하지 않는다.

## 3. 에셋과 설치

- 큰 가구 프리팹 6종(`Assets/Prefabs/Furniture/{DoubleBed_1.6x2.0, Wardrobe_1.2x0.6,
  Wardrobe_1.5x0.6, DiningTable_1.55x0.85, Shelving_2.6x0.55, Shelving_1.65x0.45}.prefab`)에
  `FurnitureDriverPoolItem`을 **제자리 편집**(`LoadPrefabContents` → 추가 → 같은 경로에
  `SaveAsPrefabAsset`)으로 추가했다 — 재굽기가 아니라 `GlobalObjectIdHash`가 바뀌지 않는다.
- 부품 프리팹 15종을 `Assets/Prefabs/FurnitureDriverParts/`에 새로 생성했다(그레이박스 사각
  메시, 기존 가구와 동일한 `FurnitureGrabTarget`·`FurnitureNetworkPhysics`·`FurnitureLauncher`·
  `FurnitureHoverMotor`·`FurnitureOutline`·`FurnitureDriverPoolItem` 구성).
- `Game/FurnitureMultiDriverPrototype/Parts`: 부품 풀 23개(6개 레시피 × 부품 개수 × 세트 수).
  세트 수는 2026-09-12 씬 스캔 기준 라이브 인스턴스 개수(Furniture_Library·비교용 집 제외,
  최소 1세트 보장)다 — 더블 침대 1, 옷장 2종 각 1, 식탁·선반 2종은 라이브 인스턴스가 없어
  세트 1개만 미리 마련해 뒀다(레벨 배치 전까지 실제 분해 대상 없음).
- `Game/FurnitureMultiDriverPrototype/AssemblyZone`: 기존 `DrillCarSafeZone_Temp` 좌표를 그대로
  쓰는 트리거 1개(MD-2, 임시).
- `FurnitureDriverSettings_Default.asset`, `FurnitureDriverCatalog_Default.asset`,
  `FurnitureDriverRecipes/*.asset`(6개), `QuickSlotItem_Driver.asset`,
  `FurnitureDriverUiSettings_Default.asset`: `Assets/Settings/Gameplay`.
- `Game/PrototypeUI`: `FurnitureDriverActionHud`(→ `FurnitureDriverUiSettings_Default`). 전체 설치
  메뉴도 붙이지만, 이미 설치된 씬에는 `가구용 멀티 드라이버 행동 UI 설치`만 실행하면 된다 — 이 메뉴는
  HUD·드라이버 안내 문구만 갱신하고 프리팹·부품 풀을 다시 저장하지 않는다.
- 기존 가구·집 구조·`Room_Presets`를 재생성하지 않는다. 설치 후 씬 NGO 해시 갱신과 0/중복 검증,
  명시적 저장.

## 4. 알려진 제약

- **FM-IMPL-4 일부 미구현.** 손목 애니메이션, 3색 실루엣의 실제 렌더링(현재는 상태값만 복제됨)이
  없다. 행동 시간 원형 게이지·중단 연출은 구현됐다(§2.6).
- 게이지 크기·색·흔들림 폭 기본값(`FurnitureDriverUiSettings_Default`)은 임시 UI 값이다. 기획서는
  위치·모양·문구·흔들림 횟수만 정했다.
- **식탁·선반 2종은 지금 당장 분해할 라이브 대상이 없다.** `House_01/PhysicsFurniture`가
  비어 있기 때문이다(맵 v0.4 일반 가구 배치 자체가 아직 안 됨) — 레벨 디자이너가 배치하면
  프리팹에 이미 붙은 `FurnitureDriverPoolItem`이 그대로 동작한다.
- MD-7(아이템 내구도 0 취급)은 "0에서 멈추고 계속 9초로 사용 가능"으로 잠정 구현했다 — 사용자
  확정이 아니므로 파손/사용 불가 규칙이 정해지면 `PlayerFurnitureDriverController.CanUseGate`에
  조건을 추가해야 한다.
- MD-8(사전 표시)은 구현하지 않았다 — 기존 크로스헤어 아웃라인(모든 가구 공통)만 있다.
- 부품 풀이 소진되면(동시에 너무 많이 분해) 분해가 조용히 실패한다(경고 로그만). 플레이어
  대상 피드백은 FM-IMPL-4 몫이다.

## 5. 검증 상태

2026-09-13 발견한 장착 연결 누락·홀더 정리 결함을 수정한 뒤 재검증했다.
**실제 Game 씬 Local Host에서 퀵슬롯 선택 → 조준 → 우클릭 → 침대 분해 완료를 확인했다.**
같은 날 기획서 1.1(우클릭 유지·중앙 원형 게이지) 구현과 부품 위치 버그 수정 후 다시 확인했다.
원격 플레이어 복제·조립·손목 애니메이션·실루엣 렌더링까지 완료했다는 뜻은 아니다.

| 검증 | 결과 |
| --- | --- |
| 컴파일 | Unity 6000.3.20f1에서 수정·테스트 컴파일 오류 0건 |
| EditMode | **235/235 통과, 건너뜀 0 (2026-09-13 1.1 구현 후 전체 실행).** 드라이버 관련은 `FurnitureAssemblyRulesTests`(9)·`FurnitureDurabilityTests`(7)·`FurnitureDriverSettingsTests`(3)·`QuickSlotTests`(12) |
| PlayMode | **32/32 통과, 건너뜀 0 (부품 위치 수정 후 재실행).** 분해 17개 + 기존 잡기·던지기 12개 + 청소 3개. 1.1에서 `우클릭을_떼면_분해를_취소하고_내구도를_유지한다`·`행동_중에는_중앙_원형_게이지가_진행도만큼_채워진다`·`분해가_중단되면_게이지가_멈춘_채_실패_문구를_띄우고_사라진다` 추가, 기존 행동 시간·이동 취소 테스트는 우클릭 유지를 주입하도록 수정 |
| 1.1 실제 Game 씬 Local Host (2026-09-13) | 가상 마우스 우클릭. **유지 완료:** 침대에 9초 행동(내구도 15로 낮춤) 동안 누르고 있어 중단 없이 완료(`cancelSerial=0`, 15→10). **뗌 취소:** 옷장(1.2m)에 1.5초 누른 시점 `Disassemble`·게이지 표시·채움 0.17·"분해 중" → 뗀 직후 행동 없음·`cancelSerial=1`·채움 0.17에서 멈춤·실패색·"분해 실패"·최대 흔들림 10.5px → 1.5초 뒤 게이지 숨김, 옷장 유지·내구도 불변. **부품 위치 수정 확인:** 새 세션에서 3초 유지로 침대 분해(3.01초, 100→95), 1.5초 뒤 매트리스·헤드·다리가 침대에서 수평 1.2m 이내·y 0.2~2.5(서로 겹쳐 생성돼 튕기는 중). 수정 전에는 풀 보관 위치에서 y≈-5270까지 낙하 중이었다 |
| 설치 | `FurnitureMultiDriverSetup.Install/Validate` 통과. Game 씬·Player 프리팹·큰 가구 프리팹 6종·부품 프리팹 15종·SO 저장 확인 |
| 실제 Game 씬 Local Host | Input System 가상 키보드·마우스로 Tab 누름 → 오른쪽 이동 → Tab 해제 → 드라이버 슬롯=1·`ServerHasDriver=true` 확인. 플레이어를 기존 침대 옆으로 이동·시점 설정한 뒤 실제 `FurnitureTargeter` 조준과 우클릭 입력으로 분해 완료: 원본 비활성·홀더 0, 매트리스/헤드/다리 각 1개 활성·내구도 100·콜라이더 켜짐·동적 Rigidbody, 아이템 내구도 100→95. 런타임 오류 0건 |
| 원격 Host/Client·Steam | **미실행.** 상대 화면 복제·원격 RPC·Steam 2PC 검증은 남음 |

PlayMode 테스트는 기존 `NetworkFurnitureFixture`의 단일 Local Host 세션을 사용한다.
가구·부품은 테스트 오브젝트이며, 장착은 컨트롤러 API 또는 실제 `QuickSlotWheelUi.Confirm`,
완료 요청은 실제 NGO RPC 래퍼를 호출한다. 행동 시간·취소 테스트는 입력 리더의 프레임 값과
행동 시작을 직접 주입한다. 실제 씬 입력 검증은 별도로 위 표에 기록했다. 사망·스킬 취소,
조립 실행, 부품 잡기·던지기 전체 흐름, 원격 플레이어 동기화는 이 테스트가 다루지 않는다.
PlayMode 테스트 어셈블리에 `GhostHunter.UI` 참조를 추가해 장착 전달 경로도 회귀 검증한다.

MCP 테스트 작업 ID(2026-09-13 1.1): EditMode `697065b6f71c4b75adc767a6125dd725`, PlayMode
`834be60db49a47f5a66af1576f09adaa`. 이전 작업 ID: EditMode `749bff44d4f64e8a966f7cf6ad346eb3`,
PlayMode `b882b841227c4a1fbf4025154a9f530b`. 가상 입력 장치를 제거하고 플레이 모드를 종료해
Bootstrap 씬으로 복귀했다.

**1.1 저장 내역:** `가구용 멀티 드라이버 행동 UI 설치` 메뉴로 Game 씬 `PrototypeUI`에
`FurnitureDriverActionHud`를 붙이고 씬을 저장했다. `FurnitureDriverUiSettings_Default.asset`을 새로
만들고 `QuickSlotItem_Driver.asset` 설명 문구를 갱신했다. 가구·부품·Player 프리팹은 다시 저장하지 않았다.
부품 위치 수정은 코드만 바뀌어 재설치가 필요 없다.

**남은 작업**: FM-IMPL-4 중 손목 애니메이션·3색 실루엣 렌더링, 조립 흐름 PlayMode 자동 테스트,
Host·Client 수동 검증(Steam 2PC 포함), MD-7·MD-8 사용자 확정, 식탁·선반 2종의 실제 레벨 배치.
부품 여러 개가 같은 점에 겹쳐 생성돼 튕기는 문제(§2.3 드롭 오프셋이 같은 부품 여러 개일 때만
분산)는 기획 판단이 필요해 손대지 않았다.

---

관련: [기획서](../project/furniture-multidriver-system.md) · [청소 구현(선례)](cleaning-system.md) ·
[roadmap.md §1.6](../project/roadmap.md) · [ADR-0009](decisions/ADR-0009-scene-placed-level-objects.md) ·
[ADR-0010](decisions/ADR-0010-server-authoritative-furniture-physics.md)

최종 갱신: 2026-09-13 (기획서 1.1 — 우클릭 유지·중앙 원형 게이지 HUD 구현, 부품이 풀 보관 위치로
되돌아가는 버그 수정. EditMode 235개·PlayMode 32개 통과, 실제 Game Local Host에서 유지 완료·뗌 취소·
부품 위치 확인)
