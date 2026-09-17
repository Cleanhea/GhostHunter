# 근접 음성 구현

기준: [기획서](../project/voice-chat-system.md) 0.2와 2026-09-17 사용자 정책 승인.

## 구성과 수명

- `Core/Voice`: `IVoiceCaptureService`, `IVoiceChatService`, `IVoiceParticipant`, `VoiceMode`.
- `Systems/Steam/SteamVoiceCapture`: Steam 캡처·디코딩. BootstrapInstaller가 전역 등록한다.
- `Gameplay/Voice/VoiceChatService`: GameInstaller가 등록하는 세션 참가자·설정 서비스.
- `PlayerVoiceEmitter`: Player 프리팹에 포함한 NGO 컴포넌트. 소유자 캡처와 서버 중계를 담당한다.
- `VoiceReceiver`: Player/VoiceAudio의 AudioSource·AudioLowPassFilter·스트리밍 클립을 관리한다.
- `VoiceAttenuation`, `VoiceActivityGate`, `VoiceFrameQueue`, `VoicePcmBuffer`, `VoicePacketLimiter`: 테스트 가능한 계산·큐·게이트.
- `VoiceIndicatorHud`: 마이크 상태와 발화자 상시 표시. ESC 메뉴 또는 개발 F1에서 모드·뮤트·음량·VAD 조절.
- `DebugTools/VoiceDebugHud`, `LoopbackVoiceCapture`: F1에서 마이크 없이 440Hz 사인파 송신. 수신 디코더는 개발 빌드에서만 연결한다.

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
입력 장치·게인·노이즈 처리는 Steam 설정에 맡긴다. 마이크 없음·권한 거부를 구별하는 API는 없으므로
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
