using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Player
{
    [DisallowMultipleComponent]
    public sealed class PlayerVisuals : NetworkBehaviour
    {
        [SerializeField] private Renderer[] _bodyRenderers;

        public override void OnNetworkSpawn()
        {
            if (_bodyRenderers == null)
                return;

            foreach (Renderer bodyRenderer in _bodyRenderers)
            {
                if (bodyRenderer != null)
                    bodyRenderer.enabled = !IsOwner;
            }
        }
    }
}
