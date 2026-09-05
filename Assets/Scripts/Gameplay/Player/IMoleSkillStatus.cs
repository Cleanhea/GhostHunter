namespace GhostHunter.Gameplay.Player
{
    /// <summary>게임플레이 UI가 두더지 스킬 상태와 남은 시간을 읽는 읽기 전용 창구.</summary>
    public interface IMoleSkillStatus
    {
        MoleSkillPhase Phase { get; }
        float PhaseRemainingSeconds { get; }
        float ActiveDurationSeconds { get; }
        float CooldownRemainingSeconds { get; }
        float CooldownDurationSeconds { get; }
    }
}
