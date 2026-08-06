using System;

namespace GameBattle
{
    // ============================================================================
    // 任务 3.3：IBattleConfigProvider —— 配置源隔离接口
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 1 节 / decision 0.6 / specs/battle-config-snapshot/spec.md
    //   "Configuration source preserves equivalent values"）：
    //   隔离具体配置源：Luban 为最终生产配置源，冻结 JSON 只作为黄金测试 Oracle。
    //   生产入口只使用 Luban Provider，JSON Provider 仅编入测试或开发验证边界。
    //
    //   运行时只依赖 BattleConfigSnapshot（不可变快照）。Provider 负责从具体数据源
    //   读取原始配置数据，经 BattleConfigNormalizer 规范化后返回不可变快照。
    //   逻辑子步不得反复访问资源加载器或可变全局配置表（spec "Runtime consumes an
    //   immutable configuration snapshot"）。
    //
    // 不变量：
    //   1. GetSnapshot 返回的快照在本次战斗期间不可变。
    //   2. Provider 不持有可变状态；每次调用返回独立快照。
    //   3. 缺失字段必须明确标注（LubanProvider），不得静默补成默认值。
    // ============================================================================

    /// <summary>
    /// 战斗配置源隔离接口。生产入口使用 Luban Provider，JSON Provider 仅用于测试。
    /// </summary>
    /// <remarks>
    /// <para><b>决策 0.6 / spec "Configuration source preserves equivalent values"：</b>
    /// Luban 为最终生产配置源，冻结 JSON 只作为黄金测试 Oracle。生产入口只使用
    /// Luban Provider，JSON Provider 仅编入测试或开发验证边界。</para>
    ///
    /// <para><b>运行时只依赖不可变快照（spec "Runtime consumes an immutable configuration snapshot"）：</b>
    /// Provider 从具体数据源读取原始配置，经 <see cref="BattleConfigNormalizer"/> 规范化后
    /// 返回 <see cref="BattleConfigSnapshot"/>。逻辑子步不得反复访问资源加载器或可变全局配置表。</para>
    /// </remarks>
    public interface IBattleConfigProvider
    {
        /// <summary>
        /// 获取本局战斗的不可变配置快照。
        /// </summary>
        /// <returns>规范化后的不可变配置快照。</returns>
        /// <remarks>
        /// <para>调用方（<c>BattleRuntimeFactory</c>）在创建运行时前调用本方法获取快照，
        /// 之后运行时只持有该快照，不再访问 Provider 或资源加载器。</para>
        /// <para>缺失字段由 Provider 或 <see cref="BattleConfigNormalizer"/> 明确标注为 TODO，
        /// 不静默补成默认值（task 39/40 BLOCKED 约束）。</para>
        /// </remarks>
        BattleConfigSnapshot GetSnapshot();
    }
}
