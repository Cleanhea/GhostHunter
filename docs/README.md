# GhostHunter 문서

1인칭 멀티플레이 "가구 던지기" 프로토타입의 설계 문서 모음입니다.

## 읽는 순서

1. [01-project-overview.md](01-project-overview.md) — **여기부터.** 무엇을 왜 만드는지
2. [02-architecture.md](02-architecture.md) — 전체 구조와 데이터 흐름
3. [03-multiplayer-setup.md](03-multiplayer-setup.md) — 개발 환경 세팅 (패키지 설치)
4. [04-player-controller.md](04-player-controller.md) — 이동/시점
5. [05-throw-system.md](05-throw-system.md) — 핵심 메커닉
6. [06-furniture-physics.md](06-furniture-physics.md) — 던져지는 대상
7. [07-conventions.md](07-conventions.md) — 코드/Git 규칙
8. [08-roadmap.md](08-roadmap.md) — 진행 상황 체크리스트
9. [09-map-generation.md](09-map-generation.md) — 맵 생성 시스템 기획서 (House / Room Preset / Spawn Point)

## 문서 관리 규칙

- 설계가 바뀌면 코드와 **같은 커밋에서** 문서를 고친다. "나중에 정리"는 안 한다.
- 아직 정하지 못한 것은 지우지 말고 `> **미결정:**` 블록으로 남긴다. 무엇을 모르는지가 정보다.
- 수치(힘, 거리, 시간)는 문서에 "초기값"으로 적되, 최종 권위는 `ScriptableObject` 설정 에셋에 있다. 플레이테스트로 값이 바뀌면 문서 수치도 갱신한다.
