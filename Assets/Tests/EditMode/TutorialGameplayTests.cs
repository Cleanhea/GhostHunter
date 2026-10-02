using System;
using System.Collections.Generic;
using System.Linq;
using GhostHunter.Gameplay.Cleaning;
using GhostHunter.Gameplay.Furniture;
using GhostHunter.Gameplay.Ghost;
using GhostHunter.Gameplay.Map;
using GhostHunter.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GhostHunter.Tests.EditMode
{
    public sealed class TutorialGameplayTests
    {
        private Scene _scene;
        private FurnitureSpawnController _furniture;

        [OneTimeSetUp]
        public void Open()
        {
            _scene = EditorSceneManager.OpenScene("Assets/Scenes/Tutorial.unity", OpenSceneMode.Additive);
            _furniture = _scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<FurnitureSpawnController>()).Single();
        }

        [OneTimeTearDown]
        public void Close() => EditorSceneManager.CloseScene(_scene, true);

        [Test]
        public void 반출_가구는_고정하고_네_소품의_자리만_섞는다()
        {
            var layouts = new HashSet<string>();
            for (int seed = 0; seed < 32; seed++)
            {
                Assert.IsTrue(_furniture.TryBuildPlan(seed, out var plan, out var requests, out string error), error);
                Assert.AreEqual(10, plan.Length);
                Assert.AreEqual(6, requests.Count(r => r.IsTarget));
                var used = new HashSet<int>();
                var blue = new List<int>();
                for (int i = 0; i < plan.Length; i++)
                {
                    Assert.IsTrue(used.Add(plan[i].Point));
                    RandomFurnitureItem item = _furniture.Items[plan[i].Item];
                    FurnitureSpawnPoint point = _furniture.Points[plan[i].Point];
                    if (requests[i].IsTarget)
                        Assert.AreEqual(item.PoolId + "Point", point.name);
                    else
                    {
                        StringAssert.StartsWith("BluePropPoint", point.name);
                        blue.Add(plan[i].Point);
                    }
                }
                layouts.Add(string.Join(",", blue));
            }
            Assert.Greater(layouts.Count, 1);
            foreach (RandomFurnitureItem item in _furniture.Items)
                foreach (Transform child in item.GetComponentsInChildren<Transform>(true))
                    Assert.AreEqual(Vector3.one, child.localScale, child.name);
        }

        [Test]
        public void 고정_네_얼룩과_다섯_후보에서_세_얼룩을_배치한다()
        {
            CleaningController controller = _scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<CleaningController>()).Single();
            var serialized = new SerializedObject(controller);
            Assert.AreEqual(4, serialized.FindProperty("_fixedPoints").arraySize);
            Assert.AreEqual(5, serialized.FindProperty("_points").arraySize);
            Assert.AreEqual(7, controller.Stains.Length);
            var settings = (CleaningSettings)serialized.FindProperty("_settings").objectReferenceValue;
            Assert.AreEqual(7, settings.StainCount);
            for (int i = 0; i < controller.Stains.Length; i++)
            {
                string material = controller.Stains[i].GetComponentInChildren<Renderer>().sharedMaterial.name;
                StringAssert.Contains(i < 4 ? "Red" : "Pink", material);
            }
        }

        [Test]
        public void 욕실은_고정이고_책은_누워서_쌓여있다()
        {
            GameObject map = _scene.GetRootGameObjects().Single(g => g.name == "TutorialMap");
            foreach (string name in new[] { "Room202_Bath_Sink", "Room202_Bath_Toilet", "Room202_Bath_Tub", "Room202_Bath_MedicineCabinet" })
            {
                Transform fixture = map.GetComponentsInChildren<Transform>(true).Single(t => t.name == name);
                Assert.IsTrue(fixture.gameObject.activeInHierarchy);
                Assert.IsNull(fixture.GetComponentInParent<FurnitureGrabTarget>());
                Assert.IsNull(fixture.GetComponentInParent<Rigidbody>());
            }
            RandomFurnitureItem books = _furniture.Items.Single(i => i.PoolId == "TutorialBooks");
            MeshFilter[] layers = books.GetComponentsInChildren<MeshFilter>().Where(f => f.name.StartsWith("LyingBook") && !f.name.EndsWith("_Outline")).ToArray();
            Assert.AreEqual(7, layers.Length);
            foreach (MeshFilter layer in layers)
                Assert.Less(layer.sharedMesh.bounds.size.y, .1f);
            Assert.Greater(books.LocalBounds.size.y, .6f);
            Assert.LessOrEqual(books.LocalBounds.size.x, .5f);
            Assert.IsNotNull(_scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<TutorialHud>()).Single());
        }

        [Test]
        public void 매_판_선택한_현상_세_종류만_반복하고_복원한다()
        {
            GhostPrototypeSettings settings = AssetDatabase.LoadAssetAtPath<GhostPrototypeSettings>("Assets/Settings/Gameplay/GhostPrototypeSettings_Tutorial.asset");
            GameObject map = _scene.GetRootGameObjects().Single(g => g.name == "TutorialMap");
            foreach (MeshCollider collider in map.GetComponentsInChildren<MeshCollider>())
                Assert.IsTrue(collider.sharedMesh.isReadable, collider.name + " 런타임 NavMesh 메시 읽기");
            var random = new System.Random(17);
            var director = new GhostPhenomenaDirector(settings, random.NextDouble);
            uint mask = director.PoolMask;
            Assert.AreEqual(3, Enumerable.Range(1, 8).Count(i => (mask & (1u << i)) != 0));
            Assert.AreEqual(0u, mask & ((1u << (int)GhostPhenomenonKind.DoorMove) | (1u << (int)GhostPhenomenonKind.DrawerOpen)));
            var seen = new HashSet<GhostPhenomenonKind>();
            GhostPhenomenonKind last = GhostPhenomenonKind.None;
            for (int i = 0; i < 100; i++)
            {
                GhostPhenomenonKind next = director.ForceNext(GhostPhase.Active, 100, false);
                Assert.AreNotEqual(last, next);
                Assert.AreNotEqual(0u, mask & (1u << (int)next));
                seen.Add(next); last = next;
            }
            Assert.AreEqual(3, seen.Count);
            var restored = new GhostPhenomenaDirector(settings, () => .9);
            restored.RestorePool(mask);
            restored.RestoreStageState(7f, last);
            Assert.AreEqual(mask, restored.PoolMask);
            Assert.AreEqual(last, restored.Last);
            Assert.AreEqual(7f, restored.SecondsUntilNext);
        }

        [Test]
        public void 지지_가구의_경계만_예외로_허용한다()
        {
            var box = new FurniturePlacementPlanner.Box(0, 0, 0, 1, 1, 1);
            var requests = new[] { new FurniturePlacementPlanner.Request(0), new FurniturePlacementPlanner.Request(1) };
            var candidates = new[] {
                new FurniturePlacementPlanner.Candidate(0, 0, 0, 202, box),
                new FurniturePlacementPlanner.Candidate(1, 1, 1, 202, box, 0),
            };
            Assert.IsTrue(FurniturePlacementPlanner.TryPlan(2, 2, requests, candidates, 0, 100,
                new System.Random(1), out _, out _));
            candidates[1] = new FurniturePlacementPlanner.Candidate(1, 1, 1, 202, box);
            Assert.IsFalse(FurniturePlacementPlanner.TryPlan(2, 2, requests, candidates, 0, 100,
                new System.Random(1), out _, out _));
        }
    }
}
