using GhostHunter.Core.Voice;
using UnityEngine;
using UnityEngine.Audio;

namespace GhostHunter.Data
{
    /// <summary>근접 음성의 감쇠·가림·전송·수신 버퍼·발화 표시 초기 설정.</summary>
    [CreateAssetMenu(menuName = "GhostHunter/Gameplay/Voice Chat Settings", fileName = "VoiceChatSettings")]
    public sealed class VoiceChatSettings : ScriptableObject
    {
        [SerializeField] private float _minimumDistance = 1.5f;
        [SerializeField] private float _fadeDistance = 7f;
        [SerializeField] private float _maximumDistance = 25f;
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
        // 아래 세 값은 "말하는 중" 표시에만 쓴다. 송신은 소리 크기로 거르지 않는다(2026-09-27).
        [SerializeField] private float _openThreshold = -42f;
        [SerializeField] private float _closeThreshold = -48f;
        [SerializeField] private float _hangoverSeconds = 0.35f;
        // 노이즈 게이트(2026-10-03). 여는 기준(dB)은 개인 설정이고, 닫힘 여유·유지·앞당김은 여기서 튜닝한다.
        // 말끝이 잘리지 않게 닫힘은 기준보다 낮게, 유지 시간은 길게, 여는 순간 직전 소리를 앞당겨 보낸다.
        [SerializeField] private float _gateHysteresisDb = 6f;
        [SerializeField] private float _gateHoldSeconds = 0.5f;
        [SerializeField] private int _gatePrerollFrames = 3;
        // Opus 인코더. 48kHz mono 음성 기준 24kbps 면 20ms 프레임이 약 60B 다.
        [SerializeField] private int _opusBitrate = 24000;
        [SerializeField] private int _opusComplexity = 5;
        [SerializeField] private VoiceMode _mode = VoiceMode.OpenMic;
        [SerializeField] private float _sendHz = 20f;
        [SerializeField] private int _serverPacketsPerSecond = 30;
        // 수신 선버퍼(재생 시작 전 모을 양)와 최대 적체. 넘치면 오래된 샘플부터 건너뛴다.
        [SerializeField] private float _jitterSeconds = 0.1f;
        [SerializeField] private float _maximumJitterSeconds = 0.4f;
        [SerializeField] private float _silenceTimeout = 0.5f;
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
        public float GateHysteresisDb => Mathf.Clamp(_gateHysteresisDb, 0f, 20f);
        public float GateHoldSeconds => Mathf.Clamp(_gateHoldSeconds, 0f, 2f);
        public int GatePrerollFrames => Mathf.Clamp(_gatePrerollFrames, 0, 10);
        public int OpusBitrate => Mathf.Clamp(_opusBitrate, 6000, 64000);
        public int OpusComplexity => Mathf.Clamp(_opusComplexity, 0, 10);
        public VoiceMode Mode => _mode;
        public float SendHz => Mathf.Clamp(_sendHz, 1f, 20f);
        public int ServerPacketsPerSecond => Mathf.Clamp(_serverPacketsPerSecond, 1, 30);
        public float JitterSeconds => Mathf.Clamp(_jitterSeconds, 0.04f, MaximumJitterSeconds);
        public float MaximumJitterSeconds => Mathf.Clamp(_maximumJitterSeconds, 0.04f, 1f);
        public float SilenceTimeout => Mathf.Max(MaximumJitterSeconds, _silenceTimeout);
        public AudioMixerGroup OutputGroup => _outputGroup;
    }
}
