using GhostHunter.Core.Audio;
using GhostHunter.Data.Audio;
using GhostHunter.Gameplay.Furniture;
using UnityEngine;

namespace GhostHunter.Gameplay.Audio
{
    /// <summary>
    /// 가구의 서버 충돌을 충돌음으로 바꿔 <see cref="NetworkSoundEmitter"/>로 알린다.
    /// 충돌 판정은 서버 물리가 하고, 소리는 각 접속자가 자기 쪽에서 낸다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(FurnitureNetworkPhysics))]
    [RequireComponent(typeof(NetworkSoundEmitter))]
    public sealed class FurnitureImpactSound : MonoBehaviour
    {
        [SerializeField] private ImpactSoundProfile _profile;

        private FurnitureNetworkPhysics _furniture;
        private NetworkSoundEmitter _emitter;
        private double _nextAllowed;

        private void Awake()
        {
            _furniture = GetComponent<FurnitureNetworkPhysics>();
            _emitter = GetComponent<NetworkSoundEmitter>();
            if (_profile == null)
            {
                Debug.LogError($"{nameof(FurnitureImpactSound)}: _profile 미할당", this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            if (_furniture != null)
                _furniture.ServerImpacted += HandleImpact;
        }

        private void OnDisable()
        {
            if (_furniture != null)
                _furniture.ServerImpacted -= HandleImpact;
        }

        private void HandleImpact(Vector3 point, float speed, FurnitureNetworkPhysics other)
        {
            if (!_emitter.IsServer || !_emitter.IsSpawned)
                return;

            // 가구끼리 부딪히면 양쪽이 같은 충돌을 보고한다. 둘 다 이 컴포넌트가 있으면 네트워크 ID 가 작은 쪽만 낸다.
            if (other != null && other.NetworkObjectId < _furniture.NetworkObjectId
                && other.TryGetComponent(out FurnitureImpactSound _))
                return;

            double now = Time.timeAsDouble;
            if (now < _nextAllowed || !_profile.TryEvaluate(speed, out SoundCue cue, out float volume))
                return;

            _nextAllowed = now + _profile.CooldownSeconds;
            _emitter.ServerPlay(cue, point, volume);
        }
    }
}
