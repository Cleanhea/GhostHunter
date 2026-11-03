using System.Collections.Generic;
using GhostHunter.Core;
using GhostHunter.Core.Scenes;
using GhostHunter.Gameplay.Furniture;
using GhostHunter.Gameplay.Interaction;
using GhostHunter.Gameplay.Sanity;
using GhostHunter.Gameplay.Recovery;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

namespace GhostHunter.Gameplay.Player
{
    /// <summary>
    /// 생존 몸체와 사망 위치에 남는 서버 물리 시체를 표시한다. 시체는 가구와 같은 규칙으로 잡는다 —
    /// 1명이 누르고 있으면 차징, 떼면 발사, 2명이 누르면 두 손잡이로 들어 옮긴다 → docs/architecture/death-system.md.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerVisuals : NetworkBehaviour
    {
        private const int MaxTrackedPlayers = 8;
        private const int MaxHolders = FurnitureGrabTarget.MaxHolders;
        private const float DoorScanSeconds = 0.2f;
        private const float DoorScanMargin = 0.6f;
        private static readonly Dictionary<Collider, PlayerVisuals> CorpseOwners = new();
        private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");

        // 가구 윤곽선(FurnitureOutline)과 같은 색 — 조준 / 내가 잡음 / 상대가 잡음 / 둘이 잡음 / 던져지는 중.
        private static readonly Color TargetableColor = Color.white;
        private static readonly Color BlockedColor = Color.gray;
        private static readonly Color MineColor = new(0.1f, 1f, 0.9f);
        private static readonly Color PartnerColor = new(1f, 0.45f, 0.1f);
        private static readonly Color CooperativeColor = new(1f, 0.9f, 0.1f);

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

        // 가구(FurnitureGrabTarget)와 같은 상태·차지·홀더. 서버만 쓰고 모든 피어가 읽는다.
        private readonly NetworkVariable<FurnitureState> _grabState = new(FurnitureState.Idle);
        private readonly NetworkVariable<float> _grabCharge = new(0f);
        private readonly NetworkList<ulong> _grabHolders = new();
        private readonly Dictionary<ulong, HolderAim> _aims = new(MaxHolders);
        private readonly ulong[] _holderBuffer = new ulong[MaxHolders];
        private readonly ulong[] _carryHolders = new ulong[MaxHolders];
        private readonly Collider[] _doorScanBuffer = new Collider[32];
        private readonly List<Collider> _holderColliderScratch = new();
        private readonly List<Collider> _playerColliderScratch = new();

        private SanityNetworkState _sanity;
        private SpectatorSettings _settings;
        private FurnitureThrowSettings _throwSettings;
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
        private Vector3 _corpseLiftStart;
        private float _corpseLiftRemaining;

        private FurnitureCollisionIgnoreSet _holderIgnores;
        private FurnitureCollisionIgnoreSet _doorIgnores;
        private FurnitureCarrySession _carry;
        private PhysicsMaterial _carryMaterial;
        private PhysicsMaterial _savedMaterial;
        private bool _carrying;
        private float _nextDoorScanAt;
        private float _heldSince;
        private float _launchedAt;
        // 2인 잡기에서 한 명이 먼저 놓은 순간부터 남은 한 명이 놓아도 '같이 내려놓기'로 치는 마감 시각.
        private float _jointPutDownUntil = float.NegativeInfinity;
        private Quaternion _heldRotation = Quaternion.identity;

        private Renderer[] _outlineShells;
        private MaterialPropertyBlock _outlineBlock;
        private bool _targeted;

        private struct HolderAim
        {
            public Vector3 Origin;
            public Vector3 Direction;
        }

        public bool HasCorpse => _corpse != null;
        public Vector3 CorpsePosition => _corpse != null ? _corpse.transform.position : transform.position;
        public FurnitureState GrabState => _grabState.Value;
        public float GrabCharge => _grabCharge.Value;
        public int HolderCount => _grabHolders.Count;

        /// <summary>지금 놓으면 나갈 힘의 비율(최대 힘 대비, 차지 곡선 적용). 차지 게이지가 표시한다.</summary>
        public float LaunchPower => _throwSettings != null ? _throwSettings.ForceRatio(_grabCharge.Value) : _grabCharge.Value;

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
            // 복원한 시체는 잡히지 않은 채 시작한다.
            ResetGrab(true);
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
            _throwSettings = GetComponent<GrabController>()?.Settings;
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
            _grabState.OnValueChanged += HandleGrabStateChanged;
            _grabHolders.OnListChanged += HandleGrabHoldersChanged;
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

            _grabState.OnValueChanged -= HandleGrabStateChanged;
            _grabHolders.OnListChanged -= HandleGrabHoldersChanged;
            if (_sanity != null)
                _sanity.AliveStateChanged -= HandleAliveChanged;
            if (_sceneFlow != null)
                _sceneFlow.SceneChanged -= HandleSceneChanged;
            _sanity = null;
            _teamService = null;
            _sceneFlow = null;
            DestroyCorpse(true);
            if (_carryMaterial != null)
                Destroy(_carryMaterial);
            _carryMaterial = null;
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
            // 가구와 같은 레이어 — 가구와 같은 충돌·시야·청소·귀신 판정을 받는다.
            if (GameLayers.Furniture >= 0)
                _corpse.layer = GameLayers.Furniture;
            _corpse.transform.SetPositionAndRotation(position, rotation);
            GameObject visual = CreateCorpseVisual(_corpse.transform);

            CapsuleCollider collider = _corpse.AddComponent<CapsuleCollider>();
            collider.height = _settings.CorpseHeight;
            collider.radius = _settings.CorpseRadius;
            _corpseCollider = collider;
            CorpseOwners.Add(collider, this);

            _corpseBody = _corpse.AddComponent<Rigidbody>();
            _corpseBody.mass = _settings.CorpseMass;
            if (IsServer)
            {
                // 던져진 시체가 얇은 벽을 뚫지 않게 하고, 굴러 멈추게 한다(가구 프리팹 값과 같다).
                _corpseBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                _corpseBody.angularDamping = 0.5f;
            }
            _corpseLiftStart = position;
            _corpseLiftRemaining = IsServer ? _settings.DeathSequenceSeconds * 0.5f : 0f;
            _corpseBody.isKinematic = !IsServer || _corpseLiftRemaining > 0f;

            Collider[] own = { collider };
            _holderIgnores = new FurnitureCollisionIgnoreSet(own);
            _doorIgnores = new FurnitureCollisionIgnoreSet(own);
            _carry = new FurnitureCarrySession(_corpseBody, null, null);
            CreateOutlineShells(visual);
            // 늦게 접속해 이미 잡힌 시체를 받은 경우를 위해 지금 상태로 한 번 맞춘다.
            RefreshHolderCollisionIgnore();
            RefreshOutline();
        }

        private GameObject CreateCorpseVisual(Transform corpseRoot)
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

            return visual;
        }

        /// <summary>
        /// 몸 렌더러마다 같은 메시·본을 쓰는 윤곽선 셸을 하나씩 붙인다(가구 윤곽선과 같은 반전 셸 셰이더).
        /// 평소에는 꺼 두고 조준·잡기 상태일 때만 켠다.
        /// </summary>
        private void CreateOutlineShells(GameObject visual)
        {
            Material material = _settings.CorpseOutlineMaterial;
            if (material == null)
                return;

            var shells = new List<Renderer>();
            foreach (SkinnedMeshRenderer source in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (source.sharedMesh == null)
                    continue;

                var shell = new GameObject($"{source.name}_Outline").AddComponent<SkinnedMeshRenderer>();
                shell.transform.SetParent(source.transform, false);
                shell.sharedMesh = source.sharedMesh;
                shell.bones = source.bones;
                shell.rootBone = source.rootBone;
                shell.localBounds = source.localBounds;
                shell.updateWhenOffscreen = source.updateWhenOffscreen;
                ConfigureShell(shell, material, source.sharedMesh.subMeshCount);
                shells.Add(shell);
            }

            foreach (MeshRenderer source in visual.GetComponentsInChildren<MeshRenderer>(true))
            {
                Mesh mesh = source.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
                if (mesh == null)
                    continue;

                var shellObject = new GameObject($"{source.name}_Outline");
                shellObject.transform.SetParent(source.transform, false);
                shellObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                var shell = shellObject.AddComponent<MeshRenderer>();
                ConfigureShell(shell, material, mesh.subMeshCount);
                shells.Add(shell);
            }

            _outlineShells = shells.ToArray();
            _outlineBlock ??= new MaterialPropertyBlock();
        }

        private static void ConfigureShell(Renderer shell, Material material, int subMeshCount)
        {
            var materials = new Material[Mathf.Max(1, subMeshCount)];
            for (int i = 0; i < materials.Length; i++)
                materials[i] = material;
            shell.sharedMaterials = materials;
            shell.shadowCastingMode = ShadowCastingMode.Off;
            shell.receiveShadows = false;
            shell.enabled = false;
        }

        private void DestroyCorpse(bool despawning = false)
        {
            EndCarry();
            ResetGrab(!despawning);
            _holderIgnores?.ClearImmediately();
            _doorIgnores?.ClearImmediately();
            _holderIgnores = null;
            _doorIgnores = null;
            _carry = null;
            _outlineShells = null;
            _targeted = false;

            if (!ReferenceEquals(_corpseCollider, null))
                CorpseOwners.Remove(_corpseCollider);
            _corpseCollider = null;
            if (_corpse != null)
                Destroy(_corpse);
            _corpse = null;
            _corpseBody = null;
            _corpseLiftRemaining = 0f;
        }

        private void FixedUpdate()
        {
            if (!IsSpawned)
                return;

            // 모든 피어 — 운반이 끝난 홀더와의 충돌을 떨어지는 대로 다시 켠다.
            if (_holderIgnores != null && !_holderIgnores.IsEmpty)
                _holderIgnores.Tick();

            if (!IsServer || _corpseBody == null)
                return;

            _doorIgnores?.Tick();

            if (_corpseLiftRemaining > 0f)
            {
                float duration = _settings.DeathSequenceSeconds * 0.5f;
                _corpseLiftRemaining = Mathf.Max(0f,
                    _corpseLiftRemaining - Time.fixedDeltaTime);
                float progress = duration > 0f ? 1f - _corpseLiftRemaining / duration : 1f;
                _corpseBody.MovePosition(_corpseLiftStart
                    + Vector3.up * (_settings.CorpseLiftHeight * progress));
                if (_corpseLiftRemaining <= 0f)
                    _corpseBody.isKinematic = false;
            }
            else
            {
                ServerStepGrab();
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

        // ── 잡기·던지기 (가구 FurnitureGrabTarget / FurnitureHoverMotor / FurnitureLauncher 와 같은 규칙) ──
        // 서버가 홀더·차지·상태를 정하고 클라이언트는 복제된 값을 읽어 윤곽선·게이지·충돌 무시만 맞춘다.

        public bool ContainsHolder(ulong clientId)
        {
            for (int i = 0; i < _grabHolders.Count; i++)
            {
                if (_grabHolders[i] == clientId)
                    return true;
            }

            return false;
        }

        /// <summary>이 시체를 잡으려 해 볼 수 있는가 — 서버가 다시 검사한다.</summary>
        public bool CanGrab(ulong clientId)
        {
            return _corpse != null && _throwSettings != null
                && _grabState.Value != FurnitureState.Launched
                && (ContainsHolder(clientId) || _grabHolders.Count < MaxHolders);
        }

        /// <summary>소유자 조준이 이 시체를 겨누는 동안 윤곽선을 켠다.</summary>
        public void SetTargeted(bool targeted)
        {
            if (_targeted == targeted)
                return;

            _targeted = targeted;
            RefreshOutline();
        }

        public bool ServerTryAddHolder(ulong clientId, Vector3 origin, Vector3 direction)
        {
            if (!IsSpawned || !IsServer || _throwSettings == null || _corpseBody == null
                || _sanity == null || _sanity.HasSanity
                || _corpseLiftRemaining > 0f || _corpseBody.isKinematic
                || _grabState.Value == FurnitureState.Launched
                || _grabHolders.Count >= MaxHolders
                || ContainsHolder(clientId)
                || !TrySanitizeAim(origin, direction, out HolderAim aim))
                return false;

            bool firstHolder = _grabHolders.Count == 0;
            _grabHolders.Add(clientId);
            _aims[clientId] = aim;
            // 새로 잡거나 다시 둘이 되면 이전 '같이 내려놓기' 대기는 끝난다.
            _jointPutDownUntil = float.NegativeInfinity;

            if (firstHolder)
            {
                _heldSince = Time.time;
                _grabCharge.Value = 0f;
                _grabState.Value = FurnitureState.ThrowReady;
                _corpseBody.useGravity = true;
            }
            else
            {
                _heldRotation = _corpseBody.rotation;
                _grabState.Value = FurnitureState.Held;
                _corpseBody.useGravity = false;
            }

            return true;
        }

        public void ServerUpdateAim(ulong clientId, Vector3 origin, Vector3 direction)
        {
            if (IsServer && ContainsHolder(clientId) && TrySanitizeAim(origin, direction, out HolderAim aim))
                _aims[clientId] = aim;
        }

        /// <summary>2인 잡기 중인 홀더의 휠 한 칸으로 목표 자세를 바꾼다(throw-system.md §3.1).</summary>
        public bool ServerRotateHeld(ulong clientId, int steps, FurnitureRotateMode mode)
        {
            if (!IsServer || _throwSettings == null
                || _grabState.Value != FurnitureState.Held
                || (steps != 1 && steps != -1)
                || mode is not (FurnitureRotateMode.Rotate or FurnitureRotateMode.Tilt)
                || !ContainsHolder(clientId)
                || !_aims.TryGetValue(clientId, out HolderAim aim))
                return false;

            _heldRotation = FurnitureHeldControl.ApplyWheel(
                _heldRotation, steps, mode, aim.Direction, _throwSettings.WheelStepDegrees);
            return true;
        }

        /// <summary>홀더가 놓았다. <paramref name="forced"/>면 발사하지 않고 그 홀더만 푼다.</summary>
        public void ServerRelease(ulong clientId, Vector3 direction, bool forced)
        {
            if (!IsServer || _throwSettings == null || _corpseBody == null || !ContainsHolder(clientId))
                return;

            Vector3 releaseDirection = ResolveReleaseDirection(clientId, direction);
            int countBeforeRelease = _grabHolders.Count;

            RemoveHolder(clientId);
            NotifyHolderReleased(clientId);

            if (forced)
            {
                RefreshStateAfterHolderLeft();
                return;
            }

            if (countBeforeRelease > 1 && !_throwSettings.LaunchOnFirstRelease)
            {
                _jointPutDownUntil = _throwSettings.JointPutDownWindow > 0f
                    ? Time.time + _throwSettings.JointPutDownWindow
                    : float.NegativeInfinity;
                RefreshStateAfterHolderLeft();
                return;
            }

            // 둘이 들다가 먼저 놓은 사람과 거의 같이 놓았다 — 던지지 않고 그 자리에 내려놓는다.
            if (FurnitureGrabTarget.IsJointPutDown(countBeforeRelease, Time.time, _jointPutDownUntil))
            {
                ReturnToIdle();
                return;
            }

            // 2인 잡기에서 먼저 놓은 순간 곧바로 나가는 발사는 남은 사람의 조준 방향만 따른다.
            Vector3 launchDirection = countBeforeRelease > 1
                ? CalculateRemainingHolderDirection(releaseDirection)
                : releaseDirection;
            float charge = _grabCharge.Value;

            ClearRemainingHolders();
            _grabState.Value = FurnitureState.Launched;
            _launchedAt = Time.time;
            _grabCharge.Value = 0f;
            Launch(launchDirection, charge, countBeforeRelease);
        }

        public void ServerForceRelease(ulong clientId)
        {
            ServerRelease(clientId, Vector3.forward, true);
        }

        private void Launch(Vector3 aimDirection, float charge, int holderCount)
        {
            // 힘은 설정 에셋 값 그대로 쓴다(가구와 같다). 시체는 heavy 가구 감쇠 없이 항상 같은 힘이다.
            float baseForce = holderCount >= 2 ? _throwSettings.TwoHolderForce : _throwSettings.OneHolderForce;
            float magnitude = baseForce * _throwSettings.ForceRatio(charge);
            Vector3 direction = FurnitureLauncher.ResolveLaunchDirection(
                aimDirection, FurnitureLauncher.MinimumLaunchAngle, FurnitureLauncher.MaximumLaunchAngle);

            _corpseBody.useGravity = true;
            _corpseBody.AddForce(direction * magnitude, ForceMode.VelocityChange);
            // 질량이 작은 시체가 가구보다 훨씬 빨리 도는 것을 막으려 질량을 곱한다.
            _corpseBody.AddTorque(
                Random.insideUnitSphere * (_throwSettings.TorqueScale * _corpseBody.mass), ForceMode.Impulse);
        }

        /// <summary>서버 FixedUpdate — 상태별 차지·이탈 해제·발사 후 안정·2인 운반.</summary>
        private void ServerStepGrab()
        {
            if (_throwSettings == null)
                return;

            switch (_grabState.Value)
            {
                case FurnitureState.Idle:
                    EndCarry();
                    return;
                case FurnitureState.Launched:
                    EndCarry();
                    float elapsed = Time.time - _launchedAt;
                    bool settled = elapsed >= 0.2f && _corpseBody.linearVelocity.sqrMagnitude
                        <= _throwSettings.SettledSpeed * _throwSettings.SettledSpeed;
                    if (settled || elapsed >= _throwSettings.RelaunchLockDuration)
                        _grabState.Value = FurnitureState.Idle;
                    return;
            }

            _grabCharge.Value = Mathf.Clamp01((Time.time - _heldSince) / _throwSettings.ChargeTime);
            ReleaseHoldersOutOfReach();

            if (_grabState.Value != FurnitureState.Held || _grabHolders.Count < MaxHolders)
            {
                EndCarry();
                return;
            }

            if ((!_carrying || !SameHolders()) && !BeginCarry())
                return;

            if (!_aims.TryGetValue(_grabHolders[0], out HolderAim a) || !_aims.TryGetValue(_grabHolders[1], out HolderAim b))
                return;

            _carry.Step(Time.fixedDeltaTime, Time.time, a.Origin, a.Direction, b.Origin, b.Direction,
                _heldRotation, false);
            ScanDoors();
        }

        /// <summary>
        /// 차징 중 눈에서 시체 표면까지 멀어지면 그 홀더를 푼다(가구 holdBreakDistance 와 같다). 2인 잡기에서는
        /// 손을 뗀 것과 같게 처리해 남은 사람의 조준으로 발사하고, 1인 투척 준비는 발사 없이 푼다.
        /// </summary>
        private void ReleaseHoldersOutOfReach()
        {
            int count = Mathf.Min(_grabHolders.Count, _holderBuffer.Length);
            for (int i = 0; i < count; i++)
                _holderBuffer[i] = _grabHolders[i];

            for (int i = 0; i < count; i++)
            {
                ulong clientId = _holderBuffer[i];
                if (!_aims.TryGetValue(clientId, out HolderAim aim))
                    continue;

                Vector3 closest = _corpseCollider.ClosestPoint(aim.Origin);
                if (Vector3.Distance(aim.Origin, closest) <= _throwSettings.HoldBreakDistance)
                    continue;

                if (_grabState.Value == FurnitureState.Held && _grabHolders.Count > 1)
                    ServerRelease(clientId, aim.Direction, false);
                else
                    ServerForceRelease(clientId);
            }
        }

        private bool BeginCarry()
        {
            EndCarry();

            if (_grabHolders.Count < MaxHolders
                || !_aims.TryGetValue(_grabHolders[0], out HolderAim a)
                || !_aims.TryGetValue(_grabHolders[1], out HolderAim b))
                return false;

            _carryHolders[0] = _grabHolders[0];
            _carryHolders[1] = _grabHolders[1];
            _carry.Begin(
                _throwSettings,
                a.Origin, a.Direction, FindGripPoint(a.Origin, a.Direction),
                b.Origin, b.Direction, FindGripPoint(b.Origin, b.Direction));
            _nextDoorScanAt = 0f;
            ApplyCarryMaterial();
            _carrying = true;
            return true;
        }

        private void EndCarry()
        {
            if (!_carrying)
                return;

            _carrying = false;
            RestoreMaterial();
            _doorIgnores?.ReleaseAll();
        }

        private bool SameHolders()
        {
            for (int i = 0; i < MaxHolders; i++)
            {
                if (_grabHolders[i] != _carryHolders[i])
                    return false;
            }

            return true;
        }

        /// <summary>조준 광선이 시체에 닿은 점. 빗나가면 눈에서 가장 가까운 표면 점.</summary>
        private Vector3 FindGripPoint(Vector3 origin, Vector3 direction)
        {
            var ray = new Ray(origin, direction);
            return _corpseCollider.Raycast(ray, out RaycastHit hit, _throwSettings.HoldBreakDistance + 1f)
                ? hit.point
                : _corpseCollider.ClosestPoint(origin);
        }

        /// <summary>근처의 열린 문짝과는 충돌을 끄고, 닫힌 문짝은 떨어지는 대로 다시 켠다(throw-system.md §3.3).</summary>
        private void ScanDoors()
        {
            if (!_throwSettings.CarryPassesOpenDoors || Time.time < _nextDoorScanAt)
                return;

            _nextDoorScanAt = Time.time + DoorScanSeconds;
            for (int i = _doorIgnores.Ignored.Count - 1; i >= 0; i--)
            {
                Collider ignored = _doorIgnores.Ignored[i];
                DoorInteractable door = ignored != null ? ignored.GetComponentInParent<DoorInteractable>() : null;
                if (door == null || !door.IsOpen)
                    _doorIgnores.Release(ignored);
            }

            Bounds bounds = _corpseCollider.bounds;
            int count = Physics.OverlapSphereNonAlloc(
                bounds.center, bounds.extents.magnitude + DoorScanMargin, _doorScanBuffer,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider other = _doorScanBuffer[i];
                if (other == null || other.attachedRigidbody == _corpseBody)
                    continue;

                DoorInteractable door = other.GetComponentInParent<DoorInteractable>();
                if (door != null && door.IsOpen)
                    _doorIgnores.Ignore(other);
            }
        }

        /// <summary>운반 중에는 마찰을 낮춰 문틀·벽을 긁으며 미끄러져 지나가게 한다. 놓으면 원래 재질로 돌린다.</summary>
        private void ApplyCarryMaterial()
        {
            if (_carryMaterial == null)
            {
                _carryMaterial = new PhysicsMaterial("CarriedCorpse")
                {
                    bounciness = 0f,
                    bounceCombine = PhysicsMaterialCombine.Minimum,
                    frictionCombine = PhysicsMaterialCombine.Minimum,
                };
            }

            _carryMaterial.dynamicFriction = _throwSettings.CarryFriction;
            _carryMaterial.staticFriction = _throwSettings.CarryFriction;
            _savedMaterial = _corpseCollider.sharedMaterial;
            _corpseCollider.sharedMaterial = _carryMaterial;
        }

        private void RestoreMaterial()
        {
            if (!ReferenceEquals(_corpseCollider, null) && _corpseCollider != null)
                _corpseCollider.sharedMaterial = _savedMaterial;
            _savedMaterial = null;
        }

        private Vector3 ResolveReleaseDirection(ulong clientId, Vector3 requested)
        {
            if (IsFinite(requested) && requested.sqrMagnitude > 0.01f)
                return requested.normalized;

            return _aims.TryGetValue(clientId, out HolderAim aim) ? aim.Direction : transform.forward;
        }

        /// <summary>남아 있는 홀더들 조준 방향의 합. 남은 홀더 조준이 없으면 <paramref name="fallback"/>.</summary>
        private Vector3 CalculateRemainingHolderDirection(Vector3 fallback)
        {
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < _grabHolders.Count; i++)
            {
                if (_aims.TryGetValue(_grabHolders[i], out HolderAim aim))
                    sum += aim.Direction;
            }

            return sum.sqrMagnitude > 0.01f ? sum.normalized : fallback.normalized;
        }

        private void RemoveHolder(ulong clientId)
        {
            for (int i = _grabHolders.Count - 1; i >= 0; i--)
            {
                if (_grabHolders[i] == clientId)
                    _grabHolders.RemoveAt(i);
            }

            _aims.Remove(clientId);
        }

        private void ClearRemainingHolders()
        {
            while (_grabHolders.Count > 0)
            {
                ulong holder = _grabHolders[_grabHolders.Count - 1];
                _grabHolders.RemoveAt(_grabHolders.Count - 1);
                _aims.Remove(holder);
                NotifyHolderReleased(holder);
            }
        }

        private void ReturnToIdle()
        {
            _corpseBody.useGravity = true;
            _grabState.Value = FurnitureState.Idle;
            _grabCharge.Value = 0f;
            _jointPutDownUntil = float.NegativeInfinity;
        }

        private void RefreshStateAfterHolderLeft()
        {
            _corpseBody.useGravity = true;

            if (_grabHolders.Count == 0)
            {
                ReturnToIdle();
                return;
            }

            _grabState.Value = FurnitureState.ThrowReady;
        }

        /// <summary>시체를 없애거나 되살릴 때 홀더를 모두 풀고 잡기 상태를 되돌린다.</summary>
        private void ResetGrab(bool writeReplicated)
        {
            if (IsServer)
            {
                for (int i = _grabHolders.Count - 1; i >= 0; i--)
                    NotifyHolderReleased(_grabHolders[i]);

                if (writeReplicated && IsSpawned)
                {
                    if (_grabHolders.Count > 0)
                        _grabHolders.Clear();
                    if (_grabState.Value != FurnitureState.Idle)
                        _grabState.Value = FurnitureState.Idle;
                    if (_grabCharge.Value != 0f)
                        _grabCharge.Value = 0f;
                }
            }

            _aims.Clear();
            _jointPutDownUntil = float.NegativeInfinity;
            if (_corpseBody != null)
                _corpseBody.useGravity = true;
        }

        private void NotifyHolderReleased(ulong clientId)
        {
            if (NetworkManager == null
                || !NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client)
                || client.PlayerObject == null)
                return;

            client.PlayerObject.GetComponent<GrabController>()?.ServerClearHeldCorpse(NetworkObjectId);
        }

        private void HandleGrabStateChanged(FurnitureState previous, FurnitureState current)
        {
            RefreshHolderCollisionIgnore();
            RefreshOutline();
        }

        private void HandleGrabHoldersChanged(NetworkListEvent<ulong> change)
        {
            RefreshHolderCollisionIgnore();
            RefreshOutline();
        }

        /// <summary>
        /// 2인 운반(Held) 중에는 두 홀더의 플레이어 콜라이더와 시체의 충돌을 끈다 — 서버에서는 시체가 들고 있는 사람
        /// 몸에 막히지 않고, 각 클라이언트에서는 자기 캐릭터가 키네마틱 시체 사본에 막히지 않는다(throw-system.md §3.3).
        /// 상태와 홀더 목록이 모든 피어에 복제되므로 각 피어가 같은 판단을 한다.
        /// </summary>
        private void RefreshHolderCollisionIgnore()
        {
            if (_holderIgnores == null)
                return;

            _holderColliderScratch.Clear();
            if (_throwSettings != null && _throwSettings.CarryIgnoresHolders
                && _grabState.Value == FurnitureState.Held)
            {
                for (int i = 0; i < _grabHolders.Count; i++)
                {
                    NetworkObject player = FindPlayerObject(_grabHolders[i]);
                    if (player == null)
                        continue;

                    _playerColliderScratch.Clear();
                    player.GetComponentsInChildren(_playerColliderScratch);
                    foreach (Collider part in _playerColliderScratch)
                    {
                        if (!part.isTrigger)
                            _holderColliderScratch.Add(part);
                    }
                }
            }

            for (int i = _holderIgnores.Ignored.Count - 1; i >= 0; i--)
            {
                Collider ignored = _holderIgnores.Ignored[i];
                if (!_holderColliderScratch.Contains(ignored))
                    _holderIgnores.Release(ignored);
            }

            foreach (Collider part in _holderColliderScratch)
                _holderIgnores.Ignore(part);
        }

        /// <summary>
        /// 홀더의 플레이어 오브젝트. 클라이언트-서버 구조에서 <c>ConnectedClients</c>는 서버만 남의 것을 주므로
        /// 클라이언트는 스폰 목록에서 찾는다(홀더가 바뀔 때만 부른다).
        /// </summary>
        private NetworkObject FindPlayerObject(ulong clientId)
        {
            if (NetworkManager == null)
                return null;

            if (IsServer)
            {
                return NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client)
                    ? client.PlayerObject
                    : null;
            }

            if (NetworkManager.SpawnManager == null)
                return null;

            foreach (NetworkObject spawned in NetworkManager.SpawnManager.SpawnedObjectsList)
            {
                if (spawned != null && spawned.IsPlayerObject && spawned.OwnerClientId == clientId)
                    return spawned;
            }

            return null;
        }

        private void RefreshOutline()
        {
            if (_outlineShells == null || _outlineShells.Length == 0 || !IsSpawned)
                return;

            bool show = false;
            Color color = TargetableColor;
            ulong localClientId = NetworkManager != null ? NetworkManager.LocalClientId : ulong.MaxValue;

            if (_grabHolders.Count >= MaxHolders)
            {
                show = true;
                color = CooperativeColor;
            }
            else if (ContainsHolder(localClientId))
            {
                show = true;
                color = MineColor;
            }
            else if (_grabHolders.Count == 1)
            {
                show = true;
                color = PartnerColor;
            }
            else if (_targeted)
            {
                show = true;
                color = _grabState.Value == FurnitureState.Launched ? BlockedColor : TargetableColor;
            }

            foreach (Renderer shell in _outlineShells)
            {
                if (shell == null)
                    continue;

                shell.enabled = show;
                if (!show)
                    continue;

                shell.GetPropertyBlock(_outlineBlock);
                _outlineBlock.SetColor(OutlineColorId, color);
                shell.SetPropertyBlock(_outlineBlock);
            }
        }

        private static bool TrySanitizeAim(Vector3 origin, Vector3 direction, out HolderAim aim)
        {
            if (!IsFinite(origin) || !IsFinite(direction) || direction.sqrMagnitude < 0.25f)
            {
                aim = default;
                return false;
            }

            aim = new HolderAim
            {
                Origin = origin,
                Direction = direction.normalized,
            };
            return true;
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
        }
    }

}
