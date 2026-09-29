using GhostHunter.Core;
using GhostHunter.Core.Voice;
using GhostHunter.Core.Scenes;
using GhostHunter.Data;
using GhostHunter.Gameplay.Voice;
using GhostHunter.Core.Player;
using GhostHunter.Gameplay.Cleaning;
using GhostHunter.Gameplay.Ghost;
using GhostHunter.Gameplay.FurnitureDriver;
using GhostHunter.Gameplay.Interaction;
using GhostHunter.Gameplay.Lighting;
using GhostHunter.Gameplay.Map;
using GhostHunter.Gameplay.Player;
using GhostHunter.Gameplay.Sanity;
using UnityEngine;

namespace GhostHunter.Systems.Installers
{
    /// <summary>
    /// 스테이지 씬(ProtoTypeGame·Stage1)의 스폰 위치와 로컬 플레이어 컴포넌트 접근을 등록한다.
    /// 귀신·청소는 선택 배선이지만 스테이지 씬은 둘 다 둔다(ADR-0019 후속). 조명은 Stage1 만 배선한다.
    /// </summary>
    [DefaultExecutionOrder(SceneInstaller.ExecutionOrder)]
    [DisallowMultipleComponent]
    public sealed class GameInstaller : SceneInstaller
    {
        [SerializeField] private VoiceChatSettings _voiceSettings;
        [SerializeField] private PlayerSpawnRegistry _playerSpawns;
        [SerializeField] private SanityTeamService _sanityTeam;
        [SerializeField] private GhostPrototypeSpawner _ghostSpawner;
        [SerializeField] private CleaningController _cleaning;
        [SerializeField] private StageLightingController _lighting;

        private readonly LocalPlayerContext _localPlayer = new();

        protected override void InstallBindings()
        {
            DrillCarSafeZone drillCar = FindFirstObjectByType<DrillCarSafeZone>();
            if (drillCar != null && drillCar.PlacesBehindSpawnsAtRuntime && _playerSpawns != null
                && _playerSpawns.TryGetSpawnBounds(out Bounds spawnBounds, out Vector3 spawnFacing))
            {
                Vector3 previousPosition = drillCar.transform.position;
                drillCar.PlaceBehindSpawns(spawnBounds, spawnFacing);
                FurnitureAssemblyZone assembly = FindFirstObjectByType<FurnitureAssemblyZone>();
                if (assembly != null && assembly.transform.parent != null
                    && assembly.transform.parent.name == "FurnitureMultiDriverPrototype")
                    assembly.transform.parent.position +=
                        drillCar.transform.position - previousPosition;
            }

            if (GetComponent<FurnitureDeliveryTracker>() == null)
                gameObject.AddComponent<FurnitureDeliveryTracker>();
            FurnitureDeliveryZone.CreateInDrillCar();
            if (GetComponent<TemporaryShopShelf>() == null)
                gameObject.AddComponent<TemporaryShopShelf>();
            StageExitInteractable.CreateInDrillCar();
            if (_voiceSettings != null)
                Bind<IVoiceChatService>(new VoiceChatService(Services.Get<IVoiceCaptureService>(),
                    _voiceSettings, Services.Get<ISceneFlow>()));
            Bind<IPlayerSpawnRegistry>(_playerSpawns);
            Bind<ILocalPlayerContext>(_localPlayer);
            Bind<ISanityTeamService>(_sanityTeam);
            Bind<ISanityDebug>(_sanityTeam);
            if (_ghostSpawner != null)
                Bind<IGhostDebug>(_ghostSpawner);
            if (_cleaning != null)
                Bind<ICleaningService>(_cleaning);
            if (_lighting != null)
                Bind<IStageLightingDebug>(_lighting);
        }
    }
}
