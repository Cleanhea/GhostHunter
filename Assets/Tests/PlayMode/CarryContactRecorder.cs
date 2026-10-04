using UnityEngine;

namespace GhostHunter.Tests.PlayMode
{
    /// <summary>
    /// 테스트 전용 — <c>FurnitureHoverMotor</c>와 같은 기준(접촉 법선이 수평에 가까운가)으로 옆으로 막는 접촉을 기록한다.
    /// 물리 스텝마다 <see cref="Consume"/>로 읽고 지운다.
    /// </summary>
    public sealed class CarryContactRecorder : MonoBehaviour
    {
        private const float WallNormalLimit = 0.7f;

        private bool _touchingWall;

        public bool Consume()
        {
            bool touching = _touchingWall;
            _touchingWall = false;
            return touching;
        }

        private void OnCollisionEnter(Collision collision) => Note(collision);

        private void OnCollisionStay(Collision collision) => Note(collision);

        private void Note(Collision collision)
        {
            for (int i = 0; i < collision.contactCount; i++)
            {
                if (Mathf.Abs(collision.GetContact(i).normal.y) < WallNormalLimit)
                {
                    _touchingWall = true;
                    return;
                }
            }
        }
    }
}
