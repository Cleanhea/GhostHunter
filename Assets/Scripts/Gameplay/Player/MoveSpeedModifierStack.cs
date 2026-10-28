namespace GhostHunter.Gameplay.Player
{
    /// <summary>이동 속도 배율을 거는 출처. 출처마다 슬롯 하나라 서로 덮어쓰지 않는다.</summary>
    public enum SpeedModifierSource
    {
        /// <summary>매몰(Buried) 중 굴착 스킬.</summary>
        Burrow,
    }

    /// <summary>
    /// 출처별 이동 속도 배율을 모아 곱한 값을 낸다. 비어 있는 슬롯은 1이라 아무 영향이 없다.
    /// 새 출처(버프 등)는 <see cref="SpeedModifierSource"/> 에 값을 하나 더하면 된다.
    /// </summary>
    public sealed class MoveSpeedModifierStack
    {
        private readonly float[] _multipliers;

        public MoveSpeedModifierStack()
        {
            _multipliers = new float[System.Enum.GetValues(typeof(SpeedModifierSource)).Length];
            for (int i = 0; i < _multipliers.Length; i++)
                _multipliers[i] = 1f;
        }

        /// <summary>모든 슬롯을 곱한 최종 배율.</summary>
        public float Multiplier
        {
            get
            {
                float result = 1f;
                for (int i = 0; i < _multipliers.Length; i++)
                    result *= _multipliers[i];
                return result;
            }
        }

        public void Set(SpeedModifierSource source, float multiplier)
        {
            _multipliers[(int)source] = multiplier;
        }

        public void Clear(SpeedModifierSource source)
        {
            _multipliers[(int)source] = 1f;
        }
    }
}
