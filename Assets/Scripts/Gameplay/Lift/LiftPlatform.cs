using UnityEngine;

namespace GhostHunter.Gameplay.Lift
{
    /// <summary>
    /// 승강 발판 표지. 발판 오브젝트(키네마틱 Rigidbody + 판 콜라이더)에 붙인다.
    /// 플레이어는 소유자 권위로 움직이는 CharacterController라 발판이 알아서 싣고 가지 않는다 —
    /// <c>PlayerMotor</c>가 발밑에서 이 컴포넌트를 찾아 발판이 움직인 만큼 같이 옮긴다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LiftPlatform : MonoBehaviour
    {
        private Rigidbody _body;

        /// <summary>
        /// 발판의 <b>물리 자세</b> — 콜라이더가 실제로 있는 곳. 발판 몸체는 물리 스텝(50Hz)마다만 뛰고,
        /// <c>transform.position</c> 은 그 사이를 보간한 화면 위치다. <c>CharacterController</c> 탑승자는 콜라이더를
        /// 밟고 서므로 싣고 가는 변위는 이쪽으로 재고, 화면과의 차이는 <see cref="VisualOffset"/> 으로 상쇄한다.
        /// </summary>
        public Vector3 PhysicsPosition
        {
            get
            {
                if (_body == null)
                    _body = GetComponent<Rigidbody>();
                return _body != null ? _body.position : transform.position;
            }
        }

        /// <summary>화면(보간) 위치 − 물리 자세. 정지 중이면 0, 올라가는 중이면 아래쪽으로 스텝 이동량 이내다.</summary>
        public Vector3 VisualOffset => transform.position - PhysicsPosition;
    }
}
