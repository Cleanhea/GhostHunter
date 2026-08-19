# ADR — 아키텍처 결정 기록

## 1. 언제 ADR을 쓰는가

되돌리기 비싼 선택을 했을 때 MUST 남긴다.

- 네트워크 토폴로지, 권위 모델, 동기화 전략
- 어셈블리 분리 / 모듈 경계 / 폴더 루트
- 의존성 주입, 상태 관리, 씬 전환 등 코드 전반에 퍼지는 패턴
- 데이터 영속화·세이브 포맷
- 외부 패키지·서비스 도입, 서드파티 패키지 벤더링·패치
- 성능이나 조작감을 위해 규약을 의도적으로 어기는 경우

**쓰지 않아도 되는 것**: 한 파일 안에서 끝나는 구현 선택, 언제든 바꿀 수 있는 값.

## 2. 작성 방법

1. [ADR-template.md](ADR-template.md)를 복사한다.
2. `ADR-<번호 4자리>-<kebab-case-제목>.md`로 저장한다. 번호는 순차 증가.
3. 아래 목록에 한 줄 추가한다.
4. 기존 결정을 뒤집는 경우, 이전 ADR의 상태를 `Superseded by ADR-XXXX`로 바꾼다. **이전 ADR을 삭제하지 않는다.**

## 3. 상태 값

| 상태 | 의미 |
| --- | --- |
| `Proposed` | 제안됨, 미확정 — 사용자 결정 대기 |
| `Accepted` | 확정, 현재 유효 |
| `Rejected` | 검토했으나 채택하지 않음. 같은 논의 반복을 막으려고 남긴다 |
| `Deprecated` | 더 이상 권장하지 않음 |
| `Superseded` | 다른 ADR로 대체됨 |

## 4. 목록

| # | 제목 | 상태 | 날짜 |
| --- | --- | --- | --- |
| [0001](ADR-0001-steam-p2p-facepunch-transport.md) | Steam P2P + Facepunch.Steamworks를 네트워크 트랜스포트로 사용한다 | Accepted | 2026-08-19 |
| [0002](ADR-0002-scene-flow-so-event-channel.md) | 씬 전환 요청을 ScriptableObject 이벤트 채널로 전달한다 | **Rejected** | 2026-08-19 |
| [0003](ADR-0003-service-locator.md) | 서비스 접근을 `Core`의 서비스 로케이터로 한다 | Accepted | 2026-08-19 |
| [0004](ADR-0004-multi-scene-additive.md) | 멀티씬 아키텍처 — Bootstrap을 언로드하지 않고 additive로 얹는다 | Accepted | 2026-08-19 |
| [0005](ADR-0005-unitask-async.md) | 비동기 처리를 UniTask로 통일한다 | Accepted | 2026-08-19 |
| [0006](ADR-0006-facepunch-transport-embed.md) | FacepunchTransport를 임베드하고 5건을 패치한다 | Accepted | 2026-08-19 |
| [0007](ADR-0007-flat-assets-layout.md) | `Assets/_Project/` 래퍼를 쓰지 않고 평면 배치를 유지한다 | Accepted | 2026-08-19 |
| [0008](ADR-0008-owner-authoritative-player-movement.md) | 플레이어 이동만 소유자 권위로 둔다 | Accepted | 2026-08-19 |
| [0009](ADR-0009-scene-placed-level-objects.md) | 가구·문·붙박이는 프리팹 인스턴스로 씬에 배치한다 (런타임 스폰하지 않는다) | Accepted | 2026-08-19 |
| [0010](ADR-0010-server-authoritative-furniture-physics.md) | 가구 물리는 서버 권위로 시뮬레이션한다 | Accepted | 2026-08-19 |
| [0011](ADR-0011-local-transport-path.md) | 로컬 UTP 경로를 유지하고 빌드 가드로 릴리스를 보호한다 | Accepted | 2026-08-20 |
| [0012](ADR-0012-room-code-and-lobby-visibility.md) | 방 코드 6자리 + 로비 가시성·난입 정책 | **Proposed** | 2026-08-19 |

## 5. 작성 대기

확정되면 ADR을 남겨야 할 항목:

- 세이브/설정 데이터 영속화 방식
- 오디오 시스템 구조
- 승패 조건·라운드 구조가 정해진 뒤의 세션 수명주기
- 실제 Steam App ID 발급 후 480 교체

---

최종 갱신: 2026-08-20
