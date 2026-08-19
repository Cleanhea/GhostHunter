using System;

namespace GhostHunter.Core.Scenes
{
    /// <summary>
    /// 씬 전환의 단일 진입점. 다른 코드는 UnityEngine.SceneManagement.SceneManager 를 직접 부르지 않는다.
    ///
    /// <see cref="Load"/> 는 "요청"이지 "결정"이 아니다. 실제 전환 가능 여부(세션 중 서버 권한,
    /// 이미 전환 중인지)는 구현체가 판정하고, 거부되면 아무 일도 일어나지 않는다.
    /// 완료를 알아야 하면 <see cref="SceneChanged"/> 를 구독한다.
    /// </summary>
    public interface ISceneFlow
    {
        /// <summary>현재 올라와 있는 게임플레이 씬. Bootstrap 은 항상 그 아래에 깔려 있다.</summary>
        SceneId Current { get; }

        /// <summary>전환이 진행 중인가. true 인 동안의 <see cref="Load"/> 호출은 무시된다.</summary>
        bool IsLoading { get; }

        /// <summary>전환이 끝나 새 씬이 활성 씬이 된 뒤 발생한다.</summary>
        event Action<SceneId> SceneChanged;

        /// <summary>전환을 요청한다. 실패·거부는 로그로만 남고 예외를 던지지 않는다.</summary>
        void Load(SceneId scene);
    }
}
