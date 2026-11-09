# ADR-0024: 귀신 종류 설정은 기본 귀신 설정을 상속한다

- **상태**: Accepted
- **날짜**: 2026-11-09
- **결정자**: 사용자(caduga1055)
- **관련**: [ghost-dino.md](../../project/ghost-dino.md), [ghost-prototype.md "박치기 공룡"](../ghost-prototype.md), [ghost-system.md §11.4](../../project/ghost-system.md)

## 배경 (Context)

2026-11-07 박치기 공룡을 구현할 때 기믹 값(`_dinoChargeEnabled`·돌진 시간·속도·피해 등)을 기본 귀신 설정 `GhostPrototypeSettings`
안에 불(bool) 스위치와 함께 넣었고, 사용 위치가 정해지지 않아 `GhostPrototypeSettings_Default`(Stage1·ProtoTypeGame)에서 켜 두었다(BD-3 임시).
2026-11-09 사용자가 Tutorial 귀신을 박치기로 바꾸며 "Default 는 박치기가 아니다 — 기본 귀신 아래에 박치기가 있다. 기본 세팅과 박치기를
완전히 분리하고, 박치기 귀신이 기본 귀신을 상속받도록 설계를 고정"하라고 결정했다.

## 검토한 선택지 (Options)

| 선택지 | 장점 | 단점 |
| --- | --- | --- |
| A. 지금처럼 기본 설정 안에 스위치(`_dinoChargeEnabled`) | 변경 없음 | 기본 귀신 에셋에 다른 종류의 값이 섞인다. 스위치만 켜면 어느 에셋이든 박치기가 되어 "기본 = 박치기 아님"이 보장되지 않는다 |
| B. **기본 설정을 상속한 종류별 설정 타입**(`GhostDinoSettings : GhostPrototypeSettings`) | 기본 값은 그대로 물려받고 종류 값만 더한다. 에셋 타입이 곧 귀신 종류다 — Default 는 구조적으로 박치기가 될 수 없다 | 설정 클래스가 `sealed` 가 아니게 된다. 리플렉션 도구(F2 튜닝)는 부모 필드까지 모아야 한다 |
| C. 기본 설정 + 별도 기믹 SO 를 컨트롤러에 따로 배선 | 기믹 SO 를 여러 귀신이 나눠 쓴다 | 프리팹 배선이 둘이 되고 "상속" 요구와 다르다. 난이도 같은 기본 값을 기믹이 따로 참조해야 한다 |

## 결정 (Decision)

**B.** `GhostPrototypeSettings` 는 기본 귀신 설정이다 — 종류별 기믹 값을 두지 않는다. 박치기 공룡은 `GhostDinoSettings`
(기본 설정을 상속, 박치기 값만 추가)이고, 컨트롤러는 설정이 이 타입일 때만 박치기 사이클을 돈다(`_settings as GhostDinoSettings`).
켜고 끄는 스위치는 없다. 새 귀신 종류도 같은 방식으로 기본 설정을 상속한 설정 타입을 만든다.

## 근거 (Rationale)

사용자 결정이 "상속"을 명시했고, 에셋 타입으로 종류를 고정하면 기본 귀신 에셋에 기믹이 실수로 켜질 수 없다. 기본 값(난이도·시야·추격 속도 등)은
상속으로 그대로 쓰므로 Tutorial 에셋은 같은 파일·같은 GUID 를 유지한 채 스크립트 타입만 바꾸면 됐다(프리팹 참조 유지).

## 결과 (Consequences)

### 긍정
- `GhostPrototypeSettings_Default` 는 기본 귀신이다(Stage1·ProtoTypeGame 귀신은 박치기를 하지 않는다).
- `GhostPrototypeSettings_Tutorial` 은 `GhostDinoSettings` 다(Tutorial 귀신 = 박치기, 쉬움 → 돌진 5m/s). DinoTest 시험장은 `Ghost_Tutorial` 프리팹을 쓴다.

### 부정 / 감수하는 비용
- F2 튜닝 창의 "박치기 켜고 끄기"가 없어졌다(에셋 타입이 종류다).
- F2 튜닝 창은 부모 타입의 private 직렬화 필드까지 모으도록 바꿨다(`TuningHud.SettingsFields`).
- '뒤돌아보지마'(`_gazeFreezeEnabled` 등)는 아직 기본 설정 안 스위치로 남아 있다 — 같은 방식으로 나눌지는 별도 결정.

### 후속 작업
- [x] 영향받는 문서 갱신: ghost-dino.md(사용 위치·BD-3), ghost-prototype.md(박치기 절), overview.md(DinoTest 행), roadmap.md
- [x] 코드 반영: `GhostDinoSettings`, 컨트롤러 `DinoSettings`, Default/Tutorial 에셋, DinoTest 스포너 프리팹, 테스트

## 재검토 조건

귀신 한 마리가 기믹 두 개 이상을 동시에 가져야 하거나(다중 상속 불가), 기믹 값을 여러 종류가 공유해야 할 때 — 그때는 C(기믹 SO 조합)를 다시 본다.
