using GhostHunter.Core.Scenes;
using GhostHunter.Core.Steam;
using GhostHunter.Networking;
using GhostHunter.Systems.SceneFlow;
using UnityEngine;

namespace GhostHunter.Systems.Installers
{
    /// <summary>
    /// 전역 스코프의 컴포지션 루트. Bootstrap 은 앱 종료까지 언로드되지 않으므로
    /// 여기서 등록한 서비스는 앱 수명 전체를 산다.
    /// </summary>
    [DefaultExecutionOrder(SceneInstaller.ExecutionOrder)]
    [DisallowMultipleComponent]
    public sealed class BootstrapInstaller : SceneInstaller
    {
        [SerializeField] private SceneFlowController _sceneFlow;
        [SerializeField] private SteamLobbyManager _steamLobby;

        protected override void InstallBindings()
        {
            Bind<ISceneFlow>(_sceneFlow);
            Bind<ISteamLobbyService>(_steamLobby);
        }
    }
}
