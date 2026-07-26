# 07. 컨벤션

## 네이밍

| 대상 | 규칙 | 예 |
|---|---|---|
| 네임스페이스 | `GhostHunter.<폴더>` | `GhostHunter.Furniture` |
| 클래스 / 메서드 / 프로퍼티 | PascalCase | `FurnitureHoverMotor` |
| private 필드 | `_camelCase` | `_holders`, `_rigidbody` |
| 지역 변수 / 파라미터 | camelCase | `aimDirection` |
| 상수 | PascalCase | `MaxHolders` |
| ScriptableObject 에셋 | `<Type>_<이름>` | `FurnitureDefinition_HeavyCube` |
| 프리팹 | PascalCase, 카테고리 접두 | `Furniture_Cube`, `UI_Crosshair` |
| 씬 | PascalCase | `Bootstrap`, `Prototype` |

`public` 필드는 쓰지 않는다. 인스펙터 노출은 `[SerializeField] private`, 외부 읽기는 프로퍼티로.

## 네트워크 코드 규칙

- RPC 이름은 접미사가 곧 방향이다: `RequestGrabServerRpc`, `OnLaunchedClientRpc`.
- **모든 ServerRpc는 파라미터를 검증한다.** 거리, 상태, 소유권, NaN. 클라이언트가 보낸 값을 그대로 물리에 넣지 않는다.
- `NetworkVariable` 쓰기 권한은 기본값(서버 전용)을 유지한다. 예외가 필요하면 주석으로 이유를 남긴다.
- 서버 전용 컴포넌트는 `OnNetworkSpawn`에서 `if (!IsServer) { enabled = false; return; }`로 스스로 꺼진다. 호출부에서 매번 `IsServer`를 검사하는 것보다 낫다.
- 로컬 전용(입력/카메라/UI)은 `if (!IsOwner) { enabled = false; return; }`.
- `OnNetworkSpawn`에서 구독한 이벤트는 `OnNetworkDespawn`에서 반드시 해제한다.
- 새 프리팹을 `Spawn()` 하려면 `NetworkManager`의 Network Prefabs List에 등록해야 한다. 등록을 빠뜨리면 **에러 없이 조용히 실패**한다 — 스폰이 안 될 때 여기부터 본다.

## Unity 사용 규칙

- 물리(`Rigidbody`, `AddForce`)는 `FixedUpdate`. 입력 폴링과 레이캐스트는 `Update`. 카메라 추종은 `LateUpdate`.
- `GetComponent`는 `Awake`에서 한 번만 캐싱한다.
- `Camera.main`은 `Awake`에서 캐싱하거나 인스펙터 참조로 대체. 매 프레임 호출 금지.
- `GameObject.Find`, `SendMessage`, `Invoke("메서드명")` 금지 — 문자열 참조는 리팩터링에서 조용히 깨진다.
- 레이어/태그 문자열을 코드에 반복하지 않는다. `Core/GameLayers.cs`, `Core/GameTags.cs`에 상수로.
- 튜닝 수치는 `ScriptableObject` 설정 에셋에. 코드 리터럴 금지 ([02-architecture.md](02-architecture.md) 참조).

## 주석

- **무엇을 하는지가 아니라 왜 그렇게 했는지**를 적는다. 코드를 읽으면 아는 걸 반복하지 않는다.
- 특히 다음은 주석을 남긴다: 물리 상수의 근거, 네트워크 권위 예외, 프로토타입이라서 타협한 지점.
- `TODO:` 는 담당자나 조건을 함께 적는다. `// TODO: 2인 방향 평균 정책 — 플레이테스트 후 결정 (docs/05 미결정 참조)`

## Git

### 커밋

- `.meta` 파일은 짝이 되는 에셋과 **같은 커밋**에 넣는다. 빠뜨리면 다른 사람 프로젝트에서 참조가 깨진다.
- 씬/프리팹은 병합이 어렵다. **같은 씬을 두 사람이 동시에 수정하지 않는다.** 작업 전에 말로 조율하는 게 merge 도구보다 싸다.
- 커밋 메시지는 한국어로 쓴다. 첫 줄에 무엇을 했는지, 필요하면 본문에 왜.

### 브랜치

프로토타입 규모에서는 `main` 직접 커밋도 허용한다. 다만 다음은 브랜치를 판다:
- 네트워크 패키지 설치처럼 프로젝트 전체가 흔들리는 변경
- 실패할 수 있는 실험

### 파일 이동/삭제

**반드시 Unity 에디터 안에서** 한다. 셸에서 `mv`/`rm` 하면 `.meta`가 남거나 GUID 참조가 끊긴다.

### LFS

`.gitattributes`가 이미 Unity 템플릿 기준으로 설정되어 있다. 바이너리 에셋(텍스처, 오디오, DLL)을 추가할 때 해당 확장자가 LFS 규칙에 포함되어 있는지 확인한다.

### 커밋하지 않는 것

`.gitignore`가 처리한다: `Library/`, `Temp/`, `Logs/`, `UserSettings/`, `*.csproj`, `*.sln`.

`ProjectSettings/`는 **커밋한다.** 물리 설정, 레이어, 태그, 입력 설정이 전부 여기 들어 있다.

## Claude와 작업할 때

- Claude는 Unity 에디터를 실행할 수 없다. 컴파일 성공 여부, 인스펙터 연결, 플레이 결과는 사람이 확인해서 알려줘야 한다.
- 씬/프리팹 구성은 Claude가 직접 못 한다. 코드를 만들고 "어떤 오브젝트에 무엇을 붙이면 되는지" 지시하는 방식으로 나눈다.
- 설계가 바뀌면 코드와 함께 `docs/`도 고친다. 문서가 낡으면 Claude가 낡은 전제로 작업한다.
