using System.Linq;
using GhostHunter.Gameplay.Furniture;
using GhostHunter.Gameplay.Lift;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>
    /// 리프트 프리팹·설정·씬 배치의 정적 배선을 검증한다(lift-system.md §6).
    /// 저장된 에셋이 원본이라(ADR-0020) 코드가 아니라 에셋을 읽는다.
    /// </summary>
    public sealed class LiftWiringTests
    {
        private const string PrefabPath = "Assets/Prefabs/Map/Lift.prefab";
        private const string NetworkPrefabsPath = "Assets/DefaultNetworkPrefabs.asset";
        private const string DefinitionPath = "Assets/Settings/Gameplay/FurnitureDefinition_Lift.asset";
        private const string DefaultSettingsPath = "Assets/Settings/Gameplay/LiftSettings_Default.asset";
        private const string TutorialSettingsPath = "Assets/Settings/Gameplay/LiftSettings_Tutorial.asset";
        private const string TutorialScene = "Assets/Scenes/Tutorial.unity";
        private const string Stage1Scene = "Assets/Scenes/Stage1.unity";

        private static GameObject LoadPrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, $"{PrefabPath} 가 없습니다.");
            return prefab;
        }

        [Test]
        public void 프리팹_루트에_본체_물리와_리프트_컴포넌트가_있다()
        {
            GameObject root = LoadPrefab();

            Assert.IsNotNull(root.GetComponent<NetworkObject>(), "NetworkObject 는 루트에 있어야 한다");
            Assert.IsNotNull(root.GetComponent<NetworkTransform>());
            Assert.IsNotNull(root.GetComponent<FurnitureNetworkPhysics>());
            Assert.IsNotNull(root.GetComponent<FurnitureGrabTarget>(), "던지기 파이프라인으로 밀린다");
            Assert.IsNotNull(root.GetComponent<FurnitureLauncher>());
            Assert.IsNotNull(root.GetComponent<FurnitureHoverMotor>());
            Assert.IsNotNull(root.GetComponent<FurnitureOutline>());
            Assert.IsNotNull(root.GetComponent<LiftController>());
            Assert.AreEqual(LayerMask.NameToLayer("Furniture"), root.layer);
        }

        [Test]
        public void 본체는_회전이_고정되어_뒤집히지_않는다()
        {
            var body = LoadPrefab().GetComponent<Rigidbody>();

            Assert.AreEqual(RigidbodyConstraints.FreezeRotation, body.constraints & RigidbodyConstraints.FreezeRotation);
            Assert.IsTrue(body.useGravity);
        }

        [Test]
        public void 발판은_본체와_따로_움직이는_키네마틱_몸체다()
        {
            GameObject root = LoadPrefab();
            LiftController lift = root.GetComponent<LiftController>();
            var so = new SerializedObject(lift);
            var platform = so.FindProperty("_platform").objectReferenceValue as Transform;
            Assert.IsNotNull(platform, "_platform 배선");

            var body = platform.GetComponent<Rigidbody>();
            Assert.IsNotNull(body);
            Assert.IsTrue(body.isKinematic);
            Assert.IsFalse(body.useGravity);
            Assert.IsNotNull(platform.GetComponent<LiftPlatform>());

            var deck = so.FindProperty("_deck").objectReferenceValue as BoxCollider;
            Assert.IsNotNull(deck, "_deck 배선");
            Assert.AreEqual(new Vector3(3f, 0.08f, 2f), deck.size, "프로토타입 발판 3×2m(기획서 §4)");
            Assert.IsTrue(deck.transform.IsChildOf(platform));
            Assert.AreNotEqual(LayerMask.NameToLayer("Furniture"), platform.gameObject.layer,
                "발판은 던지기 대상(Furniture 레이어)이 아니다 — 하부 본체만 민다");
        }

        [Test]
        public void 버튼은_발판_위와_본체에_상승_하강_두_개씩_있다()
        {
            LiftController lift = LoadPrefab().GetComponent<LiftController>();
            var so = new SerializedObject(lift);
            SerializedProperty list = so.FindProperty("_buttons");
            Assert.AreEqual(4, list.arraySize);

            var buttons = Enumerable.Range(0, list.arraySize)
                .Select(i => list.GetArrayElementAtIndex(i).objectReferenceValue as LiftButton).ToArray();
            Assert.IsTrue(buttons.All(b => b != null));
            Assert.AreEqual(2, buttons.Count(b => b.Direction == LiftDirection.Up));
            Assert.AreEqual(2, buttons.Count(b => b.Direction == LiftDirection.Down));
            Assert.IsTrue(buttons.All(b => b.GetComponent<Collider>() != null), "조준선이 맞을 콜라이더");

            var platform = so.FindProperty("_platform").objectReferenceValue as Transform;
            Assert.AreEqual(2, buttons.Count(b => b.transform.IsChildOf(platform)), "발판 위 버튼판(②)");
            Assert.AreEqual(2, buttons.Count(b => !b.transform.IsChildOf(platform)), "본체 바닥 버튼판(③)");
        }

        [Test]
        public void 하부_본체_콜라이더에_바퀴_마찰이_배선되어_있다()
        {
            LiftController lift = LoadPrefab().GetComponent<LiftController>();
            var so = new SerializedObject(lift);
            SerializedProperty wheels = so.FindProperty("_wheelColliders");
            Assert.GreaterOrEqual(wheels.arraySize, 1);

            var platform = so.FindProperty("_platform").objectReferenceValue as Transform;
            for (int i = 0; i < wheels.arraySize; i++)
            {
                var collider = wheels.GetArrayElementAtIndex(i).objectReferenceValue as Collider;
                Assert.IsNotNull(collider);
                Assert.IsFalse(collider.transform.IsChildOf(platform), "바퀴 마찰은 지면에 닿는 본체 콜라이더에 입힌다");
            }

            var settings = AssetDatabase.LoadAssetAtPath<LiftSettings>(DefaultSettingsPath);
            Assert.Less(settings.WheelFriction, 0.3f, "잘 굴러가려면 마찰이 낮아야 한다");
        }

        [Test]
        public void 프리팹의_모든_트랜스폼_스케일은_1이다()
        {
            var bad = LoadPrefab().GetComponentsInChildren<Transform>(true)
                .Where(t => t.localScale != Vector3.one)
                .Select(t => $"{t.name} {t.localScale}").ToList();

            CollectionAssert.IsEmpty(bad, "스케일이 1이 아닌 오브젝트:\n" + string.Join("\n", bad));
        }

        [Test]
        public void 네트워크_프리팹_목록에_등록되어_있다()
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsPath);
            Assert.IsNotNull(list);

            GameObject prefab = LoadPrefab();
            Assert.IsTrue(list.PrefabList.Any(entry => entry.Prefab == prefab),
                "Lift.prefab 이 DefaultNetworkPrefabs 에 없습니다 — 누락하면 접속이 조용히 실패한다.");
        }

        [Test]
        public void 리프트_가구_정의는_2인_잡기를_막고_수평으로만_민다()
        {
            var definition = AssetDatabase.LoadAssetAtPath<FurnitureDefinition>(DefinitionPath);
            Assert.IsNotNull(definition);

            Assert.AreEqual(1, definition.MaxHolders);
            Assert.IsTrue(definition.HorizontalLaunchOnly);
            Assert.Less(definition.LaunchSpeedScale, 1f);
            Assert.AreEqual(0, definition.MaximumCollisionDamage, "본체는 충돌 내구도를 깎지 않는다");

            var physics = LoadPrefab().GetComponent<FurnitureNetworkPhysics>();
            Assert.AreSame(definition, physics.Definition);
        }

        [Test]
        public void 설정_에셋은_Stage1_3m_Tutorial_발코니_3_57m다()
        {
            var normal = AssetDatabase.LoadAssetAtPath<LiftSettings>(DefaultSettingsPath);
            var tutorial = AssetDatabase.LoadAssetAtPath<LiftSettings>(TutorialSettingsPath);
            Assert.IsNotNull(normal);
            Assert.IsNotNull(tutorial);

            Assert.AreEqual(3f, normal.FloorHeight, 0.0001f, "프로토타입은 최대 3m(기획서 §4)");
            Assert.AreEqual(3.57f, tutorial.FloorHeight, 0.0001f, "Tutorial 발코니 바닥 높이");
            Assert.AreEqual(2, normal.StopCount);
            Assert.AreEqual(2, tutorial.StopCount);
        }

        [TestCase(TutorialScene, "LiftSettings_Tutorial")]
        [TestCase(Stage1Scene, "LiftSettings_Default")]
        public void 씬에_리프트가_한_대_배치되고_설정이_맞다(string scenePath, string expectedSettings)
        {
            Scene scene = SceneManager.GetSceneByPath(scenePath);
            bool openedHere = false;
            if (!scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                openedHere = true;
            }

            try
            {
                LiftController[] lifts = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<LiftController>(true)).ToArray();
                Assert.AreEqual(1, lifts.Length, $"{scenePath} 의 리프트 수");

                LiftController lift = lifts[0];
                Assert.AreEqual(expectedSettings, lift.Settings.name);
                Assert.IsTrue(PrefabUtility.IsPartOfPrefabInstance(lift.gameObject), "프리팹 인스턴스여야 한다");

                var no = lift.GetComponent<NetworkObject>();
                Assert.IsTrue(no.IsSceneObject == true, "씬 배치 NetworkObject 여야 한다(m_InScenePlaced)");

                var hashField = typeof(NetworkObject).GetField(
                    "GlobalObjectIdHash", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                uint hash = (uint)hashField.GetValue(no);
                Assert.AreNotEqual(0u, hash, "GlobalObjectIdHash 가 확정되지 않았다");

                // 씬 전체에서 해시 중복이 없어야 클라이언트가 씬 오브젝트를 찾는다.
                var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<NetworkObject>(true))
                    .Select(n => (uint)hashField.GetValue(n)).ToList();
                Assert.AreEqual(all.Count, all.Distinct().Count(), "NetworkObject 해시가 중복");

                foreach (Transform t in lift.GetComponentsInChildren<Transform>(true))
                    Assert.AreEqual(Vector3.one, t.localScale, t.name);
            }
            finally
            {
                if (openedHere && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void Tutorial_앞쪽_난간은_리프트_구간만_비워져_있다()
        {
            Scene scene = SceneManager.GetSceneByPath(TutorialScene);
            bool openedHere = false;
            if (!scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(TutorialScene, OpenSceneMode.Additive);
                openedHere = true;
            }

            try
            {
                Transform[] all = scene.GetRootGameObjects()
                    .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
                Transform original = all.First(t => t.name == "Balcony_Railing");
                Assert.IsFalse(original.gameObject.activeSelf, "원래 앞쪽 난간 한 덩어리는 꺼 둔다");
                Assert.IsTrue(all.Any(t => t.name == "Rail_Left"), "개구부 서쪽 난간 조각");
                Assert.IsTrue(all.Any(t => t.name == "Rail_Right"), "개구부 동쪽 난간 조각");
            }
            finally
            {
                if (openedHere && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
