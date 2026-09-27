# ADR-0020: 일회성 에디터 설치·생성 도구를 지우고 씬·프리팹 자체를 원본으로 삼는다

- **상태**: Accepted
- **날짜**: 2026-09-28
- **결정자**: 사용자("일회적으로 있는 에디터 코드들 싹 다 삭제")
- **관련**: [ADR-0009 씬 배치 레벨 오브젝트](ADR-0009-scene-placed-level-objects.md), [ADR-0013 B안 랜덤 배치](ADR-0013-plan-b-random-furniture.md),
  [ADR-0019 Stage1 분리](ADR-0019-stage1-scene-split.md), [unity-assets.md §1](../../conventions/unity-assets.md)

## 배경

기능마다 `GhostHunter > … 설치`/`… 생성`/`… 검증` 메뉴가 붙은 에디터 스크립트를 만들어 씬·프리팹을 배선해 왔다(메뉴 36개).
대부분 한 번 돌리고 결과를 씬·프리팹에 저장한 뒤로는 다시 쓰지 않았고, 일부(`프로토타입 게임 생성`·`Stage1 씬 생성`)는
씬을 처음부터 다시 만들어 손 배치를 지우는 위험한 도구였다. 에이전트가 씬·프리팹을 텍스트로 직접 고칠 수 있게 되면서
(2026-09-28, [unity-assets.md §1](../../conventions/unity-assets.md)) 도구를 거쳐야 할 이유도 사라졌다.

## 결정

`Assets/Scripts/Editor/` 의 일회성 설치·생성·검증 도구를 모두 지운다. **저장된 씬·프리팹·설정 에셋이 유일한 원본이다.**

- 지운 것: `*Setup.cs` 21개(청소·가구 멀티 드라이버·귀신·인게임 로비·메뉴 씬·두더지 굴착 연출·두더지 스킬·네트워크 리그 선택·
  일시정지·B안 랜덤 배치·B/C안 프로토타입·플레이어 캐릭터 모델·닉네임 표시·프로토타입 게임·퀵슬롯·정신력 연출·정신력 시스템·
  정신력 테스트베드·관전·Stage1·음성 채팅), 맵 생성기 `HousePrototypeBuilder`·`FurnitureCatalog`·`MansionGrayboxBuilder`,
  이들을 테스트에 열어 주던 `AssemblyInfo.cs`, 생성기 테스트 `MapGeneratorTests`.
- 남긴 것(상시 도구): `TransportModeBuildGuard`(빌드 가드), `LocalTestBotLauncher`(`GhostHunter > 로컬 테스트 봇`),
  `Utility/SceneRuler`(`GhostHunter > Utility > Scene Ruler`).
- 설치 결과를 검사하던 테스트(`Validate…` 호출 5건)와 설치 도구 상수만 보던 테스트는 지웠다. 런타임 규칙 테스트는 그대로다.

## 결과

### 긍정
- 씬을 통째로 다시 만드는 메뉴가 없어져 손 배치가 날아갈 위험이 사라졌다.
- 도구와 씬이 갈라지는 문제(도구를 고치지 않고 씬만 고치면 재실행 때 덮어써짐)가 없어졌다.

### 부정 / 감수하는 비용
- **가구 종류 추가·집 재생성이 자동이 아니다.** 새 가구 종류는 프리팹을 직접 만들고(한 종류 = 한 프리팹, 스케일 1)
  `Furniture_Library`·해당 풀에 인스턴스를 놓는다. 집 구조를 바꾸면 씬을 직접 고친다.
- 씬 `NetworkObject` 해시 확정(`RefreshScenePlacedNetworkObjectsInCurrentScene`)·네트워크 프리팹 해시 검사
  (`ValidateNetworkPrefabIdentity`) 도우미도 함께 사라졌다 → 필요하면 [unity-assets.md §5.2](../../conventions/unity-assets.md)
  절차대로 에디터에서 확정한다.
- 씬·프리팹 배선 회귀를 잡던 설치 검증 테스트가 없어졌다. 배선이 깨지면 런타임 `Awake` 오류 로그로 드러난다.
- 도구 원본은 git 이력(커밋 `6f6e722` 까지)에 있다. 단 `InGameLobbySetup`·`Stage1SceneSetup`·`PlayerCharacterSetup`·
  `PlayerNameTagSetup` 은 커밋된 적이 없어 이력에도 없다.

### 후속 작업
- [x] 영향받는 문서 갱신: CLAUDE.md, unity-assets.md, testing.md, overview.md, 각 시스템 architecture 문서
- [x] 코드 반영: 스크립트·테스트 삭제, 런타임 오류 메시지에서 메뉴 안내 제거

## 재검토 조건

같은 배선을 여러 씬·프리팹에 반복해야 하는 작업이 생기면, 그 작업에 한정한 도구를 새로 만들고 끝나면 지운다.
