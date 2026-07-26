# 06. 가구 오브젝트 · 물리 · 윤곽선

## 가구 프리팹 (`Furniture_Cube`)

프로토타입의 "가구"는 큐브 하나다. 모델링은 하지 않는다.

```
Furniture_Cube  [layer: Furniture]
├─ MeshFilter / MeshRenderer   Cube, 1×1×1 스케일 기준
├─ BoxCollider
├─ Rigidbody
├─ NetworkObject
├─ NetworkTransform            서버 권위, Interpolate 켬
├─ FurnitureGrabTarget         홀더 슬롯, 상태 NetworkVariable
├─ FurnitureHoverMotor         서버 전용
├─ FurnitureLauncher           서버 전용
└─ FurnitureOutline            클라이언트 전용 표시
```

### Rigidbody 설정

| 항목 | 값 | 이유 |
|---|---|---|
| Mass | 8 (light) / 25 (heavy) | `FurnitureDefinition`에서 주입 |
| Drag | 0.05 | 공중에서 부자연스럽게 멈추지 않게 |
| Angular Drag | 0.5 | 무한 회전 방지 |
| Collision Detection | **Continuous Dynamic** | 빠르게 날아가면 기본 Discrete로는 벽을 통과한다 |
| Interpolate | Interpolate | 서버 렌더 부드럽게 |
| Constraints | 없음 | |

### 클라이언트에서의 Rigidbody

`OnNetworkSpawn`에서:

```
if (!IsServer) {
    rb.isKinematic = true;
    rb.detectCollisions = true;    // 물리는 끄되 로컬 조준 레이캐스트는 허용
}
```

물리는 서버에서만 돈다. 클라이언트 가구는 `NetworkTransform` 보간으로 움직이는 껍데기다.

> 대가: 클라이언트에서 충돌이 약간 늦게 보인다. 프로토타입에서는 허용한다. 체감이 나쁘면 그때 로컬 충돌 예측을 검토한다.

## 가구 간 물리 효과

별도 코드가 필요 없다 — 서버에서 PhysX가 전부 처리하고 결과 트랜스폼이 복제된다.

주의할 점만:

- 날아간 가구가 `Idle` 상태 가구를 치면, 맞은 쪽은 그냥 물리로 밀린다. **맞은 쪽을 `Launched`로 전환하지 않는다** (재흡착 금지 규칙이 엉뚱하게 걸린다).
- 여러 가구가 한 프레임에 연쇄 충돌하면 `NetworkTransform` 대역폭이 튄다. 프로토타입 씬의 가구 수를 **10개 이하**로 유지한다.
- 가구가 겹쳐 스폰되면 폭발한다. 스폰 포인트를 충분히 띄운다.

## 씬 배치 (`Prototype`)

| 오브젝트 | 사양 |
|---|---|
| `Floor` | 20×20 평면 또는 `Scale(20, 1, 20)` 큐브. layer `Default` |
| `Wall` | `Scale(8, 4, 0.5)` 큐브. 던진 가구가 부딪히는 기준물. 정적 콜라이더 |
| `FurnitureSpawnPoints` | 빈 오브젝트 5~8개. 서버가 여기에 가구 스폰 |
| `PlayerSpawnPoints` | 빈 오브젝트 2개 |

벽과 바닥은 `Static` 체크. `Rigidbody` 없음.

`DevSpawner`(서버 전용)가 게임 시작 시 스폰 포인트마다 `Furniture_Cube`를 `NetworkObject.Spawn()` 한다. 개발 편의를 위해 키(예: `R`)로 전체 리스폰하는 디버그 기능을 넣는다 — 가구를 다 던져놓고 매번 재시작하는 건 시간 낭비다.

## 윤곽선 (아웃라인)

### 방식 선택

| 방식 | 장점 | 단점 | 판정 |
|---|---|---|---|
| **A. 백페이스 확장 셰이더** | 구현 30분, 오브젝트 단위 On/Off 쉬움 | 굴곡진 메시에서 끊김 | **채택** — 대상이 큐브다 |
| B. URP Renderer Feature + Stencil | 깔끔한 결과, 오클루전 처리 가능 | 렌더러 에셋 수정, 레이어 관리 필요 | 나중에 |
| C. Full Screen Pass (URP 17) | 최고 품질 | 프로토타입에 과함 | 안 함 |

### A 방식 구현

`Assets/Shaders/FurnitureOutline.shader`의 간단한 URP HLSL 셰이더 하나:

- `Cull Front` (뒷면만 렌더)
- `ZWrite Off`, Render Queue = Transparent
- 버텍스를 노멀 방향으로 `_OutlineWidth`만큼 밀어냄
- Unlit, `_OutlineColor` 방출

가구 프리팹의 `OutlineShell` 자식 렌더러가 이 머티리얼을 공유한다.
`FurnitureOutline`은 렌더러 On/Off와 `MaterialPropertyBlock` 색만 바꾸므로 공유 머티리얼과
인스턴싱을 깨뜨리지 않는다.

### 상태별 색

| 상태 | 색 | 조건 |
|---|---|---|
| 없음 | — | 조준하지 않음 |
| 조준 중 (가능) | 흰색 | 크로스헤어가 겹치고 잡을 수 있음 |
| 조준 중 (불가) | 회색 | 슬롯 full 또는 `Launched` |
| 내가 투척 준비 중 | 청록색 | 1명일 때 가구 자체는 잡히지 않음 |
| 다른 사람이 투척 준비 중 | 주황색 | 파트너가 무엇을 누르고 있는지 보여야 협력이 성립 |
| 2인 홀드 중 | 노란색 (또는 깜빡임) | 강화 상태임을 알림 |

홀드 상태 색은 **모든 클라이언트**가 봐야 한다. `FurnitureGrabTarget`의 `NetworkList<ulong> _holders` 변경 이벤트를 구독해 갱신한다.

조준 상태 색은 로컬 전용이다 (내가 조준한 것만 나에게 보임).

## 가구 정의 (`FurnitureDefinition`)

프로토타입에서는 2종만:

| 이름 | 질량 | 등급 | 크기 |
|---|---|---|---|
| `Furniture_Light_Cube` | 8 | light | 1×1×1 |
| `Furniture_Heavy_Cube` | 25 | heavy | 1.5×1.5×1.5 |

heavy는 혼자 던지면 `heavySoloMultiplier`만큼 약해진다 ([05-throw-system.md](05-throw-system.md) 참조). 2인 흡착의 존재 이유를 만들어주는 유일한 장치이므로, 두 등급의 체감 차이가 확실히 나도록 튜닝한다.
