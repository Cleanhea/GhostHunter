using System;
using UnityEngine;

namespace GhostHunter.Gameplay.Recovery
{
    /// <summary>
    /// NGO 서버를 다시 시작할 때 필요한, Steam 로비의 한 스테이지에 속한 상태.
    /// 씬 오브젝트는 배열 인덱스/씬 경로, 플레이어는 Steam ID로 식별한다.
    /// </summary>
    [Serializable]
    public sealed class StageRecoverySnapshot
    {
        public string StageId;
        public uint Sequence;
        public PlayerState[] Players = Array.Empty<PlayerState>();
        public FurnitureState[] Furniture = Array.Empty<FurnitureState>();
        public PoolFurnitureState[] PoolFurniture = Array.Empty<PoolFurnitureState>();
        public StainState[] Stains = Array.Empty<StainState>();
        public DoorState[] Doors = Array.Empty<DoorState>();
        public GhostState Ghost;
        public int FurnitureSeed;
        public bool FurnitureReady;
        public uint StainRevision;

        [Serializable]
        public struct PlayerState
        {
            public ulong SteamId;
            public Vector3 Position;
            public Quaternion Rotation;
            public int Sanity;
            public bool Alive;
            public bool DarknessExposed;
            public float DarknessSeconds;
            public ulong[] WitnessedCorpses;
            public Vector3 CorpsePosition;
            public Quaternion CorpseRotation;
            public Vector3 CorpseVelocity;
            public Vector3 CorpseAngularVelocity;
            public ulong CorpseWitnessId;
            public bool Crouching;
            public bool Prone;
            public int BurrowPhase;
            public float BurrowTimer;
            public float BurrowCooldown;
            public bool HeadlampOn;
            /// <summary>쓴 배터리 양. 남은 양이 아니라서 이 필드가 없는 스냅샷은 가득 찬 배터리로 복원된다.</summary>
            public float HeadlampDrained;
            /// <summary>쓴 라이터 연료. 헤드라이트와 같은 이유로 남은 양이 아니라 쓴 양이다.</summary>
            public float LighterDrained;
            public int DriverDurability;
        }

        [Serializable]
        public struct FurnitureState
        {
            public byte Placement;
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 LinearVelocity;
            public Vector3 AngularVelocity;
            public int Durability;
            public bool HasLaunched;
        }

        [Serializable]
        public struct PoolFurnitureState
        {
            public string Path;
            public string PoolKey;
            public bool Active;
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 LinearVelocity;
            public Vector3 AngularVelocity;
            public int Durability;
        }

        [Serializable]
        public struct StainState
        {
            public Vector3 Position;
            public float Yaw;
            public uint Revision;
            public bool Placed;
            public bool Cleaned;
        }

        [Serializable]
        public struct DoorState
        {
            public string Path;
            public bool Open;
        }

        [Serializable]
        public struct GhostState
        {
            public bool Exists;
            public Vector3 Position;
            public Quaternion Rotation;
            public int Phase;
            public float PhaseElapsed;
            public float NextRoll;
            public bool CleaningThresholdReached;
            public bool HighRiskAttack;
            public bool AttackStartedBelowRecoveryThreshold;
            public int LastTeamSanity;
            public float PhenomenonCooldown;
            public int LastPhenomenon;
            public uint PhenomenaPoolMask;
            public int CleaningProgress;
            public int Pursuit;
            public ulong TargetSteamId;
            public ulong WitnessedHidingSteamId;
            public ulong WitnessedBurrowSteamId;
            public bool TargetWasVisible;
            public Vector3 LastKnownPosition;
            public Vector3 RoamDestination;
            public float RoamGiveUpRemaining;
            public float SearchRemaining;
            public float TargetSelectionRemaining;
            public float RepathRemaining;
            public float PathRebuildRemaining;
            public float CatchCooldownRemaining;
            public GhostTrackedPlayerState[] TrackedPlayers;
        }

        [Serializable]
        public struct GhostTrackedPlayerState
        {
            public ulong SteamId;
            public float BodyWitnessRemaining;
            public float BedConcealTimer;
            public bool BedGranted;
            public bool WasBurrowed;
            public bool BurrowExposed;
        }
    }

    /// <summary>새 서버가 스냅샷을 적용할 때 자동 랜덤 생성과 AI 틱을 중지한다.</summary>
    public static class StageRecoveryGate
    {
        public static bool Restoring { get; set; }
    }
}
