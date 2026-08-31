using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.AI
{
    [TestFixture]
    internal sealed class OpponentAIGeneralTests
    {
        private UnitSlotBoard _board;
        private GeneralCatalogSnapshot _catalog;

        [SetUp]
        public void SetUp()
        {
            _catalog = new GeneralCatalogSnapshot(new[]
            {
                new GeneralConfigSnapshot(
                    index: 1,
                    name: "张飞",
                    family: "张",
                    partWords: new[] { "张", "飞" },
                    combatArchetype: GeneralCombatArchetype.Pike,
                    rangeCells: 2.5f,
                    attackDamage: 15,
                    attackIntervalSeconds: 1f,
                    damageMode: "近战枪击",
                    targetPolicy: "nearest",
                    prefabAddress: "ZhangFei",
                    animationKey: "zhan1",
                    projectileType: null,
                    projectileSpeed: 0,
                    partRecruitWeight: 1,
                    skillId: 3),
            });
            _board = new UnitSlotBoard(5, _catalog);
            _board.Initialize(BuildTestMap(), reserveSlotCount: 5);
        }

        [TearDown]
        public void TearDown()
        {
            _board.GameOver();
        }

        [Test]
        public void GeneralPlanner_ReturnsOrderedHorizontalTargets_ForReserveParts()
        {
            FillReserveWithZhangFeiParts();
            var planner = new OpponentAiGeneralPlanner(_catalog);
            var snapshot = new OpponentAiBoardSnapshot(
                _board.Snapshot(),
                BuildTestMap(),
                maxLevel: 5);

            Assert.IsTrue(planner.TryBuildPlan(snapshot, out GeneralSynthesisPlan plan));
            Assert.AreEqual("张", plan.FirstSource.Occupant.Value.GeneralPartText);
            Assert.AreEqual("飞", plan.SecondSource.Occupant.Value.GeneralPartText);
            Assert.AreEqual(
                plan.FirstTarget.SlotId.GridPosition.X + 1,
                plan.SecondTarget.SlotId.GridPosition.X);
            Assert.AreEqual(
                plan.FirstTarget.SlotId.GridPosition.Y,
                plan.SecondTarget.SlotId.GridPosition.Y);
            Assert.IsTrue(plan.FirstTarget.IsEmpty);
            Assert.IsTrue(plan.SecondTarget.IsEmpty);
        }

        [Test]
        public void DecisionEngine_DeploysOnlyReservePart_WhenFirstPartIsAlreadyInBattle()
        {
            FillReserveWithZhangFeiParts();
            IReadOnlyList<UnitSlot> reserves = _board.GetSlots(false, SlotZone.Reserve);
            IReadOnlyList<UnitSlot> battle = _board.GetSlots(false, SlotZone.Battle);
            SlotDropResult move = _board.TryPlanDrop(
                reserves[0].SlotId,
                battle[0].SlotId);
            Assert.IsTrue(move.Success);
            Assert.IsTrue(_board.CommitDrop(move.Plan));

            var engine = new OpponentAiDecisionEngine(
                BuildTestMap(),
                new SeededRandomSource(17),
                new UnitLevelService(new UnitLevelConfigSnapshot(
                    5,
                    new[] { 1f, 1f, 1f, 1f, 1f },
                    new[] { 1f, 1f, 1f, 1f, 1f })),
                _catalog);
            var profile = new OpponentAiProfileSnapshot(
                id: 3,
                decisionIntervalMs: 500,
                initialBonusGold: 10,
                incomeWaveOrders: Array.Empty<int>(),
                incomeGoldValues: Array.Empty<int>(),
                placementPolicy: OpponentAiPlacementPolicy.RouteAware,
                candidateTopN: 5,
                handSize: 5,
                refreshBaseCost: 10,
                refreshCostIncrement: 2,
                itemCooldownMs: 1000,
                allowGeneralParts: true,
                allowFarmer: false,
                allowActiveMerge: true,
                allowTemplatePlacement: false,
                allowDangerResponse: false,
                allowFastDeploy: false,
                enableValueEvaluation: true,
                enableReclaim: false);

            IReadOnlyList<OpponentAiAction> actions = engine.BuildPlan(
                new OpponentAiBoardSnapshot(_board.Snapshot(), BuildTestMap(), 5),
                profile);

            Assert.AreEqual(1, actions.Count);
            Assert.AreEqual(OpponentAiActionType.Deploy, actions[0].Type);
            Assert.AreEqual(reserves[1].SlotId.Id, actions[0].SourceSlotId);
            Assert.AreEqual(battle[1].SlotId.Id, actions[0].TargetSlotId);
        }

        [Test]
        public void ExistingBoardSynthesis_ProducesOneBoundGeneral_AfterTwoDrops()
        {
            FillReserveWithZhangFeiParts();
            IReadOnlyList<UnitSlot> reserves = _board.GetSlots(false, SlotZone.Reserve);
            IReadOnlyList<UnitSlot> battle = _board.GetSlots(false, SlotZone.Battle);

            Assert.IsTrue(_board.TryPlanDrop(
                reserves[0].SlotId,
                battle[0].SlotId).Success);
            SlotDropResult first = _board.TryPlanDrop(
                reserves[0].SlotId,
                battle[0].SlotId);
            Assert.IsTrue(first.Success);
            Assert.IsTrue(_board.CommitDrop(first.Plan));

            SlotDropResult second = _board.TryPlanDrop(
                reserves[1].SlotId,
                battle[1].SlotId);
            Assert.IsTrue(second.Success);
            Assert.IsTrue(_board.CommitDrop(second.Plan));

            UnitSlot left = _board.GetSlotById(battle[0].SlotId.Id);
            UnitSlot right = _board.GetSlotById(battle[1].SlotId.Id);
            Assert.IsTrue(left.Occupant.HasValue);
            Assert.IsTrue(right.Occupant.HasValue);
            Assert.AreEqual(UnitKind.General, left.Occupant.Value.Kind);
            Assert.AreEqual(UnitKind.General, right.Occupant.Value.Kind);
            Assert.AreEqual(left.Occupant.Value.UnitId, right.Occupant.Value.UnitId);
            Assert.AreEqual("张飞", left.Occupant.Value.GeneralName);
        }

        private void FillReserveWithZhangFeiParts()
        {
            var units = new List<BattleUnit>
            {
                BattleUnit.CreateGeneralPart(_board.AllocateUnitId(), false, "张"),
                BattleUnit.CreateGeneralPart(_board.AllocateUnitId(), false, "飞"),
            };
            for (int i = 0; i < 3; i++)
            {
                units.Add(new BattleUnit(
                    _board.AllocateUnitId(),
                    false,
                    UnitKind.Soldier,
                    SoldierType.Knife,
                    "刀",
                    1));
            }

            Assert.IsTrue(_board.ReplaceReserve(false, units));
        }

        private static MapData BuildTestMap()
        {
            const int width = 3;
            const int height = 2;
            var cells = new GridCell[width * height];
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    cells[x * height + y] = new GridCell(
                        GridCellKind.Passage,
                        BuildableSide.None);
                }
            }

            cells[0 * height] = new GridCell(GridCellKind.Buildable, BuildableSide.Opponent);
            cells[1 * height] = new GridCell(GridCellKind.Buildable, BuildableSide.Opponent);
            return new MapData(
                cells,
                width,
                height,
                mapIndex: 0,
                playerStart: new GridPosition(0, 0),
                playerEnd: new GridPosition(0, 1),
                opponentStart: new GridPosition(2, 0),
                opponentEnd: new GridPosition(2, 1),
                playerPath: Array.Empty<GridPosition>(),
                opponentPath: Array.Empty<GridPosition>());
        }
    }
}
