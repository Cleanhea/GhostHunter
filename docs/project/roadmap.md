# 로드맵 & 태스크 보드

> 에이전트는 작업 시작 시 여기서 대상 태스크를 확인하고, 완료 시 상태를 갱신한다.
> 상태: `대기` → `진행중` → `완료` / `보류`

---

## 1. 마일스톤

| # | 마일스톤 | 목표 | 완료 기준 | 상태 |
| --- | --- | --- | --- | --- |
| M0~M6 | 프로토타입 | 던지기 메커닉 검증 | 아래 §3 | **완료** (실기 검증 항목 제외) |
| **MIG** | **아키텍처 정비** | 본 프로젝트 구조로 이관 | §2 전 항목 | **거의 완료** — MIG-11(결정 대기)·MIG-10(버전 고정) 외 전부 |
| M7 | 가구 간 물리 + 정리 | 연쇄 충돌이 두 클라이언트에서 자연스럽게 | §3 M7 | 대기 |
| **MAP** | **맵 생성 v0.4** | B안 다층 구조 + Room Preset·Spawn Point·Target Furniture Type/Count | [map-generation.md](../architecture/map-generation.md) §9·§10.2 검증 통과 | **진행 중** — B안 선택·랜덤 가구 1차 설치 완료(MAP-19), Unity Test Runner·Host/Client·정식 작업 판정·체감 검증 대기 (아래 §1.2) |
| M8 | **게임 설계** | 루프·승패·유령 세부 규칙 확정 | [gdd.md §1·§2](gdd.md) TBD 해소 + [ghost-system.md §13](ghost-system.md) 잔여 결정 | **부분 진행 — 귀신 공통 규칙 기획 확정, 유령 P1 + 정신력 코어 구현. 루프·승패 미정** |
| M9 | 실기 검증 | Steam 2PC 전 경로 통과 | [PB-08](../workflow/playbooks.md) 전항 | 대기 |
| M10 | 게임플레이 완성 | 승패 조건까지 동작 | 매치 시작→종료 완주 | 대기 |
| M11 | 폴리싱 & 빌드 | 배포 가능 상태 | Windows·macOS 빌드 + 성능 목표 | 대기 |

> **M8이 병목이다.** 2026-08-30 [귀신 시스템 기획서](ghost-system.md)로 귀신 공통 규칙(상태·정신력 구간·어택 판정·탐지·추격·예외)은 확정됐다. 남은 병목은 **게임 루프와 승패 조건(D-4)** 이며, 그것이 없으면 `Result` 씬과 정식 라운드 구조를 완성할 수 없다.

> **지금 사용자에게 필요한 것** (2026-08-23)
> 1. D-2 / D-3 결정 → MIG-11 마무리
> 2. `Packages/manifest.json` 의 Unity MCP 버전 고정 승인 → MIG-10 마무리
> 3. D-4 / D-11 / D-12 최종 결정 → M8 기획 완료 (D-10 은 귀신 시스템 기획서로 해결됨)
> 4. 필요 시 F1 HUD에서 귀신 P1을 반복 플레이 테스트하고 임시 수치를 조정
> 5. (2026-08-31 추가) D-13 / D-14 결정 → 두더지 스킬 구현 착수 가능
>    ([mole-skill-system.md](mole-skill-system.md) 초안의 MS-1~15)
> 6. (2026-09-04 갱신) **일시정지 메뉴 구현 완료.** 자동 테스트는 통과했다.
>    사용자가 해 줄 일: **① 에디터에서 직접 플레이해 §10.4~§10.5 수동 검증**,
>    **② 선행 검증 D-1**(클라이언트 씬 동기화 모드 — §5 백로그. 참이면 게스트 경로가 무너진다)
>    → [pause-menu.md §10](../architecture/pause-menu.md)
> 7. (과거 기록, 2026-09-05) **맵 생성 기획서 v0.3 반영 + 결정 4건 완료.** D-17·D-18·D-19·D-20 해결 —
>    규모 20×16m ×2층 + 다락 14×10m / `MapScale` 1.0 / 소형 오브젝트 미리 배치 / 라운드별 개별 매치.
>    층별 도면 3장도 실측값으로 반영했다. **MAP-1 그레이박스는 지금 착수할 수 있다.**
>    사용자가 해 줄 일: **① 씬 재생성 승인**(생성 도구 재실행은 `Game` 씬을 새로 만든다),
>    ② D-21 오픈 보이드 처리 방침 → [map-generation.md §12](../architecture/map-generation.md) · 위 §1.2 MAP 보드
> 8. (2026-09-12 현재) **B안 선택·랜덤 가구 1차 코드 완료.** Game 씬에서 B안 가구 랜덤 배치 설치/검증 메뉴를
>    실행한 뒤 Bootstrap Local Host·Host/Client로 확인한다. 전체 씬 재생성은 필요 없다 → [설치 절차 §10.1.3](../architecture/map-generation.md).

### 1.1 M8 귀신·정신력 통합 TODO

> 2026-08-24 전체 검토에서 확인한 후속 작업이다. 현재 P1 코어의 결함과 정식 게임플레이
> 연결 대기 항목을 함께 추적하되, 기획 결정이 필요한 작업은 D-10~D-12 확정 전에 구현하지 않는다.

| # | TODO | 선행 조건 | 상태 |
| --- | --- | --- | --- |
| M8-GS-1a | 귀신 프로토타입의 모의 정신력을 제거하고, 귀신 HUD가 `ISanityTeamService`의 팀 평균 판정값을 그대로 읽게 한다. | — | ✅ 완료 (2026-08-24) |
| M8-GS-1b | 그 팀 평균으로 귀신 상태·이벤트·어택 조건을 **판정**한다. | — (D-10 해결) | ✅ **완료 (2026-08-30)** — 팀 평균 80/60 구간으로 상태 전이, 10초 §7.3 확률 판정, 어택 30~90초, 자연 진정 30초, 강제 진정 10초 → [ghost-prototype.md](../architecture/ghost-prototype.md) |
| M8-GS-2 | 9종 초자연현상 중 무엇을 `귀신 이벤트 목격`으로 처리할지 정의하고, 서버 가시 판정에서 `ServerApplyGhostEventWitnessed()`를 호출한다. 단순 근접은 현재 감소 조건이 아니다. | [G-6](ghost-system.md) | ✅ **완료 (2026-08-31)** — 사용자 확정: 종류 불문 목격 시 전부 적용, 감소량 10→**15**. `GhostPrototypeController.ServerCheckPhenomenonWitnessed`(거리 12m·각도 70°·가림)가 현상 발생마다 판정해 `ServerApplyGhostEventWitnessed()` 호출 → [ghost-prototype.md §4](../architecture/ghost-prototype.md) |
| M8-GS-3 | 헤드라이트·드릴 카 안전 구역·시체 목격·정신력 아이템·사망·스테이지 생명주기를 정신력 서버 API에 연결한다. | 관련 시스템 구현 | 대기 |
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
> ⚠️ 전체 `GhostHunter > 프로토타입 게임 생성`은 `Game` 씬을 **처음부터 다시 만든다**(CLAUDE.md §6).
> MAP-1은 기존 두 집을 보존하는 전용 메뉴 `GhostHunter > 맵 v0.3 그레이박스 오른쪽에 추가`로 분리했다.
>
> **추가 도면:** [HousePlanB·C 비교·층별 치수](../architecture/map-generation.md#house-plan-bc)를 참고한다.
> 파일명과 이미지 내부 B/C 제목이 반대다. B안은 `HousePlanC.png`, 30×24m ×2층 + 다락 20×14m다.
> MAP-19 설치 메뉴가 B안 가구 풀·후보·앞마당 시작 위치를 연결한다. 기존 맵·C안은 보존한다.

| # | TODO | 선행 조건 | 상태 |
| --- | --- | --- | --- |
| MAP-16 | **맵 기획서 v0.4 문서 동기화** — Target Furniture Type·Count, 생성 순서·실패 처리·B/C 비교·작업량 검증을 반영하고 GDD·문서 안내를 맞춘다 | 사용자 제공 v0.4 원문 | **완료 (2026-09-12)** — 원문 표 53행·생성 순서·검증 항목 19개 대조, 로컬 링크·이미지 참조·diff 검사 통과. 문서만 수정 |
| MAP-19 | **B안 선택 + 가구 랜덤 배치 첫 구현** — 후보·씬 풀·서버 배치·탐지 대상·설정·B안 시작 위치·설치/검증 메뉴·테스트 | **B안 선택 확정 (2026-09-12, 사용자)** | **1차 설치 완료** — 풀 16개·안전 후보 91개·앞마당 시작 위치를 Game 씬에 저장하고 설치 내부 seed 0~15 검증 통과. C# 빌드 경고 0·오류 0, standalone NUnit 계획 테스트 13/13. Unity Test Runner·Host/Client 미실행 → [설치 절차 §10.1.3](../architecture/map-generation.md) |
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
| MAP-11 | **귀신 AI 층간 이동** — 지금 귀신은 집 내부 상자 하나의 X/Z 안에서만 움직인다(NavMesh 없음, Y 미고려). 2~3층이 되면 계단을 오르내리지 못한다 | MAP-2 · MAP-3 | 대기 — **다층 맵의 선행 조건** → [ghost-prototype.md](../architecture/ghost-prototype.md) |
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
> **관전 구현은 미착수다.** 승패·시체 생성과 분리해 진행하며, 미정 세부 규칙은 기획서 §5에서 확인한다.

| # | TODO | 선행 조건 | 상태 |
| --- | --- | --- | --- |
| SP-DOC | 사망·관전 요구와 기존 코드 차이 기록, 관련 기획서 연결, 구현 프롬프트 작성 | 사용자 요청 | **완료 (2026-09-12, 문서만)** — 추가 링크·코드 경로·diff 검사 통과. 미정 세부 규칙은 §5에 유지 |
| SP-IMPL-1 | 사망 시 생존 조작·특수능력 차단, 진행 중 상태 정리, 문·가구 서버 요청 생존 검증, 퀵슬롯 사망 게이팅 | §5 SP-2 | 대기 |
| SP-IMPL-2 | 충돌·중력 없는 로컬 자유 카메라, 수평·수직 이동, 모드 전환 입력 | §5 SP-1·SP-3·SP-4 | 대기 |
| SP-IMPL-3 | 생존자 1인칭 추종, 상하 시선 동기화, 대상 선택·사망·이탈 처리 | §5 SP-1·SP-4 | 대기 |
| SP-IMPL-4 | 메뉴·굴착·휠 잠금 충돌, 서버 디버그 부활·리셋·디스폰 복구, 카메라/리스너·멀티플레이 검증 | SP-IMPL-1~3 | 대기 |

## 2. 현재 스프린트 — MIG: 아키텍처 정비

2026-08-19 결정으로 씬 아키텍처·어셈블리 구조·의존성 획득 방식이 바뀌었다.
관련 ADR: [0003](../architecture/decisions/ADR-0003-service-locator.md)
· [0004](../architecture/decisions/ADR-0004-multi-scene-additive.md)
· [0005](../architecture/decisions/ADR-0005-unitask-async.md)
· [0007](../architecture/decisions/ADR-0007-flat-assets-layout.md)
· [0009](../architecture/decisions/ADR-0009-scene-placed-level-objects.md)

### 2.1 순서 (의존 관계)

```
[완료] MIG-0 문서 · MIG-9 Steamworks · MIG-10 MCP · MIG-4 UniTask · MIG-8 RPC · MIG-1 Core · MIG-2 씬 재편 · MIG-3 리그 흡수
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
| MIG-10 | Unity MCP 연결 | `.mcp.json` | — | **완료 (2026-08-20)** — 버전 고정만 남음 |
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
| ~~D-10~~ | ~~어택 타임의 수치·발동·종료·안전 규칙~~ | ✅ 해결 — 팀 평균 80/60/30 구간, 10초 주기 확률 판정, 어택 30~90초, 자연 진정 30초, 강제 진정 10초 → [ghost-system.md](ghost-system.md). 활동도(0~100) 존치 여부만 G-5로 남음 |
| D-11 | 시야 거리·각도, 달리기·걷기 소리 탐지 거리, 다중 플레이어 타깃 선정·변경 규칙 | 탐지 **방식**은 확정(원뿔 시야 + 이동 소리, 웅크리기 무음) / **수치 대기** → [ghost-system.md §13 G-3·G-4](ghost-system.md) |
| D-12 | 청소·이사 작업 중 귀신 출현·어택 처리 | [ghost-system.md §13 G-11](ghost-system.md) → M8 예외 처리 |
| D-13 | 두 스킬이 재사용 대기를 **공유하는지 독립인지** (MS-3 잔여) | **수치는 둘 다 10초로 확정** (탐지=플로우차트 / 굴착=사용자 2026-09-05). 수치가 같아 실질 차이는 "굴착 쿨타임 동안 탐지도 막히는가" 하나다 → [mole-skill-system.md §3.3](mole-skill-system.md) |
| ~~D-22~~ | ~~굴착의 최종 입력 키 (MS-16)~~ | ✅ **해결 (2026-09-05)** — **T.** 기획서의 E는 `Interact`(문 여닫기) 충돌로 채택하지 않는다. 바인딩은 이미 T이므로 **테스트의 R 단언만 고치면 red 해소** → [mole-skill-system.md §3.5](mole-skill-system.md) |
| ~~D-23~~ | ~~굴착 은신이 깨지는 예외 (MS-17)~~ | ✅ **해결 (2026-09-05)** — **채택.** 귀신에게 이미 감지된 상태에서 시전하면 땅속에서도 감지된다. 굴착은 "들키기 전에 미리 숨는" 스킬이 된다 → [mole-skill-system.md §5.2.1](mole-skill-system.md) |
| ~~D-24~~ | ~~굴착 중 조작 제한 범위 (MS-18)~~ | ✅ **해결 (2026-09-05)** — **전부 제한.** 시야 회전과 스킬 키(T) 재입력만 허용 → [mole-skill-system.md §5.5.1](mole-skill-system.md) |
| D-14 | **청소·이사 작업 시스템** — 맵 v0.4에 Type·Count 관리 방식은 정의됨. 얼룩·개별 가구 대상·반출 완료·진행도 판정은 미정(MG-22) | 탐지 스킬(MS-5), 귀신 청소 40% 트리거([G-1](ghost-system.md)), 게임 루프(D-4), MAP-17 |
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

| 날짜 | 태스크 | 비고 |
| --- | --- | --- |
| 2026-09-12 | **사망 후 관전 기획·구현 프롬프트 (SP-DOC)** | 특수능력 제한·맵 통과 자유시점·생존자 1인칭 관전 요구를 [관전 기획서](spectator-system.md)에 기록하고 [구현 프롬프트](../workflow/spectator-implementation-prompt.md)를 작성했다. GDD·스킬·귀신·퀵슬롯·라우팅을 연결했다. 미정 조작키·속도·전환/정리 정책은 제안과 구분했다. 추가 링크·코드 경로·diff 검사 통과. **관전 코드·씬 변경 없음, Unity 컴파일·테스트·Play 미실행.** |
| 2026-09-12 | **퀵슬롯(라디얼 휠) 더미 스캐폴드 구현** | QS-1~10 사용자 확정 반영. `Player/QuickSlot`(Tab 홀드) 입력·세 번째 독립 잠금(`SetWheelInputLocked`, 메뉴>굴착>휠 우선순위)·`QuickSlotSelection`(12시=0번, 시계 방향, 경계 상한 포함) 순수 계산·`QuickSlotWheelUi` 런타임 HUD(딤 오버레이·슬롯 마커·중앙 패널·포인터 화살표·우하단 장착 표시)를 추가했다. 인벤토리 시스템이 없어 확정은 로컬 상태 변경까지만 하고 RPC는 붙이지 않았다(QS-3·QS-6). `GhostHunter > 퀵슬롯 HUD 설치` 멱등 도구로 더미 아이템 2종·4슬롯 로드아웃(2개만 채움)·UI 설정을 `Game` 씬에 배선했다. EditMode **197/197 통과**(신규 12건). **수동 Play 검증(Tab 홀드·마우스 선택·확정 표시)은 대기** → [quick-slot-system.md](quick-slot-system.md), [quick-slot.md](../architecture/quick-slot.md) |
| 2026-09-12 | **B안 선택 + 가구 랜덤 배치 1차 구현(MAP-19)** | B안 사용자 선택(MG-20)·ADR-0013 기록. SO Type/Count·후보 필터·전체 배치 계획·서버 재배치·보관/대상 상태 복제·Q 마커·R 복구·B안 앞마당 시작 위치·멱등 설치/검증 메뉴를 추가했다. 최초 임시 4종 16개 별도 풀, 기존 B안 인테리어 유지. C# 빌드 경고 0·오류 0, standalone NUnit 계획 테스트 13/13 통과. **Unity 메뉴로 설정·풀 16개·안전 후보 91개·시작 위치를 저장하고 설치 내부 seed 0~15 검증을 통과했다. Unity Test Runner·Host/Client Play 검증은 대기다.** 상세 범위·수동 절차는 [맵 §10.1.3](../architecture/map-generation.md) |
| 2026-09-12 | **맵 기획서 v0.4 문서 동기화 (MAP-16)** | Target Furniture Type·Count와 임시 Sofa 2 / Drawer 3 / Chair 5 / Box 6 = 총 16개, 생성 순서·조건별 실패 처리·B/C 비교·작업량 검증을 반영했다. 이전 Work Room 기준은 과거 기록·확인 대상으로 분리하고 MG-21~23, 구현 MAP-17·검증 MAP-18을 추가했다. GDD·개요·스킬 대상 정의·인덱스·CLAUDE 라우팅까지 7개 Markdown을 동기화했다. 원문 표 53행·생성 순서·프로토타입 항목 19개 일치, 로컬 링크 283건·맵 이미지 참조 12개 보존·diff 검사 통과. **문서만 수정, Unity 컴파일·테스트·Play는 실행하지 않음.** |
| 2026-08-15 | 메인메뉴·로비 씬 추가 | 네트워크 리그를 프리팹/부트스트랩 방식으로 전환 |
| 2026-08-16 | House_01 맵 생성 + 문 상호작용 | 더미 큐브 제거, 씬 배치 가구로 전환 |
| 2026-08-17 | macOS Steam 네이티브 지원 | Apple Silicon 유니버설 바이너리 |
| 2026-08-18 | Steam 접속 빌드 지문 검사 | 트랜스포트 패치 5 + 로비 4인 확장 |
| 2026-08-19 | **본 프로젝트 승격 + 문서 재편** | AlienGhost 규약 체계 이관, ADR 0001~0012 |
| 2026-08-19 | MIG-9 `Steamworks` 격리 | UI 레이어에서 Steamworks 참조 제거 |
| 2026-08-20 | MIG-10 Unity MCP 연결 | 브리지 기동 + `.mcp.json` 등록 |
| 2026-08-20 | MIG-4 UniTask 전환 | `async void` 6건 · 코루틴 2건 제거, 취소 토큰 적용 |
| 2026-08-20 | MIG-8 통합 RPC 속성 전환 | 레거시 5건 제거. **호출 권한 기본값 역전 함정** 발견·차단 |
| 2026-08-20 | MIG-1 Core 인프라 | `Services`·`SceneInstaller`·`ISceneFlow`·`SceneFlowController`·`SceneReference`/`SceneNameSO` |
| 2026-08-20 | D-1 결정 (ADR-0011 Accepted) | 로컬 UTP 존치 + 빌드 가드. **이미 Local 로 커밋돼 있던 프리팹 기본값 정정** |
| 2026-08-20 | MIG-2 씬 재편 | 5씬 체계 + additive 전환. Bootstrap→Title→Lobby 플레이 검증, Game 씬 내용 무변경 |
| 2026-08-20 | MIG-3 리그 흡수 | Bootstrap 씬 소유 리그로 전환, `static Instance` 6건 제거. Local Host 스폰·로컬 컨텍스트 등록/해제 플레이 검증 |
| 2026-08-20 | MIG-5 asmdef 레이어 분리 | 8개 실체 DLL로 분리, Gameplay 폴더 이관, Steam/Facepunch 구체 참조를 Systems로 격리 |
| 2026-08-21 | MIG-7 테스트 어셈블리 | EditMode/PlayMode 2개 + 36건. **batchmode 가 프로젝트 스크립트를 에셋에 바인딩하지 못하는 한계** 발견·문서화 → [testing.md §5.3](../workflow/testing.md) |
| 2026-08-21 | MIG-6 가구·문 프리팹화 | `FurnitureCatalog` 도입, 가구 33종·문 3종을 프리팹 원본으로 통일. **같은 이름이 자리마다 다른 치수였던 4종을 회전으로 정리** |
| 2026-08-21 | MIG-12 브랜치 규약 | `Feature/Prototype` → `feature/prototype` (로컬). 원격은 사용자 push 필요 |
| 2026-08-21 | MIG-11 부분 — 결정 무관 항목 | `gh_game` 로비 키, 스폰 지점 점유 검사(난입자가 겹쳐 나오던 결함) |
| 2026-08-23 | Game 집·침실 프리셋 축소 | `House_01` 평면 배율 ×2 → ×1.5, 프리셋 5.7 × 5.4m 재배치·전 조합 검증 |
| 2026-08-23 | M8 유령 시스템 1차 기획 | 적대 AI 방향, 5상태, 정신력·활동도·어택·탐지·추적·연출·예외 처리 TBD 정리 |
| 2026-08-23 | 유령 P1 프로토타입 | 서버 권위 5상태·동적 스폰·탐지·추격·수색·공격 판정 구현, F1 HUD 연결, Local Host 플레이 검증 |
| 2026-08-23 | 정신력 시스템 v0.3 문서 반영 | 개인 0~100, 생존자 팀 평균, 어둠·이벤트·시체 감소, 디버프 임계값, 드릴 카 모니터 요구사항 정리 |
| 2026-08-23 | 정신력 코어 P1 | 시작 100%, 서버 권위 개인 상태·팀 평균·어둠 누적·이벤트/시체/회복 API·디버프 상태·F1 HUD 구현 및 Local Host 검증 |
| 2026-08-23 | 정신력 UI 초기 연결 | 개인/팀 정신력 게이지와 런타임 데이터 연결 검증 |
| 2026-08-24 | 정신력 World Space 모니터 | 임시 화면 고정 HUD 제거, Game 후면에 개인/팀 게이지·정수 퍼센트 모니터 배치 |
| 2026-08-24 | 정신력 4인 숫자 모니터 | 게이지 제거, P1~P4 전체 개인 정신력과 팀 평균 숫자 표시, 미접속 슬롯 흑백 처리 |
| 2026-08-24 | 귀신·정신력 통합 (M8-GS-1a) | 귀신의 모의 정신력·설정 필드·`NetworkVariable` 제거, 귀신 HUD가 실제 팀 평균을 표시. 상태 전이는 임계값 확정까지 시간 기반 유지 |
| 2026-08-24 | 정신력 테스트베드 | 집 서쪽에 시체 2구·귀신 이벤트 1개를 둔 임시 검증 공간. 서버 가시 판정(12m·70°·가림)으로 실제 감소 API 호출 |
| 2026-08-24 | 카메라 노이즈 연출 (M8-GS-4a) | 정신력 20 이하에서 비네트·필름 그레인·색수차 Volume 을 켠다. Player 카메라 포스트 프로세싱 활성화 포함 |
| 2026-08-30 | 귀신 시스템 기획서 1.0 반영 | 공통 5상태·강제 진정·정신력 80/60/30 구간·10초 어택 판정·30~90초 어택·7초 수색·은신처·드릴 카 세이프 존 확정. D-10 해결, 잔여 미결정 15건(G-1~15) 정리 → [ghost-system.md](ghost-system.md) |
| 2026-08-30 | 귀신 프로토타입 재구현 (M8-GS-1b) | 기획서 1.0 기준 처음부터 작성. 팀 평균 정신력으로 5상태 + 강제 진정 전이, 10초 §7.3 확률 어택 판정, 원뿔 시야·소리 탐지, 추격→마지막 위치→7초 수색 AI, 잡힘→`ServerMarkDead()`. F1 HUD Host 전용 연결. EditMode 33건 추가(93 통과). 활동도(0~100) 제거(G-5) → [ghost-prototype.md](../architecture/ghost-prototype.md) |
| 2026-08-30 | 귀신 Scene 뷰 식별 (M8-GS-10) | `GhostPrototype` 레이어와 파란 Gizmo 표식을 추가. 평상시 본체의 플레이어 카메라 비노출 정책은 유지하고, AI 레이캐스트에서 귀신 자신을 제외. EditMode 3건 통과 → [ghost-prototype.md](../architecture/ghost-prototype.md) |
| 2026-08-31 | 두더지 스킬 기획 초안 반영 | 탐지(5초 표시)·굴착(최대 5초 은신 → 도약) 2종, 사용 자격·무제한 사용·종료 후 쿨타임 확정. **원문이 비어 있던 공통 판정 조건·제목·이미지**를 포함해 미결정 15건(MS-1~15) 정리, D-13·D-14 신설 → [mole-skill-system.md](mole-skill-system.md) |
| 2026-08-31 | 굴착 스킬 프로토타입 구현 | 사용자 확정값(E 임시 키·4m 도약·낙하피해 없음·수직 고정·땅속 이동 불가·어디서든 가능·시전 임의·추격 중 진입 허용)으로 `MoleBurrowController` 구현. `PlayerMotor`에 이동 잠금·수직 발사 추가, 귀신 탐지에서 굴착 중 플레이어 제외(`SanityNetworkState.IsBurrowed`). `Player.prefab`에 컴포넌트 배선, `InputSystem_Actions`에 `Burrow` 액션 추가. EditMode 98/98 통과(회귀 없음). MS-7·MS-9 해소, MS-1·2·3·8·10·13 부분 진전 → [mole-skill-system.md](mole-skill-system.md) §8 |
| 2026-08-31 | 굴착 스킬 카메라 연출 추가 | 굴착 시전~매몰 동안 카메라를 바닥 근처(0.15m)로 낮추고(`PlayerMotor.CameraHeightOverride`, 웅크리기와 같은 전환 속도) 화면 비네트를 켠다(`MoleBurrowCameraEffect` + 전용 Global Volume `PP_MoleBurrowVignette.asset`, 정신력 노이즈와 별개 프로필). 로컬 전용 `MoleBurrowController.IsActive`로 구동 — 귀신 탐지용 `IsBurrowed`(안전 여부)와는 분리. `ILocalPlayerContext`에 `BurrowController` 등록 추가. Game 씬에 설치 도구(`MoleBurrowPostProcessingSetup`, 메뉴 `GhostHunter/두더지 굴착 카메라 연출 설치`)로 배선. EditMode 98/98 통과. MS-8 완전 해소 → [mole-skill-system.md](mole-skill-system.md) §5.5·§6 |
| 2026-08-31 | Volume 오버라이드 미저장 버그 수정 | 사용자가 "비네트에 오버라이드가 하나도 없다"고 보고 — `VolumeProfile.Add<T>()`만으로는 컴포넌트가 서브 에셋으로 저장되지 않고(`AssetDatabase.AddObjectToAsset` 누락) 도메인 리로드 후 조용히 사라지는 버그였다. `MoleBurrowPostProcessingSetup`·`SanityPostProcessingSetup` 둘 다 수정하고 `ValidateInstallation()`에 `AssetDatabase.Contains()` 재발 감지를 추가. **정신력 카메라 노이즈(M8-GS-4a)도 2026-08-24부터 같은 버그로 실제로는 적용되지 않고 있었다** — 재설치로 해결, 위 M8-GS-4a 행 갱신. EditMode 98/98 재확인 |
| 2026-08-31 | 귀신 초자연현상 구현 | 사용자 확정값(전체 8종 프레임 / 소리 2종은 오디오 대기로 비활성 / 정신력 비연결)으로 `GhostPhenomenaDirector`(§6.4 선정 루프 — 평상시·활동에서만, 활성 Pool에서 직전 현상 제외 랜덤) + `GhostPhenomenaPlayer`(조명 점멸·환영 연출) 구현. 6종 활성: 물건 흔들기·작은 물건 떨어뜨리기(서버가 가구 Rigidbody 임펄스, ADR-0010) · 문 열고 닫기(`DoorInteractable.ServerForceToggle` 신규) · 서랍 열기(`GhostDrawer` 신규, 씬 배선) · 조명 깜빡임/끄기(`GhostAmbientLight` 마커) · 귀신 일시 출현. 서버가 `PlayPhenomenonRpc(kind,pos,seed)` 로 전 피어 연출 재생(§10). 발생 주기·반경·세기는 `GhostPrototypeSettings` 에 `[TBD] G-13` 노출. F1 HUD `Phenomena` 줄 + `현상 랜덤` + **8종 기능별 시험 버튼**(상태·주기 무관 즉시 실행). EditMode 115/115 통과(신규 11건), PlayMode 12/12 회귀 없음. M8-GS-2 는 정신력 연결(G-6)만 남음 → [ghost-prototype.md](../architecture/ghost-prototype.md) |
| 2026-08-31 | 귀신 초자연현상 튜닝 + 이동/조준 정비 | (1) F1 HUD 접이식 정리 + `내 위치에 스폰`·`본체 보이기`(NetworkVariable) 토글. (2) 귀신 CharacterController 가 `Physics.IgnoreLayerCollision` 으로 Player·Furniture 통과 — 흔들기 방해 제거. (3) 흔들기 재작성: 반경 6m 내 Idle 가구 전체를 사인파 각속도(진폭 `ShakeTorque` 12, 9Hz)로 동시에 흔듦. (4) 떨어뜨리기 재작성: '작은 물건' 기준을 Light 등급 + 렌더러 바운즈 ≤ `SmallPropMaxSize`(0.45m)로 정의, 반경 내 전부를 질량 무관 `DropSpeed`(2.5m/s)로 튕김. (5) 좌클릭 가구 조준 `maxTargetDistance` 12→2m. (6) **달리기 구현** — `Sprint`(Left Shift) 입력, `PlayerMoveSettings.sprintMultiplier` 1.4× (5→7 m/s), 웅크림 중엔 무효 (`PlayerMotor.ResolveMoveSpeed`). EditMode 115/115. **미결: 귀신 `RunSpeedThreshold`(4.5) < 걷기(5)라 걷기/달리기 소리 구분 안 됨 → G-4 재튜닝 필요** |
| 2026-08-31 | 드릴 카 세이프 존 임시 구현 | 정식 드릴 카 없이 `DrillCarSafeZone`(순수 MonoBehaviour + 정적 레지스트리, 씬 고정 상자) 하나를 현관 앞에 둔다 — 설치 도구가 `House_01` 현관문 남쪽으로 역산 배치(`Game/DrillCarSafeZone_Temp`, 4.5×3×5, 스케일 1). 서버 `TryDetectPlayer`/`TryCatch` 가 `DrillCarSafeZone.Contains` 로 그 안의 플레이어를 탐지·잡힘에서 제외. 다른 플레이어 어택·귀신 상태·타이머는 불변. §11.1 세부(전원 이탈 타이머·재진입 처리)는 미구현. `ValidateInstallation()` 에 존재 검사 추가. EditMode 115/115 → [ghost-prototype.md](../architecture/ghost-prototype.md) |
| 2026-08-31 | 초자연현상 목격 → 정신력 연결 (M8-GS-2, G-6 해결) | 사용자 확정(종류 불문 목격 시 적용, 감소량 15 — `SanitySystemSettings.GhostEventDecrease` 10→15). `GhostPrototypeController.ServerCheckPhenomenonWitnessed` 신규 — 현상 발생마다 생존·비굴착 플레이어 전원의 목격 여부(거리 `PhenomenonWitnessDistance` 12m·각도 `PhenomenonWitnessAngle` 70°·가림, `GhostVision.IsInsideCone` 재사용, 서버는 카메라 피치를 모르므로 요만 판정)를 확인해 `SanityNetworkState.ServerApplyGhostEventWitnessed()` 호출. `GhostPrototypeSettings`·`SanitySystemSettings` 두 기본 에셋을 `manage_scriptable_object` 로 갱신(YAML 손편집 없음). 값 변경으로 깨진 기존 테스트 2건(`SanityStateTests`) 수정 — 디버프 경계 테스트는 감소량에 결합되지 않도록 `TickDarkness` 기반으로 재작성. EditMode 115/115 통과 → [ghost-prototype.md §4·§5](../architecture/ghost-prototype.md), [sanity-system.md §4.2](sanity-system.md) |
| 2026-09-05 | 스킬 아이콘 4종 + UI 화면 레퍼런스 수령 (문서만) | `Assets/Sprite/Skill_icon/` 에 `ICON_ detection_on/off.png`·`ICON_excavation_on/off.png`, `docs/images/skill_icon_reference1~2.png` 에 원형 아이콘의 실제 화면 크기·대비 레퍼런스 2장. [mole-skill-system.md](mole-skill-system.md) §6.3 갱신 + **§6.4 화면 레퍼런스 신설**(기존 "그 외 연출"은 §6.5). **탐지 아이콘 두 개는 파일명에 `ICON_` 뒤 공백이 있다** — 원문 1.0 표기 그대로라 오타가 아니고, 굴착 쪽과 규칙이 달라 경로로 부를 때 주의해야 한다. 이로써 **MS-D(공통 스킬 UI)를 막던 선행 조건이 전부 풀렸다** — 규격·색·쿨타임 10초·에셋이 다 갖춰졌고 남은 건 크기·여백 수치(MS-21)와 구현뿐이다. 미전달 이미지는 `굴착 스킬 플로우` 하나만 남는다 |
| 2026-09-05 | **굴착 확정 규칙 2건 구현** (MS-B·MS-C) | **① 조작 전부 잠금**(§5.5.1) — `PlayerInputReader.SetSkillInputLocked` 신설. 일시정지 메뉴의 전면 잠금과 **독립된 두 번째 잠금**으로, `Look`·`Burrow` 만 살리고 이동·점프·던지기·상호작용·엎드리기를 막는다(`CrouchHeld` 는 동결). 겹치면 메뉴가 이긴다. `MoleBurrowController` 가 시전 시작에 걸고 **정상 종료·시전 취소·디스폰 세 경로 모두**에서 푼다. **② 감지 상태 매몰 시 은신 무효**(§5.2.1) — `BurrowExposureTracker`(순수 클래스, `BedHideEvaluator` 관례) + `GhostPrototypeController.EvaluateBurrowExposure`. **매몰이 시작된 순간** `_pursuit != Roam && _target == player`(침대 밑과 같은 "귀신이 봤다" 기준)를 한 번 판정하고 그 굴착이 끝날 때까지 유지 — 들어간 뒤 놓쳐도 안전해지지 않는다. `TryDetectPlayer` 와 `TryCatch` **양쪽**에 적용. **버그 동반 수정**: `TryCatch` 가 굴착을 전혀 보지 않아, 탐지에서 빠진 뒤에도 수색(7초) 동안 남는 `_target` 이 **안전하게 숨은 플레이어를 땅속에서 잡을 수 있었다.** 검증: EditMode **162/162**(신규 13 — `GhostBurrowExposureTests` 8 · `MoleSkillWiringTests` 5), PlayMode **12/12**. **수동 Play 검증은 미수행** → [mole-skill-system.md §5.2.1·§5.5.1](mole-skill-system.md), [ghost-system.md §9.5](ghost-system.md) |
| 2026-09-05 | **굴착 쿨타임 10초 확정 + 개발 HUD 스킬 섹션** | 사용자 확정 **굴착 재사용 대기 10초** — 두 스킬 모두 10초가 되어 MS-3 의 수치 부분이 닫혔다(공유/독립만 D-13 잔여). 확인해 보니 `MoleBurrowSettings_Default.asset` 의 `_cooldownSeconds` 는 **이미 10** 이었고 문서가 "3초 임의값"이라고 잘못 적어 온 것이라 **에셋 변경은 없다**. **개발 HUD(Tab)에 `두더지 스킬` 섹션 신설** — 굴착 상태(대기/시전/매몰·남은 시간)·쿨타임 표시 + `굴착 시작`·`즉시 종료`·`쿨타임 리셋`(`IMoleSkillDebug` 신규, `MoleBurrowController` 가 명시적 구현, `ILocalPlayerContext.BurrowController` 로 접근 — 플레이어마다 스폰되므로 설치자 바인딩 불가). 그 아래 `MoleBurrowSettings` 값 줄을 **튜닝 창 렌더러 재사용**(`TuningHud.DrawInline` 신규)으로 펼쳐 F2 창과 같은 SO 를 만진다. `ProjectWiringTests` 의 굴착 R키 단언을 **T** 로 고쳐 **선재 red 해소**. 검증: 복제 프로젝트 batchmode 컴파일 clean(코드 0), EditMode `Player_굴착_액션은_T키에_바인딩되어_있다` **Passed**. MS-A 완료 → [mole-skill-system.md §8](mole-skill-system.md), [ghost-prototype.md](../architecture/ghost-prototype.md) |
| 2026-09-05 | 두더지 스킬 **탐지 플로우차트 + 결정 3건** (문서만) | `docs/images/DetectSkillFlow.png` 수령 — 플로우차트가 **사용 가능 판정 3개**(생존 / 그 스킬이 사용 중이 아닌가 / 재사용 대기 == 0, 실패 시 입력 무시)와 **탐지 재사용 대기 10초**를 정의해 MS-2·MS-3 을 부분 해소했다. 사용자 확정 3건: **① 굴착 키 T**(기획서 E 는 `Interact` 충돌로 미채택 — 바인딩은 이미 T라 테스트만 고치면 red 해소) **② 귀신에게 이미 감지된 상태에서 굴착하면 땅속에서도 감지**(현재 구현과 정반대 — 굴착이 "들키기 전에 미리 숨는" 스킬이 되고 침대 밑 은신과 규칙이 같아진다) **③ 굴착 중 조작 전부 제한**(시야 회전·스킬 키 재입력만 허용 — 현재는 이동만 잠금). MS-16·17·18 해결, ②·③은 **규칙 확정 / 구현 대기**. **로드맵 §1.3 MS 보드 신설**(MS-A~F), D-22·23·24 로 정정 후 해결(앞서 쓴 D-20·D-21 이 기존 라운드 구성·오픈 보이드와 번호가 겹쳤다), D-13 축소 → [mole-skill-system.md](mole-skill-system.md) |
| 2026-09-05 | 두더지 스킬 기획서 **1.0** 반영 | 기존 문서는 원문 **0.1 초안** 기준이었다. 1.0이 추가한 **탐지 입력 키 Q**(MS-1 해소)·시전 연출(레이저 포인터→화면 파란빛, 연출 중 카운트다운 정지)·표시 색(가구 `#f9f871`·얼룩 `#fc84b8`)·**시전자 카메라 전용**(MS-6 해소)·굴착 4m 확정·**공통 스킬 UI 전체**(우측 상단 원형 게이지, 시전 `#78c664` / 쿨타임 `#FFFFFF`, 배경 `#595959` 70%, 아이콘 4종 + Flaticon 크레딧)를 반영. **원문과 구현이 어긋나는 3건을 신설** — MS-16(굴착 키 E/R/**T** 3중 불일치, `ProjectWiringTests` red) · MS-17(이미 감지된 상태에서 시전 시 감지 — 구현과 정반대) · MS-18(조작 제한이 이동만 걸려 있음). D-22·D-23 신설(당시 D-20·D-21 로 표기, 번호 충돌로 정정), D-13 축소. 이미지 8장(플로우차트 2·UI 예시 4·아이콘 4)은 **미전달** → [mole-skill-system.md](mole-skill-system.md) |
| 2026-08-31 | 일반 은신처 임시 구현 (G-8 판정 시점만) | 사용자 확정: "수색 중 은신처 최초 접근 시 1회만" 30%[임시] 검사, **주기·재검사 여부는 여전히 미정**. `HidingSpot`(신규, `Ghost/HidingSpot.cs`) — 정식 가구가 아직 없어 종류를 구분하지 않고 순수 상자 하나로 통일, `DrillCarSafeZone`과 같은 정적 레지스트리 패턴. `GhostPrototypeController` — `TryDetectPlayer`/`TryCatch` 양쪽에서 은신처 안의 플레이어를 제외, `Pursuit.Search` 중 `ServerTickHidingSpots`가 반경(`HidingSpotCheckRadius` 2.5m) 안 미확인 은신처를 발견 즉시 1회 소모하며 30%(`HidingSpotCheckChance`) 판정 → 성공 시 `ServerMarkDead()`. 씬 배치는 `GhostPrototypeSetup.InstallHidingSpots`가 방 바닥 앵커(`Bedroom_01_A_Floor` 등 4개)에서 위치·크기를 역산해 `HidingSpots_Temp`에 침실1·침실2·거실·창고 4개 생성, `ValidateInstallation()`에 존재 검사 추가. EditMode 118/118 통과(신규 `HidingSpotTests` 3건 — 정적 레지스트리는 Play Mode 전용 생명주기라 EditMode 검증 대상 아님, `DrillCarSafeZone`과 동일 관례), PlayMode 12/12 회귀 없음. **알려진 한계**: 기존 귀신 배회 경계 버그로 침실 은신처 2개는 경계 가장자리에 걸림(경계 자체는 이 작업 범위 밖) → [ghost-prototype.md §4·§6](../architecture/ghost-prototype.md), [ghost-system.md §9.5·§13 G-8](ghost-system.md) |
| 2026-08-31 | 걷기 소리 탐지 반경 6m 확정 (G-4 부분 해결) | `GhostPrototypeSettings.RunSpeedThreshold` 4.5→6m/s로 조정해 걷기 5m/s는 `WalkHearingRadius` 6m, 달리기 7m/s는 `RunHearingRadius` 12m로 분리. 웅크리기는 기존 명시적 무음 유지. 기본 에셋은 Unity MCP `manage_scriptable_object`로 저장했고 EditMode 119/119 통과. 달리기 반경·어택 외 상태 적용 여부는 G-4 잔여 |
| 2026-09-04 | 엎드리기 + 침대 밑 은신 | **엎드리기(Z 토글)** 3번째 자세 — `PlayerStance`/`PlayerPosture`(순수, EditMode) 분리, `_isProne` owner-authoritative `NetworkVariable`, 캡슐 0.5m·카메라 0.35m·이동 1.4m/s, 자세 올릴 때 `CanOccupyHeight` 머리 공간 검사, 점프 불가, 엎드려 이동도 귀신 소리 탐지 제외. **침대 밑 은신** — 침대 3종을 다리로 ~0.8m 띄우고 자식 `UnderBedHide`(`BedHideZone`) 추가(`CreateBed` 수정 + 기존 프리팹은 `GlobalObjectIdHash` 보존 제자리 편집). 서버가 플레이어별 `BedHideEvaluator`를 어택 틱마다 굴려 판정: 엎드림+Idle 침대 밑+안 쫓김+시야 밖이 `BedHideConcealSeconds`(1s [임시]) 이어지면 성립 → 탐지·잡힘·수색 훔쳐보기 전부 제외. **들어가는 걸 봤으면 추격 유지 + 침대 밑에서도 잡힘**(수색 중에도 `TryCatch` 호출), 놓친 뒤에야 성립(사용자 확정 2026-09-04). EditMode 134/135(신규 14건 통과, 남은 1건은 선재 실패 `Player_굴착_액션은_R키에...`). 옷장·책상 밑은 미구현 → [player-controller.md](../architecture/player-controller.md), [ghost-prototype.md](../architecture/ghost-prototype.md), [ghost-system.md §9.5·§13 G-8](ghost-system.md) |
| 2026-09-04 | 밸런스 튜닝 창 (F2, 별도) | `TuningHud`(`Assets/Scripts/DebugTools/`) — `PlayerMoveSettings`(16)·`GhostPrototypeSettings`(52)·`MoleBurrowSettings`(6)·`FurnitureThrowSettings`(16)·`SanitySystemSettings`(12), 총 102개 `[SerializeField]` 값을 런타임 리플렉션으로 노출. **접속 HUD(Tab)와 독립된 이동식 `GUILayout.Window`**, 기본 키 `F2`(`ConnectionHud._tuningToggleKey`). 가독성: SO별 접이식 → 그 안에서 `[Header]` 그룹별 접이식(귀신은 11개 그룹, 최대 12줄) + 상단 이름 필터(가로질러 검색). 세션 시작 시 씬 컴포넌트의 `_settings` 에서 SO 를 찾아 붙잡음(배선 없음), `[Range]`→슬라이더 · 편집 후 `OnValidate` 재호출로 상호 의존 클램프 · `이 설정/전체 되돌리기`. SO 에 필드를 더하면 자동 노출. EditMode 회귀 없음(134/135) → [ghost-prototype.md §7](../architecture/ghost-prototype.md) |

| 2026-09-04 | 일시정지 메뉴 · 연결 끊김 처리 **기획·설계 문서화** (코드 변경 없음) | 사용자 확정 4건(ESC 진입 · `timeScale`=1 유지 · 메뉴 4항목 순서 · "호스트와 연결이 끊겼습니다.")을 기준으로 [pause-menu-system.md](pause-menu-system.md)(기획, PM-1~13 미결정)와 [pause-menu.md](../architecture/pause-menu.md)(구현 설계 — 서비스·권위·배선·검증)를 신규 작성. `gdd.md` 조작키·UI 표와 부록 A #16, `player-controller.md` ESC 충돌 해소안, `networking.md` 씬 전환 제약을 함께 갱신. **코드 확인에서 3건 발견** — ① 세션 중 게스트는 `ISceneFlow.Load` 가 거부된다 ② 게스트의 `Game` 씬을 `SceneFlowController` 가 추적하지 않아 나갈 때 안 내려간다 ③ `SetClientSynchronizationMode` 미호출로 동기화 모드가 문서와 달리 `Single` 이다(§5 백로그로 승격). 구현은 D-15 결정 대기 |

| 2026-09-04 | 일시정지 메뉴 **미결정 11건 사용자 확정** (문서만) | 메뉴 중 **조작 전부 잠금**(PM-1) · 안전지대 경고 없음(PM-2) · **ESC 해소안 A**(PM-3 — `PlayerLook` 의 ESC 커서 토글 제거, 메뉴가 커서 관리, `Player/Pause` 신설 + `UI/Cancel` 로 닫기) · 호스트 "타이틀로"→**게스트 전원 강제 종료**(PM-4) · 게스트는 **Steam 로비에 남음**(PM-5) · 설정은 **stub**(PM-6) · 종료는 **확인 대화상자**(PM-7) · 끊김은 **모달+확인 버튼, 자동 이동 없음**(PM-8) · **사유 불문 문구 통일**(PM-9) · **`Result` 미경유**(PM-11) · 호스트 "종료"도 같은 정리(PM-13). 파생 요구 2건 기록 — `IConnectionService` 가 **세션 종료와 로비 퇴장을 분리**해야 하고(PM-5), `SceneFlowController` 가 게스트의 `Game` 씬을 추적해야 한다(§5.4). 신설 미결정 **PM-14·PM-15** → D-16 |

| 2026-09-04 | 일시정지 메뉴 **잔여 4건 확정 → 기획 완료** (문서만) | 다른 플레이어에게 **메뉴 상태 표시 없음**(PM-12 — 복제 상태를 만들지 않는다) · 홀드 중 메뉴를 열면 **발사**(PM-15, 좌클릭 뗀 판정) · `Title` 에 **로비 복귀 진입점**(PM-14 — 로비 소속일 때만 표시, 재접속 로직은 `LobbyController` 에 이미 있음) · **열기 제한 없음**(PM-10 — 사망·어택·굴착·홀드 중 전부 허용). **구현 함정 1건 발견**: `GrabController.ReleaseGrab()` 은 `AttackReleasedThisFrame` 프레임에만 불리므로, 입력을 잠그기만 하면 가구가 발사되지 않고 계속 떠 있는다 — 잠그기 **전에** 해제를 명시 호출해야 한다(공개 API 필요) → [pause-menu.md §6.5](../architecture/pause-menu.md) |

| 2026-09-04 | **일시정지 메뉴 · 연결 끊김 처리 구현** | `PauseMenuController`(신규, 상태기계 Closed/Menu/ConfirmQuit/Disconnected) · `Player/Pause` 액션(ESC·게임패드 Start) 신설 · `PlayerInputReader.SetGameplayInputLocked`(값을 0으로, **`CrouchHeld` 는 동결** — 맵을 끄면 메뉴를 여는 것만으로 일어선다) · `GrabController.ForceRelease()`(잠그기 **전에** 호출해야 발사된다) · `PlayerLook` 의 ESC 커서 토글 제거(PM-3 A안) · `IConnectionService.Disconnect(bool leaveLobby)` + `SessionEnded`(게스트는 로비 유지, 호스트는 퇴장) · `ConnectionManager` 가 `OnTransportFailure` 도 구독 · `SceneFlowController` 가 `sceneLoaded` 로 **게스트의 NGO Game 씬을 받아들인다**(§5.4 A안) · `MainMenuController` 로비 복귀 버튼(로비 소속일 때만) · 설치 도구 `PauseMenuSetup`(Game 씬 **덧붙이기** + Title 버튼 추가, 재생성 아님) · `GhostHunter.UI` → `Unity.InputSystem` 참조 추가. EditMode **141/142**(신규 6건 통과, 잔여 1건은 선재 실패 `Player_굴착_액션은_R키에...`), PlayMode **12/12** 회귀 없음. **수동 검증과 선행 검증 D-1 은 미수행** → [pause-menu.md](../architecture/pause-menu.md), [pause-menu-system.md](pause-menu-system.md) |

| 2026-09-04 | 개발 HUD 부활 기능 | `SanityState.Revive()`(생존 상태만 복구 — 정신력 값·시체 목격 기록 유지, 어둠 누적만 0으로) → `SanityNetworkState.ServerRevive()` → `ISanityDebug.ReviveLocalPlayer`/`ReviveTeam` → Tab HUD 버튼 `부활`·`팀 전원 부활`. 사망(`ServerMarkDead`)의 짝이 없어 죽으면 되돌릴 방법이 없던 문제를 해소. EditMode 신규 5건 포함 **146/147** 통과(잔여 1건은 선재 실패). **게임 규칙으로서의 부활(동료가 되살리는 메커니즘)은 여전히 미정** — 이 API 의 게임플레이 호출부는 없다 → [sanity-system.md](../architecture/sanity-system.md) |

| 2026-09-05 | **맵 생성 기획서 v0.3 반영** (문서만, 코드 변경 없음) | v0.3 원문(Floor 개념·계단 A/B·추격 동선 유지·Work Room·생성 순서 16단계·충돌 규칙 10개·체크리스트 14항)과 2026-09-05 피드백(맵 축소 대신 청소 가구 수로 조절 · 프로토타입 ×2 · 다락/지하 열쇠 해금 · 라운드별 맵 4종)을 [map-generation.md](../architecture/map-generation.md) 에 반영. **원문 대비 실측 검산에서 모순 2건 발견** — ① "프로토타입 ×2"(침실 슬롯 7.6×7.2m)로는 §3.1 의 층당 8~9개 공간이 20×16m 에 들어가지 않는다(침실 4개만 219㎡) ② §3.1 공간 목록은 21~23개인데 §6.1 은 전체 방을 12~15개로 잡는다. 미결정 **MG-1~15** 신설, 로드맵에 **MAP 마일스톤 + MAP-1~12**(귀신 층간 이동·굴착 도약 재검증 포함), 결정 대기 **D-17~D-20** 추가. `gdd.md` §6 을 현재 구현/v0.3 목표로 분리하고 부록 A #17·#18 추가. **MIG-6 상태 정정** — "생성 도구 재실행 필요"로 남아 있었으나 커밋 `015f874`(2026-08-31)에서 이미 구워졌다(가구 33·문 3, 씬에 `PrefabInstance` 108건) |

| 2026-09-05 | **맵 v0.3 결정 4건 + 층별 도면 반영** (문서만) | 사용자 확정: **① 규모** — 한 층 20×16m ×2개 층 + 다락 14×10m, 방은 부풀리지 않음. 도면이 실측이라 **`MapScale` = 1.0** 이고 침실 슬롯은 **4.2×4.0m**(현재 5.7×5.4 에서 45% 축소 — 던지기 무대가 방에서 중앙 홀 6.8×6.8·갤러리 홀 8.4×6.4 로 옮겨 간다). **② 소형 오브젝트는 미리 배치**(런타임 스폰 없음, ADR-0009 유지). **③ 라운드마다 개별 매치.** **④ 층별 도면 3장 수령** → `docs/images/house1~3floor.png`, 층별 방 치수·계단 위치를 표로 옮겨 적음. **도면에서 새로 확인된 것** — 계단 A·B 폭 **2.2m** 로 세 층 정렬(MG-7 부분 해결) · 침실 4.2×4.0 이 프리셋 A·B·C 를 모두 담아 **MG-8 해결** · 방 개수 실측 21개인데 **1~2층만 15개**라 §6.1 "12~15개"와 맞음(**MG-5 해결**) · 본문에 없던 **비밀 보관실**(다락 2.4×1.8)·**현관**·앞마당 · **2층 갤러리 홀 중앙이 1층으로 뚫린 오픈 보이드**(MG-16 / D-21 신설 — 층간 낙하·시야·소리). 미결정 MG-16~19 추가, MAP-13 추가, **MAP-1 그레이박스 착수 가능** |

| 2026-09-05 | **MAP-1 오른쪽 그레이박스 1차 생성** | 기존 `House_01`·`House_01_OriginalScale_Right`를 보존하고 `House_01_V03_Graybox_Right`를 실제 외곽 3m 동쪽(x=38.58)에 추가. Floor 3개(20×16, 20×16, 14×10), 방 footprint 7+8+6=21과 고정 홀을 시각화. `MansionGrayboxBuilder`·덧붙이기 전용 멱등 메뉴·EditMode 테스트 2건 추가. `MapGeneratorTests` **11/11**, PlayMode **12/12** 통과. 전체 EditMode **148/149**(1건은 선재 실패 `Player/Burrow` R키 배선). 계단·오픈 보이드는 MG-7·MG-16/D-21을 닫지 않는 TBD 표식이며 체감 검증 대기 |
| **2026-09-05** | **탐지 벽 투시 철회** | 사용자 확정 번복 — 탐지 표시가 **벽 뒤까지 보이지 않는다.** `DetectionHighlight.shader` 를 `ZTest Always` → **`ZTest LEqual` + `Offset -1,-1`**(같은 지오메트리라 z-fighting 방지, `ZWrite` 는 계속 off). 기획서 원문의 "맵 내에 존재하는 모든"은 **사거리·시야각 제한이 없다**는 뜻으로만 한정하고, 가림은 적용한다. 탐지는 대상을 찾아 주는 스킬이 아니라 **눈앞의 것을 구분해 주는** 스킬이 되어 §2.4 기획 의도(작업을 오래 헤매지 않도록)의 강도가 약해진다 — 플레이 검증에서 다시 볼 것. MS-4 의 투시 항목이 닫혔다. **함께 1.0 이 못 돌린 EditMode 를 복제 batchmode 로 실행해 버그 1건 수정** — `DetectionSkillStateMachine` 의 시간 비교가 float 오차에 취약해 쿨타임을 나눠 태우면 한 프레임 더 잠겼다(`TimeEpsilon` 1e-4 도입). **165/172 passed · 1 failed · 6 skipped**, 탐지 신규 테스트 전부 통과. 실패 1건과 skip 6건은 복제 환경의 에셋 임포트 문제로 원본 재확인 필요 → [mole-skill-system.md §4.5](mole-skill-system.md) |
| **2026-09-05** | **탐지 시전 파란빛 연출 부분 구현** | `DetectionSkillSettings`에 시전 화면 색·최대 불투명도를 설정값으로 노출하고, `MoleSkillHud`가 `Casting` 상태에서만 전체 화면 로컬 오버레이를 펄스로 그린다. 시전 중간에 가장 밝고 활성 5초 카운트가 시작되기 직전에 사라진다. 기본 `#40a0ff`·0.35·0.5초는 MS-14 임시값. 현재 프리미티브 Player에는 손·주머니·레이저 포인터 모델/애니메이션 에셋이 없어 해당 동작은 미연결이며 수동 Play 검증 대기 → [mole-skill-system.md §4.4·§6.5](mole-skill-system.md) |
| **2026-09-05** | **HousePlanB·C 도면 컨텍스트 보완 (MAP-14, 문서만)** | 이미지 내부 제목 기준으로 규모·방 치수·층별 배치를 대조하고 파일명 B/C 역전 대응, 비교표·이미지 첨부 목록·인덱스 링크를 정리했다. 기존 "치수만 다르고 배치·보이드가 동일" 서술을 수정하고 B·C안의 미표기 계단 폭·층고·보이드 범위를 확인 필요로 남겼다. 새 대안의 채택은 MG-20(TBD); 기존 MG-2와 씬은 유지 → [map-generation.md §2](../architecture/map-generation.md#house-plan-bc) |
| **2026-09-05** | **B·C 대저택 실내 프로토타입 생성기 (MAP-15)** | `PlanVariantPrototypeSetup`(Editor 메뉴)와 `PlanVariantPrototypeSettings`(SO)를 추가했다. B안=`HousePlanC.png` 30×24m/다락20×14m, C안=`HousePlanB.png` 40×32m/다락28×20m을 각각 7/8/6 방으로 만들고, 실내 벽·문 개구부·현관·창문·임시 조명·가구 프리팹·시각 12단+경사 콜라이더 계단 A/B·난간(각 층 연결)을 생성한다. 앞마당과 기존 맵 방향 연결 바닥도 프로토타입 소유로 추가한다. 상부 슬래브는 계단 개구부를 잘라 내며 2층 갤러리는 `[TEMP]` solid floor로 유지(MG-16). 방·치수·가구/벽/문/계단 여유·Player 프로필·경사·Rigidbody 소유 검증과 기존 NGO 해시 갱신을 메뉴에 연결했다. **코드 컴파일만 완료, Unity Editor 메뉴 실행·Game 씬 저장·수동 Play 이동은 대기** — 기존 A안·MG-2·MG-7·MG-16·MG-20은 유지. 에디터 잠금을 피한 복제본 EditMode batchmode 시도는 Licensing Client 초기화에서 종료되어 테스트 XML/케이스 0건 |
| **2026-09-06** | **B·C 실내 여백 정리 재설계 반영 (MAP-15)** | `PlanVariantPrototypeSetup.CreatePlanB/CreatePlanC`의 방 좌표를 원본 도면 값에서 여백 정리 재설계 값으로 교체했다. 같은 날개(같은 X열) 안에 쌓인 방은 외벽↔외벽(또는 외벽↔남측 방 열 경계) 구간을 원래 도면 깊이 비율대로 빈틈없이 나눠 방-방 사이 간격을 0으로 없앴다. Unity MCP로 기존 `House_Prototype_PlanB/C`를 삭제하고 메뉴를 재실행 — 첫 시도는 `ValidateRoomBounds`가 외곽 이탈로 실패했는데(파이썬 사전 계산이 벽 두께 절반만 뺐고, Unity 쪽 검증은 전체를 뺀다는 차이), 좌표를 다시 계산하고 float 안전 여유 0.02m를 더해 통과시켰다. 검증 메뉴로 "가구 79개/79개, 계단 각 4개, 방 각 21개(7/8/6)" 확인, `Game.unity` 저장 완료. 방+홀 면적 비율이 층당 36~53% → 68.5~83.0%로 올라갔다. 부작용으로 침실 열 아래 욕실류 5곳이 침실급(41~82㎡)으로 커진 것은 사용자 확인 후 수용했다. 수동 Play 체감 검증은 대기, MG-2·MG-20은 미결정 그대로 → [map-generation.md §2](../architecture/map-generation.md#house-plan-bc) |
| **2026-09-05** | **탐지 스킬 + 공통 스킬 UI 구현** | `Player/Detect`(Q) 액션, 생존→미사용→쿨타임 0 판정, 시전 임시 0.5초→활성 5초→종료 후 10초 쿨타임, 활성 마커 색상 표시와 `ZTest Always` 셰이더를 추가했다(**투시는 같은 날 위 행에서 철회**). `MoleSkillHud`가 `IMoleSkillStatus`로 탐지·굴착을 동시에 표시하고, `MoleSkillSetup`이 아이콘 Single 재임포트·SO·Player 프리팹·Game 씬·[TEMP] 마커를 멱등 배선한다. 작업 시스템 대상 판정은 MS-5로 유지한다. 복제 batchmode 컴파일/EditMode는 Unity Licensing 및 오프라인 Git 패키지 의존성으로 완료하지 못했고 수동 Play 검증도 대기다. 새 TBD는 MS-19~MS-21 → [mole-skill-system.md §8~9](mole-skill-system.md) |

---

최종 갱신: 2026-09-12 (사망 후 관전 문서 SP-DOC 완료·구현 SP-IMPL-1~4 추가, 퀵슬롯 사망 게이팅 연계.
같은 날 맵 v0.4 문서 동기화 MAP-16 + B안 선택·랜덤 가구 1차 코드 MAP-19.
Unity 설치·씬 저장 완료, Test Runner·Host/Client Play 검증 대기. MAP-17 정식 작업·MAP-18 체감 검증 계속 대기.
Work Room 이전 기준을 보류하고 D-14·MG 의존 관계 갱신.)

이전 갱신: 2026-09-06 (MAP-15 B·C 실내 여백 정리 재설계 반영 — 메뉴 실행·씬 저장·검증 완료,
방+홀 비율 68.5~83.0%)

2026-09-05 (MAP-15 B·C 대저택 실내 프로토타입 생성기 코드 추가 —
에디터 메뉴 실행·씬 저장·수동 Play 검증 대기. MAP-14 HousePlanB·C 도면 컨텍스트 보완, MAP-1 오른쪽 그레이박스 1차 생성 및 탐지 스킬·공통 UI 코드 구현 포함)
