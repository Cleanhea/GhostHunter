namespace GhostHunter.Gameplay.Ghost
{
    /// <summary>
    /// 플레이어 한 명의 "굴착 은신이 무효인가" 판정(두더지 스킬 시스템 기획서 §5.2.1,
    /// 사용자 확정 2026-09-05). 땅속은 원래 완전 은신이지만,
    /// <b>귀신에게 이미 감지된 상태에서 들어갔으면 땅속에서도 계속 감지된다.</b>
    ///
    /// <para>판정은 <b>매몰이 시작된 순간</b> 한 번만 하고, 그 굴착이 끝날 때까지 유지한다.
    /// 들어간 뒤에 귀신이 잠깐 시야를 놓쳤다고 해서 안전해지지는 않는다 — 침대 밑 은신의
    /// "들어가는 걸 봤으면 그대로 추격+잡힘"(<see cref="BedHideEvaluator"/>)과 같은 성격이다.</para>
    ///
    /// <para><c>MonoBehaviour</c> 가 아닌 순수 클래스로 두어 EditMode 에서 검증한다 —
    /// <see cref="BedHideEvaluator"/> 와 같은 관례다.</para>
    /// </summary>
    public sealed class BurrowExposureTracker
    {
        private bool _wasBurrowed;

        /// <summary>지금 땅속에 있으면서, 그 굴착이 감지된 상태에서 시작됐는가.</summary>
        public bool Exposed { get; private set; }

        /// <summary>
        /// 어택 틱마다 한 번 부른다.
        /// </summary>
        /// <param name="burrowed">지금 매몰 상태인가(<c>MoleBurrowController.IsBurrowed</c>).</param>
        /// <param name="chased">
        /// 귀신이 이 플레이어를 쫓거나 마지막 위치를 수색 중인가 = "귀신이 봤다".
        /// 시전(0.3초) 중에는 아직 매몰이 아니므로 그 사이에 귀신을 따돌리면 안전하게 숨을 수 있다.
        /// </param>
        public void Tick(bool burrowed, bool chased)
        {
            if (!burrowed)
            {
                _wasBurrowed = false;
                Exposed = false;
                return;
            }

            if (_wasBurrowed)
                return;

            // 매몰 시작 프레임 — 여기서 한 번 정하고 굴착이 끝날 때까지 바꾸지 않는다.
            _wasBurrowed = true;
            Exposed = chased;
        }

        /// <summary>사망·어택 종료 등으로 판정을 버린다.</summary>
        public void Reset()
        {
            _wasBurrowed = false;
            Exposed = false;
        }
    }
}
