using System.Collections.Generic;
using GhostHunter.EditorTools;
using GhostHunter.Gameplay.Furniture;
using NUnit.Framework;
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
        private readonly List<Object> _created = new();

        private HousePrototypeBuilder.Palette _palette;
        private HousePrototypeBuilder.PhysicsAssets _physics;

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
            Transform house = TrackRoot(HousePrototypeBuilder.Create(_palette));
            Transform[] slots = HousePrototypeBuilder.CreateBedroomSlots(house);
            Transform library = TrackRoot(HousePrototypeBuilder.CreateFurnitureLibrary(_palette, _physics));
            Transform presets = TrackRoot(HousePrototypeBuilder.CreateBedroomPresets(_palette, _physics));

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
                TrackRoot(HousePrototypeBuilder.CreateOriginalScaleHouseRight(_palette, _physics));

            Assert.DoesNotThrow(
                () => HousePrototypeBuilder.ValidateFurnishedHouse(
                    original,
                    HousePrototypeBuilder.OriginalMapScale));
        }

        /// <summary>
        /// 맵 배율은 <b>좌표에만</b> 곱한다. 트랜스폼 스케일을 쓰면 개구부 폭과 벽 높이까지 늘어난다
        /// → CLAUDE.md §3.5
        /// </summary>
        [Test]
        public void 생성된_오브젝트의_스케일이_전부_1이다()
        {
            Transform house = TrackRoot(HousePrototypeBuilder.Create(_palette));
            Transform library = TrackRoot(HousePrototypeBuilder.CreateFurnitureLibrary(_palette, _physics));
            Transform presets = TrackRoot(HousePrototypeBuilder.CreateBedroomPresets(_palette, _physics));

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
        /// 가구 라이브러리는 종류별로 하나씩만 둔다. 이름이 겹치면 프리팹으로 뽑을 때
        /// 어느 쪽이 정본인지 알 수 없다.
        /// </summary>
        [Test]
        public void 가구_라이브러리에_같은_이름의_가구가_둘_이상_없다()
        {
            Transform library = TrackRoot(HousePrototypeBuilder.CreateFurnitureLibrary(_palette, _physics));

            var seen = new HashSet<string>();
            foreach (FurnitureGrabTarget furniture in
                     library.GetComponentsInChildren<FurnitureGrabTarget>(true))
            {
                Assert.IsTrue(
                    seen.Add(furniture.name),
                    $"가구 라이브러리에 '{furniture.name}' 이 둘 이상 있습니다.");
            }
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
