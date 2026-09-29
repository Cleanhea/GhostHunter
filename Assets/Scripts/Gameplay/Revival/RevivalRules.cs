using GhostHunter.Gameplay.Player;

namespace GhostHunter.Gameplay.Revival
{
    /// <summary>
    /// 부활 의식의 순수 판정(revival-system.md §6·§8). 네트워크·씬과 무관해 EditMode 로 검사한다.
    /// </summary>
    public static class RevivalRules
    {
        public const int CandleCount = 5;

        /// <summary>폐급 중 '개좃같은 폐급 두더지' 확률(§8 — 2%).</summary>
        public const float CursedChance = 0.02f;

        /// <summary>98% 쪽 폐급 두더지 4종(§9). 순서는 <see cref="Judge"/> 의 변종 굴림이 쓴다.</summary>
        private static readonly DefectKind[] CommonDefects =
        {
            DefectKind.Follower,
            DefectKind.Screamer,
            DefectKind.InvertedKeys,
            DefectKind.VoiceModulated,
        };

        /// <summary>
        /// 커서가 초록 구간 안에서 눌렸으면 성공. 구간을 지나서 눌렀거나(늦음) 끝까지 안 눌렀으면(<paramref name="press"/> &lt; 0)
        /// 실패다. 구간 전에 누른 입력은 클라이언트가 무시하므로 여기로 오지 않는다(2026-09-30 사용자 확정).
        /// </summary>
        public static bool IsTimingSuccess(float press, float zoneStart, float zoneWidth)
        {
            return press >= 0f && press >= zoneStart && press <= zoneStart + zoneWidth;
        }

        /// <summary>
        /// 클라이언트가 보고한 누른 위치(0~1)가 서버 시간으로 가능했는가 — 점화를 시작한 지 그만큼 시간이 지났어야 한다.
        /// 지연을 봐서 <paramref name="toleranceSeconds"/> 만큼 봐준다. 끝까지 안 누름(음수)은 언제나 가능하다.
        /// </summary>
        public static bool IsPlausiblePress(float press, float serverElapsed, float barSeconds, float toleranceSeconds)
        {
            if (press < 0f)
                return true;
            return press <= 1f && serverElapsed + toleranceSeconds >= press * barSeconds;
        }

        /// <summary>
        /// 촛불 5개가 다 켜진 뒤 결과(§8). 실패 0개면 정상. 1개 이상이면 <paramref name="cursedRoll"/>(0~1)가
        /// <see cref="CursedChance"/> 미만일 때 개좃같은 폐급, 아니면 <paramref name="variantRoll"/> 로 4종 중 하나.
        /// </summary>
        public static DefectKind Judge(int failedCandles, float cursedRoll, int variantRoll)
        {
            if (failedCandles <= 0)
                return DefectKind.None;
            if (cursedRoll < CursedChance)
                return DefectKind.CursedRandomMove;

            int index = variantRoll % CommonDefects.Length;
            if (index < 0)
                index += CommonDefects.Length;
            return CommonDefects[index];
        }

        public static string DisplayName(DefectKind kind) => kind switch
        {
            DefectKind.None => "정상",
            DefectKind.Follower => "폐급 두더지 — 한 명을 졸졸 따라다님",
            DefectKind.Screamer => "폐급 두더지 — 갑자기 소리지름",
            DefectKind.InvertedKeys => "폐급 두더지 — 키가 반대로 눌림",
            DefectKind.VoiceModulated => "폐급 두더지 — 목소리 변조",
            DefectKind.CursedRandomMove => "개좃같은 폐급 두더지 — 20초마다 3초간 제멋대로 움직임",
            _ => "-",
        };

        /// <summary>효과가 들어가 있는 종류. 나머지는 표시만 한다(2026-09-30 사용자 확정 — 효과는 다음 작업).</summary>
        public static bool HasEffect(DefectKind kind) =>
            kind is DefectKind.InvertedKeys or DefectKind.CursedRandomMove;
    }
}
