using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Combat
{
    // ============================================================================
    // 任务 5.3 返工：CancelOwner 与池回收委托测试
    // ----------------------------------------------------------------------------
    // 校验发现：
    //   5.3 REWORK 问题 2：缺少 CancelOwner 测试。
    //   5.3 REWORK 问题 3：缺少池回收委托（releaseToPool）测试。
    //
    // 验证要求：
    //   1. CancelOwner 按 owner 引用匹配取消效果，入移除队列后统一回收。
    //   2. CancelOwner 不匹配的 owner 返回 0，不移除任何效果。
    //   3. 有 releaseToPool 委托的效果移除时经委托归还池。
    //   4. 无 releaseToPool 委托的效果移除时只 Cancel 不归还池。
    //
    // 来源证据：
    //   - AttackEffectManager.js cancelOwner(owner, reason)：遍历匹配 owner 调 remove。
    //   - AttackEffectManager.js _release：records.delete → effect.cleanup →
    //     objectPool.recoverByClass（有池时回收）。
    //   - design.md 目录表：统一推进、取消和回收活动攻击效果。
    //   - task 5.3 约束：CancelOwner 按引用匹配，池回收委托对称。
    //
    // 测试策略：
    //   使用最小 IAttackEffect 桩，验证 AttackEffectManager 的 CancelOwner 与
    //   releaseToPool 委托行为。不接触 EnemyManager / AttackResolver / Scene。
    // ============================================================================

    /// <summary>
    /// CancelOwner 与池回收委托测试（task 5.3 返工）。
    /// </summary>
    [TestFixture]
    internal class AttackEffectManagerCancelAndPoolTests
    {
        // ====================================================================
        // StubEffect —— 最小 IAttackEffect 测试桩
        // ====================================================================

        /// <summary>
        /// 最小 IAttackEffect 桩，记录 Cancel 调用与 Update 推进。
        /// </summary>
        private sealed class StubEffect : IAttackEffect
        {
            internal object OwnerValue;
            internal bool ActiveValue = true;
            internal int CancelCount;
            internal int UpdateCount;
            internal long LastUpdateDeltaMs;
            internal string LastCancelReason;
            internal bool ResetCalled;

            public bool Active => ActiveValue;
            public object Owner => OwnerValue;

            public void Update(long deltaMs)
            {
                UpdateCount++;
                LastUpdateDeltaMs = deltaMs;
            }

            public void Cancel(string reason)
            {
                CancelCount++;
                LastCancelReason = reason;
                ActiveValue = false;
            }

            public void ResetState()
            {
                ResetCalled = true;
                OwnerValue = null;
                ActiveValue = false;
                CancelCount = 0;
                UpdateCount = 0;
                LastUpdateDeltaMs = 0;
                LastCancelReason = null;
            }
        }

        // ====================================================================
        // CancelOwner 测试
        // ====================================================================

        [Test]
        [Description("CancelOwner 按引用匹配取消同 owner 的全部活动效果，入移除队列后统一回收。")]
        public void CancelOwner_MatchesByReference_EnqueuesAndReleases()
        {
            var manager = new AttackEffectManager();
            var owner1 = new object();
            var owner2 = new object();

            var effect1 = new StubEffect { OwnerValue = owner1 };
            var effect2 = new StubEffect { OwnerValue = owner1 };
            var effect3 = new StubEffect { OwnerValue = owner2 };

            manager.Add(effect1);
            manager.Add(effect2);
            manager.Add(effect3);

            Assert.AreEqual(3, manager.ActiveCount, "应有三个活动效果");

            // 取消 owner1 的全部效果（2 个）。
            int cancelled = manager.CancelOwner(owner1, "owner-removed");

            Assert.AreEqual(2, cancelled, "应取消 2 个 owner1 的效果");
            // CancelOwner 只入移除队列，遍历结束后由 ProcessRemoveQueue 统一处理。
            // 但 CancelOwner 不调 Update，移除队列未处理。
            // 需调 ProcessRemoveQueue 或 Update 触发处理。
            manager.ProcessRemoveQueue();

            Assert.AreEqual(1, manager.ActiveCount, "应只剩 owner2 的效果");
            Assert.IsTrue(manager.Contains(effect3), "owner2 的效果应仍在活动集合");

            // 验证 Cancel 被调用。
            Assert.AreEqual(1, effect1.CancelCount, "effect1 应被 Cancel 一次");
            Assert.AreEqual(1, effect2.CancelCount, "effect2 应被 Cancel 一次");
            Assert.AreEqual("owner-removed", effect1.LastCancelReason, "Cancel 原因应为 owner-removed");
            Assert.AreEqual(0, effect3.CancelCount, "effect3 不应被 Cancel");
        }

        [Test]
        [Description("CancelOwner 不匹配任何效果时返回 0，不移除任何效果。")]
        public void CancelOwner_NoMatch_ReturnsZero()
        {
            var manager = new AttackEffectManager();
            var owner1 = new object();
            var unknownOwner = new object();

            var effect = new StubEffect { OwnerValue = owner1 };
            manager.Add(effect);

            int cancelled = manager.CancelOwner(unknownOwner);
            Assert.AreEqual(0, cancelled, "不匹配时应返回 0");
            Assert.AreEqual(1, manager.ActiveCount, "效果不应被移除");
            Assert.AreEqual(0, effect.CancelCount, "效果不应被 Cancel");
        }

        [Test]
        [Description("CancelOwner 传入 null 返回 0，不移除任何效果。")]
        public void CancelOwner_NullOwner_ReturnsZero()
        {
            var manager = new AttackEffectManager();
            var owner = new object();
            var effect = new StubEffect { OwnerValue = owner };
            manager.Add(effect);

            int cancelled = manager.CancelOwner(null);
            Assert.AreEqual(0, cancelled, "null owner 应返回 0");
            Assert.AreEqual(1, manager.ActiveCount, "效果不应被移除");
        }

        [Test]
        [Description("CancelOwner 在 Update 遍历中调用时只入队列，遍历结束后统一处理。")]
        public void CancelOwner_DuringUpdate_QueuedUntilAfterTraversal()
        {
            var manager = new AttackEffectManager();
            var owner = new object();

            // 创建一个在 Update 时触发 CancelOwner 的效果。
            var triggerEffect = new CancelOwnerTriggerEffect(manager, owner);
            var targetEffect = new StubEffect { OwnerValue = owner };

            manager.Add(triggerEffect);
            manager.Add(targetEffect);

            // Update 时 triggerEffect.Update 调 CancelOwner，入移除队列。
            // 遍历结束后 ProcessRemoveQueue 统一处理。
            manager.Update(50);

            // triggerEffect 自身因 Cancel 被移除，targetEffect 也被取消。
            Assert.AreEqual(0, manager.ActiveCount, "两个效果都应被移除");
            Assert.IsTrue(targetEffect.CancelCount > 0, "targetEffect 应被 Cancel");
        }

        /// <summary>
        /// 在 Update 时触发 CancelOwner 的测试桩效果。
        /// </summary>
        private sealed class CancelOwnerTriggerEffect : IAttackEffect
        {
            private readonly AttackEffectManager _manager;
            private readonly object _owner;
            internal bool ActiveValue = true;

            public bool Active => ActiveValue;
            public object Owner => _owner;

            internal CancelOwnerTriggerEffect(AttackEffectManager manager, object owner)
            {
                _manager = manager;
                _owner = owner;
            }

            public void Update(long deltaMs)
            {
                // 在 Update 中触发 CancelOwner，取消同 owner 的全部效果。
                _manager.CancelOwner(_owner, "trigger-cancel");
            }

            public void Cancel(string reason)
            {
                ActiveValue = false;
            }

            public void ResetState()
            {
                ActiveValue = false;
            }
        }

        // ====================================================================
        // 池回收委托（releaseToPool）测试
        // ====================================================================

        [Test]
        [Description("有 releaseToPool 委托的效果移除时经委托归还池。")]
        public void ReleaseToPool_DelegateCalled_OnRemove()
        {
            var manager = new AttackEffectManager();
            var owner = new object();
            var effect = new StubEffect { OwnerValue = owner };

            // 模拟池回收委托。
            var poolReturnLog = new List<IAttackEffect>();
            Action<IAttackEffect> releaseToPool = e => poolReturnLog.Add(e);

            manager.Add(effect, releaseToPool);

            // 推进一帧后效果完成（Active=false），触发移除回收。
            effect.ActiveValue = false;
            manager.Update(50);

            Assert.AreEqual(0, manager.ActiveCount, "效果应被移除");
            Assert.AreEqual(1, poolReturnLog.Count, "池回收委托应被调用一次");
            Assert.AreSame(effect, poolReturnLog[0], "归还的对象应是原效果");
            Assert.AreEqual(1, effect.CancelCount, "效果应被 Cancel 一次");
        }

        [Test]
        [Description("无 releaseToPool 委托的效果移除时只 Cancel 不归还池。")]
        public void NoReleaseToPool_OnlyCancel_OnRemove()
        {
            var manager = new AttackEffectManager();
            var owner = new object();
            var effect = new StubEffect { OwnerValue = owner };

            // 不提供 releaseToPool（null）。
            manager.Add(effect, null);
            Assert.IsFalse(manager.HasReleaseDelegate(effect), "不应有回收委托");

            // 推进一帧后效果完成，触发移除。
            effect.ActiveValue = false;
            manager.Update(50);

            Assert.AreEqual(0, manager.ActiveCount, "效果应被移除");
            Assert.AreEqual(1, effect.CancelCount, "效果应被 Cancel 一次");
            Assert.IsFalse(effect.ResetCalled, "无委托时不应调 ResetState（池 Release 才调）");
        }

        [Test]
        [Description("Clear 时有委托的效果经委托归还，无委托的效果只 Cancel。")]
        public void Clear_ReleaseToPoolDelegateCalled_ForPooledEffects()
        {
            var manager = new AttackEffectManager();
            var owner = new object();

            var pooledEffect = new StubEffect { OwnerValue = owner };
            var unpooledEffect = new StubEffect { OwnerValue = owner };

            var poolReturnLog = new List<IAttackEffect>();
            Action<IAttackEffect> releaseToPool = e => poolReturnLog.Add(e);

            manager.Add(pooledEffect, releaseToPool);
            manager.Add(unpooledEffect, null);

            Assert.AreEqual(2, manager.ActiveCount, "应有两个活动效果");

            // Settling Clear 取消全部效果。
            manager.Clear();

            Assert.AreEqual(0, manager.ActiveCount, "Clear 后应无活动效果");
            Assert.AreEqual(1, poolReturnLog.Count, "只有有委托的效果应归还池");
            Assert.AreSame(pooledEffect, poolReturnLog[0], "归还的应是有委托的效果");

            // 两个效果都应被 Cancel。
            Assert.AreEqual(1, pooledEffect.CancelCount, "有委托效果应被 Cancel");
            Assert.AreEqual(1, unpooledEffect.CancelCount, "无委托效果也应被 Cancel");
        }

        [Test]
        [Description("HasReleaseDelegate 正确反映委托存在性。")]
        public void HasReleaseDelegate_ReflectsDelegatePresence()
        {
            var manager = new AttackEffectManager();
            var owner = new object();

            var withPool = new StubEffect { OwnerValue = owner };
            var withoutPool = new StubEffect { OwnerValue = owner };

            manager.Add(withPool, e => { });
            manager.Add(withoutPool, null);

            Assert.IsTrue(manager.HasReleaseDelegate(withPool), "有委托应返回 true");
            Assert.IsFalse(manager.HasReleaseDelegate(withoutPool), "无委托应返回 false");

            // 移除后应返回 false。
            withPool.ActiveValue = false;
            manager.Update(50);
            Assert.IsFalse(manager.HasReleaseDelegate(withPool), "移除后应返回 false");
        }
    }
}
