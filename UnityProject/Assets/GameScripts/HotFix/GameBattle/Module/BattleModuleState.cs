namespace GameBattle
{
    // ============================================================================
    // BattleModuleState 合法迁移表
    // ----------------------------------------------------------------------------
    // 职责（design.md 决策 0.7 / specs/battle-runtime-lifecycle/spec.md）：
    //   BattleModuleState 枚举定义在 IBattleModule.cs（task 2.4 产物），本文件
    //   只实现合法迁移校验逻辑，不重复定义枚举。提供静态方法 CanTransition 和
    //   扩展方法 CanTransitionTo，供 BattleModule（task 2.6/2.7）在状态切换前
    //   校验迁移合法性。
    //
    //   迁移规则（决策 0.7 + IBattleModule.cs 注释）：
    //     Idle → Entering → Running → Settling          // 正常生命周期
    //     Settling → Restarting → Entering              // 重开
    //     Entering/Running/Settling/Restarting → Exiting // 活动状态退出
    //     Exiting → Idle                                // 退出完成
    //     Idle → Exiting                                // 空闲退出（幂等，无运行时）
    //     任意状态 → Faulted                            // 意外失败
    //     Faulted → Idle                                // 清理后恢复
    //
    //   公共 API 语义约束（决策 0.7）：
    //     Start  只允许 Idle；重复 Start 返回 AlreadyActive
    //     Restart 只允许 Settling
    //     Exit   在任意状态幂等
    //
    //   Faulted 必须先清理（Faulted → Idle）才能接受新的 Start/Restart 请求。
    //   Faulted 不允许直接跳到 Entering/Running/Settling/Restarting/Exiting。
    // ============================================================================

    /// <summary>
    /// 战斗模块状态迁移表（task 2.5）。
    /// </summary>
    /// <remarks>
    /// <para>本类为静态工具类，不持有实例状态。所有方法均为纯函数，
    /// 仅依据 <see cref="BattleModuleState"/> 枚举值判断迁移合法性。</para>
    /// <para>迁移规则（决策 0.7）：</para>
    /// <list type="bullet">
    /// <item><see cref="BattleModuleState.Idle"/> → <see cref="BattleModuleState.Entering"/>（Start 开始加载）</item>
    /// <item><see cref="BattleModuleState.Entering"/> → <see cref="BattleModuleState.Running"/>（加载完成）</item>
    /// <item><see cref="BattleModuleState.Running"/> → <see cref="BattleModuleState.Settling"/>（结果冻结）</item>
    /// <item><see cref="BattleModuleState.Settling"/> → <see cref="BattleModuleState.Restarting"/>（再来一局）</item>
    /// <item><see cref="BattleModuleState.Restarting"/> → <see cref="BattleModuleState.Entering"/>（新局加载）</item>
    /// <item>活动状态（Entering/Running/Settling/Restarting）→ <see cref="BattleModuleState.Exiting"/>（退出）</item>
    /// <item><see cref="BattleModuleState.Exiting"/> → <see cref="BattleModuleState.Idle"/>（退出完成）</item>
    /// <item><see cref="BattleModuleState.Idle"/> → <see cref="BattleModuleState.Exiting"/>（空闲退出，幂等）</item>
    /// <item>任意状态 → <see cref="BattleModuleState.Faulted"/>（意外失败）</item>
    /// <item><see cref="BattleModuleState.Faulted"/> → <see cref="BattleModuleState.Idle"/>（清理后恢复）</item>
    /// </list>
    /// </remarks>
    internal static class BattleModuleStateTransitions
    {
        /// <summary>
        /// 判断从 <paramref name="from"/> 到 <paramref name="to"/> 的状态迁移是否合法。
        /// </summary>
        /// <param name="from">当前状态。</param>
        /// <param name="to">目标状态。</param>
        /// <returns>合法返回 true，非法返回 false。</returns>
        /// <remarks>
        /// 本方法为纯函数，不修改任何状态。调用方（BattleModule）应在实际切换状态前调用此方法校验。
        /// 自迁移（from == to）视为非法，除非显式允许（目前无自迁移允许项）。
        /// </remarks>
        internal static bool CanTransition(BattleModuleState from, BattleModuleState to)
        {
            // 自迁移一律禁止：状态机不靠自迁移表达幂等，幂等语义在 API 层处理。
            if (from == to)
            {
                return false;
            }

            // 任意活动/终态都可以进入 Faulted（意外失败）。
            if (to == BattleModuleState.Faulted)
            {
                // Faulted → Faulted 已被自迁移规则拦截，其余均允许。
                return true;
            }

            switch (from)
            {
                // Idle：正常开始战斗（→ Entering），或空闲退出（→ Exiting，幂等），
                // 或从故障清理恢复（由 to == Faulted 分支处理，不在此处）。
                case BattleModuleState.Idle:
                    return to == BattleModuleState.Entering
                        || to == BattleModuleState.Exiting;

                // Entering：加载完成进入运行，或退出。
                case BattleModuleState.Entering:
                    return to == BattleModuleState.Running
                        || to == BattleModuleState.Exiting;

                // Running：结果冻结进入结算，或退出。
                case BattleModuleState.Running:
                    return to == BattleModuleState.Settling
                        || to == BattleModuleState.Exiting;

                // Settling：再来一局进入重开，或退出。
                case BattleModuleState.Settling:
                    return to == BattleModuleState.Restarting
                        || to == BattleModuleState.Exiting;

                // Restarting：新局开始加载，或退出。
                case BattleModuleState.Restarting:
                    return to == BattleModuleState.Entering
                        || to == BattleModuleState.Exiting;

                // Exiting：退出完成回到空闲。
                // 不允许从 Exiting 直接跳到 Entering/Running/Settling/Restarting。
                case BattleModuleState.Exiting:
                    return to == BattleModuleState.Idle;

                // Faulted：必须先清理回到 Idle，不能直接跳到其他活动状态。
                case BattleModuleState.Faulted:
                    return to == BattleModuleState.Idle;

                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// <see cref="BattleModuleState"/> 的扩展方法，提供便捷的迁移校验。
    /// </summary>
    internal static class BattleModuleStateExtensions
    {
        /// <summary>
        /// 判断当前状态是否可以迁移到 <paramref name="to"/>。
        /// </summary>
        /// <param name="from">当前状态（this 实例）。</param>
        /// <param name="to">目标状态。</param>
        /// <returns>合法返回 true，非法返回 false。</returns>
        internal static bool CanTransitionTo(this BattleModuleState from, BattleModuleState to)
            => BattleModuleStateTransitions.CanTransition(from, to);
    }
}
