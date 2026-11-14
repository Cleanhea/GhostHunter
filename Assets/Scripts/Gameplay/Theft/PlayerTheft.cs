using System;
using GhostHunter.Core;
using GhostHunter.Core.Steam;
using GhostHunter.Gameplay.Sanity;
using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Gameplay.Theft
{
    /// <summary>
    /// 플레이어의 도둑질(theft-system.md). 소유자는 조준한 귀중품 번호만 서버에 보내고, 서버가 대상·생존·거리를 검사해 확정한다.
    /// 확정되면 (1) 귀중품을 모든 피어에서 숨기고(TH-Q3), (2) 공동 잔액에 가격을 즉시 더하고(TH-Q2·TH-5),
    /// (3) 훔친 사람의 정신력을 금액표대로 올리고(sanity-system.md §4.1), (4) 소유자에게 "+ n$"·테두리 연출을 알린다.
    /// 둘이 동시에 누르면 서버가 먼저 처리한 요청만 받아 준다(사용자 확정 2026-11-12).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerTheft : NetworkBehaviour
    {
        [SerializeField] private TheftSettings _settings;
        [SerializeField] private SanityNetworkState _sanity;

        /// <summary>소유자 — 서버가 내 도둑질을 확정했다. 인자는 가격($). HUD 가 "+ n$"·테두리 연출을 띄운다.</summary>
        public event Action<int> LocalStealConfirmed;

        public TheftSettings Settings => _settings;

        private void Awake()
        {
            if (_settings == null || _sanity == null)
                Debug.LogError($"{nameof(PlayerTheft)}: 도둑질 설정·정신력이 Player 프리팹에 배선되지 않았습니다.", this);
        }

        public override void OnNetworkSpawn()
        {
            // 서버가 이미 훔쳐진 귀중품을 이 플레이어의 소유자에게 알려 준다(호스트 이전 뒤 재접속 등).
            ServerSyncOwner();
        }

        /// <summary>소유자 — 조준한 귀중품을 훔치겠다고 서버에 요청한다.</summary>
        public void RequestSteal(Valuable valuable)
        {
            if (!IsSpawned || !IsOwner || valuable == null || valuable.IsStolen)
                return;
            RequestStealRpc(valuable.Id);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestStealRpc(int valuableId) => ServerTrySteal(valuableId);

        /// <summary>서버 — 도둑질을 검사하고 확정한다. 확정했으면 참.</summary>
        public bool ServerTrySteal(int valuableId)
        {
            if (!IsServer || _settings == null || _sanity == null || !_sanity.HasSanity
                || !Services.TryGet(out IStageTheftService theft)
                || !theft.TryGetValuable(valuableId, out Valuable valuable) || valuable.IsStolen)
                return false;

            Vector3 eye = transform.position + Vector3.up * _settings.EyeHeight;
            Vector3 closest = valuable.Collider != null && valuable.Collider.enabled
                ? valuable.Collider.ClosestPoint(eye) : valuable.transform.position;
            if (Vector3.Distance(eye, closest) > _settings.ServerReachDistance)
                return false;

            if (!theft.ServerTrySteal(valuableId, out int price))
                return false;

            if (Services.TryGet(out IStageShopService shop) && shop.IsAvailable)
                shop.ServerAddStolenMoney(price);
            _sanity.ServerApplyValuableFound(price);

            ValuableStolenRpc(valuableId);
            StealConfirmedRpc(price);
            return true;
        }

        /// <summary>서버 — 지금까지 훔쳐진 귀중품을 이 플레이어의 소유자(클라이언트)에게 보낸다.</summary>
        public void ServerSyncOwner()
        {
            if (!IsServer || IsOwner || !Services.TryGet(out IStageTheftService theft) || theft.StolenIds.Count == 0)
                return;
            int[] ids = new int[theft.StolenIds.Count];
            for (int i = 0; i < ids.Length; i++)
                ids[i] = theft.StolenIds[i];
            SyncStolenRpc(ids);
        }

        // 서버는 확정하면서 이미 숨겼다.
        [Rpc(SendTo.NotServer)]
        private void ValuableStolenRpc(int valuableId)
        {
            if (Services.TryGet(out IStageTheftService theft))
                theft.ApplyStolen(valuableId);
        }

        [Rpc(SendTo.Owner)]
        private void SyncStolenRpc(int[] valuableIds)
        {
            if (!Services.TryGet(out IStageTheftService theft) || valuableIds == null)
                return;
            foreach (int id in valuableIds)
                theft.ApplyStolen(id);
        }

        [Rpc(SendTo.Owner)]
        private void StealConfirmedRpc(int price) => LocalStealConfirmed?.Invoke(price);
    }
}
