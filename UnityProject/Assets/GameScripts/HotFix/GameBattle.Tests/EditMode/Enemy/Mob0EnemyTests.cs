using System;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Enemy
{
    // ============================================================================
    // 任务 4.4：Mob0Enemy 单元测试
    // ----------------------------------------------------------------------------
    // 验证要求（task 4.4）：
    //   1. 数值初始化：InitializeStats 从 Mob0EnemyInitStats 设置 BaseMoveSpeed/MaxHealthBase。
    //   2. 死亡表现边界：BeginDeath 置 _deathPresentationStarted，幂等；
    //      OnDeathPresentationCompleted 置 _deathPresentationCompleted 并触发 GameOver。
    //   3. 动画清理：ResetState 清除 Mob0 专属字段（_stats/_deathPresentationStarted/
    //      _deathPresentationCompleted/_deathScheduled）。
    //   4. 池复用无污染：ResetState 后等价于新构造，InitializeStats 可重新设置。
    //   5. 不移植灵魂投射/吹飞：验证无相关字段或方法。
    //
    // design.md:175 "NormalEnemyBase 本期合入 Mob0Enemy"：
    //   只有一个普通敌人类型，不为未证明的第二适配器保留浅层继承 seam；
    //   灵魂投射和吹飞能力不移植。
    //
    // spec battle-simulation "Update phases are explicit and single-owned"：
    //   伤害、死亡事实、奖励和胜负候选 MUST 在其发生点同步生效。
    //
    // EnemyBase 契约（task 4.3/55 产物 EnemyBase.cs 实际签名）：
    //   - protected virtual void BeginDeath()：Mob0Enemy protected override。
    //   - public virtual void ResetState()：Mob0Enemy public override。
    //   - public virtual bool GameOver()：OnDeathPresentationCompleted 触发。
    //   - protected int MaxHealthBase { get; set; }：由 InitializeStats 设置。
    //   - protected int CurrentHealth（只读）：由 EnemyBase.Init 设置，Mob0Enemy 不直接写。
    //   - protected int BaseMoveSpeed { get; set; }：由 InitializeStats 设置。
    //   - internal bool InPool：OnDeathPresentationCompleted 守卫使用。
    //   - public int Id：运行时 ID，Mob0Enemy 通过 RuntimeId 属性委托暴露。
    //   - protected internal void AssignRuntimeId(int)：设置运行时 ID。
    //
    // EnemyFactory 契约（task 4.5 EnemyFactoryTests 已引用）：
    //   - internal Mob0Enemy()：无参构造。
    //   - internal void AssignRuntimeId(int id)：设置运行时 ID（委托 EnemyBase.AssignRuntimeId）。
    //   - internal int RuntimeId { get; }：只读属性，委托 EnemyBase.Id。
    //   - void ResetState()：IPoolableBattleObject，清除 Id 置 0（由 EnemyBase.ResetState）。
    // ============================================================================

    /// <summary>
    /// Mob0Enemy 数值初始化、死亡表现边界、动画清理与池复用测试（task 4.4）。
    /// </summary>
    /// <remarks>
    /// <para>验证覆盖：</para>
    /// <list type="bullet">
    /// <item>数值初始化：InitializeStats 设置 BaseMoveSpeed/MaxHealthBase/GetInitialHealth。</item>
    /// <item>死亡表现边界：BeginDeath 幂等，OnDeathPresentationCompleted 触发 GameOver。</item>
    /// <item>动画清理：ResetState 清除 Mob0 专属字段，池复用无污染。</item>
    /// <item>EnemyFactory 契约：RuntimeId/AssignRuntimeId/ResetState 清除 Id。</item>
    /// <item>不移植验证：无灵魂投射/吹飞相关 API。</item>
    /// </list>
    /// <para>本测试不接触 Scene、FUI 或资源加载，符合纯逻辑 EditMode 约束（task 2.2）。</para>
    /// <para><b>CurrentHealth 说明：</b>EnemyBase.CurrentHealth 为只读属性（无 protected setter），
    /// 由 EnemyBase.Init 在出生时设置。本测试不直接验证 CurrentHealth，而是验证
    /// MaxHealthBase 与 GetInitialHealth，后者由调用方传给 Init 完成血量初始化。</para>
    /// </remarks>
    [TestFixture]
    internal class Mob0EnemyTests
    {
        // ====================================================================
        // 测试夹具创建
        // ====================================================================

        /// <summary>
        /// 黄金基线 healthByWave（对应 BattleDataCore.js:17 normalEnemyHealthByWave）。
        /// </summary>
        private static readonly int[] GoldenHealthByWave =
        {
            10, 11, 57, 44, 39, 92, 138, 200, 291, 421,
            611, 886, 1285, 1863, 2701, 3917, 5680, 8235, 11941, 17315,
        };

        /// <summary>
        /// 创建黄金基线 Mob0EnemyInitStats（对应 EnemyStats 表 Mob0 数值）。
        /// </summary>
        private static Mob0EnemyInitStats CreateGoldenStats()
        {
            return new Mob0EnemyInitStats(
                healthByWave: GoldenHealthByWave,
                speed: 50,
                contactDamage: 1,
                rewardGold: 1);
        }

        /// <summary>
        /// 创建一个已初始化数值的 Mob0Enemy。
        /// </summary>
        private static Mob0Enemy CreateInitializedEnemy()
        {
            var enemy = new Mob0Enemy();
            enemy.InitializeStats(CreateGoldenStats());
            return enemy;
        }

        // ====================================================================
        // 数值初始化测试（NormalEnemyBase.js:51-68 _initializeStatsAndAnimation 合入）
        // ====================================================================

        [Test]
        [Description("InitializeStats 设置 BaseMoveSpeed 为 stats.Speed。")]
        public void InitializeStats_SetsBaseMoveSpeed_ToStatsSpeed()
        {
            Mob0Enemy enemy = CreateInitializedEnemy();

            Assert.AreEqual(50, enemy.BaseMoveSpeedValue,
                "BaseMoveSpeed = stats.Speed = 50。");
        }

        [Test]
        [Description("InitializeStats 设置 MaxHealthBase 为 healthByWave[0]。")]
        public void InitializeStats_SetsMaxHealthBase_ToFirstWaveHealth()
        {
            Mob0Enemy enemy = CreateInitializedEnemy();

            Assert.AreEqual(10, enemy.MaxHealthBaseValue,
                "MaxHealthBase = healthByWave[0] = 10。");
        }

        [Test]
        [Description("GetInitialHealth 返回 healthByWave[0]，供调用方传给 EnemyBase.Init。")]
        public void GetInitialHealth_ReturnsFirstWaveHealth()
        {
            Mob0Enemy enemy = CreateInitializedEnemy();

            Assert.AreEqual(10, enemy.GetInitialHealth(),
                "GetInitialHealth = healthByWave[0] = 10。");
        }

        [Test]
        [Description("InitializeStats 后 Stats 属性返回注入的数值快照。")]
        public void InitializeStats_StatsProperty_ReturnsInjectedSnapshot()
        {
            Mob0EnemyInitStats stats = CreateGoldenStats();
            var enemy = new Mob0Enemy();
            enemy.InitializeStats(stats);

            Assert.IsTrue(enemy.Stats.HasValue, "Stats 有值。");
            Assert.AreEqual(50, enemy.Stats.Value.Speed, "Speed=50。");
            Assert.AreEqual(1, enemy.Stats.Value.ContactDamage, "ContactDamage=1。");
            Assert.AreEqual(1, enemy.Stats.Value.RewardGold, "RewardGold=1。");
        }

        [Test]
        [Description("未初始化时 Stats 为 null，GetInitialHealth 返回 0。")]
        public void Stats_BeforeInitialize_IsNull_GetInitialHealthZero()
        {
            var enemy = new Mob0Enemy();

            Assert.IsFalse(enemy.Stats.HasValue, "未初始化时 Stats 为 null。");
            Assert.AreEqual(0, enemy.GetInitialHealth(), "未初始化时 GetInitialHealth=0。");
        }

        [Test]
        [Description("InitializeStats 接收空 healthByWave 时 MaxHealthBase=0，不抛异常。")]
        public void InitializeStats_EmptyHealthByWave_MaxHealthZero_NoThrow()
        {
            var enemy = new Mob0Enemy();
            var stats = new Mob0EnemyInitStats(
                healthByWave: Array.Empty<int>(),
                speed: 50,
                contactDamage: 1,
                rewardGold: 1);

            Assert.DoesNotThrow(() => enemy.InitializeStats(stats));
            Assert.AreEqual(0, enemy.MaxHealthBaseValue, "空 healthByWave 时 MaxHealthBase=0。");
            Assert.AreEqual(0, enemy.GetInitialHealth(), "空 healthByWave 时 GetInitialHealth=0。");
        }

        // ====================================================================
        // 死亡表现边界测试（NormalEnemyBase.js:82-97 beginDeath 合入）
        // ====================================================================

        [Test]
        [Description("BeginDeath 置 IsDeathPresentationStarted=true。")]
        public void BeginDeath_SetsIsDeathPresentationStarted_True()
        {
            Mob0Enemy enemy = CreateInitializedEnemy();

            Assert.IsFalse(enemy.IsDeathPresentationStarted,
                "初始 IsDeathPresentationStarted=false。");
            enemy.BeginDeath();

            Assert.IsTrue(enemy.IsDeathPresentationStarted,
                "BeginDeath 后 IsDeathPresentationStarted=true。");
        }

        [Test]
        [Description("BeginDeath 置 IsDeathScheduled=true（已请求表现层播放死亡动画）。")]
        public void BeginDeath_SetsIsDeathScheduled_True()
        {
            Mob0Enemy enemy = CreateInitializedEnemy();

            Assert.IsFalse(enemy.IsDeathScheduled, "初始 IsDeathScheduled=false。");
            enemy.BeginDeath();

            Assert.IsTrue(enemy.IsDeathScheduled, "BeginDeath 后 IsDeathScheduled=true。");
        }

        [Test]
        [Description("BeginDeath 幂等：重复调用不再次触发死亡表现（对应 js:83 守卫）。")]
        public void BeginDeath_Idempotent_RepeatCallNoEffect()
        {
            Mob0Enemy enemy = CreateInitializedEnemy();
            enemy.BeginDeath();
            bool firstScheduled = enemy.IsDeathScheduled;

            enemy.BeginDeath();

            Assert.IsTrue(enemy.IsDeathPresentationStarted,
                "IsDeathPresentationStarted 仍为 true。");
            Assert.AreEqual(firstScheduled, enemy.IsDeathScheduled,
                "重复 BeginDeath 不改变 IsDeathScheduled。");
        }

        [Test]
        [Description("OnDeathPresentationCompleted 置 IsDeathPresentationCompleted=true。")]
        public void OnDeathPresentationCompleted_SetsCompleted_True()
        {
            Mob0Enemy enemy = CreateInitializedEnemy();
            enemy.BeginDeath();

            enemy.OnDeathPresentationCompleted();

            Assert.IsTrue(enemy.IsDeathPresentationCompleted,
                "OnDeathPresentationCompleted 后 IsDeathPresentationCompleted=true。");
            Assert.IsFalse(enemy.IsDeathScheduled,
                "完成后 IsDeathScheduled=false（已清除调度标记）。");
        }

        [Test]
        [Description("OnDeathPresentationCompleted 未先调 BeginDeath 时为空操作。")]
        public void OnDeathPresentationCompleted_WithoutBeginDeath_NoEffect()
        {
            Mob0Enemy enemy = CreateInitializedEnemy();

            enemy.OnDeathPresentationCompleted();

            Assert.IsFalse(enemy.IsDeathPresentationCompleted,
                "未 BeginDeath 时 OnDeathPresentationCompleted 无效。");
        }

        // ====================================================================
        // 动画清理与 ResetState 测试（NormalEnemyBase.js:204-230 gameOver 合入）
        // ====================================================================

        [Test]
        [Description("ResetState 清除 Mob0 专属字段：_stats 置 null。")]
        public void ResetState_ClearsStats_ToNull()
        {
            Mob0Enemy enemy = CreateInitializedEnemy();
            Assert.IsTrue(enemy.Stats.HasValue, "初始化后 Stats 有值。");

            enemy.ResetState();

            Assert.IsFalse(enemy.Stats.HasValue, "ResetState 后 Stats=null。");
        }

        [Test]
        [Description("ResetState 清除死亡表现边界标记。")]
        public void ResetState_ClearsDeathBoundaryFlags()
        {
            Mob0Enemy enemy = CreateInitializedEnemy();
            enemy.BeginDeath();
            Assert.IsTrue(enemy.IsDeathPresentationStarted,
                "BeginDeath 后 IsDeathPresentationStarted=true。");

            enemy.ResetState();

            Assert.IsFalse(enemy.IsDeathPresentationStarted,
                "ResetState 后 IsDeathPresentationStarted=false。");
            Assert.IsFalse(enemy.IsDeathScheduled,
                "ResetState 后 IsDeathScheduled=false。");
            Assert.IsFalse(enemy.IsDeathPresentationCompleted,
                "ResetState 后 IsDeathPresentationCompleted=false。");
        }

        [Test]
        [Description("ResetState 幂等：多次调用结果状态相同。")]
        public void ResetState_Idempotent_MultipleCallsSameState()
        {
            Mob0Enemy enemy = CreateInitializedEnemy();
            enemy.BeginDeath();

            enemy.ResetState();
            enemy.ResetState();

            Assert.IsFalse(enemy.Stats.HasValue, "多次 ResetState 后 Stats=null。");
            Assert.IsFalse(enemy.IsDeathPresentationStarted,
                "多次 ResetState 后 IsDeathPresentationStarted=false。");
        }

        // ====================================================================
        // 池复用无污染测试（enemy-pool-reset-contract.md）
        // ====================================================================

        [Test]
        [Description("池复用无污染：ResetState 后 InitializeStats 可重新设置不同数值。")]
        public void PoolReuse_ResetThenReinitialize_NoContamination()
        {
            var enemy = new Mob0Enemy();
            var firstStats = new Mob0EnemyInitStats(
                healthByWave: new[] { 10, 11 },
                speed: 50,
                contactDamage: 1,
                rewardGold: 1);
            enemy.InitializeStats(firstStats);
            enemy.BeginDeath();

            enemy.ResetState();

            // 用不同数值重新初始化，验证旧状态不残留。
            var secondStats = new Mob0EnemyInitStats(
                healthByWave: new[] { 100, 200 },
                speed: 75,
                contactDamage: 2,
                rewardGold: 3);
            enemy.InitializeStats(secondStats);

            Assert.AreEqual(100, enemy.MaxHealthBaseValue, "MaxHealthBase=100（新 healthByWave[0]）。");
            Assert.AreEqual(75, enemy.BaseMoveSpeedValue, "BaseMoveSpeed=75。");
            Assert.IsFalse(enemy.IsDeathPresentationStarted, "死亡标记已清除，不残留。");
            Assert.AreEqual(3, enemy.Stats.Value.RewardGold, "RewardGold=3（新值）。");
        }

        [Test]
        [Description("池复用无污染：ResetState 后死亡表现边界可重新触发。")]
        public void PoolReuse_ResetThenBeginDeath_Retriggerable()
        {
            Mob0Enemy enemy = CreateInitializedEnemy();
            enemy.BeginDeath();
            enemy.OnDeathPresentationCompleted();
            Assert.IsTrue(enemy.IsDeathPresentationCompleted, "第一轮死亡已完成。");

            enemy.ResetState();

            // 重新初始化并触发第二轮死亡。
            enemy.InitializeStats(CreateGoldenStats());
            enemy.BeginDeath();

            Assert.IsTrue(enemy.IsDeathPresentationStarted,
                "第二轮 BeginDeath 后 IsDeathPresentationStarted=true。");
            Assert.IsFalse(enemy.IsDeathPresentationCompleted,
                "第二轮 IsDeathPresentationCompleted=false（已清除）。");
        }

        // ====================================================================
        // EnemyFactory 契约测试（task 4.5 EnemyFactoryTests 依赖）
        // ====================================================================

        [Test]
        [Description("无参构造可供 BattleObjectPool 工厂委托使用（task 4.5 契约）。")]
        public void Constructor_Parameterless_ForPoolFactory()
        {
            var enemy = new Mob0Enemy();

            Assert.IsNotNull(enemy, "无参构造成功。");
            Assert.AreEqual(0, enemy.RuntimeId, "初始 RuntimeId=0（Id 未分配）。");
        }

        [Test]
        [Description("AssignRuntimeId 设置 RuntimeId（task 4.5 契约，委托 EnemyBase.AssignRuntimeId）。")]
        public void AssignRuntimeId_SetsRuntimeId()
        {
            var enemy = new Mob0Enemy();

            enemy.AssignRuntimeId(42);

            Assert.AreEqual(42, enemy.RuntimeId, "AssignRuntimeId(42) 后 RuntimeId=42。");
        }

        [Test]
        [Description("AssignRuntimeId 传 0 或负数抛异常（EnemyBase 契约：ID 必须 > 0）。")]
        public void AssignRuntimeId_NonPositive_Throws()
        {
            var enemy = new Mob0Enemy();

            Assert.Throws<ArgumentOutOfRangeException>(() => enemy.AssignRuntimeId(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => enemy.AssignRuntimeId(-1));
        }

        [Test]
        [Description("ResetState 清除 RuntimeId 置 0（task 4.5 契约：Release 后旧 ID 失效）。")]
        public void ResetState_ClearsRuntimeId_ToZero()
        {
            var enemy = new Mob0Enemy();
            enemy.AssignRuntimeId(99);
            Assert.AreEqual(99, enemy.RuntimeId, "AssignRuntimeId(99) 后 RuntimeId=99。");

            enemy.ResetState();

            Assert.AreEqual(0, enemy.RuntimeId,
                "ResetState 后 RuntimeId=0（旧 ID 失效，EnemyBase.ResetToDefaults 清除 _id）。");
        }

        // ====================================================================
        // 不移植验证（design.md:316 灵魂投射/吹飞不移植）
        // ====================================================================

        [Test]
        [Description("Mob0Enemy 不暴露灵魂投射 API（design.md:316 不移植）。")]
        public void NoSoulProjection_API_NotPresent()
        {
            // 灵魂投射方法 _tryDeliverSoul / sB 不存在于 Mob0Enemy 公开 API。
            // 通过反射验证无相关方法（仅检查 internal/public 方法名）。
            var type = typeof(Mob0Enemy);
            var methods = type.GetMethods(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic);

            foreach (var method in methods)
            {
                string name = method.Name;
                Assert.IsFalse(
                    name.Contains("Soul") || name.Contains("soul") ||
                    name.Contains("DeliverSoul") || name.Contains("tryDeliverSoul"),
                    $"Mob0Enemy 不应包含灵魂投射方法 {name}（design.md:316 不移植）。");
            }
        }

        [Test]
        [Description("Mob0Enemy 不暴露吹飞 API（design.md:316 不移植）。")]
        public void NoBlowUp_API_NotPresent()
        {
            // 吹飞方法 Xw / Gw / blowUpCurve / blowUpState 不存在于 Mob0Enemy。
            var type = typeof(Mob0Enemy);
            var methods = type.GetMethods(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic);

            foreach (var method in methods)
            {
                string name = method.Name;
                Assert.IsFalse(
                    name.Contains("BlowUp") || name.Contains("blowUp") ||
                    name.Equals("Xw") || name.Equals("Gw"),
                    $"Mob0Enemy 不应包含吹飞方法 {name}（design.md:316 不移植）。");
            }

            // 验证无吹飞相关字段。
            var fields = type.GetFields(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic);

            foreach (var field in fields)
            {
                string name = field.Name;
                Assert.IsFalse(
                    name.Contains("BlowUp") || name.Contains("blowUp"),
                    $"Mob0Enemy 不应包含吹飞字段 {name}（design.md:316 不移植）。");
            }
        }

        // ====================================================================
        // Mob0EnemyInitStats 构造校验
        // ====================================================================

        [Test]
        [Description("Mob0EnemyInitStats 构造时 healthByWave 为 null 抛 ArgumentNullException。")]
        public void Mob0EnemyInitStats_NullHealthByWave_Throws()
        {
            // ReSharper disable once AssignNullToNotNullAttribute
            Assert.Throws<ArgumentNullException>(
                () => new Mob0EnemyInitStats(null, 50, 1, 1));
        }
    }
}
