using System.Collections.Generic;

namespace GhostHunter.Gameplay.Theft
{
    /// <summary>
    /// 스테이지 씬 하나의 귀중품 상태(theft-system.md). 모든 피어에 있다 — 서버만 훔침을 확정하고 금액을 모으며,
    /// 클라이언트는 서버가 알려 준 귀중품을 숨기기만 한다. <c>GameInstaller</c> 가 스테이지 씬마다 등록한다.
    /// </summary>
    public interface IStageTheftService
    {
        /// <summary>서버 — 이번 판에 훔친 금액의 합. 정산(Result)에 따로 보여 준다(TH-Q2 확정).</summary>
        int StolenMoney { get; }

        /// <summary>훔쳐진 귀중품 번호들(서버가 원본). 늦게 들어온 피어 동기화·호스트 이전 스냅샷에 쓴다.</summary>
        IReadOnlyList<int> StolenIds { get; }

        bool TryGetValuable(int id, out Valuable valuable);

        /// <summary>서버 — 아직 안 훔친 귀중품이면 훔친 것으로 확정하고 숨긴다. 이미 훔쳐졌으면 거짓(동시 입력은 먼저 온 쪽만).</summary>
        bool ServerTrySteal(int id, out int price);

        /// <summary>모든 피어 — 서버가 알려 준 귀중품을 숨긴다. 모르는 번호·이미 숨긴 것은 무시한다.</summary>
        void ApplyStolen(int id);

        /// <summary>서버 — 호스트 이전 스냅샷에서 훔친 귀중품과 금액을 되살린다.</summary>
        void ServerRestore(IReadOnlyList<int> stolenIds, int stolenMoney);
    }
}
