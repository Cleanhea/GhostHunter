using GhostHunter.Core;
using GhostHunter.Core.Player;
using GhostHunter.Core.Scenes;
using GhostHunter.Core.Voice;
using GhostHunter.Data;
using GhostHunter.Gameplay.Player;
using GhostHunter.Gameplay.Sanity;
using GhostHunter.Gameplay.Voice;
using UnityEngine;

namespace GhostHunter.Systems.Installers
{
    /// <summary>
    /// 인게임 로비 씬(ADR-0018)의 서비스 등록. 플레이어 프리팹은 스테이지 씬과 같은 것을 쓰므로, 플레이어가
    /// 스폰할 때 붙잡는 서비스(스폰 위치·로컬 플레이어·정신력 팀·음성)를 이 씬에서도 같은 계약으로 제공한다.
    ///
    /// <para>귀신·청소·가구 서비스는 없다. 정신력 팀은 있지만 어둠 노출을 켜 주는 판정(헤드라이트·드릴카)이 없어
    /// 정신력이 줄지 않고, 사망·전멸도 일어나지 않는다. 음성은 정산 화면처럼 전원 채널이다
    /// (<see cref="VoiceChatService"/> 가 씬으로 판단).</para>
    /// </summary>
    [DefaultExecutionOrder(SceneInstaller.ExecutionOrder)]
    [DisallowMultipleComponent]
    public sealed class InGameLobbyInstaller : SceneInstaller
    {
        [SerializeField] private VoiceChatSettings _voiceSettings;
        [SerializeField] private PlayerSpawnRegistry _playerSpawns;
        [SerializeField] private SanityTeamService _sanityTeam;

        private readonly LocalPlayerContext _localPlayer = new();

        protected override void InstallBindings()
        {
            if (_voiceSettings != null)
                Bind<IVoiceChatService>(new VoiceChatService(Services.Get<IVoiceCaptureService>(),
                    _voiceSettings, Services.Get<ISceneFlow>()));
            Bind<IPlayerSpawnRegistry>(_playerSpawns);
            Bind<ILocalPlayerContext>(_localPlayer);
            Bind<ISanityTeamService>(_sanityTeam);
            Bind<ISanityDebug>(_sanityTeam);
        }
    }
}
