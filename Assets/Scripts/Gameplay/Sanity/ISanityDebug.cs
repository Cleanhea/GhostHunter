namespace GhostHunter.Gameplay.Sanity
{
    /// <summary>F1 HUD에서 정신력 코어와 외부 시스템 연결 API를 검증하는 서비스.</summary>
    public interface ISanityDebug
    {
        bool CanControl { get; }
        string StatusSummary { get; }
        string LastStatus { get; }

        void ToggleLocalDarkness();
        void ApplyLocalGhostEvent();
        void WitnessNewCorpse();
        void WitnessSameCorpse();
        void RestoreLocalSanity();
        void MarkLocalPlayerDead();
        void ResetLocalPlayerForStage();
    }
}
