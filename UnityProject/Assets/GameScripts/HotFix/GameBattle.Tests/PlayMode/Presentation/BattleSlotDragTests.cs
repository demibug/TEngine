using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace GameBattle.Tests.PlayMode.Presentation
{
    // ============================================================================
    // BattleSlotDragTests（最终方案"表现层拖拽"）
    // ----------------------------------------------------------------------------
    // 验证内容：
    //   1. BattleInputAdapter 把拖放提交转换为 DropUnit 命令（源槽/目标槽 SlotId）。
    //   2. BattlePresenter.TryResolvePlayerBattleSlot 把屏幕坐标解析为玩家战场槽。
    //   3. 换槽命令执行结果：空目标槽换槽成功，源槽变空。
    //   4. 失败拖放弹回：目标不匹配时不修改逻辑状态。
    //
    // 说明：
    //   Unity PlayMode 测试需要 Scene 环境；本测试为纯逻辑表现层验证，不依赖 Scene，
    //   以 EditMode 兼容方式运行（asmdef includePlatforms=Editor 同样覆盖 PlayMode 测试）。
    // ============================================================================

    /// <summary>
    /// 表现层拖拽输入测试（最终方案：DropUnit 换槽/合并 + 屏幕坐标 → 战场槽解析）。
    /// </summary>
    [TestFixture]
    internal class BattleSlotDragTests
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
        private BattleInputAdapter _adapter;

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
                new SeededRandomSource(seed: 5), _slotBoard, RecruitDefinitions.ReserveSlotCount);
            _slotBoard.ReplaceReserve(true, _recruitManager.GenerateBatch(true));

            _battleState = new BattleState();
            _economy = new BattleEconomy(_battleState, refreshCostIncrement: 2);

            _controller = new BattleInputController(
                _slotBoard, _recruitManager, _levelService, _economy, _unitRegistry, _configSnapshot);
            _controller.StartGame();

            _adapter = new BattleInputAdapter(_controller, new IdentityCoordinateConverter());
        }

        [TearDown]
        public void TearDown()
        {
            _unitRegistry?.GameOver();
            _slotBoard?.GameOver();
        }

        /// <summary>恒等坐标转换器：把屏幕坐标直接解释为 GridPosition（测试用）。</summary>
        private sealed class IdentityCoordinateConverter : ICoordinateConverter
        {
            public bool TryConvertToGrid(float screenX, float screenY, out GridPosition position)
            {
                position = new GridPosition((int)screenX, (int)screenY);
                return true;
            }
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
        // 拖放提交测试
        // ====================================================================

        [Test]
        [Description("拖放提交：Adapter 把源槽/目标槽 SlotId 转换为 DropUnit 命令并成功执行。")]
        public void DragDrop_SubmitDropUnit_EmptyTarget_MovesUnit()
        {
            IReadOnlyList<UnitSlot> reserves = _slotBoard.GetSlots(true, SlotZone.Reserve);
            int sourceSlotId = reserves[0].SlotId.Id;
            int targetSlotId = reserves[1].SlotId.Id;
            Assert.IsFalse(_slotBoard.GetSlotById(sourceSlotId).IsEmpty, "源槽有单位");
            Assert.IsTrue(_slotBoard.GetSlotById(targetSlotId).IsEmpty, "目标槽空");

            BattleInputResult result = _adapter.HandleDropUnit(sourceSlotId, targetSlotId);

            Assert.IsTrue(result.IsSuccess, "拖放换槽应成功");
            Assert.IsTrue(_slotBoard.GetSlotById(sourceSlotId).IsEmpty, "源槽变空");
            Assert.IsFalse(_slotBoard.GetSlotById(targetSlotId).IsEmpty, "目标槽承载单位");
        }

        [Test]
        [Description("拖放失败弹回：目标不匹配时不修改逻辑状态。")]
        public void DragDrop_TargetMismatch_NoStateChange()
        {
            IReadOnlyList<UnitSlot> reserves = _slotBoard.GetSlots(true, SlotZone.Reserve);
            UnitSlot source = reserves[0];
            UnitSlot target = reserves[1];

            // 源 1 级刀兵，目标 1 级弓兵（不同兵种不可合并）。
            var knife = new BattleUnit(
                7000, true, UnitKind.Soldier, SoldierType.Knife, "刀", 1);
            var bow = new BattleUnit(
                7001, true, UnitKind.Soldier, SoldierType.Bow, "弓", 1);
            FillReserveAll(knife, bow);

            int sourceUnitId = _slotBoard.GetSlot(source.SlotId).OccupantUnitId;
            int targetUnitId = _slotBoard.GetSlot(target.SlotId).OccupantUnitId;

            BattleInputResult result = _adapter.HandleDropUnit(source.SlotId.Id, target.SlotId.Id);

            Assert.IsFalse(result.IsSuccess, "不同兵种应失败");
            Assert.AreEqual(BattleInputRejectReason.TargetMismatch, result.RejectReason);
            Assert.AreEqual(sourceUnitId, _slotBoard.GetSlot(source.SlotId).OccupantUnitId, "源槽未变");
            Assert.AreEqual(targetUnitId, _slotBoard.GetSlot(target.SlotId).OccupantUnitId, "目标槽未变");
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
                        unitId: 9000 + i, side: true, kind: UnitKind.Soldier,
                        soldierType: SoldierType.Knife, soldierText: "刀", level: 1);
            }

            _slotBoard.ReplaceReserve(true, batch);
        }

        [Test]
        [Description("屏幕坐标解析：Identity 转换器把屏幕坐标映射为战场槽。")]
        public void DragDrop_ScreenCoordinate_ResolvesToBattleSlot()
        {
            // 玩家战场槽在 (0,0) 与 (1,0)。恒等转换器把 (0,0) 映射为该槽位。
            IReadOnlyList<UnitSlot> battles = _slotBoard.GetSlots(true, SlotZone.Battle);
            int expectedSlotId = -1;
            foreach (UnitSlot slot in battles)
            {
                if (slot.SlotId.GridPosition == new GridPosition(0, 0))
                {
                    expectedSlotId = slot.SlotId.Id;
                    break;
                }
            }

            // 通过 Adapter 转换器解析 (0,0)。
            bool converted = _adapter.TryConvertToGrid(0f, 0f, out GridPosition grid);
            Assert.IsTrue(converted, "屏幕坐标应转换成功");
            Assert.AreEqual(new GridPosition(0, 0), grid, "转换结果应为 (0,0)");

            // 槽位面板按格子反查。
            bool found = _slotBoard.TryFindBattleSlot(true, grid, out UnitSlotId foundSlotId);
            Assert.IsTrue(found, "应找到玩家战场槽");
            Assert.AreEqual(expectedSlotId, foundSlotId.Id, "解析出的战场槽 ID 匹配");
        }

        // ====================================================================
        // BattleDragController（四向拖拽状态机）
        // ====================================================================

        [Test]
        [Description("拖拽控制器：BeginDrag→EndDrag 解析目标并提交 DropUnit（待上场→待上场）。")]
        public void DragController_BeginEnd_ReserveToReserve()
        {
            // 用一个恒等目标解析委托：任何坐标都解析为 reserves[2] 槽位。
            IReadOnlyList<UnitSlot> reserves = _slotBoard.GetSlots(true, SlotZone.Reserve);
            int sourceSlotId = reserves[0].SlotId.Id;
            int targetSlotId = reserves[2].SlotId.Id;
            Assert.IsFalse(_slotBoard.GetSlotById(sourceSlotId).IsEmpty, "源槽有单位");
            Assert.IsTrue(_slotBoard.GetSlotById(targetSlotId).IsEmpty, "目标槽空");

            var drag = new BattleDragController(
                dropUnit: _adapter.HandleDropUnit,
                resolveTargetSlot: (x, y) => targetSlotId);

            drag.BeginDrag(sourceSlotId, touchId: 1);
            Assert.IsTrue(drag.IsDragging, "拖动中");

            BattleInputResult? result = drag.EndDrag(0f, 0f);

            Assert.IsTrue(result.HasValue, "应提交命令");
            Assert.IsTrue(result.Value.IsSuccess, "拖放应成功");
            Assert.IsTrue(_slotBoard.GetSlotById(sourceSlotId).IsEmpty, "源槽变空");
            Assert.IsFalse(_slotBoard.GetSlotById(targetSlotId).IsEmpty, "目标槽承载单位");
            Assert.IsFalse(drag.IsDragging, "松手后不再拖拽");
        }

        [Test]
        [Description("拖拽控制器：目标未命中返回 null（弹回，不提交命令）。")]
        public void DragController_UnresolvedTarget_ReturnsNull()
        {
            IReadOnlyList<UnitSlot> reserves = _slotBoard.GetSlots(true, SlotZone.Reserve);
            int sourceSlotId = reserves[0].SlotId.Id;
            int unitIdBefore = _slotBoard.GetSlotById(sourceSlotId).OccupantUnitId;

            var drag = new BattleDragController(
                dropUnit: _adapter.HandleDropUnit,
                resolveTargetSlot: (x, y) => -1);

            drag.BeginDrag(sourceSlotId, touchId: 1);
            BattleInputResult? result = drag.EndDrag(0f, 0f);

            Assert.IsNull(result, "未命中目标不提交命令");
            Assert.AreEqual(unitIdBefore, _slotBoard.GetSlotById(sourceSlotId).OccupantUnitId,
                "源槽单位未变（弹回）");
            Assert.IsFalse(drag.IsDragging, "松手后不再拖拽");
        }

        [Test]
        [Description("拖拽控制器：未开始拖拽时 EndDrag 返回 null。")]
        public void DragController_NoDrag_EndReturnsNull()
        {
            var drag = new BattleDragController(
                dropUnit: _adapter.HandleDropUnit,
                resolveTargetSlot: (x, y) => 1);

            BattleInputResult? result = drag.EndDrag(0f, 0f);

            Assert.IsNull(result, "未拖拽时不提交");
        }

        [Test]
        [Description("拖拽控制器：Cancel 取消拖拽，不提交命令。")]
        public void DragController_Cancel_NoCommand()
        {
            IReadOnlyList<UnitSlot> reserves = _slotBoard.GetSlots(true, SlotZone.Reserve);
            int sourceSlotId = reserves[0].SlotId.Id;
            int unitIdBefore = _slotBoard.GetSlotById(sourceSlotId).OccupantUnitId;

            var drag = new BattleDragController(
                dropUnit: _adapter.HandleDropUnit,
                resolveTargetSlot: (x, y) => reserves[2].SlotId.Id);

            drag.BeginDrag(sourceSlotId, touchId: 1);
            drag.Cancel();
            Assert.IsFalse(drag.IsDragging, "取消后不再拖拽");

            BattleInputResult? result = drag.EndDrag(0f, 0f);
            Assert.IsNull(result, "取消后 EndDrag 不提交");
            Assert.AreEqual(unitIdBefore, _slotBoard.GetSlotById(sourceSlotId).OccupantUnitId,
                "源槽单位未变");
        }
    }
}
