# ADR-0004: 멀티씬 아키텍처 — Bootstrap을 언로드하지 않고 additive로 얹는다

- **상태**: Accepted
- **날짜**: 2026-08-19
- **결정자**: MinGiHong
- **관련**: [ADR-0001](ADR-0001-steam-p2p-facepunch-transport.md), [ADR-0003](ADR-0003-service-locator.md), [../overview.md §4](../overview.md)

> **이관 메모 (2026-08-19).** 이 결정은 AlienGhost 프로젝트에서 검증된 뒤 GhostHunter로 이관했다.
> 논거와 트레이드오프는 그대로 유효하나, **GhostHunter 코드에는 아직 반영되지 않았다.**
> 진행 상황은 [../../project/roadmap.md](../../project/roadmap.md) 마이그레이션 보드를 본다.

## 배경 (Context)

씬 전환 방식으로 두 갈래가 있다.

- **Single 교체**: `LoadSceneMode.Single`로 씬을 갈아끼운다. Unity가 이전 씬을 자동으로 내린다.
  영속 객체는 `DontDestroyOnLoad`로 별도 유사 씬에 옮겨 살려야 한다.
- **멀티씬(additive)**: `Bootstrap`을 로드된 채로 두고 나머지 씬을 그 위에 얹었다 내린다.
  영속 객체는 그냥 `Bootstrap`에 있으면 된다.

이 프로젝트는 NGO 기반 멀티플레이(호스트 = 리슨 서버)이고, `NetworkManager`가 `Bootstrap`에 있다.
또 [ADR-0003](ADR-0003-service-locator.md)에서 씬 단위 서비스를 `SceneInstaller`가 씬 수명에 맞춰 등록/해제하기로 했다.

> 문서화 경위: [../overview.md §4](../overview.md)의 씬 표가 `Title`·`Lobby`를 `Single`로 적고 있었고
> 초기 구현이 그에 맞춰 작성됐다. 실제 의도는 멀티씬이었으므로 이 ADR로 확정하고 표를 정정한다.

## 검토한 선택지 (Options)

| 선택지 | 장점 | 단점 |
| --- | --- | --- |
| A. Single 교체 | 이전 씬 정리를 Unity가 해준다. 코드가 짧다 | `DontDestroyOnLoad` 특수 상태 필요. 영속 객체 중복 판정·`SetParent(null)` 같은 우회가 따라붙는다 |
| B. **멀티씬 additive** | `Bootstrap`이 안 내려가 영속화 장치가 통째로 불필요. 스코프가 씬 경계와 일치 | 이전 씬을 직접 내려야 한다. 활성 씬 지정 필요. 카메라·`AudioListener` 중복 주의 |

## 결정 (Decision)

**B를 채택한다.** `Bootstrap`은 빌드 인덱스 0으로 로드된 뒤 **앱 종료까지 언로드하지 않는다.**
`Title`·`Lobby`·`Game`·`Result`는 `LoadSceneMode.Additive`로 올리고, `SceneFlowController`가
현재 올라와 있는 씬을 추적해 새 씬을 올린 뒤 이전 씬을 내린다.

전환 순서는 MUST **① 새 씬 additive 로드 → ② `SetActiveScene` → ③ 이전 씬 언로드** 다.

언로드 경로는 씬을 올린 주체에 따라 갈린다.

- NGO가 올린 씬 → `NetworkManager.SceneManager.UnloadScene`. 서버만 시작할 수 있고 클라이언트는 동기화로 따라온다.
- 로컬로 올린 씬 → 각 피어가 `SceneManager.UnloadSceneAsync`로 직접 내린다. NGO는 이 씬을 추적하지 않는다.

## 근거 (Rationale)

**1. 영속화 장치가 통째로 사라진다.** Single 방식에서는 `DontDestroyOnLoad`가 필요하고, 그게 root 오브젝트에만
적용되는 제약 때문에 `transform.SetParent(null)` 같은 우회가 붙었다. 또 Bootstrap 재진입 시 중복 인스턴스를
걸러내는 코드도 필요했다. **멀티씬에서는 이 세 가지가 전부 불필요하다** — 씬이 안 내려가니 그 안의 것도 안 내려간다.

**2. 스코프가 씬 경계와 일치한다.** [ADR-0003](ADR-0003-service-locator.md)의 `SceneInstaller`가
"씬이 살아 있는 동안 서비스가 산다"를 그대로 표현한다. 전역 스코프는 특별한 무언가가 아니라
**"가장 바깥 씬(Bootstrap)의 스코프"** 가 되고, `BootstrapInstaller`도 다른 인스톨러와 같은 클래스를 상속한다.
메커니즘이 하나로 통일된다.

**3. NGO에 유리하다.** `NetworkManager`가 절대 언로드되지 않는 씬에 있는 편이 안전하다.
로딩 화면을 `Bootstrap`에 두고 전환 내내 유지하는 것도 자연스러워진다.

## 결과 (Consequences)

### 긍정
- `DontDestroyOnLoad`, `SetParent(null)`, 영속 객체 중복 판정 코드가 전부 제거됐다.
- 오브젝트가 어느 씬에 속하는지가 런타임 내내 명확하다. `DontDestroyOnLoad` 유사 씬이 생기지 않는다.
- `GameBootstrap`이 사라지고 `BootstrapInstaller : SceneInstaller`로 흡수됐다.

### 부정 / 감수하는 비용
- `SceneFlowController`가 **현재 씬과 그 로드 주체(로컬/NGO)를 상태로 들고 있어야 한다.** Single 방식에는 없던 상태다.
- `SetActiveScene` 호출을 빠뜨리면 라이팅·스카이박스가 `Bootstrap` 기준이 되고 런타임 생성 오브젝트가 `Bootstrap`에 쌓인다.
  증상이 "버그"처럼 보이지 않아 원인 찾기가 어렵다.
- **`Bootstrap`의 Main Camera / `AudioListener`가 게임플레이 씬과 중복된다.** 부팅 후 비활성화하거나 아예 두지 않아야 한다.
- 로드·언로드가 겹치는 순간 두 씬이 동시에 메모리에 존재한다. 씬이 무거워지면 피크 메모리를 봐야 한다.
- 언로드를 기다리지 않고 `IsLoading`을 해제한다. 전환 직후 다시 전환하면 이전 언로드가 진행 중일 수 있다.

### 후속 작업
- [ ] `SceneFlowController` additive 전환 (로컬·NGO 양 경로, `SetActiveScene`, 이전 씬 언로드)
- [ ] `GameBootstrap` → `BootstrapInstaller : SceneInstaller`
- [ ] [../overview.md §4](../overview.md) 씬 표·규칙 정정
- [ ] `Bootstrap`의 카메라 정리 — 확인 결과 애초에 카메라·라이트가 없었다. "두지 않는다"를 규칙으로 명문화
- [ ] 플레이 검증: `Bootstrap` → `Title` → `Lobby` additive 전환, 이전 씬 언로드 확인 (M0-5)
- [ ] 실제 2인 세션에서 `Lobby`(로컬 로드) → `Game`(NGO 로드) 전환 시 언로드 경로 검증 (M0-9 이후)

## 재검토 조건

- 씬이 무거워져 로드·언로드 겹침 구간의 피크 메모리가 문제가 될 때
- NGO 클라이언트 접속 동기화에서 additive 씬 처리가 병목이나 버그로 드러날 때
