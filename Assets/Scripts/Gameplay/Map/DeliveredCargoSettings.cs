using GhostHunter.Gameplay.FurnitureDriver;
using UnityEngine;

namespace GhostHunter.Gameplay.Map
{
    /// <summary>
    /// 드릴카 화물칸에 쌓이는 반출 짐(<see cref="DeliveredCargoPile"/>)의 크기·모양·낙하 연출 수치.
    /// 모양과 재질은 분해 가구의 짐더미(<see cref="FurnitureBundleSettings"/>)를 그대로 빌려 같은 계열로 보이게 한다.
    /// </summary>
    [CreateAssetMenu(fileName = "DeliveredCargoSettings_", menuName = "GhostHunter/Map/Delivered Cargo Settings")]
    public sealed class DeliveredCargoSettings : ScriptableObject
    {
        [Header("모양")]
        [Tooltip("짐 모양(둥근 포대 + 끈)과 재질을 빌려 올 짐더미 설정.")]
        [SerializeField] private FurnitureBundleSettings _bundle;
        [Tooltip("짐 크기 후보(m). 반출 순서대로 돌아가며 쓴다. 한 칸(구역 너비 ÷ 열 수)보다 작아야 겹치지 않는다.")]
        [SerializeField] private Vector3[] _crateSizes =
        {
            new(0.75f, 0.85f, 0.65f),
            new(0.7f, 1.0f, 0.6f),
            new(0.72f, 0.8f, 0.62f),
            new(0.7f, 0.95f, 0.65f),
            new(0.75f, 0.9f, 0.6f),
        };

        [Header("배치")]
        [Tooltip("같은 값이면 모든 접속자가 같은 쌓임새를 본다.")]
        [SerializeField] private int _seed = 20261029;
        [Tooltip("짐을 살짝 비스듬히 놓는 최대 각도(도).")]
        [SerializeField, Range(0f, 20f)] private float _yawJitterDegrees = 6f;
        [Tooltip("짐이 칸 가운데에서 벗어나는 최대 거리(m).")]
        [SerializeField, Min(0f)] private float _offsetJitter = 0.06f;

        [Header("연출")]
        [Tooltip("새 짐이 떨어져 내려오는 높이(m).")]
        [SerializeField, Min(0f)] private float _dropHeight = 0.6f;
        [Tooltip("떨어져 자리 잡기까지 걸리는 시간(초).")]
        [SerializeField, Min(0.05f)] private float _dropSeconds = 0.35f;
        [Tooltip("연달아 여러 개가 늘어날 때 짐 사이의 시간차(초).")]
        [SerializeField, Min(0f)] private float _dropStaggerSeconds = 0.18f;

        public FurnitureBundleSettings Bundle => _bundle;
        public Vector3[] CrateSizes => _crateSizes;
        public int Seed => _seed;
        public float YawJitterDegrees => _yawJitterDegrees;
        public float OffsetJitter => _offsetJitter;
        public float DropHeight => _dropHeight;
        public float DropSeconds => _dropSeconds;
        public float DropStaggerSeconds => _dropStaggerSeconds;
    }
}
