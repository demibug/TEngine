using System;
using System.Collections.Generic;

namespace GameBattle
{
    // ============================================================================
    // 任务 2.1/2.2：不可变配置运行时领域类型（敌人目录 + 有序波次计划）
    // ----------------------------------------------------------------------------
    // 职责（design.md 决策 2 / specs/configured-enemy-spawning/spec.md、
    //   specs/ordered-wave-plan/spec.md）：
    //   为 BattleConfigSnapshot 提供敌人目录与有序波次计划的运行时领域类型，
    //   作为新生产链的权威配置形态。Provider 只在本层复制并深拷贝数据，
    //   禁止把 Luban 生成集合（List/Dictionary）泄漏到调用方。
    //
    // 不变量：
    //   1. 所有类型不可变：字段 readonly，集合在构造时深拷贝为数组并只读暴露。
    //   2. 构造后不暴露源集合引用；源集合后续修改不影响已构造的快照。
    //   3. EnemyCatalogSnapshot 按 enemyKey / typeIndex 双索引，NormalKeys 稳定只读。
    //   4. OrderedWavePlanSnapshot 的行按 order 升序稳定排序；
    //      策略 profile 仅保留被所选计划显式引用的原始索引。
    // ============================================================================

    /// <summary>
    /// 运行时波次类型（Luban <c>EWaveKind</c> 的业务映射，业务层不得依赖 Luban 枚举）。
    /// </summary>
    public enum WavePlanKind
    {
        /// <summary>普通波：按 enemyKey（空时按地图 EnemyTypeIndex）解析普通敌人并分车道出生。</summary>
        Normal = 1,

        /// <summary>Boss 波：按 bossKey 请求 Boss 波交接端口出生，本波不生成普通敌人。</summary>
        Boss = 2,
    }

    /// <summary>
    /// 单行波次计划条目（不可变）。字段语义与 <c>wave_plan.xlsx</c> 逐行配置一一对应。
    /// </summary>
    /// <remarks>
    /// <para>spec "Wave order is explicit and valid"：同一 planId 内按 <see cref="Order"/>
    /// 升序推进，运行时不读取原始配置行。</para>
    /// <para><see cref="EnemyKey"/> 为 Normal 行的“生效”敌人键：配置为空的行在 Provider
    /// 中已按地图 <c>EnemyTypeIndex</c> 解析为具体键，因此业务层可安全直接消费。</para>
    /// </remarks>
    public sealed class WavePlanEntry
    {
        /// <summary>计划分组键（与 <see cref="Order"/> 组成业务唯一键）。</summary>
        public string PlanId { get; }

        /// <summary>1-based 严格连续顺序。</summary>
        public int Order { get; }

        /// <summary>波次类型（Normal / Boss）。</summary>
        public WavePlanKind Kind { get; }

        /// <summary>Normal 行生效的普通敌人键（空键已解析；Boss 行为空串）。</summary>
        public string EnemyKey { get; }

        /// <summary>每个启用车道的普通敌人数量。</summary>
        public int NormalCount { get; }

        /// <summary>0-based 难度索引（同时索引血量曲线与策略乘数位置）。</summary>
        public int DifficultyIndex { get; }

        /// <summary>Boss 敌人键（Normal 行为空串，Boss 行必填）。</summary>
        public string BossKey { get; }

        /// <summary>该行开始到首次出生前的延迟（毫秒）。</summary>
        public long PreDelayMs { get; }

        /// <summary>同类连续出生间隔（毫秒）。</summary>
        public long SpawnIntervalMs { get; }

        /// <summary>最后一次出生后到允许完成的最短等待（毫秒）。</summary>
        public long PostDelayMs { get; }

        /// <summary>是否在玩家路出生。</summary>
        public bool PlayerLane { get; }

        /// <summary>是否在电脑路出生。</summary>
        public bool OpponentLane { get; }

        /// <summary>显式索引生成策略 profile（不再随机权重选择）。</summary>
        public int StrategyProfile { get; }

        /// <summary>构造单行波次计划条目。</summary>
        public WavePlanEntry(
            string planId,
            int order,
            WavePlanKind kind,
            string enemyKey,
            int normalCount,
            int difficultyIndex,
            string bossKey,
            long preDelayMs,
            long spawnIntervalMs,
            long postDelayMs,
            bool playerLane,
            bool opponentLane,
            int strategyProfile)
        {
            PlanId = planId ?? string.Empty;
            Order = order;
            Kind = kind;
            EnemyKey = enemyKey ?? string.Empty;
            NormalCount = normalCount;
            DifficultyIndex = difficultyIndex;
            BossKey = bossKey ?? string.Empty;
            PreDelayMs = preDelayMs;
            SpawnIntervalMs = spawnIntervalMs;
            PostDelayMs = postDelayMs;
            PlayerLane = playerLane;
            OpponentLane = opponentLane;
            StrategyProfile = strategyProfile;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"WavePlanEntry(planId={PlanId}, order={Order}, kind={Kind}, enemyKey={EnemyKey}, " +
                   $"bossKey={BossKey}, normalCount={NormalCount}, difficulty={DifficultyIndex}, profile={StrategyProfile})";
        }
    }

    /// <summary>
    /// 有序波次计划快照（不可变）：保存精确 planId、按 order 升序的行，
    /// 以及仅被所选计划显式引用的策略 profile。
    /// </summary>
    /// <remarks>
    /// <para>spec "Battle selects one configured wave plan"：计划选择完成后，
    /// 运行时只持有本快照，MUST NOT 再读取或切换原始配置行。</para>
    /// <para>策略 profile 以源表原始索引为键、只保留被行显式引用的条目；
    /// 深拷贝为只读数组，避免把 Luban <c>List&lt;List&lt;float&gt;&gt;</c> 泄漏给调用方。</para>
    /// </remarks>
    public sealed class OrderedWavePlanSnapshot
    {
        /// <summary>精确选中的计划标识（不得为空）。</summary>
        public string ActivePlanId { get; }

        /// <summary>按 <see cref="WavePlanEntry.Order"/> 升序稳定排序的只读行列表。</summary>
        public IReadOnlyList<WavePlanEntry> Rows { get; }

        /// <summary>仅被所选计划显式引用的策略 profile 原始索引（升序，稳定只读）。</summary>
        public IReadOnlyList<int> ReferencedProfileIndexes { get; }

        private readonly IReadOnlyDictionary<int, IReadOnlyList<float>> _profiles;

        /// <summary>构造有序波次计划快照。</summary>
        /// <param name="activePlanId">精确选中的计划标识。</param>
        /// <param name="rows">该计划的行（构造时深拷贝并按 order 升序排序）。</param>
        /// <param name="strategyProfiles">仅被行显式引用的策略 profile，键为源表原始索引
        /// （构造时深拷贝为只读数组）。</param>
        public OrderedWavePlanSnapshot(
            string activePlanId,
            IReadOnlyList<WavePlanEntry> rows,
            IReadOnlyDictionary<int, IReadOnlyList<float>> strategyProfiles)
        {
            ActivePlanId = activePlanId ?? string.Empty;

            IReadOnlyList<WavePlanEntry> source = rows ?? Array.Empty<WavePlanEntry>();
            var rowCopy = new WavePlanEntry[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                rowCopy[i] = source[i];
            }

            // 稳定排序：先按 order 升序；order 相同时保持源相对顺序（Array.Sort 不保证稳定，
            // 故采用索引稳定的插入排序，order 重复由 Validator 检测）。
            StableSortByOrder(rowCopy);
            Rows = rowCopy;

            var referencedProfiles = new HashSet<int>();
            for (int i = 0; i < rowCopy.Length; i++)
            {
                referencedProfiles.Add(rowCopy[i].StrategyProfile);
            }

            var profileCopy = new Dictionary<int, IReadOnlyList<float>>();
            if (strategyProfiles != null)
            {
                foreach (KeyValuePair<int, IReadOnlyList<float>> kv in strategyProfiles)
                {
                    if (!referencedProfiles.Contains(kv.Key))
                    {
                        continue;
                    }

                    IReadOnlyList<float> inner = kv.Value;
                    var arr = new float[inner != null ? inner.Count : 0];
                    for (int i = 0; i < arr.Length; i++)
                    {
                        arr[i] = inner[i];
                    }

                    profileCopy[kv.Key] = arr;
                }
            }

            _profiles = profileCopy;

            var indexes = new int[profileCopy.Count];
            int j = 0;
            foreach (KeyValuePair<int, IReadOnlyList<float>> kv in profileCopy)
            {
                indexes[j++] = kv.Key;
            }

            Array.Sort(indexes);
            ReferencedProfileIndexes = indexes;
        }

        /// <summary>
        /// 按源表原始索引获取策略 profile（仅返回被计划显式引用的条目）。
        /// </summary>
        /// <param name="profileIndex">源表策略索引（对应逐行 strategyProfile）。</param>
        /// <param name="profile">命中的只读乘数数组；未命中时为 null。</param>
        /// <returns>索引存在且被引用时返回 true。</returns>
        public bool TryGetProfile(int profileIndex, out IReadOnlyList<float> profile)
        {
            return _profiles.TryGetValue(profileIndex, out profile);
        }

        /// <summary>
        /// 稳定插入排序：order 相同的行保持源相对顺序，保证确定性与可断言性。
        /// </summary>
        private static void StableSortByOrder(WavePlanEntry[] rows)
        {
            for (int i = 1; i < rows.Length; i++)
            {
                WavePlanEntry key = rows[i];
                int j = i - 1;
                while (j >= 0 && rows[j].Order > key.Order)
                {
                    rows[j + 1] = rows[j];
                    j--;
                }

                rows[j + 1] = key;
            }
        }
    }

    /// <summary>
    /// 单个普通敌人定义快照（不可变）：由 <c>enemy.xlsx</c>（目录）与
    /// <c>enemystats.xlsx</c>（数值）按 enemyKey 成对关联而来。
    /// </summary>
    /// <remarks>
    /// <para>spec "Configured stats determine every normal enemy instance"：普通敌人出生时
    /// 由本定义与行难度一次性解析最大生命、移动速度、接触伤害和击杀奖励；
    /// 不再使用内嵌血量数组、固定速度/接触伤害或固定奖励 fallback。</para>
    /// <para>血量曲线与早期乘数在构造时深拷贝为只读数组。</para>
    /// </remarks>
    public sealed class EnemyDefinitionSnapshot
    {
        /// <summary>普通敌人类型索引（Mob0～Mob3 固定为 0～3，技能召唤类不占用）。</summary>
        public int TypeIndex { get; }

        /// <summary>普通敌人键（Mob0/Mob1/Mob2/Mob3）。</summary>
        public string Key { get; }

        /// <summary>Unity/YooAsset 资源地址（表现层预加载池键）。</summary>
        public string ResourceAddress { get; }

        /// <summary>移动速度（px/s）。</summary>
        public int MoveSpeed { get; }

        /// <summary>各波次基础血量（只读，全为正数）。</summary>
        public IReadOnlyList<int> HealthByWave { get; }

        /// <summary>早期波次血量乘数（只读）。</summary>
        public IReadOnlyList<float> EarlyRoundHealthMultipliers { get; }

        /// <summary>接触目标伤害（非负）。</summary>
        public int ContactDamage { get; }

        /// <summary>击杀奖励金币（非负）。</summary>
        public int RewardGold { get; }

        /// <summary>构造单个普通敌人定义快照。</summary>
        /// <param name="typeIndex">普通敌人类型索引。</param>
        /// <param name="key">普通敌人键。</param>
        /// <param name="resourceAddress">资源地址。</param>
        /// <param name="moveSpeed">移动速度（px/s）。</param>
        /// <param name="healthByWave">各波次基础血量。</param>
        /// <param name="earlyRoundHealthMultipliers">早期波次血量乘数。</param>
        /// <param name="contactDamage">接触目标伤害。</param>
        /// <param name="rewardGold">击杀奖励金币。</param>
        public EnemyDefinitionSnapshot(
            int typeIndex,
            string key,
            string resourceAddress,
            int moveSpeed,
            IReadOnlyList<int> healthByWave,
            IReadOnlyList<float> earlyRoundHealthMultipliers,
            int contactDamage,
            int rewardGold)
        {
            TypeIndex = typeIndex;
            Key = key ?? string.Empty;
            ResourceAddress = resourceAddress ?? string.Empty;
            MoveSpeed = moveSpeed;
            HealthByWave = CopyInts(healthByWave);
            EarlyRoundHealthMultipliers = CopyFloats(earlyRoundHealthMultipliers);
            ContactDamage = contactDamage;
            RewardGold = rewardGold;
        }

        private static IReadOnlyList<int> CopyInts(IReadOnlyList<int> source)
        {
            if (source == null)
            {
                return Array.Empty<int>();
            }

            var copy = new int[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                copy[i] = source[i];
            }

            return copy;
        }

        private static IReadOnlyList<float> CopyFloats(IReadOnlyList<float> source)
        {
            if (source == null)
            {
                return Array.Empty<float>();
            }

            var copy = new float[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                copy[i] = source[i];
            }

            return copy;
        }
    }

    /// <summary>
    /// 敌人目录快照（不可变）：按 enemyKey 与 typeIndex 双索引的普通敌人定义集合，
    /// 禁止向调用方暴露 Luban 集合。
    /// </summary>
    /// <remarks>
    /// <para>spec "Enemy configuration is a keyed immutable catalog"：每个可用于普通波的条目
    /// 具有唯一键、唯一受支持类型索引、非空资源地址与合法数值；运行时不再持有原始配置集合。</para>
    /// <para>构造时深拷贝定义列表并按 typeIndex 升序稳定排序；重复 key/typeIndex 为编程错误，
    /// 构造时抛 <see cref="ArgumentException"/>（Provider 会先以结构化配置错误报告）。</para>
    /// </remarks>
    public sealed class EnemyCatalogSnapshot
    {
        /// <summary>按 typeIndex 升序的只读普通敌人定义列表。</summary>
        public IReadOnlyList<EnemyDefinitionSnapshot> Definitions { get; }

        /// <summary>稳定只读的普通敌人键列表（按 typeIndex 升序）。</summary>
        public IReadOnlyList<string> NormalKeys { get; }

        private readonly IReadOnlyDictionary<string, EnemyDefinitionSnapshot> _byKey;
        private readonly IReadOnlyDictionary<int, EnemyDefinitionSnapshot> _byTypeIndex;

        /// <summary>构造敌人目录快照。</summary>
        /// <param name="definitions">普通敌人定义列表（构造时深拷贝并按 typeIndex 升序排序）。</param>
        /// <exception cref="ArgumentException">定义列表含重复 enemyKey 或重复 typeIndex。</exception>
        public EnemyCatalogSnapshot(IReadOnlyList<EnemyDefinitionSnapshot> definitions)
        {
            IReadOnlyList<EnemyDefinitionSnapshot> source = definitions ?? Array.Empty<EnemyDefinitionSnapshot>();
            var copy = new EnemyDefinitionSnapshot[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                copy[i] = source[i];
            }

            Array.Sort(copy, (a, b) => a.TypeIndex.CompareTo(b.TypeIndex));

            var byKey = new Dictionary<string, EnemyDefinitionSnapshot>(copy.Length);
            var byTypeIndex = new Dictionary<int, EnemyDefinitionSnapshot>(copy.Length);
            for (int i = 0; i < copy.Length; i++)
            {
                EnemyDefinitionSnapshot def = copy[i];
                if (string.IsNullOrEmpty(def.Key))
                {
                    throw new ArgumentException($"敌人定义 typeIndex={def.TypeIndex} 的 Key 为空");
                }

                if (byKey.ContainsKey(def.Key))
                {
                    throw new ArgumentException($"敌人目录存在重复 enemyKey='{def.Key}'");
                }

                if (byTypeIndex.ContainsKey(def.TypeIndex))
                {
                    throw new ArgumentException($"敌人目录存在重复 typeIndex={def.TypeIndex}（enemyKey='{def.Key}'）");
                }

                byKey.Add(def.Key, def);
                byTypeIndex.Add(def.TypeIndex, def);
            }

            Definitions = copy;

            var keys = new string[copy.Length];
            for (int i = 0; i < copy.Length; i++)
            {
                keys[i] = copy[i].Key;
            }

            NormalKeys = keys;
            _byKey = byKey;
            _byTypeIndex = byTypeIndex;
        }

        /// <summary>按 enemyKey 查询定义。</summary>
        /// <param name="key">普通敌人键。</param>
        /// <param name="definition">命中的定义；未命中时为 null。</param>
        /// <returns>键存在时返回 true。</returns>
        public bool TryGetByKey(string key, out EnemyDefinitionSnapshot definition)
        {
            return _byKey.TryGetValue(key, out definition);
        }

        /// <summary>按 typeIndex 查询定义。</summary>
        /// <param name="typeIndex">普通敌人类型索引。</param>
        /// <param name="definition">命中的定义；未命中时为 null。</param>
        /// <returns>索引存在时返回 true。</returns>
        public bool TryGetByTypeIndex(int typeIndex, out EnemyDefinitionSnapshot definition)
        {
            return _byTypeIndex.TryGetValue(typeIndex, out definition);
        }

        /// <summary>敌人键是否存在于目录中。</summary>
        public bool ContainsKey(string key)
        {
            return _byKey.ContainsKey(key);
        }
    }
}
