using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Map
{
    /// <summary>
    /// PlacementReservationRegistry 单元测试（task 3.8）。
    /// </summary>
    /// <remarks>
    /// <para>验证要求（task 3.8）：</para>
    /// <list type="bullet">
    /// <item>预留冲突：同一格子不可被重复预留。</item>
    /// <item>提交：Commit 释放预留，格子不再处于预留状态。</item>
    /// <item>补偿回滚：Rollback 释放预留，格子可被再次预留。</item>
    /// <item>清理：Clear 清空全部预留。</item>
    /// <item>批量预留：TryReserveBatch 任一冲突时全部回滚。</item>
    /// <item>幂等性：Commit/Rollback/Clear 对未预留格子安全。</item>
    /// </list>
    /// <para>本测试不接触 Scene、FUI 或资源加载，符合纯逻辑 EditMode 约束（task 2.2）。</para>
    /// </remarks>
    [TestFixture]
    internal class PlacementReservationRegistryTests
    {
        // ====================================================================
        // 基本预留测试
        // ====================================================================

        [Test]
        [Description("TryReserve 成功预留格子，Count 递增。")]
        public void TryReserve_Success_IncrementsCount()
        {
            var registry = new PlacementReservationRegistry();
            GridPosition pos = new GridPosition(2, 3);

            ReservationResult result = registry.TryReserve(pos);

            Assert.IsTrue(result.Success, "预留应成功。");
            Assert.AreEqual(1, registry.Count, "Count=1。");
            Assert.IsTrue(registry.IsReserved(pos), "格子已预留。");
        }

        [Test]
        [Description("TryReserve 预留不同格子，Count 累计。")]
        public void TryReserve_MultiplePositions_Accumulates()
        {
            var registry = new PlacementReservationRegistry();

            registry.TryReserve(new GridPosition(1, 1));
            registry.TryReserve(new GridPosition(2, 2));
            registry.TryReserve(new GridPosition(3, 3));

            Assert.AreEqual(3, registry.Count, "三个格子预留后 Count=3。");
        }

        // ====================================================================
        // 预留冲突测试
        // ====================================================================

        [Test]
        [Description("TryReserve 同一格子重复预留返回失败，不修改状态。")]
        public void TryReserve_Duplicate_ReturnsFailure_NoMutation()
        {
            var registry = new PlacementReservationRegistry();
            GridPosition pos = new GridPosition(1, 2);

            ReservationResult first = registry.TryReserve(pos);
            Assert.IsTrue(first.Success, "首次预留应成功。");

            int countBefore = registry.Count;
            ReservationResult second = registry.TryReserve(pos);

            Assert.IsFalse(second.Success, "重复预留应失败。");
            Assert.AreEqual(ReservationFailureReason.AlreadyReserved, second.FailureReason);
            Assert.AreEqual(countBefore, registry.Count, "Count 不变。");
            Assert.IsTrue(registry.IsReserved(pos), "格子仍被首次预留。");
        }

        // ====================================================================
        // 提交测试
        // ====================================================================

        [Test]
        [Description("Commit 释放预留，格子不再处于预留状态。")]
        public void Commit_ReleasesReservation()
        {
            var registry = new PlacementReservationRegistry();
            GridPosition pos = new GridPosition(3, 4);

            registry.TryReserve(pos);
            Assert.AreEqual(1, registry.Count, "预留后 Count=1。");

            registry.Commit(pos);

            Assert.AreEqual(0, registry.Count, "提交后 Count=0。");
            Assert.IsFalse(registry.IsReserved(pos), "提交后格子不再预留。");
        }

        [Test]
        [Description("Commit 未预留的格子为空操作（幂等）。")]
        public void Commit_Unreserved_NoOp()
        {
            var registry = new PlacementReservationRegistry();
            GridPosition pos = new GridPosition(0, 0);

            // 未预留就 Commit，不应抛异常。
            Assert.DoesNotThrow(() => registry.Commit(pos));
            Assert.AreEqual(0, registry.Count, "Count 仍为 0。");
        }

        // ====================================================================
        // 补偿回滚测试
        // ====================================================================

        [Test]
        [Description("Rollback 释放预留，格子可被再次预留。")]
        public void Rollback_ReleasesReservation_AllowsReReserve()
        {
            var registry = new PlacementReservationRegistry();
            GridPosition pos = new GridPosition(2, 5);

            registry.TryReserve(pos);
            Assert.IsTrue(registry.IsReserved(pos), "预留后格子已预留。");

            registry.Rollback(pos);

            Assert.AreEqual(0, registry.Count, "回滚后 Count=0。");
            Assert.IsFalse(registry.IsReserved(pos), "回滚后格子不再预留。");

            // 回滚后可再次预留。
            ReservationResult reReserve = registry.TryReserve(pos);
            Assert.IsTrue(reReserve.Success, "回滚后可再次预留。");
            Assert.AreEqual(1, registry.Count, "再次预留后 Count=1。");
        }

        [Test]
        [Description("Rollback 未预留的格子为空操作（幂等）。")]
        public void Rollback_Unreserved_NoOp()
        {
            var registry = new PlacementReservationRegistry();

            Assert.DoesNotThrow(() => registry.Rollback(new GridPosition(1, 1)));
            Assert.AreEqual(0, registry.Count, "Count 仍为 0。");
        }

        [Test]
        [Description("模拟创建失败后的补偿回滚：预留 → 回滚 → 格子可再次使用。")]
        public void Compensation_ReserveThenRollback_RestoresAvailability()
        {
            var registry = new PlacementReservationRegistry();
            GridPosition pos = new GridPosition(4, 6);

            // 模拟事务：预留成功 → 创建失败 → 回滚预留。
            ReservationResult reserveResult = registry.TryReserve(pos);
            Assert.IsTrue(reserveResult.Success, "预留应成功。");

            // 创建失败，回滚。
            registry.Rollback(pos);

            Assert.IsFalse(registry.IsReserved(pos), "回滚后格子可用。");
            Assert.AreEqual(0, registry.Count, "无残留预留。");
        }

        // ====================================================================
        // 清理测试
        // ====================================================================

        [Test]
        [Description("Clear 清空全部预留。")]
        public void Clear_RemovesAllReservations()
        {
            var registry = new PlacementReservationRegistry();
            registry.TryReserve(new GridPosition(1, 1));
            registry.TryReserve(new GridPosition(2, 2));
            registry.TryReserve(new GridPosition(3, 3));
            Assert.AreEqual(3, registry.Count, "三个格子预留。");

            registry.Clear();

            Assert.AreEqual(0, registry.Count, "Clear 后 Count=0。");
            Assert.IsFalse(registry.IsReserved(new GridPosition(1, 1)), "格子 1 已清除。");
            Assert.IsFalse(registry.IsReserved(new GridPosition(2, 2)), "格子 2 已清除。");
            Assert.IsFalse(registry.IsReserved(new GridPosition(3, 3)), "格子 3 已清除。");
        }

        [Test]
        [Description("Clear 幂等：空注册表 Clear 不抛异常。")]
        public void Clear_Empty_Idempotent()
        {
            var registry = new PlacementReservationRegistry();

            Assert.DoesNotThrow(() => registry.Clear());
            Assert.AreEqual(0, registry.Count, "Count 仍为 0。");
        }

        [Test]
        [Description("Clear 后可重新预留格子。")]
        public void Clear_AllowsNewReservations()
        {
            var registry = new PlacementReservationRegistry();
            GridPosition pos = new GridPosition(1, 1);

            registry.TryReserve(pos);
            registry.Clear();

            ReservationResult result = registry.TryReserve(pos);
            Assert.IsTrue(result.Success, "Clear 后可重新预留。");
        }

        // ====================================================================
        // 批量预留测试
        // ====================================================================

        [Test]
        [Description("TryReserveBatch 全部成功时批量预留。")]
        public void TryReserveBatch_AllSucceed()
        {
            var registry = new PlacementReservationRegistry();
            var positions = new GridPosition[]
            {
                new GridPosition(1, 1),
                new GridPosition(2, 2),
                new GridPosition(3, 3),
            };

            ReservationResult result = registry.TryReserveBatch(positions);

            Assert.IsTrue(result.Success, "全部应成功。");
            Assert.AreEqual(3, registry.Count, "Count=3。");
            Assert.IsTrue(registry.IsReserved(positions[0]), "格子 0 已预留。");
            Assert.IsTrue(registry.IsReserved(positions[1]), "格子 1 已预留。");
            Assert.IsTrue(registry.IsReserved(positions[2]), "格子 2 已预留。");
        }

        [Test]
        [Description("TryReserveBatch 与已预留格子冲突时全部回滚，不留下部分预留。")]
        public void TryReserveBatch_ConflictWithExisting_RollsBackAll()
        {
            var registry = new PlacementReservationRegistry();
            GridPosition existing = new GridPosition(1, 1);
            registry.TryReserve(existing);
            Assert.AreEqual(1, registry.Count, "已预留 1 个。");

            var positions = new GridPosition[]
            {
                new GridPosition(2, 2),
                existing, // 冲突
                new GridPosition(3, 3),
            };

            ReservationResult result = registry.TryReserveBatch(positions);

            Assert.IsFalse(result.Success, "冲突应返回失败。");
            Assert.AreEqual(ReservationFailureReason.AlreadyReserved, result.FailureReason);
            Assert.AreEqual(1, registry.Count, "Count 不变（回滚临时预留）。");
            Assert.IsFalse(registry.IsReserved(new GridPosition(2, 2)), "格子 (2,2) 未被预留。");
            Assert.IsFalse(registry.IsReserved(new GridPosition(3, 3)), "格子 (3,3) 未被预留。");
        }

        [Test]
        [Description("TryReserveBatch 批次内部重复时全部回滚。")]
        public void TryReserveBatch_InternalDuplicate_RollsBackAll()
        {
            var registry = new PlacementReservationRegistry();
            GridPosition dup = new GridPosition(1, 1);

            var positions = new GridPosition[]
            {
                dup,
                new GridPosition(2, 2),
                dup, // 批次内重复
            };

            ReservationResult result = registry.TryReserveBatch(positions);

            Assert.IsFalse(result.Success, "批次内重复应失败。");
            Assert.AreEqual(0, registry.Count, "全部回滚，Count=0。");
            Assert.IsFalse(registry.IsReserved(dup), "重复格子未被预留。");
            Assert.IsFalse(registry.IsReserved(new GridPosition(2, 2)), "非重复格子也未被预留。");
        }

        // ====================================================================
        // 批量回滚测试
        // ====================================================================

        [Test]
        [Description("RollbackBatch 释放多个格子预留。")]
        public void RollbackBatch_ReleasesMultiple()
        {
            var registry = new PlacementReservationRegistry();
            var positions = new GridPosition[]
            {
                new GridPosition(1, 1),
                new GridPosition(2, 2),
                new GridPosition(3, 3),
            };

            registry.TryReserveBatch(positions);
            Assert.AreEqual(3, registry.Count, "三个格子预留。");

            registry.RollbackBatch(positions);

            Assert.AreEqual(0, registry.Count, "全部回滚后 Count=0。");
        }

        [Test]
        [Description("RollbackBatch null 参数为空操作。")]
        public void RollbackBatch_Null_NoOp()
        {
            var registry = new PlacementReservationRegistry();
            registry.TryReserve(new GridPosition(1, 1));

            Assert.DoesNotThrow(() => registry.RollbackBatch(null));
            Assert.AreEqual(1, registry.Count, "Count 不变。");
        }

        // ====================================================================
        // IsReserved 查询测试
        // ====================================================================

        [Test]
        [Description("IsReserved 正确报告预留状态。")]
        public void IsReserved_ReportsCorrectStatus()
        {
            var registry = new PlacementReservationRegistry();
            GridPosition pos = new GridPosition(5, 5);

            Assert.IsFalse(registry.IsReserved(pos), "未预留时返回 false。");

            registry.TryReserve(pos);
            Assert.IsTrue(registry.IsReserved(pos), "预留后返回 true。");

            registry.Rollback(pos);
            Assert.IsFalse(registry.IsReserved(pos), "回滚后返回 false。");
        }

        // ====================================================================
        // 确定性测试
        // ====================================================================

        [Test]
        [Description("相同操作序列在新注册表上产生相同状态（确定性）。")]
        public void Deterministic_SameSequence_SameState()
        {
            // 第一局。
            var reg1 = new PlacementReservationRegistry();
            reg1.TryReserve(new GridPosition(1, 1));
            reg1.TryReserve(new GridPosition(2, 2));
            reg1.Commit(new GridPosition(1, 1));
            reg1.Rollback(new GridPosition(2, 2));

            // 第二局（重开）。
            var reg2 = new PlacementReservationRegistry();
            reg2.TryReserve(new GridPosition(1, 1));
            reg2.TryReserve(new GridPosition(2, 2));
            reg2.Commit(new GridPosition(1, 1));
            reg2.Rollback(new GridPosition(2, 2));

            Assert.AreEqual(reg1.Count, reg2.Count, "Count 一致。");
            Assert.AreEqual(reg1.IsReserved(new GridPosition(1, 1)), reg2.IsReserved(new GridPosition(1, 1)), "格子状态一致。");
        }

        // ====================================================================
        // 综合事务模拟测试
        // ====================================================================

        [Test]
        [Description("模拟购买放置事务：预留 → 提交（成功路径），无残留预留。")]
        public void Transaction_SuccessPath_ReserveThenCommit()
        {
            var registry = new PlacementReservationRegistry();
            GridPosition pos = new GridPosition(3, 7);

            // 预留。
            ReservationResult reserve = registry.TryReserve(pos);
            Assert.IsTrue(reserve.Success, "预留成功。");
            Assert.AreEqual(1, registry.Count, "预留后 Count=1。");

            // 事务成功，提交。
            registry.Commit(pos);
            Assert.AreEqual(0, registry.Count, "提交后无残留。");
            Assert.IsFalse(registry.IsReserved(pos), "提交后格子不再预留。");
        }

        [Test]
        [Description("模拟购买放置事务：预留 → 回滚（失败路径），无残留预留。")]
        public void Transaction_FailurePath_ReserveThenRollback()
        {
            var registry = new PlacementReservationRegistry();
            GridPosition pos = new GridPosition(3, 7);

            // 预留。
            ReservationResult reserve = registry.TryReserve(pos);
            Assert.IsTrue(reserve.Success, "预留成功。");
            Assert.AreEqual(1, registry.Count, "预留后 Count=1。");

            // 事务失败，回滚。
            registry.Rollback(pos);
            Assert.AreEqual(0, registry.Count, "回滚后无残留。");
            Assert.IsFalse(registry.IsReserved(pos), "回滚后格子不再预留。");
        }

        [Test]
        [Description("模拟 Settling 清理：多个活动预留 Clear 后全部释放。")]
        public void Settling_Clear_RemovesAllActiveReservations()
        {
            var registry = new PlacementReservationRegistry();
            registry.TryReserve(new GridPosition(1, 1));
            registry.TryReserve(new GridPosition(2, 2));
            registry.TryReserve(new GridPosition(3, 3));
            registry.TryReserve(new GridPosition(4, 4));
            Assert.AreEqual(4, registry.Count, "4 个活动预留。");

            // Settling 静默清理。
            registry.Clear();

            Assert.AreEqual(0, registry.Count, "清理后无残留。");
        }
    }
}
