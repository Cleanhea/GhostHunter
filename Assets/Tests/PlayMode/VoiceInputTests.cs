using System.Collections;
using GhostHunter.Gameplay.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace GhostHunter.Tests.PlayMode
{
    /// <summary>
    /// 음성 입력은 3중 잠금에 들어가지 않는다 — 메뉴가 열려 있어도 말할 수 있어야 한다
    /// (기획 §8.3, docs/project/voice-chat-system.md).
    ///
    /// <para><b>EditMode 가 아니라 PlayMode 인 이유</b> — 에디트 모드에서는 장치 상태만 바뀌고
    /// <see cref="InputAction"/> 상태가 갱신되지 않는다. <c>InputState.Change</c> 로 <c>vKey.isPressed</c>
    /// 가 true 가 되어도 <c>IsPressed()</c> 는 계속 false 다(2026-09-17 확인).</para>
    /// </summary>
    public sealed class VoiceInputTests
    {
        [UnityTest]
        public IEnumerator MenuLock_StillAllowsVoiceAndMute()
        {
            // 기본값은 키보드 입력이 창/Game View 포커스를 따르게 한다. 배치모드에는 포커스가 없어
            // 이벤트가 액션까지 오지 않으므로, 테스트 동안만 둘 다 바꿨다가 되돌린다.
            InputSettings.BackgroundBehavior previousBackground = InputSystem.settings.backgroundBehavior;
            InputSettings.EditorInputBehaviorInPlayMode previousBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            var actions = ScriptableObject.CreateInstance<InputActionAsset>();
            InputActionMap map = actions.AddActionMap("Player");
            InputAction voice = map.AddAction("Voice", InputActionType.Button, "<Keyboard>/v");
            InputAction mute = map.AddAction("VoiceMute", InputActionType.Button, "<Keyboard>/m");
            InputAction look = map.AddAction("Look", InputActionType.Value, expectedControlLayout: "Vector2");
            var root = new GameObject("VoiceInputTest");
            PlayerInputReader input = root.AddComponent<PlayerInputReader>();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            System.Type type = input.GetType();
            try
            {
                type.GetField("_runtimeActions", flags).SetValue(input, actions);
                type.GetField("_voiceAction", flags).SetValue(input, voice);
                type.GetField("_voiceMuteAction", flags).SetValue(input, mute);
                type.GetField("_lookAction", flags).SetValue(input, look);
                input.SetGameplayInputLocked(true);
                actions.Enable();
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.V, Key.M));

                // 다음 프레임의 입력 갱신이 이벤트를 처리하고, 그 뒤 PlayerInputReader.Update 가 돈다.
                // WasPressedThisFrame 은 이 한 프레임에서만 참이므로 여기서 바로 단언한다.
                yield return null;

                Assert.IsTrue(input.VoiceHeld, "메뉴 잠금 중에도 PTT 입력은 읽혀야 한다");
                Assert.IsTrue(input.VoiceMutePressedThisFrame, "메뉴 잠금 중에도 마이크 토글은 읽혀야 한다");
                Assert.That(input.Move, Is.EqualTo(Vector2.zero), "잠금 중 이동 입력은 0이어야 한다");
            }
            finally
            {
                actions.Disable();
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(actions);
                InputSystem.RemoveDevice(keyboard);
                InputSystem.settings.editorInputBehaviorInPlayMode = previousBehavior;
                InputSystem.settings.backgroundBehavior = previousBackground;
            }
        }
    }
}
