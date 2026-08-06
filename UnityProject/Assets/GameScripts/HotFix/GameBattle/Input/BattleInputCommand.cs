namespace GameBattle
{
    // ============================================================================
    // 任务 6.6：BattleInputCommand —— 本期强类型购买放置和刷新命令
    // ----------------------------------------------------------------------------
    // 职责（design.md:206 / specs/battle-simulation/spec.md "Input commands are atomic"）：
    //   定义本期仅覆盖的两种不可变输入命令：购买放置（BuyPlace）和刷新（Refresh）。
    //   升级、合并、移动、交换和拖拽命令随对应功能另行引入（design.md:206 明确排除）。
    //
    //   每条命令携带单局 CommandId（决策 0.8）：同一 ID 重复提交返回首次结果，
    //   不再次扣费、消耗卡牌或创建单位；不同 ID 即使 payload 相同也按独立命令处理。
    //   CommandId 的去重存储与首次结果缓存由 BattleInputController（task 6.7/6.8）在
    //   Runtime 生命周期内维护，本类型只定义命令与值载荷，不承担去重逻辑。
    //
    //   所有输入在 Unity 主线程通过 Runtime 串行队列执行（design.md:206 / task 6.6）。
    //   串行队列本身由 task 6.7/6.8 的 BattleInputController 实现，本任务只定义命令/结果
    //   数据结构，不实现队列。
    //
    // 不可变性：
    //   1. 命令类型为 enum，值载荷为 readonly struct，构造后不可修改。
    //   2. 不持有可变集合或可变 object 引用。
    //   3. CommandId 为单局内唯一标识，由调用方（UI 适配层）在主线程构造时赋值。
    //
    // 复用：
    //   - GridPosition（task 3.6 产物）：购买放置命令的格子坐标复用现有强类型值对象，
    //     保持 X=列、Y=行语义一致，不重复定义坐标结构。
    //   - 命令类型枚举参照还原工程 BattleInputCommand.js 的 BattleInputCommandType，
    //     但只保留本期 BuyPlace 与 Refresh 两种，不引入未实现的 BeginDrag/MoveDrag/
    //     CommitPlacement/CancelDrag/MoveUnit/MergeUnits（design.md:206 / task 6.4 排除）。
    // ============================================================================

    /// <summary>
    /// 本期输入命令类型枚举。仅覆盖购买放置和刷新两种命令。
    /// </summary>
    /// <remarks>
    /// <para><b>覆盖范围（design.md:206 / task 6.6）：</b></para>
    /// <para>本期只覆盖购买放置（<see cref="BuyPlace"/>）和刷新（<see cref="Refresh"/>）两种命令。
    /// 升级、合并、移动、交换和拖拽命令随对应功能另行引入，不在本期枚举中出现
    /// （task 6.4 明确解除 BattleInputController 对升级/合并服务的依赖）。</para>
    ///
    /// <para><b>与还原工程的对应：</b></para>
    /// <para>还原工程 <c>BattleInputCommandType</c> 含 8 种命令（PurchaseAndPlace/BeginDrag/
    /// MoveDrag/CommitPlacement/CancelDrag/MoveUnit/MergeUnits/Refresh）。本期只取其中两种：
    /// PurchaseAndPlace → <see cref="BuyPlace"/>，Refresh → <see cref="Refresh"/>。
    /// 命名从 "PurchaseAndPlace" 简化为 "BuyPlace"，语义不变，符合 CLAUDE.md 简短易懂命名原则。</para>
    ///
    /// <para><b>稳定性约束：</b>已发布枚举值名称与数值不得变更；新增命令类型只能追加到末尾。</para>
    /// </remarks>
    public enum BattleInputCommandType
    {
        /// <summary>
        /// 购买并放置单位命令。对应还原工程 PurchaseAndPlace。
        /// <para>载荷为 <see cref="BuyPlacePayload"/>：阵营、卡槽索引、放置坐标。</para>
        /// <para>原子语义（spec "Input commands are atomic"）：要么完成全部校验、扣费、创建、
        /// 放置、消耗和补牌，要么恢复到执行前状态。</para>
        /// </summary>
        BuyPlace = 0,

        /// <summary>
        /// 刷新牌组命令。对应还原工程 Refresh。
        /// <para>载荷为 <see cref="RefreshPayload"/>：阵营。</para>
        /// <para>原子语义：扣费刷新消耗并重新洗牌补满手牌，失败时恢复刷新消耗。</para>
        /// </summary>
        Refresh = 1,
    }

    /// <summary>
    /// 不可变购买放置命令值载荷。
    /// </summary>
    /// <remarks>
    /// <para>对应还原工程 <c>PurchaseAndPlacePayload(bool PlayerSide, int Slot, GridPosition Position)</c>。</para>
    /// <para>不可变值类型，构造后不可修改；不持有可变集合或可变引用。</para>
    /// </remarks>
    public readonly struct BuyPlacePayload
    {
        /// <summary>
        /// 是否玩家方。true 表示玩家方，false 表示对手方。
        /// </summary>
        public readonly bool PlayerSide;

        /// <summary>
        /// 手牌卡槽索引（从 0 开始）。标识被消耗的具体卡牌。
        /// </summary>
        public readonly int Slot;

        /// <summary>
        /// 放置目标格子坐标。复用 <see cref="GridPosition"/> 强类型值对象（X=列、Y=行）。
        /// </summary>
        public readonly GridPosition Position;

        /// <summary>
        /// 构造不可变购买放置载荷。
        /// </summary>
        /// <param name="playerSide">是否玩家方。</param>
        /// <param name="slot">手牌卡槽索引。</param>
        /// <param name="position">放置目标格子坐标。</param>
        public BuyPlacePayload(bool playerSide, int slot, GridPosition position)
        {
            PlayerSide = playerSide;
            Slot = slot;
            Position = position;
        }

        /// <inheritdoc/>
        public override string ToString()
            => $"BuyPlace(PlayerSide={PlayerSide}, Slot={Slot}, Position={Position})";
    }

    /// <summary>
    /// 不可变刷新牌组命令值载荷。
    /// </summary>
    /// <remarks>
    /// <para>对应还原工程 Refresh 命令的阵营参数。</para>
    /// <para>不可变值类型，构造后不可修改。</para>
    /// </remarks>
    public readonly struct RefreshPayload
    {
        /// <summary>
        /// 是否玩家方。true 表示玩家方，false 表示对手方。
        /// </summary>
        public readonly bool PlayerSide;

        /// <summary>
        /// 构造不可变刷新载荷。
        /// </summary>
        /// <param name="playerSide">是否玩家方。</param>
        public RefreshPayload(bool playerSide)
        {
            PlayerSide = playerSide;
        }

        /// <inheritdoc/>
        public override string ToString()
            => $"Refresh(PlayerSide={PlayerSide})";
    }

    /// <summary>
    /// 不可变战斗输入命令。携带单局 <see cref="CommandId"/> 与强类型值载荷。
    /// </summary>
    /// <remarks>
    /// <para><b>不可变性（design.md:206 / task 6.6）：</b></para>
    /// <para>本结构为 readonly struct，全部字段为 readonly，构造后不可修改。
    /// 不持有可变集合或可变 object 引用。</para>
    ///
    /// <para><b>CommandId 语义（决策 0.8）：</b></para>
    /// <para>每条命令携带单局 CommandId。同一 ID 重复提交返回首次结果，不再次扣费、消耗卡牌或
    /// 创建单位；不同 ID 即使 payload 相同也按独立命令处理。CommandId 的去重与首次结果缓存
    /// 由 BattleInputController（task 6.7/6.8）在 Runtime 生命周期内维护，随 Runtime 清空。
    /// 本类型只定义 CommandId 字段，不承担去重逻辑。</para>
    ///
    /// <para><b>主线程串行执行（design.md:206 / task 6.6）：</b></para>
    /// <para>所有输入在 Unity 主线程通过 Runtime 串行队列执行。串行队列由 task 6.7/6.8 实现，
    /// 本类型只是被队列消费的不可变数据载体，不内嵌执行逻辑。</para>
    ///
    /// <para><b>载荷类型安全：</b></para>
    /// <para><see cref="CommandType"/> 决定载荷的合法强类型：<see cref="BattleInputCommandType.BuyPlace"/>
    /// 对应 <see cref="BuyPlacePayload"/>，<see cref="BattleInputCommandType.Refresh"/> 对应
    /// <see cref="RefreshPayload"/>。调用方通过工厂方法 <see cref="CreateBuyPlace"/> /
    /// <see cref="CreateRefresh"/> 构造，保证类型与载荷匹配；不使用裸 object 载荷（参照
    /// BattleLoadoutDto/BattleResultDto 排除可变 object 的不可变性约束）。</para>
    /// </remarks>
    public readonly struct BattleInputCommand
    {
        /// <summary>
        /// 单局命令唯一标识。
        /// <para>决策 0.8：同一 ID 重复提交返回首次结果；不同 ID 按独立命令处理。
        /// 由调用方（UI 适配层）在主线程构造时赋值，单局内唯一。</para>
        /// </summary>
        public readonly int CommandId;

        /// <summary>
        /// 命令类型。决定 <see cref="Payload"/> 的强类型解释。
        /// </summary>
        public readonly BattleInputCommandType CommandType;

        /// <summary>
        /// 购买放置载荷。仅当 <see cref="CommandType"/> 为 <see cref="BattleInputCommandType.BuyPlace"/> 时有效。
        /// </summary>
        public readonly BuyPlacePayload BuyPlacePayload;

        /// <summary>
        /// 刷新载荷。仅当 <see cref="CommandType"/> 为 <see cref="BattleInputCommandType.Refresh"/> 时有效。
        /// </summary>
        public readonly RefreshPayload RefreshPayload;

        private BattleInputCommand(
            int commandId,
            BattleInputCommandType commandType,
            BuyPlacePayload buyPlacePayload,
            RefreshPayload refreshPayload)
        {
            CommandId = commandId;
            CommandType = commandType;
            BuyPlacePayload = buyPlacePayload;
            RefreshPayload = refreshPayload;
        }

        /// <summary>
        /// 构造购买放置命令。
        /// </summary>
        /// <param name="commandId">单局命令唯一标识。</param>
        /// <param name="playerSide">是否玩家方。</param>
        /// <param name="slot">手牌卡槽索引。</param>
        /// <param name="position">放置目标格子坐标。</param>
        /// <returns>不可变购买放置命令。</returns>
        public static BattleInputCommand CreateBuyPlace(
            int commandId,
            bool playerSide,
            int slot,
            GridPosition position)
            => new BattleInputCommand(
                commandId,
                BattleInputCommandType.BuyPlace,
                new BuyPlacePayload(playerSide, slot, position),
                default);

        /// <summary>
        /// 构造刷新命令。
        /// </summary>
        /// <param name="commandId">单局命令唯一标识。</param>
        /// <param name="playerSide">是否玩家方。</param>
        /// <returns>不可变刷新命令。</returns>
        public static BattleInputCommand CreateRefresh(int commandId, bool playerSide)
            => new BattleInputCommand(
                commandId,
                BattleInputCommandType.Refresh,
                default,
                new RefreshPayload(playerSide));

        /// <summary>
        /// 判断两个命令是否相等。CommandId 与 CommandType 与全部载荷字段均相同才相等。
        /// </summary>
        public bool Equals(BattleInputCommand other)
            => CommandId == other.CommandId
               && CommandType == other.CommandType
               && BuyPlacePayload.Equals(other.BuyPlacePayload)
               && RefreshPayload.Equals(other.RefreshPayload);

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is BattleInputCommand other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = CommandId;
                hash = (hash * 397) ^ (int)CommandType;
                hash = (hash * 397) ^ BuyPlacePayload.GetHashCode();
                hash = (hash * 397) ^ RefreshPayload.GetHashCode();
                return hash;
            }
        }

        /// <summary>
        /// 相等运算符。
        /// </summary>
        public static bool operator ==(BattleInputCommand left, BattleInputCommand right) => left.Equals(right);

        /// <summary>
        /// 不等运算符。
        /// </summary>
        public static bool operator !=(BattleInputCommand left, BattleInputCommand right) => !left.Equals(right);

        /// <inheritdoc/>
        public override string ToString()
        {
            switch (CommandType)
            {
                case BattleInputCommandType.BuyPlace:
                    return $"[BattleInputCommand Id={CommandId} Type=BuyPlace {BuyPlacePayload}]";
                case BattleInputCommandType.Refresh:
                    return $"[BattleInputCommand Id={CommandId} Type=Refresh {RefreshPayload}]";
                default:
                    return $"[BattleInputCommand Id={CommandId} Type={CommandType}]";
            }
        }
    }
}
