using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace GameBattle
{
    // ============================================================================
    // 任务 2.1：Skill 配置领域 —— 枚举、不可变 Skill 定义与按 key 只读目录
    // ----------------------------------------------------------------------------
    // 职责（design.md 决策 1 / specs/combat-skill-lifecycle/spec.md
    //   "Skill definitions are validated before use"）：
    //   把 Luban battle.Skill 行归一化为业务领域形态：Category 从原始字符串严格
    //   映射为领域枚举（active/boss/passive，不做 fallback），CooldownSeconds 由
    //   Provider 以 checked 转成 long 毫秒，EffectBuffType/EffectDurationMs 只读
    //   透传（框架不解释）。运行时的 attach/激活/冷却全部只消费本目录。
    //
    // 不变量：
    //   1. 所有类型不可变：字段 readonly，定义不持有 Luban 行对象。
    //   2. SkillCatalogSnapshot 按 key 唯一索引；重复 key 为编程错误，构造时抛
    //      ArgumentException（Provider 会先以结构化配置错误报告）。
    //   3. 空 key 允许进入目录（与 Buff/Enemy 目录不同），由 BattleConfigValidator
    //      以 SkillKeyInvalid 拒绝——spec 要求缺失字段在缺配置时被检出，不在目录
    //      层静默丢弃。
    //   4. 目录构造后不暴露源集合引用；源集合后续修改不影响已构造的目录。
    // ============================================================================

    /// <summary>
    /// Skill 处理器类别（Luban battle.Skill.Category 字符串的业务映射）。
    /// </summary>
    /// <remarks>
    /// <para>Category 严格映射 active/boss/passive（大小写以实际 xlsx 为准，
    /// 不 fallback）：Active 与 Boss 共用同一激活时间轴；Passive 可合法存在于
    /// 目录，但本框架不实现被动 attach/detach 语义，请求时明确返回 unsupported。</para>
    /// <para>枚举顺序不代表优先级，只是稳定的领域标识。</para>
    /// </remarks>
    public enum SkillCategory
    {
        /// <summary>主动技能：attach 后可被 Activate 激活并执行 effect/complete。</summary>
        Active = 0,

        /// <summary>Boss 技能：与 Active 共用同一激活时间轴，由 Boss 行为触发。</summary>
        Boss = 1,

        /// <summary>被动技能：可存在于目录，但本 change 不实现被动生命周期。</summary>
        Passive = 2,
    }

    /// <summary>
    /// 单个 Skill 定义快照（不可变）：由 Luban battle.Skill 行显式消费而来。
    /// </summary>
    /// <remarks>
    /// <para>spec "Skill definitions are validated before use"：每条定义标识 key、
    /// category、冷却毫秒、handler key 与具体效果字段。必填值来自导出配置，Provider
    /// 不得按 key 推导或静默替换缺失值。</para>
    /// <para><see cref="CooldownMs"/> 是 Provider 以 checked 从
    /// <c>CooldownSeconds</c>（int?）转出的 long 毫秒；null/负值由 Provider 拒绝。
    /// <see cref="EffectBuffType"/> 与 <see cref="EffectDurationMs"/> 只读透传，
    /// 框架不解释其语义（首个 Boss 技能 SoulCapture 使用 buff type 13 / 2000ms）。
    /// <see cref="RangeTiles"/> 同样只读透传：SoulCapture 专用 handler 读取它作为
    /// 同路两格筛选范围，不作为通用 DSL 消费。</para>
    /// </remarks>
    public sealed class SkillDefinitionSnapshot
    {
        /// <summary>技能主键（全局唯一；非空由 Validator 保证）。</summary>
        public string Key { get; }

        /// <summary>技能类别（Active / Boss / Passive）。</summary>
        public SkillCategory Category { get; }

        /// <summary>冷却毫秒（由 CooldownSeconds 以 checked 转出，非负）。</summary>
        public long CooldownMs { get; }

        /// <summary>技能处理器键（注册到 SkillHandlerRegistry 的显式键；非空由 Validator 保证）。</summary>
        public string HandlerKey { get; }

        /// <summary>效果 Buff 类型（只读透传，框架不解释；可为空）。</summary>
        public int? EffectBuffType { get; }

        /// <summary>效果持续毫秒（只读透传，框架不解释；可为空）。</summary>
        public int? EffectDurationMs { get; }

        /// <summary>范围（格，只读透传；SoulCapture 专用 handler 读取，不为通用 DSL）。</summary>
        public float? RangeTiles { get; }

        /// <summary>触发攻击计数（主动技能：累计多少次普通攻击后在下个可攻击槽触发；只读透传）。</summary>
        public int? TriggerAttackCount { get; }

        /// <summary>单箭伤害倍率（主动技能：每箭伤害 = 当前有效攻击力 × 倍率；只读透传）。</summary>
        public float? EffectDamageMultiplier { get; }

        /// <summary>构造单个 Skill 定义快照。</summary>
        /// <param name="key">技能主键。</param>
        /// <param name="category">技能类别。</param>
        /// <param name="cooldownMs">冷却毫秒（由 Provider 以 checked 转换，非负）。</param>
        /// <param name="handlerKey">技能处理器键。</param>
        /// <param name="effectBuffType">效果 Buff 类型（只读透传）。</param>
        /// <param name="effectDurationMs">效果持续毫秒（只读透传）。</param>
        /// <param name="rangeTiles">范围（格，只读透传；可为空）。</param>
        /// <param name="triggerAttackCount">触发攻击计数（只读透传；可为空）。</param>
        /// <param name="effectDamageMultiplier">单箭伤害倍率（只读透传；可为空）。</param>
        public SkillDefinitionSnapshot(
            string key,
            SkillCategory category,
            long cooldownMs,
            string handlerKey,
            int? effectBuffType,
            int? effectDurationMs,
            float? rangeTiles = null,
            int? triggerAttackCount = null,
            float? effectDamageMultiplier = null)
        {
            Key = key ?? string.Empty;
            Category = category;
            CooldownMs = cooldownMs;
            HandlerKey = handlerKey ?? string.Empty;
            EffectBuffType = effectBuffType;
            EffectDurationMs = effectDurationMs;
            RangeTiles = rangeTiles;
            TriggerAttackCount = triggerAttackCount;
            EffectDamageMultiplier = effectDamageMultiplier;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"SkillDefinition(key={Key}, category={Category}, cooldownMs={CooldownMs}, " +
                   $"handlerKey='{HandlerKey}', " +
                   $"effectBuffType={EffectBuffType}, effectDurationMs={EffectDurationMs}, rangeTiles={RangeTiles}, " +
                   $"triggerAttackCount={TriggerAttackCount}, effectDamageMultiplier={EffectDamageMultiplier})";
        }
    }

    /// <summary>
    /// Skill 目录快照（不可变）：按 key 唯一索引的 Skill 定义集合。
    /// </summary>
    /// <remarks>
    /// <para>spec "Skill definitions are validated before use"：开局前把全部 Skill
    /// 定义归一化为一份不可变目录，运行时按 key 查询；不再持有原始 Luban 集合。
    /// 目录可含未实现 handler 的技能行，但这样的技能不得 attach（由 Runner 检查
    /// registry 是否注册，spec "Catalog rows MAY exist without a registered handler"）。</para>
    /// <para>构造时深拷贝定义列表并按 <see cref="SkillDefinitionSnapshot.Key"/> 升序
    /// 稳定排序；重复 key 为编程错误，构造时抛 <see cref="ArgumentException"/>
    /// （Provider 会先以结构化配置错误 <see cref="BattleConfigErrorCategory.SkillKeyDuplicate"/>
    /// 报告）。空 key 允许进入目录，由 <see cref="BattleConfigValidator"/> 拒绝。</para>
    /// </remarks>
    public sealed class SkillCatalogSnapshot
    {
        /// <summary>按 key 升序的只读 Skill 定义列表。</summary>
        public IReadOnlyList<SkillDefinitionSnapshot> Definitions { get; }

        private readonly IReadOnlyDictionary<string, SkillDefinitionSnapshot> _byKey;

        /// <summary>构造 Skill 目录快照。</summary>
        /// <param name="definitions">Skill 定义列表（构造时深拷贝并按 key 升序排序）。</param>
        /// <exception cref="ArgumentException">定义列表含重复 key。</exception>
        public SkillCatalogSnapshot(IReadOnlyList<SkillDefinitionSnapshot> definitions)
        {
            IReadOnlyList<SkillDefinitionSnapshot> source = definitions ?? Array.Empty<SkillDefinitionSnapshot>();
            var copy = new SkillDefinitionSnapshot[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                copy[i] = source[i];
            }

            Array.Sort(copy, (a, b) => string.CompareOrdinal(a.Key, b.Key));

            var byKey = new Dictionary<string, SkillDefinitionSnapshot>(copy.Length, StringComparer.Ordinal);
            for (int i = 0; i < copy.Length; i++)
            {
                SkillDefinitionSnapshot def = copy[i];
                if (byKey.ContainsKey(def.Key))
                {
                    throw new ArgumentException($"Skill 目录存在重复 key='{def.Key}'");
                }

                byKey.Add(def.Key, def);
            }

            Definitions = Array.AsReadOnly(copy);

            _byKey = new ReadOnlyDictionary<string, SkillDefinitionSnapshot>(byKey);
        }

        /// <summary>按 key 查询 Skill 定义。</summary>
        /// <param name="key">技能主键。</param>
        /// <param name="definition">命中的定义；未命中时为 null。</param>
        /// <returns>键存在时返回 true。</returns>
        public bool TryGetByKey(string key, out SkillDefinitionSnapshot definition)
        {
            return _byKey.TryGetValue(key, out definition);
        }

    }
}
