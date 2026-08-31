using UnityEngine;

namespace GhostHunter.Gameplay.Sanity
{
    /// <summary>정신력 초기값, 감소량, 시간, 디버프 임계값을 보관한다.</summary>
    [CreateAssetMenu(
        menuName = "GhostHunter/Gameplay/Sanity System Settings",
        fileName = "SanitySystemSettings")]
    public sealed class SanitySystemSettings : ScriptableObject
    {
        [Header("Range and stage start")]
        [SerializeField] private int _minimumSanity;
        [SerializeField] private int _maximumSanity = 100;
        [SerializeField] private int _startingSanity = 100;

        [Header("Decrease")]
        [SerializeField] private float _darknessInterval = 10f;
        [SerializeField] private int _darknessDecrease = 1;
        [SerializeField] private int _ghostEventDecrease = 15;
        [SerializeField] private int _corpseWitnessDecrease = 20;

        [Header("Debuff thresholds")]
        [SerializeField] private int _cameraNoiseThreshold = 20;
        [SerializeField] private int _whisperThreshold = 10;
        [SerializeField] private int _breathingHeartbeatThreshold = 5;
        [SerializeField] private float _whisperMinimumInterval = 3f;
        [SerializeField] private float _whisperMaximumInterval = 10f;

        public int MinimumSanity => _minimumSanity;
        public int MaximumSanity => _maximumSanity;
        public int StartingSanity => _startingSanity;
        public float DarknessInterval => _darknessInterval;
        public int DarknessDecrease => _darknessDecrease;
        public int GhostEventDecrease => _ghostEventDecrease;
        public int CorpseWitnessDecrease => _corpseWitnessDecrease;
        public int CameraNoiseThreshold => _cameraNoiseThreshold;
        public int WhisperThreshold => _whisperThreshold;
        public int BreathingHeartbeatThreshold => _breathingHeartbeatThreshold;
        public float WhisperMinimumInterval => _whisperMinimumInterval;
        public float WhisperMaximumInterval => _whisperMaximumInterval;

        private void OnValidate()
        {
            _minimumSanity = Mathf.Max(0, _minimumSanity);
            _maximumSanity = Mathf.Max(_minimumSanity + 1, _maximumSanity);
            _startingSanity = Mathf.Clamp(_startingSanity, _minimumSanity, _maximumSanity);

            _darknessInterval = Mathf.Max(0.1f, _darknessInterval);
            _darknessDecrease = Mathf.Max(1, _darknessDecrease);
            _ghostEventDecrease = Mathf.Max(1, _ghostEventDecrease);
            _corpseWitnessDecrease = Mathf.Max(1, _corpseWitnessDecrease);

            _cameraNoiseThreshold = Mathf.Clamp(
                _cameraNoiseThreshold,
                _minimumSanity,
                _maximumSanity);
            _whisperThreshold = Mathf.Clamp(
                _whisperThreshold,
                _minimumSanity,
                _cameraNoiseThreshold);
            _breathingHeartbeatThreshold = Mathf.Clamp(
                _breathingHeartbeatThreshold,
                _minimumSanity,
                _whisperThreshold);

            _whisperMinimumInterval = Mathf.Max(0.1f, _whisperMinimumInterval);
            _whisperMaximumInterval = Mathf.Max(
                _whisperMinimumInterval,
                _whisperMaximumInterval);
        }
    }
}
