# ADR-0019: Game 씬을 ProtoTypeGame 으로 남기고 스테이지 씬 Stage1 을 분리한다

- **상태**: Accepted — 코드·설치 도구·자동 검증 완료, 원본 에디터에서 메뉴 실행·Local Host 로 Stage1 진입 확인(2026-09-28)
- **날짜**: 2026-09-28
- **결정자**: 사용자(Game → ProtoTypeGame 이름 변경 · Stage1 신설 · 인게임 로비 출발 대상 · B안·드릴카 안전 구역·조립 영역·정신력 UI 이전)
- **관련**: [ADR-0018 인게임 로비](ADR-0018-persistent-session-in-game-lobby.md), [ADR-0009 씬 배치 레벨 오브젝트](ADR-0009-scene-placed-level-objects.md),
  [스테이지 시스템 기획서 §1.1](../../project/stage-system.md), [unity-assets.md §5.2](../../conventions/unity-assets.md)
- **후속(2026-09-28)**: 이 ADR이 언급하는 설치·생성 도구(`HousePrototypeBuilder`·`…Setup`·`GhostHunter > …` 메뉴)는 [ADR-0020](ADR-0020-remove-one-off-editor-setup-tools.md)으로 삭제됐다.

## 배경

`Game.unity` 한 장에 모든 프로토타입이 쌓였다 — A안 집 두 채·v0.3 그레이박스·B/C안 비교 집·가구 라이브러리·방 프리셋·
정신력 테스트베드·귀신·은신처·청소, 그리고 스케일 100 짜리 바닥 `Plane`. 인게임 로비(ADR-0018)의 "스테이지 출발"도 이 씬을
올렸다. 사용자는 이 씬을 **프로토타입 검증 씬으로 남기고**, 실제 스테이지를 새 씬으로 시작하기로 했다.

## 결정

1. **`Game.unity` 의 이름을 `ProtoTypeGame.unity` 로 바꾼다.** 에디터 안에서 `AssetDatabase.RenameAsset` 으로 옮겨 GUID 를
   유지한다 — SceneNameSO·Build Settings 참조가 끊기지 않고, 씬 배치 NetworkObject 해시(씬 GUID 기반)도 그대로다.
   `SceneId.Game` 은 `SceneId.ProtoTypeGame`(값 3 유지)으로, `SceneNameSO._game` 은 `_protoTypeGame`
   (`FormerlySerializedAs("_game")`)으로 바꾼다. 자동 검증(`LocalSessionAutomation`)과 프로토타입 설치 메뉴들은 계속 이 씬을 쓴다.
2. **스테이지 씬 `Stage1.unity` 를 새로 둔다**(`SceneId.Stage1 = 6`). 인게임 로비의 `StartStage()` 는 이제 Stage1 을 올린다.
3. **Stage1 은 ProtoTypeGame 을 복사해 필요한 루트만 남긴다** — 설치 도구 `GhostHunter > Stage1 씬 생성`(`Stage1SceneSetup`).
   가져오는 것: B안 집(가구 랜덤 배치 포함)·임시 드릴카 안전 구역·가구 조립 영역(분해 부품 풀 포함)·정신력 UI(팀 서비스·
   월드 모니터·화면 노이즈), 그리고 스테이지가 돌아가는 데 필요한 `GameInstaller`·스폰 지점·개요 카메라·조명·HUD·
   일시정지 메뉴·굴착 화면 효과. **귀신·은신처·청소·비교용 집·테스트베드는 가져오지 않는다**(사용자가 나열한 범위).
   스케일 100 바닥 `Plane` 대신 같은 높이에 스케일 1 바닥 상자 `Ground_TEMP` 를 깐다(하드 룰). 복사한 씬의 NetworkObject
   해시는 저장·빌드 목록 등록 뒤 다시 계산한다.
4. **"스테이지인가"는 `SceneIdExtensions.IsStage()` 하나로 판정한다**(ProtoTypeGame·Stage1). 정신력·사망·정산·호스트 이전·
   음성 그룹 분리·ESC `스테이지 나가기`·인게임 로비 ⇄ 스테이지의 이전 씬 먼저 내리기(`UnloadsBeforeLoad`)가 모두 이 판정을 쓴다.
   스테이지 씬을 더 만들면 여기에 추가한다.
5. **`GameInstaller` 의 귀신(`_ghostSpawner`)은 청소처럼 선택 서비스가 된다.** Stage1 에는 귀신이 없다.

## 검토한 선택지

| 선택지 | 장점 | 단점 |
| --- | --- | --- |
| A. `SceneId.Game` 을 그대로 두고 SceneNameSO 가 Stage1 을 가리키게 | 코드 변경 최소 | 프로토타입 씬을 코드에서 부를 방법이 사라지고, 이름(Game)과 씬(Stage1)이 어긋난다 |
| B. **ProtoTypeGame·Stage1 두 SceneId + `IsStage()`** | 두 씬 모두 스테이지로 동작, 판정이 한 곳 | 17곳의 `== SceneId.Game` 을 바꿔야 한다 |
| C. Stage1 을 빈 씬에서 코드로 생성 | 재현성 최고 | B안·가구 배치·조립 영역·정신력 모니터 생성기를 전부 새 씬용으로 다시 짜야 한다 |

B 를 택했다. Stage1 은 복사+정리로 만든다(C 의 재현성은 "ProtoTypeGame 에서 다시 만들기"로 대신한다).

## 결과

- 일반 로비 → 인게임 로비 → **Stage1** → Result → 인게임 로비 … 로 돈다. ProtoTypeGame 은 Build Settings 에 남아
  자동 검증·F1 HUD 개발 경로에서 쓴다.
- Stage1 에는 귀신이 없어 정신력은 어둠 노출(헤드라이트·드릴카)로만 변한다. 호스트 이전 스냅샷(`StageRecoverySnapshotUtility`)은
  청소·귀신이 있어야 찍히므로 **Stage1 에서는 호스트 이전이 복원 없이 끝난다**(ADR-0017 후속).
- 프로토타입 설치 메뉴(정신력·일시정지·퀵슬롯·음성·귀신·B안 가구 등)는 ProtoTypeGame 을 대상으로 한다. Stage1 에 반영하려면
  `Stage1 씬 생성`을 다시 돌리거나(Stage1 을 통째로 다시 만든다) 해당 부분을 씬 파일에서 직접 옮긴다.
- 다시 만들면 Stage1 의 GUID 가 바뀐다 — SceneNameSO·Build Settings 는 도구가 다시 등록한다.
- 과거 문서의 "Game 씬"은 ProtoTypeGame 을 뜻한다.

## 검증

검증용 복제 프로젝트(2026-09-28): `Stage1SceneSetup.CreateScene` 실행 → `Game.unity → ProtoTypeGame.unity`(GUID `03c9f03f…` 유지),
Stage1 루트 13개(가져온 12 + `Ground_TEMP`), Build Settings·SceneNameSO 등록, 씬 NetworkObject 120개 해시 전부 0 아님·중복 없음·
ProtoTypeGame 과 겹침 없음, 도구 자체 검증 통과. EditMode 333건 중 331 통과(실패 2건은 이번 변경과 무관한 `MoleSkillWiringTests`
소스 문자열 검사), PlayMode 83건 중 82 통과 — `InGameLobbyFlowTests` 가 인게임 로비 → Stage1(B안 앞마당 스폰·드릴카가 스폰 줄 뒤·
정신력 서비스·종료 단말기) → 정산 → 인게임 로비 → Stage1 → 인게임 로비를 예외 0건으로 통과. 실패 1건(`VoiceFlowTests.Result_…`)은
`Allocator.Temp` 배열을 `yield` 너머로 들고 가는 기존 테스트 문제다.

원본 에디터(2026-09-28 01:57): 사용자가 메뉴를 실행해 이름 변경·Stage1 생성·검증 로그가 남았고, 이어서 Play → Local Host →
인게임 로비 → Stage1 진입, B안 가구 16개 배치까지 Editor.log 에 오류·경고 0건. **게스트 동기화·Steam 다인은 미검증.**

## 재검토 조건

- 귀신·청소를 Stage1 에 넣기로 하면 루트 목록(`Stage1SceneSetup.KeptRoots`)에 추가하거나 손으로 옮긴다.
- 스테이지가 여러 개가 되면 `IsStage()` 와 인게임 로비의 출발 대상(현재 Stage1 고정)을 선택 가능하게 바꾼다.
