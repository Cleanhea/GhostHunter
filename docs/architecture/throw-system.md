# 05. 던지기 시스템

프로토타입의 핵심. 레퍼런스: 오버워치 루시우의 밀쳐내기 — https://www.youtube.com/shorts/0ZCbaVelhp8

## 요구사항 재정리

| 요구 | 해석 |
|---|---|
| "루시우처럼 허공에 뜨게 던지기" | 조준 피치를 따르되 발사각을 수평 기준 **+20°~+70°**로 보정 |
| "밀치기 느낌" | 1명은 가구를 붙잡거나 옮기지 않고 **버튼을 뗄 때 쳐서 날린다** |
| "홀드 방식" | 1명 입력은 투척 준비, 2명이 같은 가구를 누르고 있을 때만 잡기·부양 |
| "가구를 바라봐야 됨" | 크로스헤어 레이캐스트 타겟팅 |
| "범위 X, 타겟팅으로" | 구형 범위 판정 안 씀. 정확히 조준한 하나만 |
| "가구 윤곽선 표시" | 조준 중 / 홀드 중 상태를 윤곽선 색으로 구분 |
| "2명 흡착, 한 명이 흡착 안 하면 던져지게" | 아래 "2인 흡착 규칙" 참조 |

## 상태 머신 (가구 기준)

```
        ┌──────────────────────────────────────────┐
        │                                          │
        ▼                                          │
   ┌─────────┐  첫 입력   ┌────────────┐  두 번째 입력  ┌────────┐
   │  Idle   │──────────►│ ThrowReady │──────────────►│  Held  │
   │ (지면)  │           │ (투척 준비) │◄──────────────│ (2인 잡기)│
   └─────────┘           └────────────┘  한 명 해제    └────────┘
        ▲                      │                             │
        │                      └──── 마지막 1명 해제 ────────┤
        │                                                    ▼
        │                                              ┌──────────┐
        └──────────────────────────────────────────────│ Launched │
                    감속 또는 제한 시간 경과             │  (비행)  │
                                                       └──────────┘
        └──────────────────────────────────────────────────────────┘
                          속도가 임계값 이하로 감소
```

- `Idle` — 중력 O, 일반 강체.
- `ThrowReady` — 홀더 1명. 중력 O, 일반 강체 상태를 유지하며 버튼 해제 시 발사만 준비한다.
- `Held` — 홀더가 정확히 2명일 때만 진입. 중력 X, `FurnitureHoverMotor`가 두 목표점의 중간으로 스프링 힘을 적용한다.
- `Launched` — 중력 O, 발사 속도가 적용된 직후. 이 상태에서는 **재흡착 금지** (연속 잡기로 무한 부스팅하는 걸 막는다). 속도가 `0.5 m/s` 이하가 되거나 `2초` 경과하면 `Idle`로 복귀.

상태는 서버가 소유하고 `NetworkVariable<FurnitureState>`로 복제한다.

## 흐름

### 1. 타겟팅 (클라이언트 로컬)

`FurnitureTargeter`가 매 `Update`에:

```
Physics.Raycast(camera.position, camera.forward, out hit,
                maxTargetDistance, GameLayers.FurnitureMask)
```

- `maxTargetDistance` 초기값 **12 m**
- 히트한 `FurnitureGrabTarget`이 이전 프레임과 다르면 이전 대상 윤곽선 OFF, 새 대상 ON
- 이미 입력 중이면 타겟팅을 멈춘다 (투척/잡기 대상 고정)
- 대상이 `Launched` 상태거나 슬롯이 꽉 찼으면 윤곽선을 "불가" 색으로

### 2. 상호작용 시작

버튼 누름 → `GrabController.RequestGrabServerRpc(networkObjectId)`

서버 검증 (실패 시 조용히 무시, 클라이언트에는 상태 복제로 자연히 반영됨):

- [ ] 해당 NetworkObject가 존재하고 `FurnitureGrabTarget`을 가지는가
- [ ] 상태가 `Idle` 또는 `Held`인가 (`Launched` 거부)
- [ ] 홀더 슬롯에 여유가 있는가 (최대 2)
- [ ] 요청자가 이미 다른 가구를 잡고 있지 않은가 (1인 1가구)
- [ ] 요청자와 가구의 거리 ≤ `maxTargetDistance * 1.2` (지연 보정 여유)

통과하면 `NetworkList<ulong> _holders`에 클라이언트 ID를 추가한다. 첫 번째 ID만 있으면
`ThrowReady`, 두 번째 ID가 추가되면 `Held`로 전환한다. 이 리스트 변경이 모든 클라이언트에
복제되어 UI/윤곽선을 갱신한다.

### 3. 2인 잡기·부양 (서버, FixedUpdate)

`FurnitureHoverMotor`:

```
목표점(홀더 2명) = 두 홀더 목표점의 중점

Δ    = 목표점 - rb.position
force = Δ * springStiffness - rb.velocity * springDamping
rb.AddForce(force, ForceMode.Acceleration)
rb.angularVelocity *= angularDamping
```

- `ForceMode.Acceleration`을 쓰면 질량에 무관하게 같은 속도로 따라온다. 무거운 가구가 굼뜨게 따라오길 원하면 `ForceMode.Force`로 바꾼다. **초기값은 `Force`** — 무게 차이가 느껴지는 편이 낫다.
- 스프링이 목표를 정확히 추종하면 "붙잡은" 느낌이 되어 밀치기 감각이 사라진다. **일부러 느슨하게** (낮은 강성 + 적당한 댐핑) 두어 살짝 뒤처지며 흔들리게 한다.
- 홀더가 대상 가구에서 `maxHoldDistance`(초기값 15 m) 이상 멀어지면 강제로 입력 해제.
- 서버는 홀더의 카메라 위치/방향을 알아야 한다. 홀드 중인 클라이언트만 `UpdateAimServerRpc(origin, direction)`를 **네트워크 틱 주기로** 보낸다 (매 프레임 아님).

### 4. 차징

첫 입력부터 `chargeTime`(초기값 1.0초)까지 `charge`가 0 → 1로 선형 증가. 서버가 계산하고 `NetworkVariable<float>`로 복제(UI용).

차지가 최대에 도달하면 계속 유지될 뿐 자동 발사하지 않는다.

### 5. 발사

버튼 뗌 → `RequestReleaseServerRpc(aimDirection)`

`FurnitureLauncher`가 힘을 계산한다:

```
holderCount  = 발사 시점의 홀더 수 (1 또는 2)
baseForce    = holderCount == 2 ? twoHolderForce : oneHolderForce
weightPenalty= (가구가 heavy이고 holderCount == 1) ? heavySoloMultiplier : 1.0

launchSpeed = (baseForce * (minChargeRatio + charge * (1 - minChargeRatio))) * weightPenalty

aimAngle    = atan2(aimDirection.y, length(aimDirection.xz))
launchAngle = clamp(aimAngle, minLaunchAngle, maxLaunchAngle)
dir         = horizontal(aimDirection) * cos(launchAngle)
              + Vector3.up * sin(launchAngle)

rb.useGravity = true
rb.AddForce(dir * launchSpeed, ForceMode.VelocityChange)
rb.AddTorque(random torque * torqueScale, ForceMode.Impulse)
```

발사각은 수평선 기준 **최소 +20°, 최대 +70°**다. 아래를 보고 던져도 바닥에 즉시
박히지 않고 20°로 보정되며, 지나치게 위를 봐도 70°를 넘지 않는다. 그 사이에서는
마우스 조준 피치를 그대로 따른다.

`VelocityChange`를 사용해 가구 질량으로 발사 속도가 다시 나뉘지 않게 한다. light/heavy의
차이는 충돌 모멘텀과 `heavySoloMultiplier`로 유지한다.

발사 후 `OnLaunchedClientRpc(dir, magnitude)`로 이펙트/사운드 훅을 남긴다 (프로토타입에서는 비워둠).

## 2인 흡착 규칙

> 원문: "혼자 던지기, 2명 흡착 (단, 한명이 흡착을 안하면 던져지게 만들어주세요)"

해석: **2인 협력은 강화 옵션이지 필수 조건이 아니다.** 파트너가 붙지 않아도 혼자 던질 수 있어야 하고, 파트너를 기다리며 멈춰 있으면 안 된다.

| 상황 | 동작 |
|---|---|
| 1명 입력 유지 | `ThrowReady`. 가구는 잡히거나 부양되지 않음 |
| 1명 입력 → 해제 | **즉시 밀기/발사.** 1인 힘 |
| 2명 입력 유지 | `Held`. 두 명이 누르고 있는 동안에만 잡기·부양 |
| 2명 중 1명만 해제 | **발사하지 않음.** 잡기를 끝내고 남은 1명의 `ThrowReady`로 전환 |
| 남은 1명도 해제 | **발사.** 발사 시점에는 1명이므로 1인 힘 |
| heavy 가구를 1명이 입력 → 해제 | 발사되긴 하되 `heavySoloMultiplier`(초기값 0.5)만큼 약하게 |
| 홀더 전원이 사거리 이탈/접속 종료 | 발사 없이 `Idle`로 낙하 |

"2명 잡기 → 1명만 해제"에서는 2인 잡기 조건이 깨졌으므로 즉시 중력을 복구한다. 남은 사람은
가구를 붙잡지 않으며, 자신의 버튼을 놓을 때만 1인 힘으로 던진다.

`FurnitureThrowSettings`에 `bool launchOnFirstRelease` 토글을 두어 플레이테스트에서 반대 정책도 즉시 비교할 수 있게 한다.

## 초기 파라미터

`FurnitureThrowSettings` (ScriptableObject):

| 값 | 초기값 | 메모 |
|---|---|---|
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

발사각 20°~70°는 플레이어 조준의 안전 범위로 고정된 게임 규칙이며 설정 에셋의 튜닝값으로
노출하지 않는다.

## 튜닝 순서 (플레이테스트)

값이 너무 많아 한꺼번에 만지면 길을 잃는다. 이 순서로 하나씩:

1. 20°~70° 고정 발사각 — 날아가는 궤적이 기분 좋은가
2. `oneHolderForce` — 혼자 던졌을 때 벽까지 닿는가
3. `hoverDistance` + `springStiffness` — 홀드 중 시야를 가리지 않는가
4. `chargeTime` — 홀드가 지루하지 않은가
5. `twoHolderForce` — 2인이 확실히 더 강하다고 느껴지는가

## 미결정 사항

> **미결정:** 두 홀더의 조준 방향이 정반대일 때 평균 벡터가 0에 가까워진다. 현재 안은 "그 경우 크기를 그대로 쓰되 방향은 첫 홀더 기준"이지만, 각도 차이에 따라 힘을 감쇠시키는 편(협력 보상)이 더 나을 수 있다. 실제로 그런 상황이 자주 나오는지 먼저 확인할 것.

> **미결정:** 홀드 중인 가구가 다른 플레이어나 벽에 끼었을 때의 처리. 우선은 스프링에 맡기고, 실제로 문제가 되면 "일정 시간 목표점 도달 실패 시 자동 해제"를 넣는다.

---

최종 갱신: 2026-08-19
