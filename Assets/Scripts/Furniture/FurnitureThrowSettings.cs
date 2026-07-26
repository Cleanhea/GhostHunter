using UnityEngine;

namespace GhostHunter.Furniture
{
    [CreateAssetMenu(
        fileName = "FurnitureThrowSettings_Default",
        menuName = "GhostHunter/Furniture Throw Settings")]
    public sealed class FurnitureThrowSettings : ScriptableObject
    {
        [Header("타겟팅 / 홀드")]
        [SerializeField, Min(0.1f)] private float _maxTargetDistance = 12f;
        [SerializeField, Min(0.1f)] private float _maxHoldDistance = 15f;
        [SerializeField, Min(0.1f)] private float _hoverDistance = 3f;

        [Header("부양 스프링")]
        [SerializeField, Min(0f)] private float _springStiffness = 60f;
        [SerializeField, Min(0f)] private float _springDamping = 8f;
        [SerializeField, Range(0f, 1f)] private float _angularDamping = 0.9f;
        [SerializeField, Min(0f)] private float _maxHoverForce = 500f;

        [Header("차징 / 발사")]
        [SerializeField, Min(0.01f)] private float _chargeTime = 1f;
        [SerializeField, Range(0f, 1f)] private float _minChargeRatio = 0.4f;
        [SerializeField, Min(0f)] private float _oneHolderForce = 30f;
        [SerializeField, Min(0f)] private float _twoHolderForce = 50f;
        [SerializeField, HideInInspector] private float _upwardBias = 0.45f;
        [SerializeField, Range(0f, 1f)] private float _heavySoloMultiplier = 0.5f;
        [SerializeField, Min(0f)] private float _torqueScale = 2f;

        [Header("상태")]
        [SerializeField, Min(0f)] private float _relaunchLockDuration = 2f;
        [SerializeField, Min(0f)] private float _settledSpeed = 0.5f;
        [SerializeField] private bool _launchOnFirstRelease;

        public float MaxTargetDistance => _maxTargetDistance;
        public float MaxHoldDistance => _maxHoldDistance;
        public float HoverDistance => _hoverDistance;
        public float SpringStiffness => _springStiffness;
        public float SpringDamping => _springDamping;
        public float AngularDamping => _angularDamping;
        public float MaxHoverForce => _maxHoverForce;
        public float ChargeTime => _chargeTime;
        public float MinChargeRatio => _minChargeRatio;
        public float OneHolderForce => _oneHolderForce;
        public float TwoHolderForce => _twoHolderForce;
        internal float LegacyUpwardBias => _upwardBias;
        public float HeavySoloMultiplier => _heavySoloMultiplier;
        public float TorqueScale => _torqueScale;
        public float RelaunchLockDuration => _relaunchLockDuration;
        public float SettledSpeed => _settledSpeed;
        public bool LaunchOnFirstRelease => _launchOnFirstRelease;
    }
}
