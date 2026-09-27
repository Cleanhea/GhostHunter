namespace GhostHunter.Gameplay.Ghost
{
    /// <summary>귀신 공통 상태. Idle/Suppressed 값은 과거 데이터 호환용으로만 남긴다.</summary>
    public enum GhostPhase : byte
    {
        /// <summary>과거 프로토타입 값. 새 스테이지에서는 진입하지 않는다.</summary>
        Idle = 0,

        /// <summary>스테이지 시작 상태. 집 내부를 배회한다.</summary>
        Active = 1,

        /// <summary>경고. 어택이 확정된 상태. 5초 뒤 어택으로 넘어간다.</summary>
        Warning = 2,

        /// <summary>어택. 탐지·추격·공격이 활성화된다.</summary>
        Attack = 3,

        /// <summary>자연 진정. 어택 종료 후 30초간 재어택을 막는다.</summary>
        Calming = 4,

        /// <summary>과거 프로토타입 값. 아이템은 Calming으로 진입한다.</summary>
        Suppressed = 5,
    }
}
