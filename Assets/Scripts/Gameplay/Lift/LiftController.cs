using System.Collections.Generic;
using GhostHunter.Core;
using GhostHunter.Gameplay.Sanity;
using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Gameplay.Lift
{
    /// <summary>
    /// 리프트의 승강 발판 — 서버가 버튼 입력을 받아 정지 포인트로 올리고 내리며, 복제하는 것은 높이 하나와 이동 중 여부뿐이다
    /// (<c>docs/architecture/lift-system.md</c>).
    /// <list type="bullet">
    /// <item>버튼(<see cref="LiftButton"/>)은 우클릭으로 누르고 서버가 사용 거리·생존을 다시 검사한다. 이동 중 입력은 무시한다.</item>
    /// <item>한 번 누르면 입력 방향의 가장 가까운 정지 포인트로 간다(<see cref="LiftStopRules"/>).</item>
    /// <item>이동 경로를 매 프레임 훑어 막히면 그 자리에서 멈춘다. 다음 입력은 같은 규칙으로 가장 가까운 정지 포인트로 간다.</item>
    /// <item>클라이언트는 복제된 높이를 평활해 따라간다.</item>
    /// </list>
    /// <para>
    /// <b>발판은 실행 시작 때 본체에서 떼어낸다.</b> 발판은 키네마틱 Rigidbody 라 <c>MovePosition</c> 으로 움직여야 PhysX 가 속도를 알아
    /// 위에 얹힌 짐을 같이 싣고 간다(트랜스폼만 옮기면 순간이동으로 처리되어 짐이 떨어진다). 본체(동적 Rigidbody)의 자식으로
    /// 두면 본체가 밀릴 때 계층이 발판 트랜스폼을 끌고 가서 <c>MovePosition</c> 과 싸우므로, 물리 스텝마다 본체 자세와 높이로 목표를 계산한다.
    /// 프리팹·씬에서는 발판이 본체의 자식으로 저장되어 있다.
    /// </para>
    /// 본체의 회전은 고정이라 승강 방향은 본체 위쪽(+Y)이다. 본체 이동·던지기는 <c>FurnitureGrabTarget</c> 파이프라인을 그대로 쓴다.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class LiftController : NetworkBehaviour
    {
        // 발판에 얹힌 짐(동적 강체)이 닿아 있다고 보는 거리 — 이 안의 동적 강체는 막힘으로 치지 않고 같이 올라간다.
        private const float RestingContactDistance = 0.06f;
        private const int HitCapacity = 32;

        [SerializeField] private LiftSettings _settings;

        [Tooltip("승강 발판 루트(키네마틱 Rigidbody). 프리팹에 저장된 로컬 위치가 맨 아래 정지 포인트다.")]
        [SerializeField] private Transform _platform;

        [Tooltip("막힘 판정에 쓰는 발판 판 콜라이더.")]
        [SerializeField] private BoxCollider _deck;

        [Tooltip("버튼 전부(승강 발판 위 + 본체). 서버가 사용 거리를 이 위치로 검사한다.")]
        [SerializeField] private LiftButton[] _buttons;

        [Tooltip("지면에 닿는 하부 본체 콜라이더. 실행 때 바퀴 마찰(LiftSettings.WheelFriction)을 입힌다.")]
        [SerializeField] private Collider[] _wheelColliders;

        private readonly NetworkVariable<float> _height = new(0f);
        private readonly NetworkVariable<bool> _moving = new(false);
        private readonly RaycastHit[] _hits = new RaycastHit[HitCapacity];
        private readonly HashSet<Collider> _ownColliders = new();

        private Rigidbody _platformBody;
        private PhysicsMaterial _wheelMaterial;
        private Vector3 _platformRestLocalPosition;
        private Quaternion _platformRestLocalRotation;
        private float _displayHeight;
        private float _smoothVelocity;
        private float _target;
        private float _velocity;
        private int _blockMask;

        public bool IsMoving => _moving.Value;

        /// <summary>맨 아래 정지 포인트에서 올라간 지금 높이(m). 클라이언트는 평활된 표시 값이다.</summary>
        public float Height => _displayHeight;

        public LiftSettings Settings => _settings;
        public Transform Platform => _platform;

        private void Awake()
        {
            int playerMask = GameLayers.Player >= 0 ? 1 << GameLayers.Player : 0;
            int ghostMask = GameLayers.GhostPrototype >= 0 ? 1 << GameLayers.GhostPrototype : 0;
            _blockMask = Physics.DefaultRaycastLayers & ~playerMask & ~ghostMask;

            if (_buttons != null)
            {
                foreach (LiftButton button in _buttons)
                {
                    if (button != null)
                        button.Bind(this);
                }
            }

            ApplyWheelFriction();

            if (_platform == null)
                return;

            _platformBody = _platform.GetComponent<Rigidbody>();
            if (_platformBody != null)
            {
                // 고정 간격으로 MovePosition 하는 키네마틱 몸체의 화면 트랜스폼은 보간으로 매끈하게 만든다.
                // 프리팹 값에 기대지 않고 코드로 못 박는다 — 탑승자 보정(PlatformCarry.VisualOffset)이 이 보간을 전제로 한다.
                _platformBody.interpolation = RigidbodyInterpolation.Interpolate;
            }

            _platformRestLocalPosition = _platform.localPosition;
            _platformRestLocalRotation = _platform.localRotation;

            // 본체와 발판은 서로 통과한다 — 키네마틱 발판이 본체 기둥·섀시를 밀면 본체가 튄다. 발판을 떼기 전에 짝을 모은다.
            Collider[] bodyColliders = GetComponentsInChildren<Collider>(true);
            Collider[] deckColliders = _platform.GetComponentsInChildren<Collider>(true);
            foreach (Collider body in bodyColliders)
            {
                bool onDeck = body.transform.IsChildOf(_platform);
                _ownColliders.Add(body);
                if (onDeck)
                    continue;

                foreach (Collider deck in deckColliders)
                    Physics.IgnoreCollision(body, deck, true);
            }

            _platform.SetParent(null, true);
        }

        public override void OnDestroy()
        {
            // 떼어낸 발판은 본체와 수명을 같이 한다(씬 언로드는 어차피 같이 지운다).
            if (_platform != null && _platform.parent == null)
                Destroy(_platform.gameObject);
            if (_wheelMaterial != null)
                Destroy(_wheelMaterial);
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            // 늦게 접속한 클라이언트가 올라가는 연출부터 보지 않도록 복제된 높이로 스냅한다.
            _displayHeight = _height.Value;
            _smoothVelocity = 0f;
            SnapPlatform();
        }

        public override void OnNetworkDespawn()
        {
            _velocity = 0f;
        }

        /// <summary>
        /// 높이 계산과 발판 이동 전부를 <b>고정 간격</b>에서 한다. 높이를 프레임마다(<c>Update</c>) 갱신하고 물리 스텝에서
        /// 그 값을 읽으면 스텝마다 읽는 높이 증가량이 프레임 길이만큼 들쭉날쭉해(60fps 에서 21/40/27mm 식) 보간된
        /// 발판이 일정하게 올라가지 않았다. 고정 간격으로 같은 양씩 올리면 키네마틱 보간이 매끈한 움직임을 만든다.
        /// 발판 위 짐은 <c>MovePosition</c> 으로 PhysX 가 속도를 알아 같이 실려 간다.
        /// </summary>
        private void FixedUpdate()
        {
            // 세션 밖(플레이 직후, 호스트 시작 전)에서는 씬에 저장된 위치를 그대로 둔다.
            if (!IsSpawned || _platform == null || _settings == null)
                return;

            float deltaTime = Time.fixedDeltaTime;
            if (IsServer)
            {
                ServerTick(deltaTime);
            }
            else
            {
                _displayHeight = Mathf.SmoothDamp(
                    _displayHeight, _height.Value, ref _smoothVelocity, _settings.ClientSmoothTime,
                    Mathf.Infinity, deltaTime);
            }

            if (_platformBody == null)
                return;

            PlatformPose(_displayHeight, out Vector3 position, out Quaternion rotation);
            _platformBody.MovePosition(position);
            _platformBody.MoveRotation(rotation);
        }

        /// <summary>이 방향 버튼이 지금 발판을 움직일 수 있는가(이동 중이 아니고 그 방향 끝이 아님). 안내 문구에 쓴다.</summary>
        public bool CanMove(LiftDirection direction)
        {
            return _settings != null && !_moving.Value && LiftStopRules.TryFindTarget(
                _displayHeight, direction, _settings.FloorHeight, _settings.StopCount, out _);
        }

        /// <summary>로컬 플레이어의 버튼 입력. 상태를 바꾸는 건 서버뿐이다.</summary>
        public void RequestPress(LiftDirection direction)
        {
            if (!IsSpawned)
                return;

            RequestPressRpc(direction);
        }

        /// <summary>
        /// 버튼은 아무도 소유하지 않으므로 소유권 대신 거리로 검증한다. 사망한 플레이어의 요청은 거절한다
        /// (문과 같은 규칙 — 관전 기획서 SP-2).
        /// </summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestPressRpc(LiftDirection direction, RpcParams rpcParams = default)
        {
            if (!ServerCanPress(rpcParams.Receive.SenderClientId, direction))
                return;

            bool accepted = ServerPress(direction);
            NotifyPressedRpc(direction, accepted);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void NotifyPressedRpc(LiftDirection direction, bool accepted)
        {
            if (_buttons == null)
                return;

            foreach (LiftButton button in _buttons)
            {
                if (button != null && button.Direction == direction)
                    button.PlayPressed();
            }
        }

        /// <summary>
        /// 서버 전용 — 입력 방향의 가장 가까운 정지 포인트로 출발한다. 이동 중이거나 그 방향 끝이면 무시(false).
        /// 사용 거리·생존 검증은 <see cref="RequestPressRpc"/>가 하고 이 메서드는 하지 않는다.
        /// </summary>
        public bool ServerPress(LiftDirection direction)
        {
            if (!IsServer || !IsSpawned || _settings == null || _moving.Value)
                return false;

            if (!LiftStopRules.TryFindTarget(
                    _displayHeight, direction, _settings.FloorHeight, _settings.StopCount, out float target))
            {
                return false;
            }

            _target = target;
            _velocity = 0f;
            _moving.Value = true;
            return true;
        }

        private void ServerTick(float deltaTime)
        {
            if (!_moving.Value)
                return;

            float move = LiftMotion.Step(
                _displayHeight, _target, ref _velocity,
                _settings.MaxSpeed, _settings.Acceleration, deltaTime, out bool arrived);

            if (move != 0f)
            {
                float allowed = AllowedMove(move);
                if (Mathf.Abs(allowed) < Mathf.Abs(move) - 1e-5f)
                {
                    // 막힌 자리에서 멈추고 끝낸다 — 정지 포인트가 아니므로 다음 입력은 가장 가까운 정지 포인트로 간다.
                    _displayHeight += allowed;
                    _height.Value = _displayHeight;
                    StopMoving();
                    return;
                }

                _displayHeight += move;
            }

            if (arrived)
            {
                _displayHeight = _target;
                StopMoving();
            }

            _height.Value = _displayHeight;
        }

        private void StopMoving()
        {
            _velocity = 0f;
            _moving.Value = false;
        }

        /// <summary>
        /// 이번 프레임 이동량 중 막히지 않고 갈 수 있는 만큼(부호 유지). 발판 판 모양을 이동 방향으로 훑어
        /// 본체 밖의 충돌체에 닿으면 그 앞에서 멈춘다. 발판에 얹힌 짐·플레이어는 같이 움직이므로 막힘이 아니다.
        /// 물리 몸체가 아니라 <b>논리 자세</b>(높이로 계산한 위치)에서 훑는다 — 몸체는 물리 스텝만큼 늦게 따라오기 때문이다.
        /// </summary>
        private float AllowedMove(float move)
        {
            if (_deck == null)
                return move;

            float distance = Mathf.Abs(move);
            PlatformPose(_displayHeight, out Vector3 platformPosition, out Quaternion platformRotation);
            Vector3 direction = move > 0f ? transform.up : -transform.up;

            Vector3 scale = _deck.transform.lossyScale;
            Vector3 half = Vector3.Scale(
                _deck.size * 0.5f, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            float skin = _settings.BlockProbeSkin;
            // 옆면만 줄인다. 이동 축은 그대로여야 발판 윗면에 닿은 짐이 막힘으로 잡히지 않는다.
            half.x = Mathf.Max(0.01f, half.x - skin);
            half.z = Mathf.Max(0.01f, half.z - skin);

            Vector3 center = platformPosition + platformRotation * _deck.center;
            int count = Physics.BoxCastNonAlloc(
                center, half, direction, _hits, platformRotation,
                distance + skin, _blockMask, QueryTriggerInteraction.Ignore);

            // 얹힌 짐은 발판과 한 틱만큼 어긋날 수 있다 — 급감속에서는 관성으로 떠오르고, 물리 몸체는 목표를 한 스텝 늦게 따라온다.
            // 그래서 닿아 있다고 보는 거리를 이번 틱 이동량만큼 넓힌다(고정 간격에서는 논리·물리 자세가 맞물려 이 어긋남이 그대로 드러난다).
            float restingReach = RestingContactDistance + distance;
            float limit = distance;
            for (int i = 0; i < count; i++)
            {
                Collider other = _hits[i].collider;
                if (other == null || _ownColliders.Contains(other))
                    continue;

                Rigidbody body = other.attachedRigidbody;
                if (body != null && !body.isKinematic && _hits[i].distance <= restingReach)
                    continue;

                limit = Mathf.Min(limit, Mathf.Max(0f, _hits[i].distance - skin));
            }

            return move > 0f ? limit : -limit;
        }

        private bool ServerCanPress(ulong senderClientId, LiftDirection direction)
        {
            if (_settings == null
                || NetworkManager == null
                || !NetworkManager.ConnectedClients.TryGetValue(senderClientId, out NetworkClient client)
                || client.PlayerObject == null)
            {
                return false;
            }

            NetworkObject playerObject = client.PlayerObject;
            SanityNetworkState sanity = playerObject.GetComponent<SanityNetworkState>();
            if (sanity != null && !sanity.HasSanity)
                return false;

            // 눈높이쯤에서 같은 방향 버튼까지의 거리 — 발판 위(높은 곳)와 바닥 어느 쪽에서든 가까운 버튼 하나면 된다.
            Vector3 eye = playerObject.transform.position + Vector3.up * 1.2f;
            float maxSqr = _settings.PressMaxDistance * _settings.PressMaxDistance;
            if (_buttons == null)
                return false;

            foreach (LiftButton button in _buttons)
            {
                if (button != null && button.Direction == direction
                    && (button.transform.position - eye).sqrMagnitude <= maxSqr)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 바퀴 달린 하부 본체는 잘 굴러가야 한다 — 지면 마찰을 낮춘 재질을 입힌다(결합 최소값). 에셋 대신 코드로 만들어
        /// 수치를 <see cref="LiftSettings"/>에서 조정한다. 발판 위 짐은 발판(별도 몸체)에 얹히므로 영향이 없다.
        /// </summary>
        private void ApplyWheelFriction()
        {
            if (_settings == null || _wheelColliders == null || _wheelColliders.Length == 0)
                return;

            _wheelMaterial = new PhysicsMaterial("Lift_Wheels")
            {
                dynamicFriction = _settings.WheelFriction,
                staticFriction = _settings.WheelFriction,
                bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounceCombine = PhysicsMaterialCombine.Minimum,
            };
            foreach (Collider wheel in _wheelColliders)
            {
                if (wheel != null)
                    wheel.sharedMaterial = _wheelMaterial;
            }
        }

        /// <summary>본체 자세 + 높이로 정한 발판의 월드 자세.</summary>
        private void PlatformPose(float height, out Vector3 position, out Quaternion rotation)
        {
            Quaternion bodyRotation = transform.rotation;
            position = transform.position + bodyRotation * (_platformRestLocalPosition + Vector3.up * height);
            rotation = bodyRotation * _platformRestLocalRotation;
        }

        /// <summary>발판을 현재 높이의 자세로 즉시 옮긴다(스폰·늦은 접속). 물리 보간 없이 한 번에.</summary>
        private void SnapPlatform()
        {
            if (_platform == null)
                return;

            PlatformPose(_displayHeight, out Vector3 position, out Quaternion rotation);
            _platform.SetPositionAndRotation(position, rotation);
            if (_platformBody != null)
            {
                _platformBody.position = position;
                _platformBody.rotation = rotation;
            }
        }
    }
}
