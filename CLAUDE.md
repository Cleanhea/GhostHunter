# CLAUDE.md

GhostHunter — 1인칭 멀티플레이 "가구 던지기" 프로토타입.

> 상세 설계는 [docs/](docs/)에 있습니다. 이 파일은 매 세션 컨텍스트에 로드되므로 요약만 유지합니다.

## 기술 스택

| 항목 | 값 | 비고 |
|---|---|---|
| Unity | **6000.3.20f1** (Unity 6.3) | 버전 변경 시 팀 전체 합의 필요 |
| 렌더 파이프라인 | URP 17.3 | PC 프리셋(`Assets/Settings/PC_RPAsset.asset`) 사용 |
| 입력 | Input System 1.19 (**신규 전용**, `activeInputHandler: 1`) | 레거시 `Input.*`는 런타임 예외 |
| 네트워킹 | Netcode for GameObjects **2.13.1** | 설치됨 |
| Steam 트랜스포트 | `com.community.netcode.transport.facepunch` | **임베드 + 패치됨** — 아래 주의 |
| 로컬 트랜스포트 | `com.unity.transport` 2.7.3 (UTP) | NGO 의존성으로 자동 설치 |
| Steam 래퍼 | Facepunch.Steamworks | 트랜스포트 패키지에 DLL 번들 |
| 물리 | PhysX (built-in 3D) | DOTS Physics 사용 안 함 |
| 에셋 직렬화 | Force Text (`m_SerializationMode: 2`) | 유지할 것 |

## 절대 규칙

- **`Packages/com.community.netcode.transport.facepunch/`는 벤더링된 서드파티 사본이다.**
  패치 없이는 컴파일되지 않는 upstream 버그가 있어 임베드했다. 손대기 전에
  [PATCHES.md](Packages/com.community.netcode.transport.facepunch/PATCHES.md)를 읽고,
  수정하면 거기에 기록한다. 이 폴더에 우리 기능을 추가하지 않는다.
- **`.meta` 파일은 항상 짝으로 커밋**한다. 파일/폴더 이동·삭제는 Unity 에디터 안에서 하고, 셸에서 `mv`/`rm` 하지 않는다.
- **`.unity` / `.prefab` / `.asset` YAML을 손으로 편집하지 않는다.** 씬·프리팹 구성은 에디터에서 하거나, 필요하면 `Assets/Scripts/Editor/`에 에디터 스크립트를 만들어 처리한다.
- **Unity 빌드/플레이는 Claude가 직접 실행할 수 없다.** 코드 변경 후에는 "에디터에서 컴파일 확인 후 알려달라"고 요청하고, 컴파일 성공을 임의로 단정하지 않는다.
- **물리 상태의 권위는 서버(호스트)에만 있다.** 클라이언트는 입력/의도만 RPC로 보낸다. 클라이언트에서 `Rigidbody`에 직접 힘을 가하지 않는다.
- 레거시 `Input.GetKey` / `Input.GetAxis` 금지 — Input System만 사용한다.
- `GameObject.Find`, `SendMessage`, `Camera.main`(매 프레임) 금지. 참조는 인스펙터 직렬화 또는 명시적 주입으로 해결한다.

## 폴더 구조

```
Assets/
├─ Scenes/           Prototype (현재 플레이 가능한 통합 프로토타입), SampleScene(템플릿)
├─ Scripts/          asmdef: GhostHunter.Runtime
│  ├─ Core/          GameLayers 등 상수·공통
│  ├─ Networking/    SteamLobbyManager, ConnectionManager        ← 구현됨
│  ├─ Player/        이동, 시점, 플레이어 네트워크 표현            ← 구현됨
│  ├─ Interaction/   조준 타겟팅, 그랩 컨트롤러                   ← 구현됨
│  ├─ Furniture/     가구 오브젝트, 부양 모터, 발사, 아웃라인       ← 구현됨
│  ├─ UI/            크로스헤어, 차지 게이지                      ← 구현됨
│  ├─ DebugTools/    ConnectionHud, 런타임 스모크 테스트           ← 구현됨
│  └─ Editor/        NetworkRigSetup, PrototypeSceneSetup        ← 구현됨
├─ Prefabs/          Player, Furniture_Light_Cube, Furniture_Heavy_Cube
└─ Settings/         URP 에셋 (건드리지 말 것)
```

- 폴더명이 `Debug`가 아니라 **`DebugTools`**인 이유: `GhostHunter.Debug` 네임스페이스는
  `UnityEngine.Debug`를 가려 그 안의 모든 `Debug.Log` 호출을 깨뜨린다.
- `GhostHunter.Runtime` asmdef는 `includePlatforms`가 **Editor + WindowsStandalone64**로
  제한되어 있다. Steam DLL이 그 플랫폼에만 존재하기 때문이다. 다른 플랫폼을 타겟하면
  게임플레이 코드가 통째로 사라진 것처럼 보이므로, 그때는 Steam 코드를 별도 어셈블리로 분리한다.
- `Assets/Scripts/Temp.cs` 템플릿 잔재는 Prototype 생성 도구가 삭제했다.

## 멀티플레이 빠른 시작

1. `Assets/Scenes/Prototype.unity`을 연다. 네트워크 리그와 게임플레이 배선이 이미 들어 있다.
2. 플레이 → 접속 HUD에서 `Local` / **Host**를 누른다. **F1**로 HUD를 토글한다.
3. Steam 테스트는 세션 정지 중 HUD 모드를 `Steam`으로 바꾼다.

씬/프리팹을 다시 생성해야 하면 메뉴 **`GhostHunter > 프로토타입 게임 생성`**을 사용한다.

세부 사항은 [docs/03-multiplayer-setup.md](docs/03-multiplayer-setup.md).

## 코딩 컨벤션 (요약)

- 네임스페이스는 폴더와 1:1: `GhostHunter.Player`, `GhostHunter.Furniture`, …
- `private` 필드는 `_camelCase`, 인스펙터 노출은 `[SerializeField] private`로 (public 필드 금지).
- RPC 네이밍: `DoThingServerRpc` / `OnThingClientRpc`. 서버 RPC는 항상 파라미터 유효성을 검증한다.
- 튜닝 수치는 코드 상수가 아니라 `ScriptableObject` 설정 에셋(`FurnitureThrowSettings` 등)에 둔다.
- 물리 처리는 `FixedUpdate`, 입력 폴링/카메라는 `Update`/`LateUpdate`.

전체 규칙: [docs/07-conventions.md](docs/07-conventions.md)

## 자주 쓰는 명령

```powershell
# 컴파일 오류 확인 (에디터가 켜져 있을 때 로그 tail)
Get-Content "$env:LOCALAPPDATA\Unity\Editor\Editor.log" -Tail 80

# 스크립트 변경 후 C# 프로젝트 재생성은 Unity 에디터가 자동 처리 (.csproj는 gitignore 대상)
```

빌드/플레이 스크립트는 아직 없다. 필요해지면 `Assets/Scripts/Editor/BuildPipeline`에 추가한다.

## 문서 인덱스

| 문서 | 내용 |
|---|---|
| [docs/01-project-overview.md](docs/01-project-overview.md) | 프로토타입 목표, 범위, 검증하려는 것 |
| [docs/02-architecture.md](docs/02-architecture.md) | 씬/어셈블리/런타임 구조, 데이터 흐름 |
| [docs/03-multiplayer-setup.md](docs/03-multiplayer-setup.md) | NGO + Facepunch Steamworks 설치·설정 절차 |
| [docs/04-player-controller.md](docs/04-player-controller.md) | 1인칭 이동/시점, 입력 매핑, 네트워크 동기화 |
| [docs/05-throw-system.md](docs/05-throw-system.md) | 타겟팅 → 홀드 → 부양 → 발사, 2인 흡착 규칙 |
| [docs/06-furniture-physics.md](docs/06-furniture-physics.md) | 가구 오브젝트, 아웃라인, 물리 파라미터 |
| [docs/07-conventions.md](docs/07-conventions.md) | 코딩/네이밍/Git 컨벤션 |
| [docs/08-roadmap.md](docs/08-roadmap.md) | 작업 체크리스트와 마일스톤 |
