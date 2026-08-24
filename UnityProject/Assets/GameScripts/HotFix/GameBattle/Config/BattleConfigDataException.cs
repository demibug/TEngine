using System;

namespace GameBattle
{
    // ============================================================================
    // 任务 2.4/2.5：BattleConfigDataException —— Provider 结构构建失败的载体
    // ----------------------------------------------------------------------------
    // 职责（design.md 决策 3 / Codex 契约）：
    //   LubanBattleConfigProvider 只负责复制与基础规范化。当原始配置无法构建出
    //   合法快照（如普通敌人缺 EnemyStats、typeIndex 冲突、activePlanId 缺失/无行、
    //   地图默认类型无法解析、越界 strategyProfile 等）时，Provider 抛出本异常，
    //   由 BattleStartupContext 转换为可由 BattleConfigValidator 表达的结构化错误，
    //   不静默 fallback 到首行或固定数值。
    //
    // 不变量：
    //   1. 异常携带稳定类别（BattleConfigErrorCategory）与可定位路径。
    //   2. 调用方以 Category 做程序化判断，不解析 Message 文本（与校验错误项一致）。
    //   3. 预期配置错误统一经本类型 + BattleConfigValidationResult 转结构化，
    //      不抛出通用 Exception 让上游猜测。
    // ============================================================================

    /// <summary>
    /// 配置数据无法构建合法业务快照时由 Provider 抛出的结构化异常。
    /// </summary>
    /// <remarks>
    /// <para>与 <see cref="BattleMapConfigMissingException"/>（地图行缺失）并列，
    /// 均代表预期配置失败；<see cref="BattleStartupContext.Prepare"/> 捕获后映射到
    /// <see cref="BattleErrorCode"/> 并在世界加载前阻断。</para>
    /// </remarks>
    public sealed class BattleConfigDataException : Exception
    {
        /// <summary>稳定错误类别，调用方以此做程序化判断。</summary>
        public BattleConfigErrorCategory Category { get; }

        /// <summary>可定位路径（如 "WavePlan.2.EnemyId"），仅用于诊断。</summary>
        public string Path { get; }

        /// <summary>构造结构化配置数据异常。</summary>
        /// <param name="category">稳定错误类别。</param>
        /// <param name="message">可读错误消息。</param>
        /// <param name="path">可选定位路径。</param>
        public BattleConfigDataException(
            BattleConfigErrorCategory category,
            string message,
            string path = "")
            : base(message)
        {
            Category = category;
            Path = path ?? string.Empty;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return string.IsNullOrEmpty(Path)
                ? $"[{Category}] {Message}"
                : $"[{Category}] {Path}: {Message}";
        }
    }
}
