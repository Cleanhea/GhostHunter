using UnityEngine;

namespace GhostHunter.Gameplay.Player
{
    /// <summary>F키 한 번에 대한 헤드라이트 반응.</summary>
    public enum HeadlampToggleResult
    {
        TurnedOn,
        TurnedOff,

        /// <summary>배터리가 0이라 켜지지 않았다 — broken 효과음을 낸다(기획서 §4).</summary>
        Empty,

        /// <summary>지금은 켤 수 없는 상태다(사망·은신 중). 아무 소리도 내지 않는다.</summary>
        Blocked,
    }

    /// <summary>
    /// 헤드라이트 한 개의 전원·배터리 규칙(기획서 §2-1·§2-2). <c>MonoBehaviour</c> 가 아닌 순수 클래스로 두어
    /// EditMode 에서 검증한다 — <c>BurrowExposureTracker</c> 와 같은 관례다.
    /// </summary>
    public sealed class HeadlampBattery
    {
        private readonly float _max;
        private readonly float _drainPerSecond;
        private readonly float _rechargePerSecond;

        public HeadlampBattery(float max, float drainPerSecond, float rechargePerSecond)
        {
            _max = Mathf.Max(1f, max);
            _drainPerSecond = Mathf.Max(0f, drainPerSecond);
            _rechargePerSecond = Mathf.Max(0f, rechargePerSecond);
            Charge = _max;
        }

        public float Charge { get; private set; }
        public float Max => _max;
        public bool IsOn { get; private set; }
        public bool IsEmpty => Charge <= 0f;

        /// <summary>F키 입력. 켜져 있으면 끄고, 꺼져 있으면 배터리가 남아 있을 때만 켠다.</summary>
        public HeadlampToggleResult Toggle(bool canTurnOn)
        {
            if (IsOn)
            {
                IsOn = false;
                return HeadlampToggleResult.TurnedOff;
            }

            if (!canTurnOn)
                return HeadlampToggleResult.Blocked;

            if (IsEmpty)
                return HeadlampToggleResult.Empty;

            IsOn = true;
            return HeadlampToggleResult.TurnedOn;
        }

        /// <summary>사망·은신 진입처럼 규칙이 강제로 끌 때. 효과음은 내지 않는다.</summary>
        public void ForceOff() => IsOn = false;

        /// <summary>
        /// 한 프레임 진행. 드릴카 안(<paramref name="charging"/>)이면 켜져 있어도 줄지 않고 충전만 된다.
        /// 밖이면 켜져 있을 때 <paramref name="drains"/>(스테이지 씬)인 경우에만 줄어든다.
        /// </summary>
        /// <returns>이번 틱에 배터리가 바닥나 자동으로 꺼졌으면 true.</returns>
        public bool Tick(float deltaTime, bool charging, bool drains)
        {
            if (deltaTime <= 0f)
                return false;

            if (charging)
            {
                Charge = Mathf.Min(_max, Charge + _rechargePerSecond * deltaTime);
                return false;
            }

            if (!IsOn || !drains)
                return false;

            Charge = Mathf.Max(0f, Charge - _drainPerSecond * deltaTime);
            if (Charge > 0f)
                return false;

            IsOn = false;
            return true;
        }

        /// <summary>호스트 이전 스냅샷 복원.</summary>
        public void Restore(float charge, bool on)
        {
            Charge = Mathf.Clamp(charge, 0f, _max);
            IsOn = on && Charge > 0f;
        }

        /// <summary>
        /// 저전력 깜빡임(기획서 §2-2: 15 이하에서 5초마다 3번). 저전력이 된 순간부터 잰 시간을 받아
        /// 지금 조명이 잠깐 꺼져 있어야 하는지 돌려준다. 묶음은 저전력 진입 즉시 한 번 시작한다.
        /// </summary>
        public static bool IsBlinkDark(float secondsSinceLow, float interval, int count,
            float offSeconds, float onSeconds)
        {
            if (secondsSinceLow < 0f || count <= 0 || interval <= 0f)
                return false;

            float cycle = offSeconds + onSeconds;
            if (cycle <= 0f)
                return false;

            float inBurst = secondsSinceLow % interval;
            if (inBurst >= count * cycle)
                return false;

            return inBurst % cycle < offSeconds;
        }
    }
}
