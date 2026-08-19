# ADR-0003: 서비스 접근을 `Core`의 서비스 로케이터로 한다

- **상태**: Accepted
- **날짜**: 2026-08-19
- **결정자**: MinGiHong
- **관련**: [ADR-0002](ADR-0002-scene-flow-so-event-channel.md) (대체 대상), [../overview.md](../overview.md), [../../conventions/code-style.md](../../conventions/code-style.md)

> **이관 메모 (2026-08-19).** 이 결정은 AlienGhost 프로젝트에서 검증된 뒤 GhostHunter로 이관했다.
> 논거와 트레이드오프는 그대로 유효하나, **GhostHunter 코드에는 아직 반영되지 않았다.**
> 진행 상황은 [../../project/roadmap.md](../../project/roadmap.md) 마이그레이션 보드를 본다.

## 배경 (Context)

레이어를 넘는 호출(예: Title 씬의 UI가 씬 전환을 요청)은 두 제약을 동시에 만족해야 한다.

1. **씬 간 참조 금지** — Title 씬의 버튼이 Bootstrap의 오브젝트를 인스펙터로 물 수 없다.
2. **`UI → Systems` 참조 금지** ([../overview.md §2](../overview.md)) — UI 어셈블리에서 `SceneFlowController` 타입을 쓸 수 없다.

[ADR-0002](ADR-0002-scene-flow-so-event-channel.md)에서 이를 SO 이벤트 채널로 풀었으나, 구현 직후 아래가 비용으로 드러났다.

- 이벤트 종류마다 구체 클래스와 에셋이 쌓인다. Unity는 열린 제네릭 타입으로 에셋을 만들지 못해
  제네릭 베이스를 둬도 클래스 자체는 없앨 수 없다.
- **구독자를 코드에서 추적할 수 없다.** Find Usages가 `Raise`까지만 데려가고, 그다음은 인스펙터를 뒤져야 한다.
- 배선 누락이 컴파일 에러가 아니라 "아무 일도 안 일어남"으로 나타난다.
- `.asset` 병합 충돌.

SO 채널의 핵심 이점은 **코드 수정 없는 인스펙터 재배선**이고 수혜자는 디자이너다.
이 프로젝트에는 디자이너 배선 수요가 없어 비용만 지불하는 구조였다.

## 검토한 선택지 (Options)

| 선택지 | 장점 | 단점 |
| --- | --- | --- |
| A. SO 이벤트 채널 유지 (ADR-0002) | 인스펙터에서 재배선 가능 | 위 비용 그대로 |
| B. **`Core` 인터페이스 + 서비스 로케이터** | 파일 증식 없음. Find Usages로 호출부·구현 전부 추적. 패키지 불필요 | 프로세스 전역. 의존성이 클래스 시그니처에 드러나지 않음 |
| C. DI 컨테이너 (VContainer 등) | 스코프별 생명주기, 명시적 의존, 상당수 오류를 시작 시점에 검출 | 패키지 추가 필요. **NGO 스폰 객체에는 자동 주입이 안 먹힌다** |

## 결정 (Decision)

**B를 채택한다.** `Core`에 서비스 인터페이스(`ISceneFlow`)와 정적 레지스트리(`Services`)를 둔다.
소비자는 `Services.Get<ISceneFlow>()`로 받는다.

**등록은 컴포지션 루트에서만 한다.** 공통 기반은 `SceneInstaller`(추상)이고, 씬마다 상속해 쓴다.
`Bind<T>()`가 등록과 해제를 같은 자리에서 짝지어, 씬이 언로드될 때(`OnDestroy`) 자동으로 풀린다.

- **전역 스코프** = `BootstrapInstaller`. Bootstrap은 언로드되지 않으므로([ADR-0004](ADR-0004-multi-scene-additive.md))
  여기서 등록한 서비스는 앱 수명 전체를 산다.
- **씬 스코프** = 각 씬의 `SceneInstaller` 파생 컴포넌트. 씬이 내려가면 함께 해제된다.

전역과 씬 스코프가 같은 메커니즘을 쓴다. 전역은 "가장 바깥 씬의 스코프"일 뿐 특별한 장치가 아니다.
서비스 구현체(`SceneFlowController`)는 자기 등록도, 자기 `static` 인스턴스도 갖지 않는다.

**함께 거는 규약**: 의존성은 MUST `Awake`에서 한 번 받아 필드에 캐싱한다.
메서드 안쪽에서 `Services.Get<T>()`를 그때그때 호출하지 않는다 → [../../conventions/code-style.md §7.1](../../conventions/code-style.md).
`Awake` 획득이 성립하는 근거가 위 실행 순서이므로, 그 순서를 바꾸면 규약도 함께 재검토한다.

## 근거 (Rationale)

**1. NGO와 DI 컨테이너의 궁합.** `NetworkObject`는 컨테이너가 아니라 NGO의 스폰 시스템이 생성한다.
컨테이너가 그 생성을 가로채지 못해 `OnNetworkSpawn`에서 수동으로 주입하게 되고, **그 모양은 로케이터와 같다.**
게임플레이 레이어 상당 부분이 이 경로를 타는 프로젝트에서 C가 실제로 커버하는 범위는 생각보다 좁다.

**2. 되돌리기 비용의 비대칭.** 만들 게임이 아직 정해지지 않았다([../../project/overview.md §4](../../project/overview.md) In Scope = TBD).
규모를 예측할 수 없을 때는 되돌리기 싼 쪽을 고른다. B→C 이관은 소비자 클래스마다 **획득 한 줄**을 바꾸는
기계적 작업이지만, C를 걷어내는 일은 모든 레이어에 걸린다.

> 초기 단계에서 "지금 서비스가 몇 개다"를 근거로 삼는 것은 순환논법이다. 초기니까 적은 것이고,
> 초기니까 도입 비용도 가장 싸다. 판단 근거는 개수가 아니라 위 비대칭성이다.

**3.** 위 규약(`Awake` 캐싱)을 지키면 B의 최대 약점인 "획득 지점이 코드 전역에 흩어져 이관이 불가능해지는 것"이
통제된다. 규약이 곧 탈출구를 유지하는 조건이다.

## 결과 (Consequences)

### 긍정
- 채널 클래스·에셋 증식이 사라졌다. `EventChannel`, `SceneRequest`, `SceneRequestKind`, `SceneRequestChannel` 삭제.
- 호출부와 구현이 Find Usages로 전부 추적된다.
- 인터페이스 기반이라 테스트에서 가짜 구현을 등록할 수 있다.
- 레이어 방향 유지: `UI → Core ← Systems`.
- **등록 목록이 인스톨러 한 파일에 모인다.** 서비스가 늘어도 `static`은 늘지 않고,
  "이 씬이 무엇을 제공하는가"를 한 곳에서 읽을 수 있다.
- 컴포지션 루트가 있어 VContainer 이관 시 대응 지점이 한 군데(`LifetimeScope` 설정)로 좁혀진다.
  `SceneInstaller` → `LifetimeScope`가 거의 1:1로 대응된다.

### 부정 / 감수하는 비용
- 여전히 프로세스 전역이다. **한 프로세스에 `NetworkManager`를 2개 이상 띄우는 in-process 다중 클라이언트
  테스트는 비대상이다** (ADR-0002와 동일). 멀티 검증은 별도 프로세스로 한다.
- **의존성이 클래스 시그니처에 드러나지 않는다.** `Awake` 캐싱 규약은 완화책이지 해결책이 아니다.
- 미등록 조회는 컴파일이 아니라 런타임 예외로 드러난다.
- 정적 레지스트리는 Enter Play Mode Options로 도메인 리로드를 끄면 플레이 종료 후에도 살아남는다.
  `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]`으로 플레이 시작마다 비운다.

### 후속 작업
- [ ] 코드 반영: `Scripts/Core/Services.cs`, `Scripts/Core/Scenes/ISceneFlow.cs`,
      `Scripts/Systems/BootstrapInstaller.cs`, `SceneFlowController` 수정
- [ ] 등록 지점을 컴포지션 루트(`BootstrapInstaller`)로 정리
- [ ] ADR-0002를 Rejected로 기록
- [ ] 영향받는 문서 갱신: [../overview.md](../overview.md), [README.md](README.md), [../../conventions/code-style.md](../../conventions/code-style.md)
- [ ] Bootstrap 씬에 `BootstrapInstaller` 컴포넌트 부착 및 인스펙터 배선 (M0-5)
- [ ] asmdef 도입 시 `Core`가 아무것도 참조하지 않는지 확인

## 재검토 조건

VContainer 등 DI 컨테이너로 전환할 트리거.

- 등록 서비스가 **6개를 넘을 때**
- 매치·세션마다 **다른 인스턴스가 필요한 스코프**가 실제로 생길 때
- 테스트에서 가짜 구현 주입이 잦아져 수동 등록/해제가 부담이 될 때
