namespace GhostHunter.Core.Audio
{
    /// <summary>큐 키 문자열에서 네트워크 식별자를 만든다. 빌드·플랫폼이 달라도 같은 값이 나오는 FNV-1a 다.</summary>
    public static class SoundCueId
    {
        /// <summary>유효한 식별자는 0 이 아니다.</summary>
        public const uint None = 0;

        private const uint OffsetBasis = 2166136261u;
        private const uint Prime = 16777619u;

        public static uint FromKey(string key)
        {
            if (string.IsNullOrEmpty(key))
                return None;

            uint hash = OffsetBasis;
            for (int i = 0; i < key.Length; i++)
            {
                hash ^= key[i];
                hash *= Prime;
            }

            return hash == None ? 1u : hash;
        }
    }
}
