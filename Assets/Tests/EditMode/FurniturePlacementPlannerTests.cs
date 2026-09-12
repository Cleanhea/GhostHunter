using System;
using System.Collections.Generic;
using NUnit.Framework;
using static GhostHunter.Gameplay.Map.FurniturePlacementPlanner;

namespace GhostHunter.Tests.EditMode
{
    public sealed class FurniturePlacementPlannerTests
    {
        [Test]
        public void 계획_임시16개_수량과_중복없는배치를_여러시드에서_충족한다()
        {
            int[] counts = { 2, 3, 5, 6 };
            var requests = new List<Request>();
            var candidates = new List<Candidate>();
            int item = 0;
            for (int pool = 0; pool < counts.Length; pool++)
            {
                for (int i = 0; i < counts[pool]; i++, item++)
                {
                    requests.Add(new Request(pool));
                    for (int point = 0; point < 24; point++)
                        candidates.Add(At(pool, item, point, point / 3));
                }
            }
            var signatures = new HashSet<string>();
            for (int seed = 0; seed < 100; seed++)
            {
                Assert.IsTrue(TryPlan(16, 24, requests.ToArray(), candidates.ToArray(), 3, 20000,
                    new Random(seed), out Candidate[] plan, out string error), error);
                Assert.AreEqual(16, plan.Length);
                var items = new HashSet<int>();
                var points = new HashSet<int>();
                for (int i = 0; i < plan.Length; i++)
                {
                    Assert.AreEqual(requests[i].Pool, plan[i].Pool);
                    Assert.IsTrue(items.Add(plan[i].Item));
                    Assert.IsTrue(points.Add(plan[i].Point));
                }
                signatures.Add(string.Join(",", Array.ConvertAll(plan, value => value.Point)));
            }
            Assert.Greater(signatures.Count, 1, "서로 다른 시드가 같은 배치만 만들면 안 된다.");
        }

        [Test]
        public void 계획_같은시드는_동일한결과를_재현한다()
        {
            var requests = new[] { new Request(0), new Request(0) };
            Candidate[] candidates = { At(0, 0, 0), At(0, 0, 1), At(0, 1, 0), At(0, 1, 1) };
            Assert.IsTrue(TryPlan(2, 2, requests, candidates, 0, 100, new Random(42), out Candidate[] first, out _));
            Assert.IsTrue(TryPlan(2, 2, requests, candidates, 0, 100, new Random(42), out Candidate[] second, out _));
            CollectionAssert.AreEqual(first, second);
        }

        [Test]
        public void 계획_방별요청은_다른방후보를_사용하지않는다()
        {
            Candidate[] candidates = { At(0, 0, 0, 1), At(0, 0, 1, 2) };
            Assert.IsTrue(TryPlan(1, 2, new[] { new Request(0, 2) }, candidates, 0, 100,
                new Random(1), out Candidate[] plan, out _));
            Assert.AreEqual(2, plan[0].Room);
        }

        [Test]
        public void 계획_가구풀부족시_부분결과를_반환하지않는다()
        {
            Assert.IsFalse(TryPlan(1, 2, new[] { new Request(0), new Request(0) },
                new[] { At(0, 0, 0), At(0, 0, 1) }, 0, 100, new Random(1), out Candidate[] plan, out _));
            Assert.IsEmpty(plan);
        }

        [Test]
        public void 계획_종류에맞는후보가없으면_실패한다()
        {
            Assert.IsFalse(TryPlan(1, 1, new[] { new Request(1) }, new[] { At(0, 0, 0) },
                0, 100, new Random(1), out Candidate[] plan, out _));
            Assert.IsEmpty(plan);
        }

        [Test]
        public void 계획_같은위치의다른지점도_겹치면_거부한다()
        {
            Candidate[] candidates = { At(0, 0, 0), new(0, 1, 1, 0, new Box(0, 0, 0, 1, 1, 1)) };
            Assert.IsFalse(TryPlan(2, 2, new[] { new Request(0), new Request(0) }, candidates,
                0, 100, new Random(1), out Candidate[] plan, out _));
            Assert.IsEmpty(plan);
        }

        [Test]
        public void 계획_같은평면의다른층은_겹침으로_판정하지않는다()
        {
            Candidate[] candidates = { At(0, 0, 0), new(1, 1, 1, 1, new Box(0, 3, 0, 1, 4, 1)) };
            Assert.IsTrue(TryPlan(2, 2, new[] { new Request(0), new Request(1) }, candidates,
                0, 100, new Random(1), out _, out _));
        }

        [Test]
        public void 계획_방별작업상한을_초과하면_실패한다()
        {
            Assert.IsFalse(TryPlan(2, 2, new[] { new Request(0), new Request(0) },
                new[] { At(0, 0, 0), At(0, 1, 1) }, 1, 100, new Random(1), out _, out _));
        }

        [Test]
        public void 계획_일반소품은_작업가구상한에_포함하지않는다()
        {
            Assert.IsTrue(TryPlan(2, 2, new[] { new Request(0), new Request(1, -1, false) },
                new[] { At(0, 0, 0), At(1, 1, 1) }, 1, 100, new Random(1), out _, out _));
        }

        [Test]
        public void 계획_공유후보를_막는선택은_되돌려_다시탐색한다()
        {
            var requests = new[] { new Request(0), new Request(1), new Request(2) };
            Candidate[] candidates =
            {
                At(0, 0, 0), At(0, 0, 1),
                At(1, 1, 0), At(1, 1, 2),
                At(2, 2, 0), At(2, 2, 2),
            };
            for (int seed = 0; seed < 50; seed++)
                Assert.IsTrue(TryPlan(3, 3, requests, candidates, 0, 1000, new Random(seed), out _, out string error), error);
        }

        [Test]
        public void 계획_탐색상한에서_종료하고_부분배치를_버린다()
        {
            Assert.IsFalse(TryPlan(2, 2, new[] { new Request(0), new Request(1) },
                new[] { At(0, 0, 0), At(1, 1, 1) }, 0, 1, new Random(1), out Candidate[] plan, out _));
            Assert.IsEmpty(plan);
        }

        [Test]
        public void 계획_NaN경계는_거부한다()
        {
            Assert.IsFalse(TryPlan(1, 1, new[] { new Request(0) },
                new[] { new Candidate(0, 0, 0, 0, new Box(float.NaN, 0, 0, 1, 1, 1)) },
                0, 100, new Random(1), out _, out _));
        }

        [Test]
        public void 계획_범위밖식별자는_거부한다()
        {
            Assert.IsFalse(TryPlan(1, 1, new[] { new Request(0) }, new[] { At(0, 1, 0) },
                0, 100, new Random(1), out _, out _));
        }

        private static Candidate At(int pool, int item, int point, int room = 0)
        {
            return new Candidate(pool, item, point, room, new Box(point * 3, 0, 0, point * 3 + 1, 1, 1));
        }
    }
}
