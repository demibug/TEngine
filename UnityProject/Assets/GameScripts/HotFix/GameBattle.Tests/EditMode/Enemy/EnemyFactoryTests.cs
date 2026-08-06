using System;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Enemy
{
    /// <summary>
    /// EnemyFactory 单元测试（task 4.5）。
    /// </summary>
    /// <remarks>
    /// <para>验证 task 4.5 的全部关键要求：</para>
    /// <list type="bullet">
    /// <item>只注册 Mob0：Acquire 只返回 Mob0Enemy，不提供其他类型注册。</item>
    /// <item>每次 Acquire 后分配新运行时 ID：通过 RuntimeIdAllocator，从 1 单调递增。</item>
    /// <item>Release 后旧 ID/目标引用不得继续有效：ResetState 清除旧 ID。</item>
    /// <item>池复用不复用旧 ID：Acquire 复用对象后分配新 ID，与旧 ID 不同。</item>
    /// <item>Acquire/Release 对称：每次 Acquire 对应恰好一次 Release。</item>
    /// <item>重复 Release 安全：已归还对象再次 Release 返回 false。</item>
    /// </list>
    ///
    /// <para><b>Mob0Enemy 契约依赖（并行开发推断）：</b>
    /// 本测试引用 <see cref="Mob0Enemy"/> 类型，其 API 契约基于 design.md 目录表
    /// （"Enemy/Mob0Enemy.cs | 本期唯一敌人类型"）和还原工程 EnemyBase.js:200
    /// （<c>this.id = this.gameData.allocateRuntimeId()</c>）推断：</para>
    /// <list type="bullet">
    /// <item><c>internal Mob0Enemy()</c>：无参构造，供 BattleObjectPool 工厂委托使用。</item>
    /// <item><c>internal void AssignRuntimeId(int id)</c>：设置运行时 ID，由 EnemyFactory.Acquire 调用。</item>
    /// <item><c>internal int RuntimeId { get; }</c>：只读属性，供测试验证 ID 分配。</item>
    /// <item><c>void ResetState()</c>：IPoolableBattleObject 实现，清除 RuntimeId（置 0）等全部可变状态。</item>
    /// </list>
    /// <para>若 task 55/56 的 Mob0Enemy 实现与此契约不一致，集成时需对齐。</para>
    /// </remarks>
    [TestFixture]
    internal class EnemyFactoryTests
    {
        // ====================================================================
        // 测试夹具创建
        // ====================================================================

        /// <summary>
        /// 创建一个可供测试的 EnemyFactory 实例，绑定独立的 RuntimeIdAllocator
        /// 和 BattleObjectPool&lt;Mob0Enemy&gt;。
        /// </summary>
        /// <param name="pool">输出的池实例，供测试观察 ActiveCount/FreeCount。</param>
        /// <returns>新创建的 EnemyFactory。</returns>
        private static EnemyFactory CreateFactory(out BattleObjectPool<Mob0Enemy> pool)
        {
            var idAllocator = new RuntimeIdAllocator();
            pool = new BattleObjectPool<Mob0Enemy>(() => new Mob0Enemy());
            return new EnemyFactory(idAllocator, pool);
        }

        // ====================================================================
        // 构造校验测试
        // ====================================================================

        [Test]
        [Description("构造时 idAllocator 为 null 抛 ArgumentNullException。")]
        public void Constructor_NullIdAllocator_Throws()
        {
            var pool = new BattleObjectPool<Mob0Enemy>(() => new Mob0Enemy());
            // ReSharper disable once AssignNullToNotNullAttribute
            Assert.Throws<ArgumentNullException>(
                () => new EnemyFactory(null, pool));
        }

        [Test]
        [Description("构造时 mob0Pool 为 null 抛 ArgumentNullException。")]
        public void Constructor_NullPool_Throws()
        {
            var idAllocator = new RuntimeIdAllocator();
            // ReSharper disable once AssignNullToNotNullAttribute
            Assert.Throws<ArgumentNullException>(
                () => new EnemyFactory(idAllocator, null));
        }

        // ====================================================================
        // 只注册 Mob0 测试
        // ====================================================================

        [Test]
        [Description("Acquire 返回 Mob0Enemy 实例（只注册 Mob0，不提供其他类型）。")]
        public void Acquire_ReturnsMob0Enemy_OnlyMob0Registered()
        {
            EnemyFactory factory = CreateFactory(out _);

            Mob0Enemy enemy = factory.Acquire();

            Assert.IsNotNull(enemy, "Acquire 返回非 null。");
            Assert.IsInstanceOf<Mob0Enemy>(enemy, "Acquire 返回 Mob0Enemy 实例。");

            // 清理。
            factory.Release(enemy);
        }

        // ====================================================================
        // 每次 Acquire 后分配新运行时 ID 测试
        // ====================================================================

        [Test]
        [Description("Acquire 后分配新运行时 ID（从 1 开始）。")]
        public void Acquire_AssignsNewRuntimeId_StartsFromOne()
        {
            EnemyFactory factory = CreateFactory(out _);

            Mob0Enemy enemy = factory.Acquire();

            Assert.AreEqual(1, enemy.RuntimeId, "首次 Acquire 分配的 RuntimeId=1。");

            factory.Release(enemy);
        }

        [Test]
        [Description("多次 Acquire 分配单调递增的运行时 ID。")]
        public void Acquire_MultipleTimes_RuntimeIdsMonotonicallyIncreasing()
        {
            EnemyFactory factory = CreateFactory(out BattleObjectPool<Mob0Enemy> pool);

            // Acquire 3 个（不 Release，保持活动）。
            Mob0Enemy a = factory.Acquire();
            Mob0Enemy b = factory.Acquire();
            Mob0Enemy c = factory.Acquire();

            Assert.AreEqual(1, a.RuntimeId, "第一个 RuntimeId=1。");
            Assert.AreEqual(2, b.RuntimeId, "第二个 RuntimeId=2。");
            Assert.AreEqual(3, c.RuntimeId, "第三个 RuntimeId=3。");
            Assert.AreEqual(3, pool.ActiveCount, "3 个活动租借。");

            // 清理。
            factory.Release(a);
            factory.Release(b);
            factory.Release(c);
        }

        // ====================================================================
        // Release 后旧 ID 失效测试
        // ====================================================================

        [Test]
        [Description("Release 后旧 RuntimeId 被 ResetState 清除（置 0），旧 ID 不再有效。")]
        public void Release_OldRuntimeId_ClearedByResetState()
        {
            EnemyFactory factory = CreateFactory(out _);

            Mob0Enemy enemy = factory.Acquire();
            int oldId = enemy.RuntimeId;
            Assert.Greater(oldId, 0, "Acquire 后 RuntimeId > 0。");

            factory.Release(enemy);

            // ResetState 由 BattleObjectPool.Release 在入池前调用，清除 RuntimeId。
            // 旧 ID 不得继续有效（task 4.5）。
            Assert.AreEqual(0, enemy.RuntimeId, "Release 后 RuntimeId=0（ResetState 清除）。",
                "若 Mob0Enemy.ResetState 未清除 RuntimeId，本测试失败。Mob0Enemy 需在 ResetState 中将 RuntimeId 置 0。");
        }

        [Test]
        [Description("Release 后通过旧引用观察到的 RuntimeId 为 0，旧 ID 不得继续有效。")]
        public void Release_OldReference_RuntimeIdZero_OldIdInvalid()
        {
            EnemyFactory factory = CreateFactory(out _);

            Mob0Enemy enemy = factory.Acquire();
            int oldId = enemy.RuntimeId;

            factory.Release(enemy);

            // 旧引用仍存在，但 RuntimeId 已被 ResetState 清除。
            // 这验证了 "Release 后旧 ID 不得继续有效" 的契约。
            Assert.AreNotEqual(oldId, enemy.RuntimeId,
                "Release 后旧引用的 RuntimeId 与旧 ID 不同（已被清除）。");
            Assert.AreEqual(0, enemy.RuntimeId,
                "Release 后旧引用的 RuntimeId=0（旧 ID 失效）。");
        }

        // ====================================================================
        // 池复用不复用旧 ID 测试
        // ====================================================================

        [Test]
        [Description("Acquire 复用已 Release 的对象，但分配新 RuntimeId（不复用旧 ID）。")]
        public void Acquire_AfterRelease_ReusesObject_NewRuntimeId()
        {
            EnemyFactory factory = CreateFactory(out BattleObjectPool<Mob0Enemy> pool);

            // 第一轮：Acquire -> 记录旧 ID -> Release。
            Mob0Enemy first = factory.Acquire();
            int firstId = first.RuntimeId;
            Assert.AreEqual(1, firstId, "首次 Acquire RuntimeId=1。");

            factory.Release(first);
            Assert.AreEqual(1, pool.FreeCount, "Release 后 FreeCount=1。");

            // 第二轮：Acquire 应复用同一对象（LIFO），但分配新 ID。
            Mob0Enemy second = factory.Acquire();
            Assert.AreSame(first, second, "复用同一对象实例（LIFO）。");
            Assert.AreEqual(2, second.RuntimeId, "复用对象的新 RuntimeId=2（不复用旧 ID=1）。");
            Assert.AreNotEqual(firstId, second.RuntimeId, "新 ID 与旧 ID 不同。");

            factory.Release(second);
        }

        [Test]
        [Description("多轮 Acquire/Release：每轮复用对象但分配新 RuntimeId，ID 单调递增。")]
        public void MultipleRounds_ReuseObject_NewIdsMonotonicallyIncreasing()
        {
            EnemyFactory factory = CreateFactory(out _);

            int expectedId = 1;
            Mob0Enemy reused = null;

            for (int round = 0; round < 5; round++)
            {
                Mob0Enemy enemy = factory.Acquire();

                if (round == 0)
                {
                    reused = enemy;
                }
                else
                {
                    // 从第二轮起应复用同一对象（LIFO，每次只有 1 个空闲）。
                    Assert.AreSame(reused, enemy,
                        $"第 {round} 轮复用同一对象实例。");
                }

                Assert.AreEqual(expectedId, enemy.RuntimeId,
                    $"第 {round} 轮 RuntimeId={expectedId}（单调递增，不复用旧 ID）。");

                factory.Release(enemy);
                expectedId++;
            }
        }

        // ====================================================================
        // Acquire/Release 对称性测试
        // ====================================================================

        [Test]
        [Description("Acquire/Release 对称：Release 后池 ActiveCount 归零。")]
        public void AcquireRelease_Symmetric_ActiveCountZero()
        {
            EnemyFactory factory = CreateFactory(out BattleObjectPool<Mob0Enemy> pool);

            Assert.AreEqual(0, pool.ActiveCount, "初始 ActiveCount=0。");

            Mob0Enemy enemy = factory.Acquire();
            Assert.AreEqual(1, pool.ActiveCount, "Acquire 后 ActiveCount=1。");

            Assert.IsTrue(factory.Release(enemy), "Release 返回 true。");
            Assert.AreEqual(0, pool.ActiveCount, "Release 后 ActiveCount=0。");
        }

        [Test]
        [Description("多个 Acquire/Release 对称：全部归还后 ActiveCount=0。")]
        public void MultipleAcquireRelease_AllSymmetric()
        {
            EnemyFactory factory = CreateFactory(out BattleObjectPool<Mob0Enemy> pool);

            var enemies = new System.Collections.Generic.List<Mob0Enemy>();
            for (int i = 0; i < 5; i++)
            {
                enemies.Add(factory.Acquire());
            }
            Assert.AreEqual(5, pool.ActiveCount, "5 次 Acquire 后 ActiveCount=5。");

            foreach (var e in enemies)
            {
                Assert.IsTrue(factory.Release(e), "逐个 Release 返回 true。");
            }
            Assert.AreEqual(0, pool.ActiveCount, "全部 Release 后 ActiveCount=0。");
        }

        // ====================================================================
        // 重复 Release 安全测试
        // ====================================================================

        [Test]
        [Description("重复 Release 同一对象返回 false，不重复入池。")]
        public void Release_Twice_SecondReturnsFalse()
        {
            EnemyFactory factory = CreateFactory(out BattleObjectPool<Mob0Enemy> pool);

            Mob0Enemy enemy = factory.Acquire();
            Assert.IsTrue(factory.Release(enemy), "首次 Release 返回 true。");
            Assert.AreEqual(1, pool.FreeCount, "首次 Release 后 FreeCount=1。");

            Assert.IsFalse(factory.Release(enemy), "重复 Release 返回 false。");
            Assert.AreEqual(1, pool.FreeCount, "重复 Release 后 FreeCount 仍为 1（不重复入池）。");
        }

        [Test]
        [Description("Release null 返回 false。")]
        public void Release_Null_ReturnsFalse()
        {
            EnemyFactory factory = CreateFactory(out _);

            Assert.IsFalse(factory.Release(null), "Release null 返回 false。");
        }

        // ====================================================================
        // 诊断日志测试
        // ====================================================================

        [Test]
        [Description("CreateCount/RecoverCount 准确跟踪 Acquire/Release 操作次数。")]
        public void CreateCount_RecoverCount_TrackOperations()
        {
            EnemyFactory factory = CreateFactory(out _);

            Assert.AreEqual(0, factory.CreateCount, "初始 CreateCount=0。");
            Assert.AreEqual(0, factory.RecoverCount, "初始 RecoverCount=0。");

            Mob0Enemy a = factory.Acquire();
            Assert.AreEqual(1, factory.CreateCount, "1 次 Acquire 后 CreateCount=1。");
            Assert.AreEqual(0, factory.RecoverCount, "未 Release 时 RecoverCount=0。");

            Mob0Enemy b = factory.Acquire();
            Assert.AreEqual(2, factory.CreateCount, "2 次 Acquire 后 CreateCount=2。");

            factory.Release(a);
            Assert.AreEqual(1, factory.RecoverCount, "1 次 Release 后 RecoverCount=1。");

            factory.Release(b);
            Assert.AreEqual(2, factory.RecoverCount, "2 次 Release 后 RecoverCount=2。");
        }

        [Test]
        [Description("ResetForTests 清空诊断日志。")]
        public void ResetForTests_ClearsLogs()
        {
            EnemyFactory factory = CreateFactory(out _);

            Mob0Enemy enemy = factory.Acquire();
            factory.Release(enemy);
            Assert.AreEqual(1, factory.CreateCount, "操作后 CreateCount=1。");
            Assert.AreEqual(1, factory.RecoverCount, "操作后 RecoverCount=1。");

            factory.ResetForTests();

            Assert.AreEqual(0, factory.CreateCount, "ResetForTests 后 CreateCount=0。");
            Assert.AreEqual(0, factory.RecoverCount, "ResetForTests 后 RecoverCount=0。");
        }

        // ====================================================================
        // Release 后旧目标引用失效测试
        // ====================================================================

        [Test]
        [Description("Release 后对象 RuntimeId=0，证明 ResetState 已执行（旧 ID/目标引用失效的前提）。")]
        public void Release_ResetStateExecuted_OldReferencesInvalid()
        {
            EnemyFactory factory = CreateFactory(out _);

            Mob0Enemy enemy = factory.Acquire();
            int oldId = enemy.RuntimeId;
            Assert.Greater(oldId, 0, "Acquire 后 RuntimeId > 0。");

            // Release 触发 BattleObjectPool.Release -> Mob0Enemy.ResetState。
            // ResetState MUST 清除全部可变状态（RuntimeId、阵营、生命、路径、目标引用等）。
            // 本测试验证 RuntimeId 被清除作为 ResetState 已执行的代理证据。
            // 目标引用等字段的清除由 enemy-pool-reset-contract.md 规定，由 Mob0Enemy.ResetState 实现。
            factory.Release(enemy);

            Assert.AreEqual(0, enemy.RuntimeId,
                "Release 后 RuntimeId=0，证明 ResetState 已执行。" +
                "若失败，说明 Mob0Enemy.ResetState 未清除 RuntimeId，旧 ID/目标引用可能仍有效。");
        }
    }
}
