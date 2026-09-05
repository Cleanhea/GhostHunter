namespace GhostHunter.Gameplay.Player
{
    /// <summary>탐지 대상 표시 방식. 현재는 전신 발광만 구현하고 나머지는 교체 지점으로 남긴다.</summary>
    public enum DetectionHighlightMode
    {
        FullBodyEmission,
        Outline,
        Silhouette,
    }
}
