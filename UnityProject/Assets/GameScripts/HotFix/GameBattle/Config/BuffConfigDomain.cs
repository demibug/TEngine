using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace GameBattle
{
    // ============================================================================
    // 任务 3.1：Buff 配置领域 —— 枚举、不可变 Buff 定义与按 type 索引的目录
    // ----------------------------------------------------------------------------
    // 职责（design.md 决策 1 / specs/combat-buff-lifecycle/spec.md
    //   "Buff definitions form a validated immutable catalog"）：
    //   把 Luban battle.Buff 行归一化为业务领域形态：Kind/StackPolicy 从原始
    //   int/string 转换为领域枚举，Channels 深拷贝为只读升序数组，并按 Buff type
    //   双索引成不可变目录。运行时的申请、叠层、到期全部只消费本目录，不得再
    //   散读 Luban TbBuff 单例，也不得按 type 推导缺失字段。
    //
    // 不变量：
    //   1. 所有类型不可变：字段 readonly，Channels 在构造时深拷贝为数组并升序排序。
    //   2. BuffCatalogSnapshot 按 type 唯一索引；重复 type 为编程错误，构造时抛
    //      ArgumentException（Provider 会先以结构化配置错误报告）。
    //   3. 目录构造后不暴露源集合引用；源集合后续修改不影响已构造的目录。
    // ============================================================================

    /// <summary>
    /// Buff 处理器类别（Luban battle.Buff.Kind 的业务映射）。
    /// </summary>
    /// <remarks>
    /// <para>0=Numeric：对目标数值通道按稳定算序聚合；1=State：按贡献层聚合状态；
    /// 2=Custom：只经注册的 ICustomBuffHandler 处理，定义可不含内建通道。</para>
    /// <para>枚举值与 xlsx 原始 Kind 保持一致，业务层不得依赖 Luban int 表示。</para>
    /// </remarks>
    public enum BuffKind
    {
        /// <summary>数值 Buff：对 Numeric 通道执行 Flat/Ratio 聚合。</summary>
        Numeric = 0,

        /// <summary>状态 Buff：对 State 通道按贡献层聚合。</summary>
        State = 1,

        /// <summary>自定义 Buff：由注册的 handler 拥有窄效果契约。</summary>
        Custom = 2,
    }

    /// <summary>
    /// Buff 叠层策略（Luban battle.Buff.StackPolicy 字符串的业务映射）。
    /// </summary>
    /// <remarks>
    /// <para>Add：每个成功申请创建独立层直到 maxStacks；Refresh：同 target+type+source
    /// 保留实例 ID 替换请求数据并重启 duration；Replace：提交前移除同 type 全部层。</para>
    /// <para>非空 ConflictKey 的合法申请会替换其他同 key 类型层（spec "Conflict
    /// replacement has stable order"）。</para>
    /// </remarks>
    public enum BuffStackPolicy
    {
        /// <summary>独立叠层，达到 maxStacks 后拒绝最新请求。</summary>
        Add = 0,

        /// <summary>同 target+type+source 保留实例 ID 刷新；无匹配时创建新层。</summary>
        Refresh = 1,

        /// <summary>提交前移除同 type 全部层再创建新层。</summary>
        Replace = 2,
    }

    /// <summary>
    /// Numeric（数值）Buff 通道固定语义。
    /// </summary>
    /// <remarks>
    /// <para>与 design.md 决策 1 的通道表一致：0 AttackPower、1 AttackSpeed、
    /// 2 AttackRange、3 MoveSpeed、4 MaxHealth、5 CurrentHealth、6 Scale。</para>
    /// <para>Scale 为合法配置通道，但首期 Unit/Enemy 没有稳定逻辑/表现缩放字段，
    /// 所有生产目标均返回 UnsupportedTarget；后续由具体玩法定义几何/碰撞/表现语义后再扩展。</para>
    /// </remarks>
    public enum BuffNumericChannel
    {
        /// <summary>攻击力。</summary>
        AttackPower = 0,

        /// <summary>攻击速度。</summary>
        AttackSpeed = 1,

        /// <summary>攻击范围。</summary>
        AttackRange = 2,

        /// <summary>移动速度。</summary>
        MoveSpeed = 3,

        /// <summary>最大生命。</summary>
        MaxHealth = 4,

        /// <summary>当前生命（动态量，使用 aggregate modifier 差值协议）。</summary>
        CurrentHealth = 5,

        /// <summary>缩放（首期无稳定字段，目标返回 UnsupportedTarget）。</summary>
        Scale = 6,
    }

    /// <summary>
    /// State（状态）Buff 通道固定语义。
    /// </summary>
    /// <remarks>
    /// <para>0/1/2/3/6 为稳定状态：任一贡献层存在即 active、payload 取最大实例 ID；
    /// 4 DamageImpulse 与 5 KnockbackImpulse 是一次性/复合效果，只允许由已注册
    /// Custom Handler 处理，不走普通状态聚合（design.md 决策 3）。</para>
    /// <para>当前 type 12（knockback）与 type 14（burnStatic）的 Kind/Channels 来自
    /// 原表，仍属于合法可加载目录；其 4/5 通道的运行时语义在目标申请阶段处理，
    /// 本配置层不拒绝它们。</para>
    /// </remarks>
    public enum BuffStateChannel
    {
        /// <summary>移动禁用。</summary>
        MovementDisabled = 0,

        /// <summary>攻击禁用。</summary>
        AttackDisabled = 1,

        /// <summary>目标选择被改变。</summary>
        TargetingAltered = 2,

        /// <summary>压制。</summary>
        Suppressed = 3,

        /// <summary>伤害脉冲（一次性/复合效果，Custom-only）。</summary>
        DamageImpulse = 4,

        /// <summary>击退脉冲（一次性/复合效果，Custom-only）。</summary>
        KnockbackImpulse = 5,

        /// <summary>移动锁定。</summary>
        MovementLocked = 6,
    }

    /// <summary>
    /// 单个 Buff 定义快照（不可变）：由 Luban battle.Buff 行显式消费而来。
    /// </summary>
    /// <remarks>
    /// <para>spec "Buff definitions form a validated immutable catalog"：每条定义标识
    /// type、handler kind、受影响通道、叠层策略、最大层数与可选冲突键。必填值来自
    /// 导出配置，Provider 不得按 type 推导或静默替换缺失值。</para>
    /// <para><see cref="Channels"/> 在构造时深拷贝并升序排序，保证稳定可诊断；
    /// 源集合（Luban List）后续修改不影响本快照。</para>
    /// </remarks>
    public sealed class BuffDefinitionSnapshot
    {
        /// <summary>Buff 类型标识（主键，全局唯一）。</summary>
        public int Type { get; }

        /// <summary>名称（如 "attPower"）。</summary>
        public string Name { get; }

        /// <summary>中文标签（可为空，仅显示用途）。</summary>
        public string Label { get; }

        /// <summary>处理器类别（Numeric / State / Custom）。</summary>
        public BuffKind Kind { get; }

        /// <summary>叠层策略（Add / Refresh / Replace）。</summary>
        public BuffStackPolicy StackPolicy { get; }

        /// <summary>最大层数（正整数）。</summary>
        public int MaxStacks { get; }

        /// <summary>冲突键（可为空；非空时替换其他同 key 类型层）。</summary>
        public string ConflictKey { get; }

        /// <summary>受影响的通道列表（构造时深拷贝并升序排序，只读）。</summary>
        public IReadOnlyList<int> Channels { get; }

        private readonly HashSet<int> _channels;

        /// <summary>构造单个 Buff 定义快照。</summary>
        /// <param name="type">Buff 类型标识（主键）。</param>
        /// <param name="name">名称。</param>
        /// <param name="label">中文标签（可为空）。</param>
        /// <param name="kind">处理器类别。</param>
        /// <param name="channels">受影响通道列表（构造时深拷贝并升序排序）。</param>
        /// <param name="stackPolicy">叠层策略。</param>
        /// <param name="maxStacks">最大层数（由 Validator 拒绝非正）。</param>
        /// <param name="conflictKey">冲突键（可为空）。</param>
        public BuffDefinitionSnapshot(
            int type,
            string name,
            string label,
            BuffKind kind,
            IReadOnlyList<int> channels,
            BuffStackPolicy stackPolicy,
            int maxStacks,
            string conflictKey)
        {
            Type = type;
            Name = name ?? string.Empty;
            Label = label ?? string.Empty;
            Kind = kind;
            StackPolicy = stackPolicy;
            MaxStacks = maxStacks;
            ConflictKey = conflictKey ?? string.Empty;

            IReadOnlyList<int> source = channels ?? Array.Empty<int>();
            var copy = new int[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                copy[i] = source[i];
            }

            Array.Sort(copy);
            Channels = Array.AsReadOnly(copy);
            _channels = new HashSet<int>(copy);
        }

        /// <summary>
        /// 定义是否包含指定通道值。
        /// </summary>
        /// <param name="channel">通道值（与 <see cref="BuffNumericChannel"/> /
        /// <see cref="BuffStateChannel"/> 的底层值对应）。</param>
        /// <returns>包含时返回 true。</returns>
        public bool HasChannel(int channel)
        {
            return _channels.Contains(channel);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"BuffDefinition(type={Type}, name={Name}, kind={Kind}, " +
                   $"stackPolicy={StackPolicy}, maxStacks={MaxStacks}, conflictKey='{ConflictKey}', " +
                   $"channels=[{string.Join(",", Channels)}])";
        }
    }

    /// <summary>
    /// Buff 目录快照（不可变）：按 type 唯一索引的 Buff 定义集合。
    /// </summary>
    /// <remarks>
    /// <para>spec "Buff definitions form a validated immutable catalog"：开局前把全部
    /// Buff 定义归一化为一份不可变目录，运行时按 type 查询；不再持有原始 Luban 集合。</para>
    /// <para>构造时深拷贝定义列表并按 <see cref="BuffDefinitionSnapshot.Type"/> 升序稳定排序；
    /// 重复 type 为编程错误，构造时抛 <see cref="ArgumentException"/>（Provider 会先以
    /// 结构化配置错误 <see cref="BattleConfigErrorCategory.BuffTypeDuplicate"/> 报告）。</para>
    /// </remarks>
    public sealed class BuffCatalogSnapshot
    {
        /// <summary>按 type 升序的只读 Buff 定义列表。</summary>
        public IReadOnlyList<BuffDefinitionSnapshot> Definitions { get; }

        /// <summary>稳定只读的 Buff type 列表（按 type 升序）。</summary>
        public IReadOnlyList<int> Types { get; }

        private readonly IReadOnlyDictionary<int, BuffDefinitionSnapshot> _byType;

        /// <summary>构造 Buff 目录快照。</summary>
        /// <param name="definitions">Buff 定义列表（构造时深拷贝并按 type 升序排序）。</param>
        /// <exception cref="ArgumentException">定义列表含重复 type。</exception>
        public BuffCatalogSnapshot(IReadOnlyList<BuffDefinitionSnapshot> definitions)
        {
            IReadOnlyList<BuffDefinitionSnapshot> source = definitions ?? Array.Empty<BuffDefinitionSnapshot>();
            var copy = new BuffDefinitionSnapshot[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                copy[i] = source[i];
            }

            Array.Sort(copy, (a, b) => a.Type.CompareTo(b.Type));

            var byType = new Dictionary<int, BuffDefinitionSnapshot>(copy.Length);
            for (int i = 0; i < copy.Length; i++)
            {
                BuffDefinitionSnapshot def = copy[i];
                if (byType.ContainsKey(def.Type))
                {
                    throw new ArgumentException($"Buff 目录存在重复 type={def.Type}（name='{def.Name}'）");
                }

                byType.Add(def.Type, def);
            }

            Definitions = Array.AsReadOnly(copy);

            var types = new int[copy.Length];
            for (int i = 0; i < copy.Length; i++)
            {
                types[i] = copy[i].Type;
            }

            Types = Array.AsReadOnly(types);
            _byType = new ReadOnlyDictionary<int, BuffDefinitionSnapshot>(byType);
        }

        /// <summary>按 type 查询 Buff 定义。</summary>
        /// <param name="type">Buff 类型标识。</param>
        /// <param name="definition">命中的定义；未命中时为 null。</param>
        /// <returns>类型存在时返回 true。</returns>
        public bool TryGetByType(int type, out BuffDefinitionSnapshot definition)
        {
            return _byType.TryGetValue(type, out definition);
        }

        /// <summary>Buff type 是否存在于目录中。</summary>
        public bool ContainsType(int type)
        {
            return _byType.ContainsKey(type);
        }
    }
}
