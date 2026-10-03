namespace GhostHunter.Core.Voice
{
    /// <summary>패킷 헤더의 코덱 번호. 받는 쪽은 자기가 풀 수 있는 번호만 받는다.</summary>
    public static class VoiceCodecs
    {
        /// <summary>2026-10-03 이전 Steam Voice. 지금은 받지 않는다 — 구버전과 섞이면 버린다.</summary>
        public const byte SteamLegacy = 0;
        /// <summary>개발용 사인파(DebugTools). 개발 빌드에서만 받는다.</summary>
        public const byte TestTone = 1;
        /// <summary>Unity Microphone + Opus(Concentus).</summary>
        public const byte Opus = 2;
    }
}
