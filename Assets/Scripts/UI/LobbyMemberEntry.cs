using System;
using System.Collections.Generic;
using Steamworks;
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

        // 아바타는 세션 내내 안 바뀌므로 SteamId 별로 캐시한다. 몇 장 수준이라 해제하지 않는다.
        private static readonly Dictionary<ulong, Texture2D> AvatarCache = new();

        [SerializeField] private RawImage _avatarImage;
        [SerializeField] private Text _nameText;
        [SerializeField] private Text _stateText;

        public void Bind(Friend member, bool isOwner, bool isReady)
        {
            _nameText.text = member.Name;

            if (isOwner)
            {
                _stateText.text = "방장";
                _stateText.color = OwnerColor;
            }
            else
            {
                _stateText.text = isReady ? "준비 완료" : "대기 중";
                _stateText.color = isReady ? ReadyColor : WaitingColor;
            }

            LoadAvatar(member.Id);
        }

        private async void LoadAvatar(SteamId steamId)
        {
            if (AvatarCache.TryGetValue(steamId.Value, out Texture2D cached))
            {
                _avatarImage.texture = cached;
                return;
            }

            try
            {
                Steamworks.Data.Image? image = await SteamFriends.GetMediumAvatarAsync(steamId);

                // 로비 갱신으로 이 엔트리가 이미 파괴됐을 수 있다.
                if (this == null || !image.HasValue)
                    return;

                Texture2D texture = CreateAvatarTexture(image.Value);
                AvatarCache[steamId.Value] = texture;
                _avatarImage.texture = texture;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LobbyMemberEntry] 아바타 로드 실패 ({steamId}): {e.Message}");
            }
        }

        private static Texture2D CreateAvatarTexture(Steamworks.Data.Image image)
        {
            int width = (int)image.Width;
            int height = (int)image.Height;

            // Steam 아바타는 위→아래 순서의 RGBA, Texture2D 는 아래→위라 행을 뒤집는다.
            var flipped = new byte[image.Data.Length];
            int stride = width * 4;
            for (int y = 0; y < height; y++)
                Buffer.BlockCopy(image.Data, y * stride, flipped, (height - 1 - y) * stride, stride);

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.LoadRawTextureData(flipped);
            texture.Apply();
            return texture;
        }
    }
}
