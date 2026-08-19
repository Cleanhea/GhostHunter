# 개발 루프 & 완료 기준

## 1. 작업 단위

- 작업의 단위는 [../project/roadmap.md](../project/roadmap.md)의 **태스크 1행**이다.
- 로드맵에 없는 작업을 시작하면 MUST 로드맵에 행을 추가하고 `진행중`으로 표시한다.

## 2. 6단계 루프

### 1) 컨텍스트 로드
[CLAUDE.md §2 라우팅 표](../../CLAUDE.md#2-컨텍스트-라우팅--작업-전-필독-문서)에서 필독 문서를 읽는다.
기존 유사 코드가 있으면 함께 읽고 **그 스타일을 따른다**.

### 2) 범위 정렬
- 요구를 1~3문장으로 재진술한다.
- `TBD` 항목에 의존하는 작업이면 **여기서 멈추고 질문**한다.
- 디자인 결정이 필요하면 사람에게 넘긴다.

### 3) 계획
2단계 이상 작업이면 착수 전에 제시한다.
```
변경 예정:
- Assets/Scripts/Gameplay/Player/PlayerMotor.cs (신규)
- Assets/Scripts/Data/PlayerConfig.cs (신규)
- docs/project/roadmap.md (M1-2 상태 갱신)
사용자 작업 필요: Player.prefab에 PlayerMotor 배선
```

### 4) 구현
- 규약([../conventions/code-style.md](../conventions/code-style.md)) 준수.
- 최소 diff. 요청하지 않은 리팩터링을 끼워 넣지 않는다.
- 에디터 조작이 필요한 부분은 코드로 우회하지 말고 사용자 지시로 분리한다.

### 5) 검증
| 수준 | 방법 |
| --- | --- |
| 컴파일 | Unity MCP(에디터 켜짐) 또는 `Unity.exe -quit -batchmode -nographics -projectPath …`(에디터 종료 후) |
| 단위 | EditMode 테스트 |
| 통합 | PlayMode 테스트 |
| 실제 동작 | 사용자에게 에디터 확인 요청 (번호 지시로) |

**검증하지 못했으면 "동작한다"고 말하지 않는다.** 못 한 검증은 그대로 보고한다.

### 6) 기록
- 영향받은 문서 갱신 (구조 → `architecture/`, 수치 → `gdd.md`, 규약 → `conventions/`)
- 되돌리기 비싼 결정 → [ADR](../architecture/decisions/README.md)
- 로드맵 태스크 상태 갱신 + 완료 이력 추가

## 3. 완료 기준 (Definition of Done)

아래를 모두 만족해야 태스크를 `완료`로 표시한다.

- [ ] 요구한 기능이 동작한다 (검증 방법 명시)
- [ ] 컴파일 에러·경고 없음
- [ ] [code-style.md](../conventions/code-style.md) 금지 목록 위반 없음
- [ ] 네트워크 코드면 [networking.md §6 체크리스트](../architecture/networking.md) 통과
- [ ] 새 public 동작에 테스트가 있거나, 없는 이유를 밝혔다
- [ ] 영향받는 문서를 갱신했다
- [ ] `.meta` 누락/고아 없음 (`git status` 확인)
- [ ] 커밋이 [git.md](../conventions/git.md) 컨벤션을 따른다
- [ ] 사용자 수동 작업이 남아 있다면 **명확히 목록화**했다

## 4. 리뷰 심각도

| 등급 | 기준 | 처리 |
| --- | --- | --- |
| **Blocker** | 크래시, 데이터 손상, 클라이언트 권위 위반, 이벤트 누수, GUID 파손 위험 | 머지 불가 |
| **Major** | 핫패스 할당, 규약 위반, 레이어 의존 역전, 테스트 부재 | 이번 작업에서 수정 |
| **Minor** | 네이밍, 주석, 사소한 중복 | 후속 처리 가능 |

## 5. 막혔을 때

1. 같은 접근을 3번 이상 반복하지 않는다.
2. 가정을 다시 검증한다(문서가 최신인가? 에디터가 잠겨 있나? 프리팹 배선이 안 된 건 아닌가?).
3. 그래도 막히면 **시도한 것 / 관찰한 것 / 필요한 것**을 정리해 사용자에게 보고한다.

## 6. 자주 겪는 함정

| 증상 | 원인 | 조치 |
| --- | --- | --- |
| batchmode 명령이 즉시 실패 | Unity 에디터가 프로젝트를 잠금 | 에디터 종료 후 재시도 |
| 스크립트를 만들었는데 인스펙터에 안 보임 | Unity가 아직 임포트하지 않음 | 에디터 포커스 복귀 or 재임포트 요청 |
| 클라이언트에서만 동작이 안 됨 | 서버 권위 분기 누락 | `IsServer`/`IsOwner` 확인 |
| 접속 시 프리팹 관련 에러 | NetworkPrefabsList 미등록 | 등록 요청 |
| 참조가 통째로 깨짐 | `.meta` 삭제/GUID 변경 | Git으로 복구, 재발 방지 |

---

관련: [testing.md](testing.md) · [playbooks.md](playbooks.md)

최종 갱신: 2026-08-19
