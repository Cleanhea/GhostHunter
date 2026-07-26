# 04. 플레이어 컨트롤러 (1인칭 + 동기화)

## 구성

| 컴포넌트 | 실행 위치 | 역할 |
|---|---|---|
| `PlayerInputReader` | 로컬 소유자만 | Input System 액션 → 값 노출. 다른 로직은 이걸 읽기만 함 |
| `PlayerLook` | 로컬 소유자만 | 마우스 델타 → 요(몸통 Y 회전) / 피치(카메라 X 회전) |
| `PlayerMotor` | 로컬 소유자만 | `CharacterController`로 이동·중력·점프 |
| `ClientNetworkTransform` | 전원 | 소유자가 쓰고 나머지가 읽는 트랜스폼 복제 |
| `PlayerVisuals` | 전원 | 원격 플레이어 몸통 표시, 로컬은 숨김 |

## 이동 방식: CharacterController

`Rigidbody`가 아니라 `CharacterController`를 쓴다.

- 프로토타입 이동에 필요한 건 예측 가능한 캡슐 이동이지 물리 상호작용이 아니다.
- 플레이어가 가구를 몸으로 밀어 물리를 흔드는 건 지금 원하는 게 아니다 (던지기만 물리로 다룬다).
- 나중에 "가구에 부딪히면 밀린다"가 필요해지면 `OnControllerColliderHit`에서 명시적으로 처리한다.

## 입력 매핑

`Assets/InputSystem_Actions.inputactions`의 기본 액션을 재사용한다.

| 액션 | 바인딩 | 용도 |
|---|---|---|
| `Move` | WASD / 좌스틱 | 수평 이동 |
| `Look` | Mouse Delta / 우스틱 | 시점 |
| `Jump` | Space | 점프 |
| `Attack` | 마우스 좌클릭 (**Hold 아님, press/release 둘 다 필요**) | 가구 잡기/던지기 |
| `Interact` | E | 예비 |

> **주의:** `Attack` 액션은 홀드 방식이므로 Interaction을 `Press`(Trigger Behavior: `Press And Release`)로 두고 `started`/`canceled` 콜백을 각각 잡는다. `Hold` Interaction을 붙이면 최소 유지 시간 임계값이 생겨 짧은 탭이 씹힌다.

로컬 소유자가 아닌 플레이어 오브젝트에서는 `PlayerInput`을 **비활성화**한다. 안 그러면 원격 플레이어가 내 입력으로 움직인다.

## 시점 처리

```
Player (root)          ← 요(Y) 회전. ClientNetworkTransform이 복제
└─ CameraPivot         ← 피치(X) 회전. 로컬 전용, 복제 안 함
   └─ Main Camera      ← 로컬 소유자만 enabled = true
```

- 피치는 `[-89°, +89°]`로 클램프.
- 피치는 네트워크로 복제하지 않는다 — 단, **던지기 방향은 피치에 의존**하므로 발사 시 조준 방향 벡터를 RPC 파라미터로 함께 보낸다. 원격 플레이어의 머리 각도를 시각적으로 보여줄 필요가 생기면 그때 `NetworkVariable<float> pitch`를 추가한다.
- 커서: 플레이 중 `Cursor.lockState = CursorLockMode.Locked`. **에디터에서 빠져나올 수 없어 답답하므로 Esc로 해제하는 토글을 처음부터 넣는다.**

## 네트워크 동기화 방침

플레이어 이동만은 **소유자 권위**다:

- 소유자 클라이언트가 자기 트랜스폼을 직접 쓰고, `ClientNetworkTransform`이 그 값을 서버 → 다른 클라이언트로 중계한다.
- 이유: 서버 권위 + 예측/보정은 프로토타입에서 투자 대비 효과가 나쁘다. 지금 검증하려는 건 던지기 손맛이지 이동 정확도가 아니다.
- 대가: 이동에 관한 한 클라이언트를 신뢰한다. 프로토타입에서는 허용 가능한 리스크다.

`ClientNetworkTransform`은 NGO 샘플/커뮤니티 구현을 가져오거나, `NetworkTransform`을 상속해 `OnIsServerAuthoritative() => false`만 오버라이드하면 된다.

동기화 설정:
- Position: X/Y/Z 동기화, 임계값 0.01
- Rotation: **Y만** 동기화 (피치는 로컬 카메라 전용)
- Scale: 동기화 끔
- Interpolate: 켬 (원격 플레이어 끊김 방지)

## 스폰

- `NetworkManager`의 Player Prefab에 등록 → 접속 시 자동 스폰.
- 스폰 위치는 `Prototype` 씬의 `SpawnPoint` 오브젝트들에서 `OwnerClientId` 기준으로 골라, 서버가 `OnNetworkSpawn`에서 위치를 지정하고 클라이언트에 알린다.
  - 소유자 권위 트랜스폼이므로 서버가 위치를 직접 써도 다음 틱에 소유자 값으로 덮인다. `TeleportClientRpc(position)`로 소유자에게 지시하는 방식이 안전하다.

## 초기 파라미터

`PlayerMoveSettings` (ScriptableObject):

| 값 | 초기값 |
|---|---|
| 이동 속도 | 5.0 m/s |
| 공중 제어 계수 | 0.4 |
| 점프 높이 | 1.2 m |
| 중력 | -20 m/s² (실제 중력보다 무겁게 — 체감이 좋다) |
| 마우스 감도 | 0.1 (deg per pixel) |
| 캡슐 높이 / 반지름 | 1.8 m / 0.35 m |
| 카메라 높이 | 1.65 m |

전부 플레이테스트로 바뀔 값이다. 코드에 박지 말 것.
