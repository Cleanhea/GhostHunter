using UnityEngine;

namespace GhostHunter.Gameplay.Furniture
{
    public enum FurnitureWeightClass
    {
        Light,
        Heavy,
    }

    [CreateAssetMenu(
        fileName = "FurnitureDefinition_New",
        menuName = "GhostHunter/Furniture Definition")]
    public sealed class FurnitureDefinition : ScriptableObject
    {
        [SerializeField, Min(0.01f)] private float _mass = 8f;
        [SerializeField] private FurnitureWeightClass _weightClass = FurnitureWeightClass.Light;

        [Header("충돌 내구도")]
        [SerializeField, Min(0f)] private float _damageMinimumSpeed = 6f;
        [SerializeField, Min(0f)] private float _damagePerSpeed = 1f;
        [SerializeField, Min(0f)] private float _damageWeightMultiplier = 1f;
        [SerializeField, Range(0, 100)] private int _maximumCollisionDamage = 50;
        [SerializeField, Min(0f)] private float _collisionWindowSeconds = 0.2f;
        [SerializeField, Min(0f)] private float _placementProtectionSeconds = 1f;
        [SerializeField] private bool _damageWhileHeld = true;

        [Tooltip("내구도가 0이 되면 가구를 스테이지에서 없앤다(FD-10). " +
            "끈 상태(기본값, 2026-09-16 사용자 요청)에서는 0이 되어도 그대로 남아 잡기·던지기·조립에 계속 쓸 수 있다.")]
        [SerializeField] private bool _destroyAtZeroDurability;

        [Header("취급 방식 (리프트 본체 등 특수 오브젝트)")]
        [Tooltip("동시에 잡을 수 있는 최대 인원. 1이면 2인 잡기·운반(부양)이 막히고 1인 투척 준비만 된다. " +
            "리프트 본체는 공중에 뜨지 않는다(lift-system.md).")]
        [SerializeField, Range(1, FurnitureGrabTarget.MaxHolders)] private int _maxHolders = FurnitureGrabTarget.MaxHolders;

        [Tooltip("켜면 발사 방향을 수평으로 눕히고(위 성분 없음) 회전을 주지 않는다. 바퀴 달린 리프트 본체가 날아오르거나 뒤집히지 않게 한다.")]
        [SerializeField] private bool _horizontalLaunchOnly;

        [Tooltip("발사 속도 배율. 1이 일반 가구. 리프트 본체는 멀리 날아가지 않고 굴러가듯 밀리게 1보다 낮춘다.")]
        [SerializeField, Min(0.01f)] private float _launchSpeedScale = 1f;

        public int MaxHolders => _maxHolders;
        public bool HorizontalLaunchOnly => _horizontalLaunchOnly;
        public float LaunchSpeedScale => _launchSpeedScale;

        public float Mass => _mass;
        public FurnitureWeightClass WeightClass => _weightClass;
        public bool IsHeavy => _weightClass == FurnitureWeightClass.Heavy;
        public float DamageMinimumSpeed => _damageMinimumSpeed;
        public float DamagePerSpeed => _damagePerSpeed;
        public float DamageWeightMultiplier => _damageWeightMultiplier;
        public int MaximumCollisionDamage => _maximumCollisionDamage;
        public float CollisionWindowSeconds => _collisionWindowSeconds;
        public float PlacementProtectionSeconds => _placementProtectionSeconds;
        public bool DamageWhileHeld => _damageWhileHeld;
        public bool DestroyAtZeroDurability => _destroyAtZeroDurability;
    }
}
