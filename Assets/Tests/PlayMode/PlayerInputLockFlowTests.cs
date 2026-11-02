using System.Collections;
using System.Reflection;
using GhostHunter.Gameplay.Player;
using GhostHunter.Gameplay.Recovery;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace GhostHunter.Tests.PlayMode
{
    /// <summary>실제 Input System 이벤트로 잠금 우선순위·허용 입력·즉시 초기화를 검증한다.</summary>
    public sealed class PlayerInputLockFlowTests
    {
        private GameObject _root;
        private PlayerInputReader _input;
        private InputActionAsset _actions;
        private Keyboard _keyboard;
        private Mouse _mouse;
        private InputSettings.BackgroundBehavior _previousBackground;
        private InputSettings.EditorInputBehaviorInPlayMode _previousEditorBehavior;
        private bool _previousRecovery;

        [SetUp]
        public void SetUp()
        {
            _previousBackground = InputSystem.settings.backgroundBehavior;
            _previousEditorBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            _previousRecovery = StageRecoveryGate.Restoring;
            StageRecoveryGate.Restoring = false;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _mouse = InputSystem.AddDevice<Mouse>();
            _actions = ScriptableObject.CreateInstance<InputActionAsset>();
            InputActionMap map = _actions.AddActionMap("Player");
            map.devices = new InputDevice[] { _keyboard, _mouse };
            _root = new GameObject("PlayerInputLockTest");
            _input = _root.AddComponent<PlayerInputReader>();
            Set("_runtimeActions", _actions);
            InputAction move = map.AddAction("Move", InputActionType.Value);
            move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/w");
            Set("_moveAction", move);
            Set("_lookAction", map.AddAction("Look", InputActionType.Value, "<Mouse>/delta"));
            Set("_voiceAction", map.AddAction("Voice", InputActionType.Button, "<Keyboard>/v"));
            Set("_voiceMuteAction", map.AddAction("VoiceMute", InputActionType.Button, "<Keyboard>/m"));
            Set("_burrowAction", map.AddAction("Burrow", InputActionType.Button, "<Keyboard>/b"));
            Set("_headlampAction", map.AddAction("Headlamp", InputActionType.Button, "<Keyboard>/f"));
            Bind(map, "_jumpAction", "<Keyboard>/space");
            Bind(map, "_attackAction", "<Mouse>/leftButton");
            Set("_interactAction", map.AddAction("Interact", InputActionType.Button, "<Keyboard>/e", interactions: "hold"));
            Bind(map, "_crouchAction", "<Keyboard>/leftCtrl");
            Bind(map, "_sprintAction", "<Keyboard>/leftShift");
            Bind(map, "_detectAction", "<Keyboard>/r");
            Bind(map, "_proneAction", "<Keyboard>/z");
            Bind(map, "_quickSlotAction", "<Keyboard>/q");
            Bind(map, "_useDriverAction", "<Keyboard>/g");
            Bind(map, "_spectateDescendAction", "<Keyboard>/c");
            Bind(map, "_spectateToggleAction", "<Keyboard>/t");
            Bind(map, "_spectateNextAction", "<Keyboard>/n");
            Bind(map, "_rotateFurnitureModeAction", "<Mouse>/middleButton");
            Set("_rotateFurnitureAction", map.AddAction("Rotate", InputActionType.Value, "<Mouse>/scroll"));
            _input.SetSkillInputMode(SkillInputMode.UndergroundMove);
            _actions.Enable();
        }

        [TearDown]
        public void TearDown()
        {
            _actions.Disable();
            Object.DestroyImmediate(_root);
            Object.DestroyImmediate(_actions);
            InputSystem.RemoveDevice(_keyboard);
            InputSystem.RemoveDevice(_mouse);
            StageRecoveryGate.Restoring = _previousRecovery;
            InputSystem.settings.editorInputBehaviorInPlayMode = _previousEditorBehavior;
            InputSystem.settings.backgroundBehavior = _previousBackground;
        }

        [UnityTest]
        public IEnumerator 메뉴_잠금이_굴착_이동과_시점을_막고_해제하면_복원한다()
        {
            _input.SetGameplayInputLocked(true);
            yield return CheckLockAndRestore(recovering: false);
        }

        [UnityTest]
        public IEnumerator 복구_잠금이_굴착_이동과_시점을_막고_해제하면_복원한다()
        {
            StageRecoveryGate.Restoring = true;
            yield return CheckLockAndRestore(recovering: true);
        }

        [UnityTest]
        public IEnumerator 기본_조작은_탭과_홀드를_읽고_점프는_한번_소비할_때까지_유지한다()
        {
            _input.SetSkillInputMode(SkillInputMode.None);
            PressAll();
            yield return null;

            Assert.AreEqual(Vector2.up, _input.Move);
            Assert.AreNotEqual(Vector2.zero, _input.Look);
            Assert.IsTrue(_input.AttackPressedThisFrame);
            Assert.IsTrue(_input.InteractPressedThisFrame, "Hold 액션도 누른 프레임에 탭을 읽어야 한다.");
            Assert.IsTrue(_input.InteractHeld);
            Assert.IsTrue(_input.UseDriverHeld);
            Assert.IsTrue(_input.UseDriverPressedThisFrame);
            Assert.IsTrue(_input.PronePressedThisFrame);
            Assert.IsTrue(_input.DetectPressedThisFrame);
            Assert.IsTrue(_input.FurnitureRotateModePressedThisFrame);
            Assert.AreEqual(120f, _input.FurnitureRotateScroll);
            Assert.IsTrue(_input.QuickSlotPressedThisFrame);
            Assert.IsTrue(_input.CrouchHeld);
            Assert.IsTrue(_input.SprintHeld);
            AssertVoiceAndRawLook();

            yield return null;
            Assert.IsFalse(_input.AttackPressedThisFrame);
            Assert.IsTrue(_input.ConsumeJump(), "점프는 다음 프레임까지 대기해야 한다.");
            Assert.IsFalse(_input.ConsumeJump());
            ReleaseAll();
            yield return null;
            Assert.IsTrue(_input.AttackReleasedThisFrame);
            Assert.IsTrue(_input.QuickSlotReleasedThisFrame);
            Assert.IsFalse(_input.InteractHeld);
        }

        [UnityTest]
        public IEnumerator 굴착_시전은_시점이동을_막고_매몰은_열되_일반_행동은_막는다()
        {
            _input.SetSkillInputMode(SkillInputMode.None);
            PressAll();
            yield return null;
            _input.SetSkillInputMode(SkillInputMode.Frozen);
            Assert.AreEqual(Vector2.zero, _input.Look, "시전 시작은 Update 전에 시점도 비워야 한다.");
            Assert.IsFalse(_input.ConsumeJump());
            ReleaseAll();
            yield return null;

            PressAll();
            yield return null;
            Assert.AreEqual(Vector2.zero, _input.Move);
            Assert.AreEqual(Vector2.zero, _input.Look);
            Assert.IsTrue(_input.CrouchHeld, "굴착 중에는 마지막 웅크린 자세를 유지한다.");
            Assert.IsFalse(_input.SprintHeld);
            AssertSkillInputs();

            ReleaseAll();
            yield return null;
            _input.SetSkillInputMode(SkillInputMode.UndergroundMove);
            _input.SetWheelInputLocked(true);
            PressAll();
            yield return null;
            Assert.AreEqual(Vector2.up, _input.Move);
            Assert.AreNotEqual(Vector2.zero, _input.Look, "매몰은 휠 잠금보다 우선한다.");
            AssertSkillInputs();
        }

        [UnityTest]
        public IEnumerator 휠은_이동과_자세와_휠해제를_허용하고_일반_행동은_막는다()
        {
            _input.SetSkillInputMode(SkillInputMode.None);
            _input.SetWheelInputLocked(true);
            PressAll();
            yield return null;

            Assert.AreEqual(Vector2.up, _input.Move);
            Assert.AreEqual(Vector2.zero, _input.Look);
            Assert.IsTrue(_input.CrouchHeld);
            Assert.IsTrue(_input.SprintHeld);
            Assert.IsTrue(_input.QuickSlotHeld);
            Assert.IsTrue(_input.QuickSlotPressedThisFrame);
            Assert.IsTrue(_input.HeadlampPressedThisFrame);
            Assert.IsFalse(_input.BurrowPressedThisFrame);
            AssertGameplayBlocked();
            AssertVoiceAndRawLook();

            ReleaseAll();
            yield return null;
            Assert.IsTrue(_input.QuickSlotReleasedThisFrame, "휠이 스스로 닫힐 수 있어야 한다.");
            Assert.IsFalse(_input.AttackReleasedThisFrame);
        }

        [UnityTest]
        public IEnumerator 사망은_굴착과_휠보다_우선하고_메뉴와_복구는_관전도_막는다()
        {
            _input.SetWheelInputLocked(true);
            _input.SetDeathInputLocked(true);
            PressAll();
            yield return null;

            Assert.AreEqual(Vector2.zero, _input.Move);
            Assert.AreEqual(Vector2.zero, _input.Look);
            Assert.AreEqual(Vector2.up, _input.SpectatorMove);
            Assert.IsTrue(_input.SpectatorAscendHeld);
            Assert.IsTrue(_input.SpectatorDescendHeld);
            Assert.IsTrue(_input.SpectatorToggleModePressedThisFrame);
            Assert.IsTrue(_input.SpectatorPreviousPressedThisFrame);
            Assert.IsTrue(_input.SpectatorNextPressedThisFrame);
            Assert.IsFalse(_input.BurrowPressedThisFrame);
            Assert.IsFalse(_input.HeadlampPressedThisFrame);
            AssertGameplayBlocked();
            AssertVoiceAndRawLook();

            _input.SetSkillInputMode(SkillInputMode.None);
            yield return null;
            Assert.AreEqual(Vector2.zero, _input.Move, "굴착 해제가 사망 잠금을 풀면 안 된다.");
            Assert.AreEqual(Vector2.up, _input.SpectatorMove);
            _input.SetGameplayInputLocked(true);
            yield return null;
            AssertSpectatorBlocked();
            _input.SetGameplayInputLocked(false);
            StageRecoveryGate.Restoring = true;
            yield return null;
            AssertSpectatorBlocked();
            StageRecoveryGate.Restoring = false;
            yield return null;
            Assert.AreEqual(Vector2.up, _input.SpectatorMove);
            _input.SetDeathInputLocked(false);
            AssertSpectatorBlocked();
        }

        [UnityTest]
        public IEnumerator 잠금은_대기_점프를_즉시_버리되_웅크린_자세를_바꾸지_않는다()
        {
            _input.SetSkillInputMode(SkillInputMode.None);
            PressAll();
            yield return null;
            _input.SetWheelInputLocked(true);
            Assert.IsFalse(_input.AttackPressedThisFrame);
            Assert.IsFalse(_input.ConsumeJump());
            Assert.IsTrue(_input.CrouchHeld);
            Assert.IsTrue(_input.SprintHeld);
            Assert.IsTrue(_input.QuickSlotHeld);
            Assert.AreNotEqual(Vector2.zero, _input.Look, "휠 시점 잠금은 다음 Update에 적용된다.");
            _input.SetGameplayInputLocked(true);
            Assert.AreEqual(Vector2.zero, _input.Move);
            Assert.AreEqual(Vector2.zero, _input.Look);
            Assert.IsFalse(_input.SprintHeld);
            Assert.IsFalse(_input.QuickSlotHeld);
            ReleaseAll();
            yield return null;
            Assert.IsTrue(_input.CrouchHeld, "메뉴를 열고 Ctrl을 떼도 자세를 유지해야 한다.");
        }

        [UnityTest]
        public IEnumerator 이동_보정은_허용된_이동에만_적용하고_관전은_원래_입력을_읽는다()
        {
            _input.SetSkillInputMode(SkillInputMode.None);
            _input.SetMoveModifiers(true, null);
            PressAll();
            yield return null;
            Assert.AreEqual(Vector2.down, _input.Move);
            _input.SetMoveModifiers(true, Vector2.right);
            _input.SetWheelInputLocked(true);
            yield return null;
            Assert.AreEqual(Vector2.right, _input.Move);
            _input.SetSkillInputMode(SkillInputMode.UndergroundMove);
            yield return null;
            Assert.AreEqual(Vector2.right, _input.Move);
            _input.SetSkillInputMode(SkillInputMode.Frozen);
            yield return null;
            Assert.AreEqual(Vector2.zero, _input.Move);
            _input.SetDeathInputLocked(true);
            yield return null;
            Assert.AreEqual(Vector2.zero, _input.Move);
            Assert.AreEqual(Vector2.up, _input.SpectatorMove);
        }

        private void PressAll()
        {
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(
                Key.W, Key.Space, Key.E, Key.LeftCtrl, Key.LeftShift, Key.B, Key.R,
                Key.Z, Key.Q, Key.G, Key.C, Key.T, Key.N, Key.F, Key.V, Key.M));
            InputSystem.QueueStateEvent(_mouse, new MouseState
            {
                delta = new Vector2(12f, 4f), scroll = new Vector2(0f, 120f)
            }.WithButton(MouseButton.Left).WithButton(MouseButton.Middle));
        }

        private void ReleaseAll()
        {
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            InputSystem.QueueStateEvent(_mouse, new MouseState());
        }

        private void AssertSkillInputs()
        {
            Assert.IsTrue(_input.BurrowPressedThisFrame);
            Assert.IsTrue(_input.HeadlampPressedThisFrame);
            Assert.IsFalse(_input.QuickSlotHeld);
            Assert.IsFalse(_input.QuickSlotPressedThisFrame);
            AssertGameplayBlocked();
            AssertVoiceAndRawLook();
        }

        private void AssertGameplayBlocked()
        {
            Assert.IsFalse(_input.AttackPressedThisFrame);
            Assert.IsFalse(_input.AttackReleasedThisFrame);
            Assert.IsFalse(_input.InteractPressedThisFrame);
            Assert.IsFalse(_input.InteractHeld);
            Assert.IsFalse(_input.PronePressedThisFrame);
            Assert.IsFalse(_input.DetectPressedThisFrame);
            Assert.IsFalse(_input.UseDriverPressedThisFrame);
            Assert.IsFalse(_input.UseDriverHeld);
            Assert.IsFalse(_input.FurnitureRotateModePressedThisFrame);
            Assert.AreEqual(0f, _input.FurnitureRotateScroll);
            Assert.IsFalse(_input.ConsumeJump());
        }

        private void AssertSpectatorBlocked()
        {
            Assert.AreEqual(Vector2.zero, _input.SpectatorMove);
            Assert.IsFalse(_input.SpectatorAscendHeld);
            Assert.IsFalse(_input.SpectatorDescendHeld);
            Assert.IsFalse(_input.SpectatorToggleModePressedThisFrame);
            Assert.IsFalse(_input.SpectatorPreviousPressedThisFrame);
            Assert.IsFalse(_input.SpectatorNextPressedThisFrame);
        }

        private void AssertVoiceAndRawLook()
        {
            Assert.IsTrue(_input.VoiceHeld);
            Assert.IsTrue(_input.VoiceMutePressedThisFrame);
            Assert.AreNotEqual(Vector2.zero, _input.RawLookDelta);
        }

        private InputAction Bind(InputActionMap map, string field, string path)
        {
            InputAction action = map.AddAction(field, InputActionType.Button, path);
            Set(field, action);
            return action;
        }

        private IEnumerator CheckLockAndRestore(bool recovering)
        {
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.W, Key.B, Key.F, Key.V));
            InputSystem.QueueDeltaStateEvent(_mouse.delta, new Vector2(12f, 4f));
            yield return null;

            Assert.AreEqual(Vector2.zero, _input.Move);
            Assert.AreEqual(Vector2.zero, _input.Look);
            Assert.IsFalse(_input.BurrowPressedThisFrame);
            Assert.IsFalse(_input.HeadlampPressedThisFrame);
            Assert.IsTrue(_input.VoiceHeld, "음성은 게임플레이 잠금 중에도 살아 있어야 한다.");

            if (recovering)
                StageRecoveryGate.Restoring = false;
            else
                _input.SetGameplayInputLocked(false);
            InputSystem.QueueDeltaStateEvent(_mouse.delta, new Vector2(12f, 4f));
            yield return null;

            Assert.AreEqual(Vector2.up, _input.Move, "잠금 해제 뒤 매몰 중 이동 입력이 돌아와야 한다.");
            Assert.AreNotEqual(Vector2.zero, _input.Look, "잠금 해제 뒤 매몰 중 시점 입력이 돌아와야 한다.");
        }

        private void Set(string name, object value) => typeof(PlayerInputReader)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_input, value);
    }
}
