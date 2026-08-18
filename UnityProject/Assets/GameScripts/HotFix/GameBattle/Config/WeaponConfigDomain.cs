using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace GameBattle
{
    // ============================================================================
    // 任务 3.1/3.4：Weapon 配置领域 —— 枚举、不可变武器定义、按 id 只读目录与默认 resolver
    // ----------------------------------------------------------------------------
    // 职责（design.md 决策 1/2 / specs/player-weapon-runtime/spec.md
    //   "Exactly four basic weapon definitions are enabled"）：
    //   把 Luban battle.Weapon 行复制为不可变业务快照：不保留 Luban row 引用，
    //   不含运行时默认值（Required values MUST come from exported configuration
    //   without runtime fallback）。首期四把 Basic 武器行为完全相同，只有 ID/类别
    //   映射相异，因此没有活动 Weapon 实例需要独立拥有。
    //
    // 不变量：
    //   1. 所有类型不可变：字段 readonly，定义不持有 Luban row 对象。
    //   2. WeaponCatalogSnapshot 按 id 唯一索引；重复 id 为编程错误，构造时抛
    //      ArgumentException（Provider 会先以结构化配置错误报告）。
    //   3. BasicWeaponResolver 以显式 SoldierType → definition 映射建立只读目录，
    //      禁止按 Weapon 原始 type 数值与 Soldier 枚举/单位索引数值相等推断
    //      （spec "Weapon category mapping is explicit"）。
    //   4. 目录构造后不暴露源集合引用；源集合后续修改不影响已构造的目录。
    // ============================================================================

    /// <summary>
    /// 武器类别（Luban battle.Weapon.Type 的业务映射，业务层不得依赖 Luban int 表示）。
    /// </summary>
    /// <remarks>
    /// <para>0=Bow（短弓，id 0）、1=Spear（短枪，id 10）、2=Knife（短刀，id 20）、
    /// 3=Sword（短剑，id 31）。枚举值来自 weapon.xlsx 的 type 列。</para>
    /// <para>Weapon type 数值与 <see cref="SoldierType"/>（0=刀 1=弓 2=枪 3=骑）顺序
    /// 不一致，映射 MUST 显式表达，不得比较枚举原始数值（spec "Weapon category
    /// mapping is explicit"）。</para>
    /// </remarks>
    public enum WeaponType
    {
        /// <summary>短弓（id 0 → 玩家弓兵 BowSoldier）。</summary>
        Bow = 0,

        /// <summary>短枪（id 10 → 玩家枪兵 SpearSoldier）。</summary>
        Spear = 1,

        /// <summary>短刀（id 20 → 玩家刀兵 KnifeSoldier）。</summary>
        Knife = 2,

        /// <summary>短剑（id 31 → 玩家骑兵 CavalrySoldier）。</summary>
        Sword = 3,
    }

    /// <summary>
    /// 单个武器定义快照（不可变）：由 Luban battle.Weapon 行显式消费而来。
    /// </summary>
    /// <remarks>
    /// <para>spec "Exactly four basic weapon definitions are enabled"：每条定义标识
    /// id、类别、附加攻击力、启用标记与处理器键。必填 gameplay 值来自导出配置，
    /// 运行时不保留 row、不按 id 推导、不静默替换缺失值。</para>
    /// <para><see cref="HandlerKey"/> 仅作为数据边界校验（首期必须为 Basic），
    /// 当前不解析为行为对象；等首个特殊武器确实需要攻击 hook 时再新增显式
    /// handler capability（design.md 决策 1）。</para>
    /// </remarks>
    public sealed class WeaponDefinitionSnapshot
    {
        /// <summary>武器主键（全局唯一；首期基础武器 id ∈ {0, 10, 20, 31}）。</summary>
        public int Id { get; }

        /// <summary>武器类别（Bow/Spear/Knife/Sword）。</summary>
        public WeaponType Type { get; }

        /// <summary>附加攻击力（基础武器为 1；数值来自 xlsx addAttPower 列）。</summary>
        public int AddAttackPower { get; }

        /// <summary>是否接入基础武器框架（仅 0/10/20/31 为 true，其余 40 行 false）。</summary>
        public bool Enabled { get; }

        /// <summary>武器处理器键（启用行必须为 "Basic"；禁用行可为空）。</summary>
        public string HandlerKey { get; }

        /// <summary>构造单个武器定义快照。</summary>
        /// <param name="id">武器主键。</param>
        /// <param name="type">武器类别。</param>
        /// <param name="addAttackPower">附加攻击力。</param>
        /// <param name="enabled">是否接入基础武器框架。</param>
        /// <param name="handlerKey">武器处理器键（可为空）。</param>
        public WeaponDefinitionSnapshot(
            int id,
            WeaponType type,
            int addAttackPower,
            bool enabled,
            string handlerKey)
        {
            Id = id;
            Type = type;
            AddAttackPower = addAttackPower;
            Enabled = enabled;
            HandlerKey = handlerKey ?? string.Empty;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"WeaponDefinition(id={Id}, type={Type}, addAttackPower={AddAttackPower}, " +
                   $"enabled={Enabled}, handlerKey='{HandlerKey}')";
        }
    }

    /// <summary>
    /// 武器目录快照（不可变）：按 id 唯一索引的武器定义集合。
    /// </summary>
    /// <remarks>
    /// <para>spec "Exactly four basic weapon definitions are enabled"：全部 Luban
    /// Weapon 行（含其余 40 把禁用特殊武器）归一化为不可变目录，运行时按 id 查询；
    /// 不再持有原始 Luban 集合，禁用行不产生任何运行时状态。</para>
    /// <para>构造时深拷贝定义列表并按 <see cref="WeaponDefinitionSnapshot.Id"/> 升序
    /// 稳定排序；重复 id 为编程错误，构造时抛 <see cref="ArgumentException"/>
    /// （Provider 会先以结构化配置错误 <see cref="BattleConfigErrorCategory.WeaponIdDuplicate"/>
    /// 报告）。</para>
    /// </remarks>
    public sealed class WeaponCatalogSnapshot
    {
        /// <summary>按 id 升序的只读武器定义列表。</summary>
        public IReadOnlyList<WeaponDefinitionSnapshot> Definitions { get; }

        private readonly IReadOnlyDictionary<int, WeaponDefinitionSnapshot> _byId;

        /// <summary>构造武器目录快照。</summary>
        /// <param name="definitions">武器定义列表（构造时深拷贝并按 id 升序排序）。</param>
        /// <exception cref="ArgumentException">定义列表含重复 id。</exception>
        public WeaponCatalogSnapshot(IReadOnlyList<WeaponDefinitionSnapshot> definitions)
        {
            IReadOnlyList<WeaponDefinitionSnapshot> source = definitions ?? Array.Empty<WeaponDefinitionSnapshot>();
            var copy = new WeaponDefinitionSnapshot[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                copy[i] = source[i];
            }

            Array.Sort(copy, (a, b) => a.Id.CompareTo(b.Id));

            var byId = new Dictionary<int, WeaponDefinitionSnapshot>(copy.Length);
            for (int i = 0; i < copy.Length; i++)
            {
                WeaponDefinitionSnapshot def = copy[i];
                if (byId.ContainsKey(def.Id))
                {
                    throw new ArgumentException($"武器目录存在重复 id={def.Id}（type={def.Type}）");
                }

                byId.Add(def.Id, def);
            }

            Definitions = Array.AsReadOnly(copy);
            _byId = new ReadOnlyDictionary<int, WeaponDefinitionSnapshot>(byId);
        }

        /// <summary>按 id 查询武器定义。</summary>
        /// <param name="id">武器主键。</param>
        /// <param name="definition">命中的定义；未命中时为 null。</param>
        /// <returns>id 存在时返回 true。</returns>
        public bool TryGetById(int id, out WeaponDefinitionSnapshot definition)
        {
            return _byId.TryGetValue(id, out definition);
        }

        /// <summary>武器 id 是否存在于目录中。</summary>
        public bool ContainsId(int id)
        {
            return _byId.ContainsKey(id);
        }
    }

    /// <summary>
    /// 首期基础武器默认 resolver：显式 SoldierType → definition 只读映射。
    /// </summary>
    /// <remarks>
    /// <para>spec "Weapon category mapping is explicit"：映射固定为 Knife→20、
    /// Bow→0、Spear→10、Cavalry→31。映射集中在本类型，不比较 Weapon 原始 type
    /// 数值与 Soldier 枚举/单位索引数值相等（WeaponType.Bow=0 与 SoldierType.Knife=0
    /// 数值相同但映射不同，禁止据此推断）。</para>
    /// <para>构造时验证启用行：恰好 id ∈ {0, 10, 20, 31} 四条启用，每行类别与
    /// 期望一致、handlerKey=Basic、AddAttackPower=1；任一不满足即抛
    /// <see cref="InvalidOperationException"/>（不 fallback）。启动层
    /// <see cref="BattleConfigValidator"/> 先以结构化错误拒绝非法目录，本类型为
    /// 激活时的第二道安全网。</para>
    /// <para><see cref="TryResolve"/> 为 virtual：供测试以不完整映射的子类验证
    /// "玩家缺失或不兼容默认必须失败而非 fallback"的 UnitRegistry 事务回滚路径。</para>
    /// </remarks>
    internal class BasicWeaponResolver
    {
        /// <summary>首期基础武器处理器键。</summary>
        internal const string BasicHandlerKey = "Basic";

        /// <summary>首期基础武器附加攻击力。</summary>
        internal const int BasicAttackPower = 1;

        /// <summary>
        /// 显式默认映射表：武器 id → 玩家兵种 → 期望武器类别。
        /// 每个条目同时表达类别映射（Sword→Cavalry）与 id 归属，禁止按数值相等推断。
        /// </summary>
        private static readonly (int Id, SoldierType SoldierType, WeaponType WeaponType)[] ExpectedDefaults =
        {
            (0, SoldierType.Bow, WeaponType.Bow),
            (10, SoldierType.Spear, WeaponType.Spear),
            (20, SoldierType.Knife, WeaponType.Knife),
            (31, SoldierType.Cavalry, WeaponType.Sword),
        };

        /// <summary>玩家兵种 → 武器定义只读映射（构造时建立）。</summary>
        private readonly IReadOnlyDictionary<SoldierType, WeaponDefinitionSnapshot> _bySoldierType;

        /// <summary>从武器目录构建默认 resolver，验证四条启用行并建立映射。</summary>
        /// <param name="catalog">武器目录（非 null）。</param>
        /// <exception cref="ArgumentNullException"><paramref name="catalog"/> 为 null。</exception>
        /// <exception cref="InvalidOperationException">启用行不满足首期四条 Basic +1 契约。</exception>
        internal BasicWeaponResolver(WeaponCatalogSnapshot catalog)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            var enabledById = new Dictionary<int, WeaponDefinitionSnapshot>();
            IReadOnlyList<WeaponDefinitionSnapshot> definitions = catalog.Definitions;
            for (int i = 0; i < definitions.Count; i++)
            {
                WeaponDefinitionSnapshot def = definitions[i];
                if (def.Enabled)
                {
                    // 目录构造已保证 id 唯一，此处直接索引。
                    enabledById[def.Id] = def;
                }
            }

            if (enabledById.Count != ExpectedDefaults.Length)
            {
                throw new InvalidOperationException(
                    $"基础武器启用行数={enabledById.Count} 不等于 {ExpectedDefaults.Length}" +
                    "（必须恰好 id ∈ {0,10,20,31} 四条启用，禁止隐式 fallback）");
            }

            var mapping = new Dictionary<SoldierType, WeaponDefinitionSnapshot>(ExpectedDefaults.Length);
            for (int i = 0; i < ExpectedDefaults.Length; i++)
            {
                (int id, SoldierType soldierType, WeaponType weaponType) = ExpectedDefaults[i];
                if (!enabledById.TryGetValue(id, out WeaponDefinitionSnapshot def))
                {
                    throw new InvalidOperationException(
                        $"基础武器目录缺少启用行 id={id}（SoldierType={soldierType}），禁止隐式 fallback");
                }

                if (def.Type != weaponType)
                {
                    throw new InvalidOperationException(
                        $"id={id} 的武器类别={def.Type} 与期望 {weaponType} 不一致，显式映射不得按数值相等推断");
                }

                if (!string.Equals(def.HandlerKey, BasicHandlerKey, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"id={id} 的 handlerKey='{def.HandlerKey}' 不是 '{BasicHandlerKey}'（仅数据边界校验）");
                }

                if (def.AddAttackPower != BasicAttackPower)
                {
                    throw new InvalidOperationException(
                        $"id={id} 的附加攻击力={def.AddAttackPower} 不等于 {BasicAttackPower}");
                }

                mapping.Add(soldierType, def);
            }

            _bySoldierType = new ReadOnlyDictionary<SoldierType, WeaponDefinitionSnapshot>(mapping);
        }

        /// <summary>按玩家兵种解析默认武器定义。</summary>
        /// <param name="type">玩家兵种（Knife/Bow/Spear/Cavalry）。</param>
        /// <param name="definition">命中的定义；未命中时为 null。</param>
        /// <returns>兵种存在映射时返回 true。</returns>
        internal virtual bool TryResolve(SoldierType type, out WeaponDefinitionSnapshot definition)
        {
            return _bySoldierType.TryGetValue(type, out definition);
        }
    }
}
