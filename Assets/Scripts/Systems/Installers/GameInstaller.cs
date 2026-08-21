using GhostHunter.Core.Player;
using GhostHunter.Gameplay.Player;
using UnityEngine;

namespace GhostHunter.Systems.Installers
{
    /// <summary>Game 씬의 스폰 위치와 로컬 플레이어 컴포넌트 접근을 등록한다.</summary>
    [DefaultExecutionOrder(SceneInstaller.ExecutionOrder)]
    [DisallowMultipleComponent]
    public sealed class GameInstaller : SceneInstaller
    {
        [SerializeField] private PlayerSpawnRegistry _playerSpawns;

        private readonly LocalPlayerContext _localPlayer = new();

        protected override void InstallBindings()
        {
            Bind<IPlayerSpawnRegistry>(_playerSpawns);
            Bind<ILocalPlayerContext>(_localPlayer);
        }
    }
}
