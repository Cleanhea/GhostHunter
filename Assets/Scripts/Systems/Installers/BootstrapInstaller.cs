using GhostHunter.Core.Voice;
using GhostHunter.Core.Networking;
using GhostHunter.Core.Scenes;
using GhostHunter.Core.Steam;
using GhostHunter.Networking;
using GhostHunter.Systems.Steam;
using GhostHunter.Systems.SceneFlow;
using GhostHunter.Data;
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
        [SerializeField] private ConnectionManager _connection;
        [SerializeField] private SteamVoiceCapture _voiceCapture;
        [SerializeField] private VoiceChatSettings _lobbyVoiceSettings;

        protected override void InstallBindings()
        {
            Bind<ISceneFlow>(_sceneFlow);
            Bind<ISteamLobbyService>(_steamLobby);
            Bind<IConnectionService>(_connection);
            if (_sceneFlow != null)
            {
                // 인게임 로비 ⇄ 스테이지 전환(ADR-0018). 세션과 같이 앱 수명 동안 산다.
                StageSessionFlow stageFlow = GetComponent<StageSessionFlow>();
                if (stageFlow == null)
                    stageFlow = gameObject.AddComponent<StageSessionFlow>();
                stageFlow.Initialize(_sceneFlow);
                Bind<IStageSessionFlow>(stageFlow);
            }
            if (_steamLobby != null && _connection != null && _sceneFlow != null)
            {
                StageRecoveryCoordinator recovery = GetComponent<StageRecoveryCoordinator>();
                if (recovery == null)
                    recovery = gameObject.AddComponent<StageRecoveryCoordinator>();
                recovery.Initialize(_steamLobby, _connection, _sceneFlow);
            }
            if (_voiceCapture != null) Bind<IVoiceCaptureService>(_voiceCapture);
            if (_steamLobby != null && _voiceCapture != null && _sceneFlow != null)
            {
                LobbyVoiceService lobbyVoice = GetComponent<LobbyVoiceService>();
                if (lobbyVoice == null)
                    lobbyVoice = gameObject.AddComponent<LobbyVoiceService>();
                lobbyVoice.Initialize(_steamLobby, _voiceCapture, _sceneFlow, _connection,
                    _lobbyVoiceSettings);
            }
        }
    }
}
