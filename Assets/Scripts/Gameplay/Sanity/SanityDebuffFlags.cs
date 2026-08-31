using System;

namespace GhostHunter.Gameplay.Sanity
{
    /// <summary>개인 정신력 임계값에 따라 활성화해야 하는 로컬 피드백 상태.</summary>
    [Flags]
    public enum SanityDebuffFlags
    {
        None = 0,
        CameraNoise = 1 << 0,
        Whisper = 1 << 1,
        BreathingHeartbeat = 1 << 2,
    }
}
