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
│   ├── ServicesTests.cs                   서비스 로케이터 계약
│   ├── ProjectWiringTests.cs              레이어·씬 목록·네트워크 프리팹 식별자
│   ├── GhostPrototypeStateMachineTests.cs 귀신 5상태·강제 진정 전이·10초 어택 판정(팀 평균 기반)
│   ├── GhostVisionTests.cs                원뿔 시야 각도·거리 판정, 시야 표시 메시 생성
│   ├── GhostHouseBoundsTests.cs            집 내부 X/Z 활동 경계 판정·좌표 보정
│   ├── SanityStateTests.cs                정신력 증감·누적·중복·평균·디버프
│   ├── PlayerSpawnRegistryTests.cs        스폰 지점 빈자리 선택
│   └── MapGeneratorTests.cs               맵 생성 도구 + 자체 검증 함수
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
- 검증 대상은 배치가 아니라 `FurnitureGrabTarget` 상태 기계다. 맵 생성 도구가 가구 치수를
  바꿀 때마다 테스트가 흔들리면 안 된다.

런타임에 만든 `NetworkObject`는 `GlobalObjectIdHash`가 0이라 NGO가 서로를 구분하지 못한다.
픽스처가 인스턴스마다 고유 값을 리플렉션으로 넣어 준다 → [../conventions/unity-assets.md §5.2](../conventions/unity-assets.md)

### 4.3 맵 생성 도구는 에셋 없이 검증한다

`MapGeneratorTests` 는 `GhostHunter > 프로토타입 게임 생성` 이 하는 일 중 **씬 오브젝트를
만들고 검증하는 부분만** 떼어 돌린다. 프리팹을 굽거나 씬을 저장하지 않는다.

- 설정 SO·머티리얼은 `ScriptableObject.CreateInstance` / `new Material` 로 만든다.
- 가구 원본은 `FurnitureCatalog` 에 **메모리 오브젝트로** 등록한다. 실제 생성 도구는 같은
  자리에 프리팹 에셋을 등록한다 — 카탈로그가 둘을 구분하지 않는 덕분에 배치·검증 로직이
  그대로 돌아간다.
- 따라서 **프리팹 굽기와 씬 저장은 이 테스트가 검증하지 않는다.** 그 두 가지는 §5.3 때문에
  batchmode 에서 아예 되지 않으므로, 에디터에서 생성 도구를 실행해 확인해야 한다.

### 4.4 접속하지 않은 홀더로 2인 경로를 흉내 낼 때

2인 잡기 규칙은 홀더가 둘이어야 검사할 수 있는데, 한 프로세스에서 진짜 클라이언트를 둘
띄우는 것은 비싸다. 그래서 두 번째 홀더는 접속하지 않은 가짜 clientId를 쓴다.

**그 홀더는 플레이어 오브젝트가 없다.** `FurnitureHoverMotor`는 `FixedUpdate`마다 홀더의
플레이어 위치를 확인해 찾지 못하면 강제 해제하므로, **물리 스텝을 사이에 두면 홀더가 사라진다.**
상태 전이는 `FurnitureGrabTarget` 안에서 동기적으로 끝나므로 2인 경로의 단언은
`yield` 없이 같은 프레임에 이어서 한다.

## 5. 실행

### 5.1 에디터 Test Runner (권장 · 판정 기준)

`Window > General > Test Runner`. 에디터가 켜져 있으면 이쪽이 **정답**이다 —
§5.3 때문에 batchmode 에서는 일부 검사를 할 수 없다.

Unity MCP 가 연결돼 있으면 도구로도 돌릴 수 있다 → [unity-mcp.md](unity-mcp.md)

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
- 그래서 batchmode EditMode 는 현재 **93건 중 88 통과 / 5 스킵 / 0 실패**가 정상이다
  (SceneNameSO 3건 + Player 정신력 배선 1건 + Ghost_Prototype 컨트롤러 배선 1건).
  에디터 Test Runner 에서는 93/93 통과한다.
- **씬 생성 도구를 `-executeMethod`로 batchmode 에서 돌리지 않는다.**
  `LoadOrCreateAsset`이 기존 설정 에셋을 못 찾아 새로 만들어 버린다.
  생성 도구는 MUST 에디터 메뉴에서 실행한다 → [../conventions/unity-assets.md §1.1](../conventions/unity-assets.md)
- `-quit -batchmode -nographics ... -logFile -` 로 하는 **컴파일 검증은 영향받지 않는다.**

## 6. 수동 검증 체크리스트

자동화하기 어려운 것은 사용자에게 아래 형식으로 요청한다.

```
확인 요청:
1. Bootstrap 씬에서 Play
2. Host로 시작 → 캐릭터가 WASD로 이동하는지
3. 빌드 실행 파일을 켜고 Client로 접속 → 양쪽에서 서로 보이는지
4. 클라이언트에서 이동 시 호스트 화면에서도 위치가 따라오는지
```

## 7. 현재 상태

> 2026-08-31 기준. Unity MCP로 열린 에디터의 Test Runner를 실행한 결과다.

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

최종 갱신: 2026-08-31
