using UnityEngine;

namespace GhostHunter.Gameplay.Player
{
    /// <summary>
    /// 플레이어 캐릭터 모델(두더지)의 대기·걷기 애니메이션 튜닝 값. F2 튜닝 창에 자동 노출된다.
    /// 기본값은 구현자 임시값이다 → docs/architecture/player-controller.md "캐릭터 모델·애니메이션".
    /// </summary>
    [CreateAssetMenu(
        fileName = "PlayerCharacterAnimationSettings",
        menuName = "GhostHunter/Gameplay/Player Character Animation Settings")]
    public sealed class PlayerCharacterAnimationSettings : ScriptableObject
    {
        [Header("이동 판정")]
        [Tooltip("수평 이동 속도(m/s)가 이 값을 넘으면 걷기, 아니면 대기.")]
        [SerializeField, Min(0f)] private float _moveThreshold = 0.2f;

        [Tooltip("속도 평활 시간(초). 원격 플레이어 보간의 프레임별 흔들림이 대기↔걷기를 깜빡이게 하지 않도록 거른다.")]
        [SerializeField, Min(0f)] private float _speedSmoothTime = 0.1f;

        [Tooltip("한 프레임 변위가 이 속도(m/s)를 넘으면 순간이동(스폰·텔레포트)으로 보고 속도 계산에서 뺀다.")]
        [SerializeField, Min(1f)] private float _teleportSpeed = 30f;

        [Header("걷기 재생 속도")]
        [Tooltip("걷기 클립을 1배속으로 틀 때 발이 미끄러지지 않는 이동 속도(m/s). 1.8m 두더지에 리타깃한 " +
                 "Walking.fbx 실측값: 디딤발 0.98~1.01m/s, Unity 루트 모션 평균 1.08m/s.")]
        [SerializeField, Min(0.01f)] private float _walkClipSpeed = 0.76f;

        [Tooltip("걷기 재생 배속 하한.")]
        [SerializeField, Min(0.01f)] private float _minWalkPlaybackSpeed = 0.6f;

        [Tooltip("걷기 재생 배속 상한. 걷기 5m/s·달리기 7m/s 를 발 속도에 그대로 맞추면 4.8~6.7배속이라 상한을 둔다 — 그 이상은 발이 미끄러진다.")]
        [SerializeField, Min(0.01f)] private float _maxWalkPlaybackSpeed = 2f;

        public float MoveThreshold => _moveThreshold;
        public float SpeedSmoothTime => _speedSmoothTime;
        public float TeleportSpeed => _teleportSpeed;
        public float WalkClipSpeed => _walkClipSpeed;
        public float MinWalkPlaybackSpeed => _minWalkPlaybackSpeed;
        public float MaxWalkPlaybackSpeed => _maxWalkPlaybackSpeed;

        private void OnValidate()
        {
            _moveThreshold = Mathf.Max(0f, _moveThreshold);
            _speedSmoothTime = Mathf.Max(0f, _speedSmoothTime);
            _teleportSpeed = Mathf.Max(1f, _teleportSpeed);
            _walkClipSpeed = Mathf.Max(0.01f, _walkClipSpeed);
            _minWalkPlaybackSpeed = Mathf.Max(0.01f, _minWalkPlaybackSpeed);
            _maxWalkPlaybackSpeed = Mathf.Max(_minWalkPlaybackSpeed, _maxWalkPlaybackSpeed);
        }
    }
}
