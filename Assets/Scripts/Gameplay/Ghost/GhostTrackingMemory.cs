using UnityEngine;

namespace GhostHunter.Gameplay.Ghost
{
    /// <summary>관측한 단서만 보관한다. 현재 플레이어 Transform을 조회하지 않는다.</summary>
    internal sealed class GhostTrackingMemory
    {
        public bool HasObservation { get; private set; }
        public ulong PlayerId { get; private set; }
        public Vector3 Position { get; private set; }
        public Vector3 Velocity { get; private set; }
        public float ObservedAt { get; private set; }

        public void Observe(ulong playerId, Vector3 position, float time, float speedLimit)
        {
            if (HasObservation && PlayerId == playerId && time > ObservedAt)
            {
                Vector3 velocity = (position - Position) / (time - ObservedAt);
                velocity.y = 0f;
                Velocity = Vector3.ClampMagnitude(velocity, Mathf.Max(0f, speedLimit));
            }
            else if (!HasObservation || PlayerId != playerId)
            {
                Velocity = Vector3.zero;
            }

            HasObservation = true;
            PlayerId = playerId;
            Position = position;
            ObservedAt = time;
        }

        public float Confidence(float time, float memorySeconds)
        {
            return HasObservation && memorySeconds > 0f
                ? Mathf.Clamp01(1f - Mathf.Max(0f, time - ObservedAt) / memorySeconds)
                : 0f;
        }

        public Vector3 Predict(float time, float memorySeconds, float seconds, float maxDistance)
        {
            return Position + Vector3.ClampMagnitude(Velocity * Mathf.Max(0f, seconds),
                Mathf.Max(0f, maxDistance)) * Confidence(time, memorySeconds);
        }

        public void Clear()
        {
            HasObservation = false;
            PlayerId = 0;
            Position = Vector3.zero;
            Velocity = Vector3.zero;
            ObservedAt = 0f;
        }

        internal static float SearchScore(Vector3 candidate, Vector3 origin, Vector3 direction,
            float confidence, float novelty, float pathLength, GhostPrototypeSettings settings)
        {
            Vector3 offset = candidate - origin;
            offset.y = 0f;
            direction.y = 0f;
            float alignment = offset.sqrMagnitude > 0.001f && direction.sqrMagnitude > 0.001f
                ? Vector3.Dot(offset.normalized, direction.normalized) : 0f;
            return alignment * Mathf.Clamp01(confidence) * settings.SearchDirectionWeight
                + Mathf.Clamp01(novelty) * settings.SearchNoveltyWeight
                - pathLength / Mathf.Max(0.1f, settings.ChaseSpeed) * settings.SearchTravelWeight;
        }
    }
}
