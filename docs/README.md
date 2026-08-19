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
│   └── roadmap.md                  마일스톤 & 태스크 보드
├── architecture/                   어떻게 구성되는가
│   ├── overview.md                 폴더·어셈블리·씬·런타임 구조
│   ├── networking.md               Netcode for GameObjects 규약
│   ├── steam.md                    Steam 연동 (Facepunch.Steamworks)
│   ├── player-controller.md        1인칭 이동·시점·문 상호작용
│   ├── throw-system.md             타겟팅 → 홀드 → 부양 → 발사
│   ├── furniture-physics.md        가구 오브젝트·물리·아웃라인
│   ├── map-generation.md           맵 생성 시스템 (House / Room Preset / Spawn Point)
│   └── decisions/                  ADR (기술 결정 기록)
├── conventions/                    어떻게 쓰는가
│   ├── code-style.md               C# / Unity 코딩 규약
│   ├── unity-assets.md             씬·프리팹·SO·네이밍 규약
│   └── git.md                      브랜치·커밋·PR 규약
└── workflow/                       어떻게 일하는가
    ├── development-loop.md         작업 절차 & 완료 기준(DoD)
    ├── testing.md                  테스트 전략 & 실행법
    ├── unity-mcp.md                Unity MCP — 에디터 직접 조작 규칙
    └── playbooks.md                반복 작업 레시피
```

## 2. 문서별 요약

| 문서 | 답하는 질문 | 갱신 트리거 |
| --- | --- | --- |
| [project/overview.md](project/overview.md) | 이 게임은 무엇이고 어디까지 만드는가 | 범위·플랫폼·목표 변경 |
| [project/gdd.md](project/gdd.md) | 규칙, 루프, 시스템, 밸런스는 | 게임 디자인 결정 |
| [project/roadmap.md](project/roadmap.md) | 지금 무엇을 하고 있고 다음은 무엇인가 | 태스크 시작/완료 시 |
| [architecture/overview.md](architecture/overview.md) | 코드와 에셋은 어디에 어떻게 놓이는가 | 폴더·어셈블리·씬 추가 |
| [architecture/networking.md](architecture/networking.md) | 무엇을 서버가 정하고 무엇을 동기화하는가 | 네트워크 객체·RPC 추가 |
| [architecture/steam.md](architecture/steam.md) | Steam 초기화·로비·연결은 어떻게 하는가 | Steam 연동 코드 변경 |
| [architecture/player-controller.md](architecture/player-controller.md) | 1인칭 이동·시점·입력은 | 컨트롤러·입력 매핑 변경 |
| [architecture/throw-system.md](architecture/throw-system.md) | 잡기·부양·발사는 어떤 상태로 도는가 | 던지기 규칙·수치 변경 |
| [architecture/furniture-physics.md](architecture/furniture-physics.md) | 던져지는 대상은 어떻게 생겼는가 | 가구 물리·아웃라인 변경 |
| [architecture/map-generation.md](architecture/map-generation.md) | 집·방·스폰 포인트는 어떻게 만들어지는가 | 맵 생성 규칙 변경 |
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
| project/overview.md | 🟡 프로토타입 범위 서술 — 본게임 범위 재정의 필요 |
| project/gdd.md | 🟡 던지기 시스템은 확정, 승패·루프·유령 진영 TBD |
| project/roadmap.md | 🟢 M0~M7 + 마이그레이션 보드 |
| architecture/overview.md | 🟡 목표 구조 확정 / 현재 구조와 다름 (마이그레이션 중) |
| architecture/networking.md | 🟢 규약 확정 |
| architecture/steam.md | 🟢 구현·패치 완료 / 2PC 실기 검증 대기 |
| architecture/player-controller.md | 🟢 구현됨 |
| architecture/throw-system.md | 🟢 구현됨 / 플레이테스트 튜닝 대기 |
| architecture/furniture-physics.md | 🟢 구현됨 |
| architecture/map-generation.md | 🟢 House_01 구현됨 / 콘텐츠 배치 미구현 |
| architecture/decisions/ | 🟢 ADR-0001~0010 |
| conventions/* | 🟢 규약 확정 |
| workflow/unity-mcp.md | 🔴 미설치 — 사용자 마무리 절차 대기 |
| workflow/* (그 외) | 🟢 규약 확정 |

---

최종 갱신: 2026-08-19
