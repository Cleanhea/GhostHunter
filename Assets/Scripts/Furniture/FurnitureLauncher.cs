using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Furniture
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody), typeof(FurnitureNetworkPhysics))]
    public sealed class FurnitureLauncher : NetworkBehaviour
    {
        private const float MinimumLaunchAngle = 20f;
        private const float MaximumLaunchAngle = 70f;
        private const float MinimumOneHolderLaunchSpeed = 30f;
        private const float MinimumTwoHolderLaunchSpeed = 50f;

        [SerializeField] private FurnitureThrowSettings _settings;

        private Rigidbody _rigidbody;
        private FurnitureNetworkPhysics _networkPhysics;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            _networkPhysics = GetComponent<FurnitureNetworkPhysics>();
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer)
                enabled = false;
        }

        public void ServerLaunch(Vector3 aimDirection, float charge, int holderCount)
        {
            if (!IsServer || _settings == null || !IsFinite(aimDirection))
                return;

            float baseForce = holderCount >= 2
                ? Mathf.Max(_settings.TwoHolderForce, MinimumTwoHolderLaunchSpeed)
                : Mathf.Max(_settings.OneHolderForce, MinimumOneHolderLaunchSpeed);

            bool heavySolo = holderCount < 2
                && _networkPhysics.Definition != null
                && _networkPhysics.Definition.IsHeavy;

            float weightMultiplier = heavySolo ? _settings.HeavySoloMultiplier : 1f;
            float chargeMultiplier = Mathf.Lerp(
                _settings.MinChargeRatio,
                1f,
                Mathf.Clamp01(charge));

            float magnitude = baseForce * weightMultiplier * chargeMultiplier;
            Vector3 direction = ResolveLaunchDirection(
                aimDirection,
                MinimumLaunchAngle,
                MaximumLaunchAngle);

            _rigidbody.useGravity = true;
            _rigidbody.AddForce(direction * magnitude, ForceMode.VelocityChange);
            _rigidbody.AddTorque(
                Random.insideUnitSphere * _settings.TorqueScale,
                ForceMode.Impulse);

            OnLaunchedClientRpc(direction, magnitude);
        }

        [ClientRpc]
        private void OnLaunchedClientRpc(Vector3 direction, float magnitude)
        {
            // 사운드/파티클을 붙일 때 사용할 네트워크 훅. 프로토타입 범위에서는 물리 피드백만 쓴다.
        }

        internal static Vector3 ResolveLaunchDirection(
            Vector3 aimDirection,
            float minAngle,
            float maxAngle)
        {
            Vector3 normalizedAim = aimDirection.normalized;
            Vector3 horizontal = Vector3.ProjectOnPlane(normalizedAim, Vector3.up);
            float horizontalMagnitude = horizontal.magnitude;

            if (horizontalMagnitude < 0.01f)
                horizontal = Vector3.forward;

            float correctedMin = Mathf.Clamp(minAngle, 0f, 89f);
            float correctedMax = Mathf.Clamp(maxAngle, correctedMin, 89f);
            float aimAngle = Mathf.Atan2(normalizedAim.y, horizontalMagnitude) * Mathf.Rad2Deg;
            float launchAngle = Mathf.Clamp(aimAngle, correctedMin, correctedMax) * Mathf.Deg2Rad;

            return horizontal.normalized * Mathf.Cos(launchAngle)
                + Vector3.up * Mathf.Sin(launchAngle);
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x)
                && float.IsFinite(value.y)
                && float.IsFinite(value.z)
                && value.sqrMagnitude > 0.01f;
        }
    }
}
