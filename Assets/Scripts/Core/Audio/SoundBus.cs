namespace GhostHunter.Core.Audio
{
    /// <summary>음량 조절과 믹서 라우팅의 큰 분류.</summary>
    public enum SoundBus
    {
        /// <summary>충돌·상호작용·귀신 등 사건에 붙는 효과음.</summary>
        Sfx = 0,
        /// <summary>한 자리에서 계속 깔리는 환경음.</summary>
        Ambient = 1,
    }
}
