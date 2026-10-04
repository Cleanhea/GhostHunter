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
        [Tooltip("잡고 차징하는 동안(1인 투척 준비·2인 잡기) 플레이어 눈 위치에서 가구 표면까지 이 거리(m)를 넘으면 발사 없이 잡기가 풀린다.")]
        [SerializeField, Min(0.1f)] private float _holdBreakDistance = 4f;

        [Header("2인 운반 — 두 손잡이 추종")]
        [Tooltip("손잡이(잡은 점)를 눈에서 최소 이만큼(m) 떨어뜨려 든다. 가구가 얼굴 앞을 가리지 않게 한다.")]
        [SerializeField, Min(0.1f)] private float _carryMinHandDistance = 0.9f;

        [Tooltip("손잡이를 눈에서 최대 이만큼(m) 떨어뜨려 든다. 잡을 때 거리가 이보다 멀면 당겨 온다.")]
        [SerializeField, Min(0.1f)] private float _carryMaxHandDistance = 2.2f;

        [Tooltip("홀더 조준(약 20Hz로 도착)을 부드럽게 잇는 시간 상수(초). 0이면 받은 값을 그대로 쓴다.")]
        [SerializeField, Min(0f)] private float _carryAimSmoothing = 0.05f;

        [Tooltip("위치 오차를 줄이는 시간 상수(초). 작을수록 딱 붙고, 클수록 문틀 등에 부드럽게 밀린다.")]
        [SerializeField, Min(0.02f)] private float _carryPositionResponse = 0.06f;

        [Tooltip("자세 오차를 줄이는 시간 상수(초).")]
        [SerializeField, Min(0.02f)] private float _carryRotationResponse = 0.08f;

        [Tooltip("2인 운반 가구의 최대 속력(m/s).")]
        [SerializeField, Min(0.1f)] private float _heldMaxLinearSpeed = 15f;

        [Tooltip("2인 운반 가구의 최대 각속력(도/초).")]
        [SerializeField, Min(1f)] private float _heldMaxAngularSpeed = 720f;

        [Tooltip("2인 운반 가구의 속도가 한 초에 바뀔 수 있는 최대량(m/s²). 벽·문틀에 부딪힐 때 덜컹거림을 줄인다.")]
        [SerializeField, Min(1f)] private float _carryMaxAcceleration = 60f;

        [Tooltip("2인 운반 가구의 각속도가 한 초에 바뀔 수 있는 최대량(도/초²).")]
        [SerializeField, Min(1f)] private float _carryMaxAngularAcceleration = 3600f;

        [Tooltip("두 손잡이의 수평 간격(m)이 이보다 짧으면 두 사람 위치로 방향을 돌리지 않는다(작은 가구를 한쪽에서 같이 잡은 경우).")]
        [SerializeField, Min(0.05f)] private float _carryFollowMinGripSpan = 0.35f;

        [Tooltip("두 사람의 손 높이 차이로 가구가 기울 수 있는 최대 각도(도). 0이면 수평을 유지한다.")]
        [SerializeField, Range(0f, 60f)] private float _carryMaxFollowPitch = 25f;

        [Tooltip("2인 운반 중 가구 콜라이더 마찰 계수. 낮을수록 문틀·벽을 긁으며 미끄러져 지나간다.")]
        [SerializeField, Range(0f, 1f)] private float _carryFriction = 0.05f;

        [Tooltip("2인 운반 중 들고 있는 두 사람과 가구가 서로 부딪히지 않는다.")]
        [SerializeField] private bool _carryIgnoresHolders = true;

        [Tooltip("2인 운반 중 열린 문짝과 부딪히지 않는다(닫힌 문은 그대로 막는다).")]
        [SerializeField] private bool _carryPassesOpenDoors = true;

        [Header("2인 운반 — 끼임 보조")]
        [Tooltip("문틀 등에 걸려 목표에서 이 거리(m) 넘게 밀려나 있으면 끼임 보조를 시작한다. 0이면 끈다.")]
        [SerializeField, Min(0f)] private float _squeezeTriggerDistance = 0.1f;

        [Tooltip("끼임 보조가 한 번에 시험하는 최대 회전(도).")]
        [SerializeField, Range(0f, 90f)] private float _squeezeMaxYaw = 40f;

        [Tooltip("끼임 보조 회전 간격(도). 보조가 풀릴 때도 이 간격으로 되돌린다.")]
        [SerializeField, Range(1f, 45f)] private float _squeezeYawStep = 10f;

        [Tooltip("끼임 보조가 시험하는 최대 옆 이동(m). 누적 옆 이동도 이 값을 넘지 않는다.")]
        [SerializeField, Range(0f, 1f)] private float _squeezeMaxLateral = 0.3f;

        [Tooltip("끼임 보조 옆 이동 간격(m). 보조가 풀릴 때도 이 간격으로 되돌린다.")]
        [SerializeField, Range(0.01f, 0.5f)] private float _squeezeLateralStep = 0.1f;

        [Tooltip("끼임 보조가 진행 방향으로 미리 밀어 보고 겹침을 검사하는 거리(m).")]
        [SerializeField, Range(0.02f, 0.5f)] private float _squeezeProbeDistance = 0.12f;

        [Header("휠 회전")]
        [Tooltip("마우스 휠 한 칸에 회전·기울이는 각도(도).")]
        [SerializeField, Range(1f, 90f)] private float _wheelStepDegrees = 15f;

        [Header("차징 / 발사")]
        [Tooltip("누르기 시작해서 최대 힘이 되기까지 걸리는 시간(초).")]
        [SerializeField, Min(0.01f)] private float _chargeTime = 1f;

        [Tooltip("살짝 눌렀다 뗐을 때(차지 0) 나가는 힘 — 최대 힘에 대한 비율.")]
        [SerializeField, Range(0f, 1f)] private float _minChargeRatio = 0.15f;

        [Tooltip("차지가 힘으로 바뀌는 곡선의 지수. 1이면 직선, 클수록 초반이 약하고 끝에서 빠르게 세진다.")]
        [SerializeField, Range(1f, 4f)] private float _chargeCurve = 2f;

        [Tooltip("1인 최대 발사 속도 변화량(m/s).")]
        [SerializeField, Min(0f)] private float _oneHolderForce = 30f;

        [Tooltip("2인 최대 발사 속도 변화량(m/s).")]
        [SerializeField, Min(0f)] private float _twoHolderForce = 50f;
        [SerializeField, HideInInspector] private float _upwardBias = 0.45f;
        [SerializeField, Range(0f, 1f)] private float _heavySoloMultiplier = 0.5f;
        [SerializeField, Min(0f)] private float _torqueScale = 2f;

        [Header("상태")]
        [SerializeField, Min(0f)] private float _relaunchLockDuration = 2f;
        [SerializeField, Min(0f)] private float _settledSpeed = 0.5f;
        [SerializeField] private bool _launchOnFirstRelease;

        [Tooltip("2인 잡기에서 한 명이 먼저 놓은 뒤 이 시간(초) 안에 남은 한 명도 놓으면 '같이 내려놓았다'고 보고 " +
            "발사하지 않는다. 이 시간이 지나 놓으면 평소처럼 1인 투척이다. 0이면 끈다.")]
        [SerializeField, Min(0f)] private float _jointPutDownWindow = 2.5f;

        public float MaxTargetDistance => _maxTargetDistance;
        public float HoldBreakDistance => _holdBreakDistance;
        public float CarryMinHandDistance => _carryMinHandDistance;
        public float CarryMaxHandDistance => Mathf.Max(_carryMinHandDistance, _carryMaxHandDistance);
        public float CarryAimSmoothing => _carryAimSmoothing;
        public float CarryPositionResponse => _carryPositionResponse;
        public float CarryRotationResponse => _carryRotationResponse;
        public float HeldMaxLinearSpeed => _heldMaxLinearSpeed;
        public float HeldMaxAngularSpeed => _heldMaxAngularSpeed;
        public float CarryMaxAcceleration => _carryMaxAcceleration;
        public float CarryMaxAngularAcceleration => _carryMaxAngularAcceleration;
        public float CarryFollowMinGripSpan => _carryFollowMinGripSpan;
        public float CarryMaxFollowPitch => _carryMaxFollowPitch;
        public float CarryFriction => _carryFriction;
        public bool CarryIgnoresHolders => _carryIgnoresHolders;
        public bool CarryPassesOpenDoors => _carryPassesOpenDoors;
        public float SqueezeTriggerDistance => _squeezeTriggerDistance;
        public float SqueezeMaxYaw => _squeezeMaxYaw;
        public float SqueezeYawStep => _squeezeYawStep;
        public float SqueezeMaxLateral => _squeezeMaxLateral;
        public float SqueezeLateralStep => _squeezeLateralStep;
        public float SqueezeProbeDistance => _squeezeProbeDistance;
        public float WheelStepDegrees => _wheelStepDegrees;
        public float ChargeTime => _chargeTime;
        public float MinChargeRatio => _minChargeRatio;
        public float ChargeCurve => _chargeCurve;
        public float OneHolderForce => _oneHolderForce;
        public float TwoHolderForce => _twoHolderForce;
        internal float LegacyUpwardBias => _upwardBias;
        public float HeavySoloMultiplier => _heavySoloMultiplier;
        public float TorqueScale => _torqueScale;
        public float RelaunchLockDuration => _relaunchLockDuration;
        public float SettledSpeed => _settledSpeed;
        public bool LaunchOnFirstRelease => _launchOnFirstRelease;
        public float JointPutDownWindow => _jointPutDownWindow;

        /// <summary>
        /// 차지(0~1, 누른 시간 비율)를 발사 힘 비율(최대 힘 대비)로 바꾼다 → docs/architecture/throw-system.md §4.
        /// 살짝 누르면 <see cref="MinChargeRatio"/>, 끝까지 누르면 1이다.
        /// </summary>
        public float ForceRatio(float charge) => ChargeForceRatio(charge, _minChargeRatio, _chargeCurve);

        /// <summary><c>min + (1 − min) × charge^curve</c>. 곡선 지수는 1 미만으로 내려가지 않는다.</summary>
        public static float ChargeForceRatio(float charge, float minRatio, float curve)
        {
            float shaped = Mathf.Pow(Mathf.Clamp01(charge), Mathf.Max(1f, curve));
            return Mathf.Lerp(Mathf.Clamp01(minRatio), 1f, shaped);
        }
    }
}
