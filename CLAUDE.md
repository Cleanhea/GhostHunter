# CLAUDE.md

> Claude Code가 이 저장소에서 작업을 시작할 때 **가장 먼저 읽는 파일**.
> 도구 중립 에이전트 규약은 [AGENTS.md](AGENTS.md), 세부 컨텍스트는 [docs/](docs/README.md).

GhostHunter — 1인칭 멀티플레이 "가구 던지기" 게임.

---

## 1. 프로젝트 스냅샷

| 항목 | 값 | 비고 |
| --- | --- | --- |
| Unity | **6000.3.20f1** (Unity 6.3) | 버전 변경 시 팀 전체 합의 필요 |
| 렌더 파이프라인 | URP 17.3 | PC 프리셋(`Assets/Settings/PC_RPAsset.asset`) |
| 입력 | Input System 1.19 (**신규 전용**, `activeInputHandler: 1`) | 레거시 `Input.*`는 런타임 예외 |
| 네트워킹 | Netcode for GameObjects **2.13.1** | 서버 권위 |
| Steam 트랜스포트 | `com.community.netcode.transport.facepunch` | **임베드 + 5건 패치** — 아래 주의 |
| 로컬 트랜스포트 | `com.unity.transport` 2.7.3 (UTP) | 개발 전용으로 존치 + 빌드 가드 → [ADR-0011](docs/architecture/decisions/ADR-0011-local-transport-path.md) |
| Steam 래퍼 | Facepunch.Steamworks 2.5.2 | 트랜스포트 패키지에 번들 |
| 비동기 | **UniTask 2.5.11** | [ADR-0005](docs/architecture/decisions/ADR-0005-unitask-async.md) — 전환 완료 |
| 에디터 브리지 | Unity MCP (CoplayDev) | [workflow/unity-mcp.md](docs/workflow/unity-mcp.md) — 연결됨. 버전 `#main` 추적 중 |
| 물리 | PhysX (built-in 3D) | DOTS Physics 사용 안 함 |
| 에셋 직렬화 | Force Text (`m_SerializationMode: 2`) | 유지할 것 |
| 버전 | `0.1.0` | Company/Product = `GhostHunter` |

**게임의 목표·루프·승패 조건은 [docs/project/gdd.md](docs/project/gdd.md)에 정의한다.
아직 `TBD`인 항목이 있으면 임의로 정하지 말고 사용자에게 확인한다.**

> **⚠️ 2026-08-19: 아키텍처 정비 진행 중.** 씬 구조·어셈블리 분리·의존성 획득 방식이 바뀌었고
> 코드 이관은 아직이다. `docs/`는 **목표 구조**를 기술한다. 현재 코드와 다르면 코드가 미이관 상태다.
> 진행 상황: [docs/project/roadmap.md §2](docs/project/roadmap.md)

---

## 2. 컨텍스트 라우팅 — 작업 전 필독 문서

작업을 시작하기 전, 아래 표에서 해당 행의 문서를 **먼저 읽는다**.

| 작업 유형 | 필독 문서 |
| --- | --- |
| 신규 기능 설계 / 스펙 논의 | `docs/project/overview.md`, `docs/project/gdd.md`, `docs/project/roadmap.md` |
| 시스템·폴더·어셈블리·씬 구조 변경 | `docs/architecture/overview.md`, `docs/architecture/decisions/README.md` |
| 네트워크(RPC·NetworkVariable·동기화) | `docs/architecture/networking.md` |
| Steam 연동(초기화·로비·연결·빌드) | `docs/architecture/steam.md` |
| 플레이어 이동·시점·입력 | `docs/architecture/player-controller.md` |
| 잡기·부양·발사 | `docs/architecture/throw-system.md` |
| 가구 물리·아웃라인 | `docs/architecture/furniture-physics.md` |
| 맵·방 프리셋·스폰 포인트 | `docs/architecture/map-generation.md` |
| C# 코드 작성 / 리팩터링 | `docs/conventions/code-style.md` |
| 프리팹·씬·ScriptableObject·에셋 | `docs/conventions/unity-assets.md` |
| 커밋·브랜치·PR | `docs/conventions/git.md` |
| 작업 절차 / 완료 기준 | `docs/workflow/development-loop.md` |
| 테스트 작성·실행 | `docs/workflow/testing.md` |
| **Unity 에디터 조작(MCP 도구 사용)** | `docs/workflow/unity-mcp.md` — 사용 전 필독 |
| 반복 작업(신규 시스템·NetworkBehaviour 등) | `docs/workflow/playbooks.md` |
| 기술 선택의 배경이 궁금할 때 | `docs/architecture/decisions/` (ADR) |

---

## 3. 하드 룰 (위반 금지)

### 3.1 파일 배치
- **`Assets/_Project/` 래퍼를 쓰지 않는다.** 평면 배치 유지 → [ADR-0007](docs/architecture/decisions/ADR-0007-flat-assets-layout.md)
- 서드파티 에셋은 `Assets/Plugins/` 또는 `Assets/ThirdParty/`에 두고 폴더 구조를 임의로 바꾸지 않는다.
- 문서는 `docs/` (Assets 밖)에 둔다 — `.meta`가 생기지 않는다.

### 3.2 벤더링 패키지
- **`Packages/com.community.netcode.transport.facepunch/`는 벤더링된 서드파티 사본이다.**
  패치 없이는 컴파일되지 않는 upstream 버그가 있어 임베드했다. 손대기 전에
  [PATCHES.md](Packages/com.community.netcode.transport.facepunch/PATCHES.md)를 읽고,
  수정하면 거기에 기록한다. **이 폴더에 우리 기능을 추가하지 않는다.**
  → [ADR-0006](docs/architecture/decisions/ADR-0006-facepunch-transport-embed.md)

### 3.3 Unity 특수 파일
- **`.meta` 파일을 직접 생성·삭제·편집하지 않는다.** Unity가 관리한다. **항상 짝으로 커밋**한다.
- **`.unity` / `.prefab` / `.asset` YAML을 손으로 편집하지 않는다.**
- 파일/폴더 이동·삭제는 **Unity 에디터 안에서** 한다. 셸에서 `mv`/`rm` 하지 않는다.
- 씬·프리팹·에셋을 바꿔야 하면 **Unity MCP**(Unity API 경유라 GUID 안전) 또는
  `Assets/Scripts/Editor/`의 생성 도구를 쓴다. 둘의 역할 구분은 `docs/workflow/unity-mcp.md` §4.1.
- **C# 스크립트는 MCP가 아니라 일반 파일 도구(Read/Edit/Write)로 작성한다.**
- `Library/`, `Temp/`, `Logs/`, `UserSettings/`, `*.csproj`, `*.sln*`은 **생성물**이다.
- `ProjectSettings/**` 및 `Packages/manifest.json` 수정은 **사용자 확인 후** 진행하고, 재임포트가 필요함을 알린다.

### 3.4 코드
- 레거시 `Input.GetKey` / `Input.GetAxis` 금지 — Input System만 사용한다.
- `GameObject.Find`, `SendMessage`, `Camera.main`(매 프레임) 금지. 참조는 직렬화 또는 명시적 주입.
- **`async void` 금지** — `async UniTaskVoid` + `.Forget()`. 새 코루틴 금지.
- **레거시 `[ServerRpc]`/`[ClientRpc]` 금지** — `[Rpc(SendTo.…)]` 통합 속성을 쓴다.
- **`Steamworks` 네임스페이스는 Steam 레이어에만 존재한다.** Gameplay·UI는 `ISteamLobbyService`로 받는다.
- **씬 전환은 `ISceneFlow`를 경유한다.** `SceneManager`를 직접 호출하지 않는다.
- 서비스는 자기 `static Instance`를 갖지 않는다. 등록은 `SceneInstaller`에서만.
- 튜닝 수치는 코드 상수가 아니라 `ScriptableObject` 설정 에셋에 둔다.
- **트랜스포트 모드의 직렬화 기본값은 항상 `Steam`이다.** 로컬 검증은 플레이 중 F1 HUD로 전환하고
  저장하지 않는다. 릴리스 빌드는 `TransportModeBuildGuard`가 막는다.
- 새 어셈블리 경계를 만들 땐 `.asmdef`를 함께 추가하고 `docs/architecture/overview.md`를 갱신한다.

### 3.5 게임플레이 규칙
- **물리 상태의 권위는 서버(호스트)에만 있다.** 클라이언트는 입력/의도만 RPC로 보낸다.
  **클라이언트에서 `Rigidbody`에 직접 힘을 가하지 않는다.** → [ADR-0010](docs/architecture/decisions/ADR-0010-server-authoritative-furniture-physics.md)
- **예외는 플레이어 이동 하나뿐**(소유자 권위) → [ADR-0008](docs/architecture/decisions/ADR-0008-owner-authoritative-player-movement.md).
  새 예외를 만들려면 ADR이 필요하다.
- **던질 수 있는 가구는 씬에 배치된 실제 가구다.** 런타임 스폰하지 않는다 → [ADR-0009](docs/architecture/decisions/ADR-0009-scene-placed-level-objects.md)
- **맵은 `HousePrototypeBuilder.MapScale`(현재 ×2)로 평면(X·Z)만 넓힌다.** 배율은 **좌표에만** 곱한다 —
  트랜스폼 스케일을 쓰면 개구부 폭과 벽 높이까지 늘어난다. 벽 높이·두께·문 폭·창 크기·붙박이·계단·가구·
  플레이어·투척 수치는 배율을 받지 않는다. **씬의 어떤 오브젝트도 스케일이 1이 아니면 안 된다.**
- **침실 2칸은 프리셋이 자동으로 채운다.** `Room_Presets` A·B·C 중 둘을 서버가 중복 없이 뽑아
  `House_01/RoomSlots`로 **옮긴다**(스폰이 아니다 — 중첩 `NetworkObject` 방지).
  침실에 손으로 가구를 놓지 않는다. 프리셋 가구는 방 남쪽 1.11m 띠를 비워야 한다.
- 생성 도구는 **방을 비운 채로** 집을 만들고, 가구는 `Furniture_Library`에 종류별 한 개씩 놓는다.
  방 배치는 거기서 복사해 붙여 넣는다 — **생성 도구에 방별 가구 좌표를 심지 않는다.**
  예외는 도면 배율 비교용 집(`House_01_OriginalScale_Right`)뿐이다.

### 3.6 문서 동기화
- **코드 변경이 문서의 서술을 무효화하면, 같은 작업 안에서 문서를 갱신한다.** 문서 갱신 없는 구조 변경은 미완료다.
- 되돌리기 어려운 기술 선택은 **ADR을 남긴다** → [docs/architecture/decisions/](docs/architecture/decisions/README.md).
- 갱신 트리거 표: [docs/workflow/playbooks.md PB-07](docs/workflow/playbooks.md)

---

## 4. 폴더 구조

```
Assets/
├─ Scenes/     목표: Bootstrap → Title → Lobby → Game → Result
│              현재: MainMenu → Lobby → Prototype  (MIG-2에서 재편)
├─ Scripts/    목표 레이어: Core / Data / Gameplay / Networking / UI / Systems / DebugTools / Editor
│              현재: Core, Networking, Player, Interaction, Furniture, Map, UI, DebugTools, Editor
├─ Prefabs/    NetworkRig, Player
├─ Settings/   URP 에셋, Gameplay SO
├─ Materials/  Shaders/  Tests/(미생성)
```

- 폴더명이 `Debug`가 아니라 **`DebugTools`**인 이유: `GhostHunter.Debug` 네임스페이스는
  `UnityEngine.Debug`를 가려 그 안의 모든 `Debug.Log` 호출을 깨뜨린다.
- **macOS**: 네이티브 `libsteam_api.bundle`을 arm64 포함 유니버설로 교체했다(PATCHES.md 패치 4).
  대신 Valve가 폐기한 API 49개가 맥에서만 없다 — `QuickStatus().Ping` 같은 걸 쓰면 맥에서만
  `EntryPointNotFoundException`이 난다.

상세: [docs/architecture/overview.md](docs/architecture/overview.md)

---

## 5. 네트워크 프리팹의 GlobalObjectIdHash 함정

`PrefabUtility.SaveAsPrefabAsset`을 임시 **씬 오브젝트**에 대해 부르면, `NetworkObject.OnValidate`가
에셋이 아니라 씬 기준으로 해시를 계산해서 **모든 프리팹이 같은 해시**를 갖고 `m_InScenePlaced`가
true로 박힌다. NGO가 프리팹을 구분하지 못하는데 에러 없이 엉뚱한 게 스폰되는 식으로 조용히 깨진다.

**씬에 놓는 `NetworkObject`(가구, 문)도 같은 함정이 있다.** `OnValidate`는 씬이 저장되어 영구 ID가
생기고 그 씬이 Build Settings에 들어 있어야만(`buildIndex >= 0`) 해시를 계산한다.
생성 중인 새 씬은 둘 다 아니라 해시가 0으로 남는다.

절차와 검증 함수: [docs/conventions/unity-assets.md](docs/conventions/unity-assets.md)

---

## 6. 멀티플레이 빠른 시작

**메뉴 흐름 (Steam 필요):** `Assets/Scenes/MainMenu.unity`을 열고 플레이한다.

1. **방 생성** → Steam 로비 생성 + 6자리 방 코드 발급 → 로비 씬으로 이동
2. 상대는 **방 참가**에 방 코드를 입력하거나, 호스트의 **초대** 오버레이로 들어온다
3. 게스트가 **준비**를 누르면 호스트의 **게임 시작**이 활성화된다
4. 호스트가 시작하면 게임 씬을 로드한 뒤 `StartHost` → 로비에 시작 신호 → 게스트 접속

**세션 시작 순서(씬 로드 → StartHost → 로비 신호)는 MUST 지킨다.** 로비 씬에서 바로 `StartHost` 하면
플레이어가 스폰 지점 없는 씬에 스폰되고, 신호를 먼저 보내면 게스트가 세션 없는 호스트에 접속한다.

**단독 플레이 (Steam 없이):** `Prototype.unity`을 열고 플레이 → 접속 HUD에서 `Local` / **Host**. **F1**로 HUD 토글.

씬/프리팹 재생성: 메뉴 **`GhostHunter > 프로토타입 게임 생성`**, **`GhostHunter > 메인메뉴·로비 씬 생성`**.

상세: [docs/architecture/steam.md](docs/architecture/steam.md)

---

## 7. 명령어

Unity 에디터가 열려 있으면 프로젝트가 잠겨 batchmode 명령이 실패한다.
**에디터를 닫아 달라고 요청하기 전에, Unity MCP로 해결되는 일인지 먼저 확인한다.**

```powershell
$UNITY = "C:\Program Files\Unity\Hub\Editor\6000.3.20f1\Editor\Unity.exe"
$PROJ  = "C:\MainScreen\Dev\GitDirectory\GhostHunter"

# 컴파일 검증
& $UNITY -quit -batchmode -nographics -projectPath $PROJ -logFile -

# 에디터가 켜져 있을 때 컴파일 오류 확인
Get-Content "$env:LOCALAPPDATA\Unity\Editor\Editor.log" -Tail 80
```

테스트 실행: [docs/workflow/testing.md](docs/workflow/testing.md)

---

## 8. 작업 루프 요약

1. **컨텍스트 로드** — §2 라우팅 표의 문서를 읽는다
2. **범위 확인** — 요구가 모호하면 추측 대신 질문한다(특히 `TBD` 항목)
3. **계획** — 2단계 이상이면 변경할 파일 목록을 먼저 제시한다
4. **구현** — 하드 룰(§3) 준수. 기존 코드 스타일에 맞춘다
5. **검증** — 컴파일 → 테스트 → 필요 시 사용자에게 에디터 확인 요청.
   **검증하지 못했으면 "동작한다"고 말하지 않는다**
6. **기록** — 영향받은 문서 갱신, 필요 시 ADR 작성, 커밋 컨벤션 준수

상세: [docs/workflow/development-loop.md](docs/workflow/development-loop.md)

---

## 9. 사용자에게 반드시 확인할 것

Claude가 단독으로 결정하지 않는 항목:

- 게임 디자인 결정(밸런스 수치, 룰 변경, 신규 메커닉) — 특히 `gdd.md`의 `TBD`
- 패키지 추가/삭제, Unity 버전 변경, 렌더 파이프라인 설정 변경
- Steam App ID 변경, 트랜스포트 패치 추가
- 씬/프리팹 대량 변경, 폴더 구조 대규모 이동(GUID 참조 깨질 위험)
- `git push`, 브랜치 강제 갱신, 커밋 되돌리기
- **`main` 직접 커밋은 금지다.** 브랜치를 먼저 판다 → [docs/conventions/git.md](docs/conventions/git.md)
