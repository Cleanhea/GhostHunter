using System;
using System.IO;
using System.Linq;
using GhostHunter.Gameplay.Player;
using NUnit.Framework;
using Unity.Collections;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>
    /// 머리 위 닉네임의 이름 정리 규칙과 권한을 본다.
    /// <c>PlayerNameTag</c> 는 NetworkBehaviour 라 EditMode 에서 스폰할 수 없어, 규칙은
    /// <see cref="PlayerNameRules"/> 로, 권한은 소스로 확인한다(<see cref="MoleSkillWiringTests"/> 와 같은 관례).
    /// </summary>
    public sealed class PlayerNameTagTests
    {
        private const string NameTagPath = "Assets/Scripts/Gameplay/Player/PlayerNameTag.cs";

        [Test]
        public void 제어_문자와_서식_문자를_지우고_앞뒤_공백을_걷는다()
        {
            Assert.AreEqual("홍길동", PlayerNameRules.Sanitize("  홍\n길\t동​  "));
            Assert.AreEqual("abc", PlayerNameRules.Sanitize("‮abc"), "방향 뒤집기 문자가 남으면 이름이 거꾸로 보인다");
        }

        [Test]
        public void 최대_길이로_자르되_서로게이트_쌍을_쪼개지_않는다()
        {
            Assert.AreEqual(PlayerNameRules.MaxLength, PlayerNameRules.Sanitize(new string('가', 40)).Length);

            // 31자 + 이모지(서로게이트 2칸) = 33칸 → 32번째 칸이 상위 서로게이트라 이모지째 뺀다.
            string name = new string('a', PlayerNameRules.MaxLength - 1) + "\U0001F47B";
            string sanitized = PlayerNameRules.Sanitize(name);

            Assert.AreEqual(PlayerNameRules.MaxLength - 1, sanitized.Length);
            Assert.IsFalse(char.IsHighSurrogate(sanitized[sanitized.Length - 1]));
        }

        [Test]
        public void 비거나_공백뿐인_이름은_대체_이름이_된다()
        {
            Assert.AreEqual("Player 2", PlayerNameRules.Resolve(null, 2));
            Assert.AreEqual("Player 0", PlayerNameRules.Resolve(" \n​ ", 0));
            Assert.AreEqual("Ghost", PlayerNameRules.Resolve(" Ghost ", 1));
        }

        [TestCase("가")]            // UTF-8 3바이트 × 32 = 96
        [TestCase("\U0001F47B")]    // UTF-8 4바이트 × 16 = 64
        public void 최대_길이_이름은_FixedString128Bytes_에_잘림_없이_들어간다(string unit)
        {
            string name = PlayerNameRules.Sanitize(string.Concat(Enumerable.Repeat(unit, 64)));
            Assert.AreEqual(name, new FixedString128Bytes(name).ToString());
        }

        [Test]
        public void 닉네임_요청은_소유자만_보낼_수_있다()
        {
            Assert.IsTrue(File.Exists(NameTagPath), $"{NameTagPath} 파일이 없습니다.");
            string source = File.ReadAllText(NameTagPath);

            // 통합 [Rpc] 의 기본 권한은 Everyone 이다. 빠지면 남의 이름을 바꿀 수 있다 → networking.md §3.2
            Assert.Greater(
                source.IndexOf("[Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]", StringComparison.Ordinal),
                -1,
                "RequestDisplayNameRpc 는 소유자만 호출할 수 있어야 합니다.");
            Assert.Greater(source.IndexOf("NetworkVariableWritePermission.Server", StringComparison.Ordinal), -1,
                "닉네임 NetworkVariable 은 서버만 쓴다.");
        }
    }
}
