namespace GhostHunter.Core.Scenes
{
    /// <summary>
    /// 씬을 코드에서 가리키는 식별자. 실제 씬 이름은 SceneNameSO 가 들고 있다.
    /// 값을 추가하면 SceneNameSO 와 빌드 씬 목록을 함께 갱신한다.
    /// </summary>
    public enum SceneId
    {
        /// <summary>빌드 인덱스 0. 앱 수명 내내 로드된 채로 남는다. 전환 대상이 아니다.</summary>
        Bootstrap = 0,
        Title = 1,
        Lobby = 2,
        Game = 3,
        Result = 4,
    }
}
