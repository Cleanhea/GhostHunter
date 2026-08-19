# ADR-0005: 비동기 처리를 UniTask로 통일한다

- **상태**: Accepted
- **날짜**: 2026-08-19
- **결정자**: MinGiHong
- **관련**: [ADR-0001](ADR-0001-steam-p2p-facepunch-transport.md), [ADR-0004](ADR-0004-multi-scene-additive.md), [../../conventions/code-style.md §8](../../conventions/code-style.md)

> **이관 메모 (2026-08-19).** 이 결정은 AlienGhost 프로젝트에서 검증된 뒤 GhostHunter로 이관했다.
> 논거와 트레이드오프는 그대로 유효하나, **GhostHunter 코드에는 아직 반영되지 않았다.**
> 진행 상황은 [../../project/roadmap.md](../../project/roadmap.md) 마이그레이션 보드를 본다.

## 배경 (Context)

씬 전환이 additive로 바뀌면서([ADR-0004](ADR-0004-multi-scene-additive.md)) 로드·언로드 대기가 생겼고,
초기 구현은 코루틴을 썼다. 비동기는 한 파일에서 끝나지 않고 코드 전반에 퍼지는 패턴이라
방식을 먼저 정해야 한다.

특히 [ADR-0001](ADR-0001-steam-p2p-facepunch-transport.md)에서 확정한 **Facepunch.Steamworks가 `Task` 기반 API**를 쓴다.
로비 생성·참가·친구 목록 조회가 전부 awaitable이라, Steam을 붙이는 순간(M0-9) Unity에서 `Task`를 대기해야 한다.
그 시점에 방식이 정해져 있지 않으면 코루틴과 `Task`가 섞인다.

## 검토한 선택지 (Options)

| 선택지 | 장점 | 단점 |
| --- | --- | --- |
| A. 코루틴 유지 | 외부 의존 없음. Unity 기본 | 반환값 불가. `yield`가 든 블록은 `try/catch` 불가. 취소가 "파괴되면 조용히 멈춤"이라 코드에 안 드러남. `MonoBehaviour`에서만 동작. **`Task` 기반 Steam API와 직접 못 섞임** |
| B. 순수 `async Task` | 외부 의존 없음. 표준 C# | 컨티뉴에이션이 Unity 메인 스레드로 돌아온다는 보장 없음. 오브젝트가 파괴돼도 자동으로 안 끊김. `Task` 할당이 매번 발생 |
| C. **UniTask** | 위 셋을 모두 해결. Unity 전용 설계, 할당 없음. `AsyncOperation`·`Task`를 모두 await 가능 | 외부 패키지 의존. **취소 토큰을 빠뜨리면 코루틴보다 나쁨** |

## 결정 (Decision)

**C를 채택한다.** `com.cysharp.unitask` **2.5.11**을 git URL + 태그 고정으로 설치한다.

```json
"com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask#2.5.11"
```

브랜치 추적이 아니라 태그를 고정한 것은, 다른 시점에 clone한 사람이 다른 버전을 받는 것을 막기 위해서다.
기존 `FacepunchTransport`가 커밋 해시로 고정된 것과 같은 방침이다.

**규약**: 코루틴과 순수 `async Task`는 새로 쓰지 않는다. fire-and-forget은 `async UniTaskVoid` + `.Forget()`,
`async void`는 금지. 대기에는 MUST 취소 토큰을 넘긴다 → [../../conventions/code-style.md §8](../../conventions/code-style.md).

## 근거 (Rationale)

**1. Steam 연동이 강제한다.** Facepunch가 `Task`를 돌려주므로 A(코루틴)로는 애초에 받을 수 없고,
B로 받으면 컨텍스트·취소 문제를 매번 손으로 처리하게 된다. **어차피 `Task`를 다뤄야 한다면
그걸 Unity 수명주기에 맞게 감싸주는 도구를 쓰는 게 맞다.**

**2. 취소가 코드에 드러난다.** 코루틴은 오브젝트가 파괴되면 조용히 멈추는데, 그 사실이 어디에도 적혀 있지 않다.
`destroyCancellationToken`을 넘기면 "언제 끊기는가"가 시그니처와 호출부에 남는다.
멀티씬에서 씬이 수시로 오르내리는([ADR-0004](ADR-0004-multi-scene-additive.md)) 구조라 이 점이 특히 중요하다.

**3. 도입 비용이 지금 가장 싸다.** 전환 대상 코루틴이 `SceneFlowController` 하나(5줄)뿐이었다.

## 결과 (Consequences)

### 긍정
- 코루틴·`Task`·`UniTask`가 섞이지 않고 하나로 통일된다.
- `try/catch`로 비동기 실패를 잡을 수 있다. 코루틴에서는 불가능했다.
- `MonoBehaviour`가 아닌 순수 C# 클래스에서도 비동기를 쓸 수 있다.
- Steam 연동(M0-9) 시 `Task` → `UniTask` 변환이 그대로 된다.

### 부정 / 감수하는 비용
- **서드파티 의존이 하나 늘었다.** Cysharp이 유지보수하며, Unity 버전 업그레이드 시 호환성을 확인해야 한다.
- **취소 토큰을 빠뜨리면 코루틴보다 위험하다.** 코루틴은 오브젝트가 파괴되면 자동으로 멈추지만,
  토큰 없는 `await`는 파괴된 오브젝트를 참조한 채 계속 진행해 `MissingReferenceException`을 낸다.
  규약(§8)에 MUST로 박은 이유다. **리뷰에서 우선 확인할 항목.**
- `.Forget()`으로 흘린 예외는 `UniTaskScheduler`의 미관측 예외 핸들러로 가서 로그된다.
  조용히 사라지지는 않지만, 호출부 스택이 아니라 스케줄러 쪽에서 뜨므로 추적이 한 단계 멀다.
- asmdef 도입(M0-3) 시 비동기를 쓰는 어셈블리는 `UniTask` 참조를 걸어야 한다.
  **`Core`는 UniTask 하나만 예외로 참조한다** (2026-08-20 정정).
  `Core`의 서비스 인터페이스 중 일부가 본질적으로 awaitable 이어야 하고, UniTask는 도메인 의존이 아니라
  언어 수준의 비동기 원시 타입이기 때문이다. 근거: [../overview.md §3.1](../overview.md).
  **UniTask 외에는 어떤 것도 `Core` 참조에 추가하지 않는다.**

### 후속 작업
- [ ] 패키지 설치 및 태그 고정 (`manifest.json`, `packages-lock.json`)
- [ ] `SceneFlowController.LoadLocalRoutine` → `LoadLocalAsync` 전환
- [ ] [../../conventions/code-style.md](../../conventions/code-style.md) §4·§8·§10 갱신
- [ ] [../networking.md](../networking.md) 정리 체크리스트, [../../workflow/testing.md](../../workflow/testing.md) 분류 갱신
- [ ] [../../../CLAUDE.md](../../../CLAUDE.md) 스냅샷 표에 반영
- [ ] asmdef 도입 시 `UniTask` 참조 대상 어셈블리 확정, `Core` 제외 확인
- [ ] Steam 연동에서 Facepunch의 `Task`를 `UniTask`로 받는 패턴 확립 (M0-9)

## 재검토 조건

- Unity 버전을 올렸을 때 UniTask 2.5.x가 호환되지 않는 경우
- Unity가 Awaitable 등 공식 대체 수단으로 위 세 문제를 충분히 해결하게 되는 경우
- Cysharp의 유지보수가 중단되는 경우
