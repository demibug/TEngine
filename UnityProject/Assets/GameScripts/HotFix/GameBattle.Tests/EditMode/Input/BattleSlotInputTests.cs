using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Input
{
    // ============================================================================
    // BattleSlotInputTests（最终方案：输入层只提交 Recruit / DropUnit 两个命令）
    // ----------------------------------------------------------------------------
    // 验证内容：
    //   1. 输入层经 BattleInputCommand 只生成 Recruit 与 DropUnit 两种命令。
    //   2. ExecuteDropUnit：四种空槽换槽组合全部免费，源槽变空，SlotId 不变。
    //   3. ExecuteDropUnit：目标在战场时激活战斗实例（UnitRegistry 有活动单位）。
    //   4. ExecuteDropUnit：单位离开战场时解除战斗实例（GetActiveUnits 不再包含）。
    //   5. ExecuteRecruit：征兵清除所有待上场单位并重新填满 1 级四兵。
    //   6. 待上场单位不参与攻击调度（GetActiveUnits 只返回战场实例）。
    // ============================================================================

    /// <summary>
    /// BattleSlotInput 输入测试（最终方案：征兵 + 换槽/合并 + 战斗实例激活）。
    /// </summary>
    [TestFixture]
    internal class BattleSlotInputTests
    {
        private const float CellSize = 80f;
        private const int GridSize = 80;
        private const int OpponentAttackMultiplier = 1;

        private RuntimeIdAllocator _idAllocator;
        private BattlePoolScope _poolScope;
        private BattleObjectPool<KnifeSoldier> _knifePool;
        private BattleObjectPool<BowSoldier> _bowPool;
        private BattleObjectPool<SpearSoldier> _spearPool;
        private BattleObjectPool<CavalrySoldier> _cavalryPool;
        private EnemyManager _enemyManager;
        private AttackResolver _attackResolver;
        private AttackEffectManager _attackEffectManager;
        private BattleObjectPool<SimpleDynamicArrow> _arrowPool;
        private ProjectileFactory _projectileFactory;
        private ProjectileManager _projectileManager;
        private UnitFactory _unitFactory;
        private UnitRegistry _unitRegistry;
        private BattleState _battleState;
        private BattleEconomy _economy;
        private BattleConfigSnapshot _configSnapshot;
        private UnitLevelService _levelService;
        private UnitSlotBoard _slotBoard;
        private RecruitManager _recruitManager;
        private BattleInputController _controller;

        [SetUp]
        public void SetUp()
        {
            _idAllocator = new RuntimeIdAllocator();
            _poolScope = new BattlePoolScope();
            _knifePool = _poolScope.GetPool<KnifeSoldier>(() => new KnifeSoldier());
            _bowPool = _poolScope.GetPool<BowSoldier>(() => new BowSoldier());
            _spearPool = _poolScope.GetPool<SpearSoldier>(() => new SpearSoldier());
            _cavalryPool = _poolScope.GetPool<CavalrySoldier>(() => new CavalrySoldier());

            _enemyManager = new EnemyManager(GridSize);
            _attackResolver = new AttackResolver();
            _attackEffectManager = new AttackEffectManager();

            _arrowPool = _poolScope.GetPool<SimpleDynamicArrow>(() => new SimpleDynamicArrow());
            _projectileFactory = new ProjectileFactory(_idAllocator, _arrowPool, _enemyManager, CellSize);
            _projectileManager = new ProjectileManager(_projectileFactory);

            _configSnapshot = BuildTestConfig();
            _levelService = new UnitLevelService(_configSnapshot.UnitLevel);

            _unitFactory = new UnitFactory(
                _idAllocator,
                _knifePool, _bowPool, _spearPool, _cavalryPool,
                _enemyManager, _attackResolver, _attackEffectManager,
                _projectileFactory, _projectileManager,
                CellSize, OpponentAttackMultiplier,
                _levelService);
            _unitRegistry = new UnitRegistry(_unitFactory, CellSize);

            _slotBoard = new UnitSlotBoard(_levelService.MaxLevel);
            _slotBoard.Initialize(_configSnapshot.Map, RecruitDefinitions.ReserveSlotCount);

            _recruitManager = new RecruitManager(
                new SeededRandomSource(seed: 777), _slotBoard, RecruitDefinitions.ReserveSlotCount);
            _slotBoard.ReplaceReserve(true, _recruitManager.GenerateBatch(true));

            _battleState = new BattleState();
            _economy = new BattleEconomy(_battleState, refreshCostIncrement: 2);

            _controller = new BattleInputController(
                _slotBoard, _recruitManager, _levelService, _economy, _unitRegistry, _configSnapshot);
            _controller.StartGame();
        }

        [TearDown]
        public void TearDown()
        {
            _unitRegistry?.GameOver();
            _slotBoard?.GameOver();
        }

        private static BattleConfigSnapshot BuildTestConfig()
        {
            var units = new List<UnitConfigSnapshot>
            {
                MakeUnitConfig(0, "刀", "knife", 1.5f, 3, 0.8f),
                MakeUnitConfig(1, "弓", "bow", 3.5f, 2, 0.8f),
                MakeUnitConfig(2, "枪", "pike", 2.5f, 2, 0.8f),
                MakeUnitConfig(3, "骑", "cavalry", 2f, 2, 0.8f),
            };

            return new BattleConfigSnapshot(
                map: BuildTestMap(),
                enemy: new EnemyConfigSnapshot(
                    type: "Mob0", mapEnemyTypeIndex: 0, speed: 50,
                    healthByWave: new[] { 3, 4, 5 },
                    earlyRoundHealthMultipliers: new float[] { 1f, 1f },
                    contactDamage: 1),
                wave: new WaveConfigSnapshot(
                    waveUnitCounts: new[] { 5 },
                    bossWaveNumbers: Array.Empty<int>(),
                    bossSpawnChances: Array.Empty<float>(),
                    spawnStrategyWeights: new[] { 1 },
                    spawnStrategies: new IReadOnlyList<float>[] { new float[] { 1f } },
                    skipBoss: true, delayTimeMs: 10000, maxRounds: 20),
                units: units,
                unitLevel: new UnitLevelConfigSnapshot(
                    maxLevel: 3,
                    damageLevelMultipliers: new float[] { 1f, 1.2f, 1.5f },
                    attackSpeedLevelMultipliers: new float[] { 1f, 1.1f, 1.2f }),
                economy: new EconomyConfigSnapshot(
                    initialGold: 20, refreshCostStart: 10, refreshCostIncrement: 2,
                    unitBaseCost: 1, handSize: 5,
                    playerMaxHealth: 3, opponentMaxHealth: 3),
                deck: new DeckConfigSnapshot(
                    minimalMode: true,
                    baseSoldierTexts: RecruitDefinitions.BaseSoldierTexts,
                    handSize: RecruitDefinitions.ReserveSlotCount,
                    defaultLevel: RecruitDefinitions.DefaultLevel,
                    baseUnitCost: 1),
                projectile: new ProjectileConfigSnapshot(
                    types: new[] { "SimpleDynamicArrow" },
                    primaryType: "SimpleDynamicArrow",
                    movementStrategy: "TargetEnemyBezier",
                    hitStrategy: "HitEnemy"),
                missingFieldNotes: Array.Empty<string>(),
                sourceTag: "Test");
        }

        private static MapData BuildTestMap()
        {
            const int width = 5;
            const int height = 5;
            var cells = new GridCell[width * height];
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    cells[x * height + y] = new GridCell(GridCellKind.Passage, BuildableSide.None);
                }
            }

            cells[0 * height + 0] = new GridCell(GridCellKind.Buildable, BuildableSide.Player);
            cells[1 * height + 0] = new GridCell(GridCellKind.Buildable, BuildableSide.Player);
            cells[0 * height + 4] = new GridCell(GridCellKind.Buildable, BuildableSide.Opponent);

            var emptyPath = Array.Empty<GridPosition>();
            return new MapData(
                cells, width, height, mapIndex: 0,
                playerStart: new GridPosition(0, 0),
                playerEnd: new GridPosition(0, height - 1),
                opponentStart: new GridPosition(width - 1, 0),
                opponentEnd: new GridPosition(width - 1, height - 1),
                playerPath: emptyPath,
                opponentPath: emptyPath);
        }

        private static UnitConfigSnapshot MakeUnitConfig(
            int index, string text, string animKey,
            float rangeCells, int damage, float interval)
        {
            return new UnitConfigSnapshot(
                index, text, animKey, rangeCells, damage, interval,
                "单体", "nearest");
        }

        // ====================================================================
        // 换槽组合
        // ====================================================================

        [Test]
        [Description("四种空槽换槽组合全部免费，源槽变空，SlotId 不变。")]
        public void DropUnit_FourEmptyTargetCombinations_AllFree()
        {
            // 组合1：待上场 → 待上场。
            IReadOnlyList<UnitSlot> reserves = _slotBoard.GetSlots(true, SlotZone.Reserve);
            int r0 = reserves[0].SlotId.Id;
            int r1 = reserves[1].SlotId.Id;
            Assert.IsFalse(_slotBoard.GetSlotById(r0).IsEmpty, "r0 有单位");
            Assert.IsTrue(_slotBoard.GetSlotById(r1).IsEmpty, "r1 空");

            BattleInputResult result1 = _controller.Execute(BattleInputCommand.CreateDropUnit(1, r0, r1));
            Assert.IsTrue(result1.IsSuccess, "待上场→待上场 免费换槽");
            Assert.IsTrue(_slotBoard.GetSlotById(r0).IsEmpty, "r0 变空");
            Assert.IsFalse(_slotBoard.GetSlotById(r1).IsEmpty, "r1 承载单位");

            // 组合2：待上场 → 战场（激活战斗实例）。
            IReadOnlyList<UnitSlot> battle = _slotBoard.GetSlots(true, SlotZone.Battle);
            int b0 = battle[0].SlotId.Id;
            Assert.IsTrue(_slotBoard.GetSlotById(b0).IsEmpty, "b0 空战场槽");

            int movedUnitId = _slotBoard.GetSlotById(r1).OccupantUnitId;
            BattleInputResult result2 = _controller.Execute(BattleInputCommand.CreateDropUnit(2, r1, b0));
            Assert.IsTrue(result2.IsSuccess, "待上场→战场 免费换槽");
            Assert.IsTrue(_slotBoard.GetSlotById(r1).IsEmpty, "r1 变空");
            Assert.AreEqual(movedUnitId, _slotBoard.GetSlotById(b0).OccupantUnitId, "b0 承载单位");
            Assert.IsNotNull(_unitRegistry.GetActiveByUnitId(movedUnitId), "战场槽激活战斗实例");
        }

        [Test]
        [Description("单位离开战场槽时解除战斗实例，但保留 BattleUnit。")]
        public void DropUnit_BattleToReserve_DeactivatesBattleInstance()
        {
            IReadOnlyList<UnitSlot> reserves = _slotBoard.GetSlots(true, SlotZone.Reserve);
            IReadOnlyList<UnitSlot> battle = _slotBoard.GetSlots(true, SlotZone.Battle);

            // 上场：待上场 → 战场。
            int r0 = reserves[0].SlotId.Id;
            int b0 = battle[0].SlotId.Id;
            _controller.Execute(BattleInputCommand.CreateDropUnit(1, r0, b0));

            int unitId = _slotBoard.GetSlotById(b0).OccupantUnitId;
            Assert.IsNotNull(_unitRegistry.GetActiveByUnitId(unitId), "上场后应有战斗实例");

            // 下场：战场 → 待上场（找空待上场槽）。
            int r1 = reserves[1].SlotId.Id;
            Assert.IsTrue(_slotBoard.GetSlotById(r1).IsEmpty, "r1 空");
            BattleInputResult result = _controller.Execute(BattleInputCommand.CreateDropUnit(2, b0, r1));

            Assert.IsTrue(result.IsSuccess, "战场→待上场 免费换槽");
            Assert.IsNull(_unitRegistry.GetActiveByUnitId(unitId), "下场后解除战斗实例");
            Assert.AreEqual(unitId, _slotBoard.GetSlotById(r1).OccupantUnitId, "BattleUnit 保留在待上场槽");
        }

        // ====================================================================
        // 待上场单位不参与攻击调度
        // ====================================================================

        [Test]
        [Description("GetActiveUnits 只返回战场实例，待上场单位不参与攻击调度。")]
        public void GetActiveUnits_OnlyBattleInstances_ReserveNotIncluded()
        {
            IReadOnlyList<UnitSlot> reserves = _slotBoard.GetSlots(true, SlotZone.Reserve);
            IReadOnlyList<UnitSlot> battle = _slotBoard.GetSlots(true, SlotZone.Battle);

            // 一个待上场单位（开局已填满），一个上场单位。
            int r0 = reserves[0].SlotId.Id;
            int b0 = battle[0].SlotId.Id;
            _controller.Execute(BattleInputCommand.CreateDropUnit(1, r0, b0));

            IReadOnlyList<SoldierBase> active = _unitRegistry.GetActiveUnits();

            Assert.AreEqual(1, active.Count, "只有一个战场实例参与攻击调度");
        }

        // ====================================================================
        // 征兵
        // ====================================================================

        [Test]
        [Description("征兵清除全部待上场单位（包括高级单位）并重新填满 1 级四兵。")]
        public void Recruit_ClearsAllReserve_RefillsLevel1()
        {
            // 手动放一个 3 级单位到待上场槽（填满其余槽为普通 1 级单位）。
            var level3Unit = new BattleUnit(
                unitId: 9999, side: true, kind: UnitKind.Soldier,
                soldierType: SoldierType.Knife, soldierText: "刀", level: 3);
            FillReserveAll(level3Unit);

            _battleState.ApplyGoldDelta(true, BattleState.DefaultInitialGold);
            BattleInputResult result = _controller.Execute(
                BattleInputCommand.CreateRecruit(commandId: 10, playerSide: true));

            Assert.IsTrue(result.IsSuccess, "征兵应成功");
            IReadOnlyList<UnitSlot> after = _slotBoard.GetSlots(true, SlotZone.Reserve);
            foreach (UnitSlot slot in after)
            {
                Assert.IsFalse(slot.IsEmpty, "待上场槽已填满");
                Assert.AreEqual(1, slot.Occupant.Value.Level, "重新填满 1 级单位");
            }
        }

        /// <summary>用指定单位列表填满玩家方全部待上场槽（ReplaceReserve 严格要求批次数量等于槽位数）。</summary>
        private void FillReserveAll(params BattleUnit[] units)
        {
            IReadOnlyList<UnitSlot> slots = _slotBoard.GetSlots(true, SlotZone.Reserve);
            var batch = new BattleUnit[slots.Count];
            for (int i = 0; i < slots.Count; i++)
            {
                batch[i] = i < units.Length
                    ? units[i]
                    : new BattleUnit(
                        unitId: 7000 + i, side: true, kind: UnitKind.Soldier,
                        soldierType: SoldierType.Knife, soldierText: "刀", level: 1);
            }

            _slotBoard.ReplaceReserve(true, batch);
        }
    }
}
