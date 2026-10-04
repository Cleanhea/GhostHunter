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
- `Held` — 홀더가 정확히 2명일 때만 진입. 중력 X, `FurnitureHoverMotor`가 각 홀더가 잡은 점(손잡이)을 그 사람의 손 목표에 맞추는 **두 손잡이 운반**을 하고, 자세는 두 사람 위치를 따라 돌며 휠로 더 바꾼다(§3·§3.1~3.3, 2026-10-04 변경 — 이전은 2026-09-13 조준점 중간 고정 추종, 그 전은 느슨한 스프링 부양).
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

### 3. 2인 잡기·운반 (서버, FixedUpdate)

> **2026-10-04 변경(사용자 요청 "둘이 들고 옮길 때 조작감이 별로고 번거롭다, 문 같은 데를 잘 통과하게"):**
> 이전 방식(2026-09-13) — 두 홀더의 `조준 원점 + 조준 방향 × hoverDistance(3m)` 중점에 매 스텝 순간 추종,
> 자세는 휠로만 — 의 문제는 ① 3m 지렛대라 시선을 조금만 움직여도 가구가 크게 튀고 ② 20Hz로 오는 조준을 그대로
> 따라가 계단식으로 떨리며 ③ 자세가 고정이라 문 방향으로 돌리려면 휠을 계속 굴려야 하고 ④ 문틀에 걸리면 최대 15m/s로
> 밀며 마찰로 박히는 것이었다. 아래 **두 손잡이 운반**으로 바꿨다. 휠 회전·기울이기(§3.1)는 그대로다.

구현: 네트워크와 무관한 추종 계산은 `FurnitureCarrySession`, 서버 쪽 배선(홀더 확인·손잡이 찾기·마찰·문짝)은
`FurnitureHoverMotor`, 순수 계산은 `FurnitureHeldControl`.

**두 손잡이.** Held 진입 순간 각 홀더의 조준 광선이 닿은 가구 위 점을 그 사람의 **손잡이**로 기억한다(빗나가면 눈에서
가장 가까운 표면 점). 손 목표는 `눈 + 조준 방향 × 잡은 거리`이고, 잡은 거리는 진입 때 눈~손잡이 거리를
`carryMinHandDistance`~`carryMaxHandDistance`(0.9~2.2m)로 자른 값이다 — 진입 순간 가구가 튀지 않는다.

```
손 목표_i  = 눈_i + 조준_i × 잡은거리_i         (carryAimSmoothing 시간 상수로 지수 평활 — 20Hz 계단 제거)
follow    = 진입 때 손잡이 0→1 축을 지금 손 0→1 축으로 돌리는 회전
            (수평 방향은 그대로, 높낮이 차이로 생기는 기울기는 ±carryMaxFollowPitch 25°까지)
목표 자세  = 끼임보조 yaw × follow × HeldRotation(휠 자세)
목표 위치  = 손 목표 중점 − 목표 자세 × 손잡이 중점(바디 로컬) + 끼임보조 옆 이동
```

- 두 사람이 **앞뒤로 서서 걸으면 가구의 손잡이 축(긴 쪽 양 끝을 잡았다면 긴 쪽)이 진행 방향을 향한다** — 문을 지나려고
  휠을 굴릴 필요가 거의 없다. 한 사람이 옆으로 돌아 서면 가구도 그쪽으로 돈다. 손잡이 수평 간격이 `carryFollowMinGripSpan`
  (0.35m)보다 짧으면(작은 가구를 한쪽에서 같이 잡음) 방향을 따르지 않고 이전처럼 평행 이동 + 휠만 쓴다.
- 휠 자세 위에 follow 를 얹으므로 휠로 돌린 만큼은 두 사람 축에 대해 돌아간 채 유지된다(손잡이를 다시 잡은 셈).

**속도 서보.** 힘이 아니라 속도를 직접 주는 건 이전과 같지만, 한 스텝에 목표에 닿는 대신 시간 상수로 줄인다.

```
손 속도    = (손 목표 중점 − 이전 중점) / dt              — 걷는 동안 뒤처지지 않게 미리 싣는다
원하는 속도 = clamp(손 속도 + (목표 위치 − 위치) / carryPositionResponse, heldMaxLinearSpeed)
rb.linearVelocity  = 현재 속도에서 최대 carryMaxAcceleration × dt 만큼만 원하는 속도 쪽으로
rb.angularVelocity = 같은 방식 (carryRotationResponse, heldMaxAngularSpeed, carryMaxAngularAcceleration)
```

- 정상 상태 오차는 0이고(손 속도를 실음), 벽·문틀에 부딪히면 속도 변화량 제한 덕에 덜컹거리지 않는다.
- kinematic이 아닌 동적 바디라 벽·다른 가구에는 막힌다. 질량은 운반 체감에 반영되지 않는다(무게 차이는 1인 투척에만).

**운반 중 물리 손질** (Held 동안만, 끝나면 되돌린다)

- 가구 콜라이더 재질을 마찰 `carryFriction`(0.05)·결합 Minimum 으로 바꾼다 — 문틀 모서리에 비스듬히 닿으면 미끄러져
  비켜 들어간다(시뮬레이션에서 0.05m 어긋남은 보조 없이도 통과).
- §3.3의 충돌 끄기, §3.2의 끼임 보조.

**그대로인 것.** 차징 중 눈~가구 표면 `holdBreakDistance`(4m) 초과 시 그 홀더만 발사 없이 해제(2026-09-13 사용자 요청),
플레이어 오브젝트가 없어진 홀더 해제, 홀드 중인 클라이언트만 `UpdateAimRpc(origin, direction)`를 0.05초 간격으로 보냄,
2명 중 1명이 놓으면 `ThrowReady`로 돌아가 중력이 켜지고 속도가 남는 것.

### 3.1 마우스 휠 회전·기울이기

| 입력 | 액션 | 동작 |
| --- | --- | --- |
| 휠 굴림 | `Player/RotateFurniture` (`<Mouse>/scroll`) | 현재 모드로 한 칸(`wheelStepDegrees`, 15°) |
| 휠 클릭 | `Player/RotateFurnitureMode` (`<Mouse>/middleButton`) | 회전 ↔ 기울이기 전환. 로컬 상태이며 잡기가 끝나면 회전으로 돌아간다 |

- **회전** = 월드 수직축. **기울이기** = 조작한 플레이어 조준의 오른쪽 수평축(그 사람 기준 앞뒤로 기운다).
  **각도 제한 없음** — 계속 굴리면 뒤집힌다.
- **두 홀더 모두 조작하고 입력이 합산된다.** 반대로 굴리면 상쇄된다.
- `Held`에서만 반영한다. 1인 `ThrowReady`에서는 무시한다.
- 휠 값의 크기는 플랫폼·설정마다 달라 **부호만** 쓴다 — 한 프레임에 최대 한 칸. 클라이언트는
  `GrabController.RequestRotateHeldRpc(objectId, ±1, mode)`만 보내고, 서버가 발신자·생존·잡은 대상과
  `FurnitureGrabTarget.ServerRotateHeld`의 홀더·`Held`·한 칸 범위·모드 값을 다시 검증해 목표 자세를 바꾼다.
- 현재 모드는 차지 게이지 라벨에 표시한다(`ChargeGaugeUI`).

> **검증 상태 (2026-09-13):** 컴파일 오류 0, EditMode 248/248(`FurnitureHeldControlTests` 13개 —
> 한 스텝 추종 속도·최대 속력·짧은 쪽 회전·각속력 제한·휠 회전/기울이기 축·제한 없는 한 바퀴),
> PlayMode 39/39(`FurnitureThrowFlowTests` 7개 추가 — Held 진입 시 자세 캡처, 휠 한 칸, 두 홀더 상쇄,
> 1인 거부, 비홀더·범위 밖·잘못된 모드 거부, **차징 중 표면 3.9m 유지(중심 4.4m)·4.1m 발사 없이 해제**).
> 4m 해제의 2인 잡기 경로는 두 번째 홀더에 플레이어 오브젝트가 없어 자동 테스트로 덮지 못했다. `.inputactions` 임포트 후 두 액션과 바인딩을 에셋에서 확인했다.
> **미검증:** 실제 2인 접속에서의 고정 추종 체감·벽 충돌·휠 입력 왕복. 테스트의 두 번째 홀더는 플레이어
> 오브젝트가 없어 물리 스텝을 돌리면 강제 해제되므로(testing.md §4.4) 모터의 실제 추종은 자동 테스트로
> 덮지 못했다. 에디터 단독 Local Host는 플레이어가 1명이라 2인 잡기를 만들 수 없다 — Host + 빌드 Client 또는
> Steam 2PC 수동 확인이 필요하다.

### 3.2 끼임 보조 (2026-10-04)

문틀 끝면에 정면으로 걸리면 마찰을 낮춰도 옆으로 미끄러질 성분이 없다. 그때 작은 자세 보정을 찾아 비켜 준다.

1. **막힘 판정** — 직전 물리 스텝에 옆으로 막는 접촉(법선 |y| < 0.7, 플레이어 제외)이 있었고 목표 위치에서 수평으로
   `squeezeTriggerDistance`(0.1m) 넘게 밀려나 있는 상태가 0.1초 이어지면.
2. **후보** — 회전 ±`squeezeMaxYaw`(40°)를 `squeezeYawStep`(10°) 간격, 옆 이동 ±`squeezeMaxLateral`(0.3m)을
   `squeezeLateralStep`(0.1m) 간격으로 짠 격자(63개)를 보정이 작은 순서로 시험한다(`FurnitureSqueezeAssist`).
3. **채택 조건** (`FurnitureClearanceProbe`) — ① 지금 자세에서 보정 자세까지 도중 자세를 가구 끝 이동 2cm 간격으로
   샘플해 겹침이 지금보다 2mm 넘게 깊어지지 않고 ② 보정 자세가 비어 있으며 ③ 보정 자세로 진행 방향
   `squeezeProbeDistance`(0.12m) 앞이 비어 있어야 한다. 겹침은 `Physics.ComputePenetration`, 바닥·천장처럼 위아래로
   밀어내는 겹침은 무시, 플레이어·귀신·충돌을 끈 콜라이더는 뺀다.
   > ①이 없으면 "도착 자세만 비어 있는" 회전을 고른다 — 문틀에 닿은 앞 모서리는 돌아가는 도중 끝면을 파고들어야 해서 실제로는
   > 못 돈다. 첫 구현이 이 때문에 회전 보정만 쌓이고 멈췄다(시뮬레이션 테스트로 발견).
4. 찾으면 보조값(수직축 회전·옆 이동)에 더하고 0.3초 뒤 다시 본다. 못 찾거나 보정 없이도 비어 있으면 0.5초 쉰다
   (벽에 대고 밀 때 매 스텝 검사하지 않는다). 누적은 각각 ±40°·0.3m 로 자른다.
5. **되돌리기** — 막히지 않은 동안 0.1초마다 보조값을 한 칸씩 0으로 되돌리되, ①과 같은 검사로 비는 경우에만 되돌린다
   (문 안에서 되돌렸다 다시 걸리는 왕복이 없다).

### 3.3 운반 중 충돌 끄기 (2026-10-04)

`FurnitureCollisionIgnoreSet` 이 `Physics.IgnoreCollision` 을 켜고, **다시 켤 때는 서로 떨어질 때까지 기다린다**
(겹친 채 켜면 PhysX가 밀어내며 가구가 튀거나 플레이어가 끼인다. 캐릭터 컨트롤러는 경계 상자로 판정).

| 대상 | 피어 | 조건 | 이유 |
| --- | --- | --- | --- |
| 두 홀더의 플레이어 콜라이더 | **모든 피어**(`FurnitureGrabTarget`) | `Held` 동안. 설정 `carryIgnoresHolders` | 서버에서는 가구가 들고 있는 사람 몸에 막히지 않고, 각 클라이언트에서는 자기 캐릭터가 키네마틱 가구 사본에 막히지 않는다. 상태·홀더 목록이 복제되므로 각 피어가 같은 판단을 한다 |
| 근처 **열린** 문짝 | 서버(`FurnitureHoverMotor`, 0.2초마다 주변 검사) | `Held` 동안. 설정 `carryPassesOpenDoors` | 방 안쪽으로 90° 열린 문짝이 출입구 옆 1m를 막는다. **닫힌 문은 그대로 막는다** — 운반 중 닫히면 떨어지는 대로 충돌이 돌아온다 |

클라이언트는 다른 플레이어 오브젝트를 `GetPlayerNetworkObject` 로 얻을 수 없어(NGO 클라이언트-서버 제약) 홀더가 바뀔 때
스폰 목록에서 찾는다.

> **검증 상태 (2026-10-04):** EditMode — `FurnitureHeldControlTests` 두 손잡이 자세·위치·가속 제한·평활 9개 추가,
> `FurnitureSqueezeAssistTests` 6개(후보 순서, 1.0m 문틀 모형에서 옆으로 0.2m 비키기, 도중에 파고드는 회전 거절,
> 무시 콜라이더, 떨어질 때까지 충돌 복구 대기). PlayMode — `FurnitureCarryDoorwayTests` 5개: 네트워크 없이 두 홀더의
> 눈·조준을 스크립트로 움직이고 `Physics.Simulate` 로 진행 — 1.8m 소파를 가운데로 들고 1.0m 문 통과, **0.35m 비켜 걸어
> 문틀 끝면에 걸리면 보조가 비켜 통과 / 보조를 끄면 같은 상황에서 멈춤(대조군)**, 통과 후 보조 0 복귀, 앞사람이 90° 돌면
> 소파도 돔. 기존 `FurnitureThrowFlowTests` 등 가구 PlayMode 63/63.
> **미검증:** 실제 2인 접속(Host + 빌드 Client 또는 Steam 2PC)의 체감 — 특히 클라이언트에서 보이는 지연(NetworkTransform
> 보간), 실제 가구 프리팹(복합 콜라이더)과 Stage1 문·계단에서의 동작, 끼임 보조 검사 비용(벽에 대고 미는 동안 0.5초마다
> 수 ms 예상, 측정 안 함).

### 4. 차징

첫 입력부터 `chargeTime`(초기값 1.0초)까지 `charge`가 0 → 1로 선형 증가. 서버가 계산하고 `NetworkVariable<float>`로 복제(UI용).

차지가 최대에 도달하면 계속 유지될 뿐 자동 발사하지 않는다.

**차지 → 힘 곡선 (2026-10-04, 사용자 요청 "처음 살짝 눌렀을 때는 약하게, Hub 에서 조절 가능하게"):**

```
forceRatio = minChargeRatio + (1 − minChargeRatio) × charge ^ chargeCurve      (FurnitureThrowSettings.ForceRatio)
```

- 이전은 `chargeCurve` 없이 직선, `minChargeRatio` 0.4 — 살짝 눌러도 최대의 40%(1인 12 m/s)가 나갔다.
  지금 기본값 0.15·제곱이면 0.1초 탭 ≈ 16%(1인 약 4.8 m/s), 0.3초 ≈ 23%, 0.6초 ≈ 46%, 1초 100%.
- 차지 게이지(`ChargeGaugeUI`)는 누른 시간이 아니라 이 **힘 비율**(`FurnitureGrabTarget.LaunchPower`)을 채우고 "힘 nn%"로 쓴다.
- **Hub(Tab) "가구 투척 힘" 섹션**에서 누른 시간별 힘·발사 속도(1인/2인) 미리보기와 `차징 / 발사` 값 줄(`chargeTime`·
  `minChargeRatio`·`chargeCurve`·`oneHolderForce`·`twoHolderForce`·`heavySoloMultiplier`·`torqueScale`)을 바로 조절한다.
  튜닝 창(F2)과 같은 SO 라 즉시 적용되고, 발사는 서버가 계산하므로 **호스트의 값**이 쓰인다(에디터에서는 SO 에 남는다).

### 5. 발사

버튼 뗌 → `RequestReleaseServerRpc(aimDirection)`

`FurnitureLauncher`가 힘을 계산한다:

```
holderCount  = 발사 시점의 홀더 수 (1 또는 2)
baseForce    = holderCount == 2 ? twoHolderForce : oneHolderForce
weightPenalty= (가구가 heavy이고 holderCount == 1) ? heavySoloMultiplier : 1.0

launchSpeed = baseForce * forceRatio(charge) * weightPenalty          — §4 곡선

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

`baseForce` 는 설정 값을 그대로 쓴다 — 첫 프로토타입부터 코드에 있던 하한(1인 30·2인 50, 문서에 없던 값)을
2026-10-04에 지웠다. Hub 에서 힘을 내려도 하한에 막혀 바뀌지 않았기 때문이다(에셋 값이 30/50이라 기본 동작은 같다).

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
| 남은 1명도 해제 — 먼저 놓은 뒤 `jointPutDownWindow`(2.5초) 안 | **같이 내려놓기 — 발사하지 않는다.** 홀더 없이 `Idle`, 중력으로 그 자리에 떨어진다 (2026-09-29 사용자 요청) |
| 남은 1명도 해제 — 2.5초가 지난 뒤 | **발사.** 발사 시점에는 1명이므로 1인 힘 |
| heavy 가구를 1명이 입력 → 해제 | 발사되긴 하되 `heavySoloMultiplier`(초기값 0.5)만큼 약하게 |
| 차징 중 한 홀더가 가구 표면에서 4 m 넘게 멀어짐 | 그 홀더만 발사 없이 해제. 2인이었으면 남은 1명의 `ThrowReady` |
| 홀더 전원이 4 m 이탈/접속 종료 | 발사 없이 `Idle`로 낙하 |

"2명 잡기 → 1명만 해제"에서는 2인 잡기 조건이 깨졌으므로 즉시 중력을 복구한다. 남은 사람은
가구를 붙잡지 않으며, 자신의 버튼을 놓을 때만 1인 힘으로 던진다.

> **같이 내려놓기 (2026-09-29, 사용자 요청 "동시에 멀쩡하게 내려놓는 게 불가능"):** 두 사람이 정확히 같은 틱에
> 놓는 건 불가능해서, 이전에는 둘이 들던 가구를 내려놓으면 늦게 놓은 사람 쪽으로 늘 1인 투척이 나갔다.
> 서버는 2인 잡기에서 첫 해제(비강제) 순간 `jointPutDownWindow` 마감 시각을 적고, 그 안에 마지막 홀더가
> 놓으면 발사 없이 `Idle` 로 돌린다(`FurnitureGrabTarget.IsJointPutDown`). 마감 전에 다시 둘이 잡거나 새로 잡으면
> 대기는 사라진다. 강제 해제(4m 이탈·접속 종료)로 한 명이 빠진 경우는 창을 열지 않는다. 0초면 기능이 꺼진다.
> UI 표시("같이 내려놓기 대기")는 아직 없다.

`FurnitureThrowSettings`에 `bool launchOnFirstRelease` 토글을 두어 플레이테스트에서 반대 정책도 즉시 비교할 수 있게 한다.

## 초기 파라미터

`FurnitureThrowSettings` (ScriptableObject):

| 값 | 초기값 | 메모 |
|---|---|---|
| `maxTargetDistance` | 12 m | 조준 사거리 |
| `holdBreakDistance` | 4 m | 차징 중 눈 → 가구 표면 거리 초과 시 발사 없이 해제 (2026-09-13, `maxHoldDistance` 15 m 대체) |
| `carryMinHandDistance` / `carryMaxHandDistance` | 0.9 / 2.2 m | 손잡이를 눈에서 떨어뜨려 드는 거리 범위. 진입 때 거리를 이 범위로 자른다 (2026-10-04, `hoverDistance` 3 m 대체) |
| `carryAimSmoothing` | 0.05 s | 20Hz 조준을 잇는 지수 평활 시간 상수 |
| `carryPositionResponse` / `carryRotationResponse` | 0.06 / 0.08 s | 위치·자세 오차를 줄이는 시간 상수 |
| `heldMaxLinearSpeed` | 15 m/s | 2인 운반 최대 속력 |
| `heldMaxAngularSpeed` | 720 °/s | 2인 운반 최대 각속력 |
| `carryMaxAcceleration` / `carryMaxAngularAcceleration` | 60 m/s² / 3600 °/s² | 속도 변화량 제한 — 부딪힐 때 덜컹거림 방지 |
| `carryFollowMinGripSpan` | 0.35 m | 손잡이 수평 간격이 이보다 짧으면 두 사람 방향을 따르지 않음 |
| `carryMaxFollowPitch` | 25° | 손 높이 차이로 기울 수 있는 최대 각 |
| `carryFriction` | 0.05 | 운반 중 콜라이더 마찰(결합 Minimum) |
| `carryIgnoresHolders` / `carryPassesOpenDoors` | true / true | 운반 중 두 홀더·열린 문짝과 충돌 끄기 |
| `squeezeTriggerDistance` | 0.1 m | 끼임 보조 시작 거리. 0이면 끔 |
| `squeezeMaxYaw` / `squeezeYawStep` | 40° / 10° | 끼임 보조 회전 범위·간격(누적 상한도 40°) |
| `squeezeMaxLateral` / `squeezeLateralStep` | 0.3 / 0.1 m | 끼임 보조 옆 이동 범위·간격(누적 상한도 0.3 m) |
| `squeezeProbeDistance` | 0.12 m | 보정 자세로 미리 밀어 보는 거리 |
| `wheelStepDegrees` | 15° | 휠 한 칸 회전·기울기 각도 |
| `chargeTime` | 1.0 s | 0 → 최대 차지 |
| `minChargeRatio` | 0.15 | 살짝 눌렀다 뗐을 때(차지 0) 나가는 힘 비율 (2026-10-04, 이전 0.4) |
| `chargeCurve` | 2 | 차지 → 힘 곡선 지수. 1이면 직선, 클수록 초반이 약함 (2026-10-04) |
| `oneHolderForce` | 30 | 1인 최대 발사 속도 변화량 |
| `twoHolderForce` | 50 | 2인 최대 발사 속도 변화량 |
| `heavySoloMultiplier` | 0.5 | heavy를 혼자 던질 때 |
| `torqueScale` | 2.0 | 회전하며 날아가는 정도 |
| `relaunchLockDuration` | 2.0 s | `Launched` 상태 최대 유지 |
| `launchOnFirstRelease` | false | 2인 중 1인 해제 정책 토글 |
| `jointPutDownWindow` | 2.5 s | 2인 잡기에서 먼저 놓은 뒤 이 안에 마지막 홀더도 놓으면 발사 없이 내려놓기. 0이면 끔 (2026-09-29) |

발사각 20°~70°는 플레이어 조준의 안전 범위로 고정된 게임 규칙이며 설정 에셋의 튜닝값으로
노출하지 않는다.

## 튜닝 순서 (플레이테스트)

값이 너무 많아 한꺼번에 만지면 길을 잃는다. 이 순서로 하나씩:

1. 20°~70° 고정 발사각 — 날아가는 궤적이 기분 좋은가
2. `oneHolderForce` — 혼자 던졌을 때 벽까지 닿는가
3. `carryMinHandDistance`·`carryPositionResponse`·`carryMaxFollowPitch` — 2인 운반 중 시야를 가리지 않고, 걷는 대로 딱 붙어 오는가.
   그다음 `carryFriction`·`squeeze*` — 휠 없이 문을 통과시키는가, 보조가 너무 멋대로 돌리지 않는가
4. `chargeTime`·`minChargeRatio`·`chargeCurve` — 살짝 밀기는 약하고 길게 누르면 확실히 세지는가, 홀드가 지루하지 않은가 (Hub "가구 투척 힘")
5. `twoHolderForce` — 2인이 확실히 더 강하다고 느껴지는가

## 미결정 사항

> **미결정:** 두 홀더의 조준 방향이 정반대일 때 평균 벡터가 0에 가까워진다. 현재 안은 "그 경우 크기를 그대로 쓰되 방향은 첫 홀더 기준"이지만, 각도 차이에 따라 힘을 감쇠시키는 편(협력 보상)이 더 나을 수 있다. 실제로 그런 상황이 자주 나오는지 먼저 확인할 것.

> **미결정:** 홀드 중인 가구가 벽에 끼었을 때의 처리. 2026-10-04부터 문틀 모서리는 낮은 마찰·끼임 보조(§3.2)로 비켜 가고,
> 들고 있는 두 사람과는 부딪히지 않는다(§3.3). 평평한 벽에 대고 미는 경우는 여전히 속도 서보가 벽을 미는 상태로 남는다
> (가속 제한 덕에 덜컹거리지는 않는다). 실제로 문제가 되면 "일정 시간 목표 도달 실패 시 자동 해제"를 넣는다.

> **미결정:** 두 손잡이 운반의 손 목표는 조준 방향을 따른다 — 한 사람이 주위를 둘러보면 그쪽 끝이 따라 움직인다.
> 의도된 조작(시선으로 내 쪽 끝을 움직임)이지만, 운반 중 둘러보기가 불편하다는 반응이 나오면 손 목표를 몸 방향 기준으로
> 바꾸고 시선은 높낮이에만 쓰는 안을 검토한다.

---

최종 갱신: 2026-10-04 (2인 운반을 조준점 중간 순간 추종 → 두 손잡이·속도 서보로 변경, 운반 중 마찰 낮추기·끼임 보조·
두 홀더와 열린 문짝 충돌 끄기 추가, 차지 → 힘 곡선·발사 힘 하한 제거·Hub "가구 투척 힘" 섹션). 이전: 2026-09-13 (스프링 부양 → 조준점 중간 고정 추종, 휠 회전·기울이기, 4 m 이탈 해제)
