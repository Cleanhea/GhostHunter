using System;
using GhostHunter.Gameplay.Player;
using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Gameplay.Cleaning
{
    /// <summary>얼룩의 배치·청소 상태를 서버에서 복제하고 닦이는 연출을 재생한다.</summary>
    [DisallowMultipleComponent]
    public sealed class CleaningStain : NetworkBehaviour
    {
        private struct State : INetworkSerializable, IEquatable<State>
        {
            internal Vector3 Position;
            internal float Yaw;
            internal uint Revision;
            internal bool Placed;
            internal bool Cleaned;

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref Position);
                serializer.SerializeValue(ref Yaw);
                serializer.SerializeValue(ref Revision);
                serializer.SerializeValue(ref Placed);
                serializer.SerializeValue(ref Cleaned);
            }

            public bool Equals(State other) => Position == other.Position && Yaw == other.Yaw
                && Revision == other.Revision && Placed == other.Placed && Cleaned == other.Cleaned;
        }

        private static readonly int WipeId = Shader.PropertyToID("_Wipe");
        [SerializeField] private CleaningSettings _settings;
        [SerializeField] private Renderer _visual;
        [SerializeField] private Collider _hitCollider;
        [SerializeField] private DetectionTargetMarker _marker;
        private readonly NetworkVariable<State> _state = new(default,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private MaterialPropertyBlock _properties;
        private float _wipeElapsed;
        private bool _wiping;

        public bool IsDirty => IsSpawned && _state.Value.Placed && !_state.Value.Cleaned;
        public uint Revision => _state.Value.Revision;
        public Collider HitCollider => _hitCollider;

        private void Awake()
        {
            _properties = new MaterialPropertyBlock();
            SetVisible(false);
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
                _state.Value = default;
            _state.OnValueChanged += HandleChanged;
            Apply(false);
        }

        public override void OnNetworkDespawn()
        {
            _state.OnValueChanged -= HandleChanged;
            _wiping = false;
            SetVisible(false);
        }

        private void Update()
        {
            if (!_wiping)
                return;
            _wipeElapsed += Time.deltaTime;
            float amount = Mathf.Clamp01(_wipeElapsed / _settings.WipeSeconds);
            SetWipe(amount);
            if (amount < 1f)
                return;
            _wiping = false;
            _visual.enabled = false;
        }

        /// <summary>서버가 새로운 세대의 얼룩 위치를 지정하거나 풀에 보관한다.</summary>
        public void ServerReset(Vector3 position, float yaw, uint revision, bool placed)
        {
            if (!IsServer || !IsSpawned)
                return;
            _state.Value = new State
            {
                Position = position, Yaw = yaw, Revision = revision, Placed = placed,
            };
        }

        /// <summary>동일 배치 세대의 얼룩을 한 번만 청소한다. 이전 세대 요청은 무시한다.</summary>
        public bool ServerClean(uint revision)
        {
            if (!IsServer || !IsDirty || Revision != revision)
                return false;
            State state = _state.Value;
            state.Cleaned = true;
            _state.Value = state;
            return true;
        }

        private void HandleChanged(State previous, State current)
        {
            Apply(previous.Placed && !previous.Cleaned && current.Cleaned
                && previous.Revision == current.Revision);
        }

        private void Apply(bool animate)
        {
            State state = _state.Value;
            if (state.Placed)
                transform.SetPositionAndRotation(state.Position, Quaternion.Euler(0f, state.Yaw, 0f));
            _wiping = animate && _settings != null;
            _wipeElapsed = 0f;
            SetVisible(state.Placed && !state.Cleaned);
            if (_wiping && _visual != null)
                _visual.enabled = true;
            SetWipe(state.Cleaned && !_wiping ? 1f : 0f);
        }

        private void SetVisible(bool visible)
        {
            if (_visual != null)
                _visual.enabled = visible;
            if (_hitCollider != null)
                _hitCollider.enabled = visible;
            if (_marker != null)
                _marker.SetTargetActive(visible);
        }

        private void SetWipe(float amount)
        {
            if (_visual == null || _properties == null)
                return;
            _properties.SetFloat(WipeId, amount);
            _visual.SetPropertyBlock(_properties);
        }
    }
}
