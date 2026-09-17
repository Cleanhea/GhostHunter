using System;

namespace GhostHunter.Gameplay.Voice
{
    /// <summary>고정 길이 압축 프레임 큐. 선행 음성을 보존하고 오래된 프레임부터 버린다.</summary>
    public sealed class VoiceFrameQueue
    {
        public const int MaximumPayload = 512;
        private const int Capacity = 8;
        private readonly byte[][] _frames = new byte[Capacity][];
        private readonly int[] _lengths = new int[Capacity];
        private readonly double[] _times = new double[Capacity];
        private int _head;
        private int _count;
        public VoiceFrameQueue()
        {
            for (int i = 0; i < Capacity; i++) _frames[i] = new byte[MaximumPayload];
        }
        public void Clear() { _head = 0; _count = 0; }
        public void Push(byte[] frame, int count, double time)
        {
            if (count <= 0 || count > MaximumPayload - 2) return;
            if (_count == Capacity) Pop();
            int index = (_head + _count) % Capacity;
            Buffer.BlockCopy(frame, 0, _frames[index], 0, count);
            _lengths[index] = count;
            _times[index] = time;
            _count++;
        }
        public void DiscardBefore(double time)
        {
            while (_count > 0 && _times[_head] < time) Pop();
        }
        public int Pack(byte[] destination)
        {
            int total = 0;
            for (int i = 0; i < _count; i++) total += _lengths[(_head + i) % Capacity] + 2;
            // MTU 상한에서 앞부분을 버린다. Steam 압축 블록 자체를 잘라 보내면 해독할 수 없다.
            while (total > MaximumPayload && _count > 1) { total -= _lengths[_head] + 2; Pop(); }
            int offset = 0;
            while (_count > 0)
            {
                int length = _lengths[_head];
                destination[offset++] = (byte)length;
                destination[offset++] = (byte)(length >> 8);
                Buffer.BlockCopy(_frames[_head], 0, destination, offset, length);
                offset += length;
                Pop();
            }
            return offset;
        }
        private void Pop() { _head = (_head + 1) % Capacity; _count--; }
    }
}

