using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Input
{
    // ============================================================================
    // 输入事务测试（最终方案：征兵 Recruit + 换槽/合并 DropUnit）
    // ----------------------------------------------------------------------------
    // 验证内容（最终方案"核心架构"）：
    //   1. 征兵成功：生成完整批次 → 扣费 → 清空待上场单位 → 填满槽位。
    //   2. 征兵失败：馒头不足时不清槽、不扣费。
    //   3. 征兵不影响任何战场单位。
    //   4. 换槽：空目标槽把单位换到目标槽，源槽变空，SlotId 不变。
    //   5. 合并：同阵营/同兵种/同等级且未满级时目标升一级，源单位消失。
    //   6. 合并失败：满级/不同兵种/不同等级/跨阵营时不修改状态并弹回。
    //   7. 相同 CommandId 重复提交返回首次结果（决策 0.8）。
    //
    // 测试策略：
    //   使用真实的 UnitSlotBoard、RecruitManager、UnitLevelService、BattleEconomy、
    //   UnitRegistry、BattleConfigSnapshot 构造 BattleInputController。
    //   不接触 Scene、FUI 或资源加载，纯逻辑 EditMode 测试。
    // ============================================================================

    /// <summary>
    /// BattleInputController 输入事务测试（最终方案：征兵 + 换槽/合并）。
    /// </summary>
    /// <remarks>
    /// <para>覆盖最终方案的核心验收项：开局免费填满、点击征兵才扣馒头、征兵清除所有
    /// 待上场单位并重新填满 1 级四兵、四种空槽换槽组合免费、合并只发生一次、
    /// 目标满级/不匹配弹回、CommandId 去重。</para>
    /// </remarks>
    [TestFixture]
    internal class BattleInputControllerTests
    {
        // ====================================================================
        // 测试常量
        // ====================================================================

        private const float UnitWidth = 40f;
        private const float UnitHeight = 40f;
        private const float CellSize = 80f;
        private const int GridSize = 80;
        private const int OpponentAttackMultiplier = 1;

        // ====================================================================
        // 测试上下文（由 SetUp 重建）
        // ====================================================================

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
        private MapData _mapData;
        private BattleConfigSnapshot _configSnapshot;
        private UnitLevelService _levelService;
        private UnitSlotBoard _slotBoard;
        private RecruitManager _recruitManager;
        private BattleInputController _controller;

        // ====================================================================
        // Setup / TearDown
        // ====================================================================

        [SetUp]
        public void SetUp()
        {
            BuildFixture();
            _controller.StartGame();
            _economy.StartGame();
            // 发放初始金币供征兵使用。
            _battleState.ApplyGoldDelta(true, BattleState.DefaultInitialGold);
            _battleState.ApplyGoldDelta(false, BattleState.DefaultInitialGold);
        }

        [TearDown]
        public void TearDown()
        {
            _unitRegistry?.GameOver();
            _slotBoard?.GameOver();
        }

        // ====================================================================
        // 夹具构建
        // ====================================================================

        private void BuildFixture()
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

            _mapData = BuildTestMap();
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

            _slotBoard = new UnitSlotBoard(_levelService.MaxLevel, _configSnapshot.GeneralCatalog);
            _slotBoard.Initialize(_mapData, RecruitDefinitions.ReserveSlotCount);

            _recruitManager = new RecruitManager(
                new SeededRandomSource(seed: 12345), _slotBoard, RecruitDefinitions.ReserveSlotCount);

            // 开局免费填满待上场槽。
            _slotBoard.ReplaceReserve(true, _recruitManager.GenerateBatch(true));
            _slotBoard.ReplaceReserve(false, _recruitManager.GenerateBatch(false));

            _battleState = new BattleState();
            _economy = new BattleEconomy(_battleState, refreshCostIncrement: 2);

            _controller = new BattleInputController(
                _slotBoard, _recruitManager, _levelService, _economy, _unitRegistry, _configSnapshot);
        }

        /// <summary>
        /// 构建测试地图：5 列 × 5 行。玩家方可建造格：(0,0)、(1,0)。对手方可建造格：(0,4)。
        /// </summary>
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
            cells[3 * height + 3] = new GridCell(GridCellKind.Blocked, BuildableSide.None);

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

        /// <summary>构建测试配置快照，包含四兵种单位配置与等级倍率。</summary>
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
                sourceTag: "Test",
                generalCatalog: new GeneralCatalogSnapshot(new[] { ZhangFeiDefinition() }));
        }

        /// <summary>构造单位配置快照辅助方法。</summary>
        private static UnitConfigSnapshot MakeUnitConfig(
            int index, string text, string animKey,
            float rangeCells, int damage, float interval)
        {
            return new UnitConfigSnapshot(
                index, text, animKey, rangeCells, damage, interval,
                "单体", "nearest");
        }

        /// <summary>张飞配置（有序配方：张左飞右；枪系远程定位为 Spear 兵种）。</summary>
        private static GeneralConfigSnapshot ZhangFeiDefinition()
            => new GeneralConfigSnapshot(
                1, "张飞", "张", new[] { "张", "飞" }, GeneralCombatArchetype.Pike,
                2.5f, 15, 1f, "近战枪击", "nearest", "SpearSoldier", "default", "", 0, 1);

        // ====================================================================
        // 槽位辅助
        // ====================================================================

        /// <summary>获取玩家方第一个待上场槽位。</summary>
        private UnitSlot GetFirstPlayerReserveSlot()
        {
            IReadOnlyList<UnitSlot> slots = _slotBoard.GetSlots(true, SlotZone.Reserve);
            Assert.GreaterOrEqual(slots.Count, 1, "玩家待上场槽至少 1 个");
            return slots[0];
        }

        /// <summary>获取玩家方第一个战场槽位。</summary>
        private UnitSlot GetFirstPlayerBattleSlot()
        {
            IReadOnlyList<UnitSlot> slots = _slotBoard.GetSlots(true, SlotZone.Battle);
            Assert.GreaterOrEqual(slots.Count, 1, "玩家战场槽至少 1 个");
            return slots[0];
        }

        /// <summary>构造玩家方刀兵单位。</summary>
        private static BattleUnit MakeKnife(int unitId, int level)
            => new BattleUnit(unitId, true, UnitKind.Soldier, SoldierType.Knife, "刀", level);

        /// <summary>
        /// 用指定单位列表填满玩家方全部待上场槽（ReplaceReserve 严格要求批次数量等于槽位数）。
        /// </summary>
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

        // ====================================================================
        // 征兵场景
        // ====================================================================

        [Test]
        [Description("征兵成功：扣馒头，清除全部待上场单位并重新填满 1 级四兵。")]
        public void Recruit_Success_DeductsGoldAndRefillsReserve()
        {
            // 开局已免费填满，先记录费用。
            int goldBefore = _economy.GetBalance(true);
            int recruitCost = _economy.GetRefreshCost(true);

            // 手动放一个 3 级单位到待上场槽，验证征兵会清除高级单位。
            UnitSlot firstReserve = GetFirstPlayerReserveSlot();
            var level3Unit = new BattleUnit(
                unitId: 9999, side: true, kind: UnitKind.Soldier,
                soldierType: SoldierType.Knife, soldierText: "刀", level: 3);
            FillReserveAll(level3Unit);

            BattleInputResult result = _controller.Execute(
                BattleInputCommand.CreateRecruit(commandId: 1, playerSide: true));

            Assert.IsTrue(result.IsSuccess, "征兵应成功");
            Assert.AreEqual(goldBefore - recruitCost, _economy.GetBalance(true), "征兵扣馒头");

            // 待上场槽全部填满且全部为 1 级。
            IReadOnlyList<UnitSlot> reserveSlots = _slotBoard.GetSlots(true, SlotZone.Reserve);
            Assert.AreEqual(RecruitDefinitions.ReserveSlotCount, reserveSlots.Count, "待上场槽数量不变");
            foreach (UnitSlot slot in reserveSlots)
            {
                Assert.IsFalse(slot.IsEmpty, "征兵后待上场槽不应为空");
                Assert.AreEqual(1, slot.Occupant.Value.Level, "征兵生成 1 级单位");
            }
        }

        [Test]
        [Description("征兵失败：馒头不足时不清槽、不扣费。")]
        public void Recruit_InsufficientGold_NoClearNoDeduct()
        {
            _battleState.ApplyGoldSet(true, 0);

            // 记录征兵前的待上场槽占用。
            IReadOnlyList<UnitSlot> before = _slotBoard.GetSlots(true, SlotZone.Reserve);
            var beforeOccupants = new List<BattleUnit?>();
            foreach (UnitSlot slot in before)
            {
                beforeOccupants.Add(slot.Occupant);
            }

            BattleInputResult result = _controller.Execute(
                BattleInputCommand.CreateRecruit(commandId: 2, playerSide: true));

            Assert.IsFalse(result.IsSuccess, "馒头不足应失败");
            Assert.AreEqual(BattleInputRejectReason.InsufficientGoldForRecruit, result.RejectReason);
            Assert.AreEqual(0, _economy.GetBalance(true), "未扣费");

            // 待上场槽未被清除。
            IReadOnlyList<UnitSlot> after = _slotBoard.GetSlots(true, SlotZone.Reserve);
            for (int i = 0; i < beforeOccupants.Count; i++)
            {
                Assert.AreEqual(beforeOccupants[i].HasValue, after[i].IsEmpty == false,
                    $"待上场槽 {i} 未被清除");
            }
        }

        [Test]
        [Description("征兵不影响任何战场单位。")]
        public void Recruit_DoesNotAffectBattleUnits()
        {
            // 先把一个单位上场到战场槽。
            UnitSlot reserve = GetFirstPlayerReserveSlot();
            UnitSlot battle = GetFirstPlayerBattleSlot();
            Assert.IsFalse(reserve.IsEmpty, "开局待上场槽应已填满");
            _controller.Execute(
                BattleInputCommand.CreateDropUnit(10, reserve.SlotId.Id, battle.SlotId.Id));

            int battleOccupantIdBefore = _slotBoard.GetSlot(battle.SlotId).OccupantUnitId;

            BattleInputResult result = _controller.Execute(
                BattleInputCommand.CreateRecruit(commandId: 3, playerSide: true));
            Assert.IsTrue(result.IsSuccess, "征兵应成功");

            Assert.AreEqual(battleOccupantIdBefore, _slotBoard.GetSlot(battle.SlotId).OccupantUnitId,
                "征兵不影响战场槽单位");
        }

        // ====================================================================
        // 换槽场景
        // ====================================================================

        [Test]
        [Description("换槽：空目标槽把单位换到目标槽，源槽变空，SlotId 不变。")]
        public void DropUnit_EmptyTarget_MovesUnit_SourceEmpty()
        {
            IReadOnlyList<UnitSlot> reserves = _slotBoard.GetSlots(true, SlotZone.Reserve);
            UnitSlot source = reserves[0];
            UnitSlot target = reserves[1];
            Assert.IsFalse(source.IsEmpty, "源槽应有单位");

            // 开局征兵会填满待上场槽；先把目标单位移到空战场槽，构造真实空目标。
            UnitSlot stagingBattle = GetFirstPlayerBattleSlot();
            BattleInputResult staged = _controller.Execute(
                BattleInputCommand.CreateDropUnit(900, target.SlotId.Id, stagingBattle.SlotId.Id));
            Assert.IsTrue(staged.IsSuccess, "测试准备：目标单位应能先上场");
            target = _slotBoard.GetSlotById(target.SlotId.Id);
            Assert.IsTrue(target.IsEmpty, "目标槽应为空");

            int sourceSlotId = source.SlotId.Id;
            int targetSlotId = target.SlotId.Id;
            int movedUnitId = source.OccupantUnitId;

            BattleInputResult result = _controller.Execute(
                BattleInputCommand.CreateDropUnit(1, sourceSlotId, targetSlotId));

            Assert.IsTrue(result.IsSuccess, "换槽应成功");
            Assert.IsTrue(_slotBoard.GetSlotById(sourceSlotId).IsEmpty, "源槽变空");
            Assert.AreEqual(movedUnitId, _slotBoard.GetSlotById(targetSlotId).OccupantUnitId,
                "目标槽承载被移动单位");
            Assert.AreEqual(sourceSlotId, _slotBoard.GetSlotById(sourceSlotId).SlotId.Id, "源槽 SlotId 不变");
            Assert.AreEqual(targetSlotId, _slotBoard.GetSlotById(targetSlotId).SlotId.Id, "目标槽 SlotId 不变");
        }

        [Test]
        [Description("换槽：源槽为空返回 SourceSlotEmpty，不修改状态。")]
        public void DropUnit_EmptySource_ReturnsSourceSlotEmpty()
        {
            IReadOnlyList<UnitSlot> reserves = _slotBoard.GetSlots(true, SlotZone.Reserve);
            UnitSlot source = reserves[0];
            UnitSlot target = reserves[1];

            // 先把 source 清空（把 source 的单位换到空战场槽）。
            FillReserveAll();
            IReadOnlyList<UnitSlot> battles = _slotBoard.GetSlots(true, SlotZone.Battle);
            _controller.Execute(
                BattleInputCommand.CreateDropUnit(1, source.SlotId.Id, battles[0].SlotId.Id));
            UnitSlot emptySource = _slotBoard.GetSlot(source.SlotId);
            Assert.IsTrue(emptySource.IsEmpty, "前置：source 已清空");

            BattleInputResult result = _controller.Execute(
                BattleInputCommand.CreateDropUnit(2, emptySource.SlotId.Id, target.SlotId.Id));

            Assert.IsFalse(result.IsSuccess, "空源槽应失败");
            Assert.AreEqual(BattleInputRejectReason.SourceSlotEmpty, result.RejectReason);
        }

        [Test]
        [Description("换槽：非法槽位 ID 返回 InvalidSourceSlot / InvalidTargetSlot。")]
        public void DropUnit_InvalidSlots_ReturnsRejections()
        {
            IReadOnlyList<UnitSlot> reserves = _slotBoard.GetSlots(true, SlotZone.Reserve);
            UnitSlot valid = reserves[0];

            BattleInputResult badSource = _controller.Execute(
                BattleInputCommand.CreateDropUnit(3, 9999, valid.SlotId.Id));
            Assert.IsFalse(badSource.IsSuccess, "非法源槽应失败");
            Assert.AreEqual(BattleInputRejectReason.InvalidSourceSlot, badSource.RejectReason);

            BattleInputResult badTarget = _controller.Execute(
                BattleInputCommand.CreateDropUnit(4, valid.SlotId.Id, 9999));
            Assert.IsFalse(badTarget.IsSuccess, "非法目标槽应失败");
            Assert.AreEqual(BattleInputRejectReason.InvalidTargetSlot, badTarget.RejectReason);
        }

        // ====================================================================
        // 合并场景
        // ====================================================================

        [Test]
        [Description("合并：同兵种同等级目标升一级，源单位消失，结果保留目标 UnitId。")]
        public void DropUnit_Merge_LevelsUpTarget_SourceDisappears()
        {
            IReadOnlyList<UnitSlot> reserves = _slotBoard.GetSlots(true, SlotZone.Reserve);
            UnitSlot source = reserves[0];
            UnitSlot target = reserves[1];

            // 源与目标都是 1 级刀兵。
            FillReserveAll(
                MakeKnife(100, 1),
                MakeKnife(200, 1));

            int targetUnitId = _slotBoard.GetSlot(target.SlotId).OccupantUnitId;

            BattleInputResult result = _controller.Execute(
                BattleInputCommand.CreateDropUnit(5, source.SlotId.Id, target.SlotId.Id));

            Assert.IsTrue(result.IsSuccess, "合并应成功");
            Assert.IsTrue(_slotBoard.GetSlotById(source.SlotId.Id).IsEmpty, "源槽变空（源单位消失）");
            Assert.AreEqual(targetUnitId, _slotBoard.GetSlotById(target.SlotId.Id).OccupantUnitId,
                "结果保留目标 UnitId");
            Assert.AreEqual(2, _slotBoard.GetSlotById(target.SlotId.Id).Occupant.Value.Level,
                "目标单位升一级");
        }

        [Test]
        [Description("合并只发生一次：不自动连锁，源单位消失后不再合并。")]
        public void DropUnit_Merge_OnlyOnce_NoChaining()
        {
            IReadOnlyList<UnitSlot> reserves = _slotBoard.GetSlots(true, SlotZone.Reserve);
            UnitSlot source = reserves[0];
            UnitSlot target = reserves[1];

            FillReserveAll(MakeKnife(100, 1), MakeKnife(200, 1));

            _controller.Execute(BattleInputCommand.CreateDropUnit(5, source.SlotId.Id, target.SlotId.Id));

            // 只合并一次：目标应为 2 级，且源槽为空（不会有第二张 1 级卡自动补上继续合并）。
            Assert.AreEqual(2, _slotBoard.GetSlotById(target.SlotId.Id).Occupant.Value.Level, "目标为 2 级");
            Assert.IsTrue(_slotBoard.GetSlotById(source.SlotId.Id).IsEmpty, "源槽为空");
        }

        [Test]
        [Description("互换：不同兵种互换位置，双方单位属性不变（不再拒绝）。")]
        public void DropUnit_Swap_DifferentSoldierType_Swaps()
        {
            IReadOnlyList<UnitSlot> reserves = _slotBoard.GetSlots(true, SlotZone.Reserve);
            UnitSlot source = reserves[0];
            UnitSlot target = reserves[1];

            // 源刀兵，目标弓兵（不同兵种不可合并 → 互换）。
            FillReserveAll(
                MakeKnife(100, 1),
                new BattleUnit(200, true, UnitKind.Soldier, SoldierType.Bow, "弓", 1));

            BattleInputResult result = _controller.Execute(
                BattleInputCommand.CreateDropUnit(6, source.SlotId.Id, target.SlotId.Id));

            Assert.IsTrue(result.IsSuccess, "不同兵种应互换而非拒绝");
            Assert.AreEqual(200, _slotBoard.GetSlotById(source.SlotId.Id).OccupantUnitId,
                "源槽换入目标单位");
            Assert.AreEqual(100, _slotBoard.GetSlotById(target.SlotId.Id).OccupantUnitId,
                "目标槽换入源单位");
            Assert.AreEqual(0, _unitRegistry.Count, "R→R 互换不创建战斗实例");
        }

        [Test]
        [Description("互换：不同等级互换位置，双方单位属性不变（不再拒绝）。")]
        public void DropUnit_Swap_DifferentLevel_Swaps()
        {
            IReadOnlyList<UnitSlot> reserves = _slotBoard.GetSlots(true, SlotZone.Reserve);
            UnitSlot source = reserves[0];
            UnitSlot target = reserves[1];

            // 源 2 级、目标 1 级；互换后源单位应在目标槽保持 2 级。
            FillReserveAll(MakeKnife(100, 2), MakeKnife(200, 1));

            BattleInputResult result = _controller.Execute(
                BattleInputCommand.CreateDropUnit(7, source.SlotId.Id, target.SlotId.Id));

            Assert.IsTrue(result.IsSuccess, "不同等级应互换而非拒绝");
            Assert.AreEqual(200, _slotBoard.GetSlotById(source.SlotId.Id).OccupantUnitId,
                "源槽换入目标单位");
            Assert.AreEqual(100, _slotBoard.GetSlotById(target.SlotId.Id).OccupantUnitId,
                "目标槽换入源单位");
            Assert.AreEqual(1, _slotBoard.GetSlotById(source.SlotId.Id).Occupant.Value.Level,
                "源槽换入的目标单位保留 1 级");
            Assert.AreEqual(2, _slotBoard.GetSlotById(target.SlotId.Id).Occupant.Value.Level,
                "目标槽换入的源单位保留 2 级");
        }

        [Test]
        [Description("互换：目标已满级互换位置，不修改任何单位属性（不再拒绝）。")]
        public void DropUnit_Swap_MaxLevel_Swaps()
        {
            IReadOnlyList<UnitSlot> reserves = _slotBoard.GetSlots(true, SlotZone.Reserve);
            UnitSlot source = reserves[0];
            UnitSlot target = reserves[1];

            // 目标 3 级 = 最大等级，不可再合并 → 互换。
            FillReserveAll(MakeKnife(100, 3), MakeKnife(200, 3));

            BattleInputResult result = _controller.Execute(
                BattleInputCommand.CreateDropUnit(8, source.SlotId.Id, target.SlotId.Id));

            Assert.IsTrue(result.IsSuccess, "满级目标应互换而非拒绝");
            Assert.AreEqual(200, _slotBoard.GetSlotById(source.SlotId.Id).OccupantUnitId,
                "源槽换入目标单位");
            Assert.AreEqual(100, _slotBoard.GetSlotById(target.SlotId.Id).OccupantUnitId,
                "目标槽换入源单位");
            Assert.AreEqual(3, _slotBoard.GetSlotById(target.SlotId.Id).Occupant.Value.Level,
                "互换不改变等级");
        }

        [Test]
        [Description("合并失败：跨阵营不合并，不修改状态。")]
        public void DropUnit_Merge_CrossSide_Rejects()
        {
            IReadOnlyList<UnitSlot> playerReserves = _slotBoard.GetSlots(true, SlotZone.Reserve);
            IReadOnlyList<UnitSlot> opponentReserves = _slotBoard.GetSlots(false, SlotZone.Reserve);
            UnitSlot source = playerReserves[0];
            UnitSlot target = opponentReserves[0];

            FillReserveAll(MakeKnife(100, 1));

            BattleInputResult result = _controller.Execute(
                BattleInputCommand.CreateDropUnit(9, source.SlotId.Id, target.SlotId.Id));

            Assert.IsFalse(result.IsSuccess, "跨阵营应失败");
            Assert.AreEqual(BattleInputRejectReason.CrossSideMerge, result.RejectReason);
            Assert.IsFalse(_slotBoard.GetSlotById(source.SlotId.Id).IsEmpty, "源槽未变");
        }

        // ====================================================================
        // 互换（Swap）四向覆盖（含 Runtime 迁移语义）
        // ====================================================================

        /// <summary>把指定待上场槽的单位换到第一个空战场槽（控制器 Move），返回目标战场槽。</summary>
        private UnitSlot MoveReserveToBattle(UnitSlot reserve)
        {
            IReadOnlyList<UnitSlot> battles = _slotBoard.GetSlots(true, SlotZone.Battle);
            for (int i = 0; i < battles.Count; i++)
            {
                if (battles[i].IsEmpty)
                {
                    BattleInputResult result = _controller.Execute(
                        BattleInputCommand.CreateDropUnit(3000 + i, reserve.SlotId.Id, battles[i].SlotId.Id));
                    Assert.IsTrue(result.IsSuccess, "测试前置：Move 到战场槽应成功");
                    return _slotBoard.GetSlot(battles[i].SlotId);
                }
            }

            throw new InvalidOperationException("测试前置：无空战场槽可放入单位");
        }

        [Test]
        [Description("互换 R→R：待上场源与待上场目标互换位置，不触碰 Runtime。")]
        public void DropUnit_Swap_ReserveToReserve_SwapsOccupants()
        {
            IReadOnlyList<UnitSlot> reserves = _slotBoard.GetSlots(true, SlotZone.Reserve);
            UnitSlot source = reserves[0];
            UnitSlot target = reserves[1];

            FillReserveAll(
                MakeKnife(100, 1),
                new BattleUnit(200, true, UnitKind.Soldier, SoldierType.Bow, "弓", 1));

            BattleInputResult result = _controller.Execute(
                BattleInputCommand.CreateDropUnit(31, source.SlotId.Id, target.SlotId.Id));

            Assert.IsTrue(result.IsSuccess, "R→R 互换应成功");
            Assert.AreEqual(200, _slotBoard.GetSlotById(source.SlotId.Id).OccupantUnitId, "源槽换入目标单位");
            Assert.AreEqual(100, _slotBoard.GetSlotById(target.SlotId.Id).OccupantUnitId, "目标槽换入源单位");
            Assert.AreEqual(0, _unitRegistry.Count, "R→R 互换不创建战斗实例");
        }

        [Test]
        [Description("互换 R→B：待上场源上场，战场目标下场保留冷却。")]
        public void DropUnit_Swap_ReserveToBattle_SwapsOccupants()
        {
            IReadOnlyList<UnitSlot> reserves = _slotBoard.GetSlots(true, SlotZone.Reserve);
            UnitSlot source = reserves[0];

            // 弓兵（冷却 5000ms）先上场到战场槽；刀兵源与它互换。
            FillReserveAll(
                MakeKnife(100, 1),
                new BattleUnit(200, true, UnitKind.Soldier, SoldierType.Bow, "弓", 1, 5000L));
            UnitSlot battle = MoveReserveToBattle(reserves[1]);
            Assert.IsFalse(battle.IsEmpty, "前置：战场槽已占用弓兵");
            Assert.IsNotNull(_unitRegistry.GetActiveByUnitId(200), "前置：弓兵已有活动实例");
            Assert.AreEqual(5000L, _unitRegistry.GetActiveByUnitId(200).LastAttackTimeMs,
                "前置：弓兵活动实例冷却为 5000ms");

            BattleInputResult result = _controller.Execute(
                BattleInputCommand.CreateDropUnit(32, source.SlotId.Id, battle.SlotId.Id));

            Assert.IsTrue(result.IsSuccess, "R→B 互换应成功");
            // 槽位：源待上场槽换入弓兵，战场槽换入刀兵。
            Assert.AreEqual(200, _slotBoard.GetSlotById(source.SlotId.Id).OccupantUnitId, "源待上场槽换入弓兵");
            Assert.AreEqual(100, _slotBoard.GetSlotById(battle.SlotId.Id).OccupantUnitId, "战场槽换入刀兵");
            // Runtime：弓兵下场（解除实例、保留冷却），刀兵首次上场激活。
            Assert.IsNull(_unitRegistry.GetActiveByUnitId(200), "弓兵下场后应解除活动实例");
            Assert.AreEqual(5000L, _slotBoard.GetSlotById(source.SlotId.Id).Occupant.Value.LastAttackTimeMs,
                "弓兵下场保留攻击冷却");
            SoldierBase knifeActive = _unitRegistry.GetActiveByUnitId(100);
            Assert.IsNotNull(knifeActive, "刀兵应激活战斗实例");
            Assert.AreEqual(battle.SlotId.GridPosition.X, knifeActive.GridX, "刀兵在目标战场格 X");
            Assert.AreEqual(battle.SlotId.GridPosition.Y, knifeActive.GridY, "刀兵在目标战场格 Y");
            Assert.AreEqual(1, _unitRegistry.Count, "场上仍只有一个活动实例（刀兵）");
        }

        [Test]
        [Description("互换 B→R：战场源下场保留冷却，待上场目标上场。")]
        public void DropUnit_Swap_BattleToReserve_SwapsOccupants()
        {
            IReadOnlyList<UnitSlot> reserves = _slotBoard.GetSlots(true, SlotZone.Reserve);
            UnitSlot target = reserves[1];

            // 刀兵（冷却 0）先上场到战场槽；待上场弓兵（冷却 7000ms）与它互换。
            FillReserveAll(
                MakeKnife(100, 1),
                new BattleUnit(200, true, UnitKind.Soldier, SoldierType.Bow, "弓", 1, 7000L));
            UnitSlot sourceBattle = MoveReserveToBattle(reserves[0]);
            Assert.IsFalse(sourceBattle.IsEmpty, "前置：战场源槽已占用刀兵");
            Assert.IsNotNull(_unitRegistry.GetActiveByUnitId(100), "前置：刀兵已有活动实例");

            BattleInputResult result = _controller.Execute(
                BattleInputCommand.CreateDropUnit(33, sourceBattle.SlotId.Id, target.SlotId.Id));

            Assert.IsTrue(result.IsSuccess, "B→R 互换应成功");
            // 槽位：战场槽换入弓兵，待上场槽换入刀兵。
            Assert.AreEqual(200, _slotBoard.GetSlotById(sourceBattle.SlotId.Id).OccupantUnitId, "战场槽换入弓兵");
            Assert.AreEqual(100, _slotBoard.GetSlotById(target.SlotId.Id).OccupantUnitId, "待上场槽换入刀兵");
            // Runtime：刀兵下场（解除实例、保留冷却），弓兵首次上场激活。
            Assert.IsNull(_unitRegistry.GetActiveByUnitId(100), "刀兵下场后应解除活动实例");
            Assert.AreEqual(0L, _slotBoard.GetSlotById(target.SlotId.Id).Occupant.Value.LastAttackTimeMs,
                "刀兵下场保留攻击冷却");
            SoldierBase bowActive = _unitRegistry.GetActiveByUnitId(200);
            Assert.IsNotNull(bowActive, "弓兵应激活战斗实例");
            Assert.AreEqual(sourceBattle.SlotId.GridPosition.X, bowActive.GridX, "弓兵在源战场格 X");
            Assert.AreEqual(sourceBattle.SlotId.GridPosition.Y, bowActive.GridY, "弓兵在源战场格 Y");
            Assert.AreEqual(7000L, bowActive.LastAttackTimeMs, "弓兵首次上场导入冷却");
            Assert.AreEqual(1, _unitRegistry.Count, "场上仍只有一个活动实例（弓兵）");
        }

        [Test]
        [Description("互换 B→B：两个活动实例互换战场格，双方世界位置更新。")]
        public void DropUnit_Swap_BattleToBattle_MovesBothActiveInstances()
        {
            IReadOnlyList<UnitSlot> reserves = _slotBoard.GetSlots(true, SlotZone.Reserve);

            FillReserveAll(
                MakeKnife(100, 1),
                new BattleUnit(200, true, UnitKind.Soldier, SoldierType.Bow, "弓", 1));
            UnitSlot battle0 = MoveReserveToBattle(reserves[0]);
            UnitSlot battle1 = MoveReserveToBattle(reserves[1]);
            Assert.IsFalse(battle0.IsEmpty, "前置：battle0 已占用");
            Assert.IsFalse(battle1.IsEmpty, "前置：battle1 已占用");

            SoldierBase knifeBefore = _unitRegistry.GetActiveByUnitId(100);
            SoldierBase bowBefore = _unitRegistry.GetActiveByUnitId(200);
            Assert.IsNotNull(knifeBefore, "前置：刀兵有活动实例");
            Assert.IsNotNull(bowBefore, "前置：弓兵有活动实例");
            Assert.AreEqual(battle0.SlotId.GridPosition.X, knifeBefore.GridX, "前置：刀兵在 battle0");
            Assert.AreEqual(battle1.SlotId.GridPosition.X, bowBefore.GridX, "前置：弓兵在 battle1");

            BattleInputResult result = _controller.Execute(
                BattleInputCommand.CreateDropUnit(34, battle0.SlotId.Id, battle1.SlotId.Id));

            Assert.IsTrue(result.IsSuccess, "B→B 互换应成功");
            // 槽位：battle0 换入弓兵，battle1 换入刀兵。
            Assert.AreEqual(200, _slotBoard.GetSlotById(battle0.SlotId.Id).OccupantUnitId, "battle0 换入弓兵");
            Assert.AreEqual(100, _slotBoard.GetSlotById(battle1.SlotId.Id).OccupantUnitId, "battle1 换入刀兵");
            // Runtime：双方仍活动，位置互换。
            SoldierBase knifeAfter = _unitRegistry.GetActiveByUnitId(100);
            SoldierBase bowAfter = _unitRegistry.GetActiveByUnitId(200);
            Assert.IsNotNull(knifeAfter, "刀兵仍活动");
            Assert.IsNotNull(bowAfter, "弓兵仍活动");
            Assert.AreEqual(battle1.SlotId.GridPosition.X, knifeAfter.GridX, "刀兵世界位置更新到 battle1");
            Assert.AreEqual(battle1.SlotId.GridPosition.Y, knifeAfter.GridY, "刀兵世界位置更新到 battle1");
            Assert.AreEqual(battle0.SlotId.GridPosition.X, bowAfter.GridX, "弓兵世界位置更新到 battle0");
            Assert.AreEqual(battle0.SlotId.GridPosition.Y, bowAfter.GridY, "弓兵世界位置更新到 battle0");
            Assert.AreEqual(2, _unitRegistry.Count, "B→B 互换不增删活动实例");
        }

        // ====================================================================
        // 事实发布（SlotChanged / UnitMerged）
        // ====================================================================

        /// <summary>构造带 SignalHub 的控制器（用于验证事实发布）。</summary>
        private BattleInternalSignalHub BuildControllerWithHub(out BattleInputController controller)
        {
            var hub = new BattleInternalSignalHub();
            controller = new BattleInputController(
                _slotBoard, _recruitManager, _levelService, _economy, _unitRegistry, _configSnapshot, hub);
            controller.StartGame();
            return hub;
        }

        [Test]
        [Description("互换发布两个槽位的最终状态事实（不再固定源槽为空）。")]
        public void DropUnit_Swap_PublishesBothSlotChangedFacts()
        {
            BattleInternalSignalHub hub = BuildControllerWithHub(out BattleInputController hubController);

            IReadOnlyList<UnitSlot> reserves = _slotBoard.GetSlots(true, SlotZone.Reserve);
            int sourceSlotId = reserves[0].SlotId.Id;
            int targetSlotId = reserves[1].SlotId.Id;

            FillReserveAll(
                MakeKnife(100, 1),
                new BattleUnit(200, true, UnitKind.Soldier, SoldierType.Bow, "弓", 1));

            var published = new List<SlotChangedFact>();
            hub.SlotChanged.Subscribe(fact => published.Add(fact));

            BattleInputResult result = hubController.Execute(
                BattleInputCommand.CreateDropUnit(41, sourceSlotId, targetSlotId));

            Assert.IsTrue(result.IsSuccess, "互换应成功");
            Assert.AreEqual(2, published.Count, "应发布两个槽位的最终状态");
            SlotChangedFact sourceFact = published.Find(f => f.SlotId.Id == sourceSlotId);
            SlotChangedFact targetFact = published.Find(f => f.SlotId.Id == targetSlotId);
            Assert.AreEqual(sourceSlotId, sourceFact.SlotId.Id, "源槽事实已发布");
            Assert.AreEqual(targetSlotId, targetFact.SlotId.Id, "目标槽事实已发布");
            Assert.AreEqual(200, sourceFact.Occupant?.UnitId ?? -1, "源槽最终占用弓兵");
            Assert.AreEqual(100, targetFact.Occupant?.UnitId ?? -1, "目标槽最终占用刀兵");
        }

        [Test]
        [Description("合并发布 UnitMerged 事实：目标槽、升级后的单位与新等级。")]
        public void DropUnit_Merge_PublishesUnitMergedFact()
        {
            BattleInternalSignalHub hub = BuildControllerWithHub(out BattleInputController hubController);

            IReadOnlyList<UnitSlot> reserves = _slotBoard.GetSlots(true, SlotZone.Reserve);
            int sourceSlotId = reserves[0].SlotId.Id;
            int targetSlotId = reserves[1].SlotId.Id;

            FillReserveAll(MakeKnife(100, 1), MakeKnife(200, 1));

            UnitMergedFact merged = default;
            hub.UnitMerged.Subscribe(fact => merged = fact);

            BattleInputResult result = hubController.Execute(
                BattleInputCommand.CreateDropUnit(42, sourceSlotId, targetSlotId));

            Assert.IsTrue(result.IsSuccess, "合并应成功");
            Assert.AreEqual(targetSlotId, merged.TargetSlotId.Id, "合并事实携带目标槽");
            Assert.AreEqual(200, merged.MergedUnit.UnitId, "合并结果保留目标 UnitId");
            Assert.AreEqual(2, merged.NewLevel, "合并事实新等级为 2");
        }

        // ====================================================================
        // CommandId 去重场景（决策 0.8）
        // ====================================================================

        [Test]
        [Description("相同 CommandId 重复提交返回首次结果，不再次执行（征兵只扣一次费）。")]
        public void SameCommandId_Recruit_ReturnsFirstResult_NoDoubleDeduct()
        {
            int goldBefore = _economy.GetBalance(true);
            int recruitCost = _economy.GetRefreshCost(true);

            BattleInputResult first = _controller.Execute(
                BattleInputCommand.CreateRecruit(commandId: 50, playerSide: true));
            Assert.IsTrue(first.IsSuccess, "首次征兵应成功");

            BattleInputResult second = _controller.Execute(
                BattleInputCommand.CreateRecruit(commandId: 50, playerSide: true));
            Assert.AreEqual(first, second, "相同 CommandId 返回首次结果");

            Assert.AreEqual(goldBefore - recruitCost, _economy.GetBalance(true),
                "只扣一次费，不重复扣费");
        }

        [Test]
        [Description("不同 CommandId 即使 payload 相同也独立执行（各扣一次费）。")]
        public void DifferentCommandId_Recruit_IndependentExecutions()
        {
            int firstCost = _economy.GetRefreshCost(true);
            int secondCost = firstCost + 2; // 征兵后费用递增 +2
            _battleState.ApplyGoldDelta(true, firstCost + secondCost);
            int goldBefore = _economy.GetBalance(true);

            BattleInputResult first = _controller.Execute(
                BattleInputCommand.CreateRecruit(commandId: 60, playerSide: true));
            Assert.IsTrue(first.IsSuccess, "首次征兵应成功");

            BattleInputResult second = _controller.Execute(
                BattleInputCommand.CreateRecruit(commandId: 61, playerSide: true));
            Assert.IsTrue(second.IsSuccess, "第二次征兵应成功（费用递增）");

            Assert.AreEqual(goldBefore - firstCost - secondCost, _economy.GetBalance(true),
                "两次征兵各扣一次费");
        }

        // ====================================================================
        // 武将字上战场（放开战场区域限制）：零运行时实例语义
        // ====================================================================

        [Test]
        [Description("单字上阵：字牌 Move 到空战场槽成功，不创建士兵战斗实例，槽位与事实正常。")]
        public void DropUnit_GeneralPartToBattle_Moves_NoCombatInstance()
        {
            IReadOnlyList<UnitSlot> reserves = _slotBoard.GetSlots(true, SlotZone.Reserve);
            UnitSlot battle = GetFirstPlayerBattleSlot();
            FillReserveAll(BattleUnit.CreateGeneralPart(100, true, "张"));

            BattleInputResult result = _controller.Execute(
                BattleInputCommand.CreateDropUnit(501, reserves[0].SlotId.Id, battle.SlotId.Id));

            Assert.IsTrue(result.IsSuccess, "单字上阵应成功");
            UnitSlot battleAfter = _slotBoard.GetSlotById(battle.SlotId.Id);
            Assert.AreEqual(100, battleAfter.OccupantUnitId, "战场槽承载字牌");
            Assert.AreEqual(UnitKind.GeneralPart, battleAfter.Occupant.Value.Kind, "战场槽占用为字牌");
            Assert.IsTrue(_slotBoard.GetSlotById(reserves[0].SlotId.Id).IsEmpty, "源待上场槽变空");
            Assert.AreEqual(0, _unitRegistry.Count, "单字上阵不创建战斗实例");
            Assert.IsNull(_unitRegistry.GetActiveByUnitId(100), "字牌无战斗实例");
        }

        [Test]
        [Description("字牌与战场士兵互换：士兵回待上场并保留冷却，字牌进战场且不创建战斗实例。")]
        public void DropUnit_GeneralPart_SwapWithBattleSoldier_NoCombatInstance()
        {
            IReadOnlyList<UnitSlot> reserves = _slotBoard.GetSlots(true, SlotZone.Reserve);
            UnitSlot source = reserves[0];
            UnitSlot target = reserves[1];

            FillReserveAll(
                BattleUnit.CreateGeneralPart(100, true, "张"),
                new BattleUnit(200, true, UnitKind.Soldier, SoldierType.Bow, "弓", 1, 5000L));
            UnitSlot battle = MoveReserveToBattle(target);
            Assert.IsNotNull(_unitRegistry.GetActiveByUnitId(200), "前置：弓兵已有活动实例");

            BattleInputResult result = _controller.Execute(
                BattleInputCommand.CreateDropUnit(502, source.SlotId.Id, battle.SlotId.Id));

            Assert.IsTrue(result.IsSuccess, "字牌与士兵互换应成功");
            Assert.AreEqual(200, _slotBoard.GetSlotById(source.SlotId.Id).OccupantUnitId, "待上场槽换入弓兵");
            Assert.AreEqual(100, _slotBoard.GetSlotById(battle.SlotId.Id).OccupantUnitId, "战场槽换入字牌");
            Assert.AreEqual(UnitKind.GeneralPart, _slotBoard.GetSlotById(battle.SlotId.Id).Occupant.Value.Kind,
                "战场槽占用为字牌");
            Assert.IsNull(_unitRegistry.GetActiveByUnitId(200), "弓兵下场后解除战斗实例");
            Assert.AreEqual(5000L, _slotBoard.GetSlotById(source.SlotId.Id).Occupant.Value.LastAttackTimeMs,
                "弓兵下场保留冷却");
            Assert.AreEqual(0, _unitRegistry.Count, "场上无任何战斗实例");
        }

        [Test]
        [Description("刷新区武将字只交换不合成，也不创建战斗实例。")]
        public void DropUnit_GeneralParts_InReserve_SwapWithoutSynthesisOrRuntime()
        {
            IReadOnlyList<UnitSlot> reserves = _slotBoard.GetSlots(true, SlotZone.Reserve);
            FillReserveAll(
                BattleUnit.CreateGeneralPart(100, true, "飞"),
                BattleUnit.CreateGeneralPart(200, true, "张"));

            BattleInputResult result = _controller.Execute(
                BattleInputCommand.CreateDropUnit(510, reserves[0].SlotId.Id, reserves[1].SlotId.Id));

            Assert.IsTrue(result.IsSuccess, "刷新区武将字交换应成功");
            BattleUnit left = _slotBoard.GetSlotById(reserves[0].SlotId.Id).Occupant.Value;
            BattleUnit right = _slotBoard.GetSlotById(reserves[1].SlotId.Id).Occupant.Value;
            Assert.AreEqual(UnitKind.GeneralPart, left.Kind, "刷新区左槽仍是武将字");
            Assert.AreEqual(UnitKind.GeneralPart, right.Kind, "刷新区右槽仍是武将字");
            Assert.AreEqual("张", left.GeneralPartText);
            Assert.AreEqual("飞", right.GeneralPartText);
            Assert.AreEqual(0, _unitRegistry.Count, "刷新区交换不得创建战斗实例");
        }

        [Test]
        [Description("战场张左飞右：两字牌上场后合成双格张飞，左半格激活一个武将战斗实例。")]
        public void DropUnit_GeneralParts_SynthesizeInBattle_ActivatesGeneralInstance()
        {
            IReadOnlyList<UnitSlot> reserves = _slotBoard.GetSlots(true, SlotZone.Reserve);
            IReadOnlyList<UnitSlot> battles = _slotBoard.GetSlots(true, SlotZone.Battle);
            FillReserveAll(
                BattleUnit.CreateGeneralPart(100, true, "张"),
                BattleUnit.CreateGeneralPart(200, true, "飞"));

            BattleInputResult moveZhang = _controller.Execute(
                BattleInputCommand.CreateDropUnit(511, reserves[0].SlotId.Id, battles[0].SlotId.Id));
            Assert.IsTrue(moveZhang.IsSuccess, "张字应可 Move 到战场槽");
            BattleInputResult moveFei = _controller.Execute(
                BattleInputCommand.CreateDropUnit(512, reserves[1].SlotId.Id, battles[1].SlotId.Id));
            Assert.IsTrue(moveFei.IsSuccess, "飞字应可 Move 到战场槽");
            BattleInputResult result = moveFei;

            Assert.IsTrue(result.IsSuccess, "飞字放到张字右侧后应立即按最终布局合成");
            BattleUnit leftCell = _slotBoard.GetSlotById(battles[0].SlotId.Id).Occupant.Value;
            BattleUnit rightCell = _slotBoard.GetSlotById(battles[1].SlotId.Id).Occupant.Value;
            Assert.AreEqual(UnitKind.General, leftCell.Kind, "左槽为武将半格");
            Assert.AreEqual(UnitKind.General, rightCell.Kind, "右槽为武将半格");
            Assert.AreEqual(0, leftCell.GeneralCellIndex, "左槽为左半格");
            Assert.AreEqual(1, rightCell.GeneralCellIndex, "右槽为右半格");
            Assert.AreEqual(100, leftCell.UnitId, "Move 合成保留原地邻字 UnitId");
            Assert.AreEqual(100, rightCell.UnitId, "Move 合成保留原地邻字 UnitId");
            Assert.AreEqual("张飞", leftCell.GeneralName, "合成武将名正确");
            SoldierBase generalActive = _unitRegistry.GetActiveByUnitId(100);
            Assert.IsNotNull(generalActive, "合成武将应在左半格激活一个战斗实例");
            Assert.AreEqual(battles[0].SlotId.GridPosition.X, generalActive.GridX, "武将实例在左半格 X");
            Assert.AreEqual(battles[0].SlotId.GridPosition.Y, generalActive.GridY, "武将实例在左半格 Y");
            Assert.AreEqual(1, _unitRegistry.Count, "双格武将只激活一个实例");
        }

        [Test]
        [Description("战场完整武将单字下阵：整将解散为独立字牌，旧武将战斗实例只解除一次，分离字牌不创建实例。")]
        public void DropUnit_GeneralCell_DragToReserve_DeactivatesGeneralRuntimeOnce()
        {
            IReadOnlyList<UnitSlot> reserves = _slotBoard.GetSlots(true, SlotZone.Reserve);
            IReadOnlyList<UnitSlot> battles = _slotBoard.GetSlots(true, SlotZone.Battle);
            FillReserveAll(
                BattleUnit.CreateGeneralPart(100, true, "张"),
                BattleUnit.CreateGeneralPart(200, true, "飞"));

            Assert.IsTrue(_controller.Execute(
                BattleInputCommand.CreateDropUnit(521, reserves[0].SlotId.Id, battles[0].SlotId.Id)).IsSuccess);
            BattleInputResult synthesize = _controller.Execute(
                BattleInputCommand.CreateDropUnit(522, reserves[1].SlotId.Id, battles[1].SlotId.Id));
            Assert.IsTrue(synthesize.IsSuccess, "武将应先在战场合成");
            Assert.IsNotNull(_unitRegistry.GetActiveByUnitId(100), "前置：合成武将已有战斗实例");
            Assert.AreEqual(1, _unitRegistry.Count);

            BattleInputResult moveDown = _controller.Execute(
                BattleInputCommand.CreateDropUnit(523, battles[1].SlotId.Id, reserves[1].SlotId.Id));
            Assert.IsTrue(moveDown.IsSuccess, "单字下阵应成功");
            Assert.AreEqual(UnitKind.GeneralPart,
                _slotBoard.GetSlotById(battles[0].SlotId.Id).Occupant.Value.Kind,
                "武将解散后左格为独立字牌");
            Assert.AreEqual("张",
                _slotBoard.GetSlotById(battles[0].SlotId.Id).Occupant.Value.GeneralPartText,
                "左格为张字");
            Assert.AreEqual(UnitKind.GeneralPart,
                _slotBoard.GetSlotById(reserves[1].SlotId.Id).Occupant.Value.Kind,
                "点击格飞字独立移动到待上场槽");
            Assert.AreEqual("飞",
                _slotBoard.GetSlotById(reserves[1].SlotId.Id).Occupant.Value.GeneralPartText,
                "待上场槽为飞字");
            Assert.IsNull(_unitRegistry.GetActiveByUnitId(100), "旧武将战斗实例已解除");
            Assert.AreEqual(0, _unitRegistry.Count, "分离字牌不创建任何战斗实例");

            BattleInputResult moveUp = _controller.Execute(
                BattleInputCommand.CreateDropUnit(524, reserves[0].SlotId.Id, battles[0].SlotId.Id));
            Assert.IsTrue(moveUp.IsSuccess, "张字移回应成功");
            Assert.AreEqual(UnitKind.GeneralPart,
                _slotBoard.GetSlotById(battles[0].SlotId.Id).Occupant.Value.Kind,
                "飞字不在相邻格，张字独立在战场，不重新合成");
            Assert.AreEqual(0, _unitRegistry.Count, "独立字牌仍不创建战斗实例");
        }

        [Test]
        [Description("战场完整武将单字拖到另一格（张→飞）：同将内部互换后旧武将实例恰好解除一次，反序字牌无战斗实例。")]
        public void DropUnit_CompleteGeneral_InternalSwap_DeactivatesRuntimeExactlyOnce()
        {
            IReadOnlyList<UnitSlot> reserves = _slotBoard.GetSlots(true, SlotZone.Reserve);
            IReadOnlyList<UnitSlot> battles = _slotBoard.GetSlots(true, SlotZone.Battle);
            FillReserveAll(
                BattleUnit.CreateGeneralPart(100, true, "张"),
                BattleUnit.CreateGeneralPart(200, true, "飞"));

            Assert.IsTrue(_controller.Execute(
                BattleInputCommand.CreateDropUnit(531, reserves[0].SlotId.Id, battles[0].SlotId.Id)).IsSuccess);
            BattleInputResult synthesize = _controller.Execute(
                BattleInputCommand.CreateDropUnit(532, reserves[1].SlotId.Id, battles[1].SlotId.Id));
            Assert.IsTrue(synthesize.IsSuccess, "武将应先在战场合成");
            Assert.IsNotNull(_unitRegistry.GetActiveByUnitId(100), "前置：合成武将已有战斗实例");

            int removedCount = 0;
            _unitRegistry.UnitRemoved += id => removedCount++;
            BattleInputResult result = _controller.Execute(
                BattleInputCommand.CreateDropUnit(533, battles[0].SlotId.Id, battles[1].SlotId.Id));

            Assert.IsTrue(result.IsSuccess, "同将内部互换应成功");
            Assert.AreEqual(UnitKind.GeneralPart,
                _slotBoard.GetSlotById(battles[0].SlotId.Id).Occupant.Value.Kind,
                "源格最终为独立字牌");
            Assert.AreEqual("飞",
                _slotBoard.GetSlotById(battles[0].SlotId.Id).Occupant.Value.GeneralPartText,
                "源格最终为飞字");
            Assert.AreEqual(UnitKind.GeneralPart,
                _slotBoard.GetSlotById(battles[1].SlotId.Id).Occupant.Value.Kind,
                "目标格最终为独立字牌");
            Assert.AreEqual("张",
                _slotBoard.GetSlotById(battles[1].SlotId.Id).Occupant.Value.GeneralPartText,
                "目标格最终为张字");
            Assert.IsNull(_unitRegistry.GetActiveByUnitId(100), "旧张飞战斗实例已解除并停止攻击");
            Assert.AreEqual(0, _unitRegistry.Count, "反序字牌不激活任何战斗实例");
            Assert.AreEqual(1, removedCount, "旧武将战斗实例恰好解除一次");
        }
    }
}
