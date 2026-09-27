# 테스트 전략

> Unity Test Framework 사용. 테스트 어셈블리는 MIG-7에서 구성했다.

## 1. 무엇을 테스트하는가

| 대상 | 테스트 종류 | 우선순위 |
| --- | --- | --- |
| 순수 로직(계산, 상태 전이, 규칙 판정) | EditMode | 높음 |
| ScriptableObject 데이터 유효성 | EditMode | 중간 |
| MonoBehaviour 상호작용, 비동기(UniTask) | PlayMode | 중간 |
| 네트워크 동기화·RPC 검증 | PlayMode | 높음 |
| 렌더링·아트·연출 | 테스트 안 함 (수동 확인) | — |

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
│   ├── GhostPrototypeStateMachineTests.cs 귀신 5상태·강제 진정 전이·10초 어택 판정(팀 평균 기반)
│   ├── GhostVisionTests.cs                원뿔 시야 각도·거리 판정, 시야 표시 메시 생성
│   ├── GhostHouseBoundsTests.cs            집 내부 X/Z 활동 경계 판정·좌표 보정
│   ├── SanityStateTests.cs                정신력 증감·누적·중복·평균·디버프
│   └── PlayerSpawnRegistryTests.cs        스폰 지점 빈자리 선택
└── PlayMode/
    ├── GhostHunter.Tests.PlayMode.asmdef
    ├── NetworkFurnitureFixture.cs         호스트 세션 + 가구 스폰 토대
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
2. `Bootstrap`에서 Play → **Tab** 접속 HUD → `Local` 모드 → `Host`. 인게임 로비에서 단말기(E)로 스테이지를 출발해
   `Stage1` 진입을 기다린다(봇 메뉴는 스테이지 씬 — Stage1·ProtoTypeGame — 에서만 동작한다).
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

### 5.2 batchmode CLI (에디터를 끌 수 있을 때 / CI)

```powershell
$UNITY = "C:\Program Files\Unity\Hub\Editor\6000.3.20f1\Editor\Unity.exe"
$PROJ  = "C:\MainScreen\Dev\GitDirectory\GhostHunter"

# EditMode
& $UNITY -runTests -batchmode -nographics -projectPath $PROJ `
  -testPlatform EditMode -testResults "$PROJ\Logs\editmode-results.xml" -logFile -

# PlayMode (-nographics 사용하지 않음)
& $UNITY -runTests -batchmode -projectPath $PROJ `
  -testPlatform PlayMode -testResults "$PROJ\Logs\playmode-results.xml" -logFile -
```

- MUST Unity 에디터를 먼저 종료한다(프로젝트 잠금). **끄기 전에 §5.1로 해결되는지 먼저 확인한다.**
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
4. 목표 가구를 녹색 반출 구역에 들였을 때만 완료되고, 빨간 종료 장치에서 확인 후 가구·청소·생존·실종·사망 수치가 양쪽 Result에 같은지 확인한다.
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
