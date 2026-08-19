using GhostHunter.Core;
using GhostHunter.Furniture;
using GhostHunter.Player;
using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Interaction
{
    [DefaultExecutionOrder(0)]
    [DisallowMultipleComponent]
    public sealed class FurnitureTargeter : NetworkBehaviour
    {
        [SerializeField] private Camera _camera;
        [SerializeField] private FurnitureThrowSettings _settings;

        private FurnitureGrabTarget _currentTarget;
        private FurnitureGrabTarget _holdTarget;

        public FurnitureGrabTarget CurrentTarget => _holdTarget != null ? _holdTarget : _currentTarget;
        public bool HasTarget => CurrentTarget != null;

        private ILocalPlayerContext _localPlayer;

        public override void OnNetworkSpawn()
        {
            if (!IsOwner)
            {
                enabled = false;
                return;
            }

            _localPlayer = Services.Get<ILocalPlayerContext>();
            _localPlayer.Register(this);
        }

        public override void OnNetworkDespawn()
        {
            _localPlayer?.Unregister(this);
            _localPlayer = null;

            SetRaycastTarget(null);
            _holdTarget = null;
        }

        private void Update()
        {
            if (_holdTarget != null || _camera == null || _settings == null)
                return;

            FurnitureGrabTarget hitTarget = null;
            Ray ray = new(_camera.transform.position, _camera.transform.forward);

            if (Physics.Raycast(
                    ray,
                    out RaycastHit hit,
                    _settings.MaxTargetDistance,
                    GameLayers.FurnitureMask,
                    QueryTriggerInteraction.Ignore))
            {
                hitTarget = hit.collider.GetComponentInParent<FurnitureGrabTarget>();
            }

            SetRaycastTarget(hitTarget);
        }

        public void SetHoldTarget(FurnitureGrabTarget target)
        {
            if (_holdTarget == target)
                return;

            if (_holdTarget == null)
                SetRaycastTarget(null);

            _holdTarget = target;
        }

        private void SetRaycastTarget(FurnitureGrabTarget target)
        {
            if (_currentTarget == target)
                return;

            if (_currentTarget != null
                && _currentTarget.TryGetComponent(out FurnitureOutline previousOutline))
            {
                previousOutline.SetTargeted(false);
            }

            _currentTarget = target;

            if (_currentTarget != null
                && _currentTarget.TryGetComponent(out FurnitureOutline currentOutline))
            {
                currentOutline.SetTargeted(true);
            }
        }
    }
}
