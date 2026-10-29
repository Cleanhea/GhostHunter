using System;

namespace GhostHunter.Core.Audio
{
    /// <summary>
    /// 재생 중인 소리를 가리키는 표. 재생이 끝나 슬롯이 다른 소리에 재사용되면 같은 슬롯이어도
    /// <see cref="Version"/>이 달라져 이전 표로는 새 소리를 건드릴 수 없다.
    /// </summary>
    public readonly struct SoundHandle : IEquatable<SoundHandle>
    {
        public static readonly SoundHandle None = default;

        public SoundHandle(int slot, int version)
        {
            Slot = slot;
            Version = version;
        }

        public int Slot { get; }
        public int Version { get; }

        /// <summary>재생이 시작되지 못했으면(거리 밖·쿨다운·음원 없음) 유효하지 않다.</summary>
        public bool IsValid => Version != 0;

        public bool Equals(SoundHandle other) => Slot == other.Slot && Version == other.Version;
        public override bool Equals(object obj) => obj is SoundHandle other && Equals(other);
        public override int GetHashCode() => Slot * 397 ^ Version;
    }
}
