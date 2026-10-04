using System.Collections.Generic;
using GhostHunter.Gameplay.Furniture;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>
    /// 2인 운반 끼임 보조(throw-system.md §3.2) — 후보 순서와, 문틀 모형에서 비는 자세를 찾는지·
    /// 운반 후 충돌을 떨어질 때까지 다시 켜지 않는지(§3.3)를 검증한다.
    /// </summary>
    public sealed class FurnitureSqueezeAssistTests
    {
        private readonly List<GameObject> _created = new();

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

        [Test]
        public void BuildCandidates_보정_없음부터_작은_보정_순서로_채운다()
        {
            var candidates = new List<FurnitureSqueezeAssist.Candidate>();
            FurnitureSqueezeAssist.BuildCandidates(40f, 10f, 0.3f, 0.1f, candidates);

            Assert.AreEqual(9 * 7, candidates.Count);
            Assert.IsTrue(candidates[0].IsIdentity, "첫 후보는 보정 없음");
            for (int i = 1; i < candidates.Count; i++)
                Assert.LessOrEqual(candidates[i - 1].Cost, candidates[i].Cost + 0.0001f);

            // 회전 한 칸(10/40)이 옆 이동 한 칸(0.1/0.3)보다 작은 보정이다.
            Assert.AreEqual(10f, candidates[1].Yaw, 0.001f);
            Assert.AreEqual(-10f, candidates[2].Yaw, 0.001f);
            Assert.AreEqual(0.1f, candidates[3].Lateral, 0.001f);
            Assert.AreEqual(-0.1f, candidates[4].Lateral, 0.001f);
        }

        [Test]
        public void BuildCandidates_범위가_0이면_보정_없음만_남는다()
        {
            var candidates = new List<FurnitureSqueezeAssist.Candidate>();
            FurnitureSqueezeAssist.BuildCandidates(0f, 10f, 0f, 0.1f, candidates);

            Assert.AreEqual(1, candidates.Count);
            Assert.IsTrue(candidates[0].IsIdentity);
        }

        [Test]
        public void TryFindClearPose_문틀에_걸린_가구를_옆으로_비켜_통과시킨다()
        {
            CreateDoorway();
            // 바닥 윗면(0.55)이 가구 밑면(0.5)을 5cm 파고든다 — 위아래 겹침은 막힘으로 치지 않아야 한다.
            CreateBox("Floor", new Vector3(0f, 0.3f, 0f), new Vector3(6f, 0.5f, 6f));
            Rigidbody furniture = CreateFurniture(new Vector3(0.3f, 0.75f, -2f), new Vector3(0.8f, 0.5f, 0.6f),
                out Collider[] colliders);
            Physics.SyncTransforms();

            var probe = new FurnitureClearanceProbe(furniture, colliders, Physics.DefaultRaycastLayers);
            var inDoorway = new Vector3(0.3f, 0.75f, 0f);
            Assert.IsFalse(probe.IsPoseClear(inDoorway, Quaternion.identity, null), "오른쪽 문틀에 0.2m 걸린다");
            Assert.IsTrue(probe.IsPoseClear(new Vector3(0f, 0.75f, 0f), Quaternion.identity, null),
                "가운데면 지나간다 — 바닥에 닿은 겹침은 무시한다");

            // 앞면이 문틀 끝면(z −0.1)에 막 닿은 자세에서 앞으로 0.12m 나아가려 한다.
            var touching = new Vector3(0.3f, 0.75f, -0.4f);
            var candidates = new List<FurnitureSqueezeAssist.Candidate>();
            FurnitureSqueezeAssist.BuildCandidates(40f, 10f, 0.3f, 0.1f, candidates);
            Assert.IsTrue(probe.TryFindClearPose(
                touching, Quaternion.identity, Vector3.forward, 0.12f, Vector3.right, candidates, null,
                out FurnitureSqueezeAssist.Candidate found));

            Assert.AreEqual(-0.2f, found.Lateral, 0.001f, "왼쪽으로 0.2m 비키는 것이 가장 작은 보정");
            Assert.AreEqual(0f, found.Yaw, 0.001f);
        }

        [Test]
        public void CanShift_도중에_문틀을_파고드는_회전은_거절한다()
        {
            CreateDoorway();
            // 1.8m 길이 소파 앞면이 문틀 끝면에 닿아 있고 오른쪽 0.05m 가 문틀과 겹치는 줄에 있다.
            Rigidbody furniture = CreateFurniture(new Vector3(0.2f, 1f, -1f), new Vector3(0.7f, 0.5f, 1.8f),
                out Collider[] colliders);
            Physics.SyncTransforms();
            var probe = new FurnitureClearanceProbe(furniture, colliders, Physics.DefaultRaycastLayers);
            Vector3 position = furniture.position;

            Assert.IsTrue(probe.IsPoseClear(position, Quaternion.Euler(0f, -10f, 0f), null),
                "도착 자세만 보면 앞 모서리가 문 안쪽으로 비켜 비어 있다");
            Assert.IsFalse(probe.CanShift(position, Quaternion.identity, -10f, Vector3.zero, null),
                "돌아가는 도중 앞 모서리가 문틀 끝면을 파고든다");
            Assert.IsTrue(probe.CanShift(position, Quaternion.identity, 0f, Vector3.left * 0.1f, null),
                "옆으로 미는 건 겹침이 깊어지지 않는다");
        }

        [Test]
        public void TryFindClearPose_무시하는_콜라이더는_막지_않는다()
        {
            GameObject rightJamb = CreateDoorway();
            Rigidbody furniture = CreateFurniture(new Vector3(0.3f, 1f, -1f), new Vector3(0.8f, 0.5f, 0.6f),
                out Collider[] colliders);
            Physics.SyncTransforms();

            var probe = new FurnitureClearanceProbe(furniture, colliders, Physics.DefaultRaycastLayers);
            Collider jamb = rightJamb.GetComponent<Collider>();

            Assert.IsTrue(probe.IsPoseClear(new Vector3(0.3f, 1f, 0f), Quaternion.identity, other => other == jamb));
        }

        [Test]
        public void CollisionIgnoreSet_겹쳐_있는_동안은_충돌을_다시_켜지_않는다()
        {
            GameObject wall = CreateBox("Wall", new Vector3(0f, 1f, 0f), new Vector3(1f, 2f, 0.2f));
            CreateFurniture(new Vector3(0f, 1f, 0.2f), new Vector3(0.6f, 0.6f, 0.6f), out Collider[] colliders);
            Physics.SyncTransforms();

            var ignores = new FurnitureCollisionIgnoreSet(colliders);
            Collider wallCollider = wall.GetComponent<Collider>();
            ignores.Ignore(wallCollider);
            ignores.Release(wallCollider);
            ignores.Tick();
            Assert.IsTrue(ignores.Contains(wallCollider), "겹친 채로는 다시 켜지 않는다");

            wall.transform.position = new Vector3(0f, 1f, 3f);
            Physics.SyncTransforms();
            ignores.Tick();
            Assert.IsFalse(ignores.Contains(wallCollider), "떨어지면 다시 켠다");
            Assert.IsTrue(ignores.IsEmpty);
        }

        /// <summary>폭 1.0m 출입구(x −0.5~0.5, z −0.1~0.1). 오른쪽 문틀 기둥을 돌려준다.</summary>
        private GameObject CreateDoorway()
        {
            CreateBox("LeftJamb", new Vector3(-1f, 1f, 0f), new Vector3(1f, 2f, 0.2f));
            return CreateBox("RightJamb", new Vector3(1f, 1f, 0f), new Vector3(1f, 2f, 0.2f));
        }

        private Rigidbody CreateFurniture(Vector3 position, Vector3 size, out Collider[] colliders)
        {
            var root = new GameObject("Furniture");
            _created.Add(root);
            root.transform.position = position;
            Rigidbody body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;

            BoxCollider box = root.AddComponent<BoxCollider>();
            box.size = size;
            colliders = new Collider[] { box };
            return body;
        }

        private GameObject CreateBox(string name, Vector3 position, Vector3 size)
        {
            var box = new GameObject(name);
            _created.Add(box);
            box.transform.position = position;
            box.AddComponent<BoxCollider>().size = size;
            return box;
        }
    }
}
