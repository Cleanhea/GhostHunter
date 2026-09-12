using System.IO;
using GhostHunter.Gameplay.Player;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>퀵슬롯 휠 입력 배선과 각도→인덱스 순수 계산(QuickSlotSelection)을 검증한다.</summary>
    public sealed class QuickSlotTests
    {
        private const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";
        private const string InputReaderPath = "Assets/Scripts/Gameplay/Player/PlayerInputReader.cs";
        private const string WheelUiPath = "Assets/Scripts/UI/QuickSlotWheelUi.cs";

        [Test]
        public void Player_QuickSlot_액션은_Tab키에_바인딩되어_있다()
        {
            var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
            Assert.IsNotNull(actions, $"{InputActionsPath} 를 찾지 못했습니다.");

            InputAction quickSlot = actions.FindAction("Player/QuickSlot", false);
            Assert.IsNotNull(quickSlot, "Player/QuickSlot 액션이 없습니다.");

            bool hasTab = false;
            foreach (InputBinding binding in quickSlot.bindings)
                hasTab |= binding.path == "<Keyboard>/tab";

            Assert.IsTrue(hasTab, "Player/QuickSlot 액션에 Tab 바인딩이 없습니다.");
        }

        [Test]
        public void PlayerInputReader는_세_번째_잠금으로_휠_입력을_제공한다()
        {
            Assert.IsTrue(File.Exists(InputReaderPath), $"{InputReaderPath} 파일이 없습니다.");
            string source = File.ReadAllText(InputReaderPath);

            Assert.IsTrue(source.Contains("SetWheelInputLocked"), "SetWheelInputLocked 가 없습니다.");
            Assert.IsTrue(source.Contains("QuickSlotHeld"), "QuickSlotHeld 프로퍼티가 없습니다.");
            Assert.IsTrue(source.Contains("QuickSlotPressedThisFrame"), "QuickSlotPressedThisFrame 프로퍼티가 없습니다.");
            Assert.IsTrue(source.Contains("QuickSlotReleasedThisFrame"), "QuickSlotReleasedThisFrame 프로퍼티가 없습니다.");
        }

        [Test]
        public void 퀵슬롯_휠은_로컬_전용이고_아직_RPC를_쓰지_않는다()
        {
            // QS-1/QS-3(사용자 확정 2026-09-12) — 실제 인벤토리가 없어 확정은 로컬 상태 변경까지만 한다.
            Assert.IsTrue(File.Exists(WheelUiPath), $"{WheelUiPath} 파일이 없습니다.");
            string source = File.ReadAllText(WheelUiPath);

            Assert.IsFalse(source.Contains("[Rpc("), "퀵슬롯 휠은 아직 RPC를 쓰면 안 됩니다(더미 스캐폴드 범위).");
            Assert.IsFalse(source.Contains("NetworkVariable"), "퀵슬롯 휠은 아직 NetworkVariable을 쓰면 안 됩니다(QS-6, 로컬 전용).");
        }

        [Test]
        public void 데드존_안이면_선택없음을_반환한다()
        {
            Assert.AreEqual(-1, QuickSlotSelection.Resolve(Vector2.zero, 4, 24f, -1));
            Assert.AreEqual(-1, QuickSlotSelection.Resolve(new Vector2(5f, 5f), 4, 24f, 2));
        }

        [Test]
        public void 열두시_방향은_0번_슬롯이다()
        {
            int index = QuickSlotSelection.Resolve(new Vector2(0f, 100f), 4, 10f, -1);
            Assert.AreEqual(0, index);
        }

        [TestCase(90f, 1)]  // 3시
        [TestCase(180f, 2)] // 6시
        [TestCase(270f, 3)] // 9시
        public void 시계_방향으로_인덱스가_증가한다(float angleDegrees, int expectedIndex)
        {
            float radians = angleDegrees * Mathf.Deg2Rad;
            Vector2 pointer = new(Mathf.Sin(radians) * 100f, Mathf.Cos(radians) * 100f);

            int index = QuickSlotSelection.Resolve(pointer, 4, 10f, -1);
            Assert.AreEqual(expectedIndex, index);
        }

        [Test]
        public void 슬롯_경계에서_다음_슬롯으로_넘어간다()
        {
            // N=4 → 슬롯 폭 90도, 경계는 45도. 경계 미만은 이전 슬롯, 경계는 다음 슬롯(상한 포함).
            Vector2 justBelowBoundary = AtAngle(44f);
            Vector2 atBoundary = AtAngle(45f);

            Assert.AreEqual(0, QuickSlotSelection.Resolve(justBelowBoundary, 4, 10f, -1));
            Assert.AreEqual(1, QuickSlotSelection.Resolve(atBoundary, 4, 10f, -1));
        }

        [Test]
        public void 삼백육십도_부근에서_0번_슬롯으로_되돌아온다()
        {
            Vector2 pointer = AtAngle(350f);
            Assert.AreEqual(0, QuickSlotSelection.Resolve(pointer, 4, 10f, -1));
        }

        [Test]
        public void 새_로드아웃은_기본적으로_네_슬롯_전부_비어_있다()
        {
            var loadout = ScriptableObject.CreateInstance<QuickSlotLoadout>();
            try
            {
                Assert.AreEqual(4, loadout.SlotCount);
                for (int i = 0; i < loadout.SlotCount; i++)
                    Assert.IsNull(loadout.GetSlot(i), $"슬롯 {i} 는 비어 있어야 합니다.");
            }
            finally
            {
                Object.DestroyImmediate(loadout);
            }
        }

        [Test]
        public void 로드아웃_범위_밖_인덱스는_null을_반환한다()
        {
            var loadout = ScriptableObject.CreateInstance<QuickSlotLoadout>();
            try
            {
                Assert.IsNull(loadout.GetSlot(-1));
                Assert.IsNull(loadout.GetSlot(loadout.SlotCount));
            }
            finally
            {
                Object.DestroyImmediate(loadout);
            }
        }

        private static Vector2 AtAngle(float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(radians), Mathf.Cos(radians)) * 100f;
        }
    }
}
