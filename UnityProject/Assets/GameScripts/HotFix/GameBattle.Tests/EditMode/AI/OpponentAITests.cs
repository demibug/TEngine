using System;
using System.Collections.Generic;
using GameCommon.Battle;
using NUnit.Framework;

namespace GameBattle.Tests
{
    [TestFixture]
    public sealed class OpponentAITests
    {
        [Test]
        public void SharedCommandIdAllocator_AlternatingProducers_RemainsUnique()
        {
            var allocator = new BattleCommandIdAllocator();

            int uiFirst = allocator.Allocate();
            int aiFirst = allocator.Allocate();
            int uiSecond = allocator.Allocate();

            Assert.AreEqual(1, uiFirst);
            Assert.AreEqual(2, aiFirst);
            Assert.AreEqual(3, uiSecond);
        }

        [Test]
        public void RandomStreams_OpponentConsumption_DoesNotChangePlayerRecruitSequence()
        {
            var first = new BattleRandomStreams(20260828);
            for (int i = 0; i < 20; i++)
            {
                first.OpponentRecruit.NextUnit();
                first.OpponentStrategy.NextUnit();
            }

            float firstPlayerValue = first.PlayerRecruit.NextUnit();

            var replay = new BattleRandomStreams(20260828);
            float replayPlayerValue = replay.PlayerRecruit.NextUnit();

            Assert.AreEqual(replayPlayerValue, firstPlayerValue);
        }

        [Test]
        public void LocalAiDefault_IsExplicit_AndMinimalDefaultRemainsDisabled()
        {
            BattleLoadoutDto minimal = BattleLoadoutDto.CreateMinimalDefault();
            BattleLoadoutDto localAi = BattleLoadoutDto.CreateLocalAiDefault(
                OpponentAiDifficulty.Hard);

            Assert.AreEqual(BattleOpponentMode.None, minimal.OpponentMode);
            Assert.AreEqual(BattleOpponentMode.LocalAI, localAi.OpponentMode);
            Assert.AreEqual(OpponentAiDifficulty.Hard, localAi.OpponentAiDifficulty);
        }

        [Test]
        public void ProfileIncome_ReturnsOnlyConfiguredWaveAmounts()
        {
            var profile = new OpponentAiProfileSnapshot(
                2,
                1000,
                10,
                new[] { 3, 5 },
                new[] { 10, 20 },
                OpponentAiPlacementPolicy.RouteAware,
                5);

            Assert.AreEqual(0, profile.GetIncomeForWave(1));
            Assert.AreEqual(10, profile.GetIncomeForWave(3));
            Assert.AreEqual(20, profile.GetIncomeForWave(5));
        }

        [Test]
        public void PlacementDifficulty_LowAvoidsSoldierMerge_HighPrefersIt()
        {
            BattleConfigSnapshot snapshot = new JsonBattleConfigProvider().GetSnapshot();
            Assert.IsTrue(snapshot.OpponentAiProfiles.TryGet(0, out OpponentAiProfileSnapshot easy));
            Assert.IsTrue(snapshot.OpponentAiProfiles.TryGet(2, out OpponentAiProfileSnapshot hard));

            var levelService = new UnitLevelService(snapshot.UnitLevel);
            var source = new BattleUnit(100, false, UnitKind.Soldier, SoldierType.Knife, "刀", 1);
            var mergeTarget = new BattleUnit(101, false, UnitKind.Soldier, SoldierType.Knife, "刀", 1);
            var occupiedSlotId = new UnitSlotId(10, false, SlotZone.Battle, new GridPosition(0, 0));
            var emptySlotId = new UnitSlotId(11, false, SlotZone.Battle, new GridPosition(1, 0));
            UnitSlot[] slots =
            {
                new UnitSlot(occupiedSlotId, mergeTarget),
                new UnitSlot(emptySlotId, occupant: null),
            };

            var easyPlanner = new OpponentAiPlacementPlanner(
                snapshot.Map, new SeededRandomSource(1), levelService);
            var hardPlanner = new OpponentAiPlacementPlanner(
                snapshot.Map, new SeededRandomSource(1), levelService);

            Assert.IsTrue(easyPlanner.TryChooseTarget(source, slots, easy, out UnitSlotId easyTarget));
            Assert.IsTrue(hardPlanner.TryChooseTarget(source, slots, hard, out UnitSlotId hardTarget));
            Assert.AreEqual(emptySlotId, easyTarget);
            Assert.AreEqual(occupiedSlotId, hardTarget);
        }

        [Test]
        public void FirstDecision_DeploysOneInitialOpponentReserveUnit()
        {
            BattleConfigSnapshot snapshot = new JsonBattleConfigProvider().GetSnapshot();
            BattleLoadoutDto loadout = new BattleLoadoutDto(
                mapId: 0,
                round: 0,
                randomSeed: 123,
                configVersion: 0,
                configHash: string.Empty,
                opponentMode: BattleOpponentMode.LocalAI,
                opponentAiDifficulty: OpponentAiDifficulty.Easy);

            BattleRuntimeAssembly assembly = BattleRuntimeFactory.Create(
                loadout, poolScope: null, bindings: null, configSnapshot: snapshot);
            Assert.IsTrue(assembly.IsSuccess, assembly.DiagnosticMessage);

            var runtime = new BattleRuntime(assembly);
            try
            {
                runtime.BattleManager.StartGame(startNowMs: 0, spawnStrategyIndex: 0);
                runtime.InputController.StartGame();
                runtime.OpponentAI.StartGame();

                Assert.AreEqual(30, runtime.BattleEconomy.GetBalance(false));
                Assert.AreEqual(5, CountOccupied(
                    runtime.SlotBoard.Snapshot().GetSlots(false, SlotZone.Reserve)));

                runtime.OpponentAI.Update(1999);
                Assert.AreEqual(5, CountOccupied(
                    runtime.SlotBoard.Snapshot().GetSlots(false, SlotZone.Reserve)));

                runtime.OpponentAI.Update(1);
                Assert.AreEqual(4, CountOccupied(
                    runtime.SlotBoard.Snapshot().GetSlots(false, SlotZone.Reserve)));
                Assert.AreEqual(1, CountOccupied(
                    runtime.SlotBoard.Snapshot().GetSlots(false, SlotZone.Battle)));
            }
            finally
            {
                runtime.Dispose();
            }
        }

        private static int CountOccupied(IReadOnlyList<UnitSlot> slots)
        {
            int count = 0;
            for (int i = 0; i < slots.Count; i++)
            {
                if (!slots[i].IsEmpty)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
