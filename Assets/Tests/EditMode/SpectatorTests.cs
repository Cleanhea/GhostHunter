using System.Collections.Generic;
using System.IO;
using GhostHunter.Gameplay.Player;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.InputSystem;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>
    /// 관전 대상 순회의 순수 로직(SpectatorTargetSelector)과 사망 입력 잠금·관전 입력 배선을
    /// 검증한다 → docs/project/spectator-system.md.
    /// </summary>
    public sealed class SpectatorTests
    {
        private const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";
        private const string InputReaderPath = "Assets/Scripts/Gameplay/Player/PlayerInputReader.cs";
        private const string SanityStatePath = "Assets/Scripts/Gameplay/Sanity/SanityNetworkState.cs";
        private const string GrabControllerPath = "Assets/Scripts/Gameplay/Interaction/GrabController.cs";
        private const string DoorInteractablePath = "Assets/Scripts/Gameplay/Interaction/DoorInteractable.cs";
        private const string QuickSlotWheelUiPath = "Assets/Scripts/UI/QuickSlotWheelUi.cs";

        #region SpectatorTargetSelector — 순수 로직

        [Test]
        public void 후보가_없으면_다음_대상을_찾지_못한다()
        {
            bool found = SpectatorTargetSelector.TryGetNext(new List<ulong>(), null, out _);
            Assert.IsFalse(found);
        }

        [Test]
        public void 현재_대상이_없으면_목록의_첫_후보로_간다()
        {
            var candidates = new List<ulong> { 5, 7, 9 };
            bool found = SpectatorTargetSelector.TryGetNext(candidates, null, out ulong next);

            Assert.IsTrue(found);
            Assert.AreEqual(5ul, next);
        }

        [Test]
        public void 현재_대상이_후보에_없으면_무효로_간주해_첫_후보로_간다()
        {
            var candidates = new List<ulong> { 5, 7, 9 };
            bool found = SpectatorTargetSelector.TryGetNext(candidates, 999ul, out ulong next);

            Assert.IsTrue(found);
            Assert.AreEqual(5ul, next);
        }

        [Test]
        public void 다음_대상은_목록_순서대로_진행하고_끝에서_처음으로_돌아온다()
        {
            var candidates = new List<ulong> { 5, 7, 9 };

            Assert.IsTrue(SpectatorTargetSelector.TryGetNext(candidates, 5ul, out ulong afterFive));
            Assert.AreEqual(7ul, afterFive);

            Assert.IsTrue(SpectatorTargetSelector.TryGetNext(candidates, 9ul, out ulong afterNine));
            Assert.AreEqual(5ul, afterNine, "마지막 다음은 첫 후보로 순환해야 합니다.");
        }

        [Test]
        public void 이전_대상은_목록_역순으로_진행하고_처음에서_끝으로_돌아온다()
        {
            var candidates = new List<ulong> { 5, 7, 9 };

            Assert.IsTrue(SpectatorTargetSelector.TryGetPrevious(candidates, 7ul, out ulong beforeSeven));
            Assert.AreEqual(5ul, beforeSeven);

            Assert.IsTrue(SpectatorTargetSelector.TryGetPrevious(candidates, 5ul, out ulong beforeFive));
            Assert.AreEqual(9ul, beforeFive, "첫 후보의 이전은 마지막 후보로 순환해야 합니다.");
        }

        [Test]
        public void 후보가_하나뿐이면_다음_이전_모두_자기_자신이다()
        {
            var candidates = new List<ulong> { 42 };

            Assert.IsTrue(SpectatorTargetSelector.TryGetNext(candidates, 42ul, out ulong next));
            Assert.AreEqual(42ul, next);

            Assert.IsTrue(SpectatorTargetSelector.TryGetPrevious(candidates, 42ul, out ulong previous));
            Assert.AreEqual(42ul, previous);
        }

        [Test]
        public void 대상_유효성_검사는_목록에_있을_때만_참이다()
        {
            var candidates = new List<ulong> { 5, 7, 9 };

            Assert.IsTrue(SpectatorTargetSelector.IsValid(candidates, 7ul));
            Assert.IsFalse(SpectatorTargetSelector.IsValid(candidates, 999ul));
            Assert.IsFalse(SpectatorTargetSelector.IsValid(candidates, null));
            Assert.IsFalse(SpectatorTargetSelector.IsValid(new List<ulong>(), 7ul));
        }

        #endregion

        #region 입력 배선 — 사망 잠금이 굴착·휠 잠금보다 우선한다(SP-2, 경합 방지)

        [Test]
        public void PlayerInputReader는_사망_입력_잠금과_관전_입력을_제공한다()
        {
            Assert.IsTrue(File.Exists(InputReaderPath), $"{InputReaderPath} 파일이 없습니다.");
            string source = File.ReadAllText(InputReaderPath);

            Assert.IsTrue(source.Contains("SetDeathInputLocked"), "SetDeathInputLocked 가 없습니다.");
            Assert.IsTrue(source.Contains("SpectatorMove"), "SpectatorMove 프로퍼티가 없습니다.");
            Assert.IsTrue(source.Contains("SpectatorAscendHeld"), "SpectatorAscendHeld 프로퍼티가 없습니다.");
            Assert.IsTrue(source.Contains("SpectatorDescendHeld"), "SpectatorDescendHeld 프로퍼티가 없습니다.");
            Assert.IsTrue(
                source.Contains("SpectatorToggleModePressedThisFrame"),
                "SpectatorToggleModePressedThisFrame 프로퍼티가 없습니다.");
        }

        [Test]
        public void 사망_잠금_분기는_굴착_잠금_분기보다_Update에서_먼저_나온다()
        {
            // 실행 순서가 아니라 소스상 등장 순서를 본다 — if(_deathLocked) 분기가 먼저 return 해야
            // 굴착 취소가 같은 프레임에 SetSkillInputLocked(false)를 불러도 생존 조작이
            // 되살아나지 않는다(관전 기획서 §3 항목 6의 잠금 해제 순서 경합 방지).
            string source = File.ReadAllText(InputReaderPath);

            int deathBranchIndex = source.IndexOf("if (_deathLocked)");
            int skillBranchIndex = source.IndexOf("if (_skillLocked)");

            Assert.Greater(deathBranchIndex, -1, "if (_deathLocked) 분기가 없습니다.");
            Assert.Greater(skillBranchIndex, -1, "if (_skillLocked) 분기가 없습니다.");
            Assert.Less(deathBranchIndex, skillBranchIndex, "사망 잠금 분기가 굴착 잠금 분기보다 먼저 검사돼야 합니다.");
        }

        [Test]
        public void SanityNetworkState는_생존_변화_이벤트를_공개한다()
        {
            Assert.IsTrue(File.Exists(SanityStatePath), $"{SanityStatePath} 파일이 없습니다.");
            string source = File.ReadAllText(SanityStatePath);

            Assert.IsTrue(
                source.Contains("public event Action<bool> AliveStateChanged"),
                "AliveStateChanged 이벤트가 없습니다.");
        }

        [Test]
        public void GrabController는_서버에서_사망한_요청자를_거절한다()
        {
            string source = File.ReadAllText(GrabControllerPath);

            Assert.IsTrue(
                source.Contains("_sanity != null && !_sanity.HasSanity"),
                "Grab/Aim/Release RPC에 생존 검사가 없습니다.");
            Assert.IsTrue(
                source.Contains("ServerForceRelease(OwnerClientId)"),
                "사망 시 홀드를 강제 해제하는 경로가 없습니다.");
        }

        [Test]
        public void DoorInteractable은_플레이어_토글_요청에서만_생존을_검사한다()
        {
            string source = File.ReadAllText(DoorInteractablePath);

            Assert.IsTrue(source.Contains("ServerIsSenderAlive"), "ServerIsSenderAlive 검사가 없습니다.");
            // 귀신 전용 서버 API는 플레이어 생존 검사를 거치면 안 된다.
            Assert.IsFalse(
                ExtractMethodBody(source, "ServerForceOpen").Contains("ServerIsSenderAlive"),
                "ServerForceOpen은 생존 검사를 거치면 안 됩니다(플레이어 요청이 아님).");
            Assert.IsFalse(
                ExtractMethodBody(source, "ServerForceClose").Contains("ServerIsSenderAlive"),
                "ServerForceClose는 생존 검사를 거치면 안 됩니다(플레이어 요청이 아님).");
        }

        [Test]
        public void QuickSlotWheelUi는_생존_검사를_CanOpen에_포함한다()
        {
            string source = File.ReadAllText(QuickSlotWheelUiPath);

            Assert.IsTrue(
                source.Contains("_localPlayer.Sanity == null || _localPlayer.Sanity.HasSanity"),
                "CanOpen에 생존 검사가 없습니다.");
        }

        [Test]
        public void 관전_전용_입력_액션_세_개가_Player_맵에_있다()
        {
            var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
            Assert.IsNotNull(actions, $"{InputActionsPath} 를 찾지 못했습니다.");

            AssertActionExists(actions, "Player/SpectateDescend");
            AssertActionExists(actions, "Player/SpectateToggleMode");
            AssertActionExists(actions, "Player/SpectateNext");
        }

        #endregion

        private static void AssertActionExists(InputActionAsset actions, string actionPath)
        {
            InputAction action = actions.FindAction(actionPath, false);
            Assert.IsNotNull(action, $"{actionPath} 액션이 없습니다 — SpectatorSetup 메뉴 도구를 실행했는지 확인하세요.");
        }

        /// <summary>테스트 전용 — 메서드 이름 다음 첫 중괄호 블록만 거칠게 잘라낸다.</summary>
        private static string ExtractMethodBody(string source, string methodName)
        {
            int nameIndex = source.IndexOf(methodName);
            if (nameIndex < 0)
                return string.Empty;

            int openBrace = source.IndexOf('{', nameIndex);
            if (openBrace < 0)
                return string.Empty;

            int depth = 0;
            for (int i = openBrace; i < source.Length; i++)
            {
                if (source[i] == '{')
                    depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(openBrace, i - openBrace + 1);
                }
            }

            return string.Empty;
        }
    }
}
