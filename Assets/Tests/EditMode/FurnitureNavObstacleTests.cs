using System.Collections.Generic;
using GhostHunter.Gameplay.Furniture;
using GhostHunter.Gameplay.Ghost;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>
    /// 가구-몸 물리 충돌 변경(2026-11-07)의 순수 계산과 설정 값: 귀신 경로 장애물의 로컬 경계,
    /// 귀신·운반 충돌 설정의 기본값. 실제 충돌·carving 은 PlayMode <c>GhostFurnitureCollisionTests</c>.
    /// </summary>
    public sealed class FurnitureNavObstacleTests
    {
        private const string ThrowSettingsPath = "Assets/Settings/Gameplay/FurnitureThrowSettings_Default.asset";

        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
            {
                if (_created[i] != null)
                    Object.DestroyImmediate(_created[i]);
            }

            _created.Clear();
        }

        [Test]
        public void 가구가_돌아가_있어도_로컬_경계는_같다()
        {
            GameObject root = Create("Sofa", new Vector3(10f, 0f, 5f), Quaternion.Euler(0f, 90f, 0f));
            BoxCollider box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.5f, 0f);
            box.size = new Vector3(2f, 1f, 0.5f);

            Assert.IsTrue(FurnitureNavObstacle.TryComputeLocalBounds(root.transform, new[] { box }, out Bounds local));

            Assert.AreEqual(2f, local.size.x, 1e-4f);
            Assert.AreEqual(1f, local.size.y, 1e-4f);
            Assert.AreEqual(0.5f, local.size.z, 1e-4f);
            Assert.AreEqual(0.5f, local.center.y, 1e-4f);
        }

        [Test]
        public void 자식_콜라이더는_루트_기준으로_합쳐진다()
        {
            GameObject root = Create("Table", new Vector3(3f, 0f, 3f), Quaternion.Euler(0f, 30f, 0f));
            BoxCollider top = root.AddComponent<BoxCollider>();
            top.size = Vector3.one;
            GameObject legObject = Create("Leg", Vector3.zero, Quaternion.identity);
            legObject.transform.SetParent(root.transform, false);
            legObject.transform.localPosition = new Vector3(1f, 0f, 0f);
            BoxCollider leg = legObject.AddComponent<BoxCollider>();
            leg.size = Vector3.one;

            Assert.IsTrue(FurnitureNavObstacle.TryComputeLocalBounds(root.transform, new Collider[] { top, leg }, out Bounds local));

            Assert.AreEqual(2f, local.size.x, 1e-4f, "x: -0.5 ~ 1.5");
            Assert.AreEqual(0.5f, local.center.x, 1e-4f);
            Assert.AreEqual(1f, local.size.z, 1e-4f);
        }

        [Test]
        public void 꺼진_콜라이더도_형상으로_계산하고_트리거는_뺀다()
        {
            GameObject root = Create("Bed", Vector3.zero, Quaternion.identity);
            BoxCollider body = root.AddComponent<BoxCollider>();
            body.size = new Vector3(1f, 1f, 2f);
            body.enabled = false;
            BoxCollider trigger = root.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(9f, 9f, 9f);

            Assert.IsTrue(FurnitureNavObstacle.TryComputeLocalBounds(root.transform, new Collider[] { body, trigger }, out Bounds local));
            Assert.AreEqual(2f, local.size.z, 1e-4f, "트리거 9m 는 들어가지 않는다");

            Assert.IsFalse(FurnitureNavObstacle.TryComputeLocalBounds(root.transform, new Collider[] { trigger }, out _));
        }

        [Test]
        public void 낮거나_작은_소품은_경로_장애물이_아니다()
        {
            Assert.IsTrue(FurnitureNavObstacle.IsLargeEnough(new Vector3(0.6f, 0.8f, 0.4f)));
            Assert.IsFalse(FurnitureNavObstacle.IsLargeEnough(new Vector3(2f, 0.3f, 1f)), "턱보다 낮은 매트류");
            Assert.IsFalse(FurnitureNavObstacle.IsLargeEnough(new Vector3(0.2f, 1f, 0.2f)), "바닥 면적이 작은 소품");
        }

        [Test]
        public void 귀신은_기본적으로_가구와_충돌한다()
        {
            var settings = ScriptableObject.CreateInstance<GhostPrototypeSettings>();
            _created.Add(settings);
            Assert.IsTrue(settings.CollideWithFurniture);

            foreach (string guid in AssetDatabase.FindAssets("t:GhostPrototypeSettings"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<GhostPrototypeSettings>(path);
                Assert.IsTrue(asset.CollideWithFurniture, $"{path} 가 가구 충돌을 끄고 있다");
            }
        }

        [Test]
        public void 운반_중에도_홀더_몸과_가구는_충돌한다()
        {
            var created = ScriptableObject.CreateInstance<FurnitureThrowSettings>();
            _created.Add(created);
            Assert.IsFalse(created.CarryIgnoresHolders, "코드 기본값");

            var asset = AssetDatabase.LoadAssetAtPath<FurnitureThrowSettings>(ThrowSettingsPath);
            Assert.IsNotNull(asset, ThrowSettingsPath);
            Assert.IsFalse(asset.CarryIgnoresHolders, "기본 설정 에셋");
        }

        private GameObject Create(string name, Vector3 position, Quaternion rotation)
        {
            var go = new GameObject(name);
            go.transform.SetPositionAndRotation(position, rotation);
            _created.Add(go);
            return go;
        }
    }
}
