using GhostHunter.Core;
using GhostHunter.Core.Voice;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GhostHunter.DebugTools
{
    /// <summary>
    /// 혼자서 음성을 검증하는 창. 마이크 없이 보내는 사인파와, 내 목소리를 켠 자리에서 되돌려
    /// 듣는 자가 모니터를 켠다 → docs/architecture/voice-chat.md "혼자 검증".
    /// </summary>
    public sealed class VoiceDebugHud : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [SerializeField] private Key _toggleKey = Key.F1;
        [SerializeField] private Key _selfMonitorKey = Key.F3;
        private readonly LoopbackVoiceCapture _fake = new();
        private IVoiceChatService _chat;
        private bool _visible;
        private void Awake() { _chat = Services.Get<IVoiceChatService>(); _chat.TestDecoder = _fake; }
        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard[_toggleKey].wasPressedThisFrame) _visible = !_visible;
            // 창을 열지 않고도 걸어 다니며 켜고 끌 수 있어야 한다 — 스피커는 켠 자리에 놓인다.
            if (keyboard[_selfMonitorKey].wasPressedThisFrame) ToggleSelfMonitor();
        }
        private void ToggleSelfMonitor()
        {
            if (_chat == null) return;
            _chat.SelfMonitor = !_chat.SelfMonitor;
        }
        private void OnGUI()
        {
            if (!_visible || _chat == null) return;
            GUILayout.BeginArea(new Rect(Screen.width - 340, Screen.height - 165, 325, 150), GUI.skin.box);
            GUILayout.Label($"음성 혼자 검증   ·   창 {_toggleKey}");
            if (GUILayout.Button(_chat.TestCapture == null ? "테스트 사인파 송신 켜기" : "테스트 사인파 끄기"))
                _chat.TestCapture = _chat.TestCapture == null ? _fake : null;
            if (GUILayout.Button(_chat.SelfMonitor
                    ? $"자가 모니터 끄기 ({_selfMonitorKey})"
                    : $"여기에 테스트 스피커 놓고 듣기 ({_selfMonitorKey})"))
                ToggleSelfMonitor();
            if (_chat.SelfMonitor) GUILayout.Label("⚠ 헤드폰을 쓴다. 스피커로 들으면 하울링이 난다.");
            if (_chat.Diagnostics != null) GUILayout.Label(_chat.Diagnostics);
            GUILayout.EndArea();
        }
        private void OnDestroy()
        {
            if (_chat == null) return;
            if (ReferenceEquals(_chat.TestCapture, _fake)) _chat.TestCapture = null;
            _chat.SelfMonitor = false;
            _chat.TestDecoder = null;
        }
#endif
    }
}
