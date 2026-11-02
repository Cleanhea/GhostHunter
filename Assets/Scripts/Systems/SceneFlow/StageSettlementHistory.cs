using System;
using System.Collections.Generic;
using GhostHunter.Core.Scenes;

namespace GhostHunter.Systems.SceneFlow
{
    /// <summary>방 이력·판별 중복 방지·게시 대기열. 보상 지급과 로비 접근 권한은 Controller가 맡는다.</summary>
    internal sealed class StageSettlementHistory
    {
        private readonly List<StageSettlementRecord> _records = new();
        private readonly Queue<StageSettlementRecord> _pending = new();
        private bool _recorded;
        private int _stageStartCount;
        private float _nextPublishAttempt;

        public IReadOnlyList<StageSettlementRecord> Records => _records;
        public int FailureDeadCount { get; private set; }
        public bool HasCurrentResult => _records.Count > _stageStartCount;
        public bool HasPendingPublications => _pending.Count > 0;

        public void BeginStage()
        {
            FailureDeadCount = 0;
            _recorded = false;
            _stageStartCount = _records.Count;
        }

        public bool TryRecord(StageSettlementRecord record)
        {
            if (_recorded)
                return false;
            _records.Add(record);
            _recorded = true;
            return true;
        }

        public void RecordFailure(int deadCount) => FailureDeadCount = Math.Max(0, deadCount);

        public void ImportPublished(StageSettlementRecord record, bool duringStage)
        {
            int index = _records.Count;
            _records.Add(record);
            if (duringStage && index >= _stageStartCount)
                _recorded = true;
            if (record.TeamWiped)
                RecordFailure(record.Dead);
        }

        public void QueuePublication(StageSettlementRecord record) => _pending.Enqueue(record);

        /// <summary>게시가 성공할 때까지 선두 기록을 유지한다. 실패하면 같은 기록부터 다시 시도한다.</summary>
        public bool PublishPending(Func<StageSettlementRecord, bool> publish)
        {
            while (_pending.Count > 0)
            {
                if (!publish(_pending.Peek()))
                    return false;
                _pending.Dequeue();
            }
            return true;
        }

        public bool ShouldRetry(float now)
        {
            if (_pending.Count == 0 || now < _nextPublishAttempt)
                return false;
            _nextPublishAttempt = now + 1f;
            return true;
        }

        public void ClearLobby()
        {
            _records.Clear();
            _pending.Clear();
        }
    }
}
