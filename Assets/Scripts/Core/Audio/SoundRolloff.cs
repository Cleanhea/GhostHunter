namespace GhostHunter.Core.Audio
{
    /// <summary>3D 소리의 거리 감쇠 곡선 종류.</summary>
    public enum SoundRolloff
    {
        Logarithmic = 0,
        Linear = 1,
        /// <summary>큐의 커스텀 곡선. x 는 min~max 거리를 0~1 로 편 값이다.</summary>
        Custom = 2,
    }
}
