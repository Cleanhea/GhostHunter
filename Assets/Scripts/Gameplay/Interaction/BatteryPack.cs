using System.Collections.Generic;
using GhostHunter.Gameplay.Player;
using GhostHunter.Gameplay.Recovery;
using GhostHunter.Gameplay.Sanity;
using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Gameplay.Interaction
{
    /// <summary>
    /// 드릴카 안의 배터리 팩 — 헤드라이트 배터리를 충전하는 오브젝트(기획서 headlamp-system.md §8). 플레이어가 E를 홀드하면
    /// 팩의 잔량이 헤드라이트로 옮겨 간다. 잔량은 <b>모든 플레이어가 공유</b>하고 서버가 쓴다.
    ///
    /// <para><b>권위.</b> 헤드라이트 배터리는 소유자 권위(<see cref="PlayerHeadlamp"/>)라서, 소유자가 자기가 충전할 양을 일정 간격으로
    /// <see cref="RequestDrawRpc"/> 로 청구하고 서버가 잔량·거리·생존·청구 속도를 확인해 실제로 뺀 양만큼 <see cref="GrantRpc"/> 로 돌려준다.
    /// 소유자는 받은 양만 배터리에 더한다 — 두 사람이 동시에 당겨도 잔량이 음수가 되거나 두 번 쓰이지 않는다(서버가 순서대로 처리).
    /// 잔량이 0이면 아무도 충전할 수 없다.</para>
    ///
    /// <para>표시: 앞면 패널에 남은 %를 글자로 띄운다(레퍼런스 이미지의 "200%"). 글자는 코드로 만든다 — 모든 접속자가 복제된 잔량을 읽는다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class BatteryPack : NetworkBehaviour
    {
        /// <summary>한 번에 청구할 수 있는 시간 여유(초) — 오래 쉰 뒤 한꺼번에 청구해도 이만큼의 충전량까지만 받아 준다.</summary>
        private const float MaxClaimWindow = 0.5f;

        [SerializeField] private BatteryPackSettings _settings;

        [Tooltip("글자를 띄울 앞면 패널 중심(팩 로컬). 팩 앞이 +Z 다.")]
        [SerializeField] private Vector3 _displayLocalPosition = new(0f, 0.125f, 0.134f);

        [SerializeField, Min(0.001f)] private float _displayCharacterSize = 0.0052f;

        private readonly NetworkVariable<float> _remaining = new(0f,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // 서버 — 청구한 플레이어마다 마지막으로 받아 준 시각. 청구 속도를 제한한다.
        private readonly Dictionary<ulong, double> _lastClaimAt = new();

        private TextMesh _display;
        private int _shownPercent = int.MinValue;
        private bool _shownEmpty;

        public BatteryPackSettings Settings => _settings;

        /// <summary>남은 양(%). 스폰 전에는 0.</summary>
        public float Remaining => IsSpawned ? _remaining.Value : 0f;

        public bool IsEmpty => Remaining <= 0.0001f;

        public void CaptureStageState(StageRecoverySnapshot snapshot)
        {
            snapshot.HasBatteryPack = true;
            snapshot.BatteryPackRemaining = _remaining.Value;
        }

        /// <summary>호스트 이전 복구 — 잔량을 되살린다. 팩이 없던 옛 스냅샷(<c>HasBatteryPack</c> 거짓)이면 가득 찬 채로 둔다.</summary>
        public void ServerRestoreStageState(StageRecoverySnapshot snapshot)
        {
            if (IsServer && IsSpawned && snapshot.HasBatteryPack && _settings != null)
                _remaining.Value = Mathf.Clamp(snapshot.BatteryPackRemaining, 0f, _settings.Capacity);
        }

        private void Awake()
        {
            if (_settings == null)
                Debug.LogError($"{nameof(BatteryPack)}: 배터리 팩 설정이 배선되지 않았습니다.", this);
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer && !StageRecoveryGate.Restoring && _settings != null)
                _remaining.Value = _settings.Capacity;

            _remaining.OnValueChanged += HandleRemainingChanged;
            BuildDisplay();
            RefreshDisplay();
        }

        public override void OnNetworkDespawn()
        {
            _remaining.OnValueChanged -= HandleRemainingChanged;
            _lastClaimAt.Clear();
        }

        private void HandleRemainingChanged(float previous, float current) => RefreshDisplay();

        /// <summary>
        /// 소유자 → 서버: 헤드라이트로 <paramref name="amount"/>(%p)를 옮겨 달라는 청구. 서버가 조건을 확인해 실제로 뺀 양을
        /// 발신자에게만 돌려준다. 청구량이 0 이하이거나 거리·생존 조건이 안 맞으면 아무 것도 빼지 않는다.
        /// </summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestDrawRpc(float amount, RpcParams rpcParams = default)
        {
            ulong clientId = rpcParams.Receive.SenderClientId;
            if (!IsServer || _settings == null || float.IsNaN(amount) || amount <= 0f)
                return;

            float granted = ServerDraw(clientId, amount);
            GrantRpc(amount, granted, RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }

        /// <summary>서버 → 소유자: 청구한 양(<paramref name="requested"/>) 중 실제로 받은 양(<paramref name="granted"/>).</summary>
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        private void GrantRpc(float requested, float granted, RpcParams rpcParams = default)
        {
            NetworkManager manager = NetworkManager;
            if (manager == null || manager.LocalClient == null || manager.LocalClient.PlayerObject == null)
                return;

            if (manager.LocalClient.PlayerObject.TryGetComponent(out PlayerBatteryCharger charger))
                charger.OnPackGrant(requested, granted);
        }

        /// <summary>
        /// 서버 전용 — 청구를 검증하고 잔량에서 뺀다. 지난 청구 이후 시간 × 최대 충전 속도(× 여유)를 넘는 청구는 그만큼만 받는다.
        /// 받은 양을 돌려준다(0이면 거절).
        /// </summary>
        public float ServerDraw(ulong clientId, float amount)
        {
            if (!IsServer || !IsSpawned || _settings == null || IsEmpty
                || NetworkManager == null || !NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client)
                || client.PlayerObject == null)
                return 0f;

            NetworkObject player = client.PlayerObject;
            if (player.TryGetComponent(out SanityNetworkState sanity) && !sanity.HasSanity)
                return 0f;
            if (Vector3.Distance(player.transform.position, transform.position) > _settings.UseDistance * 1.5f)
                return 0f;

            double now = Time.timeAsDouble;
            _lastClaimAt.TryGetValue(clientId, out double last);
            double elapsed = last <= 0d ? MaxClaimWindow : System.Math.Min(MaxClaimWindow, now - last);
            _lastClaimAt[clientId] = now;

            float allowance = (float)elapsed * _settings.MaxRatePerSecond * _settings.ServerRateTolerance;
            float granted = Mathf.Min(amount, Mathf.Min(allowance, _remaining.Value));
            if (granted <= 0f)
                return 0f;

            _remaining.Value = Mathf.Max(0f, _remaining.Value - granted);
            return granted;
        }

        /// <summary>소유자 전용 — 충전 컴포넌트가 청구를 보낸다.</summary>
        public void RequestDraw(float amount) => RequestDrawRpc(amount);

        private void BuildDisplay()
        {
            if (_display != null)
                return;

            GameObject textObject = new("BatteryPackDisplay");
            textObject.transform.SetParent(transform, false);
            textObject.transform.localPosition = _displayLocalPosition;
            // TextMesh 는 -Z 에서 본 모습이 정방향이다 — 팩 앞(+Z)에서 읽히도록 뒤집는다.
            textObject.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            _display = textObject.AddComponent<TextMesh>();
            _display.anchor = TextAnchor.MiddleCenter;
            _display.alignment = TextAlignment.Center;
            _display.fontStyle = FontStyle.Bold;
            _display.fontSize = 120;
            _display.characterSize = _displayCharacterSize;

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _display.font = font;
            if (textObject.TryGetComponent(out MeshRenderer meshRenderer) && font != null)
            {
                meshRenderer.sharedMaterial = font.material;
                meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                meshRenderer.receiveShadows = false;
            }
        }

        private void RefreshDisplay()
        {
            if (_display == null || _settings == null)
                return;

            int percent = Mathf.CeilToInt(Remaining - 0.0001f);
            bool empty = IsEmpty;
            if (percent == _shownPercent && empty == _shownEmpty)
                return;

            _shownPercent = percent;
            _shownEmpty = empty;
            _display.text = $"{Mathf.Max(0, percent)}%";
            _display.color = empty ? _settings.EmptyDisplayColor : _settings.DisplayColor;
        }
    }
}
