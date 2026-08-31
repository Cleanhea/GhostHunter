using System;
using UnityEngine;

namespace GhostHunter.Gameplay.Sanity
{
    /// <summary>팀 평균 반올림과 정신력 디버프 상태를 계산한다.</summary>
    internal static class SanityMath
    {
        internal static int RoundTeamAverage(int totalSanity, int livingPlayerCount)
        {
            if (livingPlayerCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(livingPlayerCount));

            float average = (float)totalSanity / livingPlayerCount;

            // 기획서의 일반적인 반올림을 보장한다. Mathf.RoundToInt의 midpoint-to-even은 쓰지 않는다.
            return Mathf.FloorToInt(average + 0.5f);
        }

        internal static SanityDebuffFlags ResolveDebuffs(
            int sanity,
            bool isAlive,
            SanitySystemSettings settings)
        {
            if (!isAlive || settings == null)
                return SanityDebuffFlags.None;

            SanityDebuffFlags flags = SanityDebuffFlags.None;
            if (sanity <= settings.CameraNoiseThreshold)
                flags |= SanityDebuffFlags.CameraNoise;
            if (sanity <= settings.WhisperThreshold)
                flags |= SanityDebuffFlags.Whisper;
            if (sanity <= settings.BreathingHeartbeatThreshold)
                flags |= SanityDebuffFlags.BreathingHeartbeat;

            return flags;
        }
    }
}
