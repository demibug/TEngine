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

        [Test]
        public void TemplateResolver_PrefersRouteNeighbour_WhenCandidateTopNIsOne()
        {
            MapData map = BuildTemplateTestMap();
            var board = new UnitSlotBoard(maxLevel: 5);
            board.Initialize(map, reserveSlotCount: 5);
            Assert.IsTrue(board.ReplaceReserve(false, new BattleUnit[]
            {
                MakeOpponentKnife(100),
                MakeOpponentKnife(101),
                MakeOpponentKnife(102),
                MakeOpponentKnife(103),
                MakeOpponentKnife(104),
            }));

            var snapshot = new OpponentAiBoardSnapshot(board.Snapshot(), map, maxLevel: 5);
            var profile = new OpponentAiProfileSnapshot(
                id: 3,
                decisionIntervalMs: 500,
                initialBonusGold: 10,
                incomeWaveOrders: Array.Empty<int>(),
                incomeGoldValues: Array.Empty<int>(),
                placementPolicy: OpponentAiPlacementPolicy.RouteAware,
                candidateTopN: 1,
                handSize: 5,
                refreshBaseCost: 10,
                refreshCostIncrement: 2,
                itemCooldownMs: 1000,
                allowGeneralParts: true,
                allowFarmer: true,
                allowActiveMerge: true,
                allowTemplatePlacement: true,
                allowDangerResponse: true,
                allowFastDeploy: true,
                enableValueEvaluation: true,
                enableReclaim: true);
            var resolver = new OpponentAiTemplateResolver(
                map,
                new SeededRandomSource(7),
                new OpponentAiValueEvaluator());

            try
            {
                BattleUnit source = board.GetSlots(false, SlotZone.Reserve)[0].Occupant.Value;
                Assert.IsTrue(resolver.TryChooseTarget(
                    source,
                    snapshot,
                    snapshot.GetEmptyBattleCells(),
                    profile,
                    out UnitSlotId target,
                    out string reason));

                Assert.AreEqual(new GridPosition(0, 2), target.GridPosition);
                Assert.AreEqual("template_route_neighbour_value", reason);
                Assert.AreEqual("7_f", resolver.GetTemplateKey(profile));
            }
            finally
            {
                board.GameOver();
            }
        }

        [Test]
        public void ItemController_FarmerHonorsCooldown_AndPrioritizesTwoOne()
        {
            MapData map = BuildFarmerTestMap();
            var board = new UnitSlotBoard(maxLevel: 5);
            board.Initialize(map, reserveSlotCount: 5);
            Assert.IsTrue(board.ReplaceReserve(false, new BattleUnit[]
            {
                BattleUnit.CreateFarmer(200, false),
                MakeOpponentKnife(201),
                MakeOpponentKnife(202),
                MakeOpponentKnife(203),
                MakeOpponentKnife(204),
            }));

            BattleConfigSnapshot config = new JsonBattleConfigProvider().GetSnapshot();
            Assert.IsTrue(config.OpponentAiProfiles.TryGet(3, out OpponentAiProfileSnapshot profile));
            var controller = new OpponentAiItemController(new BattleMapState(map), map);
            var snapshot = new OpponentAiBoardSnapshot(board.Snapshot(), map, maxLevel: 5);

            controller.StartGame();
            Assert.IsFalse(controller.TryBuildAction(snapshot, profile, out _),
                "冷却未完成时不能使用农民");

            controller.Update(profile.ItemCooldownMs - 1);
            Assert.IsFalse(controller.TryBuildAction(snapshot, profile, out _),
                "冷却还差 1ms 时不能使用农民");

            controller.Update(1);
            Assert.IsTrue(controller.TryBuildAction(snapshot, profile, out OpponentAiAction action));
            Assert.AreEqual(OpponentAiActionType.UseFarmer, action.Type);
            Assert.AreEqual(new GridPosition(2, 1), action.TargetPosition);
            Assert.AreEqual("farmer_priority_2_1", action.Reason);

            controller.MarkUsed();
            Assert.IsFalse(controller.TryBuildAction(snapshot, profile, out _),
                "使用后应重新进入冷却");
            board.GameOver();
        }

        [Test]
        public void ExpertEconomy_UsesAuthorityRefreshCostAndIncrement()
        {
            BattleConfigSnapshot snapshot = new JsonBattleConfigProvider().GetSnapshot();
            Assert.IsTrue(snapshot.OpponentAiProfiles.TryGet(3, out OpponentAiProfileSnapshot profile));
            BattleLoadoutDto loadout = BattleLoadoutDto.CreateLocalAiDefault(
                OpponentAiDifficulty.Expert);
            BattleRuntimeAssembly assembly = BattleRuntimeFactory.Create(
                loadout,
                poolScope: null,
                bindings: null,
                configSnapshot: snapshot);
            Assert.IsTrue(assembly.IsSuccess, assembly.DiagnosticMessage);

            var runtime = new BattleRuntime(assembly);
            try
            {
                var service = new OpponentEconomyService(
                    profile,
                    runtime.BattleEconomy,
                    runtime.InputController,
                    new BattleCommandIdAllocator());
                Assert.AreEqual(profile.RefreshBaseCost, service.CurrentRefreshCost);
                Assert.IsTrue(service.IsRefreshCostAligned);

                runtime.BattleEconomy.Award(false, 100, "test");
                EconomyResult paid = runtime.BattleEconomy.TryPayRecruitBatch(false);

                Assert.IsTrue(paid.Success);
                Assert.AreEqual(
                    profile.RefreshBaseCost + profile.RefreshCostIncrement,
                    service.CurrentRefreshCost);
                Assert.IsTrue(service.IsRefreshCostAligned);
            }
            finally
            {
                runtime.Dispose();
            }
        }

        [Test]
        public void ExpertFarmer_UsesInputCommandAndOpensOpponentTile()
        {
            BattleConfigSnapshot snapshot = new JsonBattleConfigProvider().GetSnapshot();
            BattleLoadoutDto loadout = BattleLoadoutDto.CreateLocalAiDefault(
                OpponentAiDifficulty.Expert);
            BattleRuntimeAssembly assembly = BattleRuntimeFactory.Create(
                loadout,
                poolScope: null,
                bindings: null,
                configSnapshot: snapshot);
            Assert.IsTrue(assembly.IsSuccess, assembly.DiagnosticMessage);
            Assert.IsTrue(TryFindOpponentCultivable(snapshot.Map, out _),
                "黄金地图必须至少包含一个对手可开垦格");

            Assert.IsTrue(assembly.SlotBoard.ReplaceReserve(false, new BattleUnit[]
            {
                BattleUnit.CreateFarmer(900, false),
                MakeOpponentKnife(901),
                MakeOpponentKnife(902),
                MakeOpponentKnife(903),
                MakeOpponentKnife(904),
            }));

            var runtime = new BattleRuntime(assembly);
            try
            {
                runtime.BattleManager.StartGame(startNowMs: 0, spawnStrategyIndex: 0);
                runtime.InputController.StartGame();
                runtime.OpponentAI.StartGame();
                int openedBefore = runtime.InputController.MapState.OpenedTileCount;

                for (int i = 0; i < 10; i++)
                {
                    runtime.OpponentAI.Update(500);
                }

                Assert.Greater(
                    runtime.InputController.MapState.OpenedTileCount,
                    openedBefore,
                    "专家 AI 应通过 UseFarmer 输入命令开垦至少一个对手格");
                IReadOnlyList<UnitSlot> reserves =
                    runtime.SlotBoard.Snapshot().GetSlots(false, SlotZone.Reserve);
                for (int i = 0; i < reserves.Count; i++)
                {
                    Assert.IsFalse(
                        reserves[i].Occupant.HasValue
                        && reserves[i].Occupant.Value.Kind == UnitKind.Farmer,
                        "农民成功使用后不应继续停留在待上场槽");
                }
            }
            finally
            {
                runtime.Dispose();
            }
        }

        [Test]
        public void ExpertShovel_UsesInputCommandAndOpensOpponentTile()
        {
            BattleConfigSnapshot snapshot = new JsonBattleConfigProvider().GetSnapshot();
            BattleLoadoutDto loadout = BattleLoadoutDto.CreateLocalAiDefault(
                OpponentAiDifficulty.Expert);
            BattleRuntimeAssembly assembly = BattleRuntimeFactory.Create(
                loadout,
                poolScope: null,
                bindings: null,
                configSnapshot: snapshot);
            Assert.IsTrue(assembly.IsSuccess, assembly.DiagnosticMessage);
            Assert.IsTrue(TryFindOpponentCultivable(snapshot.Map, out _),
                "黄金地图必须至少包含一个对手可开垦格");

            Assert.IsTrue(assembly.SlotBoard.ReplaceReserve(false, new BattleUnit[]
            {
                BattleUnit.CreateShovel(910, false),
                MakeOpponentKnife(911),
                MakeOpponentKnife(912),
                MakeOpponentKnife(913),
                MakeOpponentKnife(914),
            }));

            var runtime = new BattleRuntime(assembly);
            try
            {
                runtime.BattleManager.StartGame(startNowMs: 0, spawnStrategyIndex: 0);
                runtime.InputController.StartGame();
                runtime.OpponentAI.StartGame();
                int openedBefore = runtime.InputController.MapState.OpenedTileCount;

                for (int i = 0; i < 4; i++)
                {
                    runtime.OpponentAI.Update(500);
                }

                Assert.Greater(
                    runtime.InputController.MapState.OpenedTileCount,
                    openedBefore,
                    "专家 AI 应通过 UseShovel 输入命令开垦至少一个对手格");
                IReadOnlyList<UnitSlot> reserves =
                    runtime.SlotBoard.Snapshot().GetSlots(false, SlotZone.Reserve);
                for (int i = 0; i < reserves.Count; i++)
                {
                    Assert.IsFalse(
                        reserves[i].Occupant.HasValue
                        && reserves[i].Occupant.Value.IsShovel,
                        "铲子成功使用后不应继续停留在待上场槽");
                }
            }
            finally
            {
                runtime.Dispose();
            }
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

        private static MapData BuildTemplateTestMap()
        {
            IReadOnlyList<IReadOnlyList<string>> grid = new[]
            {
                (IReadOnlyList<string>)new[] { "0_0", "0_0", "1_1" },
                (IReadOnlyList<string>)new[] { "0_0", "0_0", "0_0" },
                (IReadOnlyList<string>)new[] { "0_0", "0_0", "0_0" },
                (IReadOnlyList<string>)new[] { "0_0", "0_0", "1_1" },
            };

            return MapData.FromColumnMajorGrid(
                grid,
                BattleConfigNormalizer.DecodeCell,
                mapIndex: 7,
                playerStart: new GridPosition(3, 1),
                playerEnd: new GridPosition(3, 1),
                opponentStart: new GridPosition(0, 1),
                opponentEnd: new GridPosition(0, 1),
                playerPath: Array.Empty<GridPosition>(),
                opponentPath: new[] { new GridPosition(0, 1) });
        }

        private static MapData BuildFarmerTestMap()
        {
            IReadOnlyList<IReadOnlyList<string>> grid = new[]
            {
                (IReadOnlyList<string>)new[] { "2_1", "0_0", "0_0" },
                (IReadOnlyList<string>)new[] { "0_0", "0_0", "0_0" },
                (IReadOnlyList<string>)new[] { "0_0", "2_1", "0_0" },
                (IReadOnlyList<string>)new[] { "0_0", "0_0", "0_0" },
            };

            return MapData.FromColumnMajorGrid(
                grid,
                BattleConfigNormalizer.DecodeCell,
                mapIndex: 8,
                playerStart: new GridPosition(0, 1),
                playerEnd: new GridPosition(0, 1),
                opponentStart: new GridPosition(1, 1),
                opponentEnd: new GridPosition(1, 1),
                playerPath: Array.Empty<GridPosition>(),
                opponentPath: new[] { new GridPosition(1, 1) });
        }

        private static bool TryFindOpponentCultivable(MapData map, out GridPosition target)
        {
            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    GridPosition position = new GridPosition(x, y);
                    if (map.GetCell(position).IsCultivable
                        && map.GetCell(position).BelongsToSide(playerSide: false))
                    {
                        target = position;
                        return true;
                    }
                }
            }

            target = default;
            return false;
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
