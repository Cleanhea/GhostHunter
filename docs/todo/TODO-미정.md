# TODO — 날짜 미정 (착수 시점 결정 대기)

> 날짜별 폴더(`docs/todo/YYYY-MM-DD/`)와 달리, **언제 할지 자체가 아직 정해지지 않은** 작업을
> 모은다. 착수 시점이 정해지면 그날 날짜의 TODO로 옮기거나 여기서 지운다.

---

## ~~가구·문 프리팹 굽기 (MIG-6 잔여)~~ → ✅ 해결 (2026-08-31)

**이 항목은 더 이상 유효하지 않다.** 2026-09-05 실측:

- `Assets/Prefabs/Furniture/` 33개 · `Assets/Prefabs/Map/` 3개 프리팹이 존재한다.
- `Assets/Scenes/Game.unity` 에 `PrefabInstance` 문서(`!u!1001`)가 **108건** 있다.
- 커밋 `015f874`("가구 프리팹 추가 및 씬 수정", 2026-08-31)에서 실제로 구워졌고,
  이후 `a6ab189`(엎드리기·침대 밑 은신)에서 침대 3종을 **제자리 편집**으로 고쳤다.

즉 "생성 도구를 재실행해 프리팹을 굽는 일"은 끝났다. 프리팹을 고칠 때 **전체 재굽기를 하지 않는다** —
`SaveAsPrefabAsset` 재굽기는 `GlobalObjectIdHash` 를 매번 새로 만들어 씬 인스턴스와 어긋난다.
일부만 고칠 때는 `PrefabUtility.LoadPrefabContents` 로 제자리 편집한다
→ [unity-assets.md §5](../conventions/unity-assets.md)

---

## 맵 v0.3 그레이박스 (MAP-1)

**상태:** 기획·규모·도면 전부 확정(2026-09-05). MAP-1 그레이박스 체감 검증은 대기 중이며,
기존 씬을 다시 만드는 전체 재생성 승인과 별개로 **B/C 비교용 덧붙이기 메뉴 코드(MAP-15)는 준비됐다.**

### 무엇을 하는가

20 × 16m 두 개 층 + 14 × 10m 다락을 **빈 상자로** 세우고 크기·동선만 본다.
방 프리셋·Spawn Point·Work Room 은 그 다음이다(먼저 만들면 두 번 만들게 된다)
→ [map-generation.md §10.2 B](../architecture/map-generation.md)

### 확정된 것 (2026-09-05)

- 한 층 **20 × 16m**, **2개 층** + 다락 **14 × 10m**. `MapScale` = **1.0**(도면이 실측)
- 침실 슬롯 **4.2 × 4.0m** — 현재 5.7 × 5.4m 보다 작아진다
- 계단 A·B 폭 **2.2m**, 세 층 북쪽 중앙에 정렬
- 층별 실측값은 [map-generation.md §2](../architecture/map-generation.md)

### 왜 아직 날짜를 못 정하는가

**생성 도구 재실행은 `Game` 씬을 처음부터 다시 만든다**(CLAUDE.md §6). 지금 씬에는 생성 도구가
만들지 않은 것들이 붙어 있다 — 정신력 테스트베드, 드릴 카 세이프 존, 은신처 4개, 일시정지 메뉴
배선, 굴착 카메라 Volume. 각각 설치 도구(`SanityTestbedSetup`·`GhostPrototypeSetup`·
`PauseMenuSetup`·`MoleBurrowPostProcessingSetup`)가 있지만, **전부 다시 깔리는지 확인 전에는
재실행하면 안 된다.**

> **MAP-15 예외 경로:** `GhostHunter > 맵 B·C 프로토타입 추가 (실내·계단·가구)`는 전체 생성기를
> 호출하지 않고 `House_Prototype_PlanB/C` 루트만 현재 `Game` 씬에 덧붙인다. 기존 `House_01`,
> 원본 배율 집, MAP-1 그레이박스와 플레이어/네트워크/UI 배선을 이동·삭제하지 않는다. 메뉴 실행 뒤
> 씬을 저장하고 NGO in-scene hash 검증을 수행해야 하며, 생성 직후는 Unity Undo로 되돌릴 수 있다.

### 착수 조건

- [x] ~~D-17 맵 규모 확정~~ (2026-09-05)
- [x] ~~D-18 층별 도면 3장~~ (2026-09-05, `docs/images/house1~3floor.png`)
- [ ] **사용자가 씬 재생성을 승인**
- [ ] **MAP-15 B/C 덧붙이기 메뉴 실행** — Editor가 열려 있을 때 `Game.unity`에서 수동 실행
- [ ] 재생성 전, 설치 도구가 손 배치 요소를 전부 되살릴 수 있는지 점검
- [ ] 재생성 후 `unity-assets.md` 의 `GlobalObjectIdHash` 검증 절차 수행

관련: [roadmap.md §1.2 MAP](../project/roadmap.md) · [map-generation.md §12](../architecture/map-generation.md) ·
[ADR-0009](../architecture/decisions/ADR-0009-scene-placed-level-objects.md) · [unity-assets.md §5](../conventions/unity-assets.md)
