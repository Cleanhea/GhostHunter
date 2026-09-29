namespace GhostHunter.Core.Steam
{
    public enum DriverTier
    {
        Wood = 0,
        Iron = 1,
    }

    /// <summary>
    /// 플레이어 한 명의 장비 — 판 사이에 유지된다(2026-09-29 사용자 확정: 드라이버는 플레이어별).
    /// 방장이 Steam 로비 데이터에 "등급,내구도,라이터" 로 적는다. 기록이 없으면 시작 지급(나무 드라이버 100, 라이터 없음).
    /// </summary>
    public readonly struct MemberGear
    {
        public readonly DriverTier Driver;
        public readonly int DriverDurability;
        public readonly bool HasLighter;

        public MemberGear(DriverTier driver, int driverDurability, bool hasLighter)
        {
            Driver = driver;
            DriverDurability = StageShopRules.Clamp(driverDurability);
            HasLighter = hasLighter;
        }

        /// <summary>시작 지급 — 나무 드라이버(내구도 최대), 라이터 없음.</summary>
        public static MemberGear Starting => new(DriverTier.Wood, StageShopRules.MaxDriverDurability, false);

        public MemberGear WithDurability(int durability) => new(Driver, durability, HasLighter);

        public string ToLobbyData() => $"{(int)Driver},{DriverDurability},{(HasLighter ? 1 : 0)}";

        public static bool TryFromLobbyData(string data, out MemberGear gear)
        {
            gear = Starting;
            if (string.IsNullOrEmpty(data))
                return false;

            string[] fields = data.Split(',');
            if (fields.Length != 3
                || !int.TryParse(fields[0], out int tier) || tier is < 0 or > 1
                || !int.TryParse(fields[1], out int durability)
                || !int.TryParse(fields[2], out int lighter) || lighter is < 0 or > 1)
            {
                return false;
            }

            gear = new MemberGear((DriverTier)tier, durability, lighter == 1);
            return true;
        }
    }
}
