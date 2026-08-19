using System;
using GhostHunter.Core.Scenes;

namespace GhostHunter.Core.Networking
{
    public enum TransportMode
    {
        /// <summary>Steam P2P. 실제 플레이 경로.</summary>
        Steam,

        /// <summary>127.0.0.1. Steam 없이 로직만 검증할 때.</summary>
        Local,
    }

    /// <summary>Netcode 세션의 시작·종료와 개발용 트랜스포트 전환을 제공한다.</summary>
    public interface IConnectionService
    {
        TransportMode Mode { get; }
        bool IsRunning { get; }
        bool IsHost { get; }

        event Action<string> StatusChanged;

        void SetTransportMode(TransportMode mode);
        void StartHost();
        void StartHostInGameScene(SceneId scene);
        void ConnectToSteamHost(ulong hostSteamId);
        void StartLocalClient();
        void Disconnect();
    }
}
