using GhostHunter.Gameplay.Recovery;
using NUnit.Framework;
using UnityEngine;

namespace GhostHunter.Tests.EditMode
{
    public sealed class StageRecoverySnapshotTests
    {
        [Test]
        public void UnityJsonRoundTrip_PreservesSteamAndCorpseIdsAndStageState()
        {
            var original = new StageRecoverySnapshot
            {
                StageId = "a-stage-instance",
                Sequence = 4294967294,
                FurnitureReady = true,
                FurnitureSeed = -123456,
                StainRevision = 4294967294,
                Players = new[]
                {
                    new StageRecoverySnapshot.PlayerState
                    {
                        SteamId = 76561198012345678UL,
                        Alive = false,
                        Sanity = 27,
                        DarknessSeconds = 0.75f,
                        WitnessedCorpses = new[] { 0xC000000000000123UL },
                        CorpseWitnessId = 0xC000000000000456UL,
                        BurrowPhase = 2,
                        BurrowTimer = 2.5f,
                        DriverDurability = 43,
                    },
                },
                Furniture = new[]
                {
                    new StageRecoverySnapshot.FurnitureState
                    {
                        Placement = 7,
                        Position = new Vector3(1.2f, -3.4f, 5.6f),
                        Durability = 14,
                    },
                },
                Stains = new[]
                {
                    new StageRecoverySnapshot.StainState
                    {
                        Placed = true, Cleaned = true, Revision = 55,
                    },
                },
                PoolFurniture = new[]
                {
                    new StageRecoverySnapshot.PoolFurnitureState
                    {
                        Path = "Game:1/2/3", PoolKey = "PartA", Active = true,
                        Durability = 0,
                    },
                },
                Ghost = new StageRecoverySnapshot.GhostState
                {
                    Exists = true, Phase = 2, PhaseElapsed = 7.25f,
                    PhenomenonCooldown = 12.5f,
                },
            };

            StageRecoverySnapshot restored = JsonUtility.FromJson<StageRecoverySnapshot>(
                JsonUtility.ToJson(original));

            Assert.That(restored.StageId, Is.EqualTo(original.StageId));
            Assert.That(restored.Sequence, Is.EqualTo(original.Sequence));
            Assert.That(restored.Players[0].SteamId, Is.EqualTo(original.Players[0].SteamId));
            Assert.That(restored.Players[0].WitnessedCorpses[0],
                Is.EqualTo(original.Players[0].WitnessedCorpses[0]));
            Assert.That(restored.Players[0].CorpseWitnessId,
                Is.EqualTo(original.Players[0].CorpseWitnessId));
            Assert.That(restored.Players[0].BurrowTimer, Is.EqualTo(2.5f));
            Assert.That(restored.Furniture[0].Placement, Is.EqualTo(7));
            Assert.That(restored.Furniture[0].Position.x, Is.EqualTo(1.2f));
            Assert.That(restored.Stains[0].Cleaned, Is.True);
            Assert.That(restored.StainRevision, Is.EqualTo(original.StainRevision));
            Assert.That(restored.PoolFurniture[0].PoolKey, Is.EqualTo("PartA"));
            Assert.That(restored.Ghost.PhenomenonCooldown, Is.EqualTo(12.5f));
        }
    }
}
