using GhostHunter.Core;
using GhostHunter.Core.Voice;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GhostHunter.UI
{
    /// <summary>마이크 상태·발화자를 표시한다. 개발 빌드는 F1 로 음성 설정·진단 패널을 펼친다.</summary>
    public sealed class VoiceIndicatorHud : MonoBehaviour
    {
        private IVoiceChatService _chat;
        private bool _expanded;
        private void Awake()
        {
            _chat = Services.Get<IVoiceChatService>();
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
            // 모드·뮤트·음량은 ESC → 설정 → 마이크/오디오 탭으로 옮겼다. 여기 펼침은 개발용(F1)만 남는다.
            if (_expanded)
            {
                if (GUILayout.Button(_chat.IsMuted ? "마이크 켜기" : "마이크 끄기")) _chat.IsMuted = !_chat.IsMuted;
                if (GUILayout.Button(_chat.Mode == VoiceMode.OpenMic ? "모드: 오픈 마이크" : "모드: PTT (V)"))
                    _chat.Mode = _chat.Mode == VoiceMode.OpenMic ? VoiceMode.PushToTalk : VoiceMode.OpenMic;
                GUILayout.Label("음성 음량");
                float volume = GUILayout.HorizontalSlider(_chat.MasterVolume, 0f, 1f);
                if (!Mathf.Approximately(volume, _chat.MasterVolume)) _chat.MasterVolume = volume;
                if (_chat.Mode == VoiceMode.OpenMic) GUILayout.Label("오픈 마이크: 노이즈 게이트가 켜져 있으면 기준보다 큰 소리만 보낸다");
                // 입력 장치·게인·노이즈 게이트는 ESC → 설정 → 마이크 탭(2026-10-03).
                GUILayout.Label(_chat.Capture.IsGateOpen ? "게이트 열림" : "게이트 닫힘 (조용함)");
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
