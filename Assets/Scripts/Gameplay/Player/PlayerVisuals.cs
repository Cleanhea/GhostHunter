using System.Collections.Generic;
using GhostHunter.Core;
using GhostHunter.Core.Scenes;
using GhostHunter.Gameplay.Sanity;
using GhostHunter.Gameplay.Recovery;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

namespace GhostHunter.Gameplay.Player
{
    /// <summary>생존 몸체와 사망 위치에 남는 서버 물리 시체를 표시한다.</summary>
    [DisallowMultipleComponent]
    public sealed class PlayerVisuals : NetworkBehaviour
    {
        private const int MaxTrackedPlayers = 8;
        private static readonly Dictionary<Collider, PlayerVisuals> CorpseOwners = new();
        [SerializeField] private Renderer[] _bodyRenderers;

        [Tooltip("시체로 복제할 몸 모델 루트(발밑 피벗). 스킨 메시는 렌더러만 복제하면 본이 따라오지 않으므로 " +
                 "본까지 포함한 루트를 복제한다. 비워 두면 첫 몸 렌더러 오브젝트(중심 피벗)를 복제한다.")]
        [SerializeField] private Transform _corpseModel;

        private readonly NetworkVariable<Vector3> _corpsePosition = new(
            Vector3.zero, NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<Quaternion> _corpseRotation = new(
            Quaternion.identity, NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private SanityNetworkState _sanity;
        private SpectatorSettings _settings;
        private CharacterController _controller;
        private Camera _ownerCamera;
        private bool _cameraRenderSubscribed;
#if UNITY_EDITOR
        private Animator _ownerAnimator;
        private AnimatorCullingMode _ownerAnimatorCullingMode;
#endif
        private GameObject _corpse;
        private Collider _corpseCollider;
        private Rigidbody _corpseBody;
        private ISanityTeamService _teamService;
        private ISceneFlow _sceneFlow;
        private readonly SanityNetworkState[] _witnessCandidates = new SanityNetworkState[MaxTrackedPlayers];
        private float _nextWitnessCheck;
        private ulong _corpseGeneration;
        private ulong _corpseWitnessId;
        private readonly Dictionary<ulong, Vector3> _carrierTargets = new(4);
        private Vector3 _corpseLiftStart;
        private float _corpseLiftRemaining;
        private Vector3 _pendingPushImpulse;
        private Vector3 _pendingPushPoint;

        public bool HasCorpse => _corpse != null;
        public Vector3 CorpsePosition => _corpse != null ? _corpse.transform.position : transform.position;
        public float CarryHoldSeconds => _settings != null ? _settings.CorpseCarryHoldSeconds : 0.3f;
        public float CarryDistance => _settings != null ? _settings.CorpseCarryDistance : 2f;

        public void CaptureStageState(ref StageRecoverySnapshot.PlayerState snapshot)
        {
            snapshot.CorpsePosition = _corpse != null ? _corpse.transform.position : _corpsePosition.Value;
            snapshot.CorpseRotation = _corpse != null ? _corpse.transform.rotation : _corpseRotation.Value;
            snapshot.CorpseVelocity = _corpseBody != null ? _corpseBody.linearVelocity : Vector3.zero;
            snapshot.CorpseAngularVelocity = _corpseBody != null ? _corpseBody.angularVelocity : Vector3.zero;
            snapshot.CorpseWitnessId = _corpseWitnessId;
        }

        public void ServerRestoreStageState(StageRecoverySnapshot.PlayerState snapshot)
        {
            if (!IsServer || !IsSpawned)
                return;
            _corpsePosition.Value = snapshot.CorpsePosition;
            _corpseRotation.Value = snapshot.CorpseRotation;
            if (snapshot.Alive)
            {
                DestroyCorpse();
                return;
            }
            if (_corpse == null)
                CreateCorpse();
            if (_corpse == null)
                return;
            _corpseWitnessId = snapshot.CorpseWitnessId;
            _corpse.transform.SetPositionAndRotation(snapshot.CorpsePosition, snapshot.CorpseRotation);
            _corpseLiftRemaining = 0f;
            _corpseBody.isKinematic = false;
            _corpseBody.position = snapshot.CorpsePosition;
            _corpseBody.rotation = snapshot.CorpseRotation;
            _corpseBody.linearVelocity = snapshot.CorpseVelocity;
            _corpseBody.angularVelocity = snapshot.CorpseAngularVelocity;
        }

        public static bool TryGetCorpseOwner(Collider collider, out PlayerVisuals owner)
        {
            return CorpseOwners.TryGetValue(collider, out owner) && owner != null && owner.HasCorpse;
        }

        public override void OnNetworkSpawn()
        {
            _sanity = GetComponent<SanityNetworkState>();
            _settings = GetComponent<SpectatorController>()?.Settings;
            _controller = GetComponent<CharacterController>();
            Services.TryGet(out _teamService);
            Services.TryGet(out _sceneFlow);

            if (_sanity == null || _settings == null || _bodyRenderers == null
                || _bodyRenderers.Length == 0 || _bodyRenderers[0] == null)
            {
                Debug.LogError($"{nameof(PlayerVisuals)}: 시체 생성에 필요한 참조가 없습니다.", this);
                enabled = false;
                return;
            }

            _sanity.AliveStateChanged += HandleAliveChanged;
            if (_sceneFlow != null)
                _sceneFlow.SceneChanged += HandleSceneChanged;

            if (IsOwner)
            {
                PlayerLook look = GetComponent<PlayerLook>();
                _ownerCamera = look != null ? look.PlayerCamera : null;
                if (_ownerCamera == null)
                    Debug.LogError($"{nameof(PlayerVisuals)}: 로컬 몸을 가릴 PlayerCamera가 없습니다.", this);
                else
                {
                    RenderPipelineManager.beginCameraRendering += HandleBeginCameraRendering;
                    RenderPipelineManager.endCameraRendering += HandleEndCameraRendering;
                    _cameraRenderSubscribed = true;
                }

#if UNITY_EDITOR
                // Scene 뷰에서는 자기 몸의 애니메이션을 검사할 수 있어야 한다.
                if (_corpseModel != null)
                {
                    _ownerAnimator = _corpseModel.GetComponent<Animator>();
                    if (_ownerAnimator != null)
                    {
                        _ownerAnimatorCullingMode = _ownerAnimator.cullingMode;
                        _ownerAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    }
                }
#endif
            }

            ApplyAliveState(_sanity.HasSanity);
        }

        public override void OnNetworkDespawn()
        {
            if (_cameraRenderSubscribed)
            {
                RenderPipelineManager.beginCameraRendering -= HandleBeginCameraRendering;
                RenderPipelineManager.endCameraRendering -= HandleEndCameraRendering;
                RestoreOwnerBodyRendering();
                _cameraRenderSubscribed = false;
                _ownerCamera = null;
            }
#if UNITY_EDITOR
            if (_ownerAnimator != null)
            {
                _ownerAnimator.cullingMode = _ownerAnimatorCullingMode;
                _ownerAnimator = null;
            }
#endif

            if (_sanity != null)
                _sanity.AliveStateChanged -= HandleAliveChanged;
            if (_sceneFlow != null)
                _sceneFlow.SceneChanged -= HandleSceneChanged;
            _sanity = null;
            _teamService = null;
            _sceneFlow = null;
            DestroyCorpse();
        }

        private void HandleSceneChanged(SceneId scene)
        {
            if (!scene.IsStage())
                DestroyCorpse();
        }

        private void HandleAliveChanged(bool alive)
        {
            ApplyAliveState(alive);
        }

        private void ApplyAliveState(bool alive)
        {
            if (_controller != null)
                _controller.enabled = alive;

            bool showBody = alive && (!IsOwner || _cameraRenderSubscribed);
            for (int i = 0; i < _bodyRenderers.Length; i++)
            {
                if (_bodyRenderers[i] != null)
                    _bodyRenderers[i].enabled = showBody;
            }

            if (alive)
                DestroyCorpse();
            else
                CreateCorpse();
        }

        private void HandleBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera != _ownerCamera)
                return;

            for (int i = 0; i < _bodyRenderers.Length; i++)
            {
                if (_bodyRenderers[i] != null)
                    _bodyRenderers[i].forceRenderingOff = true;
            }
        }

        private void HandleEndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera == _ownerCamera)
                RestoreOwnerBodyRendering();
        }

        private void RestoreOwnerBodyRendering()
        {
            for (int i = 0; i < _bodyRenderers.Length; i++)
            {
                if (_bodyRenderers[i] != null)
                    _bodyRenderers[i].forceRenderingOff = false;
            }
        }

        private void CreateCorpse()
        {
            if (_corpse != null)
                return;

            Vector3 position = transform.position + Vector3.up * 0.45f;
            Quaternion rotation = transform.rotation * Quaternion.Euler(90f, 0f, 0f);
            if (IsServer)
            {
                _corpseWitnessId = 0xC000000000000000UL
                    | (NetworkObjectId << 16) | (++_corpseGeneration & 0xFFFFUL);
                _corpsePosition.Value = position;
                _corpseRotation.Value = rotation;
            }
            else if (_corpsePosition.Value != Vector3.zero)
            {
                position = _corpsePosition.Value;
                rotation = _corpseRotation.Value;
            }

            // 루트는 캡슐 중심에 두고(물리·운반·목격 판정이 이 점을 쓴다) 몸 모양은 자식으로 복제한다.
            _corpse = new GameObject($"Corpse_{OwnerClientId}");
            _corpse.transform.SetPositionAndRotation(position, rotation);
            CreateCorpseVisual(_corpse.transform);

            CapsuleCollider collider = _corpse.AddComponent<CapsuleCollider>();
            collider.height = _settings.CorpseHeight;
            collider.radius = _settings.CorpseRadius;
            _corpseCollider = collider;
            CorpseOwners.Add(collider, this);

            _corpseBody = _corpse.AddComponent<Rigidbody>();
            _corpseBody.mass = _settings.CorpseMass;
            _corpseLiftStart = position;
            _corpseLiftRemaining = IsServer ? _settings.DeathSequenceSeconds * 0.5f : 0f;
            _corpseBody.isKinematic = !IsServer || _corpseLiftRemaining > 0f;
        }

        private void CreateCorpseVisual(Transform corpseRoot)
        {
            bool hasModel = _corpseModel != null;
            GameObject source = hasModel ? _corpseModel.gameObject : _bodyRenderers[0].gameObject;
            GameObject visual = Instantiate(source, corpseRoot);
            visual.name = source.name;
            visual.SetActive(true);

            // 모델은 발밑 피벗이라 캡슐 중심에서 절반 높이만큼 내린다. 예전 캡슐 몸은 중심 피벗이다.
            visual.transform.SetLocalPositionAndRotation(
                hasModel ? Vector3.down * (_settings.CorpseHeight * 0.5f) : Vector3.zero,
                Quaternion.identity);

            Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
                renderers[i].enabled = true;

            // 소유자 쪽 몸은 렌더러가 꺼져 Animator 가 한 번도 평가되지 않았을 수 있다. 모든 피어가 같은
            // 자세로 눕도록 기본 상태 첫 프레임으로 되감아 평가한 뒤 멈춘다.
            Animator animator = visual.GetComponentInChildren<Animator>();
            if (animator != null)
            {
                if (animator.runtimeAnimatorController != null)
                {
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    animator.Rebind();
                    animator.Update(0f);
                }

                animator.enabled = false;
            }
        }

        private void DestroyCorpse()
        {
            _carrierTargets.Clear();
            if (!ReferenceEquals(_corpseCollider, null))
                CorpseOwners.Remove(_corpseCollider);
            _corpseCollider = null;
            if (_corpse != null)
                Destroy(_corpse);
            _corpse = null;
            _corpseBody = null;
            _corpseLiftRemaining = 0f;
            _pendingPushImpulse = Vector3.zero;
        }

        private void FixedUpdate()
        {
            if (!IsSpawned || !IsServer || _corpseBody == null)
                return;

            if (_corpseLiftRemaining > 0f)
            {
                float duration = _settings.DeathSequenceSeconds * 0.5f;
                _corpseLiftRemaining = Mathf.Max(0f,
                    _corpseLiftRemaining - Time.fixedDeltaTime);
                float progress = duration > 0f ? 1f - _corpseLiftRemaining / duration : 1f;
                _corpseBody.MovePosition(_corpseLiftStart
                    + Vector3.up * (_settings.CorpseLiftHeight * progress));
                if (_corpseLiftRemaining <= 0f)
                {
                    _corpseBody.isKinematic = false;
                    if (_pendingPushImpulse != Vector3.zero)
                    {
                        _corpseBody.AddForceAtPosition(_pendingPushImpulse,
                            _pendingPushPoint, ForceMode.Impulse);
                        _pendingPushImpulse = Vector3.zero;
                    }
                }
            }
            else if (_carrierTargets.Count >= 2)
            {
                Vector3 destination = Vector3.zero;
                foreach (Vector3 target in _carrierTargets.Values)
                    destination += target;
                destination /= _carrierTargets.Count;
                Vector3 acceleration = (destination - _corpseBody.position)
                    * _settings.CorpseCarrySpring
                    - _corpseBody.linearVelocity * _settings.CorpseCarryDamping;
                _corpseBody.AddForce(Vector3.ClampMagnitude(
                    acceleration, _settings.CorpseCarryMaxAcceleration), ForceMode.Acceleration);
            }

            Vector3 position = _corpseBody.position;
            Quaternion rotation = _corpseBody.rotation;
            if ((position - _corpsePosition.Value).sqrMagnitude > 0.0001f)
                _corpsePosition.Value = position;
            if (Quaternion.Angle(rotation, _corpseRotation.Value) > 0.5f)
                _corpseRotation.Value = rotation;
        }

        private void Update()
        {
            if (!IsSpawned || !IsServer || _corpse == null || _teamService == null
                || Time.time < _nextWitnessCheck)
                return;

            _nextWitnessCheck = Time.time + _settings.CorpseWitnessInterval;
            int count = _teamService.CopyPlayerStates(_witnessCandidates);
            for (int i = 0; i < count; i++)
            {
                SanityNetworkState candidate = _witnessCandidates[i];
                if (candidate == null || candidate == _sanity || !candidate.HasSanity)
                    continue;

                if (CanWitnessCorpse(candidate))
                    candidate.ServerApplyCorpseWitnessed(_corpseWitnessId);
            }
        }

        private bool CanWitnessCorpse(SanityNetworkState candidate)
        {
            PlayerMotor motor = candidate.GetComponent<PlayerMotor>();
            float eyeHeight = motor != null ? motor.CameraLocalHeight : 1.2f;
            Vector3 eye = candidate.transform.position + Vector3.up * eyeHeight;
            Vector3 target = _corpse.transform.position;
            Vector3 delta = target - eye;
            Vector3 flat = new(delta.x, 0f, delta.z);
            float distanceSquared = flat.sqrMagnitude;
            float maxDistance = _settings.CorpseWitnessDistance;
            if (distanceSquared < 0.0001f || distanceSquared > maxDistance * maxDistance)
                return false;

            float minimumDot = Mathf.Cos(_settings.CorpseWitnessAngle * 0.5f * Mathf.Deg2Rad);
            if (Vector3.Dot(candidate.transform.forward, flat.normalized) < minimumDot)
                return false;

            return Physics.Linecast(eye, target, out RaycastHit hit,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && TryGetCorpseOwner(hit.collider, out PlayerVisuals owner)
                && owner == this;
        }

        private void LateUpdate()
        {
            if (!IsSpawned || IsServer || _corpse == null)
                return;

            _corpse.transform.SetPositionAndRotation(
                Vector3.Lerp(_corpse.transform.position, _corpsePosition.Value, 0.5f),
                Quaternion.Slerp(_corpse.transform.rotation, _corpseRotation.Value, 0.5f));
        }

        public bool ServerTryPush(Vector3 hitPoint, Vector3 direction)
        {
            if (!IsSpawned || !IsServer || _sanity == null || _sanity.HasSanity
                || _corpseBody == null || direction.sqrMagnitude < 0.9f
                || direction.sqrMagnitude > 1.1f)
                return false;

            Vector3 impulse = direction * _settings.CorpsePushImpulse;
            if (_corpseBody.isKinematic)
            {
                _pendingPushImpulse += impulse;
                _pendingPushPoint = hitPoint;
            }
            else
                _corpseBody.AddForceAtPosition(impulse, hitPoint, ForceMode.Impulse);
            return true;
        }

        public bool ServerAddCarrier(ulong clientId, Vector3 target)
        {
            if (!IsSpawned || !IsServer || _corpseBody == null
                || _sanity == null || _sanity.HasSanity)
                return false;

            _carrierTargets[clientId] = target;
            return true;
        }

        public void ServerUpdateCarrier(ulong clientId, Vector3 target)
        {
            if (IsServer && _carrierTargets.ContainsKey(clientId))
                _carrierTargets[clientId] = target;
        }

        public void ServerRemoveCarrier(ulong clientId)
        {
            if (IsServer)
                _carrierTargets.Remove(clientId);
        }
    }

}
