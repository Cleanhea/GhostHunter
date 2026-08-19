# ADR-0002: 씬 전환 요청을 ScriptableObject 이벤트 채널로 전달한다

- **상태**: **Rejected** (대안: [ADR-0003](ADR-0003-service-locator.md))
- **날짜**: 2026-08-19
- **결정자**: MinGiHong
- **관련**: [ADR-0003](ADR-0003-service-locator.md), [ADR-0004](ADR-0004-multi-scene-additive.md), [../overview.md](../overview.md)

> **이 문서는 채택되지 않은 안을 기록한 것이다.** AlienGhost 프로젝트에서 실제로 구현했다가
> 되돌린 결정이고, GhostHunter에서는 처음부터 채택하지 않는다.
> **같은 논의를 다시 하지 않기 위해** 근거와 함께 남긴다.

## 배경 (Context)

`SceneFlowController`는 `Bootstrap` 씬에 있고, 실제 호출자는 그보다 나중에 로드되는 씬(`Title`, `Lobby`)의 UI다.
여기에 두 제약이 겹친다.

1. **씬 간 참조 금지** ([../../conventions/unity-assets.md](../../conventions/unity-assets.md)) —
   `Title` 씬의 버튼이 `Bootstrap`에 있는 오브젝트를 인스펙터로 물 수 없다.
2. **`UI → Systems` 참조 금지** ([../overview.md](../overview.md)) — asmdef를 분리하면
   UI 어셈블리에서 `SceneFlowController`라는 **타입 이름 자체를 쓸 수 없다.** 싱글턴 여부와 무관하게 컴파일이 실패한다.

즉 UI는 `Core` 또는 `Data`에 있는 무언가를 경유해야만 씬 전환을 요청할 수 있다.

## 검토한 선택지 (Options)

| 선택지 | 장점 | 단점 |
| --- | --- | --- |
| A. `static Instance` 싱글턴 | 구현 최소 | **asmdef 분리 시 UI에서 참조 불가 → 컴파일 실패.** 테스트 격리 어려움 |
| B. **SO 이벤트 채널** | `Data` 레이어라 UI·Systems 양쪽에서 참조 가능. 씬 간 참조 불필요 | 아래 §결과 참조 |
| C. `Core` 인터페이스 + 서비스 로케이터 | 파일 증식 없음. Find Usages로 호출부·구현 전부 추적 | 프로세스 전역. 의존성이 시그니처에 안 드러남 |
| D. 범용 `GameEventChannel` 하나 + id·`object` 페이로드 | 채널 클래스가 늘지 않음 | 타입 안전성 상실, 박싱. **id 오타가 컴파일이 아니라 런타임에 조용히 사라진다** |

## 결정 (Decision)

**B를 기각하고 [C(서비스 로케이터)](ADR-0003-service-locator.md)를 채택한다.**

## 근거 (Rationale)

AlienGhost에서 B를 실제로 구현한 뒤 아래가 비용으로 드러났다.

- **이벤트 종류마다 구체 클래스와 에셋이 쌓인다.** Unity는 열린 제네릭 타입으로 에셋을 만들지 못해
  제네릭 베이스(`EventChannel<T>`)를 둬도 클래스 파일 자체는 없앨 수 없다.
- **구독자를 코드에서 추적할 수 없다.** Find Usages가 `Raise`까지만 데려가고, 그다음은 인스펙터를 뒤져야 한다.
- **배선 누락이 컴파일 에러가 아니라 "아무 일도 안 일어남"으로 나타난다.**
- `.asset` 병합 충돌이 늘어난다.

**SO 채널의 핵심 이점은 "코드 수정 없는 인스펙터 재배선"이고, 그 수혜자는 디자이너다.**
이 프로젝트에는 디자이너 배선 수요가 없어 비용만 지불하는 구조였다.

D는 파일 수를 줄이는 대신 결합을 숨기는 거래라 더 나쁘다.
A는 asmdef를 분리하는 순간 확실히 깨진다([../overview.md](../overview.md) §3).

## 결과 (Consequences)

### 이 기각으로 얻는 것
- 채널 클래스·에셋 증식이 없다.
- 씬 전환 호출부와 구현이 모두 코드 검색으로 추적된다.

### 감수하는 것
- 서비스 로케이터의 약점(전역 상태, 시그니처에 안 드러나는 의존성)을 대신 진다 → [ADR-0003](ADR-0003-service-locator.md).

## 재검토 조건

- 디자이너가 인스펙터에서 게임 흐름을 재배선하는 워크플로가 실제로 필요해질 때
- 이벤트 종류가 씬 전환 하나가 아니라 다수가 되고, 그 대부분이 "누가 듣는지 몰라도 되는" 성격일 때
