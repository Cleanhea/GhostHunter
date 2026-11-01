# 가구용 멀티 드라이버 구현

> **2026-09-28:** 이 문서의 `GhostHunter > …` 설치·생성·검증 메뉴와 `Editor/…Setup.cs` 도구는 [ADR-0020](decisions/ADR-0020-remove-one-off-editor-setup-tools.md)으로 삭제됐다.
> 도구 실행 절차·결과는 구현 당시 기록이다. 지금은 저장된 씬·프리팹이 원본이고 직접 고친다.

기획: [가구용 멀티 드라이버](../project/furniture-multidriver-system.md). 구현 지시:
[furniture-multidriver-implementation-prompt.md](../workflow/furniture-multidriver-implementation-prompt.md).

> **2026-10-29 기획서 1.3 — 분해는 큰 가구를 짐더미 1개로 바꾼다(§2.3·§2.8, FM-IMPL-5).** 아래 §2.3의 "부품을 풀에서 꺼내
> 활성화" 서술과 §2.4의 조립은 1.2까지의 방식이다. **분해가 부품을 만들지 않으므로 조립은 입력이 닿지 않는다** — 코드·부품 풀·조립
> 영역은 남아 있고 짐더미 기준 재정의(MD-14)를 기다린다.
>
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
| `UI/FurnitureDriverActionHud.cs` | 화면 중앙 원형 게이지 — 진행도 채움, 중단 시 멈춤·좌우 흔들림·"분해 실패/조립 실패". 조립을 시작 못 하면 이유 문구만("재료가 부족합니다" 등) |
| `Gameplay/Player/FurnitureDriverFeedback.cs` | 조립을 시작 못 한 이유(재료 부족·구성 불일치·영역 밖) — 2026-09-27 |
| `UI/FurnitureAssemblyZoneView.cs` | 조립 영역 판정 트리거를 게임 화면에 바닥 채움 + 윤곽선으로 표시, 판정 상태 색 — 2026-09-27 (§2.7) |
| `Gameplay/FurnitureDriver/FurnitureDisassemblyRecipe.cs` | 큰 가구 1종의 부품 구성(부품 ID·개수) |
| `Gameplay/FurnitureDriver/FurniturePartRequirement.cs` | 레시피 안 부품 하나(ID·개수) |
| `Gameplay/FurnitureDriver/FurnitureDriverCatalog.cs` | 레시피 8종 묶음 SO(2026-09-28 싱글 침대 2종 추가). 서버·조립 판정이 공유 |
| `Gameplay/FurnitureDriver/FurnitureDriverPoolItem.cs` | 씬 배치 오브젝트의 활성/비활성 및 공통 `FurnitureNetworkPhysics` 내구도 상속. 파손 풀 재사용 차단(2026-09-15, Unity 실행 검증 대기). **2026-10-29:** `IsBundled`·`CanBundle`·`ServerConvertToBundle`, 복구 스냅샷의 `Bundled` |
| `Gameplay/FurnitureDriver/FurnitureBundle.cs` | **짐더미 형태(2026-10-29, §2.8)** — 큰 가구 오브젝트가 모양을 바꾼다. 복제 `NetworkVariable<bool>`, 짐더미 자식 생성, 형태 전환, 서버 변환 |
| `Gameplay/FurnitureDriver/FurnitureBundleMesh.cs` | 짐더미 메시를 크기로부터 코드로 만드는 순수 함수(둥근 포대 + 끈 3줄·홈). 서브메시 0 천·1 끈 |
| `Gameplay/FurnitureDriver/FurnitureBundleSettings.cs` | 짐더미 크기 비율·최소 변·모양 비율·재질 SO(`FurnitureBundleSettings_Default`) |
| `Gameplay/Furniture/FurnitureOutline.cs` | `AddOutlineRenderer` — 코드로 만든 짐더미의 윤곽선 셸을 목록에 더한다 |
| `Gameplay/FurnitureDriver/FurnitureAssemblyZone.cs` | 조립 영역 점유 추적·순수 판정 호출·실루엣 상태 복제·조립 실행 |
| `Gameplay/FurnitureDriver/FurnitureAssemblyRules.cs` | 조립 성립 판정 순수 함수(부품 ID별 개수 → Empty/Partial/Ready/Invalid) |
| `Gameplay/FurnitureDriver/FurnitureDurability.cs` | 아이템 내구도 감소, 가구 내구도 상속·평균 순수 함수 |
| `Gameplay/Furniture/FurnitureGrabTarget.cs` | `IsPlacementReady`에 `FurnitureDriverPoolItem` 활성 여부 추가(기존 `RandomFurnitureItem` 게이트와 병렬) |
| `Gameplay/Interaction/GrabController.cs` | 드라이버 장착 중 가구 잡기 차단(대걸레와 같은 자리) |
| ~~`Editor/FurnitureMultiDriverSetup.cs`~~ | ~~설치/검증·행동 UI 설치·조립 영역 재배치 메뉴~~ — ADR-0020으로 삭제 |
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

### 2.2.1 효과음 (2026-11-01)

쓰는 사람에게만 들린다(`IAudioService.Play2D`, 큐는 `FurnitureDriverSettings`). 행동을 시작할 때 시작 시점 내구도로 사용 중 소리를 고른다 —
> 임계치(15)면 `SFX_driver_3s`, ≤ 15면 `SFX_driver_9s`(클립 길이가 행동 시간 3초·9초와 같아 완료 순간에 함께 끝난다).
완료 순간 `SFX_driver_success`. 중단(`CancelAction`, 행동 중이었을 때만)은 사용 중 소리를 0.05초에 걸쳐 멈추고 `SFX_driver_fail`.
플레이어 퇴장·스폰 해제로 인한 정리는 소리를 내지 않는다. 완료 소리는 서버 재검증 전에 소유자가 내므로 서버가 거절해도 난다.

### 2.3 분해 — `RequestDisassembleRpc` (1.3: 짐더미로 변환)

서버가 발신자·장착(`ServerHasDriver`)·생존·운반·굴착 상태·거리·대상 활성 여부를 재검증한 뒤: 카탈로그에 레시피가 있는 큰 가구이고
아직 짐더미가 아니면 `FurnitureDriverPoolItem.ServerConvertToBundle(DropHeight)` → `FurnitureBundle.ServerConvert`로
**같은 오브젝트를 짐더미 형태로 바꾸고** 아이템 내구도를 감소시킨다(§2.8). 이미 짐더미이거나 짐더미 형태가 없는 가구(`CanBundle` 거짓)는 조용히 거절한다.
`ServerConvert`는 잡고 있던 홀더·차징·투척 상태를 `FurnitureGrabTarget.ServerResetForPool`로 초기화한 뒤 형태를 바꾸고, 큰 가구가 있던 자리
**위 `DropHeight`(기본 1m)** 로 옮겨 떨어뜨린다(`NetworkTransform.Teleport` + `RoomPreset.TeleportBody`, 속도 초기화, 배치 보호 1초).

> **1.2까지의 방식(폐기):** 부품 풀에서 요구 개수만큼 비활성 `FurnitureDriverPoolItem`을 찾아 원본 위치 + `DropHeight`에서 활성화하고
> 원본을 비활성화했다. 부품 풀이 모자라면 활성화한 부품을 되돌리고 경고 로그를 남기며 실패했다. 부품 풀 프리팹·씬 풀은 조립 재정의(MD-14)가 끝날 때까지 삭제하지 않았다.

아래 두 문단은 `FurnitureDriverPoolItem.ServerActivate`(부품·조립 완성품 활성화, 지금은 조립만 호출)에 관한 기록이다.
`ServerActivate`는 이제 `FurnitureBundle.ServerSetBundled(false)`도 불러 짐더미 상태로 숨겨졌던 큰 가구를 온전한 모양으로 꺼낸다.

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

> **버그 수정(2026-09-17): 조립 영역 트리거가 지면에서 1.5m 떠 있었다.**
> MD-2에 따라 `DrillCarSafeZone_Temp`의 좌표를 복사했는데, **세이프 존은 오브젝트 위치가 상자
> '중심'** 이고(`DrillCarSafeZone.ContainsPoint`) 조립 영역 트리거는 `center (0, 1.25, 0)` ·
> `size (3, 2.5, 3)`으로 **오브젝트 위치를 '바닥'으로 가정**했다. 세이프 존이 (0.975, **1.5**, -9.74)에
> 높이 3m 상자로 놓여 있어 지면은 y=0인데, 조립 트리거는 **월드 y 1.5~4.0**에 생겼다.
> 침대 부품은 가장 높은 헤드가 0.9m(매트리스 0.25 · 다리 0.15)라 바닥에 놓으면 트리거에 닿지도
> 못한다 → `OnTriggerEnter`가 걸리지 않아 상태가 `Empty`에서 바뀌지 않고, **조립이 오류 하나 없이
> 영원히 시작되지 않았다.** 부품을 들어 트리거 높이로 올려도 `IsGrounded`가 잡힌 부품을 제외하므로
> 소용없다.
>
> 설치 도구가 세이프 존의 **바닥**(`AssemblyZoneOrigin` = 중심 − 높이/2)에 영역을 놓도록 고치고,
> `Validate()`에 트리거 바닥과 지면이 어긋나면 실패하는 검사를 넣었다(설치 도구와 그 회귀 테스트 3건은
> ADR-0020으로 삭제 — `FurnitureAssemblyZonePlacementTests` 에는 조준점 테스트 2건만 남았다).
>
> **뒤따른 수정(같은 날): 조준 기준점도 바닥으로 내려가 있었다.** 영역을 지면으로 내리자
> `PlayerFurnitureDriverController`가 조준 기준으로 쓰던 `zone.transform.position`이 발밑이 되어,
> 눈높이 1.6m 기준 **수평 1.8~2.6m에서만** 45° 원뿔 안에 들어왔다. 부품을 내려놓고 바로 옆에 선
> 자리(1.2m)에서는 내적이 0.53이라 **우클릭이 아무 반응도 하지 않는다.** `FurnitureAssemblyZone.AimPoint`
> (트리거 상자의 월드 중심, 지면 위 1.25m)를 조준 기준으로 바꿔 서 있는 어느 거리에서나 잡히게 했다.
> 이 수정은 코드만 바뀌므로 메뉴 재실행이 필요 없다.
>
> **2026-09-27 조립 조작 변경(기획서 1.2, 사용자 결정 — 영역 유지):**
> - **조준:** 크로스헤어가 **영역 안의 아무 재료(부품)나** 가리키면 조립한다
>   (`PlayerFurnitureDriverController.TryBeginAssembleOnPart`). 영역 중심 조준은 가까이 서거나 5m로 넓힌 영역
>   가장자리에서 원뿔을 벗어나 반응하지 않았다. 영역 중심 조준도 조립 가능할 때는 그대로 받는다.
> - **어느 영역인지:** `FurnitureAssemblyZone.FindContaining` 이 찾는다. 서버는 트리거 후보 집합, 클라이언트는
>   복제된 재료 위치와 트리거 경계 겹침으로 판단한다(후보 집합은 서버에만 있다).
> - **시작 못 하는 경우:** 영역이 Ready 가 아니면 게이지를 시작하지 않고 `FurnitureDriverFeedback`
>   (재료 부족·구성 불일치·영역 밖)을 `FeedbackSerial` 로 알린다(§2.6).
> - **서버 거리 검사:** 영역 중심이 아니라 **조준한 재료** 기준 `UseDistance × 1.5` 다. 재료가 그 영역 후보인지도
>   다시 본다. 완료 순간 재계산이 Ready 가 아니면 `AssembleRejectedRpc` 로 소유자에게 이유를 돌려준다
>   — 예전에는 조용히 끝났다.
> - **완성된 큰 가구는 재료로 세지 않는다**(`IsLargeFurniture`). 세면 레시피에 없는 ID라 영역이 Invalid 가 되어,
>   방금 조립한 가구가 영역 가운데 떨어진 채로는 다음 조립이 막혔다.
>
> 씬의 `AssemblyZone` 트리거는 현재 **5×5×5m**(center y 1.25)로 저장돼 있다. 설치 도구 기본값 3×2.5×3m와 다르므로
> 손으로 넓힌 것으로 보인다. 설치 도구의 바닥 정렬 검증은 이 값에서 어긋난다(바닥 y −1.25). 판정에는 문제가 없어
> 이번에 건드리지 않았다.
>
> 영역을 옮기던 `조립 영역 재배치` 메뉴는 ADR-0020으로 삭제했다. 영역을 다시 놓으려면 씬에서 직접 고친다 —
> 원점 = 세이프 존 중심 − 높이/2(지면), 트리거 center y = size.y/2. 프리팹·부품 풀·NGO 해시는 건드리지 않는다.

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
> 제외하고 표시). 씬의 `_startActive`가 꺼져 있으면 침대가 계속 숨어 있다 —
> 설치 메뉴는 ADR-0020으로 삭제됐으므로 씬 인스턴스에서 직접 켠다.
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
| `FeedbackSerial` 증가(2026-09-27) | 링을 숨기고 이유 문구만 실패색으로 같은 흔들림·표시 시간으로 — "재료가 부족합니다" / "재료 구성이 맞지 않습니다" / "조립 영역 안에 재료를 모아 주세요"(`FurnitureDriverUiSettings`) |

중단 기록은 `CancelAction()`이 **진행 중인 행동이 있었을 때만** 남긴다 — 행동 없이 부르는 장착
해제·디스폰 정리는 실패 연출을 띄우지 않는다. 완료 후 서버가 요청을 거절한 경우는 중단이 아니라서 실패 연출이
없다 — 단 조립 거절은 이유 문구(`FeedbackSerial`)로 알린다. 분해의 부품 풀 부족은 여전히 로그만 남는다. HUD는 로컬 플레이어가 바뀌면 그 시점의 일련번호를 기준으로 삼아
이전 기록을 새 중단으로 오인하지 않는다.

### 2.7 조립 영역 표시 — `FurnitureAssemblyZoneView` (2026-09-27)

Stage1·Tutorial 조립 구역은 드릴카 오른쪽(+X) 차체 옆 램프 쪽에 고정 배치한다(2026-10-04 후속 사용자 요청).
형광 표시는 `TriggerBounds`·`FloorHeight`를 따라 그리므로 씬의 구역 이동과 함께 옮겨진다.
같은 오른쪽의 드릴 쪽 반출 구역과 겹치지 않게 나눈다 → [스테이지 시스템 §2.1](../project/stage-system.md#21-드릴카-stage1-2026-09-29).

사용자 요청("조립 콜라이더 시각화")으로 판정 트리거를 게임 화면에 그린다. 기획서 §6.4의 **큰 가구 실루엣은
여전히 미구현**이고, 이것은 그 대신 **영역 자체**를 보여 준다.

| 항목 | 규칙 |
| --- | --- |
| 모양 | 트리거 월드 경계(AABB)의 윤곽선 12개(LineRenderer 6개: 바닥·윗면 사각형 + 세로 4) + 바닥 반투명 채움. 트리거는 지면 아래로도 뻗지만(씬 저장값 center y 1.25·size 5 → y −1.25~3.75) **바닥(`FloorHeight` = 영역 원점)에서 잘라** 그린다 |
| 색 | 서버가 복제한 판정 상태 — 빈 영역 옅은 흰색(α 0.35), 일부만 흰색 α 0.5(§6.4), 조립 가능 초록 `#78C664`, 혼입·초과 빨강 `#D66565`. 채움은 같은 색 × `ZoneFillAlphaScale`(0.3) |
| 렌더 | 머티리얼 에셋 없이 항상 포함 셰이더 `Sprites/Default`(GraphicsSettings 10753)로 런타임 생성 — 벽에 가려지고 뒷면도 보인다. 표시 오브젝트는 **영역 밑 자식**이라 Game 씬과 함께 사라진다 |
| 대상 | `FurnitureAssemblyZone.All`(스폰 등록부) — 전역 검색 없음. 로컬 표시 전용, 네트워크 상태 추가 없음 |
| 설정 | `FurnitureDriverUiSettings` "조립 영역 표시" — 켜기(`ShowAssemblyZone`, 기본 켬)·상태별 색·선 두께 0.04m·채움 배율 |
| Scene 뷰 | `FurnitureAssemblyZone.OnDrawGizmos` 가 트리거 전체(지면 아래 포함)를 와이어 박스로, 플레이 중이면 상태 색으로 그린다 — 설치 불필요 |
| 설치 | HUD 와 함께 `PrototypeUI` 에 붙어 있다(붙이던 `행동 UI 설치` 메뉴는 ADR-0020으로 삭제) |

검증(검증용 복제 프로젝트): PlayMode `조립_영역을_바닥부터_그리고_판정_상태에_따라_색을_바꾼다` 통과 —
표시 생성·바닥 자름(바닥 +0.02m)·윗면 높이·빈 영역 색 → 조립 가능 초록·설정으로 끄기. 같은 테스트에 카메라를 붙여
빈 영역·일부·조립 가능 세 상태를 실제로 렌더해 윤곽선·채움·색 변화를 눈으로 확인했다. 실제 Game 씬 표시는 메뉴 실행 후 확인 대기.

### 2.8 짐더미 — `FurnitureBundle` (기획서 §4.1·§6.5, 1.3)

**왜 새 오브젝트가 아닌가.** 기획은 "큰 가구가 삭제되고 짐더미 1개가 생성된다"이고 짐더미는 원본의 무게·명칭·내구도를 그대로 가진다.
별도 짐더미 오브젝트를 풀에서 꺼내면 이 세 값을 일일이 옮겨야 하고 큰 가구 하나당 하나씩 씬에 미리 둬야 한다(Stage1 큰 가구 42개).
그래서 **큰 가구 오브젝트 자체가 모양만 바꾸는** 방식을 택했다 — 새 NetworkObject 스폰이 없어 ADR-0009를 그대로 지키고,
무게(`Rigidbody.mass`)·명칭(`PoolKey`)·내구도(`FurnitureNetworkPhysics`)·`FurnitureDefinition`·`RandomFurnitureItem` 같은 식별이 **저절로 보존된다.**
플레이어 눈에는 큰 가구가 사라지고 그 자리에 짐더미가 나타난다. 나중에 조립이 짐더미를 큰 가구로 되돌릴 때도 같은 전환을 거꾸로 하면 된다.

| 항목 | 규칙 |
| --- | --- |
| 복제 | `NetworkVariable<bool> _bundled`(서버 쓰기·모두 읽기). 변경 시 모든 접속자가 형태를 적용한다. 늦게 접속해도 스폰 시점 값으로 맞춘다 |
| 짐더미 자식 | `Awake`에서 코드로 만든다 — `FurnitureBundle`은 `DefaultExecutionOrder(-100)`이라 같은 오브젝트의 `FurnitureNetworkPhysics`·`FurnitureGrabTarget`·`FurnitureHoverMotor`가 콜라이더·렌더러 목록을 캐시하기 **전에** 끝난다. 자식 = `FurnitureBundle`(MeshFilter·MeshRenderer 2슬롯·BoxCollider, 처음엔 꺼짐) + 그 밑 `FurnitureBundle_Outline`(조준 윤곽선 셸, `FurnitureOutline.AddOutlineRenderer`로 목록에 등록) |
| 형태 전환 | 큰 가구의 **직계 자식을 모두 끄고** 짐더미 자식을 켠다(파츠·윤곽선·표지가 함께 꺼진다). 원래 꺼져 있던 자식은 되돌릴 때도 꺼 둔다. 꺼진 자식의 콜라이더는 PhysX 복합 콜라이더에서 빠지고, `FurnitureClearanceProbe`·`TryGetTopCenter`는 `activeInHierarchy`를 이미 확인한다 |
| 크기 | 원본 렌더러 메시의 가구 로컬 경계 × `SizeScale`(0.4), 각 변 최소 `MinimumDimension`(0.3m). **짐더미 바닥 = 큰 가구 바닥, 가로·세로 중심 = 큰 가구 중심.** 모든 접속자가 같은 프리팹에서 같은 값을 계산하므로 복제하지 않는다. 트랜스폼 스케일은 1을 유지한다(하드 룰) |
| 메시 | `FurnitureBundleMesh.Build(size, shape)` — 에셋 파일 없이 접속자마다 **처음 짐더미가 될 때 한 번** 만든다(가구 오브젝트 하나당 메시 하나, `OnDestroy`에서 해제) |
| 무게·내구도·명칭 | 건드리지 않는다. 짐더미 충돌의 내구도 감소는 원본 가구의 `FurnitureDefinition`(무게 등급)을 그대로 쓴다 |
| 중복 분해 | 이미 짐더미(`IsBundled`)면 조준 대상에서 제외하고, 진행 중 행동도 `IsTargetStillValid`가 취소한다. 서버도 거절한다 |
| 복구 | `StageRecoverySnapshot.PoolFurnitureState.Bundled` — 호스트 이전 복구에서 짐더미 상태를 되살린다(`ServerSetBundled`). 필드가 없던 옛 스냅샷은 false로 읽힌다 |
| 회수 | `ServerActivate`(큰 가구를 풀에서 다시 꺼냄)가 `ServerSetBundled(false)`를 불러 온전한 모양으로 돌려놓는다. 부활 의식 방 비우기가 짐더미를 `ServerDeactivate`해도 같다 |

**메시 모양 — `FurnitureBundleMesh`.** 가장 긴 변을 정규 x, 가장 짧은 변을 y, 나머지를 z로 옮겨 만든 뒤 원래 축으로 되돌린다
(세로로 선 옷장은 세로로 선 짐더미).

- **천:** 모서리가 크게 둥근 상자(반지름 = 가장 짧은 반변 × 0.6). 상자 겉면 격자점마다 `안쪽 평평한 상자로 clamp → 방향 → +반지름`으로 둥근 표면에 사영한다.
  눈금은 모서리 호는 각도가 고르게(45°를 4등분, `r·tanθ`), 평평한 구간은 6cm 간격으로 나누고 끈 자리 눈금을 끼운다. 이음매는 격자 좌표로 용접한다.
- **끈:** 긴 변을 삼등분하는 자리 2줄(긴 변에 수직인 면) + 짧은 변 가운데 1줄(짧은 변에 수직인 면). 천과 **같은 표면 함수**를 지나가므로 홈에 정확히 앉고,
  가로 3줄(가장자리·가운데·가장자리)이라 홈의 굴곡을 따라 휜다. 끈 자리는 천을 코사인 곡선으로 안쪽 눌러 홈을 판다(깊이 = 짧은 변 × 0.06, 끈 폭 = 짧은 변 × 0.12, 끈 폭 × 1.5가 홈 반폭, 끈 두께 = 짧은 변 × 0.04 — 끈 가장자리 자리의 천 높이를 넘으면 잘라 경계 밖으로 솟지 않게 한다).
- **서브메시:** 0 = 천(`M_FurnitureBundle_Fabric`), 1 = 끈(`M_FurnitureBundle_Strap`). 법선은 `RecalculateNormals`(홈 음영 반영).
- **감는 방향:** 삼각형마다 꼭짓점 법선 평균과 비교해 바깥을 향하게 맞춘다 — 축 치환의 홀짝을 따로 계산하지 않는다.

## 3. 에셋과 설치

- **짐더미(2026-10-29, 사용자 요청 — 메시를 직접 생성):** 분해 가능한 큰 가구 프리팹 8종(위 6종 + 싱글 침대 2종)에 `FurnitureBundle` 컴포넌트를
  텍스트로 추가하고 `_settings`를 `Assets/Settings/Gameplay/FurnitureBundleSettings_Default.asset`에 연결했다(루트 `m_Component`에 항목 1개 + `MonoBehaviour` 블록 1개 —
  프리팹 `GlobalObjectIdHash`는 그대로). 씬 인스턴스는 프리팹을 따르므로 씬 파일은 건드리지 않았다(`_poolKey`가 씬에 덮어쓰이지 않음을 확인).
  재질 `Assets/Materials/M_FurnitureBundle_Fabric.mat`(회갈색 천)·`M_FurnitureBundle_Strap.mat`(어두운 끈)은 `Map_Furniture_Fabric.mat`을 복제해 색만 바꿨다.
  윤곽선은 기존 `Furniture_Outline.mat`을 쓴다. **부품 프리팹·부품 풀은 삭제하지 않았다**(조립 재정의 MD-14 전까지 남김 — 분해는 더 이상 쓰지 않는다).
- 큰 가구 프리팹 6종(`Assets/Prefabs/Furniture/{DoubleBed_1.6x2.0, Wardrobe_1.2x0.6,
  Wardrobe_1.5x0.6, DiningTable_1.55x0.85, Shelving_2.6x0.55, Shelving_1.65x0.45}.prefab`)에
  `FurnitureDriverPoolItem`을 **제자리 편집**(`LoadPrefabContents` → 추가 → 같은 경로에
  `SaveAsPrefabAsset`)으로 추가했다 — 재굽기가 아니라 `GlobalObjectIdHash`가 바뀌지 않는다.
- 부품 프리팹 15종을 `Assets/Prefabs/FurnitureDriverParts/`에 새로 생성했다(그레이박스 사각
  메시, 기존 가구와 동일한 `FurnitureGrabTarget`·`FurnitureNetworkPhysics`·`FurnitureLauncher`·
  `FurnitureHoverMotor`·`FurnitureOutline`·`FurnitureDriverPoolItem` 구성).
- **싱글 침대 2종 추가(2026-09-28, 사용자 요청).** `SingleBed_1.0x2.0`·`SingleBed_1.1x2.0` 프리팹에
  `FurnitureDriverPoolItem`(`_startActive` 0)을 텍스트로 붙였다. 부품은 **크기별 전용**이다 —
  `SingleMattress_1.0/1.1`·`SingleBedHead_1.0/1.1`·`SingleBedLeg_1.0/1.1`(6종, 더블 부품을 폭 비율로 줄인
  메시 `FurnitureDriverPartMeshes/Single*Mesh.asset`). 부품을 공유하면 조립 판정이 목록의 첫 레시피로만
  조립하므로 나눴다. 레시피 `FurnitureDisassemblyRecipe_SingleBed_*.asset` 2개를 카탈로그에 추가했다.
  새 부품 프리팹의 `GlobalObjectIdHash` 는 에셋 식(`GlobalObjectId_V1-1-<프리팹 GUID>-<NetworkObject fileID>-0`)으로
  넣고 `m_InScenePlaced` 는 0이다(기존 15종은 씬 기준 값·1로 박혀 있다 — unity-assets.md §5.2의 프리팹 함정).
  ProtoTypeGame 의 싱글 침대 6개에는 기존처럼 보이도록 `_startActive: 1` 을 덮어썼다.
- `Game/FurnitureMultiDriverPrototype/Parts`: 부품 풀 23개(6개 레시피 × 부품 개수 × 세트 수).
  세트 수는 2026-09-12 씬 스캔 기준 라이브 인스턴스 개수(Furniture_Library·비교용 집 제외,
  최소 1세트 보장)다 — 더블 침대 1, 옷장 2종 각 1, 식탁·선반 2종은 라이브 인스턴스가 없어
  세트 1개만 미리 마련해 뒀다(레벨 배치 전까지 실제 분해 대상 없음).
- **Stage1 부품 풀(2026-09-28): 방에 놓인 큰 가구 1개당 1세트.** 더블 침대 6 · 싱글 1.0 6 · 싱글 1.1 6 ·
  옷장 1.2 12 · 옷장 1.5 6 · 식탁 2 · 선반 2.6 3 · 선반 1.65 1개에 맞춰 부품 137개(기존 23 + 114)를
  `FurnitureMultiDriverPrototype/Parts/<부품 ID>` 아래 y=-50에 둔다. 큰 가구를 더 놓으면 세트도 늘려야 동시 분해가 된다.
- **방에 놓인 큰 가구는 인스턴스에서 `_startActive: 1` 을 MUST 덮어쓴다.** 프리팹 기본값 0이면 세션 시작 때
  서버가 숨겨 버린다. Stage1 의 기존 21개가 이 값 없이 저장돼 있어 함께 고쳤다(2026-09-28).
- `Game/FurnitureMultiDriverPrototype/AssemblyZone`: 기존 `DrillCarSafeZone_Temp` 자리를 쓰는
  트리거 1개(MD-2, 임시). **좌표를 그대로 복사하지 않고 세이프 존 상자의 바닥에 맞춘다**(§2.4 버그 수정).
- `FurnitureDriverSettings_Default.asset`, `FurnitureDriverCatalog_Default.asset`,
  `FurnitureDriverRecipes/*.asset`(6개), `QuickSlotItem_Driver.asset`,
  `FurnitureDriverUiSettings_Default.asset`: `Assets/Settings/Gameplay`.
- `Game/PrototypeUI`: `FurnitureDriverActionHud`(→ `FurnitureDriverUiSettings_Default`). 전체 설치
  메뉴도 붙이지만, 이미 설치된 씬에는 `가구용 멀티 드라이버 행동 UI 설치`만 실행하면 된다 — 이 메뉴는
  HUD·드라이버 안내 문구만 갱신하고 프리팹·부품 풀을 다시 저장하지 않는다.
- 기존 가구·집 구조·`Room_Presets`를 재생성하지 않는다. 설치 후 씬 NGO 해시 갱신과 0/중복 검증,
  명시적 저장.

## 4. 알려진 제약

- **(2026-10-29, 1.3) 조립은 입력이 닿지 않는다.** 분해가 부품을 만들지 않으므로 조립 영역에 올릴 재료가 없다. 짐더미를 큰 가구로 되돌리는 규칙은 **MD-14 TBD**다.
  그때까지 분해한 가구는 되돌릴 수 없다(전환은 되돌릴 수 있게 짜 두었다 — §2.8 회수).
- **짐더미 외형은 게임 화면에서 확인하지 않았다.** 메시는 EditMode로 크기·경계·감는 방향·부피를 검증했고 렌더 결과를 이미지로 확인했지만([기획서 §6.5](../project/furniture-multidriver-system.md)),
  실제 가구 위에서의 모습·조명·윤곽선 셸 모양과 Host/Client 복제는 실기 미확인이다. 크기 비율(0.4배·최소 0.3m)은 **임시값**이다(MD-15).
- **드릴카 적재(반출) 판정은 짐더미를 세지 않을 수 있다.** 짐더미는 일반 가구처럼 잡고 옮길 수 있지만, 반출 완료 판정(`FurnitureDeliveryTracker`)은
  `RandomFurnitureItem` 목표 가구만 센다. 분해한 침대·옷장의 반출 집계는 MD-3(MG-22) 대기.

- **조립은 아직 한 번도 끝까지 돌아간 적이 없다.** §2.4의 뜬 트리거 때문에 그동안 불가능했고,
  수정은 코드에만 있다 — 씬에 반영(재배치 메뉴)한 뒤 실기로 확인해야 "된다"고 말할 수 있다.
  Ready 상태는 2026-09-27부터 **영역 표시 색**(초록)으로 볼 수 있다(§2.7 — 행동 UI 설치 메뉴 실행 후). 큰 가구 실루엣은 여전히 없다.
- **FM-IMPL-4 일부 미구현.** 손목 애니메이션, 3색 실루엣의 실제 렌더링(현재는 상태값만 복제됨)이
  없다. 행동 시간 원형 게이지·중단 연출은 구현됐다(§2.6).
- 게이지 크기·색·흔들림 폭 기본값(`FurnitureDriverUiSettings_Default`)은 임시 UI 값이다. 기획서는
  위치·모양·문구·흔들림 횟수만 정했다.
- **식탁·선반·싱글 침대는 실기 분해를 확인하지 않았다.** ProtoTypeGame 에는 식탁·선반 라이브 대상이 없고,
  Stage1 에는 있지만(식탁 2·선반 4) Stage1 에서의 분해·조립은 아직 실행해 보지 않았다.
- MD-7(아이템 내구도 0 취급)은 "0에서 멈추고 계속 9초로 사용 가능"으로 잠정 구현했다 — 사용자
  확정이 아니므로 파손/사용 불가 규칙이 정해지면 `PlayerFurnitureDriverController.CanUseGate`에
  조건을 추가해야 한다.
- MD-8(사전 표시)은 구현하지 않았다 — 기존 크로스헤어 아웃라인(모든 가구 공통)만 있다.
- 부품 풀이 소진되면(동시에 너무 많이 분해) 분해가 조용히 실패한다(경고 로그만). 플레이어
  대상 피드백은 FM-IMPL-4 몫이다.

## 5. 검증 상태

**2026-10-29 (기획서 1.3, 짐더미):** 검증용 복제 프로젝트에서 컴파일 오류 0건. EditMode **541건 중 539 통과**(실패 2건은 기존 `MoleSkillWiringTests` 소스 검사 —
이번 변경과 무관). 전체 PlayMode **116/116 통과**. 새 `FurnitureBundleMeshTests`(크기·경계, 서브메시·인덱스, NaN·법선, 닫힌 껍질 부피 부호, 끈이 경계를 넘지 않음, 크기 계산, 결정성; 4가지 크기)가 모두 통과.
PlayMode `FurnitureDisassemblyFlowTests` **31/31 통과** — 분해가 무게·명칭·내구도를 유지한 짐더미 하나로 바꾸고 부품을 만들지 않음, 파츠가 자식에 있는 가구의 형태 전환
(파츠 끄고 짐더미 켬·크기 0.8m·바닥 정렬), 짐더미 형태가 없는 가구는 분해 거절, 이미 짐더미면 행동 취소, 잡고 있던 홀더 해제·재활성화 시 온전한 모양 복귀.
1.2까지의 "부품 풀 부족이면 되돌림" 테스트는 부품이 없어져 위 두 테스트로 바꿨다. 조립 테스트는 영향 없음.
**실제 Game/Stage1 씬 Host·Client 실기와 Steam 2PC는 미수행.**

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

테스트 작업 ID(2026-09-13 1.1): EditMode `697065b6f71c4b75adc767a6125dd725`, PlayMode
`834be60db49a47f5a66af1576f09adaa`. 이전 작업 ID: EditMode `749bff44d4f64e8a966f7cf6ad346eb3`,
PlayMode `b882b841227c4a1fbf4025154a9f530b`. 가상 입력 장치를 제거하고 플레이 모드를 종료해
Bootstrap 씬으로 복귀했다.

**1.1 저장 내역:** `가구용 멀티 드라이버 행동 UI 설치` 메뉴로 Game 씬 `PrototypeUI`에
`FurnitureDriverActionHud`를 붙이고 씬을 저장했다. `FurnitureDriverUiSettings_Default.asset`을 새로
만들고 `QuickSlotItem_Driver.asset` 설명 문구를 갱신했다. 가구·부품·Player 프리팹은 다시 저장하지 않았다.
부품 위치 수정은 코드만 바뀌어 재설치가 필요 없다.

**남은 작업**: **조립 영역 재배치 메뉴 실행(씬 반영)과 조립 실기 확인**, FM-IMPL-4 중 손목
애니메이션·3색 실루엣 렌더링, 조립 흐름 PlayMode 자동 테스트,
Host·Client 수동 검증(Steam 2PC 포함), MD-7·MD-8 사용자 확정, 식탁·선반 2종의 실제 레벨 배치.
부품 여러 개가 같은 점에 겹쳐 생성돼 튕기는 문제(§2.3 드롭 오프셋이 같은 부품 여러 개일 때만
분산)는 기획 판단이 필요해 손대지 않았다.

---

관련: [기획서](../project/furniture-multidriver-system.md) · [청소 구현(선례)](cleaning-system.md) ·
[roadmap.md §1.6](../project/roadmap.md) · [ADR-0009](decisions/ADR-0009-scene-placed-level-objects.md) ·
[ADR-0010](decisions/ADR-0010-server-authoritative-furniture-physics.md)

최종 갱신: 2026-10-29 (기획서 1.3 — 분해 = 짐더미 1개: `FurnitureBundle`·`FurnitureBundleMesh`·`FurnitureBundleSettings` 추가, §2.3 재작성, §2.8 신설, 큰 가구 프리팹 8종 배선.
EditMode 541건 중 539 통과(기존 실패 2건), PlayMode 116/116(`FurnitureDisassemblyFlowTests` 31). 조립 재정의 MD-14 대기.)

이전 갱신: 2026-09-28 (설치·재배치 메뉴와 설치 수치 회귀 테스트 3건 삭제 — ADR-0020. 이전: 2026-09-27 조립 영역 표시 `FurnitureAssemblyZoneView`·Scene 뷰 기즈모 추가 — §2.7, 행동 UI 설치 메뉴로 설치. 같은 날: 기획서 1.2 — 영역 안 아무 재료나 조준해 조립, 조립 불가 이유 문구("재료가 부족합니다" 등),
서버 거리 검사를 조준 재료 기준으로, 완성 가구를 재료에서 제외. 검증용 복제 프로젝트 PlayMode `FurnitureDisassemblyFlowTests`
28/28(새 6건), EditMode 새 1건 통과. **실제 Game 씬에서의 조립 실기 확인은 여전히 남아 있다.**)

이전 갱신: 2026-09-17 (조립 영역 트리거가 지면에서 1.5m 떠 있어 조립이 시작될 수 없던 문제를
설치 도구에서 수정 — §2.4. 재배치 전용 메뉴·설치 검증·EditMode 회귀 테스트 3건 추가, EditMode
305/305 통과. **씬 반영과 조립 실기 확인은 남아 있다.**)

이전 갱신: 2026-09-13 (기획서 1.1 — 우클릭 유지·중앙 원형 게이지 HUD 구현, 부품이 풀 보관 위치로
되돌아가는 버그 수정. EditMode 235개·PlayMode 32개 통과, 실제 Game Local Host에서 유지 완료·뗌 취소·
부품 위치 확인)
