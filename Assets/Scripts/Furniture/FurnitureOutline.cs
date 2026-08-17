using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Furniture
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(FurnitureGrabTarget))]
    public sealed class FurnitureOutline : NetworkBehaviour
    {
        private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");

        [Tooltip("윤곽선 셸 렌더러들. 가구가 여러 파츠로 이뤄지므로 파츠마다 하나씩 둔다.")]
        [SerializeField] private Renderer[] _outlineRenderers;

        [SerializeField] private Color _targetableColor = Color.white;
        [SerializeField] private Color _blockedColor = Color.gray;
        [SerializeField] private Color _mineColor = new(0.1f, 1f, 0.9f);
        [SerializeField] private Color _partnerColor = new(1f, 0.45f, 0.1f);
        [SerializeField] private Color _cooperativeColor = new(1f, 0.9f, 0.1f);

        private FurnitureGrabTarget _target;
        private MaterialPropertyBlock _propertyBlock;
        private bool _isTargeted;

        private void Awake()
        {
            _target = GetComponent<FurnitureGrabTarget>();
            _propertyBlock = new MaterialPropertyBlock();

            SetRenderersEnabled(false);
        }

        public override void OnNetworkSpawn()
        {
            _target.Holders.OnListChanged += HandleHoldersChanged;
            _target.StateChanged += HandleStateChanged;
            Refresh();
        }

        public override void OnNetworkDespawn()
        {
            _target.Holders.OnListChanged -= HandleHoldersChanged;
            _target.StateChanged -= HandleStateChanged;
        }

        public void SetTargeted(bool targeted)
        {
            _isTargeted = targeted;
            Refresh();
        }

        private void HandleHoldersChanged(NetworkListEvent<ulong> changeEvent)
        {
            Refresh();
        }

        private void HandleStateChanged(FurnitureState previous, FurnitureState current)
        {
            Refresh();
        }

        private void Refresh()
        {
            if (_outlineRenderers == null || _outlineRenderers.Length == 0 || !IsSpawned)
                return;

            bool show = false;
            Color color = _targetableColor;
            ulong localClientId = NetworkManager != null ? NetworkManager.LocalClientId : ulong.MaxValue;

            if (_target.HolderCount >= 2)
            {
                show = true;
                color = _cooperativeColor;
            }
            else if (_target.ContainsHolder(localClientId))
            {
                show = true;
                color = _mineColor;
            }
            else if (_target.HolderCount == 1)
            {
                show = true;
                color = _partnerColor;
            }
            else if (_isTargeted)
            {
                show = true;
                color = _target.State == FurnitureState.Launched
                    ? _blockedColor
                    : _targetableColor;
            }

            SetRenderersEnabled(show);
            if (!show)
                return;

            foreach (Renderer renderer in _outlineRenderers)
            {
                if (renderer == null)
                    continue;

                renderer.GetPropertyBlock(_propertyBlock);
                _propertyBlock.SetColor(OutlineColorId, color);
                renderer.SetPropertyBlock(_propertyBlock);
            }
        }

        private void SetRenderersEnabled(bool enabled)
        {
            if (_outlineRenderers == null)
                return;

            foreach (Renderer renderer in _outlineRenderers)
            {
                if (renderer != null)
                    renderer.enabled = enabled;
            }
        }
    }
}
