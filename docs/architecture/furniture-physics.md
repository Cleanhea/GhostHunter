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

> **침대는 다리로 띄운다.** `SingleBed_1.0x2.0` · `SingleBed_1.1x2.0` · `DoubleBed_1.6x2.0` 은
> 네 모서리 다리로 밑을 **약 0.8m 비우고**, 자식 `UnderBedHide` 에 `BedHideZone`(순수 판정 컴포넌트,
> 콜라이더 없음)을 단다 — 엎드린 플레이어가 기어 들어가 숨는 공간이다
> ([ghost-prototype.md](ghost-prototype.md), [player-controller.md](player-controller.md)).
> `Frame` 파츠는 이름·평면 치수(w × l)를 유지한다(생성 검증 `ValidateFootprint`·`RequireBedHideZone`).
> 기존 프리팹 3개는 씬 인스턴스가 소스 해시를 참조하므로(CLAUDE.md §5) 전체 재굽기가 아니라
> `GlobalObjectIdHash` 를 보존하는 **제자리 편집**으로 갱신했다 — 다리 아웃라인 셸은 다음 전체
> 생성 때 `CreateBed` 가 채운다.

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
  ([map-generation.md §10.1](map-generation.md#101-구현-현황-2026-09-05)).

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
- **충돌 내구도 코드를 구현했다** — 수직 상대 속도·초기값·개발 복구 승인. Unity 테스트·Local Host/Client 충돌·복제 검증 통과 →
  [가구 내구도 시스템 기획서](../project/furniture-durability-system.md), 아래 구현 상세.
- 여러 가구가 한 프레임에 연쇄 충돌하면 `NetworkTransform` 대역폭이 튄다. 라이브러리 21종 +
  손으로 배치한 가구가 전부 스폰되는데, 가만히 있는 가구는 아무것도 보내지 않으므로
  평상시 비용은 없다. 한 방에서 대량으로
  연쇄 충돌시켰을 때 대역폭이 문제가 되면 그때 방 단위 관심 영역(관측자 필터)을 검토한다.
- **가구가 겹친 채로 시작하면 폭발한다.** PhysX가 서로 밀어내면서 세션 시작과 동시에 가구가
  튀어나가는데, 눈으로는 원인을 알기 어렵다. 그래서 생성 시점에 파츠 콜라이더 단위로 겹침을
  검사하고(`ValidateFurnitureClearance`), 도면 좌표가 떠 있으면 지지면에 정확히 얹는다
  (`RestOnSupport`).

## 가구 내구도 구현 (2026-09-15, 2026-09-16 파괴 끄기)

> **2026-09-16 — 내구도 0에서 가구를 파괴하지 않는다(FD-10 재확정).** 아래 "파손" 서술은
> `FurnitureDefinition.DestroyAtZeroDurability`를 **켰을 때만** 일어난다. 기본값은 꺼짐이고,
> 끈 상태에서는 0이 되어도 렌더러·콜라이더·물리·잡기·풀 재사용이 모두 그대로다.
> `FurnitureNetworkPhysics.IsBroken`이 이 스위치를 포함하므로, 파손을 참조하는 모든 게이트
> (`IsAvailable`·`FurnitureDriverPoolItem.IsActive`·`TryFindInactive`·조립 영역 제거)가 한 번에 꺼진다.
> `ServerSetDurability`는 파괴를 끈 동안 **0도 유효한 값**으로 받는다 — 조립 평균이 0이 될 수 있기 때문이다.

- **값의 소유자:** 모든 가구에 있던 `FurnitureNetworkPhysics`가 서버 쓰기 `NetworkVariable<int>`
  하나로 0~100 내구도를 복제한다. 0은 파괴를 켠 경우에만 파손 상태다. `FurnitureDriverPoolItem.Durability`는 이 값을
  읽으며 분해·조립 시 `ServerSetDurability`로 상속한다. 별도 NetworkBehaviour나 어셈블리는 추가하지 않는다.
- **설정:** 기존 `FurnitureDefinition_Light/Heavy` SO에 최소 속도 6, 초과 속도당 피해 1,
  등급 계수 1, 피해 상한 50, 판정 창 0.2초, 배치 보호 1초, Held 중 피해 적용을 추가했다.
  기존 에셋에 없는 필드는 C# 초기값으로 로드된다. Inspector에서 등급별로 조정한다.
- **충돌:** 서버 `OnCollisionEnter`에서 `Collision.relativeVelocity`를 각 접촉 법선에 투영하고
  가장 큰 수직 속도를 쓴다. 가구끼리는 같은 속도를 양쪽에 전달한다. 각 가구가 독립적으로
  보호 시간·설정·판정 창을 적용하므로 상대 콜백과 복합 콜라이더의 중복도 누적되지 않는다.
  `OnCollisionStay`는 처리하지 않는다. 플레이어·귀신은 레이어와 부모 컴포넌트로 제외한다.
- **판정 창:** 첫 유효 피해부터 0.2초 동안 최대 피해를 기억한다. 더 강한 접촉이 오면 차액만
  즉시 차감한다. 따라서 합계는 최대 충돌 한 번과 같고, 0 도달 시 창 종료까지 기다리지 않는다.
  피해 없는 접촉은 판정 창을 시작하지 않는다.
- **파손(`DestroyAtZeroDurability`를 켠 경우만):** 홀더를 모두 해제하고 발사 상태를 초기화한다. `Renderer.forceRenderingOff`로
  윤곽선까지 숨기고 콜라이더·충돌·물리를 끈다. 기존 풀 활성/랜덤 배치 값은 보존한다.
  파손 풀은 `IsActive`가 false이며 `TryFindInactive`·`ServerActivate`·`ServerPlace`에서도 거절한다.
  파손 시 조립 영역 후보 목록에서도 즉시 제거한다(콜라이더 비활성화는 이탈 콜백을 보장하지 않는다).
  진행 행동은 기존 재검증으로 취소된다.
- **표시 순서:** 공통 물리의 Awake를 풀보다 먼저 실행해 원래 콜라이더 배선을 기억한다.
  `OnNetworkPostSpawn`과 풀 배치 변경 후에 가용 상태를 다시 적용한다. 늦게 접속해도 파손
  상태가 반영되며 풀의 표시 코드가 파손 가구를 다시 켜지 않는다. Local 별도 프로세스의 파손 후 접속 검증 통과(아래).
- **배치 보호:** 네트워크 스폰, `RoomPreset.TeleportBody`(프리셋·랜덤 배치·리셋 공통),
  분해/조립 활성화가 보호 시간을 갱신한다. 배치 위치를 R의 복귀 위치로 기억한다.
- **개발 복구:** F1 접속 HUD의 가구 내구도 섹션에서 조준 대상 값 확인 및 서버 전체 100 복구.
  R은 기존 초기 위치 복귀에 더해 공통 가구 레지스트리 전체를 복구하므로 기존 직렬화 목록 밖의
  분해 부품도 포함한다. 풀 대기 인스턴스는 계속 숨겨 두고, 파손 전 활성 가구만 다시 나타난다.
  F1 복구는 위치를 바꾸지 않으며 R의 복귀 위치도 덮어쓰지 않는다. 클라이언트 변경 RPC는 없다.
- **개발 라벨 (2026-09-15):** F1 HUD가 켜져 있는 동안 조준 카메라 기준 반경 20m(`ConnectionHud._durabilityLabelRange`)
  안의 사용 가능한 가구마다 켜진 콜라이더 경계 윗면 위에 `내구도 N`을 띄운다. 글자색은 0 빨강 → 100 초록.
  복제된 값을 읽는 로컬 IMGUI 표시라 Host·Client 모두 보이며, 벽 뒤 가구도 가리지 않는다.
  파손·풀 대기 가구는 표시하지 않는다. HUD 가구 내구도 섹션의 토글로 라벨만 끌 수 있다.
  검증: `.NET` C# 빌드(Gameplay·DebugTools) 경고 0·오류 0. **에디터 Play 화면 확인은 미수행**(Unity MCP 미연결).

### 검증 상태

**2026-09-16 파괴 끄기 변경분:** `dotnet build`로 `GhostHunter.Gameplay`·`GhostHunter.Tests.PlayMode`·
`GhostHunter.Tests.EditMode` 경고 0·오류 0. **Unity Test Runner와 에디터 Play는 미실행**
(에디터가 열려 있어 batchmode 잠김, Unity MCP 미연결). 추가한 테스트도 아직 실행되지 않았다:
`FurnitureCollisionFlowTests.없애지_않는_설정에서는_내구도가_0이어도_가구가_그대로_남는다`,
`FurnitureDisassemblyFlowTests.조립하면_완성_가구가_부품_내구도의_평균을_가진다`·
`내구도가_0인_부품도_조립에_쓰이고_평균이_그대로_적용된다`,
`FurnitureDurabilityTests.내구도_0인_부품도_평균에_그대로_들어간다`.
기존 파손 경로 테스트 4건은 정의 SO에서 `_destroyAtZeroDurability`를 켜도록 수정했다.

- `.NET` C# 빌드: Gameplay·DebugTools·EditMode·PlayMode 코드 컴파일 확인. Unity Test Runner와는 별개다.
- 순수 내구도 테스트 18건: 컴파일한 `FurnitureDurabilityTests`를 별도 .NET 실행기로 실행해 18 통과·0 실패.
  Unity Test Runner 밖의 계산 검증이며 PhysX·네트워크·에셋 임포트는 검증하지 않는다.
- 씬·프리팹에 직접 직렬화된 가구 138개: 공통 물리·설정 참조 누락 0 (프리팹 오버라이드 최종 해석은 에디터 확인 필요).
- `FurnitureDurabilityTests`: 속도별 공식, 상한, 수직 성분, 비정상 입력, 기존 상속·평균.
- `FurnitureCollisionFlowTests`: 보호, 1m 낙하·바닥 안착, 최대 피해 창, 실제 양쪽 충돌, 플레이어/귀신 제외,
  보호 대상과 충돌, Held 중 파손과 홀더 해제, 복구.
- `FurnitureDisassemblyFlowTests`: 파손 풀 재사용 차단·복구, 대기 부품의 복구 후 비활성 유지,
  이탈 콜백 없이도 조립 영역 후보에서 파손 부품 제거 추가.
- **2026-09-15 Unity MCP 실행:** EditMode `FurnitureDurabilityTests` **18/18**,
  PlayMode `FurnitureCollisionFlowTests` **8/8** + `FurnitureDisassemblyFlowTests` **20/20** +
  `FurnitureThrowFlowTests` **19/19** 통과. 합계 **65 통과·0 실패·0 스킵**.
  실제 PhysX 가구 간 충돌의 양쪽 피해·1m 낙하·바닥 안착·플레이어/귀신 레이어 충돌 제외를 포함한다.
- **Development Windows 빌드:** `Build/DurabilityTest/GhostHunter.exe`, 오류 0·경고 0.
  게임 규칙 변경 없이 `LocalSessionAutomation`에 선택한 가구의 복제 상태 로그와 명시적 Client 권위 검사 인자를 추가했다.
- **실제 Game 씬, 에디터 Host + 별도 실행 파일 Client 2개(Local UTP):**

  | 검사 | 관찰 결과 |
  | --- | --- |
  | 실제 바닥 충돌 | `DeskChair_0.55x0.55`(NetworkObjectId 12)를 2m 올리고 서버 하향 속도 20m/s 부여. 직접 피해 함수를 부르지 않은 PhysX 충돌에서 Host·Client 모두 **100 → 86** |
  | 충돌 파손 | 서버에서 검증용 시작 내구도 3을 설정하고 같은 충돌을 반복. 양쪽 **0**, 가용 false, 활성 렌더러/콜라이더 **0/0**, 충돌 비활성. Host에서 잡기 불가 확인 |
  | 늦은 접속 | 파손 후 두 번째 Client 접속. 플레이어 3명, `Bootstrap,Game` 동기화. 처음부터 내구도 0·렌더러/콜라이더 0/0 |
  | Client 권위 | 첫 Client에서 `ServerSetDurability(1)` 거절, 충돌 피해·복구 API 호출 후 **100 유지**. 늦은 Client에서도 같은 호출 후 **0 유지** |
  | F1 복구 경로 | `ServerResetAll(false)` 호출. 세 피어 모두 **100**, 렌더러/콜라이더 **4/4**, 충돌 활성. 복구 직후 서버 피해 호출은 보호 시간으로 무시 |
  | R 복구 경로 | 원래 위치에서 옆으로 옮겨 다시 실제 충돌 파손 후 `FurnitureResetter.ResetAll` 호출. 세 피어 모두 **100**, 원래 `(-7.59, 0.00, 12.80)` 복귀, 렌더러/콜라이더 **4/4** |
  | 물리 권위 | 정상 가구는 Host dynamic / Client kinematic, 파손 시 양쪽 kinematic |

- **관찰 한계:** Client는 `-batchmode -nographics`로 실행했다. 렌더러 활성 상태를 검사했으며 화면 픽셀·사람의 조작감은 미검증.
  F1 버튼/R 키 자체 대신 해당 처리 함수를 호출했다. Held 파손 검사는 테스트의 가짜 두 번째 홀더를 사용한다.
  실제 두 플레이어의 운반 중 파손·Steam 2PC는 미검증이다.
- **로그:** `Logs/durability-client.log`, `Logs/durability-late-client.log`(gitignore 대상).
  세션 관측 로그의 오류 수는 0이나, 관측기 생성 전 Bootstrap의 **Steam 초기화 실패(NoSteamClient)** 로그는 별도로 존재한다.
  에디터에서도 같은 오류 1건과 Bootstrap을 NGO 동기화에서 제외하는 경고가 있었다. Local 접속/내구도 검증은 통과했다.
- **정리:** 테스트 Client 2개 종료, 에디터 Play 종료 후 Bootstrap 복귀. 씬·프리팹·설정 에셋은 저장하지 않았다.

재현 절차(직접 입력·화면 확인은 남은 수동 검증):
1. Unity에서 프로젝트를 임포트하고 Console 컴파일 오류가 없는지 확인한다.
2. Test Runner에서 EditMode의 `FurnitureDurabilityTests`, PlayMode의 `FurnitureCollisionFlowTests`,
   `FurnitureDisassemblyFlowTests`, `FurnitureThrowFlowTests`를 실행한다.
3. Bootstrap → F1 Local Host에서 가구 충돌·F1 내구도 표시·0 파손·R 복구를 확인한다.
4. Local Client로 같은 값·숨김·복구와 클라이언트 변경 불가를 확인한다. Steam 검증은 PC 2대·계정 2개로 수행한다.

## 씬 배치 (`Prototype`)

| 오브젝트 | 사양 |
|---|---|
| `House_01/Rooms_Fixed` | 방별 바닥. 정적 콜라이더 |
| `House_01/Walls_Doors_Windows` | 외벽·내벽·창, 여닫이 문 5개 |
| `House_01/Fixtures` | 붙박이(카운터·위생도기). 정적 콜라이더 |
| `House_01/PhysicsFurniture` | 손으로 배치한 가구가 들어갈 자리. **생성 직후에는 비어 있다** |
| `House_01/RoomSlots` | 5.7 × 5.4m 침실 슬롯 2개. 방 중심(바닥면), 회전 없음 |
| `Furniture_Library/Items` | **던질 수 있는 가구 21종**을 종류별 하나씩 일렬로. 복붙용 원본 |
| `Furniture_Library/Ground` | 라이브러리 받침 바닥 |
| `Room_Presets/BedroomPreset_A·B·C` | 침실 프리셋 3종(집 남쪽 바깥). 세션 시작 시 둘이 슬롯으로 간다 |
| `RoomSlotAssigner` | 서버가 프리셋을 중복 없이 뽑아 슬롯에 배치 ([09](map-generation.md)) |
| `House_01_OriginalScale_Right` | 도면 치수 그대로(배율 ×1) 지은 비교용 집. 집 동쪽 3m 옆 |
| `House_01_OriginalScale_Right/PhysicsFurniture` | **여기는 생성 도구가 가구를 깔아 둔다** (29개) |
| `PlayerSpawnPoints` | 빈 오브젝트 4개 |
| `FurnitureReset` | 개발용. 호스트가 `R`을 누르면 가구를 초기 위치로 되돌린다 |

`FurnitureReset`은 현재 씬 기준 라이브러리 21종 + 프리셋 34개 + 비교용 집 29개를 모두 들고 있다.
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

최종 갱신: 2026-09-04 (침대 3종을 다리로 띄우고 `UnderBedHide`/`BedHideZone` 추가 — 엎드려 침대 밑 은신. 이전: 2026-08-23)
