using System.Collections.Generic;
using GhostHunter.Core;
using GhostHunter.Core.Steam;
using GhostHunter.Gameplay.Cleaning;
using GhostHunter.Gameplay.Furniture;
using GhostHunter.Gameplay.FurnitureDriver;
using GhostHunter.Gameplay.Interaction;
using GhostHunter.Gameplay.Map;
using GhostHunter.Gameplay.Player;
using GhostHunter.Gameplay.Sanity;
using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Gameplay.Revival
{
    public enum CandleSlotState : byte
    {
        Empty = 0,
        Placed = 1,
        Lit = 2,
        /// <summary>실패 촛불이 판정 1초 뒤 꺼진 상태.</summary>
        Out = 3,
    }

    /// <summary>
    /// 부활 의식(revival-system.md). 서버가 스테이지마다 문이 있는 방 하나를 무작위로 골라 <b>의식 방</b>으로 만든다 —
    /// 랜덤 가구·얼룩 배치에서 빼고, 고정 가구를 숨겨 치운 뒤 가운데에 마법진, 오망성 꼭짓점마다 촛대 5개를 세운다
    /// (2026-09-30 사용자 요청 "방 싹 비우고 부활 의식 할 것처럼"). 촛불 배치·점화·판정·부활은 서버가 확정하고,
    /// 각 피어는 복제된 위치·촛불 상태로 로컬 표시물을 만든다.
    ///
    /// <list type="number">
    /// <item>빈 촛대에서 E — 팀 촛불 재고에서 1개를 꺼내 꽂는다(꽂는 순간 소모).</item>
    /// <item>다섯 자리가 다 차고 시체 1구가 소환진 가운데 있으면, 놓인 촛불에서 E 로 점화 미니게임을 연다.
    /// 커서가 바를 한 번 지나가고 랜덤 초록 구간에서 E 를 다시 누르면 성공, 지나서 누르거나 안 누르면 실패.
    /// 실패도 똑같이 켜진다 — 결과는 다섯 개가 다 켜진 뒤 공개한다.</item>
    /// <item>점화 중 이동·사망·메뉴, 촛불이 켜진 채 생존자가 모두 소환진 방을 떠남, 귀신이 그 방 문을 엶 →
    /// 켜진 촛불을 모두 끄고 진행도를 초기화한다(놓인 촛불·시체는 그대로). 최종 판정이 시작되면 중단되지 않는다.</item>
    /// <item>실패 촛불은 1초 뒤 꺼진다. 실패 0개면 정상, 아니면 98% 폐급 4종 / 2% 개좃같은 폐급으로 부활 —
    /// 소환진 가운데, 3초 보호.</item>
    /// </list>
    ///
    /// <para>씬 NetworkObject(<c>RandomFurniture/Controller</c>) 의 자식 컴포넌트다 — 씬 NetworkObject 를 새로 넣지 않으려고
    /// (unity-assets.md §5.2). 호스트 이전 스냅샷은 의식 진행을 복원하지 않는다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RevivalRitual : NetworkBehaviour
    {
        private const int SlotBits = 2;
        private const float RoomCheckInterval = 0.25f;
        private const float IgnitionTimeoutPadding = 3f;
        private const float MessageSeconds = 4f;
        private const float StatusDistance = 5f;

        [SerializeField] private RevivalRitualSettings _settings;
        [Tooltip("방(RoomDimensions 를 가진 오브젝트)을 찾을 집 루트.")]
        [SerializeField] private Transform _houseRoot;
        [Tooltip("가구 배치가 끝난 뒤 빈자리를 찾는다.")]
        [SerializeField] private FurnitureSpawnController _furniture;

        private readonly NetworkVariable<int> _roomIndex = new(-1);
        private readonly NetworkVariable<Vector3> _center = new(Vector3.zero);
        private readonly NetworkVariable<int> _slots = new(0);
        private readonly NetworkVariable<int> _candleStock = new(0);
        private readonly NetworkVariable<bool> _judging = new(false);

        private struct Room
        {
            public Transform Root;
            public BoxCollider Bounds;
            public DoorInteractable[] Doors;
        }

        private struct Ignition
        {
            public bool Active;
            public ulong Client;
            public float StartedAt;
            public float ZoneStart;
        }

        private readonly List<Room> _rooms = new();
        private readonly bool[] _failed = new bool[RevivalRules.CandleCount];
        private readonly Ignition[] _ignitions = new Ignition[RevivalRules.CandleCount];
        private readonly SanityNetworkState[] _players = new SanityNetworkState[4];

        // 서버
        private ISanityTeamService _team;
        private IStageShopService _shop;
        private int _devStock;
        private int _chosenRoom = -1;
        private float _roomCheckRemaining;
        private float _judgeElapsed;
        private bool _failedCandlesOut;
        private SanityNetworkState _revivee;

        // 각 피어 — 표시물·미니게임
        private GameObject _visualRoot;
        private GameObject[] _candles;
        private GameObject[] _flames;
        private ILocalPlayerContext _localPlayer;
        private bool _localIgniting;
        private int _localSlot;
        private float _localStartedAt;
        private float _localZoneStart;
        private string _message = string.Empty;
        private float _messageUntil;
        private static GUIStyle _centered;

        public bool IsPlaced => _roomIndex.Value >= 0;
        public Vector3 Center => _center.Value;
        public int CandleStock => _candleStock.Value;
        public bool IsJudging => _judging.Value;

        /// <summary>소환진이 놓인 방 이름. 없으면 빈 문자열.</summary>
        public string RoomName => IsPlaced && _roomIndex.Value < _rooms.Count
            ? _rooms[_roomIndex.Value].Root.name : string.Empty;

        /// <summary>의식 방 후보 수 — 문이 있고 마법진·촛대가 들어가는 크기의 방(스테이지 배선 검사용).</summary>
        public int EligibleRoomCount
        {
            get
            {
                if (_rooms.Count == 0 && _houseRoot != null)
                    CollectRooms();
                int count = 0;
                foreach (Room room in _rooms)
                    if (IsEligible(room))
                        count++;
                return count;
            }
        }

        /// <summary>그 위치가 의식 방 안인가. 방이 정해지지 않았으면 false.</summary>
        public bool ContainsInRoom(Vector3 position)
        {
            int room = IsPlaced ? _roomIndex.Value : _chosenRoom;
            return room >= 0 && room < _rooms.Count && IsInsideRoom(_rooms[room].Bounds, position);
        }

        private bool IsEligible(Room room)
        {
            float minSize = _settings != null ? _settings.MinRoomSize : 4f;
            Vector3 size = room.Bounds.size;
            return room.Doors.Length > 0 && size.x >= minSize && size.z >= minSize;
        }

        public CandleSlotState GetSlot(int index) =>
            (CandleSlotState)((_slots.Value >> (index * SlotBits)) & ((1 << SlotBits) - 1));

        public Vector3 SlotPosition(int index)
        {
            float angle = index * Mathf.PI * 2f / RevivalRules.CandleCount;
            float radius = _settings != null ? _settings.CandleRingRadius : 1.1f;
            return _center.Value + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
        }

        private void Awake()
        {
            if (_settings == null || _houseRoot == null)
            {
                Debug.LogError($"{nameof(RevivalRitual)}: 설정·집 루트가 배선되지 않았습니다.", this);
                enabled = false;
                return;
            }

            CollectRooms();
        }

        /// <summary>방 목록을 계층 순서로 모은다 — 모든 피어가 같은 씬이라 같은 순서다(방 번호로 복제).</summary>
        private void CollectRooms()
        {
            _rooms.Clear();
            foreach (BoxCollider box in _houseRoot.GetComponentsInChildren<BoxCollider>(true))
            {
                if (box.name != "RoomDimensions" || box.transform.parent == null)
                    continue;
                Transform root = box.transform.parent;
                _rooms.Add(new Room
                {
                    Root = root,
                    Bounds = box,
                    Doors = root.GetComponentsInChildren<DoorInteractable>(true),
                });
            }
        }

        public override void OnNetworkSpawn()
        {
            _roomIndex.OnValueChanged += HandlePlacementChanged;
            _center.OnValueChanged += HandleCenterChanged;
            _slots.OnValueChanged += HandleSlotsChanged;
            Services.TryGet(out _localPlayer);

            if (IsServer)
            {
                Services.TryGet(out _team);
                Services.TryGet(out _shop);
                _devStock = _settings != null ? _settings.DevCandleStock : 0;
                RefreshStock();
                DoorInteractable.ServerOpenedByGhost += HandleGhostOpenedDoor;
                if (!IsPlaced)
                {
                    if (_furniture != null && !_furniture.IsReady)
                        _furniture.ServerPreparingLayout += ServerChooseRoom;
                    else
                        ServerChooseRoom(_furniture != null ? _furniture.GenerationSeed : 0);
                }
            }

            if (IsPlaced)
                BuildVisuals();
        }

        public override void OnNetworkDespawn()
        {
            _roomIndex.OnValueChanged -= HandlePlacementChanged;
            _center.OnValueChanged -= HandleCenterChanged;
            _slots.OnValueChanged -= HandleSlotsChanged;
            DoorInteractable.ServerOpenedByGhost -= HandleGhostOpenedDoor;
            if (_furniture != null)
                _furniture.ServerPreparingLayout -= ServerChooseRoom;
            _localIgniting = false;
            if (_visualRoot != null)
                Destroy(_visualRoot);
            _visualRoot = null;
        }

        private void Update()
        {
            if (!IsSpawned || _settings == null)
                return;

            if (IsServer)
                ServerTick(Time.deltaTime);
            if (_localIgniting)
                TickLocalIgnition();
        }

        // ───────────────────────── 서버 ─────────────────────────

        private void ServerTick(float deltaTime)
        {
            if (!IsPlaced)
            {
                if (_chosenRoom >= 0 && (_furniture == null || _furniture.IsReady))
                    ServerPrepareRoom();
                return;
            }

            if (_judging.Value)
            {
                TickJudging(deltaTime);
                return;
            }

            for (int i = 0; i < _ignitions.Length; i++)
            {
                if (!_ignitions[i].Active)
                    continue;
                if (!TryGetAlivePlayer(_ignitions[i].Client, out _))
                {
                    ServerResetProgress("점화하던 사람이 쓰러져 촛불이 모두 꺼졌습니다.");
                    return;
                }
                if (Time.time - _ignitions[i].StartedAt > _settings.BarSeconds + IgnitionTimeoutPadding)
                    _ignitions[i].Active = false;
            }

            _roomCheckRemaining -= deltaTime;
            if (_roomCheckRemaining > 0f)
                return;
            _roomCheckRemaining = RoomCheckInterval;
            if (AnyLit() && !AnyAlivePlayerInRoom())
                ServerResetProgress("모두 소환진 방을 떠나 촛불이 모두 꺼졌습니다.");
        }

        /// <summary>
        /// 방 이동이 끝난 뒤(가구·얼룩 배치 전), 작업 가구를 보존하며 비울 수 있는 방을 무작위로 고른다.
        /// </summary>
        private void ServerChooseRoom(int seed)
        {
            if (!IsServer || IsPlaced || _chosenRoom >= 0)
                return;
            var candidates = new List<int>();
            for (int i = 0; i < _rooms.Count; i++)
                if (IsEligible(_rooms[i]))
                    candidates.Add(i);
            if (candidates.Count == 0)
            {
                Debug.LogError($"{nameof(RevivalRitual)}: 의식 방 후보가 없습니다(문이 있고 {_settings.MinRoomSize}m 이상).", this);
                enabled = false;
                return;
            }

            for (int i = 0; i < candidates.Count - 1; i++)
            {
                int other = Random.Range(i, candidates.Count);
                (candidates[i], candidates[other]) = (candidates[other], candidates[i]);
            }
            foreach (int candidate in candidates)
            {
                BoxCollider bounds = _rooms[candidate].Bounds;
                bool Contains(Vector3 position) => IsInsideRoom(bounds, position);
                if (_furniture != null && !_furniture.TryBuildPlan(seed, Contains, out _, out _, out _))
                    continue;

                _chosenRoom = candidate;
                if (_furniture != null)
                    _furniture.ServerExcludeArea(Contains);
                if (Services.TryGet(out ICleaningService cleaning))
                    cleaning.ServerExcludeArea(Contains);
                return;
            }

            Debug.LogError($"{nameof(RevivalRitual)}: 작업 가구 배치를 보존하며 비울 수 있는 의식 방이 없습니다 (seed {seed}).", this);
            enabled = false;
        }

        /// <summary>
        /// 가구 배치가 끝난 뒤 의식 방에 남은 가구를 모두 치우고 방 가운데 바닥에 소환진을 놓는다. 분해 풀 가구는 비활성화
        /// (조립이 나중에 다시 꺼내 쓸 수 있다), 나머지 고정 가구는 보관(숨김·물리 정지)한다.
        /// </summary>
        private void ServerPrepareRoom()
        {
            Room room = _rooms[_chosenRoom];
            Physics.SyncTransforms();
            int cleared = 0;
            foreach (FurnitureNetworkPhysics furniture in FindObjectsByType<FurnitureNetworkPhysics>(FindObjectsSortMode.None))
            {
                if (furniture == null || furniture.gameObject.scene != gameObject.scene || !furniture.IsAvailable
                    || !IsInsideRoom(room.Bounds, furniture.transform.position))
                    continue;

                RandomFurnitureItem random = furniture.GetComponent<RandomFurnitureItem>();
                FurnitureDriverPoolItem pool = furniture.GetComponent<FurnitureDriverPoolItem>();
                if (random != null)
                {
                    random.ServerPark();
                }
                else if (pool != null)
                {
                    pool.ServerDeactivate();
                }
                else
                {
                    furniture.ServerStow();
                }
                cleared++;
            }

            Vector3 center = room.Bounds.transform.TransformPoint(room.Bounds.center);
            if (Physics.Raycast(center + Vector3.up * 1.5f, Vector3.down, out RaycastHit floor, 3f, FloorMask(),
                    QueryTriggerInteraction.Ignore))
                center = floor.point;

            _center.Value = center;
            _roomIndex.Value = _chosenRoom;
            Debug.Log($"[RevivalRitual] 의식 방 — {room.Root.name} {center}, 가구 {cleared}개 치움", this);
        }

        private static int FloorMask()
        {
            int mask = ~0;
            if (GameLayers.Player >= 0)
                mask &= ~(1 << GameLayers.Player);
            if (GameLayers.GhostPrototype >= 0)
                mask &= ~(1 << GameLayers.GhostPrototype);
            if (GameLayers.Furniture >= 0)
                mask &= ~(1 << GameLayers.Furniture);
            return mask;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void InteractRpc(int slot, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (!IsPlaced || _judging.Value || slot < 0 || slot >= RevivalRules.CandleCount
                || !TryGetAlivePlayer(sender, out SanityNetworkState player)
                || Vector3.Distance(player.transform.position, SlotPosition(slot)) > _settings.InteractDistance)
                return;

            switch (GetSlot(slot))
            {
                case CandleSlotState.Empty:
                    if (ServerConsumeCandle())
                        SetSlot(slot, CandleSlotState.Placed);
                    else
                        NotifyRpc("촛불이 없습니다 — 인게임 로비 상점에서 촛대 세트를 사세요.", Target(sender));
                    break;

                case CandleSlotState.Placed:
                    ServerTryBeginIgnition(slot, sender);
                    break;
            }
        }

        private void ServerTryBeginIgnition(int slot, ulong sender)
        {
            if (AnyEmpty())
            {
                NotifyRpc("촛대 5개에 촛불을 모두 꽂아야 점화할 수 있습니다.", Target(sender));
                return;
            }

            int corpses = CountCorpsesInCircle(out _);
            if (corpses != 1)
            {
                NotifyRpc(corpses == 0 ? "시체를 소환진 가운데에 놓아야 합니다."
                    : "한 번에 시체 1구만 부활시킬 수 있습니다.", Target(sender));
                return;
            }

            for (int i = 0; i < _ignitions.Length; i++)
                if (_ignitions[i].Active && (i == slot || _ignitions[i].Client == sender))
                    return;

            float zone = Random.Range(_settings.ZoneMinStart, Mathf.Max(_settings.ZoneMinStart, 1f - _settings.ZoneWidth));
            _ignitions[slot] = new Ignition { Active = true, Client = sender, StartedAt = Time.time, ZoneStart = zone };
            BeginIgnitionRpc(slot, zone, Target(sender));
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void SubmitIgnitionRpc(int slot, float press, RpcParams rpcParams = default)
        {
            if (slot < 0 || slot >= RevivalRules.CandleCount || _judging.Value)
                return;
            Ignition ignition = _ignitions[slot];
            if (!ignition.Active || ignition.Client != rpcParams.Receive.SenderClientId
                || GetSlot(slot) != CandleSlotState.Placed)
                return;

            float elapsed = Time.time - ignition.StartedAt;
            if (!RevivalRules.IsPlausiblePress(press, elapsed, _settings.BarSeconds, _settings.PressToleranceSeconds))
                press = -1f;

            _ignitions[slot].Active = false;
            _failed[slot] = !RevivalRules.IsTimingSuccess(press, ignition.ZoneStart, _settings.ZoneWidth);
            SetSlot(slot, CandleSlotState.Lit);

            if (AllLit())
                ServerStartJudging();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void CancelIgnitionRpc(int slot, RpcParams rpcParams = default)
        {
            if (slot < 0 || slot >= RevivalRules.CandleCount || _judging.Value)
                return;
            if (_ignitions[slot].Active && _ignitions[slot].Client == rpcParams.Receive.SenderClientId)
                ServerResetProgress("점화가 끊겨 촛불이 모두 꺼졌습니다.");
        }

        private void ServerStartJudging()
        {
            if (CountCorpsesInCircle(out SanityNetworkState revivee) != 1)
            {
                ServerResetProgress("시체가 소환진을 벗어나 촛불이 모두 꺼졌습니다.");
                return;
            }

            _revivee = revivee;
            _judgeElapsed = 0f;
            _failedCandlesOut = false;
            _judging.Value = true;
        }

        private void TickJudging(float deltaTime)
        {
            _judgeElapsed += deltaTime;
            if (!_failedCandlesOut && _judgeElapsed >= _settings.FailedCandleOutSeconds)
            {
                _failedCandlesOut = true;
                for (int i = 0; i < _failed.Length; i++)
                    if (_failed[i])
                        SetSlot(i, CandleSlotState.Out);
            }

            if (_judgeElapsed >= _settings.ReviveDelaySeconds)
                ServerFinishRevival();
        }

        private void ServerFinishRevival()
        {
            int failedCount = 0;
            for (int i = 0; i < _failed.Length; i++)
                if (_failed[i])
                    failedCount++;

            DefectKind kind = RevivalRules.Judge(failedCount, Random.value, Random.Range(0, int.MaxValue));
            SanityNetworkState player = _revivee;
            string message;
            if (player != null && player.IsSpawned && !player.HasSanity && player.ServerRevive())
            {
                player.ServerGrantProtection(_settings.ProtectionSeconds);
                PlayerMotor motor = player.GetComponent<PlayerMotor>();
                if (motor != null)
                    motor.ServerTeleport(_center.Value + Vector3.up * 0.05f,
                        Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                // 폐급은 스테이지 끝까지 간다 — 다시 정상으로 부활해도 풀지 않는다(§8).
                if (kind != DefectKind.None)
                    player.GetComponent<PlayerDefectState>()?.ServerApply(kind);
                message = $"{NameOf(player)} 부활 — {RevivalRules.DisplayName(kind)}";
            }
            else
            {
                message = "부활하지 못했습니다.";
            }

            _revivee = null;
            for (int i = 0; i < _failed.Length; i++)
            {
                _failed[i] = false;
                _ignitions[i].Active = false;
            }
            _slots.Value = 0;
            _judging.Value = false;
            AnnounceRpc(message);
        }

        /// <summary>켜진 촛불을 모두 끄고 타이밍 결과를 지운다. 놓인 촛불·시체는 그대로. 판정이 시작됐으면 무시한다.</summary>
        private void ServerResetProgress(string reason)
        {
            if (_judging.Value)
                return;

            int slots = _slots.Value;
            for (int i = 0; i < RevivalRules.CandleCount; i++)
            {
                _failed[i] = false;
                if (_ignitions[i].Active)
                {
                    StopIgnitionRpc(Target(_ignitions[i].Client));
                    _ignitions[i].Active = false;
                }
                if (GetSlot(i) is CandleSlotState.Lit or CandleSlotState.Out)
                    slots = WithSlot(slots, i, CandleSlotState.Placed);
            }
            _slots.Value = slots;
            AnnounceRpc(reason);
        }

        private void HandleGhostOpenedDoor(DoorInteractable door)
        {
            if (!IsServer || !IsPlaced || _judging.Value || door == null
                || _roomIndex.Value >= _rooms.Count
                || !door.transform.IsChildOf(_rooms[_roomIndex.Value].Root))
                return;
            if (AnyLit() || AnyIgniting())
                ServerResetProgress("귀신이 소환진 방의 문을 열어 촛불이 모두 꺼졌습니다.");
        }

        private bool ServerConsumeCandle()
        {
            bool consumed;
            if (_shop != null && _shop.IsAvailable)
            {
                consumed = _shop.TryConsumeCandle();
            }
            else
            {
                consumed = _devStock > 0;
                if (consumed)
                    _devStock--;
            }
            RefreshStock();
            return consumed;
        }

        private void RefreshStock()
        {
            _candleStock.Value = _shop != null && _shop.IsAvailable ? _shop.CandleCount : _devStock;
        }

        private int CountCorpsesInCircle(out SanityNetworkState single)
        {
            single = null;
            if (_team == null)
                return 0;

            int count = 0;
            int players = _team.CopyPlayerStates(_players);
            for (int i = 0; i < players; i++)
            {
                SanityNetworkState player = _players[i];
                if (player == null || !player.IsSpawned || player.HasSanity)
                    continue;
                PlayerVisuals visuals = player.GetComponent<PlayerVisuals>();
                if (visuals == null || !visuals.HasCorpse)
                    continue;

                Vector3 offset = visuals.CorpsePosition - _center.Value;
                if (Mathf.Abs(offset.y) > 1.5f)
                    continue;
                offset.y = 0f;
                if (offset.magnitude > _settings.CorpseRadius)
                    continue;

                count++;
                single = player;
            }

            if (count != 1)
                single = null;
            return count;
        }

        private bool AnyAlivePlayerInRoom()
        {
            if (_team == null || _roomIndex.Value >= _rooms.Count)
                return true;
            BoxCollider bounds = _rooms[_roomIndex.Value].Bounds;
            int players = _team.CopyPlayerStates(_players);
            for (int i = 0; i < players; i++)
            {
                SanityNetworkState player = _players[i];
                if (player != null && player.IsSpawned && player.HasSanity
                    && IsInsideRoom(bounds, player.transform.position))
                    return true;
            }
            return false;
        }

        /// <summary>방 범위 상자(바닥 높이 ±0.5m)의 수평 안쪽이고, 바닥에서 2.5m 안의 높이인가.</summary>
        internal static bool IsInsideRoom(BoxCollider bounds, Vector3 position)
        {
            Vector3 local = bounds.transform.InverseTransformPoint(position) - bounds.center;
            Vector3 half = bounds.size * 0.5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.z) <= half.z
                && local.y >= -1f && local.y <= 2.5f;
        }

        private bool TryGetAlivePlayer(ulong clientId, out SanityNetworkState player)
        {
            player = null;
            if (NetworkManager == null
                || !NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client)
                || client.PlayerObject == null)
                return false;
            player = client.PlayerObject.GetComponent<SanityNetworkState>();
            return player != null && player.HasSanity;
        }

        private static string NameOf(SanityNetworkState player)
        {
            PlayerNameTag tag = player.GetComponent<PlayerNameTag>();
            return tag != null && !string.IsNullOrEmpty(tag.DisplayName)
                ? tag.DisplayName : $"플레이어 {player.OwnerClientId}";
        }

        private RpcParams Target(ulong clientId) => RpcTarget.Single(clientId, RpcTargetUse.Temp);

        private void SetSlot(int index, CandleSlotState state) => _slots.Value = WithSlot(_slots.Value, index, state);

        private static int WithSlot(int packed, int index, CandleSlotState state)
        {
            int shift = index * SlotBits;
            int mask = ((1 << SlotBits) - 1) << shift;
            return (packed & ~mask) | ((int)state << shift);
        }

        private bool AnyEmpty()
        {
            for (int i = 0; i < RevivalRules.CandleCount; i++)
                if (GetSlot(i) == CandleSlotState.Empty)
                    return true;
            return false;
        }

        private bool AnyLit()
        {
            for (int i = 0; i < RevivalRules.CandleCount; i++)
                if (GetSlot(i) is CandleSlotState.Lit or CandleSlotState.Out)
                    return true;
            return false;
        }

        private bool AllLit()
        {
            for (int i = 0; i < RevivalRules.CandleCount; i++)
                if (GetSlot(i) != CandleSlotState.Lit)
                    return false;
            return true;
        }

        private bool AnyIgniting()
        {
            for (int i = 0; i < _ignitions.Length; i++)
                if (_ignitions[i].Active)
                    return true;
            return false;
        }

        // ───────────────────────── 각 피어 ─────────────────────────

        /// <summary>촛불 자리 E — <see cref="RevivalCandleSlot"/> 이 부른다. 점화 미니게임 중이면 그 입력은 미니게임 몫이다.</summary>
        public void RequestInteract(int index)
        {
            if (IsSpawned && !_localIgniting)
                InteractRpc(index);
        }

        public string PromptFor(int index)
        {
            if (!IsPlaced || _judging.Value || _localIgniting)
                return string.Empty;
            return GetSlot(index) switch
            {
                CandleSlotState.Empty => _candleStock.Value > 0
                    ? $"E: 촛대에 촛불 꽂기 (남은 촛불 {_candleStock.Value}개)"
                    : "촛불이 없습니다",
                CandleSlotState.Placed => "E: 촛불 점화",
                _ => string.Empty,
            };
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        private void BeginIgnitionRpc(int slot, float zoneStart, RpcParams rpcParams = default)
        {
            _localIgniting = true;
            _localSlot = slot;
            _localStartedAt = Time.time;
            _localZoneStart = zoneStart;
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        private void StopIgnitionRpc(RpcParams rpcParams = default) => _localIgniting = false;

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        private void NotifyRpc(string message, RpcParams rpcParams = default) => ShowMessage(message);

        [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
        private void AnnounceRpc(string message) => ShowMessage(message);

        private void ShowMessage(string message)
        {
            _message = message;
            _messageUntil = Time.time + MessageSeconds;
        }

        /// <summary>
        /// 점화 미니게임 — 커서가 0→1 로 한 번 지나간다. 초록 구간 전에 누른 E 는 무시, 구간 안이면 성공, 지나서 누르거나
        /// 끝까지 안 누르면 늦음(실패). 결과는 서버에만 보낸다 — 화면에는 성공·실패를 알리지 않는다(§6).
        /// 점화 중 움직이거나 메뉴를 열거나 죽으면 중단이다.
        /// </summary>
        private void TickLocalIgnition()
        {
            PlayerInputReader input = _localPlayer != null ? _localPlayer.Input : null;
            if (input == null || input.Move.sqrMagnitude > 0.01f
                || input.IsGameplayInputLocked || input.IsDeathInputLocked)
            {
                _localIgniting = false;
                CancelIgnitionRpc(_localSlot);
                return;
            }

            float cursor = (Time.time - _localStartedAt) / _settings.BarSeconds;
            if (cursor > 1f)
            {
                _localIgniting = false;
                SubmitIgnitionRpc(_localSlot, -1f);
                return;
            }

            if (input.InteractPressedThisFrame && cursor >= _localZoneStart)
            {
                _localIgniting = false;
                SubmitIgnitionRpc(_localSlot, cursor);
            }
        }

        private void HandlePlacementChanged(int previous, int current)
        {
            if (current >= 0)
                BuildVisuals();
        }

        private void HandleCenterChanged(Vector3 previous, Vector3 current)
        {
            if (IsPlaced)
                BuildVisuals();
        }

        private void HandleSlotsChanged(int previous, int current) => RefreshVisuals();

        private const float StandHeight = 0.95f;
        private const float CandleLength = 0.2f;
        private const float CandleTop = StandHeight + CandleLength;

        /// <summary>
        /// 바닥 마법진(빛나는 오망성)·가운데 붉은 빛·오망성 꼭짓점마다 철제 촛대(E 대상 콜라이더)·촛불·불꽃을 로컬로 만든다.
        /// 모델 에셋이 들어오면 교체한다.
        /// </summary>
        private void BuildVisuals()
        {
            if (_visualRoot != null)
                Destroy(_visualRoot);

            // 표시물은 방 바닥 마감(RoomFloor) 윗면에 얹는다 — 충돌체 바닥보다 4cm 높아서 거기 그리면 마감 밑에 묻힌다.
            _visualRoot = new GameObject("RevivalCircleVisual");
            _visualRoot.transform.SetParent(transform, false);
            _visualRoot.transform.SetPositionAndRotation(_center.Value + Vector3.up * FloorFinishHeight(), Quaternion.identity);

            // 마법진 — 바닥에 눕힌 스프라이트. 스프라이트 기본 재질은 빛을 받지 않아 어두운 방에서도 은은히 빛난다.
            // 스프라이트 1 단위 = 텍스처 반 폭이므로 반경만큼 키우면 지름이 맞는다.
            var circle = new GameObject("MagicCircle");
            circle.transform.SetParent(_visualRoot.transform, false);
            circle.transform.SetLocalPositionAndRotation(new Vector3(0f, 0.012f, 0f), Quaternion.Euler(90f, 0f, 0f));
            circle.transform.localScale = Vector3.one * _settings.CircleRadius;
            SpriteRenderer sprite = circle.AddComponent<SpriteRenderer>();
            sprite.sprite = RevivalCircleTexture.Sprite;
            sprite.color = new Color(1f, 1f, 1f, 0.9f);

            var glow = new GameObject("CircleGlow");
            glow.transform.SetParent(_visualRoot.transform, false);
            glow.transform.localPosition = new Vector3(0f, 0.35f, 0f);
            Light glowLight = glow.AddComponent<Light>();
            glowLight.type = LightType.Point;
            glowLight.color = new Color(0.9f, 0.1f, 0.08f);
            glowLight.range = _settings.CircleRadius * 2.6f;
            glowLight.intensity = 0.5f;
            glowLight.shadows = LightShadows.None;

            _candles = new GameObject[RevivalRules.CandleCount];
            _flames = new GameObject[RevivalRules.CandleCount];
            Color iron = new(0.09f, 0.08f, 0.07f);
            for (int i = 0; i < RevivalRules.CandleCount; i++)
            {
                var stand = new GameObject($"Candlestick_{i}");
                stand.transform.SetParent(_visualRoot.transform, false);
                stand.transform.position = SlotPosition(i) + Vector3.up * (_visualRoot.transform.position.y - _center.Value.y);
                BoxCollider hit = stand.AddComponent<BoxCollider>();
                hit.size = new Vector3(0.32f, CandleTop + 0.3f, 0.32f);
                hit.center = new Vector3(0f, (CandleTop + 0.3f) * 0.5f, 0f);
                stand.AddComponent<RevivalCandleSlot>().Bind(this, i);

                // 받침 → 고리 → 기둥 → 매듭 → 촛농 접시.
                Primitive(PrimitiveType.Cylinder, stand.transform, "Foot",
                    new Vector3(0f, 0.02f, 0f), new Vector3(0.3f, 0.02f, 0.3f), iron);
                Primitive(PrimitiveType.Cylinder, stand.transform, "FootRing",
                    new Vector3(0f, 0.06f, 0f), new Vector3(0.16f, 0.03f, 0.16f), iron);
                Primitive(PrimitiveType.Cylinder, stand.transform, "Stem",
                    new Vector3(0f, StandHeight * 0.5f, 0f), new Vector3(0.045f, StandHeight * 0.5f, 0.045f), iron);
                Primitive(PrimitiveType.Sphere, stand.transform, "Knot",
                    new Vector3(0f, StandHeight * 0.55f, 0f), new Vector3(0.08f, 0.06f, 0.08f), iron);
                Primitive(PrimitiveType.Cylinder, stand.transform, "Dish",
                    new Vector3(0f, StandHeight, 0f), new Vector3(0.17f, 0.012f, 0.17f), iron);

                _candles[i] = Primitive(PrimitiveType.Cylinder, stand.transform, "Candle",
                    new Vector3(0f, StandHeight + CandleLength * 0.5f, 0f),
                    new Vector3(0.06f, CandleLength * 0.5f, 0.06f), new Color(0.93f, 0.9f, 0.8f));

                _flames[i] = Primitive(PrimitiveType.Sphere, stand.transform, "Flame",
                    new Vector3(0f, CandleTop + 0.04f, 0f), new Vector3(0.035f, 0.06f, 0.035f), new Color(1f, 0.78f, 0.3f));
                Light light = _flames[i].AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, 0.68f, 0.32f);
                light.range = 2.2f;
                light.intensity = 0.9f;
                light.shadows = LightShadows.None;
            }

            RefreshVisuals();
        }

        /// <summary>충돌체 바닥(<see cref="Center"/>) 위로 솟은 방 바닥 마감 메시의 높이(m). 마감이 없으면 0.</summary>
        private float FloorFinishHeight()
        {
            if (_roomIndex.Value < 0 || _roomIndex.Value >= _rooms.Count)
                return 0f;
            Transform finish = _rooms[_roomIndex.Value].Root.Find("RoomFloor");
            Renderer renderer = finish != null ? finish.GetComponent<Renderer>() : null;
            return renderer != null ? Mathf.Clamp(renderer.bounds.max.y - _center.Value.y, 0f, 0.2f) : 0f;
        }

        private void RefreshVisuals()
        {
            if (_candles == null)
                return;
            for (int i = 0; i < RevivalRules.CandleCount; i++)
            {
                CandleSlotState state = GetSlot(i);
                _candles[i].SetActive(state != CandleSlotState.Empty);
                _flames[i].SetActive(state == CandleSlotState.Lit);
            }
        }

        private static GameObject Primitive(PrimitiveType type, Transform parent, string name,
            Vector3 localPosition, Vector3 localScale, Color color)
        {
            GameObject item = GameObject.CreatePrimitive(type);
            item.name = name;
            Destroy(item.GetComponent<Collider>());
            item.transform.SetParent(parent, false);
            item.transform.localPosition = localPosition;
            item.transform.localScale = localScale;
            item.GetComponent<Renderer>().material.color = color;
            return item;
        }

        private void OnGUI()
        {
            if (!IsSpawned || _settings == null)
                return;

            if (_localIgniting)
                DrawTimingBar();

            if (Time.time < _messageUntil && !string.IsNullOrEmpty(_message))
                GUI.Label(new Rect((Screen.width - 520f) * 0.5f, Screen.height * 0.2f, 520f, 26f), _message,
                    CenteredStyle());

            DrawNearbyStatus();
        }

        private void DrawTimingBar()
        {
            const float width = 420f;
            const float height = 26f;
            float x = (Screen.width - width) * 0.5f;
            float y = Screen.height * 0.72f;
            float cursor = Mathf.Clamp01((Time.time - _localStartedAt) / _settings.BarSeconds);

            GUI.Box(new Rect(x - 6f, y - 28f, width + 12f, height + 40f), "촛불 점화 — 초록에서 E (움직이면 중단)");
            DrawRect(new Rect(x, y, width, height), new Color(0.1f, 0.1f, 0.1f, 0.9f));
            DrawRect(new Rect(x + width * _localZoneStart, y, width * _settings.ZoneWidth, height),
                new Color(0.25f, 0.8f, 0.3f, 0.9f));
            DrawRect(new Rect(x + width * cursor - 2f, y - 4f, 4f, height + 8f), Color.white);
        }

        private void DrawNearbyStatus()
        {
            if (!IsPlaced || _localPlayer == null || _localPlayer.Input == null)
                return;
            Vector3 me = _localPlayer.Input.transform.position;
            if (Vector3.Distance(me, _center.Value) > StatusDistance)
                return;

            int placed = 0;
            int lit = 0;
            for (int i = 0; i < RevivalRules.CandleCount; i++)
            {
                CandleSlotState state = GetSlot(i);
                if (state != CandleSlotState.Empty)
                    placed++;
                if (state is CandleSlotState.Lit or CandleSlotState.Out)
                    lit++;
            }

            string text = _judging.Value
                ? "부활 의식 — 판정 중…"
                : $"부활 의식 — 촛불 {placed}/{RevivalRules.CandleCount} 놓음 · {lit} 켜짐 · 남은 촛불 {_candleStock.Value}개";
            GUI.Label(new Rect((Screen.width - 520f) * 0.5f, Screen.height * 0.15f, 520f, 24f), text, CenteredStyle());
        }

        private static GUIStyle CenteredStyle()
        {
            if (_centered == null)
                _centered = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            return _centered;
        }

        private static void DrawRect(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }
    }
}
