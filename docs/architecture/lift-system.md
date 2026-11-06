# 리프트 구현

> 상태: **코드·프리팹·Tutorial/Stage1 배치 구현 (2026-11-06).** 규칙의 권위는 [리프트 시스템 기획서](../project/lift-system.md).
> 원문에 없는 수치·선택은 전부 `[TEMP]`다. 실제 화면 배치·조작감·2인 접속은 아직 확인하지 않았다 → §7.

## 1. 구성

| 대상 | 경로 | 역할 |
| --- | --- | --- |
| `LiftController` | `Assets/Scripts/Gameplay/Lift/LiftController.cs` | 프리팹 루트. 서버가 버튼 입력을 받아 승강 발판을 정지 포인트로 움직이고, 막힘을 판정한다. 복제: 높이 `NetworkVariable<float>`·이동 중 `NetworkVariable<bool>` |
| `LiftStopRules` | `…/Lift/LiftStopRules.cs` | **순수 계산** — 입력 방향의 가장 가까운 정지 포인트. EditMode `LiftStopRulesTests` |
| `LiftMotion` | `…/Lift/LiftMotion.cs` | **순수 계산** — 한 프레임 이동량(가속·감속·정확한 도착). EditMode `LiftMotionTests` |
| `LiftSettings` | `…/Lift/LiftSettings.cs`, `Assets/Settings/Gameplay/LiftSettings_Default.asset`(3m)·`LiftSettings_Tutorial.asset`(3.57m) | 층 높이·정지 포인트 수·속도·버튼 거리·막힘 여유 |
| `LiftButton` | `…/Lift/LiftButton.cs` | 버튼 한 개(상승/하강). 조준 대상·안내 문구·눌림 연출. 상태는 바꾸지 않는다 |
| `LiftPlatform` | `…/Lift/LiftPlatform.cs` | 발판 표지 — 플레이어가 발밑에서 찾는다 |
| `PlatformCarry` | `Assets/Scripts/Gameplay/Player/PlatformCarry.cs` | `PlayerMotor`가 쓰는 internal 클래스 — 발판 위 플레이어를 발판 변위만큼 옮긴다 |
| 입력 | `PlayerInteractor`(`Interaction/`) | 조준선 끝 `LiftButton`을 찾고 **우클릭**(`Player/UseDriver`) 누른 프레임에 `Press()`. 안내 문구는 `OnGUI` |
| 본체 취급 | `FurnitureDefinition` 3개 필드 + `FurnitureGrabTarget`·`FurnitureLauncher` | 홀더 1명 제한·수평 발사·속도 배율 → §5 |
| 가구 정의 | `Assets/Settings/Gameplay/FurnitureDefinition_Lift.asset` | 질량 150·Heavy·홀더 1·수평 발사·배율 0.5·충돌 피해 0 |
| 프리팹 | `Assets/Prefabs/Map/Lift.prefab` | `DefaultNetworkPrefabs`에 등록 |
| 메시 | `Assets/Settings/Gameplay/LiftMeshes/` (`LiftBox_*`·`LiftWheel_*`·`LiftRail_*`) | 스케일 1 규칙 때문에 치수마다 메시를 둔다(Stage1 `S1Box_*`와 같은 방식) |
| 머티리얼 | `Assets/Materials/M_Lift_*.mat` 6종 | 프레임·본체·발판·바퀴·상승 버튼·하강 버튼 |

## 2. 권위와 복제

| 데이터 | 권위 | 복제 |
| --- | --- | --- |
| 발판 높이 | **서버** | `NetworkVariable<float>` Everyone 읽기·Server 쓰기. 이동 중 매 프레임 갱신(NGO가 틱마다 보냄). 클라이언트는 `SmoothDamp`(0.08초)로 따라간다 |
| 이동 중 여부 | 서버 | `NetworkVariable<bool>`. 버튼 안내 문구("이동 중")가 읽는다 |
| 본체 위치 | 서버(물리) | `NetworkTransform` — 가구와 같다. 클라이언트의 본체 Rigidbody는 키네마틱 껍데기(`FurnitureNetworkPhysics`) |
| 버튼 입력 | 클라이언트 요청 → 서버 검증 | `RequestPressRpc(direction)` — `SendTo.Server`, `InvokePermission = Everyone`(리프트에 소유자가 없다). 서버가 **플레이어 오브젝트 존재·생존·같은 방향 버튼까지 거리(≤3.5m, 눈높이 기준)** 를 다시 검사한다 |
| 눌림 연출 | 서버 통지 | `NotifyPressedRpc(direction, accepted)` — `SendTo.ClientsAndHost`, 모든 피어의 해당 방향 버튼이 0.2초 눌린다 |
| 발판 위 플레이어 이동 | **소유자** | 새 상태 없음 — ADR-0008 이동 권위 예외의 연장. 소유자가 발판 변위를 자기 `CharacterController.Move`에 더한다 |

늦게 접속한 클라이언트는 `OnNetworkSpawn`에서 복제된 높이로 **스냅**한다(올라가는 연출부터 보지 않는다).
호스트 이전 복구 스냅샷(`StageRecoverySnapshot`)에는 리프트 상태가 없다 — 새 호스트에서는 맨 아래로 돌아간다 `[TEMP]`.

## 3. 프리팹 구조 (`Lift.prefab`)

```
Lift                      [Furniture 레이어] NetworkObject · Rigidbody(동적, 회전 고정, 질량 150) · NetworkTransform ·
│                         FurnitureNetworkPhysics · FurnitureGrabTarget · FurnitureHoverMotor · FurnitureLauncher ·
│                         FurnitureOutline · NetworkSoundEmitter · FurnitureImpactSound · LiftController
├─ Chassis                BoxCollider 3.8×0.16×2.2 + 메시, 자식 Chassis_Outline(윤곽선 셸)
├─ Wheel_FL/FR/BL/BR      메시만(충돌체 없음) — ④의 바퀴
├─ Post_NW/NE             기둥 BoxCollider 0.12×5×0.12. 로컬 +Z 쪽에만 둔다(§4.2 "문 쪽을 비운다")
├─ BasePanel              버튼판 ③ BoxCollider, 자식 BasePanel_Outline·BaseButton_Up/Down(+ _Face)
└─ Platform               [Default 레이어] Rigidbody(키네마틱·보간) · LiftPlatform
   ├─ Deck                BoxCollider 3.0×0.08×2.0 + 메시 — 막힘 판정 기준(`_deck`)
   └─ DeckPanel           버튼판 ② BoxCollider, 자식 DeckButton_Up/Down(+ _Face)
```

- **한 종류 = 한 프리팹.** 맵마다 달라지는 것은 위치·회전과 `LiftController._settings`(층 높이) 참조뿐이다. Tutorial 인스턴스가 `LiftSettings_Tutorial`을 가리킨다
  (`GhostPrototypeSettings_Tutorial`과 같은 방식). 프리팹의 기둥(5m)은 3.57m 이동 + 가구를 든 높이까지 넉넉하다.
- **발판(`Platform`)은 Default 레이어**다 — 조준해도 던지기 대상이 아니다. 하부 본체(Furniture 레이어)만 좌클릭으로 민다.
- 본체 윤곽선 셸은 `Chassis`·`BasePanel`에만 있다(조준 표시용).
- 충돌음 컴포넌트는 모든 가구 프리팹이 같은 구성을 가져야 해서(`AudioWiringTests`) 일반 가구와 같은 프로파일을 연결했다 — 리프트 전용 소리는 아직 없다.
- 프리팹 `GlobalObjectIdHash`는 에셋 식(`GlobalObjectId_V1-1-<GUID>-<NetworkObject fileID>-0`의 XXH32)으로 계산해 넣었다. 씬 인스턴스는
  `GlobalObjectId_V1-2-<씬 GUID>-<프리팹 NetworkObject fileID>-<PrefabInstance fileID>` 식이다 — 기존 Tutorial 인스턴스 해시를 같은 식으로 재현해 확인했다.

## 4. 승강 발판 구동

### 4.1 발판은 실행 때 본체에서 떼어낸다

발판은 **키네마틱 Rigidbody**라 `MovePosition`으로 움직여야 PhysX가 속도를 알고 위에 얹힌 짐을 같이 싣고 간다. 트랜스폼만 옮기면 순간이동으로 처리되어 짐이 남는다.
그런데 동적 본체의 자식으로 두면 본체가 밀릴 때 계층이 발판 트랜스폼을 끌고 가서 `MovePosition`과 싸운다. 그래서:

1. 프리팹·씬에서는 `Platform`이 본체의 자식이다(에디터에서 보고 편집하기 쉽다).
2. `LiftController.Awake`가 본체 콜라이더와 발판 콜라이더 사이의 충돌을 끄고(`Physics.IgnoreCollision` — 발판이 기둥·섀시를 밀어 본체가 튀지 않게), 발판을 `SetParent(null)`로 뗀다.
3. `FixedUpdate`가 **본체 자세 + 높이**로 발판 목표를 계산해 `MovePosition`/`MoveRotation`한다(본체 회전은 고정이라 위쪽 = 본체 +Y).
4. 본체가 파괴되면(`OnDestroy`) 떼어낸 발판도 지운다.

### 4.2 서버 틱

`FixedUpdate`(고정 간격 0.02s)에서 서버가 `LiftMotion.Step`으로 높이를 진행하고, 같은 `FixedUpdate`에서 `MovePosition`한다. 클라이언트도 복제된 높이를 같은
`FixedUpdate`에서 `SmoothDamp`(고정 간격)한다. 발판 Rigidbody는 보간(`Interpolate`)이라 화면 위치는 스텝 사이를 매끈하게 잇는다.

> **2026-11-06 수정(올라갈 때 달달달 떨림):** 처음에는 높이를 `Update`(프레임 시간)에서 진행하고 `FixedUpdate`가 그 값을 읽어 `MovePosition`했다. 스텝마다 읽는
> 증가량이 프레임 길이만큼 들쭉날쭉해(60fps 에서 21/40/27mm) 보간된 발판이 일정하게 올라가지 않았고, 탑승자 문제(§6)와 겹쳤다. 고정 간격으로 같은 양씩 올린다.
> 시도했다 버린 것: 발판을 `Update`에서 트랜스폼으로 직접 옮기기 — 탑승자는 매끈했지만 키네마틱 몸체가 순간이동이라 짐이 오를 때 2~4cm 파고들고
> 내릴 때 14cm 튀었다. `MovePosition` 을 유지한다.

### 4.3 막힘 판정 (LF-4·5)

매 프레임 이동 직전에 **발판 판 모양**(`Deck` 박스, 옆면만 0.04m 줄임)을 이동 방향으로 `BoxCastNonAlloc`으로 훑는다.
기준 자세는 물리 몸체가 아니라 **논리 자세**(높이로 계산한 위치)다 — 몸체는 물리 스텝만큼 늦게 따라오기 때문이다.

| 맞은 것 | 처리 |
| --- | --- |
| 본체·발판 자신의 콜라이더 | 무시 |
| 플레이어·귀신 레이어 | 마스크에서 제외(발판에 같이 타고 있다) |
| 동적 강체이면서 발판에서 0.06m 이내 | **얹힌 짐** — 막힘이 아니다. 같이 올라간다 |
| 그 밖의 충돌체(벽·천장·정적 지형·떨어져 있는 가구) | 막힘 — 닿기 `BlockProbeSkin`(0.04m) 앞에서 멈추고 **이동을 끝낸다** |

막혀서 선 높이는 정지 포인트가 아니다. 다음 입력은 `LiftStopRules.TryFindTarget`가 그 방향의 가장 가까운 정지 포인트를 고른다(제자리에서 한 층 더 가지 않는다).
발판 위 짐이 천장에 끼는 경우는 판정하지 않는다(기획서 LF-Q4).

## 5. 본체 밀기 — 가구 던지기 파이프라인 재사용

리프트 본체는 `FurnitureGrabTarget`·`FurnitureLauncher`·`FurnitureHoverMotor`를 그대로 쓰는 **가구 하나**다. 일반 가구와 다른 점은 `FurnitureDefinition`의 새 필드뿐이다.

| 필드 | 기본 | 리프트 | 효과 |
| --- | --- | --- | --- |
| `_maxHolders` | 2 | **1** | `FurnitureGrabTarget.MaxHoldersAllowed`가 `HasFreeSlot`·`CanGrab`·`ServerTryAddHolder`에 쓰인다 — 둘이 잡아 `Held`(부양)로 올릴 수 없다. 두 번째 사람은 조준 불가(회색) |
| `_horizontalLaunchOnly` | 꺼짐 | **켜짐** | 발사 방향을 조준의 수평 성분으로 눕힌다(위 성분 없음, 20~70° 보정 없음). 토크를 주지 않는다 |
| `_launchSpeedScale` | 1 | **0.5** | 발사 속도 배율. 최대 차지: 30 × 0.5(Heavy 1인) × 1.0 × 0.5 = **7.5m/s**(실측 약 3.8m 미끄러짐), 살짝 눌러 1.1m/s(약 0.2m) — PlayMode `LiftPrefabFlowTests` 실측 |

본체 Rigidbody는 **회전 고정**(`FreezeRotation`, 뒤집히지 않고 돌지도 않는다)·선형 감쇠 1.2다. `Launched` 상태는 일반 가구와 같이 속도가 0.5m/s 아래로 줄거나 2초가 지나면 풀린다.
충돌 피해는 본체 자신에게 0이다(`_maximumCollisionDamage: 0`) — 부딪힌 가구는 평소처럼 깎인다.

## 6. 플레이어 탑승 (`PlatformCarry`)

`CharacterController`는 발밑 키네마틱 바디를 따라가지 않는다. `PlayerMotor.TickMovement`가 `PlatformCarry.Move`를 부르면, 땅에 서 있을 때(`isGrounded`)
발밑 0.6m 광선으로 `LiftPlatform`을 찾아 발판의 **물리 자세**(`Rigidbody.position`, 콜라이더가 실제로 있는 곳)가 이전 프레임 대비 움직인 만큼 **먼저 별도 `Move`** 로 옮기고,
그다음 입력·중력 이동을 `Move`한다. 1.5m를 넘는 한 프레임 변위는 순간이동으로 보고 무시한다. 발판을 바꿔 타거나 떠나면 기준점을 새로 잡는다.
회전은 따라가지 않는다(본체 회전이 고정이라 필요 없다).

**눈(카메라)은 보간된 발판을 따라간다.** 발은 물리 스텝(50Hz)마다 뛰는 콜라이더 위에 일관되게 서므로 스텝(최대 30mm)씩 움직인다. 이를 상쇄하려고
`PlatformCarry.VisualOffset`(= 발판 화면 위치 − 물리 자세)을 `PlayerMotor.ApplyPosture`가 카메라 피벗 로컬 위치에 얹는다. 자세 높이 상태(`_networkCameraHeight`,
`CameraLocalHeight`)에는 섞지 않는다(`_appliedPlatformOffset`을 빼고 보간한 뒤 다시 얹는다).

> **2026-11-06 수정(올라갈 때 달달달):** 처음에는 발판의 보간된 `transform.position` 변위를 접지 중력과 **같은 `Move`** 에 합쳤다. 접지 중력(`-2m/s` 이상)이
> 프레임당 상승분보다 커서 순변위가 아래쪽이었고, 발이 올라가는 콜라이더를 파고들다 `CharacterController` 스킨 폭(0.05m)을 넘는 순간에만 한꺼번에 튀어 올랐다
> (144fps 에서 0 0 0 0 62mm 0 0 0…, 60fps 에서 0 75 0 50 0 0 75…). 내려갈 때도 발이 계단식으로 콜라이더에 얹혔다. 변위를 물리 자세 기준 별도 `Move`로 먼저 하고
> 시각만 보정하는 지금 방식으로 바꿨다 — 보이는 높이의 프레임 변위 편차 0%, `isGrounded` 풀림 0프레임. 회귀: PlayMode `LiftRideSmoothnessTests`.

## 7. 검증 상태

> **2026-11-06 구현 직후 — 자동 검증은 통과했고, 아래 "미검증"(화면·조작감·2인)이 남아 있다.**
> 검증은 에디터가 열려 있어 검증용 복제 프로젝트(`tools/run-tests.ps1`, batchmode)에서 했다.

| 검증 | 결과 |
| --- | --- |
| C# 컴파일 | 오류 0 |
| **전체 EditMode** | **659/659 통과** — 신규 `LiftStopRulesTests`·`LiftMotionTests`·`LiftWiringTests`(프리팹·가구 정의·설정·씬 배치·해시)와 `FurnitureLaunchDirectionTests` 수평 방향 5건 포함. 처음 한 번 `AudioWiringTests`가 리프트 프리팹의 충돌음 컴포넌트 누락으로 실패해 프리팹에 추가해 고쳤다 |
| **전체 PlayMode** | **204/204 통과** — 신규 `LiftFlowTests` 11건(승강·하강·이동 중 무시·끝 방향 무시·막힘 후 가장 가까운 정지 포인트·막혀 선 뒤 하강·얹힌 짐 동반 상승·먼 플레이어 거절·가까운 요청 수락·홀더 1명 제한·수평 발사)과 실제 `Lift.prefab`을 스폰하는 `LiftPrefabFlowTests` 3건(3m 승강·본체 밀기·두 번째 홀더 거절) |
| 씬 배치 정적 검사(batchmode 에서 Tutorial·Stage1 을 열어 실제 충돌체로 질의) | NetworkObject 해시 0·중복 0, 배치 위치의 충돌체 겹침 없음(지면에 닿은 것 제외), 이동 경로(바닥→맨 위) 막힘 없음, Tutorial 발코니 → 발판 자리·Stage1 방 → 개구부 캡슐 통로 막힘 없음, 개구부 문턱 y 3.00 = 2층 바닥 |
| 본체 밀기 실측(`LiftPrefabFlowTests`) | 살짝 눌러 **0.21m**, 최대 차지 **3.81m** 미끄러짐. 높이 변화 없음·회전 0.5° 미만·발판이 본체를 따라옴 |

**미검증 (사람이 에디터에서 확인해야 한다):**

1. 화면에서 리프트·임시 난간·외벽 개구부가 어떻게 보이는가 (아트 없음, 박스 모양).
2. 우클릭으로 버튼을 누르고, 발판이 오르내리는 체감·소리 없음·플레이어가 실제로 같이 올라가는지.
3. 발판에 가구를 실어 올리고 내릴 때 미끄러지거나 떨어지는지(특히 하강, 본체를 민 직후).
4. 본체를 좌클릭으로 미는 거리·세기, 2인이 동시에 잡으려 할 때 거절되는지.
5. 클라이언트(2번째 PC)에서 발판·본체 보간과 호스트 이전 후 상태(맨 아래로 복귀).
6. 귀신 NavMesh가 리프트를 포함해 구워졌을 때의 길찾기 — Tutorial·Stage1 귀신은 아직 확인하지 않았다.

## 8. 씬 배치 기록 (2026-11-06, 씬 텍스트 직접 수정)

씬 `NetworkObject`를 텍스트로 넣었으므로 `m_InScenePlaced`·`GlobalObjectIdHash`·`InScenePlacedSourceGlobalObjectIdHash` 오버라이드를 같이 썼다(`conventions/unity-assets.md §5.2`).
**에디터에 Tutorial/Stage1이 저장하지 않은 채 열려 있으면 에디터 저장이 이 수정을 덮어쓴다 — 닫고 다시 열어 확인한다.**

| 씬 | 인스턴스 | 위치 | 설정 | 맵 수정 |
| --- | --- | --- | --- | --- |
| Tutorial | `Lift_Balcony` | (−5.125, 0.03, 2.75), 회전 0 | `LiftSettings_Tutorial`(3.57m) | `Balcony_Railing` 비활성(`TutorialMap` 프리팹 인스턴스 오버라이드 `m_IsActive`), `Lift_BalconyRailPatch` 추가(`Rail_Left`·`Rail_Right`·`Rail_Post_A/B`, 충돌체는 한 덩어리씩) |
| Stage1 | `Lift_Front` | (61.67, 0, −13.22), Y 180° | `LiftSettings_Default`(3m) | `House_Prototype_PlanB/Floor_02/Structure/Outer_South`를 3조각(`Outer_South`·`_Right`·`_Lintel`)으로, `FamilyRoom/Wall_South`를 3조각(`Wall_South`·`_Right`·`_Lintel`)으로 나눠 x −7~−5(집 기준) 폭 2.0m·높이 2.5m 개구부, 개구부 안 `Window_South_Left_*` 5개 비활성. 새 메시 `S1Box_*` 6개 |

배치 위치는 실제 콜라이더 질의(Unity batchmode)로 골랐다. Tutorial은 발코니 앞 기둥 `Column_03`(x −3.73~−3.22, z 1.10~1.63)과 모텔 바닥 슬래브(y 0.03)를 피해 z 2.75, y 0.03에 둔다.
Stage1은 1층 외벽 `Outer_South_A`와 앞마당 바닥(`FrontYard_TEMP`, 윗면 y 0)에 맞췄고, 본체 문 쪽 가장자리를 비우려고 기둥을 한쪽에만 둔 이유가 같다(건물 벽에 기둥이 닿았다).

---

최종 갱신: 2026-11-06 (신규 — 리프트 구현·Tutorial/Stage1 배치)
