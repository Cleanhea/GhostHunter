using System;

namespace GhostHunter.Gameplay.FurnitureDriver
{
    /// <summary>레시피 하나가 요구하는 부품 종류와 개수(기획서 §4.3).</summary>
    [Serializable]
    public struct FurniturePartRequirement
    {
        public string PartId;
        public int Count;

        public FurniturePartRequirement(string partId, int count)
        {
            PartId = partId;
            Count = count;
        }
    }
}
