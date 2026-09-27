namespace GhostHunter.Gameplay.Player
{
    /// <summary>
    /// 드라이버 우클릭이 행동을 시작하지 못한 이유 — 화면 중앙에 문구로 알린다
    /// (<c>FurnitureDriverActionHud</c>). 문구는 <c>FurnitureDriverUiSettings</c>에 있다.
    /// </summary>
    public enum FurnitureDriverFeedback
    {
        None,
        /// <summary>조립 영역에 이 가구의 재료가 다 모이지 않았다.</summary>
        NotEnoughMaterials,
        /// <summary>다른 가구의 재료가 섞였거나 같은 재료가 너무 많다(§4.3 규칙 1).</summary>
        MismatchedMaterials,
        /// <summary>조준한 재료가 조립 영역 밖에 있다.</summary>
        OutsideAssemblyZone,
    }
}
