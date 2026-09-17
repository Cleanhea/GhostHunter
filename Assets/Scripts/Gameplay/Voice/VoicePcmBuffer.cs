using System;
using System.Threading;

namespace GhostHunter.Gameplay.Voice
{
    /// <summary>단일 생산자/소비자 PCM 큐. 읽기 커서는 오디오 스레드만 변경한다.</summary>
    public sealed class VoicePcmBuffer
    {
        private readonly float[] _samples;
        private readonly int _maximumQueued;
        private long _written;
        private long _read;
        private long _discardBefore;
        public int Count => (int)Math.Max(0, Volatile.Read(ref _written) - Math.Max(Volatile.Read(ref _read), Volatile.Read(ref _discardBefore)));
        public VoicePcmBuffer(int capacity, int maximumQueued)
        {
            _samples = new float[capacity];
            _maximumQueued = Math.Min(capacity, maximumQueued);
        }
        public void Write(float[] source, int count)
        {
            long write = _written;
            long read = Math.Max(Volatile.Read(ref _read), Volatile.Read(ref _discardBefore));
            // 생산자는 읽는 슬롯을 덮어쓰지 않는다. 소비자가 다음 콜백에서 오래된 샘플을 건너뛴다.
            int available = _samples.Length - (int)(write - read);
            count = Math.Min(count, available);
            for (int i = 0; i < count; i++) _samples[(int)((write + i) % _samples.Length)] = source[i];
            Volatile.Write(ref _written, write + count);
        }
        public void Read(float[] destination)
        {
            long write = Volatile.Read(ref _written);
            long read = Math.Max(_read, Volatile.Read(ref _discardBefore));
            read = Math.Max(read, write - _maximumQueued);
            int count = (int)Math.Min(destination.Length, write - read);
            for (int i = 0; i < count; i++) destination[i] = _samples[(int)((read + i) % _samples.Length)];
            Array.Clear(destination, count, destination.Length - count);
            // Clear와 겹친 콜백은 재사용된 슬롯의 데이터를 출력하지 않는다.
            if (Volatile.Read(ref _discardBefore) > read) Array.Clear(destination, 0, destination.Length);
            Volatile.Write(ref _read, read + count);
        }
        public void Clear() => Volatile.Write(ref _discardBefore, Volatile.Read(ref _written));
    }
}

