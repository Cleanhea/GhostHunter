using UnityEngine;

namespace GhostHunter.Gameplay.Ghost
{
    /// <summary>
    /// "DON'T TURN AROUND" 위반 판정(project/ghost-inker.md IK-2 임시 해석): 최근 <c>window</c> 초 안의 어느 시점과 비교해
    /// 바라보는 방향(요)이 <c>threshold</c> 도 이상 달라졌으면 뒤돈 것으로 본다. 천천히 둘러보거나 좌우로 흔드는 것은
    /// 위반이 아니다. 서버가 플레이어마다 하나씩 두고 매 틱 복제된 몸 방향을 넣는다.
    /// </summary>
    public sealed class InkerTurnAroundDetector
    {
        private const int Capacity = 128;

        private readonly float[] _times = new float[Capacity];
        private readonly float[] _yaws = new float[Capacity];
        private int _start;
        private int _count;

        public void Reset()
        {
            _start = 0;
            _count = 0;
        }

        /// <summary>표본 하나를 넣는다. 창 안의 이전 표본과 비교해 기준 이상 돌았으면 true.</summary>
        public bool Sample(float time, float yaw, float window, float threshold)
        {
            while (_count > 0 && time - _times[_start] > window)
            {
                _start = (_start + 1) % Capacity;
                _count--;
            }

            bool turned = false;
            for (int i = 0; i < _count; i++)
            {
                int index = (_start + i) % Capacity;
                if (Mathf.Abs(Mathf.DeltaAngle(_yaws[index], yaw)) >= threshold)
                {
                    turned = true;
                    break;
                }
            }

            if (_count == Capacity)
            {
                _start = (_start + 1) % Capacity;
                _count--;
            }

            int write = (_start + _count) % Capacity;
            _times[write] = time;
            _yaws[write] = yaw;
            _count++;
            return turned;
        }
    }
}
