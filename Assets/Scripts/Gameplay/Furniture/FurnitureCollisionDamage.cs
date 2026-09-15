using UnityEngine;

namespace GhostHunter.Gameplay.Furniture
{
    /// <summary>충돌 면의 수직 속도와 내구도 감소량을 계산한다.</summary>
    public static class FurnitureCollisionDamage
    {
        /// <summary>미끄러지는 접선 방향을 제외한 상대 속도의 크기를 구한다.</summary>
        public static float NormalSpeed(Vector3 relativeVelocity, Vector3 normal)
        {
            float speed = Mathf.Abs(Vector3.Dot(relativeVelocity, normal.normalized));
            return float.IsFinite(speed) ? speed : 0f;
        }

        /// <summary>최소 속도 초과분에 비례하는 정수 피해를 상한 안에서 구한다.</summary>
        public static int Calculate(float speed, float minimumSpeed, float perSpeed,
            float weightMultiplier, int maximum)
        {
            if (!float.IsFinite(speed) || !float.IsFinite(minimumSpeed)
                || !float.IsFinite(perSpeed) || !float.IsFinite(weightMultiplier)
                || speed <= minimumSpeed || perSpeed <= 0f || weightMultiplier <= 0f || maximum <= 0)
                return 0;
            double damage = ((double)speed - minimumSpeed) * perSpeed * weightMultiplier;
            return (int)System.Math.Min(maximum, System.Math.Floor(damage));
        }
    }
}
