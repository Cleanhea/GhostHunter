# 프로젝트 개요

## 1. 기본 정보

| 항목 | 값 |
| --- | --- |
| 프로젝트명 | GhostHunter |
| 리포지토리 | `c:/MainScreen/Dev/GitDirectory/GhostHunter` |
| Unity | **6000.3.20f1** (Unity 6.3) |
| 렌더 파이프라인 | URP 17.3 — PC 프리셋(`Assets/Settings/PC_RPAsset.asset`) |
| 네트워크 | Netcode for GameObjects 2.13.1 + **Steam P2P (FacepunchTransport 임베드)** |
| 배포 플랫폼 | **Steam (Windows x86_64 + macOS)** — [ADR-0001](../architecture/decisions/ADR-0001-steam-p2p-facepunch-transport.md) |
| 입력 | Input System 1.19 (**신규 전용**, `activeInputHandler: 1`) |
| 비동기 | **UniTask 2.5.11** — [ADR-0005](../architecture/decisions/ADR-0005-unitask-async.md) |
| 물리 | PhysX (built-in 3D). DOTS Physics 사용 안 함 |
| 현재 버전 | `0.1.0` |
| Company / Product | `GhostHunter` / `GhostHunter` |
| Steam App ID | 미발급 — 개발 중에는 **480 (Spacewar)** |

## 2. 무엇을 만드는가

**1인칭 시점에서 가구를 조준해 홀드로 붙잡고, 마우스 방향으로 밀쳐 날리는 멀티플레이 게임.**

> **2026-08-19 — 프로토타입에서 본 프로젝트로 승격했다.**
> 던지기 메커닉 검증이라는 프로토타입 목표는 달성했다는 전제이며,
> 이제 **그 메커닉을 무엇에 쓸 것인가**가 미결이다 → [gdd.md §1·§2](gdd.md)

### 확정된 것

| 항목 | 값 |
| --- | --- |
| 시점 / 조작 | 1인칭. WASD 이동, 마우스 시점, 점프, 좌클릭 홀드 던지기, E 상호작용 |
| 플레이 인원 | **1~4명** — Steam Lobby 최대 정원 4명 |
| 토폴로지 | 호스트 = 리슨 서버. 데디케이티드 서버 없음 |
| 무대 | 집 내부. **현재** `House_01` 단층 19.2 × 12.0m / **확정 목표** 3층 대저택 20×16m ×2층 + 다락 14×10m, 방 21개 → [gdd.md §6](gdd.md) |
| 핵심 메커닉 | 레이캐스트 타겟팅 → 홀드 → 1인 밀치기 / 2인 부양 → 발사 → 연쇄 충돌 |
| 유령 방향 | 플레이어 조작 진영이 아닌 적대 AI. 평상시·활동·경고·어택·진정 5개 상위 상태 |

### 미확정 (TBD)

- **게임 루프 · 승패 조건 · 점수** — [gdd.md §2](gdd.md)
- **정신력 연출·콘텐츠 연결** — 코어·플레이 HUD 구현 완료, 카메라·SFX·드릴 카 상세 UI와 헤드라이트·이벤트·시체·아이템 연결 대기 — [sanity-system.md](sanity-system.md)
- **유령 세부 규칙** — 활동도·어택 타임 수치, 정신력 연동 임계값, 탐지·타깃 선정, 추적·수색, 예외 처리 — [gdd.md §5](gdd.md)
- **두더지 스킬(탐지·굴착)** — **굴착은 기획서 1.0 규칙까지 구현**, 탐지는 Q 입력·5초 표시·10초 쿨타임·활성 마커 색상·공통 UI·시전 파란빛 오버레이까지 구현됐다(2026-09-05). 탐지는 작업 시스템을 추측하지 않고 `DetectionTargetMarker`가 활성인 오브젝트만 대상으로 삼으며, 실제 청소·이사 판정과 손·레이저 포인터 애니메이션은 MS-5·MS-14다. `Game` 씬·`Player` 프리팹·아이콘 배선은 정적 확인됐고 수동 Play 검증이 남았다 — [mole-skill-system.md](mole-skill-system.md)
- **청소·이사 작업 시스템** — 얼룩·이동 대상 가구·진행도의 정의. 기획서 미작성이며 탐지 스킬과 귀신 청소 40% 트리거가 여기에 걸려 있다
- ~~맵 규모·라운드 구성~~ → **확정 (2026-09-05)** — 20×16m 2개 층 + 다락 14×10m, `MapScale` 1.0, 라운드마다 개별 매치. 남은 것은 열쇠 해금·오픈 보이드 처리 등 → [map-generation.md §12](../architecture/map-generation.md)
- 장르 한 줄 정의, 1회 플레이 길이
- 아트 방향, 오디오

**에이전트는 이 항목들을 추측으로 채우지 않는다.** 의존하는 작업을 만나면 멈추고 질문한다.

## 3. 타깃

| 항목 | 값 |
| --- | --- |
| 주요 플랫폼 | **Steam / Windows x86_64** (확정) |
| 보조 플랫폼 | **macOS** — Apple Silicon 네이티브 지원 완료 |
| Steam 클라이언트 | **필수** — 미실행 시 멀티플레이 불가 |
| 최소 사양 | TBD |
| 목표 프레임 | TBD |
| 언어 | 한국어 (UI 텍스트 기준) |

## 4. 이번 개발 범위 (Scope)

### 포함 (In Scope)

| # | 항목 | 상태 |
| --- | --- | --- |
| 1 | 1인칭 이동 + 4인 멀티플레이 동기화 | 구현됨 / 2PC 실기 검증 대기 |
| 2 | Steam 로비 — 방 생성·6자리 코드 참가·친구 초대·준비·시작 | 구현됨 / 2PC 실기 검증 대기 |
| 3 | 조준 타겟팅 + 윤곽선 | 구현됨 |
| 4 | 홀드 방식 던지기 (1인 밀치기 / 2인 흡착·부양) | 구현됨 / 원격 재튜닝 필요 |
| 5 | 가구 간 물리 연쇄 | 부분 구현 |
| 6 | House_01 맵 + 방 프리셋 랜덤 배치 + 문 상호작용 | 구현됨 (단층) |
| 6-1 | **맵 생성 v0.3** — Floor·계단 A/B·Spawn Point·Work Room | **기획·규모 확정 / 오른쪽 MAP-1 그레이박스 1차 생성 + B/C 실내·가구·계단 생성기 코드 구현. 메뉴 실행·씬 저장·체감/수동 Play 검증 대기** → [roadmap §1.2 MAP](roadmap.md) |
| 7 | **아키텍처 정비** — 씬 재편·asmdef 분리·서비스 로케이터·UniTask | **진행 중** → [roadmap §2](roadmap.md) |
| 8 | **게임 루프 · 승패 조건** | **TBD — 설계 대기** |
| 9 | **유령·정신력 시스템** | 귀신 P1 + 정신력 코어·Game 후면 World Space 4인 숫자 모니터 구현, 정식 연출·콘텐츠 연결·귀신 밸런스 대기 |
| 10 | **두더지 스킬(탐지·공통 UI)** | 로컬 탐지 상태 머신·활성 마커·하이라이트 셰이더(**벽 투시 없음**)·탐지/굴착 공통 게이지·시전 파란빛 오버레이 코드 구현. 씬·프리팹·아이콘 배선은 정적 확인, 수동 Play 검증 대기, 손·레이저 포인터 애니메이션과 작업 시스템 대상 정의는 MS-14·MS-5 |

### 제외 (Out of Scope)

프로토타입 시기의 제외 목록 중 **아래는 본 프로젝트에서 다시 열린다.**

| 항목 | 프로토타입 | 본 프로젝트 |
| --- | --- | --- |
| 게임 루프 / 승패 / 점수 | 제외 | **In Scope (설계 대기)** |
| 유령, 적, AI, 내비메시 | 제외 | **In Scope (유령 P1 구현, 정식 AI·NavMesh 설계 대기)** |
| 3인 이상 접속 | 제외 | **4인까지 In Scope — 구현됨** |
| 캐릭터 모델·애니메이션 | 제외 | 보류 (캡슐 유지) |
| 사운드·파티클·포스트 프로세싱 | 제외 | 보류 |
| 매치메이킹(랜덤 매칭) | 제외 | 여전히 제외 — 방 코드/초대만 |
| 재접속·호스트 마이그레이션 | 제외 | 여전히 제외 |
| 클라이언트 예측/롤백 | 제외 | 여전히 제외 → [ADR-0010](../architecture/decisions/ADR-0010-server-authoritative-furniture-physics.md) |
| CI / 빌드 파이프라인 | 제외 | 보류 (테스트 어셈블리 구축 후 재검토) |

명시적으로 "제외"에 적힌 항목은 요청이 있어도 먼저 범위 재확인을 거친다.

## 5. 성공 기준

| # | 기준 | 상태 |
| --- | --- | --- |
| 1 | 서로 다른 Steam 계정 2개가 방 코드/초대로 만나 같은 맵에서 논다 | ⏳ PC 2대 검증 대기 |
| 2 | 원격 접속(SDR 릴레이 경유)에서도 던지기 손맛이 성립한다 | ⏳ 미검증 — 최대 리스크 |
| 3 | 4인이 동시에 접속해 가구를 던져도 호스트 프레임·대역폭이 버틴다 | ⏳ 미검증 |
| 4 | 승패 조건이 있는 한 판이 시작~종료까지 완주된다 | ❌ 설계 대기 |
| 5 | Windows·macOS 빌드가 모두 정상 동작한다 | ⏳ 부분 검증 |

## 6. 제약 사항

| 유형 | 내용 |
| --- | --- |
| 인원 | 1~4명 |
| 기술 | NGO 확정 → 서버 권위 구조 강제 |
| 기술 | Steam P2P 확정 → 데디케이티드 서버 없음, **호스트 이탈 시 세션 종료**, 호스트 치팅 방어 불가 |
| 기술 | 가구 물리 서버 권위 → 게스트는 왕복 지연을 감수. **로컬 기준 튜닝 금지** |
| 테스트 | Steam 실경로 2인 검증에 **PC 2대 + Steam 계정 2개** 필요. 같은 계정으로는 불가 |
| 테스트 | EditMode·PlayMode 테스트 어셈블리 운영. 기능별 과거 결과는 각 문서에 기록하며, 두더지 스킬 마지막 완료 복제 결과는 EditMode 165/172 passed·1 failed·6 skipped(최신 재시도는 패키지/라이선스 단계에서 중단) |
| 에셋 | 전부 프리미티브. 외주/스토어/자체 제작 여부 TBD |
| 패키지 | FacepunchTransport는 **벤더링 사본**. upstream 업데이트가 자동으로 오지 않는다 |

## 7. 관련 문서

- 게임 규칙·밸런스: [gdd.md](gdd.md)
- 정신력 상세 규칙: [sanity-system.md](sanity-system.md)
- 두더지 스킬(탐지·굴착) 규칙: [mole-skill-system.md](mole-skill-system.md) — 기획서 1.0 + 저장소 반영 1.2
- 맵 생성 규칙(House / Floor / Room Preset / Spawn Point): [../architecture/map-generation.md](../architecture/map-generation.md)
- 진행 상황: [roadmap.md](roadmap.md)
- 구조: [../architecture/overview.md](../architecture/overview.md)
- 유령 P1 구조·임시 수치·검증: [../architecture/ghost-prototype.md](../architecture/ghost-prototype.md)
- 정신력 코드 구조·검증: [../architecture/sanity-system.md](../architecture/sanity-system.md)
- 기술 결정 배경: [../architecture/decisions/](../architecture/decisions/README.md)

---

최종 갱신: 2026-09-05 (맵 v0.3 규모 확정 + B/C 실내 프로토타입 생성기 코드 + 탐지 스킬·공통 스킬 UI·시전 파란빛 오버레이 구현 — 에디터 실행·씬 저장·레이저 포인터 애니메이션·수동 검증 대기)
