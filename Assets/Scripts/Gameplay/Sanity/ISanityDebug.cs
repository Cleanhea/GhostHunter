namespace GhostHunter.Gameplay.Sanity
{
    /// <summary>Tab 개발 HUD에서 정신력 코어와 외부 시스템 연결 API를 검증하는 서비스.</summary>
    public interface ISanityDebug
    {
        bool CanControl { get; }
        string StatusSummary { get; }
        string LastStatus { get; }

        /// <summary>정신력 슬라이더의 최소·최대 눈금. 설정 에셋의 범위를 그대로 노출한다.</summary>
        int SanityMinimum { get; }
        int SanityMaximum { get; }

        void ToggleLocalDarkness();
        void ApplyLocalGhostEvent();
        void WitnessNewCorpse();
        void WitnessSameCorpse();
        void RestoreLocalSanity();
        void MarkLocalPlayerDead();

        /// <summary>사망한 로컬 플레이어를 정신력 값은 유지한 채 생존으로 되돌린다.</summary>
        void ReviveLocalPlayer();

        /// <summary>연결된 사망 플레이어 전원을 되살린다(호스트 전용).</summary>
        void ReviveTeam();

        void ResetLocalPlayerForStage();

        /// <summary>로컬 소유 플레이어의 현재 개인 정신력. 생존한 로컬 상태가 없으면 false.</summary>
        bool TryGetLocalSanity(out int sanity);

        /// <summary>로컬 소유 플레이어의 개인 정신력을 지정 값으로 즉시 맞춘다(서버 권위).</summary>
        void SetLocalSanity(int value);

        /// <summary>생존 플레이어 팀 평균의 반올림 정수. 생존자가 없으면 false.</summary>
        bool TryGetTeamSanity(out int roundedAverage);

        /// <summary>연결된 전체 플레이어의 개인 정신력을 한 번에 지정 값으로 맞춘다(호스트 전용).</summary>
        void SetTeamSanity(int value);
    }
}
