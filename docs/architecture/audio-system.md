# 오디오 시스템 — 3D 효과음·환경음

> 효과음과 환경음을 3D 위치에서 재생하는 기반이다. 음성 채팅은 별도다 → [voice-chat.md](voice-chat.md).
> 어떤 상황에 어떤 소리를 낼지(GDD §9)는 아직 `TBD`이고, 이 문서는 소리를 낼 수 있는 틀만 다룬다.

## 1. 구조

| 계층 | 파일 | 역할 |
| --- | --- | --- |
| `Core/Audio` | `IAudioService` | 이 클라이언트에서 소리를 내는 서비스 계약. 모든 호출은 **로컬 재생**이다 |
| | `SoundCue` (SO) | 소리 하나의 정의 — 클립 묶음, 음량·음높이 흔들림, 3D 감쇠, 우선순위, 쿨다운, 동시 수, 믹서 그룹 |
| | `SoundBus`, `SoundRolloff`, `SoundHandle`, `SoundCueId` | 버스(Sfx·Ambient), 감쇠 곡선 종류, 재생 표, 큐 식별자 해시(FNV-1a) |
| `Data/Audio` | `GameAudioSettings` (SO) | 동시 재생 수, 리스너 탐색 간격, 버스 기본 음량·믹서 그룹, 벽 가림 튜닝 |
| | `SoundLibrary` (SO) | 네트워크로 주고받을 큐 목록. 식별자 → 큐 조회, 키 중복·빈 칸·클립 없음 검사 |
| | `ImpactSoundProfile` (SO) | 가구 충돌 속력 → 큐 단계·음량 |
| `Systems/Audio` | `AudioService` | 미리 만든 `AudioSource` 풀, 컬링·가림·쿨다운·버스 음량. `BootstrapInstaller`가 등록 |
| | `SoundVoiceAllocator`, `SoundOcclusion` | 칸 선택·벽 가림 계산(엔진 상태 없는 순수 로직) |
| `Gameplay/Audio` | `NetworkSoundEmitter` | **서버 → 모든 접속자** 소리 알림 RPC |
| | `FurnitureImpactSound` | 가구의 서버 충돌 → 충돌음 |
| | `AmbientSoundSource` | 씬에 놓는 환경음 발생원(각 접속자가 로컬로 재생) |

에셋: `Assets/Settings/Audio/` — `GameAudioSettings.asset`(Bootstrap 씬 `BootstrapInstaller._audioSettings`에 배선),
`SoundLibrary.asset`, `ImpactSoundProfile_Default.asset`. 어셈블리는 새로 만들지 않았다.

## 2. 재생 흐름

**로컬 재생** — `Services.Get<IAudioService>()`를 `Awake`에서 받아 쓴다.

```csharp
_audio.Play(cue, position, volumeScale);          // 월드 좌표 3D 원샷
_handle = _audio.PlayAttached(loopCue, transform); // 대상을 따라다님. 반복 큐는 이 방식
_audio.Play2D(cue);                                // UI·내 머릿속 소리(컬링·가림 없음)
_audio.Stop(_handle, fadeSeconds);
```

`Play*`가 `SoundHandle.None`을 돌려주면 재생되지 않은 것이다 — 들릴 거리 밖, 쿨다운 안, 큐 동시 수 초과,
풀이 가득 차고 새 소리가 덜 중요함, 클립 없음. 오류가 아니다.

**서버가 정한 소리** — 가구 물리처럼 서버만 아는 사건은 서버가 알리고 각 접속자가 자기 쪽에서 재생한다.

```csharp
// 서버 코드 (같은 NetworkObject 에 NetworkSoundEmitter 가 있어야 한다)
_emitter.ServerPlay(cue, worldPosition, volumeScale);
```

## 3. 네트워크 권위 설계

- **서버가 정하는 것:** 소리가 날 사건(충돌 속력·지점)과 어떤 큐를 얼마나 크게 낼지.
- **클라이언트가 하는 것:** 받은 식별자로 `SoundLibrary`에서 큐를 찾아 로컬 재생. 클라이언트는 서버에 소리를 요청하지 않는다.
- **전달:** `[Rpc(SendTo.ClientsAndHost, Delivery = Unreliable, InvokePermission = Server)]`. 페이로드는
  `uint cueId` + `Vector3` + `byte volume`(약 17B). 소리는 유실돼도 다시 보내지 않는다. 호스트도 클라이언트로서 듣는다.
- **서버 보호:** `ServerPlay`가 NaN·무한대 위치를 거르고, 같은 이미터에서 같은 큐를 `max(0.03초, 큐 쿨다운)` 안에 다시 보내지 않는다.
- **식별자:** 큐 키(`SoundCue.Key`, 비우면 에셋 이름)의 FNV-1a 해시. **키를 바꾸면 이전 빌드와 맞지 않는다.**
  네트워크로 보낼 큐는 반드시 `SoundLibrary`에 넣는다 — 빠지면 받은 쪽이 경고를 남기고 소리를 내지 않는다.
- 소리 위치는 월드 좌표로 보낸다. 객체에 붙여 따라가는 소리가 필요하면(반복 소리 등) 상태를 `NetworkVariable`로 복제하고
  각 접속자가 `PlayAttached`로 로컬 재생한다(`AmbientSoundSource`가 그 로컬 쪽 예).

NetworkObject 가 없는 곳(씬 전역)에서는 보낼 수 없다. **씬에 새 NetworkObject 를 텍스트로 넣으면 `GlobalObjectIdHash` 가
확정되지 않으므로**([unity-assets.md §5.2](../conventions/unity-assets.md)) 전역 중계 오브젝트를 두지 않고,
소리가 나는 객체(가구·귀신·플레이어·문)에 `NetworkSoundEmitter`를 붙이는 방식으로 했다.

## 4. 3D 처리

- **감쇠:** 큐의 `MinDistance`·`MaxDistance`·`Rolloff`(로그/선형/커스텀 곡선)·`Spread`·`DopplerLevel`. 도플러 기본 0.
- **거리 컬링:** 완전한 3D 큐(`SpatialBlend ≥ 0.99`)는 리스너가 `MaxDistance` 밖이면 풀을 쓰지 않고 버린다.
  활성 `AudioListener`는 서비스가 직접 찾아 캐시한다(기본 0.5초 간격으로 다시 탐색 — 씬 전환·관전 카메라 교체 대응).
  `PlayerLook`·`SpectatorController`는 건드리지 않는다.
- **벽 가림:** `GameLayers.VoiceOccluderMask`(플레이어·가구·귀신 제외)로 리스너 → 소리 위치 레이를 쏴 벽 수를 세고,
  `벽 한 겹당 음량 × WallVolumeFactor`, `차단 주파수 × WallCutoffFactor`(바닥값 있음)를 적용한다. 원샷은 시작 때 한 번,
  반복·따라가는 소리는 `OcclusionHz`마다 다시 잰다. 큐에서 `Occludable`을 끌 수 있다.
- **동시 재생 한도:** `GameAudioSettings.VoiceCount`(기본 32) 칸. 가득 차면 우선순위 값이 크고(덜 중요하고) 멀고 오래된 소리를
  새 소리가 더 중요할 때만 밀어낸다. 큐별로 `Cooldown`, `MaxSimultaneous`를 둘 수 있다.

## 5. 오디오 파일이 들어왔을 때

1. 파일을 `Assets/Audio/…`(또는 합의된 폴더)에 `SFX_` / `BGM_` 접두사로 넣는다. 짧은 효과음은 Load Type `Decompress On Load`,
   긴 소리는 `Streaming` → [unity-assets.md](../conventions/unity-assets.md). `.wav`/`.mp3`/`.ogg`는 LFS 규칙에 있다.
2. `Create > GhostHunter > Audio > Sound Cue`로 큐를 만든다. 클립을 넣고 음량·감쇠·우선순위를 정한다.
   같은 상황의 변형 소리는 한 큐에 클립을 여러 개 넣는다(연속 반복 안 함).
3. 네트워크로 보낼 큐면 `SoundLibrary.asset`의 `Cues`에 넣는다(콘솔에 중복·빈 칸·클립 없음 경고가 뜬다).
4. 큐를 쓰는 곳에 연결한다 — 가구 충돌은 `ImpactSoundProfile`의 단계에, 환경음은 `AmbientSoundSource`의 큐에,
   그 외는 코드가 큐 필드를 직렬화해 `IAudioService`/`NetworkSoundEmitter`로 부른다.

## 6. 가구 충돌음 연결

`FurnitureNetworkPhysics`는 서버 충돌마다 인스턴스 이벤트 `ServerImpacted(point, speed, other)`를 낸다
(기존 정적 `ServerImpactReported`와 같은 조건 — 배치 직후 보호 시간·복구 중·파손/보관 가구는 제외).
`FurnitureImpactSound`가 이를 받아 `ImpactSoundProfile`로 큐·음량을 정하고 `NetworkSoundEmitter.ServerPlay`로 보낸다.

- 가구끼리 부딪히면 양쪽이 같은 충돌을 보고하므로, 둘 다 이 컴포넌트가 있으면 네트워크 ID 가 작은 쪽만 소리를 낸다.
- 가구가 플레이어·귀신 콜라이더와 부딪힌 접촉은 `FurnitureNetworkPhysics`가 거르므로(내구도와 같은 규칙) 소리도 나지 않는다.
- **`FurnitureNetworkPhysics`가 붙은 프리팹 64개 전부**(`Assets/Prefabs/Furniture`, `Assets/Prefabs/FurnitureDriverParts`)에
  `NetworkSoundEmitter` + `FurnitureImpactSound`(`_profile` = `ImpactSoundProfile_Default`)를 붙였다(2026-10-29).
  새 가구 프리팹을 만들면 같이 붙인다 — `AudioWiringTests`가 빠진 프리팹을 알려 준다.

## 7. 믹서와 음량

- 큐에 믹서 그룹이 없으면 버스 기본 그룹을 쓴다. 현재 효과음·환경음 모두 `VoiceMixer.mixer`의 `SFX` 그룹이다.
  `Ambient` 그룹을 믹서에 만들고 `GameAudioSettings._ambientGroup`에 넣으면 분리된다. 믹서 이름이 `VoiceMixer`인 것도 다듬을 후보다.
- 버스 음량(`IAudioService.SetBusVolume`)은 서비스가 소스 음량에 직접 곱한다 — 믹서 노출 파라미터가 필요 없다.
  전체 음량은 기존대로 `AudioListener.volume`([settings-menu.md](settings-menu.md)).
- 설정 창 오디오 탭의 **효과음·환경음 슬라이더**(`IUserSettings.SfxVolume`·`AmbientVolume`, 저장 키 `Audio.SfxVolume`·`Audio.AmbientVolume`)가
  `AudioService`의 버스 음량이 된다: `GameAudioSettings`의 버스 음량(기본 배율) × 설정 창 값. 값이 바뀔 때마다 다시 적용되고 재생 중인 소리에도 바로 반영된다.

## 8. 검증 상태

- 순수 로직·서비스 거절 조건·라이브러리·충돌 프로파일: EditMode(`SoundCueTests`, `SoundVoiceAllocatorTests`,
  `ImpactSoundProfileTests`, `AudioServiceTests`).
- 저장된 에셋·Bootstrap 배선·가구 프리팹 연결: `AudioWiringTests`. 설정 창 음량: `UserSettingsStoreTests`, `AudioServiceTests`.
- **미검증:** 실제 소리(오디오 에셋 없음), 벽 가림 체감, Host/Client 2인에서 RPC 재생(Steam은 PC 2대 필요),
  가구 충돌음 체감, 풀 한도 체감, 설정 창 오디오 탭의 슬라이더 배치(슬라이더 2개가 늘어 레이아웃 육안 확인 필요).

관련: [networking.md §3.2](networking.md) · [furniture-physics.md](furniture-physics.md) · [voice-chat.md](voice-chat.md)
