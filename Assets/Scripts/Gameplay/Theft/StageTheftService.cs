using System.Collections.Generic;
using UnityEngine;

namespace GhostHunter.Gameplay.Theft
{
    /// <summary>
    /// <see cref="IStageTheftService"/> 구현. <c>GameInstaller</c> 가 스테이지 씬의 설치 오브젝트에 붙이고 등록한다 —
    /// 씬이 바뀌면 함께 사라지므로 판마다 새로 시작한다. 귀중품 목록은 처음 쓸 때 <b>자기 씬 안에서만</b> 찾는다
    /// (Bootstrap 은 늘 함께 열려 있다).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StageTheftService : MonoBehaviour, IStageTheftService
    {
        private readonly Dictionary<int, Valuable> _valuables = new();
        private readonly List<int> _stolenIds = new();
        private bool _indexed;

        public int StolenMoney { get; private set; }

        public IReadOnlyList<int> StolenIds => _stolenIds;

        public bool TryGetValuable(int id, out Valuable valuable)
        {
            EnsureIndexed();
            return _valuables.TryGetValue(id, out valuable) && valuable != null;
        }

        public bool ServerTrySteal(int id, out int price)
        {
            price = 0;
            if (!TryGetValuable(id, out Valuable valuable) || valuable.IsStolen)
                return false;
            price = valuable.Price;
            valuable.MarkStolen();
            _stolenIds.Add(id);
            StolenMoney += price;
            return true;
        }

        public void ApplyStolen(int id)
        {
            if (!TryGetValuable(id, out Valuable valuable) || valuable.IsStolen)
                return;
            valuable.MarkStolen();
            _stolenIds.Add(id);
        }

        public void ServerRestore(IReadOnlyList<int> stolenIds, int stolenMoney)
        {
            if (stolenIds != null)
                foreach (int id in stolenIds)
                    ApplyStolen(id);
            StolenMoney = Mathf.Max(StolenMoney, stolenMoney);
        }

        private void EnsureIndexed()
        {
            if (_indexed)
                return;
            _indexed = true;
            Valuable[] all = FindObjectsByType<Valuable>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (Valuable valuable in all)
            {
                if (valuable.gameObject.scene != gameObject.scene)
                    continue;
                if (_valuables.TryGetValue(valuable.Id, out Valuable other) && other != null)
                {
                    Debug.LogError($"귀중품 '{valuable.name}' 이 '{other.name}' 과 같은 자리라 번호가 겹칩니다 — 하나는 훔칠 수 없습니다.",
                        valuable);
                    continue;
                }
                _valuables[valuable.Id] = valuable;
            }
        }
    }
}
