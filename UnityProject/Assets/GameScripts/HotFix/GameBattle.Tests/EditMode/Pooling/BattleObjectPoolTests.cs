using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Pooling
{
    /// <summary>
    /// BattleObjectPool / BattlePoolScope / IPoolableBattleObject 单元测试（task 4.1）。
    /// </summary>
    /// <remarks>
    /// <para>验证 task 4.1 的全部关键要求：</para>
    /// <list type="bullet">
    /// <item>Acquire/Release 对称性：每次 Acquire 对应恰好一次 Release。</item>
    /// <item>活动租借计数：ActiveCount 准确追踪未归还对象。</item>
    /// <item>完整 Reset 契约：Release 前执行 ResetState，回收后无残留状态。</item>
    /// <item>池复用无污染：Acquire 取得的复用对象等价于新构造。</item>
    /// <item>不预热：首次 Acquire 必定新建。</item>
    /// <item>不硬编码容量：空闲列表无上限，高水位由实际使用形成。</item>
    /// <item>同会话复用：ClearForNewBattle 保留空闲容量。</item>
    /// <item>返回主界面/Shutdown 清空：ClearAll 清空全部容量。</item>
    /// <item>重复 Release 安全：已归还对象再次 Release 返回 false。</item>
    /// <item>BattlePoolScope 统一管理多个池：ClearForNewBattle / ClearAll / 断言。</item>
    /// </list>
    ///
    /// <para><b>测试夹具：</b>使用 <see cref="FakePoolable"/> 作为最小可池化对象，
    /// 带有可观察的可变状态（Id / Tag / Data 列表），用于验证 ResetState 完整性与
    /// 池复用无污染。<see cref="FakePoolable"/> 不依赖 UnityEngine，可在 EditMode 运行。</para>
    /// </remarks>
    [TestFixture]
    internal class BattleObjectPoolTests
    {
        // ====================================================================
        // 最小可池化测试对象
        // ====================================================================

        /// <summary>
        /// 最小可池化对象，带有可观察的可变状态，用于验证 ResetState 完整性与池复用无污染。
        /// </summary>
        private sealed class FakePoolable : IPoolableBattleObject
        {
            /// <summary>模拟运行时 ID（每次 Acquire 后由调用方重新分配）。</summary>
            public int Id;

            /// <summary>模拟阵营/标记字符串。</summary>
            public string Tag;

            /// <summary>模拟可变集合（如伤害贡献者列表）。</summary>
            public List<int> Data = new List<int>();

            /// <summary>
            /// 重置到等价于新构造的状态。
            /// 清除 Id、Tag、Data，模拟还原工程池复位契约要求的所有字段。
            /// </summary>
            public void ResetState()
            {
                Id = 0;
                Tag = null;
                Data.Clear();
            }
        }

        // ====================================================================
        // Acquire/Release 对称性测试
        // ====================================================================

        [Test]
        [Description("Acquire 后 ActiveCount=1，Release 后 ActiveCount=0：对称租借。")]
        public void AcquireRelease_Symmetric_ActiveCountTracksRental()
        {
            var pool = new BattleObjectPool<FakePoolable>(() => new FakePoolable());

            Assert.AreEqual(0, pool.ActiveCount, "初始 ActiveCount=0。");

            FakePoolable obj = pool.Acquire();
            Assert.AreEqual(1, pool.ActiveCount, "Acquire 后 ActiveCount=1。");
            Assert.IsNotNull(obj, "Acquire 返回非 null 对象。");

            bool released = pool.Release(obj);
            Assert.IsTrue(released, "Release 返回 true。");
            Assert.AreEqual(0, pool.ActiveCount, "Release 后 ActiveCount=0。");
        }

        [Test]
        [Description("多次 Acquire/Release 对称：每个 Acquire 对应恰好一次 Release。")]
        public void MultipleAcquireRelease_AllSymmetric()
        {
            var pool = new BattleObjectPool<FakePoolable>(() => new FakePoolable());
            var rented = new List<FakePoolable>();

            // Acquire 5 个。
            for (int i = 0; i < 5; i++)
            {
                rented.Add(pool.Acquire());
            }
            Assert.AreEqual(5, pool.ActiveCount, "5 次 Acquire 后 ActiveCount=5。");

            // 逐个 Release。
            foreach (var obj in rented)
            {
                Assert.IsTrue(pool.Release(obj), "逐个 Release 返回 true。");
            }
            Assert.AreEqual(0, pool.ActiveCount, "全部 Release 后 ActiveCount=0。");
        }

        // ====================================================================
        // 活动租借计数测试
        // ====================================================================

        [Test]
        [Description("ActiveCount 准确反映当前未归还对象数量。")]
        public void ActiveCount_TracksUnreleasedObjects()
        {
            var pool = new BattleObjectPool<FakePoolable>(() => new FakePoolable());

            FakePoolable a = pool.Acquire();
            FakePoolable b = pool.Acquire();
            FakePoolable c = pool.Acquire();
            Assert.AreEqual(3, pool.ActiveCount, "3 次 Acquire 后 ActiveCount=3。");

            pool.Release(b);
            Assert.AreEqual(2, pool.ActiveCount, "Release 中间对象后 ActiveCount=2。");

            FakePoolable d = pool.Acquire();
            Assert.AreEqual(3, pool.ActiveCount, "再次 Acquire 后 ActiveCount=3。");

            pool.Release(a);
            pool.Release(c);
            pool.Release(d);
            Assert.AreEqual(0, pool.ActiveCount, "全部归还后 ActiveCount=0。");
        }

        [Test]
        [Description("HighWaterMark 记录活动对象峰值，不复用旧局容量。")]
        public void HighWaterMark_RecordsPeak_ActiveCount()
        {
            var pool = new BattleObjectPool<FakePoolable>(() => new FakePoolable());
            Assert.AreEqual(0, pool.HighWaterMark, "初始 HighWaterMark=0。");

            var rented = new List<FakePoolable>();
            for (int i = 0; i < 4; i++)
            {
                rented.Add(pool.Acquire());
            }
            Assert.AreEqual(4, pool.HighWaterMark, "4 次 Acquire 后 HighWaterMark=4。");

            foreach (var obj in rented)
            {
                pool.Release(obj);
            }
            Assert.AreEqual(4, pool.HighWaterMark, "全部 Release 后 HighWaterMark 仍为 4（峰值保留）。");

            // 再次 Acquire 2 个，HighWaterMark 不下降。
            rented.Clear();
            for (int i = 0; i < 2; i++)
            {
                rented.Add(pool.Acquire());
            }
            Assert.AreEqual(4, pool.HighWaterMark, "再次 Acquire 2 个，HighWaterMark 仍为 4（不低于历史峰值）。");

            foreach (var obj in rented)
            {
                pool.Release(obj);
            }
        }

        // ====================================================================
        // 完整 Reset 契约测试
        // ====================================================================

        [Test]
        [Description("Release 前执行 ResetState：回收后对象状态等价于新构造。")]
        public void Release_PerformsResetState_ObjectClearedBeforePooling()
        {
            var pool = new BattleObjectPool<FakePoolable>(() => new FakePoolable());

            FakePoolable obj = pool.Acquire();
            obj.Id = 42;
            obj.Tag = "enemy";
            obj.Data.Add(1001);
            obj.Data.Add(1002);

            pool.Release(obj);

            // 验证对象已被 Reset（虽然已入池，但引用仍可观察）。
            Assert.AreEqual(0, obj.Id, "Release 后 Id=0（已 Reset）。");
            Assert.IsNull(obj.Tag, "Release 后 Tag=null（已 Reset）。");
            Assert.AreEqual(0, obj.Data.Count, "Release 后 Data 已清空（已 Reset）。");
        }

        [Test]
        [Description("ResetState 覆盖全部可变字段：无残留状态（对应还原工程池复位契约）。")]
        public void ResetState_ClearsAllMutableFields_NoResidue()
        {
            var pool = new BattleObjectPool<FakePoolable>(() => new FakePoolable());

            FakePoolable obj = pool.Acquire();
            // 模拟使用中设置的全部可变状态。
            obj.Id = 999;
            obj.Tag = "projectile";
            obj.Data.AddRange(new[] { 1, 2, 3, 4, 5 });

            pool.Release(obj);

            // 验证全部字段已清空，等价于新构造。
            Assert.AreEqual(0, obj.Id, "Id 已清空。");
            Assert.IsNull(obj.Tag, "Tag 已清空。");
            Assert.IsEmpty(obj.Data, "Data 已清空。");
        }

        // ====================================================================
        // 池复用无污染测试
        // ====================================================================

        [Test]
        [Description("Acquire 复用已归还对象：状态等价于新构造，无旧局残留。")]
        public void Acquire_Reuse_Pool_NoPollution()
        {
            var pool = new BattleObjectPool<FakePoolable>(() => new FakePoolable());

            // 第一轮：Acquire -> 污染 -> Release。
            FakePoolable first = pool.Acquire();
            first.Id = 777;
            first.Tag = "old-target";
            first.Data.Add(42);
            pool.Release(first);

            // 第二轮：Acquire 应复用同一对象，但状态已清空。
            FakePoolable second = pool.Acquire();
            Assert.AreSame(first, second, "复用同一对象实例（LIFO）。");
            Assert.AreEqual(0, second.Id, "复用对象 Id=0（无旧 ID 残留）。");
            Assert.IsNull(second.Tag, "复用对象 Tag=null（无旧目标残留）。");
            Assert.AreEqual(0, second.Data.Count, "复用对象 Data 空（无旧贡献者残留）。");

            pool.Release(second);
        }

        [Test]
        [Description("多轮 Acquire/Release 无污染：每轮复用对象状态均等价于新构造。")]
        public void MultipleRounds_Reuse_NoPollution()
        {
            var pool = new BattleObjectPool<FakePoolable>(() => new FakePoolable());

            for (int round = 0; round < 10; round++)
            {
                FakePoolable obj = pool.Acquire();
                // 模拟使用中污染。
                obj.Id = round + 1;
                obj.Tag = "round-" + round;
                obj.Data.Add(round);

                pool.Release(obj);

                // Release 后立即验证已清空。
                Assert.AreEqual(0, obj.Id, $"第 {round} 轮 Release 后 Id=0。");
                Assert.IsNull(obj.Tag, $"第 {round} 轮 Release 后 Tag=null。");
                Assert.AreEqual(0, obj.Data.Count, $"第 {round} 轮 Release 后 Data 空。");
            }

            Assert.AreEqual(0, pool.ActiveCount, "10 轮后 ActiveCount=0。");
            Assert.AreEqual(1, pool.FreeCount, "10 轮后 FreeCount=1（同一对象复用）。");
        }

        [Test]
        [Description("两个对象交替 Acquire/Release：复用时不会交叉污染。")]
        public void TwoObjects_Alternating_NoCrossPollution()
        {
            var pool = new BattleObjectPool<FakePoolable>(() => new FakePoolable());

            FakePoolable a = pool.Acquire();
            FakePoolable b = pool.Acquire();
            a.Id = 100;
            b.Id = 200;
            a.Tag = "A";
            b.Tag = "B";
            a.Data.Add(1);
            b.Data.Add(2);

            pool.Release(a);
            pool.Release(b);

            // LIFO：先取回 b，再取回 a。
            FakePoolable firstReuse = pool.Acquire();
            Assert.AreSame(b, firstReuse, "LIFO 先取回 b。");
            Assert.AreEqual(0, firstReuse.Id, "b 复用后 Id=0。");
            Assert.IsNull(firstReuse.Tag, "b 复用后 Tag=null。");
            Assert.AreEqual(0, firstReuse.Data.Count, "b 复用后 Data 空。");

            FakePoolable secondReuse = pool.Acquire();
            Assert.AreSame(a, secondReuse, "LIFO 再取回 a。");
            Assert.AreEqual(0, secondReuse.Id, "a 复用后 Id=0。");
            Assert.IsNull(secondReuse.Tag, "a 复用后 Tag=null。");
            Assert.AreEqual(0, secondReuse.Data.Count, "a 复用后 Data 空。");

            pool.Release(firstReuse);
            pool.Release(secondReuse);
        }

        // ====================================================================
        // 重复 Release 安全测试
        // ====================================================================

        [Test]
        [Description("重复 Release 同一对象返回 false，不重复入池（ObjectPool.js __InPool 语义）。")]
        public void DuplicateRelease_ReturnsFalse_DoesNotDoublePool()
        {
            var pool = new BattleObjectPool<FakePoolable>(() => new FakePoolable());

            FakePoolable obj = pool.Acquire();
            Assert.IsTrue(pool.Release(obj), "首次 Release 返回 true。");
            Assert.AreEqual(0, pool.ActiveCount, "首次 Release 后 ActiveCount=0。");
            Assert.AreEqual(1, pool.FreeCount, "首次 Release 后 FreeCount=1。");

            // 重复 Release。
            Assert.IsFalse(pool.Release(obj), "重复 Release 返回 false。");
            Assert.AreEqual(0, pool.ActiveCount, "重复 Release 后 ActiveCount 仍为 0。");
            Assert.AreEqual(1, pool.FreeCount, "重复 Release 后 FreeCount 仍为 1（不重复入池）。");
        }

        [Test]
        [Description("Release null 对象返回 false。")]
        public void Release_Null_ReturnsFalse()
        {
            var pool = new BattleObjectPool<FakePoolable>(() => new FakePoolable());
            Assert.IsFalse(pool.Release(null), "Release null 返回 false。");
        }

        [Test]
        [Description("未 Acquire 直接 Release 返回 false（ActiveCount=0 防御）。")]
        public void Release_WithoutAcquire_ReturnsFalse()
        {
            var pool = new BattleObjectPool<FakePoolable>(() => new FakePoolable());
            // 构造一个对象但不通过 Acquire 取得，直接尝试 Release。
            var orphan = new FakePoolable();
            Assert.IsFalse(pool.Release(orphan), "未 Acquire 的对象 Release 返回 false。");
            Assert.AreEqual(0, pool.FreeCount, "未 Acquire 的对象不入池。");
        }

        [Test]
        [Description("多租约交叉重复 Release：Acquire A,B → Release A → 再 Release A " +
                     "返回 false，FreeCount 不增加，ActiveCount 不递减（任务 53 返工修复）。")]
        public void DuplicateRelease_CrossRental_ReturnsFalse_NoDoublePool()
        {
            var pool = new BattleObjectPool<FakePoolable>(() => new FakePoolable());

            // Acquire A, Acquire B：ActiveCount=2，FreeCount=0。
            FakePoolable a = pool.Acquire();
            FakePoolable b = pool.Acquire();
            Assert.AreEqual(2, pool.ActiveCount, "两次 Acquire 后 ActiveCount=2。");
            Assert.AreEqual(0, pool.FreeCount, "两次 Acquire 后 FreeCount=0。");

            // Release A：成功，A 入 _free，ActiveCount=1。
            Assert.IsTrue(pool.Release(a), "首次 Release A 返回 true。");
            Assert.AreEqual(1, pool.ActiveCount, "Release A 后 ActiveCount=1。");
            Assert.AreEqual(1, pool.FreeCount, "Release A 后 FreeCount=1。");

            // 重复 Release A：必须返回 false，不重复入池，ActiveCount 不递减。
            // 此为任务 53 返工核心验证点：旧实现仅靠 _activeCount<=0 守卫，
            // 在 ActiveCount=1>0 时会错误通过，导致 A 在 _free 中出现两次。
            Assert.IsFalse(pool.Release(a), "重复 Release A 返回 false。");
            Assert.AreEqual(1, pool.ActiveCount, "重复 Release A 后 ActiveCount 仍为 1（不递减）。");
            Assert.AreEqual(1, pool.FreeCount, "重复 Release A 后 FreeCount 仍为 1（不重复入池）。");

            // 收尾：Release B，恢复对称。
            Assert.IsTrue(pool.Release(b), "Release B 返回 true。");
            Assert.AreEqual(0, pool.ActiveCount, "全部归还后 ActiveCount=0。");
            Assert.AreEqual(2, pool.FreeCount, "全部归还后 FreeCount=2。");

            // 再次重复 Release A（此时 ActiveCount=0，但 A 已在池中）：
            // 验证 _inPool 查重在 ActiveCount=0 状态下同样生效（双重防线）。
            Assert.IsFalse(pool.Release(a), "ActiveCount=0 时重复 Release A 仍返回 false。");
            Assert.AreEqual(2, pool.FreeCount, "FreeCount 不变。");
        }

        // ====================================================================
        // 不预热测试
        // ====================================================================

        [Test]
        [Description("构造时空闲列表为空，不预热（task 4.1）。")]
        public void Construct_NoPreWarm_FreeListEmpty()
        {
            var pool = new BattleObjectPool<FakePoolable>(() => new FakePoolable());
            Assert.AreEqual(0, pool.FreeCount, "构造后 FreeCount=0（不预热）。");
            Assert.AreEqual(0, pool.ActiveCount, "构造后 ActiveCount=0。");
            Assert.AreEqual(0, pool.HighWaterMark, "构造后 HighWaterMark=0。");
        }

        // ====================================================================
        // 不硬编码容量测试
        // ====================================================================

        [Test]
        [Description("空闲列表无上限：Acquire 超过已归还数量时新建对象，不硬编码容量。")]
        public void Acquire_BeyondFree_CreatesNew_NoHardcodedCap()
        {
            var pool = new BattleObjectPool<FakePoolable>(() => new FakePoolable());

            // 第一轮：Acquire 3 个并全部归还。
            var firstBatch = new List<FakePoolable>();
            for (int i = 0; i < 3; i++)
            {
                firstBatch.Add(pool.Acquire());
            }
            Assert.AreEqual(3, pool.HighWaterMark, "第一轮 HighWaterMark=3。");
            foreach (var obj in firstBatch)
            {
                pool.Release(obj);
            }
            Assert.AreEqual(3, pool.FreeCount, "第一轮归还后 FreeCount=3。");

            // 第二轮：Acquire 5 个（超过空闲的 3 个）。
            var secondBatch = new List<FakePoolable>();
            for (int i = 0; i < 5; i++)
            {
                secondBatch.Add(pool.Acquire());
            }
            Assert.AreEqual(5, pool.HighWaterMark, "第二轮 HighWaterMark=5（超过旧高水位）。");
            Assert.AreEqual(0, pool.FreeCount, "第二轮 Acquire 后 FreeCount=0（全部复用+新建）。");

            // 前 3 个应为复用（LIFO 逆序），后 2 个为新建。
            Assert.AreSame(firstBatch[2], secondBatch[0], "LIFO：第二个 batch 首个取回第一个 batch 最后归还的。");
            Assert.AreSame(firstBatch[1], secondBatch[1], "LIFO：取回第一个 batch 倒数第二个。");
            Assert.AreSame(firstBatch[0], secondBatch[2], "LIFO：取回第一个 batch 最早归还的。");
            // 后两个是新建的，不与第一轮相同。
            Assert.AreNotSame(firstBatch[0], secondBatch[3], "第 4 个为新建。");
            Assert.AreNotSame(firstBatch[1], secondBatch[3], "第 4 个为新建。");
            Assert.AreNotSame(firstBatch[2], secondBatch[3], "第 4 个为新建。");

            foreach (var obj in secondBatch)
            {
                pool.Release(obj);
            }
        }

        // ====================================================================
        // ClearForNewBattle 同会话复用测试
        // ====================================================================

        [Test]
        [Description("ClearForNewBattle 保留空闲容量，断言活动租借为 0。")]
        public void ClearForNewBattle_KeepsFree_AssertsActiveZero()
        {
            var pool = new BattleObjectPool<FakePoolable>(() => new FakePoolable());

            // 模拟一局使用：Acquire 3 个并全部归还。
            var batch = new List<FakePoolable>();
            for (int i = 0; i < 3; i++)
            {
                batch.Add(pool.Acquire());
            }
            foreach (var obj in batch)
            {
                pool.Release(obj);
            }
            Assert.AreEqual(3, pool.FreeCount, "一局结束 FreeCount=3。");
            Assert.AreEqual(0, pool.ActiveCount, "一局结束 ActiveCount=0。");

            // 重开：保留空闲容量。
            Assert.IsTrue(pool.ClearForNewBattle(), "ClearForNewBattle 返回 true。");
            Assert.AreEqual(3, pool.FreeCount, "ClearForNewBattle 后 FreeCount=3（保留容量）。");
            Assert.AreEqual(0, pool.ActiveCount, "ClearForNewBattle 后 ActiveCount=0。");

            // 新局 Acquire 复用空闲容量。
            FakePoolable reused = pool.Acquire();
            Assert.AreEqual(2, pool.FreeCount, "新局 Acquire 后 FreeCount=2（复用）。");
            pool.Release(reused);
        }

        [Test]
        [Description("ClearForNewBattle 有活动租借时返回 false（Settling 未完成）。")]
        public void ClearForNewBattle_WithActive_ReturnsFalse()
        {
            var pool = new BattleObjectPool<FakePoolable>(() => new FakePoolable());
            FakePoolable active = pool.Acquire();
            Assert.AreEqual(1, pool.ActiveCount, "有 1 个活动租借。");

            Assert.IsFalse(pool.ClearForNewBattle(), "有活动租借时返回 false。");

            // 清理。
            pool.Release(active);
            Assert.IsTrue(pool.ClearForNewBattle(), "归还后返回 true。");
        }

        // ====================================================================
        // ClearAll 返回主界面/Shutdown 清空测试
        // ====================================================================

        [Test]
        [Description("ClearAll 清空全部空闲容量，重置活动计数。")]
        public void ClearAll_ClearsFreeAndActive()
        {
            var pool = new BattleObjectPool<FakePoolable>(() => new FakePoolable());

            // 模拟使用：Acquire 3 个并归还。
            var batch = new List<FakePoolable>();
            for (int i = 0; i < 3; i++)
            {
                batch.Add(pool.Acquire());
            }
            foreach (var obj in batch)
            {
                pool.Release(obj);
            }
            Assert.AreEqual(3, pool.FreeCount, "使用后 FreeCount=3。");

            // 返回主界面：清空全部。
            pool.ClearAll();
            Assert.AreEqual(0, pool.FreeCount, "ClearAll 后 FreeCount=0。");
            Assert.AreEqual(0, pool.ActiveCount, "ClearAll 后 ActiveCount=0。");
        }

        [Test]
        [Description("ClearAll 后 Acquire 必定新建（空闲已清空）。")]
        public void ClearAll_ThenAcquire_CreatesNew()
        {
            var pool = new BattleObjectPool<FakePoolable>(() => new FakePoolable());
            FakePoolable first = pool.Acquire();
            pool.Release(first);
            Assert.AreEqual(1, pool.FreeCount, "归还后 FreeCount=1。");

            pool.ClearAll();
            Assert.AreEqual(0, pool.FreeCount, "ClearAll 后 FreeCount=0。");

            FakePoolable second = pool.Acquire();
            Assert.AreNotSame(first, second, "ClearAll 后 Acquire 新建对象。");
            pool.Release(second);
        }

        // ====================================================================
        // BattlePoolScope 统一管理测试
        // ====================================================================

        [Test]
        [Description("BattlePoolScope.GetPool<T> 惰性创建并返回同一实例。")]
        public void GetPool_LazyCreate_ReturnsSameInstance()
        {
            var scope = new BattlePoolScope();
            Assert.AreEqual(0, scope.PoolCount, "初始 PoolCount=0。");

            var pool1 = scope.GetPool<FakePoolable>(() => new FakePoolable());
            Assert.AreEqual(1, scope.PoolCount, "GetPool 后 PoolCount=1。");

            // 第二次调用返回同一实例，工厂不使用但仍需传参（API 要求）。
            var pool2 = scope.GetPool<FakePoolable>(() => new FakePoolable());
            Assert.AreSame(pool1, pool2, "同类型返回同一池实例。");
            Assert.AreEqual(1, scope.PoolCount, "同类型不重复创建。");
        }

        [Test]
        [Description("BattlePoolScope.ClearForNewBattle 对全部池断言活动租借为 0，保留空闲。")]
        public void Scope_ClearForNewBattle_AllPoolsAssertActiveZero()
        {
            var scope = new BattlePoolScope();
            var pool = scope.GetPool<FakePoolable>(() => new FakePoolable());

            // 模拟一局使用并归还。
            FakePoolable obj = pool.Acquire();
            pool.Release(obj);

            Assert.IsTrue(scope.ClearForNewBattle(), "全部归还后 ClearForNewBattle 返回 true。");
            Assert.AreEqual(1, pool.FreeCount, "空闲容量保留。");
        }

        [Test]
        [Description("BattlePoolScope.ClearForNewBattle 有活动租借时返回 false。")]
        public void Scope_ClearForNewBattle_WithActive_ReturnsFalse()
        {
            var scope = new BattlePoolScope();
            var pool = scope.GetPool<FakePoolable>(() => new FakePoolable());
            FakePoolable active = pool.Acquire();

            Assert.IsFalse(scope.ClearForNewBattle(), "有活动租借时返回 false。");

            pool.Release(active);
            Assert.IsTrue(scope.ClearForNewBattle(), "归还后返回 true。");
        }

        [Test]
        [Description("BattlePoolScope.ClearAll 清空全部池的全部容量。")]
        public void Scope_ClearAll_ClearsAllPools()
        {
            var scope = new BattlePoolScope();
            var pool = scope.GetPool<FakePoolable>(() => new FakePoolable());

            FakePoolable obj = pool.Acquire();
            pool.Release(obj);
            Assert.AreEqual(1, pool.FreeCount, "归还后 FreeCount=1。");

            scope.ClearAll();
            Assert.AreEqual(0, pool.FreeCount, "ClearAll 后 FreeCount=0。");
            Assert.AreEqual(0, pool.ActiveCount, "ClearAll 后 ActiveCount=0。");
        }

        [Test]
        [Description("BattlePoolScope.AssertAllActiveReleased 全部归还返回 true，有活动返回 false。")]
        public void Scope_AssertAllActiveReleased_TracksActiveCount()
        {
            var scope = new BattlePoolScope();
            var pool = scope.GetPool<FakePoolable>(() => new FakePoolable());

            Assert.IsTrue(scope.AssertAllActiveReleased(), "无活动租借时返回 true。");

            FakePoolable active = pool.Acquire();
            Assert.IsFalse(scope.AssertAllActiveReleased(), "有活动租借时返回 false。");

            pool.Release(active);
            Assert.IsTrue(scope.AssertAllActiveReleased(), "归还后返回 true。");
        }

        [Test]
        [Description("BattlePoolScope 管理多个不同类型池：ClearForNewBattle / ClearAll 统一生效。")]
        public void Scope_MultiplePoolTypes_ClearAllAffectsAll()
        {
            var scope = new BattlePoolScope();
            var poolA = scope.GetPool<FakePoolable>(() => new FakePoolable());
            var poolB = scope.GetPool<AnotherFakePoolable>(() => new AnotherFakePoolable());
            Assert.AreEqual(2, scope.PoolCount, "两种类型 PoolCount=2。");

            // 两池都使用并归还。
            var a = poolA.Acquire();
            var b = poolB.Acquire();
            poolA.Release(a);
            poolB.Release(b);
            Assert.AreEqual(1, poolA.FreeCount, "poolA FreeCount=1。");
            Assert.AreEqual(1, poolB.FreeCount, "poolB FreeCount=1。");

            // ClearAll 清空两池。
            scope.ClearAll();
            Assert.AreEqual(0, poolA.FreeCount, "ClearAll 后 poolA FreeCount=0。");
            Assert.AreEqual(0, poolB.FreeCount, "ClearAll 后 poolB FreeCount=0。");
        }

        // ====================================================================
        // 第二个测试用可池化对象（用于多类型池测试）
        // ====================================================================

        /// <summary>
        /// 第二个最小可池化对象，用于验证 BattlePoolScope 管理多种类型池。
        /// </summary>
        private sealed class AnotherFakePoolable : IPoolableBattleObject
        {
            public int Value;

            public void ResetState()
            {
                Value = 0;
            }
        }
    }
}
