namespace GhostHunter.Core.Steam
{
    public enum DriverTier
    {
        Wood = 0,
        Iron = 1,
    }

    /// <summary>
    /// 플레이어 한 명의 장비 — 판 사이에 유지된다(2026-09-29 사용자 확정: 드라이버는 플레이어별).
    /// 방장이 Steam 로비 데이터에 "등급,내구도,라이터,촛불" 로 적는다. 기록이 없으면 시작 지급(나무 드라이버 100, 라이터·촛불 없음).
    /// 철제 드라이버·라이터·촛불은 드릴카 선반(<see cref="ShelfStock"/>)에서 꺼낸 사람이 갖는다(2026-11-09).
    /// </summary>
    public readonly struct MemberGear
    {
        public readonly DriverTier Driver;
        public readonly int DriverDurability;
        public readonly bool HasLighter;

        /// <summary>들고 있는 촛불 수. 부활 의식에서 촛대에 꽂을 때 하나씩 쓴다.</summary>
        public readonly int Candles;

        public MemberGear(DriverTier driver, int driverDurability, bool hasLighter, int candles = 0)
        {
            Driver = driver;
            DriverDurability = StageShopRules.Clamp(driverDurability);
            HasLighter = hasLighter;
            Candles = candles < 0 ? 0 : candles;
        }

        /// <summary>시작 지급 — 나무 드라이버(내구도 최대), 라이터·촛불 없음.</summary>
        public static MemberGear Starting => new(DriverTier.Wood, StageShopRules.MaxDriverDurability, false);

        public MemberGear WithDurability(int durability) => new(Driver, durability, HasLighter, Candles);

        public MemberGear WithCandles(int candles) => new(Driver, DriverDurability, HasLighter, candles);

        public string ToLobbyData() => $"{(int)Driver},{DriverDurability},{(HasLighter ? 1 : 0)},{Candles}";

        /// <summary>"등급,내구도,라이터,촛불" 을 읽는다. 촛불이 없던 옛 세 필드 형식은 촛불 0 으로 읽는다.</summary>
        public static bool TryFromLobbyData(string data, out MemberGear gear)
        {
            gear = Starting;
            if (string.IsNullOrEmpty(data))
                return false;

            string[] fields = data.Split(',');
            int candles = 0;
            if (fields.Length is < 3 or > 4
                || !int.TryParse(fields[0], out int tier) || tier is < 0 or > 1
                || !int.TryParse(fields[1], out int durability)
                || !int.TryParse(fields[2], out int lighter) || lighter is < 0 or > 1
                || (fields.Length == 4 && (!int.TryParse(fields[3], out candles) || candles < 0)))
            {
                return false;
            }

            gear = new MemberGear((DriverTier)tier, durability, lighter == 1, candles);
            return true;
        }
    }
}
