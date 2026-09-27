# Unity 에셋 규약

> 씬·프리팹·ScriptableObject·리소스를 다루기 전 MUST 읽는다.

## 1. 에이전트 작업 방식

에이전트는 씬·프리팹·ScriptableObject(`.unity`·`.prefab`·`.asset`)를 **텍스트(YAML)로 직접 고치고**, 파일을 셸
(`git mv`/`rm`)로 옮기거나 지울 수 있다(2026-09-28 사용자 결정).

- 옮기거나 지울 때는 `.meta`를 짝으로 함께 처리하고 짝으로 커밋한다. `.meta`의 `guid`를 바꾸면 그 에셋을 가리키는
  참조(씬·프리팹·SO·Build Settings)가 모두 끊긴다.
- 새 에셋의 `.meta`는 Unity가 임포트할 때 만든다. 직접 만들어야 하면 다른 에셋과 겹치지 않는 새 `guid`를 쓴다.
- Unity 에디터가 열려 있으면 바깥에서 고친 파일은 에디터가 포커스를 받을 때 다시 임포트된다. 에디터에 같은 씬이
  **저장하지 않은 변경과 함께** 열려 있으면 에디터 쪽 저장이 파일 수정을 덮어쓸 수 있다.
- 씬 `NetworkObject`를 텍스트로 새로 넣거나 복사하면 `GlobalObjectIdHash`가 확정되지 않는다 → §5.2.
- 씬·프리팹을 고쳤으면 무엇을 바꿨는지 보고한다.

### 1.1 에디터 생성 도구를 두지 않는다

씬·프리팹을 만들고 배선하던 일회성 설치·생성 메뉴(`GhostHunter > … 설치`/`… 생성`/`… 검증`)와 맵 생성기
(`HousePrototypeBuilder`)는 2026-09-28에 모두 지웠다([ADR-0020](../architecture/decisions/ADR-0020-remove-one-off-editor-setup-tools.md)).
**저장된 씬·프리팹·설정 에셋이 유일한 원본이다.**

- 씬·프리팹은 직접 고친다(§1). 다른 문서에 남은 메뉴 실행 절차는 구현 당시 기록이다.
- 방별 가구 배치 좌표를 코드에 심지 않는다 — 침실은 `Room_Presets`를 서버가 옮겨 채운다([map-generation.md](../architecture/map-generation.md)).
- 같은 배선을 여러 씬·프리팹에 반복해야 하면 그 작업에 한정한 도구를 만들고, 끝나면 지운다.

## 2. 네이밍

| 대상 | 규칙 | 예 |
| --- | --- | --- |
| 씬 | PascalCase | `Bootstrap.unity`, `Stage1.unity` |
| 프리팹 | PascalCase, 카테고리 접두 | `Furniture_Chair`, `UI_Crosshair`, `NetworkRig` |
| 프리팹 배리언트 | `<Base>_<Variant>` | `Furniture_Chair_Heavy` |
| ScriptableObject 에셋 | `<Type>_<이름>` | `FurnitureDefinition_HeavyCube` |
| 머티리얼 | `M_<이름>` | `M_WallPaint` |
| 텍스처 | `T_<이름>_<타입>` | `T_Wall_BaseColor` |
| 메시/모델 | `SM_` (static) / `SK_` (skinned) | `SM_Chair`, `SK_Player` |
| 애니메이션 클립 | `A_<대상>_<동작>` | `A_Player_Run` |
| 오디오 | `SFX_` / `BGM_` | `SFX_FurnitureImpact_01` |
| 셰이더 | `SH_<이름>` | `SH_Outline` |

- 공백·한글·특수문자를 파일명에 쓰지 않는다.
- 버전 번호를 파일명에 붙이지 않는다(`Chair_final2` 금지). Git이 버전을 관리한다.

## 3. 폴더

배치는 [../architecture/overview.md](../architecture/overview.md)를 따른다.
**이 프로젝트는 `Assets/_Project/` 래퍼를 쓰지 않는다** → [ADR-0007](../architecture/decisions/ADR-0007-flat-assets-layout.md).

- `Resources/` 폴더 사용 MUST NOT (빌드 크기·로드 시점 통제 불가). 직접 참조 또는 Addressables를 쓴다.
- `StreamingAssets/`는 런타임 외부 파일이 정말 필요할 때만.
- 에디터 전용 스크립트는 `Assets/Scripts/Editor/` 안에 둔다(Unity 특수 폴더 규칙).
- 서드파티 에셋은 폴더 구조를 임의로 바꾸지 않는다.

## 4. ScriptableObject

밸런스·설정 데이터는 MUST SO로 관리한다.

```csharp
[CreateAssetMenu(menuName = "GhostHunter/Gameplay/Player Move Settings", fileName = "PlayerMoveSettings")]
public sealed class PlayerMoveSettings : ScriptableObject
{
    [SerializeField] private float _moveSpeed = 5f;
    public float MoveSpeed => _moveSpeed;
}
```

**규칙**
- `menuName`은 MUST `GhostHunter/` 로 시작한다.
- SO에 런타임 가변 상태를 저장하지 않는다(에디터에서 값이 영구 변경됨). SO는 읽기 전용 데이터.
- SO 에셋은 `Assets/Settings/Gameplay/`에 둔다.
- 수치를 바꾸면 [../project/gdd.md](../project/gdd.md) 밸런스 표와 해당 시스템 문서의 초기값 표를 함께 갱신한다.

### 4.1 씬 참조

씬을 코드에서 가리킬 때 **문자열을 직접 쓰지 않는다.** `GhostHunter.Data.Scenes.SceneReference`를 쓴다.

```csharp
[SerializeField] private UnityEditor.SceneAsset _sceneAsset;   // #if UNITY_EDITOR
[SerializeField, HideInInspector] private string _sceneName;   // OnBeforeSerialize에서 구워짐
```

- 에디터에서는 씬 에셋을 드래그해 지정하고, 런타임에는 이름 문자열만 남는다(에디터 필드는 빌드에서 제외).
- **씬 파일명을 바꿔도 저장 시 자동으로 다시 구워진다.** 손으로 친 문자열이 조용히 깨지는 것을 막는 게 목적이다.
- 다만 `SceneNameSO` 에셋이 다시 직렬화될 때 갱신되므로, 리네임 직후가 아니라 다음 저장·도메인 리로드 시점에 반영된다.
- 씬 목록은 `Assets/Settings/Scenes/SceneNameSO.asset` 하나에 모은다.
  **여기 등록한 씬은 MUST 빌드 씬 목록에도 등록한다.** 누락은 런타임 로드 실패로만 드러난다.

## 5. 프리팹

- 재사용 대상은 MUST 프리팹 **에셋**으로 만든다. `GameObject.CreatePrimitive`로 만든 익명 씬 오브젝트를 그대로 두지 않는다.
- 프리팹 배리언트를 우선 쓰고, 프리팹 인스턴스의 오버라이드를 남발하지 않는다.
- 프리팹 루트에는 `NetworkObject`를 둔다(자식에 두지 않는다).
- 네트워크 프리팹은 MUST NetworkPrefabsList(`Assets/DefaultNetworkPrefabs.asset`)에 등록한다.
  등록을 빠뜨리면 **에러 없이 조용히 스폰이 실패**한다 → [../architecture/networking.md](../architecture/networking.md)

### 5.1 프리팹 에셋 ≠ 런타임 스폰

**이 둘을 혼동하지 않는다.**

| | 규칙 |
| --- | --- |
| 프리팹 **에셋**으로 만든다 | MUST — 재사용·일괄 수정·배리언트를 위해 |
| 런타임에 **스폰**한다 | 대상에 따라 다름. 아래 표 |

| 대상 | 배치 방식 | 이유 |
| --- | --- | --- |
| 가구, 문, 붙박이, 방 프리셋 | **프리팹 인스턴스를 씬에 배치** | 레벨 디자인이 씬 파일에 남고, 접속 시 스폰 폭풍이 없으며, 라이트맵·정적 배칭 대상이 된다 → [ADR-0009](../architecture/decisions/ADR-0009-scene-placed-level-objects.md) |

**한 종류 = 한 프리팹.** 같은 이름의 가구가 자리마다 다른 치수를 가지면 프리팹으로 대표할 수
없다. 벽 방향이 다른 자리는 치수를 바꿔 넘기지 말고 **회전으로** 맞춘다.
씬 인스턴스의 오버라이드는 **위치·회전에 한정**한다.
| 플레이어 | NGO Player Prefab 자동 스폰 | 접속 인원이 가변 |
| 투사체·이펙트 등 런타임 생성물 | 서버 스폰 | 개수가 사전에 정해지지 않음 |

### 5.2 씬 배치 NetworkObject의 GlobalObjectIdHash 함정

`NetworkObject.OnValidate`는 **씬이 저장되어 영구 ID가 생기고 그 씬이 Build Settings에 들어 있어야만**
(`buildIndex >= 0`) 해시를 계산한다. 생성 중인 새 씬은 둘 다 아니라 해시가 `0`으로 남고,
`0`이 여럿이면 클라이언트가 씬 오브젝트를 찾지 못한다.

- 새 씬은 MUST 저장·Build Settings 등록 뒤 에디터에서 다시 열어 씬의 모든 `NetworkObject`에 `OnValidate`를 돌리고
  한 번 더 저장한다.
- 그다음 해시가 0/중복이 아니고 `m_InScenePlaced`가 참인지 MUST 검사한다.
- 씬 파일을 **텍스트로 복사**하면(새 GUID) 안의 해시는 원본 씬 기준 값 그대로다. 텍스트로 `NetworkObject`를 새로 넣으면 해시가 없다.
  어느 쪽이든 그 씬을 Build Settings에 넣고 위 절차로 확정·저장한다.
- 이 절차를 하던 도우미(`PrototypeSceneSetup.RefreshScenePlacedNetworkObjectsInCurrentScene`)는 ADR-0020으로 지웠다.
  필요하면 git 이력의 구현을 참고해 그 작업용 에디터 스크립트를 잠깐 만든다.

**프리팹에도 같은 함정이 있다.** `PrefabUtility.SaveAsPrefabAsset`을 임시 **씬 오브젝트**에 대해 부르면
`OnValidate`가 에셋이 아니라 씬 기준으로 해시를 계산해서 **모든 프리팹이 같은 해시**를 갖고
`m_InScenePlaced`가 true로 박힌다. 저장 후 `ImportAsset(ForceUpdate)`로 재계산시키고, 프리팹을 다시 더럽혀 저장해
디스크까지 내려보낸 뒤 네트워크 프리팹마다 해시가 0/중복이 아니고 `m_InScenePlaced`가 거짓인지 검사한다.

## 6. 씬

- 씬 계층 최상단은 카테고리 빈 오브젝트로 정리한다: `--- Environment ---`, `--- Lighting ---`, `--- Systems ---`, `--- UI ---`.
- NGO `NetworkManager` GameObject는 중첩을 허용하지 않으므로 카테고리 parenting의 예외로 씬 root에 둔다.
- 씬 간 참조를 만들지 않는다. 필요하면 서비스 로케이터를 경유한다 → [code-style.md §7.1](code-style.md).
- **씬의 어떤 오브젝트도 트랜스폼 스케일이 1이 아니면 안 된다.** 크기는 메시·콜라이더 치수로 맞춘다
  → [../architecture/map-generation.md](../architecture/map-generation.md)
- 씬 파일은 병합 충돌이 어렵다. 동시에 같은 씬을 편집하지 않는다 → [git.md](git.md)

## 7. 임포트 설정

| 에셋 | 기본 설정 |
| --- | --- |
| 텍스처 | 압축 사용, Mipmap은 3D만, UI 스프라이트는 Mipmap 끔 |
| 오디오 | SFX = Decompress on Load(짧은 것), BGM = Streaming |
| 모델 | 불필요한 Rig/Animation/Material 임포트 해제, Read/Write 끔 |

- 에셋 직렬화는 **Force Text**(`m_SerializationMode: 2`)를 유지한다. 바꾸면 diff·병합이 불가능해진다.
- 프로젝트 전역 임포트 기본값 변경은 사용자 승인 후.

## 8. 정리 대상 (현재 상태)

| 항목 | 문제 | 조치 |
| --- | --- | --- |
| ~~맵 가구~~ | ~~익명 씬 오브젝트~~ | ✅ MIG-6 — `Assets/Prefabs/Furniture/` 33종 |
| ~~문 5개~~ | ~~위와 동일~~ | ✅ MIG-6 — `Assets/Prefabs/Map/` 3종(1.5·1.2·0.9m) |
| 붙박이(주방 카운터·욕실 기구), 창·문틀 | `GameObject.CreatePrimitive`로 만든 익명 씬 오브젝트 | 프리팹화 미정 — 던질 수 없고 정적이라 이득이 작다 |
| `Assets/TutorialInfo/`, `Assets/Readme.asset` | Unity 템플릿 잔재 | 삭제 검토 |
| `Assets/Scenes/SampleScene.unity` | 템플릿 잔재 | 삭제 검토 |

---

관련: [code-style.md](code-style.md) · [git.md](git.md)

최종 갱신: 2026-09-28 (§1 에이전트가 씬·프리팹·에셋을 텍스트로 직접 수정·셸 이동 가능, §1.1 생성 도구 삭제(ADR-0020), §5.2 텍스트 복사 시 해시)
