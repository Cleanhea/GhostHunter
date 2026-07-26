using Unity.Netcode.Components;

namespace GhostHunter.Player
{
    /// <summary>
    /// 플레이어 이동만은 반응성을 위해 소유자 권위로 복제한다. 가구 물리는 서버 권위를 유지한다.
    /// </summary>
    public sealed class ClientNetworkTransform : NetworkTransform
    {
        protected override bool OnIsServerAuthoritative()
        {
            return false;
        }
    }
}
