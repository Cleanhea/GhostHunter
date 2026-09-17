using UnityEngine;

namespace GhostHunter.Core.Voice
{
    /// <summary>음성 화자의 공개 상태. UI와 중계가 같은 참가자 목록을 읽는다.</summary>
    public interface IVoiceParticipant
    {
        ulong ClientId { get; }
        bool IsAlive { get; }
        bool IsSpeaking { get; }
        Vector3 MouthPosition { get; }
        Transform Ear { get; }
        string DisplayName { get; }
        float Volume { get; set; }
    }
}
