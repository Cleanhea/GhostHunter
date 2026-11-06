using UnityEngine;

namespace GhostHunter.Gameplay.Lift
{
    /// <summary>
    /// 리프트 버튼 한 개(상승 또는 하강). 버튼 판 콜라이더 위에 올라가는 작은 박스 콜라이더에 붙인다 —
    /// <c>PlayerInteractor</c>가 조준선 끝에서 이 컴포넌트를 찾고 우클릭을 넘긴다.
    /// 상태를 바꾸는 건 서버의 <see cref="LiftController"/>뿐이고, 여기서는 눌림 연출만 한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LiftButton : MonoBehaviour
    {
        [SerializeField] private LiftDirection _direction;

        [Tooltip("눌릴 때 안쪽(로컬 -Z)으로 들어가는 버튼 면. 비우면 눌림 연출이 없다.")]
        [SerializeField] private Transform _face;

        [SerializeField, Min(0f)] private float _pressDepth = 0.03f;
        [SerializeField, Min(0.02f)] private float _pressSeconds = 0.2f;

        private LiftController _lift;
        private Vector3 _faceRestPosition;
        private float _pressTimer;

        public LiftDirection Direction => _direction;

        /// <summary>
        /// 이 버튼이 속한 리프트. 승강 발판 위 버튼은 실행 시작 때 발판이 본체에서 떨어져 나오므로
        /// 계층이 아니라 컨트롤러가 <see cref="Bind"/>로 알려 준 값을 쓴다(계층 탐색은 에디터·테스트 대비 대체).
        /// </summary>
        public LiftController Lift
        {
            get
            {
                if (_lift == null)
                    _lift = GetComponentInParent<LiftController>(true);
                return _lift;
            }
        }

        internal void Bind(LiftController lift)
        {
            _lift = lift;
        }

        /// <summary>조준 중 화면에 띄울 안내 문구.</summary>
        public string Prompt
        {
            get
            {
                LiftController lift = Lift;
                if (lift == null)
                    return string.Empty;
                if (lift.IsMoving)
                    return "리프트 이동 중";
                if (!lift.CanMove(_direction))
                    return _direction == LiftDirection.Up ? "리프트가 가장 위에 있다" : "리프트가 가장 아래에 있다";
                return _direction == LiftDirection.Up ? "우클릭: 리프트 올리기" : "우클릭: 리프트 내리기";
            }
        }

        private void Awake()
        {
            if (_face != null)
                _faceRestPosition = _face.localPosition;
        }

        /// <summary>로컬 플레이어의 우클릭 입력. 서버가 거리·생존을 다시 검사한다.</summary>
        public void Press()
        {
            LiftController lift = Lift;
            if (lift != null)
                lift.RequestPress(_direction);
        }

        /// <summary>서버가 버튼 입력을 받았다고 알렸을 때 모든 피어에서 부르는 눌림 연출.</summary>
        internal void PlayPressed()
        {
            _pressTimer = _pressSeconds;
        }

        private void Update()
        {
            if (_pressTimer <= 0f || _face == null)
                return;

            _pressTimer = Mathf.Max(0f, _pressTimer - Time.deltaTime);
            float progress = 1f - _pressTimer / _pressSeconds;
            float depth = Mathf.Sin(progress * Mathf.PI) * _pressDepth;
            _face.localPosition = _faceRestPosition + Vector3.back * depth;
        }
    }
}
