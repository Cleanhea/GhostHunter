using UnityEngine;

namespace GhostHunter.Core.Audio
{
    /// <summary>
    /// 이 클라이언트에서 소리를 내는 서비스. 모든 호출은 로컬 재생이다 — 다른 접속자에게도 들려야 하는 소리는
    /// 서버가 <c>NetworkSoundEmitter</c> 로 알리고, 각 접속자가 받은 쪽에서 이 서비스로 재생한다.
    /// </summary>
    public interface IAudioService
    {
        /// <summary>활성 AudioListener 의 위치. 리스너가 없으면 false.</summary>
        bool TryGetListenerPosition(out Vector3 position);

        /// <summary>월드 좌표에서 3D 로 재생한다. 들릴 수 없는 거리·쿨다운·한도 초과면 <see cref="SoundHandle.None"/>.</summary>
        SoundHandle Play(SoundCue cue, Vector3 position, float volumeScale = 1f);

        /// <summary>대상을 따라다니며 재생한다. 대상이 파괴되면 소리도 멈춘다. 반복 큐는 이 방식으로 건다.</summary>
        SoundHandle PlayAttached(SoundCue cue, Transform target, float volumeScale = 1f);

        /// <summary>위치 없이 재생한다(UI·내 머릿속 소리). 거리 컬링과 가림은 적용되지 않는다.</summary>
        SoundHandle Play2D(SoundCue cue, float volumeScale = 1f);

        bool IsPlaying(SoundHandle handle);

        /// <summary>소리를 멈춘다. <paramref name="fadeSeconds"/>가 0 보다 크면 그 시간 동안 줄이며 멈춘다.</summary>
        void Stop(SoundHandle handle, float fadeSeconds = 0f);

        /// <summary>재생 중인 모든 소리를 멈춘다(반복 포함).</summary>
        void StopAll();

        float GetBusVolume(SoundBus bus);

        /// <summary>0~1. 재생 중인 소리에도 바로 적용된다. 저장은 하지 않는다.</summary>
        void SetBusVolume(SoundBus bus, float volume);

        /// <summary>네트워크로 받은 식별자에서 큐를 찾는다. 라이브러리에 없으면 false.</summary>
        bool TryResolve(uint cueId, out SoundCue cue);
    }
}
