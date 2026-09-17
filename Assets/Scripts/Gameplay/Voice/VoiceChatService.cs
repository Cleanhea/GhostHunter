using System.Collections.Generic;
using GhostHunter.Core.Voice;
using GhostHunter.Data;
using UnityEngine;

namespace GhostHunter.Gameplay.Voice
{
    /// <summary>게임 씬 음성 참가자와 로컬 사용자 설정을 보유한다.</summary>
    public sealed class VoiceChatService : IVoiceChatService
    {
        private readonly List<IVoiceParticipant> _participants = new(4);
        private readonly IVoiceCaptureService _capture;
        private IVoiceCaptureService _testCapture;
        private VoiceMode _mode;
        private bool _muted;
        private float _master;
        private float _threshold;
        public VoiceChatService(IVoiceCaptureService capture, VoiceChatSettings settings)
        {
            _capture = capture;
            _mode = (VoiceMode)Mathf.Clamp(PlayerPrefs.GetInt("Voice.Mode", (int)settings.Mode), 0, 1);
            _muted = PlayerPrefs.GetInt("Voice.Muted", 0) != 0;
            _master = Mathf.Clamp01(PlayerPrefs.GetFloat("Voice.Volume", 1f));
            _threshold = Mathf.Clamp(PlayerPrefs.GetFloat("Voice.Threshold", settings.OpenThreshold), -80f, 0f);
        }
        public VoiceMode Mode { get => _mode; set { _mode = value; PlayerPrefs.SetInt("Voice.Mode", (int)value); } }
        public bool IsMuted
        {
            get => _muted;
            set
            {
                _muted = value;
                if (value) { Capture.SetRecording(false); IsTransmitting = false; }
                PlayerPrefs.SetInt("Voice.Muted", value ? 1 : 0);
            }
        }
        public float MasterVolume { get => _master; set { _master = Mathf.Clamp01(value); PlayerPrefs.SetFloat("Voice.Volume", _master); } }
        public float OpenThreshold { get => _threshold; set { _threshold = Mathf.Clamp(value, -80f, 0f); PlayerPrefs.SetFloat("Voice.Threshold", _threshold); } }
        public bool IsTransmitting { get; set; }
        public IVoiceCaptureService Capture => _testCapture ?? _capture;
        public IVoiceCaptureService TestDecoder { get; set; }
        public IVoiceCaptureService TestCapture
        {
            get => _testCapture;
            set { Capture.SetRecording(false); _testCapture = value; IsTransmitting = false; }
        }
        public IVoiceParticipant LocalParticipant { get; private set; }
        public IReadOnlyList<IVoiceParticipant> Participants => _participants;
        public void Register(IVoiceParticipant participant, bool local)
        {
            if (!_participants.Contains(participant)) _participants.Add(participant);
            if (local) LocalParticipant = participant;
        }
        public void Unregister(IVoiceParticipant participant)
        {
            _participants.Remove(participant);
            if (ReferenceEquals(LocalParticipant, participant))
            {
                Capture.SetRecording(false);
                IsTransmitting = false;
                LocalParticipant = null;
                PlayerPrefs.Save();
            }
        }
    }
}

