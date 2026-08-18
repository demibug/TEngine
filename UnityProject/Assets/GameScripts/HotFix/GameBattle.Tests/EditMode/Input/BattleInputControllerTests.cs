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

            _slotBoard = new UnitSlotBoard(_levelService.MaxLevel);
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
                sourceTag: "Test");
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
        [Description("合并失败：不同兵种不合并，不修改状态。")]
        public void DropUnit_Merge_DifferentSoldierType_Rejects()
        {
            IReadOnlyList<UnitSlot> reserves = _slotBoard.GetSlots(true, SlotZone.Reserve);
            UnitSlot source = reserves[0];
            UnitSlot target = reserves[1];

            // 源刀兵，目标弓兵。
            FillReserveAll(
                MakeKnife(100, 1),
                new BattleUnit(200, true, UnitKind.Soldier, SoldierType.Bow, "弓", 1));

            BattleInputResult result = _controller.Execute(
                BattleInputCommand.CreateDropUnit(6, source.SlotId.Id, target.SlotId.Id));

            Assert.IsFalse(result.IsSuccess, "不同兵种应失败");
            Assert.AreEqual(BattleInputRejectReason.TargetMismatch, result.RejectReason);
            Assert.IsFalse(_slotBoard.GetSlotById(source.SlotId.Id).IsEmpty, "源槽未变");
            Assert.AreEqual(1, _slotBoard.GetSlotById(target.SlotId.Id).Occupant.Value.Level, "目标等级未变");
        }

        [Test]
        [Description("合并失败：不同等级不合并，不修改状态。")]
        public void DropUnit_Merge_DifferentLevel_Rejects()
        {
            IReadOnlyList<UnitSlot> reserves = _slotBoard.GetSlots(true, SlotZone.Reserve);
            UnitSlot source = reserves[0];
            UnitSlot target = reserves[1];

            FillReserveAll(MakeKnife(100, 1), MakeKnife(200, 2));

            BattleInputResult result = _controller.Execute(
                BattleInputCommand.CreateDropUnit(7, source.SlotId.Id, target.SlotId.Id));

            Assert.IsFalse(result.IsSuccess, "不同等级应失败");
            Assert.AreEqual(BattleInputRejectReason.TargetMismatch, result.RejectReason);
            Assert.IsFalse(_slotBoard.GetSlotById(source.SlotId.Id).IsEmpty, "源槽未变");
        }

        [Test]
        [Description("合并失败：目标已满级不合并，不修改状态。")]
        public void DropUnit_Merge_MaxLevel_Rejects()
        {
            IReadOnlyList<UnitSlot> reserves = _slotBoard.GetSlots(true, SlotZone.Reserve);
            UnitSlot source = reserves[0];
            UnitSlot target = reserves[1];

            // 目标 3 级 = 最大等级，不可再合并。
            FillReserveAll(MakeKnife(100, 3), MakeKnife(200, 3));

            BattleInputResult result = _controller.Execute(
                BattleInputCommand.CreateDropUnit(8, source.SlotId.Id, target.SlotId.Id));

            Assert.IsFalse(result.IsSuccess, "满级目标应失败");
            Assert.AreEqual(BattleInputRejectReason.MaxLevelReached, result.RejectReason);
            Assert.IsFalse(_slotBoard.GetSlotById(source.SlotId.Id).IsEmpty, "源槽未变");
            Assert.AreEqual(3, _slotBoard.GetSlotById(target.SlotId.Id).Occupant.Value.Level, "目标等级未变");
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
    }
}
