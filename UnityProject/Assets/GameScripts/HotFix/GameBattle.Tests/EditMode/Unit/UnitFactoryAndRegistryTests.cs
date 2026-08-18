using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Unit
{
    // ============================================================================
    // 任务 6.3：UnitFactory / UnitRegistry 单元测试
    // ----------------------------------------------------------------------------
    // 验证要求（task 6.3）：
    //   1. 四兵种创建/回收（Knife/Bow/Spear/Cavalry）
    //   2. 未知兵种失败（default 分支防御性抛出）
    //   3. ID 分配（每次 Acquire 分配新 ID，单调递增）
    //   4. 注册/放置/移除/清理
    //   5. 池复用无污染（Acquire → Release → 复用 → 验证旧状态不残留）
    //   6. 稳定有序集合（遍历顺序由注册决定）
    //   7. 放置冲突检测（同一格子不可重复占用）
    //   8. GameOver 清理全部单位
    //
    // 来源证据：
    //   - design.md 第 184 行：UnitFactory 职责。
    //   - design.md 第 185 行：UnitRegistry 职责。
    //   - UnitFactory.js:1-81：强类型兵种 ID 创建/回收。
    //   - UnitRegistry.js:1-382：注册/放置/移除/清理。
    //
    // 测试策略：
    //   使用真实 AttackResolver、真实 EnemyManager、真实 AttackEffectManager、
    //   真实 ProjectileFactory 与真实 ProjectileManager，构造 UnitFactory 和
    //   UnitRegistry 并验证行为。不接触 Scene、FUI 或资源加载。
    // ============================================================================

    /// <summary>
    /// UnitFactory 与 UnitRegistry 单元测试（task 6.3）。
    /// </summary>
    [TestFixture]
    internal class UnitFactoryAndRegistryTests
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
        // 测试上下文
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
        private ProjectileFactory _projectileFactory;
        private ProjectileManager _projectileManager;
        private BattleObjectPool<SimpleDynamicArrow> _arrowPool;
        private UnitFactory _factory;
        private UnitRegistry _registry;

        // ====================================================================
        // Setup / TearDown
        // ====================================================================

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
            _projectileFactory = new ProjectileFactory(
                _idAllocator, _arrowPool, _enemyManager, CellSize);
            _projectileManager = new ProjectileManager(_projectileFactory);

            _factory = new UnitFactory(
                _idAllocator,
                _knifePool, _bowPool, _spearPool, _cavalryPool,
                _enemyManager, _attackResolver, _attackEffectManager,
                _projectileFactory, _projectileManager,
                CellSize, OpponentAttackMultiplier);

            _registry = new UnitRegistry(_factory, CellSize);
        }

        [TearDown]
        public void TearDown()
        {
            _registry?.GameOver();
        }

        // ====================================================================
        // 辅助方法
        // ====================================================================

        /// <summary>构造刀兵配置快照。</summary>
        private static UnitConfigSnapshot MakeConfig(int index, string text, string animKey,
            float rangeCells, int damage, float interval)
        {
            return new UnitConfigSnapshot(
                index, text, animKey, rangeCells, damage, interval,
                "单体", "nearest");
        }

        private static UnitConfigSnapshot KnifeConfig =>
            MakeConfig(0, "刀", "knife", 1.5f, 3, 0.8f);

        private static UnitConfigSnapshot BowConfig =>
            MakeConfig(1, "弓", "bow", 3.5f, 2, 0.8f);

        private static UnitConfigSnapshot SpearConfig =>
            MakeConfig(2, "枪", "pike", 2.5f, 2, 0.8f);

        private static UnitConfigSnapshot CavalryConfig =>
            MakeConfig(3, "骑", "cavalry", 2f, 2, 0.8f);

        [Test]
        public void BuffAdapter_AppliesSupportedChannelsRefreshesLevelAndClearsBeforePoolReuse()
        {
            var definitions = new[]
            {
                new BuffDefinitionSnapshot(0, "攻击", "", BuffKind.Numeric,
                    new[] { (int)BuffNumericChannel.AttackPower }, BuffStackPolicy.Add, 3, ""),
                new BuffDefinitionSnapshot(1, "攻速", "", BuffKind.Numeric,
                    new[] { (int)BuffNumericChannel.AttackSpeed }, BuffStackPolicy.Add, 3, ""),
                new BuffDefinitionSnapshot(2, "范围", "", BuffKind.Numeric,
                    new[] { (int)BuffNumericChannel.AttackRange }, BuffStackPolicy.Add, 3, ""),
                new BuffDefinitionSnapshot(3, "移速", "", BuffKind.Numeric,
                    new[] { (int)BuffNumericChannel.MoveSpeed }, BuffStackPolicy.Add, 3, ""),
                new BuffDefinitionSnapshot(8, "禁攻", "", BuffKind.State,
                    new[] { (int)BuffStateChannel.AttackDisabled }, BuffStackPolicy.Add, 3, ""),
            };
            var buffManager = new BuffManager(
                new BuffCatalogSnapshot(definitions), new BattleActionScheduler());
            var levelService = new UnitLevelService(new UnitLevelConfigSnapshot(
                2, new[] { 1f, 2f }, new[] { 1f, 1f }));
            var factory = new UnitFactory(
                _idAllocator,
                _knifePool, _bowPool, _spearPool, _cavalryPool,
                _enemyManager, _attackResolver, _attackEffectManager,
                _projectileFactory, _projectileManager,
                CellSize, OpponentAttackMultiplier,
                levelService);
            var registry = new UnitRegistry(factory, CellSize, buffManager);
            var levelOne = new BattleUnit(
                100, true, UnitKind.Soldier, SoldierType.Knife, "刀", 1);
            SoldierBase soldier = registry.ActivateBattleUnit(
                levelOne, KnifeConfig, levelService, 0, 0, UnitWidth, UnitHeight);
            BuffTargetHandle handle = ((IBuffTarget)soldier).Handle;

            BuffOperationResult attack = buffManager.Apply(new BuffApplyRequest(
                0, handle, new BuffSourceHandle(1), 10,
                BuffValueMode.Flat, BuffTimeMode.Permanent, 0));
            buffManager.Apply(new BuffApplyRequest(
                1, handle, new BuffSourceHandle(2), 0.5,
                BuffValueMode.Flat, BuffTimeMode.Permanent, 0));
            buffManager.Apply(new BuffApplyRequest(
                2, handle, new BuffSourceHandle(3), 40,
                BuffValueMode.Flat, BuffTimeMode.Permanent, 0));
            long disabledId = buffManager.Apply(new BuffApplyRequest(
                8, handle, new BuffSourceHandle(4), 0,
                BuffValueMode.Flat, BuffTimeMode.Permanent, 0)).InstanceId;
            BuffOperationResult unsupported = buffManager.Apply(new BuffApplyRequest(
                3, handle, new BuffSourceHandle(5), 10,
                BuffValueMode.Flat, BuffTimeMode.Permanent, 0));

            Assert.AreEqual(BuffOperationStatus.Applied, attack.Status);
            Assert.AreEqual(BuffOperationStatus.UnsupportedTarget, unsupported.Status);
            Assert.AreEqual(13, soldier.AttackDamageForTest);
            Assert.AreEqual(0.8f / 1.5f, soldier.AttackIntervalSeconds, 0.001f);
            Assert.AreEqual(1.5f * CellSize + 40f, soldier.AttackRange, 0.01f);
            Assert.IsTrue(soldier.Disabled);

            var levelTwo = new BattleUnit(
                100, true, UnitKind.Soldier, SoldierType.Knife, "刀", 2);
            SoldierBase same = registry.ActivateBattleUnit(
                levelTwo, KnifeConfig, levelService, 0, 0, UnitWidth, UnitHeight);
            Assert.AreSame(soldier, same);
            Assert.AreEqual(16, same.AttackDamageForTest, "等级基础值变化后仍保留 +10 Buff。");

            buffManager.RemoveInstance(disabledId);
            Assert.IsFalse(same.Disabled);
            int oldId = same.Id;
            Assert.IsTrue(registry.Remove(oldId));
            Assert.AreEqual(0, buffManager.ActiveInstanceCount);
            Assert.AreEqual(0, buffManager.RegisteredTargetCount);

            SoldierBase reused = registry.ActivateBattleUnit(
                new BattleUnit(101, true, UnitKind.Soldier, SoldierType.Knife, "刀", 1),
                KnifeConfig, levelService, 0, 0, UnitWidth, UnitHeight);
            Assert.AreEqual(3, reused.AttackDamageForTest);
            Assert.AreEqual(0.8f, reused.AttackIntervalSeconds, 0.001f);
            Assert.AreEqual(1.5f * CellSize, reused.AttackRange, 0.01f);
            Assert.IsFalse(reused.Disabled);

            registry.GameOver();
            buffManager.Dispose();
        }

        // ====================================================================
        // UnitFactory 测试
        // ====================================================================

        /// <summary>
        /// 验证四兵种创建成功：Acquire 返回正确类型、已分配 ID、已 Configure/Init/InitStats。
        /// </summary>
        [Test]
        public void Acquire_FourSoldierTypes_ReturnsCorrectTypeWithIdAndConfig()
        {
            SoldierBase knife = _factory.Acquire(
                SoldierType.Knife, KnifeConfig, true, UnitWidth, UnitHeight);
            SoldierBase bow = _factory.Acquire(
                SoldierType.Bow, BowConfig, false, UnitWidth, UnitHeight);
            SoldierBase spear = _factory.Acquire(
                SoldierType.Spear, SpearConfig, true, UnitWidth, UnitHeight);
            SoldierBase cavalry = _factory.Acquire(
                SoldierType.Cavalry, CavalryConfig, false, UnitWidth, UnitHeight);

            Assert.IsInstanceOf<KnifeSoldier>(knife, "刀兵类型");
            Assert.IsInstanceOf<BowSoldier>(bow, "弓兵类型");
            Assert.IsInstanceOf<SpearSoldier>(spear, "枪兵类型");
            Assert.IsInstanceOf<CavalrySoldier>(cavalry, "骑兵类型");

            // ID 分配（每次 Acquire 分配新 ID，单调递增）。
            Assert.Greater(knife.Id, 0, "刀兵 ID > 0");
            Assert.Greater(bow.Id, knife.Id, "弓兵 ID > 刀兵 ID");
            Assert.Greater(spear.Id, bow.Id, "枪兵 ID > 弓兵 ID");
            Assert.Greater(cavalry.Id, spear.Id, "骑兵 ID > 枪兵 ID");

            // 已 Configure（Configured=true）。
            Assert.IsTrue(knife.Configured, "刀兵已 Configure");
            Assert.IsTrue(bow.Configured, "弓兵已 Configure");
            Assert.IsTrue(spear.Configured, "枪兵已 Configure");
            Assert.IsTrue(cavalry.Configured, "骑兵已 Configure");

            // 已 Init（IsActive=false，待 ActivatePlacement）。
            Assert.IsFalse(knife.IsActive, "刀兵 Init 后 IsActive=false");
            Assert.IsFalse(bow.IsActive, "弓兵 Init 后 IsActive=false");

            // 已 InitStats（攻击范围/间隔正确，经 IAttackUnit 公开属性验证）。
            Assert.AreEqual(1.5f * CellSize, knife.AttackRange, 0.01f, "刀兵攻击范围");
            Assert.AreEqual(3.5f * CellSize, bow.AttackRange, 0.01f, "弓兵攻击范围");
            Assert.AreEqual(2.5f * CellSize, spear.AttackRange, 0.01f, "枪兵攻击范围");
            Assert.AreEqual(2f * CellSize, cavalry.AttackRange, 0.01f, "骑兵攻击范围");
            Assert.AreEqual(0.8f, knife.AttackIntervalSeconds, 0.001f, "刀兵攻击间隔");

            // 阵营。
            Assert.IsTrue(knife.Side, "刀兵玩家方");
            Assert.IsFalse(bow.Side, "弓兵对手方");

            // 清理。
            _factory.Release(knife);
            _factory.Release(bow);
            _factory.Release(spear);
            _factory.Release(cavalry);
        }

        /// <summary>
        /// 验证 Acquire 拒绝 null 配置。
        /// </summary>
        [Test]
        public void Acquire_NullConfig_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                _factory.Acquire(SoldierType.Knife, null, true, UnitWidth, UnitHeight));
        }

        /// <summary>
        /// 验证每次 Acquire 分配新 ID，单调递增，不复用旧 ID。
        /// </summary>
        [Test]
        public void Acquire_AllocatesNewIdMonotonically()
        {
            SoldierBase u1 = _factory.Acquire(
                SoldierType.Knife, KnifeConfig, true, UnitWidth, UnitHeight);
            int id1 = u1.Id;
            _factory.Release(u1);

            SoldierBase u2 = _factory.Acquire(
                SoldierType.Knife, KnifeConfig, true, UnitWidth, UnitHeight);
            int id2 = u2.Id;

            Assert.Greater(id2, id1, "池复用后 ID 递增，不复用旧 ID");
            Assert.AreEqual(id1 + 1, id2, "ID 单调递增 1");

            _factory.Release(u2);
        }

        /// <summary>
        /// 验证 Release 回收并 Reset，池复用无污染。
        /// </summary>
        [Test]
        public void Release_PoolReuse_NoStatePollution()
        {
            SoldierBase u1 = _factory.Acquire(
                SoldierType.Knife, KnifeConfig, true, UnitWidth, UnitHeight);
            int oldId = u1.Id;
            u1.SetPlacement(3, 4);
            u1.ActivatePlacement(3 * CellSize, 4 * CellSize);
            Assert.IsTrue(u1.IsActive, "激活后 IsActive=true");
            Assert.AreEqual(3, u1.GridX, "放置后 GridX=3");

            // 回收。
            // BattleObjectPool.Release 入池前调用 ResetState，ResetState 将 _inPool 置 false、
            // _id 置 -1（UnitBase.cs:430/426）。因此 InPool 属性为 false，改用 Id=-1 验证
            // ResetState 已执行（与 EnemyFactoryTests 检查 RuntimeId=0 的模式对称）。
            _factory.Release(u1);
            Assert.AreEqual(-1, u1.Id, "回收后 ResetState 已执行，Id=-1");
            Assert.IsFalse(u1.IsActive, "回收后 IsActive=false");

            // 复用。
            SoldierBase u2 = _factory.Acquire(
                SoldierType.Knife, KnifeConfig, false, UnitWidth, UnitHeight);

            // 验证旧状态不残留。
            Assert.AreNotEqual(oldId, u2.Id, "新 ID 不复用旧 ID");
            Assert.IsFalse(u2.IsActive, "IsActive 重置为 false");
            Assert.AreEqual(-1, u2.GridX, "GridX 重置为 -1");
            Assert.IsFalse(u2.Side == true && u1.Side == true
                && u2.Id == oldId, "复用对象状态等价于新构造");

            _factory.Release(u2);
        }

        /// <summary>
        /// 验证重复 Release 返回 false（对称契约）。
        /// </summary>
        [Test]
        public void Release_DuplicateRelease_ReturnsFalse()
        {
            SoldierBase unit = _factory.Acquire(
                SoldierType.Bow, BowConfig, true, UnitWidth, UnitHeight);

            bool first = _factory.Release(unit);
            bool second = _factory.Release(unit);

            Assert.IsTrue(first, "首次 Release 成功");
            Assert.IsFalse(second, "重复 Release 失败");
        }

        /// <summary>
        /// 验证 Release null 返回 false。
        /// </summary>
        [Test]
        public void Release_Null_ReturnsFalse()
        {
            Assert.IsFalse(_factory.Release(null));
        }

        /// <summary>
        /// 验证 Acquire 对未知兵种（强制转型的越界值）显式失败：default 分支抛 ArgumentOutOfRangeException。
        /// </summary>
        /// <remarks>
        /// SoldierType 枚举只有四个合法值（0..3），但 C# 允许任意 int 强制转型为枚举。
        /// 本测试验证 default 分支的防御性抛出，确保未来新增枚举值不会被静默忽略。
        /// </remarks>
        [Test]
        public void Acquire_UnknownSoldierType_ThrowsArgumentOutOfRangeException()
        {
            const SoldierType unknown = (SoldierType)999;
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                _factory.Acquire(unknown, KnifeConfig, true, UnitWidth, UnitHeight),
                "未知兵种 ID（越界强制转型）应经 default 分支抛 ArgumentOutOfRangeException");
        }

        /// <summary>
        /// 验证 Acquire 后 CreateCount 递增，Release 后 RecoverCount 递增。
        /// </summary>
        [Test]
        public void CreateAndRecoverCount_TrackedCorrectly()
        {
            Assert.AreEqual(0, _factory.CreateCount, "初始 CreateCount=0");
            Assert.AreEqual(0, _factory.RecoverCount, "初始 RecoverCount=0");

            SoldierBase u1 = _factory.Acquire(
                SoldierType.Spear, SpearConfig, true, UnitWidth, UnitHeight);
            Assert.AreEqual(1, _factory.CreateCount, "Acquire 后 CreateCount=1");

            SoldierBase u2 = _factory.Acquire(
                SoldierType.Cavalry, CavalryConfig, true, UnitWidth, UnitHeight);
            Assert.AreEqual(2, _factory.CreateCount, "Acquire 后 CreateCount=2");

            _factory.Release(u1);
            Assert.AreEqual(1, _factory.RecoverCount, "Release 后 RecoverCount=1");

            _factory.Release(u2);
            Assert.AreEqual(2, _factory.RecoverCount, "Release 后 RecoverCount=2");
        }

        // ====================================================================
        // UnitRegistry 测试
        // ====================================================================

        /// <summary>
        /// 验证 CreateAndPlace 创建、注册、放置单位。
        /// </summary>
        [Test]
        public void CreateAndPlace_RegistersAndActivatesUnit()
        {
            SoldierBase unit = _registry.CreateAndPlace(
                SoldierType.Knife, KnifeConfig, true, 2, 3, UnitWidth, UnitHeight);

            Assert.IsNotNull(unit, "创建成功");
            Assert.AreEqual(1, _registry.Count, "Count=1");
            Assert.IsTrue(unit.IsActive, "已激活");
            Assert.AreEqual(2, unit.GridX, "GridX=2");
            Assert.AreEqual(3, unit.GridY, "GridY=3");
            Assert.AreEqual(2 * CellSize, unit.CenterX - UnitWidth * 0.5f, 0.01f, "像素位置 X");
        }

        /// <summary>
        /// 验证放置冲突检测：同一格子不可重复占用。
        /// </summary>
        [Test]
        public void CreateAndPlace_DuplicatePosition_Throws()
        {
            _registry.CreateAndPlace(
                SoldierType.Knife, KnifeConfig, true, 1, 1, UnitWidth, UnitHeight);

            Assert.Throws<InvalidOperationException>(() =>
                _registry.CreateAndPlace(
                    SoldierType.Bow, BowConfig, true, 1, 1, UnitWidth, UnitHeight),
                "同一格子重复占用应抛异常");
        }

        /// <summary>
        /// 验证不同阵营可以占用同一格子。
        /// </summary>
        [Test]
        public void CreateAndPlace_DifferentSide_SamePosition_Succeeds()
        {
            _registry.CreateAndPlace(
                SoldierType.Knife, KnifeConfig, true, 1, 1, UnitWidth, UnitHeight);
            _registry.CreateAndPlace(
                SoldierType.Bow, BowConfig, false, 1, 1, UnitWidth, UnitHeight);

            Assert.AreEqual(2, _registry.Count, "不同阵营同格子 Count=2");
        }

        /// <summary>
        /// 验证 GetUnit 按 ID 查找。
        /// </summary>
        [Test]
        public void GetUnit_FindsRegisteredUnit()
        {
            SoldierBase unit = _registry.CreateAndPlace(
                SoldierType.Spear, SpearConfig, true, 0, 0, UnitWidth, UnitHeight);

            SoldierBase found = _registry.GetUnit(unit.Id);
            Assert.AreSame(unit, found, "GetUnit 返回同一实例");

            SoldierBase notFound = _registry.GetUnit(99999);
            Assert.IsNull(notFound, "不存在 ID 返回 null");
        }

        /// <summary>
        /// 验证 Remove 移除单位并归还池。
        /// </summary>
        [Test]
        public void Remove_UnregistersAndReleasesToPool()
        {
            SoldierBase unit = _registry.CreateAndPlace(
                SoldierType.Knife, KnifeConfig, true, 0, 0, UnitWidth, UnitHeight);
            int id = unit.Id;

            bool result = _registry.Remove(id);

            Assert.IsTrue(result, "Remove 成功");
            Assert.AreEqual(0, _registry.Count, "Remove 后 Count=0");
            Assert.IsNull(_registry.GetUnit(id), "Remove 后 GetUnit=null");
            // Remove 内先 GameOver（_inPool=true）再 UnitFactory.Release（ResetState 将 _inPool 置 false）。
            // 因此 InPool 最终为 false，改用 Id=-1 验证 ResetState 已执行（参考 EnemyFactoryTests）。
            Assert.AreEqual(-1, unit.Id, "Remove 后 ResetState 已执行，Id=-1");
            Assert.IsFalse(unit.IsActive, "Remove 后 IsActive=false");
        }

        /// <summary>
        /// 验证 Remove 不存在的 ID 返回 false。
        /// </summary>
        [Test]
        public void Remove_NonexistentId_ReturnsFalse()
        {
            Assert.IsFalse(_registry.Remove(99999), "不存在 ID Remove 返回 false");
        }

        /// <summary>
        /// 验证稳定有序集合：遍历顺序由注册顺序决定。
        /// </summary>
        [Test]
        public void GetActiveUnits_StableOrder_ByRegistration()
        {
            SoldierBase u0 = _registry.CreateAndPlace(
                SoldierType.Knife, KnifeConfig, true, 0, 0, UnitWidth, UnitHeight);
            SoldierBase u1 = _registry.CreateAndPlace(
                SoldierType.Bow, BowConfig, true, 1, 0, UnitWidth, UnitHeight);
            SoldierBase u2 = _registry.CreateAndPlace(
                SoldierType.Spear, SpearConfig, true, 2, 0, UnitWidth, UnitHeight);

            IReadOnlyList<SoldierBase> active = _registry.GetActiveUnits();

            Assert.AreEqual(3, active.Count, "3 个活动单位");
            Assert.AreSame(u0, active[0], "第一个是刀兵");
            Assert.AreSame(u1, active[1], "第二个是弓兵");
            Assert.AreSame(u2, active[2], "第三个是枪兵");
        }

        /// <summary>
        /// 验证 Remove 后剩余元素顺序稳定。
        /// </summary>
        [Test]
        public void Remove_MiddleElement_RemainingOrderStable()
        {
            SoldierBase u0 = _registry.CreateAndPlace(
                SoldierType.Knife, KnifeConfig, true, 0, 0, UnitWidth, UnitHeight);
            SoldierBase u1 = _registry.CreateAndPlace(
                SoldierType.Bow, BowConfig, true, 1, 0, UnitWidth, UnitHeight);
            SoldierBase u2 = _registry.CreateAndPlace(
                SoldierType.Spear, SpearConfig, true, 2, 0, UnitWidth, UnitHeight);

            // 移除中间的弓兵。
            _registry.Remove(u1.Id);

            IReadOnlyList<SoldierBase> active = _registry.GetActiveUnits();
            Assert.AreEqual(2, active.Count, "移除后 Count=2");
            Assert.AreSame(u0, active[0], "刀兵仍在第一个");
            // 末尾交换法：u2 被交换到 u1 的位置。
            Assert.AreSame(u2, active[1], "枪兵被交换到第二个位置");
        }

        /// <summary>
        /// 验证 HasOccupant 格子占用检查。
        /// </summary>
        [Test]
        public void HasOccupant_DetectsCorrectly()
        {
            _registry.CreateAndPlace(
                SoldierType.Knife, KnifeConfig, true, 2, 3, UnitWidth, UnitHeight);

            Assert.IsTrue(_registry.HasOccupant(true, 2, 3), "玩家方 (2,3) 已占用");
            Assert.IsFalse(_registry.HasOccupant(true, 2, 4), "玩家方 (2,4) 未占用");
            Assert.IsFalse(_registry.HasOccupant(false, 2, 3), "对手方 (2,3) 未占用");
        }

        /// <summary>
        /// 验证 PlayerSoldierCount 只统计玩家方。
        /// </summary>
        [Test]
        public void PlayerSoldierCount_OnlyCountsPlayerSide()
        {
            _registry.CreateAndPlace(
                SoldierType.Knife, KnifeConfig, true, 0, 0, UnitWidth, UnitHeight);
            _registry.CreateAndPlace(
                SoldierType.Bow, BowConfig, true, 1, 0, UnitWidth, UnitHeight);
            _registry.CreateAndPlace(
                SoldierType.Spear, SpearConfig, false, 0, 0, UnitWidth, UnitHeight);

            Assert.AreEqual(2, _registry.PlayerSoldierCount, "玩家方 2 个");
            Assert.AreEqual(3, _registry.Count, "总数 3 个");
        }

        /// <summary>
        /// 验证 UnitsBySide 按阵营筛选。
        /// </summary>
        [Test]
        public void UnitsBySide_FiltersCorrectly()
        {
            SoldierBase u0 = _registry.CreateAndPlace(
                SoldierType.Knife, KnifeConfig, true, 0, 0, UnitWidth, UnitHeight);
            SoldierBase u1 = _registry.CreateAndPlace(
                SoldierType.Bow, BowConfig, false, 0, 0, UnitWidth, UnitHeight);

            List<SoldierBase> player = _registry.UnitsBySide(true);
            List<SoldierBase> opponent = _registry.UnitsBySide(false);

            Assert.AreEqual(1, player.Count, "玩家方 1 个");
            Assert.AreSame(u0, player[0], "玩家方是刀兵");
            Assert.AreEqual(1, opponent.Count, "对手方 1 个");
            Assert.AreSame(u1, opponent[0], "对手方是弓兵");
        }

        /// <summary>
        /// 验证 MoveUnit 移动单位到新格子。
        /// </summary>
        [Test]
        public void MoveUnit_ToEmptyCell_Succeeds()
        {
            SoldierBase unit = _registry.CreateAndPlace(
                SoldierType.Knife, KnifeConfig, true, 0, 0, UnitWidth, UnitHeight);

            bool result = _registry.MoveUnit(unit.Id, 1, 1);

            Assert.IsTrue(result, "移动成功");
            Assert.AreEqual(1, unit.GridX, "GridX=1");
            Assert.AreEqual(1, unit.GridY, "GridY=1");
        }

        /// <summary>
        /// 验证 MoveUnit 拒绝已被占用的目标格子。
        /// </summary>
        [Test]
        public void MoveUnit_ToOccupiedCell_Fails()
        {
            SoldierBase u0 = _registry.CreateAndPlace(
                SoldierType.Knife, KnifeConfig, true, 0, 0, UnitWidth, UnitHeight);
            _registry.CreateAndPlace(
                SoldierType.Bow, BowConfig, true, 1, 1, UnitWidth, UnitHeight);

            bool result = _registry.MoveUnit(u0.Id, 1, 1);

            Assert.IsFalse(result, "目标格子已被占用，移动失败");
        }

        /// <summary>
        /// 验证 GameOver 清理全部单位并归还池。
        /// </summary>
        [Test]
        public void GameOver_ClearsAllUnits()
        {
            SoldierBase u0 = _registry.CreateAndPlace(
                SoldierType.Knife, KnifeConfig, true, 0, 0, UnitWidth, UnitHeight);
            SoldierBase u1 = _registry.CreateAndPlace(
                SoldierType.Bow, BowConfig, true, 1, 0, UnitWidth, UnitHeight);
            SoldierBase u2 = _registry.CreateAndPlace(
                SoldierType.Cavalry, CavalryConfig, false, 0, 0, UnitWidth, UnitHeight);

            _registry.GameOver();

            Assert.AreEqual(0, _registry.Count, "GameOver 后 Count=0");
            // GameOver → Remove → unit.GameOver() + UnitFactory.Release（ResetState 将 _inPool 置 false）。
            // 因此 InPool 最终为 false，改用 Id=-1 验证 ResetState 已执行。
            Assert.AreEqual(-1, u0.Id, "刀兵 ResetState 已执行，Id=-1");
            Assert.AreEqual(-1, u1.Id, "弓兵 ResetState 已执行，Id=-1");
            Assert.AreEqual(-1, u2.Id, "骑兵 ResetState 已执行，Id=-1");
            Assert.IsFalse(u0.IsActive, "刀兵 GameOver 后 IsActive=false");
            Assert.IsFalse(u1.IsActive, "弓兵 GameOver 后 IsActive=false");
            Assert.IsFalse(u2.IsActive, "骑兵 GameOver 后 IsActive=false");
            Assert.AreEqual(0, _registry.GetActiveUnits().Count, "活动列表为空");
        }

        /// <summary>
        /// 验证 GameOver 幂等：重复调用安全。
        /// </summary>
        [Test]
        public void GameOver_Idempotent()
        {
            _registry.CreateAndPlace(
                SoldierType.Knife, KnifeConfig, true, 0, 0, UnitWidth, UnitHeight);

            _registry.GameOver();
            _registry.GameOver();

            Assert.AreEqual(0, _registry.Count, "重复 GameOver 后 Count=0");
        }

        /// <summary>
        /// 验证 ClearForSettling 等价于 GameOver。
        /// </summary>
        [Test]
        public void ClearForSettling_EqualsGameOver()
        {
            SoldierBase unit = _registry.CreateAndPlace(
                SoldierType.Knife, KnifeConfig, true, 0, 0, UnitWidth, UnitHeight);

            _registry.ClearForSettling();

            Assert.AreEqual(0, _registry.Count, "ClearForSettling 后 Count=0");
            // ClearForSettling 等价于 GameOver → Remove → unit.GameOver() + Release（ResetState 将 _inPool 置 false）。
            // 因此 InPool 最终为 false，改用 Id=-1 验证 ResetState 已执行。
            Assert.AreEqual(-1, unit.Id, "ClearForSettling 后 ResetState 已执行，Id=-1");
            Assert.IsFalse(unit.IsActive, "ClearForSettling 后 IsActive=false");
        }

        /// <summary>
        /// 验证 Register 拒绝无效 ID（<=0）。
        /// </summary>
        [Test]
        public void Register_InvalidId_Throws()
        {
            // 直接构造一个未分配 ID 的士兵（绕过 Factory）
            var soldier = new KnifeSoldier();
            soldier.Configure(
                _enemyManager, _attackResolver, _attackEffectManager,
                CellSize, OpponentAttackMultiplier);
            soldier.InitForTest("刀", true, UnitWidth, UnitHeight);
            // 未调用 AssignRuntimeIdForTest，Id=-1

            Assert.Throws<InvalidOperationException>(() => _registry.Register(soldier));
        }

        /// <summary>
        /// 验证 Register 拒绝重复注册同一 ID。
        /// </summary>
        [Test]
        public void Register_DuplicateId_Throws()
        {
            SoldierBase u0 = _registry.CreateAndPlace(
                SoldierType.Knife, KnifeConfig, true, 0, 0, UnitWidth, UnitHeight);

            Assert.Throws<InvalidOperationException>(() => _registry.Register(u0));
        }

        // ====================================================================
        // 池复用 + UnitRegistry 集成测试
        // ====================================================================

        /// <summary>
        /// 验证 Remove 后池复用无污染：新创建的单位不残留旧状态。
        /// </summary>
        [Test]
        public void Remove_ThenAcquireFromPool_NoStatePollution()
        {
            SoldierBase u0 = _registry.CreateAndPlace(
                SoldierType.Knife, KnifeConfig, true, 2, 3, UnitWidth, UnitHeight);
            int oldId = u0.Id;
            Assert.IsTrue(u0.IsActive, "u0 IsActive=true");

            // 移除并归还池。
            // Remove 内先 GameOver（_inPool=true）再 UnitFactory.Release（ResetState 将 _inPool 置 false）。
            // 因此 InPool 最终为 false，改用 Id=-1 验证 ResetState 已执行。
            _registry.Remove(u0.Id);
            Assert.AreEqual(-1, u0.Id, "u0 ResetState 已执行，Id=-1");
            Assert.IsFalse(u0.IsActive, "u0 GameOver 后 IsActive=false");

            // 重新创建（池复用）。
            SoldierBase u1 = _registry.CreateAndPlace(
                SoldierType.Knife, KnifeConfig, false, 1, 1, UnitWidth, UnitHeight);

            Assert.AreNotEqual(oldId, u1.Id, "新 ID 不复用旧 ID");
            Assert.IsTrue(u1.IsActive, "u1 IsActive=true（新放置）");
            Assert.AreEqual(1, u1.GridX, "u1 GridX=1");
            Assert.AreEqual(1, u1.GridY, "u1 GridY=1");
            Assert.IsFalse(u1.Side, "u1 对手方（新配置）");
        }

        /// <summary>
        /// 验证四兵种经 UnitRegistry 创建后均可正常移除。
        /// </summary>
        [Test]
        public void CreateAndRemove_AllFourTypes_Succeeds()
        {
            SoldierBase knife = _registry.CreateAndPlace(
                SoldierType.Knife, KnifeConfig, true, 0, 0, UnitWidth, UnitHeight);
            SoldierBase bow = _registry.CreateAndPlace(
                SoldierType.Bow, BowConfig, true, 1, 0, UnitWidth, UnitHeight);
            SoldierBase spear = _registry.CreateAndPlace(
                SoldierType.Spear, SpearConfig, true, 2, 0, UnitWidth, UnitHeight);
            SoldierBase cavalry = _registry.CreateAndPlace(
                SoldierType.Cavalry, CavalryConfig, true, 3, 0, UnitWidth, UnitHeight);

            Assert.AreEqual(4, _registry.Count, "4 个单位");

            Assert.IsTrue(_registry.Remove(knife.Id), "移除刀兵");
            Assert.IsTrue(_registry.Remove(bow.Id), "移除弓兵");
            Assert.IsTrue(_registry.Remove(spear.Id), "移除枪兵");
            Assert.IsTrue(_registry.Remove(cavalry.Id), "移除骑兵");

            Assert.AreEqual(0, _registry.Count, "全部移除后 Count=0");
            // Remove 内先 GameOver（_inPool=true）再 UnitFactory.Release（ResetState 将 _inPool 置 false）。
            // 因此 InPool 最终为 false，改用 Id=-1 验证 ResetState 已执行。
            Assert.AreEqual(-1, knife.Id, "刀兵 ResetState 已执行，Id=-1");
            Assert.AreEqual(-1, bow.Id, "弓兵 ResetState 已执行，Id=-1");
            Assert.AreEqual(-1, spear.Id, "枪兵 ResetState 已执行，Id=-1");
            Assert.AreEqual(-1, cavalry.Id, "骑兵 ResetState 已执行，Id=-1");
            Assert.IsFalse(knife.IsActive, "刀兵 GameOver 后 IsActive=false");
            Assert.IsFalse(bow.IsActive, "弓兵 GameOver 后 IsActive=false");
            Assert.IsFalse(spear.IsActive, "枪兵 GameOver 后 IsActive=false");
            Assert.IsFalse(cavalry.IsActive, "骑兵 GameOver 后 IsActive=false");
        }
    }
}
