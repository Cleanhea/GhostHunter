# 정신력 시스템 구현

> 상태: **코어 P1 + World Space 정신력 모니터 구현 및 Local Host 검증 완료 (2026-08-24).
> 귀신 프로토타입의 모의 정신력을 제거하고 팀 평균 표시로 통합 (2026-08-24).
> 집 서쪽 테스트베드와 카메라 노이즈 연출 추가 (2026-08-24) — **단, Volume 오버라이드 미저장
> 버그로 실제 화면 효과는 2026-08-31에야 확인됨(§8 정정 참고).**  
> 게임 규칙의 권위는 [정신력 시스템 기획서](../project/sanity-system.md)이며, 이 문서는 코드 구조와
> 현재 연결 범위를 설명한다.

## 1. 구현 범위

```text
외부 서버 판정
  ├─ 최종 어둠 노출 여부
  ├─ 귀신 이벤트 목격
  ├─ 시체 NetworkObjectId 목격
  ├─ 정신력 아이템 회복량
  └─ 플레이어 사망
          ↓
Player/SanityNetworkState (Server 권위)
  ├─ 개인 정신력 0~100
  ├─ 어둠 노출 누적
  ├─ 동일 시체 중복 방지
  └─ NetworkVariable 복제
          ↓ Register / Unregister
Game/SanitySystem/SanityTeamService
  ├─ 생존 플레이어 산술 평균
  ├─ 일반 반올림 판정값
  ├─ 로컬 소유 플레이어 상태 조회
  └─ F1 개발 HUD 검증
          ↓ ISanityTeamService
  ├─ Game/SanityWorldMonitor/SanityHudUI (각 Client 동일 집계 표시)
  │    └─ P1~P4 개인 슬롯 + 팀 평균 심볼·정수 퍼센트 World Space 표시
  └─ Game/GhostPrototypeSystem/GhostPrototypeSpawner + Ghost_Prototype/GhostPrototypeController
       ├─ TryGetTeamAverage → 팀 평균으로 귀신 상태 전이·어택 판정 (귀신은 자체 정신력 없음)
       ├─ CopyPlayerStates  → 어택 중 탐지·추격 대상의 위치를 읽는다
       └─ 잡힘 시 SanityNetworkState.ServerMarkDead() 호출 → [ghost-prototype.md](ghost-prototype.md)
```

포함:

- 스테이지 시작 개인 정신력 100%
- 서버 권위 개인 정신력·생존·최종 어둠 노출 상태 복제
- 어둠 누적 10초마다 1 감소, 비노출 중 누적 시간 일시 정지
- 귀신 이벤트 목격 15 감소
- 시체 최초 목격 20 감소와 `NetworkObjectId`별 중복 방지
- 양수 아이템 회복 API와 0~100 Clamp
- 생존·접속 중인 플레이어 팀 평균과 일반 반올림 정수
- 20·10·5 이하 누적 디버프 상태와 숨·심장 1회 트리거 이벤트
- 사망 시 개인 수치 비활성화, 팀 평균 제외, `-%` 디버그 표시
- 귀신 프로토타입 F1 HUD의 팀 평균 정신력 표시 — 정신력 값의 출처를 이 시스템 하나로 통일
- 개인 정신력 20 이하에서 켜지는 카메라 테두리 연출(비네트+필름 그레인+색수차)
- 집 서쪽 테스트베드 — 서버 가시 판정으로 시체 2구·귀신 이벤트 1개를 실제로 목격해 보는 공간
- Game 후면 World Space 모니터의 최대 4인 개인 슬롯·팀 평균 정수 퍼센트 표시
- 미접속 플레이어 슬롯의 흑백 비활성 표시
- F1 HUD의 모든 입력 경로와 상태 표시

아직 포함하지 않음:

- 헤드라이트 입력과 드릴 카 안전 구역 판정
- 실제 귀신 이벤트·시체·아이템·사망 시스템에서의 API 호출
- 속삭임, 숨·심장 SFX 재생 컴포넌트와 오디오 에셋 (카메라 노이즈는 구현했다)
- 정식 시체·귀신 이벤트·아이템 시스템 (테스트베드는 그 자리를 임시로 채운 것이다)
- 드릴 카 모니터 정식 UI
- Steam 2PC 복제 검증

## 2. 네트워크 권위

| 데이터·행동 | 권위 | 복제·노출 |
| --- | --- | --- |
| 개인 정신력 | Server | `NetworkVariable<int>` / Everyone Read |
| 생존 여부 | Server | `NetworkVariable<bool>` / Everyone Read |
| 최종 어둠 노출 여부 | Server | `NetworkVariable<bool>` / Everyone Read |
| 어둠 누적 초 | Server 로컬 상태 | 복제하지 않음 |
| 목격한 시체 ID 집합 | Server 로컬 상태 | 복제하지 않음 |
| World Space 개인·팀 모니터 | 각 Client | `CopyPlayerStates` + `TryGetTeamAverage`를 0.1초 간격으로 읽어 P1~P4와 팀 평균 정수 퍼센트 갱신 |
| 귀신 F1 HUD의 팀 평균 표시 | 각 Client | `TryGetTeamAverage`를 그대로 읽는다. 귀신 `NetworkVariable`에는 정신력이 없다 |
| 팀 평균 | 각 Peer의 복제 상태로 계산 | `ISanityTeamService.TryGetTeamAverage` |
| 카메라 테두리 연출 | 각 Client 로컬 | 로컬 소유 상태의 `CurrentDebuffs` 를 읽어 Global Volume 가중치만 올린다 |
| SFX 피드백 | Owner Client | 복제 상태의 이벤트·플래그를 표현 예정 |
| 테스트베드 목격 판정 | Server | 서버가 가시 판정을 계산해 위 `Server…` API 를 호출한다 |

클라이언트가 정신력을 직접 쓰는 RPC는 제공하지 않는다. 헤드라이트·귀신 이벤트·시체·아이템·사망
시스템은 먼저 서버에서 판정을 확정한 뒤 아래 `Server…` API를 호출해야 한다.

## 3. 외부 연결 API

`Assets/Scripts/Gameplay/Sanity/SanityNetworkState.cs`:

| API | 호출 시점 |
| --- | --- |
| `ServerResetForStage()` | 스테이지 시작 |
| `ServerSetDarknessExposed(bool)` | 자신의 헤드라이트 OFF이며 드릴 카 밖인지 계산한 최종 결과 변경 시 |
| `ServerApplyGhostEventWitnessed()` | 서버가 귀신 이벤트 목격을 확정했을 때 |
| `ServerApplyCorpseWitnessed(ulong)` | 서버가 시체 최초 가시 판정을 확정했을 때 |
| `ServerRestoreSanity(int)` | 정신력 아이템 사용을 서버가 승인했을 때 |
| `ServerMarkDead()` | 사망 판정 확정 시 |

`SanityNetworkState`는 `SanityChanged`, `DebuffsChanged`, `BreathingHeartbeatTriggered` 이벤트를 제공한다.
실제 로컬 피드백 컴포넌트는 이 이벤트를 구독하고 `OnNetworkDespawn`에서 반드시 해제해야 한다.

## 4. 팀 평균

`SanityTeamService`는 각 `SanityNetworkState.OnNetworkSpawn`에서 등록되고 Despawn에서 해제된다.
사망 상태는 목록에 남아 모니터의 `-%` 표시를 가능하게 하지만 평균에서는 제외한다. 연결 종료 상태는
Player NetworkObject가 Despawn되며 목록에서 제거된다.

반올림은 비음수 값에 대해 `floor(average + 0.5)`를 사용한다. 따라서 99.5는 100이 되며,
`Mathf.RoundToInt`의 midpoint-to-even 동작에 의존하지 않는다.

## 5. 에셋과 배선

| 대상 | 경로·오브젝트 |
| --- | --- |
| 런타임 코드 | `Assets/Scripts/Gameplay/Sanity/` |
| 설정 | `Assets/Settings/Gameplay/SanitySystemSettings_Default.asset` |
| 개인 상태 | `Assets/Prefabs/Player.prefab`의 `SanityNetworkState` |
| 팀 서비스 | `Game/SanitySystem`의 `SanityTeamService` |
| 서비스 등록 | `GameInstaller` → `ISanityTeamService` + `ISanityDebug` |
| 플레이 UI | `Game/SanityWorldMonitor`의 World Space `Canvas` + `SanityHudUI` — 4인 개인 슬롯·팀 평균 숫자 표시 |
| 귀신 소비자 | `Game/GhostPrototypeSystem`의 `GhostPrototypeSpawner` — `Awake`에서 `Services.TryGet`으로 선택 의존 |
| 카메라 연출 | `Game/SanityCameraNoise`의 `Volume` + `SanityCameraNoise`, 프로필 `Assets/Settings/PostProcessing/PP_SanityCameraNoise.asset` |
| 테스트베드 | `Game/SanityTestbed` — `Assets/Scripts/Gameplay/Sanity/SanityWitnessProp.cs` |
| 개발 UI | `Bootstrap/NetworkRig/ConnectionHud` — F1 |

`GhostHunter > 정신력 시스템 설치` 메뉴가 설정, Player 프리팹 컴포넌트, Game 씬 서비스,
인스톨러 배선과 World Space 모니터를 반복 설치·검증한다. 검증은 기존 화면 고정 HUD와 게이지 제거,
World Space 모드, 월드 위치 `(0, 4.25, 10.25)`, 960×540 RectTransform, 0.006 배율,
P1~P4 라벨·심볼·수치 배열과 팀 평균 참조를 확인한다. 전체 `GhostHunter > 프로토타입 게임 생성`의 마지막에도 같은 설치 단계를 실행하므로
Player 프리팹과 Game 씬을 다시 구워도 정신력 컴포넌트와 모니터가 사라지지 않는다.

## 6. 카메라 테두리 연출

기획서 §5의 `n ≤ 20` 규칙을 그대로 옮긴 것이다. **세기 단계는 없다** — 조건을 만족하면 켜고,
벗어나거나 사망하면 끈다. 임계값 판정은 `SanityNetworkState.CurrentDebuffs` 가 이미 갖고 있고,
`SanityCameraNoise` 는 그 결과를 Volume 가중치로 옮기기만 한다.

| 오버라이드 | 값 | 역할 |
| --- | ---: | --- |
| Vignette | intensity 0.45 / smoothness 0.45 | 화면 테두리를 어둡게 좁힌다 |
| Film Grain | Medium1, intensity 0.75 / response 0.8 | 그 위에 입자 노이즈를 얹는다 |
| Chromatic Aberration | intensity 0.6 | 가장자리로 갈수록 색이 갈라진다 |

- **저장된 `Volume.weight` 는 MUST 0이다.** 0이 아니면 에디터에서 늘 노이즈 낀 화면으로 보인다.
  런타임에 `SanityCameraNoise` 가 0↔1 사이를 이동시킨다(켜짐 0.35초, 꺼짐 0.8초).
- **URP는 카메라마다 포스트 프로세싱을 켜야 한다.** Player 프리팹 카메라에는
  `UniversalAdditionalCameraData` 자체가 없었다 — 설치 도구가 붙이고 `renderPostProcessing` 를 켠다.
  이게 빠지면 프로필이 아무리 정확해도 화면은 그대로다.
- 연출은 각 클라이언트의 로컬 판단이다. 복제하지 않고, 로컬 소유 플레이어 상태만 본다.

## 7. 정신력 테스트베드 (임시)

집 서쪽(`x` -22~-12, `z` -5~5)에 감소 조건을 걸어서 확인하는 공간을 둔다.
`GhostHunter > 정신력 테스트베드 설치` 메뉴가 매번 처음부터 다시 짓는다.

| 칸 | 위치 | 소품 | 기대 결과 |
| --- | --- | --- | --- |
| 북 (`z` 2.2~5) | `(-18.5, 3.6)` | `Corpse_A` (id 1) | 최초 목격 −20, 다시 봐도 무변화 |
| 중 (`z` -0.8~2.2) | `(-18.5, 0.7)` | `Corpse_B` (id 2) | 다른 시체라 또 −20 |
| 남 (`z` -5~-0.8) | `(-18, -3)` | `GhostEvent` | 발생 5초 / 휴지 5초, 발생 1회당 −15 |

- 세 칸은 벽으로 막는다. 옆 칸 소품이 시야에 같이 들어오면 무엇이 깎았는지 구분할 수 없다.
- 목격 판정은 서버가 한다 — 거리 12m, 수평 시야각 70°, 물리 가림. **단순 근접은 조건이 아니다.**
- **서버는 플레이어의 카메라 피치를 모른다**(피치는 소유자 로컬 값이라 복제하지 않는다).
  그래서 수평 방향만 본다. 정식 가시 판정 규칙은 귀신 이벤트 문서에서 정의한다.
- 귀신 이벤트의 발생 위상은 `NetworkManager.ServerTime` 으로 계산한다. 복제 없이 모든 피어가
  같은 순간에 켜고 끄므로, 소품을 `NetworkObject` 로 만들지 않아도 된다 — 씬 배치 해시
  함정(CLAUDE.md §5)을 통째로 피한다.
- 시체 목격 기록은 F1 HUD의 `스테이지 리셋` 으로 지운다. 그래야 같은 시체를 다시 시험할 수 있다.

> **정식 시스템이 아니다.** "어떤 현상을 귀신 이벤트 목격으로 칠지"는 2026-08-31 해결됐다(G-6,
> roadmap M8-GS-2) — 실제 귀신의 초자연현상은 종류를 가리지 않고 목격 시 전부 적용된다
> ([ghost-prototype.md §4](ghost-prototype.md)). 이 소품은 그 판정 로직을 시체·정지된 위치
> 기준으로 재현한 시험대일 뿐이다. 실제 시체·이벤트 시스템이 생기면 `SanityWitnessProp` 과
> 이 공간은 통째로 삭제한다.

## 8. 검증 기록

2026-08-23 코어 검증 및 2026-08-24 World Space 모니터 교체 검증,
Unity 6000.3.20f1 Editor + Local Host:

- 강제 재컴파일 오류 0건
- EditMode **60/60 통과** — 정신력 계산 13건과 Player 배선 검사 포함
- PlayMode **12/12 통과** — 기존 네트워크 던지기 회귀 없음
- Local Host 1명: 개인 100%, 팀 평균 100% 시작 확인
- 어둠 10초 노출 후 99%, OFF 중 누적 일시 정지 확인
- 귀신 이벤트 10 감소, 새 시체 20 감소, 같은 시체 재목격 무감소 확인
- 디버그 아이템 10 회복과 100 Clamp 확인
- 19% 노이즈, 9% 노이즈+속삭임, 0% 전체 누적 상태 확인
- 사망 시 `-%`, 생존 0, 팀 평균 없음; 스테이지 리셋 후 100% 확인
- 기존 화면 고정 `SanityHudCanvas` 제거
- Game 후면 `(0, 4.25, 10.25)`에 960×540 World Space `SanityWorldMonitor` 배치
- 검은 패널·청색 제목선·P1~P4 슬롯·팀 평균 숫자 구성과 게이지 오브젝트 제거 확인
- Local Host 1명 시작 시 P1과 팀 평균 `100%`, P2~P4 흑백 `-%` 확인
- 귀신 이벤트 적용 후 P1과 팀 평균 `90%` 갱신 확인
- 런타임 콘솔 오류 0건

2026-08-24 모니터 검증은 Editor 프로세스가 기본 UDP 7777을 점유하고 있어 런타임에만 17777로
바꿔 수행했다. 프로젝트의 UTP 설정 에셋은 변경하지 않았다.

2026-08-24 테스트베드·카메라 노이즈 추가 검증 (Unity MCP, 에디터 + Local Host 1명):

- 에디터 Test Runner **EditMode 60/60**, **PlayMode 12/12 통과**, 콘솔 오류 0건
- 북쪽 칸에서 `Corpse_A` 를 보자 100% → **80%**, 계속 봐도 80% 유지(같은 시체 무감소)
- 중간 칸 `Corpse_B` 에서 80% → **60%**(다른 시체라 또 −20)
- 남쪽 칸 `GhostEvent` 앞에서 발생마다 −10 — 60 → 50 → 40 → 30 → 20% 로 이어짐
  (휴지 구간에는 감소하지 않음)
- 20% 이하로 내려간 순간 `CurrentDebuffs` 에 `CameraNoise` 가 켜지고
  `Volume.weight` 가 **1.000** 까지 올라감
- F1 `스테이지 리셋` 으로 100% 복귀 후 `Volume.weight` **0.000** 으로 되돌아옴
- 시체 목격 기록도 함께 초기화되어 같은 시체를 다시 시험할 수 있음

> **2026-08-31 정정.** 위 검증은 `Volume.weight` 값만 봤을 뿐, 그 Volume 이 실제로 무엇을
> 블렌딩하는지는 확인하지 않았다. `PP_SanityCameraNoise.asset` 을 만드는 코드가
> `VolumeProfile.Add<T>()` 로 Vignette·FilmGrain·ChromaticAberration 을 만들고도
> `AssetDatabase.AddObjectToAsset()` 을 부르지 않아서, 그 컴포넌트들이 **프로필의 서브 에셋으로
> 저장되지 않았다.** 즉 `weight`가 1.0 이어도 블렌딩할 오버라이드 자체가 디스크에 없어
> **실제로는 화면에 아무 변화도 없었을 가능성이 높다.** 두더지 굴착 스킬의 카메라 연출을 만들다
> 같은 버그를 발견해 `SanityPostProcessingSetup`·`MoleBurrowPostProcessingSetup` 둘 다 고치고
> 재설치했다 → [mole-skill-system.md §8](../project/mole-skill-system.md).
> **재검증 필요**: 이 문서의 "확인" 문구는 `Volume.weight` 전환만 검증됐다는 뜻으로 읽는다.

`SanityWitnessProp` 이 목격마다 `[SanityTestbed]` 로그를 남긴다. 어느 소품이 얼마를 깎았는지
콘솔에서 바로 확인할 수 있다.

2026-08-24 귀신 모의 정신력 제거 후 Unity MCP로 에디터 재검증: EditMode **60/60**,
PlayMode **12/12** 통과. Local Host 1명에서 귀신 HUD의 `팀 정신력`이 `TryGetTeamAverage` 판정값과
같은 값을 표시하고(100% → 이벤트·시체 적용 후 60%), 생존 0명일 때 `-%`로 떨어지는 것까지 확인했다.
런타임 콘솔 오류 0건.

실제 카메라·오디오 연출과 Steam 2PC는 아직 검증 대상이 아니다.

---

관련: [정신력 시스템 기획서](../project/sanity-system.md) · [networking.md](networking.md) ·
[ghost-prototype.md](ghost-prototype.md) · [testing.md](../workflow/testing.md)

최종 갱신: 2026-08-31 (귀신 이벤트 목격 감소량 10 → 15 확정, G-6 해결 반영 — 실제 귀신 목격 판정은 [ghost-prototype.md §4](ghost-prototype.md))
