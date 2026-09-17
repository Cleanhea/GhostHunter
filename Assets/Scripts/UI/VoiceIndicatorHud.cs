using GhostHunter.Core;
using GhostHunter.Core.Voice;
using GhostHunter.Gameplay.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GhostHunter.UI
{
    /// <summary>마이크 상태·발화자와 음성 설정을 표시한다.</summary>
    public sealed class VoiceIndicatorHud : MonoBehaviour
    {
        private IVoiceChatService _chat;
        private ILocalPlayerContext _local;
        private string _settingsHint;
        private bool _expanded;
        private void Awake()
        {
            _chat = Services.Get<IVoiceChatService>();
            _local = Services.Get<ILocalPlayerContext>();
        }
        private void Update()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame) _expanded = !_expanded;
#endif
        }
        private void OnGUI()
        {
            if (_chat == null || _chat.LocalParticipant == null) return;
            GUILayout.BeginArea(new Rect(Screen.width - 340, 15, 325, Screen.height - 30), GUI.skin.box);
            GUILayout.Label(_chat.IsMuted ? "마이크 꺼짐 [M]" : _chat.IsTransmitting ? "송신 중 [M: 끄기]" : "마이크 켜짐 [M: 끄기]");
            if (!_chat.Capture.IsAvailable) GUILayout.Label(_chat.Capture.Status);
            foreach (IVoiceParticipant participant in _chat.Participants)
                if (participant.IsSpeaking) GUILayout.Label(participant.DisplayName + " · 말하는 중");
            bool menuOpen = _local.Input != null && _local.Input.IsGameplayInputLocked;
            if (_expanded || menuOpen)
            {
                if (GUILayout.Button(_chat.IsMuted ? "마이크 켜기" : "마이크 끄기")) _chat.IsMuted = !_chat.IsMuted;
                if (GUILayout.Button(_chat.Mode == VoiceMode.OpenMic ? "모드: 오픈 마이크" : "모드: PTT (V)"))
                    _chat.Mode = _chat.Mode == VoiceMode.OpenMic ? VoiceMode.PushToTalk : VoiceMode.OpenMic;
                GUILayout.Label("음성 음량");
                float volume = GUILayout.HorizontalSlider(_chat.MasterVolume, 0f, 1f);
                if (!Mathf.Approximately(volume, _chat.MasterVolume)) _chat.MasterVolume = volume;
                GUILayout.Label($"마이크 감지 임계값: {_chat.OpenThreshold:F0} dBFS");
                float threshold = GUILayout.HorizontalSlider(_chat.OpenThreshold, -70f, -10f);
                if (!Mathf.Approximately(threshold, _chat.OpenThreshold)) _chat.OpenThreshold = threshold;
                // 입력 장치·게인은 Steam 이 쥐고 있다(기획 §5.1 제약 1). 버튼이 실패하면 경로를 알려준다.
                if (GUILayout.Button("Steam 음성 설정 열기 (입력 장치·게인)"))
                    _settingsHint = _chat.Capture.OpenSettings()
                        ? null
                        : "Steam 오버레이를 열 수 없다. Steam 친구 → 음성 설정에서 바꾼다.";
                if (_settingsHint != null) GUILayout.Label(_settingsHint);
                foreach (IVoiceParticipant participant in _chat.Participants)
                {
                    if (ReferenceEquals(participant, _chat.LocalParticipant)) continue;
                    GUILayout.Label(participant.DisplayName);
                    participant.Volume = GUILayout.HorizontalSlider(participant.Volume, 0f, 1f);
                    if (GUILayout.Button(participant.Volume > 0f ? "이 플레이어 음소거" : "음소거 해제"))
                        participant.Volume = participant.Volume > 0f ? 0f : 1f;
                }
            }
            GUILayout.EndArea();
        }
    }
}
