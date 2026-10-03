# ADR-0022: 음성 캡처를 Steam Voice 에서 Unity Microphone + Opus(Concentus)로 바꾼다

- **상태**: Accepted
- **날짜**: 2026-10-03
- **결정자**: 사용자(2026-10-03 "그대로 구현해줘")
- **관련**: [voice-chat-system.md](../../project/voice-chat-system.md) VC-1·VC-19, [voice-chat.md](../voice-chat.md), [settings-menu.md](../settings-menu.md)

## 배경 (Context)

VC-1(2026-09-17)은 Steam Voice 를 골랐고 "입력 장치 선택이 게임 안에 없다"는 제약을 받아들였다.
VC-19(2026-09-27)는 "게임은 입력에 아무 처리도 하지 않는다 — 장치·게인·노이즈는 Steam 설정"으로 확정했다.
설정 창을 만들자 사용자가 마이크 설정을 Steam 이 아니라 게임 안에서 하기를 원했다.
Steam Voice 는 녹음 시작/정지와 **이미 압축된 블록**만 준다 — 장치 선택·게인을 바꿀 API 가 없고,
압축 전 PCM 이 없어 게임이 게인을 걸 수도 없다.

## 검토한 선택지 (Options)

| 선택지 | 장점 | 단점 |
| --- | --- | --- |
| A. Steam Voice 유지 + "Steam 음성 설정 열기" 버튼 | 변경 없음, Steam 노이즈 제거 | 장치·게인을 게임에서 못 바꾼다(사용자 요구 불충족) |
| B. Unity `Microphone` + 무압축 PCM | 라이브러리 없음 | 48kHz 16bit 96KB/s/인 — 호스트 업로드 감당 불가 |
| **C. Unity `Microphone` + Opus(Concentus, 순수 C#)** | 장치 선택·게인·게이트·내 목소리 듣기 가능, Steam 없이도 음성, 약 3KB/s/인 | 서드파티 DLL 추가, 노이즈 제거는 직접(게이트만), 인코딩 CPU |
| D. 상용 미들웨어(Dissonance·Vivox·Photon Voice) | 기능 완비 | 유료·외부 서비스·전송 계층 중복 |

## 결정 (Decision)

C. `Systems/Voice/MicrophoneVoiceCapture` 가 Unity `Microphone` 으로 녹음해 48kHz 로 맞추고
게인 → 노이즈 게이트(오픈 마이크에서만, 기본 켬) → Opus(Concentus 1.1.7, `Assets/Plugins/Concentus/`) 로 압축한다.
전송(NGO RPC·로비 Steam P2P)·서버 컬링·재생은 그대로다. 코덱 번호는 `VoiceCodecs.Opus`(2), Steam Voice(0)는 더 받지 않는다.

## 근거 (Rationale)

- 사용자 요구(장치·게인을 게임 안에서)는 압축 전 PCM 을 가져야만 가능하다 → B 또는 C.
- B 는 대역폭이 감당되지 않는다. C 는 같은 전송 구조에 캡처만 바꾸면 된다.
- Concentus 1.1.7 은 의존성 없는 netstandard1.0 관리 DLL 이라 모든 플랫폼(맥 포함)에서 같은 코드가 돈다. 라이선스 BSD-3.
- **48kHz 인 이유:** Concentus 1.1.7 은 24kHz VOIP(SILK) 에서 디코딩 결과가 무음이었다. 16·48kHz 는 정상
  (2026-10-03 EditMode 테스트와 콘솔 측정, 비율 1.00). 대부분의 마이크·출력 장치가 48kHz 라 리샘플도 줄어든다.

## 결과 (Consequences)

### 긍정
- 설정 창 마이크 탭에서 입력 장치·입력 게인(±12dB)·노이즈 게이트(켬/끔·기준)·내 목소리 듣기를 바꾼다.
- Steam 클라이언트 없이도 음성이 된다(로컬 UTP 개발 경로 포함). 개발용 사인파(VC-18)는 여전히 마이크 없는 검증용이다.
- `Steamworks` 참조가 음성 캡처에서 빠졌다(로비 P2P 전송만 남음).

### 부정 / 감수하는 비용
- **Steam 의 노이즈 제거·에코 처리가 없어진다.** 대신 노이즈 게이트로 조용한 잡음을 거른다. 키보드처럼 큰 잡음은 게이트를 넘는다.
- **이전 빌드와 음성이 섞이지 않는다**(코덱 0 ↔ 2). 모두 같은 빌드여야 한다.
- 인코딩이 CPU 를 쓴다(20ms 마다 한 번, 순수 C#). 게이트가 닫혀 있어도 인코더 상태를 잇기 위해 압축은 계속한다.
- VC-19 "게임은 입력 처리를 하지 않는다"를 뒤집었다 — 게인·게이트는 게임이 한다.

### 후속 작업
- [x] 영향받는 문서 갱신: voice-chat.md, voice-chat-system.md(VC-1·VC-19), settings-menu.md, overview.md, roadmap.md
- [x] 코드 반영
- [ ] **macOS:** `ProjectSettings` 의 Microphone Usage Description 이 비어 있다. 맥 빌드는 이 문구가 없으면 마이크 권한 요청 없이 막히거나 종료된다 — 사용자 확인 후 채운다.
- [ ] Steam 2PC·4인 실기: 음질·지연·CPU·대역폭

## 재검토 조건

- 실기에서 Concentus 인코딩 CPU 가 프레임에 보일 만큼 크면 → 네이티브 libopus 또는 미들웨어(D).
- 잡음 민원이 게이트로 해결되지 않으면 → RNNoise 류 노이즈 억제 추가 검토.
