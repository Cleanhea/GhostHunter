using GhostHunter.Core.Voice;
using UnityEngine;
using UnityEngine.Audio;

namespace GhostHunter.Data
{
    /// <summary>근접 음성의 감쇠·가림·전송·VAD 초기 설정.</summary>
    [CreateAssetMenu(menuName = "GhostHunter/Gameplay/Voice Chat Settings", fileName = "VoiceChatSettings")]
    public sealed class VoiceChatSettings : ScriptableObject
    {
        [SerializeField] private float _minimumDistance = 1.5f;
        [SerializeField] private float _fadeDistance = 7f;
        [SerializeField] private float _maximumDistance = 10f;
        [SerializeField] private float _rolloffExponent = 0.9f;
        [SerializeField] private float _verticalNear = 1.2f;
        [SerializeField] private float _verticalCut = 2.6f;
        [SerializeField] private float _wallFactor = 0.32f;
        [SerializeField] private int _maximumWalls = 3;
        [SerializeField] private float _occlusionFloor = 0.04f;
        [SerializeField] private float _rayOffset = 0.6f;
        [SerializeField] private float _openCutoff = 22000f;
        [SerializeField] private float _blockedCutoff = 750f;
        [SerializeField] private float _gainSmoothTime = 0.12f;
        [SerializeField] private float _cutoffSmoothTime = 0.18f;
        [SerializeField] private float _occlusionHz = 10f;
        [SerializeField] private float _serverMarginXZ = 2f;
        [SerializeField] private float _serverMarginY = 1f;
        [SerializeField] private float _openThreshold = -42f;
        [SerializeField] private float _closeThreshold = -48f;
        [SerializeField] private float _hangoverSeconds = 0.35f;
        [SerializeField] private float _preRollSeconds = 0.15f;
        [SerializeField] private VoiceMode _mode = VoiceMode.OpenMic;
        [SerializeField] private float _sendHz = 20f;
        [SerializeField] private int _serverPacketsPerSecond = 30;
        [SerializeField] private float _jitterSeconds = 0.08f;
        [SerializeField] private float _maximumJitterSeconds = 0.2f;
        [SerializeField] private float _silenceTimeout = 0.2f;
        [SerializeField] private AudioMixerGroup _outputGroup;
        public float MinimumDistance => Mathf.Max(0.01f, _minimumDistance);
        public float FadeDistance => Mathf.Clamp(_fadeDistance, MinimumDistance, MaximumDistance - 0.01f);
        public float MaximumDistance => Mathf.Max(MinimumDistance + 0.02f, _maximumDistance);
        public float RolloffExponent => Mathf.Max(0f, _rolloffExponent);
        public float VerticalNear => Mathf.Max(0f, _verticalNear);
        public float VerticalCut => Mathf.Max(VerticalNear + 0.01f, _verticalCut);
        public float WallFactor => Mathf.Clamp01(_wallFactor);
        public int MaximumWalls => Mathf.Clamp(_maximumWalls, 1, 8);
        public float OcclusionFloor => Mathf.Clamp01(_occlusionFloor);
        public float RayOffset => Mathf.Max(0f, _rayOffset);
        public float OpenCutoff => Mathf.Clamp(_openCutoff, 10f, 22000f);
        public float BlockedCutoff => Mathf.Clamp(_blockedCutoff, 10f, OpenCutoff);
        public float GainSmoothTime => Mathf.Max(0.001f, _gainSmoothTime);
        public float CutoffSmoothTime => Mathf.Max(0.001f, _cutoffSmoothTime);
        public float OcclusionHz => Mathf.Clamp(_occlusionHz, 1f, 30f);
        public float ServerMarginXZ => Mathf.Max(0f, _serverMarginXZ);
        public float ServerMarginY => Mathf.Max(0f, _serverMarginY);
        public float OpenThreshold => Mathf.Clamp(_openThreshold, -80f, 0f);
        public float CloseThreshold => Mathf.Min(OpenThreshold, _closeThreshold);
        public float HangoverSeconds => Mathf.Max(0f, _hangoverSeconds);
        public float PreRollSeconds => Mathf.Clamp(_preRollSeconds, 0f, 0.15f);
        public VoiceMode Mode => _mode;
        public float SendHz => Mathf.Clamp(_sendHz, 1f, 20f);
        public int ServerPacketsPerSecond => Mathf.Clamp(_serverPacketsPerSecond, 1, 30);
        public float JitterSeconds => Mathf.Clamp(_jitterSeconds, 0.04f, MaximumJitterSeconds);
        public float MaximumJitterSeconds => Mathf.Clamp(_maximumJitterSeconds, 0.04f, 0.2f);
        public float SilenceTimeout => Mathf.Max(MaximumJitterSeconds, _silenceTimeout);
        public AudioMixerGroup OutputGroup => _outputGroup;
    }
}
