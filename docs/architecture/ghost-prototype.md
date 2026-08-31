# 귀신 프로토타입 구현

> 상태: **팀 평균 정신력 기반 5상태 + 강제 진정, 어택 판정, 원뿔 시야·소리 탐지, 추격→마지막
> 위치→7초 수색 AI, 잡힘→사망 구현. 집 내부 활동 경계 제한과 F1 HUD 연결.
> 초자연현상 선정 루프(§6.4)와 6종 현상 구현. 목격 시 정신력 −15 연결 완료(G-6).
> 일반 은신처 임시 구현 — 방마다 상자 하나, 최초 접근 시 1회 30% 검사(G-8 판정 시점만).**
> 게임 규칙의 권위는 [귀신 시스템 기획서](../project/ghost-system.md)이고, 이 문서는 코드 구조와
> 현재 연결 범위, 검증 기록을 설명한다. `[임시]` 수치는 `GhostPrototypeSettings` 에셋에 있다.

이전까지 저장소에는 귀신 코드가 없었다(문서만 유령 P1을 서술). 이번 구현은 처음부터
[귀신 시스템 기획서 1.0](../project/ghost-system.md)을 기준으로 작성했고, 상태 전이는 **기존
정신력 시스템**([sanity-system.md](sanity-system.md))의 팀 평균 판정값을 읽어 판정한다
(roadmap `M8-GS-1b`).

## 1. 구현 범위

```text
Game/SanitySystem/SanityTeamService  (기존)
  └─ ISanityTeamService.TryGetTeamAverage → 반올림 팀 평균 (귀신은 자체 정신력 없음)
  └─ ISanityTeamService.CopyPlayerStates  → 생존·접속 플레이어의 SanityNetworkState[]
          ↓ 서버가 매 프레임 읽는다
Ghost_Prototype/GhostPrototypeController  (서버 동적 스폰 NetworkObject)
  ├─ GhostStateMachine        평상시/활동/경고/어택/자연 진정 + 강제 진정, 10초 어택 판정(§7.3 확률표)
  ├─ GhostPhenomenaDirector   평상시·활동에서만 주기마다 §6.4 선정 — 활성 Pool에서 직전 현상 제외 랜덤
  ├─ 탐지 (어택 중만)         원뿔 시야 15m/120° + 근거리 3m + 이동 소리(달리기>걷기>웅크림)
  │                          은신처(HidingSpot) 안이면 시야·소리·잡힘 전부 제외
  ├─ 추격·수색 AI             배회 → 추격 → 마지막 확인 위치 → 7초 수색(은신처 최초 접근 시 30%) → 배회
  ├─ 이동                     CharacterController 직접 조향(collide-and-slide), NavMesh 없음, 집 내부 X/Z 경계 강제
  ├─ 공격                     잡힘 → SanityNetworkState.ServerMarkDead(), 어택은 계속(§11.2)
  ├─ 문                       추격·수색 중 앞을 막은 닫힌 방문을 DoorInteractable.ServerForceOpen (§9.4)
  ├─ 현상 실행 (서버 권위)    물건 흔들기·떨어뜨리기(가구 Rigidbody 임펄스, ADR-0010) / 문 여닫기(ServerForceToggle)
  ├─ PlayPhenomenonRpc        (kind, 위치, 시드) → 전 피어가 GhostPhenomenaPlayer 로 연출 재생(§10)
  └─ NetworkVariable<GhostPhase>  현재 상태만 복제 → 각 클라이언트가 연출 재생
          ↓ IGhostDebug
Game/GhostPrototypeSystem/GhostPrototypeSpawner  (Game 씬 서비스, Host 전용)
  └─ F1 HUD 귀신 섹션: 스폰/제거, 어택 강제, 강제 진정, 활동 강제, 청소 진행도 스텁, 상태 요약
```

포함:

- 팀 평균 정신력 **80 이하 → 활동**, **60 이하 → 어택 판정 실행**, 30 이하 고위험(§5.2·§5.3)
- 10초 주기 어택 판정 + §7.3 정신력별 확률표(20/40/60/80%), 판정 성공 시 경고
- 경고 **5초** → 어택 **30~90초**(30초 전 종료 불가 / 70 이상이면 조기 종료 / 90초 강제 종료)
- 자연 진정 **30초** 후 조건 재검사 → 활동 또는 평상시(§4.7)
- 강제 진정 **10초** — 별개 시스템, 어느 상태에서든 걸리고 진행 중인 어택을 즉시 끊는다(§4.8)
- 특수 어택 Trigger(§7.4) — F1 `어택 강제`. 평상시·활동에서 즉시 경고로. 이미 어택 중이면 무시(G-2)
- 청소 진행도 40% 최초 도달 → 활동 전환 + 방해 증가 구간 90초(§6.2). **일회성 트리거로 취급(G-1)**
- 어택 상태에서만 켜지는 원뿔 시야 탐지(각도·거리 + 물리 가림 검사)와 근거리 감지
- 이동 소리 탐지 — 서버가 플레이어 위치 변화로 추정한 속력으로 달리기/걷기/웅크림을 구분
- 배회 → 추격 → 마지막 확인 위치 이동 → **7초 수색** → 재탐지 시 추격, 실패 시 배회(§9.1)
- `GhostPrototypeSpawner`의 집 내부 상자를 서버 활동 경계로 사용 — 이동 좌표를 X/Z 안으로 보정하고,
  집 밖 플레이어는 시야·소리·잡힘 대상에서 제외(§3.1·§11.1)
- 잡힘 판정 → 기존 정신력 시스템의 `ServerMarkDead()` 호출, 어택 유지, 잡기 쿨타임
- **드릴 카 세이프 존(임시, §11.1)** — 정식 드릴 카 없이 현관 앞에 상자 하나(`DrillCarSafeZone`).
  그 안의 플레이어는 서버의 탐지·잡힘 판정에서 제외된다(`TryDetectPlayer`/`TryCatch` 가
  `DrillCarSafeZone.Contains` 로 건너뜀). 다른 플레이어 어택·귀신 상태·타이머는 그대로.
  `NetworkObject` 아님 — 씬 고정 기하라 서버만 질의한다. 정식 드릴 카가 생기면 삭제
- 상태별 연출: 본체는 경고·어택에만 노출(§3.3), 경고 점광원 맥동(심장 박동 시각 버전),
  어택 빨간 점광원 + 지면에 눕힌 빨간 부채꼴 시야 표시(§8.2)
- **초자연현상(§6) — 평상시·활동에서만** 주기마다 §6.4 6단계 선정. 8종 enum 중 6종 활성:
  물건 흔들기 · 작은 물건 떨어뜨리기 · 문 열고 닫기 · 서랍 열기 · 조명 깜빡임/끄기 · 귀신 일시 출현.
  물리 간섭은 서버가 처리한다(ADR-0010): **흔들기**는 **귀신 반경(`PhenomenonRadius`) 안의 Idle
  가구를 전부** `ShakeDuration` 동안 동시에 흔든다(§6.5 #9 지진 효과) — 가구마다 거의 수직인 축을
  잡고 **사인파 각속도**(진폭 `ShakeTorque`, 9Hz)로 좌우로 크게 비틀고, 0.05s마다 좌우로 번갈아
  미는 힘(`ShakeShoveSpeed`)으로 바닥에서 달그락거리게 한다. 왕복이라 제자리를 지키고 질량과
  무관하다. **떨어뜨리기**는 반경 안 **작은 소품**(Light 등급 + 렌더러 바운즈 최대 변 ≤
  `SmallPropMaxSize` 0.45m — Cup·Book_Stack·CosmeticBox 등)을 전부 `DropSpeed`(2.5m/s)로 위로
  튕겨 올린다(질량 무관). **문**은 반경 안 가장 가까운 문(`DoorInteractable.ServerForceToggle`).
  서랍·조명·환영은
  `PlayPhenomenonRpc` 로 전 피어가 로컬 재생. 발생 주기·반경·세기는 `GhostPrototypeSettings` 에
  `[TBD] G-13` 으로 노출.
- F1 HUD 귀신 섹션 — `§12.1` 디버그 형식(Ghost State / Team Sanity / Attack Check / Cleaning Progress)
  + `Phenomena` 줄(최근 현상·다음까지 초) + `현상 랜덤`/8종 기능별 시험 버튼 + `본체 보이기` 토글
- **초자연현상 목격 → 정신력 연결(§6.3, G-6 해결 — 사용자 확정 2026-08-31)** — 현상이 발생할 때마다
  `GhostPrototypeController.ServerCheckPhenomenonWitnessed` 가 모든 생존·비굴착 플레이어에 대해
  발생 지점을 볼 수 있었는지(거리 `PhenomenonWitnessDistance` 12m · 각도 `PhenomenonWitnessAngle`
  70° · 가림 검사, 서버가 카메라 피치를 모르므로 수평만 판정 — `SanityWitnessProp` 과 같은 방식)
  확인하고, 목격한 플레이어마다 `SanityNetworkState.ServerApplyGhostEventWitnessed()`(−15,
  [sanity-system.md §4.2](../project/sanity-system.md))를 호출한다. **현상 종류를 가리지 않는다** —
  발생만 하면 목격 여부만 본다. 물리적으로 대상을 못 찾은 경우(예: 반경 안에 문이 없음)도 판정한다.
- **일반 은신처 임시 구현(§9.5, G-8 판정 시점만 임시 확정 — 사용자 확정 2026-08-31)** — 정식
  가구(옷장·침대 밑·책상 밑)가 아직 없어(docs/todo/TODO-미정.md) 종류를 구분하지 않고, `HidingSpot`
  상자 하나를 방마다 둔다(침실1·침실2·거실·창고, 방 바닥 크기에서 역산해 벽 쪽 구석에 배치).
  이 안의 플레이어는 `TryDetectPlayer`/`TryCatch` 양쪽에서 정상 탐지·잡힘 대상이 아니다(드릴 카
  세이프 존과 같은 제외 패턴). 대신 수색(`Pursuit.Search`) 중 은신처 반경(`HidingSpotCheckRadius`
  2.5m) 안에 처음 들어오면 그 자리에서 딱 1회, `HidingSpotCheckChance`(30% [임시]) 확률로 안을
  들여다본다 — 성공하면 `ServerMarkDead()`. 실패해도 이번 수색(7초) 동안은 같은 은신처를 다시
  보지 않는다. **G-8의 주기·재검사 여부는 여전히 미정** — "최초 접근 시 1회"만 임시로 확정했다.
  `HidingSpots_Temp`(씬 고정, `NetworkObject` 아님)를 `GhostHunter > 귀신 프로토타입 설치`가
  방 바닥 앵커(`Bedroom_01_A_Floor` 등)에서 위치를 역산해 설치한다.

아직 포함하지 않음:

- **드릴 카 자체**(§11.1) — 미구현. 세이프 존만 현관 앞 상자로 임시 구현했다(위 "포함" 참고).
  전원 집 밖 → 어택 타이머 계속, 재진입 시 현재 어택 재적용 같은 §11.1 세부는 아직 없다.
- **벽·문 두드리는 소리 · 발소리**(§6.5 #7·#8) — enum·`GhostPhenomenonKind` 에는 있으나
  `GhostPrototypeSettings.SoundPhenomenaEnabled = false` 로 선정 Pool에서 빠져 있다(오디오 에셋 대기).
- **정리된 소품 위치 변경(§6.5 #3) · 가전제품 작동(#6) · 방 전체 지진(#9)** — 8종 목록(§4.4) 밖이라
  이번 범위에 없다.
- 심장 박동·추격 사운드 등 오디오 연출 — 오디오 에셋 대기(정신력 시스템과 동일).
- 복수 귀신(§3.1·§11.4, G-10), 저주받은 오브젝트 특수 어택(§7.4), 귀신별 특수 능력·탐지(§8.4).
- PlayMode 통합 테스트(roadmap M8-GS-7)와 Steam 2PC 검증(M8-GS-8).

## 2. 네트워크 권위

물리·상태의 권위는 서버(호스트)에만 있다. 귀신은 아무도 소유하지 않는 서버 스폰 오브젝트라
`IsServer` 로만 분기하고 `IsOwner` 는 쓰지 않는다.

| 데이터·행동 | 권위 | 복제·노출 |
| --- | --- | --- |
| 귀신 상태(`GhostPhase`) | Server | `NetworkVariable<GhostPhase>` / Everyone Read |
| 귀신 위치·회전 | Server | `NetworkTransform`(서버 권위, 보간) — 위치 + Y 회전 |
| 상태 전이·어택 판정·타이머 | Server 로컬(`GhostStateMachine`) | 복제하지 않음. 결과인 `GhostPhase` 만 복제 |
| 팀 평균 정신력 | 각 Peer의 복제 상태로 계산 | 서버가 `ISanityTeamService.TryGetTeamAverage` 로 읽음 |
| 플레이어 위치·생존 | Server(정신력 시스템) | 서버가 `CopyPlayerStates` 로 `SanityNetworkState[]` 를 읽어 탐지·추격에 사용 |
| 플레이어 이동 속력 추정 | Server 로컬 | 위치 변화 / dt 를 평활. 복제하지 않음(이동 모드는 소유자 로컬 값이라) |
| 잡힘 → 사망 | Server | `SanityNetworkState.ServerMarkDead()` (정신력 시스템이 복제) |
| 문 열기 | Server | `DoorInteractable.ServerForceOpen()` → 기존 `_isOpen` NetworkVariable |
| 본체·점광원·빨간 시야 표시 | 각 Client 로컬 | 복제된 `GhostPhase` 를 보고 재생. 상태 판정과 분리(§10) |
| 초자연현상 연출 | 각 Peer 로컬 | 서버가 `PlayPhenomenonRpc(kind,pos,seed)` 를 전송, 각자 재생(§10) |
| 드릴 카 세이프 존(임시) | 판정은 Server | `DrillCarSafeZone` 는 씬 고정 상자. 서버가 `Contains(playerPos)` 로 탐지·잡힘에서 제외. 복제 없음 |
| 본체 강제 표시(디버그) | Server(F1) | `NetworkVariable<bool> _debugForceVisible` / Everyone Read — 켜면 상태 무관 본체 렌더, §3.3 정책 자체는 불변 |
| F1 귀신 조작 | **Host 전용** | 클라이언트용 RPC 없음. `IGhostDebug.CanControl` 이 `IsServer` 를 검사 |

클라이언트가 귀신 상태를 바꾸는 RPC는 없다. F1 HUD의 귀신 조작은 정신력 시스템의 F1 조작과
같은 방식으로 **Local Host 에서만** 동작한다 — 그래서 원격 클라이언트가 귀신을 찾지 못하던
문제(roadmap M8-GS-6)는 이 구조에서 발생하지 않는다.

## 3. 상태 기계 (`GhostStateMachine`)

NGO·씬에 의존하지 않는 순수 로직이다. `SanityState` 와 같은 방식으로 EditMode 로 검증한다.
어택 판정 난수는 `Func<double>` 로 주입해 테스트에서 결정적으로 만든다.

| 입력(매 틱) | 출처 |
| --- | --- |
| `deltaTime` | `Time.deltaTime` |
| `teamSanity` | `ISanityTeamService.TryGetTeamAverage` 의 반올림 판정값. 생존자 0명이면 100으로 취급 |
| `hasLivingPlayers` | `TryGetTeamAverage` 의 `livingPlayerCount > 0` |
| `cleaningProgress` | F1 디버그 스텁(0~100). 40% 최초 도달만 의미 있다 |

| 외부 트리거 | 효과 |
| --- | --- |
| `SetForceActive(bool)` | 정신력·청소와 무관한 활동 조건 하나. F1 `활동 강제` |
| `ForceSpecialAttack()` | 평상시·활동에서 즉시 경고로(§7.4). 그 외 상태면 무시(G-2) |
| `ForceSuppression()` | 어느 상태에서든 강제 진정으로(§4.8) |

전이 규칙은 기획서 §4.2 흐름을 그대로 옮겼다. `EnterPhase(Active)` 는 다음 어택 판정까지를
`AttackRollInterval`(10초)로 초기화한다 — 활동 진입 직후가 아니라 10초 뒤 첫 Roll이다(G-14).

## 4. 탐지와 추격 (`GhostPrototypeController`, 어택 중만)

- **원뿔 시야** — `GhostVision.IsInsideCone` 의 순수 각도·거리 판정 + `Physics.Linecast` 가림 검사.
  거리 `VisionDistance`(15m [TBD] G-3), 각도 `VisionAngle`(120° [TBD] G-3).
- **근거리 감지** — `NearDetectRadius`(3m). 기획서에 없는 P1 보조값이라 0으로 끄면 사라진다.
- **이동 소리** — 서버가 추정한 속력이 `RunSpeedThreshold`(6m/s) 이상이면
  `RunHearingRadius`(12m), `WalkSpeedThreshold` 이상이면 `WalkHearingRadius`(6m), 그 미만이면
  소리 없음. 웅크리기는 속력과 무관하게 명시적으로 제외한다. 걷기 반경 6m는 확정,
  달리기 반경·어택 외 적용 여부는 `[TBD]` (G-4). 소리 탐지는 시야각·가림을 무시한다.
- **추격·수색** — `Roam`/`Chase`/`Search` 내부 FSM. 놓치면 `Search` 로 전환해 마지막 확인
  위치로 이동하고 `SearchDuration`(7초) 수색한다. 재탐지 시 `Chase`, 실패 시 `Roam`.
- **잡힘** — `Chase` 중 평면 거리가 `CatchRadius` 이내면 대상의 `ServerMarkDead()` 호출,
  `CatchCooldown` 동안 재잡힘 없음. 대상은 잃고 `Search` 로 — 어택은 계속된다(§11.2).
- **문** — 재경로 주기(`RepathInterval`)마다 목표 지점으로 Linecast. 닫힌 `DoorInteractable` 가
  `DoorOpenRange` 안이면 `ServerForceOpen()`.
- **배회 범위** — `GhostPrototypeSpawner` 가 스폰 직후 `ServerConfigureRoam(center, size)` 로
  집 내부 상자를 넘겨준다. 이 상자는 배회 목적지뿐 아니라 모든 귀신 이동의 X/Z 경계다. 귀신은
  경계 안에서만 무작위 지점을 뽑아 배회하며, 집 밖 플레이어는 시야·소리·잡힘 판정에서 제외한다.

이동은 **NavMesh 없이** `CharacterController.Move` 직접 조향이다(overview.md §5). 박스형 프로토타입
집에서는 충분하고, 정식 길찾기는 이후 과제다. 귀신 프리팹 전체는 `GhostPrototype` 레이어다.

귀신은 유령이라 **`Physics.IgnoreLayerCollision` 으로 `GhostPrototype` ↔ `Player`·`Furniture` 충돌을
끈다**(`IgnoreLevelActorCollisions()`, 스폰 시 idempotent). 플레이어·가구는 귀신을 그대로
통과하고, 귀신은 `Default`(벽·바닥)와는 계속 충돌해 이동이 정상적으로 미끄러진다. 물건 흔들기가
귀신 캡슐에 걸려 튀던 문제도 이걸로 사라진다.
스폰 뒤 Scene 뷰에는 파란 Gizmo 표식이 표시되므로, 본체가 평상시에 렌더링되지 않아도 위치를
확인할 수 있다. 이 레이어를 카메라 culling mask에 추가·변경하지 않으며 본체의 상태별 노출 정책도
그대로다. 배회·시야·문 감지 레이캐스트는 `GameLayers.NonGhostPrototypeRaycastMask`로 귀신 자신을
제외한다.

## 5. 정신력 연동

정신력 수치의 출처는 정신력 시스템 하나뿐이다. 귀신은 `NetworkVariable` 에 정신력을 갖지 않는다.

- **읽기** — 상태 전이·어택 판정은 `ISanityTeamService.TryGetTeamAverage` 의 반올림 팀 평균.
- **탐지 대상** — `ISanityTeamService.CopyPlayerStates` 로 생존·접속 플레이어의
  `SanityNetworkState` 를 받아 `.transform` 을 위치로 쓴다(`SanityWitnessProp` 과 같은 경로).
- **쓰기** — 잡힘 판정과 초자연현상 목격 판정 둘뿐. 잡히면 `SanityNetworkState.ServerMarkDead()`,
  현상을 목격하면 `ServerApplyGhostEventWitnessed()`(§4 참고)를 호출한다. 사망 처리·팀 평균 제외·
  `-%` 표시·감소량 자체는 정신력 시스템이 한다.
- **활동도(0~100) 개념은 두지 않는다**(G-5). 기획서는 상태 + 정신력 구간만 쓰므로 HUD·SO에
  활동도 필드를 만들지 않았다.

## 6. 에셋과 배선

| 대상 | 경로·오브젝트 |
| --- | --- |
| 런타임 코드 | `Assets/Scripts/Gameplay/Ghost/` (`GhostHunter.Gameplay.Ghost`), 현상은 `Interaction/GhostDrawer.cs`, 세이프 존은 `Ghost/DrillCarSafeZone.cs`, 은신처는 `Ghost/HidingSpot.cs` |
| 설정 | `Assets/Settings/Gameplay/GhostPrototypeSettings_Default.asset` |
| 귀신 프리팹 | `Assets/Prefabs/Ghost/Ghost_Prototype.prefab` (NetworkObject·NetworkTransform·CharacterController·GhostPrototypeController·**GhostPhenomenaPlayer**·GhostPrototypeSceneMarker + Body/StateLight/VisionCone) |
| 네트워크 프리팹 등록 | `Assets/DefaultNetworkPrefabs.asset` — 동적 스폰이라 MUST 등록 |
| Game 씬 서비스 | `Game/GhostPrototypeSystem` 의 `GhostPrototypeSpawner` + 자식 `GhostSpawnPoint` |
| 현상 대상(씬 배선) | `House_01/RoomLights/*` 에 `GhostAmbientLight`, 서랍장류 가구(Dresser/Nightstand/BedsideTable)에 자식 `GhostDrawer_Face` + `GhostDrawer`. 설치 도구가 idempotent 하게 붙인다(테스트베드와 같은 범위) |
| 세이프 존(씬) | `Game/DrillCarSafeZone_Temp` — 현관문(`House_01/…/FrontDoor_1.5m`) 남쪽 상자 `(4.5×3×5)`. 설치 도구가 문 위치에서 역산해 배치 |
| 은신처(씬) | `Game/HidingSpots_Temp/HidingSpot_{침실1,침실2,거실,창고}` — 방 바닥 앵커(`Bedroom_01_A_Floor` 등) 위치·크기에서 벽 쪽 구석을 역산해 배치, 상자 `1.2×1.4×1.2` |
| 서비스 등록 | `GameInstaller` → `IGhostDebug` |
| 소비자(HUD) | `Bootstrap/NetworkRig/ConnectionHud` — F1, 귀신 섹션 |
| 머티리얼 | `Assets/Materials/M_GhostBody.mat`(환영 재사용), `M_GhostVisionCone.mat`, `M_GhostDrawer.mat` |

`GhostHunter > 귀신 프로토타입 설치` 메뉴(`GhostPrototypeSetup`)가 설정·프리팹·씬 서비스·인스톨러
배선을 반복 설치·검증한다. 프리팹은 `SaveAsPrefabAsset` 후 `ForceUpdate` 재임포트로
`GlobalObjectIdHash` 를 에셋 기준으로 다시 잡는다(CLAUDE.md §5 / unity-assets.md §5.2).
전체 `GhostHunter > 프로토타입 게임 생성` 마지막에도 이 설치가 실행되므로 Game 씬을 다시 구워도
귀신 배선이 사라지지 않는다(정신력 시스템과 동일).

## 7. F1 HUD

```text
Ghost State      : ACTIVE
Team Sanity      : 57  (생존 2)
Attack Check     : 4.2 sec
Cleaning Progress: 43%
Phenomena        : DRAWEROPEN  (다음 6.1s)   ← 평상시·활동 중에만
Pursuit          : CHASE                     ← 어택 중에만
```

HUD 는 접이식 섹션(`▶/▼`)이다 — `연결·세션` / `정신력` / `귀신 프로토타입`, 그 안에
`초자연현상` 하위 섹션. Steam·세션 요약 두 줄과 마지막 상태 줄만 항상 보인다.

버튼(Host 전용, `귀신` 섹션): `귀신 스폰`/`귀신 제거` · `내 위치에 스폰`(Host 로컬 플레이어
현재 위치에 바로 스폰, 배회 경계 밖이면 안으로 보정) · `본체 보이기`(§3.3 무시하고 본체 렌더
강제 표시 — 디버그, `NetworkVariable<bool>` 라 게스트도 같이 보인다) · `활동 강제` · `어택 강제` ·
`강제 진정` · `청소 +10%` · `청소 리셋`.

**초자연현상 하위 섹션** — `랜덤`(직전 제외 Pool) 하나와 8종 기능별 버튼(`1 흔들기` … `8 출현`).
상태·주기와 무관하게 즉시 실행한다. 6·7번(두드림·발소리)은 오디오 대기라 무음이고, 물리·문
현상이 귀신 반경(6m) 안에 대상을 못 찾으면 콘솔에 알린다.

## 8. 기획서 §12.3 대비 현황

기획서 §12.3 "P1 현재 구현" 표는 이전 구현 기준이다. 이번 구현은 다음과 같이 기획서 값으로 맞췄다.

| 항목 | 기획서 | 이번 구현 |
| --- | --- | --- |
| 상태 전이 | 정신력·청소·특수 조건 | **정신력 팀 평균 + 청소 40% + F1 강제** (시간 순환 아님) |
| 경고 지속 | 5초 | **5초** |
| 어택 지속 | 30~90초 + 조기 종료 | **30~90초, 70 이상 조기 종료, 90초 강제 종료** |
| 진정 | 자연 30초 / 강제 10초(별개) | **자연 30초 / 강제 10초 별개 시스템** |
| 어택 발동 | 10초 주기 확률 + 특수 Trigger | **10초 주기 §7.3 확률표 + F1 어택 강제** |
| 수색 | 7초 | **7초** |
| 시야 | 거리·각도 TBD | **15m / 120° (SO, [TBD] G-3)** |
| 근거리 감지 | 기획서에 없음 | **3m (SO, 0으로 끌 수 있음)** |
| 소리 탐지 | 달리기 > 걷기 > 웅크리기 | **속력 추정으로 달리기 12m / 걷기 6m / 웅크리기 무음.** `RunSpeedThreshold` 6m/s로 걷기 5·달리기 7m/s를 구분한다. 걷기 반경만 확정, 나머지는 G-4 잔여 |
| 시야 표시 | 어택 중 빨간 원뿔 | **어택 중 지면 부채꼴 빨간 표시** |
| 배회 | 집 내부 배회 | **집 내부 상자 X/Z를 넘지 않는 무작위 배회·추격·수색** |
| 문 | 일반 방문을 연다 | **추격·수색 중 앞을 막은 닫힌 방문을 연다** |
| 초자연현상 | 9종 Pool, 직전 현상 제외 | **§6.4 선정 루프 + 8종 enum 중 6종 활성** (소리 2종은 SO로 끔). 물리·문 현상은 전부 귀신 반경(`PhenomenonRadius` 6m) 기준. 흔들기는 반경 안 가구 전체 동시(§6.5 #9). **목격 시 정신력 −15 연결 완료(G-6)** — 종류 불문, 거리 12m·각도 70°·가림으로 판정 |
| 활동도(0~100) | 기획서에 개념 없음 | **제거 (G-5)** |
| 사망 | 잡히면 사망, 어택 유지 | **`ServerMarkDead()` + 어택 유지** |
| 은신처 / 드릴 카 | 4종 + 30% / 세이프 존 | 은신처 **1종 임시 구현(G-8 판정 시점만)** — 방마다 상자 하나(`HidingSpot`), 최초 접근 시 1회 30% 검사. 드릴 카 세이프 존 **임시 구현** — 현관 앞 상자(`DrillCarSafeZone`), 그 안이면 탐지·잡힘 제외 |

## 9. 검증 기록

2026-08-30 · Unity 6000.3.20f1 Editor + MCP:

- 강제 재컴파일 오류 0건
- EditMode **93/93 통과** — 신규: 상태 기계 24건, 시야 기하 7건, `Ghost_Prototype` 프리팹 배선 3건
- PlayMode **12/12 통과** — 기존 네트워크 던지기 회귀 없음
- `GhostHunter > 귀신 프로토타입 설치` 실행 → 설정·프리팹·씬 서비스·인스톨러 배선 후
  `ValidateInstallation()` 통과
- `Ghost_Prototype` `GlobalObjectIdHash` 2372020120 (0 아님, `m_InScenePlaced` false),
  Player 해시와 중복 없음, `DefaultNetworkPrefabs.asset` 에 등록됨
- Game 씬에 `GhostPrototypeSystem/GhostPrototypeSpawner` + `GhostSpawnPoint`, `GameInstaller._ghostSpawner` 배선 확인

2026-08-31 · 초자연현상 추가 · Unity 6000.3.20f1 Editor + MCP:

- 재컴파일 오류 0건
- EditMode **115/115 통과** — 신규: `GhostPhenomenaDirector` 11건(주기·상태 게이트·직전 제외·소리 Pool 토글·강제 실행)
- PlayMode **12/12 통과** — 회귀 없음
- `GhostHunter > 귀신 프로토타입 설치` 재실행 → `ValidateInstallation()` 통과.
  프리팹에 `GhostPhenomenaPlayer` + 환영 머티리얼 배선, `House_01`·`House_01_OriginalScale_Right`
  RoomLights 12개에 `GhostAmbientLight`, 서랍장류 10개에 `GhostDrawer_Face` + `GhostDrawer` 부착 확인

2026-08-31 · 초자연현상 목격 → 정신력 연결(G-6) · Unity 6000.3.20f1 Editor + MCP:

- 재컴파일 오류 0건
- EditMode **115/115 통과** — `SanitySystemSettings.GhostEventDecrease` 10→15 변경으로 깨진
  기존 2건(`SanityStateTests`)을 새 값·경계 검증 방식으로 수정, 신규 없이 총 건수는 그대로
- `SanitySystemSettings_Default.asset`·`GhostPrototypeSettings_Default.asset` 을
  `manage_scriptable_object` 로 갱신(감소량 15, 목격 판정 거리 12m·각도 70°·눈높이 1.5m) —
  YAML 손편집 없이 반영 확인

2026-08-31 · 일반 은신처 임시 구현(G-8 판정 시점) · Unity 6000.3.20f1 Editor + MCP:

- 재컴파일 오류 0건
- EditMode **118/118 통과** — 신규: `HidingSpotTests` 3건(상자 판정·회전 로컬축 포함).
  정적 레지스트리(`OnEnable`/`OnDisable`)는 Play Mode 에서만 도는 생명주기라 EditMode 검증 대상이
  아니다(`DrillCarSafeZone`·`GhostAmbientLight` 와 동일 — 기존에도 이 부분은 테스트가 없었다)
- PlayMode **12/12 통과** — 회귀 없음
- `GhostHunter > 귀신 프로토타입 설치` 재실행 → `HidingSpots_Temp` 에 침실1·침실2·거실·창고 4개
  생성 확인, `ValidateInstallation()` 통과. 좌표: 침실1 `(-8.6, 0.6, 3.22)` · 침실2
  `(4.72, 0.6, 3.22)` · 거실 `(-5.375, 0.6, -3.23)` · 창고 `(6.82, 0.6, -3.23)` — 전부 House_01
  내부, 스케일 1
- **알려진 한계**: 귀신의 배회·탐지 경계(`GhostPrototypeSpawner._roamSize`)가 실제 집 전체를
  덮지 못하는 기존 문제(2026-08-31 코드 리뷰 기록)가 있어, 침실1·침실2 은신처는 그 경계
  가장자리에 겨우 걸린다. 거실·창고 은신처는 경계 안쪽이라 영향이 없다. 배회 경계 자체는 이
  작업의 범위 밖이다

2026-08-31 · 걷기 소리 탐지 반경 6m 확정(G-4 부분 해결) · Unity 6000.3.20f1 Editor + MCP:

- `RunSpeedThreshold` 4.5→6m/s: 걷기 5m/s는 6m, 달리기 7m/s는 12m, 웅크리기는 명시적 무음
- 재컴파일 오류 0건, EditMode **119/119 통과**
- 달리기 반경과 어택 외 상태 적용 여부는 G-4 잔여

아직 하지 않은 검증(수동/후속):

- **은신처 실사용** — 어택 중 은신처에 들어가면 시야·소리로 안 잡히는지, 수색 중 귀신이
  근처에 왔을 때 30% 확률로 잡히는지, 실패 후 같은 수색 동안 다시 확인 안 하는지
- **목격 판정 실사용** — F1 `현상 강제`로 현상을 켠 채 시야 안/밖·가림 뒤에서 정신력이
  실제로 15씩 줄고 몸을 돌리거나 벽 뒤로 숨으면 줄지 않는지

- Local Host 플레이에서 F1 스폰 → 정신력을 80/60/30 아래로 낮췄을 때 활동/경고/어택 전이,
  10초 판정 카운트다운, 조기·강제 종료, 강제 진정
- 어택 중 원뿔 시야에 들어갔을 때 추격, 벗어났을 때 7초 수색, 잡힘 시 `-%` 전환
- 빨간 부채꼴 시야 표시가 어택에만 켜지는지, 본체가 경고·어택에만 보이는지
- **F1 `현상 강제` 로 6종 현상이 눈에 보이는지** — 흔들림·떨어짐(서버 물리), 문 토글, 서랍 슬라이드,
  조명 점멸, 반투명 환영 페이드. 발생 주기·반경·세기 튜닝
- Steam 2PC 동기화 (roadmap M8-GS-8) — 현상 RPC 포함

---

관련: [귀신 시스템 기획서](../project/ghost-system.md) · [정신력 시스템 구현](sanity-system.md) ·
[networking.md](networking.md) · [overview.md](overview.md) · [testing.md](../workflow/testing.md) ·
[roadmap.md](../project/roadmap.md)

최종 갱신: 2026-08-31 (걷기 소리 탐지 반경 6m 확정, G-4 부분 해결. 이전: 일반 은신처 임시 구현 — 방마다 상자 하나, 최초 접근 시 1회 30% 검사(G-8 판정 시점만). 초자연현상 목격 → 정신력 −15 연결(G-6 해결), 선정 루프 + 6종 현상 구현. 드릴 카 세이프 존 임시 구현(현관 앞 상자))
