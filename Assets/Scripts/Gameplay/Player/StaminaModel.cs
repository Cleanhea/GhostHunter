using UnityEngine;

namespace GhostHunter.Gameplay.Player
{
    /// <summary>
    /// 스태미나의 순수 규칙 — 소모·회복·탈진·지침 속도·헐떡임 판정. <see cref="PlayerStamina"/> 는 NetworkBehaviour 라
    /// EditMode 로 직접 돌리기 어려워서, 규칙만 여기로 뺐다(<see cref="PlayerPosture"/> 와 같은 이유).
    /// 설정을 매번 읽으므로 F2 튜닝 창에서 바꾼 값이 바로 적용된다.
    /// </summary>
    public sealed class StaminaModel
    {
        private readonly StaminaSettings _settings;
        private float _regenTimer;

        public StaminaModel(StaminaSettings settings)
        {
            _settings = settings;
            Current = settings.MaxStamina;
        }

        public float Current { get; private set; }

        public float Max => _settings.MaxStamina;

        /// <summary>0~1.</summary>
        public float Ratio => Max > 0f ? Mathf.Clamp01(Current / Max) : 0f;

        /// <summary>0 까지 다 써서 달릴 수 없는 상태. 회복 비율에 닿아야 풀린다.</summary>
        public bool Exhausted { get; private set; }

        /// <summary>헐떡이는 중인가. 시작·정지 비율에 간격을 둬서 경계에서 깜빡이지 않는다.</summary>
        public bool Panting { get; private set; }

        public bool CanSprint => !Exhausted && Current > 0f;

        /// <summary>
        /// 지쳐서 줄어든 이동 속도 배율. 느려지기 시작하는 비율 위에서는 1, 0 에서는
        /// <see cref="StaminaSettings.MinSpeedMultiplier"/> 이며 그 사이는 선형이다.
        /// </summary>
        public float SpeedMultiplier
        {
            get
            {
                float start = _settings.SlowStartRatio;
                if (start <= 0f || Ratio >= start)
                    return 1f;

                return Mathf.Lerp(_settings.MinSpeedMultiplier, 1f, Ratio / start);
            }
        }

        public void Refill()
        {
            Current = Max;
            Exhausted = false;
            Panting = false;
            _regenTimer = 0f;
        }

        /// <summary>
        /// 한 프레임을 진행한다. <paramref name="sprinting"/> 은 실제로 달리고 있는지다 —
        /// 탈진 중에 true 가 들어와도 소모하지 않는다(호출한 쪽이 <see cref="CanSprint"/> 로 막는 게 맞다).
        /// </summary>
        public void Tick(float deltaTime, bool sprinting)
        {
            float max = Max;
            Current = Mathf.Min(Current, max);

            if (sprinting && !Exhausted)
            {
                Current -= _settings.SprintDrainPerSecond * deltaTime;
                _regenTimer = _settings.RegenDelaySeconds;
                if (Current <= 0f)
                {
                    Current = 0f;
                    Exhausted = true;
                }
            }
            else if (_regenTimer > 0f)
            {
                _regenTimer -= deltaTime;
            }
            else
            {
                Current = Mathf.Min(max, Current + _settings.RegenPerSecond * deltaTime);
            }

            if (Exhausted && Current >= max * _settings.ExhaustedRecoverRatio)
                Exhausted = false;

            UpdatePanting();
        }

        private void UpdatePanting()
        {
            float ratio = Ratio;
            if (!Panting)
                Panting = Exhausted || ratio <= _settings.BreathStartRatio;
            else if (!Exhausted && ratio >= _settings.BreathStopRatio)
                Panting = false;
        }
    }
}
