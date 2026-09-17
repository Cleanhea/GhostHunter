using GhostHunter.Core;
using GhostHunter.Core.Voice;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GhostHunter.DebugTools
{
    /// <summary>F1에서 실제 마이크와 사인파 송신을 전환한다.</summary>
    public sealed class VoiceDebugHud : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private readonly LoopbackVoiceCapture _fake = new();
        private IVoiceChatService _chat;
        private bool _visible;
        private void Awake() { _chat = Services.Get<IVoiceChatService>(); _chat.TestDecoder = _fake; }
        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame) _visible = !_visible;
        }
        private void OnGUI()
        {
            if (!_visible || _chat == null) return;
            if (GUI.Button(new Rect(Screen.width - 340, Screen.height - 50, 325, 30),
                _chat.TestCapture == null ? "테스트 사인파 송신 켜기" : "테스트 사인파 끄기"))
                _chat.TestCapture = _chat.TestCapture == null ? _fake : null;
        }
        private void OnDestroy()
        {
            if (_chat == null) return;
            if (ReferenceEquals(_chat.TestCapture, _fake)) _chat.TestCapture = null;
            _chat.TestDecoder = null;
        }
#endif
    }
}
