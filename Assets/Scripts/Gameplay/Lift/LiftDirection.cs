namespace GhostHunter.Gameplay.Lift
{
    /// <summary>리프트 버튼의 방향 — 상승·하강 버튼이 각각 한 방향만 맡는다(기획서 §2.3).</summary>
    public enum LiftDirection : byte
    {
        Up = 0,
        Down = 1,
    }
}
