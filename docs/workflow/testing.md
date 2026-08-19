# 테스트 전략

> Unity Test Framework 사용. 테스트 어셈블리는 아직 **미구성**이다 → roadmap MIG-7.

## 1. 무엇을 테스트하는가

| 대상 | 테스트 종류 | 우선순위 |
| --- | --- | --- |
| 순수 로직(계산, 상태 전이, 규칙 판정) | EditMode | 높음 |
| ScriptableObject 데이터 유효성 | EditMode | 중간 |
| MonoBehaviour 상호작용, 비동기(UniTask) | PlayMode | 중간 |
| 네트워크 동기화·RPC 검증 | PlayMode | 높음 |
| 렌더링·아트·연출 | 테스트 안 함 (수동 확인) | — |

**원칙**: 버그가 나면 비싼 것부터 테스트한다. 커버리지 숫자를 목표로 삼지 않는다.

## 2. 폴더 구성 (MIG-7에서 생성)

```
Assets/Tests/
├── EditMode/
│   └── GhostHunter.Tests.EditMode.asmdef
└── PlayMode/
    └── GhostHunter.Tests.PlayMode.asmdef
```

- 테스트 asmdef는 `UnityEngine.TestRunner`, `UnityEditor.TestRunner`(EditMode)를 참조하고
  `nunit.framework.dll`을 Override References에 추가한다.
- 테스트 대상 런타임 asmdef를 참조에 추가한다 → [../architecture/overview.md §3](../architecture/overview.md)

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
- PlayMode 테스트는 `[UnityTest]` + `IEnumerator`, 생성한 `GameObject`는 MUST `TearDown`에서 정리한다.

## 4. 네트워크 테스트

- `NetworkManager`를 코드로 구성해 Host/Client를 띄우고 검증한다.
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

## 5. 실행

### 5.1 Unity MCP 경유 (에디터가 켜져 있을 때 권장)

에디터가 실행 중이면 MCP 도구로 테스트를 돌린다. 에디터를 끄지 않아도 되고 결과를 바로 받는다.
→ [unity-mcp.md](unity-mcp.md)

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
- 결과 XML의 `<test-run result="Passed" ...>` 및 실패 케이스를 확인해 보고한다.
- 에디터 GUI에서는 `Window > General > Test Runner`.

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

| 항목 | 상태 |
| --- | --- |
| 테스트 어셈블리 | ❌ 미구성 → roadmap MIG-7 |
| EditMode 테스트 | 0건 |
| PlayMode 테스트 | 0건 |
| **런타임 스모크 테스트** | ⚠️ `Assets/Scripts/DebugTools/PrototypeRuntimeSmoke.cs` |
| CI | ❌ 없음 |

### 7.1 런타임 스모크 테스트의 위치

`PrototypeRuntimeSmoke`는 정식 테스트가 아니라 **플레이 중 자동으로 도는 점검 스크립트**다.
Local Host 시작, 1인 투척 준비/발사, 2슬롯 배정, 첫 홀더 해제 시 복귀, 마지막 해제 시 발사를 확인한다.

- 테스트 러너가 아니라 씬에서 돌기 때문에 **결과가 XML로 남지 않고 CI에서 판정할 수 없다.**
- 코루틴 기반이라 [../conventions/code-style.md §8](../conventions/code-style.md)의 유일한 예외다.
  MIG-4에서 나머지 코루틴을 전부 걷어낼 때 **의도적으로 남겼다** — 곧 대체될 코드라 전환 비용이 회수되지 않고,
  현재 유일한 자동 검증 수단이라 손대는 위험이 이득보다 크다.
- **MIG-7에서 PlayMode 테스트로 이관한다.** 그때까지는 유일한 자동 검증 수단이므로 지우지 않는다.

### 7.2 UTP 의존성 주의

현재 스모크 테스트와 일상 검증이 `TransportMode.Local`(UnityTransport)에 의존한다.
이 경로의 존치 여부는 [../architecture/decisions/ADR-0011](../architecture/decisions/ADR-0011-local-transport-path.md)에서
결정 대기 중이며, **테스트 어셈블리 구축이 그 결정의 선행 조건**이다.

---

관련: [development-loop.md](development-loop.md)

최종 갱신: 2026-08-20
