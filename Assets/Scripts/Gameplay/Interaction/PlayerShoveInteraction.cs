using System;
using GhostHunter.Gameplay.Furniture;
using GhostHunter.Gameplay.Player;
using GhostHunter.Gameplay.Sanity;
using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Gameplay.Interaction
{
    /// <summary>가장 가까운 대상 판정과 로컬 밀치기 차징. 넉백·생존·거리 검증은 기존 서버 RPC가 맡는다.</summary>
    internal sealed class PlayerShoveInteraction
    {
        private readonly Transform _player;
        private readonly NetworkObject _identity;
        private readonly Camera _camera;
        private readonly FurnitureThrowSettings _settings;
        private readonly Action<ulong> _begin;
        private readonly Action<ulong, Vector3> _release;
        private readonly RaycastHit[] _hits = new RaycastHit[8];
        private ulong _targetId = GrabController.NoObjectId;
        private float _pressAt;

        public bool IsActive => _targetId != GrabController.NoObjectId;
        public float Power
        {
            get
            {
                if (!IsActive || _settings == null)
                    return 0f;
                float charge = Mathf.Clamp01((Time.unscaledTime - _pressAt) / _settings.ChargeTime);
                return _settings.ShoveForceRatio(charge);
            }
        }

        public PlayerShoveInteraction(
            Transform player, NetworkObject identity, Camera camera, FurnitureThrowSettings settings,
            Action<ulong> begin, Action<ulong, Vector3> release)
        {
            _player = player;
            _identity = identity;
            _camera = camera;
            _settings = settings;
            _begin = begin;
            _release = release;
        }

        public bool TryBegin(bool blocked, bool alive)
        {
            if (blocked || IsActive || !alive || _settings == null || !TryFindTarget(out ulong targetId))
                return false;
            _targetId = targetId;
            _pressAt = Time.unscaledTime;
            _begin(targetId);
            return true;
        }

        public void Release(bool isSpawned)
        {
            ulong targetId = _targetId;
            _targetId = GrabController.NoObjectId;
            if (targetId != GrabController.NoObjectId && isSpawned)
                _release(targetId, _camera.transform.forward);
        }

        /// <summary>차징만 접는다. 서버 시작 기록은 다음 시작 때 덮어쓰므로 취소 RPC는 필요 없다.</summary>
        public void Cancel() => _targetId = GrabController.NoObjectId;

        private bool TryFindTarget(out ulong targetId)
        {
            targetId = GrabController.NoObjectId;
            Transform eye = _camera.transform;
            float reach = _settings.ShoveMaxDistance;
            int count = Physics.RaycastNonAlloc(eye.position, eye.forward, _hits,
                Mathf.Max(reach, _settings.MaxTargetDistance),
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            int nearest = -1;
            for (int i = 0; i < count; i++)
            {
                if (_hits[i].collider.transform.IsChildOf(_player))
                    continue;
                if (nearest < 0 || _hits[i].distance < _hits[nearest].distance)
                    nearest = i;
            }
            if (nearest < 0 || _hits[nearest].distance > reach)
                return false;

            NetworkObject owner = _hits[nearest].collider.GetComponentInParent<NetworkObject>();
            if (owner == null || owner == _identity
                || !owner.TryGetComponent(out PlayerMotor _)
                || !owner.TryGetComponent(out SanityNetworkState sanity) || !sanity.HasSanity)
                return false;
            targetId = owner.NetworkObjectId;
            return true;
        }
    }
}
