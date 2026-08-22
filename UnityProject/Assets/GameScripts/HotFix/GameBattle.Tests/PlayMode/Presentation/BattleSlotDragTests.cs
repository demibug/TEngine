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

            // ReplaceReserve 严格要求填满，开局无空槽；先腾出目标槽。
            ClearReserveSlot(reserves[1].SlotId);
            Assert.IsTrue(_slotBoard.GetSlotById(targetSlotId).IsEmpty, "目标槽空");

            BattleInputResult result = _adapter.HandleDropUnit(sourceSlotId, targetSlotId);

            Assert.IsTrue(result.IsSuccess, "拖放换槽应成功");
            Assert.IsTrue(_slotBoard.GetSlotById(sourceSlotId).IsEmpty, "源槽变空");
            Assert.IsFalse(_slotBoard.GetSlotById(targetSlotId).IsEmpty, "目标槽承载单位");
        }

        [Test]
        [Description("拖放目标不匹配：走互换而非拒绝，两槽原子交换。")]
        public void DragDrop_TargetMismatch_Swaps()
        {
            IReadOnlyList<UnitSlot> reserves = _slotBoard.GetSlots(true, SlotZone.Reserve);
            UnitSlot source = reserves[0];
            UnitSlot target = reserves[1];

            // 源 1 级刀兵，目标 1 级弓兵（不同兵种不可合并 → 互换）。
            var knife = new BattleUnit(
                7000, true, UnitKind.Soldier, SoldierType.Knife, "刀", 1);
            var bow = new BattleUnit(
                7001, true, UnitKind.Soldier, SoldierType.Bow, "弓", 1);
            FillReserveAll(knife, bow);

            BattleInputResult result = _adapter.HandleDropUnit(source.SlotId.Id, target.SlotId.Id);

            Assert.IsTrue(result.IsSuccess, "不同兵种应互换而非拒绝");
            Assert.AreEqual(bow.UnitId, _slotBoard.GetSlot(source.SlotId).OccupantUnitId, "源槽换入目标单位");
            Assert.AreEqual(knife.UnitId, _slotBoard.GetSlot(target.SlotId).OccupantUnitId, "目标槽换入源单位");
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

        /// <summary>
        /// 把指定待上场槽的单位换到第一个空战场槽，使该待上场槽变为空槽。
        /// ReplaceReserve 严格要求填满，开局无空槽，需显式腾出测试用空槽。
        /// </summary>
        private void ClearReserveSlot(UnitSlotId reserveSlotId)
        {
            IReadOnlyList<UnitSlot> battles = _slotBoard.GetSlots(true, SlotZone.Battle);
            foreach (UnitSlot battle in battles)
            {
                if (battle.IsEmpty)
                {
                    _adapter.HandleDropUnit(reserveSlotId.Id, battle.SlotId.Id);
                    return;
                }
            }

            // 无空战场槽：把最后一个待上场槽换到第一个战场槽，腾出战场槽再换入目标槽单位。
            IReadOnlyList<UnitSlot> reserves = _slotBoard.GetSlots(true, SlotZone.Reserve);
            if (reserves.Count >= 2)
            {
                int lastReserveId = reserves[reserves.Count - 1].SlotId.Id;
                _adapter.HandleDropUnit(lastReserveId, battles[0].SlotId.Id);
                _adapter.HandleDropUnit(reserveSlotId.Id, battles[0].SlotId.Id);
            }
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

            // ReplaceReserve 严格要求填满，开局无空槽；先腾出目标槽。
            ClearReserveSlot(reserves[2].SlotId);
            Assert.IsTrue(_slotBoard.GetSlotById(targetSlotId).IsEmpty, "目标槽空");

            var drag = new BattleDragController(
                dropUnit: _adapter.HandleDropUnit,
                resolveTargetSlot: (x, y) => targetSlotId);

            drag.BeginDrag(sourceSlotId, touchId: 1);
            Assert.IsTrue(drag.IsDragging, "拖动中");

            BattleInputResult? result = drag.EndDrag(0f, 0f, touchId: 1);

            Assert.IsTrue(result.HasValue, "应提交命令");
            Assert.IsTrue(result.Value.IsSuccess, "拖放应成功");
            Assert.IsTrue(_slotBoard.GetSlotById(sourceSlotId).IsEmpty, "源槽变空");
            Assert.IsFalse(_slotBoard.GetSlotById(targetSlotId).IsEmpty, "目标槽承载单位");
            Assert.IsFalse(drag.IsDragging, "松手后不再拖拽");
        }

        [Test]
        [Description("拖拽控制器：错误 touchId 不能结束拖拽（多指保护）。")]
        public void DragController_WrongTouchId_CannotEnd()
        {
            IReadOnlyList<UnitSlot> reserves = _slotBoard.GetSlots(true, SlotZone.Reserve);
            int sourceSlotId = reserves[0].SlotId.Id;
            int unitIdBefore = _slotBoard.GetSlotById(sourceSlotId).OccupantUnitId;

            var drag = new BattleDragController(
                dropUnit: _adapter.HandleDropUnit,
                resolveTargetSlot: (x, y) => reserves[2].SlotId.Id);

            drag.BeginDrag(sourceSlotId, touchId: 1);

            // 另一个手指（touchId=2）不能结束拖拽。
            BattleInputResult? wrongTouchResult = drag.EndDrag(0f, 0f, touchId: 2);
            Assert.IsNull(wrongTouchResult, "错误 touchId 不提交");
            Assert.IsTrue(drag.IsDragging, "拖拽仍在进行（源槽未变）");
            Assert.AreEqual(unitIdBefore, _slotBoard.GetSlotById(sourceSlotId).OccupantUnitId,
                "源槽单位未变");

            // 正确的 touchId 仍能结束并提交。
            BattleInputResult? rightTouchResult = drag.EndDrag(0f, 0f, touchId: 1);
            Assert.IsTrue(rightTouchResult.HasValue, "正确 touchId 应提交");
            Assert.IsTrue(rightTouchResult.Value.IsSuccess, "拖放应成功");
            Assert.IsTrue(_slotBoard.GetSlotById(sourceSlotId).IsEmpty, "源槽变空");
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
            BattleInputResult? result = drag.EndDrag(0f, 0f, touchId: 1);

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

            BattleInputResult? result = drag.EndDrag(0f, 0f, touchId: 1);

            Assert.IsNull(result, "未拖拽时不提交");
        }

        [Test]
        [Description("拖拽控制器：Cancel(touchId) 校验所有权；错误 touchId 不取消。")]
        public void DragController_Cancel_WrongTouchId_KeepsDragging()
        {
            IReadOnlyList<UnitSlot> reserves = _slotBoard.GetSlots(true, SlotZone.Reserve);
            int sourceSlotId = reserves[0].SlotId.Id;

            var drag = new BattleDragController(
                dropUnit: _adapter.HandleDropUnit,
                resolveTargetSlot: (x, y) => reserves[2].SlotId.Id);

            drag.BeginDrag(sourceSlotId, touchId: 1);
            drag.Cancel(touchId: 2);
            Assert.IsTrue(drag.IsDragging, "错误 touchId 不取消拖拽");

            drag.Cancel(touchId: 1);
            Assert.IsFalse(drag.IsDragging, "正确 touchId 取消拖拽");
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

            BattleInputResult? result = drag.EndDrag(0f, 0f, touchId: 1);
            Assert.IsNull(result, "取消后 EndDrag 不提交");
            Assert.AreEqual(unitIdBefore, _slotBoard.GetSlotById(sourceSlotId).OccupantUnitId,
                "源槽单位未变");
        }

        [Test]
        [Description("拖拽控制器：四向空槽拖动（Reserve→Reserve / Reserve→Battle / Battle→Battle / Battle→Reserve）。")]
        public void DragController_FourWayEmptyTargets()
        {
            IReadOnlyList<UnitSlot> reserves = _slotBoard.GetSlots(true, SlotZone.Reserve);
            IReadOnlyList<UnitSlot> battles = _slotBoard.GetSlots(true, SlotZone.Battle);

            int r0 = reserves[0].SlotId.Id;
            int r1 = reserves[1].SlotId.Id;
            int r2 = reserves[2].SlotId.Id;
            int b0 = battles[0].SlotId.Id;
            int b1 = battles[1].SlotId.Id;

            // 开局：全部待上场槽满，战场槽空。
            Assert.IsFalse(_slotBoard.GetSlotById(r0).IsEmpty, "r0 有单位");

            // 先腾出 r1（把 r1 单位换到 b0），使 r1 变空。
            ClearReserveSlot(reserves[1].SlotId);
            Assert.IsTrue(_slotBoard.GetSlotById(r1).IsEmpty, "r1 空（腾出）");

            // R→R：待上场[0] → 待上场[1]（r1 空）。
            var dragRR = new BattleDragController(_adapter.HandleDropUnit, (x, y) => r1);
            dragRR.BeginDrag(r0, touchId: 1);
            Assert.IsTrue(dragRR.EndDrag(0f, 0f, 1).Value.IsSuccess, "R→R 应成功");
            Assert.IsTrue(_slotBoard.GetSlotById(r0).IsEmpty, "r0 变空");
            Assert.IsFalse(_slotBoard.GetSlotById(r1).IsEmpty, "r1 有单位");

            // R→B：待上场[0]（现空，先把 r1 换回 r0 恢复满）→ 战场[1]（b1 空，b0 已被占用）。
            var dragBack = new BattleDragController(_adapter.HandleDropUnit, (x, y) => r0);
            dragBack.BeginDrag(r1, touchId: 1);
            Assert.IsTrue(dragBack.EndDrag(0f, 0f, 1).Value.IsSuccess, "r1→r0 归位");
            Assert.IsTrue(_slotBoard.GetSlotById(b1).IsEmpty, "b1 空");
            var dragRB = new BattleDragController(_adapter.HandleDropUnit, (x, y) => b1);
            dragRB.BeginDrag(r0, touchId: 1);
            Assert.IsTrue(dragRB.EndDrag(0f, 0f, 1).Value.IsSuccess, "R→B 应成功");
            Assert.IsTrue(_slotBoard.GetSlotById(r0).IsEmpty, "r0 变空");
            Assert.IsFalse(_slotBoard.GetSlotById(b1).IsEmpty, "b1 有单位");

            // B→B：战场[1] → 战场[0]（b0 已有腾出时放入的单位，先挪走）。
            // 把 b1 单位换到 b0（b0 当前被 r1 腾出时占用，先把它换到 r2 腾出 b0）。
            var dragFreeB0 = new BattleDragController(_adapter.HandleDropUnit, (x, y) => r2);
            dragFreeB0.BeginDrag(b0, touchId: 1);
            Assert.IsTrue(dragFreeB0.EndDrag(0f, 0f, 1).Value.IsSuccess, "b0→r2 腾出 b0");
            Assert.IsTrue(_slotBoard.GetSlotById(b0).IsEmpty, "b0 空（腾出）");

            var dragBB = new BattleDragController(_adapter.HandleDropUnit, (x, y) => b0);
            dragBB.BeginDrag(b1, touchId: 1);
            Assert.IsTrue(dragBB.EndDrag(0f, 0f, 1).Value.IsSuccess, "B→B 应成功");
            Assert.IsTrue(_slotBoard.GetSlotById(b1).IsEmpty, "b1 变空");
            Assert.IsFalse(_slotBoard.GetSlotById(b0).IsEmpty, "b0 有单位");

            // B→R：战场[0] → 待上场[0]（r0 空）。
            var dragBR = new BattleDragController(_adapter.HandleDropUnit, (x, y) => r0);
            dragBR.BeginDrag(b0, touchId: 1);
            Assert.IsTrue(dragBR.EndDrag(0f, 0f, 1).Value.IsSuccess, "B→R 应成功");
            Assert.IsTrue(_slotBoard.GetSlotById(b0).IsEmpty, "b0 变空");
            Assert.IsFalse(_slotBoard.GetSlotById(r0).IsEmpty, "r0 有单位");
        }

        [Test]
        [Description("拖拽控制器：四向占用目标互换（R→R / R→B / B→R / B→B 的 Swap 方向）。")]
        public void DragController_FourWaySwapTargets()
        {
            IReadOnlyList<UnitSlot> reserves = _slotBoard.GetSlots(true, SlotZone.Reserve);
            IReadOnlyList<UnitSlot> battles = _slotBoard.GetSlots(true, SlotZone.Battle);
            int r0 = reserves[0].SlotId.Id;
            int r1 = reserves[1].SlotId.Id;
            int b0 = battles[0].SlotId.Id;
            int b1 = battles[1].SlotId.Id;

            // 前置：r0=刀兵，r1=弓兵（不同兵种，任何占用目标互换都走 Swap 而非 Merge）。
            FillReserveAll(
                new BattleUnit(100, true, UnitKind.Soldier, SoldierType.Knife, "刀", 1),
                new BattleUnit(200, true, UnitKind.Soldier, SoldierType.Bow, "弓", 1));

            // R→R 互换：r0（刀）→ r1（弓）。
            var dragRR = new BattleDragController(_adapter.HandleDropUnit, (x, y) => r1);
            dragRR.BeginDrag(r0, touchId: 1);
            Assert.IsTrue(dragRR.EndDrag(0f, 0f, 1).Value.IsSuccess, "R→R 互换应成功");
            Assert.AreEqual(200, _slotBoard.GetSlotById(r0).OccupantUnitId, "r0 换入弓兵");
            Assert.AreEqual(100, _slotBoard.GetSlotById(r1).OccupantUnitId, "r1 换入刀兵");

            // 前置：r1（刀）上场到 b0（空目标 Move），使 b0 占用刀兵。
            _adapter.HandleDropUnit(r1, b0);
            Assert.IsTrue(_slotBoard.GetSlotById(b0).IsEmpty == false, "前置：b0 占用刀兵");

            // R→B 互换：r0（弓）→ b0（刀）。
            var dragRB = new BattleDragController(_adapter.HandleDropUnit, (x, y) => b0);
            dragRB.BeginDrag(r0, touchId: 1);
            Assert.IsTrue(dragRB.EndDrag(0f, 0f, 1).Value.IsSuccess, "R→B 互换应成功");
            Assert.AreEqual(100, _slotBoard.GetSlotById(r0).OccupantUnitId, "r0 换入刀兵");
            Assert.AreEqual(200, _slotBoard.GetSlotById(b0).OccupantUnitId, "b0 换入弓兵");

            // B→R 互换：b0（弓）→ r0（刀）。
            var dragBR = new BattleDragController(_adapter.HandleDropUnit, (x, y) => r0);
            dragBR.BeginDrag(b0, touchId: 1);
            Assert.IsTrue(dragBR.EndDrag(0f, 0f, 1).Value.IsSuccess, "B→R 互换应成功");
            Assert.AreEqual(200, _slotBoard.GetSlotById(r0).OccupantUnitId, "r0 换入弓兵");
            Assert.AreEqual(100, _slotBoard.GetSlotById(b0).OccupantUnitId, "b0 换入刀兵");

            // 前置：r0（弓）上场到 b1（空目标 Move），使 b1 占用弓兵。
            _adapter.HandleDropUnit(r0, b1);
            Assert.IsTrue(_slotBoard.GetSlotById(b1).IsEmpty == false, "前置：b1 占用弓兵");

            // B→B 互换：b0（刀）→ b1（弓）。
            var dragBB = new BattleDragController(_adapter.HandleDropUnit, (x, y) => b1);
            dragBB.BeginDrag(b0, touchId: 1);
            Assert.IsTrue(dragBB.EndDrag(0f, 0f, 1).Value.IsSuccess, "B→B 互换应成功");
            Assert.AreEqual(200, _slotBoard.GetSlotById(b0).OccupantUnitId, "b0 换入弓兵");
            Assert.AreEqual(100, _slotBoard.GetSlotById(b1).OccupantUnitId, "b1 换入刀兵");
        }

        // ====================================================================
        // 真实事件顺序回归：Reserve 卡拖动 → Stage TouchEnd（应被守卫忽略）
        // → Card DragEnd（独占结算，只提交一次 DropUnit）
        // ====================================================================

        [Test]
        [Description("回归：Reserve 卡拖动时 Stage TouchEnd 被守卫忽略，Card DragEnd 独占结算只提交一次。")]
        public void DragController_ReserveDrag_StageEndIgnored_CardEndSettlesOnce()
        {
            IReadOnlyList<UnitSlot> reserves = _slotBoard.GetSlots(true, SlotZone.Reserve);
            IReadOnlyList<UnitSlot> battles = _slotBoard.GetSlots(true, SlotZone.Battle);
            int sourceSlotId = reserves[0].SlotId.Id;
            int targetSlotId = battles[0].SlotId.Id;
            Assert.IsFalse(_slotBoard.GetSlotById(sourceSlotId).IsEmpty, "源槽有单位");
            Assert.IsTrue(_slotBoard.GetSlotById(targetSlotId).IsEmpty, "战场目标槽空");

            // 记录提交次数，验证只提交一次。
            int dropCount = 0;
            var drag = new BattleDragController(
                dropUnit: (s, t) =>
                {
                    dropCount++;
                    return _adapter.HandleDropUnit(s, t);
                },
                resolveTargetSlot: (x, y) => targetSlotId);

            // 事件 1：Reserve 卡开始拖动。
            drag.BeginDrag(sourceSlotId, touchId: 1);
            Assert.IsTrue(drag.IsDragging, "拖动中");

            // 事件 2：Stage.onTouchEnd 捕获先执行。
            // 与 BattleHudPanel.OnStageTouchEnd 相同的守卫：查源槽 Zone，
            // 非 Battle 来源直接返回（不结算、不销毁、不提交）。
            UnitSlot source = _slotBoard.GetSlotById(drag.SourceSlotId);
            if (source.SlotId.Zone == SlotZone.Battle)
            {
                drag.EndDrag(0f, 0f, 1);
            }

            // 守卫拦截后：拖动状态保留，未提交。
            Assert.IsTrue(drag.IsDragging, "Stage TouchEnd 守卫不应消费 Reserve 拖动");
            Assert.AreEqual(0, dropCount, "Stage TouchEnd 不应提交 DropUnit");
            Assert.IsFalse(_slotBoard.GetSlotById(sourceSlotId).IsEmpty, "源槽未变");

            // 事件 3：Card.onDragEnd 独占结算。
            BattleInputResult? result = drag.EndDrag(0f, 0f, touchId: 1);

            Assert.IsTrue(result.HasValue, "Card DragEnd 应提交");
            Assert.AreEqual(1, dropCount, "只提交一次 DropUnit");
            Assert.IsFalse(drag.IsDragging, "结算后不再拖拽");
            Assert.IsTrue(_slotBoard.GetSlotById(sourceSlotId).IsEmpty, "Reserve 源槽为空");
            Assert.IsFalse(_slotBoard.GetSlotById(targetSlotId).IsEmpty, "Battle 目标槽有单位");
        }

        [Test]
        [Description("回归：完整武将单字拖动（张→飞）经拖拽控制器只移动点击格，整将解散为反序独立字牌。")]
        public void DragController_CompleteGeneral_SingleCellDrag_DisassemblesToReversedParts()
        {
            IReadOnlyList<UnitSlot> reserves = _slotBoard.GetSlots(true, SlotZone.Reserve);
            var zhangFei = new GeneralConfigSnapshot(
                1, "张飞", "张", new[] { "张", "飞" }, GeneralCombatArchetype.Pike,
                2.5f, 15, 1f, "近战枪击", "nearest", "SpearSoldier", "default", "", 0, 1);
            FillReserveAll(
                BattleUnit.CreateGeneralCell(100, true, zhangFei, 0),
                BattleUnit.CreateGeneralCell(100, true, zhangFei, 1));

            var drag = new BattleDragController(
                _adapter.HandleDropUnit,
                resolveTargetSlot: (x, y) => reserves[1].SlotId.Id);
            drag.BeginDrag(reserves[0].SlotId.Id, touchId: 1);
            Assert.IsTrue(drag.IsDragging, "拖动中");

            BattleInputResult? result = drag.EndDrag(0f, 0f, touchId: 1);

            Assert.IsTrue(result.HasValue, "应提交命令");
            Assert.IsTrue(result.Value.IsSuccess, "同将内部单字互换应成功");
            Assert.AreEqual(UnitKind.GeneralPart,
                _slotBoard.GetSlotById(reserves[0].SlotId.Id).Occupant.Value.Kind,
                "源格最终为独立字牌");
            Assert.AreEqual("飞",
                _slotBoard.GetSlotById(reserves[0].SlotId.Id).Occupant.Value.GeneralPartText,
                "源格最终为飞字");
            Assert.AreEqual(UnitKind.GeneralPart,
                _slotBoard.GetSlotById(reserves[1].SlotId.Id).Occupant.Value.Kind,
                "目标格最终为独立字牌");
            Assert.AreEqual("张",
                _slotBoard.GetSlotById(reserves[1].SlotId.Id).Occupant.Value.GeneralPartText,
                "目标格最终为张字");
            Assert.IsFalse(drag.IsDragging, "松手后不再拖拽");
        }
    }
}
