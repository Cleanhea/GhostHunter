using System;

namespace GhostHunter.Core.Steam
{
    /// <summary>
    /// 드릴카 선반에 쌓인 팀 공동 아이템 수(2026-11-09 사용자 확정). 상점에서 사면 늘고, 스테이지에서 플레이어가
    /// 선반에서 E 로 꺼내면 그 사람 장비(<see cref="MemberGear"/>)로 옮겨 가며 준다. 꺼내지 않은 아이템은 판 사이에 남는다.
    /// 방장이 로비 데이터 <c>gh_shop_state</c> 에 잔액과 함께 "드라이버,라이터,촛대" 로 적는다.
    /// </summary>
    public readonly struct ShelfStock : IEquatable<ShelfStock>
    {
        public readonly int IronDrivers;
        public readonly int Lighters;
        public readonly int CandleSets;

        public ShelfStock(int ironDrivers, int lighters, int candleSets)
        {
            IronDrivers = Math.Max(0, ironDrivers);
            Lighters = Math.Max(0, lighters);
            CandleSets = Math.Max(0, candleSets);
        }

        public int Total => IronDrivers + Lighters + CandleSets;

        public int CountOf(ShopItem item) => item switch
        {
            ShopItem.IronDriver => IronDrivers,
            ShopItem.IronLighter => Lighters,
            ShopItem.CandleSet => CandleSets,
            _ => 0,
        };

        /// <summary>그 품목 수를 <paramref name="delta"/> 만큼 바꾼다. 0 아래로는 내려가지 않는다.</summary>
        public ShelfStock With(ShopItem item, int delta) => item switch
        {
            ShopItem.IronDriver => new ShelfStock(Add(IronDrivers, delta), Lighters, CandleSets),
            ShopItem.IronLighter => new ShelfStock(IronDrivers, Add(Lighters, delta), CandleSets),
            ShopItem.CandleSet => new ShelfStock(IronDrivers, Lighters, Add(CandleSets, delta)),
            _ => this,
        };

        public string ToData() => $"{IronDrivers},{Lighters},{CandleSets}";

        /// <summary><paramref name="fields"/>[<paramref name="start"/>..+3] 의 0 이상 정수 세 개를 읽는다.</summary>
        public static bool TryParse(string[] fields, int start, out ShelfStock stock)
        {
            stock = default;
            if (fields == null || start < 0 || fields.Length < start + 3
                || !int.TryParse(fields[start], out int drivers) || drivers < 0
                || !int.TryParse(fields[start + 1], out int lighters) || lighters < 0
                || !int.TryParse(fields[start + 2], out int candles) || candles < 0)
                return false;
            stock = new ShelfStock(drivers, lighters, candles);
            return true;
        }

        public bool Equals(ShelfStock other) =>
            IronDrivers == other.IronDrivers && Lighters == other.Lighters && CandleSets == other.CandleSets;

        public override bool Equals(object obj) => obj is ShelfStock other && Equals(other);

        public override int GetHashCode() => (IronDrivers * 397 ^ Lighters) * 397 ^ CandleSets;

        public override string ToString() => $"드라이버 {IronDrivers} · 라이터 {Lighters} · 촛대 {CandleSets}";

        private static int Add(int value, int delta) => (int)Math.Min(Math.Max((long)value + delta, 0L), int.MaxValue);
    }
}
