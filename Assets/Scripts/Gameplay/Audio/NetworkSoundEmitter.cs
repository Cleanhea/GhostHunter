using System.Collections.Generic;
using GhostHunter.Core;
using GhostHunter.Core.Audio;
using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Gameplay.Audio
{
    /// <summary>
    /// 서버가 정한 소리를 모든 접속자에게 알린다. 서버는 어떤 큐를 어디서 얼마나 크게 낼지만 보내고,
    /// 받은 접속자가 자기 <see cref="IAudioService"/>로 재생한다. 지연이 소리 위치에 영향을 주지 않도록
    /// 신뢰성 없는 전달을 쓴다 — 유실된 소리는 다시 보내지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetworkSoundEmitter : NetworkBehaviour
    {
        // 같은 큐를 연달아 보낼 때 서버가 막는 기본 최소 간격. 큐의 쿨다운이 더 길면 그쪽을 따른다.
        private const double MinimumResendSeconds = 0.03;

        private readonly Dictionary<uint, double> _lastSent = new();
        private IAudioService _audio;
        private bool _warnedMissingService;
        private uint _warnedUnknownCue;

        public override void OnNetworkSpawn()
        {
            Services.TryGet(out _audio);
        }

        public override void OnNetworkDespawn()
        {
            _audio = null;
            _lastSent.Clear();
        }

        /// <summary>서버 전용 — 큐를 월드 좌표에서 모든 접속자에게 재생하게 한다. 서버 자신(호스트)도 듣는다.</summary>
        public void ServerPlay(SoundCue cue, Vector3 position, float volumeScale = 1f)
        {
            if (!IsServer || !IsSpawned || cue == null || !(volumeScale > 0f) || !IsFinite(position))
                return;

            uint id = cue.Id;
            double now = Time.timeAsDouble;
            double interval = System.Math.Max(MinimumResendSeconds, cue.Cooldown);
            if (_lastSent.TryGetValue(id, out double last) && now - last < interval)
                return;

            _lastSent[id] = now;
            PlaySoundRpc(id, position, NetworkSoundCodec.QuantizeVolume(volumeScale));
        }

        [Rpc(SendTo.ClientsAndHost, Delivery = RpcDelivery.Unreliable, InvokePermission = RpcInvokePermission.Server)]
        private void PlaySoundRpc(uint cueId, Vector3 position, byte volume)
        {
            if (_audio == null)
            {
                if (!_warnedMissingService)
                {
                    _warnedMissingService = true;
                    Debug.LogWarning($"{nameof(NetworkSoundEmitter)}: IAudioService 가 없어 소리를 재생하지 못한다.", this);
                }

                return;
            }

            if (!_audio.TryResolve(cueId, out SoundCue cue))
            {
                // 라이브러리에 큐를 빠뜨렸을 때 같은 경고가 소리마다 쏟아지지 않게 마지막 한 건만 막는다.
                if (_warnedUnknownCue != cueId)
                {
                    _warnedUnknownCue = cueId;
                    Debug.LogWarning($"{nameof(NetworkSoundEmitter)}: SoundLibrary 에 없는 큐 식별자 {cueId}.", this);
                }

                return;
            }

            _audio.Play(cue, position, NetworkSoundCodec.DequantizeVolume(volume));
        }

        private static bool IsFinite(Vector3 value) =>
            !(float.IsNaN(value.x) || float.IsNaN(value.y) || float.IsNaN(value.z)
              || float.IsInfinity(value.x) || float.IsInfinity(value.y) || float.IsInfinity(value.z));
    }
}
