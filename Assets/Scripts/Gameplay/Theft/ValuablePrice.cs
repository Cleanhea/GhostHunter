namespace GhostHunter.Gameplay.Theft
{
    /// <summary>
    /// 귀중품 가격($). 정신력 금액표(sanity-system.md §4.1)와 같은 4종만 있다 — 사용자 확정 2026-11-12(theft-system.md TH-Q1).
    /// 값이 곧 달러다. 새 금액을 넣으려면 정신력 금액표(<c>SanitySystemSettings</c>)도 함께 늘린다.
    /// </summary>
    public enum ValuablePrice
    {
        Two = 2,
        Three = 3,
        Five = 5,
        Ten = 10,
    }
}
