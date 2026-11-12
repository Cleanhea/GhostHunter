using System;
using System.Globalization;

namespace GhostHunter.Core.Scenes
{
    /// <summary>
    /// 한 판이 정상 종료된 시점의 결과. 금전 보상은 단가가 정해질 때까지 기록하지 않는다.
    /// <see cref="StolenMoney"/> 는 그 판에 훔친 귀중품 금액의 합이다(theft-system.md — 돈은 훔친 순간 공동 잔액에 이미
    /// 더해졌고 사망·실종에도 유지된다. 정산은 따로 보여 주기만 하고 다시 더하지 않는다).
    /// </summary>
    public readonly struct StageSettlementRecord
    {
        private const int LegacyFieldCount = 7;
        private const int FieldCount = 8;

        public readonly int DeliveredFurniture;
        public readonly int TargetFurniture;
        public readonly int CleaningPercent;
        public readonly int Survivors;
        public readonly int Missing;
        public readonly int Dead;
        public readonly bool TeamWiped;
        public readonly int StolenMoney;

        public StageSettlementRecord(int deliveredFurniture, int targetFurniture,
            int cleaningPercent, int survivors, int missing, int dead, bool teamWiped, int stolenMoney = 0)
        {
            DeliveredFurniture = deliveredFurniture;
            TargetFurniture = targetFurniture;
            CleaningPercent = cleaningPercent;
            Survivors = survivors;
            Missing = missing;
            Dead = dead;
            TeamWiped = teamWiped;
            StolenMoney = Math.Max(0, stolenMoney);
        }

        /// <summary>Steam 로비 데이터에 저장할 작은 고정 형식. 방이 사라지면 함께 삭제된다.</summary>
        public string ToLobbyData() => string.Join(",",
            DeliveredFurniture.ToString(CultureInfo.InvariantCulture),
            TargetFurniture.ToString(CultureInfo.InvariantCulture),
            CleaningPercent.ToString(CultureInfo.InvariantCulture),
            Survivors.ToString(CultureInfo.InvariantCulture),
            Missing.ToString(CultureInfo.InvariantCulture),
            Dead.ToString(CultureInfo.InvariantCulture),
            TeamWiped ? "1" : "0",
            StolenMoney.ToString(CultureInfo.InvariantCulture));

        /// <summary>8칸(훔친 금액 포함) 또는 도둑질 이전의 7칸 기록을 읽는다 — 7칸이면 훔친 금액은 0.</summary>
        public static bool TryFromLobbyData(string data, out StageSettlementRecord record)
        {
            record = default;
            if (string.IsNullOrEmpty(data))
                return false;
            string[] fields = data.Split(',');
            if (fields.Length != FieldCount && fields.Length != LegacyFieldCount)
                return false;
            int[] values = new int[FieldCount];
            for (int i = 0; i < fields.Length; i++)
                if (!int.TryParse(fields[i], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out values[i]) || values[i] < 0)
                    return false;
            if (values[2] > 100 || values[6] > 1 || values[0] > values[1])
                return false;
            record = new StageSettlementRecord(values[0], values[1], values[2],
                values[3], values[4], values[5], values[6] == 1, values[7]);
            return true;
        }
    }
}
