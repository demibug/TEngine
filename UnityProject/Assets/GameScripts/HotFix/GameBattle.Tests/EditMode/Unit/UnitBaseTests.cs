using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Unit
{
    // ============================================================================
    // 任务 6.1 返工：UnitBase / SoldierBase 单元测试
    // ----------------------------------------------------------------------------
    // 校验要求（返工轮次 1）：
    //   缺少 UnitBase/SoldierBase 专属测试，需补充覆盖：
    //     1. Acquire（构造/Configure/Init）后字段值正确
    //     2. Release（GameOver + ResetState）后全部字段清零/默认
    //     3. ResetState 幂等性
    //     4. ResetState 后无目标/冷却/注入依赖引用残留
    //     5. GameOver 幂等（重复调用返回 false）
    //     6. AssignRuntimeId 拒绝非正 ID
    //     7. Configure 拒绝 null 参数（SoldierBase）
    //     8. Init 前 RequireConfigured 守卫
    //     9. SetState 状态切换回调顺序（OnExitState → OnEnterState）
    //    10. 池复用无污染（Acquire → 配置 → Release → 复用 → 验证旧状态不残留）
    //
    // 测试不接触 Scene、FUI 或资源加载，符合纯逻辑 EditMode 约束（task 2.2）。
    // 依赖 InternalsVisibleTo("GameBattle.Tests")，可访问 internal 类型与构造。
    // ============================================================================

    /// <summary>
    /// UnitBase 生命周期、状态机、回收与池化契约单元测试（task 6.1 返工）。
    /// </summary>
    /// <remarks>
    /// 验证覆盖：构造默认值、Configure/Init 守卫、AssignRuntimeId 校验、
    /// SetState 回调顺序、GameOver 幂等、ResetState 完整清空与幂等、池复用无污染。
    /// </remarks>
    [TestFixture]
    internal class UnitBaseTests
    {
        // ====================================================================
        // 测试常量
        // ====================================================================

        /// <summary>测试用逻辑宽度（对应 displayObject.width）。</summary>
        private const float UnitWidth = 40f;

        /// <summary>测试用逻辑高度。</summary>
        private const float UnitHeight = 40f;

        // ====================================================================
        // 测试子类 —— 暴露 protected/internal API 供测试
        // ====================================================================

        /// <summary>
        /// 最小 UnitBase 具体子类，暴露受保护 API 并记录状态回调顺序。
        /// </summary>
        private class FakeUnitBase : UnitBase
        {
            /// <summary>状态回调记录列表，按调用顺序追加。</summary>
            public readonly List<string> StateCallbacks = new List<string>();

            /// <summary>Attack 调用次数。</summary>
            public int AttackCallCount;

            /// <summary>暴露 Configure 供测试调用。</summary>
            internal void ConfigureForTest() => Configure();

            /// <summary>暴露 AssignRuntimeId 供测试调用。</summary>
            internal void AssignId(int id) => AssignRuntimeId(id);

            /// <summary>暴露 Init 供测试调用。</summary>
            internal void InitForTest(string unitText, bool side, float width, float height)
                => Init(unitText, side, width, height);

            /// <summary>暴露 SetPlacement 供测试调用。</summary>
            internal void PlaceAt(int gridX, int gridY) => SetPlacement(gridX, gridY);

            /// <summary>暴露 ActivatePlacement 供测试调用。</summary>
            internal void ActivateAt(float pixelX, float pixelY) => ActivatePlacement(pixelX, pixelY);

            /// <summary>暴露 UnitText 供测试验证。</summary>
            internal string TestUnitText => UnitText;

            /// <summary>暴露 Configured 供测试验证。</summary>
            internal bool IsConfigured => Configured;

            /// <summary>暴露 LifecycleGeneration 供测试验证。</summary>
            internal int Generation => LifecycleGeneration;

            /// <summary>暴露 GridX/GridY 供测试验证。</summary>
            internal int TestGridX => GridX;

            /// <summary>暴露 GridY 供测试验证。</summary>
            internal int TestGridY => GridY;

            /// <summary>暴露 PreviousGridX 供测试验证。</summary>
            internal int TestPrevGridX => PreviousGridX;

            /// <summary>暴露 PreviousGridY 供测试验证。</summary>
            internal int TestPrevGridY => PreviousGridY;

            /// <inheritdoc/>
            protected override void OnExitState(AttackUnitState previousState)
            {
                StateCallbacks.Add($"Exit:{previousState}");
            }

            /// <inheritdoc/>
            protected override void OnEnterState(AttackUnitState nextState)
            {
                StateCallbacks.Add($"Enter:{nextState}");
            }

            /// <inheritdoc/>
            public override void Attack()
            {
                AttackCallCount++;
            }
        }

        // ====================================================================
        // 构造与默认值测试
        // ====================================================================

        [Test]
        [Description("构造后全部字段为默认值，等价于新构造。")]
        public void Constructor_FieldsAreDefaults()
        {
            var unit = new FakeUnitBase();

            Assert.AreEqual(-1, unit.Id, "Id 默认 -1。");
            Assert.IsTrue(unit.Side, "Side 默认 true（玩家方）。");
            Assert.IsFalse(unit.IsActive, "IsActive 默认 false。");
            Assert.IsFalse(unit.Disabled, "Disabled 默认 false。");
            Assert.IsFalse(unit.InPool, "InPool 默认 false。");
            Assert.AreEqual(AttackUnitState.Idle, unit.CurrentState, "CurrentState 默认 Idle。");
            Assert.AreEqual(0f, unit.CenterX, "CenterX 默认 0（x=0, width=0）。");
            Assert.AreEqual(0f, unit.CenterY, "CenterY 默认 0。");
            Assert.AreEqual(0f, unit.AttackRange, "AttackRange 默认 0。");
            Assert.AreEqual(1f, unit.AttackIntervalSeconds, "AttackIntervalSeconds 默认 1。");
            Assert.AreEqual(0, unit.LastAttackTimeMs, "LastAttackTimeMs 默认 0。");
            Assert.AreEqual(-1, unit.TestGridX, "GridX 默认 -1。");
            Assert.AreEqual(-1, unit.TestGridY, "GridY 默认 -1。");
            Assert.IsFalse(unit.IsConfigured, "Configured 默认 false。");
        }

        // ====================================================================
        // Configure / RequireConfigured 守卫测试
        // ====================================================================

        [Test]
        [Description("Init 前未 Configure 抛 InvalidOperationException。")]
        public void Init_BeforeConfigure_Throws()
        {
            var unit = new FakeUnitBase();
            Assert.Throws<InvalidOperationException>(() =>
                unit.InitForTest("刀", true, UnitWidth, UnitHeight));
        }

        [Test]
        [Description("Configure 后 Configured 标记为 true，Init 不抛异常。")]
        public void Configure_ThenInit_Succeeds()
        {
            var unit = new FakeUnitBase();
            unit.ConfigureForTest();
            Assert.IsTrue(unit.IsConfigured, "Configure 后标记为 true。");

            Assert.DoesNotThrow(() =>
                unit.InitForTest("刀", true, UnitWidth, UnitHeight));
        }

        // ====================================================================
        // AssignRuntimeId 校验测试
        // ====================================================================

        [Test]
        [Description("AssignRuntimeId 设置运行时 ID，<=0 抛 ArgumentOutOfRangeException。")]
        public void AssignRuntimeId_SetsId_InvalidThrows()
        {
            var unit = new FakeUnitBase();
            unit.AssignId(42);
            Assert.AreEqual(42, unit.Id, "ID 已设置。");

            Assert.Throws<ArgumentOutOfRangeException>(() => unit.AssignId(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => unit.AssignId(-1));
        }

        // ====================================================================
        // Init 后字段值正确测试
        // ====================================================================

        [Test]
        [Description("Acquire（Configure + Init）后字段值正确。")]
        public void Acquire_AfterConfigureInit_FieldsCorrect()
        {
            var unit = new FakeUnitBase();
            unit.ConfigureForTest();
            unit.AssignId(100);
            unit.InitForTest("弓", false, UnitWidth, UnitHeight);

            Assert.AreEqual(100, unit.Id, "ID 已由 AssignRuntimeId 设置。");
            Assert.AreEqual("弓", unit.TestUnitText, "UnitText 已设置。");
            Assert.IsFalse(unit.Side, "Side=false（对手方）。");
            Assert.AreEqual(UnitWidth * 0.5f, unit.CenterX, "CenterX = width/2 = 20。");
            Assert.AreEqual(UnitHeight * 0.5f, unit.CenterY, "CenterY = height/2 = 20。");
            Assert.AreEqual(AttackUnitState.Idle, unit.CurrentState, "Init 后状态为 Idle。");
            Assert.IsFalse(unit.IsActive, "Init 后 IsActive=false（尚未 ActivatePlacement）。");
            Assert.AreEqual(1, unit.Generation, "Init 后生命周期代号递增到 1。");
        }

        // ====================================================================
        // SetState 状态切换回调顺序测试
        // ====================================================================

        [Test]
        [Description("SetState 切换状态时回调顺序为 OnExitState → OnEnterState。")]
        public void SetState_CallbackOrder_ExitThenEnter()
        {
            var unit = new FakeUnitBase();
            unit.ConfigureForTest();
            unit.InitForTest("刀", true, UnitWidth, UnitHeight);
            unit.StateCallbacks.Clear();

            // Idle → Attack：应先 Exit:Idle 再 Enter:Attack。
            unit.SetState(AttackUnitState.Attack);
            Assert.AreEqual(2, unit.StateCallbacks.Count, "切换产生两个回调。");
            Assert.AreEqual("Exit:Idle", unit.StateCallbacks[0], "先退出旧状态。");
            Assert.AreEqual("Enter:Attack", unit.StateCallbacks[1], "后进入新状态。");
            Assert.AreEqual(AttackUnitState.Attack, unit.CurrentState, "当前状态为 Attack。");

            unit.StateCallbacks.Clear();
            // Attack → Attack：同状态不触发回调。
            unit.SetState(AttackUnitState.Attack);
            Assert.AreEqual(0, unit.StateCallbacks.Count, "同状态切换不触发回调。");
        }

        [Test]
        [Description("SetState 双向切换（Idle→Attack→Idle）回调正确。")]
        public void SetState_RoundTrip_CallbacksCorrect()
        {
            var unit = new FakeUnitBase();
            unit.ConfigureForTest();
            unit.InitForTest("刀", true, UnitWidth, UnitHeight);
            unit.StateCallbacks.Clear();

            unit.SetState(AttackUnitState.Attack);
            unit.SetState(AttackUnitState.Idle);

            Assert.AreEqual(4, unit.StateCallbacks.Count, "两次切换产生四个回调。");
            Assert.AreEqual("Exit:Idle", unit.StateCallbacks[0]);
            Assert.AreEqual("Enter:Attack", unit.StateCallbacks[1]);
            Assert.AreEqual("Exit:Attack", unit.StateCallbacks[2]);
            Assert.AreEqual("Enter:Idle", unit.StateCallbacks[3]);
            Assert.AreEqual(AttackUnitState.Idle, unit.CurrentState, "最终回到 Idle。");
        }

        // ====================================================================
        // ActivatePlacement 测试
        // ====================================================================

        [Test]
        [Description("ActivatePlacement 设置逻辑位置并标记 IsActive=true。")]
        public void ActivatePlacement_SetsPosition_AndIsActive()
        {
            var unit = new FakeUnitBase();
            unit.ConfigureForTest();
            unit.InitForTest("刀", true, UnitWidth, UnitHeight);
            unit.PlaceAt(2, 3);
            unit.ActivateAt(160f, 240f);

            Assert.IsTrue(unit.IsActive, "ActivatePlacement 后 IsActive=true。");
            Assert.AreEqual(160f + UnitWidth * 0.5f, unit.CenterX, "CenterX = pixelX + width/2。");
            Assert.AreEqual(240f + UnitHeight * 0.5f, unit.CenterY, "CenterY = pixelY + height/2。");
            Assert.AreEqual(2, unit.TestGridX, "GridX 已由 SetPlacement 设置。");
            Assert.AreEqual(3, unit.TestGridY, "GridY 已由 SetPlacement 设置。");
        }

        [Test]
        [Description("SetPlacement 记录 previousGridPosition。")]
        public void SetPlacement_RecordsPreviousGrid()
        {
            var unit = new FakeUnitBase();
            unit.ConfigureForTest();
            unit.InitForTest("刀", true, UnitWidth, UnitHeight);

            unit.PlaceAt(1, 2);
            Assert.AreEqual(-1, unit.TestPrevGridX, "首次放置 previousGridX=-1。");
            Assert.AreEqual(-1, unit.TestPrevGridY, "首次放置 previousGridY=-1。");

            unit.PlaceAt(3, 4);
            Assert.AreEqual(1, unit.TestPrevGridX, "二次放置 previousGridX=前次 gridX。");
            Assert.AreEqual(2, unit.TestPrevGridY, "二次放置 previousGridY=前次 gridY。");
            Assert.AreEqual(3, unit.TestGridX, "当前 gridX=3。");
            Assert.AreEqual(4, unit.TestGridY, "当前 gridY=4。");
        }

        // ====================================================================
        // GameOver 幂等测试
        // ====================================================================

        [Test]
        [Description("GameOver 首次返回 true，重复调用返回 false（幂等）。")]
        public void GameOver_Idempotent()
        {
            var unit = new FakeUnitBase();
            unit.ConfigureForTest();
            unit.AssignId(1);
            unit.InitForTest("刀", true, UnitWidth, UnitHeight);
            unit.ActivateAt(0f, 0f);
            unit.LastAttackTimeMs = 5000;

            Assert.IsTrue(unit.GameOver(), "首次 GameOver 返回 true。");
            Assert.IsTrue(unit.InPool, "已标记入池。");
            Assert.IsFalse(unit.IsActive, "IsActive 已置 false。");
            Assert.AreEqual(AttackUnitState.Idle, unit.CurrentState, "状态回到 Idle。");
            Assert.AreEqual(0, unit.LastAttackTimeMs, "LastAttackTimeMs 已清零。");

            Assert.IsFalse(unit.GameOver(), "重复 GameOver 返回 false。");
        }

        // ====================================================================
        // ResetState 完整清空测试
        // ====================================================================

        [Test]
        [Description("Release（GameOver + ResetState）后全部字段清零/默认。")]
        public void Release_GameOverPlusReset_AllFieldsCleared()
        {
            var unit = new FakeUnitBase();
            unit.ConfigureForTest();
            unit.AssignId(777);
            unit.InitForTest("弓", false, UnitWidth, UnitHeight);
            unit.PlaceAt(3, 4);
            unit.ActivateAt(240f, 320f);
            unit.LastAttackTimeMs = 9999;
            unit.SetState(AttackUnitState.Attack);

            // 模拟池 Release：先 GameOver 再 ResetState。
            unit.GameOver();
            (unit as IPoolableBattleObject).ResetState();

            // 逐字段断言。
            Assert.AreEqual(-1, unit.Id, "ID 已重置为 -1。");
            Assert.IsTrue(unit.Side, "Side 已重置为 true。");
            Assert.IsFalse(unit.IsActive, "IsActive 已重置为 false。");
            Assert.IsFalse(unit.Disabled, "Disabled 已重置为 false。");
            Assert.IsFalse(unit.InPool, "InPool 已重置为 false。");
            Assert.AreEqual(AttackUnitState.Idle, unit.CurrentState, "CurrentState 已重置为 Idle。");
            Assert.AreEqual(0f, unit.CenterX, "CenterX 已重置为 0。");
            Assert.AreEqual(0f, unit.CenterY, "CenterY 已重置为 0。");
            Assert.AreEqual(0f, unit.AttackRange, "AttackRange 已重置为 0。");
            Assert.AreEqual(1f, unit.AttackIntervalSeconds, "AttackIntervalSeconds 已重置为 1。");
            Assert.AreEqual(0, unit.LastAttackTimeMs, "LastAttackTimeMs 已重置为 0。");
            Assert.AreEqual(-1, unit.TestGridX, "GridX 已重置为 -1。");
            Assert.AreEqual(-1, unit.TestGridY, "GridY 已重置为 -1。");
            Assert.AreEqual(-1, unit.TestPrevGridX, "PreviousGridX 已重置为 -1。");
            Assert.AreEqual(-1, unit.TestPrevGridY, "PreviousGridY 已重置为 -1。");
            Assert.IsFalse(unit.IsConfigured, "Configured 已重置为 false。");
        }

        [Test]
        [Description("ResetState 幂等：多次调用结果状态相同。")]
        public void ResetState_Idempotent()
        {
            var unit = new FakeUnitBase();
            unit.ConfigureForTest();
            unit.AssignId(555);
            unit.InitForTest("枪", true, UnitWidth, UnitHeight);
            unit.ActivateAt(100f, 100f);
            unit.SetState(AttackUnitState.Attack);

            (unit as IPoolableBattleObject).ResetState();
            var stateAfterFirst = unit.CurrentState;
            var idAfterFirst = unit.Id;
            var activeAfterFirst = unit.IsActive;

            (unit as IPoolableBattleObject).ResetState();
            Assert.AreEqual(stateAfterFirst, unit.CurrentState, "多次 Reset 状态相同。");
            Assert.AreEqual(idAfterFirst, unit.Id, "多次 Reset ID 相同。");
            Assert.AreEqual(activeAfterFirst, unit.IsActive, "多次 Reset IsActive 相同。");

            // 第三次仍安全。
            (unit as IPoolableBattleObject).ResetState();
            Assert.AreEqual(stateAfterFirst, unit.CurrentState, "三次 Reset 状态仍相同。");
        }

        // ====================================================================
        // 池复用无污染测试
        // ====================================================================

        [Test]
        [Description("池复用无污染：Acquire → 配置 → Release → 复用 → 旧状态不残留。")]
        public void PoolReuse_NoPollution()
        {
            var pool = new BattleObjectPool<FakeUnitBase>(() => new FakeUnitBase());

            // 第一轮：Acquire → 配置 → 污染 → Release。
            FakeUnitBase first = pool.Acquire();
            first.ConfigureForTest();
            first.AssignId(888);
            first.InitForTest("骑", false, UnitWidth, UnitHeight);
            first.PlaceAt(5, 6);
            first.ActivateAt(400f, 480f);
            first.LastAttackTimeMs = 12345;
            first.SetState(AttackUnitState.Attack);

            pool.Release(first);

            // 验证 Release 后已 Reset。
            Assert.AreEqual(-1, first.Id, "Release 后 ID=-1。");
            Assert.IsFalse(first.IsActive, "Release 后 IsActive=false。");
            Assert.IsFalse(first.InPool, "Release 后 InPool=false（Reset 清除）。");

            // 第二轮：Acquire 应复用同一对象，状态已清空。
            FakeUnitBase second = pool.Acquire();
            Assert.AreSame(first, second, "LIFO 复用同一对象。");
            Assert.AreEqual(-1, second.Id, "复用对象 ID=-1（无旧 ID 残留）。");
            Assert.IsFalse(second.IsActive, "复用对象 IsActive=false（无旧活动残留）。");
            Assert.AreEqual(AttackUnitState.Idle, second.CurrentState, "复用对象状态 Idle（无旧 Attack 残留）。");
            Assert.AreEqual(0, second.LastAttackTimeMs, "复用对象 LastAttackTimeMs=0（无旧冷却残留）。");
            Assert.IsFalse(second.IsConfigured, "复用对象 Configured=false（需重新 Configure）。");
            Assert.AreEqual(-1, second.TestGridX, "复用对象 GridX=-1（无旧坐标残留）。");
            Assert.AreEqual(0f, second.CenterX, "复用对象 CenterX=0（无旧位置残留）。");

            pool.Release(second);
        }

        // ====================================================================
        // 不持有 Unity GameObject 验证
        // ====================================================================

        [Test]
        [Description("UnitBase 不继承 UnityEngine.MonoBehaviour 或引用 UnityEngine 组件。")]
        public void UnitBase_DoesNotReferenceUnityEngine()
        {
            Type type = typeof(UnitBase);
            Assert.IsFalse(type.IsSubclassOf(typeof(UnityEngine.MonoBehaviour)),
                "UnitBase 不继承 MonoBehaviour。");
            Assert.IsFalse(type.IsSubclassOf(typeof(UnityEngine.ScriptableObject)),
                "UnitBase 不继承 ScriptableObject。");
        }
    }

    // ============================================================================
    // SoldierBase 测试
    // ============================================================================

    /// <summary>
    /// SoldierBase 攻击数值、Configure 校验、回收与池化契约单元测试（task 6.1 返工）。
    /// </summary>
    /// <remarks>
    /// 验证覆盖：Configure null 拒绝、InitializeStats 数值正确、Attack 守卫、
    /// GameOver 取消攻击效果与清空目标、ResetState 无目标/冷却/依赖残留、池复用无污染。
    /// </remarks>
    [TestFixture]
    internal class SoldierBaseTests
    {
        // ====================================================================
        // 测试常量
        // ====================================================================

        /// <summary>格子尺寸（px，对应 map.gridWidth=80）。</summary>
        private const float CellSize = 80f;

        /// <summary>测试用逻辑宽度。</summary>
        private const float UnitWidth = 40f;

        /// <summary>测试用逻辑高度。</summary>
        private const float UnitHeight = 40f;

        // ====================================================================
        // 测试子类 —— 暴露 protected/internal API 供测试
        // ====================================================================

        /// <summary>
        /// 最小 SoldierBase 具体子类，暴露受保护 API 并记录 PerformAttack 调用。
        /// </summary>
        private class FakeSoldier : SoldierBase
        {
            /// <summary>PerformAttack 调用次数。</summary>
            public int PerformAttackCallCount;

            /// <summary>暴露 Configure 供测试调用。</summary>
            internal void ConfigureForTest(
                EnemyManager enemyManager,
                AttackResolver attackResolver,
                AttackEffectManager attackEffectManager)
            {
                Configure(enemyManager, attackResolver, attackEffectManager, CellSize, 1);
            }

            internal void ConfigureForTest(
                EnemyManager enemyManager,
                AttackResolver attackResolver,
                AttackEffectManager attackEffectManager,
                float cellSize,
                int opponentAttackMultiplier)
            {
                Configure(enemyManager, attackResolver, attackEffectManager, cellSize, opponentAttackMultiplier);
            }

            /// <summary>暴露 Init 供测试调用。</summary>
            internal void InitForTest(string unitText, bool side, float width, float height)
                => Init(unitText, side, width, height);

            /// <summary>暴露 InitializeStats 供测试调用。</summary>
            internal void InitStatsForTest(UnitConfigSnapshot config) => InitializeStats(config);

            /// <summary>暴露 AssignRuntimeId 供测试调用。</summary>
            internal void AssignId(int id) => AssignRuntimeId(id);

            /// <summary>暴露 SetPlacement 供测试调用。</summary>
            internal void PlaceAt(int gridX, int gridY) => SetPlacement(gridX, gridY);

            /// <summary>暴露 ActivatePlacement 供测试调用。</summary>
            internal void ActivateAt(float pixelX, float pixelY) => ActivatePlacement(pixelX, pixelY);

            /// <summary>暴露 Configured 供测试验证。</summary>
            internal bool IsConfigured => Configured;

            /// <summary>暴露 GridX 供测试验证。</summary>
            internal int TestGridX => GridX;

            /// <summary>暴露 GridY 供测试验证。</summary>
            internal int TestGridY => GridY;

            /// <summary>暴露 Targets 列表计数供测试验证（通过反射读取 _targets）。</summary>
            internal int TargetCount => GetTargetCount();

            /// <summary>暴露 HasAttackEffectManager 引用是否为 null。</summary>
            internal bool HasAttackEffectManager => GetAttackEffectManagerPresent();

            /// <summary>暴露 HasEnemyManager 引用是否为 null。</summary>
            internal bool HasEnemyManager => GetEnemyManagerPresent();

            /// <summary>暴露 HasAttackResolver 引用是否为 null。</summary>
            internal bool HasAttackResolver => GetAttackResolverPresent();

            /// <inheritdoc/>
            protected internal override void PerformAttack()
            {
                PerformAttackCallCount++;
            }

            // 通过反射读取 private 字段，避免为测试改动生产代码可见性。
            private int GetTargetCount()
            {
                var field = typeof(SoldierBase).GetField(
                    "_targets",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic);
                var list = field?.GetValue(this) as List<EnemyTargetDto>;
                return list?.Count ?? 0;
            }

            private bool GetAttackEffectManagerPresent()
            {
                var field = typeof(SoldierBase).GetField(
                    "_attackEffectManager",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic);
                return field?.GetValue(this) != null;
            }

            private bool GetEnemyManagerPresent()
            {
                var field = typeof(SoldierBase).GetField(
                    "_enemyManager",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic);
                return field?.GetValue(this) != null;
            }

            private bool GetAttackResolverPresent()
            {
                var field = typeof(SoldierBase).GetField(
                    "_attackResolver",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic);
                return field?.GetValue(this) != null;
            }
        }

        // ====================================================================
        // 测试夹具
        // ====================================================================

        /// <summary>
        /// 构造测试用依赖三件套（EnemyManager / AttackResolver / AttackEffectManager）。
        /// </summary>
        private static (EnemyManager em, AttackResolver ar, AttackEffectManager aem) BuildDependencies()
        {
            var em = new EnemyManager();
            var ar = new AttackResolver();
            var aem = new AttackEffectManager();
            return (em, ar, aem);
        }

        /// <summary>
        /// 构造一个已 Configure + Init + InitializeStats 的测试士兵。
        /// </summary>
        private static FakeSoldier CreateConfiguredSoldier(
            int id = 1,
            bool side = true,
            string unitText = "刀")
        {
            var (em, ar, aem) = BuildDependencies();
            var soldier = new FakeSoldier();
            soldier.ConfigureForTest(em, ar, aem);
            soldier.AssignId(id);
            soldier.InitForTest(unitText, side, UnitWidth, UnitHeight);
            soldier.InitStatsForTest(BuildUnitConfig());
            return soldier;
        }

        /// <summary>
        /// 构造刀兵配置快照（index=0, rangeCells=1, attackDamage=10, interval=1.5s）。
        /// </summary>
        private static UnitConfigSnapshot BuildUnitConfig()
        {
            return new UnitConfigSnapshot(
                index: 0,
                text: "刀",
                animationKey: "knife",
                rangeCells: 1f,
                attackDamage: 10,
                attackIntervalSeconds: 1.5f,
                damageMode: "melee",
                targetPolicy: "nearest");
        }

        // ====================================================================
        // 构造与默认值测试
        // ====================================================================

        [Test]
        [Description("构造后 SoldierBase 专属字段为默认值。")]
        public void Constructor_SoldierFieldsAreDefaults()
        {
            var soldier = new FakeSoldier();

            Assert.AreEqual(-1, soldier.Id, "Id 默认 -1（基类）。");
            Assert.IsFalse(soldier.IsActive, "IsActive 默认 false（基类）。");
            Assert.AreEqual(0, soldier.TargetCount, "Targets 列表默认空。");
            Assert.IsFalse(soldier.HasAttackEffectManager, "AttackEffectManager 默认 null。");
            Assert.IsFalse(soldier.HasEnemyManager, "EnemyManager 默认 null。");
            Assert.IsFalse(soldier.HasAttackResolver, "AttackResolver 默认 null。");
            Assert.AreEqual(0f, soldier.AttackRange, "AttackRange 默认 0。");
            Assert.AreEqual(1f, soldier.AttackIntervalSeconds, "AttackIntervalSeconds 默认 1。");
        }

        // ====================================================================
        // Configure null 拒绝测试
        // ====================================================================

        [Test]
        [Description("Configure 时 enemyManager 为 null 抛 ArgumentNullException。")]
        public void Configure_NullEnemyManager_Throws()
        {
            var (_, ar, aem) = BuildDependencies();
            var soldier = new FakeSoldier();
            // ReSharper disable once AssignNullToNotNullAttribute
            Assert.Throws<ArgumentNullException>(() =>
                soldier.ConfigureForTest(null, ar, aem));
        }

        [Test]
        [Description("Configure 时 attackResolver 为 null 抛 ArgumentNullException。")]
        public void Configure_NullAttackResolver_Throws()
        {
            var (em, _, aem) = BuildDependencies();
            var soldier = new FakeSoldier();
            // ReSharper disable once AssignNullToNotNullAttribute
            Assert.Throws<ArgumentNullException>(() =>
                soldier.ConfigureForTest(em, null, aem));
        }

        [Test]
        [Description("Configure 时 attackEffectManager 为 null 抛 ArgumentNullException。")]
        public void Configure_NullAttackEffectManager_Throws()
        {
            var (em, ar, _) = BuildDependencies();
            var soldier = new FakeSoldier();
            // ReSharper disable once AssignNullToNotNullAttribute
            Assert.Throws<ArgumentNullException>(() =>
                soldier.ConfigureForTest(em, ar, null));
        }

        // ====================================================================
        // Init 前 RequireConfigured 守卫测试
        // ====================================================================

        [Test]
        [Description("Init 前未 Configure 抛 InvalidOperationException。")]
        public void Init_BeforeConfigure_Throws()
        {
            var soldier = new FakeSoldier();
            Assert.Throws<InvalidOperationException>(() =>
                soldier.InitForTest("刀", true, UnitWidth, UnitHeight));
        }

        [Test]
        [Description("InitializeStats 前未 Configure 抛 InvalidOperationException。")]
        public void InitializeStats_BeforeConfigure_Throws()
        {
            var (_, ar, aem) = BuildDependencies();
            var soldier = new FakeSoldier();
            // 未 Configure，直接 InitStats。
            Assert.Throws<InvalidOperationException>(() =>
                soldier.InitStatsForTest(BuildUnitConfig()));
        }

        // ====================================================================
        // AssignRuntimeId 校验测试
        // ====================================================================

        [Test]
        [Description("AssignRuntimeId 拒绝非正 ID。")]
        public void AssignRuntimeId_InvalidThrows()
        {
            var soldier = new FakeSoldier();
            Assert.Throws<ArgumentOutOfRangeException>(() => soldier.AssignId(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => soldier.AssignId(-5));
        }

        // ====================================================================
        // Acquire 后字段值正确测试
        // ====================================================================

        [Test]
        [Description("Acquire（Configure + Init + InitializeStats）后攻击数值正确。")]
        public void Acquire_AfterFullInit_StatsCorrect()
        {
            var soldier = CreateConfiguredSoldier(id: 200, side: true, unitText: "刀");

            Assert.AreEqual(200, soldier.Id, "ID 已设置。");
            Assert.AreEqual(0, soldier.TargetCount, "Targets 列表为空。");
            Assert.IsTrue(soldier.HasEnemyManager, "EnemyManager 已注入。");
            Assert.IsTrue(soldier.HasAttackResolver, "AttackResolver 已注入。");
            Assert.IsTrue(soldier.HasAttackEffectManager, "AttackEffectManager 已注入。");
            // rangeCells=1, cellSize=80 → attackRange=80。
            Assert.AreEqual(80f, soldier.AttackRange, "AttackRange = rangeCells * cellSize = 80。");
            // attackIntervalSeconds=1.5, attackSpeedBonus=0 → 1.5。
            Assert.AreEqual(1.5f, soldier.AttackIntervalSeconds, "AttackIntervalSeconds = 1.5。");
        }

        // ====================================================================
        // Attack 守卫测试
        // ====================================================================

        [Test]
        [Description("Attack 在非活动状态下不调用 PerformAttack。")]
        public void Attack_NotActive_DoesNotCallPerformAttack()
        {
            var soldier = CreateConfiguredSoldier();
            // 未 ActivatePlacement，IsActive=false。
            Assert.IsFalse(soldier.IsActive, "尚未激活。");

            soldier.Attack();
            Assert.AreEqual(0, soldier.PerformAttackCallCount, "非活动状态不触发 PerformAttack。");
        }

        [Test]
        [Description("Attack 在活动状态下调用 PerformAttack。")]
        public void Attack_Active_CallsPerformAttack()
        {
            var soldier = CreateConfiguredSoldier();
            soldier.ActivateAt(0f, 0f);
            Assert.IsTrue(soldier.IsActive, "已激活。");

            soldier.Attack();
            Assert.AreEqual(1, soldier.PerformAttackCallCount, "活动状态触发 PerformAttack 一次。");
        }

        // ====================================================================
        // GameOver 幂等与攻击效果取消测试
        // ====================================================================

        [Test]
        [Description("GameOver 首次返回 true，重复调用返回 false（幂等）。")]
        public void GameOver_Idempotent()
        {
            var soldier = CreateConfiguredSoldier();
            soldier.ActivateAt(0f, 0f);

            Assert.IsTrue(soldier.GameOver(), "首次 GameOver 返回 true。");
            Assert.IsTrue(soldier.InPool, "已标记入池。");
            Assert.IsFalse(soldier.GameOver(), "重复 GameOver 返回 false。");
        }

        [Test]
        [Description("GameOver 后 IsActive=false、LastAttackTimeMs=0、目标列表已清空。")]
        public void GameOver_ClearsRuntimeState()
        {
            var soldier = CreateConfiguredSoldier();
            soldier.ActivateAt(100f, 100f);
            soldier.LastAttackTimeMs = 8000;
            soldier.SetState(AttackUnitState.Attack);

            soldier.GameOver();

            Assert.IsFalse(soldier.IsActive, "IsActive 已置 false。");
            Assert.AreEqual(0, soldier.LastAttackTimeMs, "LastAttackTimeMs 已清零。");
            Assert.AreEqual(AttackUnitState.Idle, soldier.CurrentState, "状态回到 Idle。");
            Assert.AreEqual(0, soldier.TargetCount, "目标列表已清空。");
        }

        // ====================================================================
        // ResetState 完整清空与无残留测试
        // ====================================================================

        [Test]
        [Description("ResetState 后无目标/冷却/注入依赖引用残留。")]
        public void ResetState_NoResidualReferences()
        {
            var soldier = CreateConfiguredSoldier();
            soldier.ActivateAt(200f, 200f);
            soldier.LastAttackTimeMs = 5000;
            soldier.SetState(AttackUnitState.Attack);

            (soldier as IPoolableBattleObject).ResetState();

            // 无目标残留。
            Assert.AreEqual(0, soldier.TargetCount, "Targets 列表已清空。");
            // 无冷却残留。
            Assert.AreEqual(0, soldier.LastAttackTimeMs, "LastAttackTimeMs 已清零。");
            // 无注入依赖引用残留。
            Assert.IsFalse(soldier.HasAttackEffectManager, "AttackEffectManager 引用已清除。");
            Assert.IsFalse(soldier.HasEnemyManager, "EnemyManager 引用已清除。");
            Assert.IsFalse(soldier.HasAttackResolver, "AttackResolver 引用已清除。");
            // 基类字段也已清空。
            Assert.AreEqual(-1, soldier.Id, "ID 已重置为 -1。");
            Assert.IsFalse(soldier.IsActive, "IsActive 已重置为 false。");
            Assert.IsFalse(soldier.InPool, "InPool 已重置为 false。");
            Assert.AreEqual(AttackUnitState.Idle, soldier.CurrentState, "CurrentState 已重置为 Idle。");
            Assert.AreEqual(0f, soldier.AttackRange, "AttackRange 已重置为 0。");
            Assert.AreEqual(1f, soldier.AttackIntervalSeconds, "AttackIntervalSeconds 已重置为 1。");
            Assert.AreEqual(-1, soldier.TestGridX, "GridX 已重置为 -1。");
            Assert.IsFalse(soldier.IsConfigured, "Configured 已重置为 false。");
        }

        [Test]
        [Description("Release（GameOver + ResetState）后全部字段清零/默认。")]
        public void Release_GameOverPlusReset_AllFieldsCleared()
        {
            var soldier = CreateConfiguredSoldier(id: 333, side: false);
            soldier.ActivateAt(300f, 300f);
            soldier.LastAttackTimeMs = 7777;
            soldier.SetState(AttackUnitState.Attack);

            // 模拟池 Release：先 GameOver 再 ResetState。
            soldier.GameOver();
            (soldier as IPoolableBattleObject).ResetState();

            Assert.AreEqual(-1, soldier.Id, "ID 已重置为 -1。");
            Assert.IsTrue(soldier.Side, "Side 已重置为 true。");
            Assert.IsFalse(soldier.IsActive, "IsActive 已重置为 false。");
            Assert.IsFalse(soldier.InPool, "InPool 已重置为 false。");
            Assert.AreEqual(AttackUnitState.Idle, soldier.CurrentState, "CurrentState 已重置为 Idle。");
            Assert.AreEqual(0, soldier.LastAttackTimeMs, "LastAttackTimeMs 已重置为 0。");
            Assert.AreEqual(0, soldier.TargetCount, "Targets 已清空。");
            Assert.IsFalse(soldier.HasAttackEffectManager, "AttackEffectManager 已清除。");
            Assert.IsFalse(soldier.HasEnemyManager, "EnemyManager 已清除。");
            Assert.IsFalse(soldier.HasAttackResolver, "AttackResolver 已清除。");
            Assert.AreEqual(0f, soldier.AttackRange, "AttackRange 已重置为 0。");
            Assert.AreEqual(1f, soldier.AttackIntervalSeconds, "AttackIntervalSeconds 已重置为 1。");
            Assert.AreEqual(-1, soldier.TestGridX, "GridX 已重置为 -1。");
            Assert.IsFalse(soldier.IsConfigured, "Configured 已重置为 false。");
        }

        [Test]
        [Description("ResetState 幂等：多次调用结果状态相同。")]
        public void ResetState_Idempotent()
        {
            var soldier = CreateConfiguredSoldier();
            soldier.ActivateAt(100f, 100f);
            soldier.LastAttackTimeMs = 9999;

            (soldier as IPoolableBattleObject).ResetState();
            int targetsAfterFirst = soldier.TargetCount;
            long cooldownAfterFirst = soldier.LastAttackTimeMs;
            bool hasEffectMgrAfterFirst = soldier.HasAttackEffectManager;

            (soldier as IPoolableBattleObject).ResetState();
            Assert.AreEqual(targetsAfterFirst, soldier.TargetCount, "多次 Reset TargetCount 相同。");
            Assert.AreEqual(cooldownAfterFirst, soldier.LastAttackTimeMs, "多次 Reset 冷却相同。");
            Assert.AreEqual(hasEffectMgrAfterFirst, soldier.HasAttackEffectManager, "多次 Reset 依赖引用相同。");

            // 第三次仍安全。
            (soldier as IPoolableBattleObject).ResetState();
            Assert.AreEqual(targetsAfterFirst, soldier.TargetCount, "三次 Reset 仍相同。");
        }

        // ====================================================================
        // 池复用无污染测试
        // ====================================================================

        [Test]
        [Description("池复用无污染：Acquire → 配置 → Release → 复用 → 旧状态不残留。")]
        public void PoolReuse_NoPollution()
        {
            var pool = new BattleObjectPool<FakeSoldier>(() => new FakeSoldier());
            var (em, ar, aem) = BuildDependencies();

            // 第一轮：Acquire → 配置 → 污染 → Release。
            FakeSoldier first = pool.Acquire();
            first.ConfigureForTest(em, ar, aem);
            first.AssignId(999);
            first.InitForTest("枪", false, UnitWidth, UnitHeight);
            first.InitStatsForTest(BuildUnitConfig());
            first.PlaceAt(4, 5);
            first.ActivateAt(320f, 400f);
            first.LastAttackTimeMs = 6666;
            first.SetState(AttackUnitState.Attack);

            pool.Release(first);

            // 验证 Release 后已 Reset。
            Assert.AreEqual(-1, first.Id, "Release 后 ID=-1。");
            Assert.AreEqual(0, first.TargetCount, "Release 后 TargetCount=0。");
            Assert.IsFalse(first.HasAttackEffectManager, "Release 后 AttackEffectManager=null。");
            Assert.IsFalse(first.HasEnemyManager, "Release 后 EnemyManager=null。");

            // 第二轮：Acquire 应复用同一对象，状态已清空。
            FakeSoldier second = pool.Acquire();
            Assert.AreSame(first, second, "LIFO 复用同一对象。");
            Assert.AreEqual(-1, second.Id, "复用对象 ID=-1（无旧 ID 残留）。");
            Assert.IsFalse(second.IsActive, "复用对象 IsActive=false。");
            Assert.AreEqual(0, second.TargetCount, "复用对象 TargetCount=0（无旧目标残留）。");
            Assert.AreEqual(0, second.LastAttackTimeMs, "复用对象 LastAttackTimeMs=0（无旧冷却残留）。");
            Assert.IsFalse(second.HasAttackEffectManager, "复用对象无 AttackEffectManager（无旧引用残留）。");
            Assert.IsFalse(second.HasEnemyManager, "复用对象无 EnemyManager。");
            Assert.IsFalse(second.HasAttackResolver, "复用对象无 AttackResolver。");
            Assert.AreEqual(0f, second.AttackRange, "复用对象 AttackRange=0（无旧数值残留）。");
            Assert.AreEqual(1f, second.AttackIntervalSeconds, "复用对象 AttackIntervalSeconds=1（默认值）。");
            Assert.AreEqual(-1, second.TestGridX, "复用对象 GridX=-1（无旧坐标残留）。");
            Assert.IsFalse(second.IsConfigured, "复用对象 Configured=false（需重新 Configure）。");

            pool.Release(second);
        }

        // ====================================================================
        // 不持有 Unity GameObject 验证
        // ====================================================================

        [Test]
        [Description("SoldierBase 不继承 UnityEngine.MonoBehaviour 或引用 UnityEngine 组件。")]
        public void SoldierBase_DoesNotReferenceUnityEngine()
        {
            Type type = typeof(SoldierBase);
            Assert.IsFalse(type.IsSubclassOf(typeof(UnityEngine.MonoBehaviour)),
                "SoldierBase 不继承 MonoBehaviour。");
            Assert.IsFalse(type.IsSubclassOf(typeof(UnityEngine.ScriptableObject)),
                "SoldierBase 不继承 ScriptableObject。");
        }
    }
}
