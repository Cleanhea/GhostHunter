# 설정 창 구현

> 상태: **구현 (2026-10-03). 자동 테스트만 통과, 실기 수동 검증 대기.**
> 항목은 사용자 요청(2026-10-03 "게임 테마에 맞는 설정창, 오디오·마이크 설정은 꼭")으로 정했다 —
> [pause-menu-system.md §4.3](../project/pause-menu-system.md) PM-6 해소.

## 1. 항목

| 탭 | 항목 | 저장 | 적용 |
| --- | --- | --- | --- |
| 오디오 | 전체 음량 0~100% | `Audio.MasterVolume` | `AudioListener.volume` — 목소리 포함 모든 소리 |
| 오디오 | 음성 채팅 음량 0~100% | `Voice.Volume` | `IVoiceChatService.MasterVolume` → `VoiceReceiver` |
| 오디오 | 플레이어별 음량·음소거 | **저장 안 함**(이번 게임만) | `IVoiceParticipant.Volume`. 스테이지·인게임 로비에서만 목록이 생긴다 |
| 마이크 | 마이크 켜짐/꺼짐 | `Voice.Muted` | 게임 중 `M` 과 같은 값 |
| 마이크 | 송신 방식 — 오픈 마이크 / 눌러서 말하기(V) | `Voice.Mode` | 기본 오픈 마이크(VC-3) |
| 마이크 | 입력 레벨 막대(-60~0 dBFS) | — | `IVoiceCaptureService.InputLevelDb` (§4) |
| 마이크 | Steam 음성 설정 열기 | — | 입력 장치·감도·노이즈 제거는 Steam 몫(VC 기획 §5.1 제약 1, VC-19) |
| 조작 | 마우스 감도 x0.10~x3.00 | `Input.MouseSensitivity` | `PlayerMoveSettings.MouseSensitivity`·`SpectatorSettings.LookSensitivity` 에 **곱한다** |
| 조작 | 상하 반전 | `Input.InvertMouseY` | 생존자 시점·관전 자유비행 |
| 화면 | 화면 모드(전체/창 모드 전체/창) · 해상도 | Unity 자체 저장 | `Screen.fullScreenMode`·`Screen.SetResolution`. 에디터에서는 바뀌지 않는다 |
| 화면 | 수직 동기화 · 프레임 제한(무제한/30/60/120/144/240) | `Display.VSync`·`Display.FrameRateLimit` | `QualitySettings.vSyncCount`·`Application.targetFrameRate` |

- 기본값은 설정 창 이전 동작 그대로다 — 음량 100%, 오픈 마이크, 감도 x1, 수직 동기화 끔(프로젝트 품질 설정 `vSyncCount 0`), 프레임 무제한.
- 값은 **바꾸는 즉시 적용**되고 창을 닫을 때 `PlayerPrefs.Save()` 한다. "이 탭 기본값으로"는 보고 있는 탭만 되돌린다
  (화면 탭은 수직 동기화·프레임 제한만).
- 음성 세 키(`Voice.*`)는 예전 `VoiceChatService` 가 쓰던 이름 그대로라 기존 사용자 값이 이어진다.
- **넣지 않은 것:** 효과음/배경음 분리 음량(소리가 `VoiceMixer` 의 SFX 그룹으로 라우팅되어 있지 않다), 밝기(감마),
  시야각, 키 재설정, 언어. 다음 후보다.

## 2. 구성

```
Core/Settings/IUserSettings          값·Changed·ResetToDefaults·Save 계약
Core/Settings/UserSettingsGroup      탭 단위(Audio·Microphone·Controls·Display)
Core/Settings/UserSettingsLimits     감도 범위·프레임 상한 목록
Systems/Settings/UserSettingsStore   구현 — 자르기·저장·엔진 적용. BootstrapInstaller 가 맨 먼저 등록(앱 수명)
Systems/Settings/IUserSettingsStorage / PlayerPrefsSettingsStorage   저장 위치(테스트는 메모리)
UI/SettingsMenuView                  창. 탭·위젯·마이크 테스트·플레이어별 음량
UI/SettingsUiKit                     색·UGUI 위젯 생성
```

새 어셈블리·참조는 없다. `IUserSettings` 소비자:

| 소비자 | 쓰는 값 | 획득 |
| --- | --- | --- |
| `VoiceChatService`(Gameplay) | 모드·뮤트·음성 음량 — **자기 필드 대신 위임**. 없으면(테스트) 인스턴스 안에만 둔다 | 생성자 인자. `GameInstaller`·`InGameLobbyInstaller`·`LobbyVoiceService` 가 넘긴다 |
| `PlayerLook` · `SpectatorController` | 감도 배율·상하 반전 | `OnNetworkSpawn` 에서 `Services.TryGet` |
| `SettingsMenuView` | 전부 | `Open()` 마다 `Services.TryGet` |

`M` 키(음성 송신기·로비 음성)와 설정 창은 같은 `IUserSettings` 값을 본다 — 한쪽에서 바꾸면 다른 쪽에 바로 보인다.

## 3. 창의 수명 — 씬에 배치하지 않는다

설정 창은 `Title` 과 일시정지 메뉴가 있는 네 씬(`ProtoTypeGame`·`Stage1`·`Tutorial`·`InGameLobby`)이 함께 쓴다.
씬마다 같은 UI 를 배치하지 않으려고 **여는 쪽이 `SettingsMenuView.Create()` 로 런타임에 짓는다.**
자기 Canvas(Screen Space Overlay, sortingOrder 1000, 1920×1080 기준 스케일)를 갖고 **여는 메뉴와 같은 씬의 루트**에 생긴다
(메뉴 밑에 붙였다 떼는 방식 — 스테이지 로드 직후에는 활성 씬이 아직 이전 씬일 수 있다).
만든 쪽이 `OnDestroy` 에서 함께 파괴한다. 각 씬의 `EventSystem` 으로 클릭을 받는다.

| 여는 쪽 | ESC | 닫힌 뒤 |
| --- | --- | --- |
| `MainMenuController`(Title) | 창이 스스로 닫는다(`closeOnEscape: true`) | 타이틀 그대로 |
| `PauseMenuController` | **메뉴가 다룬다**(`closeOnEscape: false`). `State.Settings` 에서 `UI/Cancel` → `Menu` | 일시정지 메뉴로 돌아간다 |

일시정지 메뉴는 ESC 를 직접 다룬다. 설정 창도 ESC 로 스스로 닫히면 같은 프레임의 ESC 로 메뉴까지 닫히기 때문이다.
`Disconnected` 로 넘어가면 설정 창도 닫힌다(`ApplyState` 가 `Settings` 가 아닌 모든 상태에서 `Close()`).
메뉴 중 입력 잠금·커서는 일시정지 메뉴 규칙 그대로다 → [pause-menu.md](pause-menu.md).

`VoiceIndicatorHud` 는 메뉴가 열려 있을 때 모드·뮤트·음량 버튼을 그리던 것을 그만두고, 상태 표시와 개발용 F1 펼침만 남았다.

## 4. 마이크 입력 막대

`IVoiceCaptureService.InputLevelDb` 는 **누가 `ReadFrame` 을 부르든** 마지막으로 읽힌 블록에서 가장 큰
20ms 창의 dBFS 다. 0.3초 넘게 읽힌 블록이 없거나 녹음 중이 아니면 -120 이다. `SteamVoiceCapture` 는 읽은 블록을
한 번 더 풀어 잰다. 측정 실패는 무시한다 — `ReadFrame` 의 예외 경로는 음성 전체를 끄기 때문이다.

- **Title:** 아무도 마이크를 쓰지 않으므로 창이 녹음을 켜고 50ms 마다 읽어서 **버린다(송신하지 않는다).**
  마이크 탭을 벗어나거나 창을 닫으면 끈다.
- **스테이지·인게임 로비:** 음성 송신기(`PlayerVoiceEmitter`)가 읽는 값을 그대로 보여 준다. 창이 따로 읽으면 송신 프레임을 빼앗는다.
  마이크가 꺼져 있으면 막대도 멈춘다. 송신기는 PTT 모드에서도 녹음은 켜 두므로 막대가 움직인다.

## 5. 검증

| 항목 | 결과 |
| --- | --- |
| EditMode `UserSettingsStoreTests` 9건 — 기본값·자르기·영속·예전 음성 키·알림·탭 되돌리기·음성 서비스 위임 | 통과 §5.1 |
| 창 외형·클릭·ESC·마이크 막대·해상도 변경 (Title·Stage1, 빌드) | **미검증** |
| Steam 2PC 에서 설정 창 뮤트/모드 변경이 상대에게 반영 | **미검증** |

### 5.1 자동 테스트 기록

2026-10-03 검증용 복제 프로젝트(`tools/run-tests.ps1`):
- EditMode **408건 중 406 통과** — 새 `UserSettingsStoreTests` 9건 포함. 실패 2건은 이 작업 전부터 있던
  `MoleSkillWiringTests` 소스 검사(굴착 노출·일시정지 잠금 분기)다.
- PlayMode **94/94 통과** — 음성 흐름·재생 테스트 포함.

최종 갱신: 2026-10-03 (최초 작성)
