using UnityEngine;

namespace GhostHunter.Gameplay.Player
{
    /// <summary>
    /// 이동 속도 → 애니메이터 입력 변환 규칙. <see cref="PlayerCharacterAnimator"/> 가 매 프레임 쓰고,
    /// MonoBehaviour 없이 EditMode 에서 검사할 수 있게 순수 함수로 뗐다(<see cref="PlayerPosture"/> 와 같은 관례).
    /// </summary>
    public static class PlayerCharacterAnimationRules
    {
        /// <summary>
        /// 한 프레임의 수평 변위로 잰 속도. 순간이동으로 볼 만큼 크거나 시간이 흐르지 않았으면
        /// <paramref name="fallback"/>(직전 값)을 돌려준다.
        /// </summary>
        public static float MeasurePlanarSpeed(Vector3 previous, Vector3 current, float deltaTime,
            float teleportSpeed, float fallback)
        {
            if (deltaTime <= 0f)
                return fallback;

            Vector3 delta = current - previous;
            delta.y = 0f;
            float speed = delta.magnitude / deltaTime;
            return speed > teleportSpeed ? fallback : speed;
        }

        /// <summary>프레임률과 무관한 지수 평활. 평활 시간이 0이면 곧바로 목표값이 된다.</summary>
        public static float SmoothSpeed(float current, float target, float smoothTime, float deltaTime)
        {
            if (smoothTime <= 0f)
                return target;

            float t = 1f - Mathf.Exp(-Mathf.Max(0f, deltaTime) / smoothTime);
            return Mathf.Lerp(current, target, t);
        }

        public static bool IsMoving(float planarSpeed, PlayerCharacterAnimationSettings settings)
        {
            return planarSpeed > settings.MoveThreshold;
        }

        /// <summary>발이 덜 미끄러지도록 이동 속도에 비례시키되 설정 범위로 자른 걷기 배속.</summary>
        public static float WalkPlaybackSpeed(float planarSpeed, PlayerCharacterAnimationSettings settings)
        {
            return Mathf.Clamp(planarSpeed / settings.WalkClipSpeed,
                settings.MinWalkPlaybackSpeed, settings.MaxWalkPlaybackSpeed);
        }
    }
}
