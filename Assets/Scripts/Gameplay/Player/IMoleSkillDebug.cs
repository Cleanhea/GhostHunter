namespace GhostHunter.Gameplay.Player
{
    /// <summary>
    /// 개발 HUD(Tab)의 <c>두더지 스킬</c> 섹션이 탐지·굴착 상태를 읽고 강제로 조작하는
    /// 소유자 전용 창구. 두 스킬은 플레이어마다 하나씩 런타임에 스폰되므로 설치자에서
    /// 바인딩할 수 없다 — HUD는 <see cref="ILocalPlayerContext"/> 로 로컬 소유자의 것을 집어 온다.
    ///
    /// <para><b>수치 조절은 여기 없다.</b> 튜닝 값은 각 스킬 설정 에셋에 있고
    /// 개발 HUD 가 리플렉션으로 직접 노출한다(<c>TuningHud</c>). 이 인터페이스는 "지금 어떤
    /// 상태인가"와 "그 상태를 강제로 바꾼다"만 담당한다 — 수치를 바꿔도 5초 유지·10초 쿨타임을
    /// 매번 기다려야 한다면 튜닝이 되지 않기 때문이다.</para>
    /// </summary>
    public interface IMoleSkillDebug
    {
        /// <summary>로컬 소유자의 스킬을 지금 조작할 수 있는 상태인지(스폰 + 소유 + 설정 있음).</summary>
        bool CanControl { get; }

        /// <summary>현재 상태·남은 시간·쿨타임 한 줄 요약.</summary>
        string StatusSummary { get; }

        /// <summary>쿨타임을 무시하고 스킬 시전을 시작한다. 이미 진행 중이면 아무 것도 하지 않는다.</summary>
        void ForceStart();

        /// <summary>
        /// 진행 중인 스킬을 즉시 끝낸다. 스킬별 정상 종료 규칙을 우회하지 않는 디버그 취소다.
        /// </summary>
        void ForceEnd();

        /// <summary>남은 재사용 대기 시간을 0으로 만든다.</summary>
        void ResetCooldown();
    }
}
