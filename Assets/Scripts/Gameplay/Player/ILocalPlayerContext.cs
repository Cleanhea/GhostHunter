using GhostHunter.Gameplay.Interaction;

namespace GhostHunter.Gameplay.Player
{
    /// <summary>현재 클라이언트가 소유한 플레이어의 로컬 전용 컴포넌트를 제공한다.</summary>
    public interface ILocalPlayerContext
    {
        FurnitureTargeter Targeter { get; }
        GrabController GrabController { get; }
        PlayerInteractor Interactor { get; }
        MoleBurrowController BurrowController { get; }

        void Register(FurnitureTargeter targeter);
        void Register(GrabController grabController);
        void Register(PlayerInteractor interactor);
        void Register(MoleBurrowController burrowController);

        void Unregister(FurnitureTargeter targeter);
        void Unregister(GrabController grabController);
        void Unregister(PlayerInteractor interactor);
        void Unregister(MoleBurrowController burrowController);
    }
}
