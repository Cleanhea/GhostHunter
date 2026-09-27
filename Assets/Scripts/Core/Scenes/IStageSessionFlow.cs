namespace GhostHunter.Core.Scenes
{
    /// <summary>
    /// 세션을 끊지 않고 인게임 로비와 스테이지(Stage1)를 오가게 한다(ADR-0018·0019). 호스트만 전환을 시작한다.
    ///
    /// <para>전환할 때마다 서버가 플레이어를 디스폰하고 새 씬에서 다시 스폰한다. 플레이어 컴포넌트는 스폰할 때
    /// 그 씬의 서비스(스폰 위치·로컬 플레이어·정신력 팀·음성)를 붙잡으므로, 씬을 넘어 살려 두면 사라진 이전 씬의
    /// 서비스를 계속 쓰게 된다.</para>
    /// </summary>
    public interface IStageSessionFlow
    {
        /// <summary>전환 진행 중인가.</summary>
        bool IsTransitioning { get; }

        /// <summary>이 피어가 전환을 시작할 수 있는가 — 세션의 서버(호스트)이고 전환 중이 아니다.</summary>
        bool CanControl { get; }

        /// <summary>인게임 로비 → 스테이지(<see cref="SceneId.Stage1"/>). 거부되면 false.</summary>
        bool StartStage();

        /// <summary>스테이지(<see cref="SceneIdExtensions.IsStage"/>)·정산(Result) → 인게임 로비. 거부되면 false.</summary>
        bool ReturnToInGameLobby();
    }
}
