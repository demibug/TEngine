using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Input
{
    /// <summary>
    /// BattleInputCommand / BattleInputResult 单元测试（最终方案：Recruit / DropUnit）。
    /// </summary>
    /// <remarks>
    /// <para>验证内容（最终方案"输入层只提交两个命令"）：</para>
    /// <list type="bullet">
    /// <item>不可变性测试：命令与结果构造后字段不可修改。</item>
    /// <item>CommandId 唯一性/相等性测试：同 ID 同 payload 相等，不同 ID 或不同 payload 不等。</item>
    /// <item>命令类型覆盖：征兵（Recruit）和换槽/合并（DropUnit）两种命令可正确构造。</item>
    /// <item>Result 成功/失败状态测试：Ok/Fail 构造与 IsSuccess/RejectReason 判断。</item>
    /// </list>
    ///
    /// <para>决策 0.8 语义验证：CommandId 作为单局唯一标识，同 ID 重复提交返回首次结果。
    /// 本测试只验证命令/结果数据结构的字段语义，去重缓存逻辑由 BattleInputController 测试覆盖。</para>
    /// </remarks>
    [TestFixture]
    internal class BattleInputCommandTests
    {
        // ====================================================================
        // 命令类型覆盖测试
        // ====================================================================

        [Test]
        [Description("CreateRecruit 构造征兵命令，CommandType 为 Recruit 且载荷正确。")]
        public void CreateRecruit_ConstructsCorrectTypeAndPayload()
        {
            var cmd = BattleInputCommand.CreateRecruit(commandId: 42, playerSide: true);

            Assert.AreEqual(42, cmd.CommandId, "CommandId 应为 42。");
            Assert.AreEqual(BattleInputCommandType.Recruit, cmd.CommandType, "CommandType 应为 Recruit。");
            Assert.IsTrue(cmd.RecruitPayload.PlayerSide, "PlayerSide 应为 true。");
        }

        [Test]
        [Description("CreateDropUnit 构造换槽/合并命令，CommandType 为 DropUnit 且载荷正确。")]
        public void CreateDropUnit_ConstructsCorrectTypeAndPayload()
        {
            var cmd = BattleInputCommand.CreateDropUnit(commandId: 7, sourceSlotId: 3, targetSlotId: 9);

            Assert.AreEqual(7, cmd.CommandId, "CommandId 应为 7。");
            Assert.AreEqual(BattleInputCommandType.DropUnit, cmd.CommandType, "CommandType 应为 DropUnit。");
            Assert.AreEqual(3, cmd.DropUnitPayload.SourceSlotId, "SourceSlotId 应为 3。");
            Assert.AreEqual(9, cmd.DropUnitPayload.TargetSlotId, "TargetSlotId 应为 9。");
        }

        [Test]
        [Description("Recruit 命令的 DropUnitPayload 为默认值，DropUnit 命令的 RecruitPayload 为默认值。")]
        public void CreateRecruit_DropUnitPayloadIsDefault_AndViceVersa()
        {
            var recruit = BattleInputCommand.CreateRecruit(1, true);
            Assert.AreEqual(default(DropUnitPayload), recruit.DropUnitPayload,
                "Recruit 命令的 DropUnitPayload 应为默认值。");

            var drop = BattleInputCommand.CreateDropUnit(2, 3, 4);
            Assert.AreEqual(default(RecruitPayload), drop.RecruitPayload,
                "DropUnit 命令的 RecruitPayload 应为默认值。");
        }

        // ====================================================================
        // 不可变性测试
        // ====================================================================

        [Test]
        [Description("BattleInputCommand 为 readonly struct：构造后字段值固定，无法通过任何 API 修改。")]
        public void BattleInputCommand_IsImmutable_AfterConstruction()
        {
            var cmd = BattleInputCommand.CreateDropUnit(commandId: 10, sourceSlotId: 2, targetSlotId: 4);

            // readonly struct 的字段在构造后不可修改——编译期保证。
            Assert.AreEqual(10, cmd.CommandId, "CommandId 不变。");
            Assert.AreEqual(BattleInputCommandType.DropUnit, cmd.CommandType, "CommandType 不变。");
            Assert.AreEqual(2, cmd.DropUnitPayload.SourceSlotId, "SourceSlotId 不变。");
            Assert.AreEqual(4, cmd.DropUnitPayload.TargetSlotId, "TargetSlotId 不变。");
        }

        [Test]
        [Description("BattleInputResult 为 readonly struct：Ok/Fail 构造后字段值固定。")]
        public void BattleInputResult_IsImmutable_AfterConstruction()
        {
            var ok = BattleInputResult.Ok(15);
            Assert.AreEqual(15, ok.CommandId, "Ok 的 CommandId 固定。");
            Assert.AreEqual(BattleInputRejectReason.None, ok.RejectReason, "Ok 的 RejectReason 固定为 None。");
            Assert.AreEqual(string.Empty, ok.DiagnosticMessage, "Ok 的 DiagnosticMessage 固定为空串。");

            var fail = BattleInputResult.Fail(16, BattleInputRejectReason.InsufficientGoldForRecruit, "馒头不足");
            Assert.AreEqual(16, fail.CommandId, "Fail 的 CommandId 固定。");
            Assert.AreEqual(BattleInputRejectReason.InsufficientGoldForRecruit, fail.RejectReason, "Fail 的 RejectReason 固定。");
            Assert.AreEqual("馒头不足", fail.DiagnosticMessage, "Fail 的 DiagnosticMessage 固定。");
        }

        [Test]
        [Description("BattleInputResult.Fail 的 diagnosticMessage 为 null 时规范化为空串。")]
        public void BattleInputResult_Fail_NullDiagnostic_NormalizedToEmpty()
        {
            var fail = BattleInputResult.Fail(1, BattleInputRejectReason.InvalidTargetSlot, null);

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
            var cmd1 = BattleInputCommand.CreateDropUnit(100, 1, 2);
            var cmd2 = BattleInputCommand.CreateDropUnit(100, 1, 2);

            Assert.IsTrue(cmd1.Equals(cmd2), "相同 ID 和 payload 的命令应相等。");
            Assert.IsTrue(cmd1 == cmd2, "== 运算符应返回 true。");
            Assert.IsFalse(cmd1 != cmd2, "!= 运算符应返回 false。");
            Assert.AreEqual(cmd1.GetHashCode(), cmd2.GetHashCode(), "相等命令的 GetHashCode 应一致。");
        }

        [Test]
        [Description("不同 CommandId 的命令不相等（决策 0.8：不同 ID 按独立命令处理）。")]
        public void DifferentCommandId_AreNotEqual_EvenWithSamePayload()
        {
            var cmd1 = BattleInputCommand.CreateDropUnit(100, 1, 2);
            var cmd2 = BattleInputCommand.CreateDropUnit(200, 1, 2);

            Assert.IsFalse(cmd1.Equals(cmd2), "不同 ID 的命令不应相等，即使 payload 相同。");
            Assert.IsFalse(cmd1 == cmd2, "== 运算符应返回 false。");
            Assert.IsTrue(cmd1 != cmd2, "!= 运算符应返回 true。");
        }

        [Test]
        [Description("相同 CommandId 但不同 payload 的命令不相等。")]
        public void SameCommandId_DifferentPayload_AreNotEqual()
        {
            var cmd1 = BattleInputCommand.CreateDropUnit(100, 1, 2);
            var cmd2 = BattleInputCommand.CreateDropUnit(100, 1, 3);

            Assert.IsFalse(cmd1.Equals(cmd2), "相同 ID 但不同 TargetSlotId 的命令不应相等。");
        }

        [Test]
        [Description("相同 CommandId 但不同命令类型的命令不相等。")]
        public void SameCommandId_DifferentType_AreNotEqual()
        {
            var recruit = BattleInputCommand.CreateRecruit(50, true);
            var drop = BattleInputCommand.CreateDropUnit(50, 1, 2);

            Assert.IsFalse(recruit.Equals(drop), "相同 ID 但不同类型的命令不应相等。");
        }

        [Test]
        [Description("Recruit 命令的 CommandId 相等性：同 ID 同阵营相等，不同 ID 不等。")]
        public void RecruitCommand_CommandIdEquality()
        {
            var r1 = BattleInputCommand.CreateRecruit(30, true);
            var r2 = BattleInputCommand.CreateRecruit(30, true);
            var r3 = BattleInputCommand.CreateRecruit(31, true);

            Assert.IsTrue(r1.Equals(r2), "同 ID 同阵营的 Recruit 命令相等。");
            Assert.IsFalse(r1.Equals(r3), "不同 ID 的 Recruit 命令不等。");
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
            var result = BattleInputResult.Fail(6, BattleInputRejectReason.SourceSlotEmpty);

            Assert.IsFalse(result.IsSuccess, "Fail 结果 IsSuccess 应为 false。");
            Assert.AreEqual(BattleInputRejectReason.SourceSlotEmpty, result.RejectReason,
                "RejectReason 应为 SourceSlotEmpty。");
            Assert.AreEqual(6, result.CommandId, "CommandId 应为 6。");
        }

        [Test]
        [Description("不同拒绝原因的失败结果可以通过 RejectReason 程序化区分。")]
        public void Fail_DifferentRejectReasons_AreDistinguishable()
        {
            var insufficient = BattleInputResult.Fail(1, BattleInputRejectReason.InsufficientGoldForRecruit);
            var mismatch = BattleInputResult.Fail(1, BattleInputRejectReason.TargetMismatch);
            var maxLevel = BattleInputResult.Fail(1, BattleInputRejectReason.MaxLevelReached);

            Assert.IsFalse(insufficient.IsSuccess, "InsufficientGoldForRecruit 失败。");
            Assert.IsFalse(mismatch.IsSuccess, "TargetMismatch 失败。");
            Assert.IsFalse(maxLevel.IsSuccess, "MaxLevelReached 失败。");

            Assert.AreNotEqual(insufficient.RejectReason, mismatch.RejectReason, "不同拒绝原因可区分。");
            Assert.AreNotEqual(mismatch.RejectReason, maxLevel.RejectReason, "不同拒绝原因可区分。");
        }

        [Test]
        [Description("BattleInputResult 相等性：同 CommandId、同 RejectReason、同 DiagnosticMessage 相等。")]
        public void BattleInputResult_Equality()
        {
            var r1 = BattleInputResult.Fail(10, BattleInputRejectReason.InvalidTargetSlot, "越界");
            var r2 = BattleInputResult.Fail(10, BattleInputRejectReason.InvalidTargetSlot, "越界");
            var r3 = BattleInputResult.Fail(10, BattleInputRejectReason.InvalidTargetSlot, "其他");
            var r4 = BattleInputResult.Fail(11, BattleInputRejectReason.InvalidTargetSlot, "越界");

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
            var fail = BattleInputResult.Fail(6, BattleInputRejectReason.InsufficientGoldForRecruit, "馒头不足");

            StringAssert.Contains("Success", ok.ToString(), "Ok ToString 含 Success。");
            StringAssert.Contains("5", ok.ToString(), "Ok ToString 含 CommandId。");

            StringAssert.Contains("Failed", fail.ToString(), "Fail ToString 含 Failed。");
            StringAssert.Contains("InsufficientGoldForRecruit", fail.ToString(), "Fail ToString 含 RejectReason。");
            StringAssert.Contains("6", fail.ToString(), "Fail ToString 含 CommandId。");
        }

        [Test]
        [Description("命令 ToString 按类型显示对应载荷信息（仅用于日志）。")]
        public void Command_ToString_ByType()
        {
            var recruit = BattleInputCommand.CreateRecruit(1, true);
            var drop = BattleInputCommand.CreateDropUnit(5, 2, 3);

            StringAssert.Contains("Recruit", recruit.ToString(), "Recruit 命令 ToString 含 Recruit。");
            StringAssert.Contains("DropUnit", drop.ToString(), "DropUnit 命令 ToString 含 DropUnit。");
        }

        // ====================================================================
        // 决策 0.8 语义：CommandId 作为单局唯一标识的数据结构就绪性
        // ====================================================================

        [Test]
        [Description("决策 0.8：CommandId 字段存在于命令和结果中，为去重缓存提供数据基础。")]
        public void CommandId_PresentInBoth_CommandAndResult()
        {
            // 命令携带 CommandId，由调用方在主线程构造时赋值。
            var cmd = BattleInputCommand.CreateRecruit(999, true);
            Assert.AreEqual(999, cmd.CommandId, "命令携带 CommandId。");

            // 结果携带同一 CommandId，便于调用方对应请求与缓存首次结果。
            var result = BattleInputResult.Ok(999);
            Assert.AreEqual(999, result.CommandId, "结果携带同一 CommandId。");

            var failResult = BattleInputResult.Fail(999, BattleInputRejectReason.SourceSlotEmpty);
            Assert.AreEqual(999, failResult.CommandId, "失败结果也携带同一 CommandId。");
        }

        [Test]
        [Description("决策 0.8：不同 CommandId 即使 payload 完全相同也构造为独立命令实例。")]
        public void DifferentCommandId_SamePayload_AreIndependentCommands()
        {
            var cmdA = BattleInputCommand.CreateDropUnit(100, 1, 2);
            var cmdB = BattleInputCommand.CreateDropUnit(101, 1, 2);

            // 不同 ID 是独立命令，不相等。
            Assert.AreNotEqual(cmdA, cmdB, "不同 CommandID 的命令是独立命令。");
            Assert.AreNotEqual(cmdA.GetHashCode(), cmdB.GetHashCode(),
                "独立命令的 GetHashCode 不同（不保证，但此处 ID 不同通常不同）。");
        }
    }
}
