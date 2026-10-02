# ADR-0021: 첫 게임을 Tutorial로 시작하고 정산 뒤 Stage1으로 진행한다

- **상태**: Accepted
- **날짜**: 2026-10-02
- **결정자**: 사용자(Tutorial 첫 진입·종료 뒤 Stage1), 구현 시 기존 정산/허브 경로 유지
- **관련**: [ADR-0018](ADR-0018-persistent-session-in-game-lobby.md), [ADR-0019](ADR-0019-stage1-scene-split.md), [ADR-0020](ADR-0020-remove-one-off-editor-setup-tools.md), [Tutorial](../../project/tutorial-stage.md)

## 배경 (Context)

사용자는 TutorialMap 모텔을 현재 크기로 새 Tutorial 씬에 구성하고, 첫 게임은 Tutorial, 종료 또는 탈락 후 다음 스테이지는 Stage1으로 연결하도록 요청했다.
현재 세션은 스테이지 → Result → InGameLobby 경로를 유지하며, 서버가 출발/복귀와 플레이어 재스폰을 담당한다.

## 검토한 선택지 (Options)

| 선택지 | 장점 | 비용 |
| --- | --- | --- |
| 기존 Result·InGameLobby를 거쳐 다음 출발을 Stage1으로 변경 | 기존 정산·보상·상점·세션/서버 권위 사용 | 다음 출발 단말기 조작 필요 |
| Tutorial 종료 즉시 Stage1 로드 | 즉시 다음 맵 진입 | 정산·허브 흐름과 신규 예외 처리 필요 |

## 결정 (Decision)

첫 게임의 시작 씬은 Tutorial이다. 종료/전멸은 기존 Result와 InGameLobby를 거치고, 다음 출발은 Stage1이다.
SceneId.Tutorial은 값 7로 추가하여 기존 직렬화 값을 유지한다. SceneId.IsStage에 포함해 정신력·사망·정산·음성·씬 전환의 기존 계약을 사용한다.
StageSessionFlow는 각 피어의 Tutorial → Result 전환으로 완료를 기억하며 실제 출발/복귀는 기존 서버 권위를 유지한다.
정산 없는 ESC 이탈은 완료로 보지 않는다. Title/Lobby 진입 시 상태를 초기화하고 디스크에 저장하지 않는다.
모텔은 저장된 프리팹/메시/재질이며 map localScale은 1로 유지한다. 원본 FBX는 보존하고 스케일을 정점에 베이크한다.

## 근거 (Rationale)

사용자 요청의 다음 스테이지를 Stage1으로 연결하면서 이미 정의된 정산·보상·상점 루프를 재사용한다.
별도 어셈블리나 런타임 맵 생성기 없이 기존 SceneFlow와 스테이지 서비스를 사용한다.
조명은 Stage1의 환경 설정과 StageLightingSettings_Default를 공유한다.

## 결과 (Consequences)

- Tutorial은 세션의 첫 스테이지이며 정산 후에는 Stage1 루프를 사용한다.
- 최초 구성은 정적 모텔이었다. 2026-10-02 후속 사용자 결정으로 202호 청소/반출 목표·귀신·안내를 연결했다([tutorial-stage.md](../../project/tutorial-stage.md)). 문은 정적 열린 배치다. 자동 정산은 추가하지 않았다.
- FBX 재출력 시 저장된 프리팹/베이크 메시와 재질 매핑을 함께 점검해야 한다.
- Unity 구조 검사와 Local Host 정상 종료·전멸·재시도 테스트를 추가했다. Steam 다인과 실제 외형/이동 검증은 남아 있다.

## 재검토 조건

Tutorial 전용 목표·자동 종료, 영구 완료 저장, 정산/허브 생략, 호스트 이전 중 첫 스테이지 진행 상태 복구 요구가 확정될 때 재검토한다.
