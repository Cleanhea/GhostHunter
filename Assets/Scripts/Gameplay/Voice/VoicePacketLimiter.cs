using System.Collections.Generic;

namespace GhostHunter.Gameplay.Voice
{
    /// <summary>슬라이딩 1초 구간에 허용된 패킷 수를 제한한다.</summary>
    public sealed class VoicePacketLimiter
    {
        private readonly Queue<double> _accepted = new(30);
        public bool Accept(double now, int maximum)
        {
            while (_accepted.Count > 0 && now - _accepted.Peek() >= 1d) _accepted.Dequeue();
            if (_accepted.Count >= maximum) return false;
            _accepted.Enqueue(now);
            return true;
        }
        public void Clear() => _accepted.Clear();
    }
}
