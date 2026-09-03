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

        /// <summary>
        /// 이쪽에서 요청하지 않았는데 세션이 끝났을 때 발생한다 — 호스트 이탈, 타임아웃,
        /// 트랜스포트 실패, 강퇴. 사유는 구분하지 않는다(문구 통일).
        /// 스스로 <see cref="Disconnect"/> 를 부른 경우에는 발생하지 않는다.
        /// </summary>
        event Action SessionEnded;

        void SetTransportMode(TransportMode mode);
        void StartHost();
        void StartHostInGameScene(SceneId scene);
        void ConnectToSteamHost(ulong hostSteamId);
        void StartLocalClient();

        /// <summary>
        /// 세션을 끝낸다. <paramref name="leaveLobby"/> 가 false 면 Netcode 세션만 닫고
        /// Steam 로비 멤버로는 남는다 — 게스트가 매치에서만 빠질 때 쓴다.
        /// </summary>
        void Disconnect(bool leaveLobby = true);
    }
}
