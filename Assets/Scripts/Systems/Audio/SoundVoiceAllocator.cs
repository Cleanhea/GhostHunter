namespace GhostHunter.Systems.Audio
{
    /// <summary>새 소리를 어느 칸에 재생할지 고른다.</summary>
    public static class SoundVoiceAllocator
    {
        /// <summary>
        /// 빈 칸이 있으면 첫 빈 칸. 없으면 가장 덜 중요한 칸(우선순위 값이 크고, 멀고, 오래된 순)이
        /// 새 소리보다 덜 중요할 때만 그 칸. 그 외에는 -1(새 소리를 버린다).
        /// </summary>
        public static int Choose(SoundVoiceSlot[] slots, int priority, float distance)
        {
            int victim = -1;
            for (int i = 0; i < slots.Length; i++)
            {
                if (!slots[i].Busy)
                    return i;
                if (victim < 0 || IsLessImportant(slots[i], slots[victim]))
                    victim = i;
            }

            if (victim < 0)
                return -1;

            SoundVoiceSlot weakest = slots[victim];
            bool newcomerWins = priority < weakest.Priority
                || (priority == weakest.Priority && distance < weakest.Distance);
            return newcomerWins ? victim : -1;
        }

        private static bool IsLessImportant(SoundVoiceSlot a, SoundVoiceSlot b)
        {
            if (a.Priority != b.Priority)
                return a.Priority > b.Priority;
            if (a.Distance != b.Distance)
                return a.Distance > b.Distance;
            return a.StartTime < b.StartTime;
        }
    }
}
