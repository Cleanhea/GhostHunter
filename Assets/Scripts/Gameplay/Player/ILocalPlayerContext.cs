using GhostHunter.Gameplay.Interaction;
using GhostHunter.Gameplay.Sanity;

namespace GhostHunter.Gameplay.Player
{
    /// <summary>현재 클라이언트가 소유한 플레이어의 로컬 전용 컴포넌트를 제공한다.</summary>
    public interface ILocalPlayerContext
    {
        FurnitureTargeter Targeter { get; }
        GrabController GrabController { get; }
        PlayerInteractor Interactor { get; }
        MoleBurrowController BurrowController { get; }
        DetectionSkillController DetectionController { get; }

        /// <summary>일시정지 메뉴가 게임플레이 입력을 잠글 때 쓴다.</summary>
        PlayerInputReader Input { get; }

        /// <summary>생존 여부 조회·구독용. 관전 게이팅(spectator-system.md)과 QS-사망 게이팅이 읽는다.</summary>
        SanityNetworkState Sanity { get; }

        void Register(FurnitureTargeter targeter);
        void Register(GrabController grabController);
        void Register(PlayerInteractor interactor);
        void Register(MoleBurrowController burrowController);
        void Register(DetectionSkillController detectionController);
        void Register(PlayerInputReader input);
        void Register(SanityNetworkState sanity);

        void Unregister(FurnitureTargeter targeter);
        void Unregister(GrabController grabController);
        void Unregister(PlayerInteractor interactor);
        void Unregister(MoleBurrowController burrowController);
        void Unregister(DetectionSkillController detectionController);
        void Unregister(PlayerInputReader input);
        void Unregister(SanityNetworkState sanity);
    }
}
