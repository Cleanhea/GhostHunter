# 퀵슬롯(라디얼 휠) 구현

> 상태: **대걸레·드라이버 장착 연결 (2026-09-13).** 일반 인벤토리는 미구현.
> [청소 구현](cleaning-system.md) · [가구 분해 구현](furniture-multidriver.md) 참조.
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

### 4.1 확정은 플레이어 장착 요청으로 전달 (QS-3·QS-6)

`Confirm`은 `ILocalPlayerContext.CleaningController.EquipSlot(index)`과
`FurnitureDriverController.EquipSlot(index)`으로 선택을 전달한다. 드라이버 이외의 슬롯을
선택해도 양쪽에 전달해야 이전 도구의 장착 상태가 남지 않는다. 드라이버 컴포넌트가 없으면
드라이버 슬롯 선택은 거절한다. 드라이버 해제 시 진행 중인 분해/조립도 취소한다.
UI 자체는 로컬 MonoBehaviour이며 RPC를 소유하지 않는다. 플레이어 컴포넌트가 서버에 장착을
요청하고 소유자에게 결과를 회신한다. UI는 대걸레 컨트롤러의 `EquippedSlot`(없으면 드라이버)을 읽어 재접속이나
서버 거부 후에도 표시를 맞춘다. 다른 플레이어에게 장착 모델·선택을 복제하지 않는다.

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

- ~~**사망 상태 게이팅 없음.**~~ ✅ **해결 (2026-09-12, SP-IMPL-1)** — `ILocalPlayerContext`에
  `SanityNetworkState Sanity` 참조를 추가했고(`SanityNetworkState`가 자신을 등록), `QuickSlotWheelUi.
  CanOpen`이 `_localPlayer.Sanity.HasSanity`를 함께 검사한다. 사망 시 열려 있던 휠은 기존
  `CanRemainOpen` → `Close()` 경로로 선택을 취소하고 닫힌다 — 별도 사망 분기가 필요 없었다.
- **게임패드 미지원**(QS-7). `Player/QuickSlot`은 키보드 바인딩만 갖는다.
- **아이콘은 자리표시자**(QS-10). 현재 0번 슬롯은 `QuickSlotItem_Mop.asset`, 1번은
  `QuickSlotItem_Driver.asset`, 2번은 기존 `QuickSlotItem_Placeholder2.asset`를 맨손으로 표시한다.
  3번은 비어 있다.

## 6. 씬·에셋 배선

| 대상 | 경로 | 상태 |
| --- | --- | --- |
| 설정 에셋 | `Assets/Settings/Gameplay/QuickSlotUiSettings_Default.asset` | 설치 도구가 생성 |
| 로드아웃 | `Assets/Settings/Gameplay/QuickSlotLoadout_Default.asset` | 0번 대걸레·1번 드라이버·2번 맨손, 빈 슬롯 1개 |
| 아이템 | `QuickSlotItem_Mop.asset`·`QuickSlotItem_Placeholder2.asset` | 청소 설치 도구가 대걸레와 맨손 표시 연결 |
| HUD 컴포넌트 | `Game` 씬 `PrototypeUI` (기존 `MoleSkillHud`와 같은 오브젝트) | 설치 도구가 `QuickSlotWheelUi` 추가 |

`GhostHunter > 퀵슬롯 HUD 설치` 메뉴는 `PauseMenuSetup`/`MoleSkillSetup`과 같은 멱등 패턴이다 —
이미 있으면 덮어쓰지 않고 `ValidateInstallation()`으로 배선만 검증한다. `Game` 씬을 통째로
다시 만들지 않는다.

## 7. 검증 상태

2026-09-13: 드라이버 장착 전달 누락을 수정했다. `FurnitureDisassemblyFlowTests`에서 실제
`QuickSlotWheelUi.Confirm`을 통한 드라이버 장착·대걸레 전환 시 분해 취소·맨손 전환 시 양쪽
도구 해제를 확인했다(분해 14/14, 전체 PlayMode 29/29 통과). 관련 EditMode는 31/31 통과.
별도로 실제 Game 씬 Local Host에 가상 Tab·마우스 이동·Tab 해제 입력을 넣어 1번 슬롯의
클라이언트·서버 드라이버 장착을 확인했고, 실제 조준·우클릭으로 침대 분해까지 완료했다.
씬·프리팹을 재저장하거나 재설치할 필요는 없다. 원격 Host/Client 검증은 남아 있다.

청소 연결 후 Local Host에서 Input System의 Tab 누름·마우스 델타·Tab 해제를 주입해
휠 열림, 맨손 슬롯 확정, 서버 장착 반영, 대걸레 모델 숨김과 휠 잠금 해제를 확인했다.
아래 표는 초기 휠 구현 당시 검증 이력이며, 현재 결과는 [cleaning-system.md §4](cleaning-system.md)를 본다.

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

최종 갱신: 2026-09-13 (드라이버 장착·해제 전달 수정, 자동 테스트와 실제 Local Host 입력 검증)
