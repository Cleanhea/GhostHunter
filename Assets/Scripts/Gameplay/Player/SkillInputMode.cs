namespace GhostHunter.Gameplay.Player
{
    /// <summary>
    /// 굴착 스킬이 <see cref="PlayerInputReader"/> 에 거는 조작 제한 단계(두더지 스킬 기획서 §5.5.1).
    /// 두 단계 모두 점프·달리기·엎드리기·던지기·상호작용·탐지는 막고, 굴착 키(T)와 헤드라이트(F)는 살린다.
    /// </summary>
    public enum SkillInputMode
    {
        /// <summary>제한 없음.</summary>
        None,

        /// <summary>시전(Entering) 중 — 시야 회전과 이동까지 막는다.</summary>
        Frozen,

        /// <summary>매몰(Buried) 중 — 시야 회전과 이동만 허용한다.</summary>
        UndergroundMove,
    }
}
