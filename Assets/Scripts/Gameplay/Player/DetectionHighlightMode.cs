namespace GhostHunter.Gameplay.Player
{
    /// <summary>
    /// 탐지 대상 표시 방식. 2026-10-04 사용자 결정(MS-19)으로 <see cref="FluorescentOverlay"/> 만 구현한다 —
    /// 원래 모습·메시 모양을 유지한 채 형광 테두리와 은은한 발광을 덧입힌다. 나머지 값은 예전 직렬화 호환용이다.
    /// </summary>
    public enum DetectionHighlightMode
    {
        /// <summary>폐기 — 전체를 단색으로 덮었다(텍스처가 사라지고 얼룩이 네모로 보였다).</summary>
        FullBodyEmission,
        Outline,
        Silhouette,
        FluorescentOverlay,
    }
}
