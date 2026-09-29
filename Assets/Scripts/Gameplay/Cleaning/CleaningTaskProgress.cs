namespace GhostHunter.Gameplay.Cleaning
{
    /// <summary>
    /// 스테이지 청소 작업 두 갈래의 현재 수치 — 대걸레로 닦은 얼룩, 드릴카로 반출한 목표 가구.
    /// 둘을 합친 전체 진행도 계산은 미정(D-14)이라 따로 보관한다. 복제된 상태에서 읽으므로 모든 피어가 같은 값을 본다.
    /// </summary>
    public readonly struct CleaningTaskProgress
    {
        public readonly int CleanedStains;
        public readonly int TotalStains;
        public readonly int DeliveredFurniture;
        public readonly int TotalFurniture;

        public CleaningTaskProgress(int cleanedStains, int totalStains, int deliveredFurniture, int totalFurniture)
        {
            CleanedStains = cleanedStains;
            TotalStains = totalStains;
            DeliveredFurniture = deliveredFurniture;
            TotalFurniture = totalFurniture;
        }

        public float StainRatio => Ratio(CleanedStains, TotalStains);
        public float FurnitureRatio => Ratio(DeliveredFurniture, TotalFurniture);

        public static float Ratio(int done, int total)
        {
            return total > 0 ? UnityEngine.Mathf.Clamp01((float)done / total) : 0f;
        }
    }
}
