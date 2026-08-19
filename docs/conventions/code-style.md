# C# / Unity 코딩 규약

> 코드를 쓰기 전 MUST 읽는다. **기존 파일의 스타일이 이 문서와 다르면 기존 파일을 따르고, 불일치를 보고**한다.

## 1. 네이밍

| 대상 | 규칙 | 예 |
| --- | --- | --- |
| 클래스 / 구조체 / 열거형 | PascalCase | `FurnitureHoverMotor` |
| 인터페이스 | `I` + PascalCase | `ISteamLobbyService` |
| public 멤버 / 메서드 | PascalCase | `RequestGrab()` |
| private 필드 | `_` + camelCase | `_holders`, `_rigidbody` |
| `[SerializeField]` private 필드 | `_` + camelCase | `_moveSpeed` |
| 지역 변수 / 파라미터 | camelCase | `aimDirection` |
| 상수 / `static readonly` | PascalCase | `MaxHolders` |
| 네임스페이스 | `GhostHunter.<Layer>.<Feature>` | `GhostHunter.Gameplay.Furniture` |
| RPC 메서드 | `…Rpc` 접미사 필수 | `RequestGrabRpc` |
| 비동기 메서드 | `…Async` 접미사 | `CreateLobbyAsync` |
| 이벤트 | 과거형 명사, 또는 `On` + 과거형 | `LobbyUpdated`, `OnHealthChanged` |

- 축약어 금지(`mgr`, `ctrl`, `pos` 대신 `manager`, `controller`, `position`). 단 `id`, `ui`, `fx`, `rpc`는 허용.
- 부울은 상태를 묻는 형태로: `IsDead`, `HasKey`, `CanInteract`.
- **폴더명 `Debug`를 쓰지 않는다.** `GhostHunter.Debug` 네임스페이스는 `UnityEngine.Debug`를 가려
  그 안의 모든 `Debug.Log` 호출을 깨뜨린다. 현재 이름은 `DebugTools`다.

## 2. 파일 · 구조

- **파일 1개 = public 타입 1개**, 파일명 = 타입명.
- 네임스페이스는 MUST 폴더 구조와 일치시킨다 → [../architecture/overview.md](../architecture/overview.md)
- 멤버 순서: 상수 → static 필드 → serialize 필드 → private 필드 → 프로퍼티 → 이벤트 → Unity 생명주기 → public 메서드 → private 메서드.
- Unity 생명주기 순서: `Awake` → `OnEnable` → `Start` → `Update` → `FixedUpdate` → `LateUpdate` → `OnDisable` → `OnDestroy`.
  `NetworkBehaviour`는 `OnNetworkSpawn` / `OnNetworkDespawn`을 `Awake` 다음에 둔다.

## 3. 필드 노출

```csharp
[SerializeField] private float _moveSpeed = 5f;   // 인스펙터 노출
public float MoveSpeed => _moveSpeed;             // 읽기 전용 공개
```

- `public` 필드를 인스펙터 노출 용도로 쓰지 않는다. MUST `[SerializeField] private`.
- 밸런스 수치는 하드코딩하지 않고 ScriptableObject로 뺀다 → [unity-assets.md](unity-assets.md).
- `[SerializeField]`에는 기본값을 넣어 프리팹 없이도 의미가 읽히게 한다.

## 4. 성능 규칙 (핫패스)

**MUST NOT — `Update`/`FixedUpdate`/`LateUpdate` 안에서:**
- `GetComponent`, `FindObjectOfType`, `GameObject.Find`, `Camera.main`
- `new` 할당(배열, 리스트, 람다 클로저, 문자열 결합)
- LINQ
- `string` 보간으로 매 프레임 로그

**대신:**
```csharp
private Transform _cachedTransform;
private void Awake() => _cachedTransform = transform;
```

- 컴포넌트 참조는 `Awake`에서 캐싱하거나 `[SerializeField]`로 직접 물린다.
- `GameObject.Find`, `SendMessage`, `Invoke("메서드명")` MUST NOT — 문자열 참조는 리팩터링에서 조용히 깨진다.
- 레이어/태그 문자열을 코드에 반복하지 않는다. `Core/GameLayers.cs`, `Core/GameTags.cs`에 상수로.
- 반복 호출되는 컬렉션은 재사용한다(`Clear()` 후 재사용).
- 매 프레임 필요 없는 로직은 UniTask(`UniTask.Delay`·`WaitUntil`)·이벤트로 옮긴다 → §8.

### 4.1 실행 시점 배분

| 대상 | 시점 | 이유 |
| --- | --- | --- |
| `Rigidbody` / `AddForce` / 스프링 부양 | `FixedUpdate` | 물리 스텝에 묶여야 안정적 |
| 입력 폴링, 레이캐스트, 타겟팅 | `Update` | 프레임 반응성 |
| 카메라 추종 | `LateUpdate` | 이동이 끝난 뒤 따라가야 떨림이 없다 |
| **`CharacterController.Move` (`PlayerMotor`)** | **`Update`** | **예외.** 스윕 이동이라 물리 스텝에 묶을 이유가 없고, 50Hz로 움직이면 자식 카메라가 끊겨 보인다 → [../architecture/player-controller.md](../architecture/player-controller.md) |

## 5. 입력

- MUST **Input System**만 사용한다. `Input.GetKey`, `Input.GetAxis`, `Input.mousePosition` 등 레거시 API 금지
  (`activeInputHandler: 1` 이라 레거시 호출은 런타임 예외가 된다).
- Input Actions 에셋은 `Assets/InputSystem_Actions.inputactions` 하나를 쓴다.
- 액션 구독은 `OnEnable`, 해제는 MUST `OnDisable`에서 한다.
- 로컬 소유자가 아닌 플레이어 오브젝트에서는 입력 컴포넌트를 MUST 비활성화한다.

## 6. Null · 방어

- Unity 오브젝트에 `?.` / `??` 사용 금지(가짜 null 문제). `if (obj == null)`을 쓴다.
- 필수 참조가 비었으면 `Awake`에서 즉시 에러 로그를 남기고 컴포넌트를 비활성화한다.

```csharp
private void Awake()
{
    if (_settings == null)
    {
        Debug.LogError($"{nameof(PlayerMotor)}: _settings 미할당", this);
        enabled = false;
    }
}
```

- `Debug.Log` 계열에는 MUST 컨텍스트 객체(`this`)를 두 번째 인자로 넘긴다 — 콘솔에서 클릭 추적 가능.

## 7. 이벤트 · 결합

- C# `event Action<T>` 또는 `UnityEvent`(인스펙터 배선용)를 상황에 맞게 쓴다.
- 구독은 `OnEnable`, 해제는 MUST `OnDisable`. **해제 없는 구독은 리뷰 Blocker.**
- `OnNetworkSpawn`에서 구독한 것은 MUST `OnNetworkDespawn`에서 해제한다.
- 정적 이벤트는 씬 전환 시 누수 위험이 크다. 쓰려면 해제 책임을 명시한다.

### 7.1 의존성 획득

레이어나 씬을 넘는 협력은 `Core`의 서비스 로케이터를 경유한다 → [ADR-0003](../architecture/decisions/ADR-0003-service-locator.md).

- MUST **인터페이스로 받는다.** 구체 타입(`SceneFlowController`, `SteamLobbyManager`)을 다른 레이어에서 직접 참조하지 않는다.
- MUST **`Awake`에서 한 번 받아 필드에 캐싱한다.** 메서드 안쪽에서 `Services.Get<T>()`를 그때그때 부르지 않는다.
- **서비스는 스스로 등록하지 않는다.** 등록은 MUST `SceneInstaller` 파생 컴포넌트에서 `Bind<T>()`로만 한다.
  전역 서비스는 `BootstrapInstaller`, 씬 서비스는 그 씬의 인스톨러가 맡는다.
  **서비스마다 자기 `static Instance`를 들고 중복을 걸러내는 코드를 두지 않는다.**
- **같은 씬 안의 협력 컴포넌트는 로케이터를 쓰지 않는다.** `[SerializeField]`로 직접 문다.
  로케이터는 인스펙터로 이을 수 없을 때(레이어·씬을 넘을 때)만 쓴다.
- **전역 서비스의 이벤트를 구독하면 MUST `OnDisable`에서 해제한다.** 서비스는 `Bootstrap`에 살아
  씬보다 오래 남으므로(→ [ADR-0004](../architecture/decisions/ADR-0004-multi-scene-additive.md)),
  해제를 빠뜨리면 이미 파괴된 씬 오브젝트를 전역 서비스가 계속 붙들고 있게 된다.

```csharp
// 좋음 — Awake에서 한 번, 필드로
private ISceneFlow _sceneFlow;
private void Awake() => _sceneFlow = Services.Get<ISceneFlow>();
public void OnStartButton() => _sceneFlow.Load(SceneId.Lobby);

// 나쁨 — 호출 시점마다 조회
public void OnStartButton() => Services.Get<ISceneFlow>().Load(SceneId.Lobby);
```

`Awake` 획득이 안전한 이유는 인스톨러가 `[DefaultExecutionOrder(SceneInstaller.ExecutionOrder)]`로
같은 씬의 다른 `Awake`보다 먼저 등록을 끝내기 때문이다. **이 실행 순서를 없애면 위 규칙이 깨진다.**

- `SceneInstaller`를 상속할 때 `Awake`/`OnDestroy`를 재정의하면 MUST `base`를 호출한다.
- **파생 인스톨러에도 MUST `[DefaultExecutionOrder(SceneInstaller.ExecutionOrder)]`를 직접 단다.**
  Unity의 실행 순서 속성은 상속이 보장되지 않는다. 빠뜨리면 조용히 기본 순서로 돌아
  다른 컴포넌트의 `Awake`에서 `Services.Get`이 실패한다.

**예외 — `NetworkBehaviour`.** NGO가 스폰한 객체는 인스톨러와 생성 시점이 어긋날 수 있다.
스폰 객체는 MUST `OnNetworkSpawn`에서 `Services.Get<T>()`를 받는다.

로컬 소유 플레이어 컴포넌트는 `static LocalInstance`를 만들지 않는다. Game 씬의
`ILocalPlayerContext`에 `OnNetworkSpawn`에서 등록하고 `OnNetworkDespawn`에서 해제한다.
UI는 `Awake`에서 컨텍스트를 한 번 받아 현재 로컬 플레이어 참조를 읽는다.

## 8. 비동기

**UniTask를 기본으로 쓴다** (`com.cysharp.unitask` 2.5.11 → [ADR-0005](../architecture/decisions/ADR-0005-unitask-async.md)).
코루틴과 순수 `async Task`는 새로 쓰지 않는다.

- 값을 돌려주는 비동기는 `UniTask<T>`, 돌려줄 값이 없으면 `UniTask`.
- fire-and-forget은 MUST `async UniTaskVoid` + `.Forget()`. **`async void`는 MUST 금지**(예외 유실).
- 대기에는 MUST 취소 토큰을 넘긴다. MonoBehaviour에서는 `destroyCancellationToken`이 기본이다.
- Facepunch API는 `Task`를 돌려준다. MUST `.AsUniTask()`로 감싸 받는다.

```csharp
private void Load() => LoadAsync().Forget();

private async UniTaskVoid LoadAsync()
{
    await SceneManager.LoadSceneAsync(name, LoadSceneMode.Additive)
        .ToUniTask(cancellationToken: destroyCancellationToken);
    // 오브젝트가 파괴되면 여기 도달하지 않는다
}
```

**왜 코루틴이 아닌가** — 반환값을 돌려줄 수 없고, `yield`가 들어간 블록은 `try/catch`로 감쌀 수 없으며,
취소가 "파괴되면 조용히 멈춤"이라 코드에 드러나지 않는다. `MonoBehaviour`에서만 돌아가는 것도 제약이다.

**왜 순수 `async Task`가 아닌가** — 컨티뉴에이션이 Unity 메인 스레드로 돌아온다는 보장이 없고,
오브젝트가 파괴돼도 자동으로 끊기지 않으며, `Task` 할당이 매번 발생한다. UniTask가 이 셋을 해결한다.

## 9. 주석

- **무엇을**이 아니라 **왜**를 적는다. 코드를 반복 서술하지 않는다.
- 특히 다음은 주석을 남긴다: 물리 상수의 근거, 네트워크 권위 예외, 성능을 위해 규약을 어긴 지점.
- Unity 템플릿 기본 주석(`// Start is called once before...`)은 MUST 제거한다.
- public API에는 XML 문서 주석을 붙인다(요약 한 줄이면 충분).
- 주석 언어는 한국어. 식별자는 영문.
- `TODO:`는 담당자나 조건을 함께 적는다.
  `// TODO: 2인 방향 평균 정책 — 플레이테스트 후 결정 (throw-system.md 미결정 참조)`

### 9.1 클래스 summary

클래스 정의 상단의 `<summary>`는 MUST **그 코드를 처음 읽는 사람** 기준으로 쓴다.
그 클래스가 무엇을 하는지만 간결하게 설명한다.

**MUST NOT — summary에 넣지 않는 것**

| 넣지 않는 것 | 대신 어디에 |
| --- | --- |
| 작성자의 소견·권고 (~하는 게 낫다, 잠정적으로 ~로 두었다) | ADR 또는 PR 설명 |
| 이 클래스를 만들 때 받은 요청·지시, 작업 경위, 검토한 대안 | ADR 또는 커밋 메시지 |
| 다른 코드를 향한 지시 (다른 코드는 ~하지 않는다) | 이 문서(규약) |
| 구현 세부 서술 | 해당 줄 옆 `//` 주석 |

설계 배경과 트레이드오프는 summary가 아니라 [ADR](../architecture/decisions/README.md)에 남긴다.

## 10. 금지 목록 요약

| 금지 | 대안 |
| --- | --- |
| `public` 필드 인스펙터 노출 | `[SerializeField] private` + 읽기 전용 프로퍼티 |
| 레거시 `Input.*` | Input System (§5) |
| `Update`의 `GetComponent`/`Find`/`Camera.main` | `Awake` 캐싱 (§4) |
| `GameObject.Find` / `SendMessage` / `Invoke("이름")` | 직렬화 참조 또는 명시적 주입 |
| `async void` | `async UniTaskVoid` + `.Forget()` (§8) |
| 새 코루틴(`IEnumerator` + `StartCoroutine`) | `UniTask` (§8) |
| 취소 토큰 없는 `await` | `destroyCancellationToken` 전달 (§8) |
| Unity 오브젝트에 `?.` / `??` | `== null` 비교 (§6) |
| 매직 넘버 | 상수 또는 ScriptableObject |
| 구독 해제 없는 이벤트 | `OnDisable` / `OnNetworkDespawn`에서 해제 (§7) |
| `#region` 남용 | 클래스 분리 |
| summary에 소견·작업 경위·타 코드 지시 | 클래스 기능만 서술 (§9.1) |
| 메서드 안에서 `Services.Get<T>()` | `Awake`에서 받아 필드에 캐싱 (§7.1) |
| 같은 씬 컴포넌트를 로케이터로 조회 | `[SerializeField]`로 직접 배선 (§7.1) |
| 서비스가 자기 `static Instance`로 중복 관리 | `BootstrapInstaller`에서 일괄 등록 (§7.1) |
| 레거시 `[ServerRpc]` / `[ClientRpc]` | `[Rpc(SendTo.…)]` → [../architecture/networking.md](../architecture/networking.md) |
| `UI`/`Gameplay`에서 `Steamworks` 참조 | `ISteamLobbyService` 경유 → [../architecture/steam.md](../architecture/steam.md) |
| `SceneManager` 직접 호출 | `ISceneFlow` 경유 → [ADR-0004](../architecture/decisions/ADR-0004-multi-scene-additive.md) |

---

관련: [unity-assets.md](unity-assets.md) · [git.md](git.md) · [../architecture/networking.md](../architecture/networking.md)

최종 갱신: 2026-08-20
