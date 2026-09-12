using Unity.Netcode;
using GhostHunter.Gameplay.Map;
using UnityEngine;

namespace GhostHunter.Gameplay.Furniture
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class FurnitureNetworkPhysics : NetworkBehaviour
    {
        [SerializeField] private FurnitureDefinition _definition;

        private Rigidbody _rigidbody;

        public FurnitureDefinition Definition => _definition;
        public Rigidbody Rigidbody => _rigidbody;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();

            if (_definition != null)
                _rigidbody.mass = _definition.Mass;
        }

        public override void OnNetworkSpawn()
        {
            RandomFurnitureItem randomItem = GetComponent<RandomFurnitureItem>();
            bool placed = randomItem == null || randomItem.IsPlaced;
            bool simulate = IsServer && placed;
            _rigidbody.isKinematic = !simulate;

            // Clients do not simulate furniture physics, but their colliders must stay
            // queryable so local targeting raycasts can select and grab furniture.
            _rigidbody.detectCollisions = placed;
        }
    }
}
