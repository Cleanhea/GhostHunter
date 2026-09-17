using System;

namespace GhostHunter.Gameplay.Voice
{
    /// <summary>이력과 행오버로 문장 중간의 게이트 떨림을 억제한다.</summary>
    public sealed class VoiceActivityGate
    {
        private float _quietSeconds;
        public bool IsOpen { get; private set; }
        public bool Step(float decibels, float seconds, float open, float close, float hangover)
        {
            if (!IsOpen)
            {
                IsOpen = decibels > open;
                _quietSeconds = 0f;
            }
            else if (decibels < close)
            {
                _quietSeconds += seconds;
                if (_quietSeconds >= hangover) IsOpen = false;
            }
            else _quietSeconds = 0f;
            return IsOpen;
        }
        public void Reset() { IsOpen = false; _quietSeconds = 0f; }
        public static float Decibels(float[] samples, int offset, int count)
        {
            if (count <= 0) return -120f;
            double sum = 0;
            for (int i = offset; i < offset + count; i++) sum += samples[i] * samples[i];
            return (float)(20 * Math.Log10(Math.Max(1e-6, Math.Sqrt(sum / count))));
        }
    }
}
