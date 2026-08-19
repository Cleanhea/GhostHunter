# 게임 디자인 문서 (GDD)

> 이 문서의 내용은 **사람이 결정한다.** 에이전트는 빈 항목을 추측으로 채우지 않고 질문한다.
> 여기 적힌 수치·규칙이 코드와 다르면, **코드가 틀린 것**으로 간주하고 보고한다.
>
> **상태**: 던지기 메커닉은 확정·구현됨. **게임 루프·승패 조건·유령 진영은 TBD.**

---

## 1. 핵심 루프

### 확정된 것 — 던지기 메커닉

```
[가구 조준] → [홀드로 투척 준비] → [혼자 밀치기 or 둘이 잡아 부양] → [떼면 발사] → [연쇄 충돌]
```

1인칭 시점에서 가구를 조준해 홀드로 붙잡고, 마우스 방향으로 밀쳐 날린다.
오버워치 루시우의 밀쳐내기처럼 **대상이 마우스 방향을 따라 위로 붕 뜨는 감각**이 목표다.
상세: [../architecture/throw-system.md](../architecture/throw-system.md)

### TBD — 이걸 무엇에 쓰는가

> **TBD** — 던지기가 어떤 게임 루프 안에서 의미를 갖는지가 정해지지 않았다.
> 프로토타입은 "던지기가 재미있는가"만 검증했고, 그 답은 나왔다는 전제로 본 프로젝트에 들어왔다.

- **한 줄 소개**: TBD
- **장르**: TBD (협력? 비대칭 대전?)
- **핵심 판타지**: TBD
- **1회 플레이 길이**: TBD

**확정된 것**: 1인칭 · 최대 4인 · Steam PC · 호스트 리슨 서버 · 집 내부 맵

## 2. 승리 / 패배 조건

> **TBD — 사용자 입력 필요.** 이 항목이 정해지기 전까지 아래가 막힌다.
> `Result` 씬 내용 · 라운드 구조 · 난입 정책([ADR-0012](../architecture/decisions/ADR-0012-room-code-and-lobby-visibility.md) §4) · 스폰 규칙

| 진영·역할 | 승리 조건 | 패배 조건 |
| --- | --- | --- |
| TBD | TBD | TBD |

## 3. 플레이어

### 3.1 이동 / 조작

`Assets/InputSystem_Actions.inputactions`의 액션 이름 기준.

| 액션 | 바인딩 | 용도 |
| --- | --- | --- |
| `Move` | WASD / 좌스틱 | 수평 이동 |
| `Look` | Mouse Delta / 우스틱 | 시점 |
| `Jump` | Space | 점프 |
| `Attack` | 마우스 좌클릭 (**Press And Release**) | 가구 잡기/던지기 |
| `Interact` | E | 문 여닫기 (조준선 2.5m 안의 문) |

> `Attack`은 홀드 방식이므로 Interaction을 `Press`로 두고 `started`/`canceled`를 각각 잡는다.
> `Hold` Interaction을 붙이면 최소 유지 시간 임계값이 생겨 짧은 탭이 씹힌다.

상세: [../architecture/player-controller.md](../architecture/player-controller.md)

### 3.2 능력 / 상태

| 능력 | 효과 | 쿨다운 | 네트워크 권위 |
| --- | --- | --- | --- |
| 가구 잡기/던지기 | 홀드 중 부양, 해제 시 발사 | `relaunchLockDuration` 2.0s | **Server** |
| 문 여닫기 | 열림/닫힘 토글 | 없음 | **Server** (거리 검증) |
| 이동·점프 | — | — | **Owner** ([ADR-0008](../architecture/decisions/ADR-0008-owner-authoritative-player-movement.md)) |

## 4. 게임 모드 · 세션 흐름

```
[ 타이틀 ] → [ 로비 ] → [ 매치 ] → [ 결과 ] → 로비
```

| 단계 | 설명 | 씬 | 네트워크 상태 |
| --- | --- | --- | --- |
| 타이틀 | 방 생성(6자리 코드 발급), 방 코드 참가, 설정, 종료 | `Title` | 미접속 |
| 로비 | 방 코드·멤버 목록·준비·시작. 최대 4명 | `Lobby` | Steam Lobby만 (NGO 세션 없음) |
| 매치 | House_01 맵. 가구 던지기 | `Game` | Host/Client 연결됨 |
| 결과 | **TBD** — 승패 조건 확정 후 | `Result` | 정산 후 해산 |

**세션 시작 순서는 MUST `씬 로드 → StartHost → 로비 시작 신호`다.**
로비에서 바로 `StartHost` 하면 스폰 지점 없는 씬에 스폰되고, 신호를 먼저 보내면
게스트가 세션 없는 호스트에 접속한다.

- 방 코드: 6자리 커스텀 코드 (32자 문자셋, 혼동 글자 제외)
- 로비 가시성·난입: [ADR-0012](../architecture/decisions/ADR-0012-room-code-and-lobby-visibility.md) — **결정 대기**

## 5. AI / 유령

> **TBD.** 프로젝트명이 GhostHunter이지만 유령·적·AI는 아직 설계되지 않았다.
> 프로토타입 단계에서는 명시적 범위 밖이었다.

| 개체 | 행동 | 의사결정 방식 | 권위 |
| --- | --- | --- | --- |
| TBD | TBD | FSM / BT / NavMesh | Server only |

**결정이 필요한 것**: 유령이 플레이어인가 AI인가. 비대칭 대전인가 협력 PvE인가.
이 답이 §2(승패 조건)와 직결된다.

## 6. 레벨 / 맵

| 맵 | 크기 | 특징 | 상태 |
| --- | --- | --- | --- |
| `House_01` | 도면 ×2 (평면만) | 침실 2 + 거실 + 주방 + 욕실 + 창고, 문 5개 | 구현됨 |
| `House_01_OriginalScale_Right` | ×1 | 도면 배율 비교용. 집 동쪽 3m | 검증 전용 |

- 방 프리셋 3종(A·B·C) 중 2개를 세션 시작 시 서버가 중복 없이 뽑아 침실 슬롯에 배치한다.
- 가구는 씬에 배치된 실제 `NetworkObject`다. 더미 큐브를 스폰하지 않는다.

상세: [../architecture/map-generation.md](../architecture/map-generation.md)

## 7. 밸런스 수치

> 밸런스 수치는 **하드코딩하지 않는다.** ScriptableObject(`Assets/Settings/Gameplay/`)로 관리하고,
> 이 표는 그 SO의 기준값을 기록한다. 규약은 [../conventions/unity-assets.md](../conventions/unity-assets.md).

### 7.1 이동 (`PlayerMoveSettings`)

| 항목 | 초기값 |
| --- | --- |
| 이동 속도 | 5.0 m/s |
| 공중 제어 계수 | 0.4 |
| 점프 높이 | 1.2 m |
| 중력 | -20 m/s² (실제보다 무겁게 — 체감이 좋다) |
| 마우스 감도 | 0.1 (deg per pixel) |
| 캡슐 높이 / 반지름 | 1.8 m / 0.35 m |
| 카메라 높이 | 1.65 m |

### 7.2 던지기 (`FurnitureThrowSettings`)

| 항목 | 초기값 | 메모 |
| --- | --- | --- |
| `maxTargetDistance` | 12 m | 조준 사거리 |
| `maxHoldDistance` | 15 m | 초과 시 강제 해제 |
| `hoverDistance` | 3 m | 조준점 앞 부양 거리 |
| `springStiffness` | 60 | 낮을수록 흐물흐물 |
| `springDamping` | 8 | 낮으면 진동, 높으면 뻣뻣 |
| `angularDamping` | 0.9 | 프레임당 각속도 감쇠 |
| `chargeTime` | 1.0 s | 0 → 최대 차지 |
| `minChargeRatio` | 0.4 | 차지 0에서도 이만큼은 나감 |
| `oneHolderForce` | 30 | 1인 최대 발사 속도 변화량 |
| `twoHolderForce` | 50 | 2인 최대 발사 속도 변화량 |
| `heavySoloMultiplier` | 0.5 | heavy를 혼자 던질 때 |
| `torqueScale` | 2.0 | 회전하며 날아가는 정도 |
| `relaunchLockDuration` | 2.0 s | `Launched` 상태 최대 유지 |
| `launchOnFirstRelease` | false | 2인 중 1인 해제 정책 토글 |
| 발사각 보정 | 수평 기준 +20° ~ +70° | 아래로 박히거나 수직으로 뜨지 않게 |

> **이 값들은 로컬(UTP) 기준으로 튜닝되었다.** 서버 권위 물리라 원격 접속에서는 왕복 지연이 실린다.
> **MUST 실제 Steam 원격 접속에서 재튜닝한다** → [ADR-0010](../architecture/decisions/ADR-0010-server-authoritative-furniture-physics.md)

### 7.3 상호작용

| 항목 | 초기값 |
| --- | --- |
| 문 조준 사거리 | 2.5 m |
| 문 상호작용 최대 거리(서버 검증) | 4 m |
| 가구당 홀더 슬롯 | 2 |

## 8. UI / UX

| 화면 | 요소 | 상태 |
| --- | --- | --- |
| `Title` | 방 생성, 방 코드 참가, 설정, 종료, Steam 상태 메시지 | 구현됨 (`MainMenuController`) |
| `Lobby` | 방 코드·복사·초대, 멤버 4슬롯(아바타·닉네임·준비), 준비/시작/나가기 | 구현됨 (`LobbyController`) |
| `Game` HUD | 크로스헤어(조준 대상 유무), 차지 게이지(1인 준비/2인 잡기) | 구현됨 |
| `Game` 윤곽선 | 조준 중 / 내가 홀드 / 남이 홀드 / 2인 홀드 색 구분 | 구현됨 |
| `Result` | TBD | 미구현 |
| 개발 HUD (F1) | 접속·트랜스포트 전환·가구 리셋(R) | 구현됨 (릴리스에서 제외 예정) |

## 9. 오디오

> **TBD.** 프로토타입 범위 밖이었다.

| 상황 | 사운드 | 비고 |
| --- | --- | --- |
| TBD | TBD | TBD |

## 10. 아트 방향

> **TBD.** 현재는 전부 프리미티브 + 단색 머티리얼이다.

- 스타일: TBD
- 카메라: 1인칭 고정, 피치 클램프 ±89°
- 라이팅: URP Volume Profile (`Assets/Settings/`) — 세부 TBD

## 11. 용어

| 용어 | 정의 | 코드상 표기 |
| --- | --- | --- |
| 홀드(Hold) | 던지기 버튼을 누르고 있는 상태. 가구가 허공에 부양한다 | `Attack` started~canceled |
| 흡착(Attach) | 특정 가구의 홀더 슬롯을 점유한 상태. 가구당 최대 2슬롯 | `FurnitureGrabTarget.holders` |
| 부양(Hover) | 홀드 중 가구가 중력을 무시하고 조준점 앞에 떠 있는 상태 | `FurnitureHoverMotor` |
| 발사(Launch) | 입력 해제로 가구에 속도 변화가 적용되어 날아가는 순간 | `FurnitureLauncher` |
| 투척 준비(ThrowReady) | 1명만 홀드 중. 잡히거나 부양되지 않음 | `FurnitureState.ThrowReady` |
| 방 슬롯(Room Slot) | 프리셋이 들어갈 자리. 현재 침실 2칸 | `House_01/RoomSlots` |
| 방 프리셋(Room Preset) | 슬롯에 채워지는 가구 묶음. A·B·C 중 2개 선택 | `Room_Presets` |

---

## 부록 A. 결정 대기 목록

에이전트가 작업 중 마주치면 **여기에 추가하고 사용자에게 질문**한다.

| # | 질문 | 막히는 작업 | 상태 |
| --- | --- | --- | --- |
| 1 | **게임 루프와 승패 조건은?** | `Result` 씬, 라운드 구조, 난입 정책, 점수 시스템 | **대기** |
| 2 | **유령은 플레이어인가 AI인가?** | AI 설계, 비대칭 여부, 스폰 규칙 | **대기** |
| 3 | 로비 가시성 — Public + 접속 승인 재검증 / FriendsOnly + 초대 전용 | 방 코드 기능 존치 | **대기** → [ADR-0012](../architecture/decisions/ADR-0012-room-code-and-lobby-visibility.md) |
| 4 | 매치 중 난입을 허용하는가 | `SetJoinable` 정책, 스폰 안전성 | **대기** → [ADR-0012](../architecture/decisions/ADR-0012-room-code-and-lobby-visibility.md) |
| 5 | 로컬(UTP) 개발 경로 존치 방식 | 테스트 전략 | **대기** → [ADR-0011](../architecture/decisions/ADR-0011-local-transport-path.md) |
| 6 | ~~최대 인원~~ | — | ✅ 해결 — 4명 |
| 7 | ~~방 코드 형식~~ | — | ✅ 해결 — 6자리 커스텀 코드 |
| 8 | ~~타깃 플랫폼~~ | — | ✅ 해결 — Steam / Windows + macOS |

---

최종 갱신: 2026-08-19
