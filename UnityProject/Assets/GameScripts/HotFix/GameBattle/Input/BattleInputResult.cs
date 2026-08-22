namespace GameBattle
{
    // ============================================================================
    // 任务 6.6：BattleInputResult —— 输入命令执行结果
    // ----------------------------------------------------------------------------
    // 职责（design.md:207 / specs/battle-simulation/spec.md "Input commands are atomic"）：
    //   定义输入命令执行后的成功/失败状态与拒绝原因，不用异常表达正常校验失败
    //   （design.md:207："不用异常表达正常校验失败"）。
    //
    //   本类型是 BattleInputController（task 6.7）执行命令后返回的不可变结果。
    //   成功时携带执行的 CommandId 以便调用方对应请求；失败时携带拒绝原因枚举与诊断信息。
    //
    //   决策 0.8：同一 CommandId 重复提交返回首次结果。BattleInputController 在 Runtime
    //   生命周期内缓存首次结果，重复提交直接返回缓存的 BattleInputResult，不重新执行。
    //   本类型只定义结果结构，不承担缓存逻辑。
    //
    // 不可变性：
    //   1. readonly struct，全部字段 readonly，构造后不可修改。
    //   2. 不持有可变集合或可变 object 引用。
    //   3. 诊断信息字符串不为 null（规范化为空串，参照 BattleOperationResult 约定）。
    //
    // 复用：
    //   - 拒绝原因枚举参照 BattleErrorCode（task 2.4）的稳定错误码模式：已发布枚举值名称
    //     与数值不得变更，新增只能追加到末尾，调用方以此做程序化判断。
    //   - 诊断信息字段参照 BattleOperationResult.DiagnosticMessage 约定：仅用于日志/诊断，
    //     调用方 MUST NOT 解析文本判断失败原因。
    //   - 成功判断参照 BattleOperationResult.IsSuccess 模式。
    // ============================================================================

    /// <summary>
    /// 输入命令拒绝原因稳定枚举。
    /// </summary>
    /// <remarks>
    /// <para><b>设计依据（design.md:207 / spec "Input commands are atomic"）：</b></para>
    /// <para>正常校验失败（余额不足、非法槽位、目标不匹配等）MUST 返回结构化结果而非异常。
    /// 本枚举提供稳定、可编程判断的拒绝原因，覆盖本期征兵和换槽/合并命令的可能失败路径。</para>
    ///
    /// <para><b>稳定性约束（参照 BattleErrorCode）：</b></para>
    /// <list type="bullet">
    /// <item>已发布枚举值名称与数值不得变更，调用方以此做程序化判断。</item>
    /// <item>新增拒绝原因只能追加到末尾，不得插入或重排已有值。</item>
    /// <item><see cref="None"/> 固定为 0，表示成功；调用方应优先判断 <c>IsSuccess</c>。</item>
    /// </list>
    ///
    /// <para><b>与 BattleErrorCode 的关系：</b></para>
    /// <para>BattleErrorCode 覆盖模块级生命周期/配置/资源错误（task 2.4），本枚举覆盖单条输入
    /// 命令的执行级校验失败。两者独立，不互相复用，因为输入命令的正常校验失败不属于
    /// 模块级错误（design.md:207 明确"不用异常表达正常校验失败"，输入校验失败是预期内的
    /// 正常结果，不是模块故障）。</para>
    /// </remarks>
    public enum BattleInputRejectReason
    {
        /// <summary>
        /// 无拒绝（成功）。固定为 0。
        /// </summary>
        None = 0,

        // ====================================================================
        // 购买放置命令失败原因（task 6.7 原子事务步骤）
        // ====================================================================

        /// <summary>
        /// 金币不足，无法支付购买费用。
        /// <para>对应 task 6.7 原子事务的扣费步骤失败。</para>
        /// </summary>
        InsufficientGold = 10,

        /// <summary>
        /// 格子非法或不可放置（越界、不可建造或已被占用）。
        /// <para>对应 task 6.7 原子事务的格子预留步骤失败。</para>
        /// </summary>
        InvalidCell = 11,

        /// <summary>
        /// 格子已被其他放置事务预留，当前命令无法获取预留。
        /// <para>对应 task 6.7 原子事务的格子预留步骤冲突。</para>
        /// </summary>
        CellReserved = 12,

        /// <summary>
        /// 卡槽索引非法或对应卡牌不可用。
        /// <para>对应 task 6.7 原子事务的消耗卡牌步骤失败。</para>
        /// </summary>
        InvalidCard = 13,

        /// <summary>
        /// 单位创建失败（未知兵种或工厂拒绝创建）。
        /// <para>对应 task 6.7 原子事务的创建步骤失败。</para>
        /// </summary>
        UnitCreateFailed = 14,

        /// <summary>
        /// 未知兵种 ID，工厂无法识别。
        /// <para>本期只支持刀/弓/枪/骑四种兵种（task 6.2/6.3）。</para>
        /// </summary>
        UnknownUnitType = 15,

        // ====================================================================
        // 征兵命令失败原因
        // ====================================================================

        /// <summary>
        /// 金币不足，无法支付征兵消耗。
        /// <para>对应最终方案征兵流程的扣费步骤失败：失败不清槽、不扣费。</para>
        /// </summary>
        InsufficientGoldForRecruit = 20,

        // ====================================================================
        // 换槽/合并命令失败原因
        // ====================================================================

        /// <summary>
        /// 源槽位非法（无效槽位标识或不存在）。
        /// </summary>
        InvalidSourceSlot = 21,

        /// <summary>
        /// 目标槽位非法（无效槽位标识或不存在）。
        /// </summary>
        InvalidTargetSlot = 22,

        /// <summary>
        /// 源槽为空，无单位可移动。
        /// </summary>
        SourceSlotEmpty = 23,

        /// <summary>
        /// 源槽与目标槽相同。
        /// </summary>
        SameSlot = 24,

        /// <summary>
        /// 源单位与目标单位阵营不同，不可合并。
        /// </summary>
        CrossSideMerge = 25,

        /// <summary>
        /// 目标单位不满足合并条件（不同兵种或不同等级）。
        /// </summary>
        TargetMismatch = 26,

        /// <summary>
        /// 目标单位已满级，不可继续合并。
        /// </summary>
        MaxLevelReached = 27,

        /// <summary>
        /// 换槽/合并事务的战斗实例准备失败（配置缺失/工厂失败等），槽位不变化。
        /// </summary>
        BattleInstancePrepareFailed = 28,

        /// <summary>
        /// 槽位事务版本冲突（计划生成后被其他事务修改），需重试。
        /// </summary>
        TransactionVersionConflict = 29,

        // ====================================================================
        // 命令状态/时序失败原因
        // ====================================================================

        /// <summary>
        /// 命令类型不在本期支持范围内。
        /// <para>本期只支持 Recruit 和 DropUnit。</para>
        /// </summary>
        UnsupportedCommand = 30,

        /// <summary>武将字尝试进入战场或离开同阵营待上场区域。</summary>
        UnitZoneRestricted = 31,

        /// <summary>
        /// 未知/非预期错误。实现层捕获非预期异常后包装为此拒绝原因。
        /// </summary>
        Unknown = 99,
    }

    /// <summary>
    /// 输入命令执行的不可变结果。
    /// </summary>
    /// <remarks>
    /// <para><b>不可变性（design.md:207 / task 6.6）：</b></para>
    /// <para>本结构为 readonly struct，全部字段为 readonly，构造后不可修改。
    /// 不持有可变集合或可变 object 引用。</para>
    ///
    /// <para><b>成功/失败语义（design.md:207）：</b></para>
    /// <para>成功时 <see cref="IsSuccess"/> 为 true、<see cref="RejectReason"/> 为
    /// <see cref="BattleInputRejectReason.None"/>。失败时 <see cref="IsSuccess"/> 为 false、
    /// <see cref="RejectReason"/> 为具体拒绝原因。正常校验失败不用异常表达，
    /// 调用方通过 <see cref="RejectReason"/> 做程序化判断。</para>
    ///
    /// <para><b>CommandId 关联（决策 0.8）：</b></para>
    /// <para>结果携带 <see cref="CommandId"/> 以便调用方对应请求。同一 CommandId 重复提交
    /// 返回首次结果的同一 CommandId；不同 ID 即使 payload 相同也返回独立结果。
    /// 缓存逻辑由 BattleInputController（task 6.8）维护。</para>
    ///
    /// <para><b>诊断信息（参照 BattleOperationResult.DiagnosticMessage）：</b></para>
    /// <para><see cref="DiagnosticMessage"/> 仅用于日志/诊断，调用方 MUST NOT 解析文本判断
    /// 失败原因，必须使用 <see cref="RejectReason"/> 做程序化判断。成功时为空串。</para>
    /// </remarks>
    public readonly struct BattleInputResult
    {
        /// <summary>
        /// 操作是否成功。等价于 <see cref="RejectReason"/> == <see cref="BattleInputRejectReason.None"/>。
        /// 调用方应优先使用本属性判断成功。
        /// </summary>
        public bool IsSuccess => RejectReason == BattleInputRejectReason.None;

        /// <summary>
        /// 对应命令的单局 CommandId。
        /// <para>决策 0.8：同一 CommandId 重复提交返回首次结果，CommandId 与首次执行一致。</para>
        /// </summary>
        public readonly int CommandId;

        /// <summary>
        /// 稳定拒绝原因。成功时为 <see cref="BattleInputRejectReason.None"/>。
        /// 调用方以此做程序化判断，不得依赖 <see cref="DiagnosticMessage"/> 文本。
        /// </summary>
        public readonly BattleInputRejectReason RejectReason;

        /// <summary>
        /// 诊断信息（仅用于日志/诊断）。调用方 MUST NOT 解析此文本判断失败原因，
        /// 必须使用 <see cref="RejectReason"/> 做程序化判断。成功时为空串。
        /// </summary>
        public readonly string DiagnosticMessage;

        private BattleInputResult(
            int commandId,
            BattleInputRejectReason rejectReason,
            string diagnosticMessage)
        {
            CommandId = commandId;
            RejectReason = rejectReason;
            // 明确拒绝 null：诊断信息用空串而非 null，避免接收方判空歧义。
            DiagnosticMessage = diagnosticMessage ?? string.Empty;
        }

        /// <summary>
        /// 构造成功结果。
        /// </summary>
        /// <param name="commandId">对应命令的 CommandId。</param>
        /// <returns>成功结果，拒绝原因为 None。</returns>
        public static BattleInputResult Ok(int commandId)
            => new BattleInputResult(commandId, BattleInputRejectReason.None, string.Empty);

        /// <summary>
        /// 构造失败结果。
        /// </summary>
        /// <param name="commandId">对应命令的 CommandId。</param>
        /// <param name="rejectReason">稳定拒绝原因（非 None）。</param>
        /// <param name="diagnosticMessage">诊断信息（仅用于日志），为 null 时规范化为空串。</param>
        /// <returns>失败结果。</returns>
        public static BattleInputResult Fail(
            int commandId,
            BattleInputRejectReason rejectReason,
            string diagnosticMessage = null)
            => new BattleInputResult(commandId, rejectReason, diagnosticMessage);

        /// <summary>
        /// 返回结果的字符串表示（仅用于日志/诊断）。调用方不得解析此字符串判断失败原因。
        /// </summary>
        public override string ToString()
            => IsSuccess
                ? $"[BattleInputResult] Success, CommandId={CommandId}"
                : $"[BattleInputResult] Failed, CommandId={CommandId}, Reason={RejectReason}, Msg={DiagnosticMessage}";

        /// <summary>
        /// 判断两个结果是否相等。
        /// </summary>
        public bool Equals(BattleInputResult other)
            => CommandId == other.CommandId
               && RejectReason == other.RejectReason
               && DiagnosticMessage == other.DiagnosticMessage;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is BattleInputResult other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = CommandId;
                hash = (hash * 397) ^ (int)RejectReason;
                hash = (hash * 397) ^ (DiagnosticMessage?.GetHashCode() ?? 0);
                return hash;
            }
        }

        /// <summary>
        /// 相等运算符。
        /// </summary>
        public static bool operator ==(BattleInputResult left, BattleInputResult right) => left.Equals(right);

        /// <summary>
        /// 不等运算符。
        /// </summary>
        public static bool operator !=(BattleInputResult left, BattleInputResult right) => !left.Equals(right);
    }
}
