# 로드맵 & 태스크 보드

> 에이전트는 작업 시작 시 여기서 대상 태스크를 확인하고, 완료 시 상태를 갱신한다.
> 상태: `대기` → `진행중` → `완료` / `보류`

---

## 1. 마일스톤

| # | 마일스톤 | 목표 | 완료 기준 | 상태 |
| --- | --- | --- | --- | --- |
| M0~M6 | 프로토타입 | 던지기 메커닉 검증 | 아래 §3 | **완료** (실기 검증 항목 제외) |
| **MIG** | **아키텍처 정비** | 본 프로젝트 구조로 이관 | §2 전 항목 | **진행중** |
| M7 | 가구 간 물리 + 정리 | 연쇄 충돌이 두 클라이언트에서 자연스럽게 | §3 M7 | 대기 |
| M8 | **게임 설계** | 루프·승패·유령 확정 | [gdd.md §1·§2·§5](gdd.md) TBD 해소 | **대기 (사용자 입력 필요)** |
| M9 | 실기 검증 | Steam 2PC 전 경로 통과 | [PB-08](../workflow/playbooks.md) 전항 | 대기 |
| M10 | 게임플레이 완성 | 승패 조건까지 동작 | 매치 시작→종료 완주 | 대기 |
| M11 | 폴리싱 & 빌드 | 배포 가능 상태 | Windows·macOS 빌드 + 성능 목표 | 대기 |

> **M8이 병목이다.** 승패 조건이 없으면 `Result` 씬·라운드 구조·난입 정책·스폰 규칙이 전부 막힌다.

---

## 2. 현재 스프린트 — MIG: 아키텍처 정비

2026-08-19 결정으로 씬 아키텍처·어셈블리 구조·의존성 획득 방식이 바뀌었다.
관련 ADR: [0003](../architecture/decisions/ADR-0003-service-locator.md)
· [0004](../architecture/decisions/ADR-0004-multi-scene-additive.md)
· [0005](../architecture/decisions/ADR-0005-unitask-async.md)
· [0007](../architecture/decisions/ADR-0007-flat-assets-layout.md)
· [0009](../architecture/decisions/ADR-0009-scene-placed-level-objects.md)

### 2.1 순서 (의존 관계)

```
[완료] MIG-0 문서 재편 · MIG-9 Steamworks 격리 · MIG-10 MCP · MIG-4 UniTask · MIG-8 RPC 정리
                                    │
                                    ▼
                          MIG-1 Core 인프라 ──▶ MIG-2 씬 재편 ──▶ MIG-3 리그 흡수 ──▶ MIG-5 asmdef 분리
                                                     │                                      │
                                                     └──▶ MIG-6 프리팹화                    └──▶ MIG-7 테스트
```

**MIG-4(UniTask)를 MIG-1보다 먼저 했다.** Core 인프라(`SceneFlowController`·`Services`)를 새로 쓸 때
이미 UniTask가 있어야 두 번 쓰지 않는다.

### 2.2 태스크

| # | 태스크 | 산출물 | 선행 | 상태 |
| --- | --- | --- | --- | --- |
| MIG-0 | 문서 트리 재편 + ADR 작성 | `docs/**` | — | **완료 (2026-08-19)** |
| MIG-9 | **`Steamworks` 격리** — UI 3파일에서 제거 | `Core/Steam/ISteamLobbyService.cs` 외 | — | **완료 (2026-08-19)** |
| MIG-10 | Unity MCP 연결 | `.mcp.json` | — | **완료 (2026-08-20)** — 버전 고정만 남음 |
| MIG-4 | **UniTask 전환** — `async void` 6건, 코루틴 2건 | `manifest.json`, asmdef + 6파일 | — | **완료 (2026-08-20)** — 컴파일 검증됨 |
| MIG-1 | Core 인프라 — `Services`, `SceneInstaller`, `ISceneFlow`, `SceneReference`/`SceneNameSO` | `Scripts/Core`, `Scripts/Data`, `Scripts/Systems` | MIG-4 ✅ | **다음 작업** |
| MIG-2 | **씬 재편** — `Bootstrap` 신규, `MainMenu`→`Title`, `Prototype`→`Game`, `Result` 신규, additive 전환 | `Assets/Scenes/**` | MIG-1 | 대기 |
| MIG-3 | `NetworkRig` 프리팹 → `Bootstrap` 씬 흡수, `static Instance` 6건 제거 | `Bootstrap.unity`, `ConnectionManager` 외 | MIG-2 | 대기 |
| MIG-5 | **asmdef 레이어 분리** + 폴더 이동 | asmdef 8개 | MIG-3 | 대기 |
| MIG-6 | 가구·문 프리팹화 + 생성 도구 전환 | `Assets/Prefabs/Furniture/**`, `HousePrototypeBuilder` | MIG-2 | 대기 |
| MIG-7 | 테스트 어셈블리 + 스모크 테스트 이관 | `Assets/Tests/**` | MIG-5 | 대기 |
| MIG-8 | 레거시 RPC 속성 → `[Rpc(SendTo.…)]` | `GrabController` 3 · `FurnitureLauncher` 1 · `PlayerNetworkSpawn` 1 | — | **완료 (2026-08-20)** — 컴파일 검증됨 |
| MIG-11 | 로비 정책 반영 — `gh_game` 키, 가시성, 난입, 접속 승인 검증 | `SteamLobbyManager`, `ConnectionManager` | [ADR-0012](../architecture/decisions/ADR-0012-room-code-and-lobby-visibility.md) 확정 | **대기 (결정 필요)** |
| MIG-12 | 브랜치 규약 적용 — `Feature/Prototype` → kebab-case | git | — | 대기 |

### 2.3 결정 대기 (에이전트가 진행할 수 없는 것)

| # | 항목 | 막히는 태스크 |
| --- | --- | --- |
| D-1 | 로컬(UTP) 경로 존치 방식 | [ADR-0011](../architecture/decisions/ADR-0011-local-transport-path.md) → MIG-3, MIG-7 |
| D-2 | 로비 가시성 (Public vs FriendsOnly) | [ADR-0012](../architecture/decisions/ADR-0012-room-code-and-lobby-visibility.md) §3 → MIG-11 |
| D-3 | 난입 허용 여부 | [ADR-0012](../architecture/decisions/ADR-0012-room-code-and-lobby-visibility.md) §4 → MIG-11 |
| D-4 | 게임 루프·승패 조건 | [gdd.md §2](gdd.md) → M8 전체 |
| D-5 | 유령의 정체 (플레이어/AI) | [gdd.md §5](gdd.md) → M8 전체 |

---

## 3. 완료된 마일스톤 (프로토타입)

<details>
<summary>M0 — 프로젝트 정지 작업</summary>

- [x] `Assets/Scripts/` 하위 폴더 생성
- [x] `GhostHunter.Runtime` / `GhostHunter.Editor` asmdef 생성 → **MIG-5에서 8개로 분리 예정**
- [x] Project Settings에 레이어 추가: `Player`, `Furniture`
- [x] 바닥·벽·스폰 포인트 배치
- [x] `Assets/Scripts/Temp.cs` 삭제
- [x] 프로젝트 설정: Company Name `GhostHunter`
- [x] 씬 분리 — `MainMenu` / `Lobby` / `Prototype` → **MIG-2에서 재편 예정**
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
- [x] `NetworkManager` 세팅 — `NetworkRigSetup` 에디터 도구
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
- [ ] 프리팹 에셋화 → **MIG-6**

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
| 2026-08-15 | 메인메뉴·로비 씬 추가 | 네트워크 리그를 프리팹/부트스트랩 방식으로 전환 |
| 2026-08-16 | House_01 맵 생성 + 문 상호작용 | 더미 큐브 제거, 씬 배치 가구로 전환 |
| 2026-08-17 | macOS Steam 네이티브 지원 | Apple Silicon 유니버설 바이너리 |
| 2026-08-18 | Steam 접속 빌드 지문 검사 | 트랜스포트 패치 5 + 로비 4인 확장 |
| 2026-08-19 | **본 프로젝트 승격 + 문서 재편** | AlienGhost 규약 체계 이관, ADR 0001~0012 |
| 2026-08-19 | MIG-9 `Steamworks` 격리 | UI 레이어에서 Steamworks 참조 제거 |
| 2026-08-20 | MIG-10 Unity MCP 연결 | 브리지 기동 + `.mcp.json` 등록 |
| 2026-08-20 | MIG-4 UniTask 전환 | `async void` 6건 · 코루틴 2건 제거, 취소 토큰 적용 |
| 2026-08-20 | MIG-8 통합 RPC 속성 전환 | 레거시 5건 제거. **호출 권한 기본값 역전 함정** 발견·차단 |

---

최종 갱신: 2026-08-20
