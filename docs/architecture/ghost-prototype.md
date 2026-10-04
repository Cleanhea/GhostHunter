# 귀신 공통 시스템 구현

기획 권위: [귀신 공통 시스템 기획서 0.2](../project/ghost-system.md). 이 문서는
2026-09-27 코드 구조와 검증 범위를 기록한다. 과거 프로토타입 규칙은 Git 이력에 보존한다.

## 1. 구성과 서버 권위

| 구성 | 역할 |
| --- | --- |
| `GhostPrototypeSpawner` | Game 씬에서 Host가 기본 귀신 1마리를 자동 스폰. F1 디버그 조작 제공 |
| `GhostStateMachine` | 팀 평균 정신력·실제 청소 진행도를 받아 활동/경고/어택/자연 진정 결정 |
| `GhostPrototypeController` | 서버 탐지·타깃 선정·경로 이동·포획·정신력 감소, 각 피어의 연출 |
| `GhostPrototypeSettings` | 지속시간·탐지·추격·초자연현상 수치의 ScriptableObject |
| `GhostPhenomenaDirector` / `GhostPhenomenaPlayer` | 별도 기획 대기 중인 기존 초자연현상 프로토타입 |

귀신은 소유자가 없는 `NetworkObject`다. 상태와 위치는 서버가 정하고
`NetworkVariable<GhostPhase>` 및 `NetworkTransform`으로 복제한다. 클라이언트는 복제 상태로
조명·본체·소리를 재생하며 어택 판정이나 사망 판정을 하지 않는다. 잡히면 기존
`SanityNetworkState.ServerMarkDead()`를 호출한다. 집 밖과 `DrillCarSafeZone`의 플레이어는
탐지·포획 후보에서 제외한다.

**Stage1 은 드릴카 모델(`Prefabs/Map/DrillCar.prefab`)을 씬에 고정 배치한다(2026-09-29)** — 안전 구역은 차 실내 상자이고
런타임에 옮기지 않는다(`_placeBehindSpawnsAtRuntime` 끔). 배치·치수는 [stage-system.md §2.1](../project/stage-system.md).
아래 런타임 재배치는 모델이 없는 **ProtoTypeGame 의 임시 상자**에만 해당한다.

`DrillCarSafeZone`(임시 상자)은 씬이 올라올 때 `GameInstaller`가 스폰 줄 **뒤쪽**(스폰이 바라보는
반대편)으로 옮긴다(`PlaceBehindSpawns`). 스폰 줄 끝에서 `_gapBehindSpawns`(2.5m [TEMP]) 떨어진
자리이고, 바닥 높이는 아래로 레이를 쏴서 찾는다. B안에서는 앞마당(4m) 밖 땅(0.54m 낮음)에 서서
현관에서 약 5.8m 떨어진다. 스폰 자리에 겹쳐 두던 때는 상자 안의 종료 단말기가 현관을 막았다.
종료 단말기·공동 아이템 선반은 이 상자의 자식이라 같이 움직이고, 조립 영역
(`FurnitureMultiDriverPrototype`)은 같은 이동량만큼 따라간다. 2026-10-04부터 반출 구역은 상자 **오른쪽(+X) 바깥 땅**
(`FurnitureDeliveryZone.CreateBesideDrillCar`, 4 × 3.5 × 4m, 초록 빛기둥·윤곽선 표시 `FurnitureDeliveryZoneView`), 조립 영역은 상자 **왼쪽(−X) 바깥 땅**에 둔다(`GameInstaller`) —
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
| 어택 | 일반 60초, 0~30에서 진입한 고위험 90초. 시작 후 20초 이내 팀 평균이 61 이상으로 회복되면 자연 진정. 포획은 서버만 실행 |
| 자연 진정 | 30초 배회·재어택 금지. 끝나면 현재 정신력으로 다시 판정 |

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
굽는다(`BuildHouseNavMesh`). 에이전트 종류는 기본(0)이지만 반경은 설정 `NavAgentRadius`(0.25m [TEMP]),
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

경로가 없으면 벽을 가로질러 직선 이동하지 않는다.

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
현상 목격 −15와 별개의 경로다. 본체 목격의 임시 기준은 기존 현상 목격의 12m/70°·가림
판정을 재사용한다. 최종 기준과 두 감소의 중복 정책은 G-17 대기다.

귀신 본체는 활동·경고·어택에 노출한다. 경고에는 `GhostAmbientLight`로 표시된 방 조명을
5초간 점멸시키고, 각 피어의 귀신 `AudioSource`에서 임시 합성 심장 박동음을 반복한다.
어택에는 방 조명을 빨간색으로 바꾸며 종료 후 원래 색·밝기로 복구한다. "원래 값"은
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
- ⚠️ 본체 목격 −5·현상 목격 −15 판정은 여전히 거리·각도·가림만 본다 — **어두워서 안 보여도 목격으로 친다.**
  빛(헤드라이트·천장등) 조건을 넣을지는 G-17 과 함께 사용자 결정 대기.
- 빨간 원뿔 시야 표시(`M_GhostVisionCone`)는 UI 성격이라 Unlit 그대로 뒀다(기획서 미결정 #10).

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
  목격 판정(−5/−15)은 알파와 무관하게 그대로다.

## 5. 초자연현상 프로토타입

기존 `GhostPhenomenaDirector`는 활동 중 발생 간격을 정하고, 서버가 물건 흔들기·작은 소품
떨어뜨리기·문 조작을 실행한다. 서랍·조명·환영 연출은 RPC를 받아 각 피어가 재생한다.
목격한 플레이어의 정신력 −15는 기존 서버 판정을 사용한다. 이 세부 규칙은 사용자 원문 0.2의
공통 규칙이 아니라 별도 기획 대기 중인 프로토타입이다(G-13). 은신처·본체 목격의 새 규칙과
같은 프레임에 적용될 수 있으며 최종 중복 정책은 G-17에 따른다.

## 6. 검증과 남은 결정

- 2026-09-30 AI 실험: 검증 복제본 Unity 컴파일 통과, 관련 EditMode **65/65**,
  Local Host PlayMode **16/16** 통과. 단서 상실·전체 OFF 복귀·가구 소음·은신·이벤트 해제를 검증했다.
  실제 Stage1 체감·예측 이동 연출·Steam 다인 접속은 미검증 → [실험 문서](../project/ghost-ai-experiments.md).

- `NavMeshPath`는 `MonoBehaviour` 필드 초기화 중 생성할 수 없어
  `GhostPrototypeController.Awake()`에서 생성한다. Unity 에디터가 보고한
  `InitializeNavMeshPath` 예외에 대한 수정이다.
- `dotnet build GhostHunter.Tests.PlayMode.csproj -t:Rebuild --ignore-failed-sources`는
  2026-09-27 경고·오류 0건으로 통과했다. Unity 런타임 재확인은 대기 중이다.
- Unity batchmode는 같은 프로젝트를 연 에디터가 있어 시작되지 않았다.
  EditMode·PlayMode 테스트 실행 및 실제 NavMesh 이동·조명·음향 확인은 **미검증**이다.
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

Ghost_Tutorial은 기존 베이직 모델/AI와 Tutorial 전용 GhostPrototypeSettings를 사용한다.
PhenomenaPoolSize=3으로 매 스테이지 3종을 고르고, PhenomenaCandidateMask로 원룸에서 연출 가능한 흔들기·소품 떨어뜨리기·조명 이상·환영만 후보에 둔다.
기본 설정의 두 값은 0이므로 다른 스테이지는 기존 전체 후보를 쓴다. 주기·정신력 피해·어택 수치는 바꾸지 않았다.
선택 종류의 비트 마스크를 GhostState 스냅샷에 저장하여 호스트 이전 후 재선정하지 않는다.
GhostPrototypeSpawner._restrictRoamHeight를 Tutorial에서만 켜서 202호 아래층/지붕을 배회·탐지 후보에서 제외한다.
모텔 FBX는 런타임 NavMesh의 읽기 요구에 맞춰 Read/Write를 켰다.
