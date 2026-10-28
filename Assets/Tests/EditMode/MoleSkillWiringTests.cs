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

        private static int CountOf(string source, string needle)
        {
            int count = 0;
            int at = 0;
            while ((at = source.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
            {
                count++;
                at += 1;
            }

            return count;
        }

        [Test]
        public void 굴착이_상태_전환마다_조작_제한을_한곳에서_건다()
        {
            string source = ReadSource(BurrowControllerPath);

            // 시전(Frozen) · 매몰(UndergroundMove) · 해제(None) 세 매핑이 모두 있어야 한다.
            Assert.Greater(IndexOf(source, "SkillInputMode.Frozen"), -1,
                "시전 중 시야·이동 잠금(Frozen)이 없습니다 → mole-skill-system.md §5.5.1");
            Assert.Greater(IndexOf(source, "SkillInputMode.UndergroundMove"), -1,
                "매몰 중 시야·이동 허용(UndergroundMove)이 없습니다 → mole-skill-system.md §5.5.1");
            Assert.Greater(IndexOf(source, "SkillInputMode.None"), -1,
                "굴착 해제(None)가 없습니다 — 조작이 잠긴 채로 남습니다.");

            // 컨트롤러가 입력 모드·이동 잠금을 직접 만지면 경로마다 따로 놀게 된다.
            Assert.AreEqual(1, CountOf(source, "SetSkillInputMode("),
                "SetSkillInputMode 는 ApplyControl 안에서만 불러야 합니다.");
            Assert.AreEqual(1, CountOf(source, "MovementLocked ="),
                "MovementLocked 는 ApplyControl 안에서만 바꿔야 합니다.");

            // 호출 지점은 여섯이다: 시전 시작 · 매몰 완료 · 정상 종료(Pop) · 시전 취소(Cancel) ·
            // 디스폰(State 를 직접 넘김) + 스테이지 복구(복원한 _state 를 넘김).
            // 하나라도 빠지면 굴착이 끝난 뒤에도 조작이 잠긴 채로 남는다.
            Assert.AreEqual(5, CountOf(source, "ApplyControl(State."),
                "ApplyControl 은 StartEntering · EnterBuried · Pop · Cancel · OnNetworkDespawn 다섯 곳에서 " +
                "State 를 직접 넘겨 불러야 합니다. 하나라도 빠지면 조작이 잠긴 채로 남습니다.");
            Assert.AreEqual(1, CountOf(source, "ApplyControl(_state)"),
                "스테이지 복구(RestoreStageStateRpc)가 복원한 상태로 조작 제한을 맞추지 않습니다.");
        }

        [Test]
        public void 굴착_가속은_매몰_중에만_걸리고_그_밖에는_ApplyControl이_걷어_낸다()
        {
            string source = ReadSource(BurrowControllerPath);

            // 거는 곳과 걷는 곳이 ApplyControl 하나라서, 종료·취소·디스폰·복구 어느 경로로 나가도
            // 가속이 남지 않는다.
            Assert.AreEqual(1, CountOf(source, "SetSpeedModifier(SpeedModifierSource.Burrow"),
                "굴착 가속은 ApplyControl 안에서 한 번만 걸어야 합니다.");
            Assert.AreEqual(1, CountOf(source, "ClearSpeedModifier(SpeedModifierSource.Burrow"),
                "굴착 가속을 걷는 곳은 ApplyControl 하나여야 합니다.");
            Assert.Greater(IndexOf(source, "state == State.Buried"), -1,
                "가속은 매몰(Buried) 중에만 걸려야 합니다.");
            Assert.Greater(IndexOf(source, "BurrowedMoveSpeedMultiplier"), -1,
                "배율은 MoleBurrowSettings 에서 읽어야 합니다(튜닝 수치는 설정 에셋에).");
        }

        [Test]
        public void 굴착_잠금은_시전_중_시야이동을_막고_매몰_중에만_연다()
        {
            string source = ReadSource(InputReaderPath);

            int skillBranch = IndexOf(source, "if (_skillMode != SkillInputMode.None)");
            Assert.Greater(skillBranch, -1, "PlayerInputReader 에 굴착 잠금 분기가 없습니다.");

            string branch = source.Substring(skillBranch);
            int branchEnd = IndexOf(branch, "return;");
            Assert.Greater(branchEnd, -1, "굴착 잠금 분기가 닫히지 않았습니다.");
            branch = branch.Substring(0, branchEnd);

            Assert.Greater(IndexOf(branch, "_burrowAction.WasPressedThisFrame"), -1,
                "굴착 중에도 스킬 키는 살아 있어야 합니다 — 안 그러면 스스로 나올 수 없습니다.");

            // 시야·이동은 매몰(UndergroundMove)일 때만 읽는다 — 시전(Frozen) 중에는 0이다.
            Assert.Greater(IndexOf(branch, "SkillInputMode.UndergroundMove"), -1,
                "시야·이동 허용은 UndergroundMove 에서만 열려야 합니다.");
            Assert.Greater(IndexOf(branch, "_lookAction.ReadValue"), -1,
                "매몰 중에는 시야 회전이 살아 있어야 합니다 → mole-skill-system.md §5.5.1");
            Assert.Greater(IndexOf(branch, "_moveAction.ReadValue"), -1,
                "매몰 중에는 이동 입력이 살아 있어야 합니다 → mole-skill-system.md §5.5.1");
            Assert.Greater(IndexOf(branch, "Vector2.zero"), -1,
                "시전 중에는 시야 입력을 0으로 만들어야 합니다.");

            // 매몰 중에도 막혀야 하는 입력은 읽지 않는다.
            Assert.AreEqual(-1, IndexOf(branch, "_jumpAction"), "굴착 중 점프 입력을 읽으면 안 됩니다.");
            Assert.AreEqual(-1, IndexOf(branch, "_sprintAction"), "굴착 중 달리기 입력을 읽으면 안 됩니다.");
            Assert.AreEqual(-1, IndexOf(branch, "_proneAction"), "굴착 중 엎드리기 입력을 읽으면 안 됩니다.");
            Assert.AreEqual(-1, IndexOf(branch, "_attackAction"), "굴착 중 던지기 입력을 읽으면 안 됩니다.");
            Assert.AreEqual(-1, IndexOf(branch, "_interactAction"), "굴착 중 상호작용 입력을 읽으면 안 됩니다.");
        }

        [Test]
        public void 시전_모드로_들어갈_때_시야_입력을_즉시_비운다()
        {
            // PlayerLook 이 PlayerInputReader 보다 먼저 돌아서, 비우지 않으면 시전 직후 한 프레임
            // 직전 마우스 입력이 적용된다(SetDeathInputLocked 가 같은 이유로 Look 을 비운다).
            string source = ReadSource(InputReaderPath);

            int setter = IndexOf(source, "public void SetSkillInputMode(");
            Assert.Greater(setter, -1, "SetSkillInputMode 가 없습니다.");

            string body = source.Substring(setter);
            body = body.Substring(0, IndexOf(body, "\n        }"));

            Assert.Greater(IndexOf(body, "SkillInputMode.Frozen"), -1);
            Assert.Greater(IndexOf(body, "Look = Vector2.zero"), -1,
                "Frozen 진입 시 Look 을 즉시 비우지 않으면 시전 직후 한 프레임 시점이 샙니다.");
        }

        [Test]
        public void 일시정지_잠금이_굴착_잠금보다_먼저_판정된다()
        {
            string source = ReadSource(InputReaderPath);

            int pause = IndexOf(source, "if (_inputLocked)");
            int skill = IndexOf(source, "if (_skillMode != SkillInputMode.None)");

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
