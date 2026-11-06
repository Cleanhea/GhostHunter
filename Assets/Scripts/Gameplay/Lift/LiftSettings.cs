using UnityEngine;

namespace GhostHunter.Gameplay.Lift
{
    /// <summary>
    /// 리프트 튜닝값 — 정지 포인트(층 높이)·이동 속력·버튼 거리·막힘 판정.
    /// 수치는 전부 [TEMP]이며 기획서(lift-system.md)가 확정하기 전까지 구현자가 둔 임시값이다.
    /// 층 높이가 다른 맵은 이 에셋을 복제해 `LiftSettings_Tutorial`처럼 따로 둔다 — 한 종류 = 한 프리팹이라 치수를 인스턴스에서 바꾸지 않는다.
    /// </summary>
    [CreateAssetMenu(
        fileName = "LiftSettings_Default",
        menuName = "GhostHunter/Gameplay/Lift Settings")]
    public sealed class LiftSettings : ScriptableObject
    {
        [Header("정지 포인트")]
        [Tooltip("건물 한 층의 높이(m). 승강 발판은 맨 아래에서 0, 1×, 2×… 높이마다 멈춘다. 프로토타입 기본은 최대 3m(기획서 §3).")]
        [SerializeField, Min(0.5f)] private float _floorHeight = 3f;

        [Tooltip("정지 포인트 개수(맨 아래 포함). 2면 맨 아래와 한 층 위.")]
        [SerializeField, Range(2, 6)] private int _stopCount = 2;

        [Header("이동")]
        [Tooltip("승강 발판 최대 속력(m/s).")]
        [SerializeField, Min(0.1f)] private float _maxSpeed = 1.5f;

        [Tooltip("출발·정지 가속도(m/s²). 정지 포인트에 부드럽게 닿게 한다.")]
        [SerializeField, Min(0.1f)] private float _acceleration = 2f;

        [Tooltip("클라이언트가 복제된 높이를 따라가는 평활 시간(초). 0이면 받은 값을 그대로 쓴다.")]
        [SerializeField, Min(0f)] private float _clientSmoothTime = 0.08f;

        [Header("버튼")]
        [Tooltip("서버가 다시 검사하는 버튼 사용 거리(m) — 플레이어와 같은 방향 버튼 사이. 조준 거리보다 넉넉해야 한다.")]
        [SerializeField, Min(0.5f)] private float _pressMaxDistance = 3.5f;

        [Header("하부 본체")]
        [Tooltip("하부 본체(바퀴)와 지면 사이 마찰 계수. 낮을수록 밀었을 때 잘 굴러간다. 결합 방식은 최소값이라 지면 마찰을 이 값 아래로 줄인다.")]
        [SerializeField, Range(0f, 1f)] private float _wheelFriction = 0.1f;

        [Header("막힘 판정")]
        [Tooltip("이동 방향 경로를 미리 훑을 때 발판 옆면을 안쪽으로 줄이는 여유(m). 옆 벽을 스치는 것을 막힘으로 오인하지 않게 한다.")]
        [SerializeField, Range(0f, 0.2f)] private float _blockProbeSkin = 0.04f;

        public float FloorHeight => _floorHeight;
        public int StopCount => _stopCount;
        public float MaxSpeed => _maxSpeed;
        public float Acceleration => _acceleration;
        public float ClientSmoothTime => _clientSmoothTime;
        public float PressMaxDistance => _pressMaxDistance;
        public float BlockProbeSkin => _blockProbeSkin;
        public float WheelFriction => _wheelFriction;

        /// <summary>맨 아래에서 가장 위 정지 포인트까지 오르는 거리(m).</summary>
        public float MaxHeight => (_stopCount - 1) * _floorHeight;
    }
}
