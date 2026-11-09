# ADR-0025: Stage1 정산 뒤 다음 출발을 Stage2로 연결한다

- 상태: Accepted
- 날짜: 2026-11-09
- 결정자: 사용자
- 관련: [ADR-0018](ADR-0018-persistent-session-in-game-lobby.md), [ADR-0021](ADR-0021-tutorial-first-stage.md), [Stage2](../../project/stage2.md)

## 배경

사용자가 Stage2 원본 맵으로 새 씬을 만들고 Stage1 다음으로 연결하도록 요청했다.
기존 StageSessionFlow는 Tutorial 정산 여부만 기억해 이후 Stage1을 반복했다.

## 결정

기존 직렬화 값을 보존하고 SceneId.Stage2를 9로 추가한다. IsStage·SceneNameSO·빌드 씬 목록에 함께 등록한다.
StageSessionFlow는 다음 출발 대상을 기억하며 Tutorial → Result에서 Stage1, Stage1 → Result에서 Stage2로 갱신한다.
Result·InGameLobby를 거치는 세션 유지와 서버 권위, 플레이어 재스폰은 그대로 사용한다.
정산 없는 허브 복귀는 다음 대상을 갱신하지 않고, Title/Lobby 진입은 Tutorial로 초기화한다.
Stage2 뒤 새 단계가 정의되기 전까지 기존 마지막 스테이지 재출발 동작을 유지한다.

## 결과

정산·음성·정신력·사망·상점·호스트 이전의 스테이지 판정이 새 씬에도 적용된다.
맵 가구는 씬 배치 프리팹과 기존 배치/반출 시스템을 사용하며 별도 어셈블리·런타임 맵 생성기는 추가하지 않는다.
진행 상태의 영구 저장과 기존 호스트 이전 한계는 바꾸지 않는다. 검증 범위는 Stage2 문서에 기록한다.
