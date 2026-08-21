# 06. 가구 오브젝트 · 물리 · 윤곽선

## 가구 오브젝트 (맵에 배치된 실제 가구)

던질 수 있는 가구는 **맵 가구 그 자체**다. 스폰되는 더미 큐브는 없다 —
침대·옷장·식탁·의자·소파·선반을 그대로 잡고 던진다. **프리팹 에셋의 인스턴스를 씬에 놓은**
`NetworkObject`이므로 런타임 스폰 코드가 필요 없고, 호스트가 시작될 때 한꺼번에 스폰된다
→ [ADR-0009](decisions/ADR-0009-scene-placed-level-objects.md)

### 가구는 프리팹 하나에서 나온다

가구·문 원본은 `Assets/Prefabs/Furniture/` · `Assets/Prefabs/Map/` 에 **종류별로 하나씩**
있고, 맵에 놓인 것은 전부 그 인스턴스다. 원본 목록과 배치는 `FurnitureCatalog` 가 잇는다.

| | 규칙 |
| --- | --- |
| 한 종류 = 한 프리팹 | 같은 이름의 가구가 자리마다 다른 치수를 갖지 않는다 |
| 벽 방향은 **회전으로** 맞춘다 | X·Z 치수를 바꿔 넘기면 같은 이름이 두 가지 모양이 된다 |
| 인스턴스 오버라이드는 위치·회전에 한정 | 그 외 값을 씬에서 바꾸면 프리팹 수정이 반영되지 않는다 |
| 종류를 추가하려면 | `HousePrototypeBuilder.FurnitureKinds()` 에 넣는다. 라이브러리 진열은 자동 |

가구 치수·질량·컴포넌트를 바꿀 때는 **프리팹을 고친다.** 씬 인스턴스를 하나씩 고치면
같은 가구가 자리마다 달라진다.

### 가구 라이브러리에서 복사해 배치한다

생성 도구(`GhostHunter > 프로토타입 게임 생성`)는 **방을 비운 채로** 집을 만든다.
가구는 집 북쪽(z ≈ 9.5) `Furniture_Library`에 **카탈로그 전 종류를 한 개씩 일렬로** 진열해
두고, 방 배치는 이것을 복사해 `House_01/PhysicsFurniture` 아래에 붙여 넣는 방식이다.

- 복사본은 물리·네트워크 배선이 이미 끝나 있다. 붙여 넣고 위치만 잡으면 된다.
- 줄에서의 자리와 간격은 실제 콜라이더 크기로 계산한다. 가구 치수를 바꿔도 줄이 알아서 맞는다.
- 받침 `Library_Ground/Library_Floor`가 없으면 세션 시작과 동시에 전부 허공으로 떨어진다.
- 새로 붙여 넣은 가구는 `FurnitureReset`(R) 목록에 없다. 필요하면 인스펙터에서 직접 넣는다.
- 생성 검증이 "집 안에 물리 가구 0개"를 강제한다. 생성 도구에 방별 가구 좌표를 되살리면
  손으로 한 배치와 겹쳐서 시작하자마자 물리가 폭발한다.
- **침실 2칸은 손대지 않는다.** 세션이 시작되면 방 프리셋이 옮겨 오므로 손으로 놓은 가구와
  겹친다. 침실 배치를 바꾸려면 `Room_Presets`의 프리셋을 고친다
  ([09-map-generation.md](map-generation.md#구현-현황-20260816)).

```
House_01/PhysicsFurniture/<가구>   [layer: Furniture]   ← 프리팹 인스턴스. 루트에만 물리·네트워크 컴포넌트
├─ Rigidbody                   자식 파츠 콜라이더를 컴파운드로 묶는다
├─ NetworkObject               씬 배치(in-scene placed)
├─ NetworkTransform            서버 권위, Interpolate 켬
├─ FurnitureGrabTarget         홀더 슬롯, 상태 NetworkVariable
├─ FurnitureHoverMotor         서버 전용
├─ FurnitureLauncher           서버 전용
├─ FurnitureOutline            클라이언트 전용 표시
└─ 파츠 (Frame, Mattress, Leg_NW, …)
   ├─ MeshFilter / MeshRenderer / BoxCollider
   └─ <파츠>_Outline           윤곽선 셸 (평소 꺼둠)
```

붙박이(주방 카운터·싱크·쿡탑, 욕실 변기·세면대·샤워)는 `Fixtures/` 아래에 정적 콜라이더로
남는다. 던질 수 없고 정적 배칭 대상이며, **아직 프리팹화하지 않았다**
(→ [unity-assets.md §8](../conventions/unity-assets.md)).

> **정적 배칭 주의:** 물리 가구 파츠는 `StaticEditorFlags`를 반드시 0으로 둔다. 배칭된 메시는
> 정점이 월드 좌표로 구워져서, 던지면 콜라이더만 날아가고 그림은 제자리에 남는다.
> 생성 검증이 이걸 막는다.

> **시작 상태:** 씬에 저장된 `Rigidbody`는 `isKinematic = true`다. 세션 시작 전(플레이 직후,
> 접속 전)에도 물리가 돌면 클라이언트마다 가구가 다른 자리에 멈춘다.
> `FurnitureNetworkPhysics`가 스폰 시 **서버에서만** 푼다.

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
- 여러 가구가 한 프레임에 연쇄 충돌하면 `NetworkTransform` 대역폭이 튄다. 라이브러리 21종 +
  손으로 배치한 가구가 전부 스폰되는데, 가만히 있는 가구는 아무것도 보내지 않으므로
  평상시 비용은 없다. 한 방에서 대량으로
  연쇄 충돌시켰을 때 대역폭이 문제가 되면 그때 방 단위 관심 영역(관측자 필터)을 검토한다.
- **가구가 겹친 채로 시작하면 폭발한다.** PhysX가 서로 밀어내면서 세션 시작과 동시에 가구가
  튀어나가는데, 눈으로는 원인을 알기 어렵다. 그래서 생성 시점에 파츠 콜라이더 단위로 겹침을
  검사하고(`ValidateFurnitureClearance`), 도면 좌표가 떠 있으면 지지면에 정확히 얹는다
  (`RestOnSupport`).

## 씬 배치 (`Prototype`)

| 오브젝트 | 사양 |
|---|---|
| `House_01/Rooms_Fixed` | 방별 바닥. 정적 콜라이더 |
| `House_01/Walls_Doors_Windows` | 외벽·내벽·창, 여닫이 문 5개 |
| `House_01/Fixtures` | 붙박이(카운터·위생도기). 정적 콜라이더 |
| `House_01/PhysicsFurniture` | 손으로 배치한 가구가 들어갈 자리. **생성 직후에는 비어 있다** |
| `House_01/RoomSlots` | 침실 슬롯 2개. 방 중심(바닥면), 회전 없음 |
| `Furniture_Library/Items` | **던질 수 있는 가구 21종**을 종류별 하나씩 일렬로. 복붙용 원본 |
| `Furniture_Library/Ground` | 라이브러리 받침 바닥 |
| `Room_Presets/BedroomPreset_A·B·C` | 침실 프리셋 3종(집 남쪽 바깥). 세션 시작 시 둘이 슬롯으로 간다 |
| `RoomSlotAssigner` | 서버가 프리셋을 중복 없이 뽑아 슬롯에 배치 ([09](map-generation.md)) |
| `House_01_OriginalScale_Right` | 도면 치수 그대로(배율 ×1) 지은 비교용 집. 집 동쪽 3m 옆 |
| `House_01_OriginalScale_Right/PhysicsFurniture` | **여기는 생성 도구가 가구를 깔아 둔다** (28개) |
| `PlayerSpawnPoints` | 빈 오브젝트 2개 |
| `FurnitureReset` | 개발용. 호스트가 `R`을 누르면 가구를 초기 위치로 되돌린다 |

`FurnitureReset`은 라이브러리 21종 + 프리셋 40개 + 비교용 집 28개를 모두 들고 있다.
프리셋이 슬롯으로 옮겨진 **뒤에** `CapturePoses()`가 다시 불려서, `R`은 전시 자리가 아니라
배치된 방으로 되돌린다.

### 도면 배율 비교용 집만 예외로 가구가 깔려 있다

`House_01_OriginalScale_Right`는 "도면 치수(12.8 × 10.4m)에서 사람과 가구가 어떻게 느껴지는가"를
보려고 세워 둔 집이라, 라이브러리에서 복사해 넣을 때까지 비워 둘 이유가 없다. 그래서
생성 도구가 방마다 가구를 깔아 준다 — 침실 2칸(도면 Bedroom_A · Bedroom_C 구성), 주방 식탁,
거실 소파·좌탁·TV장, 창고 선반·상자. **게임플레이용 `House_01`은 지금도 비어 있어야 한다.**

`ValidateFurnishedHouse`가 이 집만 따로 검사한다: 가구 배선·겹침, 문짝이 도는 동안 가구를
쓸지 않는지, 문 개구부와 방 한가운데를 가구가 막지 않는지. 통행 판정은 **가구 레이어만** 본다 —
벽·붙박이는 도면 그대로라 여기서 걸리면 안 되기 때문이다.
`GhostHunter > Place Original Scale House Right` 메뉴로 이 집만 다시 놓을 수도 있고,
그때도 같은 검증과 `R` 목록 재배선이 함께 돈다.

벽·바닥·붙박이는 `Static` 체크. `Rigidbody` 없음.

`FurnitureResetter`는 씬 생성 도구가 가구 목록을 직접 꽂아준다(런타임 탐색 없음). 물리 권위가
서버에 있으므로 호스트에서만 동작하고, 큰 이동이 보간되지 않도록 `NetworkTransform.Teleport`를 쓴다.

## 윤곽선 (아웃라인)

### 방식 선택

| 방식 | 장점 | 단점 | 판정 |
|---|---|---|---|
| **A. 백페이스 확장 셰이더** | 구현 30분, 오브젝트 단위 On/Off 쉬움 | 굴곡진 메시에서 끊김 | **채택** — 대상이 전부 박스다 |
| B. URP Renderer Feature + Stencil | 깔끔한 결과, 오클루전 처리 가능 | 렌더러 에셋 수정, 레이어 관리 필요 | 나중에 |
| C. Full Screen Pass (URP 17) | 최고 품질 | 프로토타입에 과함 | 안 함 |

### A 방식 구현

`Assets/Shaders/FurnitureOutline.shader`의 간단한 URP HLSL 셰이더 하나:

- `Cull Front` (뒷면만 렌더)
- `ZWrite Off`, Render Queue = Transparent
- 버텍스를 노멀 방향으로 `_OutlineWidth`만큼 밀어냄
- Unlit, `_OutlineColor` 방출

가구는 파츠가 여러 개이므로 **파츠마다 `<파츠>_Outline` 셸을 하나씩** 두고
`FurnitureOutline._outlineRenderers`에 전부 꽂는다. 셸은 파츠의 자식이라 스케일·회전을
그대로 물려받는다. `FurnitureOutline`은 렌더러 On/Off와 `MaterialPropertyBlock` 색만 바꾸므로
공유 머티리얼과 인스턴싱을 깨뜨리지 않는다.

> 셸이 파츠의 로컬 스케일을 따르므로, 얇은 파츠(테이블 상판 등)에서는 노멀 확장 폭이 축마다
> 달라 윤곽선 두께가 균일하지 않다. 프로토타입에서는 "어느 가구를 조준했는지" 읽히면 충분해 넘어간다.

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

프로토타입에서는 2종만. 맵 가구가 생성 시점에 둘 중 하나를 물려받는다:

| 에셋 | 질량 | 등급 | 해당 가구 |
|---|---|---|---|
| `FurnitureDefinition_Light` | 8 | light | 의자·스툴·협탁·소형 테이블·TV·창고 상자 |
| `FurnitureDefinition_Heavy` | 25 | heavy | 침대·옷장·서랍장·책상·식탁·소파·선반·콘솔 |

heavy는 혼자 던지면 `heavySoloMultiplier`만큼 약해진다 ([05-throw-system.md](throw-system.md) 참조). 2인 흡착의 존재 이유를 만들어주는 유일한 장치이므로, 두 등급의 체감 차이가 확실히 나도록 튜닝한다.

---

최종 갱신: 2026-08-19
