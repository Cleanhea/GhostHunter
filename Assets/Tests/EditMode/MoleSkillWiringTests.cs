using System;
using System.IO;
using NUnit.Framework;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>
    /// 굴착 스킬의 확정 규칙 2건이 실제로 배선돼 있는지 본다
    /// (사용자 확정 2026-09-05 — mole-skill-system.md §5.2.1·§5.5.1).
    ///
    /// <para><c>MoleBurrowController</c>·<c>PlayerInputReader</c>·
    /// <c>GhostPrototypeController</c> 는 전부 <c>NetworkBehaviour</c> 라 EditMode 에서 스폰할 수 없다.
    /// 그래서 <see cref="PauseMenuTests"/> 와 같은 관례로 <b>소스 배선</b>을 확인한다 —
    /// 규칙 자체의 로직은 <see cref="GhostBurrowExposureTests"/> 가 순수 클래스로 검증한다.</para>
    /// </summary>
    public sealed class MoleSkillWiringTests
    {
        private const string BurrowControllerPath =
            "Assets/Scripts/Gameplay/Player/MoleBurrowController.cs";

        private const string InputReaderPath =
            "Assets/Scripts/Gameplay/Player/PlayerInputReader.cs";

        private const string GhostControllerPath =
            "Assets/Scripts/Gameplay/Ghost/GhostPrototypeController.cs";

        private static string ReadSource(string path)
        {
            Assert.IsTrue(File.Exists(path), $"{path} 파일이 없습니다.");
            return File.ReadAllText(path);
        }

        private static int IndexOf(string source, string needle)
        {
            return source.IndexOf(needle, StringComparison.Ordinal);
        }

        [Test]
        public void 굴착이_시전_시작에_조작을_잠그고_종료에_푼다()
        {
            string source = ReadSource(BurrowControllerPath);

            Assert.Greater(IndexOf(source, "SetSkillInputLocked(true)"), -1,
                "굴착 시전 시작에서 조작을 잠그지 않습니다 → mole-skill-system.md §5.5.1");

            // 종료 경로는 셋이다: 정상 종료(Pop) · 시전 취소(Cancel) · 디스폰.
            // 하나라도 빠지면 굴착이 끝난 뒤에도 조작이 잠긴 채로 남는다.
            int unlockCount = 0;
            int at = 0;
            while ((at = source.IndexOf("SetSkillInputLocked(false)", at, StringComparison.Ordinal)) >= 0)
            {
                unlockCount++;
                at += 1;
            }

            Assert.AreEqual(3, unlockCount,
                "잠금 해제는 Pop(정상 종료) · Cancel(시전 취소) · OnNetworkDespawn 세 곳에 있어야 합니다. " +
                "하나라도 빠지면 조작이 잠긴 채로 남습니다.");
        }

        [Test]
        public void 굴착_잠금은_시야와_굴착키만_살려_둔다()
        {
            string source = ReadSource(InputReaderPath);

            int skillBranch = IndexOf(source, "if (_skillLocked)");
            Assert.Greater(skillBranch, -1, "PlayerInputReader 에 굴착 잠금 분기가 없습니다.");

            string branch = source.Substring(skillBranch);
            int branchEnd = IndexOf(branch, "return;");
            Assert.Greater(branchEnd, -1, "굴착 잠금 분기가 닫히지 않았습니다.");
            branch = branch.Substring(0, branchEnd);

            Assert.Greater(IndexOf(branch, "_lookAction.ReadValue"), -1,
                "굴착 중에도 시야 회전은 살아 있어야 합니다 → mole-skill-system.md §5.5.1");
            Assert.Greater(IndexOf(branch, "_burrowAction.WasPressedThisFrame"), -1,
                "굴착 중에도 스킬 키는 살아 있어야 합니다 — 안 그러면 스스로 나올 수 없습니다.");
            Assert.AreEqual(-1, IndexOf(branch, "_moveAction"),
                "굴착 중 이동 입력을 읽으면 안 됩니다.");
            Assert.AreEqual(-1, IndexOf(branch, "_jumpAction"),
                "굴착 중 점프 입력을 읽으면 안 됩니다.");
        }

        [Test]
        public void 일시정지_잠금이_굴착_잠금보다_먼저_판정된다()
        {
            string source = ReadSource(InputReaderPath);

            int pause = IndexOf(source, "if (_inputLocked)");
            int skill = IndexOf(source, "if (_skillLocked)");

            Assert.Greater(pause, -1, "일시정지 잠금 분기가 없습니다.");
            Assert.Greater(skill, -1, "굴착 잠금 분기가 없습니다.");
            Assert.Less(pause, skill,
                "메뉴가 열리면 시점까지 잠겨야 하므로 일시정지 잠금이 먼저 걸러야 합니다 " +
                "→ pause-menu-system.md §3.4");
        }

        [Test]
        public void 귀신이_탐지와_포획_양쪽에서_굴착_노출을_본다()
        {
            string source = ReadSource(GhostControllerPath);

            Assert.Greater(IndexOf(source, "EvaluateBurrowExposure"), -1,
                "굴착 노출 판정을 갱신하지 않습니다 → mole-skill-system.md §5.2.1");

            Assert.Greater(IndexOf(source, "player.IsBurrowed && !IsBurrowExposed(player)"), -1,
                "탐지가 굴착 노출을 보지 않습니다 — 감지된 채로 숨은 플레이어도 놓치게 됩니다.");

            Assert.Greater(IndexOf(source, "_target.IsBurrowed && !IsBurrowExposed(_target)"), -1,
                "포획이 굴착을 보지 않습니다 — 탐지에서 빠져도 _target 은 수색 동안 남으므로, " +
                "안전하게 숨은 플레이어가 땅속에서 잡힙니다.");
        }

        [Test]
        public void 굴착_노출_판정이_탐지보다_먼저_돈다()
        {
            string source = ReadSource(GhostControllerPath);

            int evaluate = IndexOf(source, "EvaluateBurrowExposure(playerCount);");
            int detect = IndexOf(source, "TryDetectPlayer(playerCount, out");

            Assert.Greater(evaluate, -1, "어택 틱에서 굴착 노출을 갱신하지 않습니다.");
            Assert.Greater(detect, -1, "어택 틱에서 탐지를 부르지 않습니다.");
            Assert.Less(evaluate, detect,
                "탐지가 이번 틱의 노출 판정을 읽어야 하므로 갱신이 먼저여야 합니다 " +
                "(EvaluateBedHide 와 같은 이유).");
        }
    }
}
