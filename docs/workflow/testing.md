# 테스트 전략

> Unity Test Framework 사용. 테스트 어셈블리는 MIG-7에서 구성했다.

## 리팩토링 6단계 검증 (2026-11-03)

변경 전 전체는 EditMode **588/588**·PlayMode **167/167**, 관련 기준선은 **50/50**·**16/16**이다.
변경 후 전체 EditMode **597/597**·PlayMode **171/171** 통과(실패·스킵 0), C# 컴파일 오류·경고 0건이다.
최종 전체 XML에서 씬/정산 관련 클래스를 집계하면 **59/59**·**20/20**이다.

- `StageSettlementHistoryTests` 9건: 판별 중복 기록 방지·같은 결과의 다음 판 기록, 수신 정산의 로컬 중복 차단,
  이전 판과 현재 판 결과 분리, 전멸 사망수/음수 보정, 게시 실패 시 FIFO 보존·성공 기록 재게시 금지,
  1초 재시도와 방 이탈 시 대기열 삭제를 확인했다.
- `SceneInputTransitionFlowTests` 2건: 실제 Scene의 EventSystem/AudioListener를 중단·복원하고,
  원래 꺼진 컴포넌트와 이미 파괴된 컴포넌트를 안전하게 처리하는지 검사했다.
- `InGameLobbyFlowTests` 2건 추가: 실제 Local Host의 중복 정산이 이력·$50 기본 보상·실패 사망수를 다시
  바꾸지 않는지, NGO 로드 도중 세션 종료 후 `IsLoading`이 풀리고 타이틀로 복귀하는지 확인했다.
  기존 Tutorial 정상 종료/전멸/재시도·Result 플레이어 보존·인게임 로비 재스폰·상점·음성·복구 회귀도 통과했다.
- 새 정산 통합 검사의 첫 실행은 기존 `Failed to remove InGameLobby scene handles`로 실패했다.
  ADR-0018/SF-FIX-1의 알려진 오류를 `LogAssert.Expect`로 명시한 뒤 새 PlayMode 4건과 전체를 재실행했다.
  기존 통합 픽스처는 Steam 초기화 로그를 무시하고 예외를 모으므로 **통과는 Unity 오류 로그 0건을 뜻하지 않는다.**
  SF-FIX-1·Steam 2PC/원격 씬 동기화·사용자 화면/입력 체감은 남아 있다.
- `LoadAsync`의 순서·권한·예외 정리와 `StageSessionFlow`의 사망자 우선 디스폰/재스폰을 변경 전과 비교했다.
  `ISceneFlow`·`IStageSessionFlow`·정산 DTO·복구 스냅샷/코디네이터·Steam 로비 구현 등 계약/복구 파일 7개,
  직렬화 필드와 보호 에셋/설정 425개의 해시를 유지했다. 신규 헬퍼는 일반 C# 클래스이며 asmdef 참조는 바꾸지 않았다.
- 이번 단계의 대상 런타임은 Controller·세션 흐름 959줄에서 헬퍼 2개·EditMode 내부 접근 선언 포함 1,002줄로
  43줄 증가했다. 계획한 0~6단계 누적은 `Assets/Scripts` **47,927→40,911줄(−7,016)**,
  `Assets/Tests` 포함 **61,825→55,711줄(−6,114)**이다. 수치는 각 경로의 `.cs` 전체 행을 같은 방식으로 집계했다.

변경 전 전체 결과는 `Logs/tests/scene-flow-baseline-*`, 관련 기준선은 `scene-flow-related-baseline-*`,
기존 소스 14개는 `scene-flow-before-source.json`, 최종 비교 결과는 `scene-flow-final-audit.json`에 보관한다(커밋 제외).
기본 보상은 기존 규칙대로이고, 정산 문서·화면의 미정 문구는 추가 보상/치료비와 구분해 바로잡았다.

## 리팩토링 5단계 검증 (2026-11-02)

변경 전 전체 결과는 EditMode **589/589**·PlayMode **146/146**, 귀신 관련 기준선은 **93/93**·**11/11**이다.
타깃 교체·시야선·이동 소리 검사 9건을 먼저 추가해 기존 구현에서 `GhostAiExperimentFlowTests` **20/20**을 확인했다.
최종 전체 EditMode **588/588**·PlayMode **167/167** 통과(실패·스킵 0), C# 컴파일 오류·경고 0건이다.
최종 전체 XML에서 귀신 관련 클래스를 집계하면 EditMode **93/93**·PlayMode **32/32**다.

- `GhostAiExperimentFlowTests` 17건 추가: 기본/단서 모드의 1m 타깃 교체 4건, 벽 가림 2건,
  시야 뒤쪽의 이동 소리/웅크림/엎드림 3건, 일반/고위험 거리·각도와 순수 원뿔 시야 4건,
  경로 재계산 대기시간 복구/이전 경로 무효화 1건, 실제 귀신 디스폰의 NavMesh 해제 1건,
  같은 틱에 다른 후보가 있어도 기존 추격 대상의 굴착 노출을 먼저 기록하는 검사 2건.
- `GhostNavigationFlowTests` 4건 추가: 실제 NavMesh·CharacterController로 후보 경로 측정 후 진행 코너 보존,
  경로 없는 목적지로 수평 직선 이동 금지, 집 평면 경계 제한, 반복 Dispose와 경로/배회 캐시 해제를 확인했다.
- 기존 `GhostNavigationTests`·`Stage1GhostNavigationTests`·`GhostHouseBoundsTests`는 같은 검증을
  새 책임인 `GhostNavigation`에 직접 연결했다. 실제 Stage1 계단 NavMesh 검사도 통과했다.
- 첫 전체 실행은 PlayMode **165/165** 통과, EditMode는 `TryDetectPlayer`라는 소스 이름을 찾던
  `MoleSkillWiringTests` 1건이 실패했다. 해당 검사를 위 같은 틱 굴착 경합 2건으로 대체해 최종 재실행했다.
  EditMode 총수 감소는 이 이관 때문이다.
- RPC 2개의 속성·서명·본문, NetworkVariable·직렬화 필드, 복구 스냅샷 매핑과 기존 규칙/AI 파일 8개를
  비교했다. 설정·상태 기계·튜닝 값·복구 형식과 보호 파일 425개의 해시를 유지한다.
- Controller는 2,728→2,164줄이며 일반 헬퍼 2개가 594줄이다. 합계는 2,758줄(30줄 증가)로,
  이번 단계는 중복 계산 통합과 감지/이동 책임 분리에 초점을 뒀다. 새 컴포넌트·어셈블리·프리팹 배선은 없다.

실제 Stage1 이동 체감·시야/조명/음향 연출·Steam 다인 검증은 남아 있다. G-22·G-23·G-25 등 미결정 규칙을
이번 리팩토링으로 확정하거나 구현하지 않았다. 변경 전 전체 결과는 `Logs/tests/ghost-baseline-*`,
기존 소스 32개는 `ghost-before-source.json`, 새 흐름 검사의 기준선은 `ghost-flow-baseline-*`,
첫 전체 결과는 `ghost-first-full-*`에 보관한다(커밋 제외).

## 리팩토링 4단계 검증 (2026-11-02)

기존 검증 복제본에서 컴파일과 전체 EditMode **589/589**·PlayMode **146/146** 통과(실패·스킵 0).
관련 회귀 검사는 EditMode **42/42**·PlayMode **45/45** 통과. C# 컴파일 오류·경고 0건이다.
변경 전 전체 결과(591/591·133/133)는 `Logs/tests/input-action-baseline-*`, 소스 6개는
`input-action-before-source.json`, 변경 후 관련 결과는 `input-action-related-*`로 보관한다(커밋 제외).

- `PlayerInputLockFlowTests`: 기존 메뉴/복구 2건에 6건 추가. 가상 키보드·마우스로 정상 탭/홀드,
  점프 대기·1회 소비/즉시 폐기, 시전/매몰·휠 허용 입력, 사망과 잠금 해제 경합, 관전 차단,
  웅크린 자세 유지, 음성/음소거·원시 시점, 이동 반전/강제 이동의 잠금·관전 분리를 확인했다.
- `CorpsePushInputFlowTests`: 기존 시체 밀기 1건에 7건 추가. Local Host에서 시체 홀드→서버 운반자 등록→
  버튼 해제, 메뉴 진입 전 강제 해제, 사망·디스폰 정리와 실제 마우스 기반 플레이어 밀치기 차징→
  서버 승인→소유자 넉백 수신, 강제 해제 후 넉백 없음, 가까운 벽에 가린 대상 거절을 확인했다.
  시체가 kinematic에서 풀린 직후 속도가 0인 것을 정착으로 오인하지 않도록 낙하·회전이 끝난 뒤 조준한다.
  짧은 클릭의 물리 검사는 정착한 시체를 수평으로 밀어 바닥에 누르는 힘과 구분한다.
- `MoleSkillWiringTests`의 굴착 분기 문자열 검사와 `SpectatorTests`의 if문 등장 순서 검사 각 1건을
  실제 입력 검사로 대체했다. EditMode 총수 감소는 이 두 검사의 이관 때문이다.
- 기존 RPC 10개의 서명·속성과 서버 검증을 비교했다. 서버 본문 변경은 진단 로그에 들어 있던
  `ServerTryPush` 호출을 별도 실행문으로 옮긴 것뿐이다. NetworkVariable·직렬화 필드·기존 GUID와
  보호 파일 425개 해시를 보존했고 새 일반 C# 헬퍼 3개의 `.meta` GUID 중복이 없음을 확인했다.

실제 조작감·Steam 다인 동기화는 수동 검증이 필요하다. 자동 넉백 검사는 Local Host에서 대상 소유권을
호스트로 이전해 RPC 수신을 확인한 것이며 Steam 2PC 검증을 대신하지 않는다.

## 리팩토링 3단계 검증 (2026-11-02)

기존 검증용 복제 프로젝트에서 상점·로비 기준선(EditMode `InGameLobbyTests` 10/10·PlayMode
`InGameLobbyFlowTests` 5/5)을 확보한 뒤 공통 계산으로 변경했다.
변경 후 컴파일·전체 EditMode **591/591**, PlayMode **133/133** 통과(실패·스킵 0).

- `InGameLobbyTests`에 거래 결과·거절 시 입력 상태 보존 20건 추가: 품목별 차감·획득, 재구매·잔액 부족·
  잘못된 품목·권한/씬/로딩 거절, 촛대 반복 구매, 수리비 올림·내구도/라이터 보존.
- `InGameLobbyFlowTests`에 실제 Local Host 거래 검사 1건 추가: 비참가자·잘못된 품목·나무 드라이버 수리 거절,
  장비 구매 순서와 기존 내구도/라이터 보존, 잔액 부족·재구매 거절 뒤 재고·잔액·변경 알림 보존.
- 공통 계산은 저장 부작용이 없다. Local과 Steam의 참가자 검사·메시지/로비 데이터 형식·성공 알림과
  Steam의 장비 → 공동 상태 기록 순서는 유지했다. 이전 단계의 씬·프리팹·설정 보호 파일도 보존했다.
- Core 재컴파일에서 기존 `DeliveredCargoPile`·`FurnitureDeliveryZoneView`·`DetectionTargetMarker`의
  private `DestroyObject`가 Unity 상속 메서드를 가리는 CS0108 경고 3건을 확인했다.
  함수와 내부 호출 이름만 `DestroyRuntimeObject`로 정리했고, 최종 컴파일의 C# 오류·경고는 0건이다.

실제 Steam 로비 저장·원격 클라이언트 동기화·저장 실패 상황은 Steam 2PC 실기 확인이 필요하다.
변경 전 전체 결과는 `Logs/tests/shop-baseline-*`, 관련 기준선은 `shop-related-baseline-*`에 보관한다(커밋 제외).

## 리팩토링 0~2단계 검증 (2026-11-02)

`tools/run-tests.ps1 -Clone Always -ClonePath "C:\Users\mr hong\GHV"`로 열린 원본 에디터와 분리해 실행했다.

| 시점 | 컴파일 | EditMode | PlayMode |
| --- | --- | --- | --- |
| 변경 전 | 통과 | 580건 중 578 통과·기존 실패 2·스킵 0 | 122/122 통과·스킵 0 |
| 변경 후 | 통과 | **571/571 통과·스킵 0** | **132/132 통과·스킵 0** |

- `MoleSkillWiringTests`의 기존 실패 2건은 소스 문자열 검사를 실제 흐름 검사로 대체했다.
  `PlayerInputLockFlowTests`는 메뉴·복구 잠금 우선순위와 해제 뒤 굴착 이동/시점 복원을 검사한다.
  `GhostAiExperimentFlowTests`는 감지된 굴착의 수색·접근 포획과 비감지 굴착의 안전을 AI 실험 ON/OFF 양쪽에서 검사한다.
- `MapGeneratorTests` 9건은 삭제한 생성기 전용 검사다. 가구의 저장 물리 설정·질량 정의 배선은
  `ProjectWiringTests` 2건으로 옮기고, 실제 질량 주입은 `FurnitureThrowFlowTests` 2건으로 확인한다.
  프리팹의 저장 질량은 실행 시 `Awake`에서 덮어쓰므로 저장값과 정의값의 일치를 요구하지 않는다.
- `HudPresentationFlowTests` 2건은 HUD 5개의 배치·채움 방향과 생성 스프라이트/텍스처 해제·외부 아이콘 보존을 검사한다.
  기존 드라이버 진행·취소 UI와 실제 씬 전환을 포함한 전체 PlayMode도 통과했다.
- 보호 대상으로 잡은 씬·프리팹·설정·입력·기존 UI 스크립트 meta·프로젝트 설정 등 **425개 파일의 SHA-256이 동일**했다.
  검증 로그와 변경 전 결과는 `Logs/tests/`의 `refactor-baseline-*`에 보관한다(커밋 제외).

자동 검증은 배치·상태·자원 수명 범위다. 실제 HUD 화면의 육안 확인과 Steam 2PC 실기는 이번 작업에서 실행하지 않았다.

## Tutorial 검증 기록 (2026-10-02)

원본 프로젝트에서 컴파일 통과, 전체 EditMode 399건 중 397 통과. 기존 MoleSkillWiringTests의 귀신 굴착 노출·일시정지 잠금 순서 소스 검사 2건은 실패가 남아 있다.
InGameLobbyFlowTests·CleaningFlowTests·FurnitureThrowFlowTests·FurnitureCollisionFlowTests·GhostAiExperimentFlowTests를 함께 실행한 PlayMode 43/43 통과.
Tutorial 구조/배치·물리 이동·청소/반출 UI·씬 전환의 검증 범위와 다인 미검증 항목은 [tutorial-stage.md](../project/tutorial-stage.md#검증)에 기록한다.

## 1. 무엇을 테스트하는가

| 대상 | 테스트 종류 | 우선순위 |
| --- | --- | --- |
| 순수 로직(계산, 상태 전이, 규칙 판정) | EditMode | 높음 |
| ScriptableObject 데이터 유효성 | EditMode | 중간 |
| MonoBehaviour 상호작용, 비동기(UniTask) | PlayMode | 중간 |
| 네트워크 동기화·RPC 검증 | PlayMode | 높음 |
| 렌더링·아트·연출 | 구조·자원 수명은 자동 검사, 최종 외형·청감은 수동 확인 | — |

**원칙**: 버그가 나면 비싼 것부터 테스트한다. 커버리지 숫자를 목표로 삼지 않는다.

## 2. 폴더 구성

```
Assets/Tests/
├── EditMode/
│   ├── GhostHunter.Tests.EditMode.asmdef
│   ├── FurnitureLaunchDirectionTests.cs   발사각 보정 (순수 계산)
│   ├── FurniturePlacementPlannerTests.cs  랜덤 가구 수량·중복·충돌·방 분포·시드·실패 처리 (순수 계산)
│   ├── ServicesTests.cs                   서비스 로케이터 계약
│   ├── ProjectWiringTests.cs              레이어·씬 목록·네트워크 프리팹 식별자
│   ├── GhostPrototypeStateMachineTests.cs 귀신 활동·경고·어택·자연 진정 전이·팀 평균 어택 판정
│   ├── GhostAiExperimentTests.cs          단서 만료·예측 제한·수색 점수·고정 기억·토글·현상 간격
│   ├── GhostVisionTests.cs                원뿔 시야 각도·거리 판정, 시야 표시 메시 생성
│   ├── GhostHouseBoundsTests.cs            집 내부 X/Z 활동 경계 판정·좌표 보정
│   ├── SanityStateTests.cs                정신력 증감·누적·중복·평균·디버프
│   └── PlayerSpawnRegistryTests.cs        스폰 지점 빈자리 선택
└── PlayMode/
    ├── GhostHunter.Tests.PlayMode.asmdef
    ├── NetworkFurnitureFixture.cs         호스트 세션 + 가구 스폰 토대
    ├── GhostAiExperimentFlowTests.cs      Host 단서 상실·OFF 복귀·은신 안전·소음·이벤트 해제
    ├── PlayerInputLockFlowTests.cs        메뉴·복구 잠금 우선순위·해제 후 입력 복원
    ├── HudPresentationFlowTests.cs        HUD 배치·채움 방향·생성 자원 해제
    └── FurnitureThrowFlowTests.cs         잡기 → 차징 → 발사 상태 기계
```

- 테스트 asmdef는 `UnityEngine.TestRunner`, `UnityEditor.TestRunner`를 참조하고
  `nunit.framework.dll`을 Override References에 추가한다.
- `defineConstraints`에 `UNITY_INCLUDE_TESTS`를 넣어 플레이어 빌드에서 빠지게 한다.
- **EditMode 어셈블리는 `includePlatforms: ["Editor"]`**, PlayMode 어셈블리는 플랫폼 제한이 없다.
- 테스트 대상 런타임 asmdef를 참조에 추가한다 → [../architecture/overview.md §3](../architecture/overview.md)
- `internal` 멤버를 테스트해야 하면 대상 어셈블리의 `AssemblyInfo.cs`에 `InternalsVisibleTo`를 추가한다.
  현재는 `GhostHunter.Gameplay`가 두 테스트 어셈블리에 열려 있다
  (`FurnitureLauncher.ResolveLaunchDirection`).

## 3. 작성 규칙

- 파일명: `<대상클래스>Tests.cs` (예: `HealthTests.cs`)
- 메서드명: `<대상>_<조건>_<기대결과>`

```csharp
[Test]
public void TakeDamage_체력보다_큰_피해_체력은_0이_된다()
{
    var health = new Health(maxHealth: 100);
    health.TakeDamage(150);
    Assert.AreEqual(0, health.Current);
}
```

- 테스트 1개 = 검증 1개. `Assert`를 나열해 여러 개념을 섞지 않는다.
- 테스트는 서로 독립적이어야 한다. 실행 순서에 의존 금지.
  **전역 상태(`Services`)를 건드리는 테스트는 MUST `TearDown`에서 등록을 해제한다.**
  `Services.Unbind`는 제네릭 타입이 등록할 때와 같아야 지워진다 — 구체 타입으로 부르면
  조용히 아무것도 지우지 않고 다음 테스트로 새어 나간다.
- PlayMode 테스트는 `[UnityTest]` + `IEnumerator`, 생성한 `GameObject`는 MUST `TearDown`에서 정리한다.

## 4. 네트워크 테스트

- `NetworkManager`를 코드로 구성해 Host/Client를 띄우고 검증한다 → `NetworkFurnitureFixture`.
- 검증 포인트: 서버 권위 유지, NetworkVariable 전파, RPC 검증 로직 거부 케이스.
- 프레임 대기는 `yield return null` 또는 조건 대기 헬퍼를 쓴다. 고정 `WaitForSeconds` 남용 금지(불안정).

### 4.1 Steam 트랜스포트는 테스트하지 않는다

이 프로젝트의 런타임 트랜스포트는 **FacepunchTransport(Steam)** 이지만, 자동화 테스트는 Steam을 쓰지 않는다.

| 이유 | 내용 |
| --- | --- |
| 환경 | batchmode/CI에는 Steam 클라이언트가 없어 `SteamClient.Init`이 실패한다 |
| 계정 | Steam은 계정당 인스턴스를 1개로 제한해 한 프로세스에서 2인을 만들 수 없다 |

**따라서 테스트에서는 `UnityTransport`로 `NetworkManager`를 구성한다.**
이것이 성립하려면 게임 로직이 트랜스포트 구현을 직접 참조하지 않아야 한다
→ [../architecture/steam.md §7](../architecture/steam.md). 이 규칙을 어기면 네트워크 로직 전체가 테스트 불가능해진다.

Steam 실경로(로비·초대·SDR 연결)는 사람이 2대로 확인한다 → [playbooks.md PB-08](playbooks.md).

### 4.2 테스트는 프리팹 에셋에 기대지 않는다

`NetworkFurnitureFixture`는 가구를 `Assets/Prefabs/**`에서 불러오지 않고 코드로 조립한다.

- 프리팹을 읽으려면 `AssetDatabase`가 필요해 **플레이어 빌드에서 돌릴 수 없게 된다.**
- 검증 대상은 배치가 아니라 `FurnitureGrabTarget` 상태 기계다. 씬·프리팹의 가구 치수가
  바뀔 때마다 테스트가 흔들리면 안 된다.

런타임에 만든 `NetworkObject`는 `GlobalObjectIdHash`가 0이라 NGO가 서로를 구분하지 못한다.
픽스처가 인스턴스마다 고유 값을 리플렉션으로 넣어 준다 → [../conventions/unity-assets.md §5.2](../conventions/unity-assets.md)

### 4.3 (삭제) 맵 생성 도구 테스트

2026-11-02 작업 트리에서 남아 있던 도구 11개와 `MapGeneratorTests`를 다시 정리했다.
현재 저장 에셋을 검사하는 `ProjectWiringTests`와 실제 질량 주입을 확인하는 `FurnitureThrowFlowTests`로
유효한 물리 검증을 이관했다. 생성기 자체의 치수·카탈로그 생성·과거 Game 도면 검사는 제거했다.

맵 생성 도구와 `MapGeneratorTests` 는 2026-09-28 에 지웠다
([ADR-0020](../architecture/decisions/ADR-0020-remove-one-off-editor-setup-tools.md)). 설치 결과를 검사하던
`…Setup.ValidateInstallation` 호출 테스트도 함께 지웠다 — 씬·프리팹 배선은 런타임 `Awake` 오류 로그와 PlayMode 흐름 테스트로 드러난다.

### 4.4 접속하지 않은 홀더로 2인 경로를 흉내 낼 때

2인 잡기 규칙은 홀더가 둘이어야 검사할 수 있는데, 한 프로세스에서 진짜 클라이언트를 둘
띄우는 것은 비싸다. 그래서 두 번째 홀더는 접속하지 않은 가짜 clientId를 쓴다.

**그 홀더는 플레이어 오브젝트가 없다.** `FurnitureHoverMotor`는 `FixedUpdate`마다 홀더의
플레이어 위치를 확인해 찾지 못하면 강제 해제하므로, **물리 스텝을 사이에 두면 홀더가 사라진다.**
상태 전이는 `FurnitureGrabTarget` 안에서 동기적으로 끝나므로 2인 경로의 단언은
`yield` 없이 같은 프레임에 이어서 한다.

### 4.5 여러 프로세스로 입장·이탈 확인 (`LocalSessionAutomation`)

한 프로세스 PlayMode 테스트로는 게스트의 씬 동기화·자발적 이탈·호스트 종료를 볼 수 없다. 개발 빌드와
에디터에만 들어가는 `DebugTools/LocalSessionAutomation`(`DEVELOPMENT_BUILD || UNITY_EDITOR`)이 명령행
인자로 Local(UTP) 세션을 자동 진행한다. `RuntimeInitializeOnLoadMethod` 로 인자가 있을 때만 생기므로
씬 배치가 필요 없다.

| 인자 | 동작 |
| --- | --- |
| `-gh-auto=host` / `-gh-auto=client` | Title 이 뜨면 Local 모드로 `StartHostInGameScene(ProtoTypeGame)`(구 Game — ADR-0019) / `StartLocalClient()` |
| `-gh-leave-after=초` | 세션 참여 후 그 시간이 지나면 "타이틀로"와 같은 순서로 떠난다(게스트는 로비 유지, 호스트는 로비 퇴장) |
| `-gh-return-on-end` | 요청하지 않은 종료(호스트 이탈 등) 뒤 끊김 모달 "확인"과 같은 경로로 Title 로드 |
| `-gh-bot-follow=번호` | 실제 Local 클라이언트가 자기 플레이어를 Host 뒤에서 따라간다. 개발 빌드의 봇 모드 |
| `-gh-quit-on-end` / `-gh-quit-after=초` | 세션 종료 뒤 / 지정 시간이 지나면 해당 클라이언트 프로세스를 종료 |

2초마다 `[GhAuto] t phase net connected players team flow scenes errors sessionEnded` 한 줄을 남기고,
오류·예외 로그를 세어 앞 8건을 함께 찍는다.

절차: 빌드 씬 전체로 **Development** 빌드(`Build/SessionTest/`, gitignore 대상) → `-batchmode -nographics
-logFile <경로>` 로 인스턴스를 띄운다. **호스트를 먼저 띄워 `phase=InSession` 을 확인한 뒤 클라이언트를
띄운다** — 호스트가 떠나기 전에 클라이언트가 붙을 만큼 `-gh-leave-after` 를 넉넉히 준다. 같은 PC 에서 PlayMode 테스트를
동시에 돌리면 초기화 시간 초과가 난다. 결과 기록 → [pause-menu.md §10.5](../architecture/pause-menu.md)

#### 혼자 하는 Local Host + 봇 검증 (Windows)

`GhostHunter > 로컬 테스트 봇` 메뉴는 위 자동 클라이언트를 개발 빌드로 띄운다. 각 봇은
**별도 NGO 클라이언트 프로세스**이고, Host의 플레이어를 따라 이동한다. 씬 전환·플레이어
스폰·원격 위치 복제·정신력 사망·전멸 Result를 한 PC에서 확인할 수 있다.

1. Play 모드를 끄고 `GhostHunter > 로컬 테스트 봇 > 1. 개발 빌드 만들기` 실행.
   활성 Build Settings 씬의 첫 항목이 `Bootstrap`이어야 한다. 출력은 `Build/LocalBots/`.
2. `Bootstrap`에서 Play → **Tab** 접속 HUD → `Local` 모드 → `Host`. Tutorial 드릴카에서 종료·정산한 뒤 인게임 로비 단말기(E)로
   `Stage1`에 출발한다(봇 메뉴는 스테이지 씬 — Tutorial·Stage1·ProtoTypeGame — 에서만 동작한다).
3. `GhostHunter > 로컬 테스트 봇 > 2. 봇 1명 추가` 또는 `3. 봇 2명 추가` 실행.
   메뉴를 다시 실행해 총 3명까지 붙일 수 있다. Host HUD의 접속 수와 스테이지 화면의 원격
   캐릭터를 확인한다. 봇 로그는 `Build/LocalBots/Logs/bot-N.log`에 남는다.
4. 사망 흐름은 Host의 Tab HUD → 정신력 → `원격 플레이어 1명 사망 처리 (봇 검증)`으로 확인한다.
   마지막에는 Host도 사망 처리해 전멸 Result → Lobby를 확인한다.
5. `4. 실행한 봇 종료`로 정리한다. Play를 끝내거나 에디터를 종료해도 실행한 봇을 종료한다.

봇 이동은 테스트용 `CharacterController.Move`이며 사람이 누르는 입력·경로 찾기를 재현하지
않는다. 시체를 **두 명이 함께 운반하는 입력**과 마이크/음성, Steam P2P는 검증하지 못한다.
시체 목격은 봇이 시체를 시야에 두었을 때만 발생한다. 벽이나 복도에 막히면 봇은 따라오지
못할 수 있다. 빌드를 새로 만든 뒤에야 코드 변경이 봇에 반영된다.

## 5. 실행

### 5.1 에디터 Test Runner (권장 · 판정 기준)

`Window > General > Test Runner`. 에디터가 켜져 있으면 이쪽이 **정답**이다 —
§5.3 때문에 batchmode 에서는 일부 검사를 할 수 없다.

### 5.2 batchmode CLI — `tools/run-tests.ps1` (에디터를 켜 둔 채로 가능 · 에이전트 기본)

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/run-tests.ps1                     # EditMode
powershell -NoProfile -ExecutionPolicy Bypass -File tools/run-tests.ps1 -Platform All        # EditMode → PlayMode
powershell -NoProfile -ExecutionPolicy Bypass -File tools/run-tests.ps1 -Platform PlayMode -Filter "VoicePlaybackTests"
powershell -NoProfile -ExecutionPolicy Bypass -File tools/run-tests.ps1 -Platform Compile    # 컴파일만
```

- 에디터가 이 프로젝트를 열고 있으면(`Temp/UnityLockfile` 잠김) **검증용 복제 프로젝트** `%USERPROFILE%\GHV`로
  `Assets`·`Packages`·`ProjectSettings`를 미러링(robocopy /MIR)해 거기서 돌린다. 작업 트리 그대로(커밋 안 한 변경 포함)다.
  경로를 짧게 둔 것은 복제본 긴 경로의 Burst 오류 때문이다. 바꾸려면 `-ClonePath` 또는 `GH_VERIFY_CLONE`.
  복제본은 자기 `Library`를 유지하므로 전체 임포트는 첫 실행에만 든다(2026-09-28 실측: 첫 실행 EditMode 포함 약 3분).
- 콘솔에는 **요약만** 나온다 — `total/passed/failed/skipped`, 실패 테스트 이름과 메시지 첫 줄, skip 5건.
  XML이 없으면 로그에서 `error CS`·라이선스·인스턴스 충돌 줄만 뽑는다. 전체 로그·XML은 `Logs/tests/`.
  종료 코드 0 = 요청한 실행이 모두 끝났고 실패 0.
- Unity 경로는 `ProjectSettings/ProjectVersion.txt` 버전의 Hub 기본 설치 경로다. 다르면 `-UnityPath` 또는 `UNITY_EXE`.
- 에이전트(Claude Code)는 **백그라운드로 실행**한다 — 복제본 첫 임포트가 도구 제한 시간을 넘길 수 있다.

직접 부를 때(에디터를 끈 상태, `$PROJ`는 이 저장소 루트):

```powershell
$UNITY = "C:\Program Files\Unity\Hub\Editor\6000.3.20f1\Editor\Unity.exe"
$PROJ  = (Get-Location).Path

# EditMode
& $UNITY -runTests -batchmode -nographics -projectPath $PROJ `
  -testPlatform EditMode -testResults "$PROJ\Logs\editmode-results.xml" -logFile -

# PlayMode (-nographics 사용하지 않음)
& $UNITY -runTests -batchmode -projectPath $PROJ `
  -testPlatform PlayMode -testResults "$PROJ\Logs\playmode-results.xml" -logFile -
```

- 직접 부를 때는 MUST Unity 에디터를 먼저 종료한다(프로젝트 잠금). **끄기 전에 §5.1로 해결되는지 먼저 확인한다.**
  `-logFile -`는 로그 전체를 콘솔로 쏟으므로 에이전트는 쓰지 않는다 — 스크립트를 쓴다.
- 결과 XML의 `<test-run result="..." total=... passed=... failed=... skipped=...>`를 확인해 보고한다.
  **`skipped`가 0이 아니면 §5.3 때문일 수 있으니 그대로 보고한다.**

### 5.3 batchmode 한계 — 프로젝트 스크립트가 에셋에 바인딩되지 않는다

> 2026-08-21 · Unity 6000.3.20f1 · Windows 에서 확인.

`-batchmode`로 띄운 에디터는 **`Assets/` 아래 스크립트를 관리 클래스에 연결하지 못한다.**
결과는 이렇다.

| 증상 | 예 |
| --- | --- |
| 우리 `ScriptableObject` 에셋이 열리지 않는다 | `LoadAssetAtPath<SceneNameSO>` → `null` |
| 씬의 우리 컴포넌트가 "missing script" 로 보인다 | `Bootstrap` 의 `SceneFlowController` 등 |
| `MonoScript.GetClass()` 가 언제나 `null` | 새로 만든 빈 스크립트도 마찬가지 |

**패키지 어셈블리(NGO 등)는 정상이다.** 프리팹을 `GameObject`로 열거나 `NetworkObject`를
읽는 것은 되고, 우리 `MonoBehaviour`/`ScriptableObject` 타입만 안 된다.

이것은 **이 저장소의 문제가 아니다.** 스크립트 한 개짜리 새 프로젝트를 만들어도 같은 결과가
나온다(대조군 확인). 따라서:

- 우리 SO·`MonoBehaviour`에 기대는 EditMode 테스트는 **파일·프리팹 존재는 단언하고,
  역직렬화나 `GetComponent`가 실패하면 `Assert.Ignore`로 건너뛴다.** 에셋을 지운 실수는 계속
  잡히고, 환경 한계만 비켜 간다. `ProjectWiringTests`의 `LoadCatalogOrIgnore()`와
  `GetProjectComponentOrIgnore<T>()`가 그 형태다.
- **패키지 어셈블리로 되는 검사는 `Assert.Ignore` 호출보다 앞에 둔다.** Ghost 프리팹 테스트는
  `NetworkObject`의 `GlobalObjectIdHash`를 먼저 단언하고 컨트롤러 확인을 마지막에 둔다 —
  해시가 깨지면 batchmode 에서도 스킵되지 않고 그대로 실패한다.
- 그래서 batchmode EditMode 는 2026-08-21 기준 **93건 중 88 통과 / 5 스킵 / 0 실패**가 정상이었다
  (SceneNameSO 3건 + Player 정신력 배선 1건 + Ghost_Prototype 컨트롤러 배선 1건).
  에디터 Test Runner 에서는 93/93 통과했다.
  **2026-09-17 재측정에서는 검증용 복제 프로젝트 batchmode 에서 EditMode 302/302 · PlayMode 57/57 로
  스킵이 남지 않았다.** 위 `Assert.Ignore` 장치는 그대로 두되, 스킵 건수를 기대값으로 삼지 않는다.
- **씬·에셋을 만드는 에디터 스크립트를 `-executeMethod`로 batchmode 에서 돌리지 않는다.**
  `AssetDatabase` 로드가 기존 설정 에셋을 못 찾아 새로 만들어 버린다(2026-08 생성 도구에서 확인).
- `-quit -batchmode -nographics ... -logFile -` 로 하는 **컴파일 검증은 영향받지 않는다.**

### 5.4 batchmode 한계 — 입력이 `InputAction` 까지 오지 않는다

> 2026-09-17 · Unity 6000.3.20f1 · Windows 에서 확인.

키 입력을 흉내 내는 테스트는 **EditMode 에서 아예 불가능하고, PlayMode 에서는 설정을 바꿔야 한다.**

| 상황 | 결과 |
| --- | --- |
| EditMode + `InputState.Change` | `keyboard.vKey.isPressed` 는 `true` 가 되지만 `action.IsPressed()` 는 **계속 false**. 액션 상태가 에디트 모드에서 갱신되지 않는다 |
| PlayMode + `QueueStateEvent` (기본 설정) | 이벤트가 **버려진다.** 기본값 `PointersAndKeyboardsRespectGameViewFocus` 인데 배치모드에는 포커스가 없다 |
| PlayMode + 두 설정을 바꾼 뒤 | 정상 동작. `yield return null` 한 프레임 뒤 액션이 눌린 상태로 읽힌다 |

```csharp
// 테스트 동안만 바꾸고 finally 에서 되돌린다.
InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
InputSystem.settings.editorInputBehaviorInPlayMode =
    InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
```

- `WasPressedThisFrame` 은 이벤트가 처리된 **그 한 프레임에서만** 참이다. `yield return null` 직후에 단언한다.
- 본보기: `Assets/Tests/PlayMode/VoiceInputTests.cs`.
- **사용자 에디터에서만 통과하는 입력 테스트를 EditMode 에 두지 않는다.** 포커스가 있는 에디터에서는
  초록이지만 batchmode/CI 에서는 코드와 무관하게 빨간색이 된다.

## 6. 수동 검증 체크리스트

자동화하기 어려운 것은 사용자에게 아래 형식으로 요청한다.

```
확인 요청:
1. Bootstrap 씬에서 Play
2. Host로 시작 → 캐릭터가 WASD로 이동하는지
3. 빌드 실행 파일을 켜고 Client로 접속 → 양쪽에서 서로 보이는지
4. 클라이언트에서 이동 시 호스트 화면에서도 위치가 따라오는지
```

### 스테이지 시스템 (Steam PC 2대·계정 2개 이상 필요)

1. 방장 혼자 시작 버튼이 비활성화이고, 게스트가 들어와 준비하면 활성화되는지 확인한다.
2. 로비에서 양쪽 마이크가 들리고 Temp1·2·3 구매 시 공동 잔액·보유 수량이 양쪽에 같게 보이는지 확인한다.
3. 시작 로딩 중 방 코드·초대 재참가가 거절되고, 스폰 후 드릴카 구역·조립 구역이 네 시작 지점과 겹치는지 확인한다.
4. 목표 가구를 녹색 반출 구역에 들였을 때만 완료되어 양쪽 화면에서 사라지고(들고 있던 경우 손에서 풀림), 빨간 종료 장치에서 확인 후 가구·청소·생존·실종·사망 수치가 양쪽 Result에 같은지 확인한다.
5. Result에서 생존·사망 음성이 서로 들리고, 방장만 **인게임 로비** 복귀를 진행하며(세션 유지), 인게임 로비에서 이력이 유지되는지 확인한다.
6. 방장의 `스테이지 나가기`가 정산 없이 양쪽을 **인게임 로비**로 보내는지(세션 유지·플레이어 재스폰), 인게임 로비 단말기에서 방장만 구매·출발할 수 있는지 확인한다(ADR-0018).
7. 로딩 중 방장이 이탈했을 때 2명 이상이면 새 방장이 이어서 시작하고, 1명이면 취소되는지 확인한다.
8. 진행 중 호스트 이탈과 상태 복구는 ADR-0017의 재호스팅 구현이 끝난 뒤 검증한다. 현재 완료로 판정하지 않는다.

## 7. 현재 상태

> 2026-08-31 기준. 열린 에디터의 Test Runner를 실행한 결과다.

| 항목 | 상태 |
| --- | --- |
| 테스트 어셈블리 | ✅ EditMode / PlayMode 2개 |
| EditMode 테스트 | **119건 — 119 통과** (귀신 상태 기계·시야 기하·프리팹 배선·집 내부 활동 경계·굴착 입력·걷기/달리기 소리 반경 배선 포함) |
| PlayMode 테스트 | **12건 — 12 통과** |
| **런타임 스모크 테스트** | `Assets/Scripts/DebugTools/PrototypeRuntimeSmoke.cs` — 존치 (§7.1) |
| CI | ❌ 없음 → roadmap 백로그 |

### 7.1 런타임 스모크 테스트를 남겨 두는 이유

`PrototypeRuntimeSmoke`는 정식 테스트가 아니라 **빌드된 플레이어에서 `-smoke-test` 인자로
도는 점검 스크립트**다. MIG-7에서 상태 기계 검증은 `FurnitureThrowFlowTests`로 옮겼지만,
스크립트 자체는 지우지 않았다.

- 테스트 러너는 **에디터 안**에서 돈다. 스모크는 `BuildSmokeBatch`가 만든 **실제 실행 파일**을
  켜서 확인하므로 겹치지 않는다 — 빌드에서만 드러나는 문제(스트립핑, 씬 목록, IL2CPP)를 잡는다.
- 코루틴을 쓰는 것은 [../conventions/code-style.md §8](../conventions/code-style.md)의 유일한 예외다.
- `Bootstrap` 씬의 `NetworkRig`에 컴포넌트로 붙어 있다. 지우려면 씬 편집이 필요하다.

**중복이라고 판단되면 지우는 것도 가능하다.** 그때는 씬에서 컴포넌트를 먼저 떼고
스크립트를 지운다(순서를 뒤집으면 씬에 missing script 가 남는다).

### 7.2 테스트는 UTP 경로에 의존한다

테스트는 `UnityTransport`로 세션을 만든다(§4.1). 이 경로의 존치는
[../architecture/decisions/ADR-0011](../architecture/decisions/ADR-0011-local-transport-path.md)에서
**Accepted** 로 확정됐다 — 로컬 UTP 를 남기고 릴리스는 `TransportModeBuildGuard`가 막는다.

PlayMode 테스트는 개발용 Local Host의 기본 포트 `7777`과 충돌하지 않도록 테스트 전용 포트
`17777`을 사용한다.

---

관련: [development-loop.md](development-loop.md)

최종 갱신: 2026-09-28 (§2·§4.3·§5.3 생성 도구·MapGeneratorTests 삭제 반영 — ADR-0020)
