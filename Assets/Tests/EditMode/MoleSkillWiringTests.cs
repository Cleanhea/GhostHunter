using System;
using System.IO;
using NUnit.Framework;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>
    /// 굴착 스킬의 확정 규칙 2건이 실제로 배선돼 있는지 본다
    /// (사용자 확정 2026-09-05 — mole-skill-system.md §5.2.1·§5.5.1).
    ///
    /// <para><c>MoleBurrowController</c>·<c>PlayerInputReader</c>의 정적 배선을 확인한다. 규칙 자체는
    /// <see cref="GhostBurrowExposureTests"/> 가 순수 클래스로 검증하고,
    /// 입력 잠금 우선순위와 굴착의 탐지·포획 결과는 PlayMode 흐름 테스트로 검증한다.</para>
    /// </summary>
    public sealed class MoleSkillWiringTests
    {
        private const string BurrowControllerPath =
            "Assets/Scripts/Gameplay/Player/MoleBurrowController.cs";

        private const string InputReaderPath =
            "Assets/Scripts/Gameplay/Player/PlayerInputReader.cs";

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
    }
}
