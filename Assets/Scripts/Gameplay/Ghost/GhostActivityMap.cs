using System.Collections.Generic;
using UnityEngine;

namespace GhostHunter.Gameplay.Ghost
{
    /// <summary>
    /// 층별 플레이어 활동량(원문 0.4 §7.2). 활동한 만큼 쌓이고 반감기로 줄어든다.
    /// 배회 목적지 점수에 "활동이 많은 층 가산점"을 주는 데만 쓴다 — 활동량이 다 비슷하거나 거의 없으면
    /// 가산점이 같아져 기존 방문 기록 기반 배회(층 무관)와 다르지 않다(§7.4 "비슷하면 평상시 배회").
    /// </summary>
    internal sealed class GhostActivityMap
    {
        private readonly Dictionary<int, float> _byFloor = new();
        private readonly List<int> _keys = new(4);

        public int FloorCount => _byFloor.Count;

        public void Clear() => _byFloor.Clear();

        public float Get(int floor) => _byFloor.TryGetValue(floor, out float value) ? value : 0f;

        public float Peak
        {
            get
            {
                float peak = 0f;
                foreach (KeyValuePair<int, float> pair in _byFloor)
                    peak = Mathf.Max(peak, pair.Value);
                return peak;
            }
        }

        public void Add(int floor, float amount)
        {
            if (amount <= 0f)
                return;
            _byFloor[floor] = Get(floor) + amount;
        }

        public void Decay(float deltaTime, float halfLifeSeconds)
        {
            if (deltaTime <= 0f || _byFloor.Count == 0)
                return;

            float factor = Mathf.Pow(0.5f, deltaTime / Mathf.Max(0.1f, halfLifeSeconds));
            // 열거 중 수정을 피하려고 키를 복사한다(층은 많아야 몇 개).
            _keys.Clear();
            foreach (int key in _byFloor.Keys)
                _keys.Add(key);
            for (int i = 0; i < _keys.Count; i++)
            {
                float value = _byFloor[_keys[i]] * factor;
                if (value < 0.001f)
                    _byFloor.Remove(_keys[i]);
                else
                    _byFloor[_keys[i]] = value;
            }
        }

        /// <summary>
        /// 그 층의 배회 가산점(초). 가장 활동이 많은 층이 <paramref name="bonusSeconds"/>, 나머지는 비례한다.
        /// 최고 활동량이 <paramref name="minimumPeak"/> 미만이면 모든 층 0 — 활동이 없으면 편향하지 않는다.
        /// </summary>
        public float Bonus(int floor, float bonusSeconds, float minimumPeak)
        {
            float peak = Peak;
            if (peak <= 0f || peak < minimumPeak)
                return 0f;
            return bonusSeconds * Get(floor) / peak;
        }
    }
}
