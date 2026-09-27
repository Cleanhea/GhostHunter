using System;
using System.Globalization;

namespace GhostHunter.Core.Scenes
{
    /// <summary>한 판이 정상 종료된 시점의 결과. 금전 보상은 단가가 정해질 때까지 기록하지 않는다.</summary>
    public readonly struct StageSettlementRecord
    {
        public readonly int DeliveredFurniture;
        public readonly int TargetFurniture;
        public readonly int CleaningPercent;
        public readonly int Survivors;
        public readonly int Missing;
        public readonly int Dead;
        public readonly bool TeamWiped;

        public StageSettlementRecord(int deliveredFurniture, int targetFurniture,
            int cleaningPercent, int survivors, int missing, int dead, bool teamWiped)
        {
            DeliveredFurniture = deliveredFurniture;
            TargetFurniture = targetFurniture;
            CleaningPercent = cleaningPercent;
            Survivors = survivors;
            Missing = missing;
            Dead = dead;
            TeamWiped = teamWiped;
        }

        /// <summary>Steam 로비 데이터에 저장할 작은 고정 형식. 방이 사라지면 함께 삭제된다.</summary>
        public string ToLobbyData() => string.Join(",",
            DeliveredFurniture.ToString(CultureInfo.InvariantCulture),
            TargetFurniture.ToString(CultureInfo.InvariantCulture),
            CleaningPercent.ToString(CultureInfo.InvariantCulture),
            Survivors.ToString(CultureInfo.InvariantCulture),
            Missing.ToString(CultureInfo.InvariantCulture),
            Dead.ToString(CultureInfo.InvariantCulture),
            TeamWiped ? "1" : "0");

        public static bool TryFromLobbyData(string data, out StageSettlementRecord record)
        {
            record = default;
            if (string.IsNullOrEmpty(data))
                return false;
            string[] fields = data.Split(',');
            if (fields.Length != 7)
                return false;
            int[] values = new int[7];
            for (int i = 0; i < values.Length; i++)
                if (!int.TryParse(fields[i], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out values[i]) || values[i] < 0)
                    return false;
            if (values[2] > 100 || values[6] > 1 || values[0] > values[1])
                return false;
            record = new StageSettlementRecord(values[0], values[1], values[2],
                values[3], values[4], values[5], values[6] == 1);
            return true;
        }
    }
}
