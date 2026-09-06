namespace GameBattle
{
    // ============================================================================
    // 输入命令：BattleInputCommand —— 征兵（Recruit）与换槽/合并（DropUnit）命令
    // ----------------------------------------------------------------------------
    // 职责（最终方案"核心架构"一节）：
    //   输入层只提交两个命令：Recruit(side) 与 DropUnit(sourceSlotId, targetSlotId)。
    //   本期删除旧购买放置（BuyPlace）命令，Refresh 命令更名为领域名称 Recruit（征兵）。
    //
    //   每条命令携带单局 CommandId（决策 0.8）：同一 ID 重复提交返回首次结果，
    //   不再次扣费、消耗卡牌或创建单位；不同 ID 即使 payload 相同也按独立命令处理。
    //   CommandId 的去重存储与首次结果缓存由 BattleInputController 维护。
    //
    //   所有输入在 Unity 主线程通过 Runtime 串行队列执行。本类型只定义命令与值载荷，
    //   不承担去重或执行逻辑。
    //
    // 不可变性：
    //   1. 命令类型为 enum，值载荷为 readonly struct，构造后不可修改。
    //   2. 不持有可变集合或可变 object 引用。
    //   3. CommandId 为单局内唯一标识，由调用方（UI 适配层）在主线程构造时赋值。
    //
    // 载荷类型安全：
    //   命令的 CommandType 决定载荷的合法强类型：
    //     - Recruit  → RecruitPayload（阵营）
    //     - DropUnit → DropUnitPayload（源槽位 ID、目标槽位 ID）
    //   调用方通过工厂方法 CreateRecruit / CreateDropUnit 构造，保证类型与载荷匹配。
    // ============================================================================

    /// <summary>
    /// 本期输入命令类型枚举。仅覆盖征兵和换槽/合并两种命令。
    /// </summary>
    public enum BattleInputCommandType
    {
        /// <summary>
        /// 征兵命令（原 Refresh 更名）。对应最终方案 Recruit(side)。
        /// <para>载荷为 <see cref="RecruitPayload"/>：阵营。</para>
        /// <para>原子语义：验证馒头 → 扣费 → 清除全部待上场单位 → 重新填满 1 级四兵。
        /// 失败时不扣费、不清槽。</para>
        /// </summary>
        Recruit = 0,

        /// <summary>
        /// 换槽/合并命令。对应最终方案 DropUnit(sourceSlotId, targetSlotId)。
        /// <para>载荷为 <see cref="DropUnitPayload"/>：源槽位 ID、目标槽位 ID。</para>
        /// <para>内部根据目标槽是否为空决定执行换槽还是合并；全程不访问经济模块。</para>
        /// </summary>
        DropUnit = 1,

        /// <summary>消费待上场槽中的铲子并开垦一个地图格。</summary>
        UseShovel = 2,

        /// <summary>消费待上场槽中的农民并开垦一个地图格。</summary>
        UseFarmer = 3,

        /// <summary>回收对手战场单位并按等级返还金币。</summary>
        ReclaimUnit = 4,
    }

    /// <summary>
    /// 不可变征兵命令值载荷。
    /// </summary>
    /// <remarks>
    /// 对应最终方案 Recruit(side) 的阵营参数。不可变值类型，构造后不可修改。
    /// </remarks>
    public readonly struct RecruitPayload
    {
        /// <summary>
        /// 是否玩家方。true 表示玩家方，false 表示对手方。
        /// </summary>
        public readonly bool PlayerSide;

        /// <summary>
        /// 构造不可变征兵载荷。
        /// </summary>
        /// <param name="playerSide">是否玩家方。</param>
        public RecruitPayload(bool playerSide)
        {
            PlayerSide = playerSide;
        }

        /// <inheritdoc/>
        public override string ToString()
            => $"Recruit(PlayerSide={PlayerSide})";
    }

    /// <summary>
    /// 不可变换槽/合并命令值载荷。
    /// </summary>
    /// <remarks>
    /// 对应最终方案 DropUnit(sourceSlotId, targetSlotId)。不可变值类型，构造后不可修改。
    /// </remarks>
    public readonly struct DropUnitPayload
    {
        /// <summary>
        /// 源槽位固定标识（UnitSlotId.Id）。
        /// </summary>
        public readonly int SourceSlotId;

        /// <summary>
        /// 目标槽位固定标识（UnitSlotId.Id）。
        /// </summary>
        public readonly int TargetSlotId;

        /// <summary>
        /// 构造不可变换槽/合并载荷。
        /// </summary>
        /// <param name="sourceSlotId">源槽位固定标识。</param>
        /// <param name="targetSlotId">目标槽位固定标识。</param>
        public DropUnitPayload(int sourceSlotId, int targetSlotId)
        {
            SourceSlotId = sourceSlotId;
            TargetSlotId = targetSlotId;
        }

        /// <inheritdoc/>
        public override string ToString()
            => $"DropUnit(SourceSlotId={SourceSlotId}, TargetSlotId={TargetSlotId})";
    }

    public readonly struct UseShovelPayload
    {
        public readonly int SourceReserveSlotId;
        public readonly GridPosition Target;

        public UseShovelPayload(int sourceReserveSlotId, GridPosition target)
        {
            SourceReserveSlotId = sourceReserveSlotId;
            Target = target;
        }

        public override string ToString()
            => $"UseShovel(SourceReserveSlotId={SourceReserveSlotId}, Target={Target})";
    }

    public readonly struct UseFarmerPayload
    {
        public readonly int SourceReserveSlotId;
        public readonly GridPosition Target;

        public UseFarmerPayload(int sourceReserveSlotId, GridPosition target)
        {
            SourceReserveSlotId = sourceReserveSlotId;
            Target = target;
        }

        public override string ToString()
            => $"UseFarmer(SourceReserveSlotId={SourceReserveSlotId}, Target={Target})";
    }

    /// <summary>不可变对手战场单位回收载荷。</summary>
    public readonly struct ReclaimUnitPayload
    {
        public readonly int SourceBattleSlotId;
        public readonly int ExpectedUnitId;

        public ReclaimUnitPayload(int sourceBattleSlotId, int expectedUnitId)
        {
            SourceBattleSlotId = sourceBattleSlotId;
            ExpectedUnitId = expectedUnitId;
        }

        public override string ToString()
            => $"ReclaimUnit(SourceBattleSlotId={SourceBattleSlotId}, ExpectedUnitId={ExpectedUnitId})";
    }

    /// <summary>
    /// 不可变战斗输入命令。携带单局 <see cref="CommandId"/> 与强类型值载荷。
    /// </summary>
    /// <remarks>
    /// <para><b>不可变性：</b>本结构为 readonly struct，全部字段为 readonly，
    /// 构造后不可修改。不持有可变集合或可变 object 引用。</para>
    ///
    /// <para><b>CommandId 语义（决策 0.8）：</b>
    /// 每条命令携带单局 CommandId。同一 ID 重复提交返回首次结果，不再次扣费、消耗卡牌
    /// 或创建单位；不同 ID 即使 payload 相同也按独立命令处理。CommandId 的去重与首次结果
    /// 缓存由 BattleInputController 在 Runtime 生命周期内维护。</para>
    ///
    /// <para><b>主线程串行执行：</b>所有输入在 Unity 主线程通过 Runtime 串行队列执行。
    /// 本类型只是被队列消费的不可变数据载体，不内嵌执行逻辑。</para>
    ///
    /// <para><b>载荷类型安全：</b><see cref="CommandType"/> 决定载荷的合法强类型：
    /// <see cref="BattleInputCommandType.Recruit"/> 对应 <see cref="RecruitPayload"/>，
    /// <see cref="BattleInputCommandType.DropUnit"/> 对应 <see cref="DropUnitPayload"/>。
    /// 调用方通过工厂方法 <see cref="CreateRecruit"/> / <see cref="CreateDropUnit"/> 构造。</para>
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
        /// 征兵载荷。仅当 <see cref="CommandType"/> 为 <see cref="BattleInputCommandType.Recruit"/> 时有效。
        /// </summary>
        public readonly RecruitPayload RecruitPayload;

        /// <summary>
        /// 换槽/合并载荷。仅当 <see cref="CommandType"/> 为 <see cref="BattleInputCommandType.DropUnit"/> 时有效。
        /// </summary>
        public readonly DropUnitPayload DropUnitPayload;

        /// <summary>使用铲子载荷。仅当 CommandType=UseShovel 时有效。</summary>
        public readonly UseShovelPayload UseShovelPayload;

        /// <summary>使用农民载荷。仅当 CommandType=UseFarmer 时有效。</summary>
        public readonly UseFarmerPayload UseFarmerPayload;

        /// <summary>回收载荷。仅当 CommandType=ReclaimUnit 时有效。</summary>
        public readonly ReclaimUnitPayload ReclaimUnitPayload;

        private BattleInputCommand(
            int commandId,
            BattleInputCommandType commandType,
            RecruitPayload recruitPayload,
            DropUnitPayload dropUnitPayload,
            UseShovelPayload useShovelPayload,
            UseFarmerPayload useFarmerPayload,
            ReclaimUnitPayload reclaimUnitPayload)
        {
            CommandId = commandId;
            CommandType = commandType;
            RecruitPayload = recruitPayload;
            DropUnitPayload = dropUnitPayload;
            UseShovelPayload = useShovelPayload;
            UseFarmerPayload = useFarmerPayload;
            ReclaimUnitPayload = reclaimUnitPayload;
        }

        /// <summary>
        /// 构造征兵命令。
        /// </summary>
        /// <param name="commandId">单局命令唯一标识。</param>
        /// <param name="playerSide">是否玩家方。</param>
        /// <returns>不可变征兵命令。</returns>
        public static BattleInputCommand CreateRecruit(int commandId, bool playerSide)
            => new BattleInputCommand(
                commandId,
                BattleInputCommandType.Recruit,
                new RecruitPayload(playerSide),
                default,
                default,
                default,
                default);

        /// <summary>
        /// 构造换槽/合并命令。
        /// </summary>
        /// <param name="commandId">单局命令唯一标识。</param>
        /// <param name="sourceSlotId">源槽位固定标识。</param>
        /// <param name="targetSlotId">目标槽位固定标识。</param>
        /// <returns>不可变换槽/合并命令。</returns>
        public static BattleInputCommand CreateDropUnit(int commandId, int sourceSlotId, int targetSlotId)
            => new BattleInputCommand(
                commandId,
                BattleInputCommandType.DropUnit,
                default,
                new DropUnitPayload(sourceSlotId, targetSlotId),
                default,
                default,
                default);

        public static BattleInputCommand CreateUseShovel(
            int commandId,
            int sourceReserveSlotId,
            GridPosition target)
            => new BattleInputCommand(
                commandId,
                BattleInputCommandType.UseShovel,
                default,
                default,
                new UseShovelPayload(sourceReserveSlotId, target),
                default,
                default);

        public static BattleInputCommand CreateUseFarmer(
            int commandId,
            int sourceReserveSlotId,
            GridPosition target)
            => new BattleInputCommand(
                commandId,
                BattleInputCommandType.UseFarmer,
                default,
                default,
                default,
                new UseFarmerPayload(sourceReserveSlotId, target),
                default);

        /// <summary>构造对手战场单位回收命令。</summary>
        public static BattleInputCommand CreateReclaimUnit(
            int commandId,
            int sourceBattleSlotId,
            int expectedUnitId)
            => new BattleInputCommand(
                commandId,
                BattleInputCommandType.ReclaimUnit,
                default,
                default,
                default,
                default,
                new ReclaimUnitPayload(sourceBattleSlotId, expectedUnitId));

        /// <summary>
        /// 判断两个命令是否相等。CommandId 与 CommandType 与全部载荷字段均相同才相等。
        /// </summary>
        public bool Equals(BattleInputCommand other)
            => CommandId == other.CommandId
               && CommandType == other.CommandType
               && RecruitPayload.Equals(other.RecruitPayload)
               && DropUnitPayload.Equals(other.DropUnitPayload)
               && UseShovelPayload.Equals(other.UseShovelPayload)
               && UseFarmerPayload.Equals(other.UseFarmerPayload)
               && ReclaimUnitPayload.Equals(other.ReclaimUnitPayload);

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is BattleInputCommand other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = CommandId;
                hash = (hash * 397) ^ (int)CommandType;
                hash = (hash * 397) ^ RecruitPayload.GetHashCode();
                hash = (hash * 397) ^ DropUnitPayload.GetHashCode();
                hash = (hash * 397) ^ UseShovelPayload.GetHashCode();
                hash = (hash * 397) ^ UseFarmerPayload.GetHashCode();
                hash = (hash * 397) ^ ReclaimUnitPayload.GetHashCode();
                return hash;
            }
        }

        /// <summary>相等运算符。</summary>
        public static bool operator ==(BattleInputCommand left, BattleInputCommand right) => left.Equals(right);

        /// <summary>不等运算符。</summary>
        public static bool operator !=(BattleInputCommand left, BattleInputCommand right) => !left.Equals(right);

        /// <inheritdoc/>
        public override string ToString()
        {
            switch (CommandType)
            {
                case BattleInputCommandType.Recruit:
                    return $"[BattleInputCommand Id={CommandId} Type=Recruit {RecruitPayload}]";
                case BattleInputCommandType.DropUnit:
                    return $"[BattleInputCommand Id={CommandId} Type=DropUnit {DropUnitPayload}]";
                case BattleInputCommandType.UseShovel:
                    return $"[BattleInputCommand Id={CommandId} Type=UseShovel {UseShovelPayload}]";
                case BattleInputCommandType.UseFarmer:
                    return $"[BattleInputCommand Id={CommandId} Type=UseFarmer {UseFarmerPayload}]";
                case BattleInputCommandType.ReclaimUnit:
                    return $"[BattleInputCommand Id={CommandId} Type=ReclaimUnit {ReclaimUnitPayload}]";
                default:
                    return $"[BattleInputCommand Id={CommandId} Type={CommandType}]";
            }
        }
    }
}
