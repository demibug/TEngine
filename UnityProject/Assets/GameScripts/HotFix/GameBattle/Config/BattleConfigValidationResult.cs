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

        /// <summary>路径相邻点曼哈顿距离不为 1（路径不连续）。</summary>
        PathDiscontinuous = 29,

        /// <summary>路径首点/末点与配置起终点不一致。</summary>
        PathEndpointMismatch = 30,

        /// <summary>双路入口越界或与对应起点不相邻。</summary>
        EntryInvalid = 31,

        /// <summary>表现层路径标记超出 marker 坐标域（0..Width × 0..Height）。</summary>
        RouteMarkerOutOfBounds = 32,

        /// <summary>敌人类型索引为负。</summary>
        EnemyTypeIndexInvalid = 33,

        /// <summary>地图身份（MapIndex）与预期 MapId 不一致。</summary>
        InvalidMapIdentity = 34,

        /// <summary>地图呈现数据非法（名称为空、世界资源地址为空等），映射 ConfigInvalid。</summary>
        InvalidMapPresentation = 35,

        /// <summary>地图路径为空或不可用（属于字段非法而非缺表，映射 ConfigInvalid）。</summary>
        InvalidMapPath = 36,

        // ----------------------------------------------------------------
        // 敌人目录 / 有序波次计划（tasks 2.7/2.9，本 change 新增）
        // ----------------------------------------------------------------

        /// <summary>敌人目录或计划无法构建：普通敌人缺 EnemyStats 行（Provider 抛）。</summary>
        EnemyStatsMissing = 40,

        /// <summary>敌人目录 typeIndex 重复冲突，无法构建合法双索引目录（Provider 抛）。</summary>
        EnemyTypeIndexConflict = 41,

        /// <summary>Normal 行引用了目录中不存在的 enemyKey。</summary>
        EnemyKeyUnknown = 42,

        /// <summary>地图默认敌人索引（EnemyTypeIndex）无法在目录中解析（未知/越界）。</summary>
        EnemyTypeIndexUnknown = 43,

        /// <summary>敌人目录条目数值非法（空资源地址、非正速度/血量、负接触伤害/奖励等）。</summary>
        EnemyCatalogInvalid = 44,

        /// <summary>波次计划缺失：activePlanId 为空、不存在或对应计划没有任何行。</summary>
        WavePlanMissing = 45,

        /// <summary>同一计划内存在重复 order。</summary>
        WaveOrderDuplicate = 46,

        /// <summary>同一计划内 order 缺号/不连续（应从 1 开始严格连续）。</summary>
        WaveOrderGap = 47,

        /// <summary>未知波次类型（不在 WavePlanKind.Normal/Boss 内）。</summary>
        WaveKindUnknown = 48,

        /// <summary>非法时序（preDelay/spawnInterval/postDelay 为负）。</summary>
        WaveTimingInvalid = 49,

        /// <summary>两个出生车道都关闭。</summary>
        WaveLaneInvalid = 50,

        /// <summary>Normal 行数量非正。</summary>
        WaveCountInvalid = 51,

        /// <summary>Boss 行缺少 bossKey。</summary>
        WaveBossKeyMissing = 52,

        /// <summary>难度索引越界（超出血量曲线或策略 profile 乘数长度）。</summary>
        DifficultyIndexInvalid = 53,

        /// <summary>逐行 strategyProfile 引用无效（越界或未被计划保留）。</summary>
        StrategyProfileInvalid = 54,

        /// <summary>所选计划含 Boss 行但当前运行时能力未声明支持（能力 gate 失败）。</summary>
        BossCapabilityUnsupported = 55,

        // ----------------------------------------------------------------
        // Buff 目录（tasks 3.5，本 change 新增）
        // ----------------------------------------------------------------

        /// <summary>未知 Buff Kind（handler 类别，合法 0 Numeric/1 State/2 Custom）。</summary>
        BuffKindUnknown = 56,

        /// <summary>未知 Buff 叠层策略 StackPolicy（合法 Add/Refresh/Replace）。</summary>
        BuffStackPolicyUnknown = 57,

        /// <summary>Buff 目录存在重复 type，无法构建按 type 索引目录（Provider 抛）。</summary>
        BuffTypeDuplicate = 58,

        /// <summary>Buff 定义含未知通道值（不在合法通道 0..6 集合内）。</summary>
        BuffChannelInvalid = 59,

        /// <summary>Numeric/State Buff 定义缺少通道（Custom 才允许空通道）。</summary>
        BuffChannelMissing = 60,

        /// <summary>Buff 定义 maxStacks 非正。</summary>
        BuffMaxStacksInvalid = 61,

        // ----------------------------------------------------------------
        // Skill 目录（tasks 2.2/2.3，本 change 新增；新增类别只能追加到末尾）
        // ----------------------------------------------------------------

        /// <summary>Skill 定义 key 为空（缺失主键，Validator 拒绝）。</summary>
        SkillKeyInvalid = 62,

        /// <summary>Skill 目录存在重复 key，无法构建按 key 索引目录（Provider/构造抛）。</summary>
        SkillKeyDuplicate = 63,

        /// <summary>未知 Skill Category（合法 active/boss/passive，严格映射不 fallback）。</summary>
        SkillCategoryUnknown = 64,

        /// <summary>Skill 冷却非法：CooldownSeconds 为空或为负，或 CooldownMs 为负。</summary>
        SkillCooldownInvalid = 65,

        /// <summary>Skill 定义 handlerKey 为空（必填，缺配置时在 xlsx 补齐，运行时不 fallback）。</summary>
        SkillHandlerKeyMissing = 67,

        // ----------------------------------------------------------------
        // Boss 目录与依赖（task 4.1-4.3，本 change 新增；新增类别只能追加到末尾）
        // ----------------------------------------------------------------

        /// <summary>Boss 目录存在重复 key，无法构建按 key 索引目录（Provider/构造抛）。</summary>
        BossKeyDuplicate = 68,

        /// <summary>所选计划含 Boss 行但配置快照没有 Boss 目录（启动门禁）。</summary>
        BossCatalogMissing = 69,

        /// <summary>Boss 定义数值非法（非正生命倍率/移动速度/逻辑尺寸、负接触伤害/奖励等）。</summary>
        BossConfigInvalid = 70,

        /// <summary>Boss 行引用了目录中不存在的 bossKey。</summary>
        BossKeyUnknown = 71,

        /// <summary>Boss 行引用的 Boss 定义未启用（disabled，不得出生）。</summary>
        BossDisabled = 72,

        /// <summary>Boss 定义缺少技能键（skillKey 为空）。</summary>
        BossSkillKeyMissing = 73,

        /// <summary>Boss 引用的技能键在 Skill 目录中不存在。</summary>
        BossSkillDefinitionMissing = 74,

        /// <summary>Boss 引用的技能 handlerKey 为空（缺 handler 配置，运行时不 fallback）。</summary>
        BossSkillHandlerMissing = 75,

        /// <summary>Boss 引用的技能缺少效果 Buff 或 Buff 不在 Buff 目录中（如 SoulCapture/Buff14 缺失）。</summary>
        BossEffectBuffMissing = 76,

        /// <summary>Boss 技能时间轴非法（effect/complete 为负或 effect &gt;= complete）。</summary>
        BossTimelineInvalid = 77,

        /// <summary>Boss 技能范围/持续等 effect 配置字段缺失（SoulCapture 专用配置）。</summary>
        BossSkillEffectConfigMissing = 78,

        // ----------------------------------------------------------------
        // 武器目录（tasks 3.1-3.3，本 change 新增；新增类别只能追加到末尾）
        // ----------------------------------------------------------------

        /// <summary>未知武器类型（合法 0 Bow/1 Spear/2 Knife/3 Sword，Provider 严格映射不 fallback）。</summary>
        WeaponTypeUnknown = 79,

        /// <summary>武器目录存在重复 id，无法构建按 id 索引目录（Provider/构造抛）。</summary>
        WeaponIdDuplicate = 80,

        /// <summary>武器定义数值非法（负附加攻击力等）。</summary>
        WeaponConfigInvalid = 81,

        /// <summary>启用行集合非法：不是恰好四条 Basic +1 基础武器（id 1/11/21/32 类别匹配）。</summary>
        WeaponEnabledSetInvalid = 82,

        /// <summary>武将定义、配方、战斗原型、数值、表现或投射物配置非法。</summary>
        GeneralConfigInvalid = 83,

        // ----------------------------------------------------------------
        // 武将主动技能绑定（本 change 新增；新增类别只能追加到末尾）
        // ----------------------------------------------------------------

        /// <summary>武将配置了 skillKey 但 Skill 目录中不存在该技能。</summary>
        GeneralSkillDefinitionMissing = 84,

        /// <summary>武将引用的技能不是 Active 类别（无法作为主动技能触发）。</summary>
        GeneralSkillCategoryInvalid = 85,

        /// <summary>武将引用的主动技能缺 triggerAttackCount 或 triggerAttackCount 非正。</summary>
        GeneralSkillTriggerInvalid = 86,

        /// <summary>武将引用的技能缺少其 handler 专用的 effect 配置字段（如 BattleShout 的 range/buffType/duration、FireArrowBarrage 的 range/damageMultiplier）。</summary>
        GeneralSkillEffectConfigMissing = 87,
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
