using UnityEngine;

namespace GhostHunter.Gameplay.Ghost
{
    /// <summary>
    /// 베이스 귀신의 '뒤돌아보지마' 기믹(ghost-base.md §3) 상태 기계. 순수 C# 이라 EditMode 로 검증한다.
    /// 어택 중 누군가 귀신을 바라보면 정지(최대 <c>maxFreeze</c>초)하고, 해제된 뒤에는 정지 면역과
    /// 이동 가속이 붙는다. 서버만 돌린다 — 클라이언트는 아무것도 판정하지 않는다.
    /// </summary>
    public sealed class GhostGazeFreeze
    {
        private enum Mode : byte
        {
            Free = 0,
            Frozen = 1,
            Released = 2,
        }

        private readonly float _maxFreeze;
        private readonly float _immunity;
        private readonly float _boost;
        private readonly float _boostMultiplier;

        private Mode _mode;
        private float _elapsed;

        public GhostGazeFreeze(float maxFreezeSeconds, float immunitySeconds,
            float boostSeconds, float boostMultiplier)
        {
            _maxFreeze = Mathf.Max(0.01f, maxFreezeSeconds);
            _immunity = Mathf.Max(0f, immunitySeconds);
            _boost = Mathf.Max(0f, boostSeconds);
            _boostMultiplier = Mathf.Max(0f, boostMultiplier);
        }

        public bool IsFrozen => _mode == Mode.Frozen;

        /// <summary>해제 직후 면역 구간이다. 이 동안에는 바라봐도 정지하지 않는다.</summary>
        public bool IsImmune => _mode == Mode.Released && _elapsed < _immunity;

        /// <summary>해제 직후 가속 구간이면 설정 배율, 아니면 1.</summary>
        public float SpeedMultiplier =>
            _mode == Mode.Released && _elapsed < _boost ? _boostMultiplier : 1f;

        /// <param name="gazed">지금 한 명이라도 귀신을 바라보고 있는가.</param>
        public void Tick(float deltaTime, bool gazed)
        {
            switch (_mode)
            {
                case Mode.Free:
                    if (gazed)
                        Freeze();
                    break;

                case Mode.Frozen:
                    _elapsed += deltaTime;
                    if (!gazed || _elapsed >= _maxFreeze)
                        Release();
                    break;

                case Mode.Released:
                    _elapsed += deltaTime;
                    // 면역이 끝난 뒤 다시 정지시키면 가속도 끝난다.
                    if (_elapsed >= _immunity && gazed)
                        Freeze();
                    else if (_elapsed >= Mathf.Max(_immunity, _boost))
                        _mode = Mode.Free;
                    break;
            }
        }

        /// <summary>0.8m 이하 근접 공격처럼 시선과 무관하게 정지를 풀어야 할 때. 정지 중이 아니면 무시한다.</summary>
        public void ForceRelease()
        {
            if (_mode == Mode.Frozen)
                Release();
        }

        public void Reset()
        {
            _mode = Mode.Free;
            _elapsed = 0f;
        }

        private void Freeze()
        {
            _mode = Mode.Frozen;
            _elapsed = 0f;
        }

        private void Release()
        {
            _mode = Mode.Released;
            _elapsed = 0f;
        }
    }
}
