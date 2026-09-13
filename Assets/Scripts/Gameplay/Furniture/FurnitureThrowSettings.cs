using UnityEngine;

namespace GhostHunter.Gameplay.Furniture
{
    [CreateAssetMenu(
        fileName = "FurnitureThrowSettings_Default",
        menuName = "GhostHunter/Furniture Throw Settings")]
    public sealed class FurnitureThrowSettings : ScriptableObject
    {
        [Header("타겟팅 / 홀드")]
        [Tooltip("좌클릭으로 가구를 조준·붙잡을 수 있는 최대 거리(m).")]
        [SerializeField, Min(0.1f)] private float _maxTargetDistance = 2f;
        [SerializeField, Min(0.1f)] private float _maxHoldDistance = 15f;
        [SerializeField, Min(0.1f)] private float _hoverDistance = 3f;

        [Header("2인 잡기 고정 추종")]
        [Tooltip("2인 잡기 가구가 조준점 중간을 따라갈 최대 속력(m/s). 이 안에서는 한 물리 스텝에 목표에 닿는다.")]
        [SerializeField, Min(0.1f)] private float _heldMaxLinearSpeed = 15f;

        [Tooltip("2인 잡기 가구가 목표 자세로 돌 최대 각속력(도/초).")]
        [SerializeField, Min(1f)] private float _heldMaxAngularSpeed = 720f;

        [Header("휠 회전")]
        [Tooltip("마우스 휠 한 칸에 회전·기울이는 각도(도).")]
        [SerializeField, Range(1f, 90f)] private float _wheelStepDegrees = 15f;

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
        public float HeldMaxLinearSpeed => _heldMaxLinearSpeed;
        public float HeldMaxAngularSpeed => _heldMaxAngularSpeed;
        public float WheelStepDegrees => _wheelStepDegrees;
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
