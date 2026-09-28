using System.Collections.Generic;
using GhostHunter.Gameplay.Cleaning;
using GhostHunter.Gameplay.Ghost;
using GhostHunter.Gameplay.FurnitureDriver;
using GhostHunter.Gameplay.Interaction;
using GhostHunter.Gameplay.Map;
using GhostHunter.Gameplay.Player;
using GhostHunter.Gameplay.Recovery;
using GhostHunter.Gameplay.Sanity;
using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Systems.Steam
{
    /// <summary>현재 Game 씬에서 재시작 가능한 상태만 읽고, 새 서버의 씬 풀에 적용한다.</summary>
    internal static class StageRecoverySnapshotUtility
    {
        internal static StageRecoverySnapshot Capture(string stageId, uint sequence)
        {
            NetworkManager network = NetworkManager.Singleton;
            FurnitureSpawnController furniture = Object.FindFirstObjectByType<FurnitureSpawnController>();
            CleaningController cleaning = Object.FindFirstObjectByType<CleaningController>();
            GhostPrototypeSpawner ghostSpawner = Object.FindFirstObjectByType<GhostPrototypeSpawner>();
            if (network == null || !network.IsServer || furniture == null || !furniture.IsReady
                || cleaning == null || cleaning.Stains == null || ghostSpawner == null
                || ghostSpawner.ActiveGhost == null || string.IsNullOrEmpty(stageId))
                return null;

            var snapshot = new StageRecoverySnapshot
            {
                StageId = stageId,
                Sequence = sequence,
                FurnitureSeed = furniture.GenerationSeed,
                FurnitureReady = furniture.IsReady,
                StainRevision = cleaning.Revision,
                Ghost = ghostSpawner.ActiveGhost.CaptureStageState(),
            };

            RandomFurnitureItem[] items = furniture.Items;
            snapshot.Furniture = new StageRecoverySnapshot.FurnitureState[items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i] == null || !items[i].IsSpawned)
                    return null;
                snapshot.Furniture[i] = items[i].CaptureStageState();
            }

            CleaningStain[] stains = cleaning.Stains;
            snapshot.Stains = new StageRecoverySnapshot.StainState[stains.Length];
            for (int i = 0; i < stains.Length; i++)
            {
                if (stains[i] == null || !stains[i].IsSpawned)
                    return null;
                snapshot.Stains[i] = stains[i].CaptureStageState();
            }

            var players = new List<StageRecoverySnapshot.PlayerState>(4);
            var playerSteamIds = new HashSet<ulong>();
            foreach (NetworkClient client in network.ConnectedClientsList)
            {
                NetworkObject player = client.PlayerObject;
                if (player == null)
                    return null;
                PlayerNameTag nameTag = player.GetComponent<PlayerNameTag>();
                SanityNetworkState sanity = player.GetComponent<SanityNetworkState>();
                PlayerVisuals visuals = player.GetComponent<PlayerVisuals>();
                PlayerMotor motor = player.GetComponent<PlayerMotor>();
                if (nameTag == null || nameTag.SteamId == 0
                    || !playerSteamIds.Add(nameTag.SteamId) || sanity == null
                    || visuals == null || motor == null)
                    return null;
                var state = new StageRecoverySnapshot.PlayerState
                {
                    SteamId = nameTag.SteamId,
                    Position = player.transform.position,
                    Rotation = player.transform.rotation,
                    Crouching = motor.IsCrouching,
                    Prone = motor.IsProne,
                };
                sanity.CaptureStageState(ref state);
                visuals.CaptureStageState(ref state);
                player.GetComponent<MoleBurrowController>()?.CaptureStageState(ref state);
                player.GetComponent<PlayerHeadlamp>()?.CaptureStageState(ref state);
                player.GetComponent<PlayerLighter>()?.CaptureStageState(ref state);
                player.GetComponent<PlayerFurnitureDriverController>()?.CaptureStageState(ref state);
                players.Add(state);
            }
            snapshot.Players = players.ToArray();

            FurnitureDriverPoolItem[] pool = Object.FindObjectsByType<FurnitureDriverPoolItem>(
                FindObjectsSortMode.None);
            snapshot.PoolFurniture = new StageRecoverySnapshot.PoolFurnitureState[pool.Length];
            for (int i = 0; i < pool.Length; i++)
            {
                if (pool[i] == null || !pool[i].IsSpawned)
                    return null;
                snapshot.PoolFurniture[i] = pool[i].CaptureStageState(ScenePath(pool[i].transform));
            }

            DoorInteractable[] doors = Object.FindObjectsByType<DoorInteractable>(FindObjectsSortMode.None);
            snapshot.Doors = new StageRecoverySnapshot.DoorState[doors.Length];
            for (int i = 0; i < doors.Length; i++)
                snapshot.Doors[i] = new StageRecoverySnapshot.DoorState
                {
                    Path = ScenePath(doors[i].transform), Open = doors[i].IsOpen,
                };
            return snapshot;
        }

        internal static bool RestoreWorld(StageRecoverySnapshot snapshot)
        {
            NetworkManager network = NetworkManager.Singleton;
            FurnitureSpawnController furniture = Object.FindFirstObjectByType<FurnitureSpawnController>();
            CleaningController cleaning = Object.FindFirstObjectByType<CleaningController>();
            GhostPrototypeSpawner ghostSpawner = Object.FindFirstObjectByType<GhostPrototypeSpawner>();
            if (snapshot == null || network == null || !network.IsServer || furniture == null
                || cleaning == null || ghostSpawner == null || !furniture.IsSpawned
                || !cleaning.IsSpawned || !snapshot.FurnitureReady)
                return false;
            if (!furniture.ServerRestoreStageState(snapshot)
                || !cleaning.ServerRestoreStageState(snapshot)
                || !ghostSpawner.ServerRestoreStageState(snapshot.Ghost))
                return false;

            FurnitureDriverPoolItem[] pool = Object.FindObjectsByType<FurnitureDriverPoolItem>(
                FindObjectsSortMode.None);
            if (snapshot.PoolFurniture == null || snapshot.PoolFurniture.Length != pool.Length)
                return false;
            var poolByPath = new Dictionary<string, FurnitureDriverPoolItem>(pool.Length);
            foreach (FurnitureDriverPoolItem item in pool)
                poolByPath[ScenePath(item.transform)] = item;
            foreach (StageRecoverySnapshot.PoolFurnitureState state in snapshot.PoolFurniture)
                if (!poolByPath.TryGetValue(state.Path, out FurnitureDriverPoolItem item)
                    || !item.ServerRestoreStageState(state))
                    return false;
            foreach (FurnitureAssemblyZone zone in FurnitureAssemblyZone.All)
                zone.ServerRefreshCandidates();

            DoorInteractable[] doors = Object.FindObjectsByType<DoorInteractable>(FindObjectsSortMode.None);
            var doorByPath = new Dictionary<string, DoorInteractable>(doors.Length);
            foreach (DoorInteractable door in doors)
                doorByPath[ScenePath(door.transform)] = door;
            if (snapshot.Doors == null || snapshot.Doors.Length != doors.Length)
                return false;
            foreach (StageRecoverySnapshot.DoorState state in snapshot.Doors)
            {
                if (!doorByPath.TryGetValue(state.Path, out DoorInteractable door) || !door.IsSpawned)
                    return false;
                door.ServerRestoreStageState(state.Open);
            }
            return true;
        }

        internal static bool RestorePlayer(NetworkObject player, StageRecoverySnapshot snapshot)
        {
            if (player == null || !player.IsSpawned || snapshot?.Players == null)
                return false;
            PlayerNameTag tag = player.GetComponent<PlayerNameTag>();
            if (tag == null || tag.SteamId == 0)
                return false;
            foreach (StageRecoverySnapshot.PlayerState state in snapshot.Players)
            {
                if (state.SteamId != tag.SteamId)
                    continue;
                PlayerMotor motor = player.GetComponent<PlayerMotor>();
                SanityNetworkState sanity = player.GetComponent<SanityNetworkState>();
                PlayerVisuals visuals = player.GetComponent<PlayerVisuals>();
                if (motor == null || sanity == null || visuals == null)
                    return false;
                motor.ServerRestoreStageState(state);
                sanity.ServerRestoreStageState(state);
                visuals.ServerRestoreStageState(state);
                player.GetComponent<MoleBurrowController>()?.ServerRestoreStageState(state);
                player.GetComponent<PlayerHeadlamp>()?.ServerRestoreStageState(state);
                player.GetComponent<PlayerLighter>()?.ServerRestoreStageState(state);
                player.GetComponent<PlayerFurnitureDriverController>()?.ServerRestoreStageState(state);
                return true;
            }
            // 이탈한 이전 호스트의 캐릭터는 새 세션에 존재하지 않는다.
            return false;
        }

        internal static void RebindGhostPlayers()
        {
            GhostPrototypeSpawner spawner = Object.FindFirstObjectByType<GhostPrototypeSpawner>();
            spawner?.ActiveGhost?.ServerRebindRecoveredPlayers();
        }

        private static string ScenePath(Transform transform)
        {
            var indices = new List<int>(8);
            Transform current = transform;
            while (current != null)
            {
                indices.Add(current.GetSiblingIndex());
                current = current.parent;
            }
            indices.Reverse();
            return transform.gameObject.scene.name + ":" + string.Join("/", indices);
        }
    }
}
