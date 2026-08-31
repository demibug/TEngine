using System;
using System.Collections.Generic;
using GameCommon.Battle;
using NUnit.Framework;

namespace GameBattle.Tests
{
    [TestFixture]
    public sealed class OpponentAIEndToEndTests
    {
        [Test]
        public void BasicSeed_DeploysOnlySoldierProjection()
        {
            BattleConfigSnapshot snapshot = new JsonBattleConfigProvider().GetSnapshot();
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
                runtime.OpponentAI.Update(2000);

                IReadOnlyList<UnitSlot> reserve =
                    runtime.SlotBoard.Snapshot().GetSlots(false, SlotZone.Reserve);
                IReadOnlyList<UnitSlot> battle =
                    runtime.SlotBoard.Snapshot().GetSlots(false, SlotZone.Battle);
                Assert.AreEqual(4, CountOccupied(reserve));
                Assert.AreEqual(1, CountOccupied(battle));
                for (int i = 0; i < reserve.Count; i++)
                {
                    if (reserve[i].Occupant.HasValue)
                    {
                        Assert.AreEqual(UnitKind.Soldier, reserve[i].Occupant.Value.Kind);
                    }
                }
            }
            finally
            {
                runtime.Dispose();
            }
        }

        [Test]
        public void GeneralParts_ReachBattleAndSynthesizeOneGeneral()
        {
            BattleConfigSnapshot snapshot = new JsonBattleConfigProvider().GetSnapshot();
            BattleRuntimeAssembly assembly = BattleRuntimeFactory.Create(
                BattleLoadoutDto.CreateLocalAiDefault(OpponentAiDifficulty.Expert),
                poolScope: null,
                bindings: null,
                configSnapshot: snapshot);
            Assert.IsTrue(assembly.IsSuccess, assembly.DiagnosticMessage);
            Assert.IsTrue(assembly.SlotBoard.ReplaceReserve(false, new BattleUnit[]
            {
                BattleUnit.CreateGeneralPart(1000, false, "张"),
                BattleUnit.CreateGeneralPart(1001, false, "飞"),
                MakeOpponentKnife(1002),
                MakeOpponentKnife(1003),
                MakeOpponentKnife(1004),
            }));

            var runtime = new BattleRuntime(assembly);
            try
            {
                runtime.BattleManager.StartGame(startNowMs: 0, spawnStrategyIndex: 0);
                runtime.InputController.StartGame();
                runtime.OpponentAI.StartGame();
                runtime.OpponentAI.Update(500);
                runtime.OpponentAI.Update(500);

                IReadOnlyList<UnitSlot> battle =
                    runtime.SlotBoard.Snapshot().GetSlots(false, SlotZone.Battle);
                int generalCells = 0;
                int generalUnitId = UnitSlot.InvalidUnitId;
                for (int i = 0; i < battle.Count; i++)
                {
                    if (!battle[i].Occupant.HasValue
                        || battle[i].Occupant.Value.Kind != UnitKind.General)
                    {
                        continue;
                    }

                    generalCells++;
                    if (generalUnitId == UnitSlot.InvalidUnitId)
                    {
                        generalUnitId = battle[i].Occupant.Value.UnitId;
                    }
                    else
                    {
                        Assert.AreEqual(
                            generalUnitId,
                            battle[i].Occupant.Value.UnitId,
                            "武将双格必须绑定同一个 GeneralId");
                    }
                    Assert.AreEqual(1, battle[i].Occupant.Value.GeneralIndex);
                    Assert.AreEqual("张飞", battle[i].Occupant.Value.GeneralName);
                }

                Assert.AreEqual(2, generalCells);
                Assert.AreNotEqual(UnitSlot.InvalidUnitId, generalUnitId);
                Assert.IsFalse(ContainsGeneralPart(
                    battle,
                    "张"));
                Assert.IsFalse(ContainsGeneralPart(
                    battle,
                    "飞"));
                Assert.IsFalse(ContainsGeneralPart(
                    runtime.SlotBoard.Snapshot().GetSlots(false, SlotZone.Reserve),
                    "张"));
                Assert.IsFalse(ContainsGeneralPart(
                    runtime.SlotBoard.Snapshot().GetSlots(false, SlotZone.Reserve),
                    "飞"));
            }
            finally
            {
                runtime.Dispose();
            }
        }

        private static bool ContainsGeneralPart(
            IReadOnlyList<UnitSlot> slots,
            string text)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].Occupant.HasValue
                    && slots[i].Occupant.Value.Kind == UnitKind.GeneralPart
                    && slots[i].Occupant.Value.GeneralPartText == text)
                {
                    return true;
                }
            }

            return false;
        }

        private static BattleUnit MakeOpponentKnife(int unitId)
        {
            return new BattleUnit(
                unitId,
                side: false,
                kind: UnitKind.Soldier,
                soldierType: SoldierType.Knife,
                soldierText: "刀",
                level: 1);
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
