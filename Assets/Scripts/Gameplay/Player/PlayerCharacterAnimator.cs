using UnityEngine;

namespace GhostHunter.Gameplay.Player
{
    /// <summary>
    /// 몸 모델의 Animator 에 이동 여부와 걷기 배속을 넘겨 대기·걷기를 고른다.
    ///
    /// <para>네트워크 상태를 따로 복제하지 않는다. 소유자는 CharacterController 가, 원격 피어는
    /// ClientNetworkTransform 보간이 매 프레임 루트를 옮기므로, 모든 피어가 자기 화면의 루트 변위만 보고
    /// 같은 결론을 낸다. 에디터에서는 로컬 몸의 Scene 뷰 애니메이션 검사를 위해
    /// <see cref="PlayerVisuals"/>가 소유자 Animator 를 항상 평가한다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerCharacterAnimator : MonoBehaviour
    {
        public const string IsMovingParameter = "IsMoving";
        public const string WalkSpeedParameter = "WalkSpeed";

        private static readonly int IsMovingId = Animator.StringToHash(IsMovingParameter);
        private static readonly int WalkSpeedId = Animator.StringToHash(WalkSpeedParameter);

        [SerializeField] private Animator _animator;
        [SerializeField] private PlayerCharacterAnimationSettings _settings;

        private Transform _root;
        private Vector3 _lastPosition;
        private bool _hasLastPosition;
        private float _planarSpeed;

        /// <summary>평활된 수평 이동 속도(m/s).</summary>
        public float PlanarSpeed => _planarSpeed;

        private void Awake()
        {
            _root = transform;
            if (_animator == null || _settings == null || _animator.runtimeAnimatorController == null)
            {
                Debug.LogError($"{nameof(PlayerCharacterAnimator)}: _animator·_settings·컨트롤러 중 미할당이 있습니다", this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            _hasLastPosition = false;
            _planarSpeed = 0f;
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            Vector3 position = _root.position;
            if (_hasLastPosition)
            {
                float measured = PlayerCharacterAnimationRules.MeasurePlanarSpeed(
                    _lastPosition, position, deltaTime, _settings.TeleportSpeed, _planarSpeed);
                _planarSpeed = PlayerCharacterAnimationRules.SmoothSpeed(
                    _planarSpeed, measured, _settings.SpeedSmoothTime, deltaTime);
            }

            _lastPosition = position;
            _hasLastPosition = true;

            // 굴착 중에는 몸(RemoteBody)이 비활성이라 Animator 도 꺼져 있다. 파라미터를 쓰면 경고가 난다.
            if (!_animator.isActiveAndEnabled)
                return;

            _animator.SetBool(IsMovingId, PlayerCharacterAnimationRules.IsMoving(_planarSpeed, _settings));
            _animator.SetFloat(WalkSpeedId,
                PlayerCharacterAnimationRules.WalkPlaybackSpeed(_planarSpeed, _settings));
        }
    }
}
