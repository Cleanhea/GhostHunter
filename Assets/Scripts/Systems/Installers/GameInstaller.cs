using GhostHunter.Core;
using GhostHunter.Core.Voice;
using GhostHunter.Data;
using GhostHunter.Gameplay.Voice;
using GhostHunter.Core.Player;
using GhostHunter.Gameplay.Cleaning;
using GhostHunter.Gameplay.Ghost;
using GhostHunter.Gameplay.Player;
using GhostHunter.Gameplay.Sanity;
using UnityEngine;

namespace GhostHunter.Systems.Installers
{
    /// <summary>Game 씬의 스폰 위치와 로컬 플레이어 컴포넌트 접근을 등록한다.</summary>
    [DefaultExecutionOrder(SceneInstaller.ExecutionOrder)]
    [DisallowMultipleComponent]
    public sealed class GameInstaller : SceneInstaller
    {
        [SerializeField] private VoiceChatSettings _voiceSettings;
        [SerializeField] private PlayerSpawnRegistry _playerSpawns;
        [SerializeField] private SanityTeamService _sanityTeam;
        [SerializeField] private GhostPrototypeSpawner _ghostSpawner;
        [SerializeField] private CleaningController _cleaning;

        private readonly LocalPlayerContext _localPlayer = new();

        protected override void InstallBindings()
        {
            if (_voiceSettings != null)
                Bind<IVoiceChatService>(new VoiceChatService(Services.Get<IVoiceCaptureService>(), _voiceSettings));
            Bind<IPlayerSpawnRegistry>(_playerSpawns);
            Bind<ILocalPlayerContext>(_localPlayer);
            Bind<ISanityTeamService>(_sanityTeam);
            Bind<ISanityDebug>(_sanityTeam);
            Bind<IGhostDebug>(_ghostSpawner);
            if (_cleaning != null)
                Bind<ICleaningService>(_cleaning);
        }
    }
}
