using GhostHunter.Core;
using GhostHunter.Core.Steam;

namespace GhostHunter.Gameplay.Player
{
    /// <summary>
    /// 서버가 플레이어의 판 사이 장비(<see cref="MemberGear"/>)를 찾는다. Steam 방·로컬 세션 모두 상점 서비스
    /// (<see cref="IStageShopService"/>)에 있다. 상점 서비스가 없는 경우(부트스트랩 없이 도는 테스트 픽스처)만
    /// <see cref="NoShopFallback"/> 을 쓴다.
    /// </summary>
    internal static class PlayerGearSource
    {
        /// <summary>상점 서비스가 없을 때 — 나무 드라이버 100, 라이터 있음(라이터를 시험할 수 있게).</summary>
        public static MemberGear NoShopFallback =>
            new(DriverTier.Wood, StageShopRules.MaxDriverDurability, true);

        /// <summary>상점 세션의 참가자면 true 와 저장 장비를, 아니면 false 와 <see cref="NoShopFallback"/> 을 준다.</summary>
        public static bool TryGetGear(ulong ownerClientId, out IStageShopService shop,
            out ulong memberKey, out MemberGear gear)
        {
            memberKey = 0;
            gear = NoShopFallback;
            if (!Services.TryGet(out shop) || !shop.IsAvailable
                || !shop.TryGetMemberKey(ownerClientId, out memberKey))
            {
                shop = null;
                return false;
            }

            gear = shop.GetMemberGear(memberKey);
            return true;
        }
    }
}
