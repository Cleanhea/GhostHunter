# 로드맵 & 태스크 보드

> 에이전트는 작업 시작 시 여기서 대상 태스크를 확인하고, 완료 시 상태를 갱신한다.
> 상태: `대기` → `진행중` → `완료` / `보류`

---

## 0. 시스템별 현재 상태

> 2026-09-28 CLAUDE.md 에서 옮겼다. CLAUDE.md 는 매 요청마다 컨텍스트에 실리므로 진행 상황은 여기에 둔다.
> 시스템 상태가 바뀌면 CLAUDE.md 가 아니라 이 절을 갱신한다.

**에디터 설치·생성 도구 삭제(2026-09-28, [ADR-0020](../architecture/decisions/ADR-0020-remove-one-off-editor-setup-tools.md)).**
`GhostHunter > … 설치`/`… 생성`/`… 검증` 메뉴 36개와 맵 생성기(`HousePrototypeBuilder`)를 지웠다. 저장된 씬·프리팹이 원본이다.
이 문서와 다른 문서의 메뉴 실행 기록은 당시 기록이다. 검증용 복제 프로젝트 EditMode 312건 중 308 통과 — 실패 4건은
복제본 긴 경로의 Burst 오류 2건과 커밋 전 `PlayerInputReader`·`GhostPrototypeController` 변경에 걸린 소스 검사 2건(이 작업과 무관).

**아키텍처 정비 — 마무리 단계.** 씬 구조·서비스 수명·8개 asmdef 레이어·테스트 어셈블리·
가구/문 프리팹화(굽기까지 완료, 커밋 `015f874`)가 끝났다. 남은 것은 로비 정책(MIG-11, 결정 대기)이다 → §2

**다음 큰 작업 — 맵 생성 v0.4.** Type·Count·생성/실패 처리·B/C 비교·작업량 검증을 반영했고,
MAP-1 오른쪽 그레이박스 1차 생성까지 완료됐고, B/C 비교용 실내·계단·가구 생성기(MAP-15) 코드도 추가됐다.
B/C 메뉴 실행·씬 저장까지 완료했고(2026-09-06), 같은 날개 안 방-방 사이 여백을 없애는
재설계도 반영했다(방+홀 비율 68.5~83.0%). 수동 체감 확인 후 MAP-2로 진행한다. 착수 전
§1.2 MAP 과 [map-generation.md §2·§12](../architecture/map-generation.md)를 읽는다.
B안(`PlanBFurnitureSpawnSetup` — 가구 풀·후보·서버 생성기·앞마당 시작 위치)은 코드 구현·C# 빌드·Unity 메뉴 실행·
씬/설정 저장 완료, Unity Test Runner·Host/Client Play 검증 대기.

| 시스템 | 상태 |
| --- | --- |
| 일반 로비·인게임 로비·상점·스테이지 전환 | 세션 유지, 전환마다 플레이어 재스폰, 인게임 로비 ⇄ 스테이지는 이전 씬 먼저 언로드([ADR-0018](../architecture/decisions/ADR-0018-persistent-session-in-game-lobby.md)). 코드·설치 도구 반영(2026-09-28), 게스트·Steam 다인 미검증. 스테이지 출발 대상은 **Stage1**(B안·드릴카 안전 구역·조립 영역·정신력 UI, 귀신·청소 없음), 구 Game 은 **ProtoTypeGame**([ADR-0019](../architecture/decisions/ADR-0019-stage1-scene-split.md)) — 에디터 메뉴 실행·Stage1 진입 확인(ST-9) |
| 청소·대걸레·얼룩 | 가구 완료·진행도는 미정 유지 |
| 플레이어 캐릭터 모델·애니메이션 | 모델·Animator·Player 프리팹 배선은 저장된 에셋이 원본(설치 메뉴는 ADR-0020으로 삭제). Humanoid 리타깃, 웅크리기·엎드리기 애니메이션 없음 |
| 가구 내구도·충돌 파손 | 기획 0.4(2026-09-16). 내구도 0에서 가구를 파괴하지 않는다(FD-10 재확정) — 파괴는 `FurnitureDefinition.DestroyAtZeroDurability` 스위치로 보존(기본 꺼짐). 조립 완성품은 0을 포함한 부품 내구도 평균. Unity 실행·Host/Client 검증 대기 |
| 플레이어 스킬(탐지·굴착) | 초안. 미결정 15건(MS-1~15) |
| 헤드라이트(손전등 0.1 → 기본 장착 헤드라이트) | 코드·Player 프리팹 배선(2026-09-28). F키 토글·배터리 0.6/s·드릴카 충전 5/s·15 이하 깜빡임·충전 UI·정신력 어둠 노출·땅굴/은신 규칙 연결. 효과음·아이콘 에셋 없음, 화면 밝기·그림자·Host/Client 미검증, 임시 선택 HL-1 → [headlamp-system.md](headlamp-system.md) |
| 일시정지 메뉴·나가기·연결 끊김 | 구현됨. 수동 검증·선행 검증 D-1 대기 |
| 퀵슬롯(라디얼 휠) | 대걸레·맨손 장착 연결. 일반 인벤토리는 별도 작업, Local Host 입력 검증 완료 |
| 가구 분해·조립(멀티 드라이버) | FM-IMPL-1~3 구현·씬 설치. 기획서 1.1(2026-09-13): 우클릭을 끝까지 누르고 있어야 완료, 중앙 원형 게이지 HUD 구현. 분해 부품이 풀 보관 위치로 되돌아가던 버그 수정. EditMode 235개·PlayMode 32개 통과, 실제 Game Local Host에서 유지 완료·뗌 취소·부품 착지 확인. 조립 영역 트리거가 지면에서 1.5m 떠 있어 조립이 시작될 수 없던 문제를 2026-09-17 수정(코드만) — Game 씬에서 `GhostHunter > 가구용 멀티 드라이버 조립 영역 재배치`를 실행해야 반영된다. 기획서 1.2(2026-09-27, 코드만): 영역 안 아무 재료나 조준해 조립, 못 하면 "재료가 부족합니다" 등 이유 문구, 완성 가구는 재료로 안 셈. 조립 영역을 게임 화면에 상태 색으로 표시(`FurnitureAssemblyZoneView`) — Game 씬에서 `가구용 멀티 드라이버 행동 UI 설치` 실행 필요. 손목 애니메이션·실루엣 렌더링·원격 Host/Client·조립 검증 남음. MD-3·4·6·13 TBD 유지 |
| 마이크·근접 음성 채팅 | 기획 0.2. 코드·배선 구현 완료, 자동 검증 통과(2026-09-17 재검증: EditMode 302/302 · PlayMode 57/57). XZ 원형 10m 감쇠·벽 가림(레이 3개+로우패스)·\|ΔY\| 2.6m 층 차단. Steam Voice 캡처(VC-1) · 기본 오픈 마이크(VC-3) — 2026-09-27 VAD 송신 게이트 제거(소리를 거르지 않고 전부 송신, VAD는 "말하는 중" 표시 전용)·끊김 원인이던 스트리밍 클립 재생을 `OnAudioFilterRead`로 교체 · 가구 제외 마스크(VC-6) 확정, 정책 VC-8·10·13·16 승인, UI 1차(VC-11) 구현. 남은 것은 실기 수동 검증(마이크·Steam 2PC·4인 대역폭·macOS)과 플레이테스트로 정할 수치(VC-4·7·21) |
| 맵·방 프리셋·스폰·작업 대상 가구 | 기획서 v0.4 반영. Type·Count·B/C 비교·작업량 검증, 기존 도면·MAP-15 기록 포함. 미결정은 map-generation.md §12(MG-1~23). Stage1 집은 벽·천장·지붕을 마감하고 천장등 36개를 Hub 에서 조절(MAP-20, 2026-09-28). Stage1 하늘·조명은 밤 + 암흑(천장등 기본 꺼짐·환경광 형체만 보이는 수준·검은 안개 0.025·하늘 반사 0.25(사용자 튜닝값), map-generation §10.1.6 "암흑") — EditMode 33/33 통과, 육안 확인 대기 |

---

## 1. 마일스톤

### 스테이지 시스템 (2026-09-27 규칙 확정)

기준: [스테이지 시스템 기획서](stage-system.md). 보상 금액·치료비는 추후 밸런싱이며, 이번 범위는 현재 방의 정산 이력까지만 저장한다.

| # | 작업 | 상태 |
| --- | --- | --- |
| ST-1 | 2명 이상·게스트 전원 준비 시 시작 | 코드 반영, Unity·Steam 수동 검증 대기 |
| ST-2 | 로비 전원 음성 채널 | 코드 반영, Steam 2PC 청감 검증 대기 |
| ST-3 | 시작 후 초대·방 코드·Steam 로비 중도 참가 차단 | 로비 참가 닫기·진입 검증 코드 반영, Steam 2PC 검증 대기 |
| ST-4 | 로딩·진행 중 무작위 호스트 이전과 상태 복구 | 스냅샷 배포·최신 복제본 선출·NGO 재호스트·주요 상태 복원 코드 반영, C# 빌드 통과. Steam 2~4인 실기 검증·직전 변경 커밋 확인·일시적 상태 전수 복구 대기 → [ADR-0017](../architecture/decisions/ADR-0017-host-migration.md) |
| ST-5 | 드릴카 종료 버튼·실종자 판정·정산 수치와 방 안 이력 | 코드·Steam 로비 정산 이력 게시 반영, Unity·Steam 수동 검증 대기 |
| ST-6 | ESC `스테이지 나가기`·개인 `타이틀로` | 스테이지 나가기 코드 반영, 호스트 개인 이탈은 ST-4 대기 |
| ST-8 | 일반 로비 / 인게임 로비 분리 — 세션 유지, 인게임 로비 ⇄ 스테이지, 상점 이동(방장 전용), 인게임 로비부터 참가 차단 | **코드·설치 도구 반영(2026-09-28)** — Local Host 전 흐름 자동 검증. 에디터 메뉴 `GhostHunter > 인게임 로비 씬 생성` 실행·게스트 동기화·Steam 다인 검증 대기 → [ADR-0018](../architecture/decisions/ADR-0018-persistent-session-in-game-lobby.md) |
| ST-9 | Game → ProtoTypeGame 이름 변경, 스테이지 씬 Stage1 신설·인게임 로비 출발 연동(B안·드릴카 안전 구역·조립 영역·정신력 UI 이전) | **코드·설치 도구 반영(2026-09-28)** — 검증용 복제 프로젝트에서 도구 실행·EditMode·PlayMode(인게임 로비 ⇄ Stage1) 자동 검증. 원본 에디터에서 메뉴 실행·Local Host Stage1 진입 확인 완료. 드릴카 종료·정산·게스트·Steam 다인 검증 대기. 귀신·청소 이전은 미정 → [ADR-0019](../architecture/decisions/ADR-0019-stage1-scene-split.md) |
| ST-7 | 비정상 종료의 공동 아이템 복구 | 임시 공동 상점·선반 코드 반영. Temp 아이템은 사용자 결정에 따라 구매·보유만 가능하므로 현 단계에서 소비/복구 대상 없음. |

### 청소 프로토타입 (2026-09-12 사용자 요청)

| # | 작업 | 상태 |
| --- | --- | --- |
| CL-1 | 퀵슬롯 대걸레 장착·좌클릭 얼룩 제거·B안 랜덤 얼룩·F1 HUD 초기화 | **구현·설치 완료** — Local Host 입력·청소·재배치 확인. 자동 검증은 [구현 문서](../architecture/cleaning-system.md). 얼룩 상세 룰·가구 완료·진행도는 미정 유지, Steam 2PC 검증 대기 |

| # | 마일스톤 | 목표 | 완료 기준 | 상태 |
| --- | --- | --- | --- | --- |
| M0~M6 | 프로토타입 | 던지기 메커닉 검증 | 아래 §3 | **완료** (실기 검증 항목 제외) |
| **MIG** | **아키텍처 정비** | 본 프로젝트 구조로 이관 | §2 전 항목 | **거의 완료** — MIG-11(결정 대기) 외 전부 |
| M7 | 가구 간 물리 + 정리 | 연쇄 충돌이 두 클라이언트에서 자연스럽게 | §3 M7 | 대기 |
| **MAP** | **맵 생성 v0.4** | B안 다층 구조 + Room Preset·Spawn Point·Target Furniture Type/Count | [map-generation.md](../architecture/map-generation.md) §9·§10.2 검증 통과 | **진행 중** — B안 선택·랜덤 가구 1차 설치 완료(MAP-19), Unity Test Runner·Host/Client·정식 작업 판정·체감 검증 대기 (아래 §1.2) |
| M8 | **게임 설계** | 루프·승패·유령 세부 규칙 확정 | [gdd.md §1·§2](gdd.md) TBD 해소 + [ghost-system.md §13](ghost-system.md) 잔여 결정 | **부분 진행 — 귀신 공통 규칙 기획 확정, 유령 P1 + 정신력 코어 구현. 루프·승패 미정** |
| M9 | 실기 검증 | Steam 2PC 전 경로 통과 | [PB-08](../workflow/playbooks.md) 전항 | 대기 |
| M10 | 게임플레이 완성 | 승패 조건까지 동작 | 매치 시작→종료 완주 | 대기 |
| M11 | 폴리싱 & 빌드 | 배포 가능 상태 | Windows·macOS 빌드 + 성능 목표 | 대기 |

> **M8이 병목이다.** 2026-08-30 [귀신 시스템 기획서](ghost-system.md)로 귀신 공통 규칙(상태·정신력 구간·어택 판정·탐지·추격·예외)은 확정됐다. 남은 병목은 **게임 루프와 승패 조건(D-4)** 이며, 그것이 없으면 `Result` 씬과 정식 라운드 구조를 완성할 수 없다.

> **지금 사용자에게 필요한 것** (2026-08-23)
> 1. D-2 / D-3 결정 → MIG-11 마무리
> 2. D-4 / D-11 / D-12 최종 결정 → M8 기획 완료 (D-10 은 귀신 시스템 기획서로 해결됨)
> 3. 필요 시 F1 HUD에서 귀신 P1을 반복 플레이 테스트하고 임시 수치를 조정
> 4. (2026-08-31 추가) D-13 / D-14 결정 → 두더지 스킬 구현 착수 가능
>    ([mole-skill-system.md](mole-skill-system.md) 초안의 MS-1~15)
> 5. (2026-09-04 갱신) **일시정지 메뉴 구현 완료.** 자동 테스트는 통과했다.
>    사용자가 해 줄 일: **① 에디터에서 직접 플레이해 §10.4~§10.5 수동 검증**,
>    **② 선행 검증 D-1**(클라이언트 씬 동기화 모드 — §5 백로그. 참이면 게스트 경로가 무너진다)
>    → [pause-menu.md §10](../architecture/pause-menu.md)
> 6. (과거 기록, 2026-09-05) **맵 생성 기획서 v0.3 반영 + 결정 4건 완료.** D-17·D-18·D-19·D-20 해결 —
>    규모 20×16m ×2층 + 다락 14×10m / `MapScale` 1.0 / 소형 오브젝트 미리 배치 / 라운드별 개별 매치.
>    층별 도면 3장도 실측값으로 반영했다. **MAP-1 그레이박스는 지금 착수할 수 있다.**
>    사용자가 해 줄 일: **① 씬 재생성 승인**(생성 도구 재실행은 `Game` 씬을 새로 만든다),
>    ② D-21 오픈 보이드 처리 방침 → [map-generation.md §12](../architecture/map-generation.md) · 위 §1.2 MAP 보드
> 7. (2026-09-12 현재) **B안 선택·랜덤 가구 1차 코드 완료.** Game 씬에서 B안 가구 랜덤 배치 설치/검증 메뉴를
>    실행한 뒤 Bootstrap Local Host·Host/Client로 확인한다. 전체 씬 재생성은 필요 없다 → [설치 절차 §10.1.3](../architecture/map-generation.md).

### 1.1 M8 귀신·정신력 통합 TODO

> 2026-08-24 전체 검토에서 확인한 후속 작업이다. 현재 P1 코어의 결함과 정식 게임플레이
> 연결 대기 항목을 함께 추적하되, 기획 결정이 필요한 작업은 D-10~D-12 확정 전에 구현하지 않는다.

| # | TODO | 선행 조건 | 상태 |
| --- | --- | --- | --- |
| M8-GS-1a | 귀신 프로토타입의 모의 정신력을 제거하고, 귀신 HUD가 `ISanityTeamService`의 팀 평균 판정값을 그대로 읽게 한다. | — | ✅ 완료 (2026-08-24) |
| M8-GS-1b | 그 팀 평균으로 귀신 상태·이벤트·어택 조건을 **판정**한다. | — (D-10 해결) | ✅ **완료 (2026-08-30)** — 팀 평균 80/60 구간으로 상태 전이, 10초 §7.3 확률 판정, 어택 30~90초, 자연 진정 30초, 강제 진정 10초 → [ghost-prototype.md](../architecture/ghost-prototype.md) |
| M8-GS-1c | [귀신 공통 시스템 원문 0.2](ghost-system.md)를 구현에 반영한다: 활동 시작, 0~30 어택 100%, 일반/고위험 지속시간, 자연 진정으로의 아이템 강제 종료, 10초 수색, 0.2초·1m 타깃 선정과 최단 경로 이동, 활동 중 본체 목격 정신력 감소, 경고·어택 조명. | G-9 고위험 종료·G-17 목격 기준·G-18 탐지 강화 수치, Unity PlayMode 검증 | **부분 구현 — 코드·에셋·실제 청소 진행도·자동 스폰 연결. 중간 .NET 빌드 통과, 최신 빌드는 병행 `GrabController.cs` 오류로 실패. 런타임 검증 대기(2026-09-27)** → [ghost-prototype.md](../architecture/ghost-prototype.md) |
| M8-GS-2 | 9종 초자연현상 중 무엇을 `귀신 이벤트 목격`으로 처리할지 정의하고, 서버 가시 판정에서 `ServerApplyGhostEventWitnessed()`를 호출한다. 단순 근접은 현재 감소 조건이 아니다. | [G-6](ghost-system.md) | ✅ **완료 (2026-08-31)** — 사용자 확정: 종류 불문 목격 시 전부 적용, 감소량 10→**15**. `GhostPrototypeController.ServerCheckPhenomenonWitnessed`(거리 12m·각도 70°·가림)가 현상 발생마다 판정해 `ServerApplyGhostEventWitnessed()` 호출 → [ghost-prototype.md §4](../architecture/ghost-prototype.md) |
| M8-GS-3 | 헤드라이트·드릴 카 안전 구역·시체 목격·정신력 아이템·사망·스테이지 생명주기를 정신력 서버 API에 연결한다. | 관련 시스템 구현 | **부분 — 헤드라이트·드릴카 어둠 노출 연결(2026-09-28, 코드·프리팹, EditMode 신규 테스트 통과, 실기 미검증)** → [headlamp.md](../architecture/headlamp.md). 시체·아이템·스테이지 생명주기 대기 |
| M8-GS-4a | 정신력 20 이하 카메라 테두리 노이즈를 URP Volume 연출로 연결한다. | — | ✅ 완료 (2026-08-24) — **2026-08-31 재검증**: `AssetDatabase.AddObjectToAsset` 누락으로 비네트·필름그레인·색수차가 실제로는 저장되지 않고 있었다(2026-08-24 당시엔 인스펙터에서만 보이다 사라지는 버그). 굴착 스킬 연출 작업 중 발견해 수정 완료 → [mole-skill-system.md §8](mole-skill-system.md) |
| M8-GS-4b | 10 이하 속삭임, 5 이하 숨소리·심장 소리를 실제 SFX 재생으로 연결한다. | 오디오 에셋 | 대기 |
| M8-GS-5 | 정신력 0에서 새 시체 목격 시 값이 변하지 않았는데도 감소 성공으로 보고하는 `SanityState.WitnessCorpse()` 반환값과 F1 메시지를 수정하고 회귀 테스트를 추가한다. | — | 대기 |
| M8-GS-6 | 원격 클라이언트 F1 HUD가 스폰된 귀신을 찾지 못하고 남은 상태 시간을 0초로 표시하는 문제를 수정한다. | — | ✅ **해결 (2026-08-30)** — 재구현에서 귀신 F1 조작을 Host 전용으로 두어(`IGhostDebug.CanControl` = `IsServer`) 원격이 귀신을 조회하지 않는다. 원격은 복제된 `GhostPhase` 로 연출만 재생한다 |
| M8-GS-7 | 귀신 스폰·탐지·추격·공격, 정신력 복제·팀 평균·HUD 갱신, 귀신 이벤트→정신력→HUD 흐름의 PlayMode 통합 테스트를 추가한다. | M8-GS-1~6 | 대기 — 상태 기계·시야 기하는 EditMode 로 검증됨(93건). 스폰·탐지·추격의 PlayMode 통합 테스트가 남음 |
| M8-GS-8 | Steam 2PC에서 귀신·정신력 상태와 개인/팀 HUD 동기화를 검증한다. | M8-GS-7, M9 환경 | 대기 |
| M8-GS-9 | Unity Editor 재시작 후 Local Host 기본 UDP 7777 점유가 재발하는지 확인하고, 재발하면 `UnityTransport` 종료 수명주기를 진단한다. | — | 대기 |
| M8-GS-10 | 스폰된 귀신을 Scene 뷰에서 식별할 수 있도록 전용 레이어와 Gizmo 표식을 둔다. | — | ✅ **완료 (2026-08-30)** — `GhostPrototype` 레이어(10)·파란 Scene Gizmo 추가, 플레이어 카메라 culling mask는 변경하지 않음 |


### 1.2 MAP 맵 생성 v0.4 TODO

> 2026-09-05 v0.3 반영 작업에 **v0.4(2026-09-12 수령)**의 Target Type·Count·조건별 재처리·
> B/C 비교·작업량 검증을 추가한 보드다 → [맵 생성 시스템 기획서](../architecture/map-generation.md).
> **현재 제작 기준은 B안(2026-09-12 사용자 선택, MG-20 해결).** MAP-15의 저장된 B안 실내를
> 기반으로 MAP-19 랜덤 가구를 구현한다. MAP-1은 A안의 기존 그레이박스 기록으로 보존한다.
>
> ⚠️ 전체 `GhostHunter > 프로토타입 게임 생성`은 `Game` 씬을 **처음부터 다시 만든다**(CLAUDE.md §3).
> MAP-1은 기존 두 집을 보존하는 전용 메뉴 `GhostHunter > 맵 v0.3 그레이박스 오른쪽에 추가`로 분리했다.
>
> **추가 도면:** [HousePlanB·C 비교·층별 치수](../architecture/map-generation.md#house-plan-bc)를 참고한다.
> 파일명과 이미지 내부 B/C 제목이 반대다. B안은 `HousePlanC.png`, 30×24m ×2층 + 다락 20×14m다.
> MAP-19 설치 메뉴가 B안 가구 풀·후보·앞마당 시작 위치를 연결한다. 기존 맵·C안은 보존한다.

| # | TODO | 선행 조건 | 상태 |
| --- | --- | --- | --- |
| MAP-16 | **맵 기획서 v0.4 문서 동기화** — Target Furniture Type·Count, 생성 순서·실패 처리·B/C 비교·작업량 검증을 반영하고 GDD·문서 안내를 맞춘다 | 사용자 제공 v0.4 원문 | **완료 (2026-09-12)** — 원문 표 53행·생성 순서·검증 항목 19개 대조, 로컬 링크·이미지 참조·diff 검사 통과. 문서만 수정 |
| MAP-19 | **B안 선택 + 가구 랜덤 배치 첫 구현** — 후보·씬 풀·서버 배치·탐지 대상·설정·B안 시작 위치·설치/검증 메뉴·테스트 | **B안 선택 확정 (2026-09-12, 사용자)** | **1차 설치 완료** — 풀 16개·안전 후보 91개·앞마당 시작 위치를 Game 씬에 저장하고 설치 내부 seed 0~15 검증 통과. C# 빌드 경고 0·오류 0, standalone NUnit 계획 테스트 13/13. Unity Test Runner·Host/Client 미실행 → [설치 절차 §10.1.3](../architecture/map-generation.md) |
| MAP-20 | **Stage1 집 마감·천장등** — 벽을 천장까지(실내 2.8·외벽 3.0m), 문 위 인방, 2층 테두리·다락 지붕, 층별 천장 패널, 계단 구멍 난간, 방·홀 천장등 36개 + Hub(Tab) "조명" 섹션(층·방 스위치·밝기·그림자·해·환경광). 후속: 방 출입구 23곳에 `Door_1.0m`(새 프리팹) 문 | 사용자 요청(2026-09-28) | **씬·코드 반영 (2026-09-28)** — 검증용 복제본 EditMode 320건 중 318 통과(새 `Stage1StructureTests` 6/6, 실패 2건은 손대지 않은 `MoleSkillWiringTests` 소스 검사). 에디터 Play 육안·Host/Client 미확인. 다락 게스트룸 문이 계단 구멍으로 열리는 문제는 사용자 결정 대기. **후속(2026-09-28)** — 밤 하늘 + 사용자 요청 "밝기 최대한 낮춰"로 천장등 0.3·패널 0.15·달 0.01·씬 환경광 밤값(배율 ×1)·하늘 노출 0.006 까지 내렸다(값만 바꿈, 육안 확인 대기) → [map-generation.md §10.1.6](../architecture/map-generation.md) |
| MAP-1 | **그레이박스 제작** — 20×16m 2개 층 + 14×10m 다락을 빈 상자로 세우고 크기·동선만 본다 | ✅ MG-2 · ✅ MG-3 | **1차 생성 완료 / 체감 검증 대기 (2026-09-05)** — `Game/House_01_V03_Graybox_Right`, 방 7+8+6=21. 기존 두 집 오른쪽 실제 외곽 3m. 계단·오픈 보이드는 TBD 표식만 유지 |
| MAP-2 | **B안 다층 게임 배선 통합** — MAP-15의 층별 그룹·슬래브·여백 정리 좌표를 사용. 기존 단층 생성기를 A안으로 다시 만드는 작업은 현재 제작 경로에서 대체 | MAP-15 · ✅ MG-20 | **부분 구현** — 구조 저장 완료, 랜덤 가구·시작 위치는 MAP-19. 귀신·은신처 등 전체 게임 배선은 후속 |
| MAP-3 | **B안 계단 A·B 확정·검증** — 기존 임시 경사 계단 4개를 기반으로 폭·층고·경사·계단참 검증. A안 도면의 폭 2.2m는 B안 최종값이 아님 | MG-7 | **TEMP 구조 저장 / 최종 수치·Play 대기** |
| MAP-4 | **Room Slot 카테고리화** — `RoomSlotAssigner` 가 카테고리별 풀에서 뽑도록. 현재는 침실 전용 단일 풀 | MAP-2 | 대기 |
| MAP-5 | **카테고리별 프리셋 제작** — 동일 카테고리의 House 전체 슬롯 수 이상 필요(2층 침실 4칸만 보면 최소 4종, 현재 3종). B안 여백 정리 실내에 맞추며 최종 제작 수량은 MG-13 | MG-13 · ✅ MG-20 | 대기 |
| MAP-6 | **Spawn Point 시스템** — 후보 타입·크기·허용 풀 + 타입별 수량 선택. 사전 배치 풀에서 선택한 가구만 이동 | ✅ MG-14 · MG-9 | **부분 구현(MAP-19)** — 데이터·계획·서버 코드, B안 Floor 설치 도구. Desk/Sofa/Wall 소품 배선과 기존 프리셋 전환은 대기 |
| MAP-7 | **이전 Work Room 선정 범위 확인** — v0.4에 방 선정 단계·6~8개 방·20~25개 기준이 없음. 별도 방 제한을 유지하는지 확인 | MG-6 | **보류** — 최신 작업량 구현은 MAP-17의 Type·Count 기준 |
| MAP-8 | **이동·운반 경로 검증** — 문·계단·필수 이동 경로 차단 금지, 운반 가능 위치, 모든 Target Furniture의 반출 지점 도달 가능 여부. 실패 시 §8에 맞춰 재선정·재배치 | **MG-11**(판정 방법) · MG-12(재시도 상한) | 대기 |
| MAP-9 | **스페셜 공간 해금** — 다락·지하실 잠금 + 열쇠. 다락 도면에 **비밀 보관실**(2.4×1.8m)이 있다 | MG-15 · MG-17 · MG-18(지하 도면 없음) | 대기 |
| MAP-10 | **라운드별 맵** — 원룸 / 작은 2층집 / 대저택 / 인형공장. **라운드마다 개별 매치**(매치 1 = 맵 1) | ✅ MG-4 · **D-4**(게임 루프) | 대기 |
| MAP-11 | **귀신 AI 층간 이동** — 배회 목적지를 NavMesh 에서 뽑아 모든 층이 후보, 전용 NavMesh 반경 0.25m 로 1.0m 문틀 통과, 층 인식 도착·걸림 복구·수색 배회·경로 방향 문 열기 | MAP-2 · MAP-3 | **구현 (2026-09-28)** — EditMode `GhostNavigationTests` 추가, Stage1 실제 계단 이동은 Unity 플레이 확인 대기 → [ghost-prototype.md §3](../architecture/ghost-prototype.md) |
| MAP-13 | **오픈 보이드** — 2층 갤러리 홀 한가운데가 1층으로 뚫려 있다. 난간·낙하·층간 시야/소리 판정 | D-21 | 대기 — **다층 설계의 핵심** |
| MAP-12 | **굴착 도약 높이 재검증** — 기준이 "2층을 바로 올라갈 정도"인데 현재 4m 는 단층 기준 임시값이다 | MAP-2 | 대기 → [mole-skill-system.md §5.4](mole-skill-system.md) |
| MAP-14 | **HousePlanB·C 도면 컨텍스트 반영** — 파일명/도면 제목 대응, 층별 치수·배치 비교, 미확정 항목과 이미지 인덱스 연결 | 도면 2장 수령 | **완료 (2026-09-05)** — 도면 직접 대조, 관련 링크 11건 유효·diff 검사 통과. 문서 작업이며 규모 변경·씬 생성은 별도 |
| MAP-15 | **B·C 대저택 실내 프로토타입** — 기존 Game 씬에 `House_Prototype_PlanB/C`를 덧붙이고 도면 치수 방·실내 벽·현관·창문·임시 조명·실제 계단 A/B·난간·기존 Furniture 프리팹을 배치. 앞마당·연결 바닥, 상부 계단 개구부·가구/벽/문/계단 통로 검증과 NGO 해시 갱신 포함 | MAP-14 · 기존 `House_01`/원본/그레이박스 보존 | **메뉴 실행·씬 저장 완료 (2026-09-06)** — 원본 도면 방 치수가 만든 여백(층당 36~53%)을 없애는 재설계도 함께 반영해 방+홀 비율 68.5~83.0%. 수동 Play·최종 계단 수치(MG-7)는 대기. B안 채택(MG-20)은 2026-09-12 MAP-19에서 해결 → [map-generation.md §2](../architecture/map-generation.md#house-plan-bc) |
| MAP-17 | **Target Furniture 선정·배치·검증** — Type/Count·방 분산·수량 충족·조건별 재처리 | MG-10 · MG-12 · MG-21~23 · D-14 | **부분 구현(MAP-19)** — 임시 4종 16개 추가 풀·안전 후보 91개·선정·충돌/중복 검사·대상 복제 설치 완료. 정식 작업 판정·반출·보충 정책·Host/Client 검증 대기 |
| MAP-18 | **v0.4 레벨·작업량 플레이 검증** — B안 도주 공간, 2~3인 통행, 가구 운반, 이동 시간·귀신 조우·Type/Count/총량·플레이타임. 필요 시 C안 비교 | 레벨: MAP-15 / 작업량: MAP-19·MG-21 | **대기** — B안 채택 자체는 완료(MG-20). 체감·작업량 검증과 구분 |

### 1.3 MS 두더지 스킬 TODO

> 2026-09-05 [두더지 스킬 기획서 1.0](mole-skill-system.md) 반영 + 사용자 확정 3건에서 나온 작업이다.
> **MS-A~C 는 결정이 끝났고, MS-D·MS-F도 코드 스캐폴드까지 구현됐다.** 작업 시스템(D-14)은
> 여전히 실제 대상 판정을 막고 있으므로 탐지는 `DetectionTargetMarker` 기반으로만 연결했다.

| # | TODO | 선행 조건 | 상태 |
| --- | --- | --- | --- |
| ~~MS-A~~ | ~~`ProjectWiringTests` 의 `Player/Burrow` R키 단언을 T로~~ | ✅ D-22 | ✅ **완료 (2026-09-05)** — `Player_굴착_액션은_T키에_바인딩되어_있다`. E(Interact 충돌)·R(거쳐 간 값)이 남아 있지 않은지도 함께 단언. **선재 red 해소** |
| ~~MS-G~~ | ~~개발 HUD 에서 스킬 수치를 직접 조절~~ | — | ✅ **완료 (2026-09-05)** — Hub `Tab` 에 `두더지 스킬` 섹션. 상태·강제 조작(`IMoleSkillDebug`) + `MoleBurrowSettings` 값 줄(`TuningHud.DrawInline`) |
| ~~MS-B~~ | ~~굴착 중 조작 전부 잠금~~ | ✅ D-24 | ✅ **완료 (2026-09-05)** — `PlayerInputReader.SetSkillInputLocked` 신설. 일시정지 잠금과 독립이고 겹치면 메뉴가 이긴다. `Look`·`Burrow` 만 허용, `CrouchHeld` 는 동결. 시전 시작에 걸고 정상 종료·취소·디스폰 세 경로에서 푼다 → [mole-skill-system.md §5.5.1](mole-skill-system.md) |
| ~~MS-C~~ | ~~감지 상태에서 시전하면 땅속에서도 감지~~ | ✅ D-23 | ✅ **완료 (2026-09-05)** — `BurrowExposureTracker`(순수) + `GhostPrototypeController.EvaluateBurrowExposure`. 매몰 시작 순간에 한 번 판정하고 그 굴착 내내 유지, **탐지·포획 양쪽** 적용. **`TryCatch` 가 굴착을 안 봐서 안전하게 숨은 플레이어가 수색 중 잡히던 틈새도 함께 수정** → [mole-skill-system.md §5.2.1](mole-skill-system.md) |
| MS-D | **공통 스킬 UI** — 우측 상단 원형 게이지(시전 `#78c664` / 쿨타임 `#FFFFFF`, 배경 `#595959` 70%) | ✅ 없음 | **코드 구현 (2026-09-05)** — `MoleSkillHud` + `IMoleSkillStatus`로 탐지·굴착 동시 표시, 아이콘 4종·색·배경·링 감소와 탐지 시전 중 로컬 파란빛 오버레이를 연결했다. `Game` 씬·`Player` 프리팹·아이콘 배선은 정적 확인됐고 수동 Play 검증 대기. ⚠️ 탐지 아이콘 파일명에 `ICON_` 뒤 **공백**이 있다 → [mole-skill-system §6.3](mole-skill-system.md) |
| MS-E | **중복 시전 방지** — §3.4 판정 2를 명시적으로 둘 것인가 | — | **사실상 성립** — 상태 머신이 `Idle` 일 때만 시전을 받고 매몰 중 재입력은 §5.3대로 즉시 종료다. 명시 판정은 굴착 플로우차트가 오면 정리 → [mole-skill-system.md §3.4](mole-skill-system.md) |
| MS-F | **탐지 스킬 전체** — `Player/Detect`(Q), 5초 표시, 쿨타임 10초, 색 구분 표시 | **D-14**(청소·이사 작업 시스템)의 실제 대상 판정 | **부분 구현 (2026-09-05)** — Q 입력·판정 3개·시전/활성/쿨타임·색상별 활성 마커·로컬 표시·시전 파란빛 오버레이를 연결했다. **벽 투시는 없다**(`ZTest LEqual`, 2026-09-05 확정 번복). 손·레이저 포인터 애니메이션은 MS-14, 작업 시스템의 판정은 MS-5로 남고, 수동 Play 검증 대기 |

### 1.4 QS 퀵슬롯(라디얼 휠) TODO

> 2026-09-12 [퀵슬롯 시스템 기획서](quick-slot-system.md) — QS-1~10 사용자 확정 반영, 더미
> 스캐폴드 구현 완료. 인벤토리/아이템 시스템 자체는 여전히 미작성이다.

| # | TODO | 선행 조건 | 상태 |
| --- | --- | --- | --- |
| QS-실제 인벤토리 | 아이템 목록·소지·서버 검증이 있는 진짜 인벤토리 시스템 | — | **미착수** — 별도 기획서·ADR 필요(QS-1) |
| QS-확정 RPC | `QuickSlotWheelUi.Confirm`을 `[Rpc(SendTo.Server)]` 요청으로 확장 | QS-실제 인벤토리 | **미착수** — 지금은 로컬 상태 변경까지만(QS-3) |
| QS-사망 게이팅 | 휠 열림/유지 조건에 생존 판정 추가, 사망 시 선택 취소. 기존 `ISanityTeamService` 조회 경로와 로컬 컨텍스트 확장 비교 | SP-IMPL-1에 통합 | **미착수** — 실제 인벤토리와 독립 진행 → [quick-slot.md §5](../architecture/quick-slot.md) |
| QS-아이콘 | 실제 아이콘 에셋으로 자리표시자(QS-10) 교체 | 에셋 준비 주체 결정 | **미착수** |
| QS-수동 검증 | Bootstrap → F1 → Local → Host 로 Tab 홀드·슬롯 선택·확정 표시 확인 | — | **미수행** |

---

### 1.5 SP 사망 후 관전 TODO

> 2026-09-12 사용자 요청: 사망 시 특수능력 사용 불가 + 맵을 통과하는 자유시점 + 생존 플레이어 시점 관전.
> 규칙은 [관전 시스템 기획서](spectator-system.md), 작업 지시는 [구현 프롬프트](../workflow/spectator-implementation-prompt.md)를 따른다.
> **SP-1~SP-4 확정, SP-IMPL-1~3 코드 구현 완료(2026-09-12).** 승패·시체 생성과 분리해 진행한다.
> Host/Client Play 수동 검증(SP-IMPL-4)이 남아 있다.

| # | TODO | 선행 조건 | 상태 |
| --- | --- | --- | --- |
| SP-DOC | 사망·관전 요구와 기존 코드 차이 기록, 관련 기획서 연결, 구현 프롬프트 작성 | 사용자 요청 | **완료 (2026-09-12, 문서만)** — 추가 링크·코드 경로·diff 검사 통과. 미정 세부 규칙은 §5에 유지 |
| SP-DECIDE | §5 SP-1~SP-4 미결정 사항 사용자 확정 | SP-DOC | **완료 (2026-09-12)** — 제안대로 확정. [spectator-system.md §5](spectator-system.md#5-결정-사항-2026-09-12-사용자-확정) 참고 |
| SP-IMPL-1 | 사망 시 생존 조작·특수능력 차단, 진행 중 상태 정리, 문·가구 서버 요청 생존 검증, 퀵슬롯 사망 게이팅 | SP-DECIDE | **완료 (2026-09-12)** — `PlayerInputReader.SetDeathInputLocked`(메뉴 다음 최우선), `GrabController`/`DoorInteractable` 서버 생존 검사, `FurnitureGrabTarget.ServerForceRelease` 자동 호출, `QuickSlotWheelUi.CanOpen` 게이팅. EditMode 통과, Play 미검증 |
| SP-IMPL-2 | 충돌·중력 없는 로컬 자유 카메라, 수평·수직 이동, 모드 전환 입력 | SP-DECIDE | **완료 (2026-09-12)** — `SpectatorController`(신규) 자유비행. Play 미검증 |
| SP-IMPL-3 | 생존자 1인칭 추종, 상하 시선 동기화, 대상 선택·사망·이탈 처리 | SP-DECIDE | **완료 (2026-09-12)** — `PlayerLook.Pitch`/`PlayerMotor.CameraLocalHeight` 복제 + `SpectatorTargetSelector`. Play 미검증 |
| SP-IMPL-4 | 메뉴·굴착·휠 잠금 충돌, 서버 디버그 부활·리셋·디스폰 복구, 카메라/리스너·멀티플레이 검증 | SP-IMPL-1~3 | **대기 — Host/Client Play 수동 검증 필요** (spectator-system.md §6 AC-1~9 ★ 항목) |

### 1.5.1 DEATH 죽음 시스템 TODO

> [기획서](death-system.md)와 [구현 기록](../architecture/death-system.md)을 따른다.
> 기존 Player 외형·짧은 클릭 밀기·길게 누르기 운반·전멸 Result 사망자 집계는 사용자 확인 사항이다.

| # | 작업 | 상태 |
| --- | --- | --- |
| DEATH-1 | 사망 카메라 연출, 시체 생성·서버 물리·목격·부활/디스폰 정리 | **코드 구현, Unity Play 검증 대기** |
| DEATH-2 | 짧은 좌클릭 밀기, 두 명 이상 길게 눌러 운반 | **코드 구현, Host/Client Play 검증 대기** |
| DEATH-3 | 서버 전멸 확정, 부활 차단, Result 사망자 수·로비 이동 후 세션 정리 | **코드 구현, 씬 전환 Play 검증 대기** |
| DEATH-4 | `DeathSystemFlowTests` 실행 및 Host/Client 카메라·음성·시체 검증 | **코드·컴파일 확인, Unity Play 및 Host/Client 실기 검증 대기** — Result·Lobby 전원 음성 채널 구현 |
| DEATH-5 | 1인당 치료비 금액, 개별 귀신 이벤트 사망 조건, 정식 외형·애니메이션 | **기획·에셋 결정 대기** |

---

### 1.6 FM 가구용 멀티 드라이버 TODO

> 2026-09-12 [가구용 멀티 드라이버 기획서](furniture-multidriver-system.md) 1.0 저장소 반영 +
> MD-1·2·5·9·10·11·12 사용자 확정 + **FM-IMPL-1~3 코드·씬 구현 완료(2026-09-12).**
> 구현 상세는 [architecture/furniture-multidriver.md](../architecture/furniture-multidriver.md).
> FM-IMPL-4(연출)와 수동 검증이 남아 있다.
> **2026-09-13 수정·검증:** 퀵슬롯 장착 연결과 원본 홀더 정리를 수정했다. 실제 Game Local Host
> 입력으로 침대 분해 완료. 원격·조립·연출은 남아 있다.
> **2026-09-13 기획서 1.1:** 우클릭을 끝까지 누르고 있어야 완료·중앙 원형 게이지 HUD 구현. 실제 씬
> 검증 중 발견한 "분해 부품이 풀 보관 위치로 되돌아가 낙하" 버그 수정. EditMode 235개·PlayMode 32개 통과.

| # | TODO | 선행 조건 | 상태 |
| --- | --- | --- | --- |
| FM-DECIDE | MD-1·2·5·9·10·11·12 사용자 확정 | 기획서 1.0 반영 | **완료 (2026-09-12)** — [furniture-multidriver-system.md §8](furniture-multidriver-system.md) |
| FM-IMPL-1 | 분해 가능 가구·부품 SO, 드라이버 장착 경로, 우클릭 액션 신설 | FM-DECIDE | **완료 (2026-09-12)** — `FurnitureDriverCatalog`·`FurnitureDisassemblyRecipe`·`FurnitureDriverSettings`, 퀵슬롯 1번 슬롯, `Player/UseDriver` 입력 액션 |
| FM-IMPL-2 | 분해 요청·행동 시간·중단·서버 실행(가구 제거·부품 배치·내구도 상속) | FM-IMPL-1 | **완료 (2026-09-12)** — `PlayerFurnitureDriverController.RequestDisassembleRpc`, `FurnitureDriverPoolItem` 씬 풀 재배치(ADR-0009 준수) |
| FM-IMPL-3 | 조립 영역 점유 관리, 조립 성립 순수 판정, 실루엣 상태 복제, 서버 조립 실행 | FM-IMPL-2 | **완료 (2026-09-12)** — `FurnitureAssemblyZone` + `FurnitureAssemblyRules`(EditMode 19개 테스트). 실루엣 **시각화는 FM-IMPL-4로 이월** |
| FM-IMPL-4 | 행동 시간 UI(1.1: 중앙 원형 게이지), 실패 흔들림·문구, 손목 애니메이션, 3색 실루엣 렌더링 | FM-IMPL-3 | **진행중 (2026-09-13)** — 기획서 1.1 수정(우클릭 유지·중앙 원형 게이지)과 함께 `FurnitureDriverActionHud`·`UseDriverHeld` 취소 구현, Game `PrototypeUI` 설치·저장. 손목 애니메이션·3색 실루엣 렌더링 미착수 |
| FM-TEST | PlayMode 서버 거절·홀더 정리 자동 테스트 | FM-IMPL-2~3 | **진행중 (2026-09-13)** — `FurnitureDisassemblyFlowTests` 17개(1.1 우클릭 뗌 취소·원형 게이지 진행/실패 표시 3개 추가). 전체 PlayMode 32/32 통과. 부품 위치 버그는 PlayMode로 재현하지 못해 실제 씬으로 확인. 사망·원격·조립 검증 남음 |
| FM-검증 | Host/Client 수동 분해·조립, Steam 2PC 복제 확인 | FM-IMPL-1~4 | **진행중 (2026-09-13)** — 실제 Game Local Host 가상 입력: 침대 분해·내구도 100→95, 1.1 우클릭 유지 완료·뗌 취소·"분해 실패" 게이지, 부품 위치 수정 후 부품이 침대 앞 1.2m 이내에 착지. 사람 수동 플레이·원격·Steam 2PC 미수행 → [검증 상세](../architecture/furniture-multidriver.md#5-검증-상태) |
| FM-레벨배치 | 식탁·선반 2종을 `House_01/PhysicsFurniture`에 실제 배치(맵 v0.4 일반 가구 배치와 연동) | 맵 v0.4 진행 | **미착수** — 현재 라이브 인스턴스 0개라 분해 대상 없음 |

### 1.7 FD 가구 내구도 TODO

> 2026-09-15 사용자 요청으로 [가구 내구도 시스템 기획서](furniture-durability-system.md) 0.1 초안 작성.
> 멀티 드라이버 MD-6(가구 내구도 증감 원인·0일 때 결과)을 이 기획서로 이관했다.
> **같은 날 0.2:** 모든 가구(FD-1)·가구끼리 양쪽 감소(FD-6)·플레이어/귀신 충돌 제외(FD-7)·
> 0이면 사라짐(FD-10, "일단") 확정. **0.3:** 수직 상대 속도·초기값·F1/R 복구 승인, 코드 구현·Unity 검증 대기.

| # | TODO | 선행 조건 | 상태 |
| --- | --- | --- | --- |
| FD-DECIDE-구조 | FD-1·3·6·7·10 사용자 확정 — 대상 범위, 충돌 속도 정의, 가구끼리·플레이어 충돌, **0 도달 결과** | 기획서 0.1 | **완료 (2026-09-15)** — FD-1·3·6·7·10 확정 |
| FD-DECIDE-수치 | FD-2·4·5·8·12 — 공식·`v_min`·`k`·무게 계수·상한·운반 중 적용·판정 창/보호 시간 | 기획서 0.1 | **초기값 승인 (2026-09-15)** — 제안값으로 시작, 플레이테스트 튜닝 대기 |
| FD-IMPL-1 | 모든 가구에 서버 권위 내구도 값, 충돌 속도 판정·감소량 순수 함수, 배치 직후 보호 시간, SO 설정 | FD-DECIDE-구조 | **진행중 (2026-09-15)** — 코드·테스트 추가, C# 빌드 통과. Unity 실행·Host/Client 검증 대기 |
| FD-IMPL-2 | 0 도달 처리(FD-10), F1 개발 HUD 확인·초기화 | FD-IMPL-1 | **진행중 (2026-09-15)** — 코드·테스트 추가, C# 빌드 통과. Unity 실행·Host/Client 검증 대기 |
| FD-TEST | 공식 EditMode, 충돌·보호 시간·복제 PlayMode(기획서 §6 AC-1~12) | FD-IMPL-1 | **진행중 (2026-09-15)** — 코드·테스트 추가, C# 빌드 통과. Unity 실행·Host/Client 검증 대기 |

## 2. 현재 스프린트 — MIG: 아키텍처 정비

2026-08-19 결정으로 씬 아키텍처·어셈블리 구조·의존성 획득 방식이 바뀌었다.
관련 ADR: [0003](../architecture/decisions/ADR-0003-service-locator.md)
· [0004](../architecture/decisions/ADR-0004-multi-scene-additive.md)
· [0005](../architecture/decisions/ADR-0005-unitask-async.md)
· [0007](../architecture/decisions/ADR-0007-flat-assets-layout.md)
· [0009](../architecture/decisions/ADR-0009-scene-placed-level-objects.md)

### 2.1 순서 (의존 관계)

```
[완료] MIG-0 문서 · MIG-9 Steamworks · MIG-4 UniTask · MIG-8 RPC · MIG-1 Core · MIG-2 씬 재편 · MIG-3 리그 흡수
                                    │
                                    ▼
                                      [완료] MIG-5 asmdef 분리 ──▶ [완료] MIG-7 테스트
                                                   │
                       [완료] MIG-6 프리팹화 ──────┘ (독립이었음)
```

> **D-1 해결(2026-08-20).** `Bootstrap` 씬에는 Facepunch 와 UTP 를 **둘 다** 둔다.
> 릴리스 보호는 `TransportModeBuildGuard` 가 맡는다 → [ADR-0011](../architecture/decisions/ADR-0011-local-transport-path.md)

**MIG-4(UniTask)를 MIG-1보다 먼저 했다.** Core 인프라(`SceneFlowController`·`Services`)를 새로 쓸 때
이미 UniTask가 있어야 두 번 쓰지 않는다.

### 2.2 태스크

| # | 태스크 | 산출물 | 선행 | 상태 |
| --- | --- | --- | --- | --- |
| MIG-0 | 문서 트리 재편 + ADR 작성 | `docs/**` | — | **완료 (2026-08-19)** |
| MIG-9 | **`Steamworks` 격리** — UI 3파일에서 제거 | `Core/Steam/ISteamLobbyService.cs` 외 | — | **완료 (2026-08-19)** |
| MIG-4 | **UniTask 전환** — `async void` 6건, 코루틴 2건 | `manifest.json`, asmdef + 6파일 | — | **완료 (2026-08-20)** — 컴파일 검증됨 |
| MIG-1 | Core 인프라 — `Services`, `SceneInstaller`, `ISceneFlow`, `SceneReference`/`SceneNameSO`, `SceneFlowController` | `Scripts/{Core,Data,Systems}`, `Settings/Scenes/SceneNameSO.asset` | MIG-4 ✅ | **완료 (2026-08-20)** — MIG-2/3 플레이 경로에서 런타임 검증됨 |
| MIG-2 | **씬 재편** — `Bootstrap`·`Result` 신규, `MainMenu`→`Title`, `Prototype`→`Game`, 호출부를 `ISceneFlow`로 이관 | `Assets/Scenes/**` | MIG-1 ✅ | **완료 (2026-08-20)** — 플레이 검증됨 |
| MIG-3 | `NetworkRig` 프리팹 언팩 + `NetworkRigBootstrap` 제거, `static Instance` 6건 → `Services` | `Bootstrap.unity`, `ConnectionManager` 외 | MIG-2 ✅ | **완료 (2026-08-20)** — Local Host 플레이 검증됨 |
| MIG-5 | **asmdef 레이어 분리** + 폴더 이동 | asmdef 8개 | MIG-3 ✅ | **완료 (2026-08-20)** — 8개 DLL 컴파일·Bootstrap 배선 검증됨 |
| MIG-6 | 가구·문 프리팹화 + 생성 도구 전환 | `Assets/Prefabs/{Furniture,Map}/**`, `HousePrototypeBuilder` | MIG-2 ✅ | **완료 (2026-08-31)** — 코드 2026-08-21, 실제 굽기는 커밋 `015f874` (가구 33종·문 3종, `Game.unity` 에 `PrefabInstance` 108건) |
| MIG-7 | 테스트 어셈블리 + 스모크 테스트 이관 | `Assets/Tests/**` | MIG-5 ✅ | **완료 (2026-08-21)** — EditMode 21 통과·3 건너뜀, PlayMode 12 통과 |
| MIG-8 | 레거시 RPC 속성 → `[Rpc(SendTo.…)]` | `GrabController` 3 · `FurnitureLauncher` 1 · `PlayerNetworkSpawn` 1 | — | **완료 (2026-08-20)** — 컴파일 검증됨 |
| MIG-11 | 로비 정책 반영 — `gh_game` 키, 가시성, 난입, 접속 승인 검증 | `SteamLobbyManager`, `ConnectionManager` | [ADR-0012](../architecture/decisions/ADR-0012-room-code-and-lobby-visibility.md) 확정 | **부분 완료 (2026-08-21)** — `gh_game` 키·스폰 점유 검사 완료. 가시성·난입·접속 승인은 **D-2/D-3 결정 대기** |
| MIG-12 | 브랜치 규약 적용 — `Feature/Prototype` → kebab-case | git | — | **완료 (2026-08-21)** — 로컬 완료, 원격 반영은 push 필요 |

### 2.3 결정 대기 (에이전트가 진행할 수 없는 것)

| # | 항목 | 막히는 태스크 |
| --- | --- | --- |
| D-2 | 로비 가시성 (Public vs FriendsOnly) | [ADR-0012](../architecture/decisions/ADR-0012-room-code-and-lobby-visibility.md) §3 → MIG-11 |
| D-3 | 난입 허용 여부 | [ADR-0012](../architecture/decisions/ADR-0012-room-code-and-lobby-visibility.md) §4 → MIG-11 |
| D-4 | 게임 루프·승패 조건 | [gdd.md §2](gdd.md) → M8 전체 |
| ~~D-5~~ | ~~유령의 정체 (플레이어/AI)~~ | ✅ 해결 — 적대 AI 시스템 (2026-08-23) |
| ~~D-9~~ | ~~정신력의 관리 단위·증감 조건·임계값~~ | ✅ 해결 — 시작 100%, 개인·팀 평균·증감·디버프 확정·코어 구현 → [sanity-system.md](sanity-system.md) |
| D-10 | 어택 타임의 발동·종료 세부 규칙 | **부분 해결** — 원문 0.2의 정신력별 확률·일반 60초·고위험 90초·자연 진정 30초는 반영. 청소 조건 결합(G-1), 조기 종료 회복 기준·0~30 종료 충돌(G-9), 판정 주기(G-20) 대기 → [ghost-system.md](ghost-system.md) |
| D-11 | 시야 거리·각도, 다중 플레이어 타깃 선정·변경 규칙 | **타깃 선정 해결** — 원문 0.2의 0.2초·최근접·1m 변경 기준 확정. 원뿔 시야 거리·각도(G-3)는 대기. 이동 소리 탐지는 과거 저장소판 규칙 → [ghost-system.md §8·§9](ghost-system.md) |
| D-12 | 청소·이사 작업 중 귀신 출현·어택 처리 | [ghost-system.md §13 G-11](ghost-system.md) → M8 예외 처리 |
| D-13 | 두 스킬이 재사용 대기를 **공유하는지 독립인지** (MS-3 잔여) | **수치는 둘 다 10초로 확정** (탐지=플로우차트 / 굴착=사용자 2026-09-05). 수치가 같아 실질 차이는 "굴착 쿨타임 동안 탐지도 막히는가" 하나다 → [mole-skill-system.md §3.3](mole-skill-system.md) |
| ~~D-22~~ | ~~굴착의 최종 입력 키 (MS-16)~~ | ✅ **해결 (2026-09-05)** — **T.** 기획서의 E는 `Interact`(문 여닫기) 충돌로 채택하지 않는다. 바인딩은 이미 T이므로 **테스트의 R 단언만 고치면 red 해소** → [mole-skill-system.md §3.5](mole-skill-system.md) |
| ~~D-23~~ | ~~굴착 은신이 깨지는 예외 (MS-17)~~ | ✅ **해결 (2026-09-05)** — **채택.** 귀신에게 이미 감지된 상태에서 시전하면 땅속에서도 감지된다. 굴착은 "들키기 전에 미리 숨는" 스킬이 된다 → [mole-skill-system.md §5.2.1](mole-skill-system.md) |
| ~~D-24~~ | ~~굴착 중 조작 제한 범위 (MS-18)~~ | ✅ **해결 (2026-09-05)** — **전부 제한.** 시야 회전과 스킬 키(T) 재입력만 허용 → [mole-skill-system.md §5.5.1](mole-skill-system.md) |
| D-14 | **청소·이사 작업 시스템** — 대걸레·좌클릭·랜덤 얼룩·HUD 초기화는 CL-1로 구현. **정식 얼룩 규칙·개별 가구 대상·반출 완료·진행도는 미정 유지**(MG-22) → [cleaning-system.md](cleaning-system.md) | 얼룩 탐지 마커는 연결됨. 정식 작업 판정(MS-5), 귀신 40% 트리거(G-1), 게임 루프(D-4), MAP-17 대기 |
| ~~D-15~~ | ~~일시정지 메뉴·연결 끊김 처리의 미결정 13건~~ | **대부분 해결 (2026-09-04, 사용자 확정 11건)** — 전부 잠금·경고 없음·해소안 A·전원 종료·게스트 로비 유지·설정 stub·종료 확인 대화상자·끊김 모달+확인 버튼·문구 통일·`Result` 미경유·호스트 종료 동일 정리 → [pause-menu-system.md §9](pause-menu-system.md) |
| ~~D-16~~ | ~~일시정지 메뉴 잔여 4건 (PM-10·12·14·15)~~ | ✅ 해결 (2026-09-04) — 메뉴 표시 없음 · **홀드 중 열면 발사** · `Title` 로비 복귀 진입점 · 열기 제한 없음 → [pause-menu-system.md §9](pause-menu-system.md) |
| ~~D-17~~ | ~~맵 규모~~ | ✅ **B안으로 갱신 (2026-09-12)** — **30×24m ×2층 + 다락 20×14m**, 기존 여백 정리 실내 사용. A안 20×16m·슬롯 4.2×4.0m 결정은 과거 기록으로 보존 → [MG-20](../architecture/map-generation.md) |
| ~~D-18~~ | ~~v0.3 층별 도면 3장~~ | ✅ **해결 (2026-09-05)** — `docs/images/house1~3floor.png` 수령, 층별 실측값을 [map-generation.md §2](../architecture/map-generation.md) 에 표로 반영 |
| ~~D-19~~ | ~~소형 오브젝트 생성 방식~~ | ✅ **해결 (2026-09-05)** — **후보 지점에 미리 배치.** 런타임 스폰 없음 → [ADR-0009](../architecture/decisions/ADR-0009-scene-placed-level-objects.md) 유지 |
| ~~D-20~~ | ~~라운드 구성~~ | ✅ **해결 (2026-09-05)** — **라운드마다 개별 매치.** 매치 하나 = 맵 하나 |
| D-21 | **오픈 보이드 처리** — 2층 갤러리 홀 한가운데가 1층 중앙 홀로 뚫려 있다. 던진 가구가 층을 넘어 떨어지고 시야·소리도 층을 넘는다 | [MG-16](../architecture/map-generation.md) → MAP-2·MAP-11 |

---

## 3. 완료된 마일스톤 (프로토타입)

<details>
<summary>M0 — 프로젝트 정지 작업</summary>

- [x] `Assets/Scripts/` 하위 폴더 생성
- [x] `GhostHunter.Runtime` / `GhostHunter.Editor` asmdef 생성 → **MIG-5에서 8개로 분리 완료**
- [x] Project Settings에 레이어 추가: `Player`, `Furniture`
- [x] 바닥·벽·스폰 포인트 배치
- [x] `Assets/Scripts/Temp.cs` 삭제
- [x] 프로젝트 설정: Company Name `GhostHunter`
- [x] 씬 분리 — MIG-2에서 `Bootstrap`/`Title`/`Lobby`/`Game`/`Result` 로 재편 완료
</details>

<details>
<summary>M1 — 1인칭 이동 (싱글)</summary>

- [x] `PlayerMoveSettings` ScriptableObject
- [x] `PlayerInputReader` — Input System 액션 바인딩
- [x] `PlayerLook` — 요/피치, 피치 클램프, 커서 잠금 + Esc 토글
- [x] `PlayerMotor` — CharacterController 이동/중력/점프 (`Update`)
- [x] `Player` 프리팹 조립

→ [../architecture/player-controller.md](../architecture/player-controller.md)
</details>

<details>
<summary>M2 — 멀티플레이 접속 (UTP 로컬)</summary>

- [x] NGO 2.13.1 + UTP 2.7.3
- [x] `NetworkManager` 세팅 — `Bootstrap/NetworkRig` 씬 오브젝트
- [x] `ConnectionManager` — Host/Join, 트랜스포트 스위칭
- [x] 임시 접속 UI — `ConnectionHud` (F1)
- [x] `ClientNetworkTransform` (소유자 권위)
- [x] `Player` 프리팹에 `NetworkObject` + 스폰 등록
- [x] 로컬/원격 구분 — 카메라·입력은 소유자만 활성
- [ ] **실제 2인 접속 확인** (빌드 + 에디터, 127.0.0.1) → M9
</details>

<details>
<summary>M3 — Steam 연결</summary>

- [x] FacepunchTransport 임베드 + 패치 5건 → [ADR-0006](../architecture/decisions/ADR-0006-facepunch-transport-embed.md)
- [x] `steam_appid.txt` (480) 배치
- [x] `SteamLobbyManager` — Init / RunCallbacks / Shutdown
- [x] 로비 생성·6자리 코드 참가·친구 초대 콜백
- [x] 로비 4인 확장, 준비/시작 흐름
- [x] 빌드 지문 검사 (`gh_net_fingerprint`)
- [x] macOS / Apple Silicon 네이티브 지원
- [ ] **실제 Steam 접속 확인** — PC 2대 또는 Steam 계정 2개 필요 → M9
- [ ] 트랜스포트 스위치 실기 확인 → M9
</details>

<details>
<summary>M4 — 가구 + 타겟팅</summary>

- [x] House_01 맵 가구 25개를 씬 배치 `NetworkObject`로 전환
- [x] 클라이언트 kinematic 처리
- [x] `FurnitureResetter` — 호스트가 `R`로 초기 위치 복구
- [x] `Outline` 셰이더 + `FurnitureOutline`
- [x] `FurnitureTargeter` — 카메라 레이캐스트 타겟팅
- [x] `CrosshairUI`
- [x] 프리팹 에셋화 → **MIG-6** (가구 33종 · 문 3종)

→ [../architecture/furniture-physics.md](../architecture/furniture-physics.md)
</details>

<details>
<summary>M5 — 던지기 (1인) · M6 — 2인 흡착</summary>

- [x] `FurnitureThrowSettings` ScriptableObject
- [x] `FurnitureGrabTarget` — 홀더 슬롯, 상태 NetworkVariable, 서버 검증
- [x] `GrabController` — 홀드 입력, Grab/Release/UpdateAim RPC
- [x] `FurnitureHoverMotor` — 2인 동시 입력에서만 스프링 부양
- [x] `FurnitureLauncher` — 속도 변화 + 20°~70° 발사각 보정
- [x] `Launched` 상태와 재흡착 잠금
- [x] `ChargeGaugeUI`
- [x] 홀더 2슬롯, 목표점 중점, 방향 평균, heavy 배수, 해제 정책 토글
- [x] 홀더 상태별 윤곽선 색
- [ ] **원격 접속에서 파라미터 재튜닝** → M9 이후. 현재 값은 로컬 기준

자동 런타임 스모크 테스트에서 Local Host, 1인/2인 경로를 확인했다.
**손맛 최종 판정은 원격 수동 플레이테스트가 필요하다** → [ADR-0010](../architecture/decisions/ADR-0010-server-authoritative-furniture-physics.md)
</details>

<details>
<summary>맵 생성 (M4 병행)</summary>

- [x] `House_01` 생성 도구 + 검증 (`HousePrototypeBuilder`)
- [x] 문 5개 + `DoorInteractable` (E 상호작용)
- [x] 방 프리셋 A·B·C + `RoomSlotAssigner` 중복 없는 2개 선택
- [x] 도면 배율 비교용 집 (`House_01_OriginalScale_Right`)
- [ ] 콘텐츠 배치 시스템 (Content Spawn Point) — 미구현

→ [../architecture/map-generation.md](../architecture/map-generation.md)
</details>

---

## 4. M7 — 가구 간 물리 + 정리

> 완료 기준: 던진 가구가 다른 가구를 쳐서 연쇄로 밀려나는 게 두 클라이언트에서 자연스럽게 보인다.

- [ ] 가구 여러 개 배치 후 연쇄 충돌 확인
- [ ] Continuous Dynamic 충돌 검증 (벽 관통 없는지)
- [ ] 네트워크 대역폭 확인 (Network Profiler)
- [ ] 튜닝 결과를 [gdd.md §7](gdd.md) · [throw-system](../architecture/throw-system.md) · [furniture-physics](../architecture/furniture-physics.md) 수치에 반영

---

## 5. 백로그

| 태스크 | 이유 | 우선순위 |
| --- | --- | --- |
| **클라이언트 씬 동기화 모드 확인** — 코드가 `SetClientSynchronizationMode` 를 호출하지 않아 NGO 기본값 `Single` 로 동작한다. [networking.md §3.5](../architecture/networking.md)의 "Additive" 서술과 어긋나고, Single 이면 게스트 동기화 시 **`Bootstrap` 이 언로드**될 수 있다 | 게스트의 서비스·씬 전환 전부가 여기에 걸린다. Local 2인(UTP)으로 재현 가능 → [pause-menu.md §5.5·§10.1](../architecture/pause-menu.md) | **높음 — 일시정지 메뉴 선행** |
| ~~일시정지 메뉴 · 연결 끊김 처리 구현~~ | — | ✅ **완료 (2026-09-04)** — 수동 검증만 남음 → [pause-menu.md §10](../architecture/pause-menu.md) |
| Steam App ID 발급 후 480 교체 | 480은 개발 전용 공용 ID | 출시 전 필수 |
| `Assets/TutorialInfo/`, `Readme.asset`, `SampleScene` 정리 | Unity 템플릿 잔재 | 낮음 |
| 빌드 자동화 스크립트 (`Scripts/Editor/BuildPipeline`) | 반복 빌드 비용 절감 | 낮음 |
| CI (batchmode 테스트) | MIG-7 이후 | 낮음 |
| Steam 업적·통계 연동 | 출시 요건 검토 후 | 미정 |
| 오디오 시스템 | 설계 TBD | 미정 |

---

## 6. 완료 이력

[roadmap-history.md](roadmap-history.md)로 옮겼다(2026-09-28). 태스크를 완료하면 그 문서 표 **맨 위**에 한 줄 추가한다.

---

최종 갱신: 2026-09-28 (§6 완료 이력·이전 갱신 기록을 [roadmap-history.md](roadmap-history.md)로 분리. 직전: §0 에디터 설치·생성 도구 삭제 — ADR-0020.)

### 근접 음성 (2026-09-17 사용자 요청)

| # | 작업 | 상태 |
| --- | --- | --- |
| VC-IMPL-1 | Steam Voice 근접 음성·VAD·서버 중계·HUD·가짜 마이크·검증 | 진행중 |
