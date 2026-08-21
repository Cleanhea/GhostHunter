using System;
using Cysharp.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
// System.Random 과 UnityEngine.Random 이 둘 다 보이면 모호해진다. 이 파일의 Random 은 Unity 쪽이다.
using Random = UnityEngine.Random;

namespace GhostHunter.Gameplay.Map
{
    /// <summary>
    /// 세션이 시작될 때 방 슬롯마다 프리셋을 하나씩 뽑아 배치한다. 같은 프리셋이 두 슬롯에
    /// 들어가지 않도록 풀을 섞어서 앞에서부터 나눠 준다(뽑은 것은 다시 뽑히지 않는다).
    ///
    /// 뽑기와 배치는 서버에서만 한다. 결과는 가구의 NetworkTransform 으로 복제되므로
    /// "어떤 프리셋이 뽑혔는지"를 따로 동기화할 필요가 없고, 늦게 들어온 클라이언트도
    /// 스폰 시점의 가구 위치를 그대로 받는다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class RoomSlotAssigner : NetworkBehaviour
    {
        [Tooltip("프리셋이 들어갈 자리. 방 중심(바닥면)이며 +Z가 방의 북쪽이다.")]
        [SerializeField] private Transform[] _slots;

        [Tooltip("뽑기 풀. 슬롯 수보다 많아야 중복 없이 배정할 수 있다.")]
        [SerializeField] private RoomPreset[] _pool;

        [Tooltip("배치가 끝난 뒤 초기 위치를 다시 기록할 대상. 없어도 동작한다.")]
        [SerializeField] private FurnitureResetter _resetter;

        public override void OnNetworkSpawn()
        {
            if (!IsServer)
                return;

            AssignAfterSceneObjectsSpawnAsync().Forget();
        }

        /// <summary>
        /// 배정을 한 프레임 미룬다. <see cref="OnNetworkSpawn"/>은 NGO 가 씬에 놓인
        /// NetworkObject 들을 훑어 스폰하는 <b>도중</b>에 불리므로, 이 시점에는 아직 스폰되지
        /// 않은 가구가 섞여 있다. 그런 가구는 <see cref="NetworkTransform.Teleport"/>를 쓸 수 없어
        /// 트랜스폼만 옮기게 되는데, 뒤이어 스폰되면서 Rigidbody 가 물리로 깨어나면 옮기기 전
        /// 자세로 되돌아간다. 다음 프레임이면 훑기가 끝나 전부 스폰된 상태다.
        /// </summary>
        private async UniTaskVoid AssignAfterSceneObjectsSpawnAsync()
        {
            try
            {
                await UniTask.NextFrame(destroyCancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (!IsServer || !IsSpawned)
                return;

            AssignPresets();
        }

        private void AssignPresets()
        {
            if (_slots == null || _slots.Length == 0 || _pool == null)
                return;

            if (_pool.Length < _slots.Length)
            {
                Debug.LogError(
                    $"[RoomSlotAssigner] 프리셋이 {_pool.Length}개뿐이라 슬롯 {_slots.Length}개에 " +
                    "중복 없이 배정할 수 없습니다.",
                    this);
                return;
            }

            // 세션을 다시 시작하면 지난 판에 배치된 가구가 방에 남아 있다. 전부 전시 자리로
            // 되돌린 뒤 뽑아야 새로 뽑힌 프리셋과 겹치지 않는다.
            foreach (RoomPreset preset in _pool)
            {
                if (preset != null)
                    preset.ApplyTo(preset.Anchor);
            }

            var order = new int[_pool.Length];
            for (int i = 0; i < order.Length; i++)
                order[i] = i;

            // Fisher-Yates. 앞에서부터 슬롯에 하나씩 주므로 같은 프리셋이 두 번 나올 수 없다.
            for (int i = order.Length - 1; i > 0; i--)
            {
                int swap = Random.Range(0, i + 1);
                (order[i], order[swap]) = (order[swap], order[i]);
            }

            for (int i = 0; i < _slots.Length; i++)
            {
                RoomPreset preset = _pool[order[i]];
                if (preset == null || _slots[i] == null)
                    continue;

                preset.ApplyTo(_slots[i]);
                Debug.Log(
                    $"[RoomSlotAssigner] {_slots[i].name} ← 프리셋 {preset.PresetId} " +
                    $"(가구 {preset.FurnitureCount}개, 첫 가구 {preset.FirstFurniturePosition})",
                    this);
            }

            // 가구를 옮긴 다음에 기록해야 R(리셋)이 전시 자리가 아니라 방으로 되돌린다.
            if (_resetter != null)
                _resetter.CapturePoses();
        }
    }
}
