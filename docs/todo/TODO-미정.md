# TODO — 날짜 미정 (착수 시점 결정 대기)

> 날짜별 폴더(`docs/todo/YYYY-MM-DD/`)와 달리, **언제 할지 자체가 아직 정해지지 않은** 작업을
> 모은다. 착수 시점이 정해지면 그날 날짜의 TODO로 옮기거나 여기서 지운다.

---

## 가구·문 프리팹 굽기 (MIG-6 잔여)

**상태:** 코드는 준비돼 있다. **언제 실행할지만 미정** — 사용자 결정 대기.

### 지금 상태 (2026-08-31 확인)

- `Assets/Prefabs/Furniture/`·`Assets/Prefabs/Map/` 폴더 자체가 없다.
- `Assets/Scenes/Game.unity`에 `PrefabInstance` 문서(`!u!1001`)가 **0건** — 지금 씬의 가구·문
  84개+는 전부 프리팹 링크 없는 **일반 씬 오브젝트**다(실측 확인).
- 그런데도 **NetworkObject 해시는 95개 전부 고유·0 아님**, EditMode 115/115·PlayMode 12/12
  통과 — 지금 당장 플레이가 깨지는 버그는 없다. 커밋된 씬이 우연히 유효한 상태로 저장돼
  있을 뿐, "프리팹 없이도 안전하다"는 보장은 아니다.
- `HousePrototypeBuilder`·`PrototypeSceneSetup`·`FurnitureCatalog` 코드는 이미 MIG-6 리팩터가
  끝나 있다 — `GhostHunter/프로토타입 게임 생성`을 실행하면 `BakeFurniturePrefabs`가 실제로
  `Assets/Prefabs/Furniture/*.prefab`을 굽고, `FurnitureCatalog.Instantiate`가 프리팹이면
  `PrefabUtility.InstantiatePrefab`(링크 유지)로 인스턴스를 찍어 내도록 이미 짜여 있다.
  **코드가 아니라 "그 도구를 실행하는 것" 자체가 남은 일이다.**

### 지금 이것 때문에 실제로 발생하는 문제

1. **`GhostHunter/Place Original Scale House Right` 메뉴가 지금 실행하면 확정적으로 에러로
   죽는다** — `PrototypeSceneSetup.LoadFurnitureCatalog()`가 `Assets/Prefabs/Furniture` 폴더가
   없으면 `MissingReferenceException`을 던지도록 코드에 있다.
2. **"프리팹 하나 고치면 전체 반영" 워크플로우가 안 된다** — 가구 치수·재질을 바꾸려면
   `HousePrototypeBuilder.FurnitureKinds()` 코드를 고친 뒤 전체 생성 도구를 다시 돌려야
   반영된다. 프리팹을 열어 고치는 정상 워크플로우([furniture-physics.md](../architecture/furniture-physics.md#L10-L22)가
   설명하는 것)가 지금은 아예 불가능하다.
3. **문서가 실제 상태와 다르다** — `furniture-physics.md`가 "프리팹 에셋의 인스턴스를 씬에
   놓은 NetworkObject"라고 현재형으로 단정하지만, 실측 결과 지금 씬엔 그 인스턴스가 없다.

### 왜 날짜를 못 정하는가

`GhostHunter > 프로토타입 게임 생성`을 재실행하면 `Game` 씬을 **처음부터 다시 만든다** —
CLAUDE.md §6 경고대로 방에 손으로 배치한 가구가 사라진다(도구는 방을 비운 채로 집을 짓고
`Furniture_Library`만 채운다). 지금 씬에 재실행 전 반드시 보존해야 할 손 배치가 있는지
먼저 확인이 필요하고, 이건 **씬을 열어 사용자가 직접 판단할 사안**이다.

### 착수 조건

- [ ] 사용자가 "지금 재실행해도 된다"고 확정
- [ ] 재실행 전, 씬에 생성 도구가 만들지 않은 손 배치 요소가 있는지 확인
- [ ] 재실행 후 `docs/conventions/unity-assets.md`의 `GlobalObjectIdHash` 검증 절차로
      프리팹·씬 양쪽 해시 재확인
- [ ] `furniture-physics.md`·`ghost-prototype.md` 등 "이미 프리팹화됨"으로 서술된 문서를
      실제 상태에 맞춰 갱신

관련: [roadmap.md MIG-6](../project/roadmap.md) · [ADR-0009](../architecture/decisions/ADR-0009-scene-placed-level-objects.md) ·
[furniture-physics.md](../architecture/furniture-physics.md) · [unity-assets.md §5](../conventions/unity-assets.md)
