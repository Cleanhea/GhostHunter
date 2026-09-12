# 퀵슬롯(라디얼 휠) 구현

> 상태: **더미 스캐폴드 구현 완료 (2026-09-12).** 자동 검증 통과, 수동 Play 검증 대기.
> 게임 규칙의 권위는 [퀵슬롯 시스템 기획서](../project/quick-slot-system.md)다.

## 1. 구성 요소

| 대상 | 파일 |
| --- | --- |
| 입력(Tab) 액션 | `Assets/InputSystem_Actions.inputactions` — `Player/QuickSlot` |
| 입력 노출·세 번째 잠금 | `Gameplay/Player/PlayerInputReader.cs` |
| 표시용 아이템 데이터(더미) | `Gameplay/Player/QuickSlotItemDefinition.cs` |
| 슬롯 배열(빈 슬롯 허용) | `Gameplay/Player/QuickSlotLoadout.cs` |
| 각도→인덱스 순수 계산 | `Gameplay/Player/QuickSlotSelection.cs` |
| UI 수치 설정 | `Gameplay/Player/QuickSlotUiSettings.cs` |
| 런타임 HUD(휠·확정·표시) | `UI/QuickSlotWheelUi.cs` |
| 씬 설치 도구 | `Editor/QuickSlotSetup.cs` (메뉴 `GhostHunter/퀵슬롯 HUD 설치`, 멱등) |
| 테스트 | `Tests/EditMode/QuickSlotTests.cs` |

## 2. 입력 — 세 번째 독립 잠금

`PlayerInputReader`는 이제 잠금을 세 개 갖는다. 셋은 서로 독립이고, 겹치면
**메뉴 &gt; 굴착 &gt; 휠** 순으로 더 강한 쪽이 이긴다.

| 잠금 | 메서드 | 살아 있는 입력 |
| --- | --- | --- |
| 일시정지 메뉴 | `SetGameplayInputLocked` | 없음(시점 포함 전부 0) |
| 굴착(§5.5.1) | `SetSkillInputLocked` | `Look`·`Burrow` |
| **퀵슬롯 휠(신규)** | `SetWheelInputLocked` | `Look`을 뺀 전부 — **`Move`·`Crouch`·`Sprint`·`QuickSlot` 자신은 살아 있다**(QS-5) |

휠 잠금이 막는 것은 `Attack`·`Interact`·`Burrow`·`Detect`·`Prone`·`Jump`뿐이다. `QuickSlot`
입력 자신이 막히지 않는 이유는 굴착 잠금 중 `Burrow`가 살아 있는 것과 같다 — 그래야 스스로 닫는다.

`RawLookDelta`라는 새 프로퍼티도 추가했다. `Look`과 달리 **어떤 잠금 중에도 0으로 바뀌지 않는다**
— 휠이 열린 동안 `Look`은 카메라가 돌지 않도록 0이 되지만, 휠 자신의 포인터 누적 계산은 여전히
마우스 델타가 필요하기 때문이다. `Update()` 맨 위에서 매 프레임 무조건 갱신한다.

> ⚠️ **`ClearBlockedInputs()`(메뉴·굴착 공용)와 `ClearWheelBlockedInputs()`(휠 전용)는 다른
> 헬퍼다.** 전자는 `Move`까지 0으로 만들지만 후자는 `Move`를 남긴다(QS-5). 휠 잠금에
> `ClearBlockedInputs()`를 쓰면 이동이 막혀 버린다 — 헷갈리지 않도록 주의.

## 3. 선택 계산 — `QuickSlotSelection.Resolve`

순수 정적 메서드. `Resolve(Vector2 pointer, int slotCount, float deadZone, int currentIndex)`.

- `pointer.magnitude < deadZone` → **-1**(선택 없음). `currentIndex`는 이 판정에 관여하지 않는다
  — "변경 없이 닫힌다"는 이 함수가 아니라 `QuickSlotWheelUi.Confirm`이 -1을 무시해서 구현한다.
- 그 외에는 `Mathf.Atan2(pointer.x, pointer.y)`로 12시 기준 시계 방향 각도를 구하고, 슬롯 폭
  `360/N`으로 나눠 인덱스를 낸다. 경계(슬롯 중심 ±폭/2)는 **위쪽 경계 포함**으로 다음 슬롯에 붙는다
  — `QuickSlotTests.슬롯_경계에서_다음_슬롯으로_넘어간다`가 이 규칙을 고정한다.

## 4. `QuickSlotWheelUi` — 상태 흐름

```
[Closed] ── QuickSlotPressedThisFrame && CanOpen() ──▶ [Open]
   ▲                                                      │
   │                                                      │ 매 프레임: 조건 깨짐 → 즉시 Close(선택 버림)
   │                                                      │ 포인터 누적 → Resolve → 시각 갱신
   └── Close() ◀── QuickSlotReleasedThisFrame(Confirm 먼저) ┘
```

`CanOpen`/`CanRemainOpen`은 같은 조건이다: `!IsGameplayInputLocked && !IsSkillInputLocked &&
!GrabController.IsHolding`(QS-9). 열림·유지 둘 다 이 조건으로 판정하므로 열려 있는 동안 조건이
깨지면(예: 다른 시스템이 강제로 메뉴를 열거나 굴착이 시작됨) 다음 프레임에 바로 닫힌다.

`Confirm(index)`은 `index < 0`(데드존)이면 아무 것도 하지 않고, `_loadout.GetSlot(index) ==
null`(빈 슬롯, QS-2)이어도 아무 것도 하지 않는다 — 둘 다 "장착 상태 불변"으로 귀결된다.

### 4.1 확정은 로컬 상태까지만 (QS-3·QS-6)

`Confirm`은 `_equippedIndex`/`_equippedItem` 필드를 바꾸고 우하단 표시를 갱신할 뿐이다. **RPC도
`NetworkVariable`도 없다** — `QuickSlotTests.퀵슬롯_휠은_로컬_전용이고_아직_RPC를_쓰지_않는다`가
회귀를 막는다. 실제 인벤토리 시스템이 붙으면 `[Rpc(SendTo.Server)]` 요청 → 서버 검증 경로를
추가해야 한다(코드 스타일 §3.4의 서버 권위 원칙).

### 4.2 시각 구성

`MoleSkillHud`와 같은 방식으로 런타임 `Canvas`(ScreenSpaceOverlay, `sortingOrder = 150`)를
만든다 — 일시정지 메뉴(100)와 `MoleSkillHud`(200) 사이다. 구성 요소:

- **딤 오버레이** — `_wheelRoot`의 자식이라 휠과 함께 켜지고 꺼진다(QS-8, 로컬 디밍만).
- **링 + 슬롯 마커** — 슬롯 개수만큼 원형 마커를 고정 각도(12시=0번, 시계 방향)에 배치하고, 선택된
  슬롯만 강조색으로 칠한다. 파이(wedge) 텍스처 대신 원형 마커 배치를 썼다 — 더미 스캐폴드
  범위에서 `Image.Type.Filled` 회전 조합보다 구현·검증이 단순하다.
- **중앙 패널** — 가리키는 슬롯의 이름·설명(legacy `UnityEngine.UI.Text`, `Resources.
  GetBuiltinResource<Font>("LegacyRuntime.ttf")`로 폰트 에셋 배선 없이 생성).
  아이콘 스프라이트가 없으면(QS-10) `PlaceholderColor`로 칠한 원을 대신 쓴다.
- **화살표** — 중앙 아래 고정 위치에서 포인터 각도로 회전해 방향을 보여준다. 데드존 안(선택
  없음)이면 숨긴다.
- **우하단 장착 표시** — `_isOpen`과 무관하게 항상 떠 있는 별도 UI. 장착이 바뀔 때만 갱신된다.

`OnDestroy`에서 런타임 `Texture2D`/`Sprite`를 전부 파괴한다(`MoleSkillHud`의
`DestroyRuntimeObject` 패턴 재사용).

## 5. 알려진 범위 밖 (다음 작업으로 미룸)

- **사망 상태 게이팅 없음(기획과 코드의 차이).** `QuickSlotWheelUi`는 현재 생존 상태를 조회하지 않는다.
  `ILocalPlayerContext`에는 정신력 참조가 없지만 `ISanityTeamService.TryGetLocalState`는 존재한다.
  **실제 인벤토리를 기다리지 않고 [관전 작업 SP-IMPL-1](../project/roadmap.md#15-sp-사망-후-관전-todo)에서**
  열림/유지 조건과 사망 시 선택 취소를 연결한다. 조회 경로는 구현 시 기존 서비스와 비교해 선택한다.
- **게임패드 미지원**(QS-7). `Player/QuickSlot`은 키보드 바인딩만 갖는다.
- **아이콘은 전부 자리표시자**(QS-10). `Assets/Settings/Gameplay/QuickSlotItem_Placeholder{1,2}.
  asset`는 검증용 더미이고, `QuickSlotLoadout_Default.asset`는 4슬롯 중 2개만 채워 빈 슬롯
  동작을 눈으로 확인할 수 있게 했다.

## 6. 씬·에셋 배선

| 대상 | 경로 | 상태 |
| --- | --- | --- |
| 설정 에셋 | `Assets/Settings/Gameplay/QuickSlotUiSettings_Default.asset` | 설치 도구가 생성 |
| 로드아웃(더미) | `Assets/Settings/Gameplay/QuickSlotLoadout_Default.asset` | 설치 도구가 생성, 4슬롯 중 2개만 채움 |
| 더미 아이템 2종 | `Assets/Settings/Gameplay/QuickSlotItem_Placeholder{1,2}.asset` | 설치 도구가 생성 |
| HUD 컴포넌트 | `Game` 씬 `PrototypeUI` (기존 `MoleSkillHud`와 같은 오브젝트) | 설치 도구가 `QuickSlotWheelUi` 추가 |

`GhostHunter > 퀵슬롯 HUD 설치` 메뉴는 `PauseMenuSetup`/`MoleSkillSetup`과 같은 멱등 패턴이다 —
이미 있으면 덮어쓰지 않고 `ValidateInstallation()`으로 배선만 검증한다. `Game` 씬을 통째로
다시 만들지 않는다.

## 7. 검증 상태 (2026-09-12)

| 항목 | 결과 |
| --- | --- |
| 컴파일 | Unity 6000.3.20f1 에디터, MCP 경유 강제 재컴파일 — 오류·경고 0건 |
| EditMode | **197/197 통과**(신규 12건 포함). 이전에 기록된 선재 실패(`Player_굴착_액션은_R키에...`)는
  현재 저장소 상태에서 재현되지 않는다 — 이미 T로 정리된 것으로 보인다 |
| 설치 도구 | `GhostHunter > 퀵슬롯 HUD 설치` 실행 성공, `ValidateInstallation()` 통과, `Game.unity` 저장 |
| Play 스모크 | `Bootstrap → Title` 진입까지 콘솔 오류 0건 확인 |
| **수동 확인 — 미수행** | Tab 홀드로 휠이 실제로 열리는지, 마우스로 슬롯이 바뀌는지, 뗐을 때 장착
  표시가 갱신되는지는 **사람이 직접 플레이해서 확인해야 한다** → `Bootstrap` 플레이 → F1 → `Local`
  → Host |

---

관련: [../project/quick-slot-system.md](../project/quick-slot-system.md) ·
[player-controller.md](player-controller.md) · [pause-menu.md](pause-menu.md)(입력 잠금 선례) ·
[../conventions/code-style.md](../conventions/code-style.md)

최종 갱신: 2026-09-12 (더미 스캐폴드 구현·검증 상태 문서화. 사망 게이팅은 관전 SP-IMPL-1에서 처리하도록 후속 연결, 코드 미수정.)
