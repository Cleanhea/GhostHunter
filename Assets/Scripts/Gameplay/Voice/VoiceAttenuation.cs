using System;

namespace GhostHunter.Gameplay.Voice
{
    /// <summary>엔진 상태에 의존하지 않는 음성 감쇠 공식.</summary>
    public static class VoiceAttenuation
    {
        public static float SmoothStep(float value)
        {
            value = Math.Max(0f, Math.Min(1f, value));
            return value * value * (3f - 2f * value);
        }
        public static float Horizontal(float distance, float minimum, float fade, float maximum, float exponent)
        {
            if (distance >= maximum) return 0f;
            float rolloff = (float)Math.Pow(minimum / Math.Max(distance, minimum), exponent);
            return rolloff * (1f - SmoothStep((distance - fade) / (maximum - fade)));
        }
        public static float Vertical(float distance, float near, float cut, bool verticalLinkShared = false)
        {
            return verticalLinkShared ? 1f : 1f - SmoothStep((Math.Abs(distance) - near) / (cut - near));
        }
        public static float Occlusion(int walls, float factor, int maximum, float floor)
        {
            return Math.Max(floor, (float)Math.Pow(factor, Math.Min(Math.Max(0, walls), maximum)));
        }
        public static float Smooth(float current, float target, float deltaTime, float timeConstant)
        {
            return current + (target - current) * (1f - (float)Math.Exp(-Math.Max(0f, deltaTime) / timeConstant));
        }
        public static bool CanRelay(bool speakerAlive, bool listenerAlive, float horizontal, float vertical,
            float maximum, float cut, float marginXZ, float marginY)
        {
            if (speakerAlive != listenerAlive) return false;
            return !speakerAlive || (horizontal <= maximum + marginXZ && Math.Abs(vertical) <= cut + marginY);
        }
    }
}
