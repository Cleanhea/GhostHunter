namespace GhostHunter.Gameplay.Sanity
{
    /// <summary>현재 연결된 플레이어 정신력을 집계하고 팀 평균을 제공하는 Game 씬 서비스.</summary>
    public interface ISanityTeamService
    {
        bool IsTeamWiped { get; }
        int DeadPlayerCount { get; }

        void Register(SanityNetworkState state);
        void Unregister(SanityNetworkState state);
        void ServerEvaluateTeamWipe();
        void ServerEndStageByExit();

        bool TryGetLocalState(out SanityNetworkState state);

        int CopyPlayerStates(SanityNetworkState[] destination);

        bool TryGetTeamAverage(
            out float exactAverage,
            out int roundedAverage,
            out int livingPlayerCount);
    }
}
