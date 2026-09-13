namespace GhostHunter.Gameplay.Furniture
{
    /// <summary>2인 잡기 중 마우스 휠이 가구 자세를 바꾸는 방식.</summary>
    public enum FurnitureRotateMode : byte
    {
        /// <summary>월드 수직축 기준 좌우 회전.</summary>
        Rotate,

        /// <summary>조작한 플레이어 조준의 오른쪽 수평축 기준 앞뒤 기울이기.</summary>
        Tilt,
    }
}
