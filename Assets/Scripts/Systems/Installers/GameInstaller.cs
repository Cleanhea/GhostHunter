using GhostHunter.Core;
using GhostHunter.Core.Voice;
using GhostHunter.Core.Scenes;
using GhostHunter.Core.Settings;
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
    /// 스테이지 씬(Tutorial·ProtoTypeGame·Stage1)의 스폰 위치와 로컬 플레이어 컴포넌트 접근을 등록한다.
    /// 귀신·청소는 선택 배선이다. Tutorial·Stage1 은 조명 설정을 공유한다.
    /// </summary>
    [DefaultExecutionOrder(SceneInstaller.ExecutionOrder)]
    [DisallowMultipleComponent]
    public sealed class GameInstaller : SceneInstaller
    {
        // ProtoTypeGame 임시 상자 옆 조립 영역 배치(m) — 영역 트리거가 5 × 5m 다.
        private const float PrototypeAssemblyGap = 0.5f;
        private const float PrototypeAssemblyHalfWidth = 2.5f;

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
                {
                    assembly.transform.parent.position +=
                        drillCar.transform.position - previousPosition;
                    // 조립 영역은 상자 안이 아니라 왼쪽(−X) 바깥 땅에 둔다(2026-10-04). 반출 구역은 오른쪽이다.
                    assembly.transform.position = drillCar.transform.TransformPoint(new Vector3(
                        -(drillCar.Size.x * 0.5f + PrototypeAssemblyGap + PrototypeAssemblyHalfWidth),
                        -drillCar.Size.y * 0.5f,
                        0f));
                }
            }

            if (GetComponent<FurnitureDeliveryTracker>() == null)
                gameObject.AddComponent<FurnitureDeliveryTracker>();
            FurnitureDeliveryZone.CreateBesideDrillCar();
            if (GetComponent<TemporaryShopShelf>() == null)
                gameObject.AddComponent<TemporaryShopShelf>();
            StageExitInteractable.CreateInDrillCar();
            if (_voiceSettings != null)
            {
                Services.TryGet(out IUserSettings userSettings);
                Bind<IVoiceChatService>(new VoiceChatService(Services.Get<IVoiceCaptureService>(),
                    _voiceSettings, Services.Get<ISceneFlow>(), userSettings: userSettings));
            }
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
