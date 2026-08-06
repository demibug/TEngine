using System;
using System.Collections.Generic;

namespace GameBattle
{
    // ============================================================================
    // 任务 3.5：BattleConfigValidationResult —— 结构化配置校验结果
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 1 节 / specs/battle-config-snapshot/spec.md
    //   "Invalid configuration blocks battle entry"）：
    //   承载 BattleConfigValidator 的结构化校验结果，使调用方（BattleRuntimeFactory）
    //   能基于稳定错误码与错误项列表做程序化判断，不依赖异常文本或诊断字符串解析。
    //
    //   spec "Invalid configuration blocks battle entry"：
    //   缺失表、非法权重、错误地图尺寸、未知单位或不完整路径 MUST 返回可诊断错误，
    //   并阻止运行时进入运行状态。本类型把每一条配置错误结构化为 BattleConfigValidationError，
    //   调用方据此返回 BattleErrorCode.ConfigInvalid / ConfigMissing / ConfigVersionMismatch。
    //
    //   决策 0.7：预期失败返回结构化结果而非异常。本类型为预期配置校验失败的载体。
    //
    // 不变量：
    //   1. IsValid 等价于 Errors 为空。
    //   2. Errors 只读，调用方不得修改。
    //   3. ErrorCode 为稳定枚举，调用方以此做程序化判断，不解析 ErrorText。
    //   4. DiagnosticMessage 仅用于日志，MUST NOT 作为程序化判断依据。
    // ============================================================================

    /// <summary>
    /// 配置校验错误类别，对应稳定错误码（决策 0.7 / task 3.5）。
    /// </summary>
    /// <remarks>
    /// 调用方以此枚举做程序化判断，不解析 <see cref="BattleConfigValidationError.Message"/> 文本。
    /// 新增类别只能追加到末尾，不得重排已有值。
    /// </remarks>
    public enum BattleConfigErrorCategory
    {
        /// <summary>无错误（占位，不用于错误项）。</summary>
        None = 0,

        // ----------------------------------------------------------------
        // 缺表 / 缺字段（对应 BattleErrorCode.ConfigMissing）
        // ----------------------------------------------------------------

        /// <summary>必需配置子节缺失（Map/Enemy/Wave/Units/Economy/Deck/Projectile 任一为 null）。</summary>
        MissingSection = 10,

        /// <summary>必需字段缺失或集合为空（如 WaveUnitCounts 空数组）。</summary>
        MissingField = 11,

        // ----------------------------------------------------------------
        // 非法值（对应 BattleErrorCode.ConfigInvalid）
        // ----------------------------------------------------------------

        /// <summary>配置版本非法或与预期不符。</summary>
        InvalidVersion = 20,

        /// <summary>波次生成权重为空、为负或无法选择有效索引。</summary>
        InvalidSpawnWeight = 21,

        /// <summary>未知兵种（单位索引或字标识不在本期 0..3 四兵集合内）。</summary>
        UnknownUnit = 22,

        /// <summary>非法时间值（如攻击间隔、波间延迟为负）。</summary>
        InvalidTime = 23,

        /// <summary>非法距离值（如攻击距离为负或零）。</summary>
        InvalidDistance = 24,

        /// <summary>地图尺寸非法（Width/Height 非正，或格子数与 Width*Height 不一致）。</summary>
        InvalidMapSize = 25,

        /// <summary>越界路径点（玩家或对手路径点不在地图范围内）。</summary>
        PathOutOfBounds = 26,

        /// <summary>缺失引用（如牌组引用了未定义的兵种文本）。</summary>
        MissingReference = 27,

        /// <summary>route marker 仅属表现却被当作游戏路径点（误判）。</summary>
        RouteMarkerMismatch = 28,
    }

    /// <summary>
    /// 单条结构化配置校验错误。
    /// </summary>
    /// <remarks>
    /// <para>每条错误包含稳定类别（<see cref="Category"/>）、可读消息（<see cref="Message"/>）与
    /// 可选定位路径（<see cref="Path"/>，如 "Wave.SpawnStrategyWeights[1]"）。</para>
    /// <para>调用方以 <see cref="Category"/> 做程序化判断；<see cref="Message"/> 与 <see cref="Path"/>
    /// 仅用于日志与诊断，MUST NOT 作为程序化判断依据。</para>
    /// </remarks>
    public sealed class BattleConfigValidationError
    {
        /// <summary>
        /// 稳定错误类别，调用方以此做程序化判断。
        /// </summary>
        public BattleConfigErrorCategory Category { get; }

        /// <summary>
        /// 可读错误消息（仅用于日志）。调用方 MUST NOT 解析此文本判断失败原因。
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// 可选定位路径（如 "Wave.SpawnStrategyWeights[1]"），便于诊断。可为空。
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// 构造单条配置校验错误。
        /// </summary>
        /// <param name="category">稳定错误类别。</param>
        /// <param name="message">可读错误消息。</param>
        /// <param name="path">可选定位路径。</param>
        public BattleConfigValidationError(
            BattleConfigErrorCategory category,
            string message,
            string path = "")
        {
            Category = category;
            Message = message ?? string.Empty;
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

    /// <summary>
    /// 结构化配置校验结果（task 3.5）。
    /// </summary>
    /// <remarks>
    /// <para><b>spec "Invalid configuration blocks battle entry"：</b>
    /// 缺失表、非法权重、错误地图尺寸、未知单位或不完整路径 MUST 返回可诊断错误，
    /// 并阻止运行时进入运行状态。本类型承载可诊断的结构化错误列表。</para>
    ///
    /// <para><b>决策 0.7 结构化结果：</b>
    /// 调用方（<see cref="BattleRuntimeFactory"/>）基于 <see cref="ErrorCode"/> 做程序化判断，
    /// 不依赖 <see cref="DiagnosticMessage"/> 文本。错误项列表 <see cref="Errors"/> 提供诊断细节，
    /// 仅用于日志与问题定位。</para>
    ///
    /// <para><b>错误码映射：</b></para>
    /// <list type="bullet">
    /// <item><see cref="BattleErrorCode.None"/>：校验通过，<see cref="Errors"/> 为空。</item>
    /// <item><see cref="BattleErrorCode.ConfigMissing"/>：存在 <see cref="BattleConfigErrorCategory.MissingSection"/>
    /// 或 <see cref="BattleConfigErrorCategory.MissingField"/> 类别错误。</item>
    /// <item><see cref="BattleErrorCode.ConfigVersionMismatch"/>：存在 <see cref="BattleConfigErrorCategory.InvalidVersion"/>
    /// 类别错误。</item>
    /// <item><see cref="BattleErrorCode.ConfigInvalid"/>：其余类别错误。</item>
    /// </list>
    /// <para>若同时存在多种类别，按优先级 ConfigMissing > ConfigVersionMismatch > ConfigInvalid 取首个错误码。
    /// 这样调用方能区分"缺表/缺字段"与"值非法"，便于上游诊断与回滚策略。</para>
    /// </remarks>
    public sealed class BattleConfigValidationResult
    {
        // ====================================================================
        // 只读属性
        // ====================================================================

        /// <summary>
        /// 校验是否通过。等价于 <see cref="Errors"/> 为空。
        /// </summary>
        public bool IsValid => Errors.Count == 0;

        /// <summary>
        /// 结构化错误项列表（只读）。校验通过时为空列表。
        /// </summary>
        public IReadOnlyList<BattleConfigValidationError> Errors { get; }

        /// <summary>
        /// 稳定错误码。校验通过为 <see cref="BattleErrorCode.None"/>；
        /// 失败时按错误类别优先级映射到 ConfigMissing / ConfigVersionMismatch / ConfigInvalid。
        /// 调用方以此做程序化判断，不依赖 <see cref="DiagnosticMessage"/> 文本。
        /// </summary>
        public BattleErrorCode ErrorCode { get; }

        /// <summary>
        /// 诊断信息（仅用于日志）。调用方 MUST NOT 解析此文本判断失败原因。
        /// 校验通过时为空串。
        /// </summary>
        public string DiagnosticMessage { get; }

        // ====================================================================
        // 构造
        // ====================================================================

        /// <summary>
        /// 构造校验结果。由 <see cref="BattleConfigValidator"/> 内部调用。
        /// </summary>
        /// <param name="errors">错误项列表（可为空）。</param>
        internal BattleConfigValidationResult(IReadOnlyList<BattleConfigValidationError> errors)
        {
            Errors = errors ?? Array.Empty<BattleConfigValidationError>();
            ErrorCode = ResolveErrorCode(Errors);
            DiagnosticMessage = Errors.Count == 0
                ? string.Empty
                : $"配置校验失败，共 {Errors.Count} 条错误；首条: {Errors[0]}";
        }

        // ====================================================================
        // 便捷工厂
        // ====================================================================

        /// <summary>
        /// 校验通过的便捷结果。
        /// </summary>
        internal static BattleConfigValidationResult Ok()
            => new BattleConfigValidationResult(Array.Empty<BattleConfigValidationError>());

        // ====================================================================
        // 错误码映射
        // ====================================================================

        /// <summary>
        /// 按错误类别优先级映射到稳定错误码。
        /// 优先级：MissingSection/MissingField → ConfigMissing；
        ///         InvalidVersion → ConfigVersionMismatch；其余 → ConfigInvalid。
        /// </summary>
        private static BattleErrorCode ResolveErrorCode(IReadOnlyList<BattleConfigValidationError> errors)
        {
            if (errors.Count == 0)
            {
                return BattleErrorCode.None;
            }

            bool hasMissing = false;
            bool hasVersion = false;

            for (int i = 0; i < errors.Count; i++)
            {
                BattleConfigErrorCategory c = errors[i].Category;
                if (c == BattleConfigErrorCategory.MissingSection
                    || c == BattleConfigErrorCategory.MissingField)
                {
                    hasMissing = true;
                }
                else if (c == BattleConfigErrorCategory.InvalidVersion)
                {
                    hasVersion = true;
                }
            }

            if (hasMissing)
            {
                return BattleErrorCode.ConfigMissing;
            }

            if (hasVersion)
            {
                return BattleErrorCode.ConfigVersionMismatch;
            }

            return BattleErrorCode.ConfigInvalid;
        }
    }
}
