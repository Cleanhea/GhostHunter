using GhostHunter.Gameplay.Interaction;
using UnityEngine;

namespace GhostHunter.Gameplay.Player
{
    /// <summary>로컬 플레이어가 스폰된 동안 그 플레이어의 UI용 컴포넌트 참조를 보관한다.</summary>
    public sealed class LocalPlayerContext : ILocalPlayerContext
    {
        public FurnitureTargeter Targeter { get; private set; }
        public GrabController GrabController { get; private set; }
        public PlayerInteractor Interactor { get; private set; }
        public MoleBurrowController BurrowController { get; private set; }

        public void Register(FurnitureTargeter targeter)
        {
            if (!CanRegister(Targeter, targeter))
                return;

            Targeter = targeter;
        }

        public void Register(GrabController grabController)
        {
            if (!CanRegister(GrabController, grabController))
                return;

            GrabController = grabController;
        }

        public void Register(PlayerInteractor interactor)
        {
            if (!CanRegister(Interactor, interactor))
                return;

            Interactor = interactor;
        }

        public void Register(MoleBurrowController burrowController)
        {
            if (!CanRegister(BurrowController, burrowController))
                return;

            BurrowController = burrowController;
        }

        public void Unregister(FurnitureTargeter targeter)
        {
            if (Targeter == targeter)
                Targeter = null;
        }

        public void Unregister(GrabController grabController)
        {
            if (GrabController == grabController)
                GrabController = null;
        }

        public void Unregister(PlayerInteractor interactor)
        {
            if (Interactor == interactor)
                Interactor = null;
        }

        public void Unregister(MoleBurrowController burrowController)
        {
            if (BurrowController == burrowController)
                BurrowController = null;
        }

        private static bool CanRegister<T>(T current, T incoming) where T : Component
        {
            if (incoming == null)
                return false;

            if (current == null || current == incoming)
                return true;

            Debug.LogError(
                $"[LocalPlayerContext] {typeof(T).Name}이 이미 등록되어 있습니다. " +
                "로컬 소유 플레이어가 중복 스폰됐는지 확인하세요.",
                incoming);
            return false;
        }
    }
}
