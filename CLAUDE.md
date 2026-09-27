# CLAUDE.md

> Claude Code가 이 저장소에서 가장 먼저 읽는 파일. **매 요청마다 컨텍스트에 실리므로 규칙과 안내만 둔다.**
> 진행 상황은 [roadmap.md §0](docs/project/roadmap.md), 절차·세부는 [docs/](docs/README.md), 공통 규약은 [AGENTS.md](AGENTS.md).

GhostHunter — 1인칭 멀티플레이 "가구 던지기" 게임. 규칙은 [gdd.md](docs/project/gdd.md) — **`TBD`는 임의로 정하지 않고 묻는다.**

## 1. 스택

Unity **6000.3.20f1** · URP 17.3 · Input System 1.19(신규 전용) · NGO **2.13.1**(서버 권위) ·
Steam 트랜스포트 facepunch(임베드+패치) · 로컬 UTP 2.7.3(개발 전용, [ADR-0011](docs/architecture/decisions/ADR-0011-local-transport-path.md)) ·
Facepunch.Steamworks 2.5.2 · UniTask 2.5.11 · PhysX · Force Text 직렬화.
씬: Bootstrap(상주) → Title → Lobby(일반 로비) → InGameLobby ⇄ Stage1 → Result → InGameLobby …
(additive, 세션은 스테이지 사이에도 유지 — [ADR-0018](docs/architecture/decisions/ADR-0018-persistent-session-in-game-lobby.md)).
구 Game 씬은 프로토타입 검증용 **ProtoTypeGame**, 스테이지 판정은 `SceneId.IsStage()`([ADR-0019](docs/architecture/decisions/ADR-0019-stage1-scene-split.md)). 구조: [overview.md](docs/architecture/overview.md)

## 2. 라우팅 — 해당 작업 행의 문서만, 관련 절부터 읽는다

전체 읽기 목록이 아니다. 공통 행도 그 작업을 할 때만 읽는다. 문서가 길면 목차에서 절을 찾는다.
코드와 문서가 충돌하면 [AGENTS.md §1](AGENTS.md)대로 보고한다.

| 작업 | 문서 |
| --- | --- |
| 기능 설계·스펙 | `project/gdd.md`(규칙) · `project/overview.md`(범위) · `project/roadmap.md`(상태·우선순위) 중 해당하는 것 |
| 시스템·폴더·어셈블리·씬 구조 | `architecture/overview.md`, `architecture/decisions/` |
| 네트워크(RPC·NetworkVariable) | `architecture/networking.md` |
| Steam·빠른 실행 절차 | `architecture/steam.md` |
| 일반 로비·인게임 로비·상점·스테이지 전환·Stage1 씬 | `project/stage-system.md` §1.1, ADR-0018·ADR-0019 |
| 스테이지 시작·종료·정산·호스트 이전 | `project/stage-system.md`, 구현 시 `architecture/networking.md`·ADR-0017 |
| 플레이어 이동·시점·입력·캐릭터 모델 | `architecture/player-controller.md` |
| 사망·시체·전멸 / 관전 | `project/death-system.md`·`architecture/death-system.md` / `project/spectator-system.md` |
| 잡기·부양·발사 / 가구 물리 | `architecture/throw-system.md` / `architecture/furniture-physics.md` |
| 가구 내구도 | `project/furniture-durability-system.md` |
| 가구 분해·조립 | `architecture/furniture-multidriver.md`, `project/furniture-multidriver-system.md` |
| 청소 | `project/cleaning-system.md`, `architecture/cleaning-system.md` |
| 귀신 / 정신력 | `project/ghost-system.md`·`architecture/ghost-prototype.md` / `project/sanity-system.md`·`architecture/sanity-system.md` |
| 플레이어 스킬 | `project/mole-skill-system.md` |
| 일시정지·나가기·끊김 / 퀵슬롯 | `architecture/pause-menu.md` / `architecture/quick-slot.md` (기획은 `project/` 같은 이름) |
| 음성 채팅 | `project/voice-chat-system.md`, `architecture/voice-chat.md` |
| 맵·방 프리셋·스폰·작업 대상 가구 | `architecture/map-generation.md` (미결정 §12) |
| C# / 에셋 / git | `conventions/code-style.md` / `conventions/unity-assets.md` / `conventions/git.md` |
| 절차·완료 기준 / 테스트 / 반복 작업 | `workflow/development-loop.md` / `workflow/testing.md` / `workflow/playbooks.md` |

(경로는 모두 `docs/` 아래)

## 3. 하드 룰 (위반 금지)

**파일·에셋**
- `Assets/_Project/` 래퍼 금지, 평면 배치([ADR-0007](docs/architecture/decisions/ADR-0007-flat-assets-layout.md)). 서드파티는 `Assets/Plugins/`·`Assets/ThirdParty/`(구조 변경 금지). 문서는 `docs/`.
- `Packages/com.community.netcode.transport.facepunch/`는 벤더링 사본이다. 손대기 전 [PATCHES.md](Packages/com.community.netcode.transport.facepunch/PATCHES.md)를 읽고 수정은 거기 기록, 우리 기능 추가 금지([ADR-0006](docs/architecture/decisions/ADR-0006-facepunch-transport-embed.md)).
- 씬·프리팹·에셋(`.unity`·`.prefab`·`.asset`)은 텍스트로 직접 고쳐도 되고, 파일은 셸(`git mv`/`rm`)로 옮기거나 지워도 된다.
  이때 `.meta`는 짝으로 함께 옮기고·지우고·커밋하며 `guid`는 바꾸지 않는다. 저장된 씬·프리팹이 원본이다 — 설치·생성 도구는 없다([ADR-0020](docs/architecture/decisions/ADR-0020-remove-one-off-editor-setup-tools.md)).
- `Library/`·`Temp/`·`Logs/`·`UserSettings/`·`*.csproj`·`*.sln*`은 생성물. `ProjectSettings/**`·`Packages/manifest.json` 수정은 사용자 확인 후(재임포트 고지).
- **씬의 어떤 오브젝트도 스케일이 1이 아니면 안 된다.** 네트워크 프리팹·씬 `NetworkObject`의 GlobalObjectIdHash 함정은 `unity-assets.md`의 절차를 따른다.

**코드**
- 레거시 `Input.*`, `GameObject.Find`, `SendMessage`, 매 프레임 `Camera.main` 금지. 참조는 직렬화 또는 명시적 주입.
- `async void` 금지 — `async UniTaskVoid` + `.Forget()`. 새 코루틴 금지. 레거시 `[ServerRpc]`/`[ClientRpc]` 금지 — `[Rpc(SendTo.…)]`.
- `Steamworks` 네임스페이스는 Steam 레이어에만(Gameplay·UI는 `ISteamLobbyService`). 맥에는 Valve 폐기 API가 없다(`steam.md`).
- 씬 전환은 `ISceneFlow` 경유. 서비스는 `static Instance` 금지, 등록은 `SceneInstaller`에서만.
- 튜닝 수치는 `ScriptableObject` 설정 에셋에. 네임스페이스·폴더에 `Debug` 금지(`DebugTools` — `UnityEngine.Debug`를 가린다).
- 트랜스포트 모드 직렬화 기본값은 항상 `Steam`. 로컬 검증은 F1 HUD로 바꾸고 저장하지 않는다(`TransportModeBuildGuard`).
- 새 어셈블리 경계는 `.asmdef`와 함께 만들고 `architecture/overview.md`를 갱신한다.

**게임플레이**
- 물리 권위는 서버에만. 클라이언트는 의도만 RPC로 보내고 `Rigidbody`에 직접 힘을 가하지 않는다([ADR-0010](docs/architecture/decisions/ADR-0010-server-authoritative-furniture-physics.md)).
  예외는 플레이어 이동뿐([ADR-0008](docs/architecture/decisions/ADR-0008-owner-authoritative-player-movement.md)) — 새 예외는 ADR 필요.
- 던질 수 있는 가구는 씬에 배치된 실제 가구다. 런타임 스폰 금지([ADR-0009](docs/architecture/decisions/ADR-0009-scene-placed-level-objects.md)).
  가구·문은 프리팹 인스턴스(한 종류 = 한 프리팹, 벽 방향은 회전으로, 종류 추가는 새 프리팹을 만들어 `Furniture_Library`에 진열).
- 맵 기준은 **B안**(30×24m ×2층 + 다락 20×14m, `HousePlanC.png`). 작업량은 Target Furniture Type·Count로 관리한다.
- 침실은 `Room_Presets`를 서버가 `RoomSlots`로 **옮겨** 채운다(스폰 아님) — 침실에 손으로 가구를 놓지 않는다(원본은 `Furniture_Library`).
  세부: `map-generation.md`·`furniture-physics.md`.
- 세션 시작 순서(씬 로드 → `StartHost` → 로비 신호)는 MUST 지킨다(`steam.md` "빠른 시작").

**문서**
- 코드 변경이 문서 서술을 무효화하면 같은 작업 안에서 갱신한다. 되돌리기 어려운 선택은 ADR. 트리거: `playbooks.md` PB-07.
- 진행 상황·검증 결과는 이 파일이 아니라 `roadmap.md` §0과 해당 문서에 적는다.

## 4. 검증

에디터가 열려 있으면 원본 프로젝트의 batchmode가 실패한다 — 에디터를 닫아 달라고 하기 전에 검증용 복제 프로젝트로 돌린다.
명령과 복제본 검증은 [testing.md](docs/workflow/testing.md). 작업 절차는 [AGENTS.md §2](AGENTS.md) — 검증하지 못했으면 "동작한다"고 말하지 않는다.

## 5. 사용자에게 반드시 확인할 것

[AGENTS.md §6](AGENTS.md)에 더해: Steam App ID 변경·트랜스포트 패치 추가, 씬/프리팹 대량 변경,
**`main` 직접 커밋 금지**(브랜치를 먼저 판다 → [git.md](docs/conventions/git.md)).
