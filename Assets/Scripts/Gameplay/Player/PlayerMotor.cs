using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Gameplay.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerMotor : NetworkBehaviour
    {
        private const int StandOverlapCapacity = 8;

        [SerializeField] private PlayerMoveSettings _settings;
        [SerializeField] private PlayerInputReader _input;
        [SerializeField] private Transform _cameraPivot;
        [SerializeField] private Transform _visualBody;

        private readonly NetworkVariable<bool> _isCrouching = new(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        // 엎드리기는 웅크리기와 같은 소유자 권위 패턴이다(ADR-0008 이동 예외의 연장). 서버(귀신)가
        // 침대 밑 은신 판정에 이 값을 읽으므로 Everyone 읽기로 복제한다 — IsBurrowed 와 동일.
        private readonly NetworkVariable<bool> _isProne = new(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        private readonly Collider[] _standOverlapResults = new Collider[StandOverlapCapacity];

        private CharacterController _controller;
        private float _verticalVelocity;
        private Vector3 _standingBodyPosition;
        private Vector3 _standingBodyScale;

        public bool IsCrouching => _isCrouching.Value;

        /// <summary>엎드려 있는가. 귀신의 소리 탐지와 침대 밑 은신 판정이 읽는다.</summary>
        public bool IsProne => _isProne.Value;

        /// <summary>두 자세 bool 에서 정한 현재 자세 — 엎드리기 &gt; 웅크리기 &gt; 서기.</summary>
        public PlayerStance Stance => PlayerPosture.Resolve(_isProne.Value, _isCrouching.Value);

        /// <summary>
        /// true인 동안 입력 기반 이동(<see cref="TickMovement"/>)을 완전히 건너뛴다.
        /// 굴착 스킬(<see cref="MoleBurrowController"/>)이 땅속에 있는 동안 자세만 유지하고
        /// 제자리에 얼어붙게 하는 용도 — CharacterController.Move 를 아예 호출하지 않으므로
        /// 중력도 쌓이지 않는다.
        /// </summary>
        public bool MovementLocked { get; set; }

        /// <summary>굴착 상태 프리팹(RemoteBody)을 소유자별로 재사용하기 위한 참조.</summary>
        public Transform VisualBody => _visualBody;

        /// <summary>
        /// null이 아니면 <see cref="ApplyPosture"/> 가 웅크리기 자세 대신 이 값을 카메라 목표
        /// 높이로 쓴다. 굴착 스킬이 "바닥보다 살짝 높게" 시야를 낮출 때 쓴다 — 전환은 웅크리기와
        /// 같은 <see cref="PlayerMoveSettings.PostureTransitionSpeed"/> 로 부드럽게 이어진다.
        /// </summary>
        public float? CameraHeightOverride { get; set; }

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();

            if (_visualBody != null)
            {
                _standingBodyPosition = _visualBody.localPosition;
                _standingBodyScale = _visualBody.localScale;
            }
        }

        public override void OnNetworkSpawn()
        {
            if (IsOwner)
            {
                _isCrouching.Value = false;
                _isProne.Value = false;
            }

            ApplyPosture(0f, true);
        }

        /// <summary>
        /// 물리 코드지만 FixedUpdate 가 아니라 Update 에서 돈다.
        ///
        /// <see cref="CharacterController"/>는 Rigidbody 가 아니라 즉시 반영되는 스윕 이동이라
        /// 물리 스텝에 묶을 이유가 없다. 반대로 FixedUpdate(기본 50Hz)에 두면 프레임마다
        /// 그려지는 카메라(플레이어의 자식)가 물리 스텝 단위로만 움직여서, 화면 주사율이
        /// 50Hz 의 배수가 아닐 때 프레임 드랍처럼 보이는 미세한 떨림이 생긴다.
        /// 가구 <see cref="Rigidbody"/> 물리는 그대로 FixedUpdate 에 남는다.
        /// </summary>
        private void Update()
        {
            if (!IsSpawned || _settings == null || !_controller.enabled)
                return;

            float deltaTime = Time.deltaTime;
            if (IsOwner)
            {
                if (_input == null)
                    return;

                if (!MovementLocked)
                {
                    UpdatePostureRequest();
                    TickMovement(deltaTime);
                }
            }

            ApplyPosture(deltaTime, false);
        }

        private void UpdatePostureRequest()
        {
            // 엎드리기(Z 토글)가 웅크리기보다 우선한다. 엎드린 상태에서 일어서려면 목표 자세
            // 높이만큼 머리 위 공간이 있어야 한다 — 침대 밑에서는 기어 나와야 일어설 수 있다.
            if (_input.PronePressedThisFrame)
            {
                if (_isProne.Value)
                {
                    float targetHeight = _input.CrouchHeld
                        ? _settings.CrouchHeight
                        : _settings.StandingHeight;
                    if (CanOccupyHeight(targetHeight))
                    {
                        _isProne.Value = false;
                        _isCrouching.Value = _input.CrouchHeld;
                    }
                }
                else
                {
                    _isProne.Value = true;
                    _isCrouching.Value = false;
                }

                return;
            }

            if (_isProne.Value)
                return;

            if (_input.CrouchHeld)
            {
                if (!_isCrouching.Value)
                    _isCrouching.Value = true;
                return;
            }

            if (_isCrouching.Value && CanOccupyHeight(_settings.StandingHeight))
                _isCrouching.Value = false;
        }

        private void TickMovement(float deltaTime)
        {
            bool grounded = _controller.isGrounded;
            if (grounded && _verticalVelocity < 0f)
                _verticalVelocity = -2f;

            // 엎드린 채로는 점프하지 않는다 — 입력은 소모해 일어선 직후 튀지 않게 한다.
            if (grounded && _input.ConsumeJump() && !IsProne)
                _verticalVelocity = Mathf.Sqrt(_settings.JumpHeight * -2f * _settings.Gravity);

            _verticalVelocity += _settings.Gravity * deltaTime;

            Vector2 input = Vector2.ClampMagnitude(_input.Move, 1f);
            Vector3 planar = transform.right * input.x + transform.forward * input.y;
            float control = grounded ? 1f : _settings.AirControl;
            float moveSpeed = ResolveMoveSpeed();
            Vector3 velocity = planar * (moveSpeed * control);
            velocity.y = _verticalVelocity;

            _controller.Move(velocity * deltaTime);
        }

        /// <summary>엎드리기 > 웅크리기 > 달리기 > 걷기 순으로 이동 속도를 정한다.</summary>
        private float ResolveMoveSpeed()
        {
            return PlayerPosture.MoveSpeed(Stance, _input.SprintHeld, _settings);
        }

        private void ApplyPosture(float deltaTime, bool immediate)
        {
            PlayerStance stance = Stance;
            float targetHeight = PlayerPosture.CapsuleHeight(stance, _settings);
            float targetCameraHeight = CameraHeightOverride
                ?? PlayerPosture.CameraHeight(stance, _settings);

            float height = immediate
                ? targetHeight
                : Mathf.MoveTowards(
                    _controller.height,
                    targetHeight,
                    _settings.PostureTransitionSpeed * deltaTime);
            _controller.height = height;

            Vector3 center = _controller.center;
            center.y = height * 0.5f;
            _controller.center = center;

            if (_cameraPivot != null)
            {
                Vector3 cameraPosition = _cameraPivot.localPosition;
                cameraPosition.y = immediate
                    ? targetCameraHeight
                    : Mathf.MoveTowards(
                        cameraPosition.y,
                        targetCameraHeight,
                        _settings.PostureTransitionSpeed * deltaTime);
                _cameraPivot.localPosition = cameraPosition;
            }

            if (_visualBody == null)
                return;

            float heightRatio = _settings.StandingHeight > 0f
                ? height / _settings.StandingHeight
                : 1f;
            Vector3 bodyPosition = _standingBodyPosition;
            bodyPosition.y *= heightRatio;
            _visualBody.localPosition = bodyPosition;

            Vector3 bodyScale = _standingBodyScale;
            bodyScale.y *= heightRatio;
            _visualBody.localScale = bodyScale;
        }

        /// <summary>주어진 캡슐 높이로 몸을 세울 만한 공간이 머리 위에 있는가.
        /// 엎드리기·웅크리기에서 자세를 올릴 때 천장·침대 슬랫에 막히는지 검사한다.</summary>
        private bool CanOccupyHeight(float targetHeight)
        {
            float radius = Mathf.Max(0.01f, _controller.radius);
            float halfHeight = Mathf.Max(targetHeight * 0.5f, radius);
            Vector3 localCenter = _controller.center;
            localCenter.y = halfHeight;
            Vector3 worldCenter = transform.TransformPoint(localCenter);
            Vector3 axis = transform.up * Mathf.Max(0f, halfHeight - radius);

            int count = Physics.OverlapCapsuleNonAlloc(
                worldCenter - axis,
                worldCenter + axis,
                radius,
                _standOverlapResults,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Collider hit = _standOverlapResults[i];
                if (hit == null || hit.transform == transform || hit.transform.IsChildOf(transform))
                    continue;

                return false;
            }

            return true;
        }

        /// <summary>
        /// 굴착 종료 시 수직 속도를 강제로 지정한다(§5.4 튀어오름). 이후 프레임의
        /// <see cref="TickMovement"/> 가 기존 중력 가속을 그대로 얹어 포물선을 그린다 —
        /// 점프와 같은 공식(<c>Mathf.Sqrt(height * -2 * gravity)</c>)을 재사용한다.
        /// </summary>
        public void ApplyVerticalLaunch(float verticalSpeed)
        {
            _verticalVelocity = verticalSpeed;
        }

        public void Teleport(Vector3 position, Quaternion rotation)
        {
            bool wasEnabled = _controller.enabled;
            _controller.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            _controller.enabled = wasEnabled;
            _verticalVelocity = 0f;
        }
    }
}
