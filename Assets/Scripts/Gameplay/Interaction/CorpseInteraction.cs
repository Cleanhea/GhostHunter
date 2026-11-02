using System;
using GhostHunter.Gameplay.Furniture;
using GhostHunter.Gameplay.Player;
using UnityEngine;

namespace GhostHunter.Gameplay.Interaction
{
    /// <summary>시체 클릭·홀드·해제의 로컬 상태. 서버 요청은 GrabController의 기존 RPC로 보낸다.</summary>
    internal sealed class CorpseInteraction
    {
        private readonly Camera _camera;
        private readonly FurnitureThrowSettings _settings;
        private readonly Action<ulong, Vector3, Vector3> _push;
        private readonly Action<ulong, Vector3, Vector3> _carry;
        private readonly Action<ulong, Vector3, Vector3> _aim;
        private readonly Action<ulong> _release;
        private ulong _targetId = GrabController.NoObjectId;
        private float _pressAt;
        private float _holdThreshold;
        private Vector3 _pressOrigin;
        private Vector3 _pressDirection;
        private bool _carryRequested;

        public bool IsActive => _targetId != GrabController.NoObjectId;

        public CorpseInteraction(
            Camera camera, FurnitureThrowSettings settings,
            Action<ulong, Vector3, Vector3> push, Action<ulong, Vector3, Vector3> carry,
            Action<ulong, Vector3, Vector3> aim, Action<ulong> release)
        {
            _camera = camera;
            _settings = settings;
            _push = push;
            _carry = carry;
            _aim = aim;
            _release = release;
        }

        public bool TryBegin(bool furnitureBusy)
        {
            if (furnitureBusy || _settings == null
                || !Physics.Raycast(_camera.transform.position, _camera.transform.forward,
                    out RaycastHit hit, _settings.MaxTargetDistance,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                || !PlayerVisuals.TryGetCorpseOwner(hit.collider, out PlayerVisuals corpse))
                return false;

            _targetId = corpse.NetworkObjectId;
            _pressAt = Time.unscaledTime;
            _holdThreshold = corpse.CarryHoldSeconds;
            _pressOrigin = _camera.transform.position;
            _pressDirection = _camera.transform.forward;
            _carryRequested = false;
            return true;
        }

        public void Tick(bool released, float aimSendInterval, ref float nextAimSendAt)
        {
            ulong corpseId = _targetId;
            if (released)
            {
                if (_carryRequested)
                    _release(corpseId);
                else if (Time.unscaledTime - _pressAt < _holdThreshold)
                    _push(corpseId, _pressOrigin, _pressDirection);
                Clear();
                return;
            }

            if (!_carryRequested && Time.unscaledTime - _pressAt >= _holdThreshold)
            {
                _carryRequested = true;
                _carry(corpseId, _camera.transform.position, _camera.transform.forward);
            }

            if (_carryRequested && Time.unscaledTime >= nextAimSendAt)
            {
                nextAimSendAt = Time.unscaledTime + aimSendInterval;
                _aim(corpseId, _camera.transform.position, _camera.transform.forward);
            }
        }

        public void Cancel(bool isSpawned)
        {
            if (IsActive && _carryRequested && isSpawned)
                _release(_targetId);
            Clear();
        }

        private void Clear()
        {
            _targetId = GrabController.NoObjectId;
            _carryRequested = false;
        }
    }
}
