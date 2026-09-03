namespace GhostHunter.Gameplay.Ghost
{
    /// <summary>
    /// 한 플레이어의 "침대 밑 은신" 성립 판정. 순수 로직이라 <c>GhostStateMachine</c> 처럼
    /// EditMode 로 검증한다. <see cref="GhostPrototypeController"/> 가 어택 틱마다 플레이어별로
    /// 하나씩 굴린다.
    ///
    /// <para>규칙 (사용자 확정 2026-09-03 — ghost-system.md §9.5 / §13 G-8):</para>
    /// <list type="number">
    /// <item>엎드려서 Idle 침대 밑(<see cref="BedHideZone"/>) 에 완전히 들어가 있어야 한다(= <c>eligible</c>).</item>
    /// <item>들어가는 걸 귀신이 봤다면(= 그 순간 귀신의 추격/수색 대상이면) 성립하지 않는다.
    /// 추격은 그대로 유지되고 침대 밑에서도 잡힌다. 귀신이 놓쳐 배회로 돌아간 뒤에야 성립할 수 있다.</item>
    /// <item>귀신 원뿔 시야에 직접 걸려 있는 동안에도 성립하지 않는다(타이머가 0으로 리셋).</item>
    /// <item>위 조건을 <see cref="ConcealSeconds"/> 이상 연속으로 만족하면 <see cref="Granted"/> 가 켜진다.</item>
    /// <item>성립하면 귀신의 시야·소리 탐지와 잡힘에서 완전히 빠지고, 수색 훔쳐보기 대상도 아니다
    /// (일반 은신처 <see cref="HidingSpot"/> 의 30% 검사보다 강하다).</item>
    /// </list>
    /// </summary>
    public sealed class BedHideEvaluator
    {
        private float _concealTimer;

        /// <summary>지금 침대 밑 은신이 성립해 귀신 판정에서 완전히 빠지는가.</summary>
        public bool Granted { get; private set; }

        /// <summary>디버그·검증용 — 연속 은폐 누적 시간(초).</summary>
        public float ConcealTimer => _concealTimer;

        public void Reset()
        {
            _concealTimer = 0f;
            Granted = false;
        }

        /// <param name="deltaTime">이번 틱 시간(초).</param>
        /// <param name="eligible">엎드림 + Idle 침대 밑 상자 안.</param>
        /// <param name="chasedByGhost">지금 이 플레이어가 귀신의 추격/수색 대상인가(= 들어가는 걸 봤다).</param>
        /// <param name="visibleToGhost">지금 귀신 원뿔 시야 + 시야선에 걸리는가.</param>
        /// <param name="concealSeconds">성립까지 필요한 연속 은폐 시간(초).</param>
        /// <returns><see cref="Granted"/> 와 같은 값.</returns>
        public bool Tick(
            float deltaTime,
            bool eligible,
            bool chasedByGhost,
            bool visibleToGhost,
            float concealSeconds)
        {
            if (!eligible || chasedByGhost || visibleToGhost)
            {
                _concealTimer = 0f;
                Granted = false;
                return false;
            }

            _concealTimer += deltaTime;
            Granted = _concealTimer >= concealSeconds;
            return Granted;
        }
    }
}
