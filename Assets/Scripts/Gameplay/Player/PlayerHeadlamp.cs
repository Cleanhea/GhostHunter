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
    /// 안전모 헤드라이트 — 인벤토리·퀵슬롯에 넣는 아이템이 아니라 모든 플레이어가 기본으로 달고 있는 장비다
    /// (기획서 "손전등" 0.1을 헤드라이트로 바꿔 구현, 사용자 확정 2026-09-28) → docs/project/headlamp-system.md.
    ///
    /// <para><b>권위.</b> 전원·배터리는 소유자가 진행하고 <see cref="NetworkVariable{T}"/>(Owner 쓰기 + Everyone 읽기)로
    /// 복제한다 — <see cref="MoleBurrowController.IsBurrowed"/>·<see cref="PlayerMotor.IsCrouching"/> 과 같은 패턴이다.
    /// 물리가 아니라 조명 상태라 ADR-0010 대상이 아니다. 서버는 복제된 전원 값을 믿고 두 가지를 판정한다.</para>
    /// <list type="bullet">
    /// <item>정신력 어둠 노출(정신력 기획서 §4.3) — 스테이지 씬에서 생존 중이고, 헤드라이트가 꺼져 있고, 드릴카 밖이면
    /// <see cref="SanityNetworkState.ServerSetDarknessExposed"/> 를 켠다.</item>
    /// <item>땅굴 안 헤드라이트(두더지 스킬 기획서 §5.2.1 ②) — <see cref="SanityNetworkState.IsBurrowed"/> 가 켜진
    /// 헤드라이트를 보면 은신이 아닌 것으로 친다.</item>
    /// </list>
    ///
    /// <para><b>조명.</b> 스폰할 때 스포트 라이트를 하나 만들어 모든 피어에서 그린다 — 다른 사람의 헤드라이트도 보인다.
    /// 위치는 복제된 눈높이(<see cref="PlayerMotor.CameraLocalHeight"/>), 방향은 몸 yaw + 복제된 피치
    /// (<see cref="PlayerLook.Pitch"/>)라 원격 플레이어의 불빛도 고개를 따라 움직인다. 저전력 깜빡임은 각 피어가
    /// 복제된 배터리 값으로 따로 재생하는 연출이다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerHeadlamp : NetworkBehaviour
    {
        /// <summary>배터리 값을 복제하는 간격(초). 전원은 바뀌는 즉시 보낸다.</summary>
        private const float BatteryPublishInterval = 0.25f;

        [SerializeField] private HeadlampSettings _settings;
        [SerializeField] private PlayerInputReader _input;
        [SerializeField] private PlayerLook _look;
        [SerializeField] private PlayerMotor _motor;
        [SerializeField] private SanityNetworkState _sanity;

        private readonly NetworkVariable<bool> _isOn = new(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        private readonly NetworkVariable<float> _battery = new(
            100f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        private HeadlampBattery _state;
        private PlayerLighter _lighter;
        private Light _light;
        private AudioSource _audio;
        private ISceneFlow _sceneFlow;
        private float _nextBatteryPublishAt;
        private float _lowBatterySince = -1f;

        /// <summary>전원이 켜져 있는가. 깜빡임으로 잠깐 어두운 순간도 켜진 것이다.</summary>
        public bool IsOn => IsOwner && _state != null ? _state.IsOn : _isOn.Value;

        /// <summary>남은 배터리. 원격에서는 <see cref="BatteryPublishInterval"/> 만큼 늦은 값이다.</summary>
        public float Battery => IsOwner && _state != null ? _state.Charge : _battery.Value;

        public float MaxBattery => _settings != null ? _settings.MaxBattery : 100f;

        /// <summary>드릴카 안에서 충전 중인가. 소유자에서만 의미가 있다 — 충전 UI 가 읽는다.</summary>
        public bool IsCharging { get; private set; }

        public void CaptureStageState(ref StageRecoverySnapshot.PlayerState snapshot)
        {
            snapshot.HeadlampOn = _isOn.Value;
            // 쓴 양으로 저장한다 — 필드가 비어 있는(0) 스냅샷은 "가득 참"으로 읽힌다.
            snapshot.HeadlampDrained = Mathf.Max(0f, MaxBattery - _battery.Value);
        }

        public void ServerRestoreStageState(StageRecoverySnapshot.PlayerState snapshot)
        {
            if (IsServer && IsSpawned)
                RestoreStageStateRpc(snapshot.HeadlampOn, snapshot.HeadlampDrained);
        }

        [Rpc(SendTo.Owner)]
        private void RestoreStageStateRpc(bool on, float drained)
        {
            if (!IsOwner || _state == null)
                return;

            _state.Restore(_state.Max - drained, on);
            Publish(force: true);
        }

        private void Awake()
        {
            if (_settings == null)
            {
                Debug.LogError($"{nameof(PlayerHeadlamp)}: 헤드라이트 설정 에셋이 배선되지 않았습니다.", this);
                enabled = false;
                return;
            }

            if (_motor == null || _look == null)
                Debug.LogError($"{nameof(PlayerHeadlamp)}: PlayerMotor·PlayerLook 이 배선되지 않았습니다.", this);

            // 어둠 판정에서 라이터도 빛으로 친다 — SanityNetworkState 가 헤드라이트를 찾는 방식과 같다.
            _lighter = GetComponent<PlayerLighter>();
            BuildLight();
        }

        public override void OnNetworkSpawn()
        {
            Services.TryGet(out _sceneFlow);

            if (!IsOwner || _settings == null)
                return;

            _state = new HeadlampBattery(
                _settings.MaxBattery,
                _settings.DrainPerSecond,
                _settings.RechargePerSecond);
            Publish(force: true);
        }

        public override void OnNetworkDespawn()
        {
            _state = null;
            _sceneFlow = null;
            IsCharging = false;
            _lowBatterySince = -1f;
            if (_light != null)
                _light.enabled = false;
        }

        private void Update()
        {
            if (!IsSpawned || _settings == null || StageRecoveryGate.Restoring)
                return;

            if (IsOwner && _state != null)
                OwnerTick(Time.deltaTime);

            if (IsServer)
                ServerUpdateDarkness();
        }

        private void LateUpdate()
        {
            if (_light == null)
                return;

            bool on = IsSpawned && IsOn;
            bool low = on && Battery <= _settings.LowBatteryThreshold;
            if (!low)
                _lowBatterySince = -1f;
            else if (_lowBatterySince < 0f)
                _lowBatterySince = Time.time;

            bool blinkDark = low && HeadlampBattery.IsBlinkDark(
                Time.time - _lowBatterySince,
                _settings.LowBatteryBlinkInterval,
                _settings.LowBatteryBlinkCount,
                _settings.BlinkOffSeconds,
                _settings.BlinkOnSeconds);

            _light.enabled = on && !blinkDark;
            if (on)
                FollowHead();
        }

        private void OwnerTick(float deltaTime)
        {
            bool alive = _sanity == null || _sanity.HasSanity;
            bool hidden = IsInHidingPlace();

            // 사망하면 끄고, 은신 중에는 쓸 수 없다(귀신 기획서 §9.3.1 "은신 중 손전등 사용 불가").
            if ((!alive || hidden) && _state.IsOn)
                _state.ForceOff();

            if (_input != null && _input.HeadlampPressedThisFrame)
                PlayToggleSound(_state.Toggle(alive && !hidden));

            IsCharging = alive && DrillCarSafeZone.Contains(transform.position);

            // 배터리가 바닥나 자동으로 꺼질 때는 효과음이 정해져 있지 않다 — 조용히 끈다.
            _state.Tick(deltaTime, IsCharging, IsStageScene());
            Publish(force: false);
        }

        private void Publish(bool force)
        {
            if (_isOn.Value != _state.IsOn)
                _isOn.Value = _state.IsOn;

            if (!force && Time.unscaledTime < _nextBatteryPublishAt)
                return;

            _nextBatteryPublishAt = Time.unscaledTime + BatteryPublishInterval;
            if (!Mathf.Approximately(_battery.Value, _state.Charge))
                _battery.Value = _state.Charge;
        }

        /// <summary>
        /// 정신력 기획서 §4.3 — 어둠 = 자기 헤드라이트와 라이터가 모두 꺼진 상태(라이터는 사용자 확정 2026-09-29,
        /// lighter-system.md). 드릴카 안은 어둠이 아니다. 인게임 로비처럼 스테이지가 아닌 씬에서는 판정하지 않는다.
        /// 결과가 바뀔 때만 서버 API 를 부른다.
        /// </summary>
        private void ServerUpdateDarkness()
        {
            if (_sanity == null || !_sanity.IsSpawned || !_sanity.HasSanity)
                return;

            bool exposed = IsStageScene()
                && !IsOn
                && (_lighter == null || !_lighter.IsLit)
                && !DrillCarSafeZone.Contains(transform.position);
            if (exposed != _sanity.IsDarknessExposed)
                _sanity.ServerSetDarknessExposed(exposed);
        }

        /// <summary>일반 은신처 상자 안이거나, 엎드려 침대 밑 공간에 들어가 있다 — 귀신 판정과 같은 기하다.</summary>
        private bool IsInHidingPlace()
        {
            Vector3 position = transform.position;
            return HidingSpot.Contains(position)
                || (_motor != null && _motor.IsProne && BedHideZone.Contains(position));
        }

        private bool IsStageScene() => _sceneFlow != null && _sceneFlow.Current.IsStage();

        private void PlayToggleSound(HeadlampToggleResult result)
        {
            AudioClip clip = result switch
            {
                HeadlampToggleResult.TurnedOn => _settings.OnClip,
                HeadlampToggleResult.TurnedOff => _settings.OffClip,
                HeadlampToggleResult.Empty => _settings.BrokenClip,
                _ => null,
            };

            if (clip != null && _audio != null)
                _audio.PlayOneShot(clip, _settings.SfxVolume);
        }

        private void FollowHead()
        {
            float eyeHeight = _motor != null ? _motor.CameraLocalHeight : 1.65f;
            Quaternion pitch = Quaternion.Euler(_look != null ? _look.Pitch : 0f, 0f, 0f);
            Transform lamp = _light.transform;
            lamp.localPosition = new Vector3(0f, eyeHeight, 0f) + pitch * _settings.LocalOffset;
            lamp.localRotation = pitch;
        }

        private void BuildLight()
        {
            GameObject lampObject = new("Headlamp");
            lampObject.transform.SetParent(transform, false);

            _light = lampObject.AddComponent<Light>();
            _light.type = LightType.Spot;
            _light.range = _settings.Range;
            _light.spotAngle = _settings.SpotAngle;
            _light.innerSpotAngle = _settings.InnerSpotAngle;
            _light.intensity = _settings.Intensity;
            _light.color = _settings.Color;
            _light.shadows = _settings.Shadows;
            _light.shadowStrength = _settings.ShadowStrength;
            _light.enabled = false;

            // 전원 효과음은 누른 사람에게만 들린다(기획서 §4 — F키 입력 시 재생).
            _audio = lampObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f;
        }
    }
}
