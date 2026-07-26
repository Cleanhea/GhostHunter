# 02. 아키텍처

## 권위 모델

**호스트 권위(Host-authoritative)**. 플레이어 한 명이 호스트(서버+클라이언트)로 실행되고, 나머지는 클라이언트로 접속한다.

```
[Client A = Host]                      [Client B]
  ├─ 서버 로직 (물리 시뮬레이션 권위)
  ├─ 로컬 플레이어 입력  ─────┐          ├─ 로컬 플레이어 입력
  └─ 렌더                    │          └─ 렌더
                             │                 │
                        (Steam P2P / FacepunchTransport)
```

원칙:

- **가구 `Rigidbody`는 서버에서만 시뮬레이션된다.** 클라이언트의 가구는 `isKinematic = true`이고 `NetworkTransform`으로 보간되어 따라온다.
- 클라이언트는 "이 가구를 잡고 싶다 / 이 방향으로 놓겠다"는 **의도만** ServerRpc로 보낸다.
- 플레이어 캐릭터 이동은 예외적으로 **소유자 권위(owner-authoritative)** 다. 반응성이 최우선이고, 프로토타입에서 치팅은 문제가 아니다. → [04-player-controller.md](04-player-controller.md)

## 어셈블리 구조

| asmdef | 경로 | 참조 |
|---|---|---|
| `GhostHunter.Runtime` | `Assets/Scripts/` | Unity.InputSystem, Unity.Netcode.Runtime, Facepunch.Steamworks, Netcode.Transports.Facepunch |
| `GhostHunter.Editor` | `Assets/Scripts/Editor/` | `GhostHunter.Runtime` (플랫폼: Editor 전용) |

프로토타입 단계에서 런타임 어셈블리를 더 쪼개지 않는다. 컴파일 시간 문제가 실제로 생기면 그때 분리한다.

## 씬 구성

| 씬 | 역할 |
|---|---|
| `Prototype` | **현재 구현된 통합 진입점.** `NetworkRig`, 접속 HUD, 바닥, 벽, 가구/플레이어 스폰 포인트 |
| `Bootstrap` | 향후 씬 전환이 필요할 때 `NetworkManager`, `SteamLobbyManager`를 분리할 영속 씬 |
| `Lobby` | 향후 개발용 IMGUI HUD를 실제 로비 UI로 교체할 때 추가할 씬 |

현재 프로토타입은 빠른 손맛 검증을 위해 `Prototype` 한 씬에 전부 모았다. 빌드의 시작 씬도
`Prototype`이며, 플레이 후 접속 HUD에서 `Local` / `Host`를 누르면 바로 시작한다.
실제 로비-게임 전환이 필요해질 때 `Bootstrap`과 `Lobby`를 분리하고
`NetworkManager.SceneManager.LoadScene` 경로를 추가한다.

## 런타임 컴포넌트 맵

```
NetworkRig (Bootstrap 씬, DontDestroyOnLoad)        ← 메뉴 "GhostHunter > 네트워크 리그 생성"
├─ NetworkManager
├─ FacepunchTransport       Steam P2P (실제 플레이 경로)
├─ UnityTransport           127.0.0.1 (로컬 테스트 경로)
├─ SteamLobbyManager        Steam 초기화·RunCallbacks·로비 생성/참가/초대
├─ ConnectionManager        StartHost / StartClient, 트랜스포트 전환
└─ ConnectionHud            개발용 IMGUI HUD (F1)

Player 프리팹 (NetworkObject, 플레이어당 1개 스폰)
├─ CharacterController
├─ PlayerInputReader        Input System → 입력 값 노출 (로컬 소유자만 활성)
├─ PlayerMotor              이동/중력/점프. FixedUpdate
├─ PlayerLook               피치/요. 카메라는 로컬 소유자만 활성화
├─ ClientNetworkTransform   소유자 권위 위치/회전 복제
├─ FurnitureTargeter        카메라 레이캐스트 → 현재 조준 대상 (로컬 전용)
└─ GrabController           투척 준비/2인 잡기 입력, Grab/Release ServerRpc 송신

Furniture 프리팹 (NetworkObject, 서버가 스폰)
├─ Rigidbody (서버만 non-kinematic)
├─ BoxCollider
├─ NetworkTransform         서버 권위 복제
├─ FurnitureGrabTarget      홀더 슬롯(NetworkList) 관리, 잡기 가능 여부 판정
├─ FurnitureHoverMotor      서버 전용. 2인 잡기 중에만 스프링 힘 적용
├─ FurnitureLauncher        서버 전용. 발사 속도와 보정 각도 계산·적용
└─ FurnitureOutline         클라이언트 전용. 조준/홀드 상태에 따라 윤곽선 표시

UI (Prototype 씬, 로컬 전용)
├─ CrosshairUI              조준 대상 유무에 따른 상태 변화
└─ ChargeGaugeUI            투척 준비 게이지, 1인 준비/2인 잡기 표시
```

## 데이터 흐름: 잡기 → 던지기

```
[Client B]                          [Host = Server]                    [모든 클라이언트]

FurnitureTargeter
  카메라 레이캐스트
  → 조준 대상 확정
        │
   (버튼 누름)
        │
GrabController
  RequestGrabServerRpc(objId) ─────► FurnitureGrabTarget
                                       거리·슬롯 유효성 검증
                                       holders 슬롯 배정
                                       1명: ThrowReady
                                       2명: Held
                                       (NetworkList 변경)        ─────► 홀더 UI/윤곽선 갱신
                                             │
                                     FurnitureHoverMotor
                                       Held(2명)일 때만:
                                       두 목표점의 평균으로 스프링 힘 적용
                                             │
                                     NetworkTransform            ─────► 가구 위치 보간 표시
        │
   (버튼 뗌)
        │
  RequestReleaseServerRpc(aimDir) ──► FurnitureGrabTarget
                                       홀더 제거
                                       발사 조건 판정
                                             │
                                     FurnitureLauncher
                                       힘 = f(홀더 수, 차지, 무게)
                                       AddForce(VelocityChange)
                                       + 20°~70° 각도 보정
                                             │
                                     OnLaunchedClientRpc         ─────► 이펙트/사운드 훅
```

핵심: **클라이언트는 아무 물리도 건드리지 않는다.** 조준 방향(`Vector3`)과 대상 ID만 보낸다.

## 레이어 / 태그

| 레이어 | 용도 |
|---|---|
| `Default` | 지형, 벽 |
| `Player` | 플레이어 캐릭터 콜라이더 |
| `Furniture` | 던질 수 있는 가구. **타겟팅 레이캐스트의 마스크** |
| `Ignore Raycast` | 조준을 막으면 안 되는 것 |

레이어 인덱스를 코드에 하드코딩하지 않는다. `Core/GameLayers.cs`에 `LayerMask.NameToLayer` 결과를 캐싱해 노출한다.

## 설정 에셋 (ScriptableObject)

튜닝 수치는 전부 여기에 모은다. 코드 재컴파일 없이 플레이 중 조정하기 위해서다.

| 에셋 | 담는 값 |
|---|---|
| `PlayerMoveSettings` | 이동 속도, 가속, 점프 높이, 중력 배수, 마우스 감도 |
| `FurnitureThrowSettings` | 부양 거리/강성/댐핑, 차지 시간, 1인/2인 발사 속도, 최대 사거리 |
| `FurnitureDefinition` | 가구 종류별 질량, 무게 등급(1인/2인), 기본 프리팹 참조 |

경로: `Assets/Settings/Gameplay/`
