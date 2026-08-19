namespace GhostHunter.Core.Steam
{
    /// <summary>로비 멤버 한 명을 UI가 그리는 데 필요한 값만 담은 스냅샷.</summary>
    public readonly struct LobbyMemberInfo
    {
        public LobbyMemberInfo(ulong steamId, string displayName, bool isOwner, bool isReady)
        {
            SteamId = steamId;
            DisplayName = displayName;
            IsOwner = isOwner;
            IsReady = isReady;
        }

        public ulong SteamId { get; }
        public string DisplayName { get; }
        public bool IsOwner { get; }
        public bool IsReady { get; }
    }
}
