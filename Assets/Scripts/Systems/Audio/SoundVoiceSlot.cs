namespace GhostHunter.Systems.Audio
{
    /// <summary>오디오 소스 한 칸의 재생 상태. 새 소리가 들어올 때 누구를 밀어낼지 정하는 근거다.</summary>
    public struct SoundVoiceSlot
    {
        public bool Busy;
        /// <summary>0 이 가장 중요하다(Unity AudioSource.priority 와 같은 방향).</summary>
        public int Priority;
        /// <summary>리스너까지 거리(m). 2D 소리는 0.</summary>
        public float Distance;
        public double StartTime;
    }
}
