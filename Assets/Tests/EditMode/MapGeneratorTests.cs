using System.Collections.Generic;
using GhostHunter.EditorTools;
using GhostHunter.Gameplay.Furniture;
using GhostHunter.Gameplay.Interaction;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>
    /// 맵 생성 도구를 메모리 위에서 통째로 돌리고 자체 검증 함수까지 통과시킨다.
    ///
    /// 이 테스트가 필요한 이유: `GhostHunter > 프로토타입 게임 생성`은 <b>Game 씬을 처음부터
    /// 다시 만드는</b> 되돌리기 어려운 작업이라 손으로 반복 실행하며 확인하기 어렵다.
    /// 생성물과 검증은 순수하게 씬 오브젝트 위에서 일어나므로, 에셋을 저장하지 않고
    /// 같은 코드를 그대로 돌려 볼 수 있다.
    ///
    /// <b>에셋을 만들지 않는다.</b> 설정 SO 와 머티리얼은 메모리 인스턴스로 만들어 넘긴다 —
    /// AssetDatabase 에 매이면 batchmode 에서 돌지 않는다 → workflow/testing.md §5.3
    /// </summary>
    public sealed class MapGeneratorTests
    {
        [Test]
        public void 게임플레이_집의_평면_배율은_1_5다()
        {
            Assert.AreEqual(1.5f, HousePrototypeBuilder.GameplayMapScale);
        }

        private readonly List<Object> _created = new();

        private HousePrototypeBuilder.Palette _palette;
        private HousePrototypeBuilder.PhysicsAssets _physics;
        private FurnitureCatalog _catalog;
        private GameObject _catalogStaging;

        [SetUp]
        public void SetUp()
        {
            _palette = new HousePrototypeBuilder.Palette(
                CreateMaterial(), CreateMaterial(), CreateMaterial(), CreateMaterial(),
                CreateMaterial(), CreateMaterial(), CreateMaterial(), CreateMaterial(),
                CreateMaterial(), CreateMaterial());

            _physics = new HousePrototypeBuilder.PhysicsAssets(
                CreateDefinition(8f, FurnitureWeightClass.Light),
                CreateDefinition(25f, FurnitureWeightClass.Heavy),
                Track(ScriptableObject.CreateInstance<FurnitureThrowSettings>()),
                CreateMaterial());

            _catalog = BuildInMemoryCatalog();
        }

        /// <summary>
        /// 원본을 프리팹으로 굽지 않고 메모리에 조립해 카탈로그를 만든다.
        /// 프리팹 저장은 AssetDatabase 가 필요해 batchmode 에서 실패한다(testing.md §5.3) —
        /// <see cref="FurnitureCatalog"/> 가 원본 종류를 가리지 않는 덕분에 그대로 쓸 수 있다.
        /// </summary>
        private FurnitureCatalog BuildInMemoryCatalog()
        {
            _catalogStaging = Track(new GameObject("__CatalogStaging"));
            _catalogStaging.SetActive(false);

            var catalog = new FurnitureCatalog();

            foreach ((string key, GameObject source) in
                     HousePrototypeBuilder.BuildFurnitureSources(
                         _palette,
                         _physics,
                         _catalogStaging.transform))
            {
                catalog.Register(key, source);
            }

            foreach ((string key, GameObject source) in
                     HousePrototypeBuilder.BuildDoorSources(_palette, _catalogStaging.transform))
            {
                catalog.Register(key, source);
            }

            return catalog;
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
            {
                if (_created[i] != null)
                    Object.DestroyImmediate(_created[i]);
            }

            _created.Clear();
            _catalog = null;
            _catalogStaging = null;
            Physics.SyncTransforms();
        }

        private T Track<T>(T target) where T : Object
        {
            _created.Add(target);
            return target;
        }

        private Material CreateMaterial()
        {
            // 렌더링 결과는 검사 대상이 아니다. 프로젝트가 URP 든 아니든 잡히는 셰이더면 된다.
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");

            Assert.IsNotNull(shader, "테스트용 셰이더를 찾지 못했습니다.");
            return Track(new Material(shader));
        }

        private FurnitureDefinition CreateDefinition(float mass, FurnitureWeightClass weightClass)
        {
            var definition = Track(ScriptableObject.CreateInstance<FurnitureDefinition>());
            PrototypeSceneSetup.SetFloat(definition, "_mass", mass);
            PrototypeSceneSetup.SetEnum(definition, "_weightClass", (int)weightClass);
            return definition;
        }

        private Transform TrackRoot(Transform root)
        {
            Track(root.gameObject);
            return root;
        }

        private static Transform[] CreatePlayerSpawns()
        {
            Vector3[] positions = HousePrototypeBuilder.PlayerSpawnPositions();
            var spawns = new Transform[positions.Length];
            for (int i = 0; i < positions.Length; i++)
            {
                var point = new GameObject($"PlayerSpawn_{i}");
                point.transform.position = positions[i];
                spawns[i] = point.transform;
            }

            return spawns;
        }

        /// <summary>
        /// 게임플레이용 집 + 가구 라이브러리 + 방 프리셋 전부를 만들고 도구 자신의 검증을 통과시킨다.
        /// 치수, 방 비어 있음, 가구 배선·겹침, 프리셋 전 조합 배치, 문 개구부, 전 방 이동 가능까지 본다.
        /// </summary>
        [Test]
        public void House_01_생성물이_ValidateLayout_을_통과한다()
        {
            Transform house = TrackRoot(HousePrototypeBuilder.Create(_palette, _catalog));
            Transform[] slots = HousePrototypeBuilder.CreateBedroomSlots(house);
            Transform library = TrackRoot(HousePrototypeBuilder.CreateFurnitureLibrary(_palette, _catalog));
            Transform presets = TrackRoot(HousePrototypeBuilder.CreateBedroomPresets(_palette, _catalog));

            Transform[] spawns = CreatePlayerSpawns();
            foreach (Transform spawn in spawns)
                Track(spawn.gameObject);

            Assert.DoesNotThrow(
                () => HousePrototypeBuilder.ValidateLayout(house, library, presets, slots, spawns));
        }

        /// <summary>
        /// 도면 배율(×1) 비교용 집은 방마다 가구가 깔려 있다. 문짝 동선과 통행로를 막지 않는지 본다.
        /// </summary>
        [Test]
        public void 도면배율_집이_ValidateFurnishedHouse_를_통과한다()
        {
            Transform original =
                TrackRoot(HousePrototypeBuilder.CreateOriginalScaleHouseRight(_palette, _catalog));

            Assert.DoesNotThrow(
                () => HousePrototypeBuilder.ValidateFurnishedHouse(
                    original,
                    HousePrototypeBuilder.OriginalMapScale));
        }

        /// <summary>
        /// MAP-1은 기존 두 집을 바꾸지 않고 별도 루트에 확정된 외곽·방 치수만 보여 준다.
        /// 계단 상세와 오픈 보이드는 아직 TBD라 실제 게임플레이 배선 대상이 아니다.
        /// </summary>
        [Test]
        public void 맵_v03_그레이박스가_확정_치수와_21개_방을_지킨다()
        {
            Transform graybox = TrackRoot(
                MansionGrayboxBuilder.Create(_palette, new Vector3(40f, 0f, 0f)));

            Assert.DoesNotThrow(() => MansionGrayboxBuilder.Validate(graybox));
            Assert.AreEqual(1f, MansionGrayboxBuilder.MapScale);
            Assert.AreEqual(20f, MansionGrayboxBuilder.MainFloorWidth);
            Assert.AreEqual(16f, MansionGrayboxBuilder.MainFloorDepth);
            Assert.AreEqual(14f, MansionGrayboxBuilder.AtticWidth);
            Assert.AreEqual(10f, MansionGrayboxBuilder.AtticDepth);
            Assert.IsNotNull(graybox.Find("Floor_02/TBD_Placeholders/OpenVoid_Extent_TBD"));
            Assert.IsNull(graybox.GetComponentInChildren<Unity.Netcode.NetworkObject>(true));
        }

        [Test]
        public void 맵_v03_그레이박스는_기존_두_집_오른쪽에서_3m_이상_떨어진다()
        {
            Transform gameplay = TrackRoot(HousePrototypeBuilder.Create(_palette, _catalog));
            Transform original =
                TrackRoot(HousePrototypeBuilder.CreateOriginalScaleHouseRight(_palette, _catalog));
            Transform[] preserved = { gameplay, original };

            Physics.SyncTransforms();
            Vector3 position = MansionGrayboxBuilder.RightSidePosition(preserved);
            Transform graybox = TrackRoot(MansionGrayboxBuilder.Create(_palette, position));
            Physics.SyncTransforms();

            Assert.DoesNotThrow(
                () => MansionGrayboxBuilder.ValidateRightSidePlacement(graybox, preserved));
            Assert.Greater(position.x, original.position.x);
        }

        /// <summary>
        /// 맵 배율은 <b>좌표에만</b> 곱한다. 트랜스폼 스케일을 쓰면 개구부 폭과 벽 높이까지 늘어난다
        /// → CLAUDE.md §3.5
        /// </summary>
        [Test]
        public void 생성된_오브젝트의_스케일이_전부_1이다()
        {
            Transform house = TrackRoot(HousePrototypeBuilder.Create(_palette, _catalog));
            Transform library = TrackRoot(HousePrototypeBuilder.CreateFurnitureLibrary(_palette, _catalog));
            Transform presets = TrackRoot(HousePrototypeBuilder.CreateBedroomPresets(_palette, _catalog));

            foreach (Transform root in new[] { house, library, presets })
            {
                foreach (Transform item in root.GetComponentsInChildren<Transform>(true))
                {
                    // 큐브 파츠는 메시 스케일로 크기를 만든다. 검사 대상은 그룹·가구 루트다.
                    if (item.GetComponent<MeshFilter>() != null)
                        continue;

                    Assert.AreEqual(
                        Vector3.one,
                        item.localScale,
                        $"{GetPath(item)} 의 스케일이 1이 아닙니다.");
                }
            }
        }

        /// <summary>
        /// 가구 라이브러리는 카탈로그 전 종류를 하나씩 진열한다. 하나라도 빠지면 방을 꾸밀 때
        /// 복사해 붙일 견본이 없다.
        /// </summary>
        [Test]
        public void 가구_라이브러리가_카탈로그_전_종류를_하나씩_진열한다()
        {
            Transform library = TrackRoot(HousePrototypeBuilder.CreateFurnitureLibrary(_palette, _catalog));

            var seen = new HashSet<string>();
            foreach (FurnitureGrabTarget furniture in
                     library.GetComponentsInChildren<FurnitureGrabTarget>(true))
            {
                Assert.IsTrue(
                    seen.Add(furniture.name),
                    $"가구 라이브러리에 '{furniture.name}' 이 둘 이상 있습니다.");
            }

            foreach (string key in _catalog.Keys)
            {
                // 문은 진열 대상이 아니다 — 던질 수 있는 가구만 라이브러리에 올린다.
                if (key.StartsWith("Door_"))
                    continue;

                Assert.IsTrue(seen.Contains(key), $"가구 라이브러리에 '{key}' 이(가) 없습니다.");
            }
        }

        /// <summary>
        /// 방에 놓인 가구는 전부 카탈로그 원본에서 나온다. 종류가 겹쳐도 같은 모양이어야
        /// 프리팹 하나로 대표할 수 있다 — 예전에는 같은 이름이 치수만 다르게 두 벌 있었다.
        /// </summary>
        [Test]
        public void 같은_이름의_가구는_어디에_놓여도_같은_크기다()
        {
            Transform presets = TrackRoot(HousePrototypeBuilder.CreateBedroomPresets(_palette, _catalog));
            Transform original =
                TrackRoot(HousePrototypeBuilder.CreateOriginalScaleHouseRight(_palette, _catalog));

            var footprints = new Dictionary<string, Vector3>();

            foreach (Transform root in new[] { presets, original })
            {
                foreach (FurnitureGrabTarget furniture in
                         root.GetComponentsInChildren<FurnitureGrabTarget>(true))
                {
                    Bounds bounds = LocalColliderBounds(furniture.transform);
                    string key = StripInstanceSuffix(furniture.name);

                    if (!footprints.TryGetValue(key, out Vector3 known))
                    {
                        footprints.Add(key, bounds.size);
                        continue;
                    }

                    Assert.AreEqual(
                        known.x, bounds.size.x, 0.01f, $"'{key}' 의 폭이 자리마다 다릅니다.");
                    Assert.AreEqual(
                        known.y, bounds.size.y, 0.01f, $"'{key}' 의 높이가 자리마다 다릅니다.");
                    Assert.AreEqual(
                        known.z, bounds.size.z, 0.01f, $"'{key}' 의 깊이가 자리마다 다릅니다.");
                }
            }
        }

        /// <summary>
        /// 던지는 가구의 Rigidbody 설정. `ValidatePhysicsFurniture` 는 컴포넌트 유무만 보므로
        /// <b>값</b>은 여기서 고정한다 — 특히 <c>Continuous Dynamic</c> 은 빠르게 날아가는 가구가
        /// 벽을 뚫지 않게 하는 유일한 방어다(기본 Discrete 로는 한 물리 스텝에 벽을 건너뛴다).
        /// → [furniture-physics.md](../../../docs/architecture/furniture-physics.md)
        ///
        /// 실제로 벽을 안 뚫는지는 던져 봐야 안다(roadmap M7). 여기서는 설정이 조용히 바뀌는 것만 막는다.
        /// </summary>
        [Test]
        public void 가구_Rigidbody_가_투척_설정을_지킨다()
        {
            Transform library = TrackRoot(HousePrototypeBuilder.CreateFurnitureLibrary(_palette, _catalog));

            FurnitureGrabTarget[] furniture = library.GetComponentsInChildren<FurnitureGrabTarget>(true);
            Assert.Greater(furniture.Length, 0, "가구 라이브러리가 비어 있습니다.");

            foreach (FurnitureGrabTarget item in furniture)
            {
                var body = item.GetComponent<Rigidbody>();
                Assert.IsNotNull(body, $"'{item.name}' 에 Rigidbody 가 없습니다.");

                Assert.AreEqual(
                    CollisionDetectionMode.ContinuousDynamic,
                    body.collisionDetectionMode,
                    $"'{item.name}' 이 벽을 뚫을 수 있는 충돌 판정입니다.");
                Assert.AreEqual(
                    RigidbodyInterpolation.Interpolate,
                    body.interpolation,
                    $"'{item.name}' 의 보간이 꺼져 있습니다.");
                Assert.IsTrue(
                    body.isKinematic,
                    $"'{item.name}' 이 씬 저장 시점에 물리로 깨어 있습니다. " +
                    "세션 시작 전에도 굴러떨어집니다.");
                Assert.IsTrue(body.useGravity, $"'{item.name}' 의 중력이 꺼져 있습니다.");
                Assert.Greater(body.mass, 0f, $"'{item.name}' 의 질량이 0 입니다.");
            }
        }

        /// <summary>
        /// 무거운 가구는 2인이라야 제대로 던진다. 질량이 <see cref="FurnitureDefinition"/> 에서
        /// 오지 않으면 무게 등급이 의미를 잃는다.
        /// </summary>
        [Test]
        public void 가구_질량이_무게_등급을_따른다()
        {
            Transform library = TrackRoot(HousePrototypeBuilder.CreateFurnitureLibrary(_palette, _catalog));

            foreach (FurnitureNetworkPhysics physics in
                     library.GetComponentsInChildren<FurnitureNetworkPhysics>(true))
            {
                Assert.IsNotNull(physics.Definition, $"'{physics.name}' 에 FurnitureDefinition 이 없습니다.");
                Assert.AreEqual(
                    physics.Definition.Mass,
                    physics.GetComponent<Rigidbody>().mass,
                    0.001f,
                    $"'{physics.name}' 의 질량이 정의와 다릅니다.");
            }
        }

        /// <summary>
        /// 문짝은 프리팹 하나(경첩에서 +X 로 뻗은 문짝)를 회전만 바꿔 다섯 개구부에 쓴다.
        /// 회전을 잘못 잡으면 문이 벽 안쪽으로 들어가거나 반대로 열려서, 겉보기에는 멀쩡하고
        /// 플레이해야만 드러난다. 닫힘·열림 양쪽에서 문짝이 경첩 기준 어디에 서는지 고정한다.
        /// </summary>
        [Test]
        public void 문짝이_닫힘_열림_양쪽에서_경첩_기준_제자리에_선다()
        {
            Transform house = TrackRoot(HousePrototypeBuilder.Create(_palette, _catalog));

            var expected = new Dictionary<string, (Vector2 Closed, Vector2 Open)>
            {
                ["FrontDoor_1.5m"] = (new Vector2(-0.75f, 0f), new Vector2(0f, 0.75f)),
                ["Bedroom01_Door_1.2m"] = (new Vector2(0.6f, 0f), new Vector2(0f, 0.6f)),
                ["Bedroom02_Door_1.2m"] = (new Vector2(-0.6f, 0f), new Vector2(0f, 0.6f)),
                ["Bathroom_Door_0.9m"] = (new Vector2(0f, -0.45f), new Vector2(0.45f, 0f)),
                ["Storage_Door_0.9m"] = (new Vector2(0f, -0.45f), new Vector2(0.45f, 0f)),
            };

            DoorInteractable[] doors = house.GetComponentsInChildren<DoorInteractable>(true);
            Assert.AreEqual(expected.Count, doors.Length, "여닫이 문 개수가 다릅니다.");

            foreach (DoorInteractable door in doors)
            {
                Assert.IsTrue(expected.ContainsKey(door.name), $"모르는 문 '{door.name}' 입니다.");
                (Vector2 closed, Vector2 open) = expected[door.name];

                AssertLeafOffset(door, "_closedYaw", closed, "닫힘");
                AssertLeafOffset(door, "_openYaw", open, "열림");
            }
        }

        private static void AssertLeafOffset(
            DoorInteractable door,
            string yawField,
            Vector2 expected,
            string label)
        {
            var serialized = new SerializedObject(door);
            float yaw = serialized.FindProperty(yawField).floatValue;

            Transform leaf = door.transform.Find("DoorLeaf");
            Assert.IsNotNull(leaf, $"'{door.name}' 에 DoorLeaf 가 없습니다.");

            Quaternion restore = door.transform.localRotation;
            door.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

            Vector3 offset = leaf.position - door.transform.position;
            door.transform.localRotation = restore;

            Assert.AreEqual(expected.x, offset.x, 0.01f, $"'{door.name}' {label} 문짝 X");
            Assert.AreEqual(expected.y, offset.z, 0.01f, $"'{door.name}' {label} 문짝 Z");
        }

        /// <summary>회전을 뺀 로컬 기준 크기. 벽 방향만 다른 같은 가구를 비교하기 위한 것이다.</summary>
        private static Bounds LocalColliderBounds(Transform root)
        {
            Quaternion restore = root.rotation;
            root.rotation = Quaternion.identity;
            Physics.SyncTransforms();

            bool hasBounds = false;
            Bounds bounds = default;
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
            {
                if (!hasBounds)
                {
                    bounds = collider.bounds;
                    hasBounds = true;
                    continue;
                }

                bounds.Encapsulate(collider.bounds);
            }

            root.rotation = restore;
            Physics.SyncTransforms();

            Assert.IsTrue(hasBounds, $"'{root.name}' 에 콜라이더가 없습니다.");
            return bounds;
        }

        /// <summary>머리맡/발치처럼 역할만 다른 인스턴스 이름에서 원본 키를 되찾는다.</summary>
        private static string StripInstanceSuffix(string instanceName)
        {
            const string head = "BedsideTable_Head_";
            const string foot = "BedsideTable_Foot_";

            if (instanceName.StartsWith(head))
                return "BedsideTable_" + instanceName.Substring(head.Length);
            if (instanceName.StartsWith(foot))
                return "BedsideTable_" + instanceName.Substring(foot.Length);
            if (instanceName.StartsWith("Chair_0.5x0.5"))
                return "Chair_0.5x0.5";

            return instanceName;
        }

        private static string GetPath(Transform target)
        {
            string path = target.name;
            for (Transform parent = target.parent; parent != null; parent = parent.parent)
                path = $"{parent.name}/{path}";

            return path;
        }
    }
}
