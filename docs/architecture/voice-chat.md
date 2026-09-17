# 근접 음성 구현

기준: [기획서](../project/voice-chat-system.md) 0.2와 2026-09-17 사용자 정책 승인.

## 구성과 수명

- `Core/Voice`: `IVoiceCaptureService`, `IVoiceChatService`, `IVoiceParticipant`, `VoiceMode`.
- `Systems/Steam/SteamVoiceCapture`: Steam 캡처·디코딩. BootstrapInstaller가 전역 등록한다.
- `Gameplay/Voice/VoiceChatService`: GameInstaller가 등록하는 세션 참가자·설정 서비스.
- `PlayerVoiceEmitter`: Player 프리팹에 포함한 NGO 컴포넌트. 소유자 캡처와 서버 중계를 담당한다.
- `VoiceReceiver`: Player/VoiceAudio의 AudioSource·AudioLowPassFilter·스트리밍 클립을 관리한다.
- `VoiceAttenuation`, `VoiceActivityGate`, `VoiceFrameQueue`, `VoicePcmBuffer`, `VoicePacketLimiter`: 테스트 가능한 계산·큐·게이트.
- `VoiceIndicatorHud`: 마이크 상태와 발화자 상시 표시. ESC 메뉴 또는 개발 F1에서 모드·뮤트·음량·VAD 조절과
  "Steam 음성 설정 열기" 버튼(기획 §5.1 제약 1).
- `DebugTools/VoiceDebugHud`, `LoopbackVoiceCapture`: F1에서 마이크 없이 440Hz 사인파 송신, F3으로 자가 모니터.
  수신 디코더는 개발 빌드에서만 연결한다 → 아래 "혼자 검증".

새 어셈블리나 패키지는 추가하지 않았다. Gameplay와 테스트 asmdef에 기존 `Unity.Collections` 참조를
추가해 NativeArray RPC를 사용한다. `Steamworks`는 Systems/Steam에만 등장한다.

## 캡처·전송·재생

설치된 Facepunch 2.5.2에는 기획서의 `OnVoiceData` 이벤트가 없다. 실제
`ReadVoiceData(Stream)`·`DecompressVoice(Stream, int, Stream)`을 사용하며 메모리 스트림과 배열을 재사용한다.
24kHz/16bit/mono 디코딩 후 float PCM으로 변환한다. Steam 초기화는 기존 SteamLobbyManager가 소유한다.

압축 프레임을 50ms마다 읽고 20ms PCM 창으로 VAD를 판정한다. -42/-48dBFS 이력, 350ms 행오버,
150ms 선행 프레임을 기본값으로 사용한다. 압축 블록은 코덱 내부 형식이라 임의로 자르지 않는다.
송신 패킷은 `[블록 길이 ushort + Steam 블록]`을 반복하는 최대 512B 묶음이며,
RPC 인자로 codec·sequence·생존 채널을 전달한다. 선행 프레임 합계가 512B를 넘으면 오래된 블록부터 버린다.
510B를 넘는 단일 압축 블록은 버린다. 선행 음절 보존과 드롭률은 실제 마이크로 확인해야 한다.

서버는 소유권, 길이·블록 경계, 허용 코덱, 서버 생존 상태, 슬라이딩 1초당 30패킷 상한을 검사한다.
자기 자신과 접속하지 않은 사용자를 제외하고 XZ 12m/높이 3.6m 안의 생존자에게만 중계한다.
사망자는 사망자끼리 위치 제한 없이 중계하고 생존자와 완전히 분리한다.
서버는 중계용 음성을 해독하지 않는다. 리슨 호스트의 로컬 수신 경로는 다른 클라이언트와 같이 해독한다.

수신자는 오래된 순서·잘못된 채널의 패킷을 버린다. 2초 이상 패킷이 없으면 순서 기준을 다시 잡아
멀리 떨어졌다 돌아온 화자가 sequence 반주기 때문에 영구 무음이 되는 것을 방지한다.

PCM 링은 단일 생산자(메인)·소비자(오디오) 구조다. 소비자가 200ms 넘는 오래된 샘플을 건너뛰고,
부족한 출력은 0으로 채운다. 80ms 준비 후 재생한다. 오디오 콜백에서 Unity API·lock·할당을 사용하지 않는다.
생산자는 읽는 슬롯을 덮어쓰지 않고 물리 용량 초과 입력을 버린다. Clear와 겹친 출력은 무음으로 지운다.
200ms 동안 수신이 없거나 채널 불일치·개인 뮤트·거리/층 경계 밖이면 재생과 큐를 정리한다.

## 공간과 설정

설정: `Assets/Settings/Gameplay/VoiceChatSettings.asset`.
XZ 역거리×7~10m 가장자리 페이드, 높이 1.2~2.6m 페이드, 중앙/좌우 3레이의 벽 가림과
로우패스를 사용한다. 가림 레이는 화자 ID에 따라 10Hz 주기를 분산한다.
최종 gain·cutoff를 지수 평활하며, 가청 한계 바깥은 음성 꼬리가 남지 않도록 정확히 차단한다.
사망자 채널은 2D 전역 음성이다. 자유시점 카메라로 생존자 대화를 도청할 수 없다.

`Assets/Settings/VoiceMixer.mixer`의 Master 아래 SFX/Voice를 생성하고 Voice를 출력 그룹으로 연결한다.
기존 비음성 AudioSource를 일괄 재배선하지 않는다. 믹서 생성에는 Unity 6000.3 에디터 내부 API를
리플렉션으로 호출한다. API가 바뀌면 설치 도구가 실패하도록 하며 YAML은 직접 수정하지 않는다.

모드·마이크 뮤트·마스터 음량·VAD 임계값은 PlayerPrefs에 저장하고 세션 종료 때 flush한다.
개별 화자 음량/뮤트는 세션 동안만 유지한다. NGO clientId는 다음 접속에서 달라지므로 영구 키로 쓰지 않는다.
발화자는 현재 `Player <clientId>`로 구분한다. Steam 이름과 clientId의 신뢰 가능한 매핑은 기존에 없다.
입력 장치·게인·노이즈 처리는 Steam 설정에 맡긴다. 게임 안에서는 `IVoiceCaptureService.OpenSettings()`가
오버레이의 Steam 설정(`SteamFriends.OpenOverlay("settings")`)을 연다 — 오버레이가 없거나 꺼져 있으면
false를 돌려주고 HUD가 수동 경로를 알린다. 마이크 없음·권한 거부를 구별하는 API는 없으므로
Steam 초기화/API 실패는 표시하고, 정상 API가 무음을 반환하는 상황은 실제 마이크 점검이 필요하다.

## 설치와 검증

`GhostHunter > 음성 채팅 설치` → 설정·믹서·Player·Bootstrap/Game 배선을 저장하고 참조를 검사한다.
기존 씬이 dirty이면 덮어 저장하지 않고 중단한다. 열린 씬 구성을 복원한다.
프로토타입 전체 생성 경로도 이 설치를 호출해 재생성 때 음성 배선이 빠지지 않게 했다.
전체 생성 메뉴는 맵을 재생성하므로 음성 설치 목적으로 사용하지 않는다.

네트워크 Player 컴포넌트와 RPC가 추가됐으므로 `SteamLobbyManager.NetProtocolVersion`을 2로 올렸다.
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
  감쇠·가림·층 차단을 그대로 확인할 수 있다. VAD만 확인할 수 없다.
- 자가 모니터는 **호스트에서만** 동작한다. 중계 판정을 서버가 하는데, 서버가 보는 것은
  자기 쪽 설정이기 때문이다. 혼자 검증은 어차피 Local Host라 제약이 되지 않는다.
- 저장하지 않는다. 다음 실행은 항상 꺼진 채로 시작한다.

혼자 확인할 수 있는 것: AC-1~AC-9(거리·벽·문틀·층), AC-16~AC-18(첫 음절·타자 오검출·`M`).
진단 줄의 dBFS를 보면서 타자·클릭할 때 게이트가 열리는지 눈으로 볼 수 있어 VC-21 임계값을
혼자 조정할 수 있다. 죽으면 스피커가 2D 전역으로 바뀌므로 AC-11의 절반도 확인된다.

**혼자 확인할 수 없는 것**: AC-10(4인 대역폭), AC-12(화자 퇴장), AC-14(변조 클라이언트),
AC-15(macOS), 그리고 실제 원격 왕복 지연·패킷 손실. 이것들은 아래 수동 확인이 그대로 남는다.

수동 확인:
1. Bootstrap에서 Play 후 F1 Local Host로 Game 진입. M으로 마이크 표시가 바뀌는지 확인한다.
2. ESC 음성 설정에서 OpenMic/PTT 전환, 마스터 음량과 VAD 임계값을 조절한다.
3. 개발 Local 2프로세스에서 F1 테스트 사인파를 한쪽만 켜고 거리 2/9/11m, 벽, 1층/2층을 비교한다.
4. PC 2대·Steam 계정 2개로 실제 문장 첫 음절·말끝, M, 사망자 채널, 퇴장 시 정리를 확인한다.
5. macOS에서 같은 송수신 경로와 네이티브 API 오류 여부를 확인한다.

관련: [ADR-0015](decisions/ADR-0015-steam-voice-ngo.md).

### 2026-09-17 검증 기록

- 음성 EditMode 36/36, PlayMode 3/3 통과.
- Bootstrap → 실제 Local Host 진입, Steam 캡처 활성, 뮤트 시 캡처 정지 확인.
- 개발 사인파가 같은 서버 RPC에서 533패킷 승인되는 것을 확인.
- Disconnect 후 참가자 0, 캡처 정지, 송신 표시 꺼짐 확인. 플레이 모드를 종료하고 테스트가 만든 뮤트 설정을 복원했다.
- MCP에서 합성 M 입력은 에디터 비활성 포커스 환경에서 적용되지 않았다. 실제 M 키 조작은 별도 확인 필요.
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
