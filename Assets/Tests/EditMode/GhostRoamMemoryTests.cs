using GhostHunter.Gameplay.Ghost;
using NUnit.Framework;
using UnityEngine;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>귀신 배회 방문 기록 — 오래 안 간 곳·다른 층을 먼저 고르게 하는 점수.</summary>
    public sealed class GhostRoamMemoryTests
    {
        private const float CellSize = 3f;
        private const float MemorySeconds = 90f;

        [Test]
        public void 처음_가는_칸은_기억_시간만큼_오래된_것으로_친다()
        {
            var memory = new GhostRoamMemory(CellSize, MemorySeconds);

            Assert.AreEqual(MemorySeconds, memory.Staleness(new Vector3(10f, 0f, 10f), 5f));
        }

        [Test]
        public void 방금_지나간_칸은_점수가_낮고_시간이_지나면_회복된다()
        {
            var memory = new GhostRoamMemory(CellSize, MemorySeconds);
            Vector3 spot = new(10f, 0f, 10f);
            memory.MarkVisited(spot, 100f);

            Assert.AreEqual(0f, memory.Staleness(spot, 100f));
            Assert.AreEqual(30f, memory.Staleness(spot + new Vector3(0.5f, 0f, 0.5f), 130f), 1e-4f,
                "같은 칸 안은 같은 기록을 쓴다");
            Assert.AreEqual(MemorySeconds, memory.Staleness(spot, 1000f), "기억 시간을 넘기면 처음 가는 곳과 같다");
        }

        [Test]
        public void 위층은_같은_평면_위치라도_다른_칸이다()
        {
            var memory = new GhostRoamMemory(CellSize, MemorySeconds);
            Vector3 ground = new(10f, 0.05f, 10f);
            memory.MarkVisited(ground, 100f);

            Assert.AreEqual(MemorySeconds, memory.Staleness(ground + Vector3.up * 3f, 100f), "2층");
            Assert.AreEqual(MemorySeconds, memory.Staleness(ground + Vector3.up * 6f, 100f), "다락");
            Assert.AreEqual(0f, memory.Staleness(ground + Vector3.up * 0.9f, 100f), "경사로 아래쪽은 아직 1층");
        }

        [Test]
        public void 똑같이_안_간_곳이면_가까운_곳이_점수가_높다()
        {
            var memory = new GhostRoamMemory(CellSize, MemorySeconds);

            float near = memory.Score(new Vector3(20f, 0f, 0f), 8f, 1.6f, 0f);
            float far = memory.Score(new Vector3(40f, 0f, 0f), 40f, 1.6f, 0f);

            Assert.Greater(near, far);
        }

        [Test]
        public void 최근에_간_곳보다_멀어도_안_간_곳이_점수가_높다()
        {
            var memory = new GhostRoamMemory(CellSize, MemorySeconds);
            Vector3 visited = new(10f, 0f, 0f);
            Vector3 upstairs = new(10f, 3f, 12f);
            memory.MarkVisited(visited, 50f);

            float recent = memory.Score(visited, 8f, 1.6f, 60f);
            float fresh = memory.Score(upstairs, 25f, 1.6f, 60f);

            Assert.Greater(fresh, recent);
        }
    }
}
