# ADR-0018: 인게임 로비에서 세션을 열고 스테이지 사이에도 유지한다

- **상태**: Accepted — 코드·설치 도구·자동 검증 완료, Steam 다인 실기 검증 대기
- **날짜**: 2026-09-28
- **결정자**: 사용자(인게임 로비 신설 · 캐릭터가 돌아다니는 공간 · 상점은 방장만 · 인게임 로비부터 참가 차단 · 정산 뒤 인게임 로비)
- **관련**: [스테이지 시스템 기획서](../../project/stage-system.md), [ADR-0004 멀티 씬](ADR-0004-multi-scene-additive.md),
  [ADR-0017 호스트 이전](ADR-0017-host-migration.md), [네트워크 구조 §3.5·§3.6](../networking.md)
- **후속**: [ADR-0019](ADR-0019-stage1-scene-split.md) — `Game` 은 `ProtoTypeGame` 으로 이름이 바뀌었고, 인게임 로비의 출발 대상은
  `Stage1` 이다. 아래 본문의 "Game"은 당시 스테이지 씬을 뜻한다.
- **후속(2026-09-28)**: 이 ADR이 언급하는 설치·생성 도구(`HousePrototypeBuilder`·`…Setup`·`GhostHunter > …` 메뉴)는 [ADR-0020](ADR-0020-remove-one-off-editor-setup-tools.md)으로 삭제됐다.

## 배경

이전 흐름은 `Title → Lobby(Steam 로비만) → Game → Result → Lobby → Game …` 이었다. 스테이지마다 세션을 새로 열고
(`Game 씬 로드 → StartHost → 로비 신호`), 정산 뒤 `Lobby` 로 돌아가며 세션을 끊었다. 상점은 `Lobby` 에 있었다.

사용자는 **일반 로비**(방 만들기·참가·준비 — 처음 한 번)와 **인게임 로비**(스테이지 사이에 머무는 곳 — 상점·출발)를
나누고, 흐름을 `일반 로비 → 인게임 로비 → 인게임 → 인게임 로비 → 인게임 …` 으로 바꾸기로 했다. 인게임 로비는
**캐릭터가 돌아다니는 공간**이어야 하므로 세션이 살아 있어야 한다.

## 결정

1. **세션은 일반 로비의 "게임 시작"에서 한 번 연다.** 순서는 그대로 `씬 로드 → StartHost → 로비 신호`이되, 올리는 씬이
   `Game` 이 아니라 **`InGameLobby`** 다. 게스트는 NGO 씬 동기화로 인게임 로비에 들어온다. 이 시점부터 Steam 로비 참가를
   닫는다(기존 `MarkGameStarted` 그대로) — 중도 참가는 일반 로비에서만 된다.
2. **인게임 로비 ⇄ 스테이지 전환은 세션을 끊지 않고 NGO 씬 전환으로 한다.** 진입점은 `IStageSessionFlow`
   (`StageSessionFlow`, Bootstrap 에 런타임 추가·등록). 호스트만 시작한다.
   - `StartStage()` — 인게임 로비 → Game (인게임 로비 단말기의 "스테이지 출발")
   - `ReturnToInGameLobby()` — Game(ESC `스테이지 나가기`)·Result(정산 "인게임 로비로 이동") → 인게임 로비
3. **전환마다 플레이어를 디스폰하고 새 씬에서 다시 스폰한다.** 플레이어 컴포넌트는 `OnNetworkSpawn` 에서 그 씬의
   서비스(`IPlayerSpawnRegistry`·`ILocalPlayerContext`·`ISanityTeamService`·`IVoiceChatService`)를 붙잡는다.
   NGO 는 언로드되는 씬의 동적 오브젝트를 새 활성 씬으로 옮겨 살려 두므로, 그대로 두면 사라진 이전 씬 서비스를 계속 쓴다.
   재스폰은 `NetworkConfig.PlayerPrefab` 을 `InstantiateAndSpawn(isPlayerObject: true)` 한다. **죽은 플레이어부터**
   디스폰한다 — 살아 있는 사람을 먼저 빼면 정신력 팀이 남은 사망자만 보고 가짜 전멸 정산을 낸다.
4. **인게임 로비 ⇄ Game 사이에서는 이전 씬을 먼저 내리고 새 씬을 올린다**(`SceneFlowController.UnloadsBeforeLoad`).
   두 씬의 설치 컴포넌트가 같은 서비스를 등록하므로 겹쳐 올리면 두 번째 등록이 충돌한다. 그 사이에는 Bootstrap 만 남는다.
   Game → Result 는 예전처럼 겹쳐 올린다 — 정산 화면의 전원 음성이 스테이지 플레이어를 그대로 쓴다.
5. **인게임 로비 씬**(`Assets/Scenes/InGameLobby.unity`)은 설치 도구 `GhostHunter > 인게임 로비 씬 생성`이 만든다.
   회색 박스 단칸방, 스폰 4곳, 상점·출발 단말기(`StageLobbyTerminal`), `InGameLobbyInstaller`(Game 과 같은 서비스 계약,
   귀신·청소·가구 없음), 개요 카메라, 로비 화면(`InGameLobbyPanel`), 일시정지 메뉴. **씬 배치 NetworkObject 는 두지 않는다.**
6. **상점은 인게임 로비로 옮기고 방장만 산다**(`StageShopRules`). 게스트는 잔액·보유를 본다. 일반 로비 상점 UI 와
   게스트 → 방장 P2P 구매 요청은 없앴다(받은 패킷은 읽어서 버린다). 방의 정산 이력도 인게임 로비에 보인다.
7. **음성**: 인게임 로비는 정산 화면과 같은 전원 채널이다(`LobbyVoiceService` 가 Result 와 같이 맡는다).

## 결과

- 일반 로비는 방을 모으고 시작하는 곳으로만 남는다. 세션이 끝나는 것은 전원이 나가거나 연결이 끊길 때다.
- 개발 HUD(F1) Local → Host 도 인게임 로비에서 연다. 스테이지는 단말기에서 출발한다.
  `LocalSessionAutomation` 은 여전히 `Game` 에서 바로 연다(자동 검증 경로).
- 호스트 이전(ADR-0017)은 **스테이지(Game) 중에만** 동작한다. 인게임 로비에서 방장이 나가면 세션이 끝나고 게스트는 끊김
  안내 뒤 타이틀로 간다 — 인게임 로비 이전은 후속 과제다.
- 인게임 로비에서 스킬·잡기를 막는 별도 잠금은 두지 않았다. 대상(가구·귀신)이 없어 효과가 없다.

## 검증

검증용 복제 프로젝트(2026-09-28): `GhostHunter > 인게임 로비 씬 생성` 실행 → 씬 저장·Build Settings·SceneNameSO 등록·검증 통과.
PlayMode `InGameLobbyFlowTests` — 실제 씬으로 Bootstrap → Local Host → 인게임 로비(스폰 위치 확인) → 스테이지 출발(재스폰,
로비 언로드) → 정산 → 인게임 로비(재스폰, 세션 유지) → 두 번째 스테이지 → 스테이지 나가기 → 인게임 로비, 예외 0건.
EditMode `InGameLobbyTests` 5건. **게스트(원격 클라이언트) 씬 동기화·Steam 2~4인은 실기 미검증.**

## 재검토 조건

- 인게임 로비에 스테이지 상태(드릴카 선반 배치 등)를 들고 가야 하면 재스폰 대신 서비스 재바인딩을 검토한다.
- 인게임 로비 중 호스트 이전이 필요해지면 ADR-0017 의 선출·재호스트를 인게임 로비 씬에도 적용한다.
