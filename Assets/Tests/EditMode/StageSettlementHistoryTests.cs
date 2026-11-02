using System.Collections.Generic;
using GhostHunter.Core.Scenes;
using GhostHunter.Systems.SceneFlow;
using NUnit.Framework;

namespace GhostHunter.Tests.EditMode
{
    public sealed class StageSettlementHistoryTests
    {
        private static StageSettlementRecord Record(int delivered, bool wiped = false, int dead = 0)
            => new(delivered, 4, 50, wiped ? 0 : 1, 0, dead, wiped);

        [Test]
        public void 같은_판은_한번만_기록하고_다음_판의_같은_결과는_기록한다()
        {
            var history = new StageSettlementHistory();
            history.BeginStage();
            Assert.IsFalse(history.HasCurrentResult);
            Assert.IsTrue(history.TryRecord(Record(1)));
            Assert.IsFalse(history.TryRecord(Record(2)));
            Assert.AreEqual(1, history.Records.Count);
            Assert.AreEqual(1, history.Records[0].DeliveredFurniture);
            Assert.IsTrue(history.HasCurrentResult);
            history.BeginStage();
            Assert.IsFalse(history.HasCurrentResult);
            Assert.IsTrue(history.TryRecord(Record(1)));
            Assert.AreEqual(2, history.Records.Count);
        }

        [Test]
        public void 진행_중_수신한_정산은_중복_로컬_기록을_막고_전멸_사망수를_보관한다()
        {
            var history = new StageSettlementHistory();
            history.BeginStage();
            history.ImportPublished(Record(1, wiped: true, dead: 3), duringStage: true);
            Assert.IsTrue(history.HasCurrentResult);
            Assert.AreEqual(3, history.FailureDeadCount);
            Assert.IsFalse(history.TryRecord(Record(2)));
            Assert.AreEqual(1, history.Records.Count);
        }

        [Test]
        public void 이전_판_이력은_새_판의_정산_결과로_표시하지_않는다()
        {
            var history = new StageSettlementHistory();
            history.ImportPublished(Record(1, wiped: true, dead: 2), duringStage: false);
            history.BeginStage();
            Assert.AreEqual(0, history.FailureDeadCount);
            Assert.IsFalse(history.HasCurrentResult);
            Assert.IsTrue(history.TryRecord(Record(2)));
            Assert.IsTrue(history.HasCurrentResult);
        }

        [TestCase(-1, 0)]
        [TestCase(0, 0)]
        [TestCase(3, 3)]
        public void 실패_사망수는_음수가_되지_않고_이력을_추가하지_않는다(int value, int expected)
        {
            var history = new StageSettlementHistory();
            history.RecordFailure(value);
            Assert.AreEqual(expected, history.FailureDeadCount);
            Assert.IsEmpty(history.Records);
        }

        [Test]
        public void 일부_게시가_실패하면_성공한_기록을_반복하지_않고_실패한_기록부터_재시도한다()
        {
            var history = new StageSettlementHistory();
            history.QueuePublication(Record(1));
            history.QueuePublication(Record(2));
            var attempts = new List<int>();
            bool published = history.PublishPending(record =>
            {
                attempts.Add(record.DeliveredFurniture);
                return record.DeliveredFurniture == 1;
            });
            Assert.IsFalse(published);
            Assert.IsTrue(history.HasPendingPublications);
            CollectionAssert.AreEqual(new[] { 1, 2 }, attempts);
            Assert.IsTrue(history.PublishPending(record =>
            {
                attempts.Add(record.DeliveredFurniture);
                return true;
            }));
            CollectionAssert.AreEqual(new[] { 1, 2, 2 }, attempts);
            Assert.IsFalse(history.HasPendingPublications);
        }

        [Test]
        public void 게시_재시도는_1초_간격이며_대기열이_없으면_시도하지_않는다()
        {
            var history = new StageSettlementHistory();
            Assert.IsFalse(history.ShouldRetry(10f));
            history.QueuePublication(Record(1));
            Assert.IsTrue(history.ShouldRetry(10f));
            Assert.IsFalse(history.ShouldRetry(10.9f));
            Assert.IsTrue(history.ShouldRetry(11f));
            Assert.IsTrue(history.PublishPending(record => true));
            Assert.IsFalse(history.ShouldRetry(12f));
        }

        [Test]
        public void 방을_떠나면_이력과_미게시_결과를_버리고_새_판은_다시_기록한다()
        {
            var history = new StageSettlementHistory();
            history.TryRecord(Record(1));
            history.QueuePublication(Record(1));
            history.ClearLobby();
            Assert.IsEmpty(history.Records);
            Assert.IsFalse(history.HasPendingPublications);
            history.BeginStage();
            Assert.IsTrue(history.TryRecord(Record(2)));
        }
    }
}
