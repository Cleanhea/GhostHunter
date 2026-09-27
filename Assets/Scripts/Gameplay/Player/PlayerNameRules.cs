using System.Globalization;
using System.Text;

namespace GhostHunter.Gameplay.Player
{
    /// <summary>
    /// 머리 위 닉네임의 정리 규칙. 클라이언트가 보낸 이름을 서버가 그대로 쓰지 않도록
    /// 보이지 않는 문자를 지우고 길이를 자른다. <see cref="PlayerNameTag"/> 가 소유자 송신 전과
    /// 서버 확정 시 두 번 쓴다 — NetworkBehaviour 밖에 두어 EditMode 에서 검증한다.
    /// </summary>
    public static class PlayerNameRules
    {
        /// <summary>
        /// Steam 표시 이름 최대 길이(32자). UTF-16 32자는 UTF-8 로 최대 96바이트라
        /// <c>FixedString128Bytes</c>(125바이트)에 잘림 없이 들어간다.
        /// </summary>
        public const int MaxLength = 32;

        /// <summary>
        /// 제어 문자(줄바꿈·탭)와 서식 문자(방향 뒤집기·폭 없는 공백)를 지우고, 앞뒤 공백을 걷고,
        /// <see cref="MaxLength"/> 로 자른다. 서로게이트 쌍(이모지) 중간에서는 자르지 않는다.
        /// </summary>
        public static string Sanitize(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return string.Empty;

            var builder = new StringBuilder(raw.Length);
            foreach (char character in raw)
            {
                UnicodeCategory category = char.GetUnicodeCategory(character);
                if (category == UnicodeCategory.Control || category == UnicodeCategory.Format)
                    continue;

                builder.Append(character);
            }

            string cleaned = builder.ToString().Trim();
            if (cleaned.Length <= MaxLength)
                return cleaned;

            int length = MaxLength;
            if (char.IsHighSurrogate(cleaned[length - 1]))
                length--;

            return cleaned.Substring(0, length).TrimEnd();
        }

        /// <summary>이름이 없을 때 붙이는 대체 이름. 음성 HUD 의 원격 화자 표기와 같다.</summary>
        public static string Fallback(ulong clientId) => $"Player {clientId}";

        /// <summary>서버가 확정할 이름. 정리 결과가 비면 대체 이름을 쓴다.</summary>
        public static string Resolve(string raw, ulong clientId)
        {
            string cleaned = Sanitize(raw);
            return cleaned.Length > 0 ? cleaned : Fallback(clientId);
        }
    }
}
