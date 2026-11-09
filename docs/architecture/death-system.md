# 죽음 시스템 구현

기획 기준: [죽음 시스템 0.1](../project/death-system.md). 임시 외형과 `Result` 연결은
2026-09-27 사용자 확인을 받았다. 시체는 2026-11-03 사용자 요청("가구처럼 밀치고 잡을 수 있게, 가구 판정 느낌으로")으로
가구와 같은 잡기·던지기 규칙을 쓴다 — 아래 "시체 물리·입력".

## 상태와 순서

1. 귀신의 잡힘 또는 서버 디버그 명령이 `SanityNetworkState.ServerMarkDead()`를 호출한다.
2. 서버의 `SanityState`가 생존을 종료하고 `_isAlive`를 복제한다. 모든 생존 판정과 귀신 타깃
   필터가 즉시 사망자를 제외한다.
3. 소유자 `SpectatorController`가 생존 조작을 즉시 잠그고 1.4초 기본값의 임시 카메라 연출
   (내려다보기·상승/흔들림·블랙아웃)을 거쳐 자유시점으로 전환한다. 다른 피어는 기존 몸통 메시가
   떠오른 뒤 떨어지는 것을 본다.
4. `PlayerVisuals`가 사망 위치에 시체를 만들고, 원래 몸통 렌더와 CharacterController를 끈다.
   부활하면 시체를 제거하고 원래 몸통을 복구한다. `Game` 씬 이탈·디스폰에서도 제거한다.
5. `SanityTeamService`가 남은 스폰 플레이어 전원이 사망했는지 서버에서 검사한다. 전멸을 한 번만
   확정하고 `ServerRevive`·`ServerResetForStage`를 거절한다. 사망 연출이 끝나면 서버가
   `ISceneFlow`를 통해 `Result`로 전환한다. 결과 화면에는 사망자 수와 로비 이동 버튼이 있다.
   호스트가 버튼을 누르면 NGO로 모두 `Lobby`를 연 뒤 각 참가자가 Netcode 연결을 종료한다.
   Steam 로비 멤버십은 유지하며, 호스트는 클라이언트의 종료를 기다리고 연결을 닫는다.
   `Game`을 벗어날 때 관전 카메라·AudioListener를 꺼서 Result 카메라와 겹치지 않게 한다.

기획서 §3의 표현 순서와 달리, 현재 네트워크 `Dead` 플래그는 연출 시작과 동시에 바뀐다. 이
순서여야 진행 중인 귀신 추격·문·가구·청소 RPC가 같은 틱에 사망자를 거부한다. 연출과 관전
카메라 전환은 그 뒤 로컬에서 실행된다.

## 시체 물리·입력

`PlayerVisuals`는 서버에서만 시체 `Rigidbody`를 움직이고 `NetworkVariable<Vector3>`와
`NetworkVariable<Quaternion>`로 복제한다. 각 클라이언트는 자신의 로컬 메시 복사본을 이 값으로
보간한다. 별도 네트워크 프리팹은 없다 → [ADR-0016](decisions/ADR-0016-player-owned-corpse.md).
2026-09-27부터 복사본은 두더지 모델을 **본까지** 복제한 것이다(스킨 메시는 렌더러만 복제하면 본이 원래
플레이어에 남는다). 캡슐 중심 루트 `Corpse_{id}`에 콜라이더를 달고 모델은 그 아래 절반 높이만큼 내려 둔다
→ [player-controller.md](player-controller.md) "캐릭터 모델·애니메이션".
생성된 시체 콜라이더는 `PlayerVisuals`의 로컬 등록표로 원래 Player와 연결하고 부활·디스폰 때 해제한다.
새 Player 네트워크 변수와 사망·시체 RPC 때문에 로비 네트워크 프로토콜 버전을 4로 올렸다.
2026-11-03 시체 잡기 상태 복제(`NetworkVariable` 2·`NetworkList` 1)와 시체 RPC 서명 변경으로 9로 올렸다.

**가구와 같은 규칙 (2026-11-03).** 시체는 가구와 같은 레이어(`Furniture`)에서 판정받고, 잡기·던지기 규칙도
[throw-system.md](throw-system.md)와 같다. 가구 값(`FurnitureThrowSettings`)을 그대로 쓰므로 Hub·튜닝 창의 조정이 시체에도 먹는다.

| 입력 | 동작 |
| --- | --- |
| 1명이 누르고 있음 | 투척 준비(`ThrowReady`) — 차지가 오른다(`chargeTime`·`chargeCurve`), 윤곽선 청록색, 게이지 "시체 · 1인 투척 준비" |
| 1명이 뗌 | 차지만큼 발사 — 발사각 20°~70° 보정, `oneHolderForce`×차지 곡선, 속도 변화량이라 질량과 무관 |
| 2명이 누름 | 2인 운반(`Held`) — 두 손잡이·속도 서보·휠 회전/기울이기(`FurnitureCarrySession`·`FurnitureHeldControl` 재사용), 중력 끔 |
| 2명 중 1명이 뗌 | 남은 사람 조준 방향으로 즉시 발사(`launchOnFirstRelease`, 끄면 같이 내려놓기 창) |
| 눈~시체 표면 4m 초과 | 차징 중이면 그 홀더가 놓은 것과 같다(`holdBreakDistance`) — 1인은 발사 없이 해제 |
| 메뉴 열기·대걸레/드라이버 장착 | 발사 없이 놓는다(`RequestCorpseReleaseRpc`의 `launch=false`) |
| 사망·디스폰 | 해당 홀더를 발사 없이 푼다 |

조준하면 가구와 같은 반전 셸 윤곽선이 켜진다(`SpectatorSettings.CorpseOutlineMaterial` = `Furniture_Outline`, 비우면 윤곽선 없음).
색은 가구와 같다 — 조준 흰색·내가 잡음 청록·상대가 잡음 주황·둘이 잡음 노랑·던져지는 중 회색.

구현은 가구와 같은 모양이되 `PlayerVisuals` 안에 둔다(시체는 별도 NetworkObject가 아니다 → ADR-0016).
`PlayerVisuals`가 `NetworkVariable<FurnitureState>`·`NetworkVariable<float>`(차지)·`NetworkList<ulong>`(홀더)를 복제하고,
서버가 `FixedUpdate`에서 차지·이탈 해제·발사 후 안정·2인 운반·열린 문짝 통과(`DoorInteractable`)를 처리한다.
운반 중 두 홀더의 플레이어 콜라이더와는 설정 `carryIgnoresHolders` 가 켜져 있을 때만 모든 피어에서 충돌을 끈다(`FurnitureCollisionIgnoreSet`, 가구 §3.3과 같다).
이 설정은 **2026-11-07부터 기본 끔**이라 시체도 운반 중 홀더 몸과 충돌한다.
`FurnitureGrabTarget`·`FurnitureHoverMotor`·`FurnitureLauncher`의 상태 기계를 옮겨 온 것이라 두 곳을 같이 고쳐야 한다
(**공통화 후보**; 시체 쪽에는 끼임 보조 `FurnitureSqueezeAssist`와 heavy 가구 감쇠가 없다).
시체 콜라이더는 `Furniture` 레이어라 귀신과는 **2026-11-07부터 충돌한다**(`GhostPrototypeSettings.CollideWithFurniture`, 이전에는 레이어 충돌을 껐다).
단 귀신 경로 장애물(`FurnitureNavObstacle`)은 `FurnitureNetworkPhysics` 가구에만 붙으므로 시체는 경로를 막지 않는다 — 시체에 걸리면 기존 걸림 감지가 경로를 다시 만든다.
청소·부활 의식 레이캐스트·음성 가림 마스크에서는 가구처럼 제외된다.

`GrabController`는 시체를 맞힌 좌클릭을 플레이어 밀치기·가구 입력보다 먼저 처리한다(우선순위 불변). 누르는 즉시
`RequestCorpseGrabRpc`로 잡기를 요청하고, 서버가 가시선·거리·소유자·생존·시체 상태(상승 연출 중이 아님, `Launched` 아님,
홀더 자리 있음)를 확인한다. 조준 갱신은 서버가 홀더로 받아들인 뒤에만 보낸다. RPC는 잡기·조준·놓기(`launch` 플래그)·휠 회전 4개다.

2026-11-02부터 조준(윤곽선)·누름·놓음·취소는 internal 일반 C# 클래스 `CorpseInteraction`이 관리한다.
로컬 소유자의 스폰 때 한 번 만들고 `GrabController` RPC로 의도를 보낸다. 서버 쪽 `_serverHeldCorpseId` 기록과 권한 검증은
`GrabController`에 남는다.

시체 목격은 서버가 0.2초 간격으로 생존자의 수평 시야각·거리·가림을 검사한다. 식별자는 Player
`NetworkObjectId`와 사망 횟수를 조합해 같은 플레이어가 다시 사망해도 새 시체로 취급한다.
정신력 감소량과 중복 방지는 기존 `SanityNetworkState.ServerApplyCorpseWitnessed`에 맡긴다.

## 조정 값과 범위

임시 사망 연출·시체 수치는 기존 `SpectatorSettings_Default.asset`의 `SpectatorSettings`에
추가했다. 기존 에셋에 새 필드가 없는 경우 `SpectatorSettings.OnEnable`에서 임시값으로 보정한다.
죽음 연출 1.4초, 블랙아웃 0.3초, 상승 1.3m, 시체 질량 1,
목격 거리 12m·각도 70°·간격 0.2초, 시체 콜라이더 길이 1.3m·반지름 0.3m(2026-09-27 추가, 2026-09-29 플레이어 1.3m 로 축소 — 서 있는 캡슐 몸
복제본의 실효 치수와 같다)는 **임시 기본값**이다. 기획 밸런스 확정값이 아니다.
2026-11-03에 시체 전용 밀기 충격량·홀드 기준 시간·운반 거리·스프링·감쇠·최대 가속도 설정을 없앴다(가구 값을 쓴다).
시체 질량 1은 가구(25kg 등)보다 훨씬 가볍다 — 던지기 힘은 속도 변화량이라 같지만 부딪혔을 때의 느낌은 달라
질량을 올릴지는 플레이 테스트에서 정한다. 회전 토크는 질량을 곱해 가구와 비슷한 회전 속도가 되게 했다.

치료비 단가는 기획서에서 `TBD`다. `SceneFlowController`는 전멸 결과의 사망자 수만 보관·표시하며,
전멸 기록이 있는 `Result`에서만 임시 실패 UI를 그린다.
일반 성공 종료의 결과/정산 경로는 아직 없으므로 그 경로에 대한 비용 계산은 연결하지 않았다.
개별 귀신 이벤트의 특수 사망 조건과 정식 부활 규칙도 별도 기획 대기다.

## 검증 상태

**2026-11-03 시체 가구 규칙:** 컴파일 오류 0, 전체 EditMode 616/616·PlayMode 185/185 통과(복제 프로젝트, 실패·스킵 0).
시체 PlayMode 11건·`DeathSystemFlowTests` 4건 포함 → [testing.md](../workflow/testing.md). 2인이 진짜 입력으로 함께 드는 경로, 클라이언트 복제
체감, 스킨 메시 윤곽선 모양, 문·계단 통과 체감은 미검증이다. 아래는 2026-09-27 기록이다.

- `dotnet build GhostHunter.Systems.csproj -t:Rebuild --ignore-failed-sources`와
  `GhostHunter.Tests.PlayMode.csproj`, `GhostHunter.Tests.EditMode.csproj` 빌드 통과
  (2026-09-27, 경고·오류 0).
- `DeathSystemFlowTests`는 서버 사망→시체→부활과 마지막 사망→전멸 한 번→부활 거절을 검사한다.
  Unity Test Runner 실행 결과는 아직 없다.
- Unity Play에서 Host/Client 시체 위치 복제·두 명 운반·목격·죽음 연출·Result→Lobby 흐름을
  확인해야 한다. Steam 2인 검증은 PC 2대와 계정 2개가 필요하다.
- 한 PC의 별도 Local 클라이언트 봇으로 사망 복제·전멸 흐름을 볼 수 있도록 개발 메뉴와
  Host HUD의 원격 사망 버튼을 추가했다 → [testing.md §4.5](../workflow/testing.md).
  봇의 Host 추종은 실제 NGO 클라이언트에서 실행하지만 두 명 운반 입력은 수행하지 않는다.
- Result에서는 생존·사망을 합친 2D 전역 음성 채널로 전환하도록 코드가 연결됐다.
  Game 씬을 내린 뒤 Player 음성 컴포넌트의 수명과 실제 송수신은 Unity Play에서 확인해야 한다.
  코드·검증 상태는 [근접 음성 구현](voice-chat.md#2026-09-27-result-공용-채널)을 따른다.
