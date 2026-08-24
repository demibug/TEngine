using System;
using System.Collections.Generic;

namespace GameBattle
{
    // ============================================================================
    // 任务 4.1：Boss 配置领域 —— 不可变 Boss 定义与按 key 只读目录
    // ----------------------------------------------------------------------------
    // 职责（design.md 决策 1/2 / specs/zhang-liang-boss-runtime/spec.md
    //   "Luban Boss rows are copied into immutable definitions"）：
    //   把 Luban battle.Boss 行复制为不可变业务快照：不保留 Luban row 引用，
    //   不含运行时默认值（Required gameplay values MUST come from exported
    //   configuration without runtime fallback）。运行时只消费本目录。
    //
    // 不变量：
    //   1. 所有类型不可变：字段 readonly，定义不持有 Luban row 对象。
    //   2. BossCatalogSnapshot 按 key 唯一索引；重复 key 为编程错误，构造时抛
    //      ArgumentException（Provider 会先以结构化配置错误报告）。
    //   3. 目录构造后不暴露源集合引用；源集合后续修改不影响已构造的目录。
    // ============================================================================

    /// <summary>
    /// Boss 技能时间轴（不可变）：effect 与 complete 相对激活时刻的毫秒偏移。
    /// </summary>
    /// <remarks>
    /// <para>对应 Luban <c>boss.Timeline</c>（EffectAtMs/CompleteAtMs），在
    /// Provider 层复制为业务值对象。合法时间轴要求
    /// <c>0 &lt;= EffectAtMs &lt; CompleteAtMs</c>，由
    /// <see cref="BattleConfigValidator"/> 以 <see cref="BattleConfigErrorCategory.BossTimelineInvalid"/>
    /// 拒绝非法值，运行时不 fallback。</para>
    /// </remarks>
    public sealed class BossTimelineSnapshot
    {
        /// <summary>effect 回调相对激活时刻的偏移（毫秒）。</summary>
        public int EffectAtMs { get; }

        /// <summary>complete 回调相对激活时刻的偏移（毫秒）。</summary>
        public int CompleteAtMs { get; }

        /// <summary>构造 Boss 技能时间轴。</summary>
        /// <param name="effectAtMs">effect 偏移（毫秒）。</param>
        /// <param name="completeAtMs">complete 偏移（毫秒）。</param>
        public BossTimelineSnapshot(int effectAtMs, int completeAtMs)
        {
            EffectAtMs = effectAtMs;
            CompleteAtMs = completeAtMs;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"BossTimeline(effect={EffectAtMs}ms, complete={CompleteAtMs}ms)";
        }
    }

    /// <summary>
    /// 单个 Boss 定义快照（不可变）：由 Luban battle.Boss 行显式消费而来。
    /// </summary>
    /// <remarks>
    /// <para>spec "Luban Boss rows are copied into immutable definitions"：每条定义
    /// 标识 key、技能键、动画/资源字段、时间轴与身体数值。必填 gameplay 值
    /// （moveSpeed/contactDamage/rewardGold/logical dimensions）来自导出配置，
    /// 运行时不保留 row、不按 key 推导、不静默替换缺失值。</para>
    /// <para><see cref="ResourcePath"/> 为表现资源门禁字段：本波只保证框架与
    /// 纯逻辑可验收，production resource closure 由 7.x resource plan 处理；
    /// 校验器不把空资源路径当作 gameplay 非法，避免阻断纯逻辑测试。</para>
    /// </remarks>
    public sealed class BossDefinitionSnapshot
    {
        /// <summary>Boss 主键（全局唯一；非空由 Validator 保证）。</summary>
        public int Id { get; }

        /// <summary>Boss 资源名，不参与主键索引。</summary>
        public string ResName { get; }

        /// <summary>显示名。</summary>
        public string Name { get; }

        /// <summary>技能键（Boss 行使用的 Skill key，非空由 Validator 保证）。</summary>
        public int SkillId { get; }

        /// <summary>动画键（表现层识别用）。</summary>
        public string AnimationKey { get; }

        /// <summary>资源路径（YooAsset 地址；Spine 资产未就绪前可为空，本波不实现 7.x 资源门禁）。</summary>
        public string ResourcePath { get; }

        /// <summary>攻击动画名（ZhangLiang=attackliang）。</summary>
        public string AttackAnimation { get; }

        /// <summary>待机动画名（ZhangLiang=goliang）。</summary>
        public string IdleAnimation { get; }

        /// <summary>技能时间轴（effect/complete 偏移）。</summary>
        public BossTimelineSnapshot Timeline { get; }

        /// <summary>是否启用（未启用的 Boss 不可出生）。</summary>
        public bool Enabled { get; }

        /// <summary>生命倍率（Boss 最大生命 = 普通基线 × 本倍率）。</summary>
        public float HealthMultiplier { get; }

        /// <summary>移动速度（px/s）。</summary>
        public float MoveSpeed { get; }

        /// <summary>终点接触伤害。</summary>
        public int ContactDamage { get; }

        /// <summary>击杀奖励金币。</summary>
        public int RewardGold { get; }

        /// <summary>逻辑宽度（px）。</summary>
        public float LogicalWidth { get; }

        /// <summary>逻辑高度（px）。</summary>
        public float LogicalHeight { get; }

        /// <summary>构造单个 Boss 定义快照。</summary>
        /// <param name="key">Boss 主键。</param>
        /// <param name="name">显示名。</param>
        /// <param name="skillKey">技能键。</param>
        /// <param name="animationKey">动画键。</param>
        /// <param name="resourcePath">资源路径（可为空串）。</param>
        /// <param name="attackAnimation">攻击动画名。</param>
        /// <param name="idleAnimation">待机动画名。</param>
        /// <param name="timeline">技能时间轴。</param>
        /// <param name="enabled">是否启用。</param>
        /// <param name="healthMultiplier">生命倍率。</param>
        /// <param name="moveSpeed">移动速度（px/s）。</param>
        /// <param name="contactDamage">终点接触伤害。</param>
        /// <param name="rewardGold">击杀奖励金币。</param>
        /// <param name="logicalWidth">逻辑宽度。</param>
        /// <param name="logicalHeight">逻辑高度。</param>
        public BossDefinitionSnapshot(
            int id,
            string resName,
            string name,
            int skillId,
            string animationKey,
            string resourcePath,
            string attackAnimation,
            string idleAnimation,
            BossTimelineSnapshot timeline,
            bool enabled,
            float healthMultiplier,
            float moveSpeed,
            int contactDamage,
            int rewardGold,
            float logicalWidth,
            float logicalHeight)
        {
            Id = id;
            ResName = resName ?? string.Empty;
            Name = name ?? string.Empty;
            SkillId = skillId;
            AnimationKey = animationKey ?? string.Empty;
            ResourcePath = resourcePath ?? string.Empty;
            AttackAnimation = attackAnimation ?? string.Empty;
            IdleAnimation = idleAnimation ?? string.Empty;
            Timeline = timeline;
            Enabled = enabled;
            HealthMultiplier = healthMultiplier;
            MoveSpeed = moveSpeed;
            ContactDamage = contactDamage;
            RewardGold = rewardGold;
            LogicalWidth = logicalWidth;
            LogicalHeight = logicalHeight;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"BossDefinition(id={Id}, resName={ResName}, name={Name}, skillId={SkillId}, enabled={Enabled}, " +
                   $"healthMultiplier={HealthMultiplier}, moveSpeed={MoveSpeed}, " +
                   $"contactDamage={ContactDamage}, rewardGold={RewardGold}, timeline={Timeline})";
        }
    }

    /// <summary>
    /// Boss 目录快照（不可变）：按 key 唯一索引的 Boss 定义集合。
    /// </summary>
    /// <remarks>
    /// <para>spec "Boss definitions form a validated immutable catalog"：全部 Luban
    /// Boss 行归一化为不可变目录，运行时按 key 查询；不再持有原始 Luban 集合。</para>
    /// <para>构造时深拷贝定义列表并按 <see cref="BossDefinitionSnapshot.Key"/> 升序
    /// 稳定排序；重复 key 为编程错误，构造时抛 <see cref="ArgumentException"/>
    /// （Provider 会先以结构化配置错误报告）。</para>
    /// </remarks>
    public sealed class BossCatalogSnapshot
    {
        /// <summary>按 key 升序的只读 Boss 定义列表。</summary>
        public IReadOnlyList<BossDefinitionSnapshot> Definitions { get; }

        private readonly IReadOnlyDictionary<int, BossDefinitionSnapshot> _byId;

        /// <summary>构造 Boss 目录快照。</summary>
        /// <param name="definitions">Boss 定义列表（构造时深拷贝并按 key 升序排序）。</param>
        /// <exception cref="ArgumentException">定义列表含重复 key。</exception>
        public BossCatalogSnapshot(IReadOnlyList<BossDefinitionSnapshot> definitions)
        {
            IReadOnlyList<BossDefinitionSnapshot> source = definitions ?? Array.Empty<BossDefinitionSnapshot>();
            var copy = new BossDefinitionSnapshot[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                copy[i] = source[i];
            }

            Array.Sort(copy, (a, b) => a.Id.CompareTo(b.Id));

            var byId = new Dictionary<int, BossDefinitionSnapshot>(copy.Length);
            for (int i = 0; i < copy.Length; i++)
            {
                BossDefinitionSnapshot def = copy[i];
                if (byId.ContainsKey(def.Id))
                {
                    throw new ArgumentException($"Boss 目录存在重复 id={def.Id}");
                }

                byId.Add(def.Id, def);
            }

            Definitions = copy;
            _byId = byId;
        }

        /// <summary>按 key 查询 Boss 定义。</summary>
        /// <param name="key">Boss 主键。</param>
        /// <param name="definition">命中的定义；未命中时为 null。</param>
        /// <returns>键存在时返回 true。</returns>
        public bool TryGetById(int id, out BossDefinitionSnapshot definition)
        {
            return _byId.TryGetValue(id, out definition);
        }


        /// <summary>Boss 键是否存在于目录中。</summary>
        public bool ContainsId(int id)
        {
            return _byId.ContainsKey(id);
        }
    }
}
