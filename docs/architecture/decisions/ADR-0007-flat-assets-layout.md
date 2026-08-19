# ADR-0007: `Assets/_Project/` 래퍼를 쓰지 않고 평면 배치를 유지한다

- **상태**: Accepted
- **날짜**: 2026-08-19
- **결정자**: MinGiHong
- **관련**: [../overview.md](../overview.md), [../../conventions/unity-assets.md](../../conventions/unity-assets.md)

## 배경 (Context)

에셋 루트 규약으로 두 갈래가 있다.

- **`_Project` 래퍼**: 우리 산출물을 전부 `Assets/_Project/` 아래에 모으고, 서드파티는 그 밖에 둔다.
  프로젝트 뷰에서 우리 것과 남의 것이 한눈에 갈린다. 언더스코어 접두사로 목록 최상단에 고정된다.
- **평면 배치**: `Assets/Scenes`, `Assets/Scripts`, `Assets/Prefabs`, `Assets/Settings` …

GhostHunter는 프로토타입으로 시작해 평면 배치로 컸다. 본 프로젝트 승격 시점에
씬 아키텍처·어셈블리 구조·의존성 획득 방식을 한꺼번에 바꾸기로 했고,
에셋 루트까지 함께 바꿀지 결정해야 했다.

## 검토한 선택지 (Options)

| 선택지 | 장점 | 단점 |
| --- | --- | --- |
| A. `_Project`로 전면 이동 | 서드파티와 경계가 명확. 대형 프로젝트 관례 | **전 에셋 GUID 이동.** 씬·프리팹 참조가 대량으로 재작성되고, 진행 중인 다른 마이그레이션과 충돌 |
| B. **평면 배치 유지** | 이동 비용 0. 다른 마이그레이션에 집중 가능 | 서드파티가 늘면 `Assets/` 최상단이 섞인다 |
| C. 신규만 `_Project`, 기존은 그대로 | 점진적 | **두 규약이 공존한다.** 최악 — 어디에 두는지가 파일마다 달라진다 |

## 결정 (Decision)

**B를 채택한다.** `Assets/_Project/`를 도입하지 않는다.
서드파티는 `Assets/Plugins/` 또는 `Assets/ThirdParty/`에 두고, 그 폴더 구조는 임의로 바꾸지 않는다.

## 근거 (Rationale)

**1. 이 프로젝트의 서드파티가 `Assets/` 밖에 있다.** 가장 큰 서드파티인 FacepunchTransport는
`Packages/`에 임베드되어 있고([ADR-0006](ADR-0006-facepunch-transport-embed.md)),
NGO·Input System·URP는 전부 UPM 패키지다. **`_Project`가 해결하려는 "우리 것과 남의 것이 섞인다"
문제가 현재 이 프로젝트에는 거의 없다.**

**2. 동시에 진행할 마이그레이션이 이미 많다.** 씬 재편, asmdef 분리, 서비스 로케이터 도입,
UniTask 전환이 모두 GUID·참조를 건드린다. 여기에 전 에셋 이동을 겹치면 **문제가 생겼을 때 원인이
어느 변경인지 갈리지 않는다.** 되돌리기 비용이 가장 비싼 항목을 이득이 가장 작은 시점에 치르는 셈이다.

**3. 이득이 순수하게 조직적이다.** `_Project`는 컴파일·런타임·빌드에 아무 영향이 없다.
반면 asmdef 분리는 의존 방향을 **컴파일러가 강제**하게 만든다. 같은 "구조를 정리한다"라도
후자가 실질을 준다. 한정된 마이그레이션 예산을 그쪽에 쓴다.

## 결과 (Consequences)

### 긍정
- 마이그레이션 범위가 코드와 씬으로 좁혀진다.
- 기존 씬·프리팹 참조가 그대로 유지된다.

### 부정 / 감수하는 비용
- 스토어 에셋 등 `Assets/` 아래에 설치되는 서드파티가 늘면 최상단이 섞인다.
  → 완화: 서드파티는 MUST `Assets/Plugins/` 또는 `Assets/ThirdParty/` 아래로 모은다.
- 다른 Unity 프로젝트(AlienGhost)와 폴더 규약이 다르다. 문서에 명시해 혼동을 막는다.

### 후속 작업
- [x] [../../conventions/unity-assets.md](../../conventions/unity-assets.md)에 평면 배치 명시
- [ ] `Assets/TutorialInfo/`, `Assets/Readme.asset`, `SampleScene.unity` 템플릿 잔재 정리
- [ ] 서드파티 도입 시 `Assets/Plugins/` 규칙 적용

## 재검토 조건

- `Assets/` 아래에 설치되는 서드파티가 **3개를 넘을 때**
- 팀 인원이 늘어 "어디에 두는가"를 매번 설명해야 할 때
- 대규모 리팩터링 창구가 따로 생겨 GUID 이동 비용을 한 번에 치를 수 있을 때
