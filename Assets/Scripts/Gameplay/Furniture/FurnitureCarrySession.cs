using System;
using System.Collections.Generic;
using UnityEngine;

namespace GhostHunter.Gameplay.Furniture
{
    /// <summary>
    /// 2인 운반 한 번(Held 진입 ~ 이탈)의 추종 계산 → docs/architecture/throw-system.md §3.
    /// 네트워크와 무관한 서버 물리 부분만 담아 <see cref="FurnitureHoverMotor"/>가 쓰고, 테스트는 문틀 모형에서 직접 돌린다.
    /// <list type="number">
    /// <item>두 손잡이 — 진입 순간 각 홀더가 조준한 가구 위 점(손잡이)이 그 홀더의 손 목표(눈 + 조준 × 잡은 거리)를 따라간다.
    /// 두 손을 잇는 방향으로 가구가 돌아서, 앞뒤로 서서 걸으면 긴 쪽이 진행 방향을 향한다.</item>
    /// <item>속도 서보 — 손이 움직이는 속도를 미리 싣고 남은 오차만 시간 상수로 줄이며, 속도 변화량을 제한해 부딪혀도 덜컹거리지 않는다.</item>
    /// <item>끼임 보조 — 옆으로 막혀 목표에서 밀려난 채 잠시 지나면 작은 회전·옆 이동 중 비는 자세를 찾아 비킨다.
    /// 막힘이 풀리면 비는 범위에서 한 칸씩 되돌린다.</item>
    /// </list>
    /// </summary>
    internal sealed class FurnitureCarrySession
    {
        private const float BlockedSecondsBeforeSqueeze = 0.1f;
        // 보정을 하나 더하면 서보가 그 자세에 닿을 시간을 준 뒤에 다음 보정을 찾는다.
        private const float SqueezeRetrySeconds = 0.3f;
        private const float SqueezeFailBackoffSeconds = 0.5f;
        private const float AssistReturnSeconds = 0.1f;

        private readonly Rigidbody _body;
        private readonly FurnitureClearanceProbe _probe;
        private readonly Predicate<Collider> _isIgnored;
        private readonly List<FurnitureSqueezeAssist.Candidate> _candidates = new();
        private readonly Vector3[] _gripLocal = new Vector3[2];
        private readonly float[] _handDistance = new float[2];
        private readonly Vector3[] _smoothedHand = new Vector3[2];

        private FurnitureThrowSettings _settings;
        private Vector3 _entryGripAxis;
        private Quaternion _follow = Quaternion.identity;
        private Vector3 _previousHandMidpoint;
        private float _blockedSeconds;
        private float _nextSqueezeAt;
        private float _nextAssistReturnAt;

        public FurnitureCarrySession(Rigidbody body, FurnitureClearanceProbe probe, Predicate<Collider> isIgnored)
        {
            _body = body;
            _probe = probe;
            _isIgnored = isIgnored;
        }

        /// <summary>끼임 보조로 더한 수직축 회전(도).</summary>
        public float AssistYaw { get; private set; }

        /// <summary>끼임 보조로 더한 옆 이동(월드, m).</summary>
        public Vector3 AssistOffset { get; private set; }

        /// <summary>
        /// 운반을 시작한다. <paramref name="gripA"/>·<paramref name="gripB"/>는 각 홀더 조준이 닿은 가구 위 월드 점이다.
        /// </summary>
        public void Begin(
            FurnitureThrowSettings settings,
            Vector3 originA, Vector3 directionA, Vector3 gripA,
            Vector3 originB, Vector3 directionB, Vector3 gripB)
        {
            _settings = settings;
            SetGrip(0, originA, directionA, gripA);
            SetGrip(1, originB, directionB, gripB);
            _entryGripAxis = gripB - gripA;
            _follow = Quaternion.identity;
            _previousHandMidpoint = (_smoothedHand[0] + _smoothedHand[1]) * 0.5f;
            AssistYaw = 0f;
            AssistOffset = Vector3.zero;
            _blockedSeconds = 0f;
            _nextSqueezeAt = 0f;
            _nextAssistReturnAt = 0f;
            FurnitureSqueezeAssist.BuildCandidates(
                settings.SqueezeMaxYaw, settings.SqueezeYawStep,
                settings.SqueezeMaxLateral, settings.SqueezeLateralStep, _candidates);
        }

        /// <summary>
        /// 한 물리 스텝. 바디에 선속도·각속도를 준다. <paramref name="heldRotation"/>은 휠로 바뀐 목표 자세,
        /// <paramref name="touchingWall"/>은 직전 스텝에 옆으로 막는 접촉이 있었는가.
        /// </summary>
        public void Step(
            float deltaTime,
            float now,
            Vector3 originA, Vector3 directionA,
            Vector3 originB, Vector3 directionB,
            Quaternion heldRotation,
            bool touchingWall)
        {
            if (_settings == null || deltaTime <= 0f)
                return;

            float blend = FurnitureHeldControl.SmoothingFactor(deltaTime, _settings.CarryAimSmoothing);
            _smoothedHand[0] = Vector3.Lerp(_smoothedHand[0], originA + directionA * _handDistance[0], blend);
            _smoothedHand[1] = Vector3.Lerp(_smoothedHand[1], originB + directionB * _handDistance[1], blend);

            if (FurnitureHeldControl.TryFollowRotation(
                    _entryGripAxis,
                    _smoothedHand[1] - _smoothedHand[0],
                    _settings.CarryFollowMinGripSpan,
                    _settings.CarryMaxFollowPitch,
                    out Quaternion follow))
            {
                _follow = follow;
            }

            // 휠 자세 위에 두 사람 방향(follow)과 끼임 보조 회전을 얹는다.
            Quaternion desiredRotation = Quaternion.AngleAxis(AssistYaw, Vector3.up) * _follow * heldRotation;
            Vector3 handMidpoint = (_smoothedHand[0] + _smoothedHand[1]) * 0.5f;
            Vector3 gripMidpoint = (_gripLocal[0] + _gripLocal[1]) * 0.5f;
            Vector3 desiredPosition = FurnitureHeldControl.SolveCarryPosition(
                handMidpoint, desiredRotation, gripMidpoint) + AssistOffset;

            float maxSpeed = _settings.HeldMaxLinearSpeed;
            Vector3 handVelocity = Vector3.ClampMagnitude(
                (handMidpoint - _previousHandMidpoint) / deltaTime, maxSpeed);
            _previousHandMidpoint = handMidpoint;

            float positionResponse = _settings.CarryPositionResponse;
            Vector3 wantedVelocity = FurnitureHeldControl.TrackingVelocity(
                _body.position, desiredPosition + handVelocity * positionResponse, positionResponse, maxSpeed);
            _body.linearVelocity = FurnitureHeldControl.LimitChange(
                _body.linearVelocity, wantedVelocity, _settings.CarryMaxAcceleration * deltaTime);

            Vector3 wantedAngular = FurnitureHeldControl.TrackingAngularVelocity(
                _body.rotation, desiredRotation, _settings.CarryRotationResponse, _settings.HeldMaxAngularSpeed);
            _body.angularVelocity = FurnitureHeldControl.LimitChange(
                _body.angularVelocity, wantedAngular,
                _settings.CarryMaxAngularAcceleration * Mathf.Deg2Rad * deltaTime);

            UpdateSqueeze(desiredPosition, touchingWall, deltaTime, now);
        }

        private void SetGrip(int index, Vector3 origin, Vector3 direction, Vector3 grip)
        {
            _gripLocal[index] = Quaternion.Inverse(_body.rotation) * (grip - _body.position);
            _handDistance[index] = Mathf.Clamp(
                Vector3.Distance(origin, grip), _settings.CarryMinHandDistance, _settings.CarryMaxHandDistance);
            _smoothedHand[index] = origin + direction * _handDistance[index];
        }

        private void UpdateSqueeze(Vector3 desiredPosition, bool touchingWall, float deltaTime, float now)
        {
            if (_probe == null || _settings.SqueezeTriggerDistance <= 0f || _candidates.Count <= 1)
                return;

            Vector3 error = desiredPosition - _body.position;
            Vector3 flatError = new(error.x, 0f, error.z);
            float flatDistance = flatError.magnitude;
            bool blocked = touchingWall && flatDistance > _settings.SqueezeTriggerDistance;
            _blockedSeconds = blocked ? _blockedSeconds + deltaTime : 0f;

            if (_blockedSeconds >= BlockedSecondsBeforeSqueeze)
            {
                if (now >= _nextSqueezeAt)
                    TrySqueeze(flatError / flatDistance, Mathf.Min(flatDistance, _settings.SqueezeProbeDistance), now);
                return;
            }

            if (!blocked && now >= _nextAssistReturnAt && (AssistYaw != 0f || AssistOffset != Vector3.zero))
                ReturnAssist(now);
        }

        private void TrySqueeze(Vector3 moveDirection, float probeDistance, float now)
        {
            Vector3 lateral = Vector3.Cross(Vector3.up, moveDirection);
            if (_probe.TryFindClearPose(
                    _body.position, _body.rotation, moveDirection, probeDistance, lateral, _candidates, _isIgnored,
                    out FurnitureSqueezeAssist.Candidate found)
                && !found.IsIdentity)
            {
                AssistYaw = Mathf.Clamp(AssistYaw + found.Yaw, -_settings.SqueezeMaxYaw, _settings.SqueezeMaxYaw);
                AssistOffset = Vector3.ClampMagnitude(AssistOffset + lateral * found.Lateral, _settings.SqueezeMaxLateral);
                _nextSqueezeAt = now + SqueezeRetrySeconds;
                return;
            }

            // 보정 없이도 비어 있거나(옆이 아닌 이유로 막힘) 어떤 후보도 안 되면 잠시 쉰다 — 벽에 대고 밀 때 매 스텝 검사하지 않는다.
            _nextSqueezeAt = now + SqueezeFailBackoffSeconds;
        }

        private void ReturnAssist(float now)
        {
            _nextAssistReturnAt = now + AssistReturnSeconds;
            float yawStep = Mathf.MoveTowards(AssistYaw, 0f, _settings.SqueezeYawStep) - AssistYaw;
            Vector3 offsetStep = Vector3.MoveTowards(AssistOffset, Vector3.zero, _settings.SqueezeLateralStep)
                - AssistOffset;

            if (!_probe.CanShift(_body.position, _body.rotation, yawStep, offsetStep, _isIgnored))
                return;

            AssistYaw += yawStep;
            AssistOffset += offsetStep;
        }
    }
}
