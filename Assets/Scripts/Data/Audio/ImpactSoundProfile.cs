using System;
using GhostHunter.Core.Audio;
using UnityEngine;

namespace GhostHunter.Data.Audio
{
    /// <summary>가구가 부딪힐 때 충돌 속력에 따라 어떤 큐를 얼마나 크게 낼지 정한다.</summary>
    [CreateAssetMenu(menuName = "GhostHunter/Audio/Impact Sound Profile", fileName = "ImpactSoundProfile")]
    public sealed class ImpactSoundProfile : ScriptableObject
    {
        [Serializable]
        public struct Tier
        {
            [Tooltip("이 속력(m/s) 이상일 때 쓴다. 맞는 단계가 여럿이면 가장 센 단계가 이긴다.")]
            public float MinimumSpeed;
            public SoundCue Cue;
        }

        [Tooltip("이보다 느린 충돌은 소리를 내지 않는다(m/s). 굴러가며 스치는 접촉을 거른다.")]
        [SerializeField] private float _minimumSpeed = 0.8f;
        [Tooltip("음량이 최대가 되는 충돌 속력(m/s).")]
        [SerializeField] private float _fullVolumeSpeed = 8f;
        [Tooltip("최소 속력에서의 음량 배율.")]
        [SerializeField, Range(0f, 1f)] private float _minimumVolume = 0.25f;
        [Tooltip("같은 가구가 연달아 소리를 내는 최소 간격(초). 튕기며 떨리는 소리를 막는다.")]
        [SerializeField] private float _cooldownSeconds = 0.12f;
        [SerializeField] private Tier[] _tiers = new Tier[0];

        public float MinimumSpeed => Mathf.Max(0f, _minimumSpeed);
        public float FullVolumeSpeed => Mathf.Max(MinimumSpeed + 0.01f, _fullVolumeSpeed);
        public float MinimumVolume => Mathf.Clamp01(_minimumVolume);
        public float CooldownSeconds => Mathf.Max(0f, _cooldownSeconds);

        /// <summary>충돌 속력으로 재생할 큐와 음량 배율을 정한다. 소리를 내지 않을 속력이면 false.</summary>
        public bool TryEvaluate(float speed, out SoundCue cue, out float volumeScale)
        {
            cue = null;
            volumeScale = 0f;
            if (float.IsNaN(speed) || speed < MinimumSpeed || _tiers == null)
                return false;

            float bestSpeed = float.NegativeInfinity;
            foreach (Tier tier in _tiers)
            {
                if (tier.Cue == null || speed < tier.MinimumSpeed || tier.MinimumSpeed < bestSpeed)
                    continue;
                bestSpeed = tier.MinimumSpeed;
                cue = tier.Cue;
            }

            if (cue == null)
                return false;

            float t = Mathf.InverseLerp(MinimumSpeed, FullVolumeSpeed, speed);
            volumeScale = Mathf.Lerp(MinimumVolume, 1f, t);
            return true;
        }
    }
}
