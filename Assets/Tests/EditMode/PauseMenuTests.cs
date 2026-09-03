using System;
using System.IO;
using GhostHunter.EditorTools;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.InputSystem;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>
    /// 일시정지 메뉴의 규약 검사. 여기서 잡는 것들은 전부 <b>컴파일은 통과하고 플레이해야만
    /// 드러나는</b> 종류다 — ESC 바인딩 누락, `PlayerLook` 의 옛 커서 토글 잔존,
    /// 실수로 들어간 `Time.timeScale` 조작, 메뉴 항목 순서 어긋남.
    ///
    /// 규칙은 docs/project/pause-menu-system.md, 배선은 docs/architecture/pause-menu.md.
    /// </summary>
    public sealed class PauseMenuTests
    {
        private const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";
        private const string PlayerLookPath = "Assets/Scripts/Gameplay/Player/PlayerLook.cs";
        private const string PauseMenuControllerPath = "Assets/Scripts/UI/PauseMenuController.cs";
        private const string PauseMenuSetupPath = "Assets/Scripts/Editor/PauseMenuSetup.cs";
        private const string RuntimeScriptRoot = "Assets/Scripts";

        /// <summary>메뉴 항목 순서는 확정 사항이다 → pause-menu-system.md §4.1</summary>
        private static readonly string[] ExpectedMenuOrder =
        {
            "ResumeButton", "SettingsButton", "TitleButton", "QuitButton",
        };

        [Test]
        public void 일시정지_액션이_ESC에_바인딩되어_있다()
        {
            var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
            Assert.IsNotNull(actions, $"{InputActionsPath} 를 찾지 못했습니다.");

            InputAction pause = actions.FindAction("Player/Pause", false);
            Assert.IsNotNull(pause, "Player/Pause 액션이 없습니다.");

            bool hasEscape = false;
            foreach (InputBinding binding in pause.bindings)
                hasEscape |= binding.path == "<Keyboard>/escape";

            Assert.IsTrue(hasEscape, "Player/Pause 액션에 ESC 가 바인딩되지 않았습니다.");
        }

        [Test]
        public void 메뉴를_닫는_UI_Cancel_액션이_존재한다()
        {
            var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
            Assert.IsNotNull(actions, $"{InputActionsPath} 를 찾지 못했습니다.");

            InputAction cancel = actions.FindAction("UI/Cancel", false);
            Assert.IsNotNull(cancel, "UI/Cancel 액션이 없습니다. 메뉴를 닫을 입력이 사라집니다.");
        }

        [Test]
        public void PlayerLook에_옛_ESC_커서_토글이_남아_있지_않다()
        {
            Assert.IsTrue(File.Exists(PlayerLookPath), $"{PlayerLookPath} 파일이 없습니다.");

            string source = File.ReadAllText(PlayerLookPath);
            Assert.IsFalse(
                source.Contains("escapeKey"),
                "PlayerLook 이 아직 ESC 를 직접 폴링합니다. 커서 잠금은 일시정지 메뉴가 관리합니다 " +
                "→ pause-menu-system.md §3.5 (PM-3 해소안 A).");
        }

        [Test]
        public void 런타임_코드가_timeScale을_건드리지_않는다()
        {
            // 멀티플레이라 한 명이 시간을 멈출 수 없다 → pause-menu-system.md §3.2
            foreach (string path in Directory.GetFiles(RuntimeScriptRoot, "*.cs", SearchOption.AllDirectories))
            {
                string normalized = path.Replace('\\', '/');

                // 에디터 전용 도구는 플레이 중 시간을 다룰 일이 없지만 검사 대상에서 제외한다.
                if (normalized.Contains("/Editor/"))
                    continue;

                // 값을 쓰는 것만 금지한다. 주석이나 문서 참조로 이름이 등장하는 것은 괜찮다.
                string source = File.ReadAllText(path).Replace(" ", string.Empty);
                Assert.IsFalse(
                    source.Contains("Time.timeScale="),
                    $"{normalized} 가 Time.timeScale 에 값을 씁니다. " +
                    "일시정지 메뉴는 시간을 멈추지 않습니다 → pause-menu-system.md §3.2");
            }
        }

        [Test]
        public void 메뉴_항목_순서가_확정_순서와_같다()
        {
            CollectionAssert.AreEqual(
                ExpectedMenuOrder,
                PauseMenuSetup.MenuButtonOrder,
                "메뉴 항목 순서는 계속하기 → 설정 → 타이틀로 → 종료 로 고정이다 " +
                "→ pause-menu-system.md §4.1");
        }

        [Test]
        public void 설치_도구가_Game_씬을_대상으로_한다()
        {
            // 씬을 재생성하지 않고 덧붙이는 도구여야 한다. 재생성 도구를 쓰면 배치된 가구가 사라진다.
            Assert.AreEqual("Assets/Scenes/Game.unity", PauseMenuSetup.ScenePath);
            Assert.IsTrue(File.Exists(PauseMenuSetupPath), $"{PauseMenuSetupPath} 파일이 없습니다.");
        }

        [Test]
        public void 메뉴가_잠그기_전에_가구를_먼저_놓는다()
        {
            Assert.IsTrue(File.Exists(PauseMenuControllerPath), $"{PauseMenuControllerPath} 파일이 없습니다.");

            string source = File.ReadAllText(PauseMenuControllerPath);

            int release = source.IndexOf("ForceRelease()", StringComparison.Ordinal);
            int lockInput = source.IndexOf("SetGameplayInputLocked(true)", StringComparison.Ordinal);

            Assert.Greater(release, -1, "메뉴가 GrabController.ForceRelease() 를 부르지 않습니다.");
            Assert.Greater(lockInput, -1, "메뉴가 입력을 잠그지 않습니다.");
            Assert.Less(release, lockInput,
                "입력을 잠근 뒤에 가구를 놓으면 해제 입력 프레임이 오지 않아 가구가 계속 떠 있습니다 " +
                "→ pause-menu.md §6.5 (PM-15).");
        }
    }
}
