using System;
using GameCommon.Battle;
using NUnit.Framework;

namespace GameBattle.Tests
{
    [TestFixture]
    public sealed class OpponentAIEconomyTests
    {
        [Test]
        public void Refresh_InsufficientGold_IsAtomicAndDoesNotLoop()
        {
            BattleConfigSnapshot snapshot = new JsonBattleConfigProvider().GetSnapshot();
            Assert.IsTrue(snapshot.OpponentAiProfiles.TryGet(
                3,
                out OpponentAiProfileSnapshot profile));
            BattleRuntimeAssembly assembly = BattleRuntimeFactory.Create(
                BattleLoadoutDto.CreateLocalAiDefault(OpponentAiDifficulty.Expert),
                poolScope: null,
                bindings: null,
                configSnapshot: snapshot);
            Assert.IsTrue(assembly.IsSuccess, assembly.DiagnosticMessage);

            var runtime = new BattleRuntime(assembly);
            try
            {
                runtime.BattleManager.StartGame(startNowMs: 0, spawnStrategyIndex: 0);
                runtime.InputController.StartGame();
                var service = new OpponentEconomyService(
                    profile,
                    runtime.BattleEconomy,
                    runtime.InputController,
                    new BattleCommandIdAllocator());
                service.StartGame();

                int balance = runtime.BattleEconomy.GetBalance(false);
                Assert.IsTrue(runtime.BattleEconomy.TrySpend(false, balance, "test-drain").Success);
                int refreshCount = runtime.BattleEconomy.GetRefreshCount(false);
                int refreshCost = runtime.BattleEconomy.GetRefreshCost(false);
                int occupiedReserve = CountOccupied(
                    runtime.SlotBoard.Snapshot().GetSlots(false, SlotZone.Reserve));

                for (int i = 0; i < 3; i++)
                {
                    BattleInputResult result = service.TryRefresh();
                    Assert.IsFalse(result.IsSuccess);
                    Assert.AreEqual(
                        BattleInputRejectReason.InsufficientGoldForRecruit,
                        result.RejectReason);
                }

                Assert.AreEqual(0, runtime.BattleEconomy.GetBalance(false));
                Assert.AreEqual(refreshCount, runtime.BattleEconomy.GetRefreshCount(false));
                Assert.AreEqual(refreshCost, runtime.BattleEconomy.GetRefreshCost(false));
                Assert.AreEqual(
                    occupiedReserve,
                    CountOccupied(runtime.SlotBoard.Snapshot().GetSlots(false, SlotZone.Reserve)));
            }
            finally
            {
                runtime.Dispose();
            }
        }

        [Test]
        public void WaveIncome_UsesOnlyConfiguredOrders()
        {
            BattleConfigSnapshot snapshot = new JsonBattleConfigProvider().GetSnapshot();
            BattleRuntimeAssembly assembly = BattleRuntimeFactory.Create(
                BattleLoadoutDto.CreateLocalAiDefault(OpponentAiDifficulty.Easy),
                poolScope: null,
                bindings: null,
                configSnapshot: snapshot);
            Assert.IsTrue(assembly.IsSuccess, assembly.DiagnosticMessage);

            var profile = new OpponentAiProfileSnapshot(
                id: 99,
                decisionIntervalMs: 500,
                initialBonusGold: 7,
                incomeWaveOrders: new[] { 3, 5 },
                incomeGoldValues: new[] { 11, 13 },
                placementPolicy: OpponentAiPlacementPolicy.RouteAware,
                candidateTopN: 2);
            var runtime = new BattleRuntime(assembly);
            try
            {
                runtime.BattleManager.StartGame(startNowMs: 0, spawnStrategyIndex: 0);
                int before = runtime.BattleEconomy.GetBalance(false);
                var service = new OpponentEconomyService(
                    profile,
                    runtime.BattleEconomy,
                    runtime.InputController,
                    new BattleCommandIdAllocator());

                service.StartGame();
                service.OnWaveStarted(3);
                service.OnWaveStarted(4);
                service.OnWaveStarted(5);

                Assert.AreEqual(before + 7 + 11 + 13, runtime.BattleEconomy.GetBalance(false));
            }
            finally
            {
                runtime.Dispose();
            }
        }

        [Test]
        public void StopRestart_DoesNotDuplicateInitialBonusOrDecision()
        {
            BattleConfigSnapshot snapshot = new JsonBattleConfigProvider().GetSnapshot();
            Assert.IsTrue(snapshot.OpponentAiProfiles.TryGet(
                0,
                out OpponentAiProfileSnapshot profile));
            BattleRuntimeAssembly assembly = BattleRuntimeFactory.Create(
                BattleLoadoutDto.CreateLocalAiDefault(OpponentAiDifficulty.Easy),
                poolScope: null,
                bindings: null,
                configSnapshot: snapshot);
            Assert.IsTrue(assembly.IsSuccess, assembly.DiagnosticMessage);

            var runtime = new BattleRuntime(assembly);
            try
            {
                runtime.BattleManager.StartGame(startNowMs: 0, spawnStrategyIndex: 0);
                runtime.InputController.StartGame();
                runtime.OpponentAI.StartGame();
                int balanceAfterStart = runtime.BattleEconomy.GetBalance(false);

                runtime.OpponentAI.Stop();
                runtime.OpponentAI.StartGame();

                Assert.AreEqual(balanceAfterStart, runtime.BattleEconomy.GetBalance(false));
                runtime.OpponentAI.Update(profile.DecisionIntervalMs);
                Assert.AreEqual(
                    1,
                    CountOccupied(runtime.SlotBoard.Snapshot().GetSlots(false, SlotZone.Battle)));
            }
            finally
            {
                runtime.Dispose();
            }
        }

        private static int CountOccupied(System.Collections.Generic.IReadOnlyList<UnitSlot> slots)
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
