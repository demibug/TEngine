using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Input
{
    /// <summary>
    /// BattleInputCommand / BattleInputResult 单元测试（task 6.6）。
    /// </summary>
    /// <remarks>
    /// <para>验证要求（task 6.6）：</para>
    /// <list type="bullet">
    /// <item>不可变性测试：命令与结果构造后字段不可修改。</item>
    /// <item>CommandId 唯一性/相等性测试：同 ID 同 payload 相等，不同 ID 或不同 payload 不等。</item>
    /// <item>命令类型覆盖：购买放置（BuyPlace）和刷新（Refresh）两种命令可正确构造。</item>
    /// <item>Result 成功/失败状态测试：Ok/Fail 构造与 IsSuccess/RejectReason 判断。</item>
    /// </list>
    ///
    /// <para>决策 0.8 语义验证：CommandId 作为单局唯一标识，同 ID 重复提交返回首次结果。
    /// 本测试只验证命令/结果数据结构的字段语义，去重缓存逻辑由 task 6.8 的
    /// BattleInputController 测试覆盖。</para>
    /// </remarks>
    [TestFixture]
    internal class BattleInputCommandTests
    {
        // ====================================================================
        // 命令类型覆盖测试
        // ====================================================================

        [Test]
        [Description("CreateBuyPlace 构造购买放置命令，CommandType 为 BuyPlace 且载荷正确。")]
        public void CreateBuyPlace_ConstructsCorrectTypeAndPayload()
        {
            var pos = new GridPosition(3, 5);
            var cmd = BattleInputCommand.CreateBuyPlace(
                commandId: 42,
                playerSide: true,
                slot: 1,
                position: pos);

            Assert.AreEqual(42, cmd.CommandId, "CommandId 应为 42。");
            Assert.AreEqual(BattleInputCommandType.BuyPlace, cmd.CommandType, "CommandType 应为 BuyPlace。");
            Assert.IsTrue(cmd.BuyPlacePayload.PlayerSide, "PlayerSide 应为 true。");
            Assert.AreEqual(1, cmd.BuyPlacePayload.Slot, "Slot 应为 1。");
            Assert.AreEqual(pos, cmd.BuyPlacePayload.Position, "Position 应为 (3, 5)。");
        }

        [Test]
        [Description("CreateRefresh 构造刷新命令，CommandType 为 Refresh 且载荷正确。")]
        public void CreateRefresh_ConstructsCorrectTypeAndPayload()
        {
            var cmd = BattleInputCommand.CreateRefresh(
                commandId: 7,
                playerSide: false);

            Assert.AreEqual(7, cmd.CommandId, "CommandId 应为 7。");
            Assert.AreEqual(BattleInputCommandType.Refresh, cmd.CommandType, "CommandType 应为 Refresh。");
            Assert.IsFalse(cmd.RefreshPayload.PlayerSide, "PlayerSide 应为 false（对手方）。");
        }

        [Test]
        [Description("BuyPlace 命令的 RefreshPayload 为默认值，Refresh 命令的 BuyPlacePayload 为默认值。")]
        public void CreateBuyPlace_RefreshPayloadIsDefault_AndViceVersa()
        {
            var buyPlace = BattleInputCommand.CreateBuyPlace(1, true, 0, new GridPosition(0, 0));
            Assert.AreEqual(default(RefreshPayload), buyPlace.RefreshPayload,
                "BuyPlace 命令的 RefreshPayload 应为默认值。");

            var refresh = BattleInputCommand.CreateRefresh(2, true);
            Assert.AreEqual(default(BuyPlacePayload), refresh.BuyPlacePayload,
                "Refresh 命令的 BuyPlacePayload 应为默认值。");
        }

        // ====================================================================
        // 不可变性测试
        // ====================================================================

        [Test]
        [Description("BattleInputCommand 为 readonly struct：构造后字段值固定，无法通过任何 API 修改。")]
        public void BattleInputCommand_IsImmutable_AfterConstruction()
        {
            var pos = new GridPosition(2, 4);
            var cmd = BattleInputCommand.CreateBuyPlace(
                commandId: 10,
                playerSide: true,
                slot: 2,
                position: pos);

            // readonly struct 的字段在构造后不可修改——编译期保证。
            // 运行期验证：字段值与构造时一致，不被后续操作改变。
            Assert.AreEqual(10, cmd.CommandId, "CommandId 不变。");
            Assert.AreEqual(BattleInputCommandType.BuyPlace, cmd.CommandType, "CommandType 不变。");
            Assert.AreEqual(2, cmd.BuyPlacePayload.Slot, "Slot 不变。");
            Assert.AreEqual(pos, cmd.BuyPlacePayload.Position, "Position 不变。");

            // GridPosition 本身也是 readonly struct，不可变。
            var posField = cmd.BuyPlacePayload.Position;
            Assert.AreEqual(2, posField.X, "Position.X 不变。");
            Assert.AreEqual(4, posField.Y, "Position.Y 不变。");
        }

        [Test]
        [Description("BattleInputResult 为 readonly struct：Ok/Fail 构造后字段值固定。")]
        public void BattleInputResult_IsImmutable_AfterConstruction()
        {
            var ok = BattleInputResult.Ok(15);
            Assert.AreEqual(15, ok.CommandId, "Ok 的 CommandId 固定。");
            Assert.AreEqual(BattleInputRejectReason.None, ok.RejectReason, "Ok 的 RejectReason 固定为 None。");
            Assert.AreEqual(string.Empty, ok.DiagnosticMessage, "Ok 的 DiagnosticMessage 固定为空串。");

            var fail = BattleInputResult.Fail(16, BattleInputRejectReason.InsufficientGold, "金币不足");
            Assert.AreEqual(16, fail.CommandId, "Fail 的 CommandId 固定。");
            Assert.AreEqual(BattleInputRejectReason.InsufficientGold, fail.RejectReason, "Fail 的 RejectReason 固定。");
            Assert.AreEqual("金币不足", fail.DiagnosticMessage, "Fail 的 DiagnosticMessage 固定。");
        }

        [Test]
        [Description("BattleInputResult.Fail 的 diagnosticMessage 为 null 时规范化为空串。")]
        public void BattleInputResult_Fail_NullDiagnostic_NormalizedToEmpty()
        {
            var fail = BattleInputResult.Fail(1, BattleInputRejectReason.InvalidCell, null);

            Assert.IsNotNull((object)fail.DiagnosticMessage, "null 诊断信息被规范化为非 null。");
            Assert.AreEqual(string.Empty, fail.DiagnosticMessage, "null 诊断信息被规范化为空串。");
        }

        // ====================================================================
        // CommandId 唯一性/相等性测试（决策 0.8）
        // ====================================================================

        [Test]
        [Description("相同 CommandId 和相同 payload 的命令相等（决策 0.8：同一 ID 语义）。")]
        public void SameCommandId_AndSamePayload_AreEqual()
        {
            var pos = new GridPosition(1, 1);
            var cmd1 = BattleInputCommand.CreateBuyPlace(100, true, 0, pos);
            var cmd2 = BattleInputCommand.CreateBuyPlace(100, true, 0, pos);

            Assert.IsTrue(cmd1.Equals(cmd2), "相同 ID 和 payload 的命令应相等。");
            Assert.IsTrue(cmd1 == cmd2, "== 运算符应返回 true。");
            Assert.IsFalse(cmd1 != cmd2, "!= 运算符应返回 false。");
            Assert.AreEqual(cmd1.GetHashCode(), cmd2.GetHashCode(), "相等命令的 GetHashCode 应一致。");
        }

        [Test]
        [Description("不同 CommandId 的命令不相等（决策 0.8：不同 ID 按独立命令处理）。")]
        public void DifferentCommandId_AreNotEqual_EvenWithSamePayload()
        {
            var pos = new GridPosition(1, 1);
            var cmd1 = BattleInputCommand.CreateBuyPlace(100, true, 0, pos);
            var cmd2 = BattleInputCommand.CreateBuyPlace(200, true, 0, pos);

            Assert.IsFalse(cmd1.Equals(cmd2), "不同 ID 的命令不应相等，即使 payload 相同。");
            Assert.IsFalse(cmd1 == cmd2, "== 运算符应返回 false。");
            Assert.IsTrue(cmd1 != cmd2, "!= 运算符应返回 true。");
        }

        [Test]
        [Description("相同 CommandId 但不同 payload 的命令不相等。")]
        public void SameCommandId_DifferentPayload_AreNotEqual()
        {
            var cmd1 = BattleInputCommand.CreateBuyPlace(100, true, 0, new GridPosition(1, 1));
            var cmd2 = BattleInputCommand.CreateBuyPlace(100, true, 1, new GridPosition(1, 1));

            Assert.IsFalse(cmd1.Equals(cmd2), "相同 ID 但不同 Slot 的命令不应相等。");
        }

        [Test]
        [Description("相同 CommandId 但不同命令类型的命令不相等。")]
        public void SameCommandId_DifferentType_AreNotEqual()
        {
            var buyPlace = BattleInputCommand.CreateBuyPlace(50, true, 0, new GridPosition(0, 0));
            var refresh = BattleInputCommand.CreateRefresh(50, true);

            Assert.IsFalse(buyPlace.Equals(refresh), "相同 ID 但不同类型的命令不应相等。");
        }

        [Test]
        [Description("Refresh 命令的 CommandId 相等性：同 ID 同阵营相等，不同 ID 不等。")]
        public void RefreshCommand_CommandIdEquality()
        {
            var r1 = BattleInputCommand.CreateRefresh(30, true);
            var r2 = BattleInputCommand.CreateRefresh(30, true);
            var r3 = BattleInputCommand.CreateRefresh(31, true);

            Assert.IsTrue(r1.Equals(r2), "同 ID 同阵营的 Refresh 命令相等。");
            Assert.IsFalse(r1.Equals(r3), "不同 ID 的 Refresh 命令不等。");
        }

        // ====================================================================
        // Result 成功/失败状态测试
        // ====================================================================

        [Test]
        [Description("Ok 构造成功结果：IsSuccess 为 true，RejectReason 为 None。")]
        public void Ok_IsSuccess_True()
        {
            var result = BattleInputResult.Ok(5);

            Assert.IsTrue(result.IsSuccess, "Ok 结果 IsSuccess 应为 true。");
            Assert.AreEqual(BattleInputRejectReason.None, result.RejectReason, "Ok 结果 RejectReason 应为 None。");
            Assert.AreEqual(5, result.CommandId, "CommandId 应为 5。");
            Assert.AreEqual(string.Empty, result.DiagnosticMessage, "Ok 结果 DiagnosticMessage 应为空串。");
        }

        [Test]
        [Description("Fail 构造失败结果：IsSuccess 为 false，RejectReason 为指定原因。")]
        public void Fail_IsSuccess_False()
        {
            var result = BattleInputResult.Fail(6, BattleInputRejectReason.InsufficientGold);

            Assert.IsFalse(result.IsSuccess, "Fail 结果 IsSuccess 应为 false。");
            Assert.AreEqual(BattleInputRejectReason.InsufficientGold, result.RejectReason,
                "RejectReason 应为 InsufficientGold。");
            Assert.AreEqual(6, result.CommandId, "CommandId 应为 6。");
        }

        [Test]
        [Description("不同拒绝原因的失败结果可以通过 RejectReason 程序化区分。")]
        public void Fail_DifferentRejectReasons_AreDistinguishable()
        {
            var insufficientGold = BattleInputResult.Fail(1, BattleInputRejectReason.InsufficientGold);
            var invalidCell = BattleInputResult.Fail(1, BattleInputRejectReason.InvalidCell);
            var cellReserved = BattleInputResult.Fail(1, BattleInputRejectReason.CellReserved);

            Assert.IsFalse(insufficientGold.IsSuccess, "InsufficientGold 失败。");
            Assert.IsFalse(invalidCell.IsSuccess, "InvalidCell 失败。");
            Assert.IsFalse(cellReserved.IsSuccess, "CellReserved 失败。");

            Assert.AreNotEqual(insufficientGold.RejectReason, invalidCell.RejectReason,
                "不同拒绝原因可区分。");
            Assert.AreNotEqual(invalidCell.RejectReason, cellReserved.RejectReason,
                "不同拒绝原因可区分。");
        }

        [Test]
        [Description("BattleInputResult 相等性：同 CommandId、同 RejectReason、同 DiagnosticMessage 相等。")]
        public void BattleInputResult_Equality()
        {
            var r1 = BattleInputResult.Fail(10, BattleInputRejectReason.InvalidCell, "越界");
            var r2 = BattleInputResult.Fail(10, BattleInputRejectReason.InvalidCell, "越界");
            var r3 = BattleInputResult.Fail(10, BattleInputRejectReason.InvalidCell, "其他");
            var r4 = BattleInputResult.Fail(11, BattleInputRejectReason.InvalidCell, "越界");

            Assert.IsTrue(r1.Equals(r2), "同 CommandId/Reason/Msg 相等。");
            Assert.IsTrue(r1 == r2, "== 运算符返回 true。");
            Assert.IsFalse(r1.Equals(r3), "不同 DiagnosticMessage 不等。");
            Assert.IsFalse(r1.Equals(r4), "不同 CommandId 不等。");
        }

        [Test]
        [Description("Ok 和 Fail 的 ToString 包含关键信息（仅用于日志，不用于程序化判断）。")]
        public void ToString_ContainsKeyInfo_ForLoggingOnly()
        {
            var ok = BattleInputResult.Ok(5);
            var fail = BattleInputResult.Fail(6, BattleInputRejectReason.InsufficientGold, "金币不足");

            StringAssert.Contains("Success", ok.ToString(), "Ok ToString 含 Success。");
            StringAssert.Contains("5", ok.ToString(), "Ok ToString 含 CommandId。");

            StringAssert.Contains("Failed", fail.ToString(), "Fail ToString 含 Failed。");
            StringAssert.Contains("InsufficientGold", fail.ToString(), "Fail ToString 含 RejectReason。");
            StringAssert.Contains("6", fail.ToString(), "Fail ToString 含 CommandId。");
        }

        [Test]
        [Description("命令 ToString 按类型显示对应载荷信息（仅用于日志）。")]
        public void Command_ToString_ByType()
        {
            var buyPlace = BattleInputCommand.CreateBuyPlace(1, true, 2, new GridPosition(3, 4));
            var refresh = BattleInputCommand.CreateRefresh(5, false);

            StringAssert.Contains("BuyPlace", buyPlace.ToString(), "BuyPlace 命令 ToString 含 BuyPlace。");
            StringAssert.Contains("Refresh", refresh.ToString(), "Refresh 命令 ToString 含 Refresh。");
        }

        // ====================================================================
        // 决策 0.8 语义：CommandId 作为单局唯一标识的数据结构就绪性
        // ====================================================================

        [Test]
        [Description("决策 0.8：CommandId 字段存在于命令和结果中，为去重缓存提供数据基础。")]
        public void CommandId_PresentInBoth_CommandAndResult()
        {
            // 命令携带 CommandId，由调用方在主线程构造时赋值。
            var cmd = BattleInputCommand.CreateBuyPlace(999, true, 0, new GridPosition(0, 0));
            Assert.AreEqual(999, cmd.CommandId, "命令携带 CommandId。");

            // 结果携带同一 CommandId，便于调用方对应请求与缓存首次结果。
            var result = BattleInputResult.Ok(999);
            Assert.AreEqual(999, result.CommandId, "结果携带同一 CommandId。");

            var failResult = BattleInputResult.Fail(999, BattleInputRejectReason.InsufficientGold);
            Assert.AreEqual(999, failResult.CommandId, "失败结果也携带同一 CommandId。");
        }

        [Test]
        [Description("决策 0.8：不同 CommandId 即使 payload 完全相同也构造为独立命令实例。")]
        public void DifferentCommandId_SamePayload_AreIndependentCommands()
        {
            var pos = new GridPosition(1, 2);
            var cmdA = BattleInputCommand.CreateBuyPlace(100, true, 0, pos);
            var cmdB = BattleInputCommand.CreateBuyPlace(101, true, 0, pos);

            // 不同 ID 是独立命令，不相等。
            Assert.AreNotEqual(cmdA, cmdB, "不同 CommandID 的命令是独立命令。");
            Assert.AreNotEqual(cmdA.GetHashCode(), cmdB.GetHashCode(),
                "独立命令的 GetHashCode 不同（不保证，但此处 ID 不同通常不同）。");
        }
    }
}
