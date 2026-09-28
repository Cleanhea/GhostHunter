using GhostHunter.Core;
using GhostHunter.Core.Scenes;
using GhostHunter.Gameplay.Ghost;
using GhostHunter.Gameplay.Recovery;
using GhostHunter.Gameplay.Sanity;
using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Gameplay.Player
{
    /// <summary>
    /// 라이터 — 퀵슬롯 아이템. 헤드라이트와 따로 켜지는 두 번째 광원이다(사용자 확정 2026-09-29) →
    /// docs/project/lighter-system.md.
    ///
    /// <para><b>켜짐.</b> 퀵슬롯에서 라이터를 골라 들고 있는 동안 켜진다 — 따로 누르는 키가 없다. 다른 슬롯을 고르면
    /// 집어넣어 꺼진다. 장착 상태의 출처는 <see cref="PlayerCleaningController.EquippedSlot"/>(서버 확인을 거친 퀵슬롯
    /// 장착)이다. 사망하면 꺼지고, 은신 중에는 켜지지 않는다(헤드라이트와 같은 규칙). 연료가 바닥나면 꺼지고, 들고 있는
    /// 채로 드릴카에서 연료가 차면 다시 붙는다.</para>
    ///
    /// <para><b>권위.</b> <see cref="PlayerHeadlamp"/> 와 같다 — 켜짐·연료는 소유자가 진행하고 NetworkVariable(Owner 쓰기 +
    /// Everyone 읽기)로 복제한다. 연료 규칙(켜져 있을 때 소모, 드릴카 안 충전)은 <see cref="HeadlampBattery"/> 를 그대로 쓴다.
    /// 서버는 복제된 켜짐 값을 믿고 정신력 어둠 판정(<see cref="PlayerHeadlamp"/>)·땅굴 은신 판정
    /// (<see cref="SanityNetworkState.IsBurrowed"/>)에서 헤드라이트와 똑같이 빛으로 친다.</para>
    ///
    /// <para><b>불빛.</b> 스폰할 때 손 위치에 흔들리는 주황 점광원과 작은 불꽃·몸체 메시를 만들어 모든 피어에서 그린다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerLighter : NetworkBehaviour
    {
        /// <summary>연료 값을 복제하는 간격(초). 켜짐은 바뀌는 즉시 보낸다.</summary>
        private const float FuelPublishInterval = 0.25f;

        [SerializeField] private LighterSettings _settings;
        [SerializeField] private QuickSlotLoadout _loadout;
        [Tooltip("퀵슬롯 장착 상태를 읽는다. 대걸레 컨트롤러가 모든 슬롯의 장착을 서버와 맞춘다.")]
        [SerializeField] private PlayerCleaningController _equipment;
        [SerializeField] private PlayerLook _look;
        [SerializeField] private PlayerMotor _motor;
        [SerializeField] private SanityNetworkState _sanity;

        private readonly NetworkVariable<bool> _isLit = new(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        private readonly NetworkVariable<float> _fuel = new(
            100f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        private HeadlampBattery _state;
        private Transform _root;
        private Light _light;
        private AudioSource _audio;
        private ISceneFlow _sceneFlow;
        private float _nextFuelPublishAt;
        private bool _wasHeld;
        private float _flickerSeed;

        /// <summary>불이 붙어 있는가.</summary>
        public bool IsLit => IsOwner && _state != null ? _state.IsOn : _isLit.Value;

        /// <summary>남은 연료. 원격에서는 <see cref="FuelPublishInterval"/> 만큼 늦은 값이다.</summary>
        public float Fuel => IsOwner && _state != null ? _state.Charge : _fuel.Value;

        public float MaxFuel => _settings != null ? _settings.MaxFuel : 100f;

        /// <summary>퀵슬롯에서 라이터를 골라 들고 있는가. 소유자에서만 의미가 있다 — 연료 UI 가 읽는다.</summary>
        public bool IsEquipped => IsLighterEquipped();

        /// <summary>드릴카 안에서 충전 중인가. 소유자에서만 의미가 있다.</summary>
        public bool IsCharging { get; private set; }

        public void CaptureStageState(ref StageRecoverySnapshot.PlayerState snapshot)
        {
            // 쓴 양으로 저장한다 — 필드가 비어 있는(0) 스냅샷은 "가득 참"으로 읽힌다.
            snapshot.LighterDrained = Mathf.Max(0f, MaxFuel - _fuel.Value);
        }

        public void ServerRestoreStageState(StageRecoverySnapshot.PlayerState snapshot)
        {
            if (IsServer && IsSpawned)
                RestoreStageStateRpc(snapshot.LighterDrained);
        }

        [Rpc(SendTo.Owner)]
        private void RestoreStageStateRpc(float drained)
        {
            if (!IsOwner || _state == null)
                return;

            // 켜짐은 장착에서 나온다 — 연료만 되돌리고 다음 틱이 다시 판정한다.
            _state.Restore(_state.Max - drained, false);
            _wasHeld = false;
            Publish(force: true);
        }

        private void Awake()
        {
            if (_settings == null)
            {
                Debug.LogError($"{nameof(PlayerLighter)}: 라이터 설정 에셋이 배선되지 않았습니다.", this);
                enabled = false;
                return;
            }

            if (_loadout == null || _equipment == null)
                Debug.LogError($"{nameof(PlayerLighter)}: 퀵슬롯 로드아웃·장착 컨트롤러가 배선되지 않았습니다.", this);

            BuildVisual();
        }

        public override void OnNetworkSpawn()
        {
            Services.TryGet(out _sceneFlow);
            _flickerSeed = NetworkObjectId * 7.31f % 100f;

            if (!IsOwner || _settings == null)
                return;

            _state = new HeadlampBattery(
                _settings.MaxFuel,
                _settings.DrainPerSecond,
                _settings.RechargePerSecond);
            _wasHeld = false;
            Publish(force: true);
        }

        public override void OnNetworkDespawn()
        {
            _state = null;
            _sceneFlow = null;
            IsCharging = false;
            _wasHeld = false;
            SetVisible(false);
        }

        private void Update()
        {
            if (!IsSpawned || !IsOwner || _state == null || StageRecoveryGate.Restoring)
                return;

            OwnerTick(Time.deltaTime);
        }

        private void LateUpdate()
        {
            if (_root == null)
                return;

            bool lit = IsSpawned && IsLit;
            SetVisible(lit);
            if (!lit)
                return;

            FollowHand();
            float noise = Mathf.PerlinNoise(Time.time * _settings.FlickerSpeed, _flickerSeed) * 2f - 1f;
            _light.intensity = _settings.Intensity * Mathf.Max(0f, 1f + noise * _settings.FlickerAmount);
        }

        private void OwnerTick(float deltaTime)
        {
            bool alive = _sanity == null || _sanity.HasSanity;
            bool equipped = IsLighterEquipped();
            // 은신 중에는 쓸 수 없다(귀신 기획서 §9.3.1 — 헤드라이트와 같은 규칙).
            bool held = equipped && alive && !IsInHidingPlace();
            bool wasLit = _state.IsOn;

            if (!held)
                _state.ForceOff();
            else if (!_state.IsOn && !_state.IsEmpty)
                _state.Toggle(true);

            IsCharging = alive && DrillCarSafeZone.Contains(transform.position);
            _state.Tick(deltaTime, IsCharging, IsStageScene());

            bool lit = _state.IsOn;
            if (held && !_wasHeld && !lit)
                PlaySound(_settings.EmptyClip);
            else if (lit && !wasLit)
                PlaySound(_settings.OnClip);
            else if (wasLit && !lit && !equipped && alive)
                // 집어넣을 때만 소리를 낸다 — 사망·은신·연료 소진으로 꺼질 때는 조용하다.
                PlaySound(_settings.OffClip);

            _wasHeld = held;
            Publish(force: false);
        }

        private void Publish(bool force)
        {
            if (_isLit.Value != _state.IsOn)
                _isLit.Value = _state.IsOn;

            if (!force && Time.unscaledTime < _nextFuelPublishAt)
                return;

            _nextFuelPublishAt = Time.unscaledTime + FuelPublishInterval;
            if (!Mathf.Approximately(_fuel.Value, _state.Charge))
                _fuel.Value = _state.Charge;
        }

        private bool IsLighterEquipped()
        {
            if (_equipment == null || _loadout == null)
                return false;

            QuickSlotItemDefinition item = _loadout.GetSlot(_equipment.EquippedSlot);
            return item != null && item.IsLighter;
        }

        /// <summary>일반 은신처 상자 안이거나, 엎드려 침대 밑 공간에 들어가 있다 — 헤드라이트와 같은 기하다.</summary>
        private bool IsInHidingPlace()
        {
            Vector3 position = transform.position;
            return HidingSpot.Contains(position)
                || (_motor != null && _motor.IsProne && BedHideZone.Contains(position));
        }

        private bool IsStageScene() => _sceneFlow != null && _sceneFlow.Current.IsStage();

        private void PlaySound(AudioClip clip)
        {
            if (clip != null && _audio != null)
                _audio.PlayOneShot(clip, _settings.SfxVolume);
        }

        private void FollowHand()
        {
            float eyeHeight = _motor != null ? _motor.CameraLocalHeight : 1.65f;
            Quaternion pitch = Quaternion.Euler(_look != null ? _look.Pitch : 0f, 0f, 0f);
            _root.localPosition = new Vector3(0f, eyeHeight, 0f) + pitch * _settings.LocalOffset;
            _root.localRotation = Quaternion.identity;
        }

        private void SetVisible(bool visible)
        {
            if (_root != null && _root.gameObject.activeSelf != visible)
                _root.gameObject.SetActive(visible);
        }

        private void BuildVisual()
        {
            GameObject rootObject = new("Lighter");
            rootObject.transform.SetParent(transform, false);
            _root = rootObject.transform;

            // 몸체(아래)와 불꽃(위). 충돌체는 지운다 — 손에 든 장식이라 물리·조준 레이에 걸리면 안 된다.
            if (_settings.BodyMaterial != null)
                CreatePart(PrimitiveType.Cube, "Body", _settings.BodyMaterial,
                    new Vector3(0f, -0.03f, 0f), new Vector3(0.024f, 0.05f, 0.012f));
            if (_settings.FlameMaterial != null)
                CreatePart(PrimitiveType.Sphere, "Flame", _settings.FlameMaterial,
                    new Vector3(0f, 0.012f, 0f), new Vector3(0.012f, 0.026f, 0.012f));

            GameObject lightObject = new("FlameLight");
            lightObject.transform.SetParent(_root, false);
            lightObject.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            _light = lightObject.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.range = _settings.Range;
            _light.intensity = _settings.Intensity;
            _light.color = _settings.Color;
            _light.shadows = _settings.Shadows;
            _light.shadowStrength = _settings.ShadowStrength;

            // 소리는 든 사람에게만 들린다(헤드라이트 전원 효과음과 같다). 불이 꺼지면 _root 가 비활성화되므로
            // "집어넣는" 소리를 낼 수 있게 소리는 항상 켜져 있는 별도 오브젝트에 둔다.
            GameObject audioObject = new("LighterAudio");
            audioObject.transform.SetParent(transform, false);
            _audio = audioObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f;

            rootObject.SetActive(false);
        }

        private void CreatePart(PrimitiveType type, string partName, Material material, Vector3 position, Vector3 scale)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = partName;
            Destroy(part.GetComponent<Collider>());
            part.transform.SetParent(_root, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;

            MeshRenderer meshRenderer = part.GetComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
        }
    }
}
