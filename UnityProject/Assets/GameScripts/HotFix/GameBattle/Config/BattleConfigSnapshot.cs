using System;
using System.Collections.Generic;
using GameBattle.Weapon;

namespace GameBattle
{
    // ============================================================================
    // 任务 3.3：BattleConfigSnapshot —— 不可变本局配置快照
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 1 节 / specs/battle-config-snapshot/spec.md）：
    //   保存地图、波次、敌人、单位、牌组、经济、投射物的不可变本局规范化快照。
    //   运行时只依赖本快照，逻辑子步不得反复访问资源加载器或可变全局配置表
    //   （spec "Runtime consumes an immutable configuration snapshot"）。
    //
    // 来源证据：
    //   - 地图：MapData.js MAP_BLOCKS（列优先 grid[x][y]），golden-battle-bundle.json minimalConfig.map
    //   - 敌人：BattleDataCore.js normalEnemyHealthByWave/speed，enemies.json type 清单
    //   - 波次：waves.json + BattleDataCore.js waveUnitCounts/spawnStrategies
    //   - 单位：units.json 四兵数值
    //   - 经济：battle-economy.json + BattleState.js initialGold/maxHealth
    //   - 牌组：DeckDefinitions.js BASE_POOL/handSize
    //   - 投射物：projectiles.json + ProjectileFactory 唯一注册 SimpleDynamicArrow
    //
    // 不变量：
    //   1. 不可变：所有字段为 readonly，集合为 IReadOnlyList/IReadOnlyDictionary。
    //   2. 构造后不暴露可变引用。
    //   3. 配置变更不影响已创建的快照（spec "Configuration changes after battle starts"）。
    // ============================================================================

    /// <summary>
    /// 不可变本局战斗配置快照，包含地图、波次、敌人、单位、经济、投射物、牌组。
    /// </summary>
    /// <remarks>
    /// <para><b>spec "Runtime consumes an immutable configuration snapshot"：</b>
    /// 系统在创建战斗运行时前解析并校验配置，向运行时提供不可变快照；逻辑子步
    /// 不得反复访问资源加载器或可变全局配置表。当前战斗继续使用启动时冻结的配置快照。</para>
    ///
    /// <para><b>不可变性：</b>所有字段为 readonly，集合类型只暴露 IReadOnlyList/IReadOnlyDictionary。
    /// 构造后不得修改。配置源在一局战斗运行期间被替换或重新加载时，当前战斗继续使用本快照。</para>
    ///
    /// <para><b>缺失字段标注（task 39/40 BLOCKED 约束）：</b>
    /// Luban 表结构可能缺少部分字段（如 Enemy 缺 HP/速度、Map Blocks 为 string、无 deck 表等）。
    /// 这些字段在 Provider 或 Normalizer 中标注为 <see cref="MissingFieldNotes"/>，
    /// 不静默补成默认值。黄金 JSON 快照包含完整配置。</para>
    /// </remarks>
    public sealed class BattleConfigSnapshot
    {
        // ====================================================================
        // 地图配置
        // ====================================================================

        /// <summary>
        /// 地图数据（不可变），业务层通过 GetCell(x,y)/IsInside(x,y) 等坐标 API 访问。
        /// </summary>
        public MapData Map { get; }

        // ====================================================================
        // 敌人配置
        // ====================================================================

        /// <summary>
        /// 敌人目录（不可变，按 enemyKey/typeIndex 双索引）——本 change 起的新生产链权威。
        /// </summary>
        /// <remarks>
        /// <para>spec "Enemy configuration is a keyed immutable catalog"：Mob0～Mob3 的目录与数值
        /// 按 enemyKey 关联为不可变目录，运行时不再持有原始配置集合。</para>
        /// <para>可为 null（旧兼容构造路径）；<see cref="BattleConfigValidator"/> 以
        /// MissingSection 拒绝 null 目录。</para>
        /// </remarks>
        public EnemyCatalogSnapshot EnemyCatalog { get; }

        /// <summary>
        /// 有序波次计划（不可变，按 order 升序）——本 change 起的新生产链权威。
        /// </summary>
        /// <remarks>
        /// <para>spec "Battle selects one configured wave plan"：按 ActivePlanId 精确选择
        /// 一份逐波计划并复制为本局不可变快照，计划选择完成后不再读取原始配置行。</para>
        /// <para>可为 null（旧兼容构造路径）；<see cref="BattleConfigValidator"/> 以
        /// MissingSection 拒绝 null 计划。</para>
        /// </remarks>
        public OrderedWavePlanSnapshot OrderedWavePlan { get; }

        /// <summary>
        /// Buff 目录（不可变，按 type 索引）——本 change 起的 Buff 配置权威。
        /// </summary>
        /// <remarks>
        /// <para>spec "Buff definitions form a validated immutable catalog"：全部 Buff 定义
        /// 归一化为不可变目录，按 type 唯一查询；运行时的申请/叠层/到期只消费本目录，
        /// 规则层不得再散读 Luban <c>TbBuff</c> 单例。</para>
        /// <para>可为 null（旧兼容构造路径）；<see cref="BattleConfigValidator"/> 以
        /// MissingSection 拒绝 null 目录。</para>
        /// </remarks>
        public BuffCatalogSnapshot BuffCatalog { get; }

        /// <summary>
        /// Skill 目录（不可变，按 key 索引）——本 change 起的 Skill 配置权威。
        /// </summary>
        /// <remarks>
        /// <para>spec "Skill definitions are validated before use"：全部 Skill 定义
        /// 归一化为不可变目录，按 key 唯一查询；目录可含未实现 handler 的技能行，
        /// 但这类技能不得 attach。运行时的 attach/激活/冷却只消费本目录，规则层
        /// 不得再散读 Luban <c>TbSkill</c> 单例。</para>
        /// <para>可为 null（旧兼容构造路径）；<see cref="BattleConfigValidator"/> 以
        /// MissingSection 拒绝 null 目录。</para>
        /// </remarks>
        public SkillCatalogSnapshot SkillCatalog { get; }

        /// <summary>
        /// Boss 目录（不可变，按 key 索引）——本 change 起的 Boss 配置权威。
        /// </summary>
        /// <remarks>
        /// <para>spec "Luban Boss rows are copied into immutable definitions"：全部
        /// Boss 行归一化为不可变目录，按 key 唯一查询；运行时只消费本目录，不保留
        /// Luban row，也不做运行时 fallback。</para>
        /// <para>可为 null（旧兼容构造路径）；只有所选计划含 Boss 行时才要求非 null
        /// （Boss 波启动门禁），无 Boss 行的计划不因本字段为 null 被拒。</para>
        /// </remarks>
        public BossCatalogSnapshot BossCatalog { get; }

        /// <summary>
        /// 武器目录（不可变，按 id 索引）——本 change 起的武器配置权威。
        /// </summary>
        /// <remarks>
        /// <para>spec "Exactly four basic weapon definitions are enabled"：全部
        /// Weapon 行（含其余 40 把禁用特殊武器）归一化为不可变目录，按 id 唯一
        /// 查询；运行时只消费本目录，不保留 Luban row，也不做运行时 fallback。
        /// 首期只有 id ∈ {1, 11, 21, 32} 四条 Basic +1 启用，其余行不产生运行时状态。</para>
        /// <para>可为 null（旧兼容构造路径/旧测试直接构造快照）；生产 Luban Provider
        /// 恒构造本目录。为 null 时玩家默认武器不装配（兼容语义），生产装配的
        /// 玩家目录缺失或不兼容默认 MUST 失败而非 fallback。</para>
        /// </remarks>
        public WeaponCatalogSnapshot WeaponCatalog { get; }

        /// <summary>本局启用武将、有序配方索引与玩家武将字招募项。</summary>
        public GeneralCatalogSnapshot GeneralCatalog { get; }

        /// <summary>对手 AI 四档难度目录；旧测试快照和未启用 AI 的装载可为空。</summary>
        internal OpponentAiProfileCatalogSnapshot OpponentAiProfiles { get; }

        // ====================================================================
        // 波次配置（legacy）
        // ====================================================================

        /// <summary>
        /// 【legacy】单敌人配置快照（不可变）。
        /// </summary>
        /// <remarks>
        /// <para>本字段为迁移期兼容保留：<see cref="WaveManager"/>/<see cref="BattleManager"/>
        /// 等旧生产链仍消费它。新的生产链权威为 <see cref="EnemyCatalog"/>；
        /// 后续 change（3.x/4.x）移除旧消费后本字段随之淘汰。</para>
        /// </remarks>
        public EnemyConfigSnapshot Enemy { get; }

        /// <summary>
        /// 【legacy】并行数组式波次配置快照（不可变）。
        /// </summary>
        /// <remarks>
        /// <para>本字段为迁移期兼容保留：<see cref="WaveManager"/>/<see cref="BattleManager"/>
        /// 等旧生产链仍消费它。新的生产链权威为 <see cref="OrderedWavePlan"/>；
        /// 后续 change（3.x/4.x）移除旧消费后本字段随之淘汰。</para>
        /// </remarks>
        public WaveConfigSnapshot Wave { get; }

        // ====================================================================
        // 单位配置
        // ====================================================================

        /// <summary>
        /// 单位配置列表（不可变）。
        /// </summary>
        public IReadOnlyList<UnitConfigSnapshot> Units { get; }

        /// <summary>
        /// 单位等级配置（不可变）。
        /// </summary>
        public UnitLevelConfigSnapshot UnitLevel { get; }

        // ====================================================================
        // 经济配置
        // ====================================================================

        /// <summary>
        /// 经济配置（不可变）。
        /// </summary>
        public EconomyConfigSnapshot Economy { get; }

        // ====================================================================
        // 牌组配置
        // ====================================================================

        /// <summary>
        /// 牌组配置（不可变）。
        /// </summary>
        public DeckConfigSnapshot Deck { get; }

        // ====================================================================
        // 投射物配置
        // ====================================================================

        /// <summary>
        /// 投射物配置（不可变）。
        /// </summary>
        public ProjectileConfigSnapshot Projectile { get; }

        // ====================================================================
        // 缺失字段标注
        // ====================================================================

        /// <summary>
        /// 缺失字段标注列表。Luban 表结构中缺失的字段在此明确标注，不静默补默认值。
        /// </summary>
        /// <remarks>
        /// <para>task 39/40 BLOCKED：Luban 源工程不可修改，现有表结构可能缺少部分字段。
        /// 每条标注格式为 "表名.字段名 -> 缺失原因 / 处理方式"。</para>
        /// <para>黄金 JSON 快照不产生缺失标注（包含完整配置）。</para>
        /// </remarks>
        public IReadOnlyList<string> MissingFieldNotes { get; }

        /// <summary>
        /// 配置来源标识（"Json" 或 "Luban"），用于区分黄金测试基线和生产数据。
        /// </summary>
        public string SourceTag { get; }

        // ====================================================================
        // 构造
        // ====================================================================

        /// <summary>
        /// 构造不可变战斗配置快照。
        /// </summary>
        /// <param name="map">地图数据。</param>
        /// <param name="enemy">【legacy】单敌人配置。</param>
        /// <param name="wave">【legacy】并行数组式波次配置。</param>
        /// <param name="units">单位配置列表。</param>
        /// <param name="unitLevel">单位等级配置。</param>
        /// <param name="economy">经济配置。</param>
        /// <param name="deck">牌组配置。</param>
        /// <param name="projectile">投射物配置。</param>
        /// <param name="missingFieldNotes">缺失字段标注列表（可为空）。</param>
        /// <param name="sourceTag">配置来源标识（"Json" 或 "Luban"）。</param>
        /// <param name="enemyCatalog">敌人目录（新生产链权威；旧兼容路径可为 null）。</param>
        /// <param name="orderedWavePlan">有序波次计划（新生产链权威；旧兼容路径可为 null）。</param>
        /// <param name="buffCatalog">Buff 目录（新生产链权威；旧兼容路径可为 null）。</param>
        /// <param name="skillCatalog">Skill 目录（新生产链权威；旧兼容路径可为 null）。</param>
        /// <param name="bossCatalog">Boss 目录（本 change 起权威；旧兼容路径可为 null）。</param>
        /// <param name="weaponCatalog">武器目录（本 change 起权威；旧兼容路径可为 null）。</param>
        public BattleConfigSnapshot(
            MapData map,
            EnemyConfigSnapshot enemy,
            WaveConfigSnapshot wave,
            IReadOnlyList<UnitConfigSnapshot> units,
            UnitLevelConfigSnapshot unitLevel,
            EconomyConfigSnapshot economy,
            DeckConfigSnapshot deck,
            ProjectileConfigSnapshot projectile,
            IReadOnlyList<string> missingFieldNotes,
            string sourceTag,
            EnemyCatalogSnapshot enemyCatalog = null,
            OrderedWavePlanSnapshot orderedWavePlan = null,
            BuffCatalogSnapshot buffCatalog = null,
            SkillCatalogSnapshot skillCatalog = null,
            BossCatalogSnapshot bossCatalog = null,
            WeaponCatalogSnapshot weaponCatalog = null,
            GeneralCatalogSnapshot generalCatalog = null,
            OpponentAiProfileCatalogSnapshot opponentAiProfiles = null)
        {
            Map = map ?? throw new ArgumentNullException(nameof(map));
            Enemy = enemy ?? throw new ArgumentNullException(nameof(enemy));
            Wave = wave ?? throw new ArgumentNullException(nameof(wave));
            Units = units ?? throw new ArgumentNullException(nameof(units));
            UnitLevel = unitLevel ?? throw new ArgumentNullException(nameof(unitLevel));
            Economy = economy ?? throw new ArgumentNullException(nameof(economy));
            Deck = deck ?? throw new ArgumentNullException(nameof(deck));
            Projectile = projectile ?? throw new ArgumentNullException(nameof(projectile));
            MissingFieldNotes = missingFieldNotes ?? Array.Empty<string>();
            SourceTag = sourceTag ?? string.Empty;
            EnemyCatalog = enemyCatalog;
            OrderedWavePlan = orderedWavePlan;
            BuffCatalog = buffCatalog;
            SkillCatalog = skillCatalog;
            BossCatalog = bossCatalog;
            WeaponCatalog = weaponCatalog;
            GeneralCatalog = generalCatalog ?? new GeneralCatalogSnapshot(Array.Empty<GeneralConfigSnapshot>());
            OpponentAiProfiles = opponentAiProfiles;
        }
    }

    // ========================================================================
    // 不可变配置子快照结构
    // ========================================================================

    /// <summary>
    /// 【legacy】单敌人配置快照（不可变）。
    /// </summary>
    /// <remarks>
    /// <para>迁移期兼容保留：<c>WaveManager</c>/<c>BattleManager</c> 等旧生产链仍消费本类型，
    /// 它不再是新生产链权威。新权威为 <see cref="EnemyCatalogSnapshot"/>（Mob0～Mob3 目录），
    /// 后续 change 移除旧消费后本类型淘汰。</para>
    /// </remarks>
    public sealed class EnemyConfigSnapshot
    {
        /// <summary>敌人类型标识（本期唯一 "Mob0"）。</summary>
        public int Id { get; }

        /// <summary>地图敌人类型索引（对应 MAP_BLOCKS[mapIndex].enemyTypeIndex）。</summary>
        public int MapEnemyTypeIndex { get; }

        /// <summary>敌人移动速度（px/s，BattleDataCore 硬编码 speed=50）。</summary>
        public int Speed { get; }

        /// <summary>各波次基础血量数组（BattleDataCore normalEnemyHealthByWave）。</summary>
        public IReadOnlyList<int> HealthByWave { get; }

        /// <summary>前 10 波血量乘数（BattleDataCore earlyRoundHealthMultipliers）。</summary>
        public IReadOnlyList<float> EarlyRoundHealthMultipliers { get; }

        /// <summary>接触伤害（敌人接触阿斗扣血量，BattleDataCore 硬编码 1）。</summary>
        public int ContactDamage { get; }

        /// <summary>构造敌人配置快照。</summary>
        public EnemyConfigSnapshot(
            int id,
            int mapEnemyTypeIndex,
            int speed,
            IReadOnlyList<int> healthByWave,
            IReadOnlyList<float> earlyRoundHealthMultipliers,
            int contactDamage)
        {
            Id = id;
            MapEnemyTypeIndex = mapEnemyTypeIndex;
            Speed = speed;
            HealthByWave = healthByWave ?? throw new ArgumentNullException(nameof(healthByWave));
            EarlyRoundHealthMultipliers = earlyRoundHealthMultipliers
                ?? throw new ArgumentNullException(nameof(earlyRoundHealthMultipliers));
            ContactDamage = contactDamage;
        }
    }

    /// <summary>
    /// 【legacy】并行数组式波次配置快照（不可变）。
    /// </summary>
    /// <remarks>
    /// <para>迁移期兼容保留：<c>WaveManager</c>/<c>BattleManager</c> 等旧生产链仍消费本类型，
    /// 它不再是新生产链权威。新权威为 <see cref="OrderedWavePlanSnapshot"/>（逐波计划），
    /// 后续 change 移除旧消费后本类型淘汰。</para>
    /// </remarks>
    public sealed class WaveConfigSnapshot
    {
        /// <summary>各波次怪物数量。</summary>
        public IReadOnlyList<int> WaveUnitCounts { get; }

        /// <summary>Boss 波次号列表。</summary>
        public IReadOnlyList<int> BossWaveNumbers { get; }

        /// <summary>Boss 出现概率列表。</summary>
        public IReadOnlyList<float> BossSpawnChances { get; }

        /// <summary>生成策略权重。</summary>
        public IReadOnlyList<int> SpawnStrategyWeights { get; }

        /// <summary>生成策略表（外层按权重选择，内层为各波次乘数）。</summary>
        public IReadOnlyList<IReadOnlyList<float>> SpawnStrategies { get; }

        /// <summary>是否跳过 Boss（本期固定 true）。</summary>
        public bool SkipBoss { get; }

        /// <summary>波间延迟（毫秒）。</summary>
        public long DelayTimeMs { get; }

        /// <summary>最大波次数。</summary>
        public int MaxRounds { get; }

        /// <summary>构造波次配置快照。</summary>
        public WaveConfigSnapshot(
            IReadOnlyList<int> waveUnitCounts,
            IReadOnlyList<int> bossWaveNumbers,
            IReadOnlyList<float> bossSpawnChances,
            IReadOnlyList<int> spawnStrategyWeights,
            IReadOnlyList<IReadOnlyList<float>> spawnStrategies,
            bool skipBoss,
            long delayTimeMs,
            int maxRounds)
        {
            WaveUnitCounts = waveUnitCounts ?? throw new ArgumentNullException(nameof(waveUnitCounts));
            BossWaveNumbers = bossWaveNumbers ?? throw new ArgumentNullException(nameof(bossWaveNumbers));
            BossSpawnChances = bossSpawnChances ?? throw new ArgumentNullException(nameof(bossSpawnChances));
            SpawnStrategyWeights = spawnStrategyWeights
                ?? throw new ArgumentNullException(nameof(spawnStrategyWeights));
            SpawnStrategies = spawnStrategies ?? throw new ArgumentNullException(nameof(spawnStrategies));
            SkipBoss = skipBoss;
            DelayTimeMs = delayTimeMs;
            MaxRounds = maxRounds;
        }
    }

    /// <summary>
    /// 单个单位配置快照（不可变）。
    /// </summary>
    public sealed class UnitConfigSnapshot
    {
        /// <summary>单位索引（0=刀, 1=弓, 2=枪, 3=骑）。</summary>
        public int Index { get; }

        /// <summary>显示名。</summary>
        public string Text { get; }

        /// <summary>动画键。</summary>
        public string AnimationKey { get; }

        /// <summary>攻击距离（格）。</summary>
        public float RangeCells { get; }

        /// <summary>攻击力。</summary>
        public int AttackDamage { get; }

        /// <summary>攻击间隔（秒）。</summary>
        public float AttackIntervalSeconds { get; }

        /// <summary>伤害模式。</summary>
        public string DamageMode { get; }

        /// <summary>目标策略。</summary>
        public string TargetPolicy { get; }

        /// <summary>投射物类型；近战或旧四兵配置为空。</summary>
        public string ProjectileType { get; }

        /// <summary>配置投射物速度；0 表示沿用既有兵种默认值。</summary>
        public int ProjectileSpeed { get; }

        /// <summary>单位表现 Prefab 地址；空时按既有 SoldierType 映射。</summary>
        public string PrefabAddress { get; }

        /// <summary>主动技能键（武将单位才有；普通四兵为空串）。</summary>
        public int? SkillId { get; }

        /// <summary>构造单位配置快照。</summary>
        public UnitConfigSnapshot(
            int index,
            string text,
            string animationKey,
            float rangeCells,
            int attackDamage,
            float attackIntervalSeconds,
            string damageMode,
            string targetPolicy,
            string projectileType = "",
            int projectileSpeed = 0,
            string prefabAddress = "",
            int? skillId = null)
        {
            Index = index;
            Text = text ?? throw new ArgumentNullException(nameof(text));
            AnimationKey = animationKey ?? throw new ArgumentNullException(nameof(animationKey));
            RangeCells = rangeCells;
            AttackDamage = attackDamage;
            AttackIntervalSeconds = attackIntervalSeconds;
            DamageMode = damageMode ?? throw new ArgumentNullException(nameof(damageMode));
            TargetPolicy = targetPolicy ?? throw new ArgumentNullException(nameof(targetPolicy));
            ProjectileType = projectileType ?? string.Empty;
            ProjectileSpeed = projectileSpeed;
            PrefabAddress = prefabAddress ?? string.Empty;
            SkillId = skillId;
        }
    }

    /// <summary>
    /// 单位等级配置快照（不可变）。
    /// </summary>
    public sealed class UnitLevelConfigSnapshot
    {
        /// <summary>最大等级。</summary>
        public int MaxLevel { get; }

        /// <summary>伤害等级乘数。</summary>
        public IReadOnlyList<float> DamageLevelMultipliers { get; }

        /// <summary>攻速等级乘数。</summary>
        public IReadOnlyList<float> AttackSpeedLevelMultipliers { get; }

        /// <summary>构造单位等级配置快照。</summary>
        public UnitLevelConfigSnapshot(
            int maxLevel,
            IReadOnlyList<float> damageLevelMultipliers,
            IReadOnlyList<float> attackSpeedLevelMultipliers)
        {
            MaxLevel = maxLevel;
            DamageLevelMultipliers = damageLevelMultipliers
                ?? throw new ArgumentNullException(nameof(damageLevelMultipliers));
            AttackSpeedLevelMultipliers = attackSpeedLevelMultipliers
                ?? throw new ArgumentNullException(nameof(attackSpeedLevelMultipliers));
        }
    }

    /// <summary>
    /// 经济配置快照（不可变）。
    /// </summary>
    public sealed class EconomyConfigSnapshot
    {
        /// <summary>初始金币。</summary>
        public int InitialGold { get; }

        /// <summary>刷新起始消耗。</summary>
        public int RefreshCostStart { get; }

        /// <summary>刷新递增量。</summary>
        public int RefreshCostIncrement { get; }

        /// <summary>单位基础消耗。</summary>
        public int UnitBaseCost { get; }

        /// <summary>手牌大小。</summary>
        public int HandSize { get; }

        /// <summary>玩家方最大生命。</summary>
        public int PlayerMaxHealth { get; }

        /// <summary>对手方最大生命。</summary>
        public int OpponentMaxHealth { get; }

        /// <summary>构造经济配置快照。</summary>
        public EconomyConfigSnapshot(
            int initialGold,
            int refreshCostStart,
            int refreshCostIncrement,
            int unitBaseCost,
            int handSize,
            int playerMaxHealth,
            int opponentMaxHealth)
        {
            InitialGold = initialGold;
            RefreshCostStart = refreshCostStart;
            RefreshCostIncrement = refreshCostIncrement;
            UnitBaseCost = unitBaseCost;
            HandSize = handSize;
            PlayerMaxHealth = playerMaxHealth;
            OpponentMaxHealth = opponentMaxHealth;
        }
    }

    /// <summary>
    /// 牌组配置快照（不可变）。
    /// </summary>
    public sealed class DeckConfigSnapshot
    {
        /// <summary>是否最简模式。</summary>
        public bool MinimalMode { get; }

        /// <summary>基础兵字表（最简牌池 ['刀','弓','枪','骑']）。</summary>
        public IReadOnlyList<string> BaseSoldierTexts { get; }

        /// <summary>手牌大小。</summary>
        public int HandSize { get; }

        /// <summary>默认等级。</summary>
        public int DefaultLevel { get; }

        /// <summary>基础单位消耗。</summary>
        public int BaseUnitCost { get; }

        /// <summary>构造牌组配置快照。</summary>
        public DeckConfigSnapshot(
            bool minimalMode,
            IReadOnlyList<string> baseSoldierTexts,
            int handSize,
            int defaultLevel,
            int baseUnitCost)
        {
            MinimalMode = minimalMode;
            BaseSoldierTexts = baseSoldierTexts
                ?? throw new ArgumentNullException(nameof(baseSoldierTexts));
            HandSize = handSize;
            DefaultLevel = defaultLevel;
            BaseUnitCost = baseUnitCost;
        }
    }

    /// <summary>
    /// 投射物配置快照（不可变）。
    /// </summary>
    public sealed class ProjectileConfigSnapshot
    {
        /// <summary>投射物类型列表。</summary>
        public IReadOnlyList<string> Types { get; }

        /// <summary>本期唯一注册的投射物类型。</summary>
        public string PrimaryType { get; }

        /// <summary>移动策略标识。</summary>
        public string MovementStrategy { get; }

        /// <summary>命中策略标识。</summary>
        public string HitStrategy { get; }

        /// <summary>构造投射物配置快照。</summary>
        public ProjectileConfigSnapshot(
            IReadOnlyList<string> types,
            string primaryType,
            string movementStrategy,
            string hitStrategy)
        {
            Types = types ?? throw new ArgumentNullException(nameof(types));
            PrimaryType = primaryType ?? throw new ArgumentNullException(nameof(primaryType));
            MovementStrategy = movementStrategy ?? throw new ArgumentNullException(nameof(movementStrategy));
            HitStrategy = hitStrategy ?? throw new ArgumentNullException(nameof(hitStrategy));
        }
    }
}
