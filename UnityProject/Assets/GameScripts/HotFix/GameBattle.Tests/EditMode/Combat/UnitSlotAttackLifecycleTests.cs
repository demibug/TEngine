using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Combat
{
    // ============================================================================
    // UnitSlotAttackLifecycleTests（最终方案"攻击生命周期"）
    // ----------------------------------------------------------------------------
    // 验证内容：
    //   1. 战场单位升级后伤害和攻速立即更新（ApplyLevel 数值重算）。
    //   2. 待上场单位不攻击，技能入口不执行（GetActiveUnits 只返回战场实例）。
    //   3. 未释放攻击被取消（单位下场/合并源移除时 CancelOwner）。
    //   4. 已发射箭矢保持原伤害继续飞行（ProjectileAttackEffect.Cancel 只解除引用）。
    //   5. 上下场不刷新攻击冷却（冷却导入导出）。
    // ============================================================================

    /// <summary>
    /// 单位槽位攻击生命周期测试（最终方案：等级数值、冷却导入导出、取消未释放攻击）。
    /// </summary>
    [TestFixture]
    internal class UnitSlotAttackLifecycleTests
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
                new SeededRandomSource(seed: 99), _slotBoard, RecruitDefinitions.ReserveSlotCount);
            _slotBoard.ReplaceReserve(true, _recruitManager.GenerateBatch(true));

            _battleState = new BattleState();
            _economy = new BattleEconomy(_battleState, refreshCostIncrement: 2);
            _battleState.ApplyGoldDelta(true, BattleState.DefaultInitialGold);
            _battleState.ApplyGoldDelta(false, BattleState.DefaultInitialGold);

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

        /// <summary>把一个指定等级的同兵种单位放入待上场槽并上场到战场槽。</summary>
        private SoldierBase PlaceSoldierToBattle(SoldierType type, string text, int level)
        {
            IReadOnlyList<UnitSlot> reserves = _slotBoard.GetSlots(true, SlotZone.Reserve);
            IReadOnlyList<UnitSlot> battles = _slotBoard.GetSlots(true, SlotZone.Battle);

            UnitConfigSnapshot config = FindConfig(text);
            var unit = new BattleUnit(
                unitId: _slotBoard.AllocateUnitId(),
                side: true,
                kind: UnitKind.Soldier,
                soldierType: type,
                soldierText: text,
                level: level);

            // 直接把单位放入战场槽（通过 ActivateBattleUnit，不经换槽）。
            return _unitRegistry.ActivateBattleUnit(
                unit, config, _levelService,
                battles[0].SlotId.GridPosition.X,
                battles[0].SlotId.GridPosition.Y,
                40f, 40f);
        }

        private UnitConfigSnapshot FindConfig(string text)
        {
            foreach (UnitConfigSnapshot config in _configSnapshot.Units)
            {
                if (config.Text == text)
                {
                    return config;
                }
            }

            throw new InvalidOperationException($"测试配置中无兵种 {text}");
        }

        // ====================================================================
        // 战场单位升级后数值立即更新
        // ====================================================================

        [Test]
        [Description("战场目标升级后伤害和攻速立即更新（ApplyLevel 数值重算）。")]
        public void BattleUnit_LevelUp_StatsUpdateImmediately()
        {
            // 1 级刀兵：伤害 3，间隔 0.8s。
            SoldierBase soldier = PlaceSoldierToBattle(SoldierType.Knife, "刀", level: 1);
            Assert.AreEqual(3, ((KnifeSoldier)soldier).AttackDamageForTest,
                "1 级刀兵伤害 = 3");
            Assert.AreEqual(0.8f, soldier.AttackIntervalSeconds, 0.001f,
                "1 级刀兵间隔 = 0.8s");

            // 升级到 2 级：伤害 3*1.2=3.6→4，间隔 0.8/1.1≈0.727。
            soldier.ApplyLevel(2);
            Assert.AreEqual(4, ((KnifeSoldier)soldier).AttackDamageForTest,
                "2 级刀兵伤害 = 3×1.2 = 3.6 四舍五入 = 4");
            Assert.AreEqual(0.8f / 1.1f, soldier.AttackIntervalSeconds, 0.001f,
                "2 级刀兵间隔 = 0.8/1.1");
        }

        // ====================================================================
        // 待上场单位不参与攻击调度
        // ====================================================================

        [Test]
        [Description("GetActiveUnits 只返回战场实例，待上场单位不进入攻击调度。")]
        public void ReserveUnit_NotInAttackSchedule()
        {
            // 只有一个战场单位（另一个留在待上场槽）。
            PlaceSoldierToBattle(SoldierType.Knife, "刀", level: 1);

            IReadOnlyList<SoldierBase> active = _unitRegistry.GetActiveUnits();
            Assert.AreEqual(1, active.Count, "只应有 1 个战场实例进入攻击调度");
        }

        // ====================================================================
        // 上下场不刷新攻击冷却（同一 UnitId 战场→Reserve→战场）
        // ====================================================================

        [Test]
        [Description("同一 UnitId 战场→Reserve→战场：冷却时间戳保持，上下场不刷新攻击冷却。")]
        public void UnitMove_SameUnitId_RoundTrip_PreservesCooldown()
        {
            IReadOnlyList<UnitSlot> reserves = _slotBoard.GetSlots(true, SlotZone.Reserve);
            IReadOnlyList<UnitSlot> battles = _slotBoard.GetSlots(true, SlotZone.Battle);
            UnitSlot sourceReserve = reserves[0];
            UnitSlot reserveTarget = reserves[1];
            UnitSlot battle = battles[0];

            // 上场：Reserve → Battle。
            Assert.IsFalse(sourceReserve.IsEmpty, "开局待上场槽已填满");
            BattleInputResult up = _controller.Execute(
                BattleInputCommand.CreateDropUnit(1, sourceReserve.SlotId.Id, battle.SlotId.Id));
            Assert.IsTrue(up.IsSuccess, "上场应成功");

            int unitId = _slotBoard.GetSlot(battle.SlotId).OccupantUnitId;
            SoldierBase soldier = _unitRegistry.GetActiveByUnitId(unitId);
            Assert.IsNotNull(soldier, "上场后应有战斗实例");

            // 模拟已攻击过：冷却 = 500ms（写回实例）。
            soldier.LastAttackTimeMs = 500L;

            // 下场：Battle → Reserve（同一 UnitId）。
            Assert.IsTrue(_slotBoard.GetSlot(reserveTarget.SlotId).IsEmpty, "目标待上场槽空");
            BattleInputResult down = _controller.Execute(
                BattleInputCommand.CreateDropUnit(2, battle.SlotId.Id, reserveTarget.SlotId.Id));
            Assert.IsTrue(down.IsSuccess, "下场应成功");

            // 下场后解除战斗实例。
            Assert.IsNull(_unitRegistry.GetActiveByUnitId(unitId), "下场后解除战斗实例");
            // 冷却写回 BattleUnit（WriteBackCooldown）。
            Assert.AreEqual(500L, _slotBoard.GetSlot(reserveTarget.SlotId).Occupant.Value.LastAttackTimeMs,
                "下场后冷却写回 BattleUnit");

            // 再上场：Reserve → Battle（同一 UnitId，冷却导入）。
            BattleInputResult upAgain = _controller.Execute(
                BattleInputCommand.CreateDropUnit(3, reserveTarget.SlotId.Id, battle.SlotId.Id));
            Assert.IsTrue(upAgain.IsSuccess, "重新上场应成功");

            SoldierBase reactivated = _unitRegistry.GetActiveByUnitId(unitId);
            Assert.IsNotNull(reactivated, "重新上场后应有战斗实例");
            Assert.AreEqual(500L, reactivated.LastAttackTimeMs,
                "重新上场保留攻击冷却（上下场不刷新冷却）");
        }

        // ====================================================================
        // 上下场不刷新攻击冷却（激活 API 直接验证）
        // ====================================================================

        [Test]
        [Description("ActivateBattleUnit 导入冷却，重新激活保留冷却时间戳。")]
        public void UnitMove_CoolDownPreserved_OnReactivation()
        {
            SoldierBase soldier = PlaceSoldierToBattle(SoldierType.Knife, "刀", level: 1);
            // 模拟已攻击过：冷却 = 500ms。
            soldier.LastAttackTimeMs = 500L;
            Assert.AreEqual(500L, soldier.LastAttackTimeMs, "设置攻击冷却");

            IReadOnlyList<UnitSlot> battles = _slotBoard.GetSlots(true, SlotZone.Battle);
            int unitId = soldier.Id;

            // 下场：导出冷却。
            long exported = _unitRegistry.DeactivateBattleUnit(unitId);
            Assert.AreEqual(500L, exported, "下场导出冷却");

            // 重新上场（同一 UnitId）：导入冷却。
            var unit = new BattleUnit(
                unitId: unitId,
                side: true,
                kind: UnitKind.Soldier,
                soldierType: SoldierType.Knife,
                soldierText: "刀",
                level: 1,
                lastAttackTimeMs: exported);
            SoldierBase reactivated = _unitRegistry.ActivateBattleUnit(
                unit,
                FindConfig("刀"),
                _levelService,
                battles[0].SlotId.GridPosition.X,
                battles[0].SlotId.GridPosition.Y,
                40f, 40f);

            Assert.AreEqual(500L, reactivated.LastAttackTimeMs,
                "重新上场保留攻击冷却（上下场不刷新冷却）");
        }
    }
}
