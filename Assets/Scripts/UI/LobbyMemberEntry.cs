using System;
using Cysharp.Threading.Tasks;
using GhostHunter.Core.Steam;
using UnityEngine;
using UnityEngine.UI;

namespace GhostHunter.UI
{
    /// <summary>
    /// 로비 멤버 한 줄: 스팀 아바타 + 닉네임 + 상태(방장/준비 완료/대기 중).
    /// 로비 씬의 비활성 템플릿을 LobbyController 가 복제해 쓴다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LobbyMemberEntry : MonoBehaviour
    {
        private static readonly Color OwnerColor = new(1f, 0.82f, 0.3f);
        private static readonly Color ReadyColor = new(0.45f, 0.9f, 0.5f);
        private static readonly Color WaitingColor = new(0.65f, 0.68f, 0.75f);

        [SerializeField] private RawImage _avatarImage;
        [SerializeField] private Text _nameText;
        [SerializeField] private Text _stateText;

        public void Bind(LobbyMemberInfo member, ISteamLobbyService lobby)
        {
            _nameText.text = member.DisplayName;

            if (member.IsOwner)
            {
                _stateText.text = "방장";
                _stateText.color = OwnerColor;
            }
            else
            {
                _stateText.text = member.IsReady ? "준비 완료" : "대기 중";
                _stateText.color = member.IsReady ? ReadyColor : WaitingColor;
            }

            LoadAvatarAsync(member.SteamId, lobby).Forget();
        }

        private async UniTaskVoid LoadAvatarAsync(ulong steamId, ISteamLobbyService lobby)
        {
            if (lobby == null)
                return;

            try
            {
                Texture2D texture = await lobby.GetAvatarAsync(steamId)
                    .AttachExternalCancellation(destroyCancellationToken);

                if (texture == null)
                    return;

                _avatarImage.texture = texture;
            }
            catch (OperationCanceledException)
            {
                // 로비 갱신으로 이 엔트리가 파괴됐다. 정상 종료.
            }
        }
    }
}
