# 근접 음성 구현

> **2026-09-28:** 이 문서의 `GhostHunter > …` 설치·생성·검증 메뉴와 `Editor/…Setup.cs` 도구는 [ADR-0020](decisions/ADR-0020-remove-one-off-editor-setup-tools.md)으로 삭제됐다.
> 도구 실행 절차·결과는 구현 당시 기록이다. 지금은 저장된 씬·프리팹이 원본이고 직접 고친다.

기준: [기획서](../project/voice-chat-system.md) 0.2와 2026-09-17 사용자 정책 승인.

## 구성과 수명

- `Core/Voice`: `IVoiceCaptureService`, `IVoiceChatService`, `IVoiceParticipant`, `VoiceMode`.
- `Systems/Steam/SteamVoiceCapture`: Steam 캡처·디코딩. BootstrapInstaller가 전역 등록한다.
- `Gameplay/Voice/VoiceChatService`: GameInstaller가 등록하는 Game 참가자·설정 서비스.
- `Systems/Steam/LobbyVoiceService`: Bootstrap에서 생성한다. Lobby·Result에서 Steam 로비 멤버끼리
  2D 음성을 주고받는다. Result 전환 때 Game의 Player 캡처는 멈춘다.
- `PlayerVoiceEmitter`: Player 프리팹에 포함한 NGO 컴포넌트. 소유자 캡처와 서버 중계를 담당한다.
- `VoiceReceiver`: Player/VoiceAudio의 AudioSource·AudioLowPassFilter를 구동하고 `OnAudioFilterRead`로 음성을 재생한다.
  **VoiceAudio 컴포넌트 순서는 AudioSource → VoiceReceiver → AudioLowPassFilter 여야 한다**(아래 "재생").
- `VoiceAttenuation`, `VoiceActivityGate`, `VoiceFrameQueue`, `VoicePcmBuffer`, `VoiceResampler`, `VoicePacketLimiter`:
  테스트 가능한 계산·큐·표시 판정.
- `VoiceIndicatorHud`: 마이크 상태와 발화자 상시 표시. ESC 메뉴 또는 개발 F1에서 모드·뮤트·음량과
  "Steam 음성 설정 열기" 버튼(기획 §5.1 제약 1).
- `DebugTools/VoiceDebugHud`, `LoopbackVoiceCapture`: F1에서 마이크 없이 440Hz 사인파 송신, F3으로 자가 모니터.
  수신 디코더는 개발 빌드에서만 연결한다 → 아래 "혼자 검증".

새 어셈블리나 패키지는 추가하지 않았다. Gameplay와 테스트 asmdef에 기존 `Unity.Collections` 참조를
추가해 NativeArray RPC를 사용한다. `Steamworks`는 Systems/Steam에만 등장한다.

## 캡처·전송·재생

설치된 Facepunch 2.5.2에는 기획서의 `OnVoiceData` 이벤트가 없다. 실제
`ReadVoiceData(Stream)`·`DecompressVoice(Stream, int, Stream)`을 사용하며 메모리 스트림과 배열을 재사용한다.
24kHz/16bit/mono 디코딩 후 float PCM으로 변환한다. Steam 초기화는 기존 SteamLobbyManager가 소유한다.

압축 프레임을 50ms마다 읽어 **그대로 송신한다.** 오픈 마이크는 마이크가 켜져 있는 동안 잡힌 소리를 전부,
PTT는 `V`를 누르는 동안 전부 보낸다. **소리 크기로 거르지 않는다**(2026-09-27 사용자 결정 — 이전의 VAD 게이트가
조용한 소리·말끝을 잘라 끊겨 들렸다). 20ms PCM 창의 -42/-48dBFS·350ms 판정(`VoiceActivityGate`)은
"말하는 중" 표시에만 쓴다. 게임은 게인·노이즈 억제 등 어떤 입력 처리도 하지 않는다(VC-19).
압축 블록은 코덱 내부 형식이라 임의로 자르지 않는다.
송신 패킷은 `[블록 길이 ushort + Steam 블록]`을 반복하는 최대 512B 묶음이며,
RPC 인자로 codec·sequence·생존 채널을 전달한다. 510B를 넘는 단일 압축 블록은 보낼 수 없어 버리고,
`SteamVoiceCapture`가 5초에 한 번 경고를 남긴다(예전에는 조용히 버렸다).

서버는 소유권, 길이·블록 경계, 허용 코덱, 서버 생존 상태, 슬라이딩 1초당 30패킷 상한을 검사한다.
자기 자신과 접속하지 않은 사용자를 제외하고 XZ 12m/높이 3.6m 안의 생존자에게만 중계한다.
사망자는 사망자끼리 위치 제한 없이 중계하고 생존자와 완전히 분리한다.
`Lobby`와 `Result`의 공용 음성은 `LobbyVoiceService`가 Steam 로비 멤버에게 직접 전송하고 2D로 재생한다.
`Game`의 `PlayerVoiceEmitter`는 생존·사망 그룹 분리를 유지한다. `Title`에서는 송수신을 멈춘다.
`VoiceChatService`는 Bootstrap의 `ISceneFlow.Current`를 읽는다. Game 씬이 내려가면
Player 네트워크 객체도 언로드될 수 있으므로 Result 음성은 그 객체의 수명에 의존하지 않는다.
서버는 중계용 음성을 해독하지 않는다. 리슨 호스트의 로컬 수신 경로는 다른 클라이언트와 같이 해독한다.

수신자는 오래된 순서·잘못된 채널의 패킷을 버린다. 2초 이상 패킷이 없으면 순서 기준을 다시 잡아
멀리 떨어졌다 돌아온 화자가 sequence 반주기 때문에 영구 무음이 되는 것을 방지한다.

### 재생 (2026-09-27 교체)

**이전 방식의 결함 — 측정으로 확인.** 스트리밍 `AudioClip`(`PCMReaderCallback`)은 재생 시작 순간 메인 스레드에서
약 **800ms**(4096샘플×4 + 2816)를 한꺼번에 읽고, 그 뒤 약 **400ms마다 400ms분**을 몰아 읽었다(검증용 복제 프로젝트
batchmode, 24kHz 클립, 출력 48kHz·DSP 1024). 링버퍼는 최대 200ms만 담고 80ms만 모이면 재생을 시작했으므로
**읽을 때마다 절반 이상이 0(무음)으로 채워지고 오래된 샘플은 버려졌다** — 마이크와 무관하게 "계속 끊겨" 들린 원인이다.

**지금 방식.** AudioSource 는 값이 1.0인 1024샘플 캐리어 클립을 반복 재생하고, `VoiceReceiver.OnAudioFilterRead`
(오디오 스레드, DSP 버퍼 단위 — 48kHz·1024프레임이면 21ms)가 캐리어에 음성 샘플을 곱한다.

- 필터는 **3D 패닝·음량이 적용된 뒤**의 신호를 받는다(측정: 오른쪽 3m 소스 → L 0.017 / R 0.500, volume 0.5 반영).
  그래서 캐리어에 곱하면 공간감·거리 음량이 그대로 유지된다.
- 필터는 **인스펙터 순서대로** 걸린다(측정: 뒤에 붙은 300Hz 로우패스가 우리 신호를 거의 0으로 걸렀다).
  벽 로우패스가 음성에 걸리려면 VoiceReceiver 가 AudioLowPassFilter 보다 **위**에 있어야 한다.
  순서가 어긋나면 런타임 `Initialize` 가 에러를 남긴다(순서를 맞추던 `VoiceChatSetup` 은 ADR-0020으로 삭제).
- 24kHz 음성은 수신 RPC(메인 스레드)에서 `VoiceResampler`(선형 보간, 패킷 경계 위상 유지)로 출력 레이트로 바꿔
  링에 넣는다. 오디오 스레드는 1:1로 꺼내기만 한다. Unity API·lock·할당을 쓰지 않는다.
- 선버퍼 `JitterSeconds`(100ms)가 찰 때까지 무음이고, 바닥나면 다시 찰 때까지 무음이다(찔끔찔끔 재생하지 않는다).
  `MaximumJitterSeconds`(400ms)를 넘게 쌓이면 오래된 샘플부터 건너뛴다. `SilenceTimeout`(500ms) 동안 수신이 없거나
  채널 불일치·개인 뮤트·거리/층 경계 밖이면 재생과 큐를 정리한다.
- PCM 링은 단일 생산자(메인)·소비자(오디오) 구조다. 생산자는 읽는 슬롯을 덮어쓰지 않고 물리 용량 초과 입력을 버린다.
  Clear와 겹친 출력은 무음으로 지운다.

## 공간과 설정

설정: `Assets/Settings/Gameplay/VoiceChatSettings.asset`.
XZ 역거리×7~10m 가장자리 페이드, 높이 1.2~2.6m 페이드, 중앙/좌우 3레이의 벽 가림과
로우패스를 사용한다. 가림 레이는 화자 ID에 따라 10Hz 주기를 분산한다.
최종 gain·cutoff를 지수 평활하며, 가청 한계 바깥은 음성 꼬리가 남지 않도록 정확히 차단한다.
사망자 채널은 2D 전역 음성이다. 자유시점 카메라로 생존자 대화를 도청할 수 없다.
Result 공용 채널도 2D 전역 음성이며, Game 중의 생존자 근접·사망자 분리 정책은 유지한다.

`Assets/Settings/VoiceMixer.mixer`의 Master 아래 SFX/Voice를 생성하고 Voice를 출력 그룹으로 연결한다.
기존 비음성 AudioSource를 일괄 재배선하지 않는다. 믹서는 설치 도구가 Unity 6000.3 에디터 내부 API로 만들었다
(도구는 ADR-0020으로 삭제). 믹서를 고칠 때는 에디터 Audio Mixer 창을 쓴다.

모드·마이크 뮤트·마스터 음량은 PlayerPrefs에 저장하고 세션 종료 때 flush한다(VAD 임계값 슬라이더는 2026-09-27 삭제).
개별 화자 음량/뮤트는 세션 동안만 유지한다. NGO clientId는 다음 접속에서 달라지므로 영구 키로 쓰지 않는다.
발화자는 현재 `Player <clientId>`로 구분한다. Steam 이름과 clientId의 신뢰 가능한 매핑은 기존에 없다.
입력 장치·게인·노이즈 처리는 Steam 설정에 맡긴다 — **게임 코드로 끌 수 없으므로, 게임이 거르지 않는데도 소리가 잘리면
Steam 음성 설정(노이즈 억제·전송 임계값 등)을 확인한다.** 게임 안에서는 `IVoiceCaptureService.OpenSettings()`가
오버레이의 Steam 설정(`SteamFriends.OpenOverlay("settings")`)을 연다 — 오버레이가 없거나 꺼져 있으면
false를 돌려주고 HUD가 수동 경로를 알린다. 마이크 없음·권한 거부를 구별하는 API는 없으므로
Steam 초기화/API 실패는 표시하고, 정상 API가 무음을 반환하는 상황은 실제 마이크 점검이 필요하다.

## 설치와 검증

설정·믹서·Player·Bootstrap/Game 배선은 저장된 에셋·씬·프리팹이 원본이다. 이를 설치·검사하던
`GhostHunter > 음성 채팅 설치` 메뉴는 ADR-0020으로 삭제했다.

네트워크 Player 컴포넌트와 RPC가 추가됐으므로 `SteamLobbyManager.NetProtocolVersion`을 2로 올렸다.
2026-09-27 Player 에 `PlayerNameTag`(NetworkBehaviour)가 추가돼 3으로 올렸다(음성 패킷 형식은 바뀌지 않았다).
Steam 피어는 양쪽 모두 이 변경이 포함된 빌드를 사용해야 한다.

자동 검증 결과는 아래에 갱신한다. 실제 Steam 2PC·macOS 음성·4인 대역폭·실제 마이크 VAD와
문틀 통과/코너에서의 청감은 별도 수동 검증이다. Local Host/가짜 코덱 검증으로 이를 대체하지 않는다.

## 혼자 검증 (자가 모니터)

기획 §10.2가 "혼자서 감쇠·가림·컬링을 검증한다"고 적었지만, **서버는 화자에게 자기 목소리를
되돌리지 않으므로**(§5.3 ③) 사인파만으로는 들을 사람이 없었다. 그래서 개발 빌드에만
**자가 모니터**를 둔다 — 켠 자리에 테스트 스피커를 놓고, 내 목소리가 **서버를 그대로 거쳐**
그 자리에서 재생된다. 검증·전송률 상한·컬링·디코드를 모두 지나므로 실제 경로 그대로다.

```
Bootstrap Play → F1 접속 HUD → 모드 Local → Host → 게임 진입
F3            자가 모니터 켜기 — 지금 선 자리가 스피커가 된다 (끄면 해제, 다시 켜면 그 자리로 옮긴다)
F1            음성 창 — 사인파 토글 · 진단 줄(입력 dBFS · 게이트 · 거리 · 음량 · 가림 · 컷오프)
```

- **반드시 헤드폰을 쓴다.** 스피커로 들으면 마이크가 되받아 하울링이 난다.
- 자기 목소리가 약 130ms 늦게 들린다. 왕복 지연이라 정상이다.
- **마이크가 없거나 Steam이 꺼져 있으면** F1에서 사인파를 켠다. 440Hz가 같은 경로로 흐르므로
  감쇠·가림·층 차단을 그대로 확인할 수 있다. "말하는 중" 표시 임계값만 확인할 수 없다.
- 자가 모니터는 **호스트에서만** 동작한다. 중계 판정을 서버가 하는데, 서버가 보는 것은
  자기 쪽 설정이기 때문이다. 혼자 검증은 어차피 Local Host라 제약이 되지 않는다.
- 저장하지 않는다. 다음 실행은 항상 꺼진 채로 시작한다.

혼자 확인할 수 있는 것: AC-1~AC-9(거리·벽·문틀·층), AC-16~AC-18(끊김 없는 문장·조용한 소리 송신·`M`).
진단 줄의 dBFS·발화 감지·송신 바이트를 보면서 조용히 말할 때도 송신이 끊기지 않는지 눈으로 볼 수 있다. 죽으면 스피커가 2D 전역으로 바뀌므로 AC-11의 절반도 확인된다.

**혼자 확인할 수 없는 것**: AC-10(4인 대역폭), AC-12(화자 퇴장), AC-14(변조 클라이언트),
AC-15(macOS), 그리고 실제 원격 왕복 지연·패킷 손실. 이것들은 아래 수동 확인이 그대로 남는다.

수동 확인:
1. Bootstrap에서 Play 후 F1 Local Host로 Game 진입. M으로 마이크 표시가 바뀌는지 확인한다.
2. ESC 음성 설정에서 OpenMic/PTT 전환, 마스터 음량과 VAD 임계값을 조절한다.
3. 개발 Local 2프로세스에서 F1 테스트 사인파를 한쪽만 켜고 거리 2/9/11m, 벽, 1층/2층을 비교한다.
4. PC 2대·Steam 계정 2개로 실제 문장 첫 음절·말끝, M, 사망자 채널, 퇴장 시 정리를 확인한다.
5. macOS에서 같은 송수신 경로와 네이티브 API 오류 여부를 확인한다.

관련: [ADR-0015](decisions/ADR-0015-steam-voice-ngo.md).

### 2026-09-27 Result 공용 채널

- 아래 검증 기록은 LobbyVoiceService 추가 이전의 Result 중계 경로에 관한 것이다.
- `VoiceChatService`가 Game/Result/Lobby 씬에 따른 송수신 허용과 Result 공용 채널을 제공한다.
- `PlayerVoiceEmitter` 서버 중계, 클라이언트 수신, `VoiceReceiver` 재생이 같은 씬 정책을 사용한다.
- `VoiceTests`는 Result 전원 중계를, `VoiceFlowTests`는 Game 분리 → Result 합류·2D → Lobby 차단을 검사한다.
- 두 테스트 어셈블리의 `dotnet build -t:Rebuild`는 경고·오류 0개로 통과했다.
- Unity Test Runner는 검증용 복제 프로젝트에서 시작했으나 라이선스 초기화가
  `No valid Unity Editor license found`로 종료돼 테스트 결과를 만들지 못했다.
  Host/Client 실제 Result 전환·음성 청감 검증도 대기 중이다.

### 2026-09-17 검증 기록

- 음성 EditMode 36/36, PlayMode 3/3 통과.
- Bootstrap → 실제 Local Host 진입, Steam 캡처 활성, 뮤트 시 캡처 정지 확인.
- 개발 사인파가 같은 서버 RPC에서 533패킷 승인되는 것을 확인.
- Disconnect 후 참가자 0, 캡처 정지, 송신 표시 꺼짐 확인. 플레이 모드를 종료하고 테스트가 만든 뮤트 설정을 복원했다.
- 합성 M 입력은 에디터 비활성 포커스 환경에서 적용되지 않았다. 실제 M 키 조작은 별도 확인 필요.
- PlayMode는 실제 NGO Host 안의 RPC와 가짜 코덱 검증이다. 독립 Client 프로세스·Steam 2PC 왕복 검증은 아니다.

키: PTT V / 뮤트 M / 관전 전환 C. V 중복 발견 후 선택을 질문했고, 응답이 없어 기본안(V/C)을 적용했다.

### 2026-09-17 재검증 (에디터가 열린 상태 — 검증용 복제 프로젝트 batchmode)

- **EditMode 302/302, PlayMode 57/57 통과** (음성만이 아니라 저장소 전체).
- 배선 확인: Player 프리팹의 `PlayerVoiceEmitter`·`VoiceAudio/VoiceReceiver`, Bootstrap 의
  `SteamVoiceCapture`, Game 의 `_voiceSettings`·`VoiceIndicatorHud`·`VoiceDebugHud`,
  설정 에셋의 Voice 믹서 그룹이 모두 저장돼 있다.
- `Input_MenuLockStillAllowsVoiceAndMute` 는 **EditMode 에서 검증이 불가능해** PlayMode
  `VoiceInputTests` 로 옮겼다. 원인과 대처는 [testing.md §5.4](../workflow/testing.md) — 코드 문제가 아니었다.
- **여전히 자동으로 확인하지 못한 것**: 실제 마이크(VAD 임계값·첫 음절), Steam 2PC 왕복,
  문틀/코너 청감, 4인 대역폭, macOS. 위 "수동 확인" 1~5는 그대로 남아 있다.

### 2026-09-27 끊김 수정 (VAD 송신 게이트 제거 + 재생 경로 교체)

- 원인 두 가지: ① 오픈 마이크 VAD 게이트가 -42dBFS 미만·350ms 쉼에서 송신을 닫았다(사용자 요청으로 제거).
  ② 스트리밍 AudioClip 재생이 400ms 단위로 몰아 읽어 200ms 링의 절반 이상이 무음이었다(위 "재생" — 측정 수치).
- 바뀐 것: `PlayerVoiceEmitter.ShouldTransmit`(모드·PTT만 봄), `VoiceReceiver` 캐리어+`OnAudioFilterRead`,
  `VoiceResampler` 신설, `VoicePcmBuffer.Read(dest, count)`, 설정 `PreRollSeconds` 삭제·지터 100/400/500ms,
  HUD 임계값 슬라이더 삭제, Player/VoiceAudio 컴포넌트 순서 교정, `VoiceChatSetup` 순서 보정·검증.
- 자동 검증(검증용 복제 프로젝트 batchmode): **EditMode 322/322 · PlayMode 67/67**. 신규 `VoicePlaybackTests`(연속 재생·
  선버퍼/언더런·캐리어 공간 음량 보존), `VoiceTests`(송신 판단·리샘플러·부분 읽기·설치 순서). 새 순서 검사가 옛 순서로
  조립하던 `VoiceFlowTests` 픽스처를 잡아내 프리팹과 같은 순서로 고쳤다.
- **남은 수동 확인**: 실제 마이크로 조용한 말·말끝이 끊기지 않는지, 벽 뒤에서 로우패스가 여전히 먹는지(F3 자가 모니터),
  Steam 2PC 원격 지연에서 100ms 선버퍼가 충분한지.
