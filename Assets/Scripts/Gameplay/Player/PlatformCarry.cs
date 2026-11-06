using GhostHunter.Core;
using GhostHunter.Gameplay.Lift;
using UnityEngine;

namespace GhostHunter.Gameplay.Player
{
    /// <summary>
    /// 승강 발판 위에 선 플레이어를 발판이 움직인 만큼 같이 옮긴다(lift-system.md §6.2).
    /// 플레이어 이동은 소유자 권위 <see cref="CharacterController"/>라 발밑 키네마틱 바디가 알아서 싣고 가지 않는다 —
    /// 발밑 광선이 <see cref="LiftPlatform"/>에 닿으면 이전 프레임 대비 발판 변위를 돌려준다.
    /// ADR-0008의 연장이라 새 네트워크 상태가 없다.
    /// <para>
    /// <b>변위는 발판의 물리 자세(콜라이더 위치)로 잰다.</b> 탑승자는 보간된 화면 위치가 아니라 물리 스텝마다 뛰는 콜라이더를
    /// 밟기 때문이다. 화면 위치(<c>transform.position</c>)로 재면 발이 콜라이더를 파고들다 스킨 폭(0.05m)을 넘는 순간
    /// 한꺼번에 튀어 올랐다(144fps 에서 0 0 0 0 62mm). 대신 물리 자세와 화면 위치의 차이를 <see cref="VisualOffset"/> 으로
    /// 돌려주고 호출자가 카메라에 얹어 눈에 보이는 계단을 상쇄한다.
    /// </para>
    /// </summary>
    internal sealed class PlatformCarry
    {
        private const float ProbeStartHeight = 0.1f;
        private const float ProbeDistance = 0.6f;

        // 이보다 큰 한 프레임 변위는 순간이동(씬 이동·복구)으로 보고 따라가지 않는다.
        private const float TeleportDistance = 1.5f;

        private readonly int _mask;
        private Collider _cachedCollider;
        private LiftPlatform _cachedPlatform;
        private LiftPlatform _platform;
        private Vector3 _lastPosition;

        public PlatformCarry()
        {
            _mask = GameLayers.Player >= 0 ? ~(1 << GameLayers.Player) : ~0;
        }

        /// <summary>
        /// 탑승 중 카메라(눈)에 더할 월드 보정 — 발판 화면 위치 − 물리 자세. 탑승 중이 아니면 0.
        /// 발은 콜라이더 위에 일관되게 서고(스텝 단위), 보이는 위치만 보간된 발판을 따라간다.
        /// </summary>
        public Vector3 VisualOffset { get; private set; }

        /// <summary>
        /// 플레이어를 이번 프레임 움직인다 — 발판이 움직인 만큼 싣고 가는 이동을 <b>먼저 별도 Move</b> 로, 입력·중력 이동을 그다음
        /// Move 로 한다. 한 Move 에 합치면 접지 중력(-2m/s 이상)이 상승분을 덮어 순변위가 아래쪽이 되고, 올라가는 발판에 발이
        /// 파고들다 스킨 폭(0.05m)을 넘을 때만 한꺼번에 튀어 올랐다(올라갈 때 달달달). <c>isGrounded</c> 는 마지막 Move(중력)가
        /// 정하므로 발판 위에서도 프레임마다 깜빡이지 않는다. <c>PlayerMotor</c> 와 테스트가 같은 순서를 쓰도록 한곳에 둔다.
        /// </summary>
        public void Move(CharacterController controller, Vector3 displacement, bool grounded)
        {
            Vector3 platformDelta = ConsumeDelta(controller.transform.position, grounded);
            if (platformDelta != Vector3.zero)
                controller.Move(platformDelta);

            controller.Move(displacement);
        }

        /// <summary>
        /// 이번 프레임에 발판이 움직인 변위(물리 자세 기준). 땅에 서 있지 않거나 발판이 아니면 0 —
        /// 발판을 바꿔 타거나 떠난 뒤에는 기준점을 새로 잡는다.
        /// </summary>
        public Vector3 ConsumeDelta(Vector3 feet, bool grounded)
        {
            LiftPlatform platform = grounded ? FindPlatform(feet) : null;
            if (platform == null)
            {
                _platform = null;
                VisualOffset = Vector3.zero;
                return Vector3.zero;
            }

            Vector3 current = platform.PhysicsPosition;
            Vector3 delta = ReferenceEquals(platform, _platform) ? current - _lastPosition : Vector3.zero;
            _platform = platform;
            _lastPosition = current;
            VisualOffset = platform.VisualOffset;

            return delta.sqrMagnitude > TeleportDistance * TeleportDistance ? Vector3.zero : delta;
        }

        /// <summary>순간이동·복구 뒤 기준점을 버린다.</summary>
        public void Reset()
        {
            _platform = null;
            _cachedCollider = null;
            _cachedPlatform = null;
            VisualOffset = Vector3.zero;
        }

        private LiftPlatform FindPlatform(Vector3 feet)
        {
            Vector3 origin = feet + Vector3.up * ProbeStartHeight;
            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, ProbeDistance, _mask,
                    QueryTriggerInteraction.Ignore))
            {
                return null;
            }

            if (hit.collider != _cachedCollider)
            {
                _cachedCollider = hit.collider;
                _cachedPlatform = hit.collider.GetComponentInParent<LiftPlatform>();
            }

            return _cachedPlatform;
        }
    }
}
