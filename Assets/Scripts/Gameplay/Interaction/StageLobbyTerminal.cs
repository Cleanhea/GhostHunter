using System;
using UnityEngine;

namespace GhostHunter.Gameplay.Interaction
{
    /// <summary>
    /// 인게임 로비의 상점·출발 단말기(ADR-0018). 조준하고 E 를 누르면 로컬 조작 창을 연다 — 창은 UI 계층의
    /// <c>InGameLobbyPanel</c> 이 <see cref="OpenRequested"/> 로 받는다. 네트워크 상태가 없는 로컬 표시물이며,
    /// InGameLobby 씬에 배치되어 있다. 구매·출발 권한은 창이 방장에게만 준다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StageLobbyTerminal : MonoBehaviour
    {
        /// <summary>
        /// 로컬 플레이어가 단말기에서 E 를 눌렀다. 정적 이벤트라 구독자가 <c>OnDisable</c> 에서 반드시 해제한다
        /// (<c>InGameLobbyPanel</c>).
        /// </summary>
        public static event Action OpenRequested;

        public void RequestOpen() => OpenRequested?.Invoke();
    }
}
