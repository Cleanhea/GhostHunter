using GhostHunter.Gameplay.Interaction;
using GhostHunter.Gameplay.Sanity;
using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Gameplay.Player
{
    /// <summary>
    /// 배터리 팩에 E를 홀드해 헤드라이트 배터리를 충전한다(기획서 headlamp-system.md §8, 사용자 요청 2026-10-29 — 드릴카 자동 충전을 대체).
    ///
    /// <para>소유자만 돈다. 조준 중인 팩(<see cref="PlayerInteractor.CurrentBatteryPack"/>)이 있고 E를 누르고 있고 팩에 남은 양이 있고
    /// 헤드라이트가 가득 차지 않았을 때만 <see cref="BatteryPackCharging"/> 이 정한 속도로 충전량을 쌓는다. 쌓은 양은
    /// <see cref="BatteryPackSettings.RequestInterval"/> 마다 서버(팩)에 청구하고, 서버가 실제로 뺀 양(<see cref="OnPackGrant"/>)만 배터리에 더한다.
    /// 홀드를 풀거나 조건이 하나라도 깨지면 속도가 처음(기본 속도)으로 돌아간다.</para>
    /// </summary>
    [DefaultExecutionOrder(110)]
    [DisallowMultipleComponent]
    public sealed class PlayerBatteryCharger : NetworkBehaviour
    {
        /// <summary>돌려받지 못한 청구를 잊는 시간(초) — RPC 가 유실돼도 영구히 "청구 중"으로 남지 않게.</summary>
        private const float InFlightTimeout = 2f;

        private PlayerInputReader _input;
        private PlayerInteractor _interactor;
        private PlayerHeadlamp _headlamp;
        private SanityNetworkState _sanity;

        private BatteryPackCharging _charging;
        private float _pending;
        private float _sinceRequest;
        private float _inFlight;
        private float _lastRequestAt;
        private bool _chargingNow;
        private string _prompt = string.Empty;

        /// <summary>지금 충전 중인가(홀드 + 조건 충족).</summary>
        public bool IsChargingNow => _chargingNow;

        /// <summary>지금 초당 충전량(%p). 충전 중이 아니면 0.</summary>
        public float CurrentRate => _chargingNow && _charging != null ? _charging.CurrentRate : 0f;

        private void Awake()
        {
            _input = GetComponent<PlayerInputReader>();
            _interactor = GetComponent<PlayerInteractor>();
            _headlamp = GetComponent<PlayerHeadlamp>();
            _sanity = GetComponent<SanityNetworkState>();
        }

        public override void OnNetworkSpawn()
        {
            if (!IsOwner)
                enabled = false;
        }

        public override void OnNetworkDespawn() => ResetSession();

        private void Update()
        {
            if (!IsSpawned || !IsOwner || _input == null || _headlamp == null)
                return;

            BatteryPack pack = _interactor != null ? _interactor.CurrentBatteryPack : null;
            bool locked = _input.IsGameplayInputLocked || _input.IsDeathInputLocked
                || _input.IsSkillInputLocked || _input.IsWheelInputLocked;
            bool alive = _sanity == null || _sanity.HasSanity;
            bool aiming = pack != null && pack.IsSpawned && pack.Settings != null && !locked && alive
                && Vector3.Distance(transform.position, pack.transform.position) <= pack.Settings.UseDistance * 1.5f;

            if (_inFlight > 0f && Time.unscaledTime - _lastRequestAt > InFlightTimeout)
                _inFlight = 0f;

            float room = _headlamp.MaxBattery - _headlamp.Battery - _inFlight - _pending;
            bool full = _headlamp.MaxBattery - _headlamp.Battery - _inFlight <= 0.0001f;
            bool canCharge = aiming && !pack.IsEmpty && _input.InteractHeld && room > 0.0001f;

            _prompt = aiming ? BuildPrompt(pack, full, canCharge) : string.Empty;

            if (aiming && _charging == null)
                _charging = pack.Settings.CreateCharging();
            _chargingNow = canCharge;

            float amount = _charging != null ? _charging.Tick(Time.deltaTime, canCharge) : 0f;
            _pending += Mathf.Clamp(amount, 0f, Mathf.Max(0f, room));
            _sinceRequest += Time.deltaTime;

            // 홀드를 풀거나 조건이 깨지면 쌓인 것을 바로 청구해 버리고, 충전 중에는 간격마다 청구한다.
            bool flush = !canCharge || _sinceRequest >= (aiming ? pack.Settings.RequestInterval : 0f);
            if (_pending > 0f && flush && pack != null && pack.IsSpawned)
                SendRequest(pack);
            else if (!canCharge && _pending > 0f)
                _pending = 0f;
        }

        /// <summary>팩이 서버 확인 후 돌려준 결과 — <paramref name="requested"/> 청구 중 <paramref name="granted"/> 를 받았다.</summary>
        public void OnPackGrant(float requested, float granted)
        {
            _inFlight = Mathf.Max(0f, _inFlight - requested);
            if (granted > 0f && _headlamp != null)
                _headlamp.ReceiveCharge(granted);
        }

        private void SendRequest(BatteryPack pack)
        {
            float amount = _pending;
            _pending = 0f;
            _sinceRequest = 0f;
            _inFlight += amount;
            _lastRequestAt = Time.unscaledTime;
            pack.RequestDraw(amount);
        }

        private string BuildPrompt(BatteryPack pack, bool full, bool charging)
        {
            if (pack.IsEmpty)
                return "배터리 팩이 비어 있습니다";
            if (charging)
                return $"충전 중  +{_charging.CurrentRate:0}%/초  (팩 {Mathf.CeilToInt(pack.Remaining)}%)";
            if (full)
                return "손전등 배터리가 가득 찼습니다";
            return "E 홀드: 손전등 배터리 충전";
        }

        private void ResetSession()
        {
            _charging?.Reset();
            _pending = 0f;
            _inFlight = 0f;
            _chargingNow = false;
            _prompt = string.Empty;
        }

        private void OnGUI()
        {
            if (!IsOwner || string.IsNullOrEmpty(_prompt))
                return;

            GUI.Label(new Rect((Screen.width - 320f) * 0.5f, Screen.height * 0.62f, 320f, 28f), _prompt);
        }
    }
}
