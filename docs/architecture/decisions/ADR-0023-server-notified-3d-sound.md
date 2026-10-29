# ADR-0023: 서버가 소리를 알리고 각 접속자가 3D 로 재생한다

- **상태**: Accepted
- **날짜**: 2026-10-29
- **결정자**: 프로젝트 담당자(요청), Claude Code(설계)
- **관련**: [audio-system.md](../audio-system.md), [ADR-0010](ADR-0010-server-authoritative-furniture-physics.md), [networking.md §3.2](../networking.md), [unity-assets.md §5.2](../../conventions/unity-assets.md)

## 배경 (Context)

효과음·환경음 시스템이 없었다. 가구 물리는 서버에서만 돌기 때문에 충돌 같은 사건을 아는 것도 서버뿐이다.
클라이언트의 가구는 `isKinematic` 이라 충돌 콜백이 없다. 소리는 각 접속자의 리스너 기준 3D 로 들려야 한다.
씬에 새 `NetworkObject`를 텍스트로 넣으면 `GlobalObjectIdHash`가 확정되지 않는 제약이 있다.

## 검토한 선택지 (Options)

| 선택지 | 장점 | 단점 |
| --- | --- | --- |
| A. 소리 나는 객체마다 `NetworkSoundEmitter`(RPC) | 씬 수정 없음, 객체 수명과 같이 스폰·디스폰, 프리팹 단위 배선 | 소리가 필요한 프리팹마다 컴포넌트를 붙여야 함 |
| B. 씬에 전역 중계 `NetworkObject` | 서버 코드가 어디서든 한 줄로 호출 | 씬마다 NetworkObject 추가와 해시 확정 절차(에디터 필요), 4개 씬 |
| C. `CustomMessagingManager` 이름 있는 메시지 | NetworkObject 불필요 | 프로젝트 규약(`[Rpc(SendTo.…)]` 통합 속성만)과 어긋남 |
| D. 클라이언트가 가구 위치 변화로 충돌 추정 | 네트워크 메시지 없음 | 부정확, 클라이언트가 물리 결과를 재추정 |

## 결정 (Decision)

A. 서버가 `NetworkSoundEmitter.ServerPlay(cue, position, volume)`로 큐 식별자·위치·음량을
`[Rpc(SendTo.ClientsAndHost, Delivery = Unreliable, InvokePermission = Server)]`로 보내고, 각 접속자가 자기
`IAudioService`로 재생한다. 큐 식별자는 큐 키의 FNV-1a 해시이며 `SoundLibrary`에서 조회한다.

## 근거 (Rationale)

- 소리는 연출이라 유실돼도 재전송할 이유가 없다 → 신뢰성 없는 전달, 17B 안팎.
- 클립이 아니라 키 해시를 보내므로 오디오 에셋이 바뀌어도 프로토콜이 그대로다.
- 전역 중계(B)의 편의보다 씬 NetworkObject 해시 함정을 피하는 쪽을 택했다. 가구·귀신·플레이어·문은 이미 `NetworkObject`다.

## 결과 (Consequences)

### 긍정
- 가구 물리 권위(ADR-0010)를 건드리지 않는다. 클라이언트는 소리만 낸다.
- 큐·라이브러리·설정이 SO 라서 오디오가 들어오면 에셋만 만들면 된다.

### 부정 / 감수하는 비용
- 소리를 보내려는 객체마다 `NetworkSoundEmitter`를 붙여야 한다(가구 프리팹 64개에는 붙였다).
- 큐 키를 바꾸면 이전 빌드와 식별자가 달라진다. 라이브러리에 빠진 큐는 받은 쪽이 재생하지 못한다.

### 후속 작업
- [x] 영향받는 문서 갱신: audio-system.md, overview.md, roadmap.md, CLAUDE.md 라우팅
- [x] 코드 반영
- [x] 가구 프리팹에 `NetworkSoundEmitter`·`FurnitureImpactSound` 연결, 설정 창 효과음·환경음 슬라이더
- [ ] 오디오 에셋·큐 제작(에셋 제공 대기)

## 재검토 조건

- 서버 사건이 객체에 묶이지 않는 소리(씬 전역 이벤트)가 많아져 매번 운반 객체를 고르기 어려워질 때 → 전역 중계 검토.
- 위치가 아니라 객체를 따라가는 소리를 서버가 켜고 꺼야 할 때 → `NetworkVariable` 상태 복제.
