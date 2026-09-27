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

        /// <summary>
        /// 프로토타입 검증 씬(구 Game) — 비교용 집·테스트베드·귀신·청소까지 모든 프로토타입 시스템이 모여 있다.
        /// 스테이지로 취급하지만 인게임 로비의 출발 대상은 아니다. 자동 검증(<c>LocalSessionAutomation</c>)이 바로 연다.
        /// 값 3 은 옛 Game 과 같다(직렬화된 값 유지).
        /// </summary>
        ProtoTypeGame = 3,

        Result = 4,

        /// <summary>
        /// 인게임 로비 — 세션을 연 채 스테이지 사이를 잇는 공간(상점·다음 스테이지 시작).
        /// 일반 로비(<see cref="Lobby"/>) → 인게임 로비 → Stage1 → Result → 인게임 로비 → … 로 돈다.
        /// </summary>
        InGameLobby = 5,

        /// <summary>인게임 로비 단말기의 "스테이지 출발"이 올리는 스테이지 — B안 집·드릴카·조립 영역·정신력 UI(ADR-0019).</summary>
        Stage1 = 6,
    }

    public static class SceneIdExtensions
    {
        /// <summary>
        /// 플레이어가 스테이지를 진행하는 씬인가 — 정신력·사망·정산·호스트 이전·음성 그룹 분리가 켜진다.
        /// 스테이지를 새로 추가하면 여기에 넣는다.
        /// </summary>
        public static bool IsStage(this SceneId id) => id is SceneId.ProtoTypeGame or SceneId.Stage1;
    }
}
