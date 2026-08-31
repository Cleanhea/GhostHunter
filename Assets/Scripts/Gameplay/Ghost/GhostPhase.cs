namespace GhostHunter.Gameplay.Ghost
{
    /// <summary>귀신 시스템 기획서 §4 의 5개 공통 상태와 별개 시스템인 강제 진정.</summary>
    public enum GhostPhase : byte
    {
        /// <summary>평상시. 이동만 하고 어택 판정을 실행하지 않는다.</summary>
        Idle = 0,

        /// <summary>활동. 팀 평균 80 이하이거나 청소 40% 도달로 진입한다.</summary>
        Active = 1,

        /// <summary>경고. 어택이 확정된 상태. 5초 뒤 어택으로 넘어간다.</summary>
        Warning = 2,

        /// <summary>어택. 탐지·추격·공격이 활성화된다.</summary>
        Attack = 3,

        /// <summary>자연 진정. 어택 종료 후 30초간 재어택을 막는다.</summary>
        Calming = 4,

        /// <summary>강제 진정. 아이템으로 걸리는 별개 억제 효과. 진행 중인 어택을 즉시 끊는다.</summary>
        Suppressed = 5,
    }
}
