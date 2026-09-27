using GhostHunter.Core.Scenes;

namespace GhostHunter.Core.Steam
{
    /// <summary>
    /// 임시 공동 상점 규칙(스테이지 기획서 §2, 2026-09-28 변경). 상점은 <b>인게임 로비</b>에서만 열리고
    /// <b>방장만</b> 산다 — 나머지는 잔액·보유를 본다. 일반 로비에서는 사지 않는다.
    /// </summary>
    public static class StageShopRules
    {
        public const int ItemCount = 3;

        /// <summary>Temp1·2·3 가격 — 100·200·300.</summary>
        public static int PriceOf(int itemIndex) => itemIndex * 100;

        public static bool IsValidItem(int itemIndex) => itemIndex >= 1 && itemIndex <= ItemCount;

        /// <summary>상점이 열려 있는가 — 인게임 로비에 있고 씬 전환 중이 아니다.</summary>
        public static bool IsOpen(SceneId currentScene, bool isLoading) =>
            currentScene == SceneId.InGameLobby && !isLoading;

        public static bool CanPurchase(bool isLobbyOwner, SceneId currentScene, bool isLoading,
            int balance, int itemIndex) =>
            isLobbyOwner && IsOpen(currentScene, isLoading) && IsValidItem(itemIndex)
            && balance >= PriceOf(itemIndex);
    }
}
