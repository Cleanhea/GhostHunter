# 귀신 공통 시스템 구현

기획 권위: [귀신 공통 시스템 기획서 0.4](../project/ghost-system.md). 이 문서는
2026-09-27 코드 구조와 검증 범위를 기록한다. 과거 프로토타입 규칙은 Git 이력에 보존한다.

> **원문 0.3 반영 상태 (2026-11-01).** 자연 진정 40초·조기 종료 60초 이내·Tutorial 시야 7.5m(`_Tutorial` 에셋만)는 반영했다.
> 청소 40% 필수 전제·사망 시 항상 어택 계속은 원문 0.3과 다르게 남아 있다 → [ghost-system.md §12.3](../project/ghost-system.md) "원문 0.3 반영으로 생긴 차이".

## 1. 구성과 서버 권위

| 구성 | 역할 |
| --- | --- |
| `GhostPrototypeSpawner` | Game 씬에서 Host가 기본 귀신 1마리를 자동 스폰. F1 디버그 조작 제공 |
| `GhostStateMachine` | 팀 평균 정신력·실제 청소 진행도를 받아 활동/경고/어택/자연 진정 결정 |
| `GhostPrototypeController` | 서버 상태/행동 흐름·타깃 선정·포획·정신력·복구·RPC, 각 피어의 연출 |
| `GhostPerception` | 시야·시야선·근거리 감지·이동 소리·본체 목격 판정, 플레이어 속도 표본/평활화 |
| `GhostNavigation` | 런타임 NavMesh·배회 삼각형·경로 코너·이동·걸림 감지·해제 |
| `GhostPrototypeSettings` | 지속시간·탐지·추격·초자연현상 수치의 ScriptableObject |
| `GhostPhenomenaDirector` / `GhostPhenomenaPlayer` | 기존 초자연현상 프로토타입 — 2026-11-15부터 박치기 전용 [임시](§5) |

귀신은 소유자가 없는 `NetworkObject`다. 상태와 위치는 서버가 정하고
`NetworkVariable<GhostPhase>` 및 `NetworkTransform`으로 복제한다. 클라이언트는 복제 상태로
조명·본체·소리를 재생하며 어택 판정이나 사망 판정을 하지 않는다. 잡히면 기존
`SanityNetworkState.ServerMarkDead()`를 호출한다. 집 밖과 `DrillCarSafeZone`의 플레이어는
탐지·포획 후보에서 제외한다.

**입력과 처리 경계 (2026-11-02 리팩토링 5단계).** 감지·이동 헬퍼는 같은 Gameplay 어셈블리의 internal 일반
C# 클래스이며 컴포넌트로 추가하지 않는다. `GhostPrototypeController.OnNetworkSpawn`에서 한 번 연결하고,
기존 서버 분기에서만 행동을 실행한다. 상태 기계·설정·RPC 2개·NetworkVariable·직렬화 필드와 복구 데이터 형식은 유지한다.

`GhostPerception`은 시야 계산을 공통화하되 침대 은신의 원뿔 시야(`CanSee`)에는 근거리·소리를 섞지 않는다.
일반 감지(`CanSense`/`TryDetect`)는 기존 근거리·소리 허용 범위를 유지한다. 은신·생존·집 경계·안전구역의 후보
제외는 Controller가 제공한다. 기본 추격과 단서 추적의 타깃 선정은 한 루프에서 처리하고, 단서 모드에만 관측 가능
조건을 더한다. 최근접·1m 히스테리시스·목격된 은신의 우선순위는 그대로다.

`GhostNavigation`은 후보 측정과 실제 이동의 경로 계산을 공통화하고, 진행 중 코너와 후보 측정 버퍼를 구분한다.
배회는 완전한 경로만, 추격·수색은 기존처럼 부분 경로도 허용한다. 집 경계·층 높이·문 회피와 표본 반경을 유지한다.
닫힌 문 확인은 Controller의 캐시를 콜백으로 연결하며, 프레임마다 헬퍼·콜백·코너 배열을 새로 만들지 않는다.
디스폰·파괴는 `Dispose`로 NavMesh를 제거하고, 호스트 복구는 재계산 대기시간을 복원한 뒤 이전 경로를 무효화한다.

**Stage1 은 드릴카 모델(`Prefabs/Map/DrillCar.prefab`)을 씬에 고정 배치한다(2026-09-29)** — 안전 구역은 차 실내 상자이고
런타임에 옮기지 않는다(`_placeBehindSpawnsAtRuntime` 끔). 배치·치수는 [stage-system.md §2.1](../project/stage-system.md).
아래 런타임 재배치는 모델이 없는 **ProtoTypeGame 의 임시 상자**에만 해당한다.

`DrillCarSafeZone`(임시 상자)은 씬이 올라올 때 `GameInstaller`가 스폰 줄 **뒤쪽**(스폰이 바라보는
반대편)으로 옮긴다(`PlaceBehindSpawns`). 스폰 줄 끝에서 `_gapBehindSpawns`(2.5m [TEMP]) 떨어진
자리이고, 바닥 높이는 아래로 레이를 쏴서 찾는다. B안에서는 앞마당(4m) 밖 땅(0.54m 낮음)에 서서
현관에서 약 5.8m 떨어진다. 스폰 자리에 겹쳐 두던 때는 상자 안의 종료 단말기가 현관을 막았다.
종료 단말기·공동 아이템 선반은 이 상자의 자식이라 같이 움직이고, 조립 영역
(`FurnitureMultiDriverPrototype`)은 같은 이동량만큼 따라간다. 2026-10-04부터 반출 구역은 상자 **오른쪽(+X) 바깥 땅**
(`FurnitureDeliveryZone.CreateBesideDrillCar`, 4 × 3.5 × 4m, 주황(#C65A20) 페인트·흰(#FFFFFF) 외곽선 표시 `FurnitureDeliveryZoneView`), 조립 영역은 상자 **왼쪽(−X) 바깥 땅**에 둔다(`GameInstaller`) —
이전에는 둘 다 상자 안이었다.

## 2. 생성, 청소, 상태

`GhostPrototypeSpawner.Update()`는 Game 씬에서 서버가 준비된 뒤 귀신 1마리를 한 번 자동
생성한다. F1에서 제거한 뒤 같은 스테이지 안에서 자동으로 다시 만들지는 않는다. 추가 귀신의
동시 상태·어택 규칙은 미결정이다(G-10).

`ICleaningService.ProgressPercent`는 배치된 얼룩 중 닦인 비율이다. 배치 전에는 0이고,
현재 12개 풀에서는 5개 청소 시 41%가 된다. 스포너가 실제 진행도를 귀신에게 전달한다.
F1의 청소 수치 조작은 디버그 보정값이며 실제 진행도와 큰 쪽을 사용한다.
`GhostStateMachine`은 40%에 한 번 도달하면 스테이지 동안 어택 조건을 유지한다.

| 상태 | 규칙 |
| --- | --- |
| 활동 | 스테이지 시작 상태. 정신력 61~100은 어택하지 않음. 31~60은 기존 10초 주기로 20/40/60% 확률 판정. 0~30은 즉시 100% 경고 시작 |
| 경고 | 5초. 방 조명 점멸과 2D 심장 박동음. 이후 어택 |
| 어택 | 일반 60초, 0~30에서 진입한 고위험 90초. 시작 후 60초 이내 팀 평균이 61 이상으로 회복되면 자연 진정(2026-11-01 원문 0.3, 구 20초). 포획은 서버만 실행 |
| 자연 진정 | 40초 배회·재어택 금지(2026-11-01 원문 0.3, 구 30초). 끝나면 현재 정신력으로 다시 판정 |

어택 강제 종료 API(`ServerForceSuppression`)는 진행 중인 어택을 즉시 자연 진정으로 보낸다.
`Idle`·`Suppressed` enum 값은 과거 데이터 호환용으로 남았지만 새 상태 기계에는 진입하지 않는다.
아이템 종류·사용 조건은 [기획 G-12](../project/ghost-system.md) 대기다.
31~60의 10초 판정 주기는 기존 프로토타입 값이며 원문 0.2에는 없다(G-20).
고위험 90초 종료와 0~30에서 계속 어택한다는 문장의 충돌은 미결정(G-9); 코드는
90초 종료를 임시로 사용한다. 고위험 시야 거리·각도는 별도 설정과 복제 상태로 분리했지만,
G-18 수치가 정해지지 않아 기본 에셋은 일반 시야와 같은 15m/120°를 사용한다.

## 3. 탐지, 추격, 은신

> 아래는 기본 AI(실험 전체 OFF)의 동작이다. 2026-09-30 사용자 요청으로 기본 에셋의 실험
> 토글을 ON했다. 단서 모드는 감지 가능한 후보만 추격하고 놓치면 수색한다 → §3.1.

어택 중 서버는 원뿔 시야(기본 15m/120°), 가림, 근거리와 기존 이동 소리 판정으로
첫 대상을 탐지한다. 추격 중에는 0.2초마다 집 안에서 추격 가능한 플레이어의 **좌표상 직선거리**를
비교한다. 현재 타깃을 유지하다 다른 플레이어가 1m 이상 가까워지면 교체한다. 은신·굴착 성공,
사망, 집 밖, 드릴 카 구역은 후보에서 제외한다. 추격 가능한 플레이어가 0명이면 마지막 위치
근처를 10초 수색하고, 후보가 생기면 다음 선정 주기에 추격을 재개한다.

이동은 **귀신 전용 런타임 NavMesh**를 쓴다([MAP-11](../project/roadmap.md), 2026-09-28).
스폰 직후 서버가 스포너의 `_navigationRoot`(집 루트) 자식 물리 충돌체를 `NavMeshBuilder`로 모아
굽는다(`GhostNavigation.BuildHouseNavMesh`). 에이전트 종류는 기본(0)이지만 반경은 설정 `NavAgentRadius`(0.25m [TEMP]),
높이·턱은 귀신 `CharacterController`(1.8m·0.35m)에 맞춘다 — 프로젝트 기본 Humanoid 반경 0.5로는
B안 1.0m 문틀이 양쪽에서 깎여 막혔다. Player·Furniture·GhostPrototype 레이어, 트리거, `DoorInteractable`
(움직이는 문)은 장애물에서 뺀다. 데이터는 despawn·파괴 때 지운다(씬과 무관하게 전역에 남으므로).

| 행동 | 규칙 |
| --- | --- |
| 배회 | 집 X/Z 경계 안의 NavMesh 삼각형을 면적 비례로 뽑는다 → 계단으로 이어진 1층·2층·다락이 모두 후보. 완전한 경로가 있는 곳만(지붕 위 같은 끊긴 섬 제외), `RoamMinDistance`(6m [TEMP]) 이상 떨어진 곳 우선. 포기 시간은 경로 길이 ÷ 속도 × 1.5 + 2초(최소 4초) — 층을 건너는 긴 경로도 끝까지 간다. 스포너 `_roamSize`의 높이는 쓰지 않는다 |
| 순찰 기억 (2026-09-29) | `GhostRoamMemory` 가 층(3m)·평면 격자(`RoamCellSize` 3m [TEMP])마다 마지막 방문 시각을 적는다. 목적지는 후보 `RoamCandidateCount`(6)개 중 **안 간 시간(최대 `RoamMemorySeconds` 90초) − 걸어가는 시간** 점수가 가장 높은 곳 → 방금 돈 방·층으로 되돌아가지 않고 가까운 미방문 구역부터 집 전체(다른 층 포함)를 돈다. 어택 중 배회도 같은 기억을 쓴다 |
| 두리번 (2026-09-29) | 활동 중 배회는 목적지에 닿으면 `RoamLookAround` 1.5~3.5초 [TEMP] 멈춰 좌우로 고개를 돌린다(그만큼 포기 시간을 미룬다). 어택 중에는 멈추지 않는다 |
| 추격 | 대상이 닿을 수 없는 곳(가구 위·공중)이면 NavMesh 표본 반경을 2.5m까지 넓히고 부분 경로도 받아 가장 가까운 지점까지 간다 |
| 수색 | 목격한 은신·굴착은 그 플레이어에게 곧장. 아니면 마지막 위치로 간 뒤 반경 `SearchWanderRadius`(4m [TEMP]) 안 같은 층을 돌아다닌다(§9.3) |
| 도착 | 평면 거리 ≤ `ReachDistance` **이고** 높이 차 ≤ 1.2m — 바로 위/아래 층 지점을 도착으로 치지 않는다 |
| 걸림 | 경로가 없거나 1초에 0.25m 미만 이동이면 배회는 다른 목적지를, 추격은 경로를 다시 만들고 문을 검사한다 |
| 문 | 어택 중(배회 포함) 0.5초마다 **다음 경로 코너 방향** 앞 `DoorOpenRange` 안의 닫힌 문을 연다. 활동 중 배회는 문을 열지 않는다 — 목적지를 고를 때 경로 선분(높이 1m)이 닫힌 문짝 충돌체를 지나는 후보를 버리고(2026-09-29), 가는 도중 닫히면 걸림으로 보고 목적지를 바꾼다 |
| 열린 문짝 (2026-09-29) | 문은 Default 레이어라 귀신 캡슐을 막는다. NavMesh 는 문을 빼고 구우므로 경로가 열린 문짝을 스치면 걸렸다 → 0.2초마다 **열린** 문 충돌체만 `Physics.IgnoreCollision` 으로 귀신과 무시한다. 닫힌 문은 계속 막는다 |
| 가구 (2026-11-07) | 귀신 몸은 가구(시체 포함)와 **물리 충돌한다** — 가구가 귀신을 통과하지 않고 귀신도 가구를 뚫고 걷지 못한다(설정 `CollideWithFurniture` 기본 켬, 끄면 예전 "유령" 동작). 플레이어 레이어와는 계속 충돌하지 않는다(포획은 거리 판정). 가구는 굽는 NavMesh 에 넣지 않고(던져지고 옮겨지므로) 서버에서 가구마다 `FurnitureNavObstacle`(NavMeshObstacle carving, 멈춘 뒤 0.5초부터 뚫음)로 지금 놓인 자리를 장애물로 반영한다 → 경로가 가구를 돌아간다. 높이 0.4m 미만·바닥 0.3m 미만 소품은 뺀다. NavMesh 가 새로 추가될 때 `RefreshAll` 로 다시 뚫는다 |
| 흔들기와 충돌 (2026-11-07) | 초자연현상 "물건 흔들기" 대상 가구는 흔드는 동안만 귀신 캡슐과 충돌을 끈다 — 귀신 바로 옆에서 떨리는 가구가 캡슐에 걸려 튀는 문제(충돌을 전부 끄던 이유)를 피한다. 끝나면 되돌린다 |

경로가 없으면 벽을 가로질러 직선 이동하지 않는다.

**가구 충돌과 내구도(2026-11-07).** 귀신과 부딪쳐도 가구 내구도는 줄지 않고(FD-7, `FurnitureNetworkPhysics.IsExcluded`), 귀신 충돌은 충돌 소리·
`ServerImpactReported`(귀신의 가구 충돌 조사)도 만들지 않는다 — 귀신이 자기 몸이 닿은 소리를 조사하러 가지 않는다.

일반 `HidingSpot`은 타깃이 들어가기 직전 귀신 시야에 있었다면 해당 위치로 가서 확률 없이
처치한다. 못 봤다면 탐지·포획 후보에서 빠진다. 과거의 비목격 은신처 30% 검사는 제거했다.
기존 `BedHideEvaluator`는 엎드려 침대 밑에 숨는 규칙을, `BurrowExposureTracker`는
굴착을 감지한 순간을 기록한다. 굴착 중 플레이어는 일반 추격 후보에서 빠지지만,
감지된 굴착은 해당 위치를 수색해 확률 없이 처치한다. 은신 중 음성 송신은 사용자 결정에 따라
유지한다([G-19](../project/ghost-system.md)).

### 3.1 AI 실험 토글 (2026-09-30)

[귀신 AI 실험 문서](../project/ghost-ai-experiments.md)에 승인 범위·임시 수치·검증을 기록한다.
GhostPrototypeSettings의 전체 토글과 5개 개별 토글은 F2 → 귀신 → AI experiments에서 즉시 변경한다.
전체 OFF면 기본 탐지·좌표 추격·무작위 수색·현상 간격으로 복귀한다.

- GhostTrackingMemory: 관측 위치·속도·시각 보관, 타깃 교체 시 속도 초기화, 확신도 감쇠와 제한된 예측.
- GhostSearchMemory: 고정 크기 방문 기록. 후보 경로·도주 방향·재방문·이동 비용 비교, 문 후보 추가.
- FurnitureNetworkPhysics.ServerImpactReported: 서버 충돌 위치 조사. 귀신 spawn/despawn/destroy 구독 수명 관리.
- GhostPhenomenaDirector.SetTension: 최근 위협·출현에 따른 타이머 진행 완화. 현상 Pool·어택 조건은 기존대로.
- 호스트 복구는 기존 타깃·마지막 위치를 쓰고 관측 속도·방문 기록·미접수 소음은 초기화한다.

새 코드는 기존 Gameplay와 DebugTools 어셈블리에 속한다. 판정·물리·사망은 서버만 실행한다.
목격된 은신과 완전 은신은 기존 안전 규칙을 지키고, 단서 모드의 일반 포획에 층·시야선 검사를 추가했다.

## 4. 정신력과 연출

활동 중 생존 플레이어가 귀신 본체를 보면 서버가 개인 정신력 **−5**를 즉시 적용한다.
계속 보고 있으면 **5초마다 −5**를 추가 적용하고, 시선을 돌리면 타이머를 초기화한다.
현상 목격 −5(2026-10-29: 15 → 5)와 별개의 경로다. 본체 목격의 임시 기준은 기존 현상 목격의 12m/70°·가림
판정을 재사용한다. 최종 기준과 두 감소의 중복 정책은 G-17 대기다.

귀신 본체는 **어택에만** 노출한다(2026-10-29 사용자 요청 — 활동·경고 중에는 숨김, F1 "본체 보이기"와
정신력 0 관전자는 예외). 활동 중 본체 목격 −5는 보이지 않아도 판정이 그대로라 "안 보이는 목격"이 생긴다(G-17 대기).
경고에는 `GhostAmbientLight`로 표시된 방 조명을
5초간 점멸시키고, 각 피어의 귀신 `AudioSource`에서 임시 합성 심장 박동음을 반복한다.
어택에는 방 조명을 빨간색으로 바꾸며 종료 후 원래 색·밝기로 복구한다.
**어택 천장등 깜빡임(2026-11-14 사용자 요청 "어택 시작하면 천장 라이트 깜빡깜빡, 디폴트는 꺼져 있는 게 맞다").** 그전에는 Stage1 천장등이
기본 꺼짐(`Light.enabled = false`)이라 경고 점멸·어택 빨간색이 밝기·색만 바꿔 **아무것도 보이지 않았다.** 이제 어택 동안 `GhostAmbientLight.FlickerLevel`
(1/9초 단계마다 55% 확률로 켜짐, 켜지면 기준 밝기 × 0.5~1, 조명 위치로 정한 씨앗이라 방마다 박자가 다르다)로 빨갛게 깜빡이고, 꺼 둔 천장등도 켠다.
어택이 끝나면 평소 켜짐 상태로 돌린다. 켜고 끄기는 **스테이지 조명 컨트롤러가 켜짐 상태를 알려 준 천장등**(`SetBase(밝기, 색, 켜짐)` → `StageManaged`)에만 한다 —
`Tutorial_Lamp` 처럼 가구 풀(`RandomFurnitureItem`)이 켜고 끄는 조명은 숨은 가구를 밝히지 않도록 밝기만 바꾼다. 천장 패널 발광은 `StageRoomLight` 가 조명 밝기를 따라가므로 같이 깜빡인다.
설정(`Room lights in attack` 헤더): 켜기/끄기 · 초당 단계 9 · 켜질 확률 0.55 · 밝기 하한 0.5 [TEMP]. 끄면 예전처럼 켜진 등만 빨갛게 바뀐다.
경고 5초 점멸은 그대로라 Stage1 처럼 천장등이 꺼진 방에서는 여전히 보이지 않는다.
"원래 값"은
`GhostAmbientLight.SetBase` 로 바뀔 수 있다 — Stage1 천장등 36개는 `StageLightingController`가
HUD 밝기를 기준값으로 넘긴다([map-generation.md §10.1.6](map-generation.md)). 빨간 원뿔 시야
표시는 기존 프로토타입 연출이다. 아트 음원이 들어오면 `_warningHeartbeatClip`에 연결해
합성음을 교체할 수 있다.

**빛과 귀신 모습 (2026-09-28 사용자 요청 "귀신이 빛에 영향 안 받고 너무 자유롭다" → 보이는 방식으로 확정)** —
본체·"일시 출현" 공통 재질 `M_GhostBody` 가 URP **Unlit** 반투명이라 불 꺼진 집에서도 혼자 뿌옇게 떠 보였고,
경고·어택 때 몸에 켜지는 `StateLight`(반경 9m, 밝기 1.2~4.5 / 5)가 등불처럼 주변을 밝혔다.

- `M_GhostBody` → URP **Lit** 반투명(색·알파 0.6 그대로, GUID 유지). 헤드라이트·켜진 천장등에 비칠 때만 뚜렷하고,
  어둠에서는 아주 약한 발광(`_EmissionColor` 0.007/0.008/0.012)으로 윤곽만 남는다. 환경 반사는 끔(Stage1 반사 0.25 가
  귀신을 띄우지 않게). 매끈함 0.35 라 헤드라이트에 은은한 하이라이트가 생긴다.
- `StateLight` 수치를 `GhostPrototypeSettings` 로 옮기고 줄였다 — 반경 **3m**, 경고 박동 **0.2~0.8**, 어택 **1.2** [TEMP].
  프리팹 저장값도 반경 3·밝기 0.8. 빛이 몸 안쪽에서 나오므로 Lit 몸체는 자기 조명에 밝아지지 않는다(주변만 붉게 번진다).
- ⚠️ 본체 목격 −5·현상 목격 −5 판정은 여전히 거리·각도·가림만 본다 — **어두워서 안 보여도 목격으로 친다.**
  빛(헤드라이트·천장등) 조건을 넣을지는 G-17 과 함께 사용자 결정 대기.
- 빨간 원뿔 시야 표시(`M_GhostVisionCone`)는 UI 성격이라 Unlit 그대로 뒀다(기획서 미결정 #10).
- 시야 표시 부채꼴은 **방향 표시 전용**이다(2026-10-29 사용자 요청). 반지름은 `VisionConeDisplayDistance`(기본 3m)이고
  감지 거리(`VisionDistance` 15m)와 따로 간다. 폭은 실제 시야각(일반/고위험)을 그대로 따른다.

**귀신 모델 (2026-09-30 사용자 요청 "Assets/Mesh/Ghost 모델로 적용")** — `Ghost_Prototype` 의 `Body` 캡슐을
`Mesh/Ghost/Ghost.fbx`(Blender, 원뿔형 천 귀신 + 양팔, 본 8개 스킨 메시, 애니메이션 없음)로 바꿨다.

- `Body` 는 이제 메시가 없는 **래퍼**이고 그 아래에 FBX 를 중첩 프리팹 `GhostModel` 로 둔다. 노출은
  `ApplyPhaseVisual` 이 `_body.SetActive` 로 켜고 끈다. 프리팹 저장값은 꺼짐. `_bodyRenderers` 는 모델의 스킨 메시 렌더러.
  FBX 내부 GameObject(메시·Armature·본 13개)는 전부 **레이어 오버라이드 10(GhostPrototype)** 을 프리팹에 저장했다
  (`ProjectWiringTests` 가 프리팹 전체 레이어를 검사). FBX 를 다시 내보내 노드 이름이 바뀌면 오버라이드를 다시 걸어야 한다.
- 크기: 원본 높이 0.76m → **임포트 배율(`globalScale`) 2** = 약 1.52m. 모델 루트를 y 0.55 에 둬 바닥에서
  약 0.2m 떠 있고 꼭대기가 약 1.72m(캐릭터 캡슐 1.8m 와 비슷, 플레이어 1.3m 보다 크다) [TEMP].
- 방향: 원본의 눈이 모델 −X 쪽이라 모델 루트를 **Y +90°** 돌려 귀신 정면(+Z)에 맞췄다. 팔은 좌우로 뻗는다.
- 재질 `M_Ghost` — `M_GhostBody` 설정(Lit 반투명, 알파 0.6, 환경 반사 끔)에 `Ghost_Emission.png` 를 기본·발광 맵으로
  연결(눈·아랫단이 어둡다). 발광색은 위 "빛과 귀신 모습" 결정대로 어둠에서 윤곽만 남게 낮게(0.025/0.027/0.04) 뒀다 —
  텍스처 의도대로 스스로 빛나게 하려면 `_EmissionColor` 를 올린다. FBX 의 `Ghost_Material` 은 임포트 설정에서 `M_Ghost` 로 리맵.
- "일시 출현"(`GhostPhenomenaPlayer`)은 여전히 `M_GhostBody` 캡슐이다.
- **정신력에 따른 선명도 (2026-09-30 사용자 요청 "정신력에 따라 좀 더 선명하게")** — 각 피어가 **자기 로컬 플레이어**
  정신력으로 본체 알파를 정한다(`ApplyBodyClarity`, `MaterialPropertyBlock` 의 `_BaseColor.a`, 복제 없음·연출 전용).
  정신력 최대 **0.3** → 최소 **0.9** 선형(50 에서 기존 0.6), 정신력을 잃은 관전자는 0.9.
  `SanityChanged`·`AliveStateChanged`·상태 변경 때 갱신. 수치는 `GhostPrototypeSettings._bodyAlphaAtFullSanity/_bodyAlphaAtZeroSanity` [TEMP].
  목격 판정(−5/−5)은 알파와 무관하게 그대로다.

## 5. 초자연현상 프로토타입

기존 `GhostPhenomenaDirector`는 활동 중 발생 간격을 정하고, 서버가 물건 흔들기·작은 소품
떨어뜨리기·문 조작을 실행한다. 서랍·조명·환영 연출은 RPC를 받아 각 피어가 재생한다.
목격한 플레이어의 정신력 −5(2026-10-29: 15 → 5)는 기존 서버 판정을 사용한다. 이 세부 규칙은 사용자 원문 0.2의
공통 규칙이 아니라 별도 기획 대기 중인 프로토타입이다(G-13). 은신처·본체 목격의 새 규칙과
같은 프레임에 적용될 수 있으며 최종 중복 정책은 G-17에 따른다.

**귀신별 현상 (2026-11-15 사용자 결정 — "기존 이상현상 싹 삭제, 인커는 인커 현상, 박치기는 임시로 지금 현상").**
기존 현상은 설정 타입이 정한다(ADR-0024): `GhostPrototypeSettings.UsesClassicPhenomena`(virtual, 기본 false)를
`GhostDinoSettings`만 true 로 덮어쓴다 [임시]. false 인 귀신(기본 `Ghost_Prototype`·인커)은 `OnNetworkSpawn`에서
`GhostPhenomenaDirector`를 만들지 않는다 — 현상 틱·긴장도 감속·현상 목격 −5가 모두 없고, Hub 강제 실행
(`ServerForcePhenomenon`/`ServerRunPhenomenon`)도 `None`을 돌려준다. 상태 요약 `Phenomena` 줄은 "없음"으로 나온다.
Tab Hub는 귀신 섹션에 **"이상현상"** 섹션 하나를 늘 펼쳐 둔다 — 지금 귀신과 그 귀신의 현상을 한 줄로 보이고,
인커 버튼(1 문틈 응시·2 벽 글씨·글씨 모두 지우기)은 씬 종류 목록에 인커가 있으면(`IGhostDebug.CanTestInker`) 늘 보인다.
지금 귀신이 인커가 아니면 누를 때 `GhostPrototypeSpawner.ForceInkerPhenomenon`이 종류 목록에서 설정이 `GhostInkerSettings`인
프리팹(`GhostPrototypeController.Settings`)을 찾아 스폰 지점에 교체한 뒤 바로 시작한다(인커 현상은 귀신 위치가 아니라 대상 플레이어 기준).
기존 공통 현상 버튼은 `HasClassicPhenomena`(박치기)일 때만 같은 섹션에 붙는다. 버튼 구성은 IMGUI Layout 이벤트에서만 정해
같은 프레임의 교체 클릭으로 컨트롤 수가 어긋나지 않게 한다. 인커 현상은 원래부터
이 디렉터와 따로 도는 `ServerTickInker`라 그대로다. 코드는 지우지 않았다 — 박치기가 쓰므로, 박치기 고유 현상이 정해지면
(ghost-dino.md BD-16) 디렉터·플레이어·관련 설정 필드를 함께 걷어 낸다. 현상 설정 필드는 기본 설정에 남아 있지만
`PhenomenonWitness*`는 본체 목격(`GhostPerception.CanWitnessBody`)도 쓴다.
알려진 문제: 박치기의 "조명 깜빡임"은 밝기만 바꿔, 천장등이 기본으로 꺼진 Stage1에서는 보이지 않는데 목격 −5는 적용된다(그대로 둠).
검증: EditMode `GhostPhenomenaDirectorTests.공통_현상은_박치기_설정만_쓰고_기본과_인커는_쓰지_않는다`(타입·실제 에셋 3종),
PlayMode `GhostInkerFlowTests.인커는_기존_공통_초자연현상이_없다`.

## 6. 검증과 남은 결정

- 2026-11-02 리팩토링 5단계: 컴파일 오류·경고 0, 전체 EditMode **588/588**·PlayMode **167/167**,
  그중 귀신 관련 **93/93**·**32/32** 통과. 시야/소리·고위험·타깃 교체·굴착 경합·경로 코너 보존·
  이동/경계·복구·디스폰 해제를 검사했다. 기존 메서드 이름을 찾던 굴착 소스 검사는 실제 틱 검사로 이관했다.
  실기 범위와 상세 결과는 [testing.md](../workflow/testing.md)의 5단계 기록을 따른다.
- 2026-09-30 AI 실험: 검증 복제본 Unity 컴파일 통과, 관련 EditMode **65/65**,
  Local Host PlayMode **16/16** 통과. 단서 상실·전체 OFF 복귀·가구 소음·은신·이벤트 해제를 검증했다.
  실제 Stage1 체감·예측 이동 연출·Steam 다인 접속은 미검증 → [실험 문서](../project/ghost-ai-experiments.md).

- `NavMeshPath`는 `MonoBehaviour` 필드 초기화에서 만들지 않는다. 현재는
  `GhostPrototypeController.OnNetworkSpawn()`이 생성하는 `GhostNavigation` 내부에서 만든다.
  과거 `InitializeNavMeshPath` 예외를 피하려고 `Awake`에서 만들던 것을 이 수명 경계로 옮겼다.
- `dotnet build GhostHunter.Tests.PlayMode.csproj -t:Rebuild --ignore-failed-sources`는
  2026-09-27 경고·오류 0건으로 통과했다. Unity 런타임 재확인은 대기 중이다.
- 2026-09-27 당시 Unity batchmode는 같은 프로젝트를 연 에디터가 있어 시작되지 않았다.
  현재 자동 검증은 별도 검증 복제본을 사용한다. 실제 Stage1 이동 체감·조명·음향·Steam 다인은 수동 확인이 필요하다.
- 2026-09-28 층간 이동(MAP-11): EditMode `GhostNavigationTests`가 `BuildHouseNavMesh`로 1.0m 문틀 통과·
  3m/5m 경사로 위층 도달·트리거 비장애물·층 인식 도착·배회 포기 시간을 검사한다.
  Stage1에서 실제로 계단을 오르내리는지는 Unity 플레이 확인이 필요하다.
- **2026-09-29 층간 이동이 실제로는 끊겨 있었다.** Stage1 경사 콜라이더(`Stair_Ramp_Collider`, 진행 5.0m)는 위층
  바닥 구멍(5.36m)보다 짧아 위쪽 끝과 슬래브 사이에 **수평 0.24m 틈**이 있었다. 플레이어 캡슐은 넘지만
  NavMesh 는 이를 3m 낭떠러지로 보고 양쪽 가장자리를 에이전트 반경만큼 깎아 계단 4개가 모두 위층과 끊겼다
  (합성 기하 테스트는 틈이 없어 통과했다). 각 계단 아래에 틈을 메우는 `Stair_Top_Lip_Collider`(2.2×0.2×0.43m,
  윗면 = 위층 바닥, 경사면 아래로 숨김)를 추가했다. EditMode `Stage1GhostNavigationTests` 가 실제 Stage1 집을
  굽어 계단마다 아래층 ↔ 위층이 **그 계단으로** 이어지는지 검사한다(수정 전 4건 PathPartial → 수정 후 통과).
  순찰 기억 점수는 `GhostRoamMemoryTests`. Host 플레이에서 실제로 오르내리는 모습은 아직 미검증이다.
- 고위험 탐지 강화 수치(G-18), 고위험 90초 종료 충돌(G-9), 본체 목격 판정(G-17),
  10초 확률 판정 주기(G-20)는 [기획 미결정 목록](../project/ghost-system.md)에 남아 있다.


## Tutorial 현상 선택과 한 층 제한 (2026-10-02)

Ghost_Tutorial은 기존 모델/AI와 Tutorial 전용 GhostPrototypeSettings를 사용한다(베이스 귀신 — '뒤돌아보지마' 기믹은 `_Tutorial` 에셋에서만 켠다, 아래 절).
PhenomenaPoolSize=3으로 매 스테이지 3종을 고르고, PhenomenaCandidateMask로 원룸에서 연출 가능한 흔들기·소품 떨어뜨리기·조명 이상·환영만 후보에 둔다.
기본 설정의 두 값은 0이므로 다른 스테이지는 기존 전체 후보를 쓴다. 주기·정신력 피해·어택 수치는 바꾸지 않았다.
(2026-11-15부터 기존 현상은 박치기 설정 타입에서만 돈다 — §5. 이 에셋이 박치기라 원룸 3종 선택은 그대로이고, Hub 로 Stage1 에 박치기를 불러도 이 3종 규칙을 쓴다.)
선택 종류의 비트 마스크를 GhostState 스냅샷에 저장하여 호스트 이전 후 재선정하지 않는다.
GhostPrototypeSpawner._restrictRoamHeight를 Tutorial에서만 켜서 202호 아래층/지붕을 배회·탐지 후보에서 제외한다.
모텔 FBX는 런타임 NavMesh의 읽기 요구에 맞춰 Read/Write를 켰다.


## 베이스 귀신 — 뒤돌아보지마 (구현, Tutorial 한정, 2026-11-01)

기획 권위: [ghost-base.md](../project/ghost-base.md). `GhostPrototypeSettings._gazeFreezeEnabled`가 켜진 에셋만 동작한다 —
**2026-11-09부터 어느 에셋도 켜지 않는다** — Tutorial 귀신이 박치기로 바뀌었다(이전: `_Tutorial` 만 켬). 꺼져 있으면 아래 경로는 전부 기존 동작이다.

| 구성 | 역할 |
| --- | --- |
| `GhostGazeFreeze` | 순수 C# 상태 기계(자유 → 정지 → 해제 후 면역·가속). 서버만 돌린다 |
| 클라이언트 `TickLocalGazeReport` | 어택 중 자기 카메라 절두체 안에 켜진 본체 렌더러가 있고 벽·가구(플레이어 제외 `NonGhostPrototypeRaycastMask`)에 가려지지 않는 표본점(중앙·위·아래)이 있으면 "바라봄". 바뀔 때 즉시, 바라보는 동안 0.25초마다 서버에 보고 |
| `ReportGazeRpc` | `[Rpc(SendTo.Server, InvokePermission = Everyone)]`. 귀신은 소유자가 없어 Owner 권한을 쓸 수 없다. 서버는 `SenderClientId`로 플레이어별 보고를 저장하고 0.8초 안에 못 받으면 놓은 것으로 친다 |
| `ServerTickAttack` | 매 틱 `IsAnyoneGazing`으로 상태 기계를 갱신. 정지 중에는 이동·문 열기를 건너뛰고 `TryCatch`와 은신처 처치 검사는 계속 돈다. 이동 속도는 `ChaseSpeed × SpeedMultiplier` |
| `CanCatchAt` | 포획 3경로(`TryCatch`·`ServerTickWitnessedShelter`·`ServerTickWitnessedBed`)의 공통 거리 판정. 기믹 꺼짐: `CatchRadius`(1.4m). 켜짐: 1.5m 초과 불가 / 0.8m 이하 시선 무관(정지도 해제) / 사이는 정지 중이 아니고 그 플레이어가 안 볼 때만 |
| 초기화 | 어택을 벗어날 때와 `ServerRestoreStageState`에서 상태 기계·보고를 비운다. 호스트 이전 스냅샷에는 포함하지 않았다(BG-8) |

설정 필드(`Base ghost` 헤더): 정지 최대 5초 · 면역 5초 · 가속 ×1.5 · 가속 5초 · 포획 상한 1.5m · 근접 0.8m.
붉은 원뿔 시야 표시(§4)는 그대로다 — 이미 감지 거리와 따로 3m로 그리는 방향 표시용이다.

**미구현·미검증:** 정지 시각·청각 연출(BG-3), 호스트 이전 복원, 실제 플레이에서 화면 판정·가림 체감, Steam 다인 보고 지연.
단위 검증은 EditMode `GhostGazeFreezeTests`(상태 기계 10건)뿐이다.

## 박치기 공룡 (구현, 2026-11-07)

기획 권위: [ghost-dino.md](../project/ghost-dino.md). **설정 에셋이 `GhostDinoSettings`(기본 귀신 설정 `GhostPrototypeSettings` 를 상속, 박치기 값만 추가)일 때만 동작한다**
— 2026-11-09 설계 고정([ADR-0024](decisions/ADR-0024-ghost-type-settings-inheritance.md)). 지금 `GhostPrototypeSettings_Tutorial`(Tutorial·DinoTest 의 `Ghost_Tutorial`)만 이 타입이고,
`_Default`(Stage1·ProtoTypeGame)는 기본 귀신이다. 켜고 끄는 스위치는 없다(이전: `_dinoChargeEnabled`, Default 에서 켬). 기본 설정이면 아래 경로는 전부 기존 동작이다.

| 구성 | 역할 |
| --- | --- |
| `GhostDinoCharge` | 순수 C# 상태 기계(추격 8초 → 준비 2초[앞 1.3초 조정·뒤 0.7초 고정] → 돌진 → 스턴 5초 → 추격). 서버만 돌린다. 시간은 틱마다 설정에서 읽는다. `FurnitureDamage` = ⌊거리×계수⌋ 최소 1 |
| `GhostPrototypeController.Dino.cs` | 컨트롤러 `partial`. `ServerTickAttack` 맨 앞의 `ServerTickDino` 가 준비·돌진·스턴 단계의 이동을 맡으면(true) 기존 추격·포획 틱을 건너뛴다. 추격 단계는 기존 규칙 그대로 |
| 타깃 | 추격 동안 `_target` 을 `_dinoTarget` 에 기억하고 그 **위치**를 매 틱 갱신한다. 죽거나 접속이 끊겨도 마지막 위치를 쓴다. 타깃이 한 번도 없으면 준비로 넘어가지 않는다 |
| 준비 | 제자리. 앞 구간엔 타깃 위치로 방향을 갱신하고 `IsDirectionLocked` 부터는 그 값을 굳힌다. 몸은 `DinoTurnSpeedDegrees`(540°/초)로 그 방향으로 돈다(붉은 원뿔도 같이 돈다) |
| 돌진 | 고정된 수평 방향으로 `DinoChargeSpeed`(7m/s). 한 틱을 0.05초 이하 스텝으로 쪼개 `CharacterController.Move`. 스텝마다 ① 플레이어(`CanChasePlayer` 통과 + 수평 거리 ≤ `DinoHitRadius` 0.7m + 높이 차 ≤ 1.2m) 즉사 ② `OnControllerColliderHit` 로 기록된 옆 접촉(법선 Y 0.5 이내)이 있고 의도한 이동의 80% 미만이면 막힘 ③ 집 경계 밖이면 벽 ④ 부딪힘 보고 없이 0.3초간 20% 미만이면 막힘 ⑤ 8초 상한 |
| 가구 피해 | 막힌 콜라이더의 `FurnitureNetworkPhysics` 에 `ServerApplyDirectDamage(⌊거리×5⌋, 최소 1)` — 충돌 판정 창·배치 보호·운반 피해 면제를 거치지 않는 서버 전용 직접 피해(FD-7 의 돌진 예외). 시체·벽·닫힌 문은 피해 없이 돌진만 끝난다 |
| 어택 종료 유예 | `GhostStateMachine.HoldAttackEnd` = 준비·돌진 중. **시간 종료만** 미루고 조기 종료·강제 진정 아이템은 막지 않는다. 스턴 중에는 미루지 않으므로 "시간 종료 시 돌진 중이었으면 스턴 없이 종료" |
| 초기화 | 어택에 들어갈 때·나갈 때(`_phase` 변경)와 기믹을 끌 때 `DinoReset`. 호스트 이전 스냅샷에는 넣지 않았다(BD-13) |
| 예고 연출 | `NetworkVariable<byte> _dinoStage` 로 단계를 복제하고 모든 피어가 몸 조명(`_stateLight`)을 바꾼다 — 준비: 노란빛 빠른 깜빡임 / 돌진: 흰빛 강하게 / 스턴: 푸른빛 어둡게. 소리·모델 연출 없음(BD-2) |

설정 필드(`Dino ghost` 헤더): 추격 8초 · 준비 2초 · 방향 고정 0.7초 · 스턴 5초 · 돌진 속도 7m/s · 회전 540°/초 · 충돌 반경 0.7m · 가구 피해 5/m · 돌진 상한 8초.
귀신-가구 충돌은 2026-11-07 변경(가구 행)으로 켜져 있어야 돌진이 가구에 막힌다 — `CollideWithFurniture` 를 끄면 돌진이 가구를 통과하고 피해도 없다.

검증: EditMode `GhostDinoChargeTests`(사이클 타임라인·방향 고정 구간·타깃 없을 때·충돌 종료·스턴·리셋·피해 공식·어택 종료 유예와 예외 14건),
PlayMode `GhostDinoChargeFlowTests`(실제 컨트롤러·캐릭터 컨트롤러 — 준비 중 방향 추적과 고정, 준비·돌진 중 종료 유예, 가구 충돌 시 ⌊거리×5⌋ 피해·스턴,
플레이어 충돌 즉사, 벽 충돌은 피해 없음, 스턴 5초 뒤 추격 복귀, 기믹 끄기 7건).
**미검증:** 실제 Stage1 에서 귀신이 돌진하는 장면·계단·좁은 복도 체감, 클라이언트 예고 연출, Steam 다인, 난이도별 속도, 호스트 이전 복원.

## 명령(인커) 귀신 (구현, 2026-11-14)

기획 권위: [ghost-inker.md](../project/ghost-inker.md) — 임시 해석 IK-1~18 은 그 문서 §8. **설정 에셋이 `GhostInkerSettings`(기본 설정 상속, ADR-0024)일 때만 동작한다.**
에셋 `GhostPrototypeSettings_Inker`(공통 수치는 `_Default` 와 같다 — EditMode 가 필드마다 비교), 프리팹 `Ghost_Inker`(`Ghost_Prototype` 복사본, 컨트롤러 `_settings` 만 다름,
`GlobalObjectIdHash` 633779966 = NGO 와 같은 `GlobalObjectId_V1-1-{guid}-{fileId}-0` 의 XXHash32 로 계산해 저장, `DefaultNetworkPrefabs` 등록).

| 구성 | 역할 |
| --- | --- |
| `GhostPrototypeController.Inker.cs` | 컨트롤러 `partial`. `ServerTick` 끝의 `ServerTickInker` 가 발동 판정·진행·발견·위반을, `Update` 의 `TickInkerVisual` 이 피어 연출을 맡는다. `ICommandWritingService`(대걸레 닦기)를 구현해 인커가 스폰된 동안 `Services` 에 등록한다 |
| `GhostInkerSequence` | 현상 진행표 순수 상태 기계(문틈: 열림 2 → 내밀기 1 → 대기 ≤5 → [눈 맞춤] 기울이기 0.5 → 쾅 0.5 → 긁기 2 / 벽: 쓰기 4[2m 접근 시 즉시 완성] → 목 1 → 응시 3 → 소멸 1). 남은 시간을 다음 단계로 넘긴다 |
| `InkerTurnAroundDetector` | 1초 창 안 어느 표본과 비교해 몸 방향이 135° 이상 달라지면 뒤돈 것. 플레이어마다 하나, 매 서버 틱 복제된 몸 요를 넣는다 |
| 복제 | `NetworkList<InkerWritingState>`(글씨: 명령·출처·문 앵커 ID·앵커 기준 위치/회전·폭/높이·쓰기 시작 서버 시각/시간·완성·닦기 남은/필요) + `NetworkVariable<InkerSequenceState>`(종류·단계·단계 시작 서버 시각·인형 자리·몸/얼굴 방향·문틈 이동량·문 ID). 단계가 바뀔 때만 쓴다. NetworkList 는 모든 귀신 프리팹이 `Awake` 에서 만든다(인커가 아니면 비어 있음) |
| 발동 | 활동·자연 진정에서 `InkerCheckInterval`(10초)마다 두 현상 순서를 섞어 확률(20%/30%)·남은 횟수(3/2)를 본다. 후보 플레이어 = `CanChasePlayer` 통과 |
| 문틈 후보 | 플레이어(평면 2~6m) × 씬 문: 닫힘·잡히지 않음·집 경계 안·문 1m 안에 사람 없음·카메라 정면 50°·문짝을 뺀 시야선. 정면에 가까운 문. 문짝 모양 = 문 루트 기준 비트리거 충돌체 경계(문이 돌아도 불변), 위치는 **닫힌 자세**(`DoorInteractable.ClosedWorldRotation`)로 계산 |
| 문틈 연출 | 인형은 플레이어 반대편, 손잡이 쪽 끝 뒤 0.6m 에 숨었다가 문틈 쪽으로 0.45m 내민다. 문은 `ServerBeginGhostPeek`(닫고 잠근 뒤 20° 를 2초에) → `ServerGhostSlam`(잠근 채 원래 속도로 닫음) → `ServerReleaseGhostHold`. 글씨는 ⑥에서 플레이어 쪽 면에 붙인다(문 앵커) |
| 눈 맞춤 | ③ 대기 중 서버가 플레이어 카메라(몸 요 + 복제 피치, 복제 카메라 높이)에서 얼굴(내민 자리 + 1.45m)까지 15° 안·15m 안·문짝 제외 시야선을 0.3초 유지하면 맞춤. 맞춘 사람마다 한 번 `ServerApplyDecrease(5)` |
| 벽 후보 | 플레이어 정면 ±45° 11갈래 수평 레이(높이 1.45m, 가구·플레이어·귀신 제외): 2~7m, 문·움직이는 물체 아님, 법선 수평·플레이어 쪽, 집 경계 안, 기존 글씨 1.2m 밖, 벽 앞 0.5m 바닥(같은 층)과 빈 캡슐. 정면에 가까운 곳 |
| 발견·위반 | 0.1초마다 다 쓴 글씨를 처음 본 플레이어 기록(8m·35°·앞면·시야선). 명령 글씨를 새로 본 틱에는 그 틱의 행동을 세지 않는다. 행동은 `InkerActions` 플래그로 모은다 — 뒤돌기(`InkerTurnAroundDetector`)·점프(`PlayerMotor.JumpCount` 변화)는 매 틱 보고, **문 열기**(`DoorInteractable.ServerOpenedByPlayer` — 플레이어 여닫기 RPC 가 문을 열 때만)·**가구 만지기**(`FurnitureGrabTarget.ServerTouchedByPlayer` — `ServerTryAddHolder` 성공·드라이버 분해 성공)는 서버 정적 이벤트로 플레이어별로 쌓였다가 다음 틱에 판정한다(2026-11-15). 명령 ↔ 금지 행동은 `InkerCommandRules`. 기록된 플레이어가 어기면 그 글씨들 제거 + `ServerApplyDecrease(10)` + 위반자에게만 `InkerJumpScareRpc`(`RpcTarget.Single`). 문 글씨 발견은 `ServerApplyDecrease(3)` |
| 닦기 | `PlayerCleaningController` 가 얼룩을 못 맞히면 `ICommandWritingService.TryRaycast`(다 쓴 글씨 앞면 사각형 + 앞을 가리는 물체 검사) → `RequestWipeWritingRpc` → 서버가 같은 조건(대걸레·간격·조준·벽 너머 금지)으로 `ServerTryWipe`. 6번이면 제거 |
| 어택 시작 | 문틈: 글씨·인형 제거, 문 놓고 닫음. 벽: 다 쓰기 전이면 글씨 제거, 다 썼으면 유지 |
| 피어 연출 | `InkerWritingView`(**피 글씨** — 아래 2026-11-15 절, 앵커 따라감) · `InkerPuppetView`(귀신 `_body` 복제, 충돌 없음, `_BaseColor` 알파로 나타남·사라짐) · `InkerSounds`(임시 합성음, 막 시작한 단계만 재생) · `InkerAtmosphere`(현상 중 화면 포스트프로세싱) |

**연출 다시 만듦 (2026-11-15, 사용자 "지금 연출 너무 거지같아. 피로 쓰이는 느낌으로…").**

| 구성 | 역할 |
| --- | --- |
| `InkerStrokeFont` | 손글씨 **획** 글꼴(대문자·`'`·`.`·`!`·`?`, 글자 높이 = 1). 낱말 단위 줄바꿈 `Layout(text, wrapWidth)` 과 서버 크기 계산 `Size(text, letterHeight, maxWidth, …)` — 줄이 최대 폭을 넘으면 줄을 바꾸고 한 낱말도 넓으면 글자를 줄인다(문짝). 서버·피어가 같은 계산을 쓴다 |
| `InkerBloodMesh` | 획을 손가락으로 긋듯 **진행도만큼** 리본 메시로 그린다: 획 시작에 고인 피(폭 1.4배), 압력 떨림, 끝으로 갈수록 가늘어지고 말라 갈라짐(정점 젖음 값), 날카로운 모서리에서 리본을 끊고 둥근 원판으로 잇기, 글자마다 회전 ±8°·크기·기준선 흔들림, 지금 쓰는 손끝 방울. 쓴 자리에서 **흘러내림**(획의 낮은 점·시작·끝, 글자당 ≤3, 길이는 지연 뒤 지수적으로 늘다 멈춤, 끝에 방울)과 **튄 방울**. 씨앗 = 글씨 Id → 모든 피어가 같은 모양. `IsSettled` 뒤로는 메시를 다시 짓지 않는다 |
| `InkerWritingView` | `InkerBloodMesh` 를 `MeshRenderer` 로 그린다. 머티리얼은 `GhostInkerSettings.WritingMaterial`(`M_InkerBlood`), 없으면 `Shader.Find`. 닦기 진행을 `_Alpha`(1 → 0.22)·`_Smear`(갈색으로 번짐)로. `TryGetTipWorld` — 현상 2 인형 몸이 손끝을 따라 옆으로 옮겨 간다 |
| 셰이더 `GhostHunter/InkerBlood` | URP 투명·조명 없음(어두운 집에서도 읽혀야 한다). 획 가장자리 들쭉날쭉(노이즈 문턱), 얇게 묻은 자국, 마른 끝 붓결 끊김, 젖은 광택 줄, 원판은 가운데 반짝임. 정점색은 sRGB 로 받아 선형으로 바꾼다 |
| `InkerAtmosphere` | 각 피어가 로컬로 만드는 **런타임 Volume 2개**(프로필 에셋 없음). 분위기(우선순위 60): 검붉은 비네트(심장 박동처럼 조임)·채도 −60·대비·노출 −0.4·약간 차가운 필터·필름 그레인·색수차. 충격(61): 렌즈 왜곡·강한 색수차·붉은 필터·노출 −0.6 — 지수적으로 사라진다. 세기 = 현상 단계별 값(`InkerStepMood`: 문 열림 0.35→0.6 … 눈 맞춤·쾅·응시 1) × 카메라~현상 거리(6m 이내 최대, 16m 밖 0). 충격: 고개 기울임(눈 맞춤) 0.45·문 쾅 0.85·목 꺾임 0.7·갑툭튀 1(위반자만). 늦게 들어온 피어는 지난 충격을 받지 않는다. Gameplay 어셈블리에 `Unity.RenderPipelines.Core/Universal.Runtime` 참조를 더했다 |
| 명령 2종 추가 | `DontOpenDoor`("DON'T OPEN THE DOOR")·`DontTouchFurniture`("DON'T TOUCH THE FURNITURE"). `PickWallCommand` 는 켜진 명령 중 고르게. 벽 글씨 한 줄 최대 2.4m(`WritingMaxLineWidth`) — 긴 명령은 2~3줄 |
| 기타 | `InkerWritingState` 에 `LetterHeight`·`WrapWidth`(피어가 서버와 같은 줄바꿈으로 그린다). 글씨 쓰는 소리를 젖은 문지름(저역 잡음 + 획마다 질척임 + 방울)으로 교체. 목 꺾임은 7단으로 끊기며 돈다. `NetProtocolVersion` 12 → 13 |

검증: EditMode 새 `InkerBloodWritingTests` 10건(모든 명령 획 존재·줄바꿈·문짝 축소·진행도별 메시·씨앗 일치·흘러내림이 늘다 멈춤·손끝 방향·뷰 쓰기 방향·명령 규칙·명령 고르기·단계별 분위기), PlayMode 새 `GhostInkerFlowTests` 3건(문 열지 마 위반·다른 행동 무시 / 가구 만지지 마 위반 / 보기 전 행동 무시)과
`InkerPresentationRenderTests` 2건 — 그래픽이 켜진 PlayMode 에서 실제로 렌더해 피 글씨가 분홍 오류 없이 검붉게 보이는지·쓰는 중엔 일부만·닦으면 흐려지는지, 분위기가 모서리를 어둡게·채도를 빼고 충격이 금방 사라지는지 본다
(`GH_INKER_RENDER_DIR` 를 주면 장면 PNG 저장). **미검증:** 실제 Stage1 조명·벽 재질 위에서의 모습, 다인에서 각 피어 분위기.

**스포너 귀신 종류 (같은 날).** `GhostPrototypeSpawner._ghostTypes`(`GhostTypeOption`: 이름·프리팹·`InRandomPool`)와 `_randomizeInitialType`. 비어 있으면 `_ghostPrefab` 하나.
Stage1: 기본(랜덤)·인커(랜덤)·박치기(`Ghost_Tutorial`, Hub 전용). `IGhostDebug` 에 종류 목록·선택·`ReplaceGhostWithSelected`(기존 귀신 despawn → 고른 종류 spawn)·인커 강제 실행/지우기를 더했고 Hub 귀신 섹션에 버튼을 붙였다.
호스트 이전 스냅샷 `GhostState.TypeIndex` 로 같은 종류를 다시 스폰한다(인커 글씨·현상·횟수는 미포함). `NetProtocolVersion` 11 → 12.
F2 튜닝 창(`TuningHud`)은 설정을 들고 있던 컴포넌트가 사라지면(귀신 교체) 대상을 다시 찾는다 — 전에는 처음 찾은 귀신의 설정 에셋을 계속 보여 줬다.
종류 목록이 없는 씬(Tutorial·DinoTest 등)은 그 씬의 귀신 프리팹 하나가 프리팹 이름으로 보인다.

**검증 (2026-11-14, 검증 복제본).** 컴파일 경고 신규 0. 전체 EditMode **755/755** — 새 `GhostInkerTests` 20건(진행표·눈 맞춤 구간·즉시 완성·남은 시간 이월,
뒤돌기 판정 경계·창·초기화·360° 경계, 글씨 앞면 판정·문 앵커, TextMesh 읽는 방향, 종류 고르기, 인커 에셋 원문 수치·Default 공통 수치 필드별 일치,
`Ghost_Inker` 설정 타입·고유 해시·레이어·등록, Stage1 스포너 종류 목록). PlayMode 새 `GhostInkerFlowTests` **7/7**(벽 글씨 → 발견 → 뒤돌기 −10·삭제 / 보기 전 동작 무시 /
대걸레 6번 해제 / 문틈 눈 맞춤 −5·글씨 발견 −3·문 잠금·닫힌 채 해제 / 어택 시 정리 2건 / 확률 발동·횟수 제한·어택 중 미발동). 전체 PlayMode 256건 중 255 통과 —
실패 1건은 `DinoTestSceneFlowTests` "원으로 도망치는 플레이어"(추격 중 플레이어가 잡혀 돌진 단계에 못 감)로, 변경 전 커밋 9ac1d56 에서도 8회 중 2회 같은 방식으로 실패하는
선재 불안정이다(7m 스폰 각도·배회가 `Random`). 변경 전 커밋은 단독 8회 중 2회·전체 실행 2회 중 1회 실패. 이 브랜치에서는 9회 중 7회 실패해 비율이 더 높게 나왔지만
박치기 경로는 인커 코드를 첫 줄에서 빠져나가고 추가 `Random` 호출도 없어 인과를 찾지 못했다 — 표본이 작다.
같은 날 어택 천장등 깜빡임을 더한 뒤 전체 EditMode **759/759**(새 `GhostAttackLightTests` 4건), PlayMode 257건 중 256(새 천장등 흐름 1건 포함 — 실패는 같은 박치기 시험장 1건).
**미검증:** 실제 Stage1 화면(인형 자세·문틈 위치·글씨 크기·읽는 방향), 다인 클라이언트 연출·RPC, 실제 마우스 회전 체감, 합성음.

## 원문 0.3·0.4 어택 규칙 (구현, 2026-11-07)

기획: [ghost-system.md](../project/ghost-system.md) G-9·18·22·23·27④·30. 원문에 없는 부분은 [임시 해석]이고 설정으로 켜고 끈다(F2 → 귀신 → "Attack rules").

| 규칙 | 구현 | 설정 |
| --- | --- | --- |
| 청소 40% 일회성 강제 어택 (G-22) | `GhostStateMachine`: 40%를 **처음** 넘는 틱에 대기(pending) → 활동 상태에서 경고(정신력 무관). 어택이 어떤 경로로든 시작되면 그 몫은 끝. 대기·발동 여부는 스냅샷(`CleaningAttackPending/Fired`)에 담아 호스트 이전 뒤 재발동하지 않는다. **정신력 어택의 40% 필수 전제는 유지** | `CleaningForcesAttackOnce` |
| 0~30 계속 어택 (G-9) | `TickAttack`: 어택 시간(60초·고위험 90초)이 끝나는 때 팀 정신력이 `HighRiskTeamSanity`(30) 이하면 끝내지 않는다. 31 이상이 되면 다음 틱에 종료 | `AttackContinuesWhileCritical` |
| 난이도 (G-23) | `GhostDifficulty`(쉬움/보통/어려움) — 에셋마다 하나(Default 보통·Tutorial 쉬움). 쉬움: 어택 중 `living < 어택 시작 때 생존자`이면 `ForceSuppression()` → 자연 진정. 박치기 돌진 속도도 난이도별 | `Difficulty` |
| 고위험 탐지 강화 (G-18) | 수치만 — Default 에셋 고위험 시야 15m/120° → 20m/140°(임시). Tutorial 은 일반과 같게 유지. 이동 소리 탐지 강화는 없다 | `HighRiskVisionDistance/Angle` |
| 7m 스폰 (G-30) | `GhostPrototypeController.AttackRules.cs` `TrySpawnNearPlayer`: 어택이 시작될 때(`_phase` 가 Attack 으로 바뀐 틱) 가장 가까운 추격 가능 플레이어가 반경(7m) 밖이면, 그 플레이어 둘레 16곳 중 같은 층·집 경계 안·세이프 존/은신처 밖·NavMesh 경로 ≤ 14m 인 지점을 고른다. 어떤 플레이어의 시야에도 안 걸리는 지점을 먼저, 없으면 걸려도 되는 첫 지점. 순간이동은 캐릭터 컨트롤러를 끄고 위치를 바꾼 뒤 `NetworkTransform.Teleport` | `AttackSpawnNearPlayerEnabled`·`AttackSpawnRadius` |
| 말소리 소음 (G-27④) | `ReportVoiceNoise` — 0.5초 소음 훑기 안, 어택 중 배회할 때만. `PlayerVoiceEmitter.TryGetSpeechLevel`(소유자가 RPC 로 보고한 입력 dBFS)로 반경 8~12m 를 정하고 `CanHearNoiseAt` 통과 시 소음 지점으로 기억(보통/높음) → 기존 소리 조사 경로. 음성 쪽 구조는 [voice-chat.md](voice-chat.md) | `VoiceNoise*` |

`NetProtocolVersion` 10(박치기 단계 복제 + 말소리 보고 RPC). 검증: EditMode `GhostAttackRulesTests`(청소 강제 어택·일회성·대기·스냅샷, 0~30 계속·종료·설정 끔, 난이도별 돌진 속도·에셋 값, 말소리 반경 계산),
PlayMode `GhostAttackRulesFlowTests`(7m 스폰·시야 밖 우선·갈 수 없으면 제자리·설정 끔, 쉬움 사망 종료·보통/어려움 계속, 말소리 크기·보고 끊김·어택 아님·죽음). 기존 `GhostPrototypeStateMachineTests` 는 새 규칙 두 개를 끄고 기본 메커니즘을 계속 검증한다.
**미검증:** 실제 Stage1 에서 7m 스폰 연출·0~30 영구 어택의 체감, 실제 마이크로 말하는 클라이언트의 보고 왕복, 난이도 선택 UI(미정).

## 원문 0.4 소리 조사·층 활동량 (구현, 2026-11-06)

기획: [ghost-system.md §9.6](../project/ghost-system.md#96-귀신-움직이는-로직). 설정 토글 `Noise Reaction Enabled`(AI 실험 그룹, 전체 OFF면 같이 꺼짐,
`AiFeatureMask` 비트 64). **모든 수치는 [TEMP]** — 원문에 거리·확률식이 없다(G-27~29). 서버만 판정하고 새 NetworkVariable·RPC는 없다.

| 구성 | 역할 |
| --- | --- |
| `GhostNoiseMemory` | 조사하지 못한 가장 중요한 소리 1개. 높은 수준이 낮은 수준을 덮고 같으면 최신이 이긴다. `EvidenceMemorySeconds`(8초) 지나면 버림 |
| `GhostActivityMap` | 층(`GhostRoamMemory.FloorOf`, 3m 단위)별 활동량. 반감기 20초. `Bonus(floor)` = 가장 활동한 층 대비 비례 × `ActivityBonusSeconds`(30), 최고 활동량 < `ActivityMinimumPeak`(2)면 0 |
| `GhostFurnitureNoiseScanner` | `FurnitureGrabTarget.State`·위치를 0.5초마다 비교. 든 큰 가구가 0.5m 이상 이동 → 높음, 든 가구를 내려놓음 → 작은 물건 보통 / 큰 가구 높음. 가구 코드는 건드리지 않는다 |
| `GhostPrototypeController.ServerTickNoiseSense` | `NoiseScanInterval`(0.5초)마다 활동량 갱신 + 가구 훑기. 활동량은 모든 상태에서, 소리는 어택 중 배회일 때만 기억 |
| `CanHearNoiseAt` | 충돌 조사에서 뽑은 공통 판정: 직선거리·NavMesh 경로 길이 ≤ 반경, 벽·문에 가리면 반경 × 0.5 |

**흐름.** 소리 > 활동량 > 배회 순서다.
1. 어택 중 `Pursuit.Roam`이고 타깃 선정 틱(0.2초)에 유효한 소리가 있으면 `_lastKnownPosition`을 소리 지점으로 두고 `BeginSearch()`.
   조사 = 기존 수색(지점으로 이동 → 주변 둘러보기 10초 → 배회 복귀). 추격·수색 중과 목격된 은신 확인 중에는 새 소리로 바꾸지 않는다.
2. 소리가 없으면 배회 목적지 후보 점수(`GhostRoamMemory.Score`)에 층 활동량 가산을 더한다. 활동이 없으면 가산 0 = 기존 방문 기록 배회.
3. 활동량: 집 안·드릴 카 밖 생존자의 가만히·웅크림 0.25 / 걷기 1 / 달리기 1.5 (/s) + `GrabController.IsHolding` 운반 +2. 청소 작업은 세지 않는다.

가구 충돌·낙하(매우 높음)는 기존 `HandleServerImpact` 경로(`_hasPendingImpact`, 14m, 최소 2m/s)를 그대로 쓴다 — 공통 판정만 `CanHearNoiseAt`으로 뺐고 동작은 같다.
소리·훑기 상태는 AI 토글 변경·호스트 복구·디스폰·어택 종료에서 초기화한다.

**미구현.** ① 플레이어 없는 어택의 7m 스폰(G-30 — 기준점·§3.2 상시 존재와의 충돌). ② 음성 채팅 소음(G-27 ④ — 서버가 받는 음성 패킷에 음량이 없고
열린 마이크는 무음도 보낸다). ③ 활동·자연 진정 배회의 소리 반응(G-29). ④ 웅크려 이동 "거의 반응하지 않음"과 "탐지 없음"의 통일(G-27 ②).

**검증 (2026-11-06, 검증 복제본).** EditMode **677/677**·PlayMode **212/212** 통과(컴파일 포함, 기존 충돌 조사 `GhostAiExperimentFlowTests` 회귀 없음).
새 `GhostNoiseReactionTests` 14건: 소리 우선순위·만료·소비, 활동량 가산·반감기·최소 기준, 층 번호 기준, 토글/마스크, 기본 수치 순서.
**가구 훑기가 실제 `FurnitureGrabTarget`을 보고 소리를 내는지, 조사 → 배회 복귀의 체감, 다인 접속은 미검증**(EditMode에서 NetworkBehaviour를 스폰할 수 없다).
