using System.Collections.Generic;
using GhostHunter.Core.Voice;
using GhostHunter.Core.Scenes;
using GhostHunter.Data;
using UnityEngine;

namespace GhostHunter.Gameplay.Voice
{
    /// <summary>Game·Result·Lobby 음성 참가자와 로컬 사용자 설정을 보유한다.</summary>
    public sealed class VoiceChatService : IVoiceChatService
    {
        private readonly List<IVoiceParticipant> _participants = new(4);
        private readonly IVoiceCaptureService _capture;
        private readonly ISceneFlow _sceneFlow;
        private readonly bool _allowLobby;
        private IVoiceCaptureService _testCapture;
        private VoiceMode _mode;
        private bool _muted;
        private float _master;
        public VoiceChatService(IVoiceCaptureService capture, VoiceChatSettings settings,
            ISceneFlow sceneFlow = null, bool allowLobby = false)
        {
            _capture = capture;
            _sceneFlow = sceneFlow;
            _allowLobby = allowLobby;
            _mode = (VoiceMode)Mathf.Clamp(PlayerPrefs.GetInt("Voice.Mode", (int)settings.Mode), 0, 1);
            _muted = PlayerPrefs.GetInt("Voice.Muted", 0) != 0;
            _master = Mathf.Clamp01(PlayerPrefs.GetFloat("Voice.Volume", 1f));
        }
        public VoiceMode Mode { get => _mode; set { _mode = value; PlayerPrefs.SetInt("Voice.Mode", (int)value); } }
        // sceneFlow가 없는 경우는 독립 음성 테스트의 스테이지 상태로 취급한다.
        // 인게임 로비(ADR-0018)는 정산 화면처럼 생존·사망 구분 없이 전원이 듣는 채널이다.
        public bool IsActive => _sceneFlow == null
            || _sceneFlow.Current.IsStage() || _sceneFlow.Current is SceneId.Result or SceneId.InGameLobby
            || _allowLobby && _sceneFlow.Current == SceneId.Lobby;
        public bool IsResultChannel => _sceneFlow != null
            && (_sceneFlow.Current is SceneId.Result or SceneId.InGameLobby
                || _allowLobby && _sceneFlow.Current == SceneId.Lobby);
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
        public bool IsTransmitting { get; set; }
        // 자가 모니터는 저장하지 않는다. 다음 세션에 켜진 채로 시작하면 하울링의 원인이 된다.
        public bool SelfMonitor { get; set; }
        public string Diagnostics { get; set; }
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
                SelfMonitor = false;
                Diagnostics = null;
                LocalParticipant = null;
                PlayerPrefs.Save();
            }
        }
    }
}

