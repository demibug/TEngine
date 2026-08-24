namespace GameBattle
{
    // ============================================================================
    // 任务 5.3：ZhangLiangBoss —— 只固定张梁 key 的薄 Boss 类型
    // ----------------------------------------------------------------------------
    // 职责（design.md 决策 2 / specs/zhang-liang-boss-runtime/spec.md）：
    //   只固定 <see cref="ResName"/>="ZhangLiang"，其余全部来自注入的
    //   BossDefinitionSnapshot，不在薄类型硬编码任何数值/动画/资源地址。
    // ============================================================================

    /// <summary>
    /// 张梁 Boss（首期唯一启用的 Boss 键）。
    /// </summary>
    internal sealed class ZhangLiangBoss : BossBase
    {
        /// <summary>张梁 Boss 键常量（端口支持列表/能力 gate 共用）。</summary>
        internal const string ResNameConst = "ZhangLiang";

        /// <inheritdoc/>
        internal override string ResName => ResNameConst;
    }
}
