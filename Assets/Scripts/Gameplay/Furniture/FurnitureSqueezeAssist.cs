using System.Collections.Generic;
using UnityEngine;

namespace GhostHunter.Gameplay.Furniture
{
    /// <summary>
    /// 2인 운반 가구가 문틀 등에 걸렸을 때 시험할 작은 자세 보정(수직축 회전·옆 이동) 후보를 만든다
    /// → docs/architecture/throw-system.md §3.2. 보정이 작은 것부터 시험해 처음으로 비는 자세를 쓴다.
    /// </summary>
    public static class FurnitureSqueezeAssist
    {
        public readonly struct Candidate
        {
            public Candidate(float yaw, float lateral, float cost)
            {
                Yaw = yaw;
                Lateral = lateral;
                Cost = cost;
            }

            /// <summary>월드 수직축 회전(도).</summary>
            public float Yaw { get; }

            /// <summary>진행 방향의 수평 오른쪽으로 옮기는 거리(m). 음수면 왼쪽.</summary>
            public float Lateral { get; }

            /// <summary>보정 크기. 각 축 최대값에 대한 비율의 합이라 0(보정 없음)부터 2까지.</summary>
            public float Cost { get; }

            public bool IsIdentity => Yaw == 0f && Lateral == 0f;
        }

        /// <summary>
        /// 회전 ±<paramref name="maxYaw"/>·옆 이동 ±<paramref name="maxLateral"/> 격자의 후보를 보정이 작은 순서로 채운다.
        /// 첫 후보는 항상 보정 없음이다. 같은 크기면 한 축만 쓰는 후보, 옆 이동이 작은 후보, 양수 쪽이 먼저다.
        /// </summary>
        public static void BuildCandidates(
            float maxYaw,
            float yawStep,
            float maxLateral,
            float lateralStep,
            List<Candidate> output)
        {
            output.Clear();
            int yawCount = StepCount(maxYaw, yawStep);
            int lateralCount = StepCount(maxLateral, lateralStep);

            for (int y = -yawCount; y <= yawCount; y++)
            {
                for (int l = -lateralCount; l <= lateralCount; l++)
                {
                    float cost = (yawCount > 0 ? Mathf.Abs(y) / (float)yawCount : 0f)
                        + (lateralCount > 0 ? Mathf.Abs(l) / (float)lateralCount : 0f);
                    output.Add(new Candidate(y * yawStep, l * lateralStep, cost));
                }
            }

            output.Sort(Compare);
        }

        private static int StepCount(float max, float step)
        {
            if (max <= 0f || step <= 0f)
                return 0;

            return Mathf.FloorToInt(max / step + 0.0001f);
        }

        private static int Compare(Candidate a, Candidate b)
        {
            int result = a.Cost.CompareTo(b.Cost);
            if (result != 0)
                return result;

            bool aCombined = a.Yaw != 0f && a.Lateral != 0f;
            bool bCombined = b.Yaw != 0f && b.Lateral != 0f;
            if (aCombined != bCombined)
                return aCombined ? 1 : -1;

            result = Mathf.Abs(a.Lateral).CompareTo(Mathf.Abs(b.Lateral));
            if (result != 0)
                return result;

            result = Mathf.Abs(a.Yaw).CompareTo(Mathf.Abs(b.Yaw));
            if (result != 0)
                return result;

            result = b.Yaw.CompareTo(a.Yaw);
            return result != 0 ? result : b.Lateral.CompareTo(a.Lateral);
        }
    }
}
