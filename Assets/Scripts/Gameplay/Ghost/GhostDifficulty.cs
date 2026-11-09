namespace GhostHunter.Gameplay.Ghost
{
    /// <summary>
    /// 귀신 난이도(원문 0.3 — 쉬움/보통/어려움). 난이도를 누가 어떻게 고르는가는 아직 정해지지 않았다(ghost-system.md G-23) —
    /// 지금은 <c>GhostPrototypeSettings</c> 에셋마다 하나를 정해 둔다(Tutorial = 쉬움).
    /// </summary>
    public enum GhostDifficulty : byte
    {
        Easy = 0,
        Normal = 1,
        Hard = 2,
    }
}
