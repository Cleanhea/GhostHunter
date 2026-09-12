# GhostHunter 문서 인덱스

이 폴더는 **에이전트와 사람이 공유하는 단일 진실 원천(SSOT)** 이다.
루트의 [CLAUDE.md](../CLAUDE.md) / [AGENTS.md](../AGENTS.md)가 이 인덱스를 통해 컨텍스트를 로드한다.

> **2026-08-19 개편.** `01~09` 번호 문서 체계를 카테고리 트리로 재편했다.
> 프로토타입에서 본 프로젝트로 승격하면서 규약·프로세스 층(`conventions/`, `workflow/`, ADR)을 새로 얹었다.
> 마이그레이션 진행 상황은 [project/roadmap.md §2](project/roadmap.md).

---

## 1. 문서 맵

```
docs/
├── README.md                       ← 지금 이 파일 (인덱스 & 규칙)
├── project/                        무엇을 만드는가
│   ├── overview.md                 프로젝트 개요·목표·범위
│   ├── gdd.md                      게임 디자인 문서
│   ├── sanity-system.md            개인·팀 정신력·증감·디버프·플레이/모니터 UI
│   ├── ghost-system.md             귀신 공통 상태·정신력 연동·어택·탐지·추격 규칙
│   ├── mole-skill-system.md        두더지 스킬(탐지·굴착) 공통 규칙·판정·쿨타임·UI — 저장소 반영 1.2
│   ├── pause-menu-system.md        일시정지 메뉴·호스트 연결 끊김 규칙 — 구현됨, 수동 검증 대기
│   ├── quick-slot-system.md        퀵슬롯(라디얼 휠) 규칙·QS-1~10 확정 — 더미 스캐폴드 구현됨
│   ├── spectator-system.md         사망 후 능력 제한·자유시점·생존자 관전 — 핵심 요구 확정, 구현 미착수
│   └── roadmap.md                  마일스톤 & 태스크 보드
├── architecture/                   어떻게 구성되는가
│   ├── overview.md                 폴더·어셈블리·씬·런타임 구조
│   ├── networking.md               Netcode for GameObjects 규약
│   ├── steam.md                    Steam 연동 (Facepunch.Steamworks)
│   ├── player-controller.md        1인칭 이동·시점·문 상호작용
│   ├── throw-system.md             타겟팅 → 홀드 → 부양 → 발사
│   ├── furniture-physics.md        가구 오브젝트·물리·아웃라인
│   ├── ghost-prototype.md           귀신 P1 상태·탐지·추격·HUD 스폰
│   ├── sanity-system.md             정신력 네트워크·집계·연동 API
│   ├── map-generation.md           맵 생성 시스템 v0.4 (House / Floor / Room Preset / Spawn Point / Target Furniture Type·Count)
│   ├── pause-menu.md                일시정지 메뉴·연결 끊김 배선·권위·검증 — 구현됨, 수동 검증 대기
│   ├── quick-slot.md                퀵슬롯 휠 구현 — 입력 잠금·선택 계산·UI 배선 — 더미 스캐폴드
│   └── decisions/                  ADR (기술 결정 기록)
├── conventions/                    어떻게 쓰는가
│   ├── code-style.md               C# / Unity 코딩 규약
│   ├── unity-assets.md             씬·프리팹·SO·네이밍 규약
│   └── git.md                      브랜치·커밋·PR 규약
└── workflow/                       어떻게 일하는가
    ├── development-loop.md         작업 절차 & 완료 기준(DoD)
    ├── testing.md                  테스트 전략 & 실행법
    ├── unity-mcp.md                Unity MCP — 에디터 직접 조작 규칙
    ├── spectator-implementation-prompt.md 사망 후 관전 구현 모델용 작업 지시
    └── playbooks.md                반복 작업 레시피
```

## 2. 문서별 요약

| 문서 | 답하는 질문 | 갱신 트리거 |
| --- | --- | --- |
| [project/overview.md](project/overview.md) | 이 게임은 무엇이고 어디까지 만드는가 | 범위·플랫폼·목표 변경 |
| [project/gdd.md](project/gdd.md) | 규칙, 루프, 시스템, 밸런스는 | 게임 디자인 결정 |
| [project/sanity-system.md](project/sanity-system.md) | 개인·팀 정신력은 어떻게 계산·증감·표현되는가 | 정신력 규칙·UI·피드백 변경 |
| [project/ghost-system.md](project/ghost-system.md) | 귀신은 어떤 상태를 가지고 언제 어택하는가 | 귀신 공통 규칙·수치·판정 변경 |
| [project/mole-skill-system.md](project/mole-skill-system.md) | 플레이어는 어떤 스킬을 언제 쓰고 어떻게 끝나는가 | 스킬 규칙·수치·연출 변경 |
| [project/pause-menu-system.md](project/pause-menu-system.md) | 매치를 어떻게 떠나고, 끊기면 무엇을 보는가 | 일시정지 메뉴·나가기·끊김 규칙 변경 |
| [project/quick-slot-system.md](project/quick-slot-system.md) | 퀵슬롯 휠은 언제 열리고 무엇을 담는가 | 퀵슬롯 규칙·슬롯 구성 변경 |
| [project/spectator-system.md](project/spectator-system.md) | 사망 후 무엇을 할 수 있고 두 관전 모드는 어떻게 전환하는가 | 사망·관전 규칙 확정 및 구현 |
| [workflow/spectator-implementation-prompt.md](workflow/spectator-implementation-prompt.md) | 구현 모델에 관전 기능 작업을 어떻게 지시하는가 | 관전 기획·구현 전제 변경 |
| [project/roadmap.md](project/roadmap.md) | 지금 무엇을 하고 있고 다음은 무엇인가 | 태스크 시작/완료 시 |
| [architecture/overview.md](architecture/overview.md) | 코드와 에셋은 어디에 어떻게 놓이는가 | 폴더·어셈블리·씬 추가 |
| [architecture/networking.md](architecture/networking.md) | 무엇을 서버가 정하고 무엇을 동기화하는가 | 네트워크 객체·RPC 추가 |
| [architecture/steam.md](architecture/steam.md) | Steam 초기화·로비·연결은 어떻게 하는가 | Steam 연동 코드 변경 |
| [architecture/player-controller.md](architecture/player-controller.md) | 1인칭 이동·시점·입력은 | 컨트롤러·입력 매핑 변경 |
| [architecture/throw-system.md](architecture/throw-system.md) | 잡기·부양·발사는 어떤 상태로 도는가 | 던지기 규칙·수치 변경 |
| [architecture/furniture-physics.md](architecture/furniture-physics.md) | 던져지는 대상은 어떻게 생겼는가 | 가구 물리·아웃라인 변경 |
| [architecture/sanity-system.md](architecture/sanity-system.md) | 정신력은 어디서 확정·복제·집계되는가 | 정신력 코드·배선·연동 변경 |
| [architecture/map-generation.md](architecture/map-generation.md) | 집·층·방·스폰 포인트·작업 대상 가구의 종류와 개수는 어떻게 정하는가 — [HousePlanB·C 도면 비교](architecture/map-generation.md#house-plan-bc) | 맵 생성 규칙 변경, 기획서 개정·도면 추가 |
| [architecture/pause-menu.md](architecture/pause-menu.md) | 메뉴·끊김 처리는 어떤 서비스를 거치고 무엇을 검증하는가 | 세션 종료 경로·메뉴 배선 변경 |
| [architecture/quick-slot.md](architecture/quick-slot.md) | 퀵슬롯 휠의 입력 잠금·선택 계산·UI는 어떻게 구성되는가 | 퀵슬롯 배선·잠금 규칙 변경 |
| [architecture/decisions/](architecture/decisions/README.md) | 왜 이렇게 골랐는가 | 되돌리기 비싼 선택 발생 시 |
| [conventions/code-style.md](conventions/code-style.md) | C# 코드를 어떻게 쓰는가 | 규약 합의 변경 |
| [conventions/unity-assets.md](conventions/unity-assets.md) | 에셋 이름·구조·설정은 | 에셋 파이프라인 변경 |
| [conventions/git.md](conventions/git.md) | 커밋·브랜치·PR은 | 협업 방식 변경 |
| [workflow/development-loop.md](workflow/development-loop.md) | 작업은 어떤 순서로, 언제 끝나는가 | 프로세스 변경 |
| [workflow/testing.md](workflow/testing.md) | 무엇을 어떻게 테스트하는가 | 테스트 전략 변경 |
| [workflow/unity-mcp.md](workflow/unity-mcp.md) | 에디터를 직접 조작해도 되는가, 어디까지 | MCP 버전·포트·규칙 변경 |
| [workflow/playbooks.md](workflow/playbooks.md) | 이 작업 유형의 표준 절차는 | 반복 패턴 발견 시 |

## 3. 문서 작성 규칙 (에이전트가 읽기 좋은 문서)

1. **선언형으로 쓴다.** "~하는 것이 좋다" 대신 **MUST / SHOULD / MUST NOT**을 쓴다.
2. **한 문서는 한 관심사만** 다룬다. 길어지면 쪼개고 인덱스를 갱신한다.
3. **미확정은 `TBD`로 명시**한다. 빈칸이나 그럴듯한 추측으로 채우지 않는다.
   - `TBD` 항목은 에이전트가 임의로 확정하지 않는다 → 사용자에게 질문.
   - 설계상 열려 있는 선택지는 `> **미결정:**` 블록으로 남긴다. **무엇을 모르는지가 정보다.**
4. **예시는 실제 코드 경로**로 쓴다. 가상의 파일명을 쓰지 않는다.
5. **중복 금지.** 같은 사실은 한 문서에만 두고 나머지는 링크한다.
6. **설계가 바뀌면 코드와 같은 커밋에서 문서를 고친다.** "나중에 정리"는 안 한다.
7. 수치(힘·거리·시간)는 문서에 "초기값"으로 적되, 최종 권위는 `ScriptableObject` 설정 에셋에 있다.
8. 문서 하단에 `최종 갱신` 날짜를 남긴다.

## 4. 상태

| 문서 | 상태 |
| --- | --- |
| project/overview.md | 🟡 본게임 범위 반영 — 정신력 코어 구현, 게임 루프·승패와 유령 세부 규칙 TBD |
| project/gdd.md | 🟡 던지기·유령·정신력 방향 확정, 승패·루프·유령 세부 규칙 TBD |
| project/sanity-system.md | 🟡 v0.5 규칙·코어·플레이 HUD 구현 / 상세 모니터 UI·실제 연출·콘텐츠 연결 TBD |
| project/ghost-system.md | 🟡 공통 상태·정신력 구간·어택·탐지·추격 규칙 확정 / 15개 미결정(G-1~15), 구현은 P1 임시값 |
| project/mole-skill-system.md | 🟢 **굴착: 기획서 1.0 규칙 구현 완료** (2026-09-05) — 키 T · 유지 5초 · 쿨타임 10초 · 조작 전부 잠금 · 감지 상태 매몰 시 은신 무효. 🟡 **탐지·공통 UI 코드 스캐폴드 + 시전 파란빛 오버레이 부분 구현** — Q·활성 판정·5초·10초·색상별 활성 마커·탐지/굴착 동시 게이지. **벽 투시는 없다**(시야에 보이는 표면만, 2026-09-05 확정). `Game` 씬·`Player` 프리팹·아이콘 배선은 정적 확인됐고 수동 Play 검증 대기, 손·레이저 포인터 애니메이션과 작업 시스템의 실제 대상 정의는 MS-14·MS-5. 마지막 완료 복제 batchmode EditMode **165/172**(탐지 신규 전부 통과 — 상태 머신 float 오차 버그 1건을 잡아 수정), 최신 재시도는 Licensing/오프라인 Git 패키지 단계에서 테스트 XML 전에 차단. 실패 1·skip 6은 복제 환경의 에셋 임포트 문제 |
| project/pause-menu-system.md | 🟢 **규칙 확정 + 구현 완료.** PM-1~15 전부 확정. 설정 화면 **내용**(PM-6)만 설정 시스템 기획서로 이월 / 수동 검증 대기 |
| project/quick-slot-system.md | 🟡 **QS-1~10 확정 + 더미 스캐폴드 구현 완료(2026-09-12).** 실제 인벤토리·아이콘 에셋은 별도 작업 / 수동 Play 검증 대기 |
| project/spectator-system.md | 🟡 **사망 후 특수능력 제한 + 자유시점/생존자 관전 핵심 요구 확정(2026-09-12).** 세부 키·속도·전환/정리 정책 일부 TBD / 관전 구현 미착수 |
| project/roadmap.md | 🟢 M0~M7 + 마이그레이션 보드, M8 기획 부분 진행 |
| architecture/overview.md | 🟢 씬·서비스·스크립트 레이어와 asmdef 구조 반영됨 |
| architecture/networking.md | 🟢 규약 확정 |
| architecture/steam.md | 🟢 구현·패치 완료 / 2PC 실기 검증 대기 |
| architecture/player-controller.md | 🟢 구현됨 |
| architecture/throw-system.md | 🟢 구현됨 / 플레이테스트 튜닝 대기 |
| architecture/furniture-physics.md | 🟢 구현됨 |
| architecture/ghost-prototype.md | 🟢 팀 평균 정신력 기반 5상태·어택 판정·탐지·추격·사망 구현, 자동 테스트 통과 / 은신처·드릴 카·초자연현상·NavMesh·수동 플레이 검증 TBD |
| architecture/sanity-system.md | 🟢 코어 P1·World Space 4인 숫자 모니터 구현 / 실제 연출·콘텐츠 연결 TBD |
| architecture/map-generation.md | 🟡 **B안 선택 + 가구 랜덤 배치 1차 구현(2026-09-12, MAP-19).** 임시 4종 16개 설정·서버 재배치·대상 복제·B안 앞마당 시작 위치를 설치했다. 풀 16개·안전 후보 91개 저장 및 설치 내부 seed 0~15 검증 완료. C# 빌드 경고 0·오류 0, 순수 계획 테스트 13/13 통과. **Unity Test Runner·Host/Client Play 검증 대기**. 정식 작업량·운반 완료·계단 세부·열쇠는 TBD → [설치 절차 §10.1.3](architecture/map-generation.md), [ADR-0013](architecture/decisions/ADR-0013-plan-b-random-furniture.md) |
| architecture/pause-menu.md | 🟢 **구현됨.** EditMode 141/142·PlayMode 12/12 통과 / **수동 검증(§10.4~10.6)과 선행 검증 D-1(클라이언트 씬 동기화 모드) 미수행** |
| architecture/quick-slot.md | 🟡 **더미 스캐폴드 구현됨(2026-09-12).** EditMode 197/197 통과, 설치 도구 검증됨 / **수동 Play 검증 미수행**, 사망 게이팅·실제 인벤토리는 TBD |
| architecture/decisions/ | 🟡 ADR-0001~0011·0013 확정 / ADR-0012 Proposed |
| conventions/* | 🟢 규약 확정 |
| workflow/unity-mcp.md | 🟢 설치·연결·씬 편집 검증됨 |
| workflow/* (그 외) | 🟢 규약 확정 |

---

최종 갱신: 2026-09-12 (사망 후 관전 기획서·구현 프롬프트 라우팅 추가. 같은 날 퀵슬롯 더미 스캐폴드 문서와 맵 v0.4·B안 선택·MAP-19 랜덤 가구 코드·검증/설치 안내·ADR-0013 반영.)
